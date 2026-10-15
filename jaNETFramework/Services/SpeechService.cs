/* (c) J@mBeL.net 2010-2026, John Ambeliotis. This file is part of jaNET Framework.
 * Licensed under the GNU General Public License, version 3 or later; see LICENSE. */

using jaNET.Configuration;
using jaNET.Infrastructure;
using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace jaNET.Services;

internal sealed record SpeechSettings(string Engine = "auto", string Executable = "espeak-ng", string Voice = "en",
    string PiperModel = "", string RecognitionModel = "", string InstructionOutput = "auto");

internal sealed record SpeechMessage(long Id, string Text, DateTimeOffset Created, string Output);
internal sealed record SpeechMessages(string Session, long LastId, bool Muted, SpeechMessage[] Events, string Output);
internal sealed record InstalledSpeechVoice(string Id, string Name, string Language);

/// <summary>Optional local speech engines. Models are loaded only when recognition is requested.</summary>
internal sealed class SpeechService : IDisposable
{
    readonly ISettingsStore _settings;
    readonly AppPaths _paths;
    readonly SemaphoreSlim _recognitionGate = new(1);
    readonly SemaphoreSlim _synthesisGate = new(1);
    Vosk.Model? _model;
    string _modelPath = string.Empty;
    static readonly HttpClient Downloads = new() { Timeout = TimeSpan.FromMinutes(5) };
    readonly object _messagesGate = new();
    readonly Queue<SpeechMessage> _messages = new();
    readonly string _session = Guid.NewGuid().ToString("N");
    long _messageId;
    internal event Action? SettingsChanged;

    internal bool Publish(string text) {
        if (string.IsNullOrWhiteSpace(text)) return false;
        text = text.Replace('_', ' ').Trim();
        if (text.Length == 0) return false;
        string output = InstructionOutput;
        lock (_messagesGate) {
            foreach (string chunk in Chunks(text)) {
                _messages.Enqueue(new SpeechMessage(++_messageId, chunk, DateTimeOffset.UtcNow, output));
                while (_messages.Count > 32) _messages.Dequeue();
            }
        }
        return output == "box";
    }

    internal static IEnumerable<string> Chunks(string text) {
        text = text.Trim();
        while (text.Length > 500) {
            int end = text.LastIndexOf(' ', 499);
            if (end < 250) end = 500;
            yield return text[..end];
            text = text[end..].TrimStart();
        }
        if (text.Length > 0) yield return text;
    }

    internal SpeechMessages Messages(string? session, long? after, bool muted) {
        lock (_messagesGate) {
            while (_messages.TryPeek(out SpeechMessage? first) && first.Created < DateTimeOffset.UtcNow.AddSeconds(-60)) _messages.Dequeue();
            // A new page or restarted server starts at the live cursor, never replaying old greetings.
            SpeechMessage[] events = !muted && session == _session && after.HasValue && after >= 0 && after <= _messageId
                ? _messages.Where(message => message.Id > after && message.Output == "browser").ToArray() : Array.Empty<SpeechMessage>();
            return new SpeechMessages(_session, _messageId, muted, events, InstructionOutput);
        }
    }

    public SpeechService(ISettingsStore settings, AppPaths paths) { _settings = settings; _paths = paths; }

    public SpeechSettings Settings {
        get {
            try {
                string? json = _settings.Load(SettingsFiles.Speech)?.FirstOrDefault();
                return json == null ? new SpeechSettings() : JsonSerializer.Deserialize<SpeechSettings>(json) ?? new SpeechSettings();
            }
            catch (JsonException) { return new SpeechSettings(); }
        }
    }

