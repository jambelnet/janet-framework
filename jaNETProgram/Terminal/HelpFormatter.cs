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
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace jaNETProgram.Terminal;

/// <summary>One numbered chapter of the help ("4. Scheduler") with its topics.</summary>
sealed class HelpSection
{
    public string Title { get; init; } = string.Empty;
    public List<HelpTopic> Topics { get; } = new List<HelpTopic>();
}

/// <summary>One thing the help explains ("4.2 Remove Schedule") with the commands that do it.</summary>
sealed class HelpTopic
{
    public string Title { get; init; } = string.Empty;

    /// <summary>The commands without the leading "+". Variants that differ only in the verb are one entry with the other verbs as aliases.</summary>
    public List<HelpCommand> Commands { get; } = new List<HelpCommand>();
}

/// <param name="Syntax">The command as the help writes it, with the first verb: "judo inset add [ID] ...".</param>
/// <param name="Aliases">The other verbs that do the same: "new", "set", "setup".</param>
sealed record HelpCommand(string Syntax, IReadOnlyList<string> Aliases);

sealed class HelpData
{
    public List<HelpSection> Sections { get; } = new List<HelpSection>();

    /// <summary>The footnotes, "(*) Brackets are mandatory ...".</summary>
    public List<string> Notes { get; } = new List<string>();
}

/// <summary>Reads the text of "judo help" into chapters, topics and commands so the fancy console can lay it out as tables.</summary>
static class HelpFormatter
{
    static readonly Regex SectionLine = new(@"^(\d+)\.\s+(.+)$", RegexOptions.Compiled);
    static readonly Regex TopicLine = new(@"^\d+\.\d+\s+(.+)$", RegexOptions.Compiled);
    static readonly Regex Verb = new(@"^[a-z][a-z0-9-]*$", RegexOptions.Compiled);

    /// <returns>null when the command is not a help command or the text is not in the expected shape (it is then shown as it is)</returns>
    public static HelpData? TryParse(string command, string output) {
        string[] words = (command ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 2 || !words[0].Equals("judo", StringComparison.OrdinalIgnoreCase)) return null;
        if (!words[1].Equals("help", StringComparison.OrdinalIgnoreCase) && words[1] != "?") return null;

        var data = new HelpData();
        HelpSection? section = null;
        HelpTopic? topic = null;
        var raw = new List<string>();

        void Finish() {
            if (topic != null) topic.Commands.AddRange(GroupVerbs(raw));
            raw.Clear();
        }

        foreach (string line in OutputFormatter.Lines(output)) {
            string text = line.Trim();

            Match topicMatch = TopicLine.Match(text);
            Match sectionMatch = SectionLine.Match(text);

            if (topicMatch.Success && section != null) {
                Finish();
                topic = new HelpTopic { Title = topicMatch.Groups[1].Value.Trim() };
                section.Topics.Add(topic);
            }
            else if (sectionMatch.Success) {
                Finish();
                topic = null;
                section = new HelpSection { Title = text };
                data.Sections.Add(section);
            }
            else if (text.StartsWith("+ ", StringComparison.Ordinal) && topic != null)
                raw.Add(text.Substring(2).Trim());
            else if (text.StartsWith("(", StringComparison.Ordinal))
                data.Notes.Add(text);
            else
                return null;
        }
        Finish();

        return data.Sections.Count > 0 && data.Sections.All(s => s.Topics.Count > 0) ? data : null;
    }

    // "judo inset add X" / "judo inset new X" / ... become "judo inset add X" with the aliases new, set, setup: the same line with other verbs.
    // Only a real verb in the third place is grouped; "judo ping [Host]" has an argument there and stays as it is.
    static IEnumerable<HelpCommand> GroupVerbs(List<string> commands) {
        var groups = new List<(string Key, List<string> Verbs, string[] Words)>();

        foreach (string command in commands) {
            string[] words = command.Split(' ');
            if (words.Length < 3 || !Verb.IsMatch(words[2])) {
                groups.Add((command, new List<string>(), words));
                continue;
            }

            string key = words[0] + " " + words[1] + " " + (char)1 + " " + string.Join(" ", words.Skip(3));
            var found = groups.FirstOrDefault(g => g.Key == key);
            if (found.Verbs == null) groups.Add((key, new List<string> { words[2] }, words));
            else if (!found.Verbs.Contains(words[2])) found.Verbs.Add(words[2]);
        }

        foreach (var (key, verbs, words) in groups) {
            if (verbs.Count == 0) {
                yield return new HelpCommand(key, Array.Empty<string>());
                continue;
            }
            yield return new HelpCommand(string.Join(" ", new[] { words[0], words[1], verbs[0] }.Concat(words.Skip(3))), verbs.Skip(1).ToList());
        }
    }
}
