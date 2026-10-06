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
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Xunit;

namespace jaNETFramework.Tests;

public class SettingsCipherTests
{
    static readonly byte[] Key = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();

    [Fact]
    public void RoundTripsText() {
        var cipher = new SettingsCipher(Key);

        string stored = cipher.Encrypt("pässwörd €", ".gmailsettings");

        Assert.StartsWith("v2:", stored);
        Assert.DoesNotContain("pässwörd", stored);
        Assert.Equal("pässwörd €", cipher.Decrypt(stored, ".gmailsettings"));
        Assert.True(SettingsCipher.IsCurrent(stored));
    }

    [Fact]
    public void TheSameValueLooksDifferentEveryTime() {
        var cipher = new SettingsCipher(Key);

        Assert.NotEqual(cipher.Encrypt("same", ".x"), cipher.Encrypt("same", ".x"));
    }

    [Fact]
    public void EmptyValuesWork() {
        var cipher = new SettingsCipher(Key);

        Assert.Equal(string.Empty, cipher.Decrypt(cipher.Encrypt(string.Empty, ".x"), ".x"));
    }

    [Fact]
    public void AChangedValueIsRejected() {
        var cipher = new SettingsCipher(Key);
        string stored = cipher.Encrypt("secret", ".x");
        byte[] bytes = Convert.FromBase64String(stored.Substring(3));
        bytes[bytes.Length - 1] ^= 1;

        Assert.ThrowsAny<CryptographicException>(() => cipher.Decrypt("v2:" + Convert.ToBase64String(bytes), ".x"));
    }

    [Fact]
    public void AValueCannotBeMovedToAnotherFile() {
        var cipher = new SettingsCipher(Key);
        string stored = cipher.Encrypt("secret", ".smtpsettings");

        Assert.ThrowsAny<CryptographicException>(() => cipher.Decrypt(stored, ".htaccess"));
    }

    [Fact]
    public void AnotherKeyCannotRead() {
        string stored = new SettingsCipher(Key).Encrypt("secret", ".x");

        Assert.ThrowsAny<CryptographicException>(() => new SettingsCipher(Key.Reverse().ToArray()).Decrypt(stored, ".x"));
    }

    [Theory]
    [InlineData("v2:")]
    [InlineData("v2:AAAA")]
    [InlineData("v2:!!not base64!!")]
    public void DamagedValuesAreRejected(string stored) {
        Assert.ThrowsAny<CryptographicException>(() => new SettingsCipher(Key).Decrypt(stored, ".x"));
    }

    [Fact]
    public void ValuesOfOlderVersionsAreStillReadable() {
        var cipher = new SettingsCipher(Key);

        Assert.False(SettingsCipher.IsCurrent("u0VZL6/Tv0Yfoo3vGePFaQ=="));
        Assert.Equal("admin", cipher.Decrypt("u0VZL6/Tv0Yfoo3vGePFaQ==", ".htaccess"));
    }

    [Fact]
    public void TheKeyMustBe32Bytes() {
        Assert.Throws<ArgumentException>(() => new SettingsCipher(new byte[16]));
    }
}

public class SettingsKeyTests
{
    [Fact]
    public void IsMadeOnTheFirstRunAndReusedAfterwards() {
        using var app = new TempApp();

        byte[] first = SettingsKey.LoadOrCreate(app.Paths, _ => null);
        byte[] second = SettingsKey.LoadOrCreate(app.Paths, _ => null);

        Assert.Equal(32, first.Length);
        Assert.Equal(first, second);
        Assert.True(File.Exists(app.Paths.File(".janet.key")));
    }

    [Fact]
    public void DiffersFromInstallationToInstallation() {
        using var one = new TempApp();
        using var other = new TempApp();

        Assert.NotEqual(SettingsKey.LoadOrCreate(one.Paths, _ => null), SettingsKey.LoadOrCreate(other.Paths, _ => null));
    }

