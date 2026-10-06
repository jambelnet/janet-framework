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

using System;
using System.Text;
using System.Threading;

namespace jaNET.Servers;

/// <summary>Topic names and filters as MQTT defines them.</summary>
internal static class MqttTopic
{
    const int MaxLength = 65535;

    /// <summary>A topic a message is published to: not empty, no wildcards.</summary>
    public static bool IsValidName(string topic) =>
        topic.Length is > 0 and <= MaxLength && topic.IndexOfAny(new[] { '+', '#', (char)0 }) < 0;

    /// <summary>A topic filter: "+" stands for one level and "#" for all the levels below, as the last level only.</summary>
    public static bool IsValidFilter(string filter) {
        if (filter.Length is 0 or > MaxLength || filter.Contains((char)0)) return false;

        string[] levels = filter.Split('/');
        for (int i = 0; i < levels.Length; i++) {
            string level = levels[i];
            if (level.Contains('#') && (level != "#" || i != levels.Length - 1)) return false;
            if (level.Contains('+') && level != "+") return false;
        }
        return true;
    }

    /// <summary>Does the topic of a message belong to the filter? "home/+/temp" matches "home/kitchen/temp"; "home/#" matches "home" and everything below.</summary>
    public static bool Matches(string filter, string topic) {
        string[] f = filter.Split('/');
        string[] t = topic.Split('/');

        if (t[0].StartsWith('$') && (f[0] == "#" || f[0] == "+")) return false;       // system topics are not for wildcards

        for (int i = 0; i < f.Length; i++) {
            if (f[i] == "#") return true;
            if (i >= t.Length) return false;
            if (f[i] != "+" && f[i] != t[i]) return false;
        }
        return f.Length == t.Length;
    }

    /// <summary>
    /// Text from the outside made safe to put into an instruction: only letters, digits and . , : _ / + @ # = - are kept, anything else
    /// (spaces too) becomes "_", and it is cut at <paramref name="maxLength"/>. Text that could carry ; &amp; { } " ` % * [ or a space would
    /// otherwise be able to run what it likes once it is expanded: %mqttpayload% inside a condition, a command or as a whole instruction
    /// (an expanded text that starts with "judo" or "./" is executed, so a leading "./" is defused as well).
    /// </summary>
    public static string Safe(string text, int maxLength = 200) {
        var safe = new StringBuilder(Math.Min(text.Length, maxLength));
        foreach (char c in text) {
            if (safe.Length >= maxLength) break;
            safe.Append(char.IsAsciiLetterOrDigit(c) || ".,:_/+@#=-".Contains(c) ? c : '_');
        }
        if (safe.Length >= 2 && safe[0] == '.' && safe[1] == '/') safe[0] = '_';
        return safe.ToString();
    }
}

/// <summary>
/// The message whose action is being run: %mqtttopic%, %mqttpayload% and %mqttnumber% read it. It belongs to the running action
/// (and what that starts), so messages that arrive at the same time do not mix. The values are made safe, see <see cref="MqttTopic.Safe"/>.
/// </summary>
internal static class MqttMessage
{
    static readonly AsyncLocal<(string Topic, string Payload)> Current = new();

    public static void Set(string topic, string payload) => Current.Value = (MqttTopic.Safe(topic), MqttTopic.Safe(payload));

    public static string Topic => Current.Value.Topic ?? string.Empty;

    public static string Payload => Current.Value.Payload ?? string.Empty;

    /// <summary>The first number in the payload ("21.5" in "temperature 21.5 C"), or nothing; a sign and one decimal point are kept.</summary>
    public static string Number {
        get {
            string payload = Payload;
            for (int i = 0; i < payload.Length; i++) {
                bool digit = char.IsAsciiDigit(payload[i]);
                bool sign = (payload[i] == '-' || payload[i] == '+') && i + 1 < payload.Length && char.IsAsciiDigit(payload[i + 1]);
                if (!digit && !sign) continue;

                int end = i + 1;
                bool dot = false;
                while (end < payload.Length && (char.IsAsciiDigit(payload[end]) || (!dot && payload[end] == '.' && end + 1 < payload.Length && char.IsAsciiDigit(payload[end + 1])))) {
                    if (payload[end] == '.') dot = true;
                    end++;
                }
                return payload.Substring(i, end - i);
            }
            return string.Empty;
        }
    }
}
