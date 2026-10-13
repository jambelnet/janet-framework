/* (c) J@mBeL.net 2010-2026, John Ambeliotis.
 * This file is part of jaNET Framework.
 * jaNET Framework is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.
 * jaNET Framework is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty
 * of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU General Public License for more details.
 * You should have received a copy of the GNU General Public License along with jaNET Framework. If not, see <http://www.gnu.org/licenses/>. */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using jaNET.Configuration;
using jaNET.Servers;
using Xunit;

namespace jaNETFramework.Tests;

public class WebSettingsTests
{
    static readonly string[] Areas = { "mailheaderSettings", "webserverSettings", "webserverLogin", "httpsSettings", "socketSettings", "trustedSettings", "serialSettings", "weatherSettings", "dyndnsSettings", "smsSettings", "gmailSettings", "smtpSettings", "pop3Settings" };

    [Fact]
    public void ReadsEveryDialogWithoutChangingConfigurationOrReturningSecrets() {
        using var app = new TempApp();
        var config = app.NewConfig(); var store = new SettingsStore(app.Paths, app.Log);
        config.UpdateMailHeaders("John  Smith <john@example.org>", "home@example.org", "Home  alert `ready` & safe");
        config.Update(new CommUpdate { Certificate = "C:\\certificates\\home.pfx", HttpsPort = "9443", ComPort = "COM7", BaudRate = "115200", Trusted = "localhost  127.0.0.1" });
        const string secret = "private-value-NOT-FOR-BROWSER";
        store.Save(SettingsFiles.WebLogin, "John\n" + secret);
        store.Save(SettingsFiles.Smtp, "smtp.example.org\nJohn\n" + secret + "\n2525\nFalse");
        store.Save(SettingsFiles.Pop3, "pop.example.org\nJohn\n" + secret + "\n1110\nTrue");
        store.Save(SettingsFiles.Gmail, "john@example.org\n" + secret + "\nhttps://mail.example.org/feed?token=" + secret + "\nsmtp.example.org\n2525\nFalse\npop.example.org\n1110\nFalse\nimap.example.org\n1993\nFalse");
        store.Save(SettingsFiles.Sms, "123\nJohn\n" + secret);
        store.Save(SettingsFiles.DynDns, "home.example.org\nJohn\n" + secret);
        store.Save(SettingsFiles.Tls, secret); store.Save(SettingsFiles.Weather, secret);
        config.UpdateWeatherUrl("https://user:" + secret + "@weather.example.org/current?q=London&APPID=" + secret);
        string before = File.ReadAllText(app.Paths.ConfigFile);
        var filesBefore = SettingsFiles.All.Where(store.Exists).ToDictionary(file => file, file => File.ReadAllBytes(Path.Combine(app.Paths.Root, file)));
        foreach (string area in Areas) {
            var view = WebSettings.Read(area, config, store);
            Assert.NotEmpty(view.Values);
            Assert.DoesNotContain(secret, JsonSerializer.Serialize(view));
            foreach (string field in view.Secrets.Keys) { Assert.Equal("", view.Values[field]); Assert.True(view.Secrets[field]); }
        }
        Assert.Equal(before, File.ReadAllText(app.Paths.ConfigFile));
        foreach (var pair in filesBefore) Assert.Equal(pair.Value, File.ReadAllBytes(Path.Combine(app.Paths.Root, pair.Key)));
        var headers = WebSettings.Read("mailheaderSettings", config, store).Values;
        Assert.Equal("Home  alert `ready` & safe", headers["mailSubject"]);
        Assert.Equal("John  Smith <john@example.org>", headers["mailFrom"]);
        Assert.False((bool)WebSettings.Read("gmailSettings", config, store).Values["chkGmailImapSSL"]);
        Assert.Equal("1993", WebSettings.Read("gmailSettings", config, store).Values["gmailImapPort"].ToString());
        Assert.Equal("COM7", WebSettings.Read("serialSettings", config, store).Values["serialPort"]);
    }

