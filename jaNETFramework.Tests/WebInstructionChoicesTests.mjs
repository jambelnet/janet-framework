// (c) J@mBeL.net 2010-2026. Part of jaNET Framework, GNU GPL version 3 or later.
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
const source = await readFile(new URL('../www/js/instruction-choices.js', import.meta.url), 'utf8');
const { instructionChoices } = await import('data:text/javascript;base64,' + Buffer.from(source).toString('base64'));

test('a multi-action Studio routine is one choice without changing its saved actions', () => {
    const entries = ['living-room-on', 'salute', 'weathertoday'].map(target => Object.freeze({
        id: 'welcome-home', header: 'Welcome home', categ: 'Routines', action: 'judo inset run ' + target
    }));
    const choices = instructionChoices(Object.freeze(entries));
    assert.equal(choices.length, 1);
    assert.equal(choices[0].id, 'welcome-home');
    assert.equal(choices[0].header, 'Welcome home');
    assert.notEqual(choices[0], entries[0]);
    assert.deepEqual(entries.map(entry => entry.action), ['judo inset run living-room-on', 'judo inset run salute', 'judo inset run weathertoday']);
});

test('metadata on later actions remains visible and first populated values win', () => {
    const entries = [
        { id: 'routine', header: null, categ: '', shortdescr: null, ref: null },
        { id: 'routine', header: 'My routine', categ: 'Home', shortdescr: 'Run everything', img: 'light.png', ref: 'sensor' },
        { id: 'routine', header: 'Another header', categ: 'Other', descr: 'Three actions' }
    ];
    const before = JSON.stringify(entries), [choice] = instructionChoices(entries);
    assert.equal(choice.header, 'My routine'); assert.equal(choice.categ, 'Home');
    assert.equal(choice.shortdescr, 'Run everything'); assert.equal(choice.descr, 'Three actions');
    assert.equal(choice.img, 'light.png'); assert.equal(choice.ref, 'sensor');
    assert.equal(JSON.stringify(entries), before);
});

test('exact IDs and legacy starred launchers stay distinct in original order', () => {
    const choices = instructionChoices([{ id: 'hello' }, { id: '*hello' }, { id: 'Hello' }, { id: 'hello' }, { id: '*hello' }, { id: 'last' }]);
    assert.deepEqual(choices.map(choice => choice.id), ['hello', '*hello', 'Hello', 'last']);
});

test('an unlabeled instruction does not gain a dashboard header', () => {
    const [choice] = instructionChoices([{ id: 'hidden', header: null, categ: null }, { id: 'hidden', header: null, categ: null }]);
    assert.equal(choice.header, null); assert.equal(choice.categ, null);
    assert.deepEqual(instructionChoices([{ id: '' }, {}, { id: null }]), []);
});
