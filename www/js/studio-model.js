// (c) J@mBeL.net 2010-2026. GNU GPL version 3 or later.
export const safeId = /^[A-Za-z0-9_-]+$/;
export const kinds = ['text', 'template', 'function', 'instruction', 'pointer', 'event', 'delay', 'condition', 'command', 'program', 'raw'];
export const mapLabels = { instruction: 'Instruction', function: 'Function', event: 'Event', condition: 'Condition', program: 'Program', command: 'Judo command', delay: 'Delay', text: 'Text', template: 'Text template', raw: 'Raw source', cycle: 'Cycle', missing: 'Missing target' };
export function parseBlock(source) {
    const block = { kind: 'raw', source, value: source };
    let match;
    if ((match = /^%~>([A-Za-z0-9_-]+)%$/.exec(source))) return { ...block, kind: 'event', value: match[1] };
    if (/^%(?!~>)[^%\r\n]+%$/.test(source)) return { ...block, kind: 'function', value: source };
    if ((match = /^judo inset run ([A-Za-z0-9_-]+)$/.exec(source))) return { ...block, kind: 'instruction', value: match[1] };
    if (/^\*[A-Za-z0-9_-]+$/.test(source)) return { ...block, kind: 'pointer' };
    if ((match = /^judo sleep (\d+)$/.exec(source))) return { ...block, kind: 'delay', value: match[1] };
    if ((match = /^\{\s*evalBool\(([^;{}]*)\);\s*([^;{}]*);\s*([^;{}]*);?\s*\}$/.exec(source)) &&
        [match[2], match[3]].every(branch => branch.trim().split(/\s+/).filter(Boolean).every(id => safeId.test(id) && id !== 'judo'))) {
        const fields = { expression: match[1], yes: match[2].trim(), no: match[3].trim() };
        return { ...block, kind: 'condition', ...fields, original: fields };
    }
    if (source.startsWith('./')) return { ...block, kind: 'program', program: parseProgram(source) };
    if (source.startsWith('judo ') && !source.includes('%') && !/\*[A-Za-z0-9_-]+/.test(source)) return { ...block, kind: 'command' };
    if (/%(?!~>)[^%\r\n]+%/.test(source) && !/[\*{}]/.test(source) && !source.includes('%~>') && !source.startsWith('judo')) return { ...block, kind: 'template' };
    if (!/[\%\*{}]/.test(source) && !source.startsWith('judo')) return { ...block, kind: 'text' };
    return block;
}
export function newBlock(kind, value = '') {
    if (kind === 'program') return parseBlock(value || './[launch]notepad.exe');
    return { kind, value: value || ({ template: 'Hello %user%.', function: '%user%', delay: '1000', command: 'judo help', program: './[launch]notepad.exe' }[kind] ?? ''), expression: '', yes: '', no: '' };
}
export function compileBlock(block) {
    switch (block.kind) {
        case 'function':
            if (!/^%(?!~>)[^%\r\n]+%$/.test(block.value)) throw new Error('Select one function.');
            return block.value;
        case 'instruction': case 'event':
            if (!safeId.test(block.value)) throw new Error('Select an identifier with letters, numbers, underscores or hyphens.');
            return block.kind === 'instruction' ? block.invocation === 'bare' ? block.value : block.invocation === 'star' ? '*' + block.value : `judo inset run ${block.value}` : `%~>${block.value}%`;
        case 'program': return block.program ? compileProgram(block.program, block.value) : block.value;
        case 'delay':
            if (!/^\d+$/.test(block.value) || Number(block.value) > 300000) throw new Error('Delay must be 0 to 300000 milliseconds.');
            return `judo sleep ${block.value}`;
        case 'pointer':
            if (!/^\*[A-Za-z0-9_-]+$/.test(block.value)) throw new Error('Select a starred launcher identifier.');
            return block.value;
        case 'condition': {
            if (block.original && ['expression', 'yes', 'no'].every(key => block[key] === block.original[key])) return block.source;
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
export function groupInstructions(snapshot, id, draftActions, draftId = id) {
    const groups = new Map();
    for (const entry of snapshot.instructions) {
        if (!groups.has(entry.id)) groups.set(entry.id, []);
        groups.get(entry.id).push(entry);
    }
    if (draftActions) {
        const paired = id !== draftId && editableId(id, groups) === draftId;
        groups.set(draftId, draftActions.map(action => ({ id: draftId, action })));
        if (paired) groups.set(id, draftActions.map(() => ({ id, action: draftId })));
    }
    return groups;
}
export function editableId(id, groups) {
    const actions = groups.get(id), launchers = groups.get('*' + id);
    return actions?.length && launchers?.length === actions.length && actions.every(entry => entry.action === '*' + id) ? '*' + id : id;
}
export function eventActions(source) {
    // Match InstructionRunner.Run: locks bypass splitting, duplicates run only once.
    return source.includes('</lock>') ? [source] : [...new Set(source.replaceAll('&', ';').split(';').map(part => part.trim()).filter(Boolean))];
}
export function parseEventAction(source) {
    if (safeId.test(source)) return { kind: 'instruction', value: source, invocation: 'bare' };
    if (/^\*[A-Za-z0-9_-]+$/.test(source)) return { kind: 'instruction', value: source.slice(1), invocation: 'star' };
    return parseBlock(source);
}
export function compileEventBlocks(blocks) {
    const actions = blocks.map(compileBlock);
    if (actions.some(action => !action.trim())) throw new Error('Complete or remove the empty event step.');
    if (blocks.some((block, index) => ['text', 'template'].includes(block.kind) && !actions[index].startsWith('%'))) throw new Error('Put text or templates in an instruction, then add an instruction call to this event.');
    if (blocks.some(block => block.kind === 'condition')) throw new Error('Put a condition in an instruction, then add an instruction call to this event.');
    if (actions.length > 1 && actions.some(action => action.includes('</lock>'))) throw new Error('A locked event must remain one raw action.');
    if (!actions.some(action => action.includes('</lock>')) && actions.some(action => /[;&]/.test(action))) throw new Error('An event step cannot contain semicolons or ampersands. Use a referenced instruction for this step.');
    if (new Set(actions).size !== actions.length) throw new Error('Events execute duplicate steps only once. Put repeated steps inside a referenced instruction.');
    return actions;
}
export function parseProgram(source) {
    const match = /^\.\/(?:\[(launch|capture:(\d+))\])?([\s\S]*)$/.exec(source);
    if (!match || match[3].startsWith('[')) return null;
    let target = match[3], args = '';
    if (/['`]/.test(target)) {
        const quoted = /^(['`])([^'`]+)\1(?:\s+(['`])([^'`]+)\3)?$/.exec(target);
        if (!quoted) return null;
        target = quoted[2]; args = quoted[4] ?? '';
    }
    const fields = { mode: match[1] === 'launch' ? 'launch' : match[1] ? 'capture' : 'legacy', target, arguments: args, timeout: match[2] ?? '30' };
    return { ...fields, original: { ...fields } };
}
export function compileProgram(program, originalSource) {
    if (program.original && ['mode', 'target', 'arguments', 'timeout'].every(key => program[key] === program.original[key])) return originalSource;
    if (!['launch', 'capture', 'legacy'].includes(program.mode)) throw new Error('Select a program mode.');
    if (!program.target.trim() || /['`\r\n]/.test(program.target) || /['`\r\n]/.test(program.arguments)) throw new Error('Enter a program path and arguments without single quotes, backticks or newlines.');
    if (program.mode === 'capture' && (!/^\d+$/.test(program.timeout) || Number(program.timeout) < 1 || Number(program.timeout) > 300)) throw new Error('Capture timeout must be 1 to 300 seconds.');
    const mode = program.mode === 'legacy' ? '' : program.mode === 'launch' ? '[launch]' : `[capture:${program.timeout}]`;
    return `./${mode}'${program.target}'${program.arguments ? " '" + program.arguments + "'" : ''}`;
}
export function actionDependencies(snapshot, source, position = 0, groups = groupInstructions(snapshot)) {
    const dependencies = [], seen = new Set();
    const addInstruction = (id, pointer = false, branch = '') => {
        const key = `${id}:${pointer ? position : 'all'}:${branch}`;
        if (seen.has(key)) return;
        seen.add(key);
        const sourceId = pointer ? id : editableId(id, groups), entries = groups.get(sourceId);
        const actions = pointer ? entries?.[position] ? [entries[position].action] : null : entries?.map(entry => entry.action);
        dependencies.push({ kind: 'instruction', id, sourceId, branch, actions: actions ?? [], missing: !actions?.length });
    };
    for (const id of new Set([...source.matchAll(/%~>([A-Za-z0-9_-]+)%/g)].map(match => match[1]))) {
        const definition = snapshot.events.find(entry => entry.id === id);
        dependencies.push({ kind: 'event', id, actions: definition ? eventActions(definition.action) : [], missing: !definition });
    }
    const invoke = /^judo inset run ([A-Za-z0-9_-]+)$/.exec(source);
    if (invoke) addInstruction(invoke[1]);
    for (const match of source.matchAll(/\*[A-Za-z0-9_-]+/g)) addInstruction(match[0], true);
    for (const token of new Set([...source.matchAll(/%(?!~>)[^%\r\n]+%/g)].map(match => match[0]))) {
        dependencies.push({ kind: 'function', id: token, actions: [], missing: !snapshot.functions.includes(token) });
    }
    const condition = parseBlock(source);
    if (condition.kind === 'condition') for (const [branch, value] of [['When true', condition.yes], ['When false', condition.no]]) {
        for (const id of value.split(/\s+/).filter(Boolean)) addInstruction(id, false, branch);
    }
    return dependencies;
}
export function buildGraph(snapshot, id, draftActions, limit = 180, draftId = id, kind = 'instruction') {
    const groups = groupInstructions(snapshot, id, kind === 'event' ? null : draftActions, draftId), nodes = [], edges = [], warnings = new Set();
    const functions = new Set(snapshot.functions), events = new Map();
    for (const event of snapshot.events) if (!events.has(event.id)) events.set(event.id, event.action);
    if (kind === 'event' && draftActions) events.set(id, draftActions.join('; '));
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
        if (event) eventDefinition(event[1], depth + 1, node, stack, 'event');
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
    function eventDefinition(id, depth, parent, stack, edge) {
        const key = 'event:' + id, value = events.get(id);
        const node = add(id, value == null ? 'missing' : 'event', value ?? 'Event not found', depth, parent, edge);
        if (value == null) warnings.add('Missing event ' + id);
        else if (stack.includes(key) || depth > 20) { warnings.add('Event cycle at ' + id); if (node != null) nodes[node].kind = 'cycle'; }
        else if (node != null) {
            if (value.includes('</lock>')) { action(value, 0, depth + 1, node, [...stack, key], 'locked command', true); return; }
            for (const part of eventActions(value)) {
                if (safeId.test(part) || /^\*[A-Za-z0-9_-]+$/.test(part)) instruction(part.replace(/^\*/, ''), 0, depth + 1, node, [...stack, key], 'event action');
                else action(part, 0, depth + 1, node, [...stack, key], 'event action');
            }
        }
    }
    if (kind === 'event') eventDefinition(id, 0, null, [], '');
    else instruction(id, 0, 0, null, [], '');
    if (truncated) warnings.add(`Map limited to ${limit} nodes; inspect source for remaining dependencies.`);
    return { nodes, edges, warnings: [...warnings] };
}
