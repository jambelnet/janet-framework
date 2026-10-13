// (c) J@mBeL.net 2010-2026. Part of jaNET Framework, GNU GPL version 3 or later.
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
const source = await readFile(new URL('../www/js/settings.js', import.meta.url), 'utf8');
const { applySettings, wireSettings, settingsDialogs } = await import('data:text/javascript;base64,' + Buffer.from(source).toString('base64'));

function field(id, type = 'text') { return { id, type, tagName: 'INPUT', value: 'stale', checked: true, required: true, placeholder: 'Password', dataset: {}, disabled: false }; }

test('snapshots populate exact values, false checkboxes and zero ports, and mark retained secrets', () => {
    const fields = [field('mailSubject'), field('port'), field('ssl', 'checkbox'), field('password', 'password')];
    const form = { querySelectorAll: () => fields };
    applySettings(form, { values: { mailSubject: 'Home  alert `ready`', port: 0, ssl: false, password: '' }, secrets: { password: true } });
    assert.equal(fields[0].value, 'Home  alert `ready`'); assert.equal(fields[1].value, '0'); assert.equal(fields[2].checked, false);
    assert.equal(fields[3].value, ''); assert.equal(fields[3].required, false); assert.match(fields[3].placeholder, /Saved/);
    applySettings(form, { values: { mailSubject: '', port: 110, ssl: true, password: '' }, secrets: { password: false } });
    assert.equal(fields[0].value, ''); assert.equal(fields[3].required, true); assert.equal(fields[3].placeholder, 'Password');
});

test('partial, invalid and secret-bearing responses fail without changing any field', () => {
    const fields = [field('first'), field('password', 'password'), field('ssl', 'checkbox')];
    for (const values of [{ first: 'new' }, { first: 'new', password: 'secret', ssl: false }, { first: 'new', password: '', ssl: 'false' }]) {
        assert.throws(() => applySettings({ querySelectorAll: () => fields }, { values }));
        assert(fields.every(field => field.value === 'stale'));
    }
});

function fixture() {
    function element() { return { hidden: false, dataset: {}, handlers: {}, setAttribute() {}, addEventListener(name, handler) { this.handlers[name] = handler; } }; }
    const dialogs = new Map(settingsDialogs.map(id => {
        const input = field('value'), button = { ...element(), type: 'submit', disabled: false };
        const form = { querySelectorAll: selector => selector.includes('button') ? [input, button] : [input], querySelector: () => ({ prepend() {} }) };
        const dialog = { ...element(), open: false, querySelector: () => form, showModal() { this.open = true; }, close() { this.open = false; this.handlers.close(); }, input, button };
        return [id, dialog];
    }));
    return { dialogs, document: { getElementById: id => dialogs.get(id), createElement: element } };
}

test('every supported modal loads afresh, and loading or failed reads cannot save', async () => {
    const f = fixture(); let rejectLoad = false, writes = 0, reads = 0;
    const editor = wireSettings(f.document, async () => { reads++; if (rejectLoad) throw Error(); return { values: { value: 'saved' } }; }, async () => { writes++; return { message: 'Settings saved.' }; });
    for (const id of settingsDialogs) {
        await editor.open(id); assert(editor.ready(id)); assert.equal(f.dialogs.get(id).input.value, 'saved'); f.dialogs.get(id).close();
    }
    assert.equal(reads, settingsDialogs.length); assert.equal(writes, 0);
    rejectLoad = true; await editor.open('mailheaderSettings');
    assert(!editor.ready('mailheaderSettings')); assert(f.dialogs.get('mailheaderSettings').button.disabled);
    await assert.rejects(editor.persist('mailheaderSettings')); assert.equal(writes, 0);
    rejectLoad = false; await editor.open('mailheaderSettings'); await editor.persist('mailheaderSettings'); assert.equal(writes, 1);
});

test('late responses from a closed modal cannot overwrite its newer load', async () => {
    const f = fixture(), pending = [];
    const editor = wireSettings(f.document, () => new Promise(resolve => pending.push(resolve)), async () => { throw Error('unexpected save'); });
    const first = editor.open('mailheaderSettings'); f.dialogs.get('mailheaderSettings').close();
    const second = editor.open('mailheaderSettings'); pending[1]({ values: { value: 'new' } }); await second;
    pending[0]({ values: { value: 'old' } }); await first;
    assert.equal(f.dialogs.get('mailheaderSettings').input.value, 'new'); assert(editor.ready('mailheaderSettings'));
});

test('settings transport is structured, uncached and does not execute a command', async () => {
    const apiSource = await readFile(new URL('../www/js/api.js', import.meta.url), 'utf8');
    const { loadSettings, saveSettings } = await import('data:text/javascript;base64,' + Buffer.from(apiSource).toString('base64'));
    const calls = []; globalThis.fetch = async (url, options) => { calls.push({ url, options }); return { ok: true, json: async () => ({ message: 'Settings saved.' }) }; };
    await loadSettings('mailheaderSettings'); await saveSettings('mailheaderSettings', { mailSubject: 'Home  alert `ready`' });
    assert.equal(calls[0].url, '../api/settings/mailheaderSettings'); assert.equal(calls[0].options.method, undefined);
    assert.equal(calls[1].options.method, 'POST'); assert.equal(JSON.parse(calls[1].options.body).mailSubject, 'Home  alert `ready`');
    assert(calls.every(call => call.options.cache === 'no-store' && !call.url.includes('cmd=')));
});

test('a queued close event from the previous dialog does not invalidate a quick reopen', async () => {
    const f = fixture(), pending = [];
    const editor = wireSettings(f.document, () => new Promise(resolve => pending.push(resolve)), async () => ({ message: 'Settings saved.' }));
    const first = editor.open('mailheaderSettings');
    const dialog = f.dialogs.get('mailheaderSettings'); dialog.open = false;
    const second = editor.open('mailheaderSettings'); dialog.handlers.close();
    pending[1]({ values: { value: 'current' } }); await second;
    pending[0]({ values: { value: 'old' } }); await first;
    assert(editor.ready('mailheaderSettings')); assert.equal(dialog.input.value, 'current');
});
