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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace jaNETFramework
{
    class ParsingTools
    {
        internal static List<String> SplitArguments(string arg) {
            string splitter = @"(<lock>.*?</lock>)|(""[^""]+"")|('[^']+')|(`[^`]+`)|(\/\*.*?\*\/)|[\S+]+"; // Split arguments
            string replaceConstraints = @"""|'|`|\/\*|\*\/"; // Replace constraints
            string replaceLocker = @"<lock>|</lock>"; // Replace locker tags

            string pattern = arg.Contains("</lock>") ? replaceLocker : replaceConstraints;

            var ls = new List<String>();
            var mItems = Regex.Matches(arg, splitter);

            mItems.Cast<Match>().ToList()
                                .ForEach(matchString => ls.Add(Regex.Replace(matchString.Value.Trim(), pattern, string.Empty)));

            return ls;
        }

        internal static string ParseTokens(string sValue) {
            if (sValue.Contains("%"))
                sValue = sValue.ToValues();

            while (sValue.Contains("*")) {
                var mItems = Regex.Matches(sValue, @"[*][a-zA-Z0-9_-]+");

                if (mItems.Count > 0)
                    foreach (Match matchString in mItems) {
                        if (matchString.Success) {
                            sValue = sValue.Replace(matchString.Value.Trim(),
                                                    ParseTokens(Methods.Instance.GetInstructionSet(matchString.Value.Trim()).Item(0).InnerText));
                        }
                    }
                else
                    break;
            }

            if (sValue.Contains("evalBool"))
                sValue = Evaluator.EvaluateCondition(sValue);

            if (sValue.Contains("./") || sValue.Contains("judo")) {
                if (sValue.StartsWith("./")) {
                    var mItems = Regex.Matches(sValue, @"('[^']+')|(`[^`]+`)");

                    string fileName = string.Empty;
                    string arguments = string.Empty;

                    if (mItems.Count == 0)
                        fileName = sValue.Replace("./", string.Empty);
                    else if (mItems.Count == 1)
                        fileName = mItems[0].Value.Replace("`", string.Empty).Replace("'", string.Empty);
                    if (mItems.Count == 2) {
                        fileName = mItems[0].Value.Replace("`", string.Empty).Replace("'", string.Empty);
                        arguments = mItems[1].Value.Replace("`", string.Empty).Replace("'", string.Empty);
                    }

                    return Process.Instance.Start(fileName, arguments);
                }
                if (sValue.StartsWith("judo"))
                    return sValue.Replace(sValue, Judoers.JudoParser(sValue));
                // + "\r\n"; Causing problem to evaluation - need solution
            }
            return sValue;
        }
    }
}