    public string Save(SpeechSettings settings) {
        if (settings.InstructionOutput is not ("auto" or "box" or "browser")) throw new ArgumentException("Choose browser/device or jaNET computer for instruction speech.");
        if (settings.Engine is not ("auto" or "windows" or "espeak" or "piper" or "off")) throw new ArgumentException("Choose automatic, Windows voices, eSpeak NG, Piper, or off.");
        if (settings.Engine == "windows" && !OperatingSystem.IsWindows()) throw new ArgumentException("Windows voices require Windows. Choose eSpeak NG or Piper on this system.");
        if (settings.Engine is "espeak" or "piper" && string.IsNullOrWhiteSpace(settings.Executable)) throw new ArgumentException("Enter the speech executable path.");
        if (settings.Engine == "piper" && string.IsNullOrWhiteSpace(settings.PiperModel)) throw new ArgumentException("Enter the Piper .onnx model path.");
        if (new[] { settings.Executable, settings.Voice, settings.PiperModel, settings.RecognitionModel }.Any(v => v == null || v.Length > 2048 || v.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0))
            throw new ArgumentException("Speech settings contain an invalid value.");
        SpeechSettings previous = Settings;
        string result = _settings.Save(SettingsFiles.Speech, JsonSerializer.Serialize(settings));
        if (result == "Settings saved." && settings != previous) SettingsChanged?.Invoke();
        return result;
    }

    // Old configurations keep box playback when an engine is installed. An
    // explicit browser selection never starts the box speaker as a fallback.
    internal string InstructionOutput {
        get {
            SpeechSettings settings = Settings;
            return settings.InstructionOutput is "box" or "browser" ? settings.InstructionOutput : SynthesisReady ? "box" : "browser";
        }
    }

    public object Status(bool muted = false) {
        SpeechSettings s = Settings;
        return new { settings = s, effectiveEngine = EffectiveEngine(s), recognitionReady = File.Exists(Path.Combine(s.RecognitionModel, "am", "final.mdl")),
            synthesisReady = SynthesisReady,
            modelLoaded = _model != null, muted, instructionOutput = InstructionOutput };
    }

    internal bool SynthesisReady {
        get {
            SpeechSettings settings = Settings;
            string engine = EffectiveEngine(settings);
            return engine == "windows" ? OperatingSystem.IsWindows() && FindExecutable("powershell") != null :
                engine != "off" && FindExecutable(settings.Executable) != null &&
                (settings.Engine != "piper" || File.Exists(settings.PiperModel));
        }
    }

    static string EffectiveEngine(SpeechSettings settings) => settings.Engine == "auto"
        ? OperatingSystem.IsWindows() && FindExecutable(settings.Executable) == null ? "windows" : "espeak"
        : settings.Engine;

    const string WindowsSynthesis = """
        $ErrorActionPreference = 'Stop'
        [Console]::InputEncoding = New-Object System.Text.UTF8Encoding($false)
        Add-Type -AssemblyName System.Runtime.WindowsRuntime
        $null = [Windows.Media.SpeechSynthesis.SpeechSynthesizer, Windows.Media.SpeechSynthesis, ContentType=WindowsRuntime]
        $null = [Windows.Media.SpeechSynthesis.SpeechSynthesisStream, Windows.Media.SpeechSynthesis, ContentType=WindowsRuntime]
        $synth = New-Object Windows.Media.SpeechSynthesis.SpeechSynthesizer
        if ($env:JANET_VOICE -and $env:JANET_VOICE -ne 'default') {
            $voice = [Windows.Media.SpeechSynthesis.SpeechSynthesizer]::AllVoices | Where-Object {
                $_.Id -eq $env:JANET_VOICE -or $_.DisplayName -eq $env:JANET_VOICE -or
                $_.Language -eq $env:JANET_VOICE -or $_.Language.StartsWith($env:JANET_VOICE + '-')
            } | Select-Object -First 1
            if (!$voice) { throw 'The Windows voice/language is not installed. Install it in Windows Speech settings, or use default.' }
            $synth.Voice = $voice
            if ($synth.Voice.Id -ne $voice.Id) { throw 'Windows did not select the requested voice.' }
        }
        $method = [System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {
            $_.Name -eq 'AsTask' -and $_.IsGenericMethod -and $_.GetGenericArguments().Count -eq 1 -and
            $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1'
        } | Select-Object -First 1
        $task = $method.MakeGenericMethod([Windows.Media.SpeechSynthesis.SpeechSynthesisStream]).Invoke($null, @($synth.SynthesizeTextToStreamAsync([Console]::In.ReadToEnd())))
        $stream = $task.GetAwaiter().GetResult()
        $inputStream = [System.IO.WindowsRuntimeStreamExtensions]::AsStreamForRead($stream.GetInputStreamAt(0))
        $file = [System.IO.File]::Create($env:JANET_AUDIO)
        try { $inputStream.CopyTo($file) } finally { $file.Dispose(); $inputStream.Dispose(); $synth.Dispose() }
        """;

