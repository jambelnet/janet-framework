// (c) J@mBeL.net 2010-2026. Part of jaNET Framework, GNU GPL version 3 or later; see LICENSE.
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
const source = await readFile(new URL('../www/js/cameras.js', import.meta.url), 'utf8');
const { cameraFeedUrl, validateCameras, validateCameraSetup, cameraRequest, createCameraWidget, wireCameras } = await import('data:text/javascript;base64,' + Buffer.from(source).toString('base64'));
const id = 'a'.repeat(32);
const camera = { id, name: 'Front door', mode: 'auto', refreshSeconds: 5, version: 'v1' };
const settings = { revision: 'v1', cameras: [{ ...camera, url: 'http://192.168.1.50/snapshot.jpg', username: 'viewer', password: '', passwordConfigured: true, showOnHome: true }] };
const tick = () => new Promise(resolve => setImmediate(resolve));
function documentFixture() {
    function element() { return { value: '', hidden: false, disabled: false, checked: false, children: [], attributes: {}, events: {}, dataset: {}, sources: [],
        append(...nodes) { this.children.push(...nodes); }, replaceChildren(...nodes) { this.children = nodes; },
        setAttribute(key, value) { this.attributes[key] = value; }, removeAttribute(key) { delete this.attributes[key]; if (key === 'src') this._src = ''; },
        addEventListener(key, fn) { this.events[key] = fn; }, focus() { this.focused = true; },
        set src(value) { this._src = value; this.sources.push(value); }, get src() { return this._src; } }; }
    const elements = Object.fromEntries(['cameraSettings', 'cameraForm', 'cameraName', 'cameraUrl', 'cameraMode', 'cameraRefresh', 'cameraHome', 'cameraUsername', 'cameraPassword', 'cameraClearPassword', 'cameraSave', 'cameraNew', 'cameraTest', 'cameraRemove', 'cameraRetry', 'cameraProfiles', 'cameraProfilesEmpty', 'cameraSettingsStatus', 'cameraWidgets', 'homeCameras', 'homeCamerasStatus'].map(id => [id, element()]));
    const form = elements.cameraForm, dialog = elements.cameraSettings;
    form.querySelectorAll = () => ['cameraName', 'cameraUrl', 'cameraMode', 'cameraRefresh', 'cameraHome', 'cameraUsername', 'cameraPassword', 'cameraClearPassword', 'cameraSave', 'cameraNew', 'cameraTest', 'cameraRemove', 'cameraRetry'].map(id => elements[id]);
    dialog.showModal = () => { dialog.open = true; }; dialog.close = () => { dialog.open = false; dialog.events.close(); };
    const document = { getElementById: id => elements[id], createElement: element, events: {}, addEventListener(key, fn) { this.events[key] = fn; } };
    return { document, elements, form, dialog };
}

test('camera profiles are validated and widget responses cannot carry credentials or source URLs', () => {
    assert.equal(validateCameras(settings, true), settings);
    assert.equal(validateCameras({ cameras: [camera] }).cameras[0], camera);
    for (const bad of [null, {}, { cameras: [{ ...camera, id: '../bad' }] }, { cameras: [camera, camera] },
        { cameras: [{ ...camera, url: 'http://private' }] }, { cameras: [{ ...camera, password: 'private' }] },
        { cameras: [{ ...camera, refreshSeconds: 0 }] }, { cameras: [{ ...camera, mode: 'rtsp' }] }]) assert.throws(() => validateCameras(bad));
    assert.throws(() => validateCameras({ ...settings, cameras: [{ ...settings.cameras[0], password: 'leaked' }] }, true));
});

test('feed URLs contain only safe IDs and timestamps; setup rejects embedded passwords and RTSP', () => {
    assert.equal(cameraFeedUrl(id, 42), '../api/cameras/' + id + '/feed?t=42'); assert.throws(() => cameraFeedUrl('https://camera/private'));
    const values = { ...settings.cameras[0], refreshSeconds: 5 };
    assert.equal(validateCameraSetup(values), values);
    for (const url of ['rtsp://192.168.1.50/live', 'file:///private', 'http://viewer:secret@192.168.1.50/live', 'http://192.168.1.50/live#bad'])
        assert.throws(() => validateCameraSetup({ ...values, url }));
    assert.throws(() => validateCameraSetup({ ...values, refreshSeconds: 1 }));
});

