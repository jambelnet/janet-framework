// (c) J@mBeL.net 2010-2026. Part of jaNET Framework, GNU GPL version 3 or later; see LICENSE.
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
const source = await readFile(new URL('../www/js/web-services.js', import.meta.url), 'utf8');
const { webServiceCommand, webServiceFields } = await import('data:text/javascript;base64,' + Buffer.from(source).toString('base64'));
const input = { name: 'room service', endpoint: 'https://example.org/api?a=1&b=two%20words', node: 'items/1/name', format: 'JSON' };

test('JSON ignores stale XML namespace and index after switching formats', () => {
    const command = webServiceCommand({ ...input, namespace: 's=urn:sensors', index: 'not an index' });
    assert.equal(command, 'judo json add room_service <lock>https://example.org/api?a=1&b=two%20words</lock> <lock>items/1/name</lock>');
    assert(!command.includes('urn:sensors')); assert(!command.includes('not an index'));
});

test('XML preserves namespace, index, predicate quotes and whitespace as separate arguments', () => {
    assert.equal(webServiceCommand({ ...input, format: 'XML', node: "//s:room[@label='Living Room']", namespace: 's=urn:sensors?key=value', index: '1' }),
        "judo xml add room_service <lock>https://example.org/api?a=1&b=two%20words</lock> <lock>s=urn:sensors?key=value</lock> <lock>//s:room[@label='Living Room']</lock> 1");
    assert(webServiceCommand({ ...input, format: 'XML', index: '0' }).endsWith(' 0'));
    assert(webServiceCommand({ ...input, format: 'XML' }).endsWith('</lock>'));
});

test('invalid service inputs fail before a save request is sent', () => {
    for (const patch of [{ name: '' }, { name: 'a;bad' }, { endpoint: 'javascript:alert(1)' }, { endpoint: 'not a URL' }, { node: '' }, { node: '</lock>' }, { format: 'CSV' }])
        assert.throws(() => webServiceCommand({ ...input, ...patch }));
    for (const index of ['-1', '1.5', 'no', '2147483648']) assert.throws(() => webServiceCommand({ ...input, format: 'XML', index }));
    for (const namespace of ['invalid', 's=', '=urn:sensors']) assert.throws(() => webServiceCommand({ ...input, format: 'XML', namespace }));
});

test('format changes and resets keep hidden XML inputs inactive without losing a draft', () => {
    const elements = { 'radio-xml': { checked: true }, wsHiddenInputs: { hidden: true }, wsNamespace: { disabled: true, value: 's=urn:sensors' }, wsIndex: { disabled: true, value: '1' } };
    const document = { getElementById: id => elements[id] };
    webServiceFields(document); assert(!elements.wsHiddenInputs.hidden); assert(!elements.wsNamespace.disabled);
    elements['radio-xml'].checked = false; webServiceFields(document);
    assert(elements.wsHiddenInputs.hidden); assert(elements.wsNamespace.disabled); assert(elements.wsIndex.disabled);
    assert.equal(elements.wsNamespace.value, 's=urn:sensors');
});

test('web service saves use the raw transport and do not close/reset on failure', async () => {
    const app = await readFile(new URL('../www/js/jubito.core.js', import.meta.url), 'utf8');
    const action = app.slice(app.indexOf('async saveWebService()'), app.indexOf('async saveInset()'));
    assert(action.includes('await runRawText(command)')); assert(!action.includes('await send('));
    assert(action.includes("if (!data.includes('Element added.')) { showResult(data); return false; }"));
    assert(app.includes("if (form.dataset.submit === 'saveWebService') webServiceFields(document);"));
});
