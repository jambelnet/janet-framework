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

namespace jaNETProgram.Terminal;

/// <summary>
/// A small line editor for interactive terminals: history (up/down), completion (Tab), a grey hint taken from the
/// history, syntax colours and the usual Ctrl shortcuts. Uses only System.Console, so it behaves the same on
/// Windows, Linux and macOS terminals.
/// </summary>
sealed class LineEditor
{
    readonly History _history;
    readonly Completer _completer;
    readonly JudoSyntax? _syntax;
    readonly LineBuffer _buffer = new LineBuffer();

    int _startLeft;
    int _startTop;
    int _drawnLength;

    public LineEditor(History history, Completer completer, JudoSyntax? syntax = null) {
        _history = history;
        _completer = completer;
        _syntax = syntax;
    }

    /// <summary>Reads one line. Returns null at the end of input (Ctrl+D on an empty line).</summary>
    /// <param name="writePrompt">Writes the prompt; called again after Ctrl+L cleared the screen.</param>
    public string? ReadLine(Action writePrompt) {
        _buffer.Clear();
        _history.Reset();
        _drawnLength = 0;

        writePrompt();
        RememberStart();

        bool previousMode = Console.TreatControlCAsInput;
        Console.TreatControlCAsInput = true;     // Ctrl+C cancels the line instead of killing the program

        try {
            while (true) {
                ConsoleKeyInfo key = Console.ReadKey(true);
                bool ctrl = (key.Modifiers & ConsoleModifiers.Control) != 0;
                bool shift = (key.Modifiers & ConsoleModifiers.Shift) != 0;

                switch (key.Key) {
                    case ConsoleKey.Enter:
                        MoveToEnd();
                        Console.WriteLine();
                        _history.Add(_buffer.Text);
                        return _buffer.Text;
                    case ConsoleKey.Backspace:
                        _buffer.Backspace();
                        break;
                    case ConsoleKey.Delete:
                        _buffer.Delete();
                        break;
                    case ConsoleKey.LeftArrow:
                        if (ctrl) _buffer.WordLeft(); else _buffer.Left();
                        break;
                    case ConsoleKey.RightArrow:
                        if (ctrl) _buffer.WordRight();
                        else if (!AcceptHint()) _buffer.Right();
                        break;
                    case ConsoleKey.Home:
                        _buffer.Home();
                        break;
                    case ConsoleKey.End:
                        if (!AcceptHint()) _buffer.End();
                        break;
                    case ConsoleKey.UpArrow:
                        Browse(_history.Previous(_buffer.Text));
                        break;
                    case ConsoleKey.DownArrow:
                        Browse(_history.Next());
                        break;
                    case ConsoleKey.Tab:
                        var completed = _completer.Complete(_buffer.Text, _buffer.Cursor, shift);
                        if (completed != null) _buffer.Set(completed.Value.Text, completed.Value.Cursor);
                        IReadOnlyList<string>? choices = _completer.TakeChoices();
                        if (choices != null) ShowChoices(choices, writePrompt);
                        break;
                    case ConsoleKey.Escape:
                        _buffer.Clear();
                        break;
                    default:
                        if (ctrl && HandleControl(key.Key, writePrompt, out bool endOfInput)) {
                            if (endOfInput) { Console.WriteLine(); return null; }
                            if (key.Key == ConsoleKey.C) { MoveToEnd(); Console.WriteLine(); return string.Empty; }
                        }
                        else if (!ctrl && !char.IsControl(key.KeyChar))
                            _buffer.Insert(key.KeyChar);
                        break;
                }

                if (!Console.KeyAvailable)      // pasted text arrives as a burst of keys: draw once at the end
                    Redraw();
            }
        }
        finally {
            Console.TreatControlCAsInput = previousMode;
            Console.ResetColor();
        }
    }

