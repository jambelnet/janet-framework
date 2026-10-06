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

using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace jaNET.Scripting;

/// <summary>
/// Splits a judo command into arguments. Text in "double", 'single' or `back` quotes, in /* comments */ and between
/// &lt;lock&gt;...&lt;/lock&gt; stays together; the quote characters (or lock tags) are removed.
/// </summary>
internal static class ArgumentSplitter
{
    static readonly Regex Splitter = new(@"(<lock>.*?</lock>)|(""[^""]+"")|('[^']+')|(`[^`]+`)|(\/\*.*?\*\/)|[\S+]+", RegexOptions.Compiled);
    static readonly Regex Constraints = new(@"""|'|`|\/\*|\*\/", RegexOptions.Compiled);
    static readonly Regex LockTags = new("<lock>|</lock>", RegexOptions.Compiled);

    public static List<string> Split(string text) {
        return Splitter.Matches(text)
            .Select(match => match.Value.Trim())
            .Select(value => (value.Contains("</lock>") ? LockTags : Constraints).Replace(value, string.Empty))
            .ToList();
    }
}
