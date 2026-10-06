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
using System.IO;

namespace jaNET.Services;

/// <summary>Text to speech and the mute switch (%mute% / %unmute%). Provide your own through <see cref="Hosting.JanetHostOptions.Speaker"/>.</summary>
public interface ISpeaker
{
    /// <summary>True while jaNET is muted ("%mute%"): the answers of instructions are not spoken.</summary>
    bool Muted { get; set; }

    /// <summary>Speaks the text. It is called on a background thread, one call at a time is not guaranteed.</summary>
    void Say(string text);
}

/// <summary>
/// Speaks through what the system offers: jspeech.exe next to the program on Windows,
/// festival or say on Linux and macOS. Does nothing when none of them is installed.
/// </summary>
internal sealed class SystemSpeaker : ISpeaker
{
    static readonly object Gate = new();

    readonly AppPaths _paths;
    readonly ProcessRunner _processes;

    public SystemSpeaker(AppPaths paths, ProcessRunner processes) {
        _paths = paths;
        _processes = processes;
    }

    public bool Muted { get => _muted; set => _muted = value; }
    volatile bool _muted;

    public void Say(string text) {
        lock (Gate) {
            text = text.Replace("_", " ");

            if (OperatingSystem.IsWindows()) {
                string speech = _paths.ProgramFile("jspeech.exe");
                if (File.Exists(speech))
                    _processes.Run(speech, text);
            }
            else if (File.Exists("/usr/bin/festival"))
                _processes.RunCommandLine($"festival -b '(SayText \"{text}\")'");
            else
                _processes.RunCommandLine("say " + text);
        }
    }
}
