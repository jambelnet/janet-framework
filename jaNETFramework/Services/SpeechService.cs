/* (c) J@mBeL.net 2010-2026, John Ambeliotis. This file is part of jaNET Framework.
 * Licensed under the GNU General Public License, version 3 or later; see LICENSE. */

using jaNET.Configuration;
using jaNET.Infrastructure;
using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace jaNET.Services;

internal sealed record SpeechSettings(string Engine = "espeak", string Executable = "espeak-ng", string Voice = "en",
    string PiperModel = "", string RecognitionModel = "");

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
        if (settings.Engine is not ("espeak" or "piper" or "off")) throw new ArgumentException("Choose eSpeak NG, Piper, or off.");
        if (settings.Engine != "off" && string.IsNullOrWhiteSpace(settings.Executable)) throw new ArgumentException("Enter the speech executable path.");
        if (settings.Engine == "piper" && string.IsNullOrWhiteSpace(settings.PiperModel)) throw new ArgumentException("Enter the Piper .onnx model path.");
        if (new[] { settings.Executable, settings.Voice, settings.PiperModel, settings.RecognitionModel }.Any(v => v == null || v.Length > 2048 || v.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0))
            throw new ArgumentException("Speech settings contain an invalid value.");
        return _settings.Save(SettingsFiles.Speech, JsonSerializer.Serialize(settings));
    }

    public object Status(bool muted = false) {
        SpeechSettings s = Settings;
        return new { settings = s, recognitionReady = File.Exists(Path.Combine(s.RecognitionModel, "am", "final.mdl")),
            synthesisReady = s.Engine != "off" && FindExecutable(s.Executable) != null && (s.Engine != "piper" || File.Exists(s.PiperModel)),
            modelLoaded = _model != null, muted };
    }

    internal static string? FindExecutable(string name) {
        if (string.IsNullOrWhiteSpace(name)) return null;
        if (File.Exists(name)) return Path.GetFullPath(name);
        foreach (string folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)) {
            string candidate = Path.Combine(folder.Trim('"'), name);
            if (File.Exists(candidate)) return candidate;
            if (OperatingSystem.IsWindows() && File.Exists(candidate + ".exe")) return candidate + ".exe";
        }
        return null;
    }

    public async Task<byte[]> Synthesize(string text, CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 500) throw new ArgumentException("Enter between 1 and 500 characters to speak.");
        SpeechSettings settings = Settings;
        if (settings.Engine == "off") throw new InvalidOperationException("Server speech is switched off. Browser voices remain available.");
        string executable = FindExecutable(settings.Executable) ?? throw new InvalidOperationException("Speech engine not found. Install eSpeak NG or Piper, then select its executable in Voice settings.");
        if (settings.Engine == "piper" && !File.Exists(settings.PiperModel)) throw new InvalidOperationException("The Piper voice model was not found.");
        await _synthesisGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        string file = Path.Combine(Path.GetTempPath(), "janet-speech-" + Guid.NewGuid().ToString("N") + ".wav");
        try {
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            if (settings.Engine == "piper") {
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
            if (process.ExitCode != 0 || !File.Exists(file)) throw new InvalidOperationException("The speech engine failed. Check the executable, voice, and model in Voice settings.");
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
