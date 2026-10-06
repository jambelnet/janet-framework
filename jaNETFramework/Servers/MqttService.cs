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
using MQTTnet;
using MQTTnet.Protocol;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace jaNET.Servers;

/// <summary>The broker answered the connection request with a no.</summary>
internal sealed class MqttRefusedException : Exception
{
    public MqttRefusedException(MqttClientConnectResultCode code, string? reason) : base(reason ?? code.ToString()) {
        Code = code;
    }

    public MqttClientConnectResultCode Code { get; }
}

/// <summary>The MQTT client: a broker connection that jaNET keeps up, messages in (subscriptions run actions) and messages out.</summary>
internal interface IMqttService
{
    /// <summary>Connected to the broker right now.</summary>
    bool IsConnected { get; }

    /// <summary>Started: connected, or trying to be.</summary>
    bool IsRunning { get; }

    /// <summary>Why there is no connection (while it is started); null while connected and when stopped.</summary>
    ServiceProblem? Problem => null;

    /// <summary>Starts connecting and keeps connecting, with growing pauses, until <see cref="Stop"/>.</summary>
    void Start();

    void Stop();

    /// <summary>Publishes a message. Returns "Published." or why it did not work.</summary>
    string Publish(string topic, string payload, bool retain);

    /// <summary>The subscriptions in AppConfig.xml changed: listen to the new topics and stop listening to the removed ones.</summary>
    void SubscriptionsChanged();
}

/// <summary>
/// MQTT with MQTTnet. jaNET says "online" (retained) on &lt;client id&gt;/status when it connects and the broker says "offline" if it
/// disappears. A message on a subscribed topic runs the action of the subscription, one message after the other, with %mqtttopic%,
/// %mqttpayload% and %mqttnumber% set; if messages come faster than actions finish, the oldest wait is dropped.
/// </summary>
internal sealed class MqttService : IMqttService, IDisposable
{
    const int QueueSize = 200;
    const int ActionTimeoutMs = 10000;
    const int MaxPayload = 64 * 1024;

