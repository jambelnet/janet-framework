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

using jaNET.Hosting;
using MQTTnet.Adapter;
using MQTTnet.Exceptions;
using MQTTnet;
using MQTTnet.Protocol;
using System;
using System.Security.Authentication;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace jaNET.Servers;

/// <summary>
/// Turns the exceptions of a service that could not start into a sentence the user can act on: what happened, to which address,
/// and which command or setting fixes it. The technical message goes to log.txt as before.
/// </summary>
internal static class ServiceProblems
{
    static readonly Regex WindowsPortName = new(@"^COM\d+$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static ServiceProblem WebServer(Exception e, string host, string port) {
        // Kestrel names the address that failed: it may be the https one
        Match failed = Regex.Match(e.Message, @"(https?)://[^:/\s]+:(\d+)");
        string scheme = failed.Success ? failed.Groups[1].Value : "http";
        string actualPort = failed.Success ? failed.Groups[2].Value : port;
        string address = $"{scheme}://{host}:{actualPort}/";

        SocketException? socket = Chain(e).OfType<SocketException>().FirstOrDefault();
        string reason = socket != null ? Socket(socket, host, actualPort, "judo server set")
            : e.Message.Contains("address already in use", StringComparison.OrdinalIgnoreCase)
                ? $"Port {actualPort} is already used by another program (maybe another jaNET). Stop it or choose another port: judo server set {host} <port>"
            : e is UriFormatException or ArgumentException or FormatException
                ? $"{e.Message} Set a host name or IP address of this computer and a port number: judo server set localhost 8080"
            : e.Message;
        return new ServiceProblem(NoticeLevel.Error, $"The web server cannot listen on {address}: {reason}");
    }

    /// <summary>The MQTT broker cannot be reached or refuses jaNET.</summary>
    public static ServiceProblem Mqtt(Exception e, string broker, int port, bool tls, TimeSpan retryIn) {
        string retry = $" jaNET tries again in {Math.Max(1, (int)retryIn.TotalSeconds)} seconds.";
        SocketException? socket = Chain(e).OfType<SocketException>().FirstOrDefault();

        string reason = e switch {
            MqttRefusedException { Code: MqttClientConnectResultCode.BadUserNameOrPassword or MqttClientConnectResultCode.NotAuthorized } =>
                "the broker refused the login (user name or password). Set them with: judo mqtt login <user> <password>",
            MqttRefusedException { Code: MqttClientConnectResultCode.ClientIdentifierNotValid } =>
                "the broker refused the client id. Choose another one with: judo mqtt set <broker> [port] [tls] <client id>",
            MqttRefusedException refused => $"the broker said no ({refused.Code})",
            MqttConnectingFailedException failed when failed.Message.Contains("BadUserNameOrPassword") || failed.Message.Contains("NotAuthorized") =>
                "the broker refused the login (user name or password). Set them with: judo mqtt login <user> <password>",
            MqttConnectingFailedException failed when failed.Message.Contains("ClientIdentifierNotValid") =>
                "the broker refused the client id. Choose another one with: judo mqtt set <broker> [port] [tls] <client id>",
            MqttConnectingFailedException failed => $"the broker said no ({failed.Message})",
            _ when Chain(e).Any(x => x is AuthenticationException) =>
                "the broker's certificate cannot be trusted. Use the broker's real certificate, or accept any with: judo mqtt set <broker> " + port + " insecure",
            _ when socket?.SocketErrorCode == SocketError.ConnectionRefused =>
                $"nothing listens there (is the broker running, is the port right{(tls ? string.Empty : ", does it want tls: judo mqtt set <broker> 8883 tls")}?)",
            _ when socket?.SocketErrorCode is SocketError.HostNotFound or SocketError.NoData =>
                "the broker's name cannot be found. Check it with: judo mqtt settings",
            _ when socket != null => socket.Message,
            _ when e is TimeoutException or MqttCommunicationTimedOutException => "the broker does not answer",
            _ => e.Message
        };
        return new ServiceProblem(NoticeLevel.Error, $"Cannot connect to the MQTT broker {broker}:{port}: {reason}.{retry}");
    }

