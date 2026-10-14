// (c) J@mBeL.net 2010-2026. Part of jaNET Framework, GNU GPL version 3 or later; see LICENSE.
export function calendarRows(agenda, locale) {
    if (!Array.isArray(agenda?.events) || typeof agenda.timeZone !== 'string') throw new Error('Invalid Calendar response.');
    const date = new Intl.DateTimeFormat(locale, { timeZone: agenda.timeZone, weekday: 'short', month: 'short', day: 'numeric' });
    const time = new Intl.DateTimeFormat(locale, { timeZone: agenda.timeZone, hour: 'numeric', minute: '2-digit' });
    const day = new Intl.DateTimeFormat(locale, { timeZone: 'UTC', weekday: 'short', month: 'short', day: 'numeric' });
    return agenda.events.map(event => {
        if (typeof event.summary !== 'string' || typeof event.start !== 'string' || typeof event.end !== 'string' ||
            typeof event.allDay !== 'boolean' || typeof event.location !== 'string') throw new Error('Invalid Calendar event.');
        const start = new Date(event.start), end = new Date(event.end);
        if (!Number.isFinite(start.getTime()) || !Number.isFinite(end.getTime()) || end <= start) throw new Error('Invalid Calendar event dates.');
        let when, detail;
        if (event.allDay) {
            if (!/^\d{4}-\d{2}-\d{2}$/.test(event.start) || !/^\d{4}-\d{2}-\d{2}$/.test(event.end)) throw new Error('Invalid all-day event dates.');
            // Google all-day end dates are exclusive; date-only values must not shift with the browser's zone.
            const lastDay = new Date(end.getTime() - 86400000);
            when = day.format(start) + (lastDay > start ? ' - ' + day.format(lastDay) : '') + ' | All day';
            detail = 'All day' + (lastDay > start ? ' | ' + day.format(start) + ' - ' + day.format(lastDay) : '');
        } else {
            detail = time.format(start) + ' - ' +
                (date.format(start) !== date.format(end) ? date.format(end) + ' ' : '') + time.format(end);
            when = date.format(start) + ' | ' + detail;
        }
        const parts = Object.fromEntries((event.allDay ? day : date).formatToParts(start).map(part => [part.type, part.value]));
        return { summary: event.summary || 'Untitled event', when, detail, location: event.location,
            start: event.start, dateLabel: (event.allDay ? day : date).format(start), weekday: parts.weekday, day: parts.day, month: parts.month };
    });
}

export function wireCalendarWidget(document, request, visible, now = Date.now) {
    const $ = id => document.getElementById(id);
    const widget = $('homeCalendar'), list = $('homeCalendarEvents'), status = $('homeCalendarStatus'), refreshButton = $('homeCalendarRefresh');
    let generation = 0, pending = null, lastAttempt = -Infinity;
    function clear() { list.replaceChildren(); list.hidden = true; $('homeCalendarRange').textContent = 'Next 7 days'; }
    function busy(value) { widget.setAttribute('aria-busy', String(value)); refreshButton.disabled = value; }
    async function refresh(force = false) {
        if (!visible() || pending || !force && now() - lastAttempt < 60000) return;
        const current = generation;
        lastAttempt = now(); busy(true);
        status.textContent = 'Loading upcoming events...'; status.hidden = false;
        // Only structured, read-only endpoints: background updates must never execute instructions.
        const operation = (async () => {
            try {
                const settings = await request('settings');
                if (current !== generation) return;
                if (typeof settings?.connected !== 'boolean') throw new Error('Invalid Calendar settings response.');
                if (!settings.connected) { clear(); status.textContent = 'Google Calendar is not connected.'; return; }
                const agenda = await request('events');
                if (current !== generation) return;
                const rows = calendarRows(agenda);
                const items = rows.map(row => {
                    const item = document.createElement('li');
                    const date = document.createElement('time'); date.className = 'calendar-event-date';
                    date.setAttribute('datetime', row.start); date.setAttribute('aria-label', row.dateLabel);
                    for (const [tag, text] of [['span', row.weekday], ['strong', row.day], ['span', row.month]]) {
                        const part = document.createElement(tag); part.textContent = text; date.append(part);
                    }
                    const details = document.createElement('div'); details.className = 'calendar-event-details';
                    const when = document.createElement('p'); when.className = 'calendar-event-time'; when.textContent = row.detail;
                    const title = document.createElement('p'); title.className = 'calendar-event-title'; title.textContent = row.summary;
                    details.append(title, when);
                    if (row.location) { const location = document.createElement('p'); location.className = 'calendar-event-location'; location.textContent = row.location; details.append(location); }
                    item.append(date, details);
                    return item;
                });
                list.replaceChildren(...items); list.hidden = !items.length;
                $('homeCalendarRange').textContent = 'Next 7 days | ' + agenda.timeZone;
                status.textContent = items.length ? '' : 'No events in the next 7 days.'; status.hidden = !!items.length;
            } catch (error) {
                if (current !== generation) return;
                clear(); status.textContent = 'Calendar unavailable. ' + error.message; status.hidden = false;
            }
        })();
        pending = operation;
        try { await operation; }
        finally { if (current === generation) { pending = null; busy(false); } }
    }
    function invalidate() {
        generation++; pending = null; lastAttempt = -Infinity;
        clear(); busy(false); status.textContent = 'Loading upcoming events...'; status.hidden = false;
        return refresh();
    }
    refreshButton.addEventListener('click', () => refresh(true));
    globalThis.lucide?.createIcons();
    return { refresh, invalidate };
}
