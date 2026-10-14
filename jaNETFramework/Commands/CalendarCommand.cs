/* (c) J@mBeL.net 2010-2026, John Ambeliotis.
 * This file is part of jaNET Framework.
 * jaNET Framework is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.
 * jaNET Framework is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty
 * of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU General Public License for more details.
 * You should have received a copy of the GNU General Public License along with jaNET Framework. If not, see <http://www.gnu.org/licenses/>. */
using jaNET.Services;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Text.Unicode;

namespace jaNET.Commands;

internal sealed class CalendarCommand : JudoCommand
{
    readonly GoogleCalendarService _calendar;
    public CalendarCommand(GoogleCalendarService calendar) {
        _calendar = calendar;
        On(Agenda, "upcoming", "today", "next", "json");
        On(_ => {
            var view = JsonSerializer.SerializeToElement(_calendar.View().GetAwaiter().GetResult());
            return "Google Calendar: " + (view.GetProperty("connected").GetBoolean() ? "connected (read-only)" : "not connected");
        }, "status", "state");
        Otherwise(_ => "Use judo calendar upcoming [days] [limit], today [limit], next, json [days] [limit], or status.");
    }
    public override IReadOnlyList<string> Names { get; } = new[] { "calendar" };
    string Agenda(JudoInvocation i) {
        int Number(int index, int fallback) => i.Count <= index ? fallback : int.TryParse(i.Args[index], out var number) ? number : throw new ArgumentException("Enter a whole number for the Calendar days or limit.");
        bool today = i.Sub == "today";
        int days = today ? 1 : Number(3, 7), limit = Number(today ? 3 : 4, 10);
        if (i.Count > (today ? 4 : i.Sub == "next" ? 3 : 5)) throw new ArgumentException("Too many Calendar arguments. Use judo help calendar.");
        var agenda = _calendar.Events(i.Sub == "json" ? "upcoming" : i.Sub, days, limit).GetAwaiter().GetResult();
        if (i.Sub != "json") return GoogleCalendarService.Format(agenda);
        // Standard JSON escaping keeps data intact without introducing jaNET pointers, events or evalBool syntax into a launcher result.
        var encoder = new TextEncoderSettings(UnicodeRanges.BasicLatin); encoder.ForbidCharacters('*', '%', 'B');
        return JsonSerializer.Serialize(agenda, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Encoder = JavaScriptEncoder.Create(encoder) });
    }
}
