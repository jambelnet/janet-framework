// (c) J@mBeL.net 2010-2026. GNU GPL version 3 or later.
// This file is part of jaNET Framework.
using jaNET.Configuration;
using jaNET.Servers;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml.Linq;
using Xunit;

namespace jaNETFramework.Tests;

public class StudioConfigurationTests
{
    [Fact]
    public void CreatesAndEditsWithoutAppendingOrLosingAttributes() {
        using var app = new TempApp(); var config = app.NewConfig();
        var initial = config.Studio(new[] { "%custom%" });
        Assert.Contains("%custom%", initial.Functions);
        Assert.Equal("Instruction saved.", config.SaveStudio(new StudioSave("studio_test", initial.Revision, new[] { "First", "Second" }, true, "Test", "User")));
        var doc = XDocument.Load(app.Paths.ConfigFile);
        var entries = doc.Descendants("InstructionSet").Where(e => (string)e.Attribute("id") == "studio_test").ToArray();
        entries[0].SetAttributeValue("custom", "keep"); doc.Save(app.Paths.ConfigFile);
        var current = config.Studio(Array.Empty<string>());
        Assert.Equal("Instruction saved.", config.SaveStudio(new StudioSave("studio_test", current.Revision, new[] { "Changed & <text>" })));
        Assert.Equal(new[] { "Changed & <text>" }, config.InstructionActions("studio_test"));
        doc = XDocument.Load(app.Paths.ConfigFile);
        var entry = doc.Descendants("InstructionSet").Single(e => (string)e.Attribute("id") == "studio_test");
        Assert.Equal("keep", (string)entry.Attribute("custom"));
        Assert.Equal("Test", (string)entry.Attribute("header"));
        Assert.Equal("User", (string)entry.Attribute("categ"));
        Assert.True(File.Exists(app.Paths.ConfigFile + ".bak"));
    }

    [Fact]
    public void RejectsStaleRevisionWithoutChangingConfiguration() {
        using var app = new TempApp(); var config = app.NewConfig(); var snapshot = config.Studio(Array.Empty<string>());
        config.AddInstructionSets(new[] { new InstructionSetEntry("other", "Untouched") });
        string before = File.ReadAllText(app.Paths.ConfigFile);
        Assert.StartsWith("Studio conflict:", config.SaveStudio(new StudioSave("whoami", snapshot.Revision, new[] { "Overwrite" })));
        Assert.Equal(before, File.ReadAllText(app.Paths.ConfigFile));
    }

    [Fact]
    public void ResizesOnlyCanonicalLegacyPointerPairs() {
        using var app = new TempApp(); var config = app.NewConfig();
        config.AddInstructionSets(new[] { new InstructionSetEntry("pair", "*pair", Header: "Pair"), new InstructionSetEntry("*pair", "One"), new InstructionSetEntry("unrelated", "Keep") });
        Assert.Equal("Instruction saved.", config.SaveStudio(new StudioSave("*pair", config.Studio(Array.Empty<string>()).Revision, new[] { "First", "Second" })));
        Assert.Equal(new[] { "*pair", "*pair" }, config.InstructionActions("pair"));
        Assert.Equal(new[] { "First", "Second" }, config.InstructionActions("*pair"));
        Assert.Equal(new[] { "Keep" }, config.InstructionActions("unrelated"));
        Assert.Equal("Instruction saved.", config.SaveStudio(new StudioSave("*pair", config.Studio(Array.Empty<string>()).Revision, new[] { "Only" })));
        Assert.Single(config.InstructionActions("pair"));
        config.AddInstructionSets(new[] { new InstructionSetEntry("complex", "Before *complex after"), new InstructionSetEntry("*complex", "One") });
        config.SaveStudio(new StudioSave("*complex", config.Studio(Array.Empty<string>()).Revision, new[] { "A", "B" }));
        Assert.Equal(new[] { "Before *complex after" }, config.InstructionActions("complex"));
    }

