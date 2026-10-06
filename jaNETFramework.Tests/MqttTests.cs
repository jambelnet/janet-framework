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
using jaNET.Scripting;
using jaNET.Servers;
using MQTTnet;
using MQTTnet.Protocol;
using MQTTnet.Server;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace jaNETFramework.Tests;

public class MqttTopicTests
{
    [Theory]
    [InlineData("home/kitchen/temp", "home/kitchen/temp", true)]
    [InlineData("home/+/temp", "home/kitchen/temp", true)]
    [InlineData("home/+/temp", "home/kitchen/humidity", false)]
    [InlineData("home/+/temp", "home/kitchen/oven/temp", false)]
    [InlineData("home/#", "home/kitchen/temp", true)]
    [InlineData("home/#", "home", true)]
    [InlineData("home/#", "garage/door", false)]
    [InlineData("#", "anything/at/all", true)]
    [InlineData("+", "single", true)]
    [InlineData("+", "two/levels", false)]
    [InlineData("#", "$SYS/broker/load", false)]
    [InlineData("+/broker/load", "$SYS/broker/load", false)]
    [InlineData("$SYS/#", "$SYS/broker/load", true)]
    [InlineData("a/b", "a/b/c", false)]
    [InlineData("a/b/c", "a/b", false)]
    public void FiltersMatchTopicsAsTheSpecificationSays(string filter, string topic, bool expected) {
        Assert.Equal(expected, MqttTopic.Matches(filter, topic));
    }

    [Theory]
    [InlineData("home/+/temp", true)]
    [InlineData("home/#", true)]
    [InlineData("#", true)]
    [InlineData("", false)]
    [InlineData("home/#/temp", false)]
    [InlineData("home/te+mp", false)]
    [InlineData("home/temp#", false)]
    public void FilterValidation(string filter, bool valid) {
        Assert.Equal(valid, MqttTopic.IsValidFilter(filter));
    }

    [Theory]
    [InlineData("home/kitchen/temp", true)]
    [InlineData("home/+/temp", false)]
    [InlineData("home/#", false)]
    [InlineData("", false)]
    public void ATopicToPublishToHasNoWildcards(string topic, bool valid) {
        Assert.Equal(valid, MqttTopic.IsValidName(topic));
    }

    [Theory]
    [InlineData("ON", "ON")]
    [InlineData("21.5 C", "21.5_C")]
    [InlineData("a;b&c", "a_b_c")]
    [InlineData("x\" || true || \"", "x_____true_____")]
    [InlineData("{evalBool(1==1); a; b;}", "_evalBool_1==1___a__b__")]
    [InlineData("%exit%", "_exit_")]
    [InlineData("*launcher [*x]", "_launcher___x_")]
    [InlineData("<lock>judo</lock>", "_lock_judo_/lock_")]
    [InlineData("./evil", "_/evil")]
    [InlineData("../up", "../up")]
    [InlineData("judo server stop", "judo_server_stop")]
    [InlineData("tab\tnew\nline", "tab_new_line")]
    public void OutsideTextCannotCarryInstructionsIntoAnAction(string text, string expected) {
        Assert.Equal(expected, MqttTopic.Safe(text));
    }

    [Fact]
    public void OutsideTextIsCutShort() {
        Assert.Equal(200, MqttTopic.Safe(new string('a', 5000)).Length);
        Assert.Equal(5, MqttTopic.Safe("abcdefgh", 5).Length);
    }

    [Theory]
    [InlineData("temperature 21.5 C", "21.5")]
    [InlineData("-3", "-3")]
    [InlineData("+7.25", "+7.25")]
    [InlineData("v1.2.3", "1.2")]
    [InlineData("none", "")]
    [InlineData("", "")]
    [InlineData("12.", "12")]
    public void TheFirstNumberOfAMessage(string payload, string expected) {
        MqttMessage.Set("t", payload.Replace(" ", "_"));

        Assert.Equal(expected, MqttMessage.Number);
    }

