/* (c) J@mBeL.net 2010-2026, John Ambeliotis.
 * This file is part of jaNET Framework.
 * jaNET Framework is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.
 * jaNET Framework is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty
 * of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU General Public License for more details.
 * You should have received a copy of the GNU General Public License along with jaNET Framework. If not, see <http://www.gnu.org/licenses/>. */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.WebUtilities;

namespace jaNET.Configuration;

internal sealed record WebSettingsView(Dictionary<string, object> Values, Dictionary<string, bool> Secrets);

// Only non-secret values leave the server. Blank password fields retain the encrypted credential on save.
internal static class WebSettings
{
    public static WebSettingsView Read(string area, AppConfigStore config, ISettingsStore store) {
        var values = new Dictionary<string, object>();
        var secrets = new Dictionary<string, bool>();
        void Set(string name, object value) => values[name] = value;
        void Secret(string name, string? value) { Set(name, ""); secrets[name] = !string.IsNullOrEmpty(value); }
        string[]? FileValues(string file) {
            var loaded = store.Load(file);
            if (loaded == null && store.Exists(file)) throw new InvalidOperationException("Saved settings could not be read.");
            return loaded?.ToArray();
        }
        var comm = config.Comm;
        switch (area) {
            case "mailheaderSettings":
                var mail = config.MailHeaders;
                Set("mailFrom", mail.From); Set("mailTo", mail.To); Set("mailSubject", mail.Subject); break;
            case "webserverSettings":
                Set("serverHost", comm.Hostname); Set("serverPort", comm.HttpPort); Set("serverAuth", comm.Authentication); break;
            case "socketSettings": Set("socketHost", comm.LocalHost); Set("socketPort", comm.LocalPort); break;
            case "trustedSettings": Set("trusted", comm.Trusted); break;
            case "serialSettings": Set("serialPort", comm.ComPort); Set("serialBaud", comm.BaudRate); break;
            case "webserverLogin":
                var login = FileValues(SettingsFiles.WebLogin);
                Set("serverUsername", login?[0] ?? ""); Secret("serverPassword", login?[1]); break;
            case "smsSettings":
            case "dyndnsSettings":
                bool sms = area == "smsSettings";
                var credentials = FileValues(sms ? SettingsFiles.Sms : SettingsFiles.DynDns);
                string prefix = sms ? "sms" : "dyndns";
                Set(sms ? "smsAPI" : "dyndnsHostname", credentials?[0] ?? "");
                Set(prefix + "Username", credentials?[1] ?? ""); Secret(prefix + "Password", credentials?[2]); break;
            case "smtpSettings":
            case "pop3Settings":
                bool smtp = area == "smtpSettings";
                var server = FileValues(smtp ? SettingsFiles.Smtp : SettingsFiles.Pop3);
                prefix = smtp ? "smtp" : "pop3";
                Set(prefix + "Host", server?[0] ?? ""); Set(prefix + "Username", server?[1] ?? "");
                Secret(prefix + "Password", server?[2]); Set(prefix + "Port", server?[3] ?? (smtp ? "587" : "110"));
                Set(smtp ? "chkSmtpSSL" : "chkPop3SSL", server != null && bool.Parse(server[4])); break;
            case "gmailSettings":
                FileValues(SettingsFiles.Gmail);
                var gmail = store.LoadGmail();
                Set("gmailUsername", gmail?.Username ?? ""); Secret("gmailPassword", gmail?.Password);
                Set("gmailFeedUrl", PublicUrl(gmail?.FeedUrl ?? GmailDefaults.FeedUrl));
                Set("gmailSmtpHost", gmail?.SmtpHost ?? GmailDefaults.SmtpHost); Set("gmailSmtpPort", gmail?.SmtpPort ?? GmailDefaults.SmtpPort);
                Set("chkGmailSmtpSSL", gmail?.SmtpSsl ?? GmailDefaults.SmtpSsl);
                Set("gmailPop3Host", gmail?.Pop3Host ?? GmailDefaults.Pop3Host); Set("gmailPop3Port", gmail?.Pop3Port ?? GmailDefaults.Pop3Port);
                Set("chkGmailPop3SSL", gmail?.Pop3Ssl ?? GmailDefaults.Pop3Ssl);
                Set("gmailImapHost", gmail?.ImapHost ?? "imap.gmail.com"); Set("gmailImapPort", gmail?.ImapPort ?? 993);
                Set("chkGmailImapSSL", gmail?.ImapSsl ?? true); break;
            case "httpsSettings":
                Set("httpsMode", comm.HttpsPort.Length == 0 ? "off" : comm.Certificate.Length > 0 ? "custom" : "on");
                Set("httpsPort", comm.HttpsPort.Length == 0 ? "8443" : comm.HttpsPort); Set("httpsCertFile", comm.Certificate);
                Secret("httpsCertPassword", FileValues(SettingsFiles.Tls)?.FirstOrDefault()); break;
            case "weatherSettings":
                string endpoint = config.WeatherUrl;
                bool meteo = Uri.TryCreate(endpoint, UriKind.Absolute, out var url) && url.Host == "api.open-meteo.com";
                var query = meteo ? QueryHelpers.ParseQuery(url!.Query) : null;
                Set("weatherProvider", meteo ? "openmeteo" : "legacy"); Set("weatherURI", PublicUrl(endpoint));
                Set("weatherLatitude", query != null && query.TryGetValue("latitude", out var latitude) ? latitude.ToString() : "49.6116");
                Set("weatherLongitude", query != null && query.TryGetValue("longitude", out var longitude) ? longitude.ToString() : "6.1319");
                Set("weatherLocation", config.WeatherLocation);
                Secret("weatherApiKey", FileValues(SettingsFiles.Weather)?.FirstOrDefault()); break;
            default: throw new ArgumentException("Unknown settings dialog.");
        }
        if (config.Problem != null) throw new InvalidOperationException("The configuration could not be read.");
        return new(values, secrets);
    }

