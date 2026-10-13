// (c) J@mBeL.net 2010-2026. GNU GPL version 3 or later.
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
const source = await readFile(new URL('../www/js/studio-model.js', import.meta.url), 'utf8');
const { parseBlock, newBlock, compileBlock, buildGraph, groupInstructions, editableId, eventActions, actionDependencies, parseEventAction, compileEventBlocks, parseProgram } = await import('data:text/javascript;base64,' + Buffer.from(source).toString('base64'));
const snapshot = { instructions: [
    { id: 'hello', action: '*hello' }, { id: '*hello', action: 'Hello, %user%.' },
    { id: 'sequence', action: 'judo inset run hello' }, { id: 'sequence', action: 'judo sleep 100' },
    { id: 'cycle', action: 'judo inset run cycle' }
], functions: ['%user%', '%mute%'], events: [{ id: 'checkin', action: 'hello; hello; %mute%' }] };
test('existing sources round trip exactly including whitespace and opaque scripts', () => {
    for (const value of ['Hi', '%user%', '%~>checkin%', '*hello', 'judo inset run hello', 'judo sleep 0001', './[capture:120]`script`', '{ evalBool(true); hello; goodbye; }', ' A; B & C\n%user% ', 'judo inset add <lock>a;b</lock>']) assert.equal(compileBlock(parseBlock(value)), value);
});
test('new blocks compile and validate engine-specific expressions', () => {
    assert.equal(compileBlock(newBlock('instruction', 'hello')), 'judo inset run hello');
    assert.equal(compileBlock(newBlock('event', 'checkin')), '%~>checkin%');
    assert.equal(compileBlock(parseBlock('%custom.value%')), '%custom.value%');
    assert.throws(() => compileBlock(newBlock('instruction', 'bad;command')));
    assert.throws(() => compileBlock(newBlock('delay', '300001')));
    assert.equal(compileBlock({ kind: 'condition', expression: 'true', yes: 'hello', no: 'sequence' }), '{ evalBool(true); hello; sequence; }');
    assert.throws(() => compileBlock({ kind: 'condition', expression: 'true', yes: 'judo sleep 100', no: '' }));
});
test('canonical legacy pairs open their launcher source', () => {
    assert.equal(editableId('hello', groupInstructions(snapshot)), '*hello');
    assert.equal(editableId('sequence', groupInstructions(snapshot)), 'sequence');
    const resized = buildGraph(snapshot, 'hello', ['First', 'Second'], 180, '*hello');
    assert.equal(resized.nodes.filter(node => node.label === '*hello').length, 0);
    assert.deepEqual(resized.nodes.filter(node => node.sourceId === '*hello').map(node => node.detail), ['First', 'Second']);
    assert.equal(resized.warnings.length, 0);
});
test('graph uses exact starred IDs and detects missing positions', () => {
    const graph = buildGraph(snapshot, 'hello');
    assert.equal(graph.nodes.filter(node => node.label === '*hello').length, 0);
    assert.ok(graph.edges.some(edge => edge.label === 'Action 1 via *hello'));
    assert.ok(graph.nodes.some(node => node.label === '%user%'));
    assert.equal(graph.warnings.length, 0);
    const multiple = { ...snapshot, instructions: [...snapshot.instructions, { id: 'hello', action: '*hello' }] };
    assert.ok(buildGraph(multiple, 'hello').warnings.some(w => w.includes('Missing *hello action 2')));
});
test('map follows calls, event deduplication, draft dependencies and bounded cycles', () => {
    assert.ok(buildGraph(snapshot, 'sequence').nodes.some(node => node.sourceId === '*hello'));
    assert.ok(buildGraph(snapshot, 'cycle').warnings.some(w => w.includes('Cycle')));
    const graph = buildGraph(snapshot, 'draft', ['%~>checkin%']);
    assert.equal(graph.nodes.filter(node => node.kind === 'instruction' && node.label === 'hello').length, 1);
    assert.ok(graph.nodes.some(node => node.label === '%mute%'));
    assert.ok(buildGraph(snapshot, 'draft', Array(50).fill('judo inset run sequence'), 12).nodes.length <= 12);
    assert.ok(buildGraph(snapshot, 'draft', Array(50).fill('judo inset run sequence'), 12).warnings.some(w => w.includes('limited')));
});
test('conditions keep both possible branches; locks differ between stored actions and event Run entry', () => {
    const graph = buildGraph(snapshot, 'draft', ['{ evalBool(true); hello; sequence; }']);
    assert.ok(graph.edges.some(edge => edge.label === 'true'));
    assert.ok(graph.edges.some(edge => edge.label === 'false'));
    assert.ok(graph.warnings.some(w => w.includes('before branch')));
    assert.ok(buildGraph(snapshot, 'draft', ['judo inset add <lock>%user%;*hello</lock>']).nodes.some(node => node.label === '*hello'));
    const lockedEvent = { ...snapshot, events: [{ id: 'locked', action: 'judo inset add <lock>%user%;*hello</lock>' }] };
    const eventGraph = buildGraph(lockedEvent, 'draft', ['%~>locked%']);
    assert.equal(eventGraph.nodes.length, 4);
    assert.equal(eventGraph.nodes.filter(node => node.kind === 'function').length, 0);
});
test('simple aliases show one template with source provenance, not duplicate pointer steps', () => {
    const salute = { instructions: [{ id: 'salute', action: '*salute' }, { id: '*salute', action: 'Good %salute% %user%.' }], functions: ['%salute%', '%user%'], events: [] };
    const graph = buildGraph(salute, 'salute');
    assert.deepEqual(graph.nodes.map(node => node.kind), ['instruction', 'template', 'function', 'function']);
    assert.equal(graph.nodes[1].sourceId, '*salute'); assert.equal(graph.nodes[1].sourcePosition, 0);
    assert.equal(graph.nodes[1].detail, 'Good %salute% %user%.');
    assert.equal(graph.edges[0].label, 'Action 1 via *salute');
    assert.equal(graph.nodes.some(node => node.label === '*salute'), false);
    const direct = buildGraph(salute, '*salute');
    assert.equal(direct.nodes.filter(node => node.label === '*salute').length, 1);
    assert.equal(direct.nodes[1].kind, 'template');
});
test('non-alias pointer actions retain positional targets and bounded cycles', () => {
    const values = { ...snapshot, instructions: [...snapshot.instructions, { id: 'mixed', action: '*hello' }, { id: 'mixed', action: 'Done.' }, { id: 'recursive', action: '*recursive' }, { id: '*recursive', action: '*recursive' }] };
    const graph = buildGraph(values, 'mixed');
    assert.equal(graph.nodes.filter(node => node.label === '*hello').length, 1);
    assert.equal(graph.nodes.find(node => node.label === '*hello').kind, 'instruction');
    assert.equal(graph.nodes.find(node => node.label === 'Done.').kind, 'text');
    assert.ok(buildGraph(values, 'recursive').warnings.some(warning => warning.includes('Cycle')));
    assert.equal(compileBlock(parseBlock('*hello')), '*hello');
});

