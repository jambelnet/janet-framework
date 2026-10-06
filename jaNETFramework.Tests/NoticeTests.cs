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
using jaNET.Servers;
using jaNETProgram.Terminal;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Xunit;

namespace jaNETFramework.Tests;

/// <summary>What the person at the console must see right away: why a service is not running, damaged files, unsafe settings.</summary>
public class NoticeTests
{
    static ServiceProblem Failure(string message) => new ServiceProblem(NoticeLevel.Error, message);

    [Fact]
    public void ANormalStartHasNothingToReport() {
        using var t = new TestHost();

        Assert.Empty(t.Host.Notices);
    }

    [Fact]
    public void AWebServerThatCannotStartIsReportedWithItsReason() {
        using var t = new TestHost(start: false);
        t.Web.FailWith = Failure("The web server cannot listen on http://localhost:8080/: Port 8080 is already used by another program.");

        t.Host.Start();

        HostNotice notice = Assert.Single(t.Host.Notices);
        Assert.Equal(("Web server", NoticeLevel.Error), (notice.Source, notice.Level));
        Assert.Contains("already used", notice.Message);
    }

    [Fact]
    public void AllThreeServicesAreReportedAndTheNoticeGoesWhenTheServiceRuns() {
        using var t = new TestHost(start: false);
        t.Web.FailWith = Failure("web");
        t.Socket.FailWith = Failure("socket");
        t.Serial.FailWith = new ServiceProblem(NoticeLevel.Info, "serial");
        t.Host.Start();

        Assert.Equal(new[] { "Web server", "Socket server", "Serial port" }, t.Host.Notices.Select(n => n.Source));

        t.Web.FailWith = null;
        t.Run("judo server start");
        Assert.DoesNotContain(t.Host.Notices, n => n.Source == "Web server");
    }

    [Fact]
    public void StartingFromTheConsoleAnswersWithTheReason() {
        using var t = new TestHost(start: false);
        t.Web.FailWith = Failure("Port 8080 is already used by another program.");
        t.Socket.FailWith = Failure("Port 5744 is already used by another program.");
        t.Serial.FailWith = Failure("/dev/ttyACM0 does not exist.");
        t.Host.Start();

        Assert.Equal("Web server state: False\r\nReason: Port 8080 is already used by another program.", t.Run("judo server start"));
        Assert.Equal("Socket server state: False".Replace("Socket server", "Socket") + "\r\nReason: Port 5744 is already used by another program.", t.Run("judo socket on"));
        Assert.Equal("Serial port state: False\r\nReason: /dev/ttyACM0 does not exist.", t.Run("judo serial open"));
        Assert.Equal("Web server state: False", t.Run("judo server status"));       // asking for the state stays as short as it was
    }

    [Fact]
    public void ARunningServiceAnswersAsBefore() {
        using var t = new TestHost();

        Assert.Equal("Web server state: True", t.Run("judo server start"));
        Assert.Equal("Socket state: True", t.Run("judo socket start"));
    }

    [Fact]
    public void AWebServerOpenToTheNetworkWithoutAPasswordIsAWarning() {
        using var t = new TestHost();

        Assert.Empty(t.Host.Notices);                                       // localhost and no password is the safe default
        t.Run("judo server set 0.0.0.0 8080 none");

        HostNotice notice = Assert.Single(t.Host.Notices);
        Assert.Equal(("Security", NoticeLevel.Warning), (notice.Source, notice.Level));
        Assert.Contains("judo server set 0.0.0.0 8080 basic", notice.Message);
    }

    [Fact]
    public void ThePasswordTurnsTheNetworkWarningIntoTheDefaultLoginWarningUntilItIsChanged() {
        using var t = new TestHost();
        t.Run("judo server set 0.0.0.0 8080 basic");

        Assert.Contains(t.Host.Notices, n => n.Message.Contains("admin / admin"));

        t.Run("judo server login bob s3cret");
        HostNotice notice = Assert.Single(t.Host.Notices);               // what is left: the password travels in clear text without https
        Assert.Contains("clear text", notice.Message);
    }

