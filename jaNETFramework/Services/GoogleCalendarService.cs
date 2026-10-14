/* (c) J@mBeL.net 2010-2026, John Ambeliotis.
 * This file is part of jaNET Framework.
 * jaNET Framework is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.
 * jaNET Framework is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty
 * of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU General Public License for more details.
 * You should have received a copy of the GNU General Public License along with jaNET Framework. If not, see <http://www.gnu.org/licenses/>. */
using jaNET.Configuration;
using Microsoft.AspNetCore.WebUtilities;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace jaNET.Services;

internal sealed record CalendarOptions(string ClientId = "", string ClientSecret = "", string RedirectUri = "", string CalendarId = "primary", string TimeZone = "UTC",
    string RefreshToken = "", string AccessToken = "", DateTimeOffset Expires = default);
internal sealed record CalendarSetup(string ClientId, string ClientSecret, string RedirectUri, string CalendarId, string TimeZone);
internal sealed record CalendarConnection(string Url, string State, string Browser);
internal sealed record CalendarEvent(string Id, string Summary, string Start, string End, bool AllDay, string Location);
internal sealed record CalendarAgenda(string TimeZone, IReadOnlyList<CalendarEvent> Events);

// Credentials and tokens stay together in one encrypted record. The gate also prevents a late refresh from undoing Disconnect.
internal sealed class GoogleCalendarService : IDisposable
{
    internal const string Scope = "https://www.googleapis.com/auth/calendar.events.readonly";
    readonly ISettingsStore _settings;
    readonly HttpClient _http;
    readonly bool _ownsHttp;
    readonly Func<DateTimeOffset> _now;
    readonly SemaphoreSlim _gate = new(1, 1);
    Pending? _pending;
    sealed record Pending(string State, string Browser, string Verifier, DateTimeOffset Expires);

