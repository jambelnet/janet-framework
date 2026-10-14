// (c) J@mBeL.net 2010-2026. Part of jaNET Framework, GNU GPL version 3 or later; see LICENSE.
export const iconPath = icon => 'icons/default/' + icon.id + '.svg';

const instructionAliases = new Map(Object.entries({
    whoami: 'user', whereami: 'location', whoru: 'info', unmute: 'speaker', weathertoday: 'weather',
    allmail: 'inbox', gmailreader: 'inbox', gmailcount: 'gmail', gmailone: 'gmail', pop3count: 'inbox', pop3one: 'inbox',
    time24: 'time', partofday: 'day', daypart: 'day', day: 'date', checkin: 'check-in', checkout: 'check-out',
    check_me_in: 'check-in', check_me_out: 'check-out', checkinout: 'location', eval_check_in_out: 'location',
    userstatus: 'status', sleeper: 'delay'
}));
const legacyIcons = new Map(Object.entries({
    checkin: 'check-in', checkout: 'check-out', gmail: 'gmail', settings: 'settings', user: 'user', home: 'home',
    mute: 'mute', unmute: 'speaker', weather: 'weather', email: 'mail', allmail: 'inbox', gmailreader: 'inbox',
    time: 'time', time24: 'time', calendar: 'date', day: 'date', daypart: 'day', location: 'location'
}));
const categoryIcons = new Map(Object.entries({
    home: 'home', user: 'user', system: 'settings', network: 'server', networks: 'server', weather: 'weather',
    localization: 'time', automation: 'instruction', communication: 'mail'
}));
const words = value => String(value ?? '').toLowerCase().replace(/[^a-z0-9]+/g, ' ').trim();

export function instructionThumbnail(instruction, catalog, origin = globalThis.location?.origin ?? 'https://jubito.invalid') {
    const image = typeof instruction.img === 'string' ? instruction.img.trim() : '';
    if (image) {
        // Only recognized local legacy files are replaced; a remote file with the same name is still custom.
        try {
            const url = new URL(image, origin + '/www/');
            const match = url.pathname.match(/^\/(?:www\/)?images\/(?:icon-set\/)?([a-z0-9]+)\.png$/i);
            if (url.origin === origin && match && legacyIcons.has(match[1].toLowerCase()))
                return iconPath({ id: legacyIcons.get(match[1].toLowerCase()) });
        } catch { /* Preserve custom image values rather than guessing a replacement. */ }
        return image;
    }
    const id = String(instruction.id ?? '').trim().replace(/^\*/, '').replace(/widget$/i, '').toLowerCase();
    if (instructionAliases.has(id)) return iconPath({ id: instructionAliases.get(id) });
    if (catalog) {
        const exact = catalog.icons.find(icon => icon.id === id);
        if (exact) return iconPath(exact);
        for (const value of [id, instruction.header]) {
            const text = ' ' + words(value) + ' ';
            const candidates = catalog.icons.filter(icon => text.includes(' ' + words(icon.id) + ' '));
            if (candidates.length === 1) return iconPath(candidates[0]);
        }
    }
    return iconPath({ id: categoryIcons.get(words(instruction.categ)) ?? 'instruction' });
}

