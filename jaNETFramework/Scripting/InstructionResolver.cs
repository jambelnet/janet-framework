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

using jaNET.Configuration;
using jaNET.Infrastructure;
using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace jaNET.Scripting;

/// <summary>
/// Turns the text of one instruction set into its result: expands %functions%, follows *pointers to other instruction sets,
/// evaluates { evalBool(...); then; else; } conditions and runs "judo ..." commands and "./programs".
/// </summary>
internal sealed class InstructionResolver
{
    const int MaxDepth = 32;

    static readonly Regex Pointer = new(@"[*][a-zA-Z0-9_-]+", RegexOptions.Compiled);
    static readonly Regex Quoted = new(@"('[^']+')|(`[^`]+`)", RegexOptions.Compiled);

    readonly FunctionExpander _functions;
    readonly AppConfigStore _config;
    readonly Func<ConditionEvaluator> _evaluator;
    readonly Func<Commands.JudoDispatcher> _judo;
    readonly ProcessRunner _processes;

    public InstructionResolver(FunctionExpander functions, AppConfigStore config, Func<ConditionEvaluator> evaluator, Func<Commands.JudoDispatcher> judo, ProcessRunner processes) {
        _functions = functions;
        _config = config;
        _evaluator = evaluator;
        _judo = judo;
        _processes = processes;
    }

    /// <param name="position">Which of several instruction sets with the same id is meant (normally the first)</param>
    public string Resolve(string text, int position = 0) => Resolve(text, position, 0);

    string Resolve(string text, int position, int depth) {
        if (depth > MaxDepth)
            throw new InvalidOperationException("Instruction sets refer to each other too deeply (a cycle?).");

        if (text.Contains('%'))
            text = _functions.Expand(text);

        text = FollowPointers(text, position, depth);

        if (text.Contains("evalBool"))
            text = _evaluator().EvaluateCondition(text);

        if (text.StartsWith("judo", StringComparison.Ordinal))
            return _judo().Execute(text);

        if (text.StartsWith("./", StringComparison.Ordinal))
            return RunProgram(text);

        return text;
    }

    // "*name" stands for the text of the instruction set "*name"
    string FollowPointers(string text, int position, int depth) {
        while (text.Contains('*')) {
            var pointers = Pointer.Matches(text).Select(m => m.Value).ToList();
            if (pointers.Count == 0) break;           // a plain asterisk, e.g. in "2 * 3"

            foreach (string pointer in pointers) {
                string? target = _config.InstructionActions(pointer).ElementAtOrDefault(position);
                if (target == null) throw new InstructionNotFoundException(pointer);

                text = text.Replace(pointer, Resolve(target, 0, depth + 1));
            }
        }
        return text;
    }

    // ./program, ./'my program', ./'program' 'arguments'
    string RunProgram(string text) {
        MatchCollection quoted = Quoted.Matches(text);
        string fileName = string.Empty;
        string arguments = string.Empty;

        if (quoted.Count == 0)
            fileName = text.Replace("./", string.Empty);
        else if (quoted.Count == 1)
            fileName = Unquote(quoted[0].Value);
        else if (quoted.Count == 2) {
            fileName = Unquote(quoted[0].Value);
            arguments = Unquote(quoted[1].Value);
        }

        return _processes.Run(fileName, arguments);

        static string Unquote(string value) => value.Replace("`", string.Empty).Replace("'", string.Empty);
    }
}
