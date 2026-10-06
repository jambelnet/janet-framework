/* *****************************************************************************************************************************
 * (c) J@mBeL.net 2010-2017
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

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace jaNET.Infrastructure;

/// <summary>
/// Replacement for System.Web.Script.Serialization.JavaScriptSerializer, which is not available on modern .NET.
/// The output format of the judo web API (mode=json) is kept byte-for-byte identical to what the old serializer produced.
/// </summary>
static class JsonCompat
{
    /// <summary>
    /// Serializes the result set of a parsed instruction as {"key":{"Key":"key","Value":"value"}, ...}.
    /// </summary>
    internal static string Serialize(IEnumerable<KeyValuePair<string, KeyValuePair<string, string>>> results) {
        var sb = new StringBuilder("{");
        bool first = true;

        foreach (var entry in results) {
            if (!first)
                sb.Append(',');
            first = false;

            Quote(sb, entry.Key);
            sb.Append(":{\"Key\":");
            Quote(sb, entry.Value.Key);
            sb.Append(",\"Value\":");
            Quote(sb, entry.Value.Value);
            sb.Append('}');
        }

        return sb.Append('}').ToString();
    }

    // Same escaping rules as JavaScriptSerializer: HTML-sensitive characters and a few line separators are escaped,
    // all other characters (including non-ASCII ones) are written as they are.
    static void Quote(StringBuilder sb, string value) {
        sb.Append('"');

        foreach (char c in value ?? string.Empty) {
            switch (c) {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '<':
                case '>':
                case '\'':
                case '&':
                case (char)0x0085:
                case (char)0x2028:
                case (char)0x2029:
                    sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    break;
                default:
                    if (c < ' ')
                        sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else
                        sb.Append(c);
                    break;
            }
        }

        sb.Append('"');
    }

    /// <summary>
    /// Walks a '/' separated path (object keys or array indexes) and returns the textual value found there,
    /// or an empty string when a JSON null is hit on the way.
    /// </summary>
    internal static string SelectValue(string json, string path) {
        using (JsonDocument doc = JsonDocument.Parse(json)) {
            JsonElement item = doc.RootElement;

            foreach (string step in path.Split('/')) {
                item = int.TryParse(step, out int index)
                    ? ElementAt(item, index)
                    : item.GetProperty(step);

                if (item.ValueKind == JsonValueKind.Null)
                    return string.Empty;
            }

            return Format(item);
        }
    }

    static JsonElement ElementAt(JsonElement array, int index) {
        if (array.ValueKind != JsonValueKind.Array || index < 0 || index >= array.GetArrayLength())
            throw new ArgumentOutOfRangeException("index", "Index was out of range. Must be non-negative and less than the size of the collection.");

        return array[index];
    }

    static string Format(JsonElement item) {
        switch (item.ValueKind) {
            case JsonValueKind.String:
                return item.GetString() ?? string.Empty;
            case JsonValueKind.True:
                return bool.TrueString;
            case JsonValueKind.False:
                return bool.FalseString;
            case JsonValueKind.Number:
                if (item.TryGetInt32(out int i)) return i.ToString();
                if (item.TryGetInt64(out long l)) return l.ToString();
                if (!item.GetRawText().Contains("e") && !item.GetRawText().Contains("E") && item.TryGetDecimal(out decimal m)) return m.ToString();
                return item.GetDouble().ToString();
            default:
                return item.GetRawText();
        }
    }
}
