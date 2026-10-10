// (c) J@mBeL.net 2010-2026, John Ambeliotis. Part of jaNET Framework, GNU GPL version 3 or later; see LICENSE.

const normalize = value => String(value ?? '').normalize('NFKC').toLowerCase().replace(/[^\p{L}\p{N}]/gu, '');
const weather = '%currentcity% %currenttemperature% degrees, %todayconditions%';
const aliases = [
    ['who am i', '%whoami%'], ['what is my name', '%whoami%'], ['my name', '%whoami%'],
    ['where am i', '%whereami%'], ['what is my status', '%whereami%'],
    ['weather', weather], ['what is the weather', weather], ["what's the weather", weather],
    ['what is the weather today', weather], ["what's the weather today", weather],
    ['time', '%time24%'], ['what time is it', '%time24%'], ['tell me the time', '%time24%'],
    ['check in', '%checkin%'], ['check out', '%checkout%'], ['help', 'judo help']
].map(([label, command]) => ({ label, command }));

// Only exact normalized names run directly. Partial matches are choices, never guessed actions.
export function resolveIntent(input, instructions = []) {
    const text = String(input ?? '').trim();
    if (!text) return { command: null, suggestions: [] };
    if (/^judo\s|^%[^%]+%/i.test(text)) return { command: text, suggestions: [] };
    const query = text.replace(/^please\s+/i, '').replace(/\s+please[.!?]*$/i, '');
    const key = normalize(query);
    if (!key) return { command: null, suggestions: [] };
    const configured = instructions.filter(item => item.id && !item.id.startsWith('*'))
        .map(item => ({ label: item.header || item.id, command: item.id, names: [item.id, item.header].filter(Boolean) }));
    const exact = configured.filter(item => item.names.some(name => normalize(name) === key));
    const known = aliases.filter(item => normalize(item.label) === key);
    const matches = exact.length ? exact : known;
    const unique = list => [...new Map(list.map(item => [item.command, { label: item.label, command: item.command }])).values()];
    const distinct = unique(matches);
    if (distinct.length === 1) return { command: distinct[0].command, suggestions: [] };
    if (distinct.length > 1) return { command: null, suggestions: distinct.slice(0, 5) };
    const words = query.toLowerCase().match(/[\p{L}\p{N}]+/gu) ?? [];
    const useful = words.filter(word => !['the', 'a', 'an', 'my', 'please', 'run', 'show', 'tell', 'me', 'what', 'is', 'do', 'can', 'you'].includes(word));
    if (!useful.length) return { command: null, suggestions: [] };
    const ranked = [...configured, ...aliases].map(item => {
        const names = item.names ?? [item.label];
        const tokens = names.join(' ').toLowerCase().match(/[\p{L}\p{N}]+/gu) ?? [];
        const score = useful.filter(word => tokens.some(token => token === word || (word.length >= 4 && token.startsWith(word)))).length;
        return { ...item, score };
    }).filter(item => item.score > 0).sort((a, b) => b.score - a.score);
    return { command: null, suggestions: unique(ranked).slice(0, 5) };
}
