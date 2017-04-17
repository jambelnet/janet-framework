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
using jaNET.Providers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace jaNET.Environment
{
    static class ParsingTools
    {
        internal static List<String> SplitArguments(this string arg) {
            const string splitter = @"(<lock>.*?</lock>)|(""[^""]+"")|('[^']+')|(`[^`]+`)|(\/\*.*?\*\/)|[\S+]+";  // Split arguments
            const string constraints = @"""|'|`|\/\*|\*\/";                                                       // Replace constraints
            const string locker = "<lock>|</lock>";                                                               // locker

            var ls = new List<String>();
            var mItems = Regex.Matches(arg, splitter);

            mItems.Cast<Match>().ToList()
                                .ForEach(matchString =>
                                ls.Add(Regex.Replace(matchString.Value.Trim(), matchString.Value.Trim().Contains("</lock>") ? locker : constraints, string.Empty)));

            return ls;
        }

        internal static string ParseTokens(this string sValue) {
            // Built-in functions
            if (sValue.Contains("%"))
                sValue = sValue.ToValues();

            // Pointers/Reflectors/Delegates
            while (sValue.Contains("*")) {
                var mItems = Regex.Matches(sValue, @"[*][a-zA-Z0-9_-]+");

                if (mItems.Count > 0)
                    foreach (Match matchString in mItems) {
                        if (matchString.Success) {
                            string ms = matchString.Value.Trim();
                            sValue = sValue.Replace(ms, ParseTokens(Methods.Instance.GetInstructionSet(ms).Item(0).InnerText));
                        }
                    }
                else
                    break;
            }

            // Evaluation
            if (sValue.Contains("evalBool"))
                sValue = Evaluator.EvaluateCondition(sValue);

            // judo API command
            if (sValue.StartsWith("judo"))
                return sValue.Replace(sValue, Judoers.JudoParser(sValue));

            // Process application
            if (sValue.StartsWith("./")) {
                var mItems = Regex.Matches(sValue, @"('[^']+')|(`[^`]+`)");

                string fileName = string.Empty;
                string arguments = string.Empty;
                // Applicaton invokation
                if (mItems.Count == 0)
                    fileName = sValue.Replace("./", string.Empty);
                // Applicaton invokation surrounded with quotes
                else if (mItems.Count == 1)
                    fileName = mItems[0].Value.Replace("`", string.Empty).Replace("'", string.Empty);
                // Applicaton invokation with arguments
                if (mItems.Count == 2) {
                    fileName = mItems[0].Value.Replace("`", string.Empty).Replace("'", string.Empty);
                    arguments = mItems[1].Value.Replace("`", string.Empty).Replace("'", string.Empty);
                }

                return Process.Instance.Start(fileName, arguments);
            }

            return sValue.Trim();
        }
    }
}
