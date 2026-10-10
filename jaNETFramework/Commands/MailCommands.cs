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
using jaNET.Services;
using System;
using System.Collections.Generic;

namespace jaNET.Commands;

/// <summary>judo smtp set|settings: the mail server that sends notifications</summary>
internal sealed class SmtpCommand : MailServerCommand
{
    public SmtpCommand(ISettingsStore settings, MailService? mail = null) : base(settings, SettingsFiles.Smtp, "smtp") {
        if (mail != null) On(i => mail.TestSmtp(), "test");
    }
}

/// <summary>judo pop3 set|settings: the mailbox that is read for commands</summary>
internal sealed class Pop3Command : MailServerCommand
{
    public Pop3Command(ISettingsStore settings, MailService? mail = null) : base(settings, SettingsFiles.Pop3, "pop3") {
        if (mail != null) On(i => mail.TestPop3(), "test");
    }
}

internal abstract class MailServerCommand : JudoCommand
{
    protected MailServerCommand(ISettingsStore settings, string file, string name) {
        Names = new[] { name };

        On(i => {
            if (i.Count != 8) return "Provide host, username, password, port and TLS (true/false).";
            if (!int.TryParse(i.Args[6], out int port) || port is < 1 or > 65535) return "Port must be between 1 and 65535.";
            if (!bool.TryParse(i.Args[7], out _)) return "TLS must be true or false.";
            return settings.Save(file, $"{i.Args[3]}\r\n{i.Args[4]}\r\n{i.Args[5]}\r\n{port}\r\n{i.Args[7]}");
        }, "add", "new", "setup", "set");
        On(i => {
            // blank host, user and password, port 0 and False until something was saved
            MailServerSettings? s = file == SettingsFiles.Smtp ? settings.LoadSmtp() : settings.LoadPop3();
            return $"{s?.Host}\r\n{s?.Username}\r\n{s?.Password}\r\n{s?.Port ?? 0}\r\n{s?.Ssl ?? false}";
        }, "settings");
    }

    public override IReadOnlyList<string> Names { get; }
}

/// <summary>judo gmail set|settings</summary>
internal sealed class GmailCommand : JudoCommand
{
    public GmailCommand(ISettingsStore settings, MailService? mail = null) {
        if (mail != null) On(i => mail.GmailCheck(false, force: true), "test");
        On(i => Save(settings, i), "add", "new", "setup", "set");
        On(i => {
            GmailSettings? g = settings.LoadGmail();
            return g == null
                ? $"\r\n\r\n{GmailDefaults.FeedUrl}\r\n{GmailDefaults.SmtpHost}\r\n{GmailDefaults.SmtpPort}\r\n{GmailDefaults.SmtpSsl}\r\n{GmailDefaults.Pop3Host}\r\n{GmailDefaults.Pop3Port}\r\n{GmailDefaults.Pop3Ssl}\r\nimap.gmail.com\r\n993\r\nTrue"
                : $"{g.Username}\r\n{g.Password}\r\n{g.FeedUrl}\r\n{g.SmtpHost}\r\n{g.SmtpPort}\r\n{g.SmtpSsl}\r\n{g.Pop3Host}\r\n{g.Pop3Port}\r\n{g.Pop3Ssl}\r\n{g.ImapHost}\r\n{g.ImapPort}\r\n{g.ImapSsl}";
        }, "settings");
    }

    static string Save(ISettingsStore settings, JudoInvocation i) {
        if (i.Count < 5) return "Provide the Gmail address and a Google app password.";
        string username = i.Args[3];
        string password = i.Args[4];
        string feed = i.Count > 5 ? i.Args[5] : GmailDefaults.FeedUrl;
        string smtpHost = i.Count > 6 ? i.Args[6] : GmailDefaults.SmtpHost;
        string smtpPort = i.Count > 7 ? i.Args[7] : GmailDefaults.SmtpPort.ToString();
        string smtpSsl = i.Count > 8 ? i.Args[8] : GmailDefaults.SmtpSsl.ToString();
        string pop3Host = i.Count > 9 ? i.Args[9] : GmailDefaults.Pop3Host;
        string pop3Port = i.Count > 10 ? i.Args[10] : GmailDefaults.Pop3Port.ToString();
        string pop3Ssl = i.Count > 11 ? i.Args[11] : GmailDefaults.Pop3Ssl.ToString();

        string imapHost = i.Count > 12 ? i.Args[12] : "imap.gmail.com";
        string imapPort = i.Count > 13 ? i.Args[13] : "993";
        string imapSsl = i.Count > 14 ? i.Args[14] : "True";
        foreach (string value in new[] { smtpPort, pop3Port, imapPort })
            if (!int.TryParse(value, out int port) || port is < 1 or > 65535) return "Port must be between 1 and 65535.";
        foreach (string value in new[] { smtpSsl, pop3Ssl, imapSsl })
            if (!bool.TryParse(value, out _)) return "TLS must be true or false.";

        settings.Save(SettingsFiles.Smtp, $"{smtpHost}\r\n{username}\r\n{password}\r\n{smtpPort}\r\n{smtpSsl}");
        settings.Save(SettingsFiles.Pop3, $"{pop3Host}\r\n{username}\r\n{password}\r\n{pop3Port}\r\n{pop3Ssl}");
        return settings.Save(SettingsFiles.Gmail, $"{username}\r\n{password}\r\n{feed}\r\n{smtpHost}\r\n{smtpPort}\r\n{smtpSsl}\r\n{pop3Host}\r\n{pop3Port}\r\n{pop3Ssl}\r\n{imapHost}\r\n{imapPort}\r\n{imapSsl}");
    }

    public override IReadOnlyList<string> Names { get; } = new[] { "gmail" };
}

/// <summary>judo mail send [from] [to] [subject] [message]</summary>
internal sealed class MailCommand : JudoCommand
{
    public MailCommand(MailService mail) {
        On(i => mail.Send(i.Args[3], i.Args[4], i.Args[5], i.Args[6]) ? "Mail sent!" : "Mail could not be sent. " + mail.SmtpError, "send");
    }

    public override IReadOnlyList<string> Names { get; } = new[] { "mail" };
}

/// <summary>judo sms set|settings|send</summary>
internal sealed class SmsCommand : JudoCommand
{
    public SmsCommand(ISettingsStore settings, SmsClient sms) {
        On(i => settings.Save(SettingsFiles.Sms, $"{i.Args[3]}\r\n{i.Args[4]}\r\n{i.Args[5]}"), "add", "new", "setup", "set");
        On(i => {
            SmsSettings? s = settings.LoadSms();
            return $"{s?.Api}\r\n{s?.Username}\r\n{s?.Password}";
        }, "settings");
        On(i => sms.Send(i.Args[3], i.Args[4]), "send");
    }

    public override IReadOnlyList<string> Names { get; } = new[] { "sms" };
}
