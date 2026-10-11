// (c) J@mBeL.net 2010-2026. This file is part of jaNET Framework, GNU GPL version 3 or later.
using jaNET.Infrastructure;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Xunit;

namespace jaNETFramework.Tests;

public sealed class ProcessRunnerTests
{
    static string Probe {
        get {
            var folder = new DirectoryInfo(AppContext.BaseDirectory);
            string configuration = folder.Parent.Name;
            while (!File.Exists(Path.Combine(folder.FullName, "jaNETFramework.sln"))) folder = folder.Parent;
            return Path.Combine(folder.FullName, "tools", "ProcessProbe", "bin", configuration, "net10.0", "ProcessProbe.dll");
        }
    }
    static string Dotnet => Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
    static string Arguments(params string[] arguments) => string.Join(" ", new[] { Probe }.Concat(arguments).Select(value => "\"" + value.Replace("\"", "\\\"") + "\""));
    static ProcessRunner Runner() => new(new MemoryLog());

    [Fact]
    public void CapturesStdoutExactlyWithoutReplacingItWithStderr() {
        Assert.Equal("hello\r\nworld", Runner().Run(Dotnet, Arguments("echo", "hello\r\nworld")));
        Assert.Equal(new string('o', 131072), Runner().Run(Dotnet, Arguments("both")));
    }

    [Fact]
    public void ReportsFailedStartsExitCodesAndOversizedOutput() {
        Assert.StartsWith("Error: could not run", Runner().Run("janet-missing-" + Guid.NewGuid().ToString("N")));
        string failed = Runner().Run(Dotnet, Arguments("fail"));
        Assert.Contains("exited with code 7", failed); Assert.Contains("probe failed", failed);
        Assert.Contains("capture limit", Runner().Run(Dotnet, Arguments("overflow")));
        Assert.StartsWith("Error:", Runner().Run(Dotnet, timeoutMs: 0));
    }

    [Theory]
    [InlineData("sleep")]
    [InlineData("child")]
    [InlineData("pipe")]
    public void DeadlineCoversTheProcessTreeAndInheritedPipes(string mode) {
        string folder = Directory.CreateTempSubdirectory("janet-process-test-").FullName;
        string child = Path.Combine(folder, "child.pid"), parent = Path.Combine(folder, "parent.pid");
        try {
            var clock = Stopwatch.StartNew();
            string result = Runner().Run(Dotnet, Arguments(mode, child, parent), timeoutMs: 4000);
            Assert.Contains("timed out after 4 seconds", result);
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(12), result);
            Assert.True(File.Exists(child), "The child process must actually start for this test.");
            if (mode != "pipe") Assert.False(IsRunning(int.Parse(File.ReadAllText(child))));
            if (File.Exists(parent)) Assert.False(IsRunning(int.Parse(File.ReadAllText(parent))));
        }
        finally {
            // Only PIDs written by this invocation's test helper are eligible for cleanup.
            foreach (string file in new[] { child, parent }) if (File.Exists(file)) {
                try { using Process process = Process.GetProcessById(int.Parse(File.ReadAllText(file))); if (!process.HasExited) { process.Kill(entireProcessTree: true); process.WaitForExit(2000); } }
                catch (ArgumentException) { }
            }
            Directory.Delete(folder, recursive: true);
        }
    }

    static bool IsRunning(int pid) {
        try { using Process process = Process.GetProcessById(pid); return !process.HasExited; }
        catch (ArgumentException) { return false; }
    }

    [Fact]
    public void LaunchModeIsVisibleDetachedAndNeverCapturesStreams() {
        var launch = ProcessRunner.StartInfo("notepad.exe", "file.txt", launch: true);
        Assert.True(launch.UseShellExecute); Assert.False(launch.CreateNoWindow);
        Assert.Equal(ProcessWindowStyle.Normal, launch.WindowStyle);
        Assert.False(launch.RedirectStandardOutput); Assert.False(launch.RedirectStandardError);
        Assert.Equal("file.txt", launch.Arguments);
        Assert.Equal(30000, ProcessRunner.DefaultTimeoutMs);
        bool started = false;
        var runner = new ProcessRunner(new MemoryLog(), info => {
            started = true; Assert.Equal("notepad.exe", info.FileName); Assert.True(info.UseShellExecute);
            return null;
        });
        Assert.Equal("Application started: notepad.exe", runner.Launch("notepad.exe"));
        Assert.True(started);
        var failed = new ProcessRunner(new MemoryLog(), _ => throw new InvalidOperationException("start failed"));
        Assert.StartsWith("Error: could not start", failed.Launch("missing.exe"));
    }

    [Fact]
    public void LegacyActionsAndExplicitTimeoutsWorkThroughInstructionPointers() {
        using var host = new TestHost();
        string action = "'" + Dotnet + "' '" + Arguments("echo", "answer") + "'";
        Assert.Equal("answer", host.Run("./" + action).Trim());
        Assert.Contains("Element added.", host.Run("judo inset add probe <lock>./[capture:20]" + action + "</lock>"));
        Assert.Equal("answer", host.Run("probe").Trim());
        Assert.StartsWith("Error:", host.Run("./[capture:0]notepad.exe"));
        Assert.StartsWith("Error:", host.Run("./[capture:301]notepad.exe"));
        Assert.StartsWith("Error:", host.Run("./[capture:abc]notepad.exe"));
        Assert.Contains("Launch Application", host.Run("judo help process"));
    }
}