test('camera API uses structured JSON, no cache, no commands, and handles an old server clearly', async () => {
    const original = globalThis.fetch, calls = [];
    try {
        globalThis.fetch = async (...args) => { calls.push(args); return new Response(JSON.stringify(settings), { headers: { 'Content-Type': 'application/json' } }); };
        await cameraRequest('settings'); await cameraRequest('save', { password: 'private' });
        assert.equal(calls[0][0], '../api/cameras/settings'); assert.equal(calls[0][1].cache, 'no-store'); assert(!calls[0][1].body);
        assert.equal(calls[1][1].method, 'POST'); assert.equal(JSON.parse(calls[1][1].body).password, 'private');
        await assert.rejects(cameraRequest('http://camera/'), /Invalid camera operation/);
        globalThis.fetch = async () => new Response('<html>old server</html>', { status: 404, headers: { 'Content-Type': 'text/html' } });
        await assert.rejects(cameraRequest('settings'), /updated jaNET server/);
        globalThis.fetch = async () => new Response('{"error":"Camera unavailable"}', { status: 503, headers: { 'Content-Type': 'application/json' } });
        await assert.rejects(cameraRequest(id + '/probe'), /Camera unavailable/);
    } finally { globalThis.fetch = original; }
});

test('snapshot widgets stop and abort on pause, hidden Home or disposal, then resume without leaking URLs', async t => {
    t.mock.timers.enable({ apis: ['setTimeout'] });
    const f = documentFixture(), calls = []; let visible = true;
    const widget = createCameraWidget(f.document, camera, async (...args) => { calls.push(args); return { mode: 'snapshot' }; }, () => visible, () => {});
    try {
        widget.sync(); await tick();
        const image = widget.element.children[1].children[0], buttons = widget.element.children[0].children[1].children;
        assert.equal(calls[0][0], id + '/probe'); assert(image.src.startsWith('../api/cameras/')); assert(!image.src.includes('192.168'));
        image.onload(); t.mock.timers.tick(5000); assert.equal(image.sources.length, 2);
        buttons[1].events.click(); assert(calls[0][2].aborted); assert.equal(image.src, '');
        t.mock.timers.tick(60000); assert.equal(image.sources.length, 2);
        buttons[1].events.click(); await tick(); assert.equal(calls.length, 2);
        visible = false; widget.sync(); assert(calls[1][2].aborted); assert(image.hidden);
        visible = true; widget.sync(); await tick(); assert.equal(calls.length, 3);
    } finally { widget.dispose(); }
});

test('MJPEG uses a persistent image feed and renews it on a bounded timer', async t => {
    t.mock.timers.enable({ apis: ['setTimeout'] });
    const f = documentFixture(); let probes = 0;
    const widget = createCameraWidget(f.document, camera, async () => { probes++; return { mode: 'mjpeg' }; }, () => true, () => {});
    try {
        widget.sync(); await tick(); const image = widget.element.children[1].children[0]; image.onload();
        t.mock.timers.tick(5000); assert.equal(image.sources.length, 1);
        t.mock.timers.tick(235000); await tick(); assert.equal(probes, 2); assert.equal(image.sources.length, 2);
    } finally { widget.dispose(); }
});

test('failed probes retry only while visible and event names are plain text', async t => {
    t.mock.timers.enable({ apis: ['setTimeout'] });
    const f = documentFixture(); let calls = 0, visible = true;
    const widget = createCameraWidget(f.document, { ...camera, name: '<script>bad()</script>' }, async () => { calls++; throw new Error('Offline'); }, () => visible, () => {});
    try {
        widget.sync(); await tick(); assert.equal(widget.element.children[0].children[0].textContent, '<script>bad()</script>');
        assert.equal(widget.element.children[2].textContent, 'Offline');
        t.mock.timers.tick(10000); await tick(); assert.equal(calls, 2);
        visible = false; widget.sync(); t.mock.timers.tick(60000); await tick(); assert.equal(calls, 2);
    } finally { widget.dispose(); }
});

