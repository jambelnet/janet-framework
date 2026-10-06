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
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace jaNET.Services;

/// <summary>Plain HTTP GET requests (web services, public IP lookup, update check).</summary>
internal interface IHttpFetcher
{
    string Get(string url);

    /// <summary>Gives up with a <see cref="TaskCanceledException"/> after <paramref name="timeoutMs"/>.</summary>
    string Get(string url, int timeoutMs);

    Task<string> GetAsync(string url);

    /// <summary>True if the address answers with a success status.</summary>
    bool IsReachable(string url);
}

internal sealed class HttpFetcher : IHttpFetcher
{
    // HttpClient is thread safe and meant to be shared by the whole application
    internal static readonly HttpClient Client = new();

    public string Get(string url) => GetAsync(url).GetAwaiter().GetResult();

    public string Get(string url, int timeoutMs) {
        using var cts = new CancellationTokenSource(timeoutMs);
        return Client.GetStringAsync(url, cts.Token).GetAwaiter().GetResult();
    }

    public Task<string> GetAsync(string url) => Client.GetStringAsync(url);

    public bool IsReachable(string url) {
        try {
            using HttpResponseMessage response = Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
            return response.IsSuccessStatusCode;
        }
        catch (Exception e) when (e is HttpRequestException || e is TaskCanceledException || e is InvalidOperationException) {
            return false;
        }
    }
}

/// <summary>Whether the machine can reach the Internet (%inet%, mail notifications).</summary>
internal sealed class InternetConnection
{
    readonly IHttpFetcher _http;

    public InternetConnection(IHttpFetcher http) {
        _http = http;
    }

    public bool IsAvailable(string? host = null) =>
        _http.IsReachable(string.IsNullOrEmpty(host) ? "http://www.google.com" : host);
}
