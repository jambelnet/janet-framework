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

namespace jaNETProgram.Terminal;

/// <summary>The text being typed and the cursor inside it. Pure state, no console access.</summary>
sealed class LineBuffer
{
    string _text = string.Empty;

    public string Text => _text;

    public int Cursor { get; private set; }

    public bool IsEmpty => _text.Length == 0;

    public void Set(string text) {
        _text = text ?? string.Empty;
        Cursor = _text.Length;
    }

    public void Set(string text, int cursor) {
        Set(text);
        Cursor = Math.Max(0, Math.Min(cursor, _text.Length));
    }

    public void Clear() => Set(string.Empty);

    public void Insert(char c) {
        _text = _text.Insert(Cursor, c.ToString());
        Cursor++;
    }

    public void Insert(string s) {
        _text = _text.Insert(Cursor, s);
        Cursor += s.Length;
    }

    public void Backspace() {
        if (Cursor == 0) return;
        _text = _text.Remove(Cursor - 1, 1);
        Cursor--;
    }

    public void Delete() {
        if (Cursor >= _text.Length) return;
        _text = _text.Remove(Cursor, 1);
    }

    public void Left() { if (Cursor > 0) Cursor--; }

    public void Right() { if (Cursor < _text.Length) Cursor++; }

    public void Home() => Cursor = 0;

    public void End() => Cursor = _text.Length;

    public void WordLeft() => Cursor = WordStartBefore(Cursor);

    public void WordRight() {
        int i = Cursor;
        while (i < _text.Length && char.IsWhiteSpace(_text[i])) i++;
        while (i < _text.Length && !char.IsWhiteSpace(_text[i])) i++;
        Cursor = i;
    }

    public void KillToStart() {
        _text = _text.Substring(Cursor);
        Cursor = 0;
    }

    public void KillToEnd() => _text = _text.Substring(0, Cursor);

    public void KillWordBefore() {
        int start = WordStartBefore(Cursor);
        _text = _text.Remove(start, Cursor - start);
        Cursor = start;
    }

    int WordStartBefore(int index) {
        int i = index;
        while (i > 0 && char.IsWhiteSpace(_text[i - 1])) i--;
        while (i > 0 && !char.IsWhiteSpace(_text[i - 1])) i--;
        return i;
    }
}

/// <summary>Commands entered earlier in this session (not stored on disk: commands can contain passwords).</summary>
sealed class History
{
    readonly List<string> _entries = new List<string>();
    int _position;
    string _draft = string.Empty;

    public IReadOnlyList<string> Entries => _entries;

    public void Add(string line) {
        if (!string.IsNullOrWhiteSpace(line) && (_entries.Count == 0 || _entries[_entries.Count - 1] != line))
            _entries.Add(line);
        Reset();
    }

    /// <summary>Forget the browsing position, e.g. after a line was submitted.</summary>
    public void Reset() {
        _position = _entries.Count;
        _draft = string.Empty;
    }

    /// <summary>Older entry; <paramref name="current"/> is kept as a draft to come back to.</summary>
    public string? Previous(string current) {
        if (_entries.Count == 0 || _position == 0) return null;
        if (_position == _entries.Count) _draft = current;
        _position--;
        return _entries[_position];
    }

    /// <summary>Newer entry, or the draft after the newest one; null when not browsing.</summary>
    public string? Next() {
        if (_position >= _entries.Count) return null;
        _position++;
        return _position == _entries.Count ? _draft : _entries[_position];
    }

    /// <summary>The rest of the most recent entry that starts with <paramref name="prefix"/> (for the grey hint).</summary>
    public string Suggest(string prefix) {
        if (string.IsNullOrEmpty(prefix)) return string.Empty;
        for (int i = _entries.Count - 1; i >= 0; i--)
            if (_entries[i].Length > prefix.Length && _entries[i].StartsWith(prefix, StringComparison.Ordinal))
                return _entries[i].Substring(prefix.Length);
        return string.Empty;
    }
}
