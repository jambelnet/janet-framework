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

using jaNET.Hosting;
using jaNETProgram.Terminal;
using System;
using System.Linq;
using Xunit;

namespace jaNETFramework.Tests;

public class LineBufferTests
{
    [Fact]
    public void InsertsAtTheCursor() {
        var b = new LineBuffer();
        b.Set("judo ls");
        b.Home();
        b.Insert("x");
        Assert.Equal("xjudo ls", b.Text);
        Assert.Equal(1, b.Cursor);
    }

    [Fact]
    public void BackspaceAndDeleteStayInBounds() {
        var b = new LineBuffer();
        b.Backspace();
        b.Delete();
        Assert.Equal(string.Empty, b.Text);

        b.Set("abc", 1);
        b.Backspace();
        Assert.Equal("bc", b.Text);
        b.Delete();
        Assert.Equal("c", b.Text);
    }

    [Fact]
    public void WordMovementAndKills() {
        var b = new LineBuffer();
        b.Set("judo schedule ls");
        b.WordLeft();
        Assert.Equal(14, b.Cursor);
        b.WordLeft();
        Assert.Equal(5, b.Cursor);
        b.WordRight();
        Assert.Equal(13, b.Cursor);

        b.KillWordBefore();
        Assert.Equal("judo  ls", b.Text);
        b.KillToEnd();
        Assert.Equal("judo ", b.Text);
        b.KillToStart();
        Assert.Equal(string.Empty, b.Text);
    }
}

public class HistoryTests
{
    [Fact]
    public void BrowsesBackAndForthAndRestoresTheDraft() {
        var h = new History();
        h.Add("one");
        h.Add("two");

        Assert.Equal("two", h.Previous("draft"));
        Assert.Equal("one", h.Previous("ignored"));
        Assert.Null(h.Previous("ignored"));         // oldest reached
        Assert.Equal("two", h.Next());
        Assert.Equal("draft", h.Next());
        Assert.Null(h.Next());
    }

    [Fact]
    public void IgnoresBlankAndRepeatedLines() {
        var h = new History();
        h.Add("  ");
        h.Add("same");
        h.Add("same");
        Assert.Single(h.Entries);
    }

    [Fact]
    public void SuggestsTheRestOfTheLatestMatch() {
        var h = new History();
        h.Add("judo schedule ls");
        h.Add("judo server status");
        Assert.Equal("rver status", h.Suggest("judo se"));
        Assert.Equal(string.Empty, h.Suggest("zzz"));
        Assert.Equal(string.Empty, h.Suggest("judo server status"));
    }
}

public class CompleterTests
{
    static Completer Create() => new Completer(new JudoSyntax(
        () => new[] { "event", "inset", "schedule", "serial", "server", "sleep", "socket" },
        root => root switch {
            "schedule" => new[] { "add", "enable", "enable-all", "disable", "list", "ls" },
            "inset" => new[] { "add", "remove", "list" },
            _ => new string[0]
        },
        () => new[] { "%calendardate%", "%calendarday%", "%calendarmonth%", "%calendaryear%", "%user%" },
        () => new[] { "salute", "*salute", "weathertoday", "whoami" }));

    [Fact]
    public void CompletesJudoFromTheFirstLetters() {
        var result = Create().Complete("ju", 2);
        Assert.Equal(("judo ", 5), result.Value);
    }

    [Fact]
    public void CompletesRootAndSubCommands() {
        var c = Create();
        Assert.Equal(("judo schedule ", 14), c.Complete("judo sched", 10).Value);
        Assert.Equal(("judo schedule enable-all ", 25), Create().Complete("judo schedule enable-a", 22).Value);
    }

    [Fact]
    public void CompletesInstructionSetsAndFunctions() {
        Assert.Equal(("whoami ", 7), Create().Complete("who", 3).Value);
        var token = Create().Complete("say %cal", 8).Value;
        Assert.StartsWith("say %calendar", token.Text);
    }

    [Fact]
    public void SeveralMatchesAreListedAndTheLineStaysAsTyped() {
        var c = Create();

        var first = c.Complete("judo ser", 8).Value;           // serial and server: "ser" is all they have in common

        Assert.Equal(("judo ser", 8), first);                   // never guesses
        Assert.Equal(new[] { "serial", "server" }, c.TakeChoices());
        Assert.Null(c.TakeChoices());                           // shown once
    }

    [Fact]
    public void TabAgainStepsThroughTheChoices() {
        var c = Create();
        var listed = c.Complete("judo ser", 8).Value;
        c.TakeChoices();

        var second = c.Complete(listed.Text, listed.Cursor).Value;
        var third = c.Complete(second.Text, second.Cursor).Value;
        var fourth = c.Complete(third.Text, third.Cursor).Value;

        Assert.Equal("judo serial", second.Text);
        Assert.Equal("judo server", third.Text);
        Assert.Equal("judo serial", fourth.Text);               // wrapped around
        Assert.Null(c.TakeChoices());                           // the list is not shown again while stepping
        Assert.Equal("judo server", c.Complete(fourth.Text, fourth.Cursor, backwards: true).Value.Text);
    }

