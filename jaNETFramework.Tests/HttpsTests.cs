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
using jaNET.Servers;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Xunit;

namespace jaNETFramework.Tests;

/// <summary>https on the real web server: a certificate of its own making, a certificate of the user's, the redirect from the plain port.</summary>
public class HttpsTests : IDisposable
{
    readonly TempApp _app = new TempApp();
    readonly RecordingExecutor _executor = new RecordingExecutor { Answer = input => "answer:" + input };
    readonly int _httpPort = Ports.Free();
    readonly int _httpsPort = Ports.Free();

    public void Dispose() => _app.Dispose();

    WebServer Server(string hostname = "localhost", string authentication = "none", string certificate = null) {
        AppConfigStore config = _app.NewConfig();
        config.Update(new CommUpdate {
            Hostname = hostname, HttpPort = _httpPort.ToString(), HttpsPort = _httpsPort.ToString(), Authentication = authentication,
            Certificate = certificate
        });
        return new WebServer(config, new SettingsStore(_app.Paths, _app.Log), _app.Paths, () => _executor, _app.Log);
    }

    // trusts exactly the certificate the server says it uses
    static HttpClient ClientTrusting(X509Certificate2 expected, bool follow = true) => new HttpClient(new HttpClientHandler {
        AllowAutoRedirect = follow,
        ServerCertificateCustomValidationCallback = (message, certificate, chain, errors) => certificate?.Thumbprint == expected.Thumbprint
    });

