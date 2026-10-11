// (c) J@mBeL.net 2010-2026. Part of jaNET Framework, GNU GPL version 3 or later.

// A routine has multiple execution rows, but only one browser command choice.
export function instructionChoices(entries) {
    const choices = new Map();
    const metadata = ['categ', 'header', 'shortdescr', 'descr', 'img', 'ref'];
    for (const entry of entries) {
        if (typeof entry.id !== 'string' || !entry.id) continue;
        if (!choices.has(entry.id)) {
            choices.set(entry.id, { ...entry });
            continue;
        }
        const choice = choices.get(entry.id);
        for (const field of metadata) {
            if ((choice[field] == null || choice[field] === '') && entry[field] != null && entry[field] !== '') choice[field] = entry[field];
        }
    }
    return [...choices.values()];
}
