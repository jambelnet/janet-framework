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
using System.IO.Ports;
using System.Linq;
using System.Threading;

namespace jaNET.Servers;

/// <summary>
/// The serial port (an Arduino, a Z-Wave stick, ...). Every line that arrives is remembered as the answer to the last request;
/// if it equals the id of an event, that event is run (so a sensor can trigger an action by sending its name).
/// </summary>
internal sealed class SerialPortService : ISerialService, IDisposable
{
    const int EventTimeoutMs = 10000;

    readonly AppConfigStore _config;
    readonly Func<IInstructionExecutor> _executor;
    readonly ILog _log;
    readonly object _gate = new();
    readonly object _writeGate = new();
    readonly SerialPort _port = new();
    readonly ManualResetEventSlim _lineArrived = new(false);

    volatile string _lastLine = string.Empty;
    volatile ServiceProblem? _problem;

    public SerialPortService(AppConfigStore config, Func<IInstructionExecutor> executor, ILog log) {
        _config = config;
        _executor = executor;
        _log = log;
    }

    public bool IsOpen => _port.IsOpen;

    public ServiceProblem? Problem => _problem;

    public void Open(string portName) {
        lock (_gate) {
            string name = string.IsNullOrWhiteSpace(portName) ? _config.Comm.ComPort : portName;

            try {
                if (_port.IsOpen) return;

                // on Linux and macOS the port is a device file, on Windows it is called COMn; a name of the other kind
                // (the default /dev/ttyACM0 on Windows) or a device that is not there means that nothing is plugged in
                if (OperatingSystem.IsWindows() ? !ServiceProblems.IsPortNameOfThisSystem(name) : !File.Exists(name)) {
                    _problem = ServiceProblems.SerialMissing(name);
                    return;
                }

                _port.PortName = name;
                _port.BaudRate = Convert.ToInt32(_config.Comm.BaudRate);
                _port.Open();

                _problem = null;
                new Thread(ReadLines) { IsBackground = true, Name = "jaNET serial" }.Start();
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is InvalidOperationException || e is FormatException) {
                _log.Write($"obj [ SerialPortService.Open <{e.GetType().Name}> ] Exception Message: [ {e.Message} ]");
                _problem = e is FileNotFoundException ? ServiceProblems.SerialMissing(name) : ServiceProblems.SerialPort(e, name);
            }
        }
    }

    public void Close() {
        lock (_gate) {
            if (_port.IsOpen) _port.Close();
            _problem = null;
        }
    }

    public void Dispose() {
        Close();
        _port.Dispose();
        _lineArrived.Dispose();
    }

    public string Write(string message, SerialMessageKind kind, int timeoutMs = 1000) {
        if (!_port.IsOpen) return $"Serial port state: {_port.IsOpen}";

        try {
            lock (_writeGate) {
                _port.DiscardInBuffer();
                _port.DiscardOutBuffer();
                _lastLine = string.Empty;
                _lineArrived.Reset();

                if (kind == SerialMessageKind.Send)
                    _port.WriteLine(message);

                _lineArrived.Wait(timeoutMs);
            }
        }
        catch (Exception e) when (e is IOException || e is InvalidOperationException || e is TimeoutException || e is UnauthorizedAccessException) {
            // the port went away while we were talking to it: answer with what we have
        }

        return _lastLine;
    }

    void ReadLines() {
        while (_port.IsOpen) {
            try {
                string line = _port.ReadLine().Replace("\r", string.Empty).Replace("SIGKILL", "\n");
                if (line.Trim().Length > 0) {
                    _lastLine = line;
                    _lineArrived.Set();
                }
                RunEventNamed(line);
            }
            catch (Exception e) when (e is IOException || e is InvalidOperationException || e is TimeoutException || e is OperationCanceledException || e is UnauthorizedAccessException) {
                // closing the port interrupts the blocking read; the loop condition ends the thread
            }
        }
    }

    void RunEventNamed(string name) {
        if (name.Length == 0) return;

        string? action = _config.EventActions(name).FirstOrDefault();
        if (action == null) return;

        if (!TimeLimit.Run(() => _executor().Run(action), EventTimeoutMs))
            _log.Write($"obj [ SerialPortService ] The event '{name}' took longer than {EventTimeoutMs / 1000} seconds.");
    }
}