test('templates, launchers and existing conditions get typed editors without rewriting source', () => {
    assert.equal(parseBlock('Good %salute% %user%.').kind, 'template');
    assert.equal(parseBlock('*hello').kind, 'pointer');
    assert.equal(compileBlock(newBlock('pointer', '*hello')), '*hello');
    assert.throws(() => compileBlock(newBlock('pointer', 'hello')));
    const source = '{  evalBool( "%day%" == "Sunday" ); hello  sequence ; cycle ;}';
    const block = parseBlock(source);
    assert.equal(block.kind, 'condition');
    assert.equal(compileBlock(block), source);
    block.yes = 'hello';
    assert.equal(compileBlock(block), '{ evalBool( "%day%" == "Sunday" ); hello; cycle; }');
    assert.equal(parseBlock('{ evalBool(true); judo sleep 1; hello; }').kind, 'raw');
    assert.equal(parseBlock('{ complex script; }').kind, 'raw');
});

test('builder previews event actions from typed, raw and composite references without changing data', () => {
    const values = { ...snapshot, events: [{ id: 'oncheckin', action: '%mute%; hello; weathertoday; hello' }, { id: 'oncheckin', action: 'wrong' }] };
    const before = JSON.stringify(values);
    for (const block of [newBlock('event', 'oncheckin'), { kind: 'raw', value: '%~>oncheckin%' }]) {
        const preview = actionDependencies(values, compileBlock(block));
        assert.deepEqual(preview[0].actions, ['%mute%', 'hello', 'weathertoday']);
        assert.equal(preview[0].kind, 'event');
        assert.equal(compileBlock(block), '%~>oncheckin%');
    }
    assert.equal(actionDependencies(values, 'Before %~>oncheckin% after %~>oncheckin%').filter(item => item.kind === 'event').length, 1);
    assert.equal(JSON.stringify(values), before);
});

