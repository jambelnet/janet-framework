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
using jaNET.Scripting;
using jaNET.Servers;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace jaNETFramework.Tests;

internal sealed class SimulatedSerialConnection : ISerialConnection
{
    readonly BlockingCollection<object> _incoming = new();
    int _open;
    int _activeReaders;
    public int Opens { get; private set; }
    public bool IsOpen => Volatile.Read(ref _open) == 1;
    public int MaxReaders { get; private set; }
    public ConcurrentQueue<string> Writes { get; } = new();
    public Action<string> OnWrite { get; set; }
    public Action BeforeRead { get; set; }
    public TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Open() { Opens++; Volatile.Write(ref _open, 1); }
    public void Close() { Volatile.Write(ref _open, 0); _incoming.CompleteAdding(); }
    public void Push(string line) => _incoming.Add(line);
    public void Disconnect() => _incoming.Add(new IOException("Device unplugged."));
    public string ReadLine() {
        int readers = Interlocked.Increment(ref _activeReaders);
        MaxReaders = Math.Max(MaxReaders, readers);
        try {
            BeforeRead?.Invoke();
            if (_incoming.TryTake(out object value, 250)) {
                if (value is Exception error) throw error;
                return (string)value;
            }
            if (_incoming.IsCompleted) throw new IOException("Port closed.");
            throw new TimeoutException();
        }
        finally { Interlocked.Decrement(ref _activeReaders); }
    }
    public void WriteLine(string message) {
        if (!IsOpen) throw new IOException("Port closed.");
        Writes.Enqueue(message);
        OnWrite?.Invoke(message);
    }
    public void Dispose() { Close(); Disposed.TrySetResult(); }
}

public class SerialServiceTests
{
    static TaskCompletionSource<string> Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    static SerialPortService Service(TempApp app, SimulatedSerialConnection connection, RecordingExecutor executor) =>
        new(app.NewConfig(), () => executor, app.Log, (_, _) => connection);
    static async Task<T> Until<T>(Task<T> task) => await task.WaitAsync(TimeSpan.FromSeconds(5));
    static async Task Done(Task task) => await task.WaitAsync(TimeSpan.FromSeconds(5));

    [Fact]
    public async Task AnImmediateReplyIsNotLostAndTheFirstLineIsKept() {
        using var app = new TempApp();
        var connection = new SimulatedSerialConnection();
        connection.OnWrite = _ => { connection.Push("first\r"); connection.Push("second"); };
        using var serial = Service(app, connection, new RecordingExecutor());
        serial.Open(string.Empty);
        Assert.Equal("first", await Until(Task.Run(() => serial.Write("ping", SerialMessageKind.Send, 2000))));
        Assert.Equal(new[] { "ping" }, connection.Writes);
    }

    [Fact]
    public async Task BufferedSensorEventsSurviveOpeningAndSending() {
        using var app = new TempApp();
        app.NewConfig().AddEvent("sensor", "handle-sensor");
        var seen = Signal();
        var executor = new RecordingExecutor { Answer = action => { seen.TrySetResult(action); return ""; } };
        var connection = new SimulatedSerialConnection();
        connection.Push("sensor");
        connection.OnWrite = _ => connection.Push("42");
        using var serial = Service(app, connection, executor);
        serial.Open(string.Empty);
        Assert.Equal("42", await Until(Task.Run(() => serial.Write("read", SerialMessageKind.Send, 2000))));
        Assert.Equal("handle-sensor", await Until(seen.Task));
    }

    [Fact]
    public async Task EventNotificationsDoNotReplaceCommandReplies() {
        using var app = new TempApp();
        app.NewConfig().AddEvent("button", "handle-button");
        var seen = Signal();
        var executor = new RecordingExecutor { Answer = action => { seen.TrySetResult(action); return ""; } };
        var connection = new SimulatedSerialConnection { OnWrite = _ => { } };
        var sent = Signal();
        connection.OnWrite = _ => { connection.Push(" button\r"); sent.TrySetResult("sent"); };
        using var serial = Service(app, connection, executor);
        serial.Open(string.Empty);
        Task<string> reply = Task.Run(() => serial.Write("read", SerialMessageKind.Send, 4000));
        await Until(sent.Task);
        Assert.Equal("handle-button", await Until(seen.Task));
        Assert.False(reply.IsCompleted);
        connection.Push("42");
        Assert.Equal("42", await Until(reply));
    }

