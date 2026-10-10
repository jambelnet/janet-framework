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
using System.Threading;

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
/// Speaks through local Windows voices, eSpeak NG or Piper. No legacy helper executables or shell interpolation.
/// </summary>
internal sealed class SystemSpeaker : ISpeaker
{
    static readonly object Gate = new();

    readonly SpeechService _speech;
    readonly ILog _log;
    readonly object _stateGate = new();
    CancellationTokenSource? _playing;

    public SystemSpeaker(SpeechService speech, ILog log) { _speech = speech; _log = log; }

    public bool Muted {
        get => _muted;
        set {
            lock (_stateGate) {
                _muted = value;
                if (value) _playing?.Cancel();
            }
        }
    }
    volatile bool _muted;

    public void Say(string text) {
        lock (Gate) {
            if (Muted || _speech.Settings.Engine == "off") return;
            using var cancellation = new CancellationTokenSource();
            lock (_stateGate) {
                if (Muted) return;
                _playing = cancellation;
            }
            string file = Path.Combine(Path.GetTempPath(), "janet-playback-" + Guid.NewGuid().ToString("N") + ".wav");
            try {
                foreach (string chunk in SpeechService.Chunks(text.Replace("_", " "))) {
                    cancellation.Token.ThrowIfCancellationRequested();
                    byte[] audio = _speech.Synthesize(chunk, cancellation.Token).GetAwaiter().GetResult();
                    File.WriteAllBytes(file, audio);
                    cancellation.Token.ThrowIfCancellationRequested();
                    string? player = SpeechService.FindExecutable(OperatingSystem.IsWindows() ? "powershell" : OperatingSystem.IsMacOS() ? "afplay" : "aplay");
                    if (player == null) { _log.Write("Speech: no audio player found; use browser playback or install aplay."); return; }
                    var start = new System.Diagnostics.ProcessStartInfo(player) { UseShellExecute = false, CreateNoWindow = true };
                    if (OperatingSystem.IsWindows()) {
                        start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-NonInteractive");
                        start.ArgumentList.Add("-Command");
                        start.ArgumentList.Add("$ErrorActionPreference='Stop'; $p=New-Object System.Media.SoundPlayer; $p.SoundLocation=$env:JANET_AUDIO; $p.PlaySync()");
                        start.Environment["JANET_AUDIO"] = file;
                    } else start.ArgumentList.Add(file);
                    using var process = System.Diagnostics.Process.Start(start);
                    using var registration = cancellation.Token.Register(() => {
                        try { if (process != null && !process.HasExited) process.Kill(entireProcessTree: true); }
                        catch (Exception error) { _log.Write("Speech stop: " + error.Message); }
                    });
                    if (process != null) {
                        if (!process.WaitForExit(30_000)) process.Kill(entireProcessTree: true);
                        else if (!cancellation.IsCancellationRequested && process.ExitCode != 0) _log.Write("Speech: audio playback failed. Check the box's audio output.");
                    }
                }
            } catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (Exception e) { _log.Write("Speech: " + e.Message); }
            finally {
                lock (_stateGate) _playing = null;
                try { File.Delete(file); } catch (IOException) { }
            }
        }
    }
}