test('a persisted session pause prevents reconnecting a rebuilt widget', async () => {
    const f = documentFixture(); let probes = 0, changed;
    const widget = createCameraWidget(f.document, camera, async () => { probes++; return { mode: 'snapshot' }; }, () => true, () => {}, { paused: true, changed: value => { changed = value; } });
    try {
        widget.sync(); await tick(); assert.equal(probes, 0);
        const pause = widget.element.children[0].children[1].children[1]; assert.equal(pause.attributes['aria-label'], 'Resume camera');
        pause.events.click(); await tick(); assert.equal(changed, false); assert.equal(probes, 1);
    } finally { widget.dispose(); }
});

test('camera settings load retained values with blank passwords and cannot save after a failed load', async () => {
    const f = documentFixture(); let fail = true;
    const editor = wireCameras(f.document, async () => { if (fail) throw new Error('Offline'); return settings; }, () => {}, () => false, () => true);
    await editor.open(); assert(f.elements.cameraSave.disabled); assert(!f.elements.cameraRetry.hidden);
    fail = false; await editor.open(); assert.equal(f.elements.cameraName.value, 'Front door'); assert.equal(f.elements.cameraPassword.value, '');
    assert(f.elements.cameraPassword.placeholder.includes('Saved')); assert(!f.elements.cameraTest.disabled);
    f.elements.cameraUrl.value = 'http://192.168.1.51/new'; f.form.events.input(); assert(f.elements.cameraTest.disabled);
    f.dialog.close(); await editor.open(); assert.equal(f.elements.cameraUrl.value, settings.cameras[0].url);
});

test('save and remove use the current revision and preserve drafts on failure', async () => {
    const f = documentFixture(), calls = []; let fail = true;
    const editor = wireCameras(f.document, async (action, values) => {
        calls.push([action, values]);
        if (action === 'settings') return settings;
        if (action === 'save') { if (fail) throw new Error('Save failed'); return { id, message: 'Saved', settings }; }
        return { message: 'Removed', settings: { revision: 'v2', cameras: [] } };
    }, () => {}, () => false, () => true);
    await editor.open(); f.elements.cameraName.value = 'Draft'; f.form.events.submit({ preventDefault() {} }); await tick();
    assert.equal(f.elements.cameraName.value, 'Draft'); assert.equal(calls.at(-1)[1].revision, 'v1'); assert.equal(calls.at(-1)[1].password, '');
    fail = false; f.form.events.submit({ preventDefault() {} }); await tick();
    assert.equal(f.elements.cameraName.value, 'Front door'); f.elements.cameraRemove.events.click(); await tick();
    assert.deepEqual(calls.at(-1), ['remove', { id, revision: 'v1' }]); assert.equal(f.elements.cameraProfiles.children.length, 0);
});

test('late settings loads cannot overwrite a reopened dialog and closing clears a typed password', async () => {
    const f = documentFixture(); let release, old = true;
    const editor = wireCameras(f.document, async () => old ? new Promise(resolve => { release = resolve; }) : { ...settings, cameras: [{ ...settings.cameras[0], name: 'New camera' }] }, () => {}, () => false);
    const opening = editor.open(); f.dialog.close(); old = false; await editor.open(); release(settings); await opening;
    assert.equal(f.elements.cameraName.value, 'New camera'); f.elements.cameraPassword.value = 'private'; f.dialog.close(); assert.equal(f.elements.cameraPassword.value, '');
});

test('widget settings refresh preserves an explicit session pause', async t => {
    t.mock.timers.enable({ apis: ['setTimeout'] });
    const f = documentFixture(); let version = 'v1', probes = 0, visible = true;
    const manager = wireCameras(f.document, async action => {
        if (action === 'widgets') return { cameras: [{ ...camera, version }] };
        probes++; return { mode: 'snapshot' };
    }, () => {}, () => visible);
    try {
        await manager.refresh(); await tick();
        const controls = f.elements.cameraWidgets.children[0].children[0].children[1].children;
        controls[1].events.click(); const previous = probes;
        version = 'v2'; await manager.invalidate(); await tick();
        assert.equal(probes, previous);
        assert.equal(f.elements.cameraWidgets.children[0].children[0].children[1].children[1].attributes['aria-label'], 'Resume camera');
    } finally { visible = false; await manager.refresh(); }
});
