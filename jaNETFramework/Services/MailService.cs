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
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using MimeKit;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Text;
using System.Text.RegularExpressions;

namespace jaNET.Services;

/// <summary>Sends mail through SMTP, reads the POP3 mailbox for commands and counts unread Gmail messages.</summary>
internal sealed class MailService
{
    readonly ISettingsStore _settings;
    readonly AppConfigStore _config;
    readonly Func<IInstructionExecutor> _executor;
    readonly Func<GmailSettings, GmailSnapshot> _readGmail;
    readonly object _gmailGate = new();
    GmailSettings? _cachedSettings;
    GmailSnapshot? _gmailSnapshot;
    DateTime _gmailChecked;

    public MailService(ISettingsStore settings, AppConfigStore config, InternetConnection internet, Func<IInstructionExecutor> executor,
                       Func<GmailSettings, GmailSnapshot>? readGmail = null) {
        _settings = settings;
        _config = config;
        _executor = executor;
        _readGmail = readGmail ?? ReadGmail;
    }

    public string SmtpError { get; private set; } = string.Empty;
    public string Pop3Error { get; private set; } = string.Empty;

    static string Password(string host, string password) =>
        host.EndsWith(".gmail.com", StringComparison.OrdinalIgnoreCase) ? password.Replace(" ", string.Empty) : password;

    static SecureSocketOptions SmtpTls(MailServerSettings settings) =>
        !settings.Ssl ? SecureSocketOptions.None : settings.Port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;

    static string Error(string service, Exception exception, string password) {
        string reason = exception is MailKit.Security.AuthenticationException
            ? "Sign-in was rejected. For Gmail, use a Google app password, not your Google account password."
            : exception is OperationCanceledException ? "The connection timed out."
            : exception.Message;
        if (!string.IsNullOrEmpty(password)) reason = reason.Replace(password, "[redacted]");
        return service + ": " + reason;
    }

    public bool Send(string from, string to, string subject, string body) {
        MailServerSettings? smtp = _settings.LoadSmtp();
        if (smtp == null) { SmtpError = "SMTP is not configured."; return false; }
        try {
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var client = new MailKit.Net.Smtp.SmtpClient { Timeout = 10000 };
            client.Connect(smtp.Host, smtp.Port, SmtpTls(smtp), limit.Token);
            if (!string.IsNullOrEmpty(smtp.Username))
                client.Authenticate(smtp.Username, Password(smtp.Host, smtp.Password), limit.Token);
            var message = new MimeMessage();
            message.From.AddRange(InternetAddressList.Parse(from));
            message.To.AddRange(InternetAddressList.Parse(to));
            message.Subject = subject;
            message.Body = new TextPart("plain") { Text = body };
            client.Send(message, limit.Token);
            client.Disconnect(true, limit.Token);
            SmtpError = string.Empty;
            return true;
        }
        catch (Exception e) { SmtpError = Error("SMTP", e, smtp.Password); return false; }
    }

    // Connection tests authenticate only: they never send mail or execute mailbox commands.
    public string TestSmtp() {
        MailServerSettings? smtp = _settings.LoadSmtp();
        if (smtp == null) return "SMTP is not configured.";
        try {
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var client = new MailKit.Net.Smtp.SmtpClient { Timeout = 10000 };
            client.Connect(smtp.Host, smtp.Port, SmtpTls(smtp), limit.Token);
            if (!string.IsNullOrEmpty(smtp.Username))
                client.Authenticate(smtp.Username, Password(smtp.Host, smtp.Password), limit.Token);
            client.Disconnect(true, limit.Token);
            return "SMTP connection and authentication succeeded.";
        }
        catch (Exception e) { return Error("SMTP", e, smtp.Password); }
    }

    public string TestPop3() {
        MailServerSettings? pop3 = _settings.LoadPop3();
        if (pop3 == null) return "POP3 is not configured.";
        try {
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var client = new MailKit.Net.Pop3.Pop3Client { Timeout = 10000 };
            client.Connect(pop3.Host, pop3.Port, !pop3.Ssl ? SecureSocketOptions.None
                : pop3.Port == 110 ? SecureSocketOptions.StartTls : SecureSocketOptions.SslOnConnect, limit.Token);
            client.Authenticate(pop3.Username, Password(pop3.Host, pop3.Password), limit.Token);
            int count = client.Count;
            client.Disconnect(true, limit.Token);
            return $"POP3 connection and authentication succeeded. Messages: {count}.";
        }
        catch (Exception e) { return Error("POP3", e, pop3.Password); }
    }

