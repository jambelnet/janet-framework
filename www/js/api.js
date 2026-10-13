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

// Longer than the maximum single capture deadline; multi-action instructions may still be running after a request timeout.
const REQUEST_TIMEOUT_MS = 315000;

async function request(url, options, mode, timeoutMs = REQUEST_TIMEOUT_MS) {
    const controller = new AbortController();
    let timer;
    try {
        const expired = new Promise((_, reject) => {
            timer = setTimeout(() => {
                const error = new Error('Request timed out. The command may still be running on jaNET; check before retrying.');
                error.code = 'JANET_REQUEST_TIMEOUT'; reject(error); controller.abort();
            }, timeoutMs);
        });
        const response = (async () => {
            const res = await fetch(url, { ...options, signal: controller.signal, cache: 'no-store' });
            if (!res.ok) {
                const detail = mode === 'json' ? await res.json().catch(() => null) : null;
                throw new Error(detail?.error || 'HTTP ' + res.status);
            }
            return mode === 'json' ? res.json() : res.text();
        })();
        const body = await Promise.race([response, expired]);
        setOnline(true);
        return body;
    } catch (e) {
        setOnline(false);
        throw e;
    }
    finally { clearTimeout(timer); }
}

/** Sends settings without the legacy URL decoder changing passwords or file paths. */
export async function runRawText(command, { timeoutMs = REQUEST_TIMEOUT_MS } = {}) {
    return request('../api/command', {
        method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ command })
    }, 'text', timeoutMs);
}

/** Runs a command and returns its output as plain text. */
export async function runText(cmd, { timeoutMs = REQUEST_TIMEOUT_MS } = {}) {
    return request('?cmd=' + cmd + '&mode=text', {}, 'text', timeoutMs);
}

/** Runs a command and returns {name: {Key, Value}} as produced by mode=json. */
export async function runJson(cmd) {
    return request('?cmd=' + cmd + '&mode=json', {}, 'json');
}

/**
 * The instruction sets for the dashboard and the editors: [{id, categ, header, shortdescr, descr, img, ref}], missing values are null.
 * (AppConfig.xml itself is not served; this endpoint tells what the UI needs and not more.)
 */
export async function loadInstructionSets() {
    return request('../api/instructions', {}, 'json');
}

export async function loadSettings(area) {
    return request('../api/settings/' + encodeURIComponent(area), {}, 'json', 15000);
}

export async function saveSettings(area, values) {
    return request('../api/settings/' + encodeURIComponent(area), {
        method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(values)
    }, 'json', 15000);
}