    public GoogleCalendarService(ISettingsStore settings, HttpClient? http = null, Func<DateTimeOffset>? now = null) {
        _settings = settings;
        _ownsHttp = http == null;
        _http = http ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    CalendarOptions Load() {
        var lines = _settings.Load(SettingsFiles.GoogleCalendar);
        if (lines == null) {
            if (_settings.Exists(SettingsFiles.GoogleCalendar)) throw new InvalidOperationException("Google Calendar settings could not be decrypted. Restore the installation key before saving again.");
            return new(TimeZone: ZoneId(TimeZoneInfo.Local));
        }
        try {
            var options = JsonSerializer.Deserialize<CalendarOptions>(lines.Single()) ?? throw new JsonException();
            if (new[] { options.ClientId, options.ClientSecret, options.RedirectUri, options.CalendarId, options.TimeZone, options.RefreshToken, options.AccessToken }.Any(value => value == null)) throw new JsonException();
            return options;
        }
        catch (Exception e) when (e is JsonException || e is InvalidOperationException) {
            throw new InvalidOperationException("Google Calendar settings could not be read. Restore the saved settings before continuing.");
        }
    }

    void Store(CalendarOptions options) {
        if (_settings.Save(SettingsFiles.GoogleCalendar, JsonSerializer.Serialize(options)) != "Settings saved.")
            throw new InvalidOperationException("Google Calendar settings could not be saved.");
    }

    public async Task<object> View(CancellationToken token = default) {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try {
            var o = Load();
            return new { clientId = o.ClientId, clientSecret = "", redirectUri = o.RedirectUri, calendarId = o.CalendarId, timeZone = o.TimeZone,
                secretConfigured = o.ClientSecret.Length > 0, connected = o.RefreshToken.Length > 0 };
        } finally { _gate.Release(); }
    }

    public async Task Save(CalendarSetup setup, CancellationToken token = default) {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try {
            var old = Load();
            string Clean(string? value, string field) {
                if (value == null || value.Length > 2048 || value.Any(char.IsControl)) throw new ArgumentException("Enter a valid " + field + ".");
                return value.Trim();
            }
            string client = Clean(setup.ClientId, "client ID"), secret = Clean(setup.ClientSecret, "client secret");
            string redirect = Clean(setup.RedirectUri, "redirect URI"), calendar = Clean(setup.CalendarId, "calendar ID");
            if (client.Length == 0 || !client.EndsWith(".apps.googleusercontent.com", StringComparison.Ordinal)) throw new ArgumentException("Enter the Google OAuth web client ID.");
            if (secret.Length == 0) secret = old.ClientSecret;
            if (secret.Length == 0) throw new ArgumentException("Enter the Google OAuth client secret.");
            Redirect(redirect);
            if (calendar.Length == 0) throw new ArgumentException("Enter a calendar ID, or primary.");
            string zone;
            try { zone = ZoneId(TimeZoneInfo.FindSystemTimeZoneById(Clean(setup.TimeZone, "time zone"))); }
            catch (Exception e) when (e is TimeZoneNotFoundException || e is InvalidTimeZoneException) { throw new ArgumentException("Enter a time zone such as Europe/Paris or UTC."); }
            bool changed = client != old.ClientId || secret != old.ClientSecret || redirect != old.RedirectUri;
            Store(new(client, secret, redirect, calendar, zone, changed ? "" : old.RefreshToken, changed ? "" : old.AccessToken, changed ? default : old.Expires));
            if (changed) _pending = null;
        } finally { _gate.Release(); }
    }

    public async Task<CalendarConnection> Connect(string origin, CancellationToken token = default) {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try {
            var o = Load();
            if (o.ClientId.Length == 0 || o.ClientSecret.Length == 0) throw new ArgumentException("Save the Google Calendar OAuth settings first.");
            var redirect = Redirect(o.RedirectUri);
            if (redirect.GetLeftPart(UriPartial.Authority) != origin)
                throw new ArgumentException("Open jaNET at the same origin as the saved redirect URI, then connect again.");
            string Random() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
            _pending = new(Random(), Random(), Random(), _now().AddMinutes(10));
            string url = QueryHelpers.AddQueryString("https://accounts.google.com/o/oauth2/v2/auth", new Dictionary<string, string?> {
                ["client_id"] = o.ClientId, ["redirect_uri"] = o.RedirectUri, ["response_type"] = "code", ["scope"] = Scope,
                ["access_type"] = "offline", ["prompt"] = "consent", ["state"] = _pending.State,
                ["code_challenge"] = WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(_pending.Verifier))), ["code_challenge_method"] = "S256"
            });
            return new(url, _pending.State, _pending.Browser);
        } finally { _gate.Release(); }
    }

    static Uri Redirect(string value) {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.AbsolutePath != "/api/calendar/callback" || uri.Query.Length > 0 || uri.Fragment.Length > 0 || uri.UserInfo.Length > 0)
            throw new ArgumentException("Enter a redirect URI ending in /api/calendar/callback, with no query, fragment or credentials.");
        if (uri.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6 && !uri.IsLoopback)
            throw new ArgumentException("Google does not allow IP-address redirects except loopback. Use localhost on the jaNET computer, or an HTTPS domain name, and register the same URI in Google Cloud.");
        if (!(uri.Scheme == "https" || uri.Scheme == "http" && uri.IsLoopback))
            throw new ArgumentException("Use an HTTPS domain redirect, or HTTP on localhost for local setup.");
        return uri;
    }

    public async Task Complete(string state, string browser, string code, string error, CancellationToken token = default) {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try {
            var pending = _pending;
            bool Same(string a, string b) => CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(a)), SHA256.HashData(Encoding.UTF8.GetBytes(b)));
            if (pending == null || _now() >= pending.Expires || !Same(pending.State, state) || !Same(pending.Browser, browser))
                throw new ArgumentException("Google Calendar sign-in expired or did not start in this browser. Connect again.");
            _pending = null;
            if (error.Length > 0 || string.IsNullOrEmpty(code)) throw new ArgumentException("Google Calendar permission was not granted. Connect again to approve read-only access.");
            var o = Load();
            var response = await Token(new() { ["client_id"] = o.ClientId, ["client_secret"] = o.ClientSecret, ["redirect_uri"] = o.RedirectUri,
                ["code"] = code, ["code_verifier"] = pending.Verifier, ["grant_type"] = "authorization_code" }, token).ConfigureAwait(false);
            string refresh = Text(response, "refresh_token");
            if (refresh.Length == 0) throw new InvalidOperationException("Google did not issue offline access. Reconnect and approve access again.");
            Store(WithToken(o, response) with { RefreshToken = refresh });
        } finally { _gate.Release(); }
    }

    public async Task<string> Disconnect(CancellationToken token = default) {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try {
            var o = Load();
            Store(o with { RefreshToken = "", AccessToken = "", Expires = default });
            _pending = null;
            if (o.RefreshToken.Length == 0) return "Google Calendar disconnected.";
            try {
                using var response = await _http.PostAsync("https://oauth2.googleapis.com/revoke", new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = o.RefreshToken }), token).ConfigureAwait(false);
                if (response.IsSuccessStatusCode) return "Google Calendar disconnected and Google access revoked.";
            } catch (Exception e) when (e is HttpRequestException || e is OperationCanceledException) { }
            return "Google Calendar disconnected locally. Remove the application's access in your Google Account to revoke it at Google.";
        } finally { _gate.Release(); }
    }

    async Task<JsonElement> Token(Dictionary<string, string> fields, CancellationToken token) {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://oauth2.googleapis.com/token") { Content = new FormUrlEncodedContent(fields) };
        return await Send(request, token, oauth: true).ConfigureAwait(false);
    }

    CalendarOptions WithToken(CalendarOptions o, JsonElement response) {
        string access = Text(response, "access_token");
        if (access.Length == 0 || !response.TryGetProperty("expires_in", out var expiry) || !expiry.TryGetInt32(out int seconds) || seconds <= 0)
            throw new InvalidOperationException("Google returned an incomplete authorization response. Connect again.");
        if (response.TryGetProperty("scope", out var scopes) && !scopes.GetString()!.Split(' ').Contains(Scope))
            throw new InvalidOperationException("Read-only Google Calendar access was not granted. Connect again.");
        if (!Text(response, "token_type").Equals("Bearer", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Google returned an unsupported token type.");
        return o with { AccessToken = access, Expires = _now().AddSeconds(seconds) };
    }

    public async Task<CalendarAgenda> Events(string range = "upcoming", int days = 7, int limit = 10, CancellationToken token = default) {
        if (range is not ("upcoming" or "today" or "next") || days is < 1 or > 365 || limit is < 1 or > 100) throw new ArgumentException("Use upcoming, today or next; days 1-365 and limit 1-100.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(45));
        try { return await ReadEvents(range, days, limit, deadline.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new InvalidOperationException("Google Calendar timed out. Try again later."); }
    }

    async Task<CalendarAgenda> ReadEvents(string range, int days, int limit, CancellationToken token) {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try {
            var o = Load();
            if (o.RefreshToken.Length == 0) throw new InvalidOperationException("Google Calendar is not connected. Open Settings > Google Calendar.");
            if (o.AccessToken.Length == 0 || _now().AddSeconds(60) >= o.Expires) {
                var response = await Token(new() { ["client_id"] = o.ClientId, ["client_secret"] = o.ClientSecret, ["refresh_token"] = o.RefreshToken, ["grant_type"] = "refresh_token" }, token).ConfigureAwait(false);
                o = WithToken(o, response);
                if (Text(response, "refresh_token") is { Length: > 0 } rotated) o = o with { RefreshToken = rotated };
                Store(o);
            }
            var zone = TimeZoneInfo.FindSystemTimeZoneById(o.TimeZone);
            DateTimeOffset start = _now(), end = start.AddDays(range == "next" ? 365 : days);
            if (range == "today") {
                DateTime date = TimeZoneInfo.ConvertTime(start, zone).Date;
                start = new(date, zone.GetUtcOffset(date)); end = new(date.AddDays(1), zone.GetUtcOffset(date.AddDays(1)));
            }
            if (range == "next") limit = 1;
            var items = new List<CalendarEvent>();
            string page = "";
            for (int count = 0; count < 20; count++) {
                string url = QueryHelpers.AddQueryString("https://www.googleapis.com/calendar/v3/calendars/" + Uri.EscapeDataString(o.CalendarId) + "/events", new Dictionary<string, string?> {
                    ["timeMin"] = start.ToString("o"), ["timeMax"] = end.ToString("o"), ["singleEvents"] = "true", ["orderBy"] = "startTime", ["showDeleted"] = "false",
                    ["maxResults"] = limit.ToString(CultureInfo.InvariantCulture), ["timeZone"] = o.TimeZone, ["pageToken"] = page.Length > 0 ? page : null
                });
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", o.AccessToken);
                var response = await Send(request, token).ConfigureAwait(false);
                if (response.ValueKind != JsonValueKind.Object || (!response.TryGetProperty("items", out var events) && Text(response, "kind") != "calendar#events") ||
                    events.ValueKind is not (JsonValueKind.Array or JsonValueKind.Undefined))
                    throw new InvalidOperationException("Google returned an incomplete Calendar response. Try again later.");
                if (events.ValueKind == JsonValueKind.Array) foreach (var entry in events.EnumerateArray()) {
                    if (Text(entry, "status") == "cancelled") continue;
                    items.Add(ReadEvent(entry));
                    if (items.Count == limit) return new(o.TimeZone, items);
                }
                page = Text(response, "nextPageToken");
                if (page.Length == 0) return new(o.TimeZone, items);
            }
            throw new InvalidOperationException("Google Calendar returned too many incomplete pages. Try a shorter date range.");
        } finally { _gate.Release(); }
    }

    static CalendarEvent ReadEvent(JsonElement entry) {
        try {
            var begin = entry.GetProperty("start"); var finish = entry.GetProperty("end");
            bool allDay = begin.TryGetProperty("date", out _);
            string start = Text(begin, allDay ? "date" : "dateTime"), end = Text(finish, allDay ? "date" : "dateTime");
            bool valid = allDay
                ? DateOnly.TryParseExact(start, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var first) && DateOnly.TryParseExact(end, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var last) && last > first
                : DateTimeOffset.TryParse(start, CultureInfo.InvariantCulture, DateTimeStyles.None, out var firstTime) && DateTimeOffset.TryParse(end, CultureInfo.InvariantCulture, DateTimeStyles.None, out var lastTime) && lastTime > firstTime;
            if (!valid || Text(entry, "id").Length == 0) throw new InvalidOperationException();
            return new(Text(entry, "id"), Text(entry, "summary") is { Length: > 0 } title ? title : "Busy", start, end, allDay, Text(entry, "location"));
        } catch (Exception e) when (e is InvalidOperationException || e is KeyNotFoundException) {
            throw new InvalidOperationException("Google returned an incomplete Calendar event. Try again later.");
        }
    }

    async Task<JsonElement> Send(HttpRequestMessage request, CancellationToken token, bool oauth = false) {
        try {
            using var response = await _http.SendAsync(request, token).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) {
                if (oauth) {
                    try {
                        using var details = JsonDocument.Parse(body);
                        if (Text(details.RootElement, "error") == "invalid_grant") throw new InvalidOperationException("Google Calendar authorization expired or was revoked. Connect again in Settings.");
                    } catch (JsonException) { /* A proxy may return a non-JSON failure; use the safe status message below. */ }
                }
                throw new InvalidOperationException(response.StatusCode switch {
                    HttpStatusCode.Unauthorized => "Google Calendar authorization is no longer valid. Connect again in Settings.",
                    HttpStatusCode.Forbidden => "Google Calendar access was denied. Enable the Calendar API and check read-only permission and calendar sharing.",
                    HttpStatusCode.NotFound => "Google Calendar was not found. Check its ID and sharing permissions.",
                    HttpStatusCode.TooManyRequests => "Google Calendar rate limit reached. Try again later.",
                    _ => oauth ? "Google sign-in failed. Check the OAuth client settings and redirect URI, then reconnect." : "Google Calendar is unavailable. Try again later."
                });
            }
            using var json = JsonDocument.Parse(body); return json.RootElement.Clone();
        } catch (HttpRequestException) { throw new InvalidOperationException("Google Calendar could not be reached. Check the network and try again."); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new InvalidOperationException("Google Calendar timed out. Try again later."); }
        catch (JsonException) { throw new InvalidOperationException("Google returned an invalid Calendar response. Try again later."); }
    }

    static string Text(JsonElement value, string key) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString()! : "";
    static string ZoneId(TimeZoneInfo zone) => TimeZoneInfo.TryConvertWindowsIdToIanaId(zone.Id, out string? id) ? id : zone.Id;

    public static string Format(CalendarAgenda agenda) {
        if (agenda.Events.Count == 0) return "No events in this period.";
        var zone = TimeZoneInfo.FindSystemTimeZoneById(agenda.TimeZone);
        // A launcher may interpolate this answer via *agenda before jaNET checks pointers and conditions again.
        string Line(string text) => string.Concat(text.Select(c => char.IsControl(c) ? ' ' : c)).Replace("*", "\u2217", StringComparison.Ordinal).Replace("%", "\uff05", StringComparison.Ordinal).Replace("evalBool", "eval Bool", StringComparison.Ordinal);
        return "Calendar (" + agenda.TimeZone + "):\n" + string.Join("\n", agenda.Events.Select(e => {
            string when = e.AllDay ? e.Start + " (all day)" : TimeZoneInfo.ConvertTime(DateTimeOffset.Parse(e.Start, CultureInfo.InvariantCulture), zone).ToString("yyyy-MM-dd HH:mm zzz", CultureInfo.InvariantCulture);
            return when + " - " + Line(e.Summary) + (e.Location.Length > 0 ? " - " + Line(e.Location) : "");
        }));
    }

    public void Dispose() { if (_ownsHttp) _http.Dispose(); }
}
