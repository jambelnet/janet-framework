/* *****************************************************************************************************************************
 * (c) J@mBeL.net 2010-2026
 * Author: John Ambeliotis
 *
 * License:
 *  This file is part of jaNET Framework.

    jaNET Framework is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    jaNET Framework is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with jaNET Framework. If not, see <http://www.gnu.org/licenses/>. */

using jaNET.Configuration;
using jaNET.Hosting;
using jaNET.Infrastructure;
using jaNET.Scripting;
using jaNET.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace jaNET.Servers;

/// <summary>
/// The built-in web server (Kestrel): serves the Jubito web UI (the "www" folder) and the judo API (?cmd=...&amp;mode=json|text|html).
/// Optionally protected by HTTP Basic authentication (user and password from ".htaccess") and reachable over https
/// (a port in AppConfig.xml; the plain http port then only serves this computer and sends everybody else to https).
/// </summary>
internal sealed class WebServer : IServer, IDisposable
{
    const string InstructionsPath = "/api/instructions";

    static readonly Regex CommandMarkers = new(@"\?cmd=|&mode=text|&mode=json|&mode=html", RegexOptions.Compiled);
    static readonly Regex PortInMessage = new(@":(\d{2,5})\b", RegexOptions.Compiled);

    readonly AppConfigStore _config;
    readonly ISettingsStore _settings;
    readonly AppPaths _paths;
    readonly Func<IInstructionExecutor> _executor;
    readonly ILog _log;
    readonly CertificateProvider _certificates;
    readonly SpeechService _speech;
    readonly bool _ownsSpeech;
    readonly Func<bool> _isMuted;
    readonly object _gate = new();
    readonly AsyncLocal<HttpResponse?> _commandResponse = new();

    WebApplication? _app;
    volatile ServiceProblem? _problem;
    volatile ServerCertificate? _certificate;

    public WebServer(AppConfigStore config, ISettingsStore settings, AppPaths paths, Func<IInstructionExecutor> executor, ILog log,
                     CertificateProvider? certificates = null, SpeechService? speech = null, Func<bool>? isMuted = null) {
        _config = config;
        _settings = settings;
        _paths = paths;
        _executor = executor;
        _log = log;
        _certificates = certificates ?? new CertificateProvider(paths, settings);
        _speech = speech ?? new SpeechService(settings, paths);
        _ownsSpeech = speech == null;
        _isMuted = isMuted ?? (() => false);
    }

    public bool IsRunning => _app != null;

    public ServiceProblem? Problem => _problem;

    /// <summary>The certificate of the running https listener, or null while there is none.</summary>
    public ServerCertificate? Certificate => _certificate;

    public void Start() {
        lock (_gate) {
            if (_app != null) return;

            CommSettings comm = _config.Comm;
            string host = comm.Hostname.Length > 0 && comm.HttpPort.Length > 0 ? comm.Hostname : "localhost";
            string port = comm.Hostname.Length > 0 && comm.HttpPort.Length > 0 ? comm.HttpPort : "8080";
            bool basic = comm.Authentication.Equals("basic", StringComparison.OrdinalIgnoreCase);

            WebApplication? app = null;
            try {
                int httpPort = ParsePort(port);
                int httpsPort = comm.HttpsPort.Length > 0 ? ParsePort(comm.HttpsPort) : 0;
                ServerCertificate? certificate = httpsPort > 0 ? LoadCertificate(comm, host) : null;

                var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { ContentRootPath = AppContext.BaseDirectory });
                builder.Logging.ClearProviders();
                builder.WebHost.ConfigureKestrel(kestrel => {
                    kestrel.AddServerHeader = false;
                    kestrel.Limits.MaxRequestBodySize = 1_048_576;
                    Listen(kestrel, host, httpPort, null);
                    if (certificate != null) Listen(kestrel, host, httpsPort, certificate.Certificate);
                });

                app = builder.Build();
                app.Run(context => Handle(context, basic, httpsPort));
                app.StartAsync().GetAwaiter().GetResult();

                _certificate = certificate;
                _problem = null;
                _app = app;
            }
            catch (Exception e) {
                _log.Write($"obj [ WebServer.Start <{e.GetType().Name}> ] Exception Message: [ {e.Message} ]");
                _problem = e is CertificateException c ? ServiceProblems.Certificate(c.InnerException ?? c, c.File) : ServiceProblems.WebServer(e, host, port);
                _certificate = null;
                try { app?.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch (Exception) { /* it never started */ }
            }
        }
    }