    [Fact]
    public async Task MessagesThatRunAtTheSameTimeDoNotMix() {
        var seen = new System.Collections.Concurrent.ConcurrentBag<string>();
        Task Run(string id) => Task.Run(async () => {
            MqttMessage.Set("topic/" + id, "payload" + id);
            await Task.Delay(50);
            seen.Add(MqttMessage.Topic + "=" + MqttMessage.Payload);
        });

        await Task.WhenAll(Enumerable.Range(0, 20).Select(i => Run(i.ToString())));

        Assert.Equal(20, seen.Count);
        Assert.All(seen, s => Assert.Equal(s.Split('=')[0].Replace("topic/", "payload"), s.Split('=')[1].Replace("payload", "payload")));
    }
}

/// <summary>What a hostile message can and cannot do once it is inside an instruction.</summary>
public class MqttInjectionTests
{
    [Theory]
    [InlineData("judo server stop")]
    [InlineData("judo%20server%20stop")]
    [InlineData("./some-program")]
    [InlineData("x; judo server stop")]
    [InlineData("x & judo server stop")]
    [InlineData("<lock>judo server stop</lock>")]
    [InlineData("%exit%")]
    public void APayloadAsAWholeInstructionRunsNothing(string payload) {
        using var t = new TestHost();
        t.Run("judo inset add act <lock>%mqttpayload%</lock>");
        MqttMessage.Set("some/topic", payload);

        string answer = t.Run("act");

        Assert.True(t.Web.IsRunning);
        Assert.Equal(0, t.Exits);
        Assert.DoesNotContain("stopped", answer);
    }

    [Fact]
    public void APayloadCannotBreakOutOfAConditionsString() {
        using var t = new TestHost();
        t.Run("judo inset add yes <lock>YES</lock>");
        t.Run("judo inset add no <lock>NO</lock>");
        t.Run("judo inset add check <lock>{ evalBool(\"%mqttpayload%\" == \"ON\"); yes; no; }</lock>");

        MqttMessage.Set("t", "ON");
        Assert.Equal("YES", t.Run("check").Trim());

        MqttMessage.Set("t", "x\" == \"x\" || \"x");
        Assert.Equal("NO", t.Run("check").Trim());

        MqttMessage.Set("t", "\"); System.Environment.Exit(1); (\"");
        Assert.Equal("NO", t.Run("check").Trim());
    }

    [Fact]
    public void TheMqttFunctionsAreKnownAndCannotBeReplaced() {
        using var t = new TestHost();

        Assert.Contains("%mqtttopic%", t.Host.Syntax.Functions);
        Assert.Contains("%mqttpayload%", t.Host.Syntax.Functions);
        Assert.Contains("%mqttnumber%", t.Host.Syntax.Functions);

        using var app = new TempApp();
        var options = new JanetHostOptions { RootDirectory = app.Directory, ExitProcess = () => { } };
        options.Functions["mqttpayload"] = () => "mine";
        Assert.Throws<ArgumentException>(() => new JanetHost(options, TestParts.Offline()));
    }
}

internal sealed class FakeMqtt : IMqttService
{
    public bool IsConnected { get; private set; }
    public bool IsRunning { get; private set; }
    public ServiceProblem Problem { get; private set; }
    public ServiceProblem FailWith { get; set; }
    public int Starts { get; private set; }
    public int Stops { get; private set; }
    public int SubscriptionChanges { get; private set; }
    public List<(string Topic, string Payload, bool Retain)> Published { get; } = new List<(string, string, bool)>();

    public void Start() {
        Starts++;
        IsRunning = true;
        if (FailWith != null) { Problem = FailWith; return; }
        IsConnected = true;
        Problem = null;
    }

    public void Stop() { Stops++; IsRunning = false; IsConnected = false; Problem = null; }

    public string Publish(string topic, string payload, bool retain) {
        Published.Add((topic, payload, retain));
        return "Published.";
    }

    public void SubscriptionsChanged() => SubscriptionChanges++;
}

