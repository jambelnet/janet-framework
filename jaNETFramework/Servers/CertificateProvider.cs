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
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace jaNET.Servers;

/// <summary>The certificate the web server uses for https, and where it came from.</summary>
internal sealed class ServerCertificate
{
    public ServerCertificate(X509Certificate2 certificate, bool selfSigned, string source) {
        Certificate = certificate;
        SelfSigned = selfSigned;
        Source = source;
    }

    public X509Certificate2 Certificate { get; }

    /// <summary>True for the certificate jaNET made itself: browsers warn about it once.</summary>
    public bool SelfSigned { get; }

    /// <summary>The file it was read from.</summary>
    public string Source { get; }

    /// <summary>SHA-256 fingerprint as browsers show it, e.g. "AB:CD:...".</summary>
    public string Fingerprint => string.Join(":", Certificate.GetCertHash(System.Security.Cryptography.HashAlgorithmName.SHA256).Select(b => b.ToString("X2")));

    public override string ToString() =>
        $"{(SelfSigned ? "self-signed" : "own")} certificate {Fingerprint}, valid until {Certificate.NotAfter:yyyy-MM-dd}";
}

/// <summary>
/// Finds the certificate for https: the .pfx file named in AppConfig.xml (password in the encrypted .tlssettings), or, if there is none,
/// a self-signed one that jaNET makes the first time and keeps in the data folder (.janet.cert.pfx, readable by the owner only).
/// A self-signed certificate is made again when it is about to expire or does not cover the name of the web server any more.
/// </summary>
internal sealed class CertificateProvider
{
    public const string FileName = ".janet.cert.pfx";
    static readonly TimeSpan Validity = TimeSpan.FromDays(3 * 365);
    static readonly TimeSpan RenewBefore = TimeSpan.FromDays(30);

    readonly AppPaths _paths;
    readonly ISettingsStore _settings;
    readonly Func<DateTimeOffset> _utcNow;

    public CertificateProvider(AppPaths paths, ISettingsStore settings, Func<DateTimeOffset>? utcNow = null) {
        _paths = paths;
        _settings = settings;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    /// <param name="certificateFile">The .pfx/.p12 from AppConfig.xml, or empty for the self-signed one.</param>
    /// <param name="host">The host name or address of the web server: the self-signed certificate must cover it.</param>
    public ServerCertificate Load(string certificateFile, string host) {
        if (!string.IsNullOrWhiteSpace(certificateFile)) {
            if (!File.Exists(certificateFile)) throw new FileNotFoundException("The certificate file does not exist.", certificateFile);
            string? password = _settings.Load(SettingsFiles.Tls)?.FirstOrDefault();
            X509Certificate2 own = X509CertificateLoader.LoadPkcs12FromFile(certificateFile, string.IsNullOrEmpty(password) ? null : password);
            if (!own.HasPrivateKey) throw new CryptographicException($"{certificateFile} has no private key.");
            return new ServerCertificate(own, selfSigned: false, certificateFile);
        }

        string path = _paths.File(FileName);
        if (File.Exists(path)) {
            try {
                X509Certificate2 existing = X509CertificateLoader.LoadPkcs12FromFile(path, null);
                if (existing.HasPrivateKey && StillGood(existing, host)) return new ServerCertificate(existing, selfSigned: true, path);
                existing.Dispose();
            }
            catch (CryptographicException) {
                // damaged: make a new one
            }
        }
        return Create(path, host);
    }

    bool StillGood(X509Certificate2 certificate, string host) {
        if (certificate.NotAfter.ToUniversalTime() - _utcNow() < RenewBefore) return false;

        IReadOnlyList<string> covered = Names(certificate);
        return RequiredNames(host).All(name => covered.Contains(name, StringComparer.OrdinalIgnoreCase));
    }

    // what the certificate has to cover: this computer and the name the web server is configured with
    static IEnumerable<string> RequiredNames(string host) {
        yield return "localhost";
        yield return Environment.MachineName;
        if (IsName(host)) yield return host;
    }

    static bool IsName(string host) =>
        host.Length > 0 && host != "+" && host != "*" && !host.Equals("localhost", StringComparison.OrdinalIgnoreCase) && !host.StartsWith('[');

    static IReadOnlyList<string> Names(X509Certificate2 certificate) {
        var names = new List<string>();
        foreach (X509Extension extension in certificate.Extensions) {
            if (extension is X509SubjectAlternativeNameExtension san) {
                names.AddRange(san.EnumerateDnsNames());
                names.AddRange(san.EnumerateIPAddresses().Select(a => a.ToString()));
            }
        }
        return names;
    }

    ServerCertificate Create(string path, string host) {
        using RSA key = RSA.Create(2048);
        var request = new CertificateRequest($"CN=jaNET {Environment.MachineName}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false));

        var names = new SubjectAlternativeNameBuilder();
        foreach (string name in RequiredNames(host).Distinct(StringComparer.OrdinalIgnoreCase)) {
            if (IPAddress.TryParse(name, out IPAddress? address)) names.AddIpAddress(address);
            else names.AddDnsName(name);
        }
        names.AddIpAddress(IPAddress.Loopback);
        names.AddIpAddress(IPAddress.IPv6Loopback);
        foreach (IPAddress address in LocalAddresses()) names.AddIpAddress(address);
        if (IPAddress.TryParse(host, out IPAddress? configured)) names.AddIpAddress(configured);
        request.CertificateExtensions.Add(names.Build());

        DateTimeOffset now = _utcNow();
        using X509Certificate2 created = request.CreateSelfSigned(now.AddDays(-1), now + Validity);
        byte[] pfx = created.Export(X509ContentType.Pfx);

        var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using (var stream = new FileStream(path, options))
            stream.Write(pfx);

        // loaded again from the file's bytes: a certificate made in memory has a key that some systems (Windows) cannot use for TLS
        return new ServerCertificate(X509CertificateLoader.LoadPkcs12(pfx, null), selfSigned: true, path);
    }

    static IEnumerable<IPAddress> LocalAddresses() {
        try {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Select(a => a.Address)
                .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
                .Distinct()
                .ToList();
        }
        catch (NetworkInformationException) {
            return Array.Empty<IPAddress>();
        }
    }
}