    internal async Task<InstalledSpeechVoice[]> WindowsVoices(CancellationToken cancellationToken) {
        if (!OperatingSystem.IsWindows()) return Array.Empty<InstalledSpeechVoice>();
        string executable = FindExecutable("powershell") ?? throw new InvalidOperationException("Windows PowerShell is required to list installed voices.");
        var start = new ProcessStartInfo(executable) {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-NonInteractive"); start.ArgumentList.Add("-Command");
        start.ArgumentList.Add("""
            $ErrorActionPreference = 'Stop'
            [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
            Add-Type -AssemblyName System.Runtime.WindowsRuntime
            $null = [Windows.Media.SpeechSynthesis.SpeechSynthesizer, Windows.Media.SpeechSynthesis, ContentType=WindowsRuntime]
            $voices = @([Windows.Media.SpeechSynthesis.SpeechSynthesizer]::AllVoices | ForEach-Object {
                [PSCustomObject]@{ Id = $_.Id; Name = $_.DisplayName; Language = $_.Language }
            })
            ConvertTo-Json -InputObject $voices -Compress
            """);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Windows voice discovery could not start.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        Task<string> output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        Task<string> errors = process.StandardError.ReadToEndAsync(timeout.Token);
        try {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            string json = await output.ConfigureAwait(false);
            await errors.ConfigureAwait(false);
            if (process.ExitCode != 0) throw new InvalidOperationException("Installed Windows voices could not be read.");
            InstalledSpeechVoice[] voices = JsonSerializer.Deserialize<InstalledSpeechVoice[]>(json)
                ?? throw new InvalidOperationException("Windows returned an invalid voice list.");
            if (voices.Length > 256 || voices.Any(voice => string.IsNullOrWhiteSpace(voice.Id) || string.IsNullOrWhiteSpace(voice.Name) || string.IsNullOrWhiteSpace(voice.Language)))
                throw new InvalidOperationException("Windows returned an invalid voice list.");
            return voices;
        } catch {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            try { await Task.WhenAll(output, errors).ConfigureAwait(false); } catch (Exception) { }
            throw;
        }
    }

    internal static string? FindExecutable(string name) {
        if (string.IsNullOrWhiteSpace(name)) return null;
        if (File.Exists(name)) return Path.GetFullPath(name);
        foreach (string folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)) {
            string candidate = Path.Combine(folder.Trim('"'), name);
            if (File.Exists(candidate)) return candidate;
            if (OperatingSystem.IsWindows() && File.Exists(candidate + ".exe")) return candidate + ".exe";
        }
        if (OperatingSystem.IsWindows() && name is "espeak-ng" or "espeak-ng.exe") {
            foreach (string folder in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) }) {
                string candidate = Path.Combine(folder, "eSpeak NG", "espeak-ng.exe");
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }

    public async Task<byte[]> Synthesize(string text, CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 500) throw new ArgumentException("Enter between 1 and 500 characters to speak.");
        SpeechSettings settings = Settings;
        if (settings.Engine == "off") throw new InvalidOperationException("Server speech is switched off. Browser voices remain available.");
        string engine = EffectiveEngine(settings);
        if (engine == "windows" && !OperatingSystem.IsWindows()) throw new InvalidOperationException("Windows voices require Windows.");
        string executable = FindExecutable(engine == "windows" ? "powershell" : settings.Executable) ?? throw new InvalidOperationException("Speech engine not found. Choose automatic on Windows, or install eSpeak NG/Piper and select its executable in Voice settings.");
        if (settings.Engine == "piper" && !File.Exists(settings.PiperModel)) throw new InvalidOperationException("The Piper voice model was not found.");
        await _synthesisGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        string file = Path.Combine(Path.GetTempPath(), "janet-speech-" + Guid.NewGuid().ToString("N") + ".wav");
        try {
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, StandardInputEncoding = Encoding.UTF8, RedirectStandardOutput = true, RedirectStandardError = true };
            if (engine == "windows") {
                start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-NonInteractive");
                start.ArgumentList.Add("-Command"); start.ArgumentList.Add(WindowsSynthesis);
                start.Environment["JANET_AUDIO"] = file; start.Environment["JANET_VOICE"] = settings.Voice;
            } else if (settings.Engine == "piper") {
                start.ArgumentList.Add("--model"); start.ArgumentList.Add(settings.PiperModel);
                start.ArgumentList.Add("--output_file"); start.ArgumentList.Add(file);
            } else {
                start.ArgumentList.Add("--stdin"); start.ArgumentList.Add("-w"); start.ArgumentList.Add(file);
                start.ArgumentList.Add("-v"); start.ArgumentList.Add(settings.Voice);
            }
            using var process = Process.Start(start) ?? throw new InvalidOperationException("The speech engine could not be started.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(settings.Engine == "piper" ? 90 : 30));
            Task output = process.StandardOutput.BaseStream.CopyToAsync(Stream.Null, timeout.Token);
            Task errors = process.StandardError.BaseStream.CopyToAsync(Stream.Null, timeout.Token);
            try {
                await process.StandardInput.WriteLineAsync(text.AsMemory(), timeout.Token).ConfigureAwait(false);
                process.StandardInput.Close();
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                await Task.WhenAll(output, errors).ConfigureAwait(false);
            } catch (Exception error) {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                try { await Task.WhenAll(output, errors).ConfigureAwait(false); } catch (Exception) { }
                if (error is OperationCanceledException && !cancellationToken.IsCancellationRequested)
                    throw new InvalidOperationException("The speech engine timed out. Check the selected executable and voice model.", error);
                throw;
            }
            if (process.ExitCode != 0 || !File.Exists(file)) throw new InvalidOperationException(engine == "windows"
                ? "Windows speech failed. Install the selected Windows speech language or set Voice / language to default."
                : "The speech engine failed. Check the executable, voice, and model in Voice settings.");
            if (new FileInfo(file).Length > 8_000_000) throw new InvalidOperationException("The generated audio is too large.");
            byte[] audio = await File.ReadAllBytesAsync(file, cancellationToken).ConfigureAwait(false);
            if (audio.Length < 44 || Encoding.ASCII.GetString(audio, 0, 4) != "RIFF" || Encoding.ASCII.GetString(audio, 8, 4) != "WAVE")
                throw new InvalidOperationException("The speech engine did not produce WAV audio.");
            return audio;
        } finally {
            try { File.Delete(file); } catch (IOException) { }
            _synthesisGate.Release();
        }
    }

    public async Task<string> Recognize(byte[] wave, CancellationToken cancellationToken) {
        byte[] pcm = ReadPcm(wave);
        await _recognitionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            string path = Settings.RecognitionModel;
            if (!Directory.Exists(path)) throw new InvalidOperationException("No recognition model installed. Download the English model in Voice settings, or select an existing Vosk model folder.");
            if (_model == null || _modelPath != path) {
                _model?.Dispose(); _model = null;
                Vosk.Vosk.SetLogLevel(-1);
                _model = new Vosk.Model(path); _modelPath = path;
            }
            using var recognizer = new Vosk.VoskRecognizer(_model, 16000);
            var text = new StringBuilder();
            for (int offset = 0; offset < pcm.Length; offset += 8000) {
                cancellationToken.ThrowIfCancellationRequested();
                byte[] chunk = pcm.AsSpan(offset, Math.Min(8000, pcm.Length - offset)).ToArray();
                if (recognizer.AcceptWaveform(chunk, chunk.Length)) Append(recognizer.Result());
            }
            Append(recognizer.FinalResult());
            return text.ToString().Trim();

            void Append(string json) {
                using JsonDocument result = JsonDocument.Parse(json);
                if (result.RootElement.TryGetProperty("text", out JsonElement value)) text.Append(value.GetString()).Append(' ');
            }
        } catch (DllNotFoundException) { throw new InvalidOperationException("The Vosk native runtime is unavailable for this platform."); }
        catch (BadImageFormatException) { throw new InvalidOperationException("The Vosk native runtime does not match this platform's architecture."); }
        finally { _recognitionGate.Release(); }
    }

    internal static byte[] ReadPcm(byte[] wave) {
        if (wave.Length < 44 || wave.Length > 700_000 || Encoding.ASCII.GetString(wave, 0, 4) != "RIFF" || Encoding.ASCII.GetString(wave, 8, 4) != "WAVE")
            throw new ArgumentException("Record a WAV clip of up to 20 seconds (16 kHz, mono, 16-bit PCM).");
        bool valid = false;
        byte[]? pcm = null;
        for (int offset = 12; offset + 8 <= wave.Length;) {
            int length = BinaryPrimitives.ReadInt32LittleEndian(wave.AsSpan(offset + 4, 4));
            if (length < 0 || length > wave.Length - offset - 8) throw new ArgumentException("The WAV file is truncated.");
            string name = Encoding.ASCII.GetString(wave, offset, 4);
            int start = offset + 8;
            if (name == "fmt " && length >= 16) valid = BinaryPrimitives.ReadInt16LittleEndian(wave.AsSpan(start, 2)) == 1 &&
                BinaryPrimitives.ReadInt16LittleEndian(wave.AsSpan(start + 2, 2)) == 1 && BinaryPrimitives.ReadInt32LittleEndian(wave.AsSpan(start + 4, 4)) == 16000 &&
                BinaryPrimitives.ReadInt16LittleEndian(wave.AsSpan(start + 14, 2)) == 16;
            if (name == "data") pcm = wave.AsSpan(start, length).ToArray();
            offset = start + length + (length % 2);
        }
        if (!valid || pcm == null || pcm.Length == 0 || pcm.Length > 640_000 || pcm.Length % 2 != 0) throw new ArgumentException("Use 16 kHz, mono, 16-bit PCM audio, up to 20 seconds.");
        return pcm;
    }

    public async Task<string> InstallModel(string language, CancellationToken cancellationToken) {
        string name = language switch { "en" => "vosk-model-small-en-us-0.15", _ => throw new ArgumentException("Choose the English small model, or select your own model folder.") };
        await _recognitionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        string root = Path.Combine(_paths.Root, "speech-models");
        string target = Path.Combine(root, name);
        string temp = Path.Combine(root, ".download-" + Guid.NewGuid().ToString("N"));
        try {
            Directory.CreateDirectory(root);
            if (!Directory.Exists(target)) {
                Directory.CreateDirectory(temp);
                using HttpResponseMessage response = await Downloads.GetAsync("https://alphacephei.com/vosk/models/" + name + ".zip", HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                using Stream download = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                using var memory = new MemoryStream();
                byte[] buffer = new byte[81920];
                int count;
                while ((count = await download.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0) {
                    if (memory.Length + count > 60_000_000) throw new InvalidOperationException("The recognition-model download is too large.");
                    await memory.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                }
                memory.Position = 0;
                using var archive = new ZipArchive(memory);
                if (archive.Entries.Count > 1000 || archive.Entries.Sum(entry => entry.Length) > 300_000_000) throw new InvalidOperationException("The model archive is too large.");
                foreach (ZipArchiveEntry entry in archive.Entries) {
                    string destination = Path.GetFullPath(Path.Combine(temp, entry.FullName));
                    if (!destination.StartsWith(Path.GetFullPath(temp) + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                        throw new InvalidOperationException("The model archive contains an unsafe path.");
                }
                archive.ExtractToDirectory(temp);
                string extracted = Path.Combine(temp, name);
                if (!File.Exists(Path.Combine(extracted, "am", "final.mdl"))) throw new InvalidOperationException("The downloaded recognition model is incomplete.");
                Directory.Move(extracted, target);
            }
            return Save(Settings with { RecognitionModel = target });
        } finally {
            // temp is created under our data directory; no user-selected paths are removed.
            if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true);
            _recognitionGate.Release();
        }
    }

    public void Dispose() {
        _recognitionGate.Wait();
        try { _model?.Dispose(); _model = null; }
        finally { _recognitionGate.Release(); }
    }
}