    [Theory]
    [InlineData("smtpSettings", SettingsFiles.Smtp, "smtpPassword", "smtpHost", "smtp.example.org\nJohn\nretained\n2525\nFalse", 2)]
    [InlineData("pop3Settings", SettingsFiles.Pop3, "pop3Password", "pop3Host", "pop.example.org\nJohn\nretained\n1110\nTrue", 2)]
    [InlineData("webserverLogin", SettingsFiles.WebLogin, "serverPassword", "serverUsername", "John\nretained", 1)]
    [InlineData("smsSettings", SettingsFiles.Sms, "smsPassword", "smsAPI", "123\nJohn\nretained", 2)]
    [InlineData("dyndnsSettings", SettingsFiles.DynDns, "dyndnsPassword", "dyndnsHostname", "home.example.org\nJohn\nretained", 2)]
    [InlineData("gmailSettings", SettingsFiles.Gmail, "gmailPassword", "gmailUsername", "john@example.org\nretained\nhttps://mail.example.org/custom", 1)]
    public void SavingWithBlankPasswordRetainsItAndReplacementUpdatesIt(string area, string file, string field, string editable, string seed, int index) {
        using var app = new TempApp(); var config = app.NewConfig(); var store = new SettingsStore(app.Paths, app.Log);
        store.Save(file, seed);
        var view = WebSettings.Read(area, config, store);
        var values = view.Values.ToDictionary(pair => pair.Key, pair => pair.Value.ToString()!);
        values[editable] = editable.EndsWith("Username", StringComparison.Ordinal) ? "John2" : editable == "smsAPI" ? "124" : "other.example.org";
        Assert.Equal("Settings saved.", WebSettings.Save(area, values, config, store));
        Assert.Equal("retained", store.Load(file)![index]);
        if (area == "gmailSettings") Assert.Equal("https://mail.example.org/custom", store.LoadGmail()!.FeedUrl);
        values[field] = "new +%`& password";
        WebSettings.Save(area, values, config, store);
        Assert.Equal(values[field], store.Load(file)![index]);
    }

    [Fact]
    public void WeatherRedactionRoundTripRetainsEmbeddedKeyWithoutReturningIt() {
        using var app = new TempApp(); var config = app.NewConfig(); var store = new SettingsStore(app.Paths, app.Log);
        const string url = "https://weather.example.org/data?q=London&APPID=private-key";
        config.UpdateWeatherUrl(url);
        var values = WebSettings.Read("weatherSettings", config, store).Values.ToDictionary(pair => pair.Key, pair => pair.Value.ToString()!);
        Assert.DoesNotContain("private-key", values["weatherURI"]);
        Assert.Equal("Settings saved.", WebSettings.Save("weatherSettings", values, config, store));
        Assert.Equal(url, config.WeatherUrl);
        values["weatherURI"] = "https://new-weather.example.org/current";
        WebSettings.Save("weatherSettings", values, config, store);
        Assert.Equal(values["weatherURI"], config.WeatherUrl);
    }

    [Fact]
    public void FreshAndLegacyMailDefaultsLoadWithoutStaleValues() {
        using var app = new TempApp(); var config = app.NewConfig(); var store = new SettingsStore(app.Paths, app.Log);
        var fresh = WebSettings.Read("smtpSettings", config, store);
        Assert.Equal("", fresh.Values["smtpHost"]); Assert.False(fresh.Secrets["smtpPassword"]);
        store.Save(SettingsFiles.Gmail, "john@example.org\nretained");
        var legacy = WebSettings.Read("gmailSettings", config, store);
        Assert.Equal("imap.gmail.com", legacy.Values["gmailImapHost"]); Assert.True((bool)legacy.Values["chkGmailImapSSL"]);
    }

