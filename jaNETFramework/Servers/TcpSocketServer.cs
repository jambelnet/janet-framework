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
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace jaNET.Servers;

/// <summary>Who may use the socket server: the addresses listed in the "Trusted" setting, separated by ; , or spaces.</summary>
internal static class TrustPolicy
{
    public static bool IsTrusted(string trustedList, string remoteAddress) {
        IEnumerable<string> entries = trustedList
            .Replace("localhost", "127.0.0.1")
            .Split(new[] { ';', ',', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

        return entries.Any(entry => string.Equals(entry, remoteAddress, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// The socket server: a line of text in, the answer line out ("yes\r\n" -> "Good morning ...").
/// A plain "GET /command HTTP/1.1" request is answered too, so a browser or curl can talk to it.
/// </summary>
internal sealed class TcpSocketServer : IServer, IDisposable
{
    static readonly Regex HttpGet = new("GET.*HTTP", RegexOptions.Compiled);

    readonly AppConfigStore _config;
    readonly Func<IInstructionExecutor> _executor;
    readonly ILog _log;
    readonly object _gate = new();

    TcpListener? _listener;
    CancellationTokenSource? _stop;
    volatile ServiceProblem? _problem;

    public TcpSocketServer(AppConfigStore config, Func<IInstructionExecutor> executor, ILog log) {
        _config = config;
        _executor = executor;
        _log = log;
    }

    public bool IsRunning => _listener != null;

    public ServiceProblem? Problem => _problem;

    public void Start() {
        lock (_gate) {
            if (_listener != null) return;

            CommSettings comm = _config.Comm;
            string host = comm.LocalHost.Length > 0 ? comm.LocalHost : "127.0.0.1";
            string portText = comm.LocalPort.Length > 0 ? comm.LocalPort : "5744";

            try {
                IPAddress address = IPAddress.Parse(host.Replace("localhost", "127.0.0.1"));
                int port = Convert.ToInt32(portText);

                var listener = new TcpListener(address, port);
                listener.Start();

                _problem = null;
                _listener = listener;
                _stop = new CancellationTokenSource();
                _ = AcceptLoop(listener, _stop.Token);
            }
            catch (Exception e) {
                _log.Write($"obj [ Server.TcpServer.ListenforClients <{e.GetType().Name}> ]: {e.Message}");
                _problem = ServiceProblems.SocketServer(e, host, portText);
            }
        }
    }

    public void Stop() {
        lock (_gate) {
            _stop?.Cancel();
            _listener?.Stop();
            _listener = null;
            _problem = null;
        }
    }

    public void Dispose() => Stop();

    async Task AcceptLoop(TcpListener listener, CancellationToken stop) {
        while (!stop.IsCancellationRequested) {
            try {
                TcpClient client = await listener.AcceptTcpClientAsync(stop).ConfigureAwait(false);
                _ = Task.Run(() => Serve(client, stop));
            }
            catch (Exception e) when (e is OperationCanceledException || e is ObjectDisposedException || e is SocketException) {
                if (!stop.IsCancellationRequested)
                    _log.Write($"obj [ Server.TcpServer.ListenforClients <{e.GetType().Name}> ]: {e.Message}");
                return;
            }
        }
    }

    async Task Serve(TcpClient client, CancellationToken stop) {
        using (client) {
            try {
                string trusted = _config.Comm.Trusted.Length > 0 ? _config.Comm.Trusted : "127.0.0.1";
                string remote = ((IPEndPoint)client.Client.RemoteEndPoint!).Address.MapToIPv4().ToString();
                NetworkStream stream = client.GetStream();

                var buffer = new byte[1024];
                string data = string.Empty;
                int read;

                while (!stop.IsCancellationRequested && (read = await stream.ReadAsync(buffer, stop).ConfigureAwait(false)) != 0) {
                    data += UriCodec.Decode(Encoding.ASCII.GetString(buffer, 0, read));

                    Match request = HttpGet.Match(data);
                    if (request.Success) {
                        data = request.Value.Replace("GET /", string.Empty).Replace("HTTP", string.Empty).Trim() + "\r\n";
                        if (data.Contains("favicon.ico", StringComparison.OrdinalIgnoreCase))
                            break;
                    }

                    if (!TrustPolicy.IsTrusted(trusted, remote)) {
                        await Reply(stream, new UnauthorizedAccessException().Message, stop).ConfigureAwait(false);
                        data = string.Empty;
                    }
                    else if (data.Contains("\r\n")) {
                        string command = data.Replace("\r\n", string.Empty);
                        await Reply(stream, await Task.Run(() => _executor().Run(command), stop).ConfigureAwait(false), stop).ConfigureAwait(false);
                        data = string.Empty;
                        if (request.Success) break;      // a browser request ends after its answer
                    }
                }
            }
            catch (Exception e) when (e is OperationCanceledException || e is System.IO.IOException || e is SocketException || e is ObjectDisposedException) {
                // client left or the server is stopping
            }
        }
    }

    static Task Reply(NetworkStream stream, string text, CancellationToken stop) =>
        stream.WriteAsync(Encoding.ASCII.GetBytes(text + "\r\n"), stop).AsTask();
}
