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
using System.Text;

namespace jaNETProgram.Terminal;

/// <summary>
/// Tab completion for judo commands, %functions% and instruction set names. It behaves like a shell:
/// one match completes the word; several matches complete as far as they agree; if they still differ the line is left alone and
/// the choices are listed (<see cref="TakeChoices"/>); pressing Tab again then steps through them, Shift+Tab backwards.
/// </summary>
sealed class Completer
{
    readonly JudoSyntax _syntax;

    // state of a Tab cycle: valid while the line still looks like we left it
    string? _cycleText;
    int _cycleCursor;
    int _wordStart;
    IReadOnlyList<string> _cycleCandidates = Array.Empty<string>();
    int _cycleIndex = -1;       // -1: the choices were only listed, nothing was picked yet
    IReadOnlyList<string>? _choices;

    public Completer(JudoSyntax syntax) {
        _syntax = syntax;
    }

    /// <summary>The choices to show below the line after the last <see cref="Complete"/>, or null; taking them clears them.</summary>
    public IReadOnlyList<string>? TakeChoices() {
        IReadOnlyList<string>? choices = _choices;
        _choices = null;
        return choices;
    }

    /// <summary>Returns the new line and cursor, or null when there is nothing to complete.</summary>
    public (string Text, int Cursor)? Complete(string text, int cursor, bool backwards = false) {
        _choices = null;

        if (_cycleCandidates.Count > 1 && text == _cycleText && cursor == _cycleCursor)
            return Cycle(text, backwards);

        int start = cursor;
        while (start > 0 && !char.IsWhiteSpace(text[start - 1])) start--;
        int end = cursor;                                            // the cursor may be inside a word: "judo serv|er start"
        while (end < text.Length && !char.IsWhiteSpace(text[end])) end++;

        string word = text.Substring(start, cursor - start);
        string[] before = text.Substring(0, start).Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

        var candidates = Candidates(word, before)
            .Where(c => c.Length > 0 && c.StartsWith(word, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (candidates.Count == 0) {
            _cycleCandidates = Array.Empty<string>();
            return null;
        }

        string head = text.Substring(0, start);
        string tail = text.Substring(end);

        if (candidates.Count == 1) {
            _cycleCandidates = Array.Empty<string>();
            string completed = candidates[0] + (tail.StartsWith(' ') ? string.Empty : " ");
            return (head + completed + tail, head.Length + completed.Length);
        }

        string common = CommonPrefix(candidates);
        _wordStart = start;
        _cycleCandidates = candidates;
        _cycleIndex = -1;

        if (common.Length > word.Length) {
            // they agree on more than was typed: take that much, the next Tab lists what is left to choose from
            _cycleCandidates = Array.Empty<string>();
            return (head + common + tail, head.Length + common.Length);
        }

        // nothing more to agree on: show the choices and keep the line, so a wrong guess never replaces what was typed
        _choices = candidates;
        _cycleText = text;
        _cycleCursor = cursor;
        return (text, cursor);
    }

    (string Text, int Cursor)? Cycle(string text, bool backwards) {
        int count = _cycleCandidates.Count;
        _cycleIndex = _cycleIndex < 0
            ? (backwards ? count - 1 : 0)
            : (backwards ? (_cycleIndex - 1 + count) % count : (_cycleIndex + 1) % count);

        string head = text.Substring(0, _wordStart);
        // everything after the word that is being cycled stays as it is
        int end = _wordStart;
        while (end < text.Length && !char.IsWhiteSpace(text[end])) end++;
        string tail = text.Substring(end);

        string pick = _cycleCandidates[_cycleIndex];
        _cycleText = head + pick + tail;
        _cycleCursor = head.Length + pick.Length;
        return (_cycleText, _cycleCursor);
    }

    /// <summary>The alternatives for the word under the cursor (for display), without changing the line.</summary>
    public IReadOnlyList<string> Alternatives(string text, int cursor) {
        int start = cursor;
        while (start > 0 && !char.IsWhiteSpace(text[start - 1])) start--;
        string word = text.Substring(start, cursor - start);
        string[] before = text.Substring(0, start).Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        return Candidates(word, before).Where(c => c.StartsWith(word, StringComparison.OrdinalIgnoreCase)).Distinct().ToList();
    }

    IEnumerable<string> Candidates(string word, string[] before) {
        if (word.StartsWith('%'))
            return _syntax.Functions;

        if (before.Length == 0)
            return new[] { "judo" }.Concat(_syntax.InstructionSets);

        if (before[0].Equals("judo", StringComparison.OrdinalIgnoreCase)) {
            if (before.Length == 1) return _syntax.Roots;
            if (before.Length == 2) return _syntax.SubCommands(before[1]);
        }
        return Enumerable.Empty<string>();
    }

    static string CommonPrefix(IReadOnlyList<string> items) {
        string prefix = items[0];
        foreach (string item in items) {
            int i = 0;
            while (i < prefix.Length && i < item.Length && char.ToLowerInvariant(prefix[i]) == char.ToLowerInvariant(item[i])) i++;
            prefix = prefix.Substring(0, i);
        }
        return prefix;
    }

    /// <summary>The choices in columns that fit <paramref name="width"/>, filled column by column like a shell does.</summary>
    public static string Columns(IReadOnlyList<string> items, int width) {
        int cell = items.Max(i => i.Length) + 2;
        int columns = Math.Max(1, Math.Max(1, width) / cell);
        int rows = (items.Count + columns - 1) / columns;

        var text = new StringBuilder();
        for (int row = 0; row < rows; row++) {
            for (int column = 0; column < columns; column++) {
                int index = column * rows + row;
                if (index >= items.Count) break;
                string item = items[index];
                text.Append(column == columns - 1 || index + rows >= items.Count ? item : item.PadRight(cell));
            }
            if (row < rows - 1) text.AppendLine();
        }
        return text.ToString();
    }
}
