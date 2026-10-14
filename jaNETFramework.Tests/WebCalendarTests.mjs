// (c) J@mBeL.net 2010-2026. Part of jaNET Framework, GNU GPL version 3 or later; see LICENSE.
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
const source = await readFile(new URL('../www/js/calendar.js', import.meta.url), 'utf8');
const { calendarText, wireCalendar, validateCalendarRedirect, suggestedCalendarRedirect } = await import('data:text/javascript;base64,' + Buffer.from(source).toString('base64'));
const settings = { clientId: 'test.apps.googleusercontent.com', clientSecret: '', redirectUri: 'http://localhost:8080/api/calendar/callback', calendarId: 'primary', timeZone: 'Europe/Paris', secretConfigured: true, connected: true };
const tick = () => new Promise(resolve => setImmediate(resolve));
function fixture(request, origin = 'http://localhost:8080', onChange) {
    const ids = ['googleCalendarSettings', 'calendarForm', 'calendarStatus', 'calendarClientId', 'calendarClientSecret', 'calendarRedirectUri', 'calendarId', 'calendarTimeZone', 'calendarConnect', 'calendarPreview', 'calendarDisconnect', 'calendarRetry', 'calendarAgenda', 'save'];
    const elements = Object.fromEntries(ids.map(id => [id, { id, dataset: {}, value: '', disabled: false, hidden: false, events: {},
        addEventListener(name, callback) { this.events[name] = callback; }, click() { this.events.click?.(); } }]));
    const form = elements.calendarForm, dialog = elements.googleCalendarSettings;
    form.querySelectorAll = () => ids.filter(id => id.startsWith('calendar') && !['calendarStatus', 'calendarForm', 'calendarAgenda'].includes(id)).map(id => elements[id]).concat(elements.save);
    form.reportValidity = () => true;
    form.submit = () => form.events.submit({ preventDefault() {} });
    dialog.open = false; dialog.showModal = () => { dialog.open = true; }; dialog.close = () => { dialog.open = false; dialog.events.close(); };
    const notifications = [], navigation = [];
    const editor = wireCalendar({ getElementById: id => elements[id] }, request, message => notifications.push(message), url => navigation.push(url), origin, onChange);
    return { elements, editor, form, dialog, notifications, navigation };
}

test('successful settings writes and disconnect invalidate Home, failed writes do not', async () => {
    let changes = 0, fail = false;
    const f = fixture(async (action, values) => {
        if (fail && values) throw new Error('Save failed');
        if (action === 'disconnect') return { message: 'Disconnected' };
        return values ? { message: 'Saved', settings } : settings;
    }, 'http://localhost:8080', () => { changes++; });
    await f.editor.open(); assert.equal(changes, 0);
    f.form.submit(); await tick(); assert.equal(changes, 1);
    fail = true; f.form.submit(); await tick(); assert.equal(changes, 1);
    fail = false; f.elements.calendarDisconnect.click(); await tick(); assert.equal(changes, 2);
});

test('Google redirect validation rejects IP hosts but keeps loopback and HTTPS domains', () => {
    for (const origin of ['http://localhost:8080', 'https://localhost:8443', 'http://127.0.0.1:8080', 'http://[::1]:8080', 'https://janet.example.org:8443'])
        assert.equal(suggestedCalendarRedirect(origin), origin + '/api/calendar/callback');
    for (const origin of ['https://192.168.178.23:8443', 'https://8.8.8.8', 'https://[2001:db8::1]', 'http://janet.example.org']) {
        assert.equal(suggestedCalendarRedirect(origin), ''); assert.throws(() => validateCalendarRedirect(origin + '/api/calendar/callback'));
    }
    for (const suffix of ['?code=bad', '#bad']) assert.throws(() => validateCalendarRedirect(settings.redirectUri + suffix));
    assert.throws(() => validateCalendarRedirect('https://user:pass@janet.example.org/api/calendar/callback'));
});

test('invalid IP redirects and mismatched browser origins cannot save and start authorization', async () => {
    const calls = [];
    const f = fixture(async (action, values) => { calls.push([action, values]); return settings; }, 'https://192.168.178.23:8443');
    await f.editor.open();
    f.elements.calendarRedirectUri.value = 'https://192.168.178.23:8443/api/calendar/callback';
    f.elements.calendarConnect.click(); await tick();
    assert(f.elements.calendarStatus.textContent.includes('IP-address')); assert.equal(calls.length, 1); assert(!f.elements.calendarConnect.disabled);
    f.form.submit(); await tick(); assert.equal(calls.length, 1);
    f.elements.calendarRedirectUri.value = settings.redirectUri;
    f.elements.calendarConnect.click(); await tick();
    assert(f.elements.calendarStatus.textContent.includes('http://localhost:8080/www/')); assert.equal(calls.length, 1); assert.equal(f.navigation.length, 0);
});

