// (c) J@mBeL.net 2010-2026. Part of jaNET Framework, GNU GPL version 3 or later; see LICENSE.
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import test from 'node:test';
import { fileURLToPath } from 'node:url';
const source = await readFile(new URL('../www/js/icon-library.js', import.meta.url), 'utf8');
const { iconPath, validateIconCatalog, matchingIcons, loadIconCatalog, iconButtons, wireIconPicker, instructionThumbnail } = await import('data:text/javascript;base64,' + Buffer.from(source).toString('base64'));
const catalog = JSON.parse(await readFile(new URL('../www/icons/catalog.json', import.meta.url), 'utf8'));
const clone = () => structuredClone(catalog);
const tick = () => new Promise(resolve => setImmediate(resolve));
function fixture(load = async () => catalog) {
    function element() { return { value: '', hidden: false, disabled: false, children: [], attributes: {}, events: {}, dataset: {},
        append(...nodes) { this.children.push(...nodes); }, replaceChildren(...nodes) { this.children = nodes; },
        setAttribute(key, value) { this.attributes[key] = value; }, addEventListener(key, fn) { this.events[key] = fn; },
        dispatchEvent(event) { this.dispatched = event; }, focus() { this.focused = true; } }; }
    const elements = Object.fromEntries(['iconPicker', 'iconChoices', 'iconSearch', 'iconCategory', 'iconPickerStatus', 'iconPickerRetry', 'closeIconPicker', 'thumbnail', 'choose'].map(id => [id, element()]));
    elements.choose.dataset.iconpick = 'thumbnail'; elements.thumbnail.value = 'images/custom.png';
    const dialog = elements.iconPicker;
    dialog.showModal = () => { dialog.open = true; }; dialog.close = () => { dialog.open = false; dialog.events.close(); };
    const document = { getElementById: id => elements[id], querySelectorAll: () => [elements.choose], createElement: element };
    const picker = wireIconPicker(document, load);
    return { elements, dialog, document, picker };
}

test('catalog covers supported feature families with unique safe local paths', () => {
    assert.equal(validateIconCatalog(catalog), catalog); assert.equal(catalog.icons.length, 77);
    for (const id of ['instruction', 'event', 'condition', 'delay', 'schedule', 'studio', 'program', 'capture', 'function',
        'mail', 'gmail', 'sms', 'voice', 'mqtt', 'serial', 'https', 'http', 'json', 'xml', 'web-service', 'cloud', 'ddns', 'ping', 'calendar', 'weather', 'temperature', 'humidity', 'pressure', 'terminal']) {
        const icon = catalog.icons.find(icon => icon.id === id); assert(icon, id); assert.equal(iconPath(icon), 'icons/default/' + id + '.svg');
    }
});

test('catalog rejects paths, duplicates, unsupported formats and incomplete entries', () => {
    for (const bad of [null, {}, { ...catalog, version: 2 }]) assert.throws(() => validateIconCatalog(bad));
    for (const value of ['../bad', 'https://example.org/x', 'x" onload="bad', '']) {
        const bad = clone(); bad.icons[0].id = value; assert.throws(() => validateIconCatalog(bad));
    }
    for (const mutate of [c => c.icons.push(c.icons[0]), c => c.categories.push(c.categories[0]),
        c => c.icons[0].category = 'missing', c => c.icons[0].label = null, c => c.categories[0].color = 'url(bad)']) {
        const bad = clone(); mutate(bad); assert.throws(() => validateIconCatalog(bad));
    }
});

test('search supports names, feature synonyms, multiple terms and categories', () => {
    assert.equal(matchingIcons(catalog).length, 77);
    assert.deepEqual(matchingIcons(catalog, 'GOOGLE AGENDA').map(icon => icon.id), ['calendar']);
    assert(matchingIcons(catalog, 'smtp').some(icon => icon.id === 'send-mail'));
    assert(matchingIcons(catalog, 'mqtt', 'network').length >= 3);
    assert.deepEqual(matchingIcons(catalog, 'mqtt', 'weather'), []);
    assert.deepEqual(matchingIcons(catalog, 'no-such-icon'), []);
});

