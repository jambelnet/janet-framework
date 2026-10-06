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
using System.Collections.Generic;

namespace jaNET.Commands;

/// <summary>judo smtp set|settings: the mail server that sends notifications</summary>
internal sealed class SmtpCommand : MailServerCommand
{
    public SmtpCommand(ISettingsStore settings) : base(settings, SettingsFiles.Smtp, "smtp") { }
}

/// <summary>judo pop3 set|settings: the mailbox that is read for commands</summary>
internal sealed class Pop3Command : MailServerCommand
{
    public Pop3Command(ISettingsStore settings) : base(settings, SettingsFiles.Pop3, "pop3") { }
}

internal abstract class MailServerCommand : JudoCommand
{
    protected MailServerCommand(ISettingsStore settings, string file, string name) {
        Names = new[] { name };

        On(i => settings.Save(file, $"{i.Args[3]}\r\n{i.Args[4]}\r\n{i.Args[5]}\r\n{i.Args[6]}\r\n{i.Args[7]}"), "add", "new", "setup", "set");
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
    public GmailCommand(ISettingsStore settings) {
        On(i => settings.Save(SettingsFiles.Gmail, $"{i.Args[3]}\r\n{i.Args[4]}"), "add", "new", "setup", "set");
        On(i => {
            GmailSettings? g = settings.LoadGmail();
            return $"{g?.Username}\r\n{g?.Password}";
        }, "settings");
    }

    public override IReadOnlyList<string> Names { get; } = new[] { "gmail" };
}

/// <summary>judo mail send [from] [to] [subject] [message]</summary>
internal sealed class MailCommand : JudoCommand
{
    public MailCommand(MailService mail) {
        On(i => mail.Send(i.Args[3], i.Args[4], i.Args[5], i.Args[6]) ? "Mail sent!" : "Mail could not be sent", "send");
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
