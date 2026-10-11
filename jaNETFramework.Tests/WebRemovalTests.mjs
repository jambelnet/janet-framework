// (c) J@mBeL.net 2010-2026. Part of jaNET Framework, GNU GPL version 3 or later.
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
const source = await readFile(new URL('../www/js/elements.js', import.meta.url), 'utf8');
const { removalItems, filterRemovalItems, removalCommands, wireRemovalEditor } = await import('data:text/javascript;base64,' + Buffer.from(source).toString('base64'));

test('removal list groups launchers and duplicate IDs while keeping event-only handlers', () => {
    const items = removalItems([
        { id: '*Notepad' }, { id: 'Notepad', header: 'Text editor', categ: 'Desktop' }, { id: '*Notepad' },
        { id: 'oncheckin' }, { id: 'raw' }, { id: '*standalone' }, { id: '' }
    ], ['oncheckin', 'eventOnly', 'eventOnly']);
    assert.equal(items.length, 5);
    assert.equal(items.find(item => item.id === 'Notepad').launcher, true);
    assert.equal(items.find(item => item.id === 'oncheckin').event, true);
    assert.equal(items.find(item => item.id === 'eventOnly').instruction, false);
    assert.deepEqual(filterRemovalItems(items, ' DESKTOP ').map(item => item.id), ['Notepad']);
    assert.deepEqual(filterRemovalItems(items, 'text editor').map(item => item.id), ['Notepad']);
    assert.deepEqual(filterRemovalItems(items, 'nonexistent'), []);
    assert.deepEqual(removalCommands(items.find(item => item.id === 'standalone')), ['judo inset remove <lock>standalone</lock>']);
});

test('removal targets only the selected identifier and explicitly included events', () => {
    const item = { id: 'who am I & say "hello"; 100%', instruction: true, event: true };
    assert.deepEqual(removalCommands(item, false), ['judo inset remove <lock>' + item.id + '</lock>']);
    assert.deepEqual(removalCommands(item, true), ['judo inset remove <lock>' + item.id + '</lock>', 'judo event remove <lock>' + item.id + '</lock>']);
    assert.deepEqual(removalCommands({ id: 'onlyEvent', event: true }, false), ['judo event remove <lock>onlyEvent</lock>']);
    assert.throws(() => removalCommands(null));
    assert.throws(() => removalCommands({ id: 'bad</lock>& judo inset remove other', instruction: true }));
    assert.throws(() => removalCommands({ id: 'bad\nname', instruction: true }));
});

class Element {
    value = ''; textContent = ''; checked = false; disabled = false; hidden = false;
    children = []; handlers = new Map();
    addEventListener(name, callback) { this.handlers.set(name, callback); }
    append(...children) { this.children.push(...children); }
    replaceChildren(...children) { this.children = children; }
}

function editor(load, run = async () => 'Element removed.') {
    const elements = new Map();
    globalThis.document = {
        getElementById(id) { if (!elements.has(id)) elements.set(id, new Element()); return elements.get(id); },
        createElement() { return new Element(); }
    };
    let refreshed = 0;
    const notices = [];
    const controller = wireRemovalEditor(load, run, async () => { refreshed++; }, text => notices.push(text));
    const element = id => elements.get(id);
    const choose = id => element('removalOptions').children.find(label => label.children[0].value === id).children[0].handlers.get('change')();
    return { controller, element, choose, notices, refreshed: () => refreshed };
}

test('filtering a selected item out prevents deleting an invisible target', async () => {
    const ui = editor(async () => removalItems([{ id: 'Notepad' }, { id: '*Notepad' }], []));
    await ui.controller.load();
    assert.equal(ui.element('removeElementBtn').disabled, true);
    ui.choose('Notepad'); assert.equal(ui.element('removeElementBtn').disabled, false);
    assert.equal(ui.element('removalStatus').textContent, 'Remove: Notepad, *Notepad');
    ui.element('elementNameRemove').value = 'missing'; ui.element('elementNameRemove').handlers.get('input')();
    assert.equal(ui.element('removeElementBtn').disabled, true);
    assert.equal(ui.element('removalStatus').textContent, 'No matching elements.');
    assert.equal(await ui.controller.remove(), false);
});

test('failed removal keeps the selection and never reports success', async () => {
    const ui = editor(async () => removalItems([{ id: 'Notepad' }], []), async () => 'Access denied.');
    await ui.controller.load(); ui.choose('Notepad');
    assert.equal(await ui.controller.remove(), false);
    assert.equal(ui.element('removalStatus').textContent, 'Access denied.');
    assert.equal(ui.element('removeElementBtn').disabled, false);
    assert.equal(ui.element('removalOptions').disabled, false);
    assert.equal(ui.refreshed(), 0);
    assert.deepEqual(ui.notices, ['Access denied.']);
    assert.equal(await ui.controller.remove(), false); // The selected name remains available after a failure.
});

test('pending removal cannot execute twice and event-only selection never removes an instruction', async () => {
    let resolve;
    const commands = [];
    const ui = editor(async () => removalItems([], ['eventOnly']), command => {
        commands.push(command); return new Promise(done => { resolve = done; });
    });
    await ui.controller.load(); ui.choose('eventOnly');
    assert.equal(ui.element('chkEvent').checked, true);
    assert.equal(ui.element('chkEvent').disabled, true);
    const pending = ui.controller.remove();
    assert.equal(ui.element('removeElementBtn').disabled, true);
    assert.equal(await ui.controller.remove(), false);
    resolve('Element removed.'); assert.equal(await pending, true);
    assert.deepEqual(commands, ['judo event remove <lock>eventOnly</lock>']);
    assert.equal(ui.refreshed(), 1);
    assert.deepEqual(ui.notices, ['Removed: eventOnly.']);
});

test('a failed list load leaves no stale targets available for removal', async () => {
    const ui = editor(async () => { throw new Error('HTTP 503'); });
    await ui.controller.load();
    assert.equal(ui.element('removalStatus').textContent, 'HTTP 503');
    assert.equal(ui.element('removeElementBtn').disabled, true);
    assert.equal(ui.element('removalOptions').children.length, 0);
    assert.equal(await ui.controller.remove(), false);
});
