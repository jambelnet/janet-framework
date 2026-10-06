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
using jaNET.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace jaNET.Scripting;

/// <summary>
/// The heart of jaNET: runs "instructions" - names of instruction sets, %functions%, "judo" commands - and combines their results.
/// Every front end (console, web server, socket, scheduler, serial port, mail) goes through here.
/// </summary>
internal sealed class InstructionRunner : IInstructionExecutor
{
    static readonly Regex ExtraWhitespace = new(@"[^\S\r\n]+", RegexOptions.Compiled);

    readonly AppConfigStore _config;
    readonly InstructionResolver _resolver;
    readonly Func<Commands.JudoDispatcher> _judo;
    readonly ISpeaker _speaker;
    readonly MailNotifier _notifier;
    readonly ILog _log;

    public InstructionRunner(AppConfigStore config, InstructionResolver resolver, Func<Commands.JudoDispatcher> judo, ISpeaker speaker, MailNotifier notifier, ILog log) {
        _config = config;
        _resolver = resolver;
        _judo = judo;
        _speaker = speaker;
        _notifier = notifier;
        _log = log;
    }

    public string Run(string input, ResponseFormat format = ResponseFormat.Text, bool silent = false) {
        try {
            if (input.Contains("{mute}") || input.Contains("{widget}")) {
                input = input.Replace("{mute}", string.Empty).Replace("{widget}", string.Empty);
                silent = true;
            }

            // a locked action is handed over as it is: no splitting at ; and &
            if (input.Contains("</lock>")) {
                string answer = _judo().Execute(input);
                return format == ResponseFormat.Html ? answer.Replace("\n", "<br />") : answer;
            }

            List<string> instructions = input.Replace('&', ';').Split(';')
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .Distinct()
                .ToList();

            // key: the instruction as a readable name (also the property name in the JSON answer)
            var results = new Dictionary<string, KeyValuePair<string, string>>();
            foreach (string instruction in instructions)
                results.Add(instruction.Replace(" ", "_").Replace("%", string.Empty),
                            new KeyValuePair<string, string>(instruction, ExtraWhitespace.Replace(Execute(instruction, silent), " ")));

            switch (format) {
                case ResponseFormat.Html:
                    return Texts(results).Replace("<", "&lt;").Replace(">", "&gt;").Replace("\n", "<br />");
                case ResponseFormat.Json:
                    return JsonCompat.Serialize(results);
                default:
                    return Texts(results);
            }
        }
        catch (Exception e) {
            if (e is not ArgumentOutOfRangeException { ParamName: "length" })
                _log.Write($"obj [ Parser.Parse <Exception> ] Argument: [ {input} ] Exception Message: [ {e.Message} ]");
            return e.Message;
        }
    }

    static string Texts(Dictionary<string, KeyValuePair<string, string>> results) =>
        string.Join("\r\n", results.Values.Select(pair => pair.Value));

    string Execute(string instruction, bool silent) {
        string output = string.Empty;

        if (instruction.StartsWith('%') || instruction.StartsWith("./", StringComparison.Ordinal) || instruction.StartsWith("judo", StringComparison.Ordinal))
            return _resolver.Resolve(instruction);

        // "*name" asks for the launcher of an instruction set; otherwise look up the instruction set itself
        IReadOnlyList<string> actions = _config.InstructionActions(instruction.Replace("*", string.Empty));

        if (actions.Count == 0 && !instruction.Contains('*')) {
            _log.Write($"obj [ Parser.Execute ] Argument: [ {instruction}, not found. ]");
            output = instruction + ", not found.";
        }
        else {
            for (int position = 0; position < actions.Count; position++)
                output += _resolver.Resolve(actions[position], position) + "\r\n";
        }

        if (!silent)
            _notifier.Notify(output);

        if (!string.IsNullOrWhiteSpace(output) && !_speaker.Muted && !silent)
            Task.Run(() => _speaker.Say(output));

        return output.Trim();
    }
}