    [Fact]
    public void AStoppedWebServerNeedsNoSecurityWarning() {
        using var t = new TestHost();
        t.Run("judo server set 0.0.0.0 8080 none");
        t.Run("judo server stop");

        Assert.Empty(t.Host.Notices);
    }

    [Fact]
    public void ADamagedAppConfigIsReportedAndGoesWhenItIsFixed() {
        using var t = new TestHost();
        string path = Path.Combine(t.Directory, "AppConfig.xml");
        string good = File.ReadAllText(path);

        File.WriteAllText(path, "<AppConfig><System>");
        HostNotice notice = Assert.Single(t.Host.Notices);
        Assert.Equal(("Configuration", NoticeLevel.Error), (notice.Source, notice.Level));
        Assert.Contains("AppConfig.xml.bak", notice.Message);

        File.WriteAllText(path, good);
        Assert.Empty(t.Host.Notices);
    }

    [Fact]
    public void SettingsThatCannotBeDecryptedAreReportedAndGoWhenSavedAgain() {
        using var app = new TempApp();
        var store = new SettingsStore(app.Paths, app.Log);
        store.Save(".smtpsettings", "smtp.example.org\r\nme\r\nsecret\r\n25\r\nfalse");
        File.WriteAllText(app.Paths.File(".smtpsettings"), "v2:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\r\n");

        Assert.Null(store.Load(".smtpsettings"));
        string problem = Assert.Single(store.Problems);
        Assert.Contains(".smtpsettings", problem);
        Assert.Contains(".janet.key", problem);

        store.Save(".smtpsettings", "smtp.example.org\r\nme\r\nsecret\r\n25\r\nfalse");
        Assert.NotNull(store.Load(".smtpsettings"));
        Assert.Empty(store.Problems);
    }

    // ---- the reasons the real services give

    [Fact]
    public void ASocketServerOnABusyPortSaysSo() {
        using var app = new TempApp();
        int port = Ports.Free();
        var busy = new TcpListener(IPAddress.Loopback, port);
        busy.Start();
        try {
            AppConfigStore config = app.NewConfig();
            config.Update(new CommUpdate { LocalHost = "127.0.0.1", LocalPort = port.ToString() });
            using var server = new TcpSocketServer(config, () => new RecordingExecutor(), app.Log);

            server.Start();

            Assert.False(server.IsRunning);
            Assert.Equal(NoticeLevel.Error, server.Problem.Level);
            Assert.Contains($"Port {port} is already used", server.Problem.Message);
            Assert.Contains("judo socket set", server.Problem.Message);

            busy.Stop();
            server.Start();
            Assert.True(server.IsRunning);
            Assert.Null(server.Problem);
        }
        finally {
            busy.Stop();
        }
    }

    [Theory]
    [InlineData("not-an-ip", "5744")]
    [InlineData("127.0.0.1", "abc")]
    public void ASocketServerWithAnUnusableAddressSaysSo(string host, string port) {
        using var app = new TempApp();
        AppConfigStore config = app.NewConfig();
        config.Update(new CommUpdate { LocalHost = host, LocalPort = port });
        using var server = new TcpSocketServer(config, () => new RecordingExecutor(), app.Log);

        server.Start();

        Assert.False(server.IsRunning);
        Assert.Contains("not a usable address", server.Problem.Message);
    }

    [Fact]
    public void AWebServerOnABusyPortSaysSo() {
        using var app = new TempApp();
        int port = Ports.Free();
        var other = new TcpListener(IPAddress.Loopback, port);
        other.Start();
        AppConfigStore config = app.NewConfig();
        config.Update(new CommUpdate { Hostname = "localhost", HttpPort = port.ToString() });
        using var server = new WebServer(config, new SettingsStore(app.Paths, app.Log), app.Paths, () => new RecordingExecutor(), app.Log);

        server.Start();

        Assert.False(server.IsRunning);
        Assert.Equal(NoticeLevel.Error, server.Problem.Level);
        Assert.StartsWith($"The web server cannot listen on http://localhost:{port}/: ", server.Problem.Message);
        Assert.Contains("already used by another program", server.Problem.Message);
        other.Stop();
    }