test('agenda respects saved timezone and leaves all-day dates untouched', () => {
    assert.equal(calendarText({ timeZone: 'Europe/Paris', events: [] }), 'No upcoming events.');
    const text = calendarText({ timeZone: 'Europe/Paris', events: [
        { start: '2026-10-14', summary: 'School holiday', allDay: true, location: '' },
        { start: '2026-10-14T11:00:00Z', summary: 'Home visit', allDay: false, location: 'Living room' }
    ] });
    assert(text.startsWith('2026-10-14 (all day) - School holiday\n')); assert(text.includes('1:00 PM') || text.includes('13:00'));
    assert(text.endsWith('Home visit - Living room')); assert.throws(() => calendarText({ timeZone: 'UTC' }));
});

test('opening reloads real values, retains server-side secrets and gates controls after failure', async () => {
    let fail = true;
    const f = fixture(async () => { if (fail) throw new Error('Settings unavailable'); return settings; });
    await f.editor.open(); assert(f.elements.save.disabled); assert(!f.elements.calendarRetry.hidden);
    fail = false; await f.editor.open();
    assert.equal(f.elements.calendarClientId.value, settings.clientId); assert.equal(f.elements.calendarClientSecret.value, '');
    assert(!f.elements.calendarClientSecret.required); assert(f.elements.calendarClientSecret.placeholder.includes('Saved'));
    assert(!f.elements.calendarPreview.disabled); assert(!f.elements.save.disabled);
    f.elements.calendarId.value = 'unsaved'; f.dialog.close(); await f.editor.open(); assert.equal(f.elements.calendarId.value, 'primary');
});

test('failed save preserves the dialog and draft; connect saves before navigating to Google', async () => {
    const calls = []; let fail = true;
    const f = fixture(async (action, values) => {
        calls.push([action, values]);
        if (action === 'settings' && values) { if (fail) throw new Error('Save failed'); return { message: 'Saved', settings: { ...settings, calendarId: values.calendarId } }; }
        if (action === 'connect') return { url: 'https://accounts.google.com/o/oauth2/v2/auth?state=test' };
        return settings;
    });
    await f.editor.open(); f.elements.calendarId.value = 'shared@example.org'; f.form.submit(); await tick();
    assert(f.dialog.open); assert.equal(f.elements.calendarId.value, 'shared@example.org'); assert(!f.elements.save.disabled); assert.equal(f.navigation.length, 0);
    fail = false; f.elements.calendarConnect.click(); await tick();
    assert.equal(calls.at(-2)[0], 'settings'); assert.equal(calls.at(-1)[0], 'connect');
    assert.equal(calls.at(-2)[1].clientSecret, ''); assert.equal(f.navigation.length, 1);
});

test('preview renders event data as text and disconnect hides obsolete agenda', async () => {
    const f = fixture(async action => action === 'events' ? { timeZone: 'UTC', events: [{ start: '2026-10-14', summary: '<img src=x onerror=bad()>', allDay: true }] }
        : action === 'disconnect' ? { message: 'Disconnected' } : settings);
    await f.editor.open(); f.elements.calendarPreview.click(); await tick();
    assert(f.elements.calendarAgenda.textContent.includes('<img')); assert(!f.elements.calendarAgenda.hidden);
    f.elements.calendarDisconnect.click(); await tick();
    assert(f.elements.calendarAgenda.hidden); assert(f.elements.calendarPreview.disabled); assert(f.elements.calendarDisconnect.disabled);
});

test('late settings and preview responses do not overwrite a closed or reopened dialog', async () => {
    let release; let old = true;
    const f = fixture(async () => old ? new Promise(resolve => { release = resolve; }) : { ...settings, calendarId: 'new-calendar' });
    const opening = f.editor.open(); f.dialog.close(); old = false;
    await f.editor.open(); release(settings); await opening;
    assert.equal(f.elements.calendarId.value, 'new-calendar'); assert(!f.elements.save.disabled);
});

test('unsafe settings and authorization URLs never enable a connection', async () => {
    const unsafe = fixture(async () => ({ ...settings, clientSecret: 'leaked-secret' }));
    await unsafe.editor.open(); assert(unsafe.elements.save.disabled);
    const f = fixture(async (action, values) => action === 'connect' ? { url: 'https://evil.example.org/' } : values ? { message: 'Saved', settings } : settings);
    await f.editor.open(); f.elements.calendarConnect.click(); await tick(); assert.equal(f.navigation.length, 0);
    assert(f.elements.calendarStatus.textContent.includes('Invalid Google'));
});
