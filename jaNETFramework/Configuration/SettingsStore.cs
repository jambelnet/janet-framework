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

using jaNET.Infrastructure;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace jaNET.Configuration;

/// <summary>The small encrypted settings files (.htaccess, .smtpsettings, .scheduler, ...): one encrypted value per line.</summary>
internal interface ISettingsStore
{
    /// <summary>The decrypted lines, or null if the file does not exist or cannot be read (damaged, or written with another key).</summary>
    IReadOnlyList<string>? Load(string fileName);

    bool Exists(string fileName);

    /// <summary>Replaces the file with the given text, one value per line. Returns the message shown to the user.</summary>
    string Save(string fileName, string text);

    /// <summary>Rewrites files that still use the encryption of older versions. Returns how many files were converted.</summary>
    int MigrateLegacyFiles();

    /// <summary>Settings files that cannot be read or written right now, with the reason; empty when all is well.</summary>
    IReadOnlyList<string> Problems => Array.Empty<string>();
}

/// <summary>
/// The settings files on disk. Values are protected by <see cref="SettingsCipher"/> with the key of this installation;
/// files written by older versions are read as before and rewritten in the new format the first time they are used.
/// </summary>
internal sealed class SettingsStore : ISettingsStore
{
    readonly AppPaths _paths;
    readonly ILog _log;
    readonly Lazy<SettingsCipher> _cipher;
    readonly Dictionary<string, string> _problems = new();

    public SettingsStore(AppPaths paths, ILog log) {
        _paths = paths;
        _log = log;
        _cipher = new Lazy<SettingsCipher>(() => new SettingsCipher(SettingsKey.LoadOrCreate(paths)));
    }

    public bool Exists(string fileName) => File.Exists(_paths.File(fileName));

    public IReadOnlyList<string> Problems {
        get {
            lock (_problems) return _problems.Values.ToList();
        }
    }

    void SetProblem(string fileName, string? problem) {
        lock (_problems) {
            if (problem == null) _problems.Remove(fileName);
            else _problems[fileName] = problem;
        }
    }

    public IReadOnlyList<string>? Load(string fileName) {
        string path = _paths.File(fileName);
        if (!File.Exists(path)) return null;

        try {
            string[] stored = File.ReadAllLines(path);
            List<string> values = stored.Select(line => _cipher.Value.Decrypt(line, fileName)).ToList();

            if (stored.Any(line => !SettingsCipher.IsCurrent(line))) {
                Write(path, fileName, values);
                _log.Write($"obj [ SettingsStore ] {fileName} was converted to the encryption with the key of this installation.");
            }
            SetProblem(fileName, null);
            return values;
        }
        catch (Exception e) when (e is System.Security.Cryptography.CryptographicException || e is FormatException) {
            _log.Write($"obj [ SettingsStore ] {fileName} cannot be read ({e.Message}). Was it written with another key? Save the settings again.");
            SetProblem(fileName, $"{fileName} cannot be decrypted, so it is ignored. It was probably written with another key: restore the {SettingsKey.FileName} (or {SettingsKey.EnvironmentVariable}) of the installation that wrote it, or enter the settings again.");
            return null;
        }
    }

    public string Save(string fileName, string text) {
        try {
            Write(_paths.File(fileName), fileName, text.Split('\n').Select(line => line.Trim()).ToList());
            SetProblem(fileName, null);
            return "Settings saved.";
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) {
            SetProblem(fileName, $"{fileName} cannot be saved: {e.Message} Check that jaNET may write to {_paths.Root}");
            return "Unable to save settings.";
        }
    }

    public int MigrateLegacyFiles() {
        int converted = 0;

        foreach (string fileName in SettingsFiles.All) {
            string path = _paths.File(fileName);
            if (!File.Exists(path)) continue;

            if (File.ReadLines(path).Any(line => !SettingsCipher.IsCurrent(line))) {
                if (Load(fileName) != null) converted++;
            }
        }
        return converted;
    }

    // through a temporary file, so that a crash never leaves a half written settings file; readable by the owner only on Linux and macOS
    void Write(string path, string fileName, List<string> values) {
        string temp = path + ".tmp";
        var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

        using (var stream = new FileStream(temp, options))
        using (var writer = new StreamWriter(stream))
            foreach (string value in values)
                writer.WriteLine(_cipher.Value.Encrypt(value, fileName));

        File.Move(temp, path, true);
    }
}

internal sealed record MailServerSettings(string Host, string Username, string Password, int Port, bool Ssl);

internal sealed record GmailSettings(string Username, string Password);

internal sealed record SmsSettings(string Api, string Username, string Password);

internal sealed record DynDnsSettings(string Hostname, string Username, string Password);

internal sealed record WebLogin(string Username, string Password)
{
    // compared in constant time: the time it takes must not tell how much of a guess was right
    public bool Matches(string username, string password) =>
        CryptographicOperations.FixedTimeEquals(Hash(Username), Hash(username)) & CryptographicOperations.FixedTimeEquals(Hash(Password), Hash(password));

    static byte[] Hash(string text) => SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text));
}

/// <summary>Typed access to the individual settings files. Null means the file has not been created yet.</summary>
internal static class SettingsFiles
{
    public const string Smtp = ".smtpsettings";
    public const string Pop3 = ".pop3settings";
    public const string Gmail = ".gmailsettings";
    public const string Sms = ".smssettings";
    public const string DynDns = ".dyndnssettings";
    public const string WebLogin = ".htaccess";
    public const string Scheduler = ".scheduler";
    public const string Weather = ".weathersettings";
    public const string Tls = ".tlssettings";
    public const string Mqtt = ".mqttsettings";

    public static readonly string[] All = { Smtp, Pop3, Gmail, Sms, DynDns, WebLogin, Scheduler, Weather, Tls, Mqtt };

    /// <summary>User name and password for the MQTT broker, or null when none are set.</summary>
    public static (string User, string Password)? LoadMqttLogin(this ISettingsStore store) {
        IReadOnlyList<string>? v = store.Load(Mqtt);
        return v != null && v.Count >= 2 && v[0].Length > 0 ? (v[0], v[1]) : null;
    }

    public static MailServerSettings? LoadSmtp(this ISettingsStore store) => LoadMailServer(store, Smtp);

    public static MailServerSettings? LoadPop3(this ISettingsStore store) => LoadMailServer(store, Pop3);

    static MailServerSettings? LoadMailServer(ISettingsStore store, string file) {
        IReadOnlyList<string>? v = store.Load(file);
        return v == null ? null : new MailServerSettings(v[0], v[1], v[2], Convert.ToInt32(v[3]), Convert.ToBoolean(v[4]));
    }

    public static GmailSettings? LoadGmail(this ISettingsStore store) {
        IReadOnlyList<string>? v = store.Load(Gmail);
        return v == null ? null : new GmailSettings(v[0], v[1]);
    }

    public static SmsSettings? LoadSms(this ISettingsStore store) {
        IReadOnlyList<string>? v = store.Load(Sms);
        return v == null ? null : new SmsSettings(v[0], v[1], v[2]);
    }

    public static DynDnsSettings? LoadDynDns(this ISettingsStore store) {
        IReadOnlyList<string>? v = store.Load(DynDns);
        return v == null ? null : new DynDnsSettings(v[0], v[1], v[2]);
    }

    public static WebLogin? LoadWebLogin(this ISettingsStore store) {
        IReadOnlyList<string>? v = store.Load(WebLogin);
        return v == null ? null : new WebLogin(v[0], v[1]);
    }
}
