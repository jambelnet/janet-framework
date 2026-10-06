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
using System.Threading.Tasks;

namespace jaNET.Infrastructure;

/// <summary>Starts external programs (the "./program" syntax, text to speech) and returns what they print.</summary>
internal sealed class ProcessRunner
{
    readonly ILog _log;

    public ProcessRunner(ILog log) {
        _log = log;
    }

    /// <summary>"program arg1 arg2": everything after the first space is the argument string.</summary>
    public string RunCommandLine(string commandLine) {
        int space = commandLine.Trim().IndexOf(' ');
        if (space < 0) return Run(commandLine);

        // the original split on the first space of the untrimmed text
        int first = commandLine.IndexOf(' ');
        return Run(commandLine.Substring(0, first), commandLine.Substring(first));
    }

    /// <summary>Runs a program and waits for it. Returns its standard output, or the error message if it could not be started.</summary>
    public string Run(string fileName, string arguments = "") {
        if (fileName.Length == 0) return string.Empty;

        try {
            using var process = new Process {
                StartInfo = {
                    FileName = fileName,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                }
            };
            if (arguments.Length > 0) process.StartInfo.Arguments = arguments;

            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return output;
        }
        catch (Exception e) {
            _log.Write($"obj [ Process.Start <Exception> ] Arguments: [ {fileName} {arguments} ] Exception Message: [ {e.Message} ]");
            return e.Message;
        }
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