test('builder follows instruction aliases and positional launcher references, not arbitrary text', () => {
    const values = { ...snapshot, instructions: [...snapshot.instructions, { id: '*hello', action: 'Second' }] };
    const invoke = actionDependencies(snapshot, 'judo inset run hello')[0];
    assert.equal(invoke.sourceId, '*hello');
    assert.deepEqual(invoke.actions, ['Hello, %user%.']);
    assert.deepEqual(actionDependencies(values, '*hello', 1)[0].actions, ['Second']);
    assert.equal(actionDependencies(values, '*hello', 2)[0].missing, true);
    assert.deepEqual(actionDependencies(snapshot, 'hello'), []);
    assert.deepEqual(actionDependencies(snapshot, 'Good %user% %user%.').map(item => item.id), ['%user%']);
    const draft = groupInstructions(snapshot, 'hello', ['Changed', 'Second'], '*hello');
    assert.deepEqual(actionDependencies(snapshot, 'judo inset run hello', 0, draft)[0].actions, ['Changed', 'Second']);
    assert.deepEqual(actionDependencies(snapshot, '*hello', 1, draft)[0].actions, ['Second']);
    assert.equal(snapshot.instructions.find(entry => entry.id === '*hello').action, 'Hello, %user%.');
});

test('builder previews both condition branches, unknown targets and locked or empty events safely', () => {
    const dependencies = actionDependencies(snapshot, '{ evalBool(true); hello sequence; cycle; }');
    assert.deepEqual(dependencies.map(item => [item.id, item.branch]), [['hello', 'When true'], ['sequence', 'When true'], ['cycle', 'When false']]);
    assert.equal(actionDependencies(snapshot, 'judo inset run missing')[0].missing, true);
    assert.equal(actionDependencies(snapshot, '%missing%')[0].missing, true);
    assert.equal(actionDependencies(snapshot, '%~>missing%')[0].missing, true);
    assert.deepEqual(eventActions(' hello & hello ; %mute%; '), ['hello', '%mute%']);
    const locked = 'judo inset add <lock>%user%;*hello</lock>';
    assert.deepEqual(eventActions(locked), [locked]);
    assert.deepEqual(eventActions(' ; & '), []);
    const recursive = { ...snapshot, events: [{ id: 'recursive', action: '%~>recursive%' }] };
    assert.deepEqual(actionDependencies(recursive, '%~>recursive%')[0].actions, ['%~>recursive%']);
});

