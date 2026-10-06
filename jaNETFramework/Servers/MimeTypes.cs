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

using jaNET.Scripting;
using System;
using System.Collections.Generic;
using System.IO;

namespace jaNET.Servers;

/// <summary>
/// Content types of what the built-in web server sends. Browsers refuse to run ES modules and are strict about
/// style sheets when the type is missing, so every response states it.
/// </summary>
internal static class MimeTypes
{
    internal const string Html = "text/html; charset=utf-8";

    static readonly Dictionary<string, string> ByExtension = new(StringComparer.OrdinalIgnoreCase) {
        { ".html", Html },
        { ".htm", Html },
        { ".css", "text/css; charset=utf-8" },
        { ".js", "text/javascript; charset=utf-8" },
        { ".mjs", "text/javascript; charset=utf-8" },
        { ".json", "application/json; charset=utf-8" },
        { ".webmanifest", "application/manifest+json; charset=utf-8" },
        { ".xml", "application/xml; charset=utf-8" },
        { ".txt", "text/plain; charset=utf-8" },
        { ".map", "application/json; charset=utf-8" },
        { ".svg", "image/svg+xml" },
        { ".png", "image/png" },
        { ".jpg", "image/jpeg" },
        { ".jpeg", "image/jpeg" },
        { ".gif", "image/gif" },
        { ".ico", "image/x-icon" },
        { ".woff2", "font/woff2" }
    };

    /// <summary>Content type for a file, or null when the extension is unknown (the browser then decides).</summary>
    internal static string? ForFile(string path) =>
        ByExtension.TryGetValue(Path.GetExtension(path), out string? type) ? type : null;

    /// <summary>Content type of a judo API answer (?cmd=...&amp;mode=...).</summary>
    internal static string ForCommand(ResponseFormat format) => format switch {
        ResponseFormat.Json => "application/json; charset=utf-8",
        ResponseFormat.Text => "text/plain; charset=utf-8",
        _ => Html
    };
}
