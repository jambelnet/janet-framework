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
using Spectre.Console;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace jaNETProgram.Terminal;

/// <summary>
/// The console for people at a terminal: banner, service overview, coloured prompt with history and completion,
/// tables for list commands and a spinner for slow commands. Everything it can do is also available through
/// <see cref="PlainUi"/>; the commands and their output are the same.
/// </summary>
sealed class FancyUi : IUi
{
    static readonly TimeSpan SpinnerDelay = TimeSpan.FromMilliseconds(350);

    readonly JanetHost _host;
    readonly LineEditor _editor;
    readonly PlainUi _fallback;
    readonly NoticeTracker _notices;
    bool _degraded;

    public FancyUi(JanetHost host) {
        _host = host;
        _editor = new LineEditor(new History(), new Completer(host.Syntax), host.Syntax);
        _fallback = new PlainUi(host);
        _notices = new NoticeTracker(host);
    }

    public void ShowBanner() {
        TrySetTitle();

        string copyright = string.Empty;
        // the copyright text includes a (time limited) check whether a newer version exists
        AnsiConsole.Status().Spinner(Spinner.Known.Dots).Start("Checking for updates...", _ => copyright = _host.Execute("%copyright%"));

        int width = AnsiConsole.Profile.Width;
        if (width >= 44)
            AnsiConsole.Write(new FigletText("jaNET").Color(Color.Aqua));
        else
            AnsiConsole.Write(new Rule("[aqua bold]jaNET[/]").LeftJustified());

        string[] lines = OutputFormatter.Lines(copyright);
        for (int i = 0; i < lines.Length; i++) {
            if (i == 0) AnsiConsole.MarkupLine("[bold]" + Markup.Escape(lines[i]) + "[/]");
            else if (i == 1) AnsiConsole.MarkupLine("[grey]" + Markup.Escape(lines[i]) + "[/]");
            else AnsiConsole.MarkupLine("[yellow]" + Markup.Escape(lines[i]) + "[/]");
        }
        AnsiConsole.MarkupLine("[grey]Data folder: " + Markup.Escape(_host.DataDirectory) + "[/]");
        AnsiConsole.MarkupLine("[grey]Tab completes, Up/Down browse the history, Ctrl+L clears the screen, Ctrl+D quits. Try [/][aqua]judo help[/][grey].[/]");
        AnsiConsole.WriteLine();
    }

    public void ShowServices() {
        // services start asynchronously: give the web server a moment
        for (int i = 0; i < 15 && !Query("judo server status").Contains("True") && !_notices.Has("Web server"); i++)
            Thread.Sleep(100);

        string[] web = OutputFormatter.Lines(Query("judo server settings"));
        string[] socket = OutputFormatter.Lines(Query("judo socket settings"));
        string[] serial = OutputFormatter.Lines(Query("judo serial settings"));
        string[] mqtt = OutputFormatter.Lines(Query("judo mqtt settings"));
        int schedules = OutputFormatter.Lines(Query("judo schedule names")).Length;

        var table = new Table().Border(TableBorder.Rounded).BorderColor(Color.Grey).AddColumns("Service", "Address", "State");
        table.AddRow("Web server", Escape(web, 0, 1, "http://{0}:{1}/www/"), State(Query("judo server status"), "Web server"));
        table.AddRow("Socket server", Escape(socket, 0, 1, "{0}:{1}"), State(Query("judo socket status"), "Socket server"));
        table.AddRow("Serial port", Escape(serial, 0, 1, "{0} @ {1} baud"), State(Query("judo serial state"), "Serial port"));
        table.AddRow("MQTT", mqtt.Length > 2 ? Escape(mqtt, 0, 1, "{0}:{1}") : "[grey]-[/]", State(Query("judo mqtt state"), "MQTT"));
        table.AddRow("Scheduler", schedules + " schedule(s)", "[green]ready[/]");
        AnsiConsole.Write(table);

        ShowNotices(string.Empty);
    }

