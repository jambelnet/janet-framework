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
using jaNET.Infrastructure;
using jaNET.Scripting;
using jaNET.Servers;
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace jaNETFramework.Tests;

internal static class Ports
{
    public static int Free() {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }
}

/// <summary>The real web server on a free port, answering through a recording executor.</summary>
public class WebServerIntegrationTests : IDisposable
{
    readonly TempApp _app = new TempApp();
    readonly AppConfigStore _config;
    readonly SettingsStore _settings;
    readonly RecordingExecutor _executor = new RecordingExecutor { Answer = input => "answer:" + input };
    readonly HttpClient _client = new HttpClient();
    readonly WebServer _server;
    readonly int _port = Ports.Free();

    public WebServerIntegrationTests() {
        _config = _app.NewConfig();
        _settings = new SettingsStore(_app.Paths, _app.Log);
        _config.Update(new CommUpdate { Hostname = "localhost", HttpPort = _port.ToString() });
        _server = new WebServer(_config, _settings, _app.Paths, () => _executor, _app.Log);
    }

    string Url(string path) => $"http://localhost:{_port}{path}";

    public void Dispose() {
        _server.Dispose();
        _client.Dispose();
        _app.Dispose();
    }

    [Fact]
    public async Task AnswersTheJudoApiInTheRequestedFormat() {
        _server.Start();
        Assert.True(_server.IsRunning);

        HttpResponseMessage json = await _client.GetAsync(Url("/?cmd=yes&mode=json"));
        HttpResponseMessage text = await _client.GetAsync(Url("/www/?cmd=yes;no&mode=text"));
        HttpResponseMessage html = await _client.GetAsync(Url("/?cmd=yes"));

        Assert.Equal("answer:yes", await json.Content.ReadAsStringAsync());
        Assert.Equal("application/json", json.Content.Headers.ContentType.MediaType);
        Assert.Equal("answer:yes;no", await text.Content.ReadAsStringAsync());
        Assert.Equal("text/plain", text.Content.Headers.ContentType.MediaType);
        Assert.Equal("text/html", html.Content.Headers.ContentType.MediaType);
    }

    [Fact]
    public async Task ServesFilesOfTheApplicationDirectoryWithTheirContentType() {
        Directory.CreateDirectory(Path.Combine(_app.Directory, "www"));
        File.WriteAllText(Path.Combine(_app.Directory, "www", "index.html"), "<h1>hi</h1>");
        File.WriteAllText(Path.Combine(_app.Directory, "www", "app.js"), "export {};");
        _server.Start();

        HttpResponseMessage page = await _client.GetAsync(Url("/www/"));
        HttpResponseMessage script = await _client.GetAsync(Url("/www/app.js"));

        Assert.Equal("<h1>hi</h1>", await page.Content.ReadAsStringAsync());
        Assert.Equal("text/html", page.Content.Headers.ContentType.MediaType);
        Assert.Equal("text/javascript", script.Content.Headers.ContentType.MediaType);
    }

