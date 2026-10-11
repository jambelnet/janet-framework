// (c) J@mBeL.net 2010-2026. GNU GPL version 3 or later.
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
const source = await readFile(new URL('../www/js/studio-model.js', import.meta.url), 'utf8');
const { parseBlock, newBlock, compileBlock, buildGraph, groupInstructions, editableId } = await import('data:text/javascript;base64,' + Buffer.from(source).toString('base64'));
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