    [Fact]
    public void HttpsAndWeatherRetainTheirSavedSecretsAndLoadTheirActualOptions() {
        using var app = new TempApp(); var config = app.NewConfig(); var store = new SettingsStore(app.Paths, app.Log);
        string certificate = Path.Combine(app.Paths.Root, "home.pfx"); File.WriteAllText(certificate, "fixture");
        config.Update(new CommUpdate { Certificate = certificate, HttpsPort = "9443" }); store.Save(SettingsFiles.Tls, "kept-pfx-password");
        var tls = WebSettings.Read("httpsSettings", config, store).Values.ToDictionary(pair => pair.Key, pair => pair.Value.ToString()!);
        Assert.Equal("custom", tls["httpsMode"]); Assert.Equal("9443", tls["httpsPort"]); Assert.Equal(certificate, tls["httpsCertFile"]);
        WebSettings.Save("httpsSettings", tls, config, store); Assert.Equal("kept-pfx-password", store.Load(SettingsFiles.Tls)![0]);
        config.UpdateWeatherUrl("https://api.open-meteo.com/v1/forecast?latitude=51.5&longitude=-0.1"); config.UpdateWeatherLocation("London");
        store.Save(SettingsFiles.Weather, "kept-weather-key");
        var weather = WebSettings.Read("weatherSettings", config, store).Values.ToDictionary(pair => pair.Key, pair => pair.Value.ToString()!);
        Assert.Equal("51.5", weather["weatherLatitude"]); Assert.Equal("-0.1", weather["weatherLongitude"]); Assert.Equal("London", weather["weatherLocation"]);
        weather["weatherLatitude"] = "52.25"; weather["weatherLocation"] = "Other  location";
        WebSettings.Save("weatherSettings", weather, config, store);
        Assert.Contains("latitude=52.25", config.WeatherUrl); Assert.Equal("Other  location", config.WeatherLocation);
        Assert.Equal("kept-weather-key", store.Load(SettingsFiles.Weather)![0]);
    }

    [Fact]
    public void DamagedConfigurationAndCredentialFilesFailClosed() {
        using var app = new TempApp(); var config = app.NewConfig(); var store = new SettingsStore(app.Paths, app.Log);
        File.WriteAllText(app.Paths.ConfigFile, "<broken");
        Assert.Throws<InvalidOperationException>(() => WebSettings.Read("mailheaderSettings", config, store));
        File.Delete(app.Paths.ConfigFile); config.EnsureExists();
        File.WriteAllText(Path.Combine(app.Paths.Root, SettingsFiles.Smtp), "unreadable ciphertext");
        Assert.Throws<InvalidOperationException>(() => WebSettings.Read("smtpSettings", config, store));
    }

    [Fact]
    public void InvalidSaveDoesNotWrite() {
        using var app = new TempApp(); var config = app.NewConfig(); var store = new SettingsStore(app.Paths, app.Log);
        var values = WebSettings.Read("socketSettings", config, store).Values.ToDictionary(pair => pair.Key, pair => pair.Value.ToString()!);
        values["socketPort"] = "70000";
        string before = File.ReadAllText(app.Paths.ConfigFile);
        Assert.Throws<ArgumentException>(() => WebSettings.Save("socketSettings", values, config, store));
        Assert.Equal(before, File.ReadAllText(app.Paths.ConfigFile));
    }

    [Fact]
    public async Task EndpointLoadsExactValuesWithoutExecutingCommandsAndRejectsCrossOriginRequests() {
        using var app = new TempApp(); var config = app.NewConfig(); var store = new SettingsStore(app.Paths, app.Log);
        int port = Ports.Free(); config.Update(new CommUpdate { Hostname = "localhost", HttpPort = port.ToString() });
        config.UpdateMailHeaders("John  Smith", "home@example.org", "Home  alert");
        using var server = new WebServer(config, store, app.Paths, () => throw new InvalidOperationException("Do not execute commands"), app.Log);
        server.Start(); using var client = new HttpClient(); string url = $"http://localhost:{port}/api/settings/mailheaderSettings";
        using var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.True(response.Headers.CacheControl!.NoStore);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Home  alert", json.RootElement.GetProperty("values").GetProperty("mailSubject").GetString());
        using var request = new HttpRequestMessage(HttpMethod.Get, url); request.Headers.Add("Origin", "https://other.example.org");
        using var denied = await client.SendAsync(request); Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var saved = await client.PostAsJsonAsync(url, new Dictionary<string, string> { ["mailFrom"] = "John", ["mailTo"] = "home@example.org", ["mailSubject"] = "Saved  text `with` punctuation" });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode); Assert.Equal("Saved  text `with` punctuation", config.MailHeaders.Subject);
        store.Save(SettingsFiles.WebLogin, "John\nprivate"); config.Update(new CommUpdate { Authentication = "basic" });
        server.Stop(); server.Start();
        using var anonymous = await client.GetAsync(url); Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }
}
