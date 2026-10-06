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
using jaNET.Scripting;
using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Mail;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace jaNET.Services;

/// <summary>Sends mail through SMTP, reads the POP3 mailbox for commands and counts unread Gmail messages.</summary>
internal sealed class MailService
{
    readonly ISettingsStore _settings;
    readonly AppConfigStore _config;
    readonly InternetConnection _internet;
    readonly Func<IInstructionExecutor> _executor;

    public MailService(ISettingsStore settings, AppConfigStore config, InternetConnection internet, Func<IInstructionExecutor> executor) {
        _settings = settings;
        _config = config;
        _internet = internet;
        _executor = executor;
    }

    public bool Send(string from, string to, string subject, string body) {
        if (!_internet.IsAvailable()) return false;

        try {
            MailServerSettings? smtp = _settings.LoadSmtp();
            if (smtp == null) return false;       // not configured

            using var mail = new MailMessage(from, to, subject, body);
            using var client = new SmtpClient(smtp.Host) {
                Port = smtp.Port,
                Credentials = new NetworkCredential(smtp.Username, smtp.Password),
                EnableSsl = smtp.Ssl
            };
            client.Send(mail);
            return true;
        }
        catch (Exception) {
            return false;
        }
    }

    /// <summary>
    /// Runs the commands that arrived as &lt;keyword&gt;command&lt;/keyword&gt; in the subject or body of a message
    /// (and deletes those messages) and returns the number of messages that are left.
    /// </summary>
    public int Pop3Check() {
        try {
            MailServerSettings? pop3 = _settings.LoadPop3();
            if (pop3 == null) return 0;

            using var client = new Pop3Client();
            client.Connect(pop3.Host, pop3.Port, pop3.Username, pop3.Password);

            string keyword = _config.MailKeyword;
            if (keyword.Length > 0) {
                foreach ((long number, long bytes) in client.List()) {
                    Pop3Message message = client.Retrieve(number, bytes);
                    if (!message.Text.Contains("<" + keyword + ">")) continue;

                    Match command = Regex.Match(message.Text.Replace("\r\n", " "), "(<" + keyword + ">)(.*?)(?=</" + keyword + ">)");
                    _executor().Run(command.ToString().ToLowerInvariant().Replace("<" + keyword + ">", string.Empty));
                    client.Delete(number);
                }
            }

            int remaining = client.List().Count;
            client.Quit();
            return remaining;
        }
        catch (Exception) {
            return 0;
        }
    }

    /// <summary>The number of unread Gmail messages, or a listing of them (sender, subject, date) when <paramref name="countOnly"/> is false.</summary>
    public string GmailCheck(bool countOnly) {
        try {
            var feed = new XmlDocument();
            feed.LoadXml(FetchGmailFeed());
            XmlNodeList entries = feed.GetElementsByTagName("entry");

            if (countOnly) return entries.Count.ToString();

            var output = new StringBuilder();
            for (int i = 0; i < entries.Count; ++i) {
                var entry = (XmlElement)entries[i]!;
                output.AppendFormat("Message {0}\r\n", i + 1);
                output.AppendFormat("Subject: {0}\r\n", entry["title"]!.InnerText);
                output.AppendFormat("From: {0} <{1}>\r\n", entry["author"]!["name"]!.InnerText, entry["author"]!["email"]!.InnerText);
                output.AppendFormat("Date: {0}\r\n", DateTime.Parse(entry["modified"]!.InnerText));
            }
            output.Append("Total: " + entries.Count);
            return output.ToString();
        }
        catch (Exception) {
            return "0";
        }
    }

    string FetchGmailFeed() {
        GmailSettings gmail = _settings.LoadGmail() ?? throw new InvalidOperationException("Gmail is not configured.");
        string credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes(gmail.Username + ":" + gmail.Password));

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://mail.google.com/mail/feed/atom");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);

        using HttpResponseMessage response = HttpFetcher.Client.SendAsync(request).GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();
        return response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
    }
}

/// <summary>Mails the output of an instruction to the configured address while nobody is at home.</summary>
internal sealed class MailNotifier
{
    const int TimeoutMs = 10000;

    readonly MailService _mail;
    readonly AppConfigStore _config;
    readonly ISettingsStore _settings;
    readonly UserPresence _presence;
    readonly InternetConnection _internet;
    readonly Infrastructure.ILog _log;

    public MailNotifier(MailService mail, AppConfigStore config, ISettingsStore settings, UserPresence presence, InternetConnection internet, Infrastructure.ILog log) {
        _mail = mail;
        _config = config;
        _settings = settings;
        _presence = presence;
        _internet = internet;
        _log = log;
    }

    public void Notify(string output) {
        // the cheap local checks first: the Internet probe is a network request
        if (_presence.IsPresent || string.IsNullOrWhiteSpace(output) || !_settings.Exists(SettingsFiles.Smtp) || !_internet.IsAvailable())
            return;

        MailHeaderSettings headers = _config.MailHeaders;
        if (!Infrastructure.TimeLimit.Run(() => _mail.Send(headers.From, headers.To, headers.Subject, output), TimeoutMs))
            _log.Write("obj [ MailNotifier ] Sending the notification mail took longer than 10 seconds and was abandoned.");
    }
}
