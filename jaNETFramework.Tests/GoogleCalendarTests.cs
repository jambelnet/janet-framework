/* (c) J@mBeL.net 2010-2026, John Ambeliotis.
 * This file is part of jaNET Framework.
 * jaNET Framework is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.
 * jaNET Framework is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty
 * of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU General Public License for more details.
 * You should have received a copy of the GNU General Public License along with jaNET Framework. If not, see <http://www.gnu.org/licenses/>. */
using jaNET.Commands;
using jaNET.Configuration;
using jaNET.Hosting;
using jaNET.Services;
using jaNET.Servers;
using Microsoft.AspNetCore.WebUtilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace jaNETFramework.Tests;

public class GoogleCalendarTests
{
    sealed class Google : HttpMessageHandler
    {
        public List<(string Method, string Url, string Body, string Authorization)> Requests = new();
        public Func<HttpRequestMessage, HttpResponseMessage> Reply = _ => new(HttpStatusCode.OK) { Content = new StringContent("{\"items\":[]}") };
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            Requests.Add((request.Method.Method, request.RequestUri!.ToString(), request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken), request.Headers.Authorization?.ToString() ?? ""));
            return Reply(request);
        }
        public static HttpResponseMessage Json(string json, HttpStatusCode code = HttpStatusCode.OK) => new(code) { Content = new StringContent(json) };
    }
    static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-14T10:00:00Z");
    const string Client = "test.apps.googleusercontent.com", Secret = "private-client-secret", Refresh = "private-refresh-token", Access = "private-access-token";
    static CalendarSetup Setup(string redirect = "http://localhost:8080/api/calendar/callback") => new(Client, Secret, redirect, "primary", "Europe/Paris");
    static void Connected(ISettingsStore settings, DateTimeOffset? expires = null) => settings.Save(SettingsFiles.GoogleCalendar,
        JsonSerializer.Serialize(new CalendarOptions(Client, Secret, Setup().RedirectUri, "primary", "Europe/Paris", Refresh, Access, expires ?? Now.AddHours(1))));

    [Fact]
    public async Task EncryptedStorageAndViewsNeverExportTokensOrClientSecrets() {
        using var app = new TempApp(); var store = new SettingsStore(app.Paths, app.Log);
        Connected(store);
        using var service = new GoogleCalendarService(store, now: () => Now);
        string view = JsonSerializer.Serialize(await service.View());
        Assert.Contains("secretConfigured", view); Assert.Contains(Client, view);
        foreach (string secret in new[] { Secret, Refresh, Access }) {
            Assert.DoesNotContain(secret, view);
            Assert.DoesNotContain(secret, System.IO.File.ReadAllText(app.Paths.File(SettingsFiles.GoogleCalendar)));
            Assert.DoesNotContain(app.Log.Entries, line => line.Contains(secret));
        }
    }

    [Fact]
    public async Task BlankSecretsAreRetainedAndChangingOAuthIdentityClearsAccess() {
        var store = new MemorySettingsStore(); Connected(store);
        using var service = new GoogleCalendarService(store, now: () => Now);
        await service.Save(Setup() with { ClientSecret = "", CalendarId = "other@example.org", TimeZone = "UTC" });
        var kept = JsonSerializer.Deserialize<CalendarOptions>(store.Load(SettingsFiles.GoogleCalendar)![0])!;
        Assert.Equal(Secret, kept.ClientSecret); Assert.Equal(Refresh, kept.RefreshToken); Assert.Equal("other@example.org", kept.CalendarId);
        await service.Save(Setup() with { ClientSecret = "replacement-secret" });
        var changed = JsonSerializer.Deserialize<CalendarOptions>(store.Load(SettingsFiles.GoogleCalendar)![0])!;
        Assert.Equal("", changed.AccessToken); Assert.Equal("", changed.RefreshToken);
    }

    [Theory]
    [InlineData("http://home.example.org/api/calendar/callback")]
    [InlineData("https://192.168.178.23:8443/api/calendar/callback")]
    [InlineData("https://8.8.8.8/api/calendar/callback")]
    [InlineData("https://[2001:db8::1]/api/calendar/callback")]
    [InlineData("https://home.example.org/api/calendar/callback?bad=1")]
    [InlineData("https://home.example.org/wrong")]
    [InlineData("https://user:pass@home.example.org/api/calendar/callback")]
    [InlineData("javascript:alert(1)")]
    public async Task InvalidRedirectsFailBeforeWriting(string redirect) {
        var store = new MemorySettingsStore(); using var service = new GoogleCalendarService(store);
        await Assert.ThrowsAsync<ArgumentException>(() => service.Save(Setup(redirect)));
        Assert.False(store.Exists(SettingsFiles.GoogleCalendar));
    }

    [Theory]
    [InlineData("http://localhost:8080/api/calendar/callback")]
    [InlineData("https://localhost:8443/api/calendar/callback")]
    [InlineData("http://127.0.0.1:8080/api/calendar/callback")]
    [InlineData("http://[::1]:8080/api/calendar/callback")]
    [InlineData("https://janet.example.org:8443/api/calendar/callback")]
    public async Task DomainAndLoopbackRedirectsRemainSupported(string redirect) {
        var store = new MemorySettingsStore(); using var service = new GoogleCalendarService(store);
        await service.Save(Setup(redirect));
        var connection = await service.Connect(new Uri(redirect).GetLeftPart(UriPartial.Authority));
        Assert.Equal(redirect, QueryHelpers.ParseQuery(new Uri(connection.Url).Query)["redirect_uri"]);
    }

    [Fact]
    public async Task PreviouslySavedIpRedirectCannotStartGoogleSignInAndIsNotRewritten() {
        var store = new MemorySettingsStore(); Connected(store);
        var old = JsonSerializer.Deserialize<CalendarOptions>(store.Load(SettingsFiles.GoogleCalendar)![0])! with { RedirectUri = "https://192.168.178.23:8443/api/calendar/callback" };
        store.Save(SettingsFiles.GoogleCalendar, JsonSerializer.Serialize(old));
        using var service = new GoogleCalendarService(store);
        Assert.Contains("IP-address", (await Assert.ThrowsAsync<ArgumentException>(() => service.Connect("https://192.168.178.23:8443"))).Message);
        await Assert.ThrowsAsync<ArgumentException>(() => service.Save(Setup(old.RedirectUri)));
        Assert.Equal(old, JsonSerializer.Deserialize<CalendarOptions>(store.Load(SettingsFiles.GoogleCalendar)![0]));
    }

    [Fact]
    public async Task InvalidZonesAndDamagedSettingsDoNotOverwriteStoredData() {
        var store = new MemorySettingsStore(); using var service = new GoogleCalendarService(store);
        await Assert.ThrowsAsync<ArgumentException>(() => service.Save(Setup() with { TimeZone = "not-a-zone" }));
        store.Save(SettingsFiles.GoogleCalendar, "broken");
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.Save(Setup()));
        Assert.Equal("broken", store.Load(SettingsFiles.GoogleCalendar)![0]);
        store.Save(SettingsFiles.GoogleCalendar, "{\"ClientSecret\":null}");
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.Save(Setup()));
        Assert.Contains("null", store.Load(SettingsFiles.GoogleCalendar)![0]);
    }

    [Fact]
    public async Task AuthorizationUsesReadOnlyScopeOfflineAccessPkceAndSingleUseBrowserBoundState() {
        var store = new MemorySettingsStore(); using var google = new Google(); using var http = new HttpClient(google);
        google.Reply = _ => Google.Json($$"""{"access_token":"{{Access}}","refresh_token":"{{Refresh}}","expires_in":3600,"token_type":"Bearer","scope":"{{GoogleCalendarService.Scope}}"}""");
        using var service = new GoogleCalendarService(store, http, () => Now);
        await service.Save(Setup());
        await Assert.ThrowsAsync<ArgumentException>(() => service.Connect("http://wrong-host:8080"));
        var connection = await service.Connect("http://localhost:8080"); var query = QueryHelpers.ParseQuery(new Uri(connection.Url).Query);
        Assert.Equal(GoogleCalendarService.Scope, query["scope"]); Assert.Equal("offline", query["access_type"]); Assert.Equal("S256", query["code_challenge_method"]);
        Assert.DoesNotContain(Secret, connection.Url);
        await Assert.ThrowsAsync<ArgumentException>(() => service.Complete(connection.State, "another-browser", "code", ""));
        await Assert.ThrowsAsync<ArgumentException>(() => service.Complete("wrong-state", connection.Browser, "code", ""));
        Assert.Empty(google.Requests);
        await service.Complete(connection.State, connection.Browser, "google-code", "");
        var post = QueryHelpers.ParseQuery(google.Requests.Single().Body);
        Assert.Equal("authorization_code", post["grant_type"]); Assert.Equal("google-code", post["code"]);
        Assert.Equal(query["code_challenge"], WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(post["code_verifier"].ToString()))));
        await Assert.ThrowsAsync<ArgumentException>(() => service.Complete(connection.State, connection.Browser, "google-code", ""));
        Assert.Contains(Refresh, store.Load(SettingsFiles.GoogleCalendar)![0]);
    }

    [Fact]
    public async Task ExpiredOrDeniedSignInDoesNotConnect() {
        var store = new MemorySettingsStore(); DateTimeOffset now = Now;
        using var service = new GoogleCalendarService(store, now: () => now); await service.Save(Setup());
        var connection = await service.Connect("http://localhost:8080"); now = now.AddMinutes(11);
        await Assert.ThrowsAsync<ArgumentException>(() => service.Complete(connection.State, connection.Browser, "code", ""));
        connection = await service.Connect("http://localhost:8080");
        await Assert.ThrowsAsync<ArgumentException>(() => service.Complete(connection.State, connection.Browser, "", "access_denied"));
        Assert.Equal("", JsonSerializer.Deserialize<CalendarOptions>(store.Load(SettingsFiles.GoogleCalendar)![0])!.RefreshToken);
    }

    [Fact]
    public async Task RefreshAndPaginationExpandRecurrencesAndKeepAllDayDates() {
        var store = new MemorySettingsStore(); Connected(store, Now.AddSeconds(20));
        using var google = new Google(); using var http = new HttpClient(google);
        google.Reply = request => request.RequestUri!.AbsolutePath == "/token"
            ? Google.Json("{\"access_token\":\"fresh-access-token\",\"expires_in\":3600,\"token_type\":\"Bearer\"}")
            : !request.RequestUri.Query.Contains("pageToken=") ? Google.Json("{\"items\":[],\"nextPageToken\":\"page+two\"}")
            : Google.Json("""{"items":[{"id":"day","summary":"School holiday","start":{"date":"2026-10-14"},"end":{"date":"2026-10-16"}},{"id":"meeting","summary":"Home visit","location":"Living room","start":{"dateTime":"2026-10-14T13:00:00+02:00"},"end":{"dateTime":"2026-10-14T14:00:00+02:00"}},{"status":"cancelled"}]}""");
        using var service = new GoogleCalendarService(store, http, () => Now);
        var agenda = await service.Events();
        Assert.Equal(2, agenda.Events.Count); Assert.True(agenda.Events[0].AllDay); Assert.Equal("2026-10-16", agenda.Events[0].End);
        Assert.Contains("2026-10-14 (all day) - School holiday", GoogleCalendarService.Format(agenda));
        Assert.Contains("13:00 +02:00 - Home visit - Living room", GoogleCalendarService.Format(agenda));
        Assert.Equal(3, google.Requests.Count);
        var query = QueryHelpers.ParseQuery(new Uri(google.Requests[1].Url).Query);
        Assert.Equal("true", query["singleEvents"]); Assert.Equal("startTime", query["orderBy"]); Assert.Equal("Europe/Paris", query["timeZone"]);
        Assert.Equal("Bearer fresh-access-token", google.Requests[2].Authorization);
        Assert.Equal(Refresh, JsonSerializer.Deserialize<CalendarOptions>(store.Load(SettingsFiles.GoogleCalendar)![0])!.RefreshToken);
    }

    [Fact]
    public async Task TodayUsesCalendarTimezoneAndDaylightSavingMidnightBoundaries() {
        var store = new MemorySettingsStore(); DateTimeOffset now = DateTimeOffset.Parse("2026-10-25T12:00:00Z"); Connected(store, now.AddHours(1));
        using var google = new Google(); using var http = new HttpClient(google); using var service = new GoogleCalendarService(store, http, () => now);
        var agenda = await service.Events("today");
        var query = QueryHelpers.ParseQuery(new Uri(google.Requests.Single().Url).Query);
        Assert.Equal("2026-10-25T00:00:00.0000000+02:00", query["timeMin"]); Assert.Equal("2026-10-26T00:00:00.0000000+01:00", query["timeMax"]);
        Assert.Equal("No events in this period.", GoogleCalendarService.Format(agenda));
    }

    [Fact]
    public async Task ConcurrentReadsRefreshOnlyOnceAndPersistRotatedTokens() {
        var store = new MemorySettingsStore(); Connected(store, Now);
        using var google = new Google(); using var http = new HttpClient(google);
        google.Reply = request => request.RequestUri!.AbsolutePath == "/token"
            ? Google.Json("{\"access_token\":\"fresh-access\",\"refresh_token\":\"rotated-refresh\",\"expires_in\":3600,\"token_type\":\"Bearer\"}")
            : Google.Json("{\"items\":[]}");
        using var service = new GoogleCalendarService(store, http, () => Now);
        await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => service.Events()));
        Assert.Single(google.Requests, r => r.Method == "POST");
        Assert.Equal("rotated-refresh", JsonSerializer.Deserialize<CalendarOptions>(store.Load(SettingsFiles.GoogleCalendar)![0])!.RefreshToken);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"items\":{}}")]
    [InlineData("{\"items\":[{\"id\":\"missing-dates\"}]}")]
    public async Task MalformedSuccessfulResponsesAreNotPresentedAsEmptyAgendas(string body) {
        var store = new MemorySettingsStore(); Connected(store);
        using var google = new Google(); using var http = new HttpClient(google); google.Reply = _ => Google.Json(body);
        using var service = new GoogleCalendarService(store, http, () => Now);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.Events());
    }

    [Fact]
    public async Task AuthorizationWithoutOfflineAccessOrReadPermissionDoesNotSaveTokens() {
        var store = new MemorySettingsStore(); using var google = new Google(); using var http = new HttpClient(google);
        using var service = new GoogleCalendarService(store, http, () => Now); await service.Save(Setup());
        google.Reply = _ => Google.Json("{\"access_token\":\"access\",\"expires_in\":3600,\"token_type\":\"Bearer\"}");
        var first = await service.Connect("http://localhost:8080");
        Assert.Contains("offline", (await Assert.ThrowsAsync<InvalidOperationException>(() => service.Complete(first.State, first.Browser, "code", ""))).Message);
        google.Reply = _ => Google.Json("{\"access_token\":\"access\",\"refresh_token\":\"refresh\",\"expires_in\":3600,\"token_type\":\"Bearer\",\"scope\":\"openid\"}");
        var second = await service.Connect("http://localhost:8080");
        Assert.Contains("not granted", (await Assert.ThrowsAsync<InvalidOperationException>(() => service.Complete(second.State, second.Browser, "code", ""))).Message);
        Assert.Equal("", JsonSerializer.Deserialize<CalendarOptions>(store.Load(SettingsFiles.GoogleCalendar)![0])!.RefreshToken);
    }

    [Theory]
    [InlineData(401, "authorization")]
    [InlineData(403, "denied")]
    [InlineData(404, "not found")]
    [InlineData(429, "rate limit")]
    [InlineData(500, "unavailable")]
    public async Task GoogleFailuresHaveUsefulMessagesWithoutEchoingResponseOrCredentials(int code, string text) {
        var store = new MemorySettingsStore(); Connected(store);
        using var google = new Google(); google.Reply = _ => Google.Json(Secret + Refresh + Access, (HttpStatusCode)code);
        using var http = new HttpClient(google); using var service = new GoogleCalendarService(store, http, () => Now);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.Events());
        Assert.Contains(text, error.Message); Assert.DoesNotContain(Secret, error.Message); Assert.DoesNotContain(Access, error.Message);
    }

    [Fact]
    public async Task RevokedRefreshAndDisconnectFailClosedWithoutBreakingOtherSettings() {
        var store = new MemorySettingsStore(); Connected(store, Now); store.Save(SettingsFiles.Gmail, "unchanged-mail-settings");
        using var google = new Google(); google.Reply = _ => Google.Json("{\"error\":\"invalid_grant\"}", HttpStatusCode.BadRequest);
        using var http = new HttpClient(google); using var service = new GoogleCalendarService(store, http, () => Now);
        Assert.Contains("revoked", (await Assert.ThrowsAsync<InvalidOperationException>(() => service.Events())).Message);
        Assert.Contains("disconnected locally", await service.Disconnect());
        Assert.Contains("not connected", (await Assert.ThrowsAsync<InvalidOperationException>(() => service.Events())).Message);
        Assert.Equal("unchanged-mail-settings", store.Load(SettingsFiles.Gmail)![0]);
        var stored = JsonSerializer.Deserialize<CalendarOptions>(store.Load(SettingsFiles.GoogleCalendar)![0])!;
        Assert.Equal("", stored.RefreshToken); Assert.Equal("", stored.AccessToken); Assert.Equal(Secret, stored.ClientSecret);
    }

    [Fact]
    public async Task CommandsReturnTitlesAsDataNotAsInstructionActions() {
        var store = new MemorySettingsStore(); Connected(store); using var google = new Google();
        const string title = "%exit%; *bad { evalBool(true); danger; danger; } & ./bad";
        google.Reply = _ => Google.Json(JsonSerializer.Serialize(new { items = new[] { new { id = "a", summary = title, start = new { date = "2026-10-14" }, end = new { date = "2026-10-15" } } } }));
        using var http = new HttpClient(google); using var service = new GoogleCalendarService(store, http, () => Now);
        var command = new CalendarCommand(service);
        string text = command.Execute(new("judo calendar next", new[] { "judo", "calendar", "next" }));
        Assert.DoesNotContain("*bad", text); Assert.DoesNotContain("evalBool", text); Assert.DoesNotContain("%exit%", text);
        string json = command.Execute(new("judo calendar json", new[] { "judo", "calendar", "json" }));
        Assert.DoesNotContain("*bad", json); Assert.DoesNotContain("evalBool", json); Assert.DoesNotContain("%exit%", json);
        Assert.Equal(title, JsonDocument.Parse(command.Execute(new("judo calendar json", new[] { "judo", "calendar", "json" }))).RootElement.GetProperty("events")[0].GetProperty("summary").GetString());
        Assert.All(google.Requests, r => Assert.Equal("GET", r.Method));
        using var app = new TempApp(); bool exited = false;
        using (var fullHost = new JanetHost(new JanetHostOptions { RootDirectory = app.Paths.Root, ExitProcess = () => exited = true }, new HostParts {
            Settings = store, Calendar = service, Speaker = new SilentSpeaker { Muted = true }, Web = new FakeServer(), Socket = new FakeServer(), Serial = new FakeSerial(), Mqtt = new FakeMqtt(), Http = new FakeHttp(), Console = new FakeConsole()
        })) {
            fullHost.Config.EnsureExists();
            Assert.Equal("Element added.", fullHost.Execute("judo inset add agenda <lock>judo calendar next</lock>"));
            Assert.Equal("Element added.", fullHost.Execute("judo inset add data-agenda <lock>judo calendar json</lock>"));
            Assert.Contains("Calendar (Europe/Paris)", fullHost.Execute("agenda"));
            Assert.DoesNotContain("not found", fullHost.Execute("data-agenda"));
            Assert.False(exited);
        }
        await Assert.ThrowsAsync<ArgumentException>(() => service.Events(days: 0));
        await Assert.ThrowsAsync<ArgumentException>(() => service.Events(limit: 101));
        using var host = new TestHost(); Assert.Equal("Google Calendar: not connected", host.Run("judo calendar status"));
        Assert.Contains("judo calendar upcoming", host.Run("judo help calendar"));
        Assert.Contains("not connected", host.Run("judo calendar today"));
    }

    [Fact]
    public async Task WebEndpointsEnforceAuthenticationOriginMethodsCookieBindingAndSecretRedaction() {
        using var app = new TempApp(); var config = app.NewConfig(); var store = new SettingsStore(app.Paths, app.Log);
        int port = Ports.Free(); config.Update(new CommUpdate { Hostname = "localhost", HttpPort = port.ToString() });
        using var google = new Google(); google.Reply = _ => Google.Json($$"""{"access_token":"{{Access}}","refresh_token":"{{Refresh}}","expires_in":3600,"token_type":"Bearer"}""");
        using var http = new HttpClient(google); using var calendar = new GoogleCalendarService(store, http, () => Now);
        using var server = new WebServer(config, store, app.Paths, () => throw new InvalidOperationException("No instruction execution"), app.Log, calendar: calendar);
        server.Start(); using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }); string url = $"http://localhost:{port}/api/calendar/";
        using var saved = await client.PostAsJsonAsync(url + "settings", Setup(url + "callback")); Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        using var connect = await client.PostAsJsonAsync(url + "connect", new { });
        Assert.Equal(HttpStatusCode.OK, connect.StatusCode); Assert.True(connect.Headers.CacheControl!.NoStore);
        string cookie = connect.Headers.GetValues("Set-Cookie").Single(); Assert.Contains("httponly", cookie.ToLowerInvariant()); Assert.Contains("samesite=lax", cookie.ToLowerInvariant());
        var connection = JsonDocument.Parse(await connect.Content.ReadAsStringAsync()).RootElement.GetProperty("url").GetString()!;
        var state = QueryHelpers.ParseQuery(new Uri(connection).Query)["state"];
        using var callback = await client.GetAsync(url + "callback?state=" + state + "&code=google-code"); Assert.Equal(HttpStatusCode.SeeOther, callback.StatusCode);
        Assert.Equal("/www/?calendar=connected#page3", callback.Headers.Location!.ToString());
        using var view = await client.GetAsync(url + "settings"); string body = await view.Content.ReadAsStringAsync();
        Assert.DoesNotContain(Secret, body); Assert.DoesNotContain(Refresh, body); Assert.DoesNotContain(Access, body);
        using var attack = new HttpRequestMessage(HttpMethod.Post, url + "disconnect") { Content = JsonContent.Create(new { }) }; attack.Headers.Add("Origin", "https://evil.example.org");
        using var denied = await client.SendAsync(attack); Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var getDisconnect = await client.GetAsync(url + "disconnect"); Assert.Equal(HttpStatusCode.MethodNotAllowed, getDisconnect.StatusCode);
        using var wrongContent = await client.PostAsync(url + "settings", new StringContent("{}")); Assert.Equal(HttpStatusCode.UnsupportedMediaType, wrongContent.StatusCode);
        using var replay = await client.GetAsync(url + "callback?state=" + state + "&code=google-code"); Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
        Assert.DoesNotContain("google-code", await replay.Content.ReadAsStringAsync());
        store.Save(SettingsFiles.WebLogin, "John\nprivate"); config.Update(new CommUpdate { Authentication = "basic" }); server.Stop(); server.Start();
        using var anonymous = await client.GetAsync(url + "settings"); Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }
}
