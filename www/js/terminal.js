// (c) J@mBeL.net 2010-2026, John Ambeliotis. Part of jaNET Framework, licensed under the GNU GPL version 3 or later (see LICENSE).
import { renderResponse, commandResponse, responseAppearance } from './help.js';

export function wireTerminal(run, onComplete) {
    const input = document.getElementById('textinput1');
    const form = document.getElementById('cmdform');
    const transcript = document.getElementById('response-p2');
    const button = form.querySelector('[type="submit"]');
    const history = [];
    let index = 0, draft = '', busy = false;
    const scroll = () => { transcript.scrollTop = transcript.scrollHeight; };
    function clear() { transcript.replaceChildren(); input.focus(); }
    async function execute(command, request = run) {
        if (busy || !command.trim()) return false;
        busy = true; button.disabled = true; input.setAttribute('aria-busy', 'true');
        if (history.at(-1) !== command) history.push(command);
        if (history.length > 100) history.shift();
        index = history.length;
        const entry = document.createElement('section'); entry.className = 'terminal-entry'; entry.dataset.state = 'busy';
        const prompt = document.createElement('div'); prompt.className = 'terminal-command';
        const marker = document.createElement('b'); marker.textContent = '>';
        const text = document.createElement('span'); text.textContent = command;
        prompt.append(marker, text);
        const answer = document.createElement('div'); answer.className = 'terminal-answer'; answer.textContent = 'Running...';
        entry.append(prompt, answer); transcript.append(entry);
        while (transcript.children.length > 100) transcript.firstElementChild.remove();
        scroll();
        try {
            const output = commandResponse(await request(command), command);
            renderResponse(answer, output, command);
            const failed = responseAppearance(output).state === 'error';
            entry.dataset.state = failed ? 'error' : 'complete';
            // Do not erase a new command typed while this request was running.
            if (!failed && input.value === command) { input.value = ''; draft = ''; }
            onComplete?.(command);
            return !failed;
        } catch {
            entry.dataset.state = 'error'; answer.textContent = 'Request failed. Check that jaNET is running, then retry.';
            return false;
        } finally {
            busy = false; button.disabled = false; input.removeAttribute('aria-busy'); scroll();
            if (!document.getElementById('page2').hidden) input.focus();
        }
    }
    form.addEventListener('submit', event => { event.preventDefault(); execute(input.value); });
    document.getElementById('clearBtn').addEventListener('click', clear);
    input.addEventListener('keydown', event => {
        if (event.ctrlKey && event.key.toLowerCase() === 'l') { event.preventDefault(); clear(); }
        else if (event.key === 'ArrowUp' && index > 0) {
            if (index === history.length) draft = input.value;
            input.value = history[--index]; event.preventDefault();
        } else if (event.key === 'ArrowDown' && index < history.length) {
            input.value = ++index === history.length ? draft : history[index]; event.preventDefault();
        }
    });
    input.addEventListener('input', () => { index = history.length; draft = input.value; });
    return { execute, clear };
}