test('generated icons are reproducible and retain their license notice', async () => {
    const { stdout } = await promisify(execFile)(process.execPath, [fileURLToPath(new URL('../tools/build-icons.mjs', import.meta.url)), '--check']);
    assert(stdout.includes('77 default icons'));
    for (const icon of catalog.icons) {
        const svg = await readFile(new URL('../www/' + iconPath(icon), import.meta.url), 'utf8');
        assert(svg.includes('LICENSE-lucide.txt')); assert(svg.includes('viewBox="0 0 64 64"'));
        assert(!/<script|<foreignObject|\bon\w+=|href=/i.test(svg));
    }
});

test('icon data is rendered as text with fixed image dimensions and selected-state semantics', () => {
    const f = fixture(), icon = { ...catalog.icons[0], label: '<script>bad()</script>' };
    const button = iconButtons(f.document, [icon], () => {}, iconPath(icon))[0];
    assert.equal(button.children[1].textContent, icon.label); assert.equal(button.children[0].width, 48);
    assert.equal(button.children[0].height, 48); assert.equal(button.attributes['aria-pressed'], 'true');
});

test('picker changes only the target thumbnail on selection and preserves custom paths on cancel', async () => {
    const f = fixture(); await f.picker.open(f.elements.thumbnail);
    assert.equal(f.elements.thumbnail.value, 'images/custom.png'); assert(f.elements.iconSearch.focused);
    f.dialog.close(); assert.equal(f.elements.thumbnail.value, 'images/custom.png');
    await f.picker.open(f.elements.thumbnail);
    f.elements.iconSearch.value = 'google agenda'; f.elements.iconSearch.events.input();
    assert.equal(f.elements.iconChoices.children.length, 1);
    f.elements.iconChoices.children[0].events.click();
    assert.equal(f.elements.thumbnail.value, 'icons/default/calendar.svg'); assert(!f.dialog.open);
    assert.equal(f.elements.thumbnail.dispatched.type, 'input'); assert(f.elements.thumbnail.dispatched.bubbles);
    await f.picker.open(f.elements.thumbnail); f.elements.iconSearch.value = 'no-such-icon'; f.elements.iconSearch.events.input();
    assert.equal(f.elements.iconChoices.children.length, 0); assert(f.elements.iconPickerStatus.textContent.includes('No matching'));
});

test('failed or late loads never change a thumbnail or populate an obsolete dialog', async () => {
    let fail = true;
    const f = fixture(async () => { if (fail) throw new Error('Offline'); return catalog; });
    await f.picker.open(f.elements.thumbnail); assert(!f.elements.iconPickerRetry.hidden); assert(f.elements.iconCategory.disabled);
    assert.equal(f.elements.thumbnail.value, 'images/custom.png');
    fail = false; f.elements.iconPickerRetry.events.click(); await tick(); assert.equal(f.elements.iconChoices.children.length, 77);
    let release, old = true;
    const late = fixture(async () => old ? new Promise(resolve => { release = resolve; }) : catalog);
    const opening = late.picker.open(late.elements.thumbnail); late.dialog.close(); old = false;
    await late.picker.open(late.elements.thumbnail); release({}); await opening;
    assert.equal(late.elements.iconChoices.children.length, 77); assert.equal(late.elements.thumbnail.value, 'images/custom.png');
});

test('catalog fetch stays local and read-only with explicit error handling', async () => {
    const original = globalThis.fetch; const calls = [];
    try {
        globalThis.fetch = async (...args) => { calls.push(args); return { ok: true, json: async () => catalog }; };
        assert.equal(await loadIconCatalog(), catalog); assert.equal(calls[0][0], 'icons/catalog.json');
        assert.equal(calls[0][1].cache, 'no-store'); assert(!calls[0][1].method); assert(!calls[0][1].body);
        globalThis.fetch = async () => ({ ok: false, status: 404 }); await assert.rejects(loadIconCatalog(), /HTTP 404/);
    } finally { globalThis.fetch = original; }
});

test('known local legacy images resolve to defaults, including old paths and cache queries', () => {
    const aliases = { checkin: 'check-in', checkout: 'check-out', gmail: 'gmail', settings: 'settings', user: 'user',
        home: 'home', mute: 'mute', unmute: 'speaker', weather: 'weather', email: 'mail', allmail: 'inbox',
        gmailreader: 'inbox', time: 'time', time24: 'time', calendar: 'date', day: 'date', daypart: 'day', location: 'location' };
    for (const [old, id] of Object.entries(aliases)) {
        for (const prefix of ['images/', './images/', '/www/images/', '/www/images/icon-set/', '/images/icon-set/', 'https://jubito.example/www/images/icon-set/']) {
            assert.equal(instructionThumbnail({ id: 'custom-name', img: prefix + old + '.png?version=old#image' }, catalog, 'https://jubito.example'), 'icons/default/' + id + '.svg');
        }
    }
});

