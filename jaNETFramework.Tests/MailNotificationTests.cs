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
using jaNET.Services;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace jaNETFramework.Tests;

internal sealed class RecordingNotificationMail : IMailSender
{
    public ConcurrentQueue<(string From, string To, string Subject, string Body)> Sent { get; } = new();

    public bool Send(string from, string to, string subject, string body) {
        Sent.Enqueue((from, to, subject, body));
        return true;
    }
}

public sealed class MailNotificationTests : IDisposable
{
    readonly TempApp _app = new();
    readonly MemorySettingsStore _settings = new();
    readonly RecordingNotificationMail _mail = new();
    readonly ProbeHttp _http = new();
    readonly AppConfigStore _config;
    readonly UserPresence _presence;
    readonly MailNotifier _notifier;

    public MailNotificationTests() {
        _config = _app.NewConfig();
        var executor = new RecordingExecutor();
        _presence = new UserPresence(_config, () => executor);
        _config.UpdateMailHeaders("from@example.org", "to@example.org", "Home alert");
        _settings.Save(SettingsFiles.Smtp, "smtp.example.org\nuser\nsecret\n587\ntrue");
        _notifier = new MailNotifier(_mail, _config, _settings, _presence, new InternetConnection(_http), _app.Log);
    }

    public void Dispose() => _app.Dispose();

    [Fact]
    public void PresentUserDoesNotProbeOrSend() {
        _presence.IsPresent = true;
        _notifier.Notify("Lights are on.");
        Assert.Equal(0, _http.Probes);
        Assert.Empty(_mail.Sent);
    }

    [Fact]
    public void EmptyOutputDoesNotProbeOrSend() {
        _notifier.Notify(" \r\n ");
        Assert.Equal(0, _http.Probes);
        Assert.Empty(_mail.Sent);
    }

    [Fact]
    public void MissingSmtpSettingsDoesNotProbeOrSend() {
        var notifier = new MailNotifier(_mail, _config, new MemorySettingsStore(), _presence, new InternetConnection(_http), _app.Log);
        notifier.Notify("Lights are on.");
        Assert.Equal(0, _http.Probes);
        Assert.Empty(_mail.Sent);
    }

    [Fact]
    public void AbsentAndOfflineDoesNotSend() {
        _http.Reachable = false;
        _notifier.Notify("Lights are on.");
        Assert.Equal(1, _http.Probes);
        Assert.Empty(_mail.Sent);
    }

    [Fact]
    public void AbsentAndOnlineSendsConfiguredHeadersAndOutput() {
        _notifier.Notify("Lights are on.");
        Assert.Equal(("from@example.org", "to@example.org", "Home alert", "Lights are on."), Assert.Single(_mail.Sent));
    }

    [Fact]
    public void CheckingInDuringTheProbePreventsSending() {
        _http.OnProbe = () => _presence.IsPresent = true;
        _notifier.Notify("Lights are on.");
        Assert.True(_presence.IsPresent);
        Assert.Empty(_mail.Sent);
    }

    [Fact]
    public async Task ExpiredConnectionProbeCannotStartALateSend() {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _http.OnProbe = () => {
            entered.TrySetResult();
            release.Wait(TimeSpan.FromSeconds(5));
        };
        _http.AfterProbe = () => finished.TrySetResult();
        var notifier = new MailNotifier(_mail, _config, _settings, _presence, new InternetConnection(_http), _app.Log, timeoutMs: 2000);
        Task notification = Task.Factory.StartNew(() => notifier.Notify("Must not be sent late."),
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await notification.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Empty(_mail.Sent);
        }
        finally { release.Set(); }
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(50);
        Assert.Empty(_mail.Sent);
        Assert.Contains(_app.Log.Entries, message => message.Contains("timed out"));
    }

    internal sealed class ProbeHttp : IHttpFetcher
    {
        public bool Reachable { get; set; } = true;
        public int Probes { get; private set; }
        public Action OnProbe { get; set; }
        public Action AfterProbe { get; set; }
        public string Get(string url) => throw new InvalidOperationException("No real HTTP allowed.");
        public string Get(string url, int timeoutMs) => Get(url);
        public Task<string> GetAsync(string url) => Task.FromResult(Get(url));
        public bool IsReachable(string url) {
            Probes++;
            OnProbe?.Invoke();
            AfterProbe?.Invoke();
            return Reachable;
        }
    }
}

public sealed class InstructionMailNotificationTests : IDisposable
{
    readonly TempApp _app = new();
    readonly RecordingNotificationMail _mail = new();
    readonly JanetHost _host;

    public InstructionMailNotificationTests() {
        var settings = new MemorySettingsStore();
        settings.Save(SettingsFiles.Smtp, "smtp.example.org\nuser\nsecret\n587\ntrue");
        _host = new JanetHost(new JanetHostOptions { RootDirectory = _app.Directory }, new HostParts {
            Settings = settings, NotificationMail = _mail, Http = new FakeHttp { Reachable = true },
            Clock = new FakeClock(), Speaker = new SilentSpeaker { Muted = true }, Weather = new FakeWeather(),
            Console = new FakeConsole(), Web = new FakeServer(), Socket = new FakeServer(), Serial = new FakeSerial(), Mqtt = new FakeMqtt()
        });
        _host.Config.EnsureExists();
        _host.Config.AddInstructionSets(new[] { new InstructionSetEntry("mail-test-answer", "Lights are on.") });
        _host.Config.AddEvent("mail-test-event", "mail-test-answer");
        _host.Config.RemoveEvent("oncheckin");
        _host.Config.RemoveEvent("oncheckout");
        _host.Config.AddEvent("oncheckin", "mail-test-answer");
        _host.Config.AddEvent("oncheckout", "mail-test-answer");
        _host.Start();
    }

    public void Dispose() { _host.Dispose(); _app.Dispose(); }

    [Fact]
    public void StartupAndPresenceEventsDoNotSendMail() {
        Assert.Equal("present", _host.Execute("%whereami%"));
        Assert.Empty(_mail.Sent);
        _host.Execute("%checkout%");
        Assert.Empty(_mail.Sent);
        _host.Execute("%checkin%");
        Assert.Empty(_mail.Sent);
    }

    [Theory]
    [InlineData("{mute}mail-test-event")]
    [InlineData("{widget}mail-test-event")]
    [InlineData("judo inset run mail-test-event")]
    public void SilentParentSuppressesNestedEventMailAndDoesNotMuteLaterAlerts(string command) {
        _host.Execute("%checkout%");
        _host.Execute(command);
        Assert.Empty(_mail.Sent);
        _host.Execute("mail-test-answer");
        Assert.Contains("Lights are on.", Assert.Single(_mail.Sent).Body);
    }

    [Fact]
    public void NormalEventStillNotifiesOnlyWhenAbsent() {
        _host.Execute("mail-test-event");
        Assert.Empty(_mail.Sent);
        _host.Execute("%checkout%");
        _host.Execute("mail-test-event");
        Assert.Contains("Lights are on.", Assert.Single(_mail.Sent).Body);
    }

    [Fact]
    public void SilentExecutorArgumentAlsoSuppressesNestedAlerts() {
        _host.Execute("%checkout%");
        _host.Executor.Run("mail-test-event", silent: true);
        Assert.Empty(_mail.Sent);
        _host.Execute("mail-test-answer");
        Assert.Single(_mail.Sent);
    }
}
