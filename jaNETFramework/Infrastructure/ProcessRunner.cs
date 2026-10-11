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
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace jaNET.Infrastructure;

/// <summary>Starts external programs (the "./program" syntax, text to speech) and returns what they print.</summary>
internal sealed class ProcessRunner
{
    internal const int DefaultTimeoutMs = 30_000;
    internal const int MaximumTimeoutMs = 300_000;
    const int OutputLimit = 1_048_576;
    readonly ILog _log;
    readonly Func<ProcessStartInfo, Process?> _launch;

    public ProcessRunner(ILog log, Func<ProcessStartInfo, Process?>? launch = null) {
        _log = log;
        _launch = launch ?? Process.Start;
    }

    /// <summary>"program arg1 arg2": everything after the first space is the argument string.</summary>
    public string RunCommandLine(string commandLine) {
        int space = commandLine.Trim().IndexOf(' ');
        if (space < 0) return Run(commandLine);

        // the original split on the first space of the untrimmed text
        int first = commandLine.IndexOf(' ');
        return Run(commandLine.Substring(0, first), commandLine.Substring(first));
    }

    /// <summary>Captures stdout with a finite deadline, including the time spent draining output pipes.</summary>
    public string Run(string fileName, string arguments = "", int timeoutMs = DefaultTimeoutMs) =>
        RunAsync(fileName, arguments, timeoutMs).GetAwaiter().GetResult();

    internal static ProcessStartInfo StartInfo(string fileName, string arguments, bool launch) => new(fileName, arguments) {
        UseShellExecute = launch,
        RedirectStandardOutput = !launch,
        RedirectStandardError = !launch,
        CreateNoWindow = !launch,
        WindowStyle = launch ? ProcessWindowStyle.Normal : ProcessWindowStyle.Hidden
    };

    public string Launch(string fileName, string arguments = "") {
        if (string.IsNullOrWhiteSpace(fileName)) return "Error: a program name is required.";
        try {
            using Process? process = _launch(StartInfo(fileName, arguments, launch: true));
            return "Application started: " + fileName;
        }
        catch (Exception e) {
            _log.Write($"Process launch failed: {fileName}: {e.Message}");
            return $"Error: could not start '{fileName}': {e.Message}";
        }
    }

    async Task<string> RunAsync(string fileName, string arguments, int timeoutMs) {
        if (string.IsNullOrWhiteSpace(fileName)) return "Error: a program name is required.";
        if (timeoutMs < 1 || timeoutMs > MaximumTimeoutMs) return "Error: capture timeout must be between 1 and 300000 milliseconds.";
        using var process = new Process { StartInfo = StartInfo(fileName, arguments, launch: false) };
        using var timeout = new CancellationTokenSource(timeoutMs);
        try {
            process.Start();
            Task<(string Text, bool Overflow)> stdout = ReadOutput(process.StandardOutput, timeout.Token);
            Task<(string Text, bool Overflow)> stderr = ReadOutput(process.StandardError, timeout.Token);
            Task completion = Task.WhenAll(stdout, stderr, process.WaitForExitAsync(timeout.Token));
            // A descendant can keep a pipe open after its parent exits. That must not bypass the deadline.
            _ = completion.ContinueWith(task => { _ = task.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            try { await completion.WaitAsync(timeout.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested) {
                try {
                    if (!process.HasExited) {
                        process.Kill(entireProcessTree: true);
                        if (!process.WaitForExit(1000)) _log.Write("Capture process did not exit after termination: " + fileName);
                    }
                }
                catch (Exception error) { _log.Write("Capture process termination failed: " + fileName + ": " + error.Message); }
                string seconds = (timeoutMs / 1000d).ToString("0.###", CultureInfo.InvariantCulture);
                _log.Write($"Capture process timed out: {fileName} after {seconds} seconds.");
                return $"Error: '{fileName}' timed out after {seconds} seconds.";
            }
            if (stdout.Result.Overflow || stderr.Result.Overflow)
                return $"Error: '{fileName}' output exceeded the {OutputLimit}-character capture limit.";
            if (process.ExitCode != 0) {
                string detail = string.IsNullOrWhiteSpace(stderr.Result.Text) ? stdout.Result.Text : stderr.Result.Text;
                return $"Error: '{fileName}' exited with code {process.ExitCode}." +
                    (string.IsNullOrWhiteSpace(detail) ? string.Empty : "\r\n" + detail.Trim());
            }
            // Successful stdout remains unchanged, even if the tool wrote warnings to stderr.
            return stdout.Result.Text;
        }
        catch (Exception error) {
            _log.Write($"Process capture failed: {fileName}: {error.Message}");
            return $"Error: could not run '{fileName}': {error.Message}";
        }
        finally { timeout.Cancel(); }
    }

    static async Task<(string Text, bool Overflow)> ReadOutput(StreamReader reader, CancellationToken token) {
        var output = new StringBuilder();
        char[] buffer = new char[4096];
        bool overflow = false;
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) > 0) {
            int keep = Math.Min(count, OutputLimit - output.Length);
            output.Append(buffer, 0, keep);
            overflow |= keep < count;
        }
        return (output.ToString(), overflow);
    }
}

internal static class TimeLimit
{
    /// <summary>Runs <paramref name="work"/> and gives up waiting after <paramref name="timeoutMs"/>. Returns true if it finished in time.</summary>
    public static bool Run(Action work, int timeoutMs) {
        Task task = Task.Run(work);
        return task.Wait(timeoutMs);
    }
}
