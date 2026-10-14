/* (c) J@mBeL.net 2010-2026, John Ambeliotis.
 * This file is part of jaNET Framework.
 * jaNET Framework is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.
 * jaNET Framework is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty
 * of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU General Public License for more details.
 * You should have received a copy of the GNU General Public License along with jaNET Framework. If not, see <http://www.gnu.org/licenses/>. */
using jaNET.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace jaNET.Services;

internal sealed record CameraOptions(string Id, string Name, string Url, string Mode, int RefreshSeconds, bool ShowOnHome, string Username, string Password);
internal sealed record CameraSetup(string Id, string Name, string Url, string Mode, int RefreshSeconds, bool ShowOnHome, string Username, string Password, bool ClearPassword, string Revision);
internal sealed record CameraRemoval(string Id, string Revision);
internal sealed record CameraStore(string Revision, CameraOptions[] Cameras);
internal sealed record CameraView(string Id, string Name, string Url, string Mode, int RefreshSeconds, bool ShowOnHome, string Username, string Password, bool PasswordConfigured);
internal sealed record CameraSnapshot(string Revision, CameraView[] Cameras);
internal sealed record CameraWidget(string Id, string Name, string Mode, int RefreshSeconds, string Version);

// Only saved camera IDs can be read through the proxy. There is no arbitrary-URL fetch endpoint.
internal sealed class IpCameraService : IDisposable
{
    readonly ISettingsStore _settings;
    readonly Func<CameraOptions, HttpClient> _client;
    readonly SemaphoreSlim _gate = new(1, 1), _feeds = new(8, 8);
    readonly Dictionary<string, CancellationTokenSource> _changes = new();
    internal const int MaxSnapshotBytes = 5 * 1024 * 1024;

    public IpCameraService(ISettingsStore settings, Func<CameraOptions, HttpClient>? client = null) {
        _settings = settings; _client = client ?? Client;
    }

    CameraStore Load() {
        var lines = _settings.Load(SettingsFiles.IpCameras);
        if (lines == null) {
            if (_settings.Exists(SettingsFiles.IpCameras)) throw new InvalidOperationException("Camera settings cannot be decrypted. Restore the installation key before saving.");
            return new("new", Array.Empty<CameraOptions>());
        }
        try {
            var stored = JsonSerializer.Deserialize<CameraStore>(lines.Single()) ?? throw new JsonException();
            if (string.IsNullOrEmpty(stored.Revision) || stored.Cameras == null || stored.Cameras.Length > 8 || stored.Cameras.Select(c => c.Id).Distinct().Count() != stored.Cameras.Length) throw new JsonException();
            foreach (var camera in stored.Cameras) {
                if (camera == null || !ValidId(camera.Id) || camera.Username == null || camera.Password == null) throw new JsonException();
                Validate(camera.Name, camera.Url, camera.Mode, camera.RefreshSeconds);
            }
            return stored;
        } catch (Exception e) when (e is JsonException || e is InvalidOperationException || e is ArgumentException || e is NullReferenceException) {
            throw new InvalidOperationException("Camera settings cannot be read. Restore the saved settings before continuing.");
        }
    }

    void Store(CameraOptions[] cameras) {
        if (_settings.Save(SettingsFiles.IpCameras, JsonSerializer.Serialize(new CameraStore(Guid.NewGuid().ToString("N"), cameras))) != "Settings saved.")
            throw new InvalidOperationException("Camera settings could not be saved.");
    }

    internal static bool ValidId(string? id) => id != null && Regex.IsMatch(id, "^[a-f0-9]{32}$");
    static string Clean(string? value, int max, string field) {
        if (value == null || value.Length > max || value.Any(char.IsControl)) throw new ArgumentException("Enter a valid " + field + ".");
        return value.Trim();
    }
    static void Validate(string name, string url, string mode, int refresh) {
        if (Clean(name, 128, "camera name").Length == 0) throw new ArgumentException("Enter a camera name.");
        CameraUri(Clean(url, 2048, "camera URL"));
        if (mode is not ("auto" or "snapshot" or "mjpeg")) throw new ArgumentException("Choose Automatic, Snapshot or MJPEG.");
        if (refresh < 2 || refresh > 60) throw new ArgumentException("Snapshot refresh must be between 2 and 60 seconds.");
    }

