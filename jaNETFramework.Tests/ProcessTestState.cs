/* (c) J@mBeL.net 2010-2026, John Ambeliotis. This file is part of jaNET Framework.
 * Licensed under the GNU General Public License, version 3 or later; see LICENSE. */
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace jaNETFramework.Tests;

internal static class ProcessTestState
{
    internal static bool IsRunning(int pid, Func<IEnumerable<string>> readLinuxStatus = null) {
        if (OperatingSystem.IsLinux() || readLinuxStatus != null) {
            try {
                var lines = readLinuxStatus != null ? readLinuxStatus() : File.ReadLines($"/proc/{pid}/status");
                if (LinuxStateHasExited(lines.FirstOrDefault(line => line.StartsWith("State:", StringComparison.Ordinal)))) return false;
            }
            catch (IOException) {
                // /proc can disappear even after opening it. Confirm exit below;
                // a failed status read alone must not mark a live process dead.
            }
        }
        try {
            using Process process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException) { return false; }
    }

    internal static bool LinuxStateHasExited(string line) =>
        line?.Split((char[])null, StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(1) is "Z" or "X";
}
