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

using jaNET.Scripting;
using System;
using System.Collections.Generic;
using System.Linq;

namespace jaNET.Commands;

/// <summary>
/// One "judo ..." command line. <c>Args[0]</c> is "judo", <c>Args[1]</c> the command, <c>Args[2]</c> the sub command, the rest its arguments;
/// text in quotes or between &lt;lock&gt; tags is one argument without the quotes. Reading an argument that was not given throws
/// <see cref="ArgumentOutOfRangeException"/>, which jaNET reports to the user as "Index was out of range".
/// </summary>
/// <param name="Raw">The command line as it was received.</param>
/// <param name="Args">The arguments, see above.</param>
public sealed record JudoInvocation(string Raw, IReadOnlyList<string> Args)
{
    /// <summary>The sub command (<c>Args[2]</c>).</summary>
    public string Sub => Args[2];

    /// <summary>The number of arguments including "judo" and the command.</summary>
    public int Count => Args.Count;
}

/// <summary>
/// A "judo" command with its sub commands, e.g. "serial" with open, close, send. Derive from it, name it and register the handlers in the
/// constructor with <see cref="On"/>; hand the instance to <see cref="Hosting.JanetHostOptions.Commands"/> to make it available as
/// "judo name sub ...". Whatever a handler returns is the answer shown to the user.
/// </summary>
public abstract class JudoCommand
{
    readonly Dictionary<string, Func<JudoInvocation, string>> _handlers = new(StringComparer.Ordinal);
    Func<JudoInvocation, string>? _fallback;

    /// <summary>Creates the command; register the sub commands in the constructor of the derived class.</summary>
    protected JudoCommand() { }

    /// <summary>Names the command is known by after "judo" (the first is the main one).</summary>
    public abstract IReadOnlyList<string> Names { get; }

    /// <summary>The sub commands, for help and completion.</summary>
    public IReadOnlyList<string> SubCommands => _handlers.Keys.ToList();

    /// <summary>Registers a handler for one or more sub command names.</summary>
    protected void On(Func<JudoInvocation, string> handler, params string[] names) {
        foreach (string name in names) _handlers[name] = handler;
    }

    /// <summary>The handler for sub commands nobody else claims (e.g. "list" for typos). Without one they answer with nothing.</summary>
    protected void Otherwise(Func<JudoInvocation, string> handler) => _fallback = handler;

    /// <summary>Runs the handler of the sub command of <paramref name="invocation"/>. Override it for a command without sub commands.</summary>
    public virtual string Execute(JudoInvocation invocation) {
        // a missing sub command raises the same "index out of range" as always
        string sub = invocation.Sub;

        if (_handlers.TryGetValue(sub, out Func<JudoInvocation, string>? handler)) return handler(invocation);
        return _fallback?.Invoke(invocation) ?? string.Empty;
    }
}

/// <summary>Finds the command for "judo name ..." and runs it.</summary>
internal sealed class JudoDispatcher
{
    readonly Dictionary<string, JudoCommand> _commands = new(StringComparer.Ordinal);
    readonly Func<FunctionExpander> _functions;

    public JudoDispatcher(IEnumerable<JudoCommand> commands, Func<FunctionExpander> functions) {
        _functions = functions;

        foreach (JudoCommand command in commands)
            foreach (string name in command.Names) {
                if (_commands.TryGetValue(name, out JudoCommand? existing) && !ReferenceEquals(existing, command))
                    throw new ArgumentException($"A judo command named '{name}' already exists.");
                _commands[name] = command;
            }
    }

    /// <summary>Commands that follow "judo", sorted.</summary>
    public IReadOnlyList<string> Roots => _commands.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();

    public IReadOnlyList<string> SubCommands(string root) =>
        _commands.TryGetValue(root.ToLowerInvariant(), out JudoCommand? command) ? command.SubCommands : Array.Empty<string>();

    public string Execute(string text) {
        // %functions% are expanded, except in locked text which is handed over untouched
        IReadOnlyList<string> args = ArgumentSplitter.Split(text.Contains("</lock>") ? text : _functions().Expand(text));

        return _commands.TryGetValue(args[1], out JudoCommand? command)
            ? command.Execute(new JudoInvocation(text, args))
            : string.Empty;
    }
}
