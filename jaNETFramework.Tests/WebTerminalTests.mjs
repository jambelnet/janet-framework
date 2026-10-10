// (c) J@mBeL.net 2010-2026, John Ambeliotis. Part of jaNET Framework, licensed under the GNU GPL version 3 or later (see LICENSE).
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';

const helpSource = await readFile(new URL('../www/js/help.js', import.meta.url), 'utf8');
const helpUrl = 'data:text/javascript;base64,' + Buffer.from(helpSource).toString('base64');
const source = (await readFile(new URL('../www/js/terminal.js', import.meta.url), 'utf8')).replace("'./help.js'", JSON.stringify(helpUrl));
const { wireTerminal } = await import('data:text/javascript;base64,' + Buffer.from(source).toString('base64'));

class Node {
    constructor() { this.children = []; this.dataset = {}; this.events = {}; this.value = ''; this.attributes = {}; this.className = ''; this.scrollHeight = 500; this.hidden = false; this.classList = { contains: name => this.className.split(' ').includes(name), toggle() {} }; }
    append(...nodes) { for (const node of nodes) { node.parent = this; this.children.push(node); } }
    replaceChildren(...nodes) { this.children = []; this.append(...nodes); }
    remove() { this.parent.children.splice(this.parent.children.indexOf(this), 1); }
    get firstElementChild() { return this.children[0]; }
    addEventListener(name, action) { this.events[name] = action; }
    setAttribute(name, value) { this.attributes[name] = value; }
    removeAttribute(name) { delete this.attributes[name]; }
    focus() { this.focused = true; }
}

function fixture(run) {
    const ids = Object.fromEntries(['textinput1', 'cmdform', 'response-p2', 'clearBtn', 'page2'].map(id => [id, new Node()]));
    ids.button = new Node(); ids.cmdform.querySelector = () => ids.button;
    globalThis.document = { getElementById: id => ids[id], createElement: () => new Node() };
    return { ...ids, terminal: wireTerminal(run) };
}

test('transcript appends commands and clears successful input', async () => {
    const f = fixture(async () => ''); f.textinput1.value = 'whoami';
    assert.equal(await f.terminal.execute('whoami'), true);
    assert.equal(f.textinput1.value, '');
    assert.equal(f['response-p2'].children[0].children[1].textContent, 'Operation completed: whoami');
    await f.terminal.execute('judo help'); assert.equal(f['response-p2'].children.length, 2);
});

test('request and command failures preserve input and release the prompt', async () => {
    for (const run of [async () => { throw Error('offline'); }, async () => 'Invalid command']) {
        const f = fixture(run); f.textinput1.value = 'retry';
        assert.equal(await f.terminal.execute('retry'), false);
        assert.equal(f.textinput1.value, 'retry'); assert.equal(f.button.disabled, false);
        assert.equal(f['response-p2'].children[0].dataset.state, 'error');
    }
});

test('pending submissions cannot run twice or erase an edited draft', async () => {
    let release; const f = fixture(() => new Promise(resolve => { release = resolve; }));
    f.textinput1.value = 'first'; const pending = f.terminal.execute('first');
    assert.equal(await f.terminal.execute('first'), false);
    f.textinput1.value = 'next draft'; release('done'); await pending;
    assert.equal(f.textinput1.value, 'next draft'); assert.equal(f['response-p2'].children.length, 1);
});

test('history restores an unfinished draft and survives clearing the transcript', async () => {
    const f = fixture(async () => 'done'); await f.terminal.execute('first'); await f.terminal.execute('second');
    const key = key => f.textinput1.events.keydown({ key, preventDefault() {} });
    f.textinput1.value = 'draft'; key('ArrowUp'); assert.equal(f.textinput1.value, 'second');
    key('ArrowUp'); assert.equal(f.textinput1.value, 'first');
    key('ArrowDown'); key('ArrowDown'); assert.equal(f.textinput1.value, 'draft');
    f.terminal.clear(); assert.equal(f['response-p2'].children.length, 0);
    key('ArrowUp'); assert.equal(f.textinput1.value, 'second');
});

test('transcript and history are bounded to 100 entries', async () => {
    const f = fixture(async () => 'done');
    for (let i = 0; i < 105; i++) await f.terminal.execute('command ' + i);
    assert.equal(f['response-p2'].children.length, 100);
    for (let i = 0; i < 105; i++) f.textinput1.events.keydown({ key: 'ArrowUp', preventDefault() {} });
    assert.equal(f.textinput1.value, 'command 5');
});