export function validateIconCatalog(catalog) {
    if (catalog?.version !== 1 || !Array.isArray(catalog.categories) || !Array.isArray(catalog.icons) ||
        !catalog.categories.length || !catalog.icons.length || catalog.icons.length > 300) throw new Error('Invalid icon catalog.');
    const categories = new Set(), ids = new Set();
    for (const category of catalog.categories) {
        if (!/^[a-z][a-z0-9-]*$/.test(category.id) || categories.has(category.id) || typeof category.label !== 'string' ||
            !/^#[0-9a-f]{6}$/i.test(category.color)) throw new Error('Invalid icon category.');
        categories.add(category.id);
    }
    for (const icon of catalog.icons) {
        if (!/^[a-z][a-z0-9-]*$/.test(icon.id) || ids.has(icon.id) || !categories.has(icon.category) ||
            typeof icon.label !== 'string' || typeof icon.keywords !== 'string') throw new Error('Invalid icon entry.');
        ids.add(icon.id);
    }
    return catalog;
}

export function matchingIcons(catalog, query = '', category = '') {
    const words = query.trim().toLowerCase().split(/\s+/).filter(Boolean);
    return catalog.icons.filter(icon => (!category || icon.category === category) &&
        words.every(word => (icon.id + ' ' + icon.label + ' ' + icon.keywords).toLowerCase().includes(word)));
}

export async function loadIconCatalog() {
    const response = await fetch('icons/catalog.json', { cache: 'no-store', signal: AbortSignal.timeout(15000) });
    if (!response.ok) throw new Error('Icon library could not load (HTTP ' + response.status + ').');
    return validateIconCatalog(await response.json());
}

export function iconButtons(document, icons, choose, selected = '') {
    return icons.map(icon => {
        const button = document.createElement('button'); button.type = 'button'; button.className = 'default-icon-option';
        button.title = icon.label; button.setAttribute('aria-label', icon.label); button.setAttribute('aria-pressed', String(selected === iconPath(icon)));
        const image = document.createElement('img'); image.src = iconPath(icon); image.alt = ''; image.width = 48; image.height = 48; image.loading = 'lazy';
        const label = document.createElement('span'); label.textContent = icon.label;
        button.append(image, label); button.addEventListener('click', () => choose(icon));
        return button;
    });
}

export function categoryOptions(document, catalog) {
    return [{ id: '', label: 'All categories' }, ...catalog.categories].map(category => {
        const option = document.createElement('option'); option.value = category.id; option.textContent = category.label; return option;
    });
}

export function wireIconPicker(document, load = loadIconCatalog) {
    const $ = id => document.getElementById(id), dialog = $('iconPicker');
    let catalog = null, target = null, generation = 0;
    function render() {
        if (!catalog || !target) return;
        const icons = matchingIcons(catalog, $('iconSearch').value, $('iconCategory').value);
        $('iconChoices').replaceChildren(...iconButtons(document, icons, icon => {
            target.value = iconPath(icon); target.dispatchEvent(new Event('input', { bubbles: true })); dialog.close();
        }, target.value));
        $('iconPickerStatus').textContent = icons.length ? icons.length + ' icons' : 'No matching icons.';
    }
    async function open(input) {
        const current = ++generation; target = input; catalog = null;
        $('iconSearch').value = ''; $('iconCategory').disabled = true; $('iconSearch').disabled = true;
        $('iconChoices').replaceChildren(); $('iconPickerRetry').hidden = true; $('iconPickerStatus').textContent = 'Loading icons...';
        if (!dialog.open) dialog.showModal();
        try {
            const result = await load();
            if (current !== generation || !dialog.open) return;
            catalog = validateIconCatalog(result); $('iconCategory').replaceChildren(...categoryOptions(document, catalog));
            $('iconCategory').value = ''; $('iconCategory').disabled = false; $('iconSearch').disabled = false;
            render(); $('iconSearch').focus();
        } catch (error) {
            if (current !== generation || !dialog.open) return;
            $('iconPickerStatus').textContent = error.message; $('iconPickerRetry').hidden = false;
        }
    }
    for (const button of document.querySelectorAll('[data-iconpick]')) button.addEventListener('click', () => {
        const input = $(button.dataset.iconpick); if (input && !input.disabled) open(input);
    });
    $('iconSearch').addEventListener('input', render); $('iconCategory').addEventListener('change', render);
    $('iconPickerRetry').addEventListener('click', () => { if (target) open(target); });
    $('closeIconPicker').addEventListener('click', () => dialog.close());
    dialog.addEventListener('close', () => { if (!dialog.open) { generation++; target = null; catalog = null; } });
    return { open };
}
