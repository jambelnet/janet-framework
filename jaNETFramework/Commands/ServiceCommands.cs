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
using System.Collections.Generic;
using System.Threading;

namespace jaNET.Commands;

/// <summary>The line that tells why a service that was asked to start is not running; nothing when it runs or there is nothing to report.</summary>
internal static class Reason
{
    public static string For(bool running, ServiceProblem? problem) =>
        running || problem == null ? string.Empty : "\r\nReason: " + problem.Message;
}

/// <summary>judo serial open|close|send|listen|monitor|set|settings|state</summary>
internal sealed class SerialCommand : JudoCommand
{
    readonly ISerialService _serial;
    readonly AppConfigStore _config;

    public SerialCommand(ISerialService serial, AppConfigStore config) {
        _serial = serial;
        _config = config;

        On(Open, "open");
        On(Close, "close");
        On(Transfer, "send", "listen", "monitor");
        On(Set, "set", "setup");
        On(i => $"{_config.Comm.ComPort}\r\n{_config.Comm.BaudRate}", "settings");
        On(State, "state", "status");
        Otherwise(State);
    }

    public override IReadOnlyList<string> Names { get; } = new[] { "serial" };

    string State(JudoInvocation i) => $"Serial port state: {_serial.IsOpen}";

    string Open(JudoInvocation i) {
        _serial.Open(i.Count > 3 ? i.Args[3] : string.Empty);
        Thread.Sleep(50);
        return State(i) + Reason.For(_serial.IsOpen, _serial.Problem);
    }

    string Close(JudoInvocation i) {
        _serial.Close();
        Thread.Sleep(50);
        return State(i);
    }

    string Transfer(JudoInvocation i) {
        SerialMessageKind kind = i.Sub switch {
            "send" => SerialMessageKind.Send,
            "listen" => SerialMessageKind.Listen,
            "monitor" => SerialMessageKind.Monitor,
            _ => SerialMessageKind.None
        };

        if ((kind == SerialMessageKind.Listen || kind == SerialMessageKind.Monitor) && i.Count > 3)
            return _serial.Write(string.Empty, kind, Convert.ToInt32(i.Args[3]));
        if (i.Count > 4)
            return _serial.Write(i.Args[3], kind, Convert.ToInt32(i.Args[4]));
        return _serial.Write(i.Args[3], kind);
    }

    string Set(JudoInvocation i) => _config.Update(new CommUpdate {
        ComPort = i.Count >= 4 ? i.Args[3] : null,
        BaudRate = i.Count == 5 ? i.Args[4] : null
    });
}

/// <summary>judo socket on|off|set|trust|settings|state</summary>
internal sealed class SocketCommand : JudoCommand
{
    readonly IServer _socket;
    readonly AppConfigStore _config;

    public SocketCommand(IServer socket, AppConfigStore config) {
        _socket = socket;
        _config = config;

        On(i => _config.Update(new CommUpdate { Trusted = i.Args[3] }), "trust");
        On(Start, "on", "enable", "start", "listen", "open");
        On(Stop, "off", "disable", "stop", "close");
        On(Set, "set", "setup");
        On(i => $"{_config.Comm.LocalHost}\r\n{_config.Comm.LocalPort}\r\n{_config.Comm.Trusted}", "settings");
        On(State, "state", "status");
        Otherwise(State);
    }

    public override IReadOnlyList<string> Names { get; } = new[] { "socket" };

    string State(JudoInvocation i) => $"Socket state: {_socket.IsRunning}";

    string Start(JudoInvocation i) {
        _socket.Start();
        Thread.Sleep(50);
        return State(i) + Reason.For(_socket.IsRunning, _socket.Problem);
    }

    string Stop(JudoInvocation i) {
        _socket.Stop();
        Thread.Sleep(50);
        return State(i);
    }

    string Set(JudoInvocation i) => _config.Update(new CommUpdate {
        LocalHost = i.Count >= 4 ? i.Args[3] : null,
        LocalPort = i.Count == 5 ? i.Args[4] : null
    });
}

/// <summary>judo server on|off|login|set|settings|state (the web server)</summary>
internal sealed class ServerCommand : JudoCommand
{
    readonly IServer _web;
    readonly AppConfigStore _config;
    readonly ISettingsStore _settings;
    readonly Func<ServerCertificate?> _certificate;

