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
using System.Linq;
using Xunit;

namespace jaNETFramework.Tests;

/// <summary>The "judo ..." commands through the whole stack, with the outside world faked.</summary>
public class JudoCommandTests
{
    [Fact]
    public void InstructionSetsCanBeAddedListedAndRemoved() {
        using var t = new TestHost();

        Assert.Equal("Element added.", t.Run("judo inset add hello <lock>Hello %user%</lock>"));
        Assert.Equal("Hello " + Environment.UserName, t.Run("hello"));
        Assert.Contains("<InstructionSet id=\"*hello\">Hello %user%</InstructionSet>", t.Run("judo inset ls"));

        Assert.Equal("Element removed.", t.Run("judo inset remove hello"));
        Assert.Equal("hello, not found.", t.Run("hello"));
    }

    [Fact]
    public void InstructionSetsCanBeShownOnTheDashboard() {
        using var t = new TestHost();

        t.Run("judo inset add x <lock>a</lock> Cat Head Short Long /t.png ref");

        Assert.Contains("<InstructionSet id=\"x\" img=\"/t.png\" descr=\"Long\" shortdescr=\"Short\" header=\"Head\" categ=\"Cat\" ref=\"ref\">*x</InstructionSet>",
            t.Run("judo inset list"));
    }

    [Fact]
    public void MissingArgumentsReportTheIndexError() {
        using var t = new TestHost();

        Assert.StartsWith("Index was out of range", t.Run("judo inset add"));
        Assert.StartsWith("Index was out of range", t.Run("judo"));
        Assert.Equal(string.Empty, t.Run("judo unknownroot"));
    }

    [Fact]
    public void EventsAreAddedListedAndTriggeredByTheirFunction() {
        using var t = new TestHost();
        t.Run("judo inset add yes <lock>YES</lock>");
        t.Settle();

        Assert.Equal("Element added.", t.Run("judo event add ev1 <lock>yes</lock>"));
        Assert.Contains("<event id=\"ev1\">yes</event>", t.Run("judo event ls"));

        t.Run("%~>ev1%");
        Assert.True(System.Threading.SpinWait.SpinUntil(() => t.Speaker.Spoken.Any(s => s.Trim() == "YES"), 2000));

        t.Run("judo event del ev1");
        Assert.DoesNotContain("ev1", t.Run("judo event ls"));
    }

    [Fact]
    public void UnknownEventsAreIgnored() {
        using var t = new TestHost();

        Assert.Equal(string.Empty, t.Run("%~>nothing%"));
    }

    [Fact]
    public void SchedulesCanBeAddedListedSwitchedAndRemoved() {
        using var t = new TestHost();

        Assert.Equal("Schedule s1 added", t.Run("judo schedule add s1 daily 10:00 yes"));
        Assert.Equal("Schedule s2 added", t.Run("judo schedule add s2 Monday 8:30 no"));
        Assert.Equal("s1 | daily | 10:00 | yes | Active\r\ns2 | monday | 8:30 | no | Active", t.Run("judo schedule ls").Trim());
        Assert.Equal("s2 | monday | 8:30 | no | Active", t.Run("judo schedule details s2").Trim());

        Assert.Equal("Scheduler updated [s1:Disable]", t.Run("judo schedule disable s1"));
        Assert.Equal("s1", t.Run("judo schedule inactive").Trim());
        Assert.Equal("s2", t.Run("judo schedule active").Trim());
        Assert.Equal("s1 | daily | 10:00 | yes | Inactive", t.Run("judo schedule inactive-details").Trim());
        Assert.Equal("Scheduler updated [s1:Enable]", t.Run("judo schedule enable s1"));

        Assert.Equal("Scheduler updated [:DisableAll]", t.Run("judo schedule disable-all"));
        Assert.Equal(string.Empty, t.Run("judo schedule active").Trim());
        Assert.Equal("Scheduler updated [:EnableAll]", t.Run("judo schedule enable-all"));
        Assert.Equal("s1\r\ns2", t.Run("judo schedule names").Trim());

        Assert.Equal("Scheduler updated [s2:Remove]", t.Run("judo schedule rm s2"));
        Assert.Equal("s1", t.Run("judo schedule names").Trim());
        Assert.Equal("Scheduler updated [:RemoveAll]", t.Run("judo schedule clear").Trim());
        Assert.Equal(string.Empty, t.Run("judo schedule ls").Trim());
    }

    [Fact]
    public void EveryAliasOfScheduleAddWorks() {
        // "new", "set" and "setup" used to mix up their arguments
        using var t = new TestHost();

        foreach (string alias in new[] { "add", "new", "set", "setup" })
            Assert.Equal("Schedule s_" + alias + " added", t.Run($"judo schedule {alias} s_{alias} daily 10:00 yes"));

        Assert.Equal(4, t.Run("judo schedule names").Trim().Split("\r\n").Length);
    }