    [Theory]
    [InlineData((int)SerialMessageKind.Listen)]
    [InlineData((int)SerialMessageKind.Monitor)]
    public async Task ListenersStillReceiveEventLines(int kind) {
        using var app = new TempApp();
        app.NewConfig().AddEvent("sensor", "handle-sensor");
        var connection = new SimulatedSerialConnection();
        using var serial = Service(app, connection, new RecordingExecutor());
        serial.Open(string.Empty);
        Task<string> reply = Task.Run(() => serial.Write("", (SerialMessageKind)kind, 4000));
        for (int i = 0; i < 100 && !reply.IsCompleted; i++) {
            connection.Push("sensor");
            await Task.WhenAny(reply, Task.Delay(20));
        }
        Assert.Equal("sensor", await Until(reply));
        Assert.Empty(connection.Writes);
    }

    [Fact]
    public async Task ASerialEventCanSendACommandAndReceiveItsReply() {
        using var app = new TempApp();
        app.NewConfig().AddEvent("button", "ask-board");
        var result = Signal();
        var connection = new SimulatedSerialConnection();
        connection.OnWrite = _ => connection.Push("pong");
        SerialPortService serial = null;
        var executor = new RecordingExecutor { Answer = _ => {
            result.TrySetResult(serial.Write("ping", SerialMessageKind.Send, 2000));
            return "";
        } };
        using (serial = Service(app, connection, executor)) {
            serial.Open(string.Empty);
            connection.Push("button");
            Assert.Equal("pong", await Until(result.Task));
        }
    }

    [Fact]
    public async Task SlowEventsDoNotBlockRepliesAndEventsStayOrdered() {
        using var app = new TempApp();
        AppConfigStore config = app.NewConfig();
        config.AddEvent("slow", "slow-action");
        config.AddEvent("next", "next-action");
        var started = Signal();
        var release = Signal();
        var next = Signal();
        var executor = new RecordingExecutor { Answer = action => {
            if (action == "slow-action") { started.TrySetResult(action); release.Task.GetAwaiter().GetResult(); }
            else next.TrySetResult(action);
            return "";
        } };
        var connection = new SimulatedSerialConnection();
        connection.OnWrite = _ => connection.Push("pong");
        using var serial = Service(app, connection, executor);
        serial.Open(string.Empty);
        try {
            connection.Push("slow");
            await Until(started.Task);
            connection.Push("next");
            Assert.Equal("pong", await Until(Task.Run(() => serial.Write("ping", SerialMessageKind.Send, 2000))));
            Assert.False(next.Task.IsCompleted);
            release.TrySetResult("");
            Assert.Equal("next-action", await Until(next.Task));
            lock (executor.Calls) Assert.Equal(new[] { "slow-action", "next-action" }, executor.Calls);
        }
        finally { release.TrySetResult(""); }
    }

    [Fact]
    public async Task AFailedEventIsLoggedAndDoesNotStopTheReaderOrNextEvent() {
        using var app = new TempApp();
        AppConfigStore config = app.NewConfig();
        config.AddEvent("bad", "throw");
        config.AddEvent("good", "continue");
        var seen = Signal();
        var executor = new RecordingExecutor { Answer = action => {
            if (action == "throw") throw new InvalidOperationException("Test failure.");
            seen.TrySetResult(action);
            return "";
        } };
        var connection = new SimulatedSerialConnection();
        using var serial = Service(app, connection, executor);
        serial.Open(string.Empty);
        connection.Push("bad");
        connection.Push("good");
        Assert.Equal("continue", await Until(seen.Task));
        Assert.True(serial.IsOpen);
        lock (app.Log.Entries) Assert.Contains(app.Log.Entries, entry => entry.Contains("Event 'bad' failed"));
    }