    bool HandleControl(ConsoleKey key, Action writePrompt, out bool endOfInput) {
        endOfInput = false;
        switch (key) {
            case ConsoleKey.A: _buffer.Home(); return true;
            case ConsoleKey.E: _buffer.End(); return true;
            case ConsoleKey.K: _buffer.KillToEnd(); return true;
            case ConsoleKey.U: _buffer.KillToStart(); return true;
            case ConsoleKey.W: _buffer.KillWordBefore(); return true;
            case ConsoleKey.B: _buffer.Left(); return true;
            case ConsoleKey.F: _buffer.Right(); return true;
            case ConsoleKey.C: return true;
            case ConsoleKey.D:
                if (_buffer.IsEmpty) { endOfInput = true; return true; }
                _buffer.Delete();
                return true;
            case ConsoleKey.L:
                Console.Clear();
                _drawnLength = 0;
                writePrompt();
                RememberStart();
                return true;
            default:
                return false;
        }
    }

    // The line has several possible completions and nothing more in common: list them below the line like a shell does and
    // start a fresh prompt with the line unchanged.
    void ShowChoices(IReadOnlyList<string> choices, Action writePrompt) {
        MoveToEnd();
        string hint = _history.Suggest(_buffer.Text);
        if (hint.Length > 0) Console.Write(new string(' ', hint.Length));      // the grey suggestion must not stay behind in the scroll back
        Console.WriteLine();

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine(Completer.Columns(choices, Math.Max(20, Console.WindowWidth - 1)));
        Console.ResetColor();

        writePrompt();
        RememberStart();
        _drawnLength = 0;
    }

    void Browse(string? entry) {
        if (entry != null) _buffer.Set(entry);
    }

    bool AcceptHint() {
        if (_buffer.Cursor != _buffer.Text.Length) return false;
        string hint = _history.Suggest(_buffer.Text);
        if (hint.Length == 0) return false;
        _buffer.Set(_buffer.Text + hint);
        return true;
    }

    void RememberStart() {
        _startLeft = Console.CursorLeft;
        _startTop = Console.CursorTop;
    }

    void MoveToEnd() {
        int width = Math.Max(1, Console.WindowWidth);
        int pos = _startLeft + _buffer.Text.Length;
        Console.SetCursorPosition(Math.Min(pos % width, width - 1), Math.Min(_startTop + pos / width, Console.BufferHeight - 1));
    }

    void Redraw() {
        int width = Math.Max(1, Console.WindowWidth);
        string text = _buffer.Text;
        string hint = _buffer.Cursor == text.Length ? _history.Suggest(text) : string.Empty;

        Console.CursorVisible = false;
        Console.SetCursorPosition(_startLeft, _startTop);

        foreach (var piece in Highlighter.Highlight(text, _syntax)) {
            if (piece.Color.HasValue) Console.ForegroundColor = piece.Color.Value;
            Console.Write(piece.Text);
            Console.ResetColor();
        }

        if (hint.Length > 0) {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write(hint);
            Console.ResetColor();
        }

        int written = text.Length + hint.Length;
        int erase = _drawnLength - written;                       // characters of the previous drawing that are left over
        if (erase > 0) Console.Write(new string(' ', erase));
        int extent = Math.Max(written, _drawnLength);

        // exactly filling the last column leaves terminals in different "pending wrap" states: force the wrap
        if ((_startLeft + extent) % width == 0) Console.Write(' ');

        // if writing scrolled the screen, the prompt moved up with it
        int expectedRow = _startTop + (_startLeft + extent + ((_startLeft + extent) % width == 0 ? 1 : 0)) / width;
        int actualRow = Console.CursorTop;
        if (actualRow < expectedRow) _startTop -= expectedRow - actualRow;

        _drawnLength = written;

        int cursorPos = _startLeft + _buffer.Cursor;
        Console.SetCursorPosition(cursorPos % width, Math.Max(0, _startTop + cursorPos / width));
        Console.CursorVisible = true;
    }
}
