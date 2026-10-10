// (c) J@mBeL.net 2010-2026, John Ambeliotis. Part of jaNET Framework, licensed under the GNU GPL version 3 or later (see LICENSE).

import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';

// The browser uses ES modules, without requiring a Node package configuration.
const source = await readFile(new URL('../www/js/help.js', import.meta.url), 'utf8');
const { parseHelp, responseAppearance, commandResponse } = await import('data:text/javascript;base64,' + Buffer.from(source).toString('base64'));
const header = '1. Instruction Sets & Events\n     1.1 Add New Instruction Set\n';

test('groups aliases without losing distinct signatures or literal lock tags', () => {
    const result = parseHelp(header + [
        '+ judo inset add [ID] <lock>[Action]</lock>',
        '+ judo inset new [ID] <lock>[Action]</lock>',
        '+ judo inset set [ID] <lock>[Action]</lock>',
        '+ judo inset setup [ID] <lock>[Action]</lock>',
        '+ judo inset add [ID] <lock>[Action]</lock> [Category]',
        '+ judo inset new [ID] <lock>[Action]</lock> [Category]'
    ].join('\n'));
    const commands = result.sections[0].topics[0].commands;
    assert.equal(commands.length, 2);
    assert.equal(commands[0].syntax, 'judo inset add [ID] <lock>[Action]</lock>');
    assert.deepEqual(commands[0].aliases, ['new', 'set', 'setup']);
    assert.deepEqual(commands[1].aliases, ['new']);
    assert.equal(commands.reduce((count, command) => count + 1 + command.aliases.length, 0), 6);
});

test('commands whose third token is an argument are not grouped', () => {
    const result = parseHelp('12. Ping\n12.1 Ping\n+ judo ping [Host]\n+ judo ping [Host] [Timeout]');
    assert.deepEqual(result.sections[0].topics[0].commands.map(c => c.syntax), ['judo ping [Host]', 'judo ping [Host] [Timeout]']);
});

test('supports CRLF, LF, blank lines, multiple sections, and footnotes', () => {
    const text = header + '+ judo inset list\n\n2. Mail\n2.1 SMTP\n+ judo smtp settings\n(*) A note\n';
    const result = parseHelp(text);
    assert.deepEqual(parseHelp(text.replaceAll('\n', '\r\n')), result);
    assert.equal(result.sections.length, 2);
    assert.deepEqual(result.notes, ['(*) A note']);
});

test('ordinary responses and incomplete help fall back to plain text', () => {
    for (const text of ['', 'Settings saved.', '1. Chapter', header, header + '+ judo inset list\nUnexpected text', '+ judo help']) {
        assert.equal(parseHelp(text), null, text);
    }
});

test('duplicate alias lines do not repeat the alias list', () => {
    const result = parseHelp(header + '+ judo inset add [ID]\n+ judo inset new [ID]\n+ judo inset new [ID]');
    assert.deepEqual(result.sections[0].topics[0].commands[0].aliases, ['new']);
});

test('acknowledgments, ordinary answers and explicit states stay distinct', () => {
    for (const text of ['Operation completed.', 'Element added.', 'Elements removed.', 'Settings saved.']) assert.equal(responseAppearance(text).state, 'success');
    assert.equal(responseAppearance('jaNET could not complete the request.').state, 'error');
    assert.equal(responseAppearance('No matching command found.').state, 'error');
    assert.equal(responseAppearance('jambel').state, 'neutral');
    assert.equal(responseAppearance('The word error appears in this ordinary reply.').state, 'neutral');
    assert.equal(responseAppearance('Running your command...', 'busy').state, 'busy');
    assert.equal(responseAppearance('Which command did you mean?', 'choice').state, 'choice');
    assert.equal(responseAppearance('Success is not implied by unknown output.', 'invalid-state').state, 'neutral');
});

test('generic completion identifies the command without changing ordinary output', () => {
    for (const output of ['', '  ', 'Operation completed.', 'operation completed']) {
        assert.equal(commandResponse(output, 'whoami'), 'Operation completed: whoami');
    }
    assert.equal(commandResponse('jambel', 'whoami'), 'jambel');
    assert.equal(commandResponse('Failed: unavailable', 'whoami'), 'Failed: unavailable');
    assert.equal(commandResponse('', '{mute}judo smtp setup <lock>secret</lock>'), 'Operation completed: judo smtp setup [redacted]');
    assert.equal(responseAppearance(commandResponse('', 'whoami')).state, 'success');
});
