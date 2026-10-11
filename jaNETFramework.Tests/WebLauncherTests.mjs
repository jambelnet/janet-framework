// (c) J@mBeL.net 2010-2026. Part of jaNET Framework, GNU GPL version 3 or later.
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
const source = await readFile(new URL('../www/js/launchers.js', import.meta.url), 'utf8');
const { launcherAction, wireLauncherEditor } = await import('data:text/javascript;base64,' + Buffer.from(source).toString('base64'));

test('launcher modes preserve existing actions and argument quoting', () => {
    assert.equal(launcherAction('  salute  '), 'salute');
    assert.equal(launcherAction('./notepad.exe'), './notepad.exe');
    assert.equal(launcherAction('./notepad.exe', 'launch'), './[launch]notepad.exe');
    const program = "./'C:\\Program Files\\tool.exe' '--name \"John Doe\"'";
    assert.equal(launcherAction(program, 'capture', 120), './[capture:120]' + program.slice(2));
    assert.equal(launcherAction('./[launch]notepad.exe', 'capture'), './notepad.exe');
    assert.equal(launcherAction('./[capture:90]notepad.exe', 'launch'), './[launch]notepad.exe');
    for (const seconds of [0, 301, 1.5, 'abc']) assert.throws(() => launcherAction(program, 'capture', seconds));
    assert.throws(() => launcherAction('salute', 'launch'));
});

test('program controls appear only for external actions and reset safely', async () => {
    const ids = Object.fromEntries(['launcherAction', 'launcherMode', 'launcherTimeout', 'launcherExecution', 'launcherTimeoutField'].map(id => [id, { value: '', events: {}, addEventListener(event, callback) { this.events[event] = callback; } }]));
    const form = { events: {}, addEventListener(event, callback) { this.events[event] = callback; } };
    ids.launcherAction.form = form; ids.launcherMode.value = 'capture';
    globalThis.document = { getElementById: id => ids[id] };
    wireLauncherEditor(); assert.equal(ids.launcherExecution.hidden, true);
    ids.launcherAction.value = './notepad.exe'; ids.launcherAction.events.input();
    assert.equal(ids.launcherExecution.hidden, false); assert.equal(ids.launcherTimeout.disabled, false);
    ids.launcherMode.value = 'launch'; ids.launcherMode.events.change();
    assert.equal(ids.launcherTimeoutField.hidden, true); assert.equal(ids.launcherTimeout.disabled, true);
    ids.launcherAction.value = './[capture:120]notepad.exe'; ids.launcherAction.events.input();
    assert.equal(ids.launcherMode.value, 'capture'); assert.equal(ids.launcherTimeout.value, '120');
    assert.equal(ids.launcherTimeoutField.hidden, false);
    ids.launcherAction.value = './[launch]notepad.exe'; ids.launcherAction.events.input();
    assert.equal(ids.launcherMode.value, 'launch'); assert.equal(ids.launcherTimeout.disabled, true);
    ids.launcherAction.value = ''; ids.launcherMode.value = 'capture'; form.events.reset(); await Promise.resolve();
    assert.equal(ids.launcherExecution.hidden, true); assert.equal(ids.launcherMode.disabled, true);
});
