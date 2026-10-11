# Characterization harness: runs jaNET (either the .NET Framework exe or a dotnet dll) in a clean
# directory, feeds it a fixed list of console commands plus HTTP requests, and writes a normalized
# transcript. Run it against the baseline and the refactored build, then diff the two transcripts.
#
#   tools\characterize.ps1 -Program <path to jaNETProgram.exe | jaNETProgram.dll> -Out <transcript.txt>
param(
    [Parameter(Mandatory)] [string] $Program,
    [Parameter(Mandatory)] [string] $Out,
    [string] $WwwRoot,
    [string] $KeepDir
)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $WwwRoot) { $WwwRoot = Join-Path $here '..\www' }

$Program = (Resolve-Path $Program).Path
$run = Join-Path ([IO.Path]::GetTempPath()) ("janet-run-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $run | Out-Null
$proc = $null
$started = $false
try {
Copy-Item (Join-Path (Split-Path $Program) '*') $run -Recurse -Force
# a run must start from nothing: state that an earlier run left in the build folder (config, settings, key, log) is not part of the program
foreach ($state in 'AppConfig.xml', '.htaccess', '.smtpsettings', '.pop3settings', '.gmailsettings', '.janet.key', 'log.txt') {
    Remove-Item -LiteralPath (Join-Path $run $state) -Force -ErrorAction SilentlyContinue
}
Copy-Item (Resolve-Path $WwwRoot).Path (Join-Path $run 'www') -Recurse -Force

$httpPort = 8080
$socketPort = 5744
if ($Program.EndsWith('.dll')) {
    # Independent replay runs must never send test commands to somebody's live jaNET.
    $httpListener = New-Object Net.Sockets.TcpListener([Net.IPAddress]::Loopback, 0)
    $socketListener = New-Object Net.Sockets.TcpListener([Net.IPAddress]::Loopback, 0)
    try {
        $httpListener.Start(); $socketListener.Start()
        $httpPort = $httpListener.LocalEndpoint.Port
        $socketPort = $socketListener.LocalEndpoint.Port
    } finally { $httpListener.Stop(); $socketListener.Stop() }
    [xml]$config = [IO.File]::ReadAllText((Join-Path $here '..\jaNETFramework\Configuration\DefaultAppConfig.xml'))
    $config.jaNET.System.Comm.httpPort = [string]$httpPort
    $config.jaNET.System.Comm.localPort = [string]$socketPort
    $config.Save((Join-Path $run 'AppConfig.xml'))
}

$psi = New-Object Diagnostics.ProcessStartInfo
if ($Program.EndsWith('.dll')) {
    $psi.FileName = 'dotnet'
    $psi.Arguments = '"' + (Join-Path $run (Split-Path $Program -Leaf)) + '"'
} else {
    $psi.FileName = Join-Path $run (Split-Path $Program -Leaf)
}
$psi.WorkingDirectory = $run
$psi.UseShellExecute = $false
$psi.EnvironmentVariables['JANET_HOME'] = $run     # the data folder is the run folder (by default it would be the user's own)
$psi.RedirectStandardInput = $true
[Console]::InputEncoding = New-Object Text.UTF8Encoding($false)   # no byte order mark in front of the first command
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$psi.StandardOutputEncoding = [Text.Encoding]::UTF8

$proc = New-Object Diagnostics.Process
$proc.StartInfo = $psi
Add-Type -TypeDefinition @"
using System.IO; using System.Text; using System.Threading;
public class CharPump {
    public readonly StringBuilder Buf = new StringBuilder();
    public CharPump(TextReader r) {
        var t = new Thread(() => { var c = new char[1]; while (r.Read(c, 0, 1) > 0) lock (Buf) Buf.Append(c[0]); }) { IsBackground = true };
        t.Start();
    }
    public int Length { get { lock (Buf) return Buf.Length; } }
    public string Slice(int from) { lock (Buf) return Buf.ToString(from, Buf.Length - from); }
    public int Count(string s) { string all; lock (Buf) all = Buf.ToString(); int n = 0, i = 0; while ((i = all.IndexOf(s, i, System.StringComparison.Ordinal)) >= 0) { n++; i += s.Length; } return n; }
}
"@
$proc = New-Object Diagnostics.Process
$proc.StartInfo = $psi
$null = $proc.Start()
$started = $true
$pump = New-Object CharPump($proc.StandardOutput)
$errors = New-Object CharPump($proc.StandardError)

function Wait-Prompt([int] $count, [int] $timeoutMs = 30000) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($pump.Count("@jaNET>") -lt $count -and $sw.ElapsedMilliseconds -lt $timeoutMs -and -not $proc.HasExited) { Start-Sleep -Milliseconds 25 }
    if ($pump.Count("@jaNET>") -lt $count) {
        [IO.File]::WriteAllText($Out, $pump.Slice(0) + "`nSTDERR:`n" + $errors.Slice(0))
        throw "jaNET did not reach prompt $count within $timeoutMs ms. Output: $Out"
    }
}

$transcript = New-Object Text.StringBuilder
function Add-Section([string] $title, [string] $body) {
    [void]$transcript.AppendLine("=== $title")
    [void]$transcript.AppendLine($body)
}

Wait-Prompt 1 120000
$expected = 1

$commands = Get-Content (Join-Path $here 'characterize-commands.txt') | Where-Object { $_ -and -not $_.StartsWith('#') }
foreach ($cmd in $commands) {
    if ($cmd -like '@@HTTP*') {
        # HTTP section runs while the process is alive, against the state built so far
        $file = ($cmd -split ' ', 2)[1]
        $httpCases = Get-Content (Join-Path $here $file) | Where-Object { $_ -and -not $_.StartsWith('#') }
        foreach ($h in $httpCases) {
            $path, $cred = $h -split '\|', 2
            $statusOnly = $path.StartsWith('~')   # web UI files change on purpose, compare the status only
            $path = $path.TrimStart('~')
            try {
                $headers = @{}
                if ($cred) { $headers['Authorization'] = 'Basic ' + [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes($cred)) }
                $r = Invoke-WebRequest -Uri ("http://localhost:" + $httpPort + $path) -Headers $headers -UseBasicParsing -TimeoutSec 20
                $body = [Text.Encoding]::UTF8.GetString($r.RawContentStream.ToArray())
                if ($statusOnly) { $body = '(content not compared)' } elseif ($path -like '/www/*' -or $path -eq '/') { $body = "len=$($r.RawContentLength) sha=" + ([BitConverter]::ToString([Security.Cryptography.SHA1]::Create().ComputeHash($r.RawContentStream.ToArray()))) }
                Add-Section "HTTP $h -> $($r.StatusCode)" $body
            } catch {
                $code = if ($_.Exception.Response) { [int]$_.Exception.Response.StatusCode } else { 'ERR' }
                Add-Section "HTTP $h -> $code" ''
            }
        }
        continue
    }
    if ($cmd -like '@@TCP *') {
        # Raw socket interface (judo socket): one line in, one line out
        $line = ($cmd -split ' ', 2)[1]
        try {
            $c = New-Object Net.Sockets.TcpClient('127.0.0.1', $socketPort)
            $s = $c.GetStream(); $bytes = [Text.Encoding]::ASCII.GetBytes($line + "`r`n"); $s.Write($bytes, 0, $bytes.Length)
            $s.ReadTimeout = 5000; $buf2 = New-Object byte[] 4096; $n = $s.Read($buf2, 0, 4096)
            Add-Section "TCP $line" ([Text.Encoding]::ASCII.GetString($buf2, 0, $n))
            $c.Close()
        } catch { Add-Section "TCP $line" ("ERR " + $_.Exception.GetType().Name) }
        continue
    }    $before = $pump.Length
    $wireCommand = $cmd -replace '\b8080\b', [string]$httpPort -replace '\b5744\b', [string]$socketPort
    $proc.StandardInput.WriteLine($wireCommand)
    $expected++
    Wait-Prompt $expected 30000
    $text = $pump.Slice($before)
    Add-Section "CMD $cmd" $text
}

if (-not $proc.HasExited) { $proc.StandardInput.WriteLine('%exit%'); $null = $proc.WaitForExit(8000) }
if (-not $proc.HasExited) { $proc.Kill() }

# Persisted state produced by the run
foreach ($f in 'AppConfig.xml', '.htaccess', '.smtpsettings', '.pop3settings', '.gmailsettings', '.janet.key') {
    $p = Join-Path $run $f
    if (Test-Path $p) {
        $content = [IO.File]::ReadAllText($p)
        if ($f -eq '.janet.key') {
            $content = "<key of $([Convert]::FromBase64String($content.Trim()).Length) bytes>"
        } elseif ($f -ne 'AppConfig.xml') {
            # encrypted settings: the text differs from run to run (random nonce, per installation key), so show the format only
            $lines = @($content -split "`r?`n" | Where-Object { $_ })
            $format = if (($lines | Where-Object { -not $_.StartsWith('v2:') }).Count -eq 0) { 'v2' } else { 'legacy' }
            $content = "<$($lines.Count) encrypted lines, $format format>"
        }
        Add-Section "FILE $f" $content
    } else { Add-Section "FILE $f" '<missing>' }
}

$text = $transcript.ToString()
# Normalize environment/time dependent values
$text = $text.Replace($run, '<RUN>').Replace($env:USERNAME, '<USER>')
$text = [regex]::Replace($text, 'Days\[\d+\], Hours\[\d+\], Minutes\[\d+\], Seconds\[\d+\]', '<UPTIME>')
$text = [regex]::Replace($text, '\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}:\d{2}[.\d]*', '<TS>')
$text = $text -replace "`r`n", "`n"
$text = $text.Replace("localhost:$httpPort", 'localhost:8080').Replace("localhost:$socketPort", 'localhost:5744')
$text = $text.Replace("<httpPort>$httpPort</httpPort>", '<httpPort>8080</httpPort>').Replace("<localPort>$socketPort</localPort>", '<localPort>5744</localPort>')
$text = [regex]::Replace($text, "(?m)^$httpPort$", '8080')
$text = [regex]::Replace($text, "(?m)^$socketPort$", '5744')
# Runtime differences that are not behavior: exception wording and XmlSerializer attribute order
$text = [regex]::Replace($text, "\r?\nParameter name: (\w+)", ' (Parameter ''$1'')')
$text = [regex]::Replace($text, "<jaNET xmlns:xs[di]=[^>]*>", "<jaNET xmlns...>")
[IO.File]::WriteAllText($Out, $text, (New-Object Text.UTF8Encoding($false)))
if ($KeepDir) { Copy-Item $run $KeepDir -Recurse -Force }
Write-Host "wrote $Out ($($text.Length) chars)"
} finally {
if ($started -and -not $proc.HasExited) { $proc.Kill(); $null = $proc.WaitForExit(5000) }
Get-Process | Where-Object { $_.Path -and $_.Path.StartsWith($run) } | Stop-Process -Force -ErrorAction SilentlyContinue
$resolvedRun = [IO.Path]::GetFullPath($run)
if (-not $resolvedRun.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()), [StringComparison]::OrdinalIgnoreCase) -or (Split-Path -Leaf $resolvedRun) -notmatch '^janet-run-[a-f0-9]{32}$') { throw 'Unsafe replay cleanup path.' }
Remove-Item -LiteralPath $resolvedRun -Recurse -Force -ErrorAction SilentlyContinue
}
