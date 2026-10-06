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
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;

namespace jaNET.Services;

internal sealed record Pop3Message(long Number, long Bytes, string Text);

internal sealed class Pop3Exception : Exception
{
    public Pop3Exception(string response) : base(response) { }
}

/// <summary>A minimal POP3 client (no TLS): list, retrieve and delete messages.</summary>
internal sealed class Pop3Client : IDisposable
{
    readonly TcpClient _tcp = new();
    StreamReader? _reader;
    StreamWriter? _writer;

    public void Connect(string server, int port, string username, string password) {
        _tcp.Connect(server, port);

        Stream stream = _tcp.GetStream();
        _reader = new StreamReader(stream, Encoding.ASCII);
        _writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };

        ExpectOk(ReadLine());
        Send("USER " + username);
        Send("PASS " + password);
    }

    /// <summary>Number and size of every message in the mailbox.</summary>
    public List<(long Number, long Bytes)> List() {
        Send("LIST");

        var result = new List<(long, long)>();
        string line;
        while ((line = ReadLine()) != ".") {
            string[] values = line.Split(' ');
            result.Add((long.Parse(values[0]), long.Parse(values[1])));
        }
        return result;
    }

    public Pop3Message Retrieve(long number, long bytes) {
        Send("RETR " + number);

        var text = new StringBuilder();
        string line;
        while ((line = ReadLine()) != ".")
            text.Append(line.StartsWith("..", StringComparison.Ordinal) ? line.Substring(1) : line).Append("\r\n");

        return new Pop3Message(number, bytes, text.ToString());
    }

    public void Delete(long number) => Send("DELE " + number);

    public void Quit() {
        try { Send("QUIT"); } catch (Exception e) when (e is IOException || e is Pop3Exception || e is InvalidOperationException) { }
    }

    void Send(string command) {
        if (_writer == null) throw new InvalidOperationException("Not connected.");

        _writer.WriteLine(command);
        ExpectOk(ReadLine());
    }

    string ReadLine() {
        if (_reader == null) throw new InvalidOperationException("Not connected.");
        return _reader.ReadLine() ?? throw new Pop3Exception("The server closed the connection.");
    }

    static void ExpectOk(string response) {
        if (!response.StartsWith("+OK", StringComparison.Ordinal))
            throw new Pop3Exception(response);
    }

    public void Dispose() => _tcp.Dispose();
}