    [Fact]
    public void RepeatingSchedulesTakeTheirIntervalFromTheTime() {
        using var t = new TestHost();

        Assert.Equal("Schedule r1 added", t.Run("judo schedule add r1 repeat 60000 yes"));
        Assert.Equal("r1 | repeat | 60000 | yes | Active", t.Run("judo schedule ls").Trim());
    }

    [Fact]
    public void MailServerSettingsAreSavedAndShown() {
        using var t = new TestHost();
        Assert.Equal("0\r\nFalse", t.Run("judo smtp settings").Trim());      // nothing saved yet

        Assert.Equal("Settings saved.", t.Run("judo smtp set smtp.example.org user1 secret1 587 true"));
        Assert.Equal("smtp.example.org\r\nuser1\r\nsecret1\r\n587\r\nTrue", t.Run("judo smtp settings").Trim());

        t.Run("judo pop3 set pop.example.org user2 secret2 995 false");
        Assert.Equal("pop.example.org\r\nuser2\r\nsecret2\r\n995\r\nFalse", t.Run("judo pop3 settings"));

        t.Run("judo gmail set me@example.org secret3");
        Assert.Equal("me@example.org\r\nsecret3\r\nhttps://mail.google.com/mail/feed/atom\r\nsmtp.gmail.com\r\n587\r\nTrue\r\npop.gmail.com\r\n995\r\nTrue\r\nimap.gmail.com\r\n993\r\nTrue", t.Run("judo gmail settings"));
        Assert.Equal("smtp.gmail.com\r\nme@example.org\r\nsecret3\r\n587\r\nTrue", t.Run("judo smtp settings"));
        Assert.Equal("pop.gmail.com\r\nme@example.org\r\nsecret3\r\n995\r\nTrue", t.Run("judo pop3 settings"));

        t.Run("judo sms set 1234 smsuser smspass");
        Assert.Equal("1234\r\nsmsuser\r\nsmspass", t.Run("judo sms settings"));

        t.Run("judo noip set host.no-ip.org dnsuser dnspass");
        Assert.Equal("host.no-ip.org\r\ndnsuser\r\ndnspass", t.Run("judo noip settings"));
    }

    [Fact]
    public void MailHeadersCanBeChanged() {
        using var t = new TestHost();
        Assert.Equal("noreply@xxx.org\r\nme@yyy.org\r\nAlert from Jubito", t.Run("judo mailheaders settings"));

        Assert.Equal("Element added.", t.Run("judo mailheaders set `a@x.org` `b@y.org` `Subj ect`"));

        Assert.Equal("a@x.org\r\nb@y.org\r\nSubj ect", t.Run("judo mailheaders settings"));
    }

    [Fact]
    public void SendingMailWithoutConfigurationExplainsWhy() {
        using var t = new TestHost();

        Assert.Equal("Mail could not be sent. SMTP is not configured.", t.Run("judo mail send a@x.org b@y.org Subject Message"));
    }

    [Fact]
    public void WebServerSettingsAndState() {
        using var t = new TestHost();

        Assert.Equal("localhost\r\n8080\r\nnone", t.Run("judo server settings"));
        Assert.Equal("Web server state: True", t.Run("judo server status"));
        Assert.Equal("Web server state: False", t.Run("judo server stop"));
        Assert.Equal("Web server state: True", t.Run("judo server start"));
        Assert.Equal("Element added.", t.Run("judo server setup 0.0.0.0 9090 basic"));
        Assert.Equal("0.0.0.0\r\n9090\r\nbasic", t.Run("judo server settings"));
        Assert.Equal("Settings saved.", t.Run("judo server login bob pw1"));
    }

    [Fact]
    public void SocketServerSettingsAndState() {
        using var t = new TestHost();

        Assert.Equal("localhost\r\n5744\r\nlocalhost; 192.168.1.1", t.Run("judo socket settings"));
        Assert.Equal("Socket state: True", t.Run("judo socket state"));
        Assert.Equal("Socket state: False", t.Run("judo socket off"));
        Assert.Equal("Socket state: True", t.Run("judo socket on"));
        Assert.Equal("Element added.", t.Run("judo socket trust <lock>10.0.0.1; 10.0.0.2</lock>"));
        Assert.Equal("10.0.0.1; 10.0.0.2", t.Run("judo trusted settings"));
        Assert.Equal("Element added.", t.Run("judo socket set 127.0.0.1 6000"));
        Assert.Equal("127.0.0.1\r\n6000\r\n10.0.0.1; 10.0.0.2", t.Run("judo socket settings"));
    }

