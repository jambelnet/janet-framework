/* *****************************************************************************************************************************
 * (c) J@mBeL.net 2010-2017
 * Author: John Ambeliotis
 * Created: 24 Apr. 2010
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

using jaNET.Diagnostics;
using jaNET.Environment.AppConfig;
using jaNET.Extensions;
using jaNET.Net;
using jaNET.Net.Http;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace jaNET.Environment.Core
{
    public class Parser
    {
        public static Parser Instance => Singleton<Parser>.Instance;

        static volatile bool _parserState = true;

        public static bool ParserState {
            get {
                return _parserState;
            }
            internal set {
                _parserState = value;
            }
        }

        internal static volatile bool Mute;
        static readonly object _speech_locker = new object();

        public string Parse(string args) {
            return Parse(args, WebServer.Request.DataType.text, false);
        }

        internal string Parse(string args, WebServer.Request.DataType dataType, bool disableSpeech) {
            try {
                if (args.Contains("{mute}") || args.Contains("{widget}")) {
                    args = Regex.Replace(args, "{mute}|{widget}", string.Empty);
                    disableSpeech = true;
                }

                if (args.Contains("</lock>")) // Lock is used to protect an Action content and should not be normally parsed
                    return dataType.Equals(WebServer.Request.DataType.html) ? JudoParser.Parse(args).Replace("\n", "<br />") : JudoParser.Parse(args);

                var instructionSets = args.Replace('&', ';').Split(';')
                                          .Where(s => !string.IsNullOrWhiteSpace(s))
                                          .Select(s => s.Trim())
                                          .Distinct().ToList();

                var results = new Dictionary<string, KeyValuePair<string, string>>();

                instructionSets.ForEach(instruction => results.Add(
                    instruction.Replace(" ", "_").Replace("%", string.Empty),
                    // Remove blank spaces of judo command and %% of functions to generate a friendly and readable key
                    new KeyValuePair<string, string>(instruction,
                    // Remove extra white spaces
                    Regex.Replace(Execute(instruction, disableSpeech), @"[^\S\r\n]+", " "))));

                switch (dataType) {
                    case WebServer.Request.DataType.html:
                        return results.ToDictString().Replace("<", "&lt;").Replace(">", "&gt;").Replace("\n", "<br />");
                    case WebServer.Request.DataType.json:
                        return results.ToJson();
                }
                return results.ToDictString();
            }
            catch (Exception e) {
                if (!e.Message.Contains("Parameter name: length")) {
                    Logger.Instance.Append(string.Format("obj [ Parser.Parse <Exception> ] Argument: [ {0} ] Exception Message: [ {1} ]", args, e.Message));
                }
                return e.Message;
            }
        }

        string Execute(string arg, bool disableSpeech) {
            string output = string.Empty;
            var method = Methods.Instance;

            if (arg.StartsWith("%") ||
                arg.StartsWith("./") ||
                arg.StartsWith("judo")) {
                return arg.ParseTokens();
            }
            else {
                XmlNodeList xList = InstructionSet.GetInstructionSet(arg.Replace("*", string.Empty));

                if (xList.Count <= 0 && !arg.Contains("*")) {
                    if (output == string.Empty) {
                        Logger.Instance.Append(string.Format("obj [ Parser.Execute ] Argument: [ {0}, not found. ]", arg));
                        output = arg + ", not found.";
                    }
                }
                else {
                    int pos = 0;
                    foreach (XmlNode nodeItem in xList) {
                        output += string.Format("{0}\r\n", nodeItem.InnerText.ParseTokens(pos));
                        pos++;
                    }
                }
            }

            if (method.HasInternetConnection() &&       // Is connected to the Internet
                !User.Status && !disableSpeech &&       // Is not a widget
                !string.IsNullOrWhiteSpace(output)      // Has something to send
                && File.Exists(method.GetApplicationPath + ".smtpsettings")) {

                Process.CallWithTimeout(() => new Mail().Send(
                    MailHeaders.GetMailHeaderFrom,
                    MailHeaders.GetMailHeaderTo,
                    MailHeaders.GetMailHeaderSubject,
                    output), 10000);
            }

            if (!string.IsNullOrWhiteSpace(output) && !Mute && !disableSpeech) {
                Task.Run(() => SayText(output));
            }

            if (!ParserState) {
                Thread.Sleep(1000);
                System.Environment.Exit(0);
            }

            return string.Format("{0}", output.Trim());
        }

        public void SayText(string sText) {
            lock (_speech_locker) {
                sText = sText.Replace("_", " ");
                if (OperatingSystem.Version == OperatingSystem.Type.Unix) {
                    if (File.Exists("/usr/bin/festival"))
                        Process.Instance.Start(string.Format("festival -b '(SayText \"{0}\")'", sText));
                    else
                        Process.Instance.Start("say " + sText);
                }
                else {
                    string jspeechPath = Methods.Instance.GetApplicationPath + "jspeech.exe";
                    if (File.Exists(jspeechPath))
                        Process.Instance.Start(jspeechPath, sText);
                }
            }
        }
    }
}