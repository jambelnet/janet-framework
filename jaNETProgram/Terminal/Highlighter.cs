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
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace jaNETProgram.Terminal;

/// <summary>Splits a command line into coloured pieces while it is typed.</summary>
static class Highlighter
{
    static readonly Regex Pieces = new Regex(
        @"(?<function>%[A-Za-z0-9_~>]+%)|(?<lock><lock>.*?(</lock>|$))|(?<quoted>`[^`]*`?|""[^""]*""?|'[^']*'?)|(?<space>\s+)|(?<word>\S+?(?=%|`|""|'|<lock>|\s|$))",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    /// <param name="line">The command line as typed so far.</param>
    /// <param name="syntax">When given, a command after "judo" that does not exist is red, so a typo shows before Enter.</param>
    /// <returns>pieces whose concatenation is exactly <paramref name="line"/>; a null colour is the terminal default</returns>
    public static IReadOnlyList<(string Text, ConsoleColor? Color)> Highlight(string line, JudoSyntax? syntax = null) {
        var result = new List<(string, ConsoleColor?)>();
        int words = 0;
        bool judo = false;
        int pos = 0;

        foreach (Match m in Pieces.Matches(line)) {
            if (m.Index > pos) result.Add((line.Substring(pos, m.Index - pos), null));   // anything the pattern skipped
            pos = m.Index + m.Length;

            if (m.Groups["function"].Success) result.Add((m.Value, ConsoleColor.Magenta));
            else if (m.Groups["lock"].Success || m.Groups["quoted"].Success) result.Add((m.Value, ConsoleColor.Green));
            else if (m.Groups["word"].Success) {
                words++;
                if (words == 1) {
                    judo = m.Value.Equals("judo", StringComparison.OrdinalIgnoreCase);
                    result.Add((m.Value, judo ? ConsoleColor.Cyan : (ConsoleColor?)null));
                }
                else if (words == 2 && judo) result.Add((m.Value, KnownCommand(m.Value, syntax) ? ConsoleColor.DarkCyan : ConsoleColor.Red));
                else result.Add((m.Value, null));
            }
            else result.Add((m.Value, null));
        }

        if (pos < line.Length) result.Add((line.Substring(pos), null));
        return result;
    }

    // a word that is still being typed counts as known while it starts something that exists ("judo ser" is on its way to serial)
    static bool KnownCommand(string word, JudoSyntax? syntax) =>
        syntax == null || syntax.Roots.Any(r => r.StartsWith(word, StringComparison.OrdinalIgnoreCase));
}