    [Fact]
    public void CanComeFromTheEnvironmentWithoutTouchingTheDisk() {
        using var app = new TempApp();
        byte[] key = Enumerable.Range(10, 32).Select(i => (byte)i).ToArray();

        byte[] loaded = SettingsKey.LoadOrCreate(app.Paths, name => name == "JANET_KEY" ? Convert.ToBase64String(key) : null);

        Assert.Equal(key, loaded);
        Assert.False(File.Exists(app.Paths.File(".janet.key")));
    }

    [Theory]
    [InlineData("not base64 at all")]
    [InlineData("AAAA")]                                // valid base64 but not 32 bytes
    public void ABadKeyIsAnErrorNotASilentNewKey(string text) {
        using var app = new TempApp();

        Assert.Throws<InvalidOperationException>(() => SettingsKey.LoadOrCreate(app.Paths, _ => text));
    }

    [Fact]
    public void ABadKeyFileIsAnError() {
        using var app = new TempApp();
        File.WriteAllText(app.Paths.File(".janet.key"), "garbage");

        Assert.Throws<InvalidOperationException>(() => SettingsKey.LoadOrCreate(app.Paths, _ => null));
    }

    [Fact]
    public void OnlyTheOwnerCanReadTheKeyFileOnUnix() {
        if (OperatingSystem.IsWindows()) return;          // Windows uses the permissions of the directory
        using var app = new TempApp();

        SettingsKey.LoadOrCreate(app.Paths, _ => null);

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(app.Paths.File(".janet.key")));
    }
}

public class SettingsMigrationTests
{
    // a file as the previous versions wrote it
    static void WriteLegacy(TempApp app, string fileName, params string[] values) =>
        File.WriteAllLines(app.Paths.File(fileName), values.Select(LegacySettingsCipher.Encrypt));

    [Fact]
    public void LegacyFilesAreReadAndRewrittenInTheCurrentFormat() {
        using var app = new TempApp();
        WriteLegacy(app, ".gmailsettings", "me@example.org", "secret3");
        var store = new SettingsStore(app.Paths, app.Log);

        Assert.Equal(new[] { "me@example.org", "secret3" }, store.Load(".gmailsettings"));

        string[] lines = File.ReadAllLines(app.Paths.File(".gmailsettings"));
        Assert.All(lines, line => Assert.StartsWith("v2:", line));
        Assert.DoesNotContain(app.Log.Entries, e => e.Contains("cannot be read"));
        Assert.Contains(app.Log.Entries, e => e.Contains(".gmailsettings was converted"));
        Assert.Equal(new[] { "me@example.org", "secret3" }, new SettingsStore(app.Paths, app.Log).Load(".gmailsettings"));   // and stays readable
        Assert.False(File.Exists(app.Paths.File(".gmailsettings.tmp")));
    }

    [Fact]
    public void TheRewrittenFileNoLongerOpensWithTheKeyOfTheSourceCode() {
        using var app = new TempApp();
        WriteLegacy(app, ".smtpsettings", "smtp.example.org", "user", "secret", "587", "True");
        new SettingsStore(app.Paths, app.Log).Load(".smtpsettings");

        foreach (string line in File.ReadAllLines(app.Paths.File(".smtpsettings")))
            Assert.ThrowsAny<Exception>(() => LegacySettingsCipher.Decrypt(line));
    }

    [Fact]
    public void FilesWithOldAndNewValuesAreConverted() {
        using var app = new TempApp();
        var store = new SettingsStore(app.Paths, app.Log);
        store.Save(".mixed", "new1");
        string current = File.ReadAllLines(app.Paths.File(".mixed"))[0];
        File.WriteAllLines(app.Paths.File(".mixed"), new[] { current, LegacySettingsCipher.Encrypt("old2") });

        Assert.Equal(new[] { "new1", "old2" }, store.Load(".mixed"));
        Assert.All(File.ReadAllLines(app.Paths.File(".mixed")), line => Assert.StartsWith("v2:", line));
    }

