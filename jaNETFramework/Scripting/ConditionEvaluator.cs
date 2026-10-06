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

using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using System;
using System.Text.RegularExpressions;

namespace jaNET.Scripting;

/// <summary>
/// Evaluates conditions of the form { evalBool(a == b); then; else; } and replaces each by the result of the chosen branch.
/// The condition is a C# expression; "text ~> part" tests whether the text contains the part.
/// </summary>
internal sealed class ConditionEvaluator
{
    // the C# dialect of the former runtime compiler: System is imported, nothing else
    static readonly ScriptOptions Options = ScriptOptions.Default.WithImports("System");

    static readonly Regex Block = new(@"\{(.*?)\}", RegexOptions.Compiled);
    static readonly Regex Decoration = new("evalBool|[{}]|[()]", RegexOptions.Compiled);

    readonly Func<InstructionResolver> _resolver;
    readonly Func<IInstructionExecutor> _executor;

    public ConditionEvaluator(Func<InstructionResolver> resolver, Func<IInstructionExecutor> executor) {
        _resolver = resolver;
        _executor = executor;
    }

    public string EvaluateCondition(string text) {
        foreach (Match block in Block.Matches(text)) {
            string[] parts = block.Value.Split(';');
            if (parts[0].Contains('*'))
                parts[0] = _resolver().Resolve(parts[0]);
            if (!parts[0].Contains("evalBool")) continue;

            string condition = Decoration.Replace(parts[0], string.Empty).Trim();
            bool outcome;

            if (condition.Contains("~>")) {
                string[] operands = Regex.Split(condition, "~>");
                // the spaces around the operator are not part of the texts: "abc" ~> "b" is true
                outcome = operands[0].Replace("\"", string.Empty).Trim().Contains(operands[1].Replace("\"", string.Empty).Trim());
            }
            else
                outcome = EvaluateBool(condition);

            // the chosen branch may name several instructions separated by spaces
            string branch = (outcome ? parts[1] : parts[2]).Trim().Replace(" ", ";");
            text = text.Replace(block.Value, _executor().Run(branch, ResponseFormat.Text, silent: true));
        }
        return text;
    }

    internal static bool EvaluateBool(string code) {
        try {
            return CSharpScript.EvaluateAsync<bool>(code, Options).GetAwaiter().GetResult();
        }
        catch (CompilationErrorException e) {
            throw new ConditionSyntaxException("Error Compiling Expression: Error Compiling Expression: " + string.Join(System.Environment.NewLine, e.Diagnostics));
        }
    }
}

/// <summary>The condition of an evalBool(...) is not a valid C# expression.</summary>
internal sealed class ConditionSyntaxException : Exception
{
    public ConditionSyntaxException(string message) : base(message) { }
}