    [Fact]
    public void SerialPortCommands() {
        using var t = new TestHost();

        Assert.Equal("/dev/ttyACM0\r\n9600", t.Run("judo serial settings"));
        Assert.Equal("Serial port state: True", t.Run("judo serial state"));
        Assert.Equal("42", t.Run("judo serial send dhttemp"));
        Assert.Equal("42", t.Run("judo serial send humid 250"));
        Assert.Equal("42", t.Run("judo serial listen 500"));
        Assert.Equal(new[] { "Send:dhttemp:1000", "Send:humid:250", "Listen::500" }, t.Serial.Sent);

        Assert.Equal("Serial port state: False", t.Run("judo serial close"));
        Assert.Equal("Serial port state: True", t.Run("judo serial open COM3"));
        Assert.Equal("COM3", t.Serial.OpenedWith);
        Assert.Equal("Element added.", t.Run("judo serial set COM9 19200"));
        Assert.Equal("COM9\r\n19200", t.Run("judo serial settings"));
    }

    [Fact]
    public void JsonAndXmlWebServices() {
        using var t = new TestHost();
        t.Http.Pages["http://x/y"] = "{\"a\":{\"b\":\"deep\",\"list\":[10,20]}}";
        t.Http.Pages["http://x/feed"] = "<r xmlns:p=\"urn:p\"><item a=\"1\">one</item><item a=\"2\">two</item><p:q>ns</p:q></r>";

        Assert.Equal("deep", t.Run("judo json get http://x/y a/b"));
        Assert.Equal("20", t.Run("judo json get http://x/y a/list/1"));

        Assert.Equal("one", t.Run("judo xml get http://x/feed //item"));
        Assert.Equal("two", t.Run("judo xml get http://x/feed //item 1"));
        Assert.Equal("ns", t.Run("judo xml get http://x/feed p=urn:p //p:q/"));       // namespace, node, no attribute
        Assert.Equal("2", t.Run("judo xml get http://x/feed p=urn:p //item/a 1"));
    }

    [Fact]
    public void WebServicesCanBeSavedAsInstructionSets() {
        using var t = new TestHost();
        t.Http.Pages["http://x/y"] = "{\"v\":\"stored\"}";

        Assert.Equal("Element added.", t.Run("judo json add weather <lock>http://x/y</lock> v"));

        Assert.Equal("stored", t.Run("weather"));
    }

    [Fact]
    public void HttpGetReturnsThePage() {
        using var t = new TestHost();
        t.Http.Pages["http://x/p"] = "page";

        Assert.Equal("page", t.Run("judo http get http://x/p"));
    }

    [Fact]
    public void WeatherAddressCanBeChanged() {
        using var t = new TestHost();

        Assert.Equal("Element added.", t.Run("judo weather set <lock>http://w/api?q=1&u=m</lock>"));
        Assert.Equal("http://w/api?q=1&u=m", t.Run("judo weather settings"));
    }

    [Fact]
    public void HelpHasTopics() {
        using var t = new TestHost();

        Assert.StartsWith("1. Instruction Sets & Events", t.Run("judo help"));
        Assert.StartsWith("4. Scheduler", t.Run("judo help schedule"));
        Assert.StartsWith("1. Instruction Sets & Events", t.Run("judo ? event"));
    }

    [Fact]
    public void SleepWaits() {
        using var t = new TestHost();
        var watch = System.Diagnostics.Stopwatch.StartNew();

        Assert.Equal(string.Empty, t.Run("judo sleep 120"));

        Assert.True(watch.ElapsedMilliseconds >= 100);
    }

    [Fact]
    public void TheSyntaxListsWhatTheHandlersDo() {
        using var t = new TestHost();
        t.Run("judo inset add mine <lock>x</lock>");

        Assert.Contains("schedule", t.Host.Syntax.Roots);
        Assert.Contains("serial", t.Host.Syntax.Roots);
        Assert.Contains("enable-all", t.Host.Syntax.SubCommands("schedule"));
        Assert.Contains("settings", t.Host.Syntax.SubCommands("SMTP"));
        Assert.Empty(t.Host.Syntax.SubCommands("sleep"));
        Assert.Empty(t.Host.Syntax.SubCommands("nope"));
        Assert.Contains("%user%", t.Host.Syntax.Functions);
        Assert.Contains("%exit%", t.Host.Syntax.Functions);
        Assert.Contains("mine", t.Host.Syntax.InstructionSets);
        Assert.DoesNotContain("*mine", t.Host.Syntax.InstructionSets);
    }
}
