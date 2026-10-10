// (c) J@mBeL.net 2010-2026, John Ambeliotis. Part of jaNET Framework, licensed under the GNU GPL version 3 or later (see LICENSE).
// Structured presentation of the existing judo help response.

export function parseHelp(text) {
    const data = { sections: [], notes: [] };
    let section;
    let topic;
    for (const line of String(text ?? '').split(/\r?\n/)) {
        const value = line.trim();
        if (!value) continue;
        if (/^\d+\.\d+\s+/.test(value) && section) {
            topic = { title: value.replace(/^\d+\.\d+\s+/, ''), commands: [] };
            section.topics.push(topic);
        } else if (/^\d+\.\s+/.test(value)) {
            section = { title: value, topics: [] };
            data.sections.push(section);
            topic = null;
        } else if (value.startsWith('+ judo ') && topic) {
            const syntax = value.slice(2);
            const words = syntax.split(' ');
            // Group only interchangeable verbs; distinct argument signatures stay separate.
            const key = /^[a-z][a-z0-9-]*$/.test(words[2] ?? '')
                ? words.slice(0, 2).join(' ') + '\u0001' + words.slice(3).join(' ')
                : syntax;
            const existing = topic.commands.find(command => command.key === key);
            if (existing && words[2] !== existing.syntax.split(' ')[2]) {
                if (!existing.aliases.includes(words[2])) existing.aliases.push(words[2]);
            } else if (!existing) topic.commands.push({ key, syntax, aliases: [] });
        } else if (value.startsWith('(')) data.notes.push(value);
        else return null;
    }
    return data.sections.length && data.sections.every(s => s.topics.length && s.topics.every(t => t.commands.length))
        ? data : null;
}

function element(tag, className, text) {
    const node = document.createElement(tag);
    if (className) node.className = className;
    if (text !== undefined) node.textContent = text;
    return node;
}

function commandCode(syntax) {
    const code = element('code', 'help-syntax');
    const pieces = /(<lock>.*?<\/lock>|`[^`]*`|\[[^\]]*\]|\s+|\S+)/g;
    let words = 0;
    for (const match of syntax.matchAll(pieces)) {
        const value = match[0];
        let kind = '';
        if (value.startsWith('<lock>') || value.startsWith('`')) kind = 'help-literal';
        else if (value.startsWith('[')) kind = 'help-argument';
        else if (!/^\s+$/.test(value) && ++words <= 3) kind = 'help-verb';
        code.append(kind ? element('span', kind, value) : document.createTextNode(value));
    }
    return code;
}

function renderHelp(target, data) {
    const toolbar = element('div', 'help-toolbar');
    const search = element('input');
    search.type = 'search';
    search.placeholder = 'Search help';
    search.setAttribute('aria-label', 'Search help');
    const select = element('select');
    select.setAttribute('aria-label', 'Help section');
    const all = element('option', '', 'All sections');
    all.value = '';
    select.append(all);
    data.sections.forEach((section, index) => {
        const option = element('option', '', section.title);
        option.value = String(index);
        select.append(option);
    });
    const count = element('p', 'help-count');
    count.setAttribute('role', 'status');
    toolbar.append(search, select);
    target.append(toolbar, count);

    const topics = [];
    const sections = data.sections.map((section, index) => {
        const node = element('section', 'help-section');
        node.append(element('h2', '', section.title));
        for (const topic of section.topics) {
            const article = element('section', 'help-topic');
            article.append(element('h3', '', topic.title));
            for (const command of topic.commands) {
                const row = element('div', 'help-command');
                row.append(commandCode(command.syntax));
                if (command.aliases.length) row.append(element('p', 'help-aliases', 'Aliases: ' + command.aliases.join(', ')));
                article.append(row);
            }
            const text = [section.title, topic.title, ...topic.commands.flatMap(c => [c.syntax, ...c.aliases])].join(' ').toLowerCase();
            topics.push({ node: article, section: index, text });
            node.append(article);
        }
        target.append(node);
        return node;
    });
    const empty = element('p', 'help-empty', 'No matching topics.');
    target.append(empty);
    if (data.notes.length) {
        const notes = element('aside', 'help-notes');
        notes.append(element('h3', '', 'Syntax notes'));
        data.notes.forEach(note => notes.append(element('p', '', note.replace(/^\(\*+\)\s*/, ''))));
        target.append(notes);
    }
    function filter() {
        const query = search.value.trim().toLowerCase().split(/\s+/).filter(Boolean);
        let visible = 0;
        for (const topic of topics) {
            topic.node.hidden = (select.value !== '' && topic.section !== Number(select.value)) || !query.every(word => topic.text.includes(word));
            if (!topic.node.hidden) visible++;
        }
        sections.forEach((node, index) => { node.hidden = !topics.some(t => t.section === index && !t.node.hidden); });
        count.textContent = visible + ' of ' + topics.length + ' topics';
        empty.hidden = visible > 0;
    }
    search.addEventListener('input', filter);
    select.addEventListener('change', filter);
    filter();
}

export function responseAppearance(text, state) {
    const message = String(text ?? '').trim();
    const detected = /^(operation completed|elements? (added|removed)|settings saved)[.!]?$/i.test(message) ? 'success' :
        /^(error\b|exception\b|invalid\b|failed\b|no matching command found|jaNET could not)/i.test(message) ? 'error' : 'neutral';
    const states = {
        success: { title: 'Completed', icon: 'i-completed' },
        error: { title: 'Request failed', icon: 'i-close' },
        busy: { title: 'Working', icon: 'i-auto' },
        choice: { title: 'Choose a command', icon: 'i-send' },
        neutral: { title: 'Response', icon: 'i-dashboard' }
    };
    const kind = Object.hasOwn(states, state) ? state : detected;
    return { state: kind, ...states[kind] };
}

function renderMessage(target, text, state) {
    const appearance = responseAppearance(text, state);
    const message = element('div', 'response-message'); message.dataset.state = appearance.state;
    const icon = document.createElementNS('http://www.w3.org/2000/svg', 'svg'); icon.setAttribute('aria-hidden', 'true');
    const use = document.createElementNS('http://www.w3.org/2000/svg', 'use'); use.setAttribute('href', '#' + appearance.icon); icon.append(use);
    const body = element('div'); body.append(element('h3', '', appearance.title), element('p', '', text));
    message.append(icon, body); target.append(message);
}

export function renderResponse(target, text, command = '', { state } = {}) {
    let decoded = command;
    try { decoded = decodeURIComponent(command); } catch { /* keep non-encoded commands intact */ }
    const help = /^\s*judo\s+(help|\?)(\s|$)/i.test(decoded) ? parseHelp(text) : null;
    target.replaceChildren();
    target.classList.toggle('help-output', Boolean(help));
    if (help) renderHelp(target, help);
    else if (text && (target.classList.contains('assistant-response') || target.classList.contains('result'))) renderMessage(target, text, state);
    else target.textContent = text;
    return Boolean(help);
}