test('event editor keeps calls distinct from text and compiles ordered steps with runtime-safe limits', () => {
    const blocks = eventActions('%mute%; hello; judo sleep 100; sequence').map(parseEventAction);
    assert.equal(blocks[1].kind, 'instruction'); assert.equal(blocks[1].invocation, 'bare');
    assert.deepEqual(compileEventBlocks(blocks), ['%mute%', 'hello', 'judo sleep 100', 'sequence']);
    assert.deepEqual(compileEventBlocks([blocks[3], blocks[1], blocks[0]]), ['sequence', 'hello', '%mute%']);
    assert.deepEqual(compileEventBlocks([]), []);
    assert.equal(compileBlock(parseEventAction('judo inset run hello')), 'judo inset run hello');
    assert.equal(parseEventAction('*hello').kind, 'instruction');
    assert.equal(compileBlock(parseEventAction('*hello')), '*hello');
    assert.throws(() => compileEventBlocks([newBlock('text', 'Hello')]), /text or templates/);
    assert.throws(() => compileEventBlocks([newBlock('template')]), /text or templates/);
    assert.throws(() => compileEventBlocks([newBlock('text')]), /empty/);
    assert.throws(() => compileEventBlocks([blocks[1], blocks[1]]), /duplicate/);
    assert.throws(() => compileEventBlocks([parseBlock('{ evalBool(true); hello; sequence; }')]), /condition/);
    assert.throws(() => compileEventBlocks([newBlock('raw', 'A & B')]), /ampersands/);
    const locked = parseBlock('judo inset add <lock>a;b</lock>');
    assert.deepEqual(compileEventBlocks([locked]), [locked.value]);
    assert.throws(() => compileEventBlocks([locked, blocks[1]]), /locked/);
});

test('event map roots at the active event, uses its draft and bounds event cycles', () => {
    const graph = buildGraph(snapshot, 'checkin', ['sequence', '%mute%'], 180, 'checkin', 'event');
    assert.equal(graph.nodes[0].kind, 'event'); assert.equal(graph.nodes[0].label, 'checkin');
    assert.ok(graph.nodes.some(node => node.label === 'sequence'));
    assert.equal(snapshot.events[0].action, 'hello; hello; %mute%');
    const values = { ...snapshot, events: [{ id: 'a', action: '%~>b%' }, { id: 'b', action: '%~>a%' }] };
    const cycle = buildGraph(values, 'a', null, 180, 'a', 'event');
    assert.ok(cycle.warnings.some(warning => warning.includes('Event cycle')));
    assert.ok(cycle.nodes.length < 10);
    const legacy = buildGraph(snapshot, 'old', ['*hello'], 180, 'old', 'event');
    assert.equal(legacy.nodes[1].label, 'hello');
});

test('structured program controls preserve legacy source and expose launch, capture, arguments and deadlines', () => {
    for (const source of ['./notepad.exe', './[launch]notepad.exe', "./[capture:00120]'C:/my script.cmd' '--flag \"two words\"'", './`program path` `arguments`']) {
        const block = parseBlock(source); assert.ok(block.program); assert.equal(compileBlock(block), source);
    }
    const block = newBlock('program');
    block.program.mode = 'capture'; block.program.target = 'C:/my script.cmd'; block.program.arguments = '--flag "two words"'; block.program.timeout = '120';
    assert.equal(compileBlock(block), './[capture:120]\'C:/my script.cmd\' \'--flag "two words"\'');
    block.program.timeout = '301'; assert.throws(() => compileBlock(block), /1 to 300/);
    block.program.timeout = '10'; block.program.mode = 'launch';
    assert.equal(compileBlock(block), './[launch]\'C:/my script.cmd\' \'--flag "two words"\'');
    block.program.mode = 'legacy'; assert.equal(compileBlock(block), './\'C:/my script.cmd\' \'--flag "two words"\'');
    block.program.target = ''; assert.throws(() => compileBlock(block));
    assert.equal(parseProgram('./[unknown]notepad.exe'), null);
    assert.equal(parseProgram("./'a' 'b' 'c'"), null);
});