public class MqttCommandTests
{
    static TestHost NewHost(bool start = true) => new TestHost(start);

    [Fact]
    public void WithoutABrokerThereIsNothingToShow() {
        using var t = NewHost();

        Assert.Equal("MQTT state: False", t.Run("judo mqtt state"));
        Assert.Equal("(no broker set)", t.Run("judo mqtt settings"));
        Assert.Equal(string.Empty, t.Run("judo mqtt subscriptions"));
    }

    [Theory]
    [InlineData("judo mqtt set broker.local", "broker.local", "1883", "plain")]
    [InlineData("judo mqtt set broker.local 1884", "broker.local", "1884", "plain")]
    [InlineData("judo mqtt set broker.local tls", "broker.local", "8883", "tls")]
    [InlineData("judo mqtt set broker.local 9000 tls", "broker.local", "9000", "tls")]
    [InlineData("judo mqtt set broker.local 8883 insecure", "broker.local", "8883", "insecure")]
    public void TheBrokerIsSet(string command, string host, string port, string tls) {
        using var t = NewHost();

        Assert.Equal("Element added.", t.Run(command));

        Assert.Equal($"{host}\r\n{port}\r\n{tls}\r\n(automatic)", t.Run("judo mqtt settings"));
    }

    [Fact]
    public void AClientIdIsTakenWhenItIsNotANumberOrAnOption() {
        using var t = NewHost();

        t.Run("judo mqtt set broker.local 1883 plain my-janet");

        Assert.EndsWith("my-janet", t.Run("judo mqtt settings"));
    }

    [Theory]
    [InlineData("judo mqtt set mqtt://broker.local", "is not a host name")]
    [InlineData("judo mqtt set broker.local/path", "is not a host name")]
    [InlineData("judo mqtt set broker.local 70000", "'70000' is not a port number.")]
    public void ABadBrokerIsRefused(string command, string reason) {
        using var t = NewHost();

        Assert.Contains(reason, t.Run(command));
        Assert.Equal("(no broker set)", t.Run("judo mqtt settings"));
    }

    [Fact]
    public void TheLoginIsKeptEncrypted() {
        using var t = NewHost();

        Assert.Equal("Settings saved.", t.Run("judo mqtt login me s3cret-pw"));

        string stored = File.ReadAllText(Path.Combine(t.Directory, ".mqttsettings"));
        Assert.StartsWith("v2:", stored.Trim());
        Assert.DoesNotContain("s3cret-pw", stored);
        Assert.DoesNotContain("s3cret-pw", t.Run("judo mqtt settings"));
    }

    [Fact]
    public void StartingAndStoppingIsRememberedForTheNextRun() {
        using var t = NewHost();
        t.Run("judo mqtt set broker.local");

        Assert.Equal("MQTT state: True", t.Run("judo mqtt start"));
        Assert.True(t.Host.Config.Mqtt.IsEnabled);
        Assert.Equal(1, t.Mqtt.Starts);

        Assert.Equal("MQTT state: False", t.Run("judo mqtt stop"));
        Assert.False(t.Host.Config.Mqtt.IsEnabled);
        Assert.Equal(1, t.Mqtt.Stops);
    }

    [Fact]
    public void AnEnabledClientConnectsWhenJanetStartsAndIsStoppedWhenItStops() {
        using var app = new TempApp();
        app.NewConfig().UpdateMqtt(new MqttUpdate { Broker = "broker.local", Enabled = "true" });
        var mqtt = new FakeMqtt();
        var parts = new HostParts {
            Clock = new FakeClock(), Speaker = new SilentSpeaker(), Http = new FakeHttp(), Weather = new FakeWeather(), Console = new FakeConsole(),
            Web = new FakeServer(), Socket = new FakeServer(), Serial = new FakeSerial(), Mqtt = mqtt, ExitDelay = TimeSpan.FromMilliseconds(20)
        };
        var host = new JanetHost(new JanetHostOptions { RootDirectory = app.Directory, ExitProcess = () => { } }, parts);

        host.Start();
        Assert.True(mqtt.IsRunning);

        host.Dispose();
        Assert.False(mqtt.IsRunning);
    }