    [Fact]
    public void AWebServerAddressThatMakesNoSenseSaysSo() {
        using var app = new TempApp();
        AppConfigStore config = app.NewConfig();
        config.Update(new CommUpdate { Hostname = "no such host", HttpPort = "8080" });
        using var server = new WebServer(config, new SettingsStore(app.Paths, app.Log), app.Paths, () => new RecordingExecutor(), app.Log);

        server.Start();

        Assert.False(server.IsRunning);
        Assert.NotNull(server.Problem);
        Assert.Contains("http://no such host:8080/", server.Problem.Message);
    }

    [Fact]
    public void ADeniedPortOnTheWebServerNamesTheWayOut() {
        ServiceProblem problem = ServiceProblems.WebServer(new SocketException((int)SocketError.AccessDenied), "myserver", "80");

        Assert.Equal(NoticeLevel.Error, problem.Level);
        Assert.Contains("Port 80 needs administrator", problem.Message);
        Assert.Contains("judo server set myserver 8080", problem.Message);
    }

    [Fact]
    public void ACertificateThatCannotBeReadNamesTheFileAndTheWayOut() {
        ServiceProblem problem = ServiceProblems.Certificate(new System.Security.Cryptography.CryptographicException("bad"), "/etc/my.pfx");

        Assert.Equal(NoticeLevel.Error, problem.Level);
        Assert.Contains("/etc/my.pfx", problem.Message);
        Assert.Contains("password is wrong", problem.Message);
        Assert.Contains("judo server https cert default", problem.Message);
    }

    [Fact]
    public void ASerialPortThatIsNotThereIsOnlyInformation() {
        using var app = new TempApp();
        using var serial = new SerialPortService(app.NewConfig(), () => new RecordingExecutor(), app.Log);

        serial.Open(string.Empty);                     // the default /dev/ttyACM0: nothing is plugged in

        Assert.False(serial.IsOpen);
        Assert.Equal(NoticeLevel.Info, serial.Problem.Level);
        Assert.Contains("/dev/ttyACM0 does not exist", serial.Problem.Message);
        Assert.Contains("judo serial set", serial.Problem.Message);

        serial.Close();
        Assert.Null(serial.Problem);
    }

    // ---- what the console does with them

    [Fact]
    public void EachNoticeIsShownOnceAndAgainWhenItComesBack() {
        using var t = new TestHost(start: false);
        t.Web.FailWith = Failure("web is down");
        t.Host.Start();
        var tracker = new NoticeTracker(t.Host);

        Assert.Single(tracker.TakeNew());
        Assert.Empty(tracker.TakeNew());                                   // already seen

        t.Web.FailWith = null;
        t.Run("judo server start");
        Assert.Empty(tracker.TakeNew());                                   // it went away
        t.Web.FailWith = Failure("web is down");
        t.Run("judo server stop");
        t.Run("judo server start");
        Assert.Single(tracker.TakeNew());                                  // and came back
    }

    [Fact]
    public void ANoticeThatTheAnswerAlreadyContainsIsNotRepeated() {
        using var t = new TestHost(start: false);
        t.Web.FailWith = Failure("web is down");
        t.Host.Start();
        var tracker = new NoticeTracker(t.Host);

        string answer = t.Run("judo server start");

        Assert.Empty(tracker.TakeNew(answer));
        Assert.Empty(tracker.TakeNew());                                   // and not later either
    }

    [Theory]
    [InlineData("Web server state: False\r\nReason: Port 8080 is already used.", true)]
    [InlineData("Web server state: False", false)]
    public void AnAnswerWithAReasonLooksLikeAnError(string answer, bool error) {
        Assert.Equal(error, OutputFormatter.LooksLikeError(answer));
    }
}

