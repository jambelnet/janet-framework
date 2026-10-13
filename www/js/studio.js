// (c) J@mBeL.net 2010-2026. GNU GPL version 3 or later.
import { parseBlock, newBlock, compileBlock, parseEventAction, eventActions, compileEventBlocks, groupInstructions, editableId, buildGraph, actionDependencies, safeId, mapLabels } from './studio-model.js';
import { studioRequest as request } from './studio-api.js';
import { createFlow } from './studio-flow.js';
const $ = id => document.getElementById(id);
const labels = { text: 'Text', template: 'Text template', function: 'Function', instruction: 'Instruction call', pointer: 'Launcher reference', event: 'Event call', delay: 'Delay', condition: 'Condition', command: 'Judo command', program: 'Program', raw: 'Raw source' };
const icons = { text: 'type', template: 'type', function: 'function-square', instruction: 'workflow', pointer: 'workflow', event: 'zap', delay: 'timer', condition: 'git-branch', command: 'terminal', program: 'app-window', raw: 'code' };
let snapshot, groups = new Map(), draft = null, saved = '', selected = -1, view = 'builder', history = [], future = [], graphView, graphLoad, busy = false, map, mapMode = '2d', mapGeneration = 0;
let libraryKind = 'instruction';
const flowView = createFlow($('mapFlow'), inspectNode);
function iconize(root = document) {
    root.querySelectorAll('[data-icon]').forEach(element => {
        if (!element.querySelector('[data-lucide]')) {
            const icon = document.createElement('i'); icon.dataset.lucide = element.dataset.icon; element.append(icon);
        }
    });
    window.lucide?.createIcons();
}
function element(tag, text, className) { const node = document.createElement(tag); if (text != null) node.textContent = text; if (className) node.className = className; return node; }
function button(icon, title, handler) { const node = element('button', null, 'icon-btn'); node.type = 'button'; node.dataset.icon = icon; node.title = title; node.setAttribute('aria-label', title); if (handler) node.onclick = handler; return node; }
function status(message, error = false) { $('status').textContent = message; $('status').dataset.error = String(error); }
const signature = () => JSON.stringify(draft);
const dirty = () => draft && signature() !== saved;
function discard() { return !dirty() || window.confirm('Discard unsaved routine changes?'); }
function remember() { history.push(signature()); if (history.length > 60) history.shift(); future = []; }
function change(update) { if (busy || !draft) return; remember(); update(); render(); }
function actions() { return draft.kind === 'event' ? compileEventBlocks(draft.blocks) : draft.blocks.map(compileBlock); }
function canRun() { return draft && !draft.create && !dirty() && safeId.test(draft.callable); }
function controls() {
    $('undo').disabled = busy || !history.length; $('redo').disabled = busy || !future.length;
    $('save').disabled = busy || !dirty(); $('run').disabled = busy || !canRun(); $('addRaw').disabled = busy || !draft;
    $('new').disabled = busy || !snapshot; $('refresh').disabled = busy; $('instructions').disabled = busy;
    $('blocks').querySelectorAll('.action-dependencies button').forEach(button => button.disabled = busy);
    document.querySelectorAll('.palette-item button, .library-kinds button').forEach(button => button.disabled = busy || button.dataset.intent === 'add' && (!draft || draft.kind === 'event' && ['text', 'template', 'condition', 'pointer'].includes(button.dataset.kind)));
    $('dirtyState').textContent = dirty() ? 'Unsaved changes' : draft ? 'Saved' : '';
}
function setSnapshot(value) { snapshot = value; groups = groupInstructions(value); renderChoices(); renderPalette(); }
async function load(id, kind = draft?.kind ?? libraryKind) {
    if (busy || !discard()) return;
    busy = true; controls(); $('loadError').hidden = true; status('Loading configuration...');
    try { setSnapshot(await request()); if (id) openDocument(id, kind); else { draft = null; saved = ''; selected = -1; history = []; future = []; } status('Configuration loaded.'); }
    catch (error) { status('Configuration could not load.', true); $('loadErrorMessage').textContent = error.message; $('loadError').hidden = false; }
    finally { busy = false; render(); }
}
function openInstruction(id) {
    if (!id) return;
    const target = editableId(id, groups), entries = groups.get(target);
    if (!entries) return;
    const callable = target.startsWith('*') && groups.has(target.slice(1)) ? target.slice(1) : target;
    draft = { kind: 'instruction', id: target, callable, create: false, blocks: entries.map(entry => parseBlock(entry.action)) };
    selected = draft.blocks.length ? 0 : -1; history = []; future = []; saved = signature();
    libraryKind = 'instruction'; renderChoices(); $('instructions').value = id; $('runOutput').hidden = true; render();
}
function openDocument(id, kind = 'instruction') {
    if (kind !== 'event') { openInstruction(id); return; }
    const definition = snapshot.events.find(event => event.id === id);
    if (!definition) { status('Event not found: ' + id, true); return; }
    draft = { kind: 'event', id, callable: id, create: false, blocks: eventActions(definition.action).map(parseEventAction) };
    selected = draft.blocks.length ? 0 : -1; history = []; future = []; saved = signature();
    libraryKind = 'event'; renderChoices(); $('instructions').value = id; $('runOutput').hidden = true; render();
}
function renderChoices() {
    const current = $('instructions').value, query = $('instructionSearch').value.toLowerCase(); $('instructions').replaceChildren();
    $('libraryInstructions').setAttribute('aria-pressed', String(libraryKind === 'instruction')); $('libraryEvents').setAttribute('aria-pressed', String(libraryKind === 'event'));
    const choices = libraryKind === 'instruction' ? groups : new Map(snapshot.events.map(event => [event.id, [{ header: '' }]]));
    for (const [id, entries] of [...choices].sort(([a], [b]) => a.localeCompare(b))) {
        if (!(id + ' ' + (entries[0].header ?? '')).toLowerCase().includes(query)) continue;
        const option = element('option', entries[0].header ? `${id} - ${entries[0].header}` : id); option.value = id; option.title = option.textContent; $('instructions').append(option);
    }
    $('instructions').value = current;
}
function renderPalette(palette = $('palette'), query = $('blockSearch').value.toLowerCase(), chooser = false) {
    palette.replaceChildren();
    const add = (kind, value, title) => {
        if (!title.toLowerCase().includes(query)) return;
        const item = element('div', null, 'palette-item'); item.dataset.kind = kind; item.dataset.value = value;
        const name = element('button', null, 'palette-name'); name.type = 'button'; name.dataset.kind = kind;
        const icon = element('span'); icon.dataset.icon = icons[kind]; name.append(icon, element('span', title));
        const reference = ['instruction', 'pointer', 'event'].includes(kind);
        name.dataset.intent = reference && !chooser ? 'open' : 'add';
        name.title = (reference && !chooser ? 'Open ' : 'Add ') + title;
        name.onclick = () => reference && !chooser ? editDependency(value, kind === 'event' ? 'event' : 'instruction') : addBlock(kind, value);
        const plus = button('plus', 'Add ' + title, () => addBlock(kind, value)); plus.dataset.kind = kind;
        plus.dataset.intent = 'add';
        name.disabled = busy || name.dataset.intent === 'add' && !draft; plus.disabled = busy || !draft;
        if (draft?.kind === 'event' && ['text', 'template', 'condition', 'pointer'].includes(kind)) {
            plus.disabled = true; if (name.dataset.intent === 'add') name.disabled = true;
            plus.title = 'Put this action in an instruction, then add an instruction call to the event.';
            if (name.dataset.intent === 'add') name.title = plus.title;
        }
        item.append(name, plus); palette.append(item);
    };
    palette.append(element('div', 'Actions', 'palette-group'));
    for (const kind of ['text', 'template', 'delay', 'condition', 'command', 'program', 'raw']) add(kind, '', labels[kind]);
    palette.append(element('div', 'Functions', 'palette-group'));
    for (const name of snapshot?.functions ?? []) add('function', name, name);
    palette.append(element('div', 'Instructions', 'palette-group'));
    for (const id of groups.keys()) if (safeId.test(id)) add('instruction', id, id);
    palette.append(element('div', 'Launchers', 'palette-group'));
    for (const id of groups.keys()) if (/^\*[A-Za-z0-9_-]+$/.test(id)) add('pointer', id, id);
    palette.append(element('div', 'Events', 'palette-group'));
    for (const id of new Set((snapshot?.events ?? []).map(event => event.id))) if (safeId.test(id)) add('event', id, id);
    iconize(palette);
}
function addBlock(kind, value = '', index = draft?.blocks.length) {
    if (!draft || busy) { status('Select a routine first.', true); return; }
    if (draft.kind === 'event' && ['text', 'template', 'condition', 'pointer'].includes(kind)) { status('Put this action in an instruction, then add an instruction call to the event.', true); return; }
    change(() => { const block = newBlock(kind, value); if (kind === 'instruction' && draft.kind === 'event') block.invocation = 'bare'; draft.blocks.splice(index, 0, block); selected = index; view = 'builder'; });
    $('actionDialog').close();
}
function editDependency(id, kind = 'instruction') {
    if (!busy && discard()) { view = 'builder'; openDocument(id, kind); }
}
function renderDependencies(row, source, position, previewGroups) {
    const dependencies = actionDependencies(snapshot, source, position, previewGroups).filter(dependency => dependency.missing || dependency.kind !== 'function' || dependency.id !== source);
    if (!dependencies.length) return;
    const section = element('div', null, 'action-dependencies');
    for (const dependency of dependencies) {
        const title = `${dependency.branch ? dependency.branch + ': ' : ''}${mapLabels[dependency.kind]} ${dependency.id}`;
        if (dependency.kind === 'function' || dependency.missing) {
            section.append(element('p', title + (dependency.missing ? ' (not found)' : ''), dependency.missing ? 'dependency-missing' : 'dependency-function'));
            continue;
        }
        const details = element('details', null, 'dependency-preview');
        details.append(element('summary', `${title} (${dependency.actions.length} ${dependency.actions.length === 1 ? 'action' : 'actions'})`));
        const list = element('ol', null, 'dependency-actions');
        for (const action of dependency.actions) {
            const item = element('li'), content = element('code', action);
            item.append(content);
            const target = dependency.kind === 'event' && safeId.test(action) && groups.has(action) ? action : null;
            if (target) { const edit = button('pencil', 'Edit ' + target, () => editDependency(target)); edit.disabled = busy; item.append(edit); }
            list.append(item);
        }
        if (!dependency.actions.length) list.append(element('li', 'No actions.'));
        details.append(list);
        if (['instruction', 'event'].includes(dependency.kind)) {
            const edit = button('pencil', 'Edit ' + dependency.id, () => editDependency(dependency.id, dependency.kind));
            edit.disabled = busy;
            edit.classList.add('dependency-edit'); details.querySelector('summary').append(edit);
            edit.onclick = event => { event.preventDefault(); editDependency(dependency.id, dependency.kind); };
        }
        section.append(details);
    }
    row.append(section);
}
function render() {
    controls();
    $('documentName').textContent = draft ? `${draft.kind === 'event' ? 'Event' : 'Instruction'}: ${draft.callable}` : 'No routine selected';
    $('documentMeta').textContent = draft ? `${draft.blocks.length} ${draft.blocks.length === 1 ? 'action' : 'actions'}${draft.id !== draft.callable ? ' / source: ' + draft.id : ''}${draft.create ? ' / new ' + draft.kind : ''}` : '';
    $('blocks').replaceChildren(); $('sources').replaceChildren(); $('builderEmpty').hidden = Boolean(draft);
    const previewGroups = draft && draft.kind !== 'event' ? groupInstructions(snapshot, draft.callable, draft.blocks.map(block => {
        try { return compileBlock(block); } catch { return block.value || '(incomplete action)'; }
    }), draft.id) : groups;
    renderPalette();
    for (const [index, block] of (draft?.blocks ?? []).entries()) {
        let source; try { source = compileBlock(block); } catch { source = block.value || block.expression || '(incomplete action)'; }
        const row = element('li', null, 'block' + (selected === index ? ' is-selected' : '')); row.dataset.index = String(index);
        const handle = button('grip-vertical', 'Drag action ' + (index + 1)); handle.classList.add('drag-handle');
        const program = block.kind === 'program' && block.program;
        const title = program ? program.mode === 'launch' ? 'Launch application' : program.mode === 'capture' ? 'Capture output' : 'Capture output (legacy)' : labels[block.kind];
        const summary = program ? program.target + (program.arguments ? ' ' + program.arguments : '') : block.kind === 'delay' ? block.value + ' ms' : source;
        const copy = element('button', null, 'block-copy'); copy.type = 'button'; copy.append(element('strong', title), element('code', summary)); copy.onclick = () => { selected = index; render(); };
        row.append(handle, element('span', String(index + 1).padStart(2, '0'), 'block-number'), copy, button(icons[block.kind], 'Edit action ' + (index + 1), () => { selected = index; render(); })); $('blocks').append(row);
        renderDependencies(row, block.kind === 'instruction' && ['bare', 'star'].includes(block.invocation) ? 'judo inset run ' + block.value : source, index, previewGroups);
        const sourceRow = element('div', null, 'source-row'), label = element('label', 'Action ' + (index + 1)), input = element('textarea'); input.value = source; input.spellcheck = false; input.disabled = busy;
        input.setAttribute('aria-label', 'Source for action ' + (index + 1));
        input.onchange = () => change(() => { draft.blocks[index] = draft.kind === 'event' ? parseEventAction(input.value) : parseBlock(input.value); });
        label.append(input); sourceRow.append(label); $('sources').append(sourceRow);
    }
    showView(); renderInspector(); iconize(); controls();
}
function field(labelText, value, update, { list, type = 'text', multiline = false, min, max } = {}) {
    const label = element('label', labelText), input = element(list ? 'select' : multiline ? 'textarea' : 'input');
    if (list) {
        const choices = list.map(item => typeof item === 'string' ? { value: item, label: item } : item);
        if (value && !choices.some(item => item.value === value)) choices.unshift({ value, label: value });
        for (const item of new Map(choices.map(item => [item.value, item])).values()) { const option = element('option', item.label); option.value = item.value; input.append(option); }
    }
    else if (!multiline) input.type = type;
    input.value = value ?? ''; input.disabled = busy; input.onchange = () => change(() => update(input.value));
    input.setAttribute('aria-label', labelText);
    if (min != null) input.min = String(min); if (max != null) input.max = String(max);
    label.append(input); return label;
}
function renderInspector() {
    const panel = $('inspector'); panel.replaceChildren();
    const block = draft?.blocks[selected];
    if (!block) { panel.append(element('p', 'No action selected.', 'empty')); return; }
    panel.append(element('strong', `Action ${selected + 1} / ${labels[block.kind]}`));
    if (block.kind === 'program' && block.program) {
        const program = block.program;
        panel.append(field('Mode', program.mode, value => program.mode = value, { list: [
            { value: 'launch', label: 'Launch application' }, { value: 'capture', label: 'Capture output' }, { value: 'legacy', label: 'Capture output (legacy, 30 s)' }
        ] }));
        panel.append(field('Program or script', program.target, value => program.target = value));
        panel.append(field('Arguments', program.arguments, value => program.arguments = value, { multiline: true }));
        if (program.mode === 'capture') panel.append(field('Timeout (seconds)', program.timeout, value => program.timeout = value, { type: 'number', min: 1, max: 300 }));
    } else if (block.kind === 'condition') {
        panel.append(field('Expression', block.expression, value => block.expression = value));
        panel.append(field('When true', block.yes, value => block.yes = value));
        panel.append(field('When false', block.no, value => block.no = value));
    } else {
        let list;
        if (block.kind === 'function') list = snapshot.functions;
        if (block.kind === 'instruction') list = [...groups.keys()].filter(id => safeId.test(id));
        if (block.kind === 'pointer') list = [...groups.keys()].filter(id => /^\*[A-Za-z0-9_-]+$/.test(id));
        if (block.kind === 'event') list = snapshot.events.map(event => event.id).filter(id => safeId.test(id));
        panel.append(field(block.kind === 'delay' ? 'Milliseconds' : block.kind === 'program' ? 'Program expression' : list ? 'Target' : 'Value', block.value, value => block.value = value, { list, type: block.kind === 'delay' ? 'number' : 'text', multiline: ['text', 'template', 'raw', 'command', 'program'].includes(block.kind) }));
    }
    const source = element('pre'); try { source.textContent = compileBlock(block); } catch (error) { source.textContent = error.message; }
    panel.append(source);
    if (block.kind === 'event' && snapshot.events.some(event => event.id === block.value)) {
        const edit = button('pencil', 'Edit event ' + block.value, () => editDependency(block.value, 'event')); edit.disabled = busy; panel.append(edit);
    }
    if (block.kind === 'instruction' && groups.has(block.value)) {
        const edit = button('pencil', 'Edit instruction ' + block.value, () => editDependency(block.value)); edit.disabled = busy; panel.append(edit);
    }
    const buttons = element('div', null, 'property-actions');
    const move = offset => change(() => { const next = selected + offset; [draft.blocks[selected], draft.blocks[next]] = [draft.blocks[next], draft.blocks[selected]]; selected = next; });
    const up = button('arrow-up', 'Move action up', () => move(-1)); up.disabled = busy || selected === 0;
    const down = button('arrow-down', 'Move action down', () => move(1)); down.disabled = busy || selected === draft.blocks.length - 1;
    const duplicate = button('copy', 'Duplicate action', () => change(() => { draft.blocks.splice(selected + 1, 0, structuredClone(block)); selected++; })); duplicate.disabled = busy;
    const remove = button('trash-2', 'Remove action', () => change(() => { draft.blocks.splice(selected, 1); selected = Math.min(selected, draft.blocks.length - 1); })); remove.classList.add('danger'); remove.disabled = busy;
    buttons.append(up, down, duplicate, remove); panel.append(buttons);
    if (block.kind !== 'raw') { const raw = element('button', 'Edit as raw source'); raw.type = 'button'; raw.disabled = busy; raw.onclick = () => { try { const value = compileBlock(block); change(() => draft.blocks[selected] = { kind: 'raw', value }); } catch (error) { status(error.message, true); } }; panel.append(raw); }
    else if ((draft.kind === 'event' ? parseEventAction(block.value) : parseBlock(block.value)).kind !== 'raw') {
        const typed = element('button', 'Use block editor'); typed.type = 'button'; typed.disabled = busy;
        typed.onclick = () => change(() => draft.blocks[selected] = draft.kind === 'event' ? parseEventAction(block.value) : parseBlock(block.value)); panel.append(typed);
    }
}
function showView() {
    for (const name of ['builder', 'map', 'source']) {
        $(name + 'View').hidden = view !== name;
        const tab = document.querySelector(`[data-view="${name}"]`); tab.setAttribute('aria-selected', String(view === name)); tab.tabIndex = view === name ? 0 : -1;
    }
    if (view !== 'map') stopPreview();
    flowView.setVisible(view === 'map' && mapMode === '2d'); graphView?.setVisible(view === 'map' && mapMode === '3d');
    if (view === 'map') updateMap();
}
async function updateMap() {
    if (!snapshot || !draft) return;
    let compiled; try { compiled = actions(); } catch (error) { status(error.message, true); return; }
    const generation = ++mapGeneration; stopPreview();
    map = buildGraph(snapshot, draft.callable, compiled, 180, draft.id, draft.kind);
    $('mapWarnings').replaceChildren(...map.warnings.map(warning => element('li', warning)));
    $('mapList').replaceChildren(...map.nodes.map(node => { const item = element('button', (mapLabels[node.kind] ?? node.kind) + ': ' + node.label); item.onclick = () => inspectNode(node); return item; }));
    flowView.update(map); setMapModeVisibility();
    if (mapMode === '2d') return;
    try {
        if (!graphLoad) graphLoad = import('./studio-graph.js').then(module => { graphView = module.createGraph($('mapCanvas'), inspectNode); return graphView; });
        await graphLoad;
        if (generation !== mapGeneration || mapMode !== '3d' || view !== 'map') return;
        graphView.setVisible(true); graphView.update(map); setMapModeVisibility();
    } catch {
        if (generation !== mapGeneration || mapMode !== '3d' || view !== 'map') return;
        mapMode = '2d'; graphLoad = null; setMapModeVisibility(); status('3D is unavailable in this browser. Showing the 2D map.');
    }
}
function currentMap() { return mapMode === '2d' ? flowView : graphView; }
function previewState(active) {
    const button = $('animateMap'), icon = active ? 'pause' : 'play';
    button.setAttribute('aria-pressed', String(active));
    button.title = active ? 'Pause dependency preview' : 'Animate dependency preview (no execution)'; button.setAttribute('aria-label', button.title);
    if (button.dataset.icon !== icon) { button.dataset.icon = icon; button.replaceChildren(); iconize(); }
}
function stopPreview() { flowView.animate(false); graphView?.animate(false); previewState(false); }
function setMapModeVisibility() {
    $('mapFlow').hidden = mapMode !== '2d'; $('mapCanvas').hidden = mapMode !== '3d';
    $('map2D').setAttribute('aria-pressed', String(mapMode === '2d')); $('map3D').setAttribute('aria-pressed', String(mapMode === '3d'));
    flowView.setVisible(view === 'map' && mapMode === '2d'); graphView?.setVisible(view === 'map' && mapMode === '3d');
    for (const id of ['fitMap', 'stepMap', 'animateMap', 'zoomInMap', 'zoomOutMap']) $(id).disabled = mapMode === '3d' && !graphView;
}
function inspectNode(node) {
    const panel = $('inspector'); panel.replaceChildren(element('strong', mapLabels[node.kind] ?? node.kind), element('pre', node.detail));
    if (node.sourceId) panel.append(element('p', `Source: ${node.sourceId}, action ${node.sourcePosition + 1}`));
    if (node.kind === 'instruction' && groups.has(node.label)) {
        const open = element('button', 'Edit ' + node.label); open.onclick = () => { if (discard()) { view = 'builder'; openInstruction(node.label); } }; panel.append(open);
    }
    if (node.kind === 'event' && snapshot.events.some(event => event.id === node.label)) {
        const open = element('button', 'Edit event ' + node.label); open.onclick = () => editDependency(node.label, 'event'); panel.append(open);
    }
}
async function save() {
    if (busy || !dirty()) return;
    let compiled; try { compiled = actions(); if (!compiled.length && draft.kind !== 'event') throw new Error('Add at least one action.'); } catch (error) { status(error.message, true); return; }
    busy = true; controls(); renderInspector(); status('Saving routine...');
    try {
        const result = await request('POST', { id: draft.id, kind: draft.kind, revision: snapshot.revision, actions: compiled, create: draft.create, header: draft.header, category: draft.category });
        const { id, kind } = draft; setSnapshot(result.snapshot); openDocument(id, kind); status(result.message);
    } catch (error) { status(error.message, true); }
    finally { busy = false; render(); }
}
async function run() {
    if (!canRun() || busy) return;
    $('runConfirm').textContent = draft.callable; $('runDialog').showModal();
}
$('runDialog').addEventListener('close', async () => {
    if ($('runDialog').returnValue !== 'run' || !canRun() || busy) return;
    busy = true; controls(); renderInspector(); $('runOutput').hidden = false; $('runOutput').textContent = 'Running ' + draft.callable + '...';
    try {
        const response = await fetch('/api/command', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ command: draft.kind === 'event' ? `%~>${draft.id}%` : 'judo inset run ' + draft.callable }), signal: AbortSignal.timeout(315000) });
        if (!response.ok) throw new Error('Command failed (' + response.status + ').');
        $('runOutput').textContent = (await response.text()) || 'Operation completed: ' + draft.callable;
    } catch (error) { $('runOutput').textContent = error.message + ' The host may still be running the operation; it has not been retried.'; }
    finally { busy = false; controls(); renderInspector(); }
});
$('newForm').onsubmit = event => {
    event.preventDefault(); const data = new FormData(event.currentTarget), id = data.get('id').trim();
    if (!safeId.test(id) || groups.has(id) || groups.has('*' + id) || snapshot.events.some(event => event.id === id)) { $('newError').hidden = false; $('newError').textContent = 'Choose an unused routine identifier.'; return; }
    const kind = data.get('kind');
    draft = { kind, id, callable: id, create: true, header: data.get('header'), category: data.get('category'), blocks: kind === 'event' ? [] : [newBlock('text')] };
    selected = draft.blocks.length ? 0 : -1; saved = ''; history = []; future = []; view = 'builder'; $('newDialog').close(); render(); status('New routine draft.');
};
function newKindFields() { const event = $('newForm').elements.kind.value === 'event'; $('newForm').elements.header.disabled = event; $('newForm').elements.category.disabled = event; }
$('newForm').elements.kind.onchange = newKindFields;
$('new').onclick = () => { if (discard()) { $('newForm').reset(); $('newForm').elements.kind.value = libraryKind; newKindFields(); $('newError').hidden = true; $('newDialog').showModal(); } };
$('closeNew').onclick = () => $('newDialog').close();
$('instructions').onchange = event => { const id = event.target.value; if (discard()) { view = 'builder'; openDocument(id, libraryKind); } else event.target.value = draft.kind === libraryKind ? draft.callable : ''; };
$('libraryInstructions').onclick = () => { libraryKind = 'instruction'; renderChoices(); };
$('libraryEvents').onclick = () => { libraryKind = 'event'; renderChoices(); };
$('instructionSearch').oninput = renderChoices; $('blockSearch').oninput = () => renderPalette();
$('refresh').onclick = () => load(draft?.id); $('save').onclick = save; $('run').onclick = run;
$('retryLoad').onclick = () => load(draft?.id);
$('addRaw').onclick = () => { if (!busy && draft) { $('actionSearch').value = ''; renderPalette($('actionChoices'), '', true); $('actionDialog').showModal(); $('actionSearch').focus(); } };
$('closeAction').onclick = () => $('actionDialog').close();
$('actionSearch').oninput = () => renderPalette($('actionChoices'), $('actionSearch').value.toLowerCase(), true);
function undo(redo = false) {
    const from = redo ? future : history, to = redo ? history : future;
    if (busy || !from.length) return;
    to.push(signature()); draft = JSON.parse(from.pop()); selected = Math.min(selected, draft.blocks.length - 1); render();
}
$('undo').onclick = () => undo(); $('redo').onclick = () => undo(true);
document.querySelectorAll('[data-view]').forEach(tab => {
    tab.onclick = () => { view = tab.dataset.view; showView(); };
    tab.onkeydown = event => { if (!['ArrowLeft', 'ArrowRight'].includes(event.key)) return; event.preventDefault(); const tabs = [...document.querySelectorAll('[data-view]')]; tabs[(tabs.indexOf(tab) + (event.key === 'ArrowRight' ? 1 : 2)) % 3].click(); document.querySelector('[aria-selected="true"]').focus(); };
});
$('map2D').onclick = () => { mapMode = '2d'; updateMap(); }; $('map3D').onclick = () => { mapMode = '3d'; updateMap(); };
$('fitMap').onclick = () => currentMap()?.fit(); $('stepMap').onclick = () => currentMap()?.step();
$('zoomInMap').onclick = () => currentMap()?.zoom(1 / 1.2); $('zoomOutMap').onclick = () => currentMap()?.zoom(1.2);
$('animateMap').onclick = () => { const active = $('animateMap').getAttribute('aria-pressed') !== 'true'; previewState(active); currentMap()?.animate(active); };
$('theme').onclick = () => { const dark = getComputedStyle(document.documentElement).colorScheme === 'dark' || (!document.documentElement.dataset.theme && matchMedia('(prefers-color-scheme: dark)').matches); document.documentElement.dataset.theme = dark ? 'light' : 'dark'; try { localStorage.setItem('jubito-theme', document.documentElement.dataset.theme); } catch {} graphView?.theme(); };
window.addEventListener('beforeunload', event => { if (dirty()) { event.preventDefault(); event.returnValue = ''; } });
document.querySelectorAll('a[href="./#page0"], a[href="./#page3"]').forEach(link => link.onclick = event => { if (!discard()) event.preventDefault(); });
if (matchMedia('(max-width: 700px)').matches) { $('libraryPanel').open = false; $('blocksPanel').open = false; }
if (window.Sortable) {
    new window.Sortable($('palette'), { group: { name: 'studio', pull: 'clone', put: false }, sort: false, draggable: '.palette-item', animation: 150, delayOnTouchOnly: true, delay: 180 });
    new window.Sortable($('blocks'), { group: { name: 'studio', pull: false, put: true }, handle: '.drag-handle', animation: 150,
        onMove: () => !busy && Boolean(draft),
        onAdd: event => { const { kind, value } = event.item.dataset; event.item.remove(); addBlock(kind, value, event.newIndex); },
        onUpdate: event => change(() => { const [block] = draft.blocks.splice(event.oldIndex, 1); draft.blocks.splice(event.newIndex, 0, block); selected = event.newIndex; }) });
}
iconize(); load();