    [Fact]
    public void ADisabledClientDoesNotConnectAtStart() {
        using var t = NewHost();
        t.Run("judo mqtt set broker.local");

        Assert.False(t.Mqtt.IsRunning);
    }

    [Fact]
    public void ABrokerThatCannotBeReachedAnswersWithTheReasonAndIsAnError() {
        using var t = NewHost();
        t.Run("judo mqtt set broker.local");
        t.Mqtt.FailWith = new ServiceProblem(NoticeLevel.Error, "Cannot connect to the MQTT broker broker.local:1883: nothing listens there.");

        string answer = t.Run("judo mqtt start");

        Assert.Equal("MQTT state: False\r\nReason: Cannot connect to the MQTT broker broker.local:1883: nothing listens there.", answer);
        HostNotice notice = Assert.Single(t.Host.Notices, n => n.Source == "MQTT");
        Assert.Equal(NoticeLevel.Error, notice.Level);
    }

    [Fact]
    public void ChangingTheBrokerReconnects() {
        using var t = NewHost();
        t.Run("judo mqtt set broker.local");
        t.Run("judo mqtt start");

        t.Run("judo mqtt set other.local");

        Assert.Equal(2, t.Mqtt.Starts);
        Assert.Equal(1, t.Mqtt.Stops);
    }

    [Fact]
    public void PublishingGivesTheTopicTheMessageAndRetain() {
        using var t = NewHost();

        Assert.Equal("Published.", t.Run("judo mqtt publish home/light/set ON"));
        Assert.Equal("Published.", t.Run("judo mqtt publish home/light/set `light is on` retain"));

        Assert.Equal(("home/light/set", "ON", false), t.Mqtt.Published[0]);
        Assert.Equal(("home/light/set", "light is on", true), t.Mqtt.Published[1]);
    }

    [Fact]
    public void SubscriptionsAreAddedListedChangedAndRemoved() {
        using var t = NewHost();

        Assert.Equal("Element added.", t.Run("judo mqtt subscribe home/+/temp <lock>judo inset ls</lock>"));
        t.Run("judo mqtt subscribe home/door <lock>alarm</lock>");
        Assert.Equal("home/+/temp | judo inset ls\r\nhome/door | alarm", t.Run("judo mqtt subscriptions"));

        t.Run("judo mqtt subscribe home/door <lock>alarm2</lock>");
        Assert.Equal("home/+/temp | judo inset ls\r\nhome/door | alarm2", t.Run("judo mqtt subscriptions"));

        Assert.Equal("Element removed.", t.Run("judo mqtt unsubscribe home/door"));
        Assert.Equal("home/+/temp | judo inset ls", t.Run("judo mqtt subscriptions"));
        Assert.Equal(4, t.Mqtt.SubscriptionChanges);
    }

    [Fact]
    public void ABadTopicFilterIsRefused() {
        using var t = NewHost();

        Assert.Contains("is not a topic filter", t.Run("judo mqtt subscribe home/#/x <lock>a</lock>"));
        Assert.Equal(string.Empty, t.Run("judo mqtt subscriptions"));
    }

    [Fact]
    public void ALoginThatTravelsInClearTextOverTheNetworkIsAWarning() {
        using var t = NewHost();
        t.Run("judo mqtt set broker.example.org");
        t.Run("judo mqtt login me pw");
        t.Run("judo mqtt start");

        Assert.Contains(t.Host.Notices, n => n.Source == "Security" && n.Message.Contains("MQTT login") && n.Message.Contains("tls"));

        t.Run("judo mqtt set broker.example.org 8883 tls");
        Assert.DoesNotContain(t.Host.Notices, n => n.Message.Contains("MQTT login"));

        t.Run("judo mqtt set broker.example.org 8883 insecure");
        Assert.Contains(t.Host.Notices, n => n.Message.Contains("certificate is not checked"));
    }

