// (c) J@mBeL.net 2010-2026. GNU GPL version 3 or later.
const unavailable = 'Instruction Studio API is unavailable. Stop jaNET, rebuild the project you are running, then start it again and reload Studio. Open Studio through the jaNET server, not a separate static web server.';
const invalid = 'Studio received invalid configuration data. Reload Studio; if this continues, check the jaNET server log.';
function validateSnapshot(value) {
    if (!value || typeof value.revision !== 'string' || !value.revision ||
        !Array.isArray(value.instructions) || !value.instructions.every(entry => entry && typeof entry.id === 'string' && typeof entry.action === 'string') ||
        !Array.isArray(value.events) || !value.events.every(entry => entry && typeof entry.id === 'string' && typeof entry.action === 'string') ||
        !Array.isArray(value.functions) || !value.functions.every(name => typeof name === 'string')) throw new Error(invalid);
}
export async function studioRequest(method = 'GET', data, fetcher = fetch) {
    try {
        const response = await fetcher('/api/studio', { method, headers: { Accept: 'application/json', ...(data ? { 'Content-Type': 'application/json' } : {}) }, body: data ? JSON.stringify(data) : undefined, cache: 'no-store', signal: AbortSignal.timeout(15000) });
        if (response.status === 401) throw new Error('Sign in to jaNET, then reload Studio.');
        if (response.status === 403) throw new Error('Studio access was refused. Open it from the same address as your jaNET server.');
        if (response.status === 404) throw new Error(unavailable);
        const json = /\bapplication\/(?:[\w.+-]+\+)?json\b/i.test(response.headers.get('Content-Type') ?? '');
        if (!json) throw new Error(response.ok ? unavailable : `The jaNET server returned HTTP ${response.status} instead of Studio data. Check the server log, then retry.`);
        let result;
        try { result = await response.json(); }
        catch (error) { if (error.name === 'AbortError' || error.name === 'TimeoutError') throw error; throw new Error(invalid); }
        if (!response.ok) throw new Error(typeof result?.error === 'string' ? result.error : `Studio request failed (HTTP ${response.status}).`);
        validateSnapshot(method === 'GET' ? result : result?.snapshot);
        return result;
    } catch (error) {
        if (error.name === 'AbortError' || error.name === 'TimeoutError') throw new Error('Studio request timed out. Check that jaNET is running, then retry. Changes have not been retried automatically.');
        if (error instanceof TypeError) throw new Error('Cannot reach the jaNET server. Make sure it is running, then reload Studio.');
        throw error;
    }
}