    public string? ReadCommand() {
        if (_degraded) return _fallback.ReadCommand();

        try {
            AnsiConsole.WriteLine();
            return _editor.ReadLine(WritePrompt);
        }
        catch (Exception e) when (e is IOException || e is InvalidOperationException || e is PlatformNotSupportedException) {
            // an unusual terminal that cannot position the cursor: carry on with the simple prompt
            _degraded = true;
            return _fallback.ReadCommand();
        }
    }

    public void Run(string command) {
        var timer = Stopwatch.StartNew();
        string output = Execute(command);
        timer.Stop();

        Render(command, output);
        AnsiConsole.MarkupLine("[grey]" + (output.Length == 0 ? "done, " : "") + timer.ElapsedMilliseconds + " ms[/]");

        ShowNotices(output);
    }

    public void ShowGoodbye() {
        AnsiConsole.MarkupLine("[grey]Bye.[/]");
    }

    /* ------------------------------------------------------------------ */

    void WritePrompt() {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write(_host.UserName);
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write("@");
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write("jaNET");
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write("> ");
        Console.ResetColor();
    }

    // Runs the command; shows a spinner if it takes noticeably long (e.g. "judo sleep 5000" or a slow web request).
    string Execute(string command) {
        Task<string> task = Task.Run(() => _host.Execute(command));
        if (task.Wait(SpinnerDelay)) return task.Result;

        AnsiConsole.Status().Spinner(Spinner.Known.Dots).Start("Running...", _ => task.Wait());
        return task.Result;
    }

    static void Render(string command, string output) {
        if (output.Length == 0) return;

        var help = HelpFormatter.TryParse(command, output);
        if (help != null) {
            RenderHelp(help);
            return;
        }

        var data = OutputFormatter.TryTable(command, output);
        if (data != null) {
            var table = new Table().Border(TableBorder.Rounded).BorderColor(Color.Grey);
            foreach (string header in data.Header) table.AddColumn(new TableColumn("[bold]" + Markup.Escape(header) + "[/]"));
            foreach (string[] row in data.Rows) table.AddRow(row.Select(c => new Text(c)));
            AnsiConsole.Write(table);
            return;
        }

        Color color = OutputFormatter.LooksLikeError(output) ? Color.Red : Color.Yellow;
        AnsiConsole.Write(new Text(output + Environment.NewLine, new Style(color)));
    }

    // One table per chapter: what it is for, the command, and the other verbs that do the same. [arguments] are yellow and quoted or
    // locked text is green. A narrow window gets a list instead of a table.
    static void RenderHelp(HelpData help) {
        bool narrow = AnsiConsole.Profile.Width < 70;

        foreach (HelpSection section in help.Sections) {
            if (narrow) {
                AnsiConsole.Write(new Rule("[aqua bold]" + Markup.Escape(section.Title) + "[/]").LeftJustified());
                foreach (HelpTopic topic in section.Topics) {
                    AnsiConsole.Write(new Text(topic.Title + Environment.NewLine, new Style(Color.White, decoration: Decoration.Bold)));
                    foreach (HelpCommand command in topic.Commands) {
                        AnsiConsole.Write(CommandLine(command.Syntax, "  "));
                        AnsiConsole.WriteLine();
                        if (command.Aliases.Count > 0)
                            AnsiConsole.Write(new Text("    also: " + string.Join(", ", command.Aliases) + Environment.NewLine, new Style(Color.Grey)));
                    }
                }
                continue;
            }

            var table = new Table()
                .Border(TableBorder.Rounded)
                .BorderColor(Color.Grey)
                .Title("[aqua bold]" + Markup.Escape(section.Title) + "[/]")
                .ShowRowSeparators()
                .AddColumn(new TableColumn("[bold]For[/]"))
                .AddColumn(new TableColumn("[bold]Command[/]"))
                .AddColumn(new TableColumn("[bold]Same as[/]"));

            // one row per command, so that a long command that wraps keeps its aliases beside it; the topic is named on its first row
            foreach (HelpTopic topic in section.Topics)
                for (int i = 0; i < topic.Commands.Count; i++)
                    table.AddRow(new Text(i == 0 ? topic.Title : string.Empty, new Style(Color.White, decoration: Decoration.Bold)),
                                 CommandLine(topic.Commands[i].Syntax, string.Empty),
                                 new Text(string.Join(", ", topic.Commands[i].Aliases), new Style(Color.Grey)));

            AnsiConsole.Write(table);
        }

        foreach (string note in help.Notes)
            AnsiConsole.Write(new Text(note + Environment.NewLine, new Style(Color.Grey)));
    }

