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
using System.Xml.Linq;

namespace jaNETProgram.Terminal;

/// <summary>Recognizes the output of list commands so the fancy console can show them as tables.</summary>
static class OutputFormatter
{
    public sealed class TableData
    {
        public string[] Header { get; set; } = Array.Empty<string>();
        public List<string[]> Rows { get; } = new List<string[]>();
    }

    public static string[] Lines(string output) =>
        (output ?? string.Empty).Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>The table for "judo schedule ..." / "judo inset ls" / "judo event ls", or null if the output is something else.</summary>
    public static TableData? TryTable(string command, string output) {
        string[] words = (command ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 2 || !words[0].Equals("judo", StringComparison.OrdinalIgnoreCase)) return null;

        switch (words[1].ToLowerInvariant()) {
            case "schedule": return Schedules(output);
            case "inset": return Elements(output, "InstructionSet", "Id", "Action", "Category", "Header");
            case "event": return Elements(output, "event", "Id", "Action");
            default: return null;
        }
    }

    static TableData? Schedules(string output) {
        string[] lines = Lines(output);
        if (lines.Length == 0) return null;

        var table = new TableData { Header = new[] { "Name", "Date", "Time", "Action", "State" } };
        foreach (string line in lines) {
            string[] cells = line.Split(new[] { " | " }, StringSplitOptions.None);
            if (cells.Length != 5) return null;
            table.Rows.Add(cells);
        }
        return table;
    }

    // Every line is one XML element: <InstructionSet id=".." categ="..">action</InstructionSet>
    static TableData? Elements(string output, string elementName, params string[] columns) {
        string[] lines = Lines(output);
        if (lines.Length == 0) return null;

        var table = new TableData { Header = columns };
        foreach (string line in lines) {
            XElement e;
            try { e = XElement.Parse(line); } catch (System.Xml.XmlException) { return null; }
            if (e.Name.LocalName != elementName) return null;

            table.Rows.Add(columns.Select(c => {
                switch (c) {
                    case "Id": return (string?)e.Attribute("id") ?? string.Empty;
                    case "Action": return e.Value;
                    case "Category": return (string?)e.Attribute("categ") ?? string.Empty;
                    case "Header": return (string?)e.Attribute("header") ?? string.Empty;
                    default: return string.Empty;
                }
            }).ToArray());
        }
        return table;
    }

    /// <summary>Errors the parser reports in plain words.</summary>
    public static bool LooksLikeError(string output) {
        return output.EndsWith(", not found.", StringComparison.Ordinal) ||
               output.Contains("\nReason: ", StringComparison.Ordinal) ||
               output.StartsWith("Index was out of range", StringComparison.Ordinal) ||
               output.EndsWith("Please try again.", StringComparison.Ordinal);
    }
}