test('custom images, branding, remote lookalikes and explicit new icons are never replaced', () => {
    for (const img of ['images/custom.png', '/www/images/logo.png', 'images/jaNET logo.png', 'icons/default/calendar.svg',
        'https://other.example/www/images/checkin.png', 'https://other.example/home.svg', 'data:image/png;base64,custom',
        'images/icon-set/custom.png', 'images/my-user.png']) {
        const instruction = { id: 'checkin', header: 'Calendar', img };
        assert.equal(instructionThumbnail(instruction, catalog, 'https://jubito.example'), img);
        assert.equal(instruction.img, img);
    }
});

test('automatic thumbnails cover built-ins, explicit IDs and recognizable custom routine names', () => {
    const builtins = { whoami: 'user', whereami: 'location', unmute: 'speaker', weathertoday: 'weather', allmail: 'inbox',
        time24: 'time', date: 'date', day: 'date', partofday: 'day', check_me_in: 'check-in', check_me_out: 'check-out',
        eval_check_in_out: 'location', userstatuswidget: 'status', sleeper: 'delay', '*gmailwidget': 'gmail' };
    for (const [id, icon] of Object.entries(builtins)) assert.equal(instructionThumbnail({ id }, catalog), 'icons/default/' + icon + '.svg');
    for (const icon of catalog.icons.filter(icon => icon.id !== 'day')) assert.equal(instructionThumbnail({ id: icon.id }, catalog), iconPath(icon));
    assert.equal(instructionThumbnail({ id: 'kitchen-light-on' }, catalog), 'icons/default/light.svg');
    assert.equal(instructionThumbnail({ id: 'living-room-on', header: 'Living room light' }, catalog), 'icons/default/light.svg');
    assert.equal(instructionThumbnail({ id: 'my-agenda', header: 'Google Calendar' }, catalog), 'icons/default/calendar.svg');
    assert.equal(instructionThumbnail({ id: 'custom', categ: 'Weather' }, catalog), 'icons/default/weather.svg');
    assert.equal(instructionThumbnail({ id: 'custom', categ: 'Network' }, catalog), 'icons/default/server.svg');
    assert.equal(instructionThumbnail({ id: 'light-weather', categ: 'Automation' }, catalog), 'icons/default/instruction.svg');
    assert.equal(instructionThumbnail({ id: 'custom' }, catalog), 'icons/default/instruction.svg');
    assert.equal(instructionThumbnail({ id: 'custom' }, null), 'icons/default/instruction.svg');
});

test('Home shortcuts use the new set and automatic display does not rewrite stored metadata', async () => {
    const html = await readFile(new URL('../www/index.html', import.meta.url), 'utf8');
    for (const id of ['check-in', 'check-out', 'gmail', 'settings']) assert(html.includes('src="icons/default/' + id + '.svg"'));
    for (const legacy of ['checkin.png', 'checkout.png', 'gmail.png', 'settings.png']) assert(!html.includes('images/' + legacy));
    const entry = { id: 'whoami', img: '/www/images/icon-set/user.png', ref: 'whoamiwidget', header: 'Login name', categ: 'System' };
    const original = structuredClone(entry);
    assert.equal(instructionThumbnail(entry, catalog), 'icons/default/user.svg'); assert.deepEqual(entry, original);
});

test('retired shortcut files are absent while logos, weather and RaZberry device icons remain', async () => {
    for (const filename of ['checkin.png', 'checkout.png', 'gmail.png', 'settings.png'])
        await assert.rejects(readFile(new URL('../www/images/' + filename, import.meta.url)), { code: 'ENOENT' });
    for (const filename of ['logo.png', 'jaNET logo.png', 'weather-skies.png'])
        assert((await readFile(new URL('../www/images/' + filename, import.meta.url))).length > 0);
    const razberry = await readFile(new URL('../www/js/jubito.razberry.js', import.meta.url), 'utf8');
    assert(razberry.includes("switchClass ? 'outlet' : sensorClass ? 'sensor' : 'controller'"));
    assert(!razberry.includes("spacer.className = 'noimg'"));
});
