// (c) J@mBeL.net 2010-2026. GNU GPL version 3 or later.
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
const source = await readFile(new URL('../www/js/studio-api.js', import.meta.url), 'utf8');
const { studioRequest } = await import('data:text/javascript;base64,' + Buffer.from(source).toString('base64'));
const snapshot = { revision: 'one', instructions: [{ id: 'hello', action: 'Hello' }], events: [], functions: ['%user%'] };
const json = (value, status = 200) => new Response(JSON.stringify(value), { status, headers: { 'Content-Type': 'application/json; charset=utf-8' } });
test('loads typed configuration and saves with revision, no cache or automatic retry', async () => {
    let calls = 0;
    assert.deepEqual(await studioRequest('GET', undefined, async (url, options) => {
        calls++; assert.equal(url, '/api/studio'); assert.equal(options.cache, 'no-store'); assert.equal(options.headers.Accept, 'application/json'); return json(snapshot);
    }), snapshot);
    const data = { id: 'hello', revision: 'one', actions: ['Updated'] };
    const result = await studioRequest('POST', data, async (url, options) => {
        calls++; assert.deepEqual(JSON.parse(options.body), data); assert.equal(options.headers['Content-Type'], 'application/json'); return json({ message: 'Instruction saved.', snapshot });
    });
    assert.equal(result.message, 'Instruction saved.'); assert.equal(calls, 2);
});
test('old server or static HTML yields actionable recovery instead of a JSON parse exception', async () => {
    for (const status of [200, 404]) await assert.rejects(studioRequest('GET', undefined, async () => new Response('<html><head>404</head></html>', { status, headers: { 'Content-Type': 'text/html' } })), /Stop jaNET, rebuild/);
});
test('authentication, authorization, conflict and service errors retain useful messages', async () => {
    await assert.rejects(studioRequest('GET', undefined, async () => new Response('<html>Login</html>', { status: 401 })), /Sign in/);
    await assert.rejects(studioRequest('GET', undefined, async () => json({}, 403)), /same address/);
    await assert.rejects(studioRequest('POST', {}, async () => json({ error: 'Studio conflict: changed.' }, 409)), /Studio conflict: changed/);
    await assert.rejects(studioRequest('GET', undefined, async () => new Response('<html>Unavailable</html>', { status: 503 })), /HTTP 503/);
});
test('malformed JSON or incompatible snapshots cannot partially populate the editor', async () => {
    for (const value of [null, [], {}, { ...snapshot, revision: '' }, { ...snapshot, instructions: [{ id: 'hello' }] }, { ...snapshot, functions: null }]) await assert.rejects(studioRequest('GET', undefined, async () => json(value)), /invalid configuration/);
    await assert.rejects(studioRequest('GET', undefined, async () => new Response('{broken', { headers: { 'Content-Type': 'application/json' } })), /invalid configuration/);
    await assert.rejects(studioRequest('POST', {}, async () => json({ message: 'saved' })), /invalid configuration/);
});
test('network failure and timeout never retry a write or report success', async () => {
    let calls = 0;
    await assert.rejects(studioRequest('POST', {}, async () => { calls++; throw new TypeError('Failed to fetch'); }), /Cannot reach/);
    await assert.rejects(studioRequest('POST', {}, async () => { calls++; throw new DOMException('Timed out', 'TimeoutError'); }), /not been retried/);
    assert.equal(calls, 2);
});
