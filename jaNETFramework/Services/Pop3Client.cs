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

using MailKit.Security;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace jaNET.Services;

internal sealed record Pop3Message(long Number, long Bytes, string Text);

/// <summary>POP3 transport with certificate validation, implicit TLS and STARTTLS.</summary>
internal sealed class Pop3Client : IDisposable
{
    readonly MailKit.Net.Pop3.Pop3Client _client = new() { Timeout = 10000 };
    readonly CancellationTokenSource _limit = new(TimeSpan.FromSeconds(30));
    readonly HashSet<long> _deleted = new();
    IList<int>? _sizes;

    public void Connect(string server, int port, string username, string password, bool ssl = false) {
        var tls = !ssl ? SecureSocketOptions.None
            : port == 110 ? SecureSocketOptions.StartTls : SecureSocketOptions.SslOnConnect;
        _client.Connect(server, port, tls, _limit.Token);
        _client.Authenticate(username, password, _limit.Token);
    }

    public List<(long Number, long Bytes)> List() {
        _sizes ??= _client.GetMessageSizes(_limit.Token);
        return _sizes.Select((size, index) => (Number: (long)index + 1, Bytes: (long)size))
            .Where(message => !_deleted.Contains(message.Number)).ToList();
    }

    public Pop3Message Retrieve(long number, long bytes) {
        var message = _client.GetMessage(checked((int)number - 1), _limit.Token);
        return new Pop3Message(number, bytes, message.Subject + "\r\n" + (message.TextBody ?? message.HtmlBody ?? string.Empty));
    }

    public void Delete(long number) {
        _client.DeleteMessage(checked((int)number - 1), _limit.Token);
        _deleted.Add(number);
    }

    public void Quit() => _client.Disconnect(true, _limit.Token);

    public void Dispose() {
        _client.Dispose();
        _limit.Dispose();
    }
}