    public ServerCommand(IServer web, AppConfigStore config, ISettingsStore settings, Func<ServerCertificate?>? certificate = null) {
        _web = web;
        _config = config;
        _settings = settings;
        _certificate = certificate ?? (() => null);

        On(Start, "on", "enable", "start", "listen");
        On(Stop, "off", "disable", "stop");
        On(i => _settings.Save(SettingsFiles.WebLogin, $"{i.Args[3]}\r\n{i.Args[4]}"), "login", "cred", "credentials");
        On(Set, "set", "setup");
        On(i => $"{_config.Comm.Hostname}\r\n{_config.Comm.HttpPort}\r\n{_config.Comm.Authentication}", "settings");
        On(Https, "https", "tls", "ssl");
        On(State, "state", "status");
        Otherwise(State);
    }

    public override IReadOnlyList<string> Names { get; } = new[] { "server" };

    /// <summary>judo server https on [port] | off | status | cert [file.pfx] [password] | cert default</summary>
    string Https(JudoInvocation i) {
        string action = i.Count > 3 ? i.Args[3].ToLowerInvariant() : "status";

        switch (action) {
            case "on" or "enable" or "start" or "listen":
                string port = i.Count > 4 ? i.Args[4] : _config.Comm.HttpsPort.Length > 0 ? _config.Comm.HttpsPort : "8443";
                if (!int.TryParse(port, out int number) || number is < 1 or > 65535) return $"'{port}' is not a port number.";
                if (port == _config.Comm.HttpPort) return $"Port {port} is already the plain http port; choose another for https.";
                _config.Update(new CommUpdate { HttpsPort = port });
                return Restart();
            case "off" or "disable" or "stop":
                _config.Update(new CommUpdate { HttpsPort = CommUpdate.Clear });
                return Restart();
            case "cert" or "certificate":
                if (i.Count <= 4) return HttpsStatus();
                if (i.Args[4].Equals("default", StringComparison.OrdinalIgnoreCase) || i.Args[4].Equals("self-signed", StringComparison.OrdinalIgnoreCase)) {
                    _config.Update(new CommUpdate { Certificate = CommUpdate.Clear });
                    _settings.Save(SettingsFiles.Tls, string.Empty);
                    return Restart();
                }
                if (!System.IO.File.Exists(i.Args[4])) return $"The file {i.Args[4]} does not exist.";
                _config.Update(new CommUpdate { Certificate = i.Args[4] });
                _settings.Save(SettingsFiles.Tls, i.Count > 5 ? i.Args[5] : string.Empty);
                return Restart();
            default:
                return HttpsStatus();
        }
    }

    // the listeners are made when the server starts: a change of https needs a restart to take effect
    string Restart() {
        if (_web.IsRunning) {
            _web.Stop();
            _web.Start();
        }
        return HttpsStatus() + Reason.For(_web.IsRunning, _web.Problem);
    }

    string HttpsStatus() {
        CommSettings comm = _config.Comm;
        if (comm.HttpsPort.Length == 0) return "HTTPS: off";

        string host = comm.Hostname.Length > 0 ? comm.Hostname : "localhost";
        string text = $"HTTPS: on, https://{host}:{comm.HttpsPort}/www/";
        ServerCertificate? certificate = _certificate();

        if (certificate != null) return text + "\r\nCertificate: " + certificate;
        return text + (comm.Certificate.Length > 0 ? "\r\nCertificate file: " + comm.Certificate : string.Empty) + (_web.IsRunning ? string.Empty : "\r\n(the web server is off)");
    }

    string State(JudoInvocation i) => $"Web server state: {_web.IsRunning}";

    string Start(JudoInvocation i) {
        _web.Start();
        Thread.Sleep(50);
        return State(i) + Reason.For(_web.IsRunning, _web.Problem);
    }

    string Stop(JudoInvocation i) {
        _web.Stop();
        Thread.Sleep(50);
        return State(i);
    }

    string Set(JudoInvocation i) => _config.Update(new CommUpdate {
        Hostname = i.Count >= 4 ? i.Args[3] : null,
        HttpPort = i.Count >= 5 ? i.Args[4] : null,
        Authentication = i.Count == 6 ? i.Args[5] : null
    });
}
