// (c) J@mBeL.net 2010-2026. This file is part of jaNET Framework, GNU GPL version 3 or later.
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using jaNET.ReleaseTools;

string[] runtimes = ["linux-x64", "linux-arm64", "linux-arm", "osx-arm64", "osx-x64", "win-x64", "win-arm64"];
if (args.Contains("--help"))
{
    Console.WriteLine("Usage: publish.sh [runtime ...] [--framework-dependent]\n" +
        "       publish.ps1 [-Runtime runtime,...] [-FrameworkDependent]\n" +
        "Default: all seven targets, self-contained. Requires only the .NET 10 SDK.\n" +
        "Outputs artifacts/janet-<version>-<runtime>[-framework-dependent].zip or .tar.gz.\n" +
        "Known targets: " + string.Join(", ", runtimes));
    return 0;
}

try
{
    bool dependent = args.Contains("--framework-dependent");
    string[] chosen = args.Where(arg => arg != "--framework-dependent").Distinct().ToArray();
    if (chosen.Length == 0) chosen = runtimes;
    foreach (string runtime in chosen)
        if (!runtimes.Contains(runtime)) throw new ArgumentException("Unknown runtime or option: " + runtime);

    // Resolve from the tool's build output, not the caller's working directory.
    string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
    if (!File.Exists(Path.Combine(root, "Directory.Build.props")))
        throw new DirectoryNotFoundException("Cannot find the jaNET repository root.");
    string version = Run(root, true, "msbuild", "jaNETFramework/jaNETFramework.csproj", "-getProperty:Version").Trim();
    if (!Regex.IsMatch(version, @"^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$"))
        throw new InvalidOperationException("Invalid project version: " + version);
    string output = Path.Combine(root, "artifacts");
    Directory.CreateDirectory(output);

    foreach (string runtime in chosen)
    {
        string top = $"janet-{version}-{runtime}" + (dependent ? "-framework-dependent" : "");
        string target = Path.Combine(output, top + (runtime.StartsWith("win-", StringComparison.Ordinal) ? ".zip" : ".tar.gz"));
        string staging = Directory.CreateTempSubdirectory("janet-release-").FullName;
        try
        {
            string folder = Path.Combine(staging, "publish");
            Run(root, false, "publish", "jaNETProgram", "-c", "Release", "-r", runtime,
                "--self-contained", dependent ? "false" : "true", "-o", folder, "--nologo");
            foreach (string file in new[] { "LICENSE", "CHANGELOG.md", "THIRD-PARTY-NOTICES.md", "README.md" })
                File.Copy(Path.Combine(root, file), Path.Combine(folder, file));
            if (runtime.StartsWith("linux-", StringComparison.Ordinal))
                File.Copy(Path.Combine(root, "deploy", "janet.service"), Path.Combine(folder, "janet.service"));
            ReleaseArchive.Create(target, top, folder, runtime.StartsWith("win-", StringComparison.Ordinal));
            Console.WriteLine($"Created: {target} ({new FileInfo(target).Length / 1024 / 1024} MB)");
        }
        finally
        {
            // Only the unique directory created by this invocation is removed.
            Directory.Delete(staging, recursive: true);
        }
    }
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine("Release packaging failed: " + error.Message);
    return 1;
}

static string Run(string root, bool capture, params string[] arguments)
{
    var start = new ProcessStartInfo("dotnet") { WorkingDirectory = root, UseShellExecute = false, RedirectStandardOutput = capture };
    foreach (string argument in arguments) start.ArgumentList.Add(argument);
    using Process process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start dotnet.");
    string result = capture ? process.StandardOutput.ReadToEnd() : string.Empty;
    process.WaitForExit();
    if (process.ExitCode != 0) throw new InvalidOperationException($"dotnet {arguments[0]} failed (exit {process.ExitCode}).");
    return result;
}
