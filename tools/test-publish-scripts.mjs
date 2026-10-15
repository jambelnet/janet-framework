// (c) J@mBeL.net 2010-2026. Part of jaNET Framework, GNU GPL version 3 or later.
import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, writeFileSync, readFileSync, readdirSync, existsSync, rmSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { tmpdir } from 'node:os';
import { basename, dirname, join, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';
import test from 'node:test';

const root = dirname(dirname(fileURLToPath(import.meta.url)));
const windows = process.platform === 'win32';
const bash = process.env.JANET_BASH || (windows ? 'C:/Program Files/Git/bin/bash.exe' : process.platform === 'darwin' ? '/bin/bash' : 'bash');
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

const bashAvailable = spawnSync(bash, ['--version'], { encoding: 'utf8' });
const skipBash = bashAvailable.error || bashAvailable.status !== 0 ? `${bash} is unavailable` : false;

function releaseFixture(run) {
    const folder = mkdtempSync(join(tmpdir(), 'janet-github-release-space '));
    try {
        const temporary = join(folder, 'temporary'); mkdirSync(temporary);
        const log = join(folder, 'calls.jsonl');
        const mock = join(folder, 'mock-gh.cjs');
        writeFileSync(mock, `
const fs = require('node:fs');
const args = process.argv.slice(2);
const files = args.filter(arg => /\\.(zip|tar\\.gz|nupkg|snupkg)$/.test(arg) || arg.endsWith('/SHA256SUMS.txt'));
const checksum = files.find(file => file.endsWith('/SHA256SUMS.txt'));
fs.appendFileSync(process.env.JANET_GH_LOG, JSON.stringify({ args, files, manifest: checksum ? fs.readFileSync(checksum, 'utf8') : null }) + '\\n');
if (args[0] === 'api') {
    if (process.env.JANET_API_EXIT) process.exit(Number(process.env.JANET_API_EXIT));
    if (process.env.JANET_RELEASE_EXISTS === '1') process.stdout.write('123\\n');
} else if (args[0] === 'release' && args[1] === 'create') {
    process.exit(Number(process.env.JANET_CREATE_EXIT || 0));
} else if (args[0] === 'release' && args[1] === 'upload') {
    process.exit(Number(process.env.JANET_UPLOAD_EXIT || 0));
} else process.exit(90);
`);
        writeFileSync(join(folder, 'gh'), '#!/bin/bash\nexec "$JANET_NODE" "$JANET_GH_MOCK" "$@"\n', { mode: 0o755 });
        const notes = join(folder, 'notes.md'); writeFileSync(notes, 'Release notes to preserve.');
        const fixtures = new Map([['application.zip', 'application bytes'], ['linux.tar.gz', 'linux bytes'], ['library.nupkg', 'library bytes'], ['source archive.zip', 'source bytes']]);
        const assets = [...fixtures].map(([name, body]) => { const file = join(folder, name); writeFileSync(file, body); return file; });
        const call = ({ version = '1.0.0-rc.2', tag = 'v' + version, files = assets, env = {} } = {}) => {
            rmSync(log, { force: true });
            const args = [tag, version, notes, ...files].map(value => windows ? value.replaceAll('\\', '/') : value);
            const result = spawnSync(bash, ['-c', 'JANET_BIN=$(cd "$JANET_BIN" && pwd -P); PATH="$JANET_BIN:/usr/bin:/bin:$PATH" exec bash "$JANET_SCRIPT" "$@"', 'release-test', ...args], {
                cwd: folder, encoding: 'utf8', timeout: 30000,
                env: { ...process.env, GH_TOKEN: 'test-only-never-used', GH_PROMPT_DISABLED: '1', GITHUB_REPOSITORY: 'example/test-repository',
                    TMPDIR: temporary.replaceAll('\\', '/'), JANET_BIN: folder.replaceAll('\\', '/'), JANET_NODE: process.execPath,
                    JANET_GH_MOCK: mock, JANET_GH_LOG: log, JANET_SCRIPT: join(root, 'tools/publish-github-release.sh'), ...env }
            });
            assert.ifError(result.error);
            assert.deepEqual(readdirSync(temporary), [], 'temporary asset copies must be removed');
            return { ...result, calls: existsSync(log) ? readFileSync(log, 'utf8').trim().split('\n').map(line => JSON.parse(line)) : [] };
        };
        run({ call, assets, fixtures, folder });
    } finally {
        assert(resolve(folder).startsWith(resolve(tmpdir()) + sep) && basename(folder).startsWith('janet-github-release-space '));
        rmSync(folder, { recursive: true, force: true });
    }
}

test('GitHub release: creates a missing pre-release with verified tag and flat checksums', { skip: skipBash }, () => releaseFixture(({ call, fixtures }) => {
    const result = call(); assert.equal(result.status, 0, result.stderr);
    assert.equal(result.calls.length, 2);
    assert.equal(result.calls[0].args[0], 'api');
    const create = result.calls[1];
    assert.deepEqual(create.args.slice(0, 3), ['release', 'create', 'v1.0.0-rc.2']);
    assert(create.args.includes('--prerelease')); assert(create.args.includes('--verify-tag'));
    assert(create.args.includes('--notes-file')); assert(!create.args.includes('--clobber'));
    assert.equal(create.files.length, fixtures.size + 1);
    const lines = create.manifest.trimEnd().split('\n'); assert.equal(lines.length, fixtures.size);
    for (const line of lines) {
        const match = /^([a-f0-9]{64}) \*(.+)$/.exec(line); assert(match, line);
        assert(fixtures.has(match[2]), 'checksums use flat download names, including spaces');
        assert.equal(match[1], createHash('sha256').update(fixtures.get(match[2])).digest('hex'));
    }
    assert(!create.manifest.includes('\r'));
}));

test('GitHub release: existing and repeated runs replace assets without recreating or editing the release', { skip: skipBash }, () => releaseFixture(({ call }) => {
    for (let attempt = 0; attempt < 2; attempt++) {
        const result = call({ env: { JANET_RELEASE_EXISTS: '1' } }); assert.equal(result.status, 0, result.stderr);
        assert.equal(result.calls.length, 3);
        const upload = result.calls[1];
        assert.deepEqual(upload.args.slice(0, 3), ['release', 'upload', 'v1.0.0-rc.2']);
        assert(upload.args.includes('--clobber'));
        assert(!upload.args.includes('--notes-file')); assert(!upload.args.includes('--title'));
        assert(!upload.files.some(file => basename(file) === 'SHA256SUMS.txt'));
        assert.deepEqual(result.calls[2].files.map(file => basename(file)), ['SHA256SUMS.txt']);
    }
}));

test('GitHub release: stable versions do not acquire the pre-release flag', { skip: skipBash }, () => releaseFixture(({ call }) => {
    for (const version of ['1.0.0', '1.0.1+build.42', '1.0.1+build-42']) {
        const result = call({ version });
        const diagnostics = JSON.stringify({ bash: bashAvailable.stdout.split('\n')[0], stdout: result.stdout, stderr: result.stderr, calls: result.calls });
        assert.equal(result.status, 0, diagnostics);
        assert.equal(result.stderr, '', diagnostics);
        assert.equal(result.calls.length, 2, diagnostics);
        assert.deepEqual(result.calls[1].args.slice(0, 3), ['release', 'create', 'v' + version]);
        assert(!result.calls[1].args.includes('--prerelease'));
        assert(!result.calls[1].args.includes(''));
    }
}));

test('GitHub release: API/auth failures do not trigger creation or uploads', { skip: skipBash }, () => releaseFixture(({ call }) => {
    const result = call({ env: { JANET_API_EXIT: '19' } }); assert.equal(result.status, 19);
    assert.equal(result.calls.length, 1); assert.equal(result.calls[0].args[0], 'api');
}));

test('GitHub release: upload and creation failures propagate without deleting or retrying a release', { skip: skipBash }, () => releaseFixture(({ call }) => {
    const create = call({ env: { JANET_CREATE_EXIT: '23' } }); assert.equal(create.status, 23);
    const upload = call({ env: { JANET_RELEASE_EXISTS: '1', JANET_UPLOAD_EXIT: '29' } }); assert.equal(upload.status, 29);
    assert.equal(upload.calls.length, 2); assert.equal(upload.calls[1].args[1], 'upload');
}));

test('GitHub release: invalid tags and missing or duplicate assets fail before any GitHub request', { skip: skipBash }, () => releaseFixture(({ call, assets, folder }) => {
    for (const options of [{ tag: 'v1.0.0-rc.1' }, { version: 'bad"tag' }, { files: [join(folder, 'missing.zip')] }, { files: [assets[0], assets[0]] }]) {
        const result = call(options); assert.notEqual(result.status, 0); assert.deepEqual(result.calls, []);
    }
}));
