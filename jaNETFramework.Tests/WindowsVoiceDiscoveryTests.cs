/* (c) J@mBeL.net 2010-2026, John Ambeliotis. This file is part of jaNET Framework.
 * Licensed under the GNU General Public License, version 3 or later; see LICENSE. */
using jaNET.Services;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using static jaNETFramework.Tests.ProcessTestState;

namespace jaNETFramework.Tests;

public sealed class WindowsVoiceDiscoveryTests
{
    const string Voices = """[{"Id":"installed-zira","Name":"Microsoft Zira","Language":"en-US"}]""";

    static ProcessStartInfo Probe(params string[] arguments) {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        string configuration = folder.Parent.Name;
        while (!File.Exists(Path.Combine(folder.FullName, "jaNETFramework.sln"))) folder = folder.Parent;
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet") {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        start.ArgumentList.Add(Path.Combine(folder.FullName, "tools", "ProcessProbe", "bin", configuration, "net10.0", "ProcessProbe.dll"));
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        return start;
    }

    [Fact]
    public async Task ColdDiscoveryBeyondTheOldTenSecondLimitStillReadsTheInstalledVoice() {
        using var app = new TempApp();
        string pid = Path.Combine(app.Directory, "probe.pid");
        Assert.Equal(TimeSpan.FromSeconds(30), SpeechService.VoiceDiscoveryTimeout);
        InstalledSpeechVoice[] voices = await SpeechService.ReadWindowsVoices(Probe("delayed-output", "11000", Voices, pid), CancellationToken.None);
        Assert.Equal("installed-zira", Assert.Single(voices).Id);
        Assert.Equal("Microsoft Zira", voices[0].Name);
        Assert.False(IsRunning(int.Parse(File.ReadAllText(pid))));
    }

    [Fact]
    public async Task AnInternalDeadlineReportsAnActionableErrorAndStopsTheProbe() {
        using var app = new TempApp();
        string pid = Path.Combine(app.Directory, "probe.pid");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => SpeechService.ReadWindowsVoices(
            Probe("delayed-output", "60000", Voices, pid), CancellationToken.None, TimeSpan.FromSeconds(2)));
        Assert.Contains("timed out after 2 seconds", error.Message);
        Assert.Contains("Reopen Voice settings", error.Message);
        if (ReadPid(pid) is int processId) Assert.True(SpinWait.SpinUntil(() => !IsRunning(processId), 2000));
    }

    [Fact]
    public async Task CallerCancellationIsNotReclassifiedAsADeadlineOrAnEmptyVoiceList() {
        using var app = new TempApp();
        string pid = Path.Combine(app.Directory, "probe.pid");
        using var cancellation = new CancellationTokenSource();
        Task<InstalledSpeechVoice[]> discovery = SpeechService.ReadWindowsVoices(Probe("delayed-output", "60000", Voices, pid), cancellation.Token);
        int? processId = null;
        try {
            for (int attempt = 0; attempt < 400 && (processId = ReadPid(pid)) == null && !discovery.IsCompleted; attempt++) await Task.Delay(50);
            Assert.True(processId.HasValue, "The cancellable discovery helper must actually start.");
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => discovery);
            Assert.True(SpinWait.SpinUntil(() => !IsRunning(processId.Value), 2000));
        } finally {
            cancellation.Cancel();
            try { await discovery; } catch (Exception) { }
        }
    }

    [Fact]
    public async Task AlreadyCancelledDiscoveryDoesNotStartAnExternalProgram() {
        var start = new ProcessStartInfo("janet-program-that-does-not-exist");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SpeechService.ReadWindowsVoices(start, new CancellationToken(true)));
    }

    [Fact]
    public async Task AnEngineThatExitsBeforeReadingTextReportsAnEngineFailureNotABrokenPipe() {
        using var app = new TempApp();
        using var speech = new SpeechService(new MemorySettingsStore(), app.Paths, _ => {
            var start = Probe("fail");
            start.RedirectStandardInput = true;
            Process process = Process.Start(start);
            process.WaitForExit(); // Force the exact race before jaNET writes to stdin.
            return process;
        });
        speech.Save(new SpeechSettings(Engine: "espeak", Executable: Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet"));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => speech.Synthesize("Do not leak a broken pipe.", CancellationToken.None));
        Assert.Contains("speech engine failed", error.Message);
    }

    [Fact]
    public async Task FailedOrInvalidDiscoveryNeverReturnsAUsableVoiceList() {
        await Assert.ThrowsAsync<InvalidOperationException>(() => SpeechService.ReadWindowsVoices(Probe("fail"), CancellationToken.None));
        await Assert.ThrowsAsync<System.Text.Json.JsonException>(() => SpeechService.ReadWindowsVoices(Probe("echo", "not-json"), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => SpeechService.ReadWindowsVoices(Probe("echo", """[{"Id":"","Name":"invalid","Language":"en-US"}]"""), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => SpeechService.ReadWindowsVoices(Probe("echo", "[null]"), CancellationToken.None));
    }

    static int? ReadPid(string path) {
        try { return int.TryParse(File.ReadAllText(path), out int pid) ? pid : null; }
        catch (IOException) { return null; }
    }
}
