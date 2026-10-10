// (c) J@mBeL.net 2010-2026. Part of jaNET Framework, GNU GPL version 3 or later.
import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, writeFileSync, readFileSync, existsSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, resolve, sep, basename } from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';
import test from 'node:test';

const root = dirname(dirname(fileURLToPath(import.meta.url)));
const windows = process.platform === 'win32';
const bash = process.env.JANET_BASH || (windows ? 'C:/Program Files/Git/bin/bash.exe' : 'bash');
const powershell = windows ? 'powershell.exe' : 'pwsh';
const mockSource = `
const fs = require('node:fs');
const [tool, ...args] = process.argv.slice(2);
fs.appendFileSync(process.env.JANET_MOCK_LOG, JSON.stringify({ tool, args, cwd: process.cwd() }) + '\\n');
if (args.includes('--version')) {
    console.log(tool === 'dotnet' ? (process.env.JANET_MOCK_SDK || '10.0.100') : 'v22.0.0');
} else if (tool === 'uname') {
    console.log(args[0] === '-s' ? 'Linux' : 'aarch64');
} else if ((tool === 'dotnet' && args[0] === process.env.JANET_MOCK_FAIL) ||
    (tool === 'node' && process.env.JANET_MOCK_FAIL === 'web-tests')) process.exit(17);
else if (tool === 'dotnet' && args[0] === 'publish') {
    fs.mkdirSync(args[args.indexOf('-o') + 1], { recursive: true });
}
`;

