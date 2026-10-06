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
using jaNET.Servers;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace jaNETFramework.Tests;

public class InstructionSetInfoTests
{
    [Fact]
    public void ListsInstructionSetsWithoutTheirActions() {
        using var app = new TempApp();
        var config = app.NewConfig();

        var all = config.InstructionSets();

        InstructionSetInfo whoami = all.Single(i => i.Id == "whoami");
        Assert.Equal("System", whoami.Category);
        Assert.Equal("Login name", whoami.Header);
        Assert.Equal("Get user login", whoami.ShortDescription);
        Assert.Equal("System login", whoami.Description);
        Assert.Equal("/www/images/icon-set/user.png", whoami.Thumbnail);
        Assert.Equal("whoamiwidget", whoami.Reference);

        InstructionSetInfo launcher = all.Single(i => i.Id == "*whoami");
        Assert.Null(launcher.Header);
        Assert.Null(launcher.Reference);
    }
}

/// <summary>The web server's /api/instructions endpoint and the rule that only the www folder is served.</summary>
public class ConfigEndpointTests : IDisposable
{
    readonly TempApp _app = new TempApp();
    readonly AppConfigStore _config;
    readonly SettingsStore _settings;
    readonly HttpClient _client = new HttpClient();
    readonly WebServer _server;
    readonly int _port = Ports.Free();

    public ConfigEndpointTests() {
        _config = _app.NewConfig();
        _settings = new SettingsStore(_app.Paths, _app.Log);
        _config.Update(new CommUpdate { Hostname = "localhost", HttpPort = _port.ToString() });
        _server = new WebServer(_config, _settings, _app.Paths, () => new RecordingExecutor(), _app.Log);
    }

    string Url(string path) => $"http://localhost:{_port}{path}";

    public void Dispose() {
        _server.Dispose();
        _client.Dispose();
        _app.Dispose();
    }

    [Fact]
    public async Task ServesTheInstructionSetsAsJson() {
        _server.Start();

        HttpResponseMessage response = await _client.GetAsync(Url("/api/instructions"));
        using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal("application/json", response.Content.Headers.ContentType.MediaType);
        JsonElement whoami = json.RootElement.EnumerateArray().Single(e => e.GetProperty("id").GetString() == "whoami");
        Assert.Equal("System", whoami.GetProperty("categ").GetString());
        Assert.Equal("Login name", whoami.GetProperty("header").GetString());
        Assert.Equal("whoamiwidget", whoami.GetProperty("ref").GetString());
        JsonElement launcher = json.RootElement.EnumerateArray().Single(e => e.GetProperty("id").GetString() == "*whoami");
        Assert.Equal(JsonValueKind.Null, launcher.GetProperty("header").ValueKind);
    }

    [Fact]
    public async Task NeverRevealsTheActions() {
        _server.Start();

        string body = await _client.GetStringAsync(Url("/api/instructions"));

        Assert.DoesNotContain("You are, %user%.", body);
    }

    [Fact]
    public async Task ShowsChangesAtOnce() {
        _server.Start();
        _config.AddInstructionSets(new[] { new InstructionSetEntry("fresh", "*fresh", Header: "Fresh one", Category: "New") });

        string body = await _client.GetStringAsync(Url("/api/instructions"));

        Assert.Contains("Fresh one", body);
    }

    [Fact]
    public async Task NeedsTheLoginWhenAuthenticationIsOn() {
        _settings.Save(SettingsFiles.WebLogin, "bob\r\npw1");
        _config.Update(new CommUpdate { Authentication = "basic" });
        _server.Start();

        HttpResponseMessage anonymous = await _client.GetAsync(Url("/api/instructions"));
        using var request = new HttpRequestMessage(HttpMethod.Get, Url("/api/instructions"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes("bob:pw1")));
        HttpResponseMessage allowed = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    [Theory]
    [InlineData("/AppConfig.xml")]
    [InlineData("/.htaccess")]
    [InlineData("/.smtpsettings")]
    [InlineData("/log.txt")]
    [InlineData("/www/../AppConfig.xml")]
    [InlineData("/www/%2e%2e/AppConfig.xml")]
    public async Task InternalFilesAreNotServed(string path) {
        File.WriteAllText(Path.Combine(_app.Directory, ".htaccess"), "secret");
        File.WriteAllText(Path.Combine(_app.Directory, ".smtpsettings"), "secret");
        File.WriteAllText(Path.Combine(_app.Directory, "log.txt"), "secret");
        _server.Start();

        HttpResponseMessage response = await _client.GetAsync(Url(path));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("secret", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task TheWwwFolderIsStillServed() {
        Directory.CreateDirectory(Path.Combine(_app.Directory, "www", "images"));
        File.WriteAllText(Path.Combine(_app.Directory, "www", "images", "a.png"), "png");
        _server.Start();

        Assert.Equal("png", await _client.GetStringAsync(Url("/www/images/a.png")));
    }
}

public class ServableFilesTests
{
    static WebServer NewServer(TempApp app) =>
        new WebServer(app.NewConfig(), new SettingsStore(app.Paths, app.Log), app.Paths, () => throw new NotSupportedException(), app.Log);

    [Theory]
    [InlineData("AppConfig.xml")]
    [InlineData("AppConfig.xml.bak")]
    [InlineData(".htaccess")]
    [InlineData("log.txt")]
    [InlineData("jaNETProgram.dll")]
    [InlineData("wwwroot/index.html")]        // a sibling whose name merely starts like "www"
    public void NothingOutsideWwwIsServable(string relative) {
        using var app = new TempApp();

        Assert.Throws<FileNotFoundException>(() => NewServer(app).EnsureServable(app.Paths.Root + relative.Replace('/', Path.DirectorySeparatorChar)));
    }

    [Fact]
    public void FilesBelowWwwAreServable() {
        using var app = new TempApp();

        NewServer(app).EnsureServable(app.Paths.Root + Path.Combine("www", "js", "app.js"));
    }
}
