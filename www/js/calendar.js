// (c) J@mBeL.net 2010-2026. Part of jaNET Framework, GNU GPL version 3 or later; see LICENSE.
const fields = { clientId: 'calendarClientId', clientSecret: 'calendarClientSecret', redirectUri: 'calendarRedirectUri', calendarId: 'calendarId', timeZone: 'calendarTimeZone' };

export function validateCalendarRedirect(value) {
    let url;
    try { url = new URL(value.trim()); }
    catch { throw new Error('Enter a valid Google Calendar redirect URI.'); }
    const loopback = url.hostname === 'localhost' || /^127\./.test(url.hostname) || url.hostname === '[::1]';
    const ip = /^\d+\.\d+\.\d+\.\d+$/.test(url.hostname) || url.hostname.includes(':');
    if (ip && !loopback) throw new Error('Google rejects IP-address redirects except loopback. Use localhost on the jaNET computer, or an HTTPS domain name.');
    if (!(url.protocol === 'https:' || url.protocol === 'http:' && loopback) || url.pathname !== '/api/calendar/callback' || url.search || url.hash || url.username || url.password)
        throw new Error('Use an HTTPS domain or localhost callback ending in /api/calendar/callback, with no query, fragment or credentials.');
    return url;
}

export function suggestedCalendarRedirect(origin) {
    try { return validateCalendarRedirect(origin + '/api/calendar/callback').href; }
    catch { return ''; }
}

export function calendarText(agenda) {
    if (!Array.isArray(agenda.events) || typeof agenda.timeZone !== 'string') throw new Error('Invalid Calendar response.');
    if (!agenda.events.length) return 'No upcoming events.';
    const format = new Intl.DateTimeFormat(undefined, { timeZone: agenda.timeZone, dateStyle: 'medium', timeStyle: 'short' });
    return agenda.events.map(event => {
        const start = event.allDay ? event.start + ' (all day)' : format.format(new Date(event.start));
        return start + ' - ' + event.summary + (event.location ? ' - ' + event.location : '');
    }).join('\n');
}

export function wireCalendar(document, request, notify, navigate = url => window.location.assign(url), origin = globalThis.location?.origin ?? '', onChange = () => {}) {
    const $ = id => document.getElementById(id), dialog = $('googleCalendarSettings'), form = $('calendarForm');
    const status = $('calendarStatus'), controls = [...form.querySelectorAll('input,button')].filter(button => !('close' in button.dataset));
    let ready = false, busy = false, connected = false, generation = 0;
    function setBusy(value) {
        busy = value;
        controls.forEach(control => { control.disabled = value || !ready; });
        $('calendarRetry').disabled = value;
        $('calendarPreview').disabled = value || !ready || !connected;
        $('calendarDisconnect').disabled = value || !ready || !connected;
    }
    function apply(settings) {
        if (typeof settings.secretConfigured !== 'boolean' || typeof settings.connected !== 'boolean' || settings.clientSecret !== '' ||
            Object.keys(fields).some(key => typeof settings[key] !== 'string')) throw new Error('Invalid Calendar settings response.');
        for (const [key, id] of Object.entries(fields)) $(id).value = settings[key];
        if (!settings.redirectUri) $('calendarRedirectUri').value = suggestedCalendarRedirect(origin);
        $('calendarRedirectUri').placeholder = 'http://localhost:8080/api/calendar/callback';
        $('calendarClientSecret').required = !settings.secretConfigured;
        $('calendarClientSecret').placeholder = settings.secretConfigured ? 'Saved; leave blank to keep unchanged' : '';
        connected = settings.connected; status.textContent = connected ? 'Connected (read-only)' : 'Not connected';
    }
    async function open() {
        const current = ++generation; ready = false; $('calendarRetry').hidden = true; $('calendarAgenda').hidden = true;
        setBusy(true); status.textContent = 'Loading saved settings...';
        if (!dialog.open) dialog.showModal();
        try {
            const settings = await request('settings');
            if (current !== generation || !dialog.open) return;
            apply(settings); ready = true;
        } catch (error) {
            if (current !== generation || !dialog.open) return;
            status.textContent = error.message; $('calendarRetry').hidden = false;
        } finally { if (current === generation && dialog.open) setBusy(false); }
    }
    async function save(current) {
        const values = Object.fromEntries(Object.entries(fields).map(([key, id]) => [key, $(id).value]));
        validateCalendarRedirect(values.redirectUri);
        const result = await request('settings', values);
        onChange();
        if (current()) { apply(result.settings); $('calendarAgenda').hidden = true; }
        return result;
    }
    async function run(action) {
        if (busy || !ready) return;
        const current = generation; setBusy(true);
        try { await action(() => current === generation && dialog.open); }
        catch (error) { if (current === generation && dialog.open) { status.textContent = error.message; notify(error.message); } }
        finally { if (current === generation && dialog.open) setBusy(false); }
    }
    form.addEventListener('submit', event => { event.preventDefault(); run(async current => { const result = await save(current); if (current()) notify(result.message); }); });
    $('calendarConnect').addEventListener('click', () => {
        if (!form.reportValidity()) return;
        run(async current => {
            const redirect = validateCalendarRedirect($('calendarRedirectUri').value);
            if (redirect.origin !== origin) throw new Error('Open Jubito at ' + redirect.origin + '/www/ on the jaNET computer, then connect again. The page must use the same origin as the redirect URI.');
            await save(current);
            if (!current()) return;
            const result = await request('connect', {}), url = new URL(result.url);
            if (url.origin !== 'https://accounts.google.com' || url.pathname !== '/o/oauth2/v2/auth') throw new Error('Invalid Google authorization address.');
            if (current()) navigate(url.href);
        });
    });
    $('calendarPreview').addEventListener('click', () => run(async current => {
        const result = await request('events');
        if (!current()) return;
        $('calendarAgenda').textContent = calendarText(result); $('calendarAgenda').hidden = false;
        status.textContent = 'Connected (read-only)';
    }));
    $('calendarDisconnect').addEventListener('click', () => run(async current => {
        const result = await request('disconnect', {});
        onChange();
        if (!current()) return;
        connected = false;
        status.textContent = result.message; $('calendarAgenda').hidden = true;
    }));
    $('calendarRetry').addEventListener('click', open);
    dialog.addEventListener('close', () => { if (!dialog.open) { generation++; ready = false; busy = false; } });
    return { open };
}