    [Fact]
    public void ABrokerOnThisComputerIsNoSecurityConcern() {
        using var t = NewHost();
        t.Run("judo mqtt set localhost");
        t.Run("judo mqtt login me pw");
        t.Run("judo mqtt start");

        Assert.DoesNotContain(t.Host.Notices, n => n.Source == "Security");
    }
}

/// <summary>The real client against a real broker (MQTTnet's server, in this process, on a free port).</summary>
public class MqttBrokerTests : IAsyncDisposable
{
    readonly TempApp _app = new TempApp();
    readonly int _port = Ports.Free();
    readonly List<MqttService> _services = new List<MqttService>();
    readonly List<IMqttClient> _clients = new List<IMqttClient>();
    readonly MqttClientFactory _factory = new MqttClientFactory();
    MqttServer _broker;

    public async ValueTask DisposeAsync() {
        foreach (MqttService service in _services) service.Dispose();
        foreach (IMqttClient client in _clients) client.Dispose();
        if (_broker != null) await _broker.StopAsync();
        _broker?.Dispose();
        _app.Dispose();
    }

    async Task StartBroker(Func<ValidatingConnectionEventArgs, Task> validate = null) {
        _broker = new MqttServerFactory().CreateMqttServer(new MqttServerOptionsBuilder()
            .WithDefaultEndpoint().WithDefaultEndpointBoundIPAddress(IPAddress.Loopback).WithDefaultEndpointPort(_port).Build());
        if (validate != null) _broker.ValidatingConnectionAsync += validate;
        await _broker.StartAsync();
    }

    sealed class Capture : IInstructionExecutor
    {
        public List<(string Action, string Topic, string Payload, string Number)> Runs { get; } = new List<(string, string, string, string)>();

        public string Run(string input, ResponseFormat format = ResponseFormat.Text, bool silent = false) {
            lock (Runs) Runs.Add((input, MqttMessage.Topic, MqttMessage.Payload, MqttMessage.Number));
            return "ran";
        }
    }

    (MqttService Service, Capture Capture, AppConfigStore Config) NewService(string user = null, string password = null) {
        AppConfigStore config = _app.NewConfig();
        config.UpdateMqtt(new MqttUpdate { Broker = "127.0.0.1", Port = _port.ToString(), Tls = "false", ClientId = "janet-test", Enabled = "true" });
        var settings = new SettingsStore(_app.Paths, _app.Log);
        if (user != null) settings.Save(SettingsFiles.Mqtt, user + "\r\n" + password);
        var capture = new Capture();
        var service = new MqttService(config, settings, () => capture, _app.Log);
        _services.Add(service);
        return (service, capture, config);
    }

    async Task<IMqttClient> Peer(string subscribe = null, Action<string, string, bool> onMessage = null) {
        IMqttClient client = _factory.CreateMqttClient();
        _clients.Add(client);
        client.ApplicationMessageReceivedAsync += e => {
            onMessage?.Invoke(e.ApplicationMessage.Topic, Encoding.UTF8.GetString(e.ApplicationMessage.Payload), e.ApplicationMessage.Retain);
            return Task.CompletedTask;
        };
        await client.ConnectAsync(new MqttClientOptionsBuilder().WithTcpServer("127.0.0.1", _port).WithClientId("peer-" + Guid.NewGuid().ToString("N").Substring(0, 6)).Build());
        if (subscribe != null) await client.SubscribeAsync(_factory.CreateSubscribeOptionsBuilder().WithTopicFilter(subscribe).Build());
        return client;
    }

    static async Task<bool> Until(Func<bool> condition, int milliseconds = 8000) {
        for (int waited = 0; waited < milliseconds; waited += 25) {
            if (condition()) return true;
            await Task.Delay(25);
        }
        return condition();
    }