    public static string Save(string area, Dictionary<string, string> values, AppConfigStore config, ISettingsStore store) {
        // Refuse to replace an unreadable configuration or encrypted settings file.
        Read(area, config, store);
        string Get(string key) {
            if (!values.TryGetValue(key, out string? value) || value.Contains('\r') || value.Contains('\n') || value.Contains('\0'))
                throw new ArgumentException("Enter a valid " + key + ".");
            return value;
        }
        string Required(string key) { string value = Get(key); return value.Trim().Length > 0 ? value : throw new ArgumentException("Enter " + key + "."); }
        string Number(string key, int max = 65535) {
            string value = Required(key);
            if (!int.TryParse(value, out int n) || n < 1 || n > max) throw new ArgumentException("Enter a valid " + key + ".");
            return n.ToString(CultureInfo.InvariantCulture);
        }
        string Flag(string key) => bool.TryParse(Get(key), out bool value) ? value.ToString() : throw new ArgumentException("Enter " + key + ".");
        string Password(string key, string file, int index) {
            string value = Get(key);
            return value.Length > 0 ? value : store.Load(file)?.ElementAtOrDefault(index) ?? "";
        }
        string Write(string file, params string[] fields) {
            string result = store.Save(file, string.Join("\n", fields));
            if (result != "Settings saved.") throw new InvalidOperationException("Settings could not be saved.");
            return result;
        }
        string Updated(string result) => result == "Element added." ? "Settings saved." : throw new InvalidOperationException("Settings could not be saved.");
        switch (area) {
            case "mailheaderSettings": return Updated(config.UpdateMailHeaders(Required("mailFrom"), Required("mailTo"), Required("mailSubject")));
            case "webserverSettings":
                string auth = Get("serverAuth");
                if (auth != "none" && auth != "basic") throw new ArgumentException("Choose an authentication mode.");
                return Updated(config.Update(new CommUpdate { Hostname = Required("serverHost"), HttpPort = Number("serverPort"), Authentication = auth }));
            case "socketSettings": return Updated(config.Update(new CommUpdate { LocalHost = Required("socketHost"), LocalPort = Number("socketPort") }));
            case "trustedSettings": return Updated(config.Update(new CommUpdate { Trusted = Get("trusted") }));
            case "serialSettings": return Updated(config.Update(new CommUpdate { ComPort = Required("serialPort"), BaudRate = Number("serialBaud", int.MaxValue) }));
            case "webserverLogin": return Write(SettingsFiles.WebLogin, Required("serverUsername"), Password("serverPassword", SettingsFiles.WebLogin, 1));
            case "smsSettings": return Write(SettingsFiles.Sms, Required("smsAPI"), Required("smsUsername"), Password("smsPassword", SettingsFiles.Sms, 2));
            case "dyndnsSettings": return Write(SettingsFiles.DynDns, Required("dyndnsHostname"), Required("dyndnsUsername"), Password("dyndnsPassword", SettingsFiles.DynDns, 2));
            case "smtpSettings":
            case "pop3Settings":
                bool smtp = area == "smtpSettings";
                string prefix = smtp ? "smtp" : "pop3", file = smtp ? SettingsFiles.Smtp : SettingsFiles.Pop3;
                return Write(file, Required(prefix + "Host"), Required(prefix + "Username"), Password(prefix + "Password", file, 2), Number(prefix + "Port"), Flag(smtp ? "chkSmtpSSL" : "chkPop3SSL"));
            case "gmailSettings":
                string user = Required("gmailUsername"), password = Password("gmailPassword", SettingsFiles.Gmail, 1);
                string feed = RetainUrl(Required("gmailFeedUrl"), store.LoadGmail()?.FeedUrl ?? GmailDefaults.FeedUrl);
                string sh = Required("gmailSmtpHost"), sp = Number("gmailSmtpPort"), ss = Flag("chkGmailSmtpSSL");
                string ph = Required("gmailPop3Host"), pp = Number("gmailPop3Port"), ps = Flag("chkGmailPop3SSL");
                string ih = Required("gmailImapHost"), ip = Number("gmailImapPort"), tls = Flag("chkGmailImapSSL");
                Write(SettingsFiles.Smtp, sh, user, password, sp, ss); Write(SettingsFiles.Pop3, ph, user, password, pp, ps);
                return Write(SettingsFiles.Gmail, user, password, feed, sh, sp, ss, ph, pp, ps, ih, ip, tls);
            case "httpsSettings":
                string mode = Get("httpsMode");
                if (mode == "off") return Updated(config.Update(new CommUpdate { HttpsPort = CommUpdate.Clear }));
                if (mode != "on" && mode != "default" && mode != "custom") throw new ArgumentException("Choose an HTTPS mode.");
                string port = Number("httpsPort");
                if (port == config.Comm.HttpPort) throw new ArgumentException("HTTPS needs a different port from HTTP.");
                string? certificate = null;
                if (mode == "custom") {
                    certificate = Required("httpsCertFile");
                    if (!File.Exists(certificate)) throw new ArgumentException("The certificate file was not found.");
                    string old = config.Comm.Certificate;
                    string pass = Get("httpsCertPassword");
                    Write(SettingsFiles.Tls, pass.Length == 0 && certificate == old ? Password("httpsCertPassword", SettingsFiles.Tls, 0) : pass);
                } else if (mode == "default") { certificate = CommUpdate.Clear; Write(SettingsFiles.Tls, ""); }
                return Updated(config.Update(new CommUpdate { HttpsPort = port, Certificate = certificate }));
            case "weatherSettings":
                string provider = Get("weatherProvider"), endpoint;
                if (provider == "openmeteo") {
                    string Coordinate(string key, double max) {
                        if (!double.TryParse(Required(key), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value) || Math.Abs(value) > max)
                            throw new ArgumentException("Enter a valid " + key + ".");
                        return value.ToString(CultureInfo.InvariantCulture);
                    }
                    string lat = Coordinate("weatherLatitude", 90), lon = Coordinate("weatherLongitude", 180), location = Required("weatherLocation");
                    endpoint = "https://api.open-meteo.com/v1/forecast?latitude=" + lat + "&longitude=" + lon + "&current=temperature_2m,relative_humidity_2m,pressure_msl,surface_pressure,weather_code,is_day&daily=temperature_2m_max,temperature_2m_min,weather_code&timezone=auto&forecast_days=2";
                    Updated(config.UpdateWeatherLocation(location));
                } else if (provider == "legacy") {
                    endpoint = RetainUrl(Required("weatherURI"), config.WeatherUrl);
                    string key = Get("weatherApiKey");
                    if (key.Length > 0) Write(SettingsFiles.Weather, key);
                } else throw new ArgumentException("Choose a weather provider.");
                return Updated(config.UpdateWeatherUrl(endpoint));
            default: throw new ArgumentException("Unknown settings dialog.");
        }
    }

    static bool PrivateParameter(string name) => new[] { "appid", "apikey", "api_key", "key", "token", "access_token", "password", "secret" }.Contains(name, StringComparer.OrdinalIgnoreCase);

    internal static string PublicUrl(string value) {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return "";
        var builder = new UriBuilder(uri) { UserName = "", Password = "", Fragment = "" };
        var query = QueryHelpers.ParseQuery(uri.Query);
        if (uri.UserInfo.Length == 0 && uri.Fragment.Length == 0 && !query.Keys.Any(PrivateParameter)) return value;
        builder.Query = string.Join("&", query.Where(pair => !PrivateParameter(pair.Key)).SelectMany(pair => pair.Value.Select(v => Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(v ?? ""))));
        return builder.Uri.AbsoluteUri;
    }

    static string RetainUrl(string value, string previous) {
        string result = value == PublicUrl(previous) ? previous : value;
        if (!Uri.TryCreate(result, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https")) throw new ArgumentException("Enter an HTTP or HTTPS endpoint.");
        return result;
    }
}