    public int Pop3Check() {
        MailServerSettings? pop3 = _settings.LoadPop3();
        if (pop3 == null) return 0;
        try {
            using var client = new Pop3Client();
            client.Connect(pop3.Host, pop3.Port, pop3.Username, Password(pop3.Host, pop3.Password), pop3.Ssl);
            string keyword = _config.MailKeyword;
            if (keyword.Length > 0) {
                foreach ((long number, long bytes) in client.List()) {
                    Pop3Message message = client.Retrieve(number, bytes);
                    Match command = Regex.Match(message.Text.Replace("\r\n", " "),
                        "<" + Regex.Escape(keyword) + ">(.*?)</" + Regex.Escape(keyword) + ">");
                    if (!command.Success) continue;
                    _executor().Run(command.Groups[1].Value);
                    client.Delete(number);
                }
            }
            int remaining = client.List().Count;
            client.Quit();
            Pop3Error = string.Empty;
            return remaining;
        }
        catch (Exception e) { Pop3Error = Error("POP3", e, pop3.Password); return 0; }
    }

    /// <summary>Unread INBOX messages, without marking them read. Errors are distinct from an empty inbox.</summary>
    public string GmailCheck(bool countOnly, bool force = false) {
        GmailSettings? gmail = _settings.LoadGmail();
        if (gmail == null) return countOnly ? "0" : "Gmail is not configured.";
        lock (_gmailGate) {
            if (force || _gmailSnapshot == null || gmail != _cachedSettings || DateTime.UtcNow - _gmailChecked >= TimeSpan.FromSeconds(15)) {
                try { _gmailSnapshot = _readGmail(gmail); }
                catch (Exception e) { _gmailSnapshot = new GmailSnapshot(0, Error("Gmail", e, gmail.Password), true); }
                _cachedSettings = gmail;
                _gmailChecked = DateTime.UtcNow;
            }
            if (_gmailSnapshot.Failed) return _gmailSnapshot.Headers;
            return countOnly ? _gmailSnapshot.Count.ToString(CultureInfo.InvariantCulture) : _gmailSnapshot.Headers;
        }
    }

    static GmailSnapshot ReadGmail(GmailSettings gmail) {
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var client = new ImapClient { Timeout = 10000 };
        client.Connect(gmail.ImapHost, gmail.ImapPort, !gmail.ImapSsl ? SecureSocketOptions.None
            : gmail.ImapPort == 143 ? SecureSocketOptions.StartTls : SecureSocketOptions.SslOnConnect, limit.Token);
        client.Authenticate(gmail.Username, Password(gmail.ImapHost, gmail.Password), limit.Token);
        client.Inbox.Open(FolderAccess.ReadOnly, limit.Token);
        var unread = client.Inbox.Search(SearchQuery.NotSeen, limit.Token);
        var output = new StringBuilder();
        if (unread.Count > 0) {
            // Fetch envelopes only; no body download and no read flag changes. Bound the reader output.
            var summaries = client.Inbox.Fetch(unread.Skip(Math.Max(0, unread.Count - 50)).ToList(), MessageSummaryItems.Envelope, limit.Token);
            int number = 0;
            foreach (var summary in summaries.Reverse()) {
                output.AppendLine($"Message {++number}");
                if (summary.Envelope == null) continue;
                output.AppendLine("Subject: " + summary.Envelope.Subject);
                output.AppendLine("From: " + summary.Envelope.From);
                output.AppendLine("Date: " + summary.Envelope.Date?.ToString("u"));
                output.AppendLine();
            }
        }
        output.Append("Total unread: " + unread.Count);
        client.Disconnect(true, limit.Token);
        return new GmailSnapshot(unread.Count, output.ToString());
    }
}

internal sealed record GmailSnapshot(int Count, string Headers, bool Failed = false);

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
        if (_presence.IsPresent || string.IsNullOrWhiteSpace(output) || !_settings.Exists(SettingsFiles.Smtp))
            return;

        MailHeaderSettings headers = _config.MailHeaders;
        if (!Infrastructure.TimeLimit.Run(() => _mail.Send(headers.From, headers.To, headers.Subject, output), TimeoutMs))
            _log.Write("obj [ MailNotifier ] Sending the notification mail took longer than 10 seconds and was abandoned.");
    }
}
