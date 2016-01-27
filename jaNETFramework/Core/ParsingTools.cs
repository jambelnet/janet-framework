/* *****************************************************************************************************************************
 * (c) J@mBeL.net 2010-2016
 * Author: John Ambeliotis
 * Created: 24 Apr. 2010
 *
 * License:
 *  This file is part of Project jaNET.

    Project jaNET is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    Project jaNET is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with Project jaNET. If not, see <http://www.gnu.org/licenses/>. */

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace jaNETFramework
{
    class ParsingTools
    {
        internal static string[] SplitArguments(string arg)
        {
            var argList = new List<String>();
            MatchCollection mItems = Regex.Matches(arg, @"(<lock>.*?</lock>)|(""[^""]+"")|('[^']+')|(`[^`]+`)|(\/\*.*?\*\/)|[\S+]+");

            foreach (Match matchString in mItems)
                if (!matchString.Value.Contains("</lock>"))
                    argList.Add(matchString.Value.Replace("/*", string.Empty)
                                                 .Replace("*/", string.Empty)
                                                 .Replace("`", string.Empty)
                                                 .Replace("'", string.Empty)
                                                 .Replace("\"", string.Empty)
                                                 .Trim());
                else
                    argList.Add(matchString.Value.Replace("<lock>", string.Empty)
                                                 .Replace("</lock>", string.Empty)
                                                 .Trim());

            return argList.ToArray();
        }
        
        internal static string ParseTokens(string sValue)
        {
            if (sValue.Contains("%"))
                if (sValue.Substring(0, 1) == "%")
                    return string.Format("{0}\r\n", sValue.ToLower().ToValues());
                else
                    sValue = sValue.ToValues();

            while (sValue.Contains("*"))
            {
                MatchCollection mItems = Regex.Matches(sValue, @"[*][a-zA-Z0-9_-]+");

                if (mItems.Count > 0)
                    foreach (Match matchString in mItems)
                    {
                        if (matchString.Success)
                        {
                            string retval = ParseTokens(Methods.Instance.GetInstructionSet(matchString.ToString().Trim()).Item(0).InnerText);
                            sValue = sValue.Replace(matchString.ToString().Trim(), retval);
                        }
                    }
                else
                    break;
            }

            if (sValue.Contains("evalBool"))
                sValue = Evaluator.EvaluateCondition(sValue);

            if (sValue.Contains("./") || sValue.Contains("judo"))
            {
                if (sValue.Substring(0, 2).Contains("./"))
                {
                    MatchCollection mItems = Regex.Matches(sValue, @"('[^']+')|(`[^`]+`)");

                    String fileName = string.Empty;
                    String arguments = string.Empty;

                    if (mItems.Count == 0)
                        fileName = sValue.Replace("./", string.Empty);
                    else if (mItems.Count == 1)
                        fileName = mItems[0].Value.Replace("`", string.Empty).Replace("'", string.Empty);
                    if (mItems.Count == 2)
                    {
                        fileName = mItems[0].Value.Replace("`", string.Empty).Replace("'", string.Empty);
                        arguments = mItems[1].Value.Replace("`", string.Empty).Replace("'", string.Empty);
                    }

                    return Process.Instance.Start(fileName, arguments);
                }
                switch (sValue.Substring(0, 4))
                {
                    case "judo":
                        return sValue.Replace(sValue, Judoers.JudoParser(sValue));
                } // + "\r\n"; Causing problem to evaluation - need solution
            }
            return sValue;
        }
    }
}