    [Theory]
    [InlineData("bad name")]
    [InlineData("bad;name")]
    [InlineData("*new")]
    public void ValidatesNewIdentifiers(string id) {
        using var app = new TempApp(); var config = app.NewConfig();
        Assert.Throws<ArgumentException>(() => config.SaveStudio(new StudioSave(id, config.Studio(Array.Empty<string>()).Revision, new[] { "Text" }, true)));
    }

    [Fact]
    public void ValidatesActionLimitsAndDuplicateNames() {
        using var app = new TempApp(); var config = app.NewConfig(); var snapshot = config.Studio(Array.Empty<string>());
        Assert.Throws<ArgumentException>(() => config.SaveStudio(new StudioSave("new", snapshot.Revision, Array.Empty<string>(), true)));
        Assert.Throws<ArgumentException>(() => config.SaveStudio(new StudioSave("new", snapshot.Revision, new[] { new string('a', 65537) }, true)));
        Assert.StartsWith("Studio conflict:", config.SaveStudio(new StudioSave("whoami", snapshot.Revision, new[] { "Overwrite" }, true)));
    }

    [Fact]
    public void ExplicitInstructionCallsPreserveOutputsAndBoundCycles() {
        using var host = new TestHost();
        Assert.Equal(host.Run("whoami"), host.Run("judo inset run whoami"));
        Assert.Contains("not found", host.Run("judo inset run nonexistent"));
        Assert.StartsWith("Error:", host.Run("judo inset run bad name"));
        var config = new AppConfigStore(new jaNET.Infrastructure.AppPaths(host.Directory), new MemoryLog());
        config.AddInstructionSets(new[] { new InstructionSetEntry("studio_cycle", "judo inset run studio_cycle") });
        Assert.Contains("call depth exceeded", host.Run("studio_cycle"));
        for (int i = 0; i < 40; i++) Assert.DoesNotContain("call depth exceeded", host.Run("whoami"));
    }
}

public class StudioEndpointTests : IDisposable
{
    readonly TempApp app = new();
    readonly AppConfigStore config;
    readonly SettingsStore settings;
    readonly RecordingExecutor executor = new();
    readonly HttpClient client = new();
    readonly WebServer server;
    readonly int port = Ports.Free();
    public StudioEndpointTests() {
        config = app.NewConfig(); settings = new SettingsStore(app.Paths, app.Log);
        config.Update(new CommUpdate { Hostname = "localhost", HttpPort = port.ToString() });
        server = new WebServer(config, settings, app.Paths, () => executor, app.Log, functionNames: () => new[] { "%user%" });
    }
    string Url => $"http://localhost:{port}/api/studio";
    public void Dispose() { server.Dispose(); client.Dispose(); app.Dispose(); }

    [Fact]
    public async Task SnapshotAndSaveNeverExecuteActionsAndAreNotCached() {
        server.Start();
        var response = await client.GetAsync(Url);
        Assert.True(response.Headers.CacheControl.NoStore);
        var snapshot = await response.Content.ReadFromJsonAsync<StudioSnapshot>();
        Assert.Contains("%user%", snapshot.Functions);
        Assert.Contains(snapshot.Instructions, entry => entry.Id == "*whoami" && entry.Action.Contains("%user%"));
        response = await client.PostAsJsonAsync(Url, new StudioSave("safe_new", snapshot.Revision, new[] { "./danger.exe" }, true));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new[] { "./danger.exe" }, config.InstructionActions("safe_new"));
        Assert.Empty(executor.Calls);
        response = await client.PostAsJsonAsync(Url, new StudioSave("safe_new", snapshot.Revision, new[] { "Wrong" }));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    public async Task RejectsCrossOriginRequests(string method) {
        server.Start(); using var request = new HttpRequestMessage(new HttpMethod(method), Url);
        request.Headers.Add("Origin", "https://untrusted.example");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task EnforcesAuthenticationAndValidatesBodies() {
        settings.Save(SettingsFiles.WebLogin, "bob\r\npw1"); config.Update(new CommUpdate { Authentication = "basic" }); server.Start();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Url)).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes("bob:pw1")));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Url)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync(Url, new StringContent("{", Encoding.UTF8, "application/json"))).StatusCode);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await client.PostAsync(Url, new StringContent("text"))).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PutAsync(Url, null)).StatusCode);
    }
}
