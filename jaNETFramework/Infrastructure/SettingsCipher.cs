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

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace jaNET.Infrastructure;

/// <summary>
/// Protects the values in the settings files (.htaccess, .smtpsettings, ...) with AES-256-GCM: every value gets its own random
/// nonce, is authenticated (a changed or copied-over value is rejected) and is bound to the file it belongs to.
/// A stored value looks like <c>v2:base64(nonce + tag + ciphertext)</c>. The key is per installation, see <see cref="SettingsKey"/>.
/// Values written by older versions (no "v2:" prefix) can still be read, see <see cref="LegacySettingsCipher"/>, and are rewritten on first use.
/// </summary>
internal sealed class SettingsCipher
{
    const string Prefix = "v2:";
    const int NonceSize = 12;
    const int TagSize = 16;

    readonly byte[] _key;

    public SettingsCipher(byte[] key) {
        if (key.Length != SettingsKey.Size) throw new ArgumentException("The key must be 32 bytes.", nameof(key));
        _key = key;
    }

    /// <summary>True if the stored value was written by this version (otherwise it is in the old format).</summary>
    public static bool IsCurrent(string stored) => stored.StartsWith(Prefix, StringComparison.Ordinal);

    /// <param name="purpose">What the value belongs to (the file name): a value cannot be moved to another file.</param>
    public string Encrypt(string plainText, string purpose) {
        byte[] plain = Encoding.UTF8.GetBytes(plainText);
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
        byte[] cipher = new byte[plain.Length];
        byte[] tag = new byte[TagSize];

        using (var aes = new AesGcm(_key, TagSize))
            aes.Encrypt(nonce, plain, cipher, tag, Encoding.UTF8.GetBytes(purpose));

        var stored = new byte[NonceSize + TagSize + cipher.Length];
        nonce.CopyTo(stored, 0);
        tag.CopyTo(stored, NonceSize);
        cipher.CopyTo(stored, NonceSize + TagSize);
        return Prefix + Convert.ToBase64String(stored);
    }

    /// <summary>Decrypts a stored value of either format. Throws <see cref="CryptographicException"/> if it was changed or the key is not the one that wrote it.</summary>
    public string Decrypt(string stored, string purpose) {
        if (!IsCurrent(stored))
            return LegacySettingsCipher.Decrypt(stored);

        byte[] data;
        try {
            data = Convert.FromBase64String(stored.Substring(Prefix.Length));
        }
        catch (FormatException e) {
            throw new CryptographicException("The stored value is not valid.", e);
        }
        if (data.Length < NonceSize + TagSize) throw new CryptographicException("The stored value is too short.");

        byte[] plain = new byte[data.Length - NonceSize - TagSize];
        using (var aes = new AesGcm(_key, TagSize))
            aes.Decrypt(data.AsSpan(0, NonceSize), data.AsSpan(NonceSize + TagSize), data.AsSpan(NonceSize, TagSize), plain, Encoding.UTF8.GetBytes(purpose));

        return Encoding.UTF8.GetString(plain);
    }
}

/// <summary>
/// The key of the settings files: 32 random bytes made on the first run and kept in ".janet.key" next to AppConfig.xml
/// (readable by the owner only on Linux and macOS; the web server never serves it). Containers can hand the key in through the
/// environment variable JANET_KEY (base64) instead, so that it never touches the disk.
/// Keep the key together with the settings files when you make a backup: the settings cannot be read without it.
/// </summary>
internal static class SettingsKey
{
    public const int Size = 32;
    public const string FileName = ".janet.key";
    public const string EnvironmentVariable = "JANET_KEY";

    static readonly object Gate = new();

    public static byte[] LoadOrCreate(AppPaths paths, Func<string, string?>? environment = null) {
        string? fromEnvironment = (environment ?? Environment.GetEnvironmentVariable)(EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(fromEnvironment)) return Parse(fromEnvironment, EnvironmentVariable);

        string path = paths.File(FileName);

        lock (Gate) {
            if (File.Exists(path)) return Parse(File.ReadAllText(path), FileName);

            byte[] key = RandomNumberGenerator.GetBytes(Size);
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows())
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;       // 600: the key is never visible to others, not even for a moment

            using (var stream = new FileStream(path, options))
            using (var writer = new StreamWriter(stream))
                writer.Write(Convert.ToBase64String(key));
            return key;
        }
    }

    static byte[] Parse(string text, string source) {
        try {
            byte[] key = Convert.FromBase64String(text.Trim());
            if (key.Length == Size) return key;
        }
        catch (FormatException) {
        }
        throw new InvalidOperationException($"{source} must contain {Size} bytes as base64 text.");
    }
}

/// <summary>
/// The encryption of all versions before 0.4 for the values of the settings files: AES-256-CBC with a key and IV that were
/// constants of the source code. That protects nothing; it is only here to read existing files once and rewrite them with
/// <see cref="SettingsCipher"/>. The parameters are the file format and must not be changed. The scheme (Rijndael with a key derived
/// from a pass phrase, salt and SHA1) follows the "RijndaelSimple" sample by Obviex, (C) 2002 Obviex, that the original jaNET used;
/// none of that sample's code is left, see THIRD-PARTY-NOTICES.md.
/// </summary>
internal static class LegacySettingsCipher
{
    const string PassPhrase = "pass_janet";
    const string SaltValue = "salt_janet";
    const string HashAlgorithm = "SHA1";
    const int PasswordIterations = 2;
    const string InitVector = "@1B2c3D4e5F6g7H8"; // 16 bytes
    const int KeySize = 256;

    static readonly byte[] Key = DeriveKey();
    static readonly byte[] Iv = Encoding.ASCII.GetBytes(InitVector);

    static byte[] DeriveKey() {
#pragma warning disable SYSLIB0041 // legacy PBKDF1 derivation, required to read existing settings files
#pragma warning disable CA5373     // obsolete key derivation function: same reason
        using var password = new PasswordDeriveBytes(PassPhrase, Encoding.ASCII.GetBytes(SaltValue), HashAlgorithm, PasswordIterations);
        return password.GetBytes(KeySize / 8);
#pragma warning restore CA5373
#pragma warning restore SYSLIB0041
    }

    /// <summary>Only for tests that need a file in the old format.</summary>
    internal static string Encrypt(string plainText) {
        using var aes = Aes.Create();
        aes.Mode = CipherMode.CBC;
        byte[] plain = Encoding.UTF8.GetBytes(plainText);

        using var encryptor = aes.CreateEncryptor(Key, Iv);
        return Convert.ToBase64String(encryptor.TransformFinalBlock(plain, 0, plain.Length));
    }

    internal static string Decrypt(string cipherText) {
        using var aes = Aes.Create();
        aes.Mode = CipherMode.CBC;
        byte[] cipher = Convert.FromBase64String(cipherText);

        using var decryptor = aes.CreateDecryptor(Key, Iv);
        return Encoding.UTF8.GetString(decryptor.TransformFinalBlock(cipher, 0, cipher.Length));
    }
}
