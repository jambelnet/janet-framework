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

using jaNET.Commands;
using jaNET.Configuration;
using jaNET.Infrastructure;
using jaNET.Scripting;
using jaNET.Servers;
using jaNET.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace jaNETFramework.Tests;

public class MailReliabilityTests
{
    [Fact]
    public void GmailErrorsAreNotReportedAsAnEmptyInboxAndSettingsChangesInvalidateTheCache() {
        using var app = new TempApp();
        var settings = new MemorySettingsStore();
        settings.Save(SettingsFiles.Gmail, "me@example.org\r\napp-password");
        int checks = 0;
        var service = new MailService(settings, app.NewConfig(), new InternetConnection(new FakeHttp()),
            () => new RecordingExecutor(), _ => {
                if (++checks == 1) throw new MailKit.Security.AuthenticationException("rejected");
                return new GmailSnapshot(3, "Total unread: 3");
            });
        Assert.Contains("Sign-in was rejected", service.GmailCheck(true));
        Assert.Contains("Google app password", service.GmailCheck(false));
        Assert.Equal(1, checks);
        settings.Save(SettingsFiles.Gmail, "me@example.org\r\nnew-app-password");
        Assert.Equal("3", service.GmailCheck(true));
        Assert.Equal("Total unread: 3", service.GmailCheck(false));
        Assert.Equal(2, checks);
    }

    [Fact]
    public void LockedPasswordsSurviveTheSettingsCommandAndInvalidPortsDoNotOverwriteSettings() {
        using var t = new TestHost();
        string password = "p(a)&ss;%25 \"quoted\" '`'";
        string command = "judo gmail set <lock>me@example.org</lock> <lock>" + password + "</lock>";
        Assert.Equal("Settings saved.", t.Run(command));
        Assert.Equal(password, new SettingsStore(new AppPaths(t.Directory), new MemoryLog()).LoadGmail().Password);
        Assert.Contains("Port must", t.Run(command + " feed smtp.gmail.com 99999 true pop.gmail.com 995 true"));
        Assert.Equal(password, new SettingsStore(new AppPaths(t.Directory), new MemoryLog()).LoadGmail().Password);
    }

    [Fact]
    public async Task GmailReadsOnlyUnreadEnvelopesAndDoesNotMarkMessagesRead() {
        using var app = new TempApp();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var commands = new List<string>();
        Task server = Task.Run(async () => {
            using var tcp = await listener.AcceptTcpClientAsync();
            using var reader = new StreamReader(tcp.GetStream(), Encoding.ASCII);
            using var writer = new StreamWriter(tcp.GetStream(), Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };
            await writer.WriteLineAsync("* OK [CAPABILITY IMAP4rev1 AUTH=PLAIN] ready");
            string line;
            while ((line = await reader.ReadLineAsync()) != null) {
                commands.Add(line);
                string tag = line.Split(' ')[0];
                if (line.Contains("AUTHENTICATE")) {
                    await writer.WriteLineAsync("+");
                    await reader.ReadLineAsync();
                    await writer.WriteLineAsync("* CAPABILITY IMAP4rev1 AUTH=PLAIN");
                }
                else if (line.Contains("CAPABILITY")) await writer.WriteLineAsync("* CAPABILITY IMAP4rev1 AUTH=PLAIN");
                else if (line.Contains("EXAMINE")) {
                    await writer.WriteLineAsync("* FLAGS (\\Seen)");
                    await writer.WriteLineAsync("* 5 EXISTS");
                    await writer.WriteLineAsync("* 0 RECENT");
                    await writer.WriteLineAsync("* OK [UIDVALIDITY 1] valid");
                    await writer.WriteLineAsync("* OK [UIDNEXT 46] next");
                    await writer.WriteLineAsync(tag + " OK [READ-ONLY] opened");
                    continue;
                }
                else if (line.Contains("SEARCH")) await writer.WriteLineAsync("* SEARCH 42 45");
                else if (line.Contains("FETCH")) {
                    foreach (int uid in new[] { 42, 45 })
                        await writer.WriteLineAsync("* " + (uid == 42 ? 1 : 2) + " FETCH (UID " + uid +
                            " ENVELOPE (\"Fri, 9 Oct 2026 10:00:00 +0000\" \"Unread subject\" ((\"Sender\" NIL \"sender\" \"example.org\")) NIL NIL NIL NIL NIL NIL \"<test@example.org>\"))");
                }
                else if (line.Contains("LOGOUT")) {
                    await writer.WriteLineAsync("* BYE");
                    await writer.WriteLineAsync(tag + " OK logout");
                    break;
                }
                await writer.WriteLineAsync(tag + " OK done");
            }
        });
        try {
            var settings = new MemorySettingsStore();
            settings.Save(SettingsFiles.Gmail, $"me@example.org\r\npassword\r\nfeed\r\nsmtp.gmail.com\r\n587\r\nTrue\r\npop.gmail.com\r\n995\r\nTrue\r\n127.0.0.1\r\n{port}\r\nFalse");
            var service = new MailService(settings, app.NewConfig(), new InternetConnection(new FakeHttp()), () => new RecordingExecutor());
            Assert.Equal("2", service.GmailCheck(true));
            Assert.Contains("Unread subject", service.GmailCheck(false));
            Assert.Contains("Total unread: 2", service.GmailCheck(false));
            await server.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Contains(commands, command => command.Contains("EXAMINE"));
            Assert.Contains(commands, command => command.Contains("SEARCH UNSEEN"));
            Assert.DoesNotContain(commands, command => command.Contains("STORE") || command.Contains("BODY") || command.Contains("SELECT"));
        }
        finally { listener.Stop(); }
    }
}