    static readonly System.Text.RegularExpressions.Regex HelpPieces = new(
        @"(?<arg>\[[^\]]*\])|(?<lock><lock>.*?</lock>)|(?<quoted>`[^`]*`)|(?<verbs>\S+\|\S+)|(?<word>\S+)|(?<space>\s+)",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    static Paragraph CommandLine(string command, string indent) {
        var line = new Paragraph();
        if (indent.Length > 0) line.Append(indent);

        int words = 0;
        foreach (System.Text.RegularExpressions.Match m in HelpPieces.Matches(command)) {
            Style? style = null;
            if (m.Groups["arg"].Success) style = new Style(Color.Yellow);
            else if (m.Groups["lock"].Success || m.Groups["quoted"].Success) style = new Style(Color.Green);
            else if (m.Groups["verbs"].Success) style = new Style(Color.White, decoration: Decoration.Bold);
            else if (m.Groups["word"].Success) {
                words++;
                style = words == 1 ? new Style(Color.Aqua) : words == 2 ? new Style(Color.Teal) : words == 3 ? new Style(Color.White, decoration: Decoration.Bold) : (Style?)null;
            }
            line.Append(m.Value, style);
        }
        return line;
    }

    // What needs attention is boxed in red (an error) or yellow (a warning); information is a grey line below.
    void ShowNotices(string answer) {
        var fresh = _notices.TakeNew(answer);
        var attention = fresh.Where(n => n.Level >= NoticeLevel.Warning).ToList();

        if (attention.Count > 0) {
            bool error = attention.Any(n => n.Level == NoticeLevel.Error);
            string color = error ? "red" : "yellow";
            var lines = attention.Select(n => new Markup(
                $"[{(n.Level == NoticeLevel.Error ? "red" : "yellow")} bold]{(n.Level == NoticeLevel.Error ? "x" : "!")} {Markup.Escape(n.Source)}[/]  {Markup.Escape(n.Message)}"));

            AnsiConsole.Write(new Panel(new Rows(lines))
                .Header($"[{color} bold] Needs your attention [/]")
                .BorderColor(error ? Color.Red : Color.Yellow)
                .Border(BoxBorder.Rounded)
                .Expand());
        }

        foreach (HostNotice info in fresh.Where(n => n.Level == NoticeLevel.Info))
            AnsiConsole.MarkupLine($"[grey]- {Markup.Escape(info.Source)}: {Markup.Escape(info.Message)}[/]");
    }

    string Query(string command) {
        return _host.Execute(command).Trim();
    }

    static string Escape(string[] lines, int a, int b, string format) {
        if (lines.Length <= Math.Max(a, b)) return "[grey]-[/]";
        return Markup.Escape(string.Format(format, lines[a], lines[b]));
    }

    string State(string status, string source) {
        if (status.Contains("True")) return "[green]on[/]";
        return _notices.Has(source) ? "[red]failed[/]" : "[grey]off[/]";
    }

    static void TrySetTitle() {
        try { Console.Title = "jaNET"; } catch { /* not every terminal supports it */ }
    }
}
