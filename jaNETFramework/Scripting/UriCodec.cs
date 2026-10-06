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

namespace jaNET.Scripting;

/// <summary>
/// The small percent-escape table of the judo API (web server, socket server and the stored web-service addresses).
/// Kept exactly as it was, including its quirks: the table maps a few codes to other characters than the standard does
/// (%28 and %29 become braces), a space is encoded to %2520 because % is escaped after it, and decoding stops before the last entry.
/// </summary>
internal static class UriCodec
{
    static readonly string[,] CharSet = {
                {" ", "%20"},
                {"!", "%21"},
                {"\"", "%22"},
                {"#", "%23"},
                {"$", "%24"},
                {"%", "%25"},
                {"&", "%26"},
                {"'", "%27"},
                {"{", "%28"},
                {"}", "%29"},
                {"*", "%2A"},
                {"+", "%2B"},
                {",", "%2C"},
                {"-", "%2D"},
                {".", "%2E"},
                {"/", "%2F"},
                {":", "%3A"},
                {";", "%3B"},
                {"<", "%3C"},
                {"=", "%3D"},
                {">", "%3E"},
                {"?", "%3F"},
                {"@", "%40"},
                {"[", "%5B"},
                {@"\", "%5C"},
                {"]", "%5D"},
                {"^", "%5E"},
                {"_", "%5F"},
                {"`", "%60"},
                {"{", "%7B"},
                {"|", "%7C"},
                {"}", "%7D"},
                {"~", "%7E"}
            };

    public static string Decode(string uri) {
        for (int i = 0; i < CharSet.GetUpperBound(0); i++)
            uri = uri.Replace(CharSet[i, 1], CharSet[i, 0]);

        return uri;
    }

    public static string Encode(string uri) {
        for (int i = 0; i < CharSet.GetUpperBound(0); i++)
            uri = uri.Replace(CharSet[i, 0], CharSet[i, 1]);

        return uri;
    }
}