public class HttpsWebSettingsTests
{
    [Fact]
    public async Task EnablingHttpsThroughTheWebApiReturnsBeforeRestartAndThenServesTheUi() {
        using var app = new TempApp();
        var config = app.NewConfig();
        int httpPort = Ports.Free(), httpsPort = Ports.Free();
        config.Update(new CommUpdate { Hostname = "localhost", HttpPort = httpPort.ToString() });
        var settings = new SettingsStore(app.Paths, app.Log);
        var executor = new RecordingExecutor();
        using var web = new WebServer(config, settings, app.Paths, () => executor, app.Log);
        var command = new ServerCommand(web, config, settings, () => web.Certificate);
        executor.Answer = input => command.Execute(new JudoInvocation(input, ArgumentSplitter.Split(input)));
        Directory.CreateDirectory(Path.Combine(app.Directory, "www"));
        File.WriteAllText(Path.Combine(app.Directory, "www", "index.html"), "<h1>Jubito</h1>");
        web.Start();
        Assert.True(web.IsRunning, web.Problem?.Message);
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        var response = await http.PostAsJsonAsync($"http://localhost:{httpPort}/api/command", new { command = $"judo server https on {httpsPort} default" });
        response.EnsureSuccessStatusCode();
        Assert.Contains("Applying HTTPS settings", await response.Content.ReadAsStringAsync());
        for (int attempt = 0; attempt < 200 && web.Certificate == null; attempt++) await Task.Delay(50);
        Assert.NotNull(web.Certificate);
        using var https = new HttpClient(new HttpClientHandler {
            ServerCertificateCustomValidationCallback = (_, certificate, _, _) => certificate?.Thumbprint == web.Certificate?.Certificate.Thumbprint
        }) { Timeout = TimeSpan.FromSeconds(15) };
        Assert.Equal("<h1>Jubito</h1>", await https.GetStringAsync($"https://localhost:{httpsPort}/www/"));
        var off = await https.PostAsJsonAsync($"https://localhost:{httpsPort}/api/command", new { command = "judo server https off" });
        off.EnsureSuccessStatusCode();
        Assert.Contains("HTTPS: off", await off.Content.ReadAsStringAsync());
        for (int attempt = 0; attempt < 200 && (web.Certificate != null || !web.IsRunning); attempt++) await Task.Delay(50);
        Assert.Null(web.Certificate);
        Assert.True(web.IsRunning, web.Problem?.Message);
    }

    [Fact]
    public async Task JsonCommandsPreservePasswordCharactersWithoutUrlDecoding() {
        using var app = new TempApp();
        var config = app.NewConfig();
        int port = Ports.Free();
        config.Update(new CommUpdate { Hostname = "localhost", HttpPort = port.ToString() });
        var executor = new RecordingExecutor { Answer = command => command };
        using var web = new WebServer(config, new SettingsStore(app.Paths, app.Log), app.Paths, () => executor, app.Log);
        web.Start();
        using var client = new HttpClient();
        string command = "judo smtp set host user <lock>p(a)&ss;%25 +'`</lock> 587 true";
        var response = await client.PostAsJsonAsync($"http://localhost:{port}/api/command", new { command });
        Assert.Equal(command, await response.Content.ReadAsStringAsync());
    }
}
