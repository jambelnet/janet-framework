// (c) J@mBeL.net 2010-2026, John Ambeliotis. Part of jaNET Framework, licensed under the GNU GPL version 3 or later (see LICENSE).
// Client for the jaNET judo API (GET ?cmd=...). The command strings are sent exactly as the
// previous UI sent them; the server understands its own, limited set of percent-escapes.

const listeners = new Set();
let online = true;

function setOnline(value) {
    if (value === online) return;
    online = value;
    listeners.forEach(fn => fn(online));
}

/** Subscribe to connection state changes; the callback gets true (reachable) or false. */
export function onConnection(fn) {
    listeners.add(fn);
    return () => listeners.delete(fn);
}

async function request(cmd, mode) {
    try {
        const res = await fetch('?cmd=' + cmd + '&mode=' + mode, { cache: 'no-store' });
        if (!res.ok) throw new Error('HTTP ' + res.status);
        setOnline(true);
        return res;
    } catch (e) {
        setOnline(false);
        throw e;
    }
}

/** Runs a command and returns its output as plain text. */
export async function runText(cmd) {
    return (await request(cmd, 'text')).text();
}

/** Runs a command and returns {name: {Key, Value}} as produced by mode=json. */
export async function runJson(cmd) {
    return (await request(cmd, 'json')).json();
}

/**
 * The instruction sets for the dashboard and the editors: [{id, categ, header, shortdescr, descr, img, ref}], missing values are null.
 * (AppConfig.xml itself is not served; this endpoint tells what the UI needs and not more.)
 */
export async function loadInstructionSets() {
    const res = await fetch('../api/instructions', { cache: 'no-store' });
    if (!res.ok) throw new Error('HTTP ' + res.status);
    return res.json();
}