/// <summary>The words of the help, the commands that exist and what Tab completes must tell the same story.</summary>
public class ConsistencyTests
{
    [Fact]
    public void EveryCommandOfTheHelpExistsAndEveryCommandHasHelp() {
        using var t = new TestHost();
        string help = t.Run("judo help");
        var lines = Regex.Matches(help, @"\+ judo (?<root>\S+)(?: (?<sub>[^\s\[<]\S*))?");
        var roots = t.Host.Syntax.Roots;

        foreach (Match line in lines) {
            string root = line.Groups["root"].Value;
            Assert.True(roots.Contains(root, StringComparer.OrdinalIgnoreCase), $"help mentions 'judo {root}', which does not exist");

            string sub = line.Groups["sub"].Value;
            if (sub.Length > 0 && t.Host.Syntax.SubCommands(root).Count > 0)
                Assert.True(t.Host.Syntax.SubCommands(root).Contains(sub, StringComparer.OrdinalIgnoreCase),
                            $"help mentions 'judo {root} {sub}', which does not exist");
        }

        // synonyms (ddns, dyndns, noip, no-ip) are one command with several names: the help may use any one of them
        bool Documented(string root) => lines.Any(l => l.Groups["root"].Value.Equals(root, StringComparison.OrdinalIgnoreCase));
        bool SameCommand(string a, string b) {
            var x = t.Host.Syntax.SubCommands(a);
            return x.Count > 0 && x.SequenceEqual(t.Host.Syntax.SubCommands(b));
        }

        string[] undocumented = roots.Where(r => !Documented(r) && !roots.Any(o => o != r && SameCommand(o, r) && Documented(o))).ToArray();
        Assert.True(undocumented.Length == 0, "no help for: " + string.Join(", ", undocumented));
    }

    [Fact]
    public void TypingAWholeCommandNeverTurnsItIntoAnother() {
        using var t = new TestHost();

        foreach (string root in t.Host.Syntax.Roots) {
            var rootResult = new Completer(t.Host.Syntax).Complete("judo " + root, 5 + root.Length);
            Assert.True(rootResult == null || rootResult.Value.Text.StartsWith("judo " + root, StringComparison.OrdinalIgnoreCase),
                        $"'judo {root}' + Tab became '{rootResult?.Text}'");

            foreach (string sub in t.Host.Syntax.SubCommands(root)) {
                string line = $"judo {root} {sub}";
                var result = new Completer(t.Host.Syntax).Complete(line, line.Length);
                Assert.True(result == null || result.Value.Text.StartsWith(line, StringComparison.OrdinalIgnoreCase),
                            $"'{line}' + Tab became '{result?.Text}'");
            }
        }
    }

    [Fact]
    public void EveryStartOfACommandOffersOnlyCommandsThatStartWithIt() {
        using var t = new TestHost();

        foreach (string root in t.Host.Syntax.Roots)
            for (int length = 1; length <= root.Length; length++) {
                string line = "judo " + root.Substring(0, length);
                var c = new Completer(t.Host.Syntax);
                var result = c.Complete(line, line.Length);
                var choices = c.TakeChoices();

                Assert.NotNull(result);
                foreach (string choice in choices ?? Array.Empty<string>())
                    Assert.StartsWith(root.Substring(0, length), choice, StringComparison.OrdinalIgnoreCase);
                if (choices == null)
                    Assert.StartsWith(line, result.Value.Text, StringComparison.OrdinalIgnoreCase);
                else
                    Assert.Equal(line, result.Value.Text);            // several possibilities: the line is not touched
            }
    }

    [Fact]
    public void ServiceCommandsAnswerAlike() {
        using var t = new TestHost();

        foreach (string state in new[] { "judo server state", "judo socket state", "judo serial state" })
            Assert.Matches(@"^[A-Za-z ]+ state: (True|False)$", t.Run(state));
    }
}
