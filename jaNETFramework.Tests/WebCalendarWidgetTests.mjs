// (c) J@mBeL.net 2010-2026. Part of jaNET Framework, GNU GPL version 3 or later; see LICENSE.
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
const source = await readFile(new URL('../www/js/calendar-widget.js', import.meta.url), 'utf8');
const { calendarRows, wireCalendarWidget } = await import('data:text/javascript;base64,' + Buffer.from(source).toString('base64'));
const event = { summary: 'Home visit', start: '2026-10-14T11:00:00Z', end: '2026-10-14T12:00:00Z', allDay: false, location: 'Living room' };
const agenda = { timeZone: 'Europe/Paris', events: [event] };
const tick = () => new Promise(resolve => setImmediate(resolve));
function fixture(request) {
    function element() { return { textContent: '', hidden: false, disabled: false, children: [], attributes: {}, events: {},
        append(...nodes) { this.children.push(...nodes); }, replaceChildren(...nodes) { this.children = nodes; },
        setAttribute(key, value) { this.attributes[key] = value; }, addEventListener(key, fn) { this.events[key] = fn; } }; }
    const ids = ['homeCalendar', 'homeCalendarEvents', 'homeCalendarStatus', 'homeCalendarRefresh', 'homeCalendarRange'];
    const elements = Object.fromEntries(ids.map(id => [id, element()]));
    let visible = true, clock = 0;
    const widget = wireCalendarWidget({ getElementById: id => elements[id], createElement: element }, request, () => visible, () => clock);
    return { widget, elements, hide: () => { visible = false; }, show: () => { visible = true; }, advance: value => { clock += value; } };
}

test('widget times use saved zone and all-day ranges exclude the end without browser-zone shifts', () => {
    const rows = calendarRows({ ...agenda, events: [event,
        { ...event, start: '2026-10-14', end: '2026-10-15', allDay: true },
        { ...event, start: '2026-10-14', end: '2026-10-17', allDay: true },
        { ...event, start: '2026-10-14T21:00:00Z', end: '2026-10-15T01:00:00Z' }
    ] }, 'en-GB');
    const when = index => rows[index].when.replaceAll(',', '');
    assert.equal(when(0), 'Wed 14 Oct | 13:00 - 14:00');
    assert.equal(when(1), 'Wed 14 Oct | All day');
    assert.equal(when(2), 'Wed 14 Oct - Fri 16 Oct | All day');
    assert.match(when(3), /^Wed 14 Oct \| 23:00 - Thu 15 Oct 0?3:00$/);
    assert.deepEqual([rows[0].weekday, rows[0].day, rows[0].month, rows[0].detail], ['Wed', '14', 'Oct', '13:00 - 14:00']);
    assert.equal(rows[1].detail, 'All day');
    assert.equal(rows[2].detail.replaceAll(',', ''), 'All day | Wed 14 Oct - Fri 16 Oct');
    assert.equal(calendarRows({ timeZone: 'America/Los_Angeles', events: [{ ...event, start: '2026-10-14', end: '2026-10-15', allDay: true }] }, 'en-GB')[0].when, rows[1].when);
    assert.deepEqual(calendarRows({ ...agenda, events: [] }), []);
});

test('widget rejects malformed events and invalid zones instead of displaying an empty agenda', () => {
    for (const bad of [null, {}, { ...agenda, timeZone: 'bad-zone' }, { ...agenda, events: [null] },
        { ...agenda, events: [{ ...event, start: 'bad' }] }, { ...agenda, events: [{ ...event, end: event.start }] },
        { ...agenda, events: [{ ...event, allDay: true }] }]) assert.throws(() => calendarRows(bad));
});

test('date columns follow the saved zone at midnight and keep all-day calendar dates', () => {
    const midnight = { ...event, start: '2026-10-14T23:30:00Z', end: '2026-10-15T01:00:00Z' };
    assert.equal(calendarRows({ ...agenda, events: [midnight] }, 'en-GB')[0].day, '15');
    assert.equal(calendarRows({ timeZone: 'America/Los_Angeles', events: [midnight] }, 'en-GB')[0].day, '14');
    assert.equal(calendarRows({ timeZone: 'America/Los_Angeles', events: [{ ...event, start: '2026-10-14', end: '2026-10-16', allDay: true }] }, 'en-GB')[0].day, '14');
});