for (const kind of ['powershell', 'bash']) {
    const program = kind === 'bash' ? bash : powershell;
    const available = spawnSync(program, kind === 'bash' ? ['--version'] : ['-NoProfile', '-Command', '$PSVersionTable.PSVersion.ToString()'], { encoding: 'utf8' });
    test(`${kind}: build options, quoted paths, architecture and failure propagation`, { skip: available.error || available.status !== 0 ? `${program} is unavailable` : false }, () => {
        const folder = mkdtempSync(join(tmpdir(), 'janet-build-scripts-space '));
        try {
            const runner = join(folder, 'runner.cjs'), log = join(folder, 'calls.jsonl');
            writeFileSync(runner, mockSource);
            const project = join(folder, 'project with spaces'); mkdirSync(project);
            for (const script of ['build.ps1', 'build.sh']) writeFileSync(join(project, script), readFileSync(join(root, script)));
            for (const file of ['LICENSE', 'README.md', 'CHANGELOG.md', 'THIRD-PARTY-NOTICES.md']) writeFileSync(join(project, file), file);
            mkdirSync(join(project, 'deploy')); writeFileSync(join(project, 'deploy', 'janet.service'), 'test service');
            mkdirSync(join(project, 'jaNETFramework.Tests')); writeFileSync(join(project, 'jaNETFramework.Tests', 'WebTerminalTests.mjs'), '');
            for (const tool of ['dotnet', 'node', 'uname']) {
                writeFileSync(join(folder, tool), `#!/bin/bash\nexec "$JANET_NODE" "$JANET_MOCK_RUNNER" ${tool} "$@"\n`, { mode: 0o755 });
                writeFileSync(join(folder, tool + '.cmd'), `@"%JANET_NODE%" "%JANET_MOCK_RUNNER%" ${tool} %*\r\n@exit /b %errorlevel%\r\n`);
            }
            const env = { ...process.env, JANET_NODE: process.execPath, JANET_MOCK_RUNNER: runner, JANET_MOCK_LOG: log, PROCESSOR_ARCHITECTURE: 'ARM64', PROCESSOR_ARCHITEW6432: 'ARM64' };
            const call = (args, extra = {}) => {
                writeFileSync(log, '');
                let commandArguments;
                if (kind === 'powershell') {
                    commandArguments = ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', '$env:PATH = $env:JANET_MOCK_BIN + [IO.Path]::PathSeparator + $env:PATH; & $env:JANET_BUILD_SCRIPT @args', ...args];
                } else {
                    // Git Bash invoked outside its launcher needs its own core utilities in PATH.
                    commandArguments = ['-c', 'PATH="$JANET_MOCK_BIN:/usr/bin:/bin:$PATH" exec bash "$JANET_BUILD_SCRIPT" "$@"', 'build-test', ...args];
                }
                const result = spawnSync(program, commandArguments, {
                    cwd: folder, encoding: 'utf8', timeout: 30000,
                    env: { ...env, JANET_MOCK_BIN: folder, JANET_BUILD_SCRIPT: join(project, kind === 'bash' ? 'build.sh' : 'build.ps1'), ...extra }
                });
                assert.ifError(result.error);
                const calls = readFileSync(log, 'utf8').trim().split('\n').filter(Boolean).map(line => JSON.parse(line));
                return { ...result, calls, steps: calls.filter(call => !call.args.includes('--version') && call.tool !== 'uname') };
            };
            const flags = kind === 'bash'
                ? { help: '--help', test: '--test', publish: '--publish', runtime: '--runtime', dependent: '--framework-dependent', configuration: '--configuration' }
                : { help: '-Help', test: '-Test', publish: '-Publish', runtime: '-Runtime', dependent: '-FrameworkDependent', configuration: '-Configuration' };
            const help = call([flags.help]); assert.equal(help.status, 0); assert.equal(help.calls.length, 0);
            const normal = call([]); assert.equal(normal.status, 0, normal.stderr);
            assert.deepEqual(normal.steps.map(step => step.args[0]), ['restore', 'build']);
            assert(normal.steps.every(step => resolve(step.cwd) === resolve(project)));
            assert(normal.steps[1].args.includes('Release'));
            const debug = call([flags.configuration, 'Debug']); assert.equal(debug.status, 0, debug.stderr);
            assert(debug.steps[1].args.includes('Debug'));
            const full = call([flags.test, flags.publish, flags.dependent]); assert.equal(full.status, 0, full.stderr);
            assert.deepEqual(full.steps.map(step => step.tool), ['dotnet', 'dotnet', 'dotnet', 'node', 'dotnet']);
            const published = full.steps.at(-1).args;
            const rid = kind === 'bash' ? 'linux-arm64' : 'win-arm64';
            assert.equal(published[0], 'publish'); assert(published.includes(rid));
            assert.equal(published[published.indexOf('--self-contained') + 1], 'false');
            const output = published[published.indexOf('-o') + 1];
            assert(output.endsWith(rid + '-framework-dependent'));
            assert.equal(readFileSync(join(output, 'README.md'), 'utf8'), 'README.md');
            if (kind === 'bash') assert.equal(readFileSync(join(output, 'janet.service'), 'utf8'), 'test service');
            assert(full.steps[3].args.some(arg => arg.endsWith('WebTerminalTests.mjs')));
            const cross = call([flags.publish, flags.runtime, 'win-x64']); assert.equal(cross.status, 0, cross.stderr);
            assert(cross.steps.at(-1).args.includes('win-x64'));
            assert.equal(cross.steps.at(-1).args[cross.steps.at(-1).args.indexOf('--self-contained') + 1], 'true');
            for (const bad of [[flags.runtime, 'linux-x64'], [flags.dependent], [flags.publish, flags.configuration, 'Debug'], ['--not-an-option']]) {
                const result = call(bad); assert.notEqual(result.status, 0); assert.equal(result.calls.length, 0);
            }
            const oldSdk = call([], { JANET_MOCK_SDK: '9.0.100' }); assert.notEqual(oldSdk.status, 0); assert.equal(oldSdk.steps.length, 0);
            for (const fail of ['restore', 'build', 'test', 'web-tests', 'publish']) {
                const result = call([flags.test, flags.publish], { JANET_MOCK_FAIL: fail });
                assert.notEqual(result.status, 0, `${fail}: ${result.stdout}`);
                const last = result.steps.at(-1);
                assert.equal(fail === 'web-tests' ? last.tool : last.args[0], fail === 'web-tests' ? 'node' : fail);
            }
        } finally {
            assert(resolve(folder).startsWith(resolve(tmpdir()) + sep) && basename(folder).startsWith('janet-build-scripts-'));
            if (existsSync(folder)) rmSync(folder, { recursive: true, force: true });
        }
    });
}