    public void Stop() {
        lock (_gate) {
            WebApplication? app = _app;
            _app = null;
            _problem = null;
            _certificate = null;
            if (app == null) return;

            try {
                using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                app.StopAsync(limit.Token).GetAwaiter().GetResult();
            }
            catch (Exception e) when (e is OperationCanceledException || e is ObjectDisposedException) {
                // a connection that does not let go: the process is stopping anyway
            }
            app.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    public void Dispose() {
        Stop();
        if (_ownsSpeech) _speech.Dispose();
    }

    // A settings command must finish its HTTP response before its own listener is stopped.
    internal bool RestartAfterResponse() {
        HttpResponse? response = _commandResponse.Value;
        if (response == null) return false;
        response.Headers.Connection = "close";
        response.OnCompleted(() => {
            _ = Task.Run(async () => {
                // Let Kestrel finish closing the response connection before disposing its transports.
                await Task.Delay(150).ConfigureAwait(false);
                Stop(); Start();
            });
            return Task.CompletedTask;
        });
        return true;
    }

    string ExecuteCommand(HttpResponse response, string command, ResponseFormat format) {
        _commandResponse.Value = response;
        try { return _executor().Run(command, format); }
        finally { _commandResponse.Value = null; }
    }

    static int ParsePort(string port) =>
        int.TryParse(port, out int value) && value is > 0 and <= 65535 ? value : throw new FormatException($"'{port}' is not a port number.");

    ServerCertificate LoadCertificate(CommSettings comm, string host) {
        try {
            return _certificates.Load(comm.Certificate, host);
        }
        catch (Exception e) when (e is CryptographicException || e is IOException || e is UnauthorizedAccessException) {
            throw new CertificateException(comm.Certificate, e);
        }
    }

    // An explicit LAN address also gets a loopback listener for local browser microphone access.
    static void Listen(KestrelServerOptions kestrel, string host, int port, System.Security.Cryptography.X509Certificates.X509Certificate2? certificate) {
        void Configure(ListenOptions options) {
            options.Protocols = HttpProtocols.Http1;
            if (certificate != null) options.UseHttps(certificate);
        }

        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) kestrel.ListenLocalhost(port, Configure);
        else if (host is "+" or "*" or "0.0.0.0" or "::" or "[::]") kestrel.ListenAnyIP(port, Configure);
        else if (IPAddress.TryParse(host.Trim('[', ']'), out IPAddress? address)) {
            kestrel.Listen(address, port, Configure);
            if (!IPAddress.IsLoopback(address)) kestrel.ListenLocalhost(port, Configure);
        }
        else {
            IPAddress[] addresses;
            try {
                addresses = Dns.GetHostAddresses(host);
            }
            catch (SocketException) {
                throw new ArgumentException($"The host name '{host}' cannot be found.");
            }

            IPAddress[] v4 = addresses.Where(a => a.AddressFamily == AddressFamily.InterNetwork).ToArray();
            foreach (IPAddress each in v4.Length > 0 ? v4 : addresses.Take(1)) kestrel.Listen(each, port, Configure);
        }
    }

    async Task Handle(HttpContext context, bool basic, int httpsPort) {
        HttpRequest request = context.Request;
        HttpResponse response = context.Response;
        string rawTarget = context.Features.Get<IHttpRequestFeature>()?.RawTarget ?? "/";

        // with https on, the plain port is for this computer only: everybody else is sent to the same address over https
        if (httpsPort > 0 && _config.Comm.HttpsPort.Length > 0 && !request.IsHttps && !IsThisComputer(context.Connection.RemoteIpAddress)) {
            response.StatusCode = StatusCodes.Status307TemporaryRedirect;
            response.Headers.CacheControl = "no-store";
            response.Headers.Location = $"https://{request.Host.Host}:{httpsPort}{rawTarget}";
            response.ContentLength = 0;
            return;
        }

        string mapPath = _paths.MapRequest(UriCodec.Decode(rawTarget.Substring(1)));
        string path = rawTarget.Contains('?') ? rawTarget.Substring(0, rawTarget.IndexOf('?')) : rawTarget;
        string query = rawTarget.Contains('?') ? rawTarget.Substring(rawTarget.IndexOf('?')) : string.Empty;
        byte[] body;

        try {
            if (!IsAuthenticated(request, basic)) {
                response.Headers.WWWAuthenticate = "Basic Realm=\"Authentication Required\"";
                await Task.Delay(250, context.RequestAborted).ConfigureAwait(false);       // wrong guesses are slowed down
                throw new UnauthorizedAccessException();
            }

            response.StatusCode = StatusCodes.Status200OK;
            string? redirectTo = DirectoryRedirect(path, query, rawTarget);
            if (mapPath.EndsWith("/", StringComparison.Ordinal))
                mapPath += "index.html";

            if (redirectTo != null) {
                response.StatusCode = StatusCodes.Status301MovedPermanently;
                response.Headers.Location = redirectTo;
                body = Array.Empty<byte>();
            }
            else if (path.StartsWith("/api/speech", StringComparison.Ordinal)) {
                body = await SpeechResponse(context, path).ConfigureAwait(false);
            }
            else if (path == InstructionsPath) {
                response.ContentType = "application/json; charset=utf-8";
                response.Headers.CacheControl = "no-store";
                body = Encoding.UTF8.GetBytes(InstructionsJson());
            }
            else if (path == "/api/command" && HttpMethods.IsPost(request.Method)) {
                using JsonDocument document = await JsonDocument.ParseAsync(request.Body, cancellationToken: context.RequestAborted).ConfigureAwait(false);
                string command = document.RootElement.GetProperty("command").GetString() ?? string.Empty;
                response.ContentType = MimeTypes.ForCommand(ResponseFormat.Text);
                response.Headers.CacheControl = "no-store";
                body = Encoding.UTF8.GetBytes(ExecuteCommand(response, command, ResponseFormat.Text));
            }
            else if (mapPath.Contains("?cmd=")) {
                ResponseFormat format = ResponseFormat.Html;
                if (mapPath.Contains("&mode=json")) format = ResponseFormat.Json;
                if (mapPath.Contains("&mode=text")) format = ResponseFormat.Text;

                string command = CommandMarkers.Replace(mapPath.Substring(mapPath.LastIndexOf("?cmd=", StringComparison.Ordinal)), string.Empty);
                response.ContentType = MimeTypes.ForCommand(format);
                response.Headers.CacheControl = "no-store";
                body = Encoding.UTF8.GetBytes(ExecuteCommand(response, command, format));
            }
            else {
                mapPath = _paths.MapRequest(UriCodec.Decode(path.Substring(1)));
                if (mapPath.EndsWith("/", StringComparison.Ordinal)) mapPath += "index.html";
                EnsureServable(mapPath);
                body = await File.ReadAllBytesAsync(mapPath).ConfigureAwait(false);
                response.ContentType = MimeTypes.ForFile(mapPath);
                response.Headers.CacheControl = "no-cache";
            }
        }
        catch (UnauthorizedAccessException) {
            response.StatusCode = StatusCodes.Status401Unauthorized;
            response.ContentType = MimeTypes.Html;
            body = Encoding.UTF8.GetBytes(
                "<html><head><title>401 Authorization Required</title></head>" +
                "<body>" +
                "<h1>Authorization Required</h1>" +
                "This server could not verify that you are authorized to access the document requested.<br />" +
                "Either you supplied the wrong credentials (e.g., bad password), or your browser doesn't understand how to supply the credentials required." +
                "<hr></body></html>");
        }
        catch (OperationCanceledException) {
            return;         // the browser went away
        }
        catch (Exception e) {
            response.StatusCode = StatusCodes.Status404NotFound;
            response.ContentType = MimeTypes.Html;
            body = Encoding.UTF8.GetBytes(
                "<html><head><title>404 Not Found</title></head>" +
                "<body>" +
                "<h1>Not Found</h1>" +
                "The requested URL could not be found. Are you missing the www root?<br />" +
                "e.g. <a href=/www/>/www/</a>" +
                "<hr></body></html>");
            if (!e.Message.Contains("favicon.ico") && !e.Message.Contains("The object was used after being disposed."))
                _log.Write($"obj [ WebServer.ProcessRequestAsync <Exception> ] Exception Message: [ {e.Message} ]");
        }

        try {
            response.ContentLength = body.Length;
            if (!HttpMethods.IsHead(request.Method))
                await response.Body.WriteAsync(body, context.RequestAborted).ConfigureAwait(false);
        }
        catch (Exception e) when (e is OperationCanceledException || e is ObjectDisposedException || e is IOException || e is InvalidOperationException) {
            // the browser went away
        }
    }

    static bool IsThisComputer(IPAddress? address) =>
        address == null || IPAddress.IsLoopback(address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address);

    async Task<byte[]> SpeechResponse(HttpContext context, string path) {
        HttpRequest request = context.Request;
        HttpResponse response = context.Response;
        response.Headers.CacheControl = "no-store";
        response.ContentType = "application/json; charset=utf-8";
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        byte[] Json(object value) => JsonSerializer.SerializeToUtf8Bytes(value, options);
        if (path == "/api/speech" && HttpMethods.IsGet(request.Method)) return Json(_speech.Status(_isMuted()));
        if (!HttpMethods.IsPost(request.Method)) { response.StatusCode = 405; return Json(new { error = "Use POST for this speech endpoint." }); }
        string origin = request.Headers.Origin.ToString();
        if ((origin.Length > 0 && origin != request.Scheme + "://" + request.Host) || request.Headers["Sec-Fetch-Site"] == "cross-site") {
            response.StatusCode = 403; return Json(new { error = "Speech requests must come from this jaNET page." });
        }
        try {
            if (path == "/api/speech/settings") {
                SpeechSettings settings = await JsonSerializer.DeserializeAsync<SpeechSettings>(request.Body, options, context.RequestAborted).ConfigureAwait(false)
                    ?? throw new ArgumentException("Enter speech settings.");
                string result = _speech.Save(settings);
                if (result != "Settings saved.") throw new InvalidOperationException(result);
                return Json(new { message = result });
            }
            if (path == "/api/speech/recognize") {
                using var stream = new MemoryStream();
                await request.Body.CopyToAsync(stream, context.RequestAborted).ConfigureAwait(false);
                string text = await _speech.Recognize(stream.ToArray(), context.RequestAborted).ConfigureAwait(false);
                return Json(new { text });
            }
            using JsonDocument document = await JsonDocument.ParseAsync(request.Body, cancellationToken: context.RequestAborted).ConfigureAwait(false);
            if (path == "/api/speech/synthesize") {
                if (_isMuted()) {
                    response.StatusCode = 409;
                    return Json(new { error = "Speech synthesis is muted. Run unmute to enable it." });
                }
                byte[] audio = await _speech.Synthesize(document.RootElement.GetProperty("text").GetString() ?? "", context.RequestAborted).ConfigureAwait(false);
                response.ContentType = "audio/wav";
                return audio;
            }
            if (path == "/api/speech/model") {
                string result = await _speech.InstallModel(document.RootElement.GetProperty("language").GetString() ?? "", context.RequestAborted).ConfigureAwait(false);
                if (result != "Settings saved.") throw new InvalidOperationException(result);
                return Json(new { message = result, status = _speech.Status(_isMuted()) });
            }
            response.StatusCode = 404; return Json(new { error = "Unknown speech endpoint." });
        }
        catch (Exception e) when (e is ArgumentException || e is JsonException || e is KeyNotFoundException || e is Microsoft.AspNetCore.Http.BadHttpRequestException) {
            response.StatusCode = 400; return Json(new { error = e.Message });
        }
        catch (OperationCanceledException) when (!context.RequestAborted.IsCancellationRequested) {
            response.StatusCode = 503; return Json(new { error = "The speech request timed out. Try again or check the selected engine/model." });
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception e) {
            _log.Write("Speech: " + e.Message);
            response.StatusCode = 503; return Json(new { error = e.Message });
        }
    }

    // "/www" is the folder of the web UI without the slash that makes its relative links (css/app.css, js/...) work, and "/" has nothing to show:
    // send both to "/www/". Commands (?cmd=...) and every other path are left alone.
    string? DirectoryRedirect(string path, string query, string rawTarget) {
        if (path.EndsWith('/') && path != "/") return null;
        if (rawTarget.Contains("?cmd=", StringComparison.Ordinal)) return null;

        string www = Path.GetFullPath(_paths.WebRoot);

        if (path == "/") return Directory.Exists(www) ? "/www/" + query : null;

        try {
            string mapPath = _paths.MapRequest(UriCodec.Decode(path.Substring(1)));      // without the query string
            EnsureServable(mapPath + Path.DirectorySeparatorChar);
            return Directory.Exists(mapPath) ? path + "/" + query : null;
        }
        catch (FileNotFoundException) {
            return null;
        }
    }

    bool IsAuthenticated(HttpRequest request, bool basic) {
        if (!basic) return true;

        string header = request.Headers.Authorization.ToString();
        if (!header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase)) return false;

        string user, password;
        try {
            string text = Encoding.UTF8.GetString(Convert.FromBase64String(header.Substring(6).Trim()));
            int colon = text.IndexOf(':');
            if (colon < 0) return false;
            user = text.Substring(0, colon);
            password = text.Substring(colon + 1);
        }
        catch (FormatException) {
            return false;
        }

        WebLogin? login = _settings.LoadWebLogin();
        return login != null && login.Matches(user, password);
    }

    // /api/instructions: what the dashboard shows, without exposing AppConfig.xml itself
    string InstructionsJson() =>
        JsonSerializer.Serialize(_config.InstructionSets().Select(i => new {
            id = i.Id, categ = i.Category, header = i.Header, shortdescr = i.ShortDescription, descr = i.Description, img = i.Thumbnail, @ref = i.Reference
        }));

    // Only the web UI folder is served: neither AppConfig.xml, the settings files, log.txt nor the program files,
    // and requests like /www/../../etc/passwd must not escape it.
    internal void EnsureServable(string path) {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        string full = Path.GetFullPath(path);

        if (!_paths.WebRoots.Any(root => full.StartsWith(Path.GetFullPath(root), comparison)))
            throw new FileNotFoundException("Only the www folder is served.");
    }
}

/// <summary>The certificate for https could not be read; keeps the file name so that the message can name it.</summary>
internal sealed class CertificateException : Exception
{
    public CertificateException(string file, Exception inner) : base(inner.Message, inner) {
        File = file;
    }

    public string File { get; }
}