    static readonly TimeSpan[] Pauses = { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60) };

    readonly AppConfigStore _config;
    readonly ISettingsStore _settings;
    readonly Func<IInstructionExecutor> _executor;
    readonly ILog _log;
    readonly MqttClientFactory _factory = new();
    readonly object _gate = new();
    readonly HashSet<string> _subscribed = new(StringComparer.Ordinal);

    CancellationTokenSource? _stop;
    Task? _loop;
    Task? _worker;
    Channel<(string Action, string Topic, string Payload)>? _queue;
    IMqttClient? _client;
    volatile ServiceProblem? _problem;

    public MqttService(AppConfigStore config, ISettingsStore settings, Func<IInstructionExecutor> executor, ILog log) {
        _config = config;
        _settings = settings;
        _executor = executor;
        _log = log;
    }

    public bool IsConnected => _client?.IsConnected == true;

    public bool IsRunning => _stop != null;

    public ServiceProblem? Problem => _problem;

    public void Start() {
        lock (_gate) {
            if (_stop != null) return;

            MqttSettings mqtt = _config.Mqtt;
            if (mqtt.Broker.Length == 0) {
                _problem = new ServiceProblem(NoticeLevel.Error, "No MQTT broker is set. Set it with: judo mqtt set <broker> [port] [tls]");
                return;
            }

            _problem = null;
            _stop = new CancellationTokenSource();
            _queue = Channel.CreateBounded<(string, string, string)>(new BoundedChannelOptions(QueueSize) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
            CancellationToken token = _stop.Token;
            Channel<(string Action, string Topic, string Payload)> queue = _queue;
            _worker = Task.Run(() => RunActions(queue, token));
            _loop = Task.Run(() => KeepConnected(token));
        }
    }

    public void Stop() {
        Task? loop, worker;
        lock (_gate) {
            if (_stop == null) return;
            _stop.Cancel();
            _queue?.Writer.TryComplete();
            loop = _loop;
            worker = _worker;
            _stop = null;
            _loop = null;
            _worker = null;
            _problem = null;
        }

        try { Task.WaitAll(new[] { loop, worker }.Where(t => t != null).ToArray()!, TimeSpan.FromSeconds(3)); }
        catch (AggregateException) { /* cancelled while connecting */ }
    }

    public void Dispose() {
        Stop();
        _client?.Dispose();
    }

    public string Publish(string topic, string payload, bool retain) {
        if (!MqttTopic.IsValidName(topic)) return $"'{topic}' is not a topic to publish to (empty, or with + or #).";
        if (Encoding.UTF8.GetByteCount(payload) > MaxPayload) return $"The message is longer than {MaxPayload / 1024} KB.";

        IMqttClient? client = _client;
        if (client == null || !client.IsConnected)
            return "MQTT is not connected." + (_problem != null ? "\r\nReason: " + _problem.Message : string.Empty);

        try {
            var message = new MqttApplicationMessageBuilder().WithTopic(topic).WithPayload(payload).WithRetainFlag(retain).Build();
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            client.PublishAsync(message, limit.Token).GetAwaiter().GetResult();
            return "Published.";
        }
        catch (Exception e) {
            _log.Write($"obj [ MqttService.Publish <{e.GetType().Name}> ] Exception Message: [ {e.Message} ]");
            return "The message could not be published: " + e.Message;
        }
    }

    public void SubscriptionsChanged() {
        IMqttClient? client = _client;
        if (client == null || !client.IsConnected) return;     // it is all subscribed again at the next connection

        try { Synchronize(client, CancellationToken.None).GetAwaiter().GetResult(); }
        catch (Exception e) { _log.Write($"obj [ MqttService.SubscriptionsChanged <{e.GetType().Name}> ] Exception Message: [ {e.Message} ]"); }
    }

    /* ------------------------------------------------------------------ connection */

    async Task KeepConnected(CancellationToken stop) {
        int attempt = 0;

        while (!stop.IsCancellationRequested) {
            MqttSettings mqtt = _config.Mqtt;
            IMqttClient client = _factory.CreateMqttClient();
            var disconnected = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            client.DisconnectedAsync += e => { disconnected.TrySetResult(e.Exception?.Message ?? e.ReasonString ?? "the broker closed the connection"); return Task.CompletedTask; };
            client.ApplicationMessageReceivedAsync += OnMessage;

            try {
                MqttClientConnectResult result = await client.ConnectAsync(Options(mqtt), stop).ConfigureAwait(false);
                if (result.ResultCode != MqttClientConnectResultCode.Success) throw new MqttRefusedException(result.ResultCode, result.ReasonString);
                _client = client;
                _problem = null;
                attempt = 0;

                await Synchronize(client, stop).ConfigureAwait(false);
                await client.PublishAsync(Status(mqtt, "online"), stop).ConfigureAwait(false);

                string why = await disconnected.Task.WaitAsync(stop).ConfigureAwait(false);
                _problem = new ServiceProblem(NoticeLevel.Error, $"The connection to the MQTT broker {mqtt.Broker}:{mqtt.PortNumber} was lost ({why}). jaNET connects again.");
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) {
                await SayGoodbye(client, mqtt).ConfigureAwait(false);
                return;
            }
            catch (Exception e) {
                _log.Write($"obj [ MqttService.Connect <{e.GetType().Name}> ] Exception Message: [ {e.Message} ]");
                TimeSpan pause = Pauses[Math.Min(attempt++, Pauses.Length - 1)];
                _problem = ServiceProblems.Mqtt(e, mqtt.Broker, mqtt.PortNumber, mqtt.UsesTls, pause);
            }
            finally {
                _client = null;
                lock (_gate) _subscribed.Clear();
                client.ApplicationMessageReceivedAsync -= OnMessage;
                client.Dispose();
            }

            try { await Task.Delay(Pauses[Math.Min(Math.Max(attempt - 1, 0), Pauses.Length - 1)], stop).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
        }
    }

    MqttClientOptions Options(MqttSettings mqtt) {
        string clientId = ClientId(mqtt);
        var builder = new MqttClientOptionsBuilder()
            .WithTcpServer(mqtt.Broker, mqtt.PortNumber)
            .WithClientId(clientId)
            .WithCleanSession(true)
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(30))
            .WithWillTopic(clientId + "/status")
            .WithWillPayload("offline")
            .WithWillRetain(true);

        var login = _settings.LoadMqttLogin();
        if (login != null) builder.WithCredentials(login.Value.User, login.Value.Password);

        if (mqtt.UsesTls) {
            builder.WithTlsOptions(tls => {
                tls.UseTls();
                if (mqtt.IsInsecure) tls.WithCertificateValidationHandler(_ => true);
            });
        }
        return builder.Build();
    }

    static string ClientId(MqttSettings mqtt) =>
        mqtt.ClientId.Length > 0 ? mqtt.ClientId : "janet-" + MqttTopic.Safe(Environment.MachineName.ToLowerInvariant(), 40);

    MqttApplicationMessage Status(MqttSettings mqtt, string text) =>
        new MqttApplicationMessageBuilder().WithTopic(ClientId(mqtt) + "/status").WithPayload(text).WithRetainFlag(true).Build();

    async Task SayGoodbye(IMqttClient client, MqttSettings mqtt) {
        try {
            if (client.IsConnected) {
                await client.PublishAsync(Status(mqtt, "offline"), CancellationToken.None).ConfigureAwait(false);
                await client.DisconnectAsync().ConfigureAwait(false);
            }
        }
        catch (Exception) {
            // the broker is gone, so is the last word
        }
    }

    // listen to exactly the topics of the subscriptions of AppConfig.xml
    async Task Synchronize(IMqttClient client, CancellationToken cancel) {
        List<string> wanted = _config.MqttSubscriptions.Select(s => s.Topic).Where(MqttTopic.IsValidFilter).Distinct(StringComparer.Ordinal).ToList();
        List<string> add, remove;
        lock (_gate) {
            add = wanted.Where(t => !_subscribed.Contains(t)).ToList();
            remove = _subscribed.Where(t => !wanted.Contains(t)).ToList();
        }

        if (remove.Count > 0) {
            var unsubscribe = _factory.CreateUnsubscribeOptionsBuilder();
            foreach (string topic in remove) unsubscribe.WithTopicFilter(topic);
            await client.UnsubscribeAsync(unsubscribe.Build(), cancel).ConfigureAwait(false);
        }
        if (add.Count > 0) {
            var subscribe = _factory.CreateSubscribeOptionsBuilder();
            foreach (string topic in add) subscribe.WithTopicFilter(topic, MqttQualityOfServiceLevel.AtLeastOnce);
            await client.SubscribeAsync(subscribe.Build(), cancel).ConfigureAwait(false);
        }

        lock (_gate) {
            foreach (string topic in remove) _subscribed.Remove(topic);
            foreach (string topic in add) _subscribed.Add(topic);
        }
    }

    /* ------------------------------------------------------------------ messages in */

    Task OnMessage(MqttApplicationMessageReceivedEventArgs e) {
        string topic = e.ApplicationMessage.Topic;
        string payload = Encoding.UTF8.GetString(e.ApplicationMessage.Payload);
        Channel<(string Action, string Topic, string Payload)>? queue = _queue;

        if (queue != null)
            foreach (MqttSubscription subscription in _config.MqttSubscriptions.Where(s => MqttTopic.Matches(s.Topic, topic)))
                queue.Writer.TryWrite((subscription.Action, topic, payload));

        return Task.CompletedTask;
    }

    async Task RunActions(Channel<(string Action, string Topic, string Payload)> queue, CancellationToken stop) {
        try {
            await foreach ((string action, string topic, string payload) in queue.Reader.ReadAllAsync(stop).ConfigureAwait(false)) {
                if (!TimeLimit.Run(() => {
                        MqttMessage.Set(topic, payload);
                        _executor().Run(action);
                    }, ActionTimeoutMs))
                    _log.Write($"obj [ MqttService ] The action for '{MqttTopic.Safe(topic)}' took longer than {ActionTimeoutMs / 1000} seconds.");
            }
        }
        catch (OperationCanceledException) {
            // stopping
        }
    }
}
