// (c) J@mBeL.net 2010-2026. GNU GPL version 3 or later.
export const safeId = /^[A-Za-z0-9_-]+$/;
export const kinds = ['text', 'function', 'instruction', 'event', 'delay', 'condition', 'command', 'program', 'raw'];
export const mapLabels = { instruction: 'Instruction', function: 'Function', event: 'Event', condition: 'Condition', program: 'Program', command: 'Judo command', delay: 'Delay', text: 'Text', template: 'Text template', raw: 'Raw source', cycle: 'Cycle', missing: 'Missing target' };
export function parseBlock(source) {
    const block = { kind: 'raw', source, value: source };
    let match;
    if ((match = /^%~>([A-Za-z0-9_-]+)%$/.exec(source))) return { ...block, kind: 'event', value: match[1] };
    if (/^%(?!~>)[^%\r\n]+%$/.test(source)) return { ...block, kind: 'function', value: source };
    if ((match = /^judo inset run ([A-Za-z0-9_-]+)$/.exec(source))) return { ...block, kind: 'instruction', value: match[1] };
    if ((match = /^judo sleep (\d+)$/.exec(source))) return { ...block, kind: 'delay', value: match[1] };
    if (source.startsWith('./')) return { ...block, kind: 'program' };
    if (source.startsWith('judo ') && !source.includes('%') && !/\*[A-Za-z0-9_-]+/.test(source)) return { ...block, kind: 'command' };
    if (!/[\%\*{}]/.test(source) && !source.startsWith('judo')) return { ...block, kind: 'text' };
    return block;
}
export function newBlock(kind, value = '') {
    return { kind, value: value || ({ function: '%user%', delay: '1000', command: 'judo help', program: './[launch]notepad.exe' }[kind] ?? ''), expression: '', yes: '', no: '' };
}
export function compileBlock(block) {
    switch (block.kind) {
        case 'function':
            if (!/^%(?!~>)[^%\r\n]+%$/.test(block.value)) throw new Error('Select one function.');
            return block.value;
        case 'instruction': case 'event':
            if (!safeId.test(block.value)) throw new Error('Select an identifier with letters, numbers, underscores or hyphens.');
            return block.kind === 'instruction' ? `judo inset run ${block.value}` : `%~>${block.value}%`;
        case 'delay':
            if (!/^\d+$/.test(block.value) || Number(block.value) > 300000) throw new Error('Delay must be 0 to 300000 milliseconds.');
            return `judo sleep ${block.value}`;
        case 'condition': {
            if (!block.expression.trim() || /[;{}\r\n]/.test(block.expression)) throw new Error('Enter a single condition expression without braces or semicolons.');
            const branch = value => value.trim().split(/\s+/).filter(Boolean);
            for (const value of [...branch(block.yes), ...branch(block.no)]) {
                if (!safeId.test(value) || value === 'judo') throw new Error('Condition branches accept instruction identifiers, separated by spaces. Use a referenced instruction for commands or functions.');
            }
            return `{ evalBool(${block.expression}); ${block.yes.trim()}; ${block.no.trim()}; }`;
        }
        default: return block.value;
    }
}
export function groupInstructions(snapshot) {
    const groups = new Map();
    for (const entry of snapshot.instructions) {
        if (!groups.has(entry.id)) groups.set(entry.id, []);
        groups.get(entry.id).push(entry);
    }
    return groups;
}
export function editableId(id, groups) {
    const actions = groups.get(id), launchers = groups.get('*' + id);
    return actions?.length && launchers?.length === actions.length && actions.every(entry => entry.action === '*' + id) ? '*' + id : id;
}
export function buildGraph(snapshot, id, draftActions, limit = 180, draftId = id) {
    const groups = groupInstructions(snapshot), nodes = [], edges = [], warnings = new Set();
    const functions = new Set(snapshot.functions), events = new Map();
    for (const event of snapshot.events) if (!events.has(event.id)) events.set(event.id, event.action);
    if (draftActions) {
        const paired = id !== draftId && editableId(id, groups) === draftId;
        groups.set(draftId, draftActions.map(action => ({ id: draftId, action })));
        if (paired) groups.set(id, draftActions.map(() => ({ id, action: draftId })));
    }
    let truncated = false;
    function add(label, kind, detail, depth, parent, edge = '') {
        if (nodes.length >= limit) { truncated = true; return null; }
        const node = { id: nodes.length, label, kind, detail, depth };
        nodes.push(node);
        if (parent != null) edges.push({ from: parent, to: node.id, label: edge });
        return node.id;
    }
    function instruction(target, position, depth, parent, stack, edge, pointer = false) {
        const entries = groups.get(target);
        const key = `${target}:${pointer ? position : 'all'}`;
        const node = add(target, 'instruction', `Instruction ${target}${pointer ? ', action ' + (position + 1) : ''}`, depth, parent, edge);
        if (node == null) return;
        if (!entries?.length || (pointer && !entries[position])) { warnings.add(`Missing ${target}${pointer ? ' action ' + (position + 1) : ''}`); nodes[node].kind = 'missing'; return; }
        if (stack.includes(key) || depth > 20) { warnings.add(`Cycle or depth limit at ${target}`); nodes[node].kind = 'cycle'; return; }
        const next = [...stack, key];
        const alias = !pointer && editableId(target, groups) !== target ? '*' + target : null;
        if (alias) {
            nodes[node].detail += `\nSource: ${alias}`;
            entries.forEach((entry, index) => {
                const aliasKey = `${alias}:${index}`;
                if (next.includes(aliasKey) || depth > 18) { instruction(alias, index, depth + 1, node, next, `Action ${index + 1}`, true); return; }
                const first = nodes.length;
                action(groups.get(alias)[index].action, 0, depth + 1, node, [...next, aliasKey], `Action ${index + 1} via ${alias}`);
                if (nodes[first]) { nodes[first].sourceId = alias; nodes[first].sourcePosition = index; }
            });
            return;
        }
        (pointer ? [entries[position]] : entries).forEach((entry, index) => action(entry.action, pointer ? 0 : index, depth + 1, node, next, `Action ${pointer ? position + 1 : index + 1}`));
    }
    function action(source, position, depth, parent, stack, edge, lockedByRunner = false) {
        const block = parseBlock(source);
        if (!lockedByRunner && /^\*[A-Za-z0-9_-]+$/.test(source)) { instruction(source, position, depth, parent, stack, edge, true); return; }
        const kind = source.includes('evalBool') ? 'condition' : block.kind === 'raw' && /%(?!~>)[^%\r\n]+%/.test(source) && !/[\*{}]/.test(source) && !source.includes('%~>') && !source.startsWith('judo') ? 'template' : block.kind;
        const node = add(source.length > 42 ? source.slice(0, 39) + '...' : source || '(empty action)', kind, source, depth, parent, edge);
        if (node == null) return;
        // Locks bypass expansion only at Run entry, not inside a stored resolver action.
        if (lockedByRunner) return;
        const tokens = [...source.matchAll(/%(?!~>)[^%\r\n]+%/g)].map(m => m[0]);
        for (const token of [...new Set(tokens)]) {
            const known = functions.has(token);
            add(token, known ? 'function' : 'missing', known ? 'Value or side effect resolved at runtime: ' + token : 'Unknown function: ' + token, depth + 1, node, 'expand');
            if (!known) warnings.add('Unknown function ' + token);
        }
        for (const target of [...source.matchAll(/\*[A-Za-z0-9_-]+/g)].map(m => m[0])) instruction(target, position, depth + 1, node, stack, 'pointer', true);
        const invoke = /^judo inset run ([A-Za-z0-9_-]+)$/.exec(source);
        if (invoke) instruction(invoke[1], 0, depth + 1, node, stack, 'call');
        const event = /^%~>([A-Za-z0-9_-]+)%$/.exec(source);
        if (event) {
            const key = 'event:' + event[1], value = events.get(event[1]);
            const en = add(event[1], value == null ? 'missing' : 'event', value ?? 'Event not found', depth + 1, node, 'event');
            if (value == null) warnings.add('Missing event ' + event[1]);
            else if (stack.includes(key)) warnings.add('Event cycle at ' + event[1]);
            else if (en != null) {
                if (value.includes('</lock>')) { action(value, 0, depth + 2, en, [...stack, key], 'locked command', true); return; }
                for (const part of [...new Set(value.replaceAll('&', ';').split(';').map(s => s.trim()).filter(Boolean))]) {
                    if (safeId.test(part)) instruction(part, 0, depth + 2, en, [...stack, key], 'event action');
                    else action(part, 0, depth + 2, en, [...stack, key], 'event action');
                }
            }
        }
        if (source.includes('%~>') && !event) warnings.add('Composite event expression is opaque: inspect its source.');
        if (kind === 'condition') {
            warnings.add('Conditions are evaluated at runtime. Function and pointer expansion can occur before branch selection.');
            const simple = /^\{\s*evalBool\(([^;{}]*)\);\s*([^;{}]*);\s*([^;{}]*);?\s*\}$/.exec(source);
            if (simple) [simple[2], simple[3]].forEach((branch, index) => {
                for (const target of branch.trim().split(/\s+/).filter(Boolean)) {
                    if (safeId.test(target)) instruction(target, 0, depth + 1, node, stack, index ? 'false' : 'true');
                }
            });
        }
        if (source.includes(';') && kind !== 'condition') warnings.add('Semicolons inside an XML action are not a general sequence; inspect the source.');
    }
    instruction(id, 0, 0, null, [], '');
    if (truncated) warnings.add(`Map limited to ${limit} nodes; inspect source for remaining dependencies.`);
    return { nodes, edges, warnings: [...warnings] };
}
