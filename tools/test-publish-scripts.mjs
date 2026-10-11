// (c) J@mBeL.net 2010-2026. Part of jaNET Framework, GNU GPL version 3 or later.
import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, writeFileSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';
import test from 'node:test';

const root = dirname(dirname(fileURLToPath(import.meta.url)));
const windows = process.platform === 'win32';
const bash = process.env.JANET_BASH || (windows ? 'C:/Program Files/Git/bin/bash.exe' : 'bash');
const powershell = windows ? 'powershell.exe' : 'pwsh';

for (const kind of ['powershell', 'bash']) {
    const program = kind === 'bash' ? bash : powershell;
    const available = spawnSync(program, kind === 'bash' ? ['--version'] : ['-NoProfile', '-Command', '$PSVersionTable.PSVersion.ToString()']);
    test(`${kind}: release wrapper forwards targets/options, quotes paths and propagates errors`, {
        skip: available.error || available.status !== 0 ? `${program} is unavailable` : false
    }, () => {
        const folder = mkdtempSync(join(tmpdir(), 'janet-publish-space '));
        try {
            const log = join(folder, 'calls.json');
            const mock = join(folder, 'mock.cjs');
            writeFileSync(mock, `require('node:fs').writeFileSync(process.env.JANET_LOG, JSON.stringify(process.argv.slice(2))); process.exit(Number(process.env.JANET_EXIT || 0));`);
            writeFileSync(join(folder, 'dotnet'), '#!/bin/bash\nexec "$JANET_NODE" "$JANET_MOCK" "$@"\n', { mode: 0o755 });
            writeFileSync(join(folder, 'dotnet.cmd'), '@"%JANET_NODE%" "%JANET_MOCK%" %*\r\n@exit /b %errorlevel%\r\n');
            const scriptsFolder = join(folder, 'tools'); mkdirSync(scriptsFolder);
            const script = join(scriptsFolder, kind === 'bash' ? 'publish.sh' : 'publish.ps1');
            writeFileSync(script, readFileSync(join(root, 'tools', kind === 'bash' ? 'publish.sh' : 'publish.ps1')));
            const call = (args, exitCode = 0) => {
                const command = kind === 'bash'
                    ? ['-c', 'PATH="$JANET_BIN:/usr/bin:/bin:$PATH" exec bash "$JANET_SCRIPT" "$@"', 'publish-test', ...args]
                    : ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', '$env:PATH = $env:JANET_BIN + [IO.Path]::PathSeparator + $env:PATH; & $env:JANET_SCRIPT @args', ...args];
                const result = spawnSync(program, command, {
                    cwd: tmpdir(), encoding: 'utf8', timeout: 30000,
                    env: { ...process.env, JANET_BIN: folder, JANET_SCRIPT: script, JANET_NODE: process.execPath, JANET_MOCK: mock, JANET_LOG: log, JANET_EXIT: String(exitCode) }
                });
                assert.ifError(result.error);
                return result;
            };
            const args = kind === 'bash' ? ['linux-arm64', '--framework-dependent'] : ['-Runtime', 'linux-arm64', '-FrameworkDependent'];
            const result = call(args); assert.equal(result.status, 0, result.stderr);
            const forwarded = JSON.parse(readFileSync(log, 'utf8'));
            assert.equal(forwarded[0], 'run');
            assert(forwarded[forwarded.indexOf('--project') + 1].endsWith('ReleasePackager.csproj'));
            assert(forwarded[forwarded.indexOf('--project') + 1].includes('janet-publish-space '));
            assert.equal(forwarded[forwarded.indexOf('-c') + 1], 'Release');
            assert.deepEqual(forwarded.slice(forwarded.indexOf('--') + 1).sort(), ['--framework-dependent', 'linux-arm64']);
            assert.equal(call([]).status, 0);
            assert.deepEqual(JSON.parse(readFileSync(log, 'utf8')).slice(-1), ['--']);
            assert.equal(call(kind === 'bash' ? ['--help'] : ['-Help']).status, 0);
            assert.equal(JSON.parse(readFileSync(log, 'utf8')).at(-1), '--help');
            assert.notEqual(call(args, 17).status, 0);
        } finally { rmSync(folder, { recursive: true, force: true }); }
    });
}
