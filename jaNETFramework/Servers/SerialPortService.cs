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
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace jaNET.Servers;

/// <summary>A line-oriented serial connection with event execution separate from the reader.</summary>
internal sealed class SerialPortService : ISerialService, IDisposable
{
    const int EventTimeoutMs = 10000;
    const int EventQueueSize = 256;

    readonly AppConfigStore _config;
    readonly Func<IInstructionExecutor> _executor;
    readonly ILog _log;
    readonly Func<string, int, ISerialConnection> _connect;
    readonly object _gate = new();
    readonly object _writeGate = new();

    volatile Session? _session;
    volatile ServiceProblem? _problem;
    bool _disposed;

    public SerialPortService(AppConfigStore config, Func<IInstructionExecutor> executor, ILog log,
        Func<string, int, ISerialConnection>? connect = null) {
        _config = config;
        _executor = executor;
        _log = log;
        _connect = connect ?? ((name, baud) => new SerialConnection(name, baud));
    }

    public bool IsOpen => _session?.Connection.IsOpen == true;
    public ServiceProblem? Problem => _problem;

    public void Open(string portName) {
        lock (_gate) {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (IsOpen) return;
            _session?.Stop();
            _session = null;
            string name = string.IsNullOrWhiteSpace(portName) ? _config.Comm.ComPort : portName;
            ISerialConnection? connection = null;

            try {
                connection = _connect(name, Convert.ToInt32(_config.Comm.BaudRate));
                connection.Open();
                var session = new Session(connection, name);
                _session = session;
                _problem = null;
                session.Worker = Task.Run(() => RunEvents(session));
                new Thread(() => ReadLines(session)) { IsBackground = true, Name = "jaNET serial" }.Start();
                _ = DisposeWhenFinished(session);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException ||
                                      e is InvalidOperationException || e is FormatException || e is OverflowException) {
                _session?.Stop();
                _session = null;
                connection?.Dispose();
                _log.Write($"obj [ SerialPortService.Open <{e.GetType().Name}> ] Exception Message: [ {e.Message} ]");
                _problem = e is FileNotFoundException ? ServiceProblems.SerialMissing(name) : ServiceProblems.SerialPort(e, name);
            }
        }
    }

    public void Close() {
        lock (_gate) {
            Session? session = _session;
            _session = null;
            session?.Stop();
            _problem = null;
        }
    }

    public void Dispose() {
        lock (_gate) {
            if (_disposed) return;
            Close();
            _disposed = true;
        }
    }

    public string Write(string message, SerialMessageKind kind, int timeoutMs = 1000) {
        ArgumentOutOfRangeException.ThrowIfNegative(timeoutMs);
        lock (_writeGate) {
            Session? session = _session;
            if (session == null || !session.Connection.IsOpen) return "Serial port state: False";
            var pending = new PendingRead(kind);
            try {
                // Register before writing so an immediate reply cannot be missed. Never flush sensor data.
                lock (session.Gate) {
                    if (session.Stopped) return string.Empty;
                    session.Pending = pending;
                    if (kind == SerialMessageKind.Send) session.Connection.WriteLine(message);
                }
                return pending.Answer.Task.Wait(timeoutMs) ? pending.Answer.Task.Result : string.Empty;
            }
            catch (Exception e) when (e is IOException || e is InvalidOperationException || e is UnauthorizedAccessException) {
                Fail(session, e);
                return string.Empty;
            }
            catch (TimeoutException e) {
                _log.Write($"obj [ SerialPortService.Write ] {e.Message}");
                return string.Empty;
            }
            finally {
                lock (session.Gate) {
                    if (ReferenceEquals(session.Pending, pending)) session.Pending = null;
                }
            }
        }
    }