    [Fact]
    public void ShiftTabFirstListsAndThenStepsBackwards() {
        var c = Create();
        var listed = c.Complete("judo ser", 8, backwards: true).Value;
        Assert.Equal("judo ser", listed.Text);
        Assert.NotNull(c.TakeChoices());

        Assert.Equal("judo server", c.Complete(listed.Text, listed.Cursor, backwards: true).Value.Text);
    }

    [Theory]
    [InlineData("judo serv", "judo server ")]
    [InlineData("judo serve", "judo server ")]
    [InlineData("judo server", "judo server ")]
    public void ALongerStartCompletesTheOneThatMatches(string typed, string expected) {
        var c = Create();

        Assert.Equal((expected, expected.Length), c.Complete(typed, typed.Length).Value);
        Assert.Null(c.TakeChoices());
    }

    [Fact]
    public void MatchesThatAgreeOnMoreCompleteAsFarAsTheyAgree() {
        var c = new Completer(new JudoSyntax(
            () => new[] { "calendarday", "calendardate", "calendaryear" }, _ => new string[0], () => new string[0], () => new string[0]));

        Assert.Equal(("judo calendar", 13), c.Complete("judo cal", 8).Value);
        Assert.Null(c.TakeChoices());

        var again = c.Complete("judo calendar", 13).Value;                    // nothing more in common: now the choices are listed
        Assert.Equal("judo calendar", again.Text);
        Assert.Equal(3, c.TakeChoices().Count);
    }

    [Fact]
    public void ACursorInsideAWordReplacesTheWholeWord() {
        var c = Create();

        // "judo serv|er start": the rest of the word is not left behind as "judo server er start"
        Assert.Equal(("judo server start", 11), c.Complete("judo server start", 9).Value);
    }

    [Fact]
    public void ChoicesFillTheColumnsOfTheScreen() {
        string text = Completer.Columns(new[] { "aa", "bb", "cc", "dd", "ee" }, 14);   // cell = 4 wide, three columns

        Assert.Equal("aa  cc  ee" + Environment.NewLine + "bb  dd", text);
        Assert.Equal("one", Completer.Columns(new[] { "one" }, 80));
        Assert.Equal("a" + Environment.NewLine + "b", Completer.Columns(new[] { "a", "b" }, 1));     // a screen too narrow for two columns
    }
    [Fact]
    public void UnknownWordsStayAsTheyAre() {
        Assert.Null(Create().Complete("qqq", 3));
        Assert.Null(Create().Complete("judo schedule ls extra", 22));
    }
}

public class HighlighterTests
{
    [Theory]
    [InlineData("")]
    [InlineData("judo schedule ls")]
    [InlineData("say %user% good `morning all` now")]
    [InlineData("judo inset add x <lock>{ evalBool(1 == 1); a; b; }</lock>")]
    [InlineData("odd % chars \" unclosed")]
    [InlineData("   leading and trailing   ")]
    public void PiecesAlwaysAddUpToTheLine(string line) {
        Assert.Equal(line, string.Concat(Highlighter.Highlight(line).Select(p => p.Text)));
    }

    [Fact]
    public void ACommandThatDoesNotExistIsRedAndOneThatIsBeingTypedIsNot() {
        using var t = new TestHost();

        Assert.Equal(ConsoleColor.Red, Highlighter.Highlight("judo serviceX", t.Host.Syntax).First(p => p.Text == "serviceX").Color);
        Assert.Equal(ConsoleColor.DarkCyan, Highlighter.Highlight("judo server", t.Host.Syntax).First(p => p.Text == "server").Color);
        Assert.Equal(ConsoleColor.DarkCyan, Highlighter.Highlight("judo ser", t.Host.Syntax).First(p => p.Text == "ser").Color);   // on its way to serial
        Assert.Equal(ConsoleColor.DarkCyan, Highlighter.Highlight("judo serviceX").First(p => p.Text == "serviceX").Color);     // no syntax, no opinion
    }

    [Fact]
    public void ColoursKeywordsAndFunctions() {
        var pieces = Highlighter.Highlight("judo ls %user%");
        Assert.Equal(ConsoleColor.Cyan, pieces.First(p => p.Text == "judo").Color);
        Assert.Equal(ConsoleColor.DarkCyan, pieces.First(p => p.Text == "ls").Color);
        Assert.Equal(ConsoleColor.Magenta, pieces.First(p => p.Text == "%user%").Color);
    }
}