    [Fact]
    public async Task ClosingUnblocksAPendingRequest() {
        using var app = new TempApp();
        var written = Signal();
        var connection = new SimulatedSerialConnection { OnWrite = _ => written.TrySetResult("") };
        using var serial = Service(app, connection, new RecordingExecutor());
        serial.Open(string.Empty);
        Task<string> reply = Task.Run(() => serial.Write("ping", SerialMessageKind.Send, 60000));
        await Until(written.Task);
        serial.Close();
        Assert.Equal("", await Until(reply));
        await Done(connection.Disposed.Task);
        Assert.False(serial.IsOpen);
        Assert.Null(serial.Problem);
    }

    [Fact]
    public async Task DisconnectReportsTheProblemUnblocksRequestsAndAllowsReopening() {
        using var app = new TempApp();
        var written = Signal();
        var old = new SimulatedSerialConnection { OnWrite = _ => written.TrySetResult("") };
        var fresh = new SimulatedSerialConnection();
        fresh.OnWrite = _ => fresh.Push("pong");
        var connections = new ConcurrentQueue<SimulatedSerialConnection>(new[] { old, fresh });
        using var serial = new SerialPortService(app.NewConfig(), () => new RecordingExecutor(), app.Log, (_, _) => {
            Assert.True(connections.TryDequeue(out var connection)); return connection;
        });
        serial.Open(string.Empty);
        Task<string> reply = Task.Run(() => serial.Write("ping", SerialMessageKind.Send, 60000));
        await Until(written.Task);
        old.Disconnect();
        Assert.Equal("", await Until(reply));
        await Done(old.Disposed.Task);
        Assert.False(serial.IsOpen);
        Assert.Equal(NoticeLevel.Warning, serial.Problem.Level);
        serial.Open(string.Empty);
        Assert.Null(serial.Problem);
        Assert.Equal("pong", await Until(Task.Run(() => serial.Write("ping", SerialMessageKind.Send, 2000))));
    }

    [Fact]
    public async Task AReopenedPortDoesNotReuseTheOldReaderOrRunOldEvents() {
        using var app = new TempApp();
        app.NewConfig().AddEvent("old-event", "stale-action");
        var entered = Signal();
        var release = Signal();
        var old = new SimulatedSerialConnection { BeforeRead = () => {
            entered.TrySetResult(""); release.Task.GetAwaiter().GetResult();
        } };
        old.Push("old-event");
        var fresh = new SimulatedSerialConnection();
        fresh.OnWrite = _ => fresh.Push("pong");
        var connections = new ConcurrentQueue<SimulatedSerialConnection>(new[] { old, fresh });
        var executor = new RecordingExecutor();
        using var serial = new SerialPortService(app.NewConfig(), () => executor, app.Log, (_, _) => {
            Assert.True(connections.TryDequeue(out var connection)); return connection;
        });
        try {
            serial.Open(string.Empty);
            await Until(entered.Task);
            serial.Close();
            serial.Open(string.Empty);
            Assert.Equal("pong", await Until(Task.Run(() => serial.Write("ping", SerialMessageKind.Send, 2000))));
            release.TrySetResult("");
            await Done(old.Disposed.Task);
            lock (executor.Calls) Assert.Empty(executor.Calls);
            Assert.Equal(1, old.MaxReaders);
            Assert.Equal(1, fresh.MaxReaders);
        }
        finally { release.TrySetResult(""); }
    }

    [Fact]
    public async Task ATimeoutDoesNotLeakThePreviousReplyIntoTheNextRequest() {
        using var app = new TempApp();
        var connection = new SimulatedSerialConnection();
        using var serial = Service(app, connection, new RecordingExecutor());
        serial.Open(string.Empty);
        Assert.Equal("", await Until(Task.Run(() => serial.Write("silent", SerialMessageKind.Send, 20))));
        connection.OnWrite = _ => connection.Push("fresh");
        Assert.Equal("fresh", await Until(Task.Run(() => serial.Write("reply", SerialMessageKind.Send, 2000))));
    }