    void ReadLines(Session session) {
        try {
            while (!session.Token.IsCancellationRequested) {
                string line;
                try {
                    line = session.Connection.ReadLine().Replace("\r", string.Empty).Replace("SIGKILL", "\n");
                }
                catch (TimeoutException) {
                    continue;
                }
                if (line.Trim().Length == 0 || session.Token.IsCancellationRequested) continue;
                string name = line.Trim();
                string? action = _config.EventActions(name).FirstOrDefault();
                lock (session.Gate) {
                    if (session.Stopped) break;
                    PendingRead? pending = session.Pending;
                    // Event notifications are not replies to a sent command; listen/monitor still see them.
                    if (pending != null && (action == null || pending.Kind != SerialMessageKind.Send))
                        pending.Answer.TrySetResult(line);
                    if (action != null && !session.Events.Writer.TryWrite((name, action)))
                        _log.Write($"obj [ SerialPortService ] Event queue is full; the event '{name}' was not queued.");
                }
            }
        }
        catch (Exception e) {
            if (!session.Token.IsCancellationRequested) Fail(session, e);
        }
        finally {
            session.Stop();
            try { session.Connection.Dispose(); }
            catch (Exception e) { _log.Write($"obj [ SerialPortService.Dispose ] {e.Message}"); }
            session.ReaderFinished.TrySetResult();
        }
    }

    void Fail(Session session, Exception error) {
        lock (_gate) {
            if (!ReferenceEquals(_session, session)) return;
            _session = null;
            _problem = ServiceProblems.SerialPort(error, session.Name);
            _log.Write($"obj [ SerialPortService ] Serial connection lost: {error.Message}");
            session.Stop();
        }
    }

    async Task RunEvents(Session session) {
        try {
            await foreach ((string name, string action) in session.Events.Reader.ReadAllAsync(session.Token).ConfigureAwait(false)) {
                session.Token.ThrowIfCancellationRequested();
                Task work = Task.Run(() => {
                    if (session.Token.IsCancellationRequested) return;
                    try { _executor().Run(action); }
                    catch (Exception e) { _log.Write($"obj [ SerialPortService ] Event '{name}' failed: {e.Message}"); }
                });
                using var warning = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
                Task timeout = Task.Delay(EventTimeoutMs, warning.Token);
                if (await Task.WhenAny(work, timeout).ConfigureAwait(false) == timeout && !session.Token.IsCancellationRequested)
                    _log.Write($"obj [ SerialPortService ] The event '{name}' took longer than {EventTimeoutMs / 1000} seconds.");
                // A timeout warns; it cannot cancel an instruction. Keep actions ordered instead of overlapping them.
                await work.WaitAsync(session.Token).ConfigureAwait(false);
                warning.Cancel();
            }
        }
        catch (OperationCanceledException) { }
    }

    static async Task DisposeWhenFinished(Session session) {
        await Task.WhenAll(session.ReaderFinished.Task, session.Worker).ConfigureAwait(false);
        session.StopSource.Dispose();
    }

    sealed class PendingRead(SerialMessageKind kind)
    {
        public SerialMessageKind Kind { get; } = kind;
        public TaskCompletionSource<string> Answer { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    sealed class Session
    {
        public Session(ISerialConnection connection, string name) {
            Connection = connection;
            Name = name;
            Token = StopSource.Token;
        }
        public ISerialConnection Connection { get; }
        public string Name { get; }
        public object Gate { get; } = new();
        public CancellationTokenSource StopSource { get; } = new();
        public CancellationToken Token { get; }
        public Channel<(string Name, string Action)> Events { get; } = Channel.CreateBounded<(string, string)>(
            new BoundedChannelOptions(EventQueueSize) { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
        public TaskCompletionSource ReaderFinished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Worker { get; set; } = Task.CompletedTask;
        public PendingRead? Pending { get; set; }
        public bool Stopped { get; private set; }

        public void Stop() {
            lock (Gate) {
                if (Stopped) return;
                Stopped = true;
                StopSource.Cancel();
                Events.Writer.TryComplete();
                Pending?.Answer.TrySetResult(string.Empty);
            }
            try { Connection.Close(); }
            catch (Exception e) when (e is IOException || e is InvalidOperationException || e is UnauthorizedAccessException) { }
        }
    }
}
