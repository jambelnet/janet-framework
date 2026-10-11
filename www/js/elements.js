// (c) J@mBeL.net 2010-2026. Part of jaNET Framework, GNU GPL version 3 or later.

export function parseEventIds(xml) {
    const doc = new DOMParser().parseFromString('<events>' + xml + '</events>', 'application/xml');
    if (doc.querySelector('parsererror') || (xml.trim() && !doc.documentElement.children.length))
        throw new Error('Could not load event handlers.');
    return [...doc.documentElement.children].map(element => {
        if (element.tagName !== 'event' || !element.hasAttribute('id')) throw new Error('Could not load event handlers.');
        return element.getAttribute('id');
    });
}

export function removalItems(instructions, eventIds) {
    const items = new Map();
    const item = id => {
        if (!items.has(id)) items.set(id, { id, header: '', category: '', instruction: false, launcher: false, event: false });
        return items.get(id);
    };
    for (const entry of instructions) {
        if (!entry.id) continue;
        const launcher = entry.id.startsWith('*');
        const value = item(launcher ? entry.id.slice(1) : entry.id);
        value.instruction = true;
        value.launcher ||= launcher;
        value.header ||= entry.header ?? '';
        value.category ||= entry.categ ?? '';
    }
    for (const id of eventIds) if (id) item(id).event = true;
    return [...items.values()].filter(value => value.id).sort((a, b) => a.id.localeCompare(b.id));
}

function kind(item) {
    return item.event ? (item.instruction ? 'Instruction + event' : 'Event handler') : item.launcher ? 'Launcher' : 'Instruction';
}

export function filterRemovalItems(items, query) {
    const text = query.trim().toLowerCase();
    return items.filter(item => [item.id, item.header, item.category, kind(item)].join(' ').toLowerCase().includes(text));
}

export function removalCommands(item, includeEvent) {
    // Lock the exact identifier so spaces, quotes and command separators remain data.
    if (!item?.id || /[\r\n]|<\/?lock>/i.test(item.id)) throw new Error('This identifier cannot be removed from the web editor.');
    const name = '<lock>' + item.id + '</lock>';
    const commands = [];
    if (item.instruction) commands.push('judo inset remove ' + name);
    if (item.event && (includeEvent || !item.instruction)) commands.push('judo event remove ' + name);
    if (!commands.length) throw new Error('Choose an element first.');
    return commands;
}

export function wireRemovalEditor(loadItems, runCommand, onRemoved, notify) {
    const byId = id => document.getElementById(id);
    const search = byId('elementNameRemove'), options = byId('removalOptions');
    const status = byId('removalStatus'), button = byId('removeElementBtn'), include = byId('chkEvent');
    let items = [], selected = null, busy = false;

    function selection() {
        button.disabled = busy || !selected;
        byId('removeEventOption').hidden = !selected?.event;
        include.disabled = busy || !selected?.instruction;
        if (!selected) return;
        const targets = [selected.instruction ? selected.id : '', selected.launcher ? '*' + selected.id : '',
            selected.event && (include.checked || !selected.instruction) ? 'event ' + selected.id : ''].filter(Boolean);
        status.textContent = 'Remove: ' + targets.join(', ');
    }

    function render() {
        const visible = filterRemovalItems(items, search.value);
        if (!visible.includes(selected)) { selected = null; include.checked = false; }
        options.replaceChildren(...visible.map(item => {
            const label = document.createElement('label');
            const radio = document.createElement('input'); radio.type = 'radio'; radio.name = 'removalTarget';
            radio.value = item.id; radio.checked = selected === item; radio.required = true;
            const text = document.createElement('span');
            const name = document.createElement('strong'); name.textContent = item.header && item.header !== item.id ? item.header + ' (' + item.id + ')' : item.id;
            const detail = document.createElement('small'); detail.textContent = [kind(item), item.category].filter(Boolean).join(' / ');
            text.append(name, detail); label.append(radio, text);
            radio.addEventListener('change', () => { selected = item; include.checked = item.event && !item.instruction; selection(); });
            return label;
        }));
        options.hidden = !visible.length;
        status.textContent = visible.length ? 'Choose an element.' : items.length ? 'No matching elements.' : 'No elements to remove.';
        selection();
    }

    async function load() {
        if (busy) return;
        busy = true; items = []; selected = null; search.value = ''; include.checked = false;
        options.replaceChildren(); options.hidden = true; search.disabled = true; selection();
        status.textContent = 'Loading elements...';
        try { items = await loadItems(); render(); }
        catch (error) { status.textContent = error.message || 'Could not load elements.'; }
        finally { busy = false; search.disabled = false; selection(); }
    }

    async function remove() {
        if (busy || !selected) return false;
        const item = selected;
        busy = true; search.disabled = true; options.disabled = true; selection();
        try {
            const commands = removalCommands(item, include.checked);
            for (const command of commands) {
                const output = await runCommand(command);
                if (output.trim() !== 'Element removed.') throw new Error(output || 'Removal failed.');
            }
            await onRemoved(); notify('Removed: ' + item.id + '.');
            return true;
        } catch (error) {
            status.textContent = error.message || 'Removal failed.'; notify(status.textContent);
            return false;
        } finally { busy = false; search.disabled = false; options.disabled = false; button.disabled = !selected; include.disabled = !selected?.instruction; }
    }

    search.addEventListener('input', render);
    include.addEventListener('change', selection);
    return { load, remove };
}