    internal static Uri CameraUri(string url) {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 || uri.Host.Length == 0)
            throw new ArgumentException("Use an HTTP/HTTPS snapshot or MJPEG URL, with credentials in the separate fields. RTSP needs a browser-compatible gateway.");
        if (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
            IPAddress.TryParse(uri.DnsSafeHost.Trim('[', ']'), out var address) && ForbiddenAddress(address))
            throw new ArgumentException("Use a camera on your network, not a loopback, link-local or special-purpose address.");
        return uri;
    }

    internal static bool ForbiddenAddress(IPAddress address) {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address)) return true;
        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
            return bytes[0] == 0 || bytes[0] == 127 || bytes[0] >= 224 || bytes[0] == 169 && bytes[1] == 254 ||
                bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127 || bytes[0] == 198 && bytes[1] is 18 or 19;
        // Global unicast and private ULA only; exclude mapped, transition and metadata routes.
        return !((bytes[0] & 0xfe) == 0xfc || (bytes[0] & 0xe0) == 0x20) ||
            bytes[0] == 0x20 && bytes[1] == 0x02 || bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0 && bytes[3] == 0;
    }

    static HttpClient Client(CameraOptions camera) {
        var handler = new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = false, ConnectTimeout = TimeSpan.FromSeconds(8) };
        if (camera.Username.Length > 0) {
            var credentials = new CredentialCache(); var origin = new Uri(CameraUri(camera.Url).GetLeftPart(UriPartial.Authority));
            var login = new NetworkCredential(camera.Username, camera.Password);
            credentials.Add(origin, "Basic", login); credentials.Add(origin, "Digest", login);
            handler.Credentials = credentials; handler.PreAuthenticate = true;
        }
        handler.ConnectCallback = async (context, token) => {
            var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, token).ConfigureAwait(false);
            var local = NetworkInterface.GetAllNetworkInterfaces().SelectMany(n => n.GetIPProperties().UnicastAddresses).Select(a => a.Address).ToArray();
            ValidateAddresses(addresses, local);
            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try { await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, token).ConfigureAwait(false); return new NetworkStream(socket, ownsSocket: true); }
            catch { socket.Dispose(); throw; }
        };
        return new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
    }

    internal static void ValidateAddresses(IPAddress[] addresses, IPAddress[] local) {
        static IPAddress Normal(IPAddress address) => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        var own = local.Select(Normal).ToArray();
        if (addresses.Length == 0 || addresses.Any(a => ForbiddenAddress(a) || own.Contains(Normal(a))))
            throw new HttpRequestException("Camera address is not allowed.");
    }

    public async Task<CameraSnapshot> View(CancellationToken token = default) {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try { var s = Load(); return new(s.Revision, s.Cameras.Select(c => new CameraView(c.Id, c.Name, c.Url, c.Mode, c.RefreshSeconds, c.ShowOnHome, c.Username, "", c.Password.Length > 0)).ToArray()); }
        finally { _gate.Release(); }
    }
    public async Task<CameraWidget[]> Widgets(CancellationToken token = default) {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try { var stored = Load(); return stored.Cameras.Where(c => c.ShowOnHome).Select(c => new CameraWidget(c.Id, c.Name, c.Mode, c.RefreshSeconds, stored.Revision)).ToArray(); }
        finally { _gate.Release(); }
    }

    public async Task<string> Save(CameraSetup setup, CancellationToken token = default) {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try {
            var stored = Load();
            if (setup.Revision != stored.Revision) throw new InvalidOperationException("Camera settings changed elsewhere. Reopen the dialog before saving.");
            var old = stored.Cameras.FirstOrDefault(c => c.Id == setup.Id);
            if (setup.Id != "" && old == null) throw new ArgumentException("Camera not found.");
            if (old == null && stored.Cameras.Length >= 8) throw new ArgumentException("Up to eight cameras can be saved.");
            string name = Clean(setup.Name, 128, "camera name"), url = Clean(setup.Url, 2048, "camera URL"), user = Clean(setup.Username, 256, "camera username");
            Validate(name, url, setup.Mode, setup.RefreshSeconds);
            if (setup.Password == null || setup.Password.Length > 1024 || setup.Password.Any(char.IsControl)) throw new ArgumentException("Enter a valid camera password.");
            bool identityChanged = old != null && (CameraUri(old.Url).GetLeftPart(UriPartial.Authority) != CameraUri(url).GetLeftPart(UriPartial.Authority) || old.Username != user);
            if (identityChanged && old!.Password.Length > 0 && setup.Password.Length == 0 && !setup.ClearPassword)
                throw new ArgumentException("Re-enter the password or clear credentials when changing the camera host or username.");
            string password = setup.ClearPassword || user.Length == 0 ? "" : setup.Password.Length > 0 ? setup.Password : old?.Password ?? "";
            string id = old?.Id ?? Guid.NewGuid().ToString("N");
            var camera = new CameraOptions(id, name, url, setup.Mode, setup.RefreshSeconds, setup.ShowOnHome, user, password);
            Store(old == null ? stored.Cameras.Append(camera).ToArray() : stored.Cameras.Select(c => c.Id == id ? camera : c).ToArray()); Cancel(id); return id;
        } finally { _gate.Release(); }
    }

    public async Task Remove(CameraRemoval removal, CancellationToken token = default) {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try {
            var stored = Load();
            if (removal.Revision != stored.Revision) throw new InvalidOperationException("Camera settings changed elsewhere. Reopen the dialog before removing.");
            if (!stored.Cameras.Any(c => c.Id == removal.Id)) throw new KeyNotFoundException("Camera not found.");
            Store(stored.Cameras.Where(c => c.Id != removal.Id).ToArray()); Cancel(removal.Id);
        } finally { _gate.Release(); }
    }
    void Cancel(string id) { if (_changes.Remove(id, out var change)) { change.Cancel(); change.Dispose(); } }

    public async Task<CameraFeed> Open(string id, CancellationToken token = default) {
        if (!ValidId(id)) throw new KeyNotFoundException("Camera not found.");
        if (!await _feeds.WaitAsync(0, token).ConfigureAwait(false)) throw new InvalidOperationException("All camera connections are busy. Pause a feed and retry.");
        HttpClient? client = null; HttpResponseMessage? response = null; CancellationTokenSource? deadline = null;
        try {
            CameraOptions camera;
            await _gate.WaitAsync(token).ConfigureAwait(false);
            try {
                camera = Load().Cameras.FirstOrDefault(c => c.Id == id) ?? throw new KeyNotFoundException("Camera not found.");
                if (!_changes.TryGetValue(id, out var change)) _changes[id] = change = new CancellationTokenSource();
                deadline = CancellationTokenSource.CreateLinkedTokenSource(token, change.Token); deadline.CancelAfter(TimeSpan.FromSeconds(10));
            } finally { _gate.Release(); }
            client = _client(camera);
            using var request = new HttpRequestMessage(HttpMethod.Get, CameraUri(camera.Url));
            request.Headers.Accept.ParseAdd("image/jpeg, image/png, multipart/x-mixed-replace");
            response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) throw new InvalidOperationException("Camera authentication failed. Check its username and password.");
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException("Camera returned HTTP " + (int)response.StatusCode + ". Check the feed URL.");
            var contentType = response.Content.Headers.ContentType;
            string? mediaType = contentType?.MediaType?.ToLowerInvariant();
            string kind;
            if (mediaType is "image/jpeg" or "image/png") kind = "snapshot";
            else if (string.Equals(contentType?.MediaType, "multipart/x-mixed-replace", StringComparison.OrdinalIgnoreCase)) {
                string boundary = contentType!.Parameters.FirstOrDefault(p => p.Name.Equals("boundary", StringComparison.OrdinalIgnoreCase))?.Value?.Trim('"') ?? "";
                if (!Regex.IsMatch(boundary, "^[0-9A-Za-z'()+_,./:=?\\-]{1,70}$")) throw new InvalidOperationException("Camera returned an invalid MJPEG boundary.");
                kind = "mjpeg";
            } else throw new InvalidOperationException("Camera URL is not a JPEG/PNG snapshot or MJPEG feed. Use the image endpoint, not its web page; RTSP needs a gateway.");
            if (camera.Mode != "auto" && camera.Mode != kind) throw new InvalidOperationException("Camera feed differs from the saved mode. Choose Automatic or the matching format.");
            if (kind == "mjpeg") deadline.CancelAfter(TimeSpan.FromMinutes(5));
            return new CameraFeed(client, response, deadline, kind, kind == "snapshot" ? mediaType! : contentType!.ToString(), () => _feeds.Release());
        } catch {
            response?.Dispose(); client?.Dispose(); deadline?.Dispose(); _feeds.Release(); throw;
        }
    }

    public void Dispose() { foreach (var id in _changes.Keys.ToArray()) Cancel(id); }
}