    [Fact]
    public async Task TheJudoApiAnswersOverHttpsWithACertificateThatJanetMadeAndKept() {
        using WebServer server = Server();
        server.Start();

        Assert.True(server.IsRunning, server.Problem?.Message);
        Assert.True(server.Certificate.SelfSigned);
        Assert.True(File.Exists(_app.Paths.File(CertificateProvider.FileName)));

        using HttpClient client = ClientTrusting(server.Certificate.Certificate);
        HttpResponseMessage response = await client.GetAsync($"https://localhost:{_httpsPort}/?cmd=yes&mode=text");
        Assert.Equal("answer:yes", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task NoCertificateOtherThanTheServersIsTrusted() {
        using WebServer server = Server();
        server.Start();
        using var client = new HttpClient();          // the default: the system must trust it, and nobody does

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync($"https://localhost:{_httpsPort}/?cmd=yes&mode=text"));
    }

    [Fact]
    public void TheSameCertificateIsUsedAgainAfterARestart() {
        string first, second;
        using (WebServer server = Server()) {
            server.Start();
            first = server.Certificate.Certificate.Thumbprint;
        }
        using (WebServer server = Server()) {
            server.Start();
            second = server.Certificate.Certificate.Thumbprint;
        }

        Assert.Equal(first, second);                  // browsers keep their exception
    }

    [Fact]
    public void ACertificateThatIsAboutToExpireIsMadeAgain() {
        var settings = new SettingsStore(_app.Paths, _app.Log);
        DateTimeOffset realNow = DateTimeOffset.UtcNow;
        // made three years minus ten days ago: it has ten days left
        var past = new CertificateProvider(_app.Paths, settings, () => realNow.AddDays(-(3 * 365 - 10)));
        string old = past.Load(string.Empty, "localhost").Certificate.Thumbprint;

        ServerCertificate renewed = new CertificateProvider(_app.Paths, settings).Load(string.Empty, "localhost");

        Assert.NotEqual(old, renewed.Certificate.Thumbprint);
        Assert.True(renewed.Certificate.NotAfter > DateTime.Now.AddYears(2));
    }

    [Fact]
    public void ACertificateThatDoesNotCoverTheConfiguredNameIsMadeAgain() {
        var provider = new CertificateProvider(_app.Paths, new SettingsStore(_app.Paths, _app.Log));
        string first = provider.Load(string.Empty, "localhost").Certificate.Thumbprint;
        string same = provider.Load(string.Empty, "localhost").Certificate.Thumbprint;

        ServerCertificate other = provider.Load(string.Empty, "janet.example.org");

        Assert.Equal(first, same);
        Assert.NotEqual(first, other.Certificate.Thumbprint);
        Assert.Contains("janet.example.org", other.Certificate.Extensions.OfType<X509SubjectAlternativeNameExtension>().Single().EnumerateDnsNames());
        Assert.Contains("localhost", other.Certificate.Extensions.OfType<X509SubjectAlternativeNameExtension>().Single().EnumerateDnsNames());
    }

    [Fact]
    public void ADamagedCertificateFileIsReplaced() {
        File.WriteAllText(_app.Paths.File(CertificateProvider.FileName), "this is not a certificate");

        ServerCertificate made = new CertificateProvider(_app.Paths, new SettingsStore(_app.Paths, _app.Log)).Load(string.Empty, "localhost");

        Assert.True(made.Certificate.HasPrivateKey);
    }

    string OwnCertificate(string password) {
        using RSA key = RSA.Create(2048);
        var request = new CertificateRequest("CN=my own", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using X509Certificate2 made = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        string path = Path.Combine(_app.Directory, "own.pfx");
        File.WriteAllBytes(path, made.Export(X509ContentType.Pfx, password));
        return path;
    }

    [Fact]
    public async Task ACertificateOfTheUsersOwnIsUsedWithItsPassword() {
        string file = OwnCertificate("s3cret");
        new SettingsStore(_app.Paths, _app.Log).Save(SettingsFiles.Tls, "s3cret");
        using WebServer server = Server(certificate: file);

        server.Start();

        Assert.True(server.IsRunning, server.Problem?.Message);
        Assert.False(server.Certificate.SelfSigned);
        Assert.False(File.Exists(_app.Paths.File(CertificateProvider.FileName)));      // none was made
        using HttpClient client = ClientTrusting(server.Certificate.Certificate);
        Assert.Equal("answer:yes", await (await client.GetAsync($"https://localhost:{_httpsPort}/?cmd=yes&mode=text")).Content.ReadAsStringAsync());
    }

    [Fact]
    public void AWrongPasswordIsReportedWithTheWayOut() {
        string file = OwnCertificate("s3cret");
        new SettingsStore(_app.Paths, _app.Log).Save(SettingsFiles.Tls, "wrong");
        using WebServer server = Server(certificate: file);

        server.Start();

        Assert.False(server.IsRunning);
        Assert.Equal(NoticeLevel.Error, server.Problem.Level);
        Assert.Contains(file, server.Problem.Message);
        Assert.Contains("password is wrong", server.Problem.Message);
    }

    [Fact]
    public void AMissingCertificateFileIsReported() {
        using WebServer server = Server(certificate: Path.Combine(_app.Directory, "nothing.pfx"));

        server.Start();

        Assert.False(server.IsRunning);
        Assert.Contains("the file does not exist", server.Problem.Message);
    }

    [Fact]
    public async Task OnThePlainPortThisComputerIsServedAsBefore() {
        using WebServer server = Server();
        server.Start();
        using var client = new HttpClient();

        Assert.Equal("answer:yes", await (await client.GetAsync($"http://localhost:{_httpPort}/?cmd=yes&mode=text")).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task OnThePlainPortOtherComputersAreSentToHttps() {
        IPAddress lan = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses).Select(a => a.Address)
            .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
        if (lan == null) return;                       // no network card: nothing to test with

        using WebServer server = Server(hostname: "0.0.0.0");
        server.Start();
        Assert.True(server.IsRunning, server.Problem?.Message);
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });

        HttpResponseMessage response = await client.GetAsync($"http://{lan}:{_httpPort}/www/index.html?x=1");

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal($"https://{lan}:{_httpsPort}/www/index.html?x=1", response.Headers.Location.OriginalString);
    }

    [Fact]
    public async Task BasicAuthenticationWorksOverHttps() {
        new SettingsStore(_app.Paths, _app.Log).Save(SettingsFiles.WebLogin, "bob\r\npw1");
        using WebServer server = Server(authentication: "basic");
        server.Start();
        using HttpClient client = ClientTrusting(server.Certificate.Certificate);
        string url = $"https://localhost:{_httpsPort}/?cmd=yes&mode=text";

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(url)).StatusCode);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("bob:pw1")));
        Assert.Equal("answer:yes", await (await client.SendAsync(request)).Content.ReadAsStringAsync());
    }

    [Fact]
    public void ABusyHttpsPortIsReportedWithThatPort() {
        var busy = new TcpListener(IPAddress.Loopback, _httpsPort);
        busy.Start();
        try {
            using WebServer server = Server();

            server.Start();

            Assert.False(server.IsRunning);
            Assert.StartsWith($"The web server cannot listen on https://localhost:{_httpsPort}/: ", server.Problem.Message);
            Assert.Contains($"Port {_httpsPort} is already used", server.Problem.Message);
        }
        finally {
            busy.Stop();
        }
    }
}

