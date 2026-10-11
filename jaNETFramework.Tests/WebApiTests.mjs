// (c) J@mBeL.net 2010-2026. Part of jaNET Framework, GNU GPL version 3 or later.
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
const source = await readFile(new URL('../www/js/api.js', import.meta.url), 'utf8');
const { runText, runRawText, runJson, loadInstructionSets, onConnection } = await import('data:text/javascript;base64,' + Buffer.from(source).toString('base64'));

test('legacy and raw commands retain their transport and output formats', async () => {
    const calls = [];
    globalThis.fetch = async (url, options) => {
        calls.push({ url, options }); return { ok: true, text: async () => 'answer', json: async () => ({ result: 42 }) };
    };
    assert.equal(await runText('whoami'), 'answer');
    assert.equal(await runRawText("./[launch]'C:\\Program Files\\app.exe'"), 'answer');
    assert.deepEqual(await runJson('widget'), { result: 42 });
    assert.deepEqual(await loadInstructionSets(), { result: 42 });
    assert.equal(calls[0].url, '?cmd=whoami&mode=text');
    assert.equal(JSON.parse(calls[1].options.body).command, "./[launch]'C:\\Program Files\\app.exe'");
    assert.equal(calls[1].options.method, 'POST');
    assert(calls.every(call => call.options.signal instanceof AbortSignal && call.options.cache === 'no-store'));
});

test('deadlines cover both fetch and stalled response bodies without automatic retries', async () => {
    for (const stalledBody of [false, true]) {
        let calls = 0, signal;
        const status = []; const unsubscribe = onConnection(value => status.push(value));
        globalThis.fetch = async (_, options) => {
            calls++; signal = options.signal;
            return stalledBody ? { ok: true, text: () => new Promise(() => {}) } : new Promise(() => {});
        };
        await assert.rejects(runRawText('Notepad', { timeoutMs: 15 }), error => {
            assert.equal(error.code, 'JANET_REQUEST_TIMEOUT'); assert.match(error.message, /may still be running/); return true;
        });
        assert.equal(calls, 1); assert.equal(signal.aborted, true);
        unsubscribe();
    }
});