    [Fact]
    public async Task ItConnectsAndSaysOnlineAndOfflineOnItsStatusTopic() {
        await StartBroker();
        var messages = new List<string>();
        await Peer("janet-test/status", (topic, payload, retain) => { lock (messages) messages.Add(payload); });
        var (service, _, _) = NewService();

        service.Start();

        Assert.True(await Until(() => service.IsConnected));
        Assert.True(await Until(() => { lock (messages) return messages.Contains("online"); }));
        service.Stop();
        Assert.True(await Until(() => { lock (messages) return messages.Contains("offline"); }));
        Assert.False(service.IsConnected);
    }

    [Fact]
    public async Task AMessageOnASubscribedTopicRunsTheActionWithTheMessage() {
        await StartBroker();
        var (service, capture, config) = NewService();
        config.AddMqttSubscription("home/+/temp", "act-temp");
        config.AddMqttSubscription("home/door", "act-door");
        service.Start();
        Assert.True(await Until(() => service.IsConnected));
        IMqttClient peer = await Peer();

        await peer.PublishStringAsync("home/kitchen/temp", "temperature 21.5 C");
        await peer.PublishStringAsync("home/garage/light", "ignored");

        Assert.True(await Until(() => { lock (capture.Runs) return capture.Runs.Count >= 1; }));
        await Task.Delay(300);
        lock (capture.Runs) {
            var run = Assert.Single(capture.Runs);
            Assert.Equal(("act-temp", "home/kitchen/temp", "temperature_21.5_C", "21.5"), run);
        }
    }

    [Fact]
    public async Task EveryMatchingSubscriptionRuns() {
        await StartBroker();
        var (service, capture, config) = NewService();
        config.AddMqttSubscription("home/#", "all");
        config.AddMqttSubscription("home/door", "door");
        service.Start();
        Assert.True(await Until(() => service.IsConnected));

        await (await Peer()).PublishStringAsync("home/door", "open");

        Assert.True(await Until(() => { lock (capture.Runs) return capture.Runs.Count == 2; }));
        lock (capture.Runs) Assert.Equal(new[] { "all", "door" }, capture.Runs.Select(r => r.Action).OrderBy(a => a));
    }

    [Fact]
    public async Task SubscriptionsChangedWhileConnectedTakeEffect() {
        await StartBroker();
        var (service, capture, config) = NewService();
        service.Start();
        Assert.True(await Until(() => service.IsConnected));
        IMqttClient peer = await Peer();

        config.AddMqttSubscription("later/topic", "later");
        service.SubscriptionsChanged();
        await peer.PublishStringAsync("later/topic", "1");
        Assert.True(await Until(() => { lock (capture.Runs) return capture.Runs.Count == 1; }));

        config.RemoveMqttSubscription("later/topic");
        service.SubscriptionsChanged();
        await peer.PublishStringAsync("later/topic", "2");
        await Task.Delay(400);
        lock (capture.Runs) Assert.Single(capture.Runs);
    }

    [Fact]
    public async Task PublishedMessagesReachOtherClientsAndRetainWorks() {
        await StartBroker();
        var received = new List<(string Topic, string Payload, bool Retain)>();
        await Peer("out/#", (topic, payload, retain) => { lock (received) received.Add((topic, payload, retain)); });
        var (service, _, _) = NewService();
        service.Start();
        Assert.True(await Until(() => service.IsConnected));

        Assert.Equal("Published.", service.Publish("out/a", "hello world", retain: false));
        Assert.Equal("Published.", service.Publish("out/b", "kept", retain: true));

        Assert.True(await Until(() => { lock (received) return received.Count >= 2; }));
        lock (received) {
            Assert.Contains(("out/a", "hello world", false), received);
            Assert.Contains(received, r => r.Topic == "out/b" && r.Payload == "kept");
        }

        var late = new List<(string Topic, string Payload, bool Retain)>();
        await Peer("out/b", (topic, payload, retain) => { lock (late) late.Add((topic, payload, retain)); });
        Assert.True(await Until(() => { lock (late) return late.Count == 1; }));
        lock (late) Assert.True(late[0].Retain);                         // a client that comes later still gets it
    }

