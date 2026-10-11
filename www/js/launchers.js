// (c) J@mBeL.net 2010-2026. Part of jaNET Framework, GNU GPL version 3 or later.
export function launcherAction(action, mode = 'capture', seconds = 30) {
    const text = action.trim();
    if (!text.startsWith('./')) {
        if (mode === 'launch') throw new Error('Launch application requires a ./program action.');
        return text;
    }
    const program = text.replace(/^\.\/\[(?:launch|capture:\d+)\]/, './');
    if (mode === 'launch') return './[launch]' + program.slice(2);
    if (mode !== 'capture' || !Number.isInteger(Number(seconds)) || Number(seconds) < 1 || Number(seconds) > 300)
        throw new Error('Capture timeout must be between 1 and 300 seconds.');
    return Number(seconds) === 30 ? program : './[capture:' + Number(seconds) + ']' + program.slice(2);
}

export function wireLauncherEditor() {
    const action = document.getElementById('launcherAction');
    const mode = document.getElementById('launcherMode');
    const timeout = document.getElementById('launcherTimeout');
    const fields = document.getElementById('launcherExecution');
    function update() {
        const program = action.value.trim().startsWith('./');
        fields.hidden = !program;
        mode.disabled = !program;
        document.getElementById('launcherTimeoutField').hidden = mode.value === 'launch';
        timeout.disabled = !program || mode.value === 'launch';
    }
    action.addEventListener('input', () => {
        const preset = action.value.trim().match(/^\.\/\[(launch|capture:(\d+))\]/);
        if (preset) {
            mode.value = preset[1] === 'launch' ? 'launch' : 'capture';
            if (preset[2]) timeout.value = preset[2];
        }
        update();
    });
    mode.addEventListener('change', update);
    action.form.addEventListener('reset', () => queueMicrotask(update));
    update();
}