test('background reads only settings and events, render text safely and throttle to one minute', async () => {
    const calls = [];
    const f = fixture(async (...args) => { calls.push(args); return args[0] === 'settings' ? { connected: true } : { ...agenda, events: [{ ...event, summary: '<img onerror=bad()>', location: '<script>bad()</script>' }] }; });
    await f.widget.refresh();
    assert.deepEqual(calls, [['settings'], ['events']]);
    const row = f.elements.homeCalendarEvents.children[0];
    assert.equal(row.children[0].attributes.datetime, event.start);
    assert.equal(row.children[0].children[1].textContent, '14');
    assert.equal(row.children[1].children[0].textContent, '<img onerror=bad()>');
    assert.equal(row.children[1].children[2].textContent, '<script>bad()</script>');
    assert(!f.elements.homeCalendarEvents.hidden); assert(f.elements.homeCalendarStatus.hidden);
    await f.widget.refresh(); f.advance(59999); await f.widget.refresh(); assert.equal(calls.length, 2);
    f.advance(1); await f.widget.refresh(); assert.equal(calls.length, 4);
    f.hide(); f.advance(60000); await f.widget.refresh(true); assert.equal(calls.length, 4);
    f.show(); await f.widget.refresh(); assert.equal(calls.length, 6);
    await f.widget.refresh(true); assert.equal(calls.length, 8);
});

test('disconnected, empty and unavailable states are distinct and refresh recovers', async () => {
    let mode = 'connected'; const calls = [];
    const f = fixture(async action => {
        calls.push(action);
        if (mode === 'error') throw new Error('Reconnect in Calendar settings.');
        return action === 'settings' ? { connected: mode !== 'disconnected' } : { ...agenda, events: mode === 'empty' ? [] : [event] };
    });
    await f.widget.refresh(); assert.equal(f.elements.homeCalendarEvents.children.length, 1);
    mode = 'disconnected'; await f.widget.invalidate();
    assert.equal(calls.at(-1), 'settings'); assert.equal(f.elements.homeCalendarEvents.children.length, 0);
    assert(f.elements.homeCalendarStatus.textContent.includes('not connected'));
    mode = 'empty'; await f.widget.invalidate(); assert(f.elements.homeCalendarStatus.textContent.includes('No events'));
    mode = 'error'; await f.widget.refresh(true); assert(f.elements.homeCalendarStatus.textContent.includes('Calendar unavailable'));
    assert(!f.elements.homeCalendarRefresh.disabled); assert.equal(f.elements.homeCalendar.attributes['aria-busy'], 'false');
    mode = 'connected'; f.elements.homeCalendarRefresh.events.click(); await tick(); assert.equal(f.elements.homeCalendarEvents.children.length, 1);
});

test('overlapping polls are deduplicated and invalidation prevents late events restoring disconnected data', async () => {
    let release, connected = true; const calls = [];
    const f = fixture(async action => { calls.push(action); return action === 'settings' ? { connected } : new Promise(resolve => { release = resolve; }); });
    const first = f.widget.refresh(); await tick();
    await f.widget.refresh(true); assert.deepEqual(calls, ['settings', 'events']);
    connected = false; await f.widget.invalidate();
    release(agenda); await first;
    assert(f.elements.homeCalendarStatus.textContent.includes('not connected'));
    assert.equal(f.elements.homeCalendarEvents.children.length, 0); assert(!f.elements.homeCalendarRefresh.disabled);
});

test('synchronous request failures do not leave the widget permanently busy', async () => {
    const f = fixture(() => { throw new Error('Offline'); });
    await f.widget.refresh(); await f.widget.refresh(true);
    assert(f.elements.homeCalendarStatus.textContent.includes('Offline')); assert(!f.elements.homeCalendarRefresh.disabled);
});