internal sealed class CameraFeed : IDisposable
{
    readonly HttpClient _client;
    readonly HttpResponseMessage _response;
    readonly CancellationTokenSource _deadline;
    readonly Action _release;
    bool _disposed;
    public string Kind { get; }
    public string ContentType { get; }
    public CameraFeed(HttpClient client, HttpResponseMessage response, CancellationTokenSource deadline, string kind, string contentType, Action release) {
        _client = client; _response = response; _deadline = deadline; Kind = kind; ContentType = contentType; _release = release;
    }
    public async Task<byte[]> Snapshot() {
        if (_response.Content.Headers.ContentLength > IpCameraService.MaxSnapshotBytes) throw new InvalidOperationException("Camera snapshot exceeds 5 MB.");
        using var output = new MemoryStream(); var buffer = new byte[32768];
        using var input = await _response.Content.ReadAsStreamAsync(_deadline.Token).ConfigureAwait(false);
        int count;
        while ((count = await input.ReadAsync(buffer, _deadline.Token).ConfigureAwait(false)) > 0) {
            if (output.Length + count > IpCameraService.MaxSnapshotBytes) throw new InvalidOperationException("Camera snapshot exceeds 5 MB.");
            output.Write(buffer, 0, count);
        }
        byte[] bytes = output.ToArray();
        bool valid = ContentType == "image/jpeg" ? bytes.Length >= 4 && bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff
            : bytes.Length >= 8 && bytes.Take(8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        if (!valid) throw new InvalidOperationException("Camera returned invalid image data.");
        return bytes;
    }
    public async Task CopyStream(Stream output, CancellationToken token) {
        using var input = await _response.Content.ReadAsStreamAsync(_deadline.Token).ConfigureAwait(false);
        var buffer = new byte[32768]; long total = 0;
        while (true) {
            using var idle = CancellationTokenSource.CreateLinkedTokenSource(token, _deadline.Token); idle.CancelAfter(TimeSpan.FromSeconds(15));
            int count = await input.ReadAsync(buffer, idle.Token).ConfigureAwait(false);
            if (count == 0) throw new InvalidOperationException("Camera stream ended. Reconnect to resume.");
            total += count; if (total > 512L * 1024 * 1024) throw new InvalidOperationException("Camera stream limit reached. Reconnect to resume.");
            await output.WriteAsync(buffer.AsMemory(0, count), idle.Token).ConfigureAwait(false); await output.FlushAsync(idle.Token).ConfigureAwait(false);
        }
    }
    public void Dispose() {
        if (_disposed) return; _disposed = true;
        _deadline.Cancel(); _response.Dispose(); _client.Dispose(); _deadline.Dispose(); _release();
    }
}