    [Fact]
    public async Task PublishingWithoutAConnectionSaysSo() {
        var (service, _, _) = NewService();

        string answer = service.Publish("a/b", "x", false);

        Assert.StartsWith("MQTT is not connected.", answer);
        Assert.Equal("'a/+' is not a topic to publish to (empty, or with + or #).", service.Publish("a/+", "x", false));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task ItConnectsAgainWhenTheBrokerComesBack() {
        await StartBroker();
        var (service, _, _) = NewService();
        service.Start();
        Assert.True(await Until(() => service.IsConnected));

        await _broker.StopAsync();
        _broker.Dispose();
        Assert.True(await Until(() => !service.IsConnected && service.Problem != null));
        Assert.Equal(NoticeLevel.Error, service.Problem.Level);
        Assert.Contains("127.0.0.1", service.Problem.Message);

        await StartBroker();
        Assert.True(await Until(() => service.IsConnected, 20000));
        Assert.Null(service.Problem);
    }

    [Fact]
    public async Task ABrokerThatIsNotThereIsReportedWithTheWayOut() {
        var (service, _, _) = NewService();       // nothing listens on the port

        service.Start();

        Assert.True(await Until(() => service.Problem != null));
        Assert.Contains($"Cannot connect to the MQTT broker 127.0.0.1:{_port}", service.Problem.Message);
        Assert.Contains("nothing listens there", service.Problem.Message);
        Assert.Contains("tries again in", service.Problem.Message);
        Assert.False(service.IsConnected);
    }

    [Fact]
    public async Task AWrongLoginIsReportedAsSuch() {
        await StartBroker(e => { e.ReasonCode = MqttConnectReasonCode.BadUserNameOrPassword; return Task.CompletedTask; });
        var (service, _, _) = NewService("me", "wrong");

        service.Start();

        Assert.True(await Until(() => service.Problem != null));
        Assert.True(service.Problem.Message.Contains("refused the login"), service.Problem.Message);
        Assert.Contains("judo mqtt login", service.Problem.Message);
    }

    [Fact]
    public async Task TheLoginIsSentToTheBroker() {
        string seen = null;
        await StartBroker(e => { seen = e.UserName + ":" + e.Password; return Task.CompletedTask; });
        var (service, _, _) = NewService("me", "pw1");

        service.Start();

        Assert.True(await Until(() => service.IsConnected));
        Assert.Equal("me:pw1", seen);
    }

    [Fact]
    public async Task WithoutABrokerSetItDoesNotStart() {
        AppConfigStore config = _app.NewConfig();
        var service = new MqttService(config, new SettingsStore(_app.Paths, _app.Log), () => new Capture(), _app.Log);
        _services.Add(service);

        service.Start();

        Assert.False(service.IsRunning);
        Assert.Contains("judo mqtt set", service.Problem.Message);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task StartingTwiceAndStoppingTwiceIsHarmless() {
        await StartBroker();
        var (service, _, _) = NewService();

        service.Start();
        service.Start();
        Assert.True(await Until(() => service.IsConnected));
        service.Stop();
        service.Stop();

        Assert.False(service.IsRunning);
    }

    [Fact]
    public async Task AFloodOfMessagesIsHandledWithoutFallingBehindForever() {
        await StartBroker();
        var (service, capture, config) = NewService();
        config.AddMqttSubscription("flood/#", "act");
        service.Start();
        Assert.True(await Until(() => service.IsConnected));
        IMqttClient peer = await Peer();

        for (int i = 0; i < 500; i++) await peer.PublishStringAsync("flood/x", i.ToString());

        Assert.True(await Until(() => { lock (capture.Runs) return capture.Runs.Count >= 100; }));
        Assert.True(await Until(() => { lock (capture.Runs) return capture.Runs.Last().Payload == "499"; }));   // the newest are not lost to the oldest
    }
}