    [Fact]
    public void MigrateLegacyFilesConvertsEveryKnownFileAndCountsThem() {
        using var app = new TempApp();
        WriteLegacy(app, ".htaccess", "bob", "pw1");
        WriteLegacy(app, ".pop3settings", "pop.example.org", "u", "p", "995", "False");
        var store = new SettingsStore(app.Paths, app.Log);
        store.Save(".smtpsettings", "h\r\nu\r\np\r\n25\r\nFalse");        // already current

        Assert.Equal(2, store.MigrateLegacyFiles());
        Assert.Equal(0, store.MigrateLegacyFiles());

        Assert.Equal(new WebLogin("bob", "pw1"), store.LoadWebLogin());
        Assert.Equal(new MailServerSettings("pop.example.org", "u", "p", 995, false), store.LoadPop3());
    }

    [Fact]
    public void SettingsWrittenWithAnotherKeyAreReportedNotThrown() {
        using var app = new TempApp();
        new SettingsStore(app.Paths, app.Log).Save(".gmailsettings", "a\r\nb");
        File.Delete(app.Paths.File(".janet.key"));                                      // key lost: a new one is made

        var restarted = new SettingsStore(app.Paths, app.Log);

        Assert.Null(restarted.Load(".gmailsettings"));
        Assert.True(restarted.Exists(".gmailsettings"));                                // still there, so nothing is reset to defaults
        Assert.Contains(app.Log.Entries, e => e.Contains("cannot be read"));
        Assert.Equal("Settings saved.", restarted.Save(".gmailsettings", "a\r\nb"));    // and can be saved again
        Assert.NotNull(restarted.Load(".gmailsettings"));
    }

    [Fact]
    public void ADamagedFileIsReportedNotThrown() {
        using var app = new TempApp();
        File.WriteAllText(app.Paths.File(".gmailsettings"), "this is not encrypted");

        Assert.Null(new SettingsStore(app.Paths, app.Log).Load(".gmailsettings"));
    }

    [Fact]
    public void StartingAnOldInstallationKeepsItsWebLoginAndMigratesIt() {
        string directory = Path.Combine(Path.GetTempPath(), "janet-migrate-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(directory);
        try {
            File.WriteAllLines(Path.Combine(directory, ".htaccess"), new[] { LegacySettingsCipher.Encrypt("bob"), LegacySettingsCipher.Encrypt("pw1") });
            File.WriteAllLines(Path.Combine(directory, ".smtpsettings"), new[] { "smtp.example.org", "user1", "secret1", "587", "true" }.Select(LegacySettingsCipher.Encrypt));

            using (var host = new JanetHost(new JanetHostOptions { RootDirectory = directory, ExitProcess = () => { } }, TestParts.Offline())) {
                host.Start();

                Assert.Equal("smtp.example.org\r\nuser1\r\nsecret1\r\n587\r\nTrue", host.Execute("judo smtp settings").Trim());
            }

            var store = new SettingsStore(new AppPaths(directory), new MemoryLog());
            Assert.Equal(new WebLogin("bob", "pw1"), store.LoadWebLogin());            // not replaced by the default admin/admin
            Assert.All(File.ReadAllLines(Path.Combine(directory, ".htaccess")), line => Assert.StartsWith("v2:", line));
            Assert.All(File.ReadAllLines(Path.Combine(directory, ".smtpsettings")), line => Assert.StartsWith("v2:", line));
        }
        finally {
            try { Directory.Delete(directory, true); } catch (IOException) { }
        }
    }

    [Fact]
    public void ANewInstallationNeverWritesTheOldFormat() {
        using var t = new TestHost();
        t.Run("judo smtp set smtp.example.org user secret 587 true");

        string[] files = { ".htaccess", ".smtpsettings" };
        foreach (string file in files)
            Assert.All(File.ReadAllLines(Path.Combine(t.Directory, file)), line => Assert.StartsWith("v2:", line));
        Assert.True(File.Exists(Path.Combine(t.Directory, ".janet.key")));
    }
}
