// (c) J@mBeL.net 2010-2026, John Ambeliotis. Part of jaNET Framework, GNU GPL version 3 or later; see LICENSE.
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
const source = await readFile(new URL('../www/js/intents.js', import.meta.url), 'utf8');
const { resolveIntent } = await import('data:text/javascript;base64,' + Buffer.from(source).toString('base64'));
const instructions = [
    { id: 'whoami', header: 'Login Name' },
    { id: 'kitchen_light_on', header: 'Kitchen light on' },
    { id: 'kitchen_light_off', header: 'Kitchen light off' },
    { id: 'one', header: 'Duplicate' }, { id: 'two', header: 'Duplicate' },
    { id: '*private_helper', header: 'Private helper' },
    { id: 'greek', header: '\u03c6\u03c9\u03c2' }
];
assert.equal(resolveIntent('Who am I?', instructions).command, 'whoami');
assert.equal(resolveIntent('please WHO-AM-I!', instructions).command, 'whoami');
assert.equal(resolveIntent('Login name please.', instructions).command, 'whoami');
assert.equal(resolveIntent('who am i').command, '%whoami%');
assert.equal(resolveIntent('what is my name?').command, '%whoami%');
assert.equal(resolveIntent('What time is it?').command, '%time24%');
assert.equal(resolveIntent("What's the weather today?").command.includes('%currenttemperature%'), true);
assert.equal(resolveIntent('check out').command, '%checkout%');
assert.equal(resolveIntent('Kitchen-light ON!', instructions).command, 'kitchen_light_on');
assert.equal(resolveIntent('\u03a6\u03a9\u03a3', instructions).command, 'greek');
const partial = resolveIntent('turn on kitchen light', instructions);
assert.equal(partial.command, null);
assert(partial.suggestions.some(item => item.command === 'kitchen_light_on'));
const ambiguous = resolveIntent('Duplicate', instructions);
assert.equal(ambiguous.command, null);
assert.deepEqual(ambiguous.suggestions.map(item => item.command), ['one', 'two']);
assert.equal(resolveIntent('private helper', instructions).command, null);
assert(!resolveIntent('private helper', instructions).suggestions.some(item => item.command.startsWith('*')));
assert.equal(resolveIntent('judo serial close', instructions).command, 'judo serial close');
assert.equal(resolveIntent('%whoami% %whereami%', instructions).command, '%whoami% %whereami%');
for (const input of ['', '???', 'please', 'do something random', 'kill everything']) assert.equal(resolveIntent(input, instructions).command, null);
console.log('Intent tests passed: spaced IDs, friendly names, Unicode, aliases, ambiguity, partial choices and legacy syntax.');
