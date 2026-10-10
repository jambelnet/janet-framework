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
using jaNET.Infrastructure;
using System;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace jaNET.Services;

/// <summary>Sends text messages through the Clickatell HTTP API.</summary>
internal sealed class SmsClient
{
    const string BaseUrl = "http://api.clickatell.com/http/sendmsg";

    readonly ISettingsStore _settings;

    public SmsClient(ISettingsStore settings) {
        _settings = settings;
    }

    public string Send(string number, string message) {
        SmsSettings? sms = _settings.LoadSms();

        string query = string.Format("user={0}&password={1}&api_id={2}&to={3}&text={4}",
            Uri.EscapeDataString(sms?.Username ?? string.Empty),
            Uri.EscapeDataString(sms?.Password ?? string.Empty),
            Uri.EscapeDataString(sms?.Api ?? string.Empty),
            Uri.EscapeDataString(number),
            Uri.EscapeDataString(message));

        using var request = new HttpRequestMessage(HttpMethod.Get, BaseUrl + "?" + query);
        // some gateways refuse requests without a user agent
        request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/4.0 (compatible; MSIE 6.0; Windows NT 5.2; .NET CLR 1.0.3705;)");

        using HttpResponseMessage response = HttpFetcher.Client.SendAsync(request).GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();
        return response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
    }
}

/// <summary>Keeps a dynamic DNS name (no-ip) pointing at this connection.</summary>
internal sealed class DynDnsClient
{
    readonly IHttpFetcher _http;
    readonly ILog _log;

    public DynDnsClient(IHttpFetcher http, ILog log) {
        _http = http;
        _log = log;
    }

    /// <summary>The public IP address as seen from the Internet.</summary>
    public async Task<string> CheckIpAsync() {
        string page = await _http.GetAsync("https://checkip.dyndns.org").ConfigureAwait(false);
        return Regex.Match(page, @"\b\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}\b").Value;
    }

    public async Task UpdateAsync(DynDnsSettings settings) {
        try {
            string ip = await CheckIpAsync().ConfigureAwait(false);
            string uri = "https://dynupdate.no-ip.com/nic/update?hostname=" + Uri.EscapeDataString(settings.Hostname) +
                         "&myip=" + Uri.EscapeDataString(ip);

            using var handler = new HttpClientHandler { Credentials = new NetworkCredential(settings.Username, settings.Password) };
            using var client = new HttpClient(handler);
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "jaNETFramework/1.0 admin@localhost");
            using HttpResponseMessage response = await client.GetAsync(uri).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (!body.StartsWith("good ", StringComparison.OrdinalIgnoreCase) && !body.StartsWith("nochg ", StringComparison.OrdinalIgnoreCase))
                _log.Write($"obj [ DynDns.DynamicUpdate ] No-IP answered: [ {body.Trim()} ]");
        }
        catch (Exception e) {
            _log.Write($"obj [ DynDns.DynamicUpdate <Exception> ] Exception Message: [ {e.Message} ]");
        }
    }
}

/// <summary>"judo ping host": true if the host answers an ICMP echo in time.</summary>
internal sealed class Pinger
{
    readonly ILog _log;

    public Pinger(ILog log) {
        _log = log;
    }

    public bool Ping(string host, int timeoutMs = 1000) {
        try {
            if (!NetworkInterface.GetIsNetworkAvailable()) return false;

            using var sender = new Ping();
            byte[] payload = Encoding.ASCII.GetBytes("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
            return sender.Send(host, timeoutMs, payload).Status == IPStatus.Success;
        }
        catch (Exception e) {
            _log.Write($"obj [ NetInfo.SimplePing.Pinger <Exception> ] Exception Message: [ {e.Message} ]");
            return false;
        }
    }
}