public class HttpsCommandTests
{
    [Fact]
    public void HttpsIsOffUntilItIsSwitchedOn() {
        using var t = new TestHost();

        Assert.Equal("HTTPS: off", t.Run("judo server https status"));
        Assert.Equal("HTTPS: off", t.Run("judo server https"));
    }

    [Fact]
    public void OnTakesADefaultPortOrOneYouChoose() {
        using var t = new TestHost();

        Assert.Equal("HTTPS: on, https://localhost:8443/www/\r\n(the web server is off)".Replace("\r\n(the web server is off)", string.Empty), t.Run("judo server https on"));
        Assert.Equal("8443", t.Host.Config.Comm.HttpsPort);
        Assert.StartsWith("HTTPS: on, https://localhost:9443/www/", t.Run("judo server https on 9443"));
        Assert.Equal("HTTPS: off", t.Run("judo server https off"));
        Assert.Equal(string.Empty, t.Host.Config.Comm.HttpsPort);
    }

    [Fact]
    public void ChangingHttpsRestartsTheRunningServer() {
        using var t = new TestHost();
        int starts = t.Web.Starts;

        t.Run("judo server https on");

        Assert.Equal(starts + 1, t.Web.Starts);
        Assert.Equal(1, t.Web.Stops);
        Assert.True(t.Web.IsRunning);
    }

    [Theory]
    [InlineData("judo server https on abc", "'abc' is not a port number.")]
    [InlineData("judo server https on 70000", "'70000' is not a port number.")]
    [InlineData("judo server https on 8080", "Port 8080 is already the plain http port; choose another for https.")]
    public void ABadPortIsRefused(string command, string answer) {
        using var t = new TestHost();

        Assert.Equal(answer, t.Run(command));
        Assert.Equal(string.Empty, t.Host.Config.Comm.HttpsPort);
    }

    [Fact]
    public void ACertificateFileMustExistAndItsPasswordIsKeptEncrypted() {
        using var t = new TestHost();
        string file = Path.Combine(t.Directory, "mine.pfx");

        Assert.Equal($"The file {file} does not exist.", t.Run($"judo server https cert {file} secret"));

        File.WriteAllText(file, "x");
        t.Run("judo server https on");
        string answer = t.Run($"judo server https cert {file} secret");

        Assert.Contains("HTTPS: on", answer);
        Assert.Equal(file, t.Host.Config.Comm.Certificate);
        string stored = File.ReadAllText(Path.Combine(t.Directory, ".tlssettings"));
        Assert.StartsWith("v2:", stored.Trim());
        Assert.DoesNotContain("secret", stored);

        t.Run("judo server https cert default");
        Assert.Equal(string.Empty, t.Host.Config.Comm.Certificate);
    }

    [Fact]
    public void AFailingStartAnswersWithTheReason() {
        using var t = new TestHost();
        t.Web.FailWith = new ServiceProblem(NoticeLevel.Error, "The web server cannot start https: broken.");

        string answer = t.Run("judo server https on");

        Assert.EndsWith("\r\nReason: The web server cannot start https: broken.", answer);
    }

    [Fact]
    public void APasswordThatTravelsInClearTextIsAWarningUntilHttpsIsOn() {
        using var t = new TestHost();
        t.Run("judo server login bob s3cret");
        t.Run("judo server set 0.0.0.0 8080 basic");

        Assert.Contains(t.Host.Notices, n => n.Source == "Security" && n.Message.Contains("clear text") && n.Message.Contains("judo server https on"));
    }

    [Fact]
    public void TheNoPasswordWarningMentionsHttps() {
        using var t = new TestHost();
        t.Run("judo server set 0.0.0.0 8080 none");

        Assert.Contains(t.Host.Notices, n => n.Message.Contains("judo server https on"));
    }
}
