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

using System;
using System.IO;
using System.IO.Ports;

namespace jaNET.Servers;

/// <summary>The hardware boundary used to exercise the serial reader and event pipeline without a board.</summary>
internal interface ISerialConnection : IDisposable
{
    bool IsOpen { get; }
    void Open();
    void Close();
    string ReadLine();
    void WriteLine(string message);
}

internal sealed class SerialConnection : ISerialConnection
{
    readonly SerialPort _port;

    public SerialConnection(string name, int baud) {
        if (OperatingSystem.IsWindows() ? !ServiceProblems.IsPortNameOfThisSystem(name) : !File.Exists(name))
            throw new FileNotFoundException("Serial device not found.", name);
        _port = new SerialPort(name, baud) { NewLine = "\n", ReadTimeout = 250, WriteTimeout = 1000 };
    }

    public bool IsOpen => _port.IsOpen;
    public void Open() => _port.Open();
    public void Close() => _port.Close();
    public string ReadLine() => _port.ReadLine();
    public void WriteLine(string message) => _port.WriteLine(message);
    public void Dispose() => _port.Dispose();
}