public class OutputFormatterTests
{
    [Fact]
    public void ScheduleListingBecomesATable() {
        var table = OutputFormatter.TryTable("judo schedule ls", "s1 | daily | 10:00 | yes | Active\r\ns2 | monday | 8:30 | no | Inactive\r\n");

        Assert.NotNull(table);
        Assert.Equal(2, table.Rows.Count);
        Assert.Equal("monday", table.Rows[1][1]);
    }

    [Fact]
    public void InstructionSetListingBecomesATable() {
        var table = OutputFormatter.TryTable("judo inset ls",
            "<InstructionSet id=\"a\" categ=\"System\" header=\"Head\">*a</InstructionSet>\r\n<InstructionSet id=\"*a\">Do it</InstructionSet>\r\n");

        Assert.NotNull(table);
        Assert.Equal(new[] { "a", "*a", "System", "Head" }, table.Rows[0]);
    }

    [Theory]
    [InlineData("judo schedule ls", "")]
    [InlineData("judo schedule names", "s1\r\ns2")]
    [InlineData("judo server settings", "localhost\r\n8080\r\nnone")]
    [InlineData("whoami", "You are, me.")]
    [InlineData("judo inset ls", "not xml at all")]
    public void OtherOutputStaysText(string command, string output) {
        Assert.Null(OutputFormatter.TryTable(command, output));
    }

    [Theory]
    [InlineData("foo, not found.", true)]
    [InlineData("Element added.", false)]
    public void RecognizesErrors(string output, bool error) {
        Assert.Equal(error, OutputFormatter.LooksLikeError(output));
    }
}

public class OptionsTests
{
    [Fact]
    public void SeparatesOptionsFromStartupCommands() {
        var o = Options.Parse(new[] { "--headless", "judo schedule ls", "%checkin%", "--plain", "--bogus" });

        Assert.True(o.Headless);
        Assert.True(o.Plain);
        Assert.Equal(new[] { "judo schedule ls", "%checkin%" }, o.Commands);
        Assert.Equal(new[] { "--bogus" }, o.Unknown);
    }

    [Fact]
    public void HelpAndVersion() {
        Assert.True(Options.Parse(new[] { "--help" }).Help);
        Assert.True(Options.Parse(new[] { "-h" }).Help);
        Assert.True(Options.Parse(new[] { "--version" }).Version);
    }
}

public class HelpFormatterTests
{
    [Fact]
    public void TheWholeHelpBecomesFourteenChaptersAndNothingIsLost() {
        using var t = new TestHost();
        string text = t.Run("judo help");

        HelpData data = HelpFormatter.TryParse("judo help", text);

        Assert.NotNull(data);
        Assert.Equal(14, data.Sections.Count);
        Assert.Equal(3, data.Notes.Count);
        int written = text.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries).Count(l => l.TrimStart().StartsWith("+ ", StringComparison.Ordinal));
        int shown = data.Sections.SelectMany(s => s.Topics).SelectMany(tp => tp.Commands).Sum(c => 1 + c.Aliases.Count);
        Assert.Equal(written, shown);                 // every variant is either a command or an alias of one
    }

    [Fact]
    public void VariantsThatDifferInTheVerbShareACommand() {
        using var t = new TestHost();
        HelpData data = HelpFormatter.TryParse("judo help inset", t.Run("judo help inset"));

        HelpCommand add = data.Sections.Single().Topics.First().Commands.First();
        Assert.Equal("judo inset add [ID] <lock>[Action]</lock>", add.Syntax);
        Assert.Equal(new[] { "new", "set", "setup" }, add.Aliases);
    }

    [Fact]
    public void ACommandWithAnArgumentWhereTheVerbWouldBeIsNotGrouped() {
        using var t = new TestHost();
        HelpData data = HelpFormatter.TryParse("judo help ping", t.Run("judo help ping"));

        var commands = data.Sections.Single().Topics.SelectMany(tp => tp.Commands).ToList();
        Assert.Equal(new[] { "judo ping [Host]", "judo ping [Host] [Timeout]" }, commands.Select(c => c.Syntax));
        Assert.All(commands, c => Assert.Empty(c.Aliases));
    }

    [Fact]
    public void AHelpTopicHasNoFootnotes() {
        using var t = new TestHost();

        HelpData data = HelpFormatter.TryParse("judo ? serial", t.Run("judo ? serial"));

        Assert.Equal("7. Serial Port", data.Sections.Single().Title);
        Assert.Empty(data.Notes);
    }

    [Fact]
    public void OtherCommandsAndOtherTextAreLeftAlone() {
        Assert.Null(HelpFormatter.TryParse("judo schedule ls", "1. Chapter\r\n     1.1 Topic\r\n         + judo x y"));
        Assert.Null(HelpFormatter.TryParse("judo help", "Something else entirely."));
        Assert.Null(HelpFormatter.TryParse("judo help", string.Empty));
        Assert.Null(HelpFormatter.TryParse("%user%", "x"));
    }
}
