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
using jaNET.Servers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace jaNET.Commands;

/// <summary>
/// judo mqtt set|login|start|stop|publish|subscribe|unsubscribe|subscriptions|settings|state: talk to an MQTT broker
/// (ESP32, Tasmota, ESPHome, Zigbee2MQTT, Home Assistant, ...). A message on a subscribed topic runs the action of the subscription with
/// %mqtttopic%, %mqttpayload% and %mqttnumber% set.
/// </summary>
internal sealed class MqttCommand : JudoCommand
{
    const int ConnectWaitMs = 3000;

    readonly IMqttService _mqtt;
    readonly AppConfigStore _config;
    readonly ISettingsStore _settings;

    public MqttCommand(IMqttService mqtt, AppConfigStore config, ISettingsStore settings) {
        _mqtt = mqtt;
        _config = config;
        _settings = settings;

        On(Set, "set", "setup", "add", "new");
        On(i => _settings.Save(SettingsFiles.Mqtt, $"{i.Args[3]}\r\n{i.Args[4]}"), "login", "cred", "credentials");
        On(Start, "start", "on", "enable", "connect", "listen");
        On(Stop, "stop", "off", "disable", "disconnect");
        On(Publish, "publish", "pub", "send");
        On(Subscribe, "subscribe", "sub", "listen-to");
        On(Unsubscribe, "unsubscribe", "unsub");
        On(Subscriptions, "subscriptions", "subs", "list", "ls");
        On(Settings, "settings");
        On(State, "state", "status");
        Otherwise(State);
    }

    public override IReadOnlyList<string> Names { get; } = new[] { "mqtt" };

    string State(JudoInvocation i) => $"MQTT state: {_mqtt.IsConnected}";

    string Settings(JudoInvocation i) {
        MqttSettings mqtt = _config.Mqtt;
        if (mqtt.Broker.Length == 0) return "(no broker set)";

        string tls = mqtt.IsInsecure ? "insecure" : mqtt.UsesTls ? "tls" : "plain";
        return $"{mqtt.Broker}\r\n{(mqtt.Port.Length > 0 ? mqtt.Port : mqtt.PortNumber.ToString())}\r\n{tls}\r\n{(mqtt.ClientId.Length > 0 ? mqtt.ClientId : "(automatic)")}";
    }

    // judo mqtt set <broker> [port] [plain|tls|insecure] [client id]
    string Set(JudoInvocation i) {
        string tls = "false";
        string? port = null;
        string? clientId = null;

        foreach (string argument in i.Args.Skip(4)) {
            switch (argument.ToLowerInvariant()) {
                case "tls" or "ssl" or "true":
                    tls = "true";
                    break;
                case "insecure" or "tls-insecure":
                    tls = "insecure";
                    break;
                case "plain" or "false" or "none":
                    tls = "false";
                    break;
                default:
                    if (int.TryParse(argument, out int number)) {
                        if (number is < 1 or > 65535) return $"'{argument}' is not a port number.";
                        port = argument;
                    }
                    else clientId = argument;
                    break;
            }
        }

        string broker = i.Args[3];
        if (broker.Contains('/') || broker.Contains(' ')) return $"'{broker}' is not a host name or address: leave out mqtt:// and the path.";

        string answer = _config.UpdateMqtt(new MqttUpdate {
            Broker = broker, Tls = tls, ClientId = clientId,
            Port = port ?? (tls == "false" ? "1883" : "8883")
        });
        return Restart(answer);
    }

    string Start(JudoInvocation i) {
        _config.UpdateMqtt(new MqttUpdate { Enabled = "true" });
        _mqtt.Start();
        WaitForTheBroker();
        return Answer();
    }

    string Stop(JudoInvocation i) {
        _config.UpdateMqtt(new MqttUpdate { Enabled = "false" });
        _mqtt.Stop();
        return State(i);
    }

    // a changed broker needs a new connection
    string Restart(string answer) {
        if (!_mqtt.IsRunning) return answer;

        _mqtt.Stop();
        _mqtt.Start();
        return answer;
    }

    string Publish(JudoInvocation i) {
        bool retain = i.Count > 5 && i.Args[5].Equals("retain", StringComparison.OrdinalIgnoreCase);
        return _mqtt.Publish(i.Args[3], i.Args[4], retain);
    }

    string Subscribe(JudoInvocation i) {
        string topic = i.Args[3];
        if (!MqttTopic.IsValidFilter(topic)) return $"'{topic}' is not a topic filter (+ stands for one level, # for the rest and must come last).";

        string answer = _config.AddMqttSubscription(topic, i.Args[4]);
        _mqtt.SubscriptionsChanged();
        return answer;
    }

    string Unsubscribe(JudoInvocation i) {
        string answer = _config.RemoveMqttSubscription(i.Args[3]);
        _mqtt.SubscriptionsChanged();
        return answer;
    }

    string Subscriptions(JudoInvocation i) =>
        string.Join("\r\n", _config.MqttSubscriptions.Select(s => $"{s.Topic} | {s.Action}"));

    void WaitForTheBroker() {
        for (int waited = 0; waited < ConnectWaitMs && !_mqtt.IsConnected && _mqtt.Problem == null; waited += 50)
            Thread.Sleep(50);
    }

    string Answer() => $"MQTT state: {_mqtt.IsConnected}" + Reason.For(_mqtt.IsConnected, _mqtt.Problem);
}