    [Fact]
    public async Task TheWebFolderWithoutASlashIsRedirectedToTheOneWithSlash() {
        Directory.CreateDirectory(Path.Combine(_app.Directory, "www"));
        File.WriteAllText(Path.Combine(_app.Directory, "www", "index.html"), "<h1>hi</h1>");
        _server.Start();
        using var noFollow = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });

        HttpResponseMessage redirect = await noFollow.GetAsync(Url("/www"));
        HttpResponseMessage withQuery = await noFollow.GetAsync(Url("/www?lang=fr"));
        HttpResponseMessage followed = await _client.GetAsync(Url("/www"));

        Assert.Equal(HttpStatusCode.MovedPermanently, redirect.StatusCode);
        Assert.Equal("/www/", redirect.Headers.Location.OriginalString);
        Assert.Equal("/www/?lang=fr", withQuery.Headers.Location.OriginalString);
        Assert.Equal("<h1>hi</h1>", await followed.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task TheRootIsRedirectedToTheWebFolder() {
        Directory.CreateDirectory(Path.Combine(_app.Directory, "www"));
        File.WriteAllText(Path.Combine(_app.Directory, "www", "index.html"), "<h1>hi</h1>");
        _server.Start();
        using var noFollow = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });

        HttpResponseMessage redirect = await noFollow.GetAsync(Url("/"));

        Assert.Equal(HttpStatusCode.MovedPermanently, redirect.StatusCode);
        Assert.Equal("/www/", redirect.Headers.Location.OriginalString);
    }

    [Fact]
    public async Task FoldersOutsideTheWebFolderAreNeverRedirected() {
        Directory.CreateDirectory(Path.Combine(_app.Directory, "www"));
        Directory.CreateDirectory(Path.Combine(_app.Directory, "secret"));
        _server.Start();
        using var noFollow = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });

        Assert.Equal(HttpStatusCode.NotFound, (await noFollow.GetAsync(Url("/secret"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await noFollow.GetAsync(Url("/www/../secret"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await noFollow.GetAsync(Url("/nothing"))).StatusCode);
    }

    [Fact]
    public async Task MissingFilesAreNotFound() {
        _server.Start();

        HttpResponseMessage response = await _client.GetAsync(Url("/www/nothing.html"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("404 Not Found", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task BasicAuthenticationNeedsTheSavedLogin() {
        _settings.Save(SettingsFiles.WebLogin, "bob\r\npw1");
        _config.Update(new CommUpdate { Authentication = "basic" });
        _server.Start();

        HttpResponseMessage anonymous = await _client.GetAsync(Url("/?cmd=yes&mode=text"));
        HttpResponseMessage wrong = await Get("/?cmd=yes&mode=text", "bob", "nope");
        HttpResponseMessage right = await Get("/?cmd=yes&mode=text", "bob", "pw1");

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.OK, right.StatusCode);
        Assert.Equal("answer:yes", await right.Content.ReadAsStringAsync());
    }

    async Task<HttpResponseMessage> Get(string path, string user, string password) {
        using var request = new HttpRequestMessage(HttpMethod.Get, Url(path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes(user + ":" + password)));
        return await _client.SendAsync(request);
    }

    [Fact]
    public async Task CanBeStoppedAndStartedAgain() {
        _server.Start();
        Assert.Equal("answer:yes", await _client.GetStringAsync(Url("/?cmd=yes&mode=text")));

        _server.Stop();
        Assert.False(_server.IsRunning);
        await Assert.ThrowsAsync<HttpRequestException>(() => _client.GetStringAsync(Url("/?cmd=yes&mode=text")));

        _server.Start();
        Assert.Equal("answer:yes", await _client.GetStringAsync(Url("/?cmd=yes&mode=text")));
    }

    [Fact]
    public void StartingWhileRunningDoesNothing() {
        _server.Start();

        _server.Start();

        Assert.True(_server.IsRunning);
    }

    [Fact]
    public void AnAddressThatIsInUseIsLoggedNotThrown() {
        using var blocker = new HttpListener();
        blocker.Prefixes.Add($"http://localhost:{_port}/");
        blocker.Start();

        _server.Start();

        Assert.False(_server.IsRunning);
        Assert.NotEmpty(_app.Log.Entries);
    }
}

/// <summary>The socket server on loopback.</summary>
public class SocketServerIntegrationTests : IDisposable
{
    readonly TempApp _app = new TempApp();
    readonly AppConfigStore _config;
    readonly RecordingExecutor _executor = new RecordingExecutor();
    readonly TcpSocketServer _server;
    readonly int _port = Ports.Free();

    public SocketServerIntegrationTests() {
        _config = _app.NewConfig();
        _config.Update(new CommUpdate { LocalHost = "127.0.0.1", LocalPort = _port.ToString(), Trusted = "127.0.0.1" });
        _server = new TcpSocketServer(_config, () => _executor, _app.Log);
    }

    public void Dispose() {
        _server.Dispose();
        _app.Dispose();
    }

    async Task<string> Ask(string request, bool expectReply = true) {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, _port);
        NetworkStream stream = client.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes(request));

        var buffer = new byte[1024];
        using var timeout = new System.Threading.CancellationTokenSource(3000);
        int read = await stream.ReadAsync(buffer, timeout.Token);
        return Encoding.ASCII.GetString(buffer, 0, read);
    }

    [Fact]
    public async Task AnswersALineWithALine() {
        _server.Start();
        Assert.True(_server.IsRunning);

        Assert.Equal("ran yes\r\n", await Ask("yes\r\n"));
    }

    [Fact]
    public async Task AnswersABrowserRequest() {
        _server.Start();

        Assert.Equal("ran whoami\r\n", await Ask("GET /whoami HTTP/1.1\r\nHost: x\r\n\r\n"));
    }

    [Fact]
    public async Task DecodesTheEscapesOfTheJudoApi() {
        _server.Start();

        await Ask("judo%20inset%20ls\r\n");

        Assert.Equal(new[] { "judo inset ls" }, _executor.Calls);
    }

    [Fact]
    public async Task OnlyTrustedAddressesAreServed() {
        _config.Update(new CommUpdate { Trusted = "10.9.9.9; 192.168.1.1" });
        _server.Start();

        Assert.Equal("Attempted to perform an unauthorized operation.\r\n", await Ask("yes\r\n"));
        Assert.Empty(_executor.Calls);
    }

    [Fact]
    public async Task LocalhostInTheTrustedListMeansTheLoopbackAddress() {
        _config.Update(new CommUpdate { Trusted = "localhost; 192.168.1.1" });
        _server.Start();

        Assert.Equal("ran yes\r\n", await Ask("yes\r\n"));
    }

    [Fact]
    public async Task CanBeStoppedAndStartedAgain() {
        _server.Start();
        _server.Stop();
        Assert.False(_server.IsRunning);
        await Assert.ThrowsAnyAsync<SocketException>(() => Ask("yes\r\n"));

        _server.Start();
        Assert.Equal("ran yes\r\n", await Ask("yes\r\n"));
    }
}

public class TrustPolicyTests
{
    [Theory]
    [InlineData("127.0.0.1; 192.168.1.1", "192.168.1.1", true)]
    [InlineData("127.0.0.1; 192.168.1.1", "127.0.0.1", true)]
    [InlineData("localhost; 192.168.1.1", "127.0.0.1", true)]
    [InlineData("10.0.0.1,10.0.0.2", "10.0.0.2", true)]
    [InlineData("10.0.0.1 10.0.0.2", "10.0.0.2", true)]
    [InlineData("127.0.0.1", "127.0.0.2", false)]
    [InlineData("11.1.1.10", "1.1.1.1", false)]          // used to match: a substring test
    [InlineData("192.168.1.10", "192.168.1.1", false)]
    [InlineData("", "127.0.0.1", false)]
    public void MatchesWholeAddresses(string trusted, string remote, bool expected) {
        Assert.Equal(expected, TrustPolicy.IsTrusted(trusted, remote));
    }
}