    /// <summary>The certificate for https cannot be read.</summary>
    public static ServiceProblem Certificate(Exception e, string file) {
        string what = file.Length == 0 ? "The self-signed certificate" : $"The certificate {file}";
        string reason = e switch {
            FileNotFoundException or DirectoryNotFoundException => "the file does not exist",
            UnauthorizedAccessException => "jaNET may not read it",
            CryptographicException => "it is not a certificate with a private key, or the password is wrong",
            _ => e.Message
        };
        string fix = file.Length == 0
            ? "Delete .janet.cert.pfx in the data folder to make a new one."
            : "Give the right file and password: judo server https cert <file.pfx> <password>, or use a self-signed certificate: judo server https cert default";
        return new ServiceProblem(NoticeLevel.Error, $"The web server cannot start https: {what} cannot be used: {reason}. {fix}");
    }

    static IEnumerable<Exception> Chain(Exception e) {
        for (Exception? each = e; each != null; each = each.InnerException) yield return each;
    }

    public static ServiceProblem SocketServer(Exception e, string host, string port) {
        string reason = e switch {
            SocketException s => Socket(s, host, port, "judo socket set"),
            FormatException or OverflowException or ArgumentException =>
                $"'{host}:{port}' is not a usable address. Use an IP address of this computer and a port number: judo socket set 127.0.0.1 5744",
            _ => e.Message
        };
        return new ServiceProblem(NoticeLevel.Error, $"The socket server cannot listen on {host}:{port}: {reason}");
    }

    public static ServiceProblem SerialPort(Exception e, string name) {
        string reason = e switch {
            UnauthorizedAccessException when OperatingSystem.IsWindows() =>
                $"{name} is in use by another program or access is denied. Close the program that uses it, or unplug and plug in the device.",
            UnauthorizedAccessException =>
                $"No permission for {name}. Add your user to the group that owns it (usually dialout: sudo usermod -aG dialout $USER, then log in again).",
            IOException => $"{name} could not be opened ({e.Message}). Is the device plugged in?",
            FormatException or OverflowException => $"The baud rate is not a number. Set it with: judo serial set {name} 9600",
            ArgumentException => $"'{name}' is not a serial port of this computer. Set the right one with: judo serial set COM3 9600 (Windows) or judo serial set /dev/ttyACM0 9600",
            _ => e.Message
        };
        return new ServiceProblem(NoticeLevel.Warning, $"The serial port {name} is not open: {reason}");
    }

    /// <summary>The port or device does not exist: usually nothing is plugged in. Not an error, but worth a line.</summary>
    public static ServiceProblem SerialMissing(string name) {
        string how = OperatingSystem.IsWindows()
            ? "Set the COM port of your device with: judo serial set COM3 9600"
            : "Plug the device in or set the right one with: judo serial set /dev/ttyACM0 9600";
        return new ServiceProblem(NoticeLevel.Info, $"The serial port is not open: {name} does not exist. {how}");
    }

    /// <summary>A Windows machine cannot have a Linux device name; a Linux machine cannot have a COM name.</summary>
    public static bool IsPortNameOfThisSystem(string name) =>
        OperatingSystem.IsWindows() ? WindowsPortName.IsMatch(name) : name.StartsWith('/');

    static string Socket(SocketException s, string host, string port, string setCommand) => s.SocketErrorCode switch {
        SocketError.AddressAlreadyInUse =>
            $"Port {port} is already used by another program (maybe another jaNET). Stop it or choose another port: {setCommand} {host} <port>",
        SocketError.AccessDenied =>
            $"Port {port} needs administrator (root) rights or is blocked. Use a port above 1023: {setCommand} {host} 8080",
        SocketError.AddressNotAvailable =>
            $"{host} is not an address of this computer. Use localhost or 127.0.0.1 for this computer only, or 0.0.0.0 for every network card.",
        _ => s.Message
    };
}
