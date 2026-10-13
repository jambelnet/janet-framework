// (c) J@mBeL.net 2010-2026. Part of jaNET Framework, GNU GPL version 3 or later; see LICENSE.
// Settings forms load independently of command execution. Never enable Save with a partial snapshot.
export const settingsDialogs = [
    'mailheaderSettings', 'webserverSettings', 'webserverLogin', 'httpsSettings', 'socketSettings',
    'trustedSettings', 'serialSettings', 'weatherSettings', 'dyndnsSettings', 'smsSettings',
    'gmailSettings', 'smtpSettings', 'pop3Settings'
];

export function applySettings(form, snapshot) {
    const fields = [...form.querySelectorAll('input[id], select[id], textarea[id]')];
    for (const field of fields) {
        if (!Object.hasOwn(snapshot.values ?? {}, field.id)) throw new Error('Incomplete settings response.');
        const value = snapshot.values[field.id];
        if (field.type === 'checkbox' ? typeof value !== 'boolean' : !['string', 'number'].includes(typeof value))
            throw new Error('Invalid settings response.');
        if (field.type === 'password' && value !== '') throw new Error('Unsafe settings response.');
        if (field.tagName === 'SELECT' && ![...field.options].some(option => option.value === String(value)))
            throw new Error('Unknown saved option for ' + field.id + '.');
    }
    for (const field of fields) {
        const value = snapshot.values[field.id];
        if (field.type === 'checkbox') field.checked = value;
        else field.value = String(value);
        if (field.type === 'password') {
            const configured = snapshot.secrets?.[field.id] === true;
            field.dataset.originalRequired ??= String(field.required);
            field.dataset.originalPlaceholder ??= field.placeholder;
            field.required = configured ? false : field.dataset.originalRequired === 'true';
            field.placeholder = configured ? 'Saved; leave blank to keep unchanged' : field.dataset.originalPlaceholder;
        }
    }
}

export function wireSettings(document, load, save, afterLoad = () => {}) {
    const states = new Map();
    for (const id of settingsDialogs) {
        const dialog = document.getElementById(id), form = dialog.querySelector('form');
        const status = document.createElement('p'); status.setAttribute('role', 'status'); status.hidden = true;
        const retry = document.createElement('button'); retry.type = 'button'; retry.textContent = 'Retry'; retry.hidden = true;
        form.querySelector('.dialog-body').prepend(status, retry);
        const controls = [...form.querySelectorAll('input,select,textarea,button')].filter(control => control !== retry);
        const state = { dialog, form, status, retry, controls, ready: false, generation: 0, disabled: null };
        states.set(id, state);
        retry.addEventListener('click', () => open(id));
        dialog.addEventListener('close', () => {
            // The close event is queued; a quick reopen may already have started a newer load.
            if (!dialog.open) { state.generation++; state.ready = false; }
        });
    }
    function block(state) {
        state.disabled ??= state.controls.map(control => control.disabled);
        state.controls.forEach(control => { if (!('close' in control.dataset)) control.disabled = true; });
    }
    function unblock(state) {
        state.controls.forEach((control, index) => control.disabled = state.disabled[index]);
        state.disabled = null;
    }
    async function open(id) {
        const state = states.get(id); if (!state) return false;
        const generation = ++state.generation; state.ready = false; block(state);
        state.status.hidden = false; state.status.textContent = 'Loading saved settings...'; state.retry.hidden = true;
        if (!state.dialog.open) state.dialog.showModal();
        try {
            const snapshot = await load(id);
            if (generation !== state.generation || !state.dialog.open) return true;
            applySettings(state.form, snapshot); unblock(state); afterLoad(id);
            state.ready = true; state.status.hidden = true;
        } catch {
            if (generation !== state.generation || !state.dialog.open) return true;
            state.status.textContent = 'Saved settings could not be loaded. Check the server, then retry.';
            state.retry.hidden = false;
        }
        return true;
    }
    async function persist(id) {
        const state = states.get(id);
        if (!state?.ready) throw new Error('Load the saved settings before saving.');
        const values = Object.fromEntries([...state.form.querySelectorAll('input[id],select[id],textarea[id]')]
            .map(field => [field.id, String(field.type === 'checkbox' ? field.checked : field.value)]));
        state.ready = false; block(state); state.status.hidden = false; state.status.textContent = 'Saving settings...';
        const generation = state.generation;
        try {
            const result = await save(id, values);
            if (!result || typeof result.message !== 'string') throw new Error('Invalid settings response.');
            return result.message;
        } finally {
            if (generation === state.generation && state.dialog.open) {
                unblock(state); afterLoad(id); state.ready = true; state.status.hidden = true;
            }
        }
    }
    return { open, persist, has: id => states.has(id), ready: id => states.get(id)?.ready === true };
}
