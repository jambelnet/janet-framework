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

using jaNET.Infrastructure;
using System;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;

namespace jaNET.Services;

/// <summary>Version and copyright text (%about%, %copyright%, the banner), including a notice when a newer version exists.</summary>
internal sealed class AppInfo
{
    /// <summary>The product version, e.g. "1.0.0" or "1.0.0-rc.1": the one in Directory.Build.props, without the commit the SDK adds after a "+".</summary>
    public static readonly string Version = ReadVersion();

    static string ReadVersion() {
        string version = typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
        int build = version.IndexOf('+');
        return build < 0 ? version : version.Substring(0, build);
    }

    static readonly TimeSpan UpdateCheckLifetime = TimeSpan.FromHours(1);
    const int UpdateCheckTimeoutMs = 5000;

    readonly IHttpFetcher _http;
    readonly IClock _clock;
    readonly object _gate = new();

    DateTime _checkedAt = DateTime.MinValue;
    bool _updateAvailable;

    public AppInfo(IHttpFetcher http, IClock clock) {
        _http = http;
        _clock = clock;
    }

    public string Copyright {
        get {
            string text = $"jaNET Framework [Version {Version}]\r\nCopyright (c) 2010-{_clock.Now.Year} J@mBeL.net";

            if (UpdateAvailable())
                text += "\r\n\r\nNew update available.\r\nPlease visit http://www.jubito.org/download.html";

            return text;
        }
    }

    /// <summary>Asks the project site for the latest version (at most once an hour, giving up after 5 seconds).</summary>
    public bool UpdateAvailable() {
        lock (_gate) {
            if (_clock.Now - _checkedAt < UpdateCheckLifetime) return _updateAvailable;

            _updateAvailable = IsNewerVersionPublished();
            _checkedAt = _clock.Now;
            return _updateAvailable;
        }
    }

    /// <summary>
    /// Is the published version newer than ours? A version such as "1.2.0" or "1.0.0-rc.1" is compared as one: a higher number wins, and a
    /// release is newer than its own pre-release. The file that older versions read held a number without dots ("3194" for 0.3.1.94): those
    /// versions are all older than 1.0, so such a number never announces an update.
    /// </summary>
    internal static bool IsNewer(string published, string current) {
        if (!TryParse(published, out Version? newest, out bool newestIsPreRelease) || !TryParse(current, out Version? ours, out bool oursIsPreRelease))
            return false;

        int order = newest!.CompareTo(ours!);
        return order > 0 || (order == 0 && oursIsPreRelease && !newestIsPreRelease);
    }

    static bool TryParse(string text, out Version? version, out bool preRelease) {
        text = text.Trim().TrimStart('v', 'V');
        int dash = text.IndexOf('-');
        preRelease = dash >= 0;
        string numbers = dash < 0 ? text : text.Substring(0, dash);

        version = null;
        return numbers.Contains('.') && System.Version.TryParse(numbers, out version);
    }

    bool IsNewerVersionPublished() {
        try {
            return IsNewer(_http.Get("http://www.jubito.org/current-version.txt", UpdateCheckTimeoutMs), Version);
        }
        catch (Exception e) when (e is HttpRequestException || e is TaskCanceledException) {
            return false;   // not found, no connection or timeout
        }
    }
}