    [Fact]
    public async Task ConcurrentRequestsAreSerializedAndKeepTheirOwnAnswers() {
        using var app = new TempApp();
        var firstWritten = Signal();
        var connection = new SimulatedSerialConnection();
        connection.OnWrite = command => {
            if (command == "first") firstWritten.TrySetResult("");
            else connection.Push("second-answer");
        };
        using var serial = Service(app, connection, new RecordingExecutor());
        serial.Open(string.Empty);
        Task<string> first = Task.Run(() => serial.Write("first", SerialMessageKind.Send, 4000));
        await Until(firstWritten.Task);
        Task<string> second = Task.Run(() => serial.Write("second", SerialMessageKind.Send, 4000));
        Assert.Equal(new[] { "first" }, connection.Writes);
        connection.Push("first-answer");
        Assert.Equal("first-answer", await Until(first));
        Assert.Equal("second-answer", await Until(second));
        Assert.Equal(new[] { "first", "second" }, connection.Writes);
    }

    [Fact]
    public async Task QueueOverloadIsLoggedDoesNotBlockRepliesAndCloseDropsQueuedEvents() {
        using var app = new TempApp();
        app.NewConfig().AddEvent("sensor", "slow-action");
        var started = Signal();
        var release = Signal();
        var finished = Signal();
        var executor = new RecordingExecutor { Answer = action => {
            started.TrySetResult(action);
            release.Task.GetAwaiter().GetResult();
            finished.TrySetResult(action);
            return "";
        } };
        var connection = new SimulatedSerialConnection();
        connection.OnWrite = _ => connection.Push("pong");
        using var serial = Service(app, connection, executor);
        serial.Open(string.Empty);
        try {
            connection.Push("sensor");
            await Until(started.Task);
            for (int i = 0; i < 300; i++) connection.Push("sensor");
            Assert.Equal("pong", await Until(Task.Run(() => serial.Write("ping", SerialMessageKind.Send, 4000))));
            lock (app.Log.Entries) Assert.Contains(app.Log.Entries, entry => entry.Contains("Event queue is full"));
            serial.Close();
            release.TrySetResult("");
            await Until(finished.Task);
            await Done(connection.Disposed.Task);
            lock (executor.Calls) Assert.Single(executor.Calls);
        }
        finally { release.TrySetResult(""); }
    }

    [Fact]
    public async Task FailedWritesCloseTheConnectionAndReportTheProblem() {
        using var app = new TempApp();
        var connection = new SimulatedSerialConnection { OnWrite = _ => throw new IOException("Device unplugged during write.") };
        using var serial = Service(app, connection, new RecordingExecutor());
        serial.Open(string.Empty);
        Assert.Equal("", serial.Write("ping", SerialMessageKind.Send));
        Assert.False(serial.IsOpen);
        Assert.NotNull(serial.Problem);
        await Done(connection.Disposed.Task);
    }

    [Fact]
    public void NegativeTimeoutsAreRejectedAndZeroDoesNotWait() {
        using var app = new TempApp();
        var connection = new SimulatedSerialConnection();
        using var serial = Service(app, connection, new RecordingExecutor());
        serial.Open(string.Empty);
        Assert.Throws<ArgumentOutOfRangeException>(() => serial.Write("ping", SerialMessageKind.Send, -1));
        Assert.Equal("", serial.Write("ping", SerialMessageKind.Send, 0));
        Assert.Equal(new[] { "ping" }, connection.Writes);
    }

    [Fact]
    public void InvalidBaudRatesReportAProblemInsteadOfThrowing() {
        using var app = new TempApp();
        app.NewConfig().Update(new CommUpdate { BaudRate = "999999999999999999999999" });
        using var serial = Service(app, new SimulatedSerialConnection(), new RecordingExecutor());
        serial.Open(string.Empty);
        Assert.False(serial.IsOpen);
        Assert.Contains("baud rate", serial.Problem.Message);
    }

    [Fact]
    public void OpeningAnOpenPortDoesNotCreateAnotherReaderAndDisposeIsIdempotent() {
        using var app = new TempApp();
        var connection = new SimulatedSerialConnection();
        var serial = Service(app, connection, new RecordingExecutor());
        serial.Open(string.Empty);
        serial.Open(string.Empty);
        Assert.Equal(1, connection.Opens);
        serial.Dispose();
        serial.Dispose();
        Assert.False(serial.IsOpen);
        Assert.Throws<ObjectDisposedException>(() => serial.Open(string.Empty));
    }
}
