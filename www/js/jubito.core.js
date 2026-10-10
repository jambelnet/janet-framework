// (c) J@mBeL.net 2010-2026, John Ambeliotis. Part of jaNET Framework, licensed under the GNU GPL version 3 or later (see LICENSE).
// Jubito web UI - the application layer that sits on top of the jaNET Framework judo API.
// Plain ES modules, no libraries. See css/app.css for the layout and index.html for the markup.

import { runText, runRawText, runJson, loadInstructionSets, onConnection } from './api.js';
import { createGauge } from './gauge.js';
import { renderResponse, commandResponse, responseAppearance } from './help.js';
import { wireTerminal } from './terminal.js';
import { wireVoice } from './voice.js';
import { resolveIntent } from './intents.js';
import { httpPageUrl } from './transport.js';

const byId = id => document.getElementById(id);
const val = id => byId(id).value;
const setVal = (id, value) => { byId(id).value = value; };

const VIEWS = ['page0', 'page1', 'page2', 'page3'];
const HOME_INTERVAL = 5000;
const GMAIL_INTERVAL = 30000;
const WEATHER_INTERVAL = 60000;
const DEGREE = String.fromCharCode(176);      // the degree sign, written as a code so that no file encoding can damage it

let currentView = 'page0';
let currentSettingsSection = 'settings-group';
let gauges = {};
let instructionSets = [];     // from /api/instructions
let selectedCategory = null;
let terminal;
let assistantBusy = false;
let voice;
const activity = [];
let lastGmailCount = null;
let homePins = [];
let addingToHome = false;
try { const saved = JSON.parse(localStorage.getItem('jubito-home-pins') ?? '[]'); if (Array.isArray(saved)) homePins = [...new Set(saved.filter(id => typeof id === 'string'))]; } catch { }

function addActivity(message) {
    activity.unshift({ message, time: new Date() });
    if (activity.length > 20) activity.pop();
    renderActivity();
}

function renderActivity() {
    const list = byId('activityList');
    list.replaceChildren(...activity.slice(0, 5).map(item => {
        const li = document.createElement('li');
        const text = document.createElement('span'); text.textContent = item.message;
        const time = document.createElement('time'); time.dateTime = item.time.toISOString();
        const minutes = Math.floor((Date.now() - item.time.getTime()) / 60000);
        time.textContent = minutes < 1 ? 'Just now' : minutes + ' min ago'; li.append(text, time); return li;
    }));
    if (!activity.length) { const li = document.createElement('li'); li.className = 'activity-empty'; li.textContent = 'No activity yet.'; list.append(li); }
}

/* ------------------------------------------------------------------ helpers */

function clean(text) {
    return (text ?? '').toString().trim();
}

function option(value, text) {
    const o = document.createElement('option');
    o.value = value;
    o.textContent = text;
    return o;
}

function fillSelect(id, options, blank = true) {
    const select = byId(id);
    select.replaceChildren(...(blank ? [option(' ', '')] : []), ...options.map(([v, t]) => option(v, t)));
}

let toastTimer;
function toast(message) {
    const el = byId('toast');
    el.textContent = message;
    el.hidden = false;
    clearTimeout(toastTimer);
    toastTimer = setTimeout(() => { el.hidden = true; }, 3500);
}

function showResult(text, title = 'Response', command = '') {
    const help = renderResponse(byId('responseText'), text, command);
    byId('responseTitle').textContent = help ? 'Command help' : title;
    const dialog = byId('responseDialog');
    dialog.classList.toggle('help-dialog', help);
    if (!dialog.open) dialog.showModal();
}

function openDialog(id) {
    const dialog = byId(id);
    if (!dialog.open) dialog.showModal();
}

function closeDialog(el) {
    el?.closest('dialog')?.close();
}

// Runs a command and reports failures instead of throwing.
async function send(cmd) {
    try {
        return await runText(cmd);
    } catch {
        toast('The request failed. Is jaNET still running?');
        return null;
    }
}

async function sendAndShow(cmd) {
    const data = await send(cmd);
    if (data !== null) showResult(data, 'Response', cmd);
    return data;
}

const isNumeric = s => s !== '' && !Number.isNaN(parseFloat(s)) && Number.isFinite(Number(s));

/* ------------------------------------------------------------------ theme */

const THEME_KEY = 'jubito-theme';
const THEMES = ['auto', 'light', 'dark'];
const THEME_TEXT = {
    auto: 'follows the system',
    light: 'light',
    dark: 'dark'
};

function currentTheme() {
    const stored = document.documentElement.dataset.theme;
    return THEMES.includes(stored) ? stored : 'auto';
}

function applyTheme(theme) {
    if (theme === 'auto') delete document.documentElement.dataset.theme;
    else document.documentElement.dataset.theme = theme;

    try {
        if (theme === 'auto') localStorage.removeItem(THEME_KEY);
        else localStorage.setItem(THEME_KEY, theme);
    } catch { /* private mode: the choice just lasts until the page is closed */ }

    const button = byId('themeBtn');
    button.title = 'Theme: ' + THEME_TEXT[theme] + ' (click to change)';
    button.setAttribute('aria-label', 'Theme: ' + THEME_TEXT[theme] + '. Click to change.');
}

function nextTheme() {
    applyTheme(THEMES[(THEMES.indexOf(currentTheme()) + 1) % THEMES.length]);
}

/* ------------------------------------------------------------------ routing */

function updateNavigation() {
    document.querySelectorAll('nav [data-nav], nav [data-section]').forEach(a => {
        const selected = a.dataset.section
            ? currentView === 'page3' && a.dataset.section === currentSettingsSection
            : a.dataset.nav === currentView;
        if (selected) a.setAttribute('aria-current', 'page');
        else a.removeAttribute('aria-current');
    });
}

function selectSettingsSection(id) {
    currentSettingsSection = id;
    byId('page3').querySelectorAll('details.group').forEach(group => { group.open = group.id === id; });
    updateNavigation();
}

function show(id, section = currentSettingsSection) {
    currentView = id;
    for (const view of VIEWS) byId(view).hidden = view !== id;
    if (id === 'page3') selectSettingsSection(section);
    else updateNavigation();
    window.scrollTo(0, 0);
    if (id === 'page2' && matchMedia('(pointer: fine)').matches) byId('textinput1').focus();
    refresh();
}

function route() {
    document.querySelectorAll('dialog[open]').forEach(d => d.close());
    const id = location.hash.slice(1);
    show(VIEWS.includes(id) ? id : 'page0');
}

/* ------------------------------------------------------------------ polling */

function refresh() {
    if (document.hidden) return;
    voice?.refreshMute();
    if (currentView === 'page0') refreshHome();
    else if (currentView === 'page1') refreshReferences();
    else if (currentView === 'page3') refreshSettingsState();
}

async function refreshHome() {
    runJson('%day%&%date%&%calendaryear%&%time24%').then(data => {
        byId('header').hidden = false;
        byId('time').textContent = data.time24.Value;
        const hour = Number(data.time24.Value.split(':')[0]);
        document.querySelector('.tile-time').dataset.period = hour >= 6 && hour < 20 ? 'day' : 'night';
        byId('date').textContent = data.day.Value + '\n' + data.date.Value + ', ' + data.calendaryear.Value;

    }).catch(() => {});

    runJson('%salute%&%whoami%&%whereami%').then(data => {
        byId('greeting').textContent = 'Good ' + data.salute.Value + ', ' + data.whoami.Value;
        const presence = data.whereami.Value.trim().toLowerCase();
        byId('presenceText').textContent = 'Your status is set to ' + data.whereami.Value;
        byId('userstat').dataset.presence = presence;
    }).catch(() => {});

    // Indoor widget
    // If you have a temperature/humidity sensor attached to your arduino, see the tutorial below:
    // http://jubitoblog.blogspot.com/2014/06/arduino-temperature-and-humidity-using.html
    runJson('judo%20serial%20send%20dhttemp&judo%20serial%20send%20humid').then(data => {
        const temp = data.judo_serial_send_dhttemp.Value;
        if (isNumeric(temp)) {
            const humid = data.judo_serial_send_humid.Value;
            byId('indoordiv').hidden = false;
            byId('indoor').textContent = 'Indoor: ' + temp + DEGREE + 'C ' + humid + '%';
            gauges.indoortemp.refresh(temp);
            gauges.indoorhumid.refresh(humid);
        } else byId('indoordiv').hidden = true;
    }).catch(() => {});
}

async function refreshGmail() {
    try {
        const data = await runJson('%gmailcount%&%gmailreader%');
        const count = clean(data.gmailcount.Value);
        const badge = byId('gmail-badge');
        const failed = !/^\d+$/.test(count);
        badge.textContent = failed ? '!' : count;
        badge.title = failed ? count : count + ' unread messages';
        badge.setAttribute('aria-label', badge.title);
        if (!failed && lastGmailCount !== null && Number(count) > lastGmailCount) addActivity('New email received');
        if (!failed) lastGmailCount = Number(count);
        byId('gmailreader').textContent = data.gmailreader.Value;
    } catch {
        const badge = byId('gmail-badge');
        badge.textContent = '!';
        badge.title = 'Gmail could not be checked. jaNET is unreachable.';
        badge.setAttribute('aria-label', badge.title);
        byId('gmailreader').textContent = badge.title;
    }
}

async function refreshWeather() {
    try {
        const data = await runJson('%currentcity%&%todayconditions%&%weathericon%&%currenttemperature%&%currenthumidity%&%currentpressure%');
        if (data.currenttemperature.Value.length <= 0) {
            setWeatherScene('');
            byId('conditions').textContent = 'Unavailable';
            byId('location').hidden = true;
            byId('weather-ico').hidden = true;
            byId('weatherdiv').hidden = true;
        } else {
            setWeatherScene(data.weathericon.Value);
            byId('conditions').textContent = data.currenttemperature.Value + DEGREE + 'C';
            const loc = byId('location');
            loc.hidden = false;
            loc.textContent = data.currentcity.Value + ', ' + data.todayconditions.Value;
            const ico = byId('weather-ico');
            ico.hidden = false;
            const img = document.createElement('img');
            img.src = data.weathericon.Value;
            img.alt = data.todayconditions.Value;
            ico.replaceChildren(img);
            byId('weatherdiv').hidden = false;
            gauges.curtemp.refresh(data.currenttemperature.Value);
            gauges.curhumid.refresh(data.currenthumidity.Value);
            gauges.curpres.refresh(data.currentpressure.Value);
        }
    } catch {
        setWeatherScene('');
        byId('conditions').textContent = 'Unavailable'; byId('location').hidden = true;
        byId('weather-ico').hidden = true; byId('weatherdiv').hidden = true;
    }
}

function setWeatherScene(iconUrl) {
    const code = String(iconUrl).match(/\/(\d{2})([dn])(?:@2x)?\.png(?:\?.*)?$/);
    const tile = byId('weathertile');
    const scenes = { '01': ['clear', 0, 0], '02': ['clouds', 100, 0], '03': ['clouds', 100, 0], '04': ['overcast', 0, 1],
        '09': ['rain', 100, 1], '10': ['rain', 100, 1], '11': ['storm', 0, 2], '13': ['snow', 100, 2], '50': ['fog', 0, 3] };
    let scene = code ? scenes[code[1]] : null;
    if (code?.[1] === '01' && code[2] === 'n') scene = ['night', 100, 3];
    tile.dataset.scene = scene?.[0] ?? 'unavailable';
    tile.dataset.night = String(code?.[2] === 'n');
    tile.style.setProperty('--sky-x', (scene?.[1] ?? 0) + '%');
    tile.style.setProperty('--sky-y', ((scene?.[2] ?? 0) * 100 / 3) + '%');
}

async function refreshReferences() {
    const values = new Map();
    for (const el of document.querySelectorAll('#customul [data-ref], #homePins [data-ref]')) {
        if (!values.has(el.dataset.ref)) values.set(el.dataset.ref, await send('{mute}' + el.dataset.ref).catch(() => null));
        const text = values.get(el.dataset.ref);
        if (text !== null) el.textContent = text;
    }
}

async function refreshSettingsState() {
    send('judo serial state').then(d => { if (d !== null) byId('sliderSerial').checked = d.includes('Serial port state: True'); });
    send('judo socket state').then(d => { if (d !== null) byId('sliderSocket').checked = d.includes('Socket state: True'); });
    send('%about%&%uptime%').then(d => { if (d !== null) byId('sysinfo').textContent = d.replace('Days', 'Uptime: Days'); });
}

function startPolling() {
    setInterval(refresh, HOME_INTERVAL);
    setInterval(renderActivity, 60000);
    setInterval(() => { if (!document.hidden) refreshGmail(); }, GMAIL_INTERVAL);
    setInterval(() => { if (!document.hidden) refreshWeather(); }, WEATHER_INTERVAL);
    document.addEventListener('visibilitychange', () => {
        if (!document.hidden) { refresh(); refreshWeather(); refreshGmail(); }
    });
}

/* ------------------------------------------------------------------ gauges */

function createGauges() {
    const levelColors = ['#0000ff', '#00ff00', '#ff0000'];
    gauges = {
        curtemp: createGauge(byId('curtemp'), { title: 'Temperature', label: 'Celsius', min: -20, max: 50, decimals: true, levelColors }),
        curhumid: createGauge(byId('curhumid'), { title: 'Humidity', label: '%', min: 0, max: 100, levelColors }),
        curpres: createGauge(byId('curpres'), { title: 'Pressure', label: 'hPa', min: 900, max: 1150, decimals: true, levelColors }),
        indoortemp: createGauge(byId('indoortemp'), { title: 'Temperature', label: 'Celsius', min: -20, max: 50, levelColors }),
        indoorhumid: createGauge(byId('indoorhumid'), { title: 'Humidity', label: '%', min: 0, max: 100, levelColors })
    };
}

/* ------------------------------------------------------------------ dashboard */

async function loadXml() {
    let list;
    try {
        list = await loadInstructionSets();
    } catch {
        return;
    }

    instructionSets = list.map(item => ({
        id: item.id ?? '',
        categ: item.categ,
        header: item.header,
        shortdescr: clean(item.shortdescr),
        descr: clean(item.descr),
        img: clean(item.img),
        ref: item.ref
    }));

    const launchers = instructionSets.filter(s => s.id.startsWith('*')).map(s => [s.id, s.id.replace('*', '')]);
    const plain = instructionSets.filter(s => s.id !== '' && !s.id.includes('*')).map(s => [s.id, s.id]);

    fillSelect('insetlistAsterisk', launchers, false);
    fillSelect('insetlistReference', launchers);
    fillSelect('insetlistThen', plain);
    fillSelect('insetlistElse', plain);
    fillSelect('scheduleAction', plain);

    const categories = [...new Set(instructionSets.filter(s => s.categ).map(s => s.categ))];
    const select = byId('ddCategories');
    if (!categories.includes(selectedCategory)) selectedCategory = categories[0] ?? null;
    select.replaceChildren(...categories.map(c => option(c, c)));
    if (selectedCategory !== null) select.value = selectedCategory;

    renderCards();
    renderHomePins();
    refreshReferences();
}

function renderCards() {
    const filter = byId('filter-input').value.trim().toLowerCase();
    const list = byId('customul');
    const items = instructionSets.filter(s =>
        s.header !== null && !s.id.includes('*') && s.categ === selectedCategory &&
        (filter === '' || (s.header + ' ' + s.shortdescr + ' ' + s.descr).toLowerCase().includes(filter)));

    list.replaceChildren(...items.map(instructionCard));
    byId('customul-empty').hidden = items.length > 0;
}

function instructionCard(s) {
        const li = document.createElement('li');
        const button = document.createElement('button');
        button.type = 'button';
        button.className = 'card';
        button.addEventListener('click', () => runCommand(s.id));

        if (s.img) {
            const img = document.createElement('img');
            img.src = s.img;
            img.alt = '';
            img.loading = 'lazy';
            button.append(img);
        } else {
            const icon = document.createElementNS('http://www.w3.org/2000/svg', 'svg'); icon.classList.add('noimg'); icon.setAttribute('aria-hidden', 'true');
            const use = document.createElementNS('http://www.w3.org/2000/svg', 'use'); use.setAttribute('href', '#i-dashboard'); icon.append(use); button.append(icon);
        }

        const body = document.createElement('span');
        const h = document.createElement('h3');
        h.textContent = s.header || s.id;
        const p1 = document.createElement('p');
        const strong = document.createElement('strong');
        strong.textContent = s.shortdescr;
        p1.append(strong);
        const p2 = document.createElement('p');
        p2.textContent = s.descr;
        body.append(h, p1, p2);
        button.append(body);

        const value = document.createElement('span');
        value.className = 'value';
        if (s.ref) value.dataset.ref = s.ref;
        button.append(value);

        li.append(button);
        return li;
}

function renderHomePins() {
    const items = homePins.map(id => instructionSets.find(item => item.id === id)).filter(Boolean);
    byId('homePins').replaceChildren(...items.map(instructionCard));
    byId('homePins').hidden = !items.length;
}

function saveHomePins(next) {
    try { localStorage.setItem('jubito-home-pins', JSON.stringify(next)); }
    catch { toast('Home changes could not be saved. Allow local browser storage.'); return false; }
    homePins = next; renderHomePins(); refreshReferences(); return true;
}

function renderHomeOptions() {
    const filter = val('homeFilter').trim().toLowerCase();
    const items = instructionSets.filter(item => item.id && !item.id.startsWith('*') &&
        [item.id, item.header, item.categ].join(' ').toLowerCase().includes(filter));
    byId('homePinOptions').replaceChildren(...items.map(item => {
        const label = document.createElement('label');
        const input = document.createElement('input'); input.type = 'checkbox'; input.checked = homePins.includes(item.id);
        input.addEventListener('change', () => {
            if (!saveHomePins(input.checked ? [...homePins, item.id] : homePins.filter(id => id !== item.id))) input.checked = !input.checked;
        });
        const name = document.createElement('span'); name.textContent = item.header ? item.header + ' (' + item.id + ')' : item.id;
        label.append(input, name); return label;
    }));
    byId('homePinEmpty').hidden = items.length > 0;
}

/* ------------------------------------------------------------------ commands */

// Runs a command; the answer goes to the terminal on its page and to a dialog everywhere else.
async function runCommand(cmd) {
    if (!cmd) return;
    let command = cmd;
    try { command = decodeURIComponent(cmd); } catch { }
    if (currentView === 'page2') {
        return terminal.execute(command, () => runText(cmd));
    }
    const data = await send(cmd);
    if (data === null) return;
    await voice.refreshMute();
    if (cmd === '%checkin%' || cmd === encodeURIComponent('%checkin%')) addActivity('Checked in');
    else if (cmd === '%checkout%' || cmd === encodeURIComponent('%checkout%')) addActivity('Checked out');

    if (data !== '') {
        showResult(data, 'Response', cmd);
    } else if (currentView !== 'page0') {
        showResult(commandResponse(data, command), 'Response', cmd);
    } else {
        toast(commandResponse(data, command));
    }
    if (currentView === 'page0') refreshHome();
}

async function askJubito(event) {
    event.preventDefault();
    if (assistantBusy) return;
    const input = val('askInput').trim();
    if (!input) return;
    const match = resolveIntent(input, instructionSets);
    const choices = byId('intentChoices'); choices.replaceChildren(); choices.hidden = true;
    const response = byId('askResponse'); response.hidden = false;
    voice.stop();
    if (!match.command) {
        voice.clearResponse();
        renderResponse(response, match.suggestions.length ? 'Which command did you mean?' : 'No matching command found.', '', { state: match.suggestions.length ? 'choice' : 'error' });
        choices.replaceChildren(...match.suggestions.map(item => {
            const button = document.createElement('button'); button.type = 'button'; button.className = 'btn';
            button.textContent = item.label + (match.suggestions.filter(other => other.label === item.label).length > 1 ? ' (' + item.command + ')' : '');
            button.title = 'Run ' + item.command;
            button.addEventListener('click', () => { choices.hidden = true; executeAssistant(item.command); }); return button;
        }));
        choices.hidden = !match.suggestions.length; return;
    }
    await executeAssistant(match.command);
}

async function executeAssistant(command) {
    if (assistantBusy) return;
    assistantBusy = true;
    const submitted = val('askInput');
    const response = byId('askResponse'); response.hidden = false;
    voice.clearResponse();
    const button = byId('askForm').querySelector('[type="submit"]'); button.disabled = true;
    renderResponse(response, 'Running your command...', '', { state: 'busy' });
    try {
        const text = await runRawText('{mute}' + command);
        const output = commandResponse(text, command);
        renderResponse(response, output, command);
        if (responseAppearance(output).state !== 'error') {
            if (val('askInput') === submitted) setVal('askInput', '');
            addActivity('Completed: ' + command);
        }
        await voice.response(output);
        if (command.includes('checkin') || command.includes('checkout')) refreshHome();
    } catch (error) { const message = 'jaNET could not complete the request.'; renderResponse(response, message, '', { state: 'error' }); toast(message); }
    finally { button.disabled = false; assistantBusy = false; }
}

function weatherFields() {
    const meteo = val('weatherProvider') === 'openmeteo';
    byId('weatherLegacyFields').hidden = meteo; byId('weatherMeteoFields').hidden = !meteo;
    byId('weatherURI').required = !meteo;
    ['weatherLatitude', 'weatherLongitude', 'weatherLocation'].forEach(id => { byId(id).required = meteo; });
}

function weatherCredit(endpoint) {
    const meteo = endpoint.includes('api.open-meteo.com/');
    const link = byId('weather-credit'); link.textContent = meteo ? 'Open-Meteo' : 'OpenWeather';
    link.href = meteo ? 'https://open-meteo.com/' : 'https://openweathermap.org/';
}

async function loadWeatherSettings() {
    const endpoint = await send('judo weather settings') ?? '';
    setVal('weatherURI', endpoint); setVal('weatherApiKey', ''); weatherCredit(endpoint);
    try {
        const url = new URL(endpoint);
        setVal('weatherProvider', url.hostname === 'api.open-meteo.com' ? 'openmeteo' : 'legacy');
        if (url.hostname === 'api.open-meteo.com') {
            setVal('weatherLatitude', url.searchParams.get('latitude') ?? '49.6116');
            setVal('weatherLongitude', url.searchParams.get('longitude') ?? '6.1319');
            setVal('weatherLocation', await send('judo weather location') ?? '');
        }
    } catch { setVal('weatherProvider', 'legacy'); }
    weatherFields();
}

function clearPage() {
    terminal.clear();
}

/* ------------------------------------------------------------------ settings forms */

const enc = encodeURIComponent;
const locked = text => '<lock>' + text + '</lock>';

async function saveRaw(command) {
    try { return await runRawText(command); }
    catch { toast('The settings request failed.'); return null; }
}

async function saveMail(command) {
    const result = await saveRaw(command);
    if (result !== null) showResult(result);
    return result === 'Settings saved.';
}

async function loadMailSettings(id) {
    const kind = id.replace('Settings', '');
    const values = (await send('judo ' + kind + ' settings') ?? '').split(/\r?\n/);
    const fields = kind === 'gmail'
        ? ['gmailUsername', 'gmailPassword', null, 'gmailSmtpHost', 'gmailSmtpPort', 'chkGmailSmtpSSL',
            'gmailPop3Host', 'gmailPop3Port', 'chkGmailPop3SSL', 'gmailImapHost', 'gmailImapPort', 'chkGmailImapSSL']
        : [kind + 'Host', kind + 'Username', kind + 'Password', kind + 'Port', 'chk' + (kind === 'smtp' ? 'Smtp' : 'Pop3') + 'SSL'];
    fields.forEach((field, index) => {
        if (!field || values[index] === undefined) return;
        const input = byId(field);
        if (input.type === 'checkbox') input.checked = values[index].toLowerCase() === 'true';
        else if (values[index] !== '0') input.value = values[index];
    });
}
const underscored = text => text.replace(/ /g, '_');

async function reloadAfter(promise) {
    await promise;
    await loadXml();
}

const actions = {
    async sendSMS() {
        await sendAndShow('judo sms send ' + val('phonenumber') + ' `' + val('smsText') + '`');
    },
    async gmailSettings() {
        const saved = await saveMail('judo gmail set ' + locked(val('gmailUsername')) + ' ' + locked(val('gmailPassword')) +
            ' ' + locked('https://mail.google.com/mail/feed/atom') + ' ' + locked(val('gmailSmtpHost')) + ' ' + val('gmailSmtpPort') + ' ' + byId('chkGmailSmtpSSL').checked +
            ' ' + locked(val('gmailPop3Host')) + ' ' + val('gmailPop3Port') + ' ' + byId('chkGmailPop3SSL').checked +
            ' ' + locked(val('gmailImapHost')) + ' ' + val('gmailImapPort') + ' ' + byId('chkGmailImapSSL').checked);
        if (saved) refreshGmail();
        return saved;
    },
    async smtpSettings() {
        return saveMail('judo smtp set ' + locked(val('smtpHost')) + ' ' + locked(val('smtpUsername')) + ' ' + locked(val('smtpPassword')) +
            ' ' + val('smtpPort') + ' ' + byId('chkSmtpSSL').checked);
    },
    async pop3Settings() {
        return saveMail('judo pop3 set ' + locked(val('pop3Host')) + ' ' + locked(val('pop3Username')) + ' ' + locked(val('pop3Password')) +
            ' ' + val('pop3Port') + ' ' + byId('chkPop3SSL').checked);
    },
    async mailheaderSettings() {
        await sendAndShow('judo mailheaders set `' + val('mailFrom') + '` `' + val('mailTo') + '` `' + val('mailSubject') + '`');
    },
    async smsSettings() {
        await sendAndShow('judo sms set ' + val('smsAPI') + ' ' + val('smsUsername') + ' ' + enc(val('smsPassword')));
    },
    async serverLogin() {
        await sendAndShow('judo server login ' + val('serverUsername') + ' ' + enc(val('serverPassword')));
    },
    async serverSettings() {
        await sendAndShow('judo server set ' + val('serverHost') + ' ' + val('serverPort') + ' ' + val('serverAuth'));
    },
    async httpsSettings() {
        const mode = val('httpsMode');
        let httpUrl;
        if (mode === 'off') {
            const settings = await send('judo server settings');
            if (settings === null) return false;
            try { httpUrl = httpPageUrl(window.location.href, settings, Date.now()); }
            catch (error) { toast(error.message); return false; }
        }
        if (mode === 'custom' && !val('httpsCertFile').trim()) { toast('Enter the PFX certificate file.'); return false; }
        const certificate = mode === 'custom' ? ' ' + locked(val('httpsCertFile')) + ' ' + locked(val('httpsCertPassword'))
            : mode === 'default' ? ' default' : '';
        const result = await saveRaw(mode === 'off' ? 'judo server https off' : 'judo server https on ' + val('httpsPort') + certificate);
        if (result === null) return false;
        if (!result.startsWith('HTTPS: ')) { showResult(result); return false; }
        if (mode === 'off') {
            byId('httpsResult').textContent = result;
            const link = byId('httpsLink'); link.href = httpUrl; link.textContent = 'Open HTTP'; link.hidden = false;
            if (window.location.protocol === 'https:') {
                // HTTPS is about to close; use the saved HTTP port, not the retiring listener.
                byId('httpsResult').textContent += '\nOpening the HTTP page...';
                await new Promise(resolve => setTimeout(resolve, 2500));
                window.location.replace(httpUrl);
            }
            return false;
        }
        let status = result;
        if (result.includes('Applying HTTPS settings.')) {
            for (let attempt = 0; attempt < 20; attempt++) {
                await new Promise(resolve => setTimeout(resolve, 250));
                try {
                    status = await runText('judo server https status');
                    if (status.includes('Certificate:')) break;
                } catch { /* listener is restarting */ }
            }
        }
        byId('httpsResult').textContent = status;
        const link = byId('httpsLink');
        link.textContent = 'Open HTTPS';
        link.hidden = !status.includes('Certificate:');
        if (!link.hidden) {
            const url = new URL(window.location.href);
            url.protocol = 'https:';
            url.port = val('httpsPort');
            url.pathname = '/www/';
            url.search = '';
            url.hash = '';
            link.href = url.href;
        }
        return false;
    },
    async socketSettings() {
        await sendAndShow('judo socket set ' + val('socketHost') + ' ' + val('socketPort'));
    },
    async trustedSettings() {
        await sendAndShow('judo socket trust <lock>' + val('trusted') + '</lock>');
        await loadTrusted();
    },
    async serialSettings() {
        await sendAndShow('judo serial set ' + val('serialPort') + ' ' + val('serialBaud'));
    },
    async weatherSettings() {
        const meteo = val('weatherProvider') === 'openmeteo';
        let result;
        if (meteo) {
            result = await saveRaw('judo weather openmeteo ' + val('weatherLatitude') + ' ' + val('weatherLongitude') + ' ' + locked(val('weatherLocation')));
        } else {
            const endpoint = val('weatherURI');
            try { if (!['http:', 'https:'].includes(new URL(endpoint).protocol)) throw new Error(); }
            catch { toast('Enter an HTTP or HTTPS weather endpoint.'); return false; }
            if (val('weatherApiKey').trim()) {
                const saved = await saveRaw('judo weather key ' + locked(val('weatherApiKey').trim()));
                if (saved !== 'Settings saved.') { toast(saved ?? 'Weather key could not be saved.'); return false; }
            }
            result = await saveRaw('judo weather set ' + locked(endpoint));
        }
        if (result === null || result.startsWith('Weather:') || result.startsWith('Unable')) { toast(result ?? 'Weather settings could not be saved.'); return false; }
        weatherCredit(meteo ? 'https://api.open-meteo.com/' : val('weatherURI'));
        addActivity('Weather settings updated'); await refreshWeather(); toast('Weather settings saved.');
    },
    async dyndnsSettings() {
        await sendAndShow('judo noip set ' + val('dyndnsHostname') + ' ' + val('dyndnsUsername') + ' ' + enc(val('dyndnsPassword')));
    },

    async saveSchedule() {
        const scheduleID = underscored(val('scheduleName'));
        const period = val('schedulePeriod');
        let date;
        let time;

        if (period === 'date') {
            date = scheduleDateValue();
            time = val('scheduleTime');
            if (date === '') { toast('Pick a date first.'); return false; }
        } else if (period === 'repeat') {
            date = 'repeat';
            time = val('scheduleRepeat');
        } else {
            date = period;
            time = val('scheduleTime');
        }

        const action = val('scheduleOther') !== '' ? val('scheduleOther') : val('scheduleAction');
        await sendAndShow('judo schedule add ' + scheduleID + ' ' + date + ' ' + time + ' `' + action + '`');
        resetScheduleForm();
        await enumScheduleNames();
    },

    async saveLauncher() {
        const id = underscored(val('launcherName'));
        await reloadAfter(sendAndShow('judo inset add ' + id + ' <lock>' + val('launcherAction') + '</lock>'));
    },
    async saveEvent() {
        const id = underscored(val('eventName'));
        await reloadAfter(sendAndShow('judo event add ' + id + ' <lock>' + val('eventAction') + '</lock>'));
    },
    async saveEvaluator() {
        const id = underscored(val('insetName4Eval'));
        const data = await send('judo inset add ' + id + ' <lock>' + val('insetAction4Eval') + '</lock>');
        if (data === null) return false;
        if (data.includes('Element added.')) clearEvaluatorFields();
        toast(data);
        await loadXml();
    },
    async saveWebService() {
        const id = underscored(val('wsName'));
        const judo = byId('radio-json').checked ? 'judo json add' : 'judo xml add';
        const withNamespace = val('wsNamespace') !== '' ? ' `' + val('wsNamespace') + '`' : '';
        const data = await send(judo + ' ' + id + ' <lock>' + enc(val('wsEndpoint')) + '</lock>' + withNamespace + ' `' + val('wsNode') + '` `' + val('wsIndex') + '`');
        if (data === null) return false;
        if (data.includes('Element added.')) clearWsFields();
        showResult(data);
        await loadXml();
    },
    async saveInset() {
        const id = underscored(val('insetName'));
        if (instructionSets.some(item => item.id === id || item.id === '*' + id)) { toast('An instruction with that name already exists. Choose a unique name.'); return false; }
        let cmd;
        if (val('insetCateg') !== '' && val('insetHeader') !== '') {
            cmd = 'judo inset add ' + id + ' <lock>' + val('insetAction') + '</lock> `' + val('insetCateg') + '` `' + val('insetHeader') +
                '` `' + val('insetShortDescr') + '` `' + val('insetDescr') + '` `' + val('insetThumbnail') + '` `' + val('insetlistReference').replace('*', '') + '`';
        } else {
            cmd = 'judo inset add ' + id + ' <lock>' + val('insetAction') + '</lock>';
        }
        const data = await send(cmd);
        if (data === null) return false;
        const added = data.includes('Element added.');
        if (added) clearInsetFields();
        toast(data);
        await loadXml();
        if (!added) return false;
        if (addingToHome) { saveHomePins([...new Set([...homePins, id])]); addingToHome = false; }
    },
    async removeElement() {
        const id = underscored(val('elementNameRemove'));
        let cmd;
        let message;

        if (byId('chkEvent').checked) {
            cmd = 'judo inset remove ' + id + '& judo event remove ' + id;
            message = 'Elements removed.';
        } else {
            cmd = 'judo inset remove ' + id;
            message = 'Element removed.';
        }
        const data = await send(cmd);
        if (data === null) return false;
        showResult(message);
        await loadXml();
    }
};

async function viewSettings(area) {
    await sendAndShow('judo ' + area + ' settings');
}

async function loadTrusted() {
    const data = await send('judo trusted settings');
    if (data !== null) setVal('trusted', data);
}

/* ------------------------------------------------------------------ scheduler */

async function enumScheduleNames() {
    const data = await send('judo schedule name-list');
    const names = (data ?? '').split(/\r?\n/).filter(n => n.trim() !== '');
    byId('ddScheduleNames').replaceChildren(option('', ''), ...names.map(n => option(n, n)));
}

async function changeScheduleStatus(status) {
    const name = val('ddScheduleNames');
    if (name === '') { toast('Choose a schedule first.'); return; }
    await sendAndShow('judo schedule ' + status + ' ' + name);
    await enumScheduleNames();
}

async function scheduleAction(action) {
    await sendAndShow('judo schedule ' + action);
    await enumScheduleNames();
}

// d-m-Y as expected by the scheduler; "Today" is resolved by the server (%calendardate%)
let dateIsToday = false;

function scheduleDateValue() {
    if (dateIsToday) return '%calendardate%';
    const iso = val('scheduleDate');
    if (iso === '') return '';
    const [y, m, d] = iso.split('-');
    return d + '-' + m + '-' + y;
}

function updateScheduleDivs() {
    const period = val('schedulePeriod');
    byId('period').hidden = period !== 'repeat';
    byId('specificDate').hidden = period !== 'date';
    byId('time24').hidden = period === 'repeat';
}

function resetScheduleForm() {
    byId('addSchedule').querySelector('form').reset();
    dateIsToday = false;
    updateScheduleDivs();
}

/* ------------------------------------------------------------------ instruction set editors */

const BUILTIN_FUNCTIONS = {
    '%mute%': 'Mute',
    '%unmute%': 'Unmute',
    '%inetcon%': 'Check internet connection',
    '%gmailcount%': 'Count of unread gmail messages',
    '%gmailreader%': 'Get unread gmail sender info & subject',
    '%gmailheaders%': 'Gmail header info',
    '%pop3count%': 'Count of POP3 account',
    '%whoami%': 'Get user login',
    '%checkin%': 'Check-in user',
    '%checkout%': 'Check-out user',
    '%time%': 'Get system time',
    '%time24%': 'Get system time 24h',
    '%hour%': 'Get system hour',
    '%minute%': 'Get system minute',
    '%date%': 'Get system date (e.g. November 5)',
    '%calendardate%': 'Get system date (d/m/yyyy)',
    '%day%': 'Get day (e.g. Friday)',
    '%calendarday%': 'Get calendar day (e.g. 17)',
    '%calendarmonth%': 'Get calendar month (e.g. 11)',
    '%calendaryear%': 'Get calendar year (e.g. ' + new Date().getFullYear() + ')',
    '%salute%': 'Salute in human means (e.g. good morning, good evening, etc)',
    '%partofday%': 'Part of day (e.g. morning, afternoon, etc)',
    '%todayconditions%': 'Current weather conditions (Weather API)',
    '%todaylow%': 'Current low temperature (Weather API)',
    '%todayhigh%': 'Current high temperature (Weather API)',
    '%currenttemperature%': 'Current temperature (Weather API)',
    '%currenthumidity%': 'Current humidity (Weather API)',
    '%currentpressure%': 'Current pressure (Weather API)',
    '%currentcity%': 'Current city (Weather API)',
    '%whereami%': 'Get user status',
    '%uptime%': 'System uptime',
    '%updays%': 'System uptime, days',
    '%uphours%': 'System uptime, hours',
    '%upminutes%': 'System uptime, minutes',
    '%upseconds%': 'System uptime, seconds',
    '%apppath%': 'Application path',
    '%publicip%': 'Get public IP address (from ISP)',
    '%about%': 'About'
};

function addToAction(inputId, listId) {
    const value = val(listId);
    if (value === null || value.trim() === '') return;
    setVal(inputId, val(inputId) + ' ' + value);
}

function addEval() {
    let param1 = val('evalBoolIF').trim();
    let param2 = val('evalBoolTHIS').trim();
    const cond = val('evalBoolCondition');

    if ((cond === '==' || cond === '!=') && !isNumeric(param1) && !isNumeric(param2)) {
        param1 = '"' + param1 + '"';
        param2 = '"' + param2 + '"';
    }
    setVal('insetAction4Eval', val('insetAction4Eval') + ' { evalBool(' + param1 + cond + param2 + '); ' + val('insetlistThen') + '; ' + val('insetlistElse') + '; } ');
}

function clearEvaluatorFields() {
    for (const id of ['insetName4Eval', 'insetAction4Eval', 'evalBoolIF', 'evalBoolTHIS']) setVal(id, '');
    for (const id of ['evalBoolCondition', 'insetlistThen', 'insetlistElse']) byId(id).selectedIndex = 0;
}

function clearWsFields() {
    for (const id of ['wsName', 'wsEndpoint', 'wsNamespace', 'wsNode', 'wsIndex']) setVal(id, '');
}

function clearInsetFields() {
    for (const id of ['insetName', 'insetAction', 'insetCateg', 'insetHeader', 'insetShortDescr', 'insetDescr', 'insetThumbnail']) setVal(id, '');
    for (const id of ['insetlistAsterisk', 'insetlistReference', 'insetlistFunc']) byId(id).selectedIndex = 0;
}

/* ------------------------------------------------------------------ wiring */

function wire() {
    window.addEventListener('hashchange', route);
    byId('themeBtn').addEventListener('click', nextTheme);

    // data-run: run a command; data-open: open a dialog; data-close; data-settings; data-schedule ...
    document.addEventListener('click', async event => {
        const t = event.target;
        if (t instanceof HTMLDialogElement) { t.close(); return; }      // click on the backdrop

        const el = t.closest?.('[data-run],[data-open],[data-close],[data-settings],[data-schedule],[data-schedule-status]');
        if (!el) return;

        if ('run' in el.dataset) runCommand(el.dataset.run);
        else if ('close' in el.dataset) closeDialog(el);
        else if ('settings' in el.dataset) viewSettings(el.dataset.settings);
        else if ('scheduleStatus' in el.dataset) changeScheduleStatus(el.dataset.scheduleStatus);
        else if ('schedule' in el.dataset) scheduleAction(el.dataset.schedule);
        else if ('open' in el.dataset) {
            const id = el.dataset.open;
            if (id === 'changeSchedule') enumScheduleNames();
            if (id === 'popupGmailReader') refreshGmail();
            if (['gmailSettings', 'smtpSettings', 'pop3Settings'].includes(id)) await loadMailSettings(id);
            if (id === 'httpsSettings') {
                const status = await send('judo server https status');
                if (status !== null) {
                    byId('httpsResult').textContent = status;
                    const port = status.match(/:(\d+)\/www\//);
                    if (port) setVal('httpsPort', port[1]);
                    setVal('httpsMode', status.startsWith('HTTPS: off') ? 'off' : 'on');
                }
                byId('httpsLink').hidden = true;
            }
            if (id === 'weatherSettings') await loadWeatherSettings();
            if (id === 'voiceSettings') await voice.loadSettings();
            openDialog(id);
        }
    });

    document.addEventListener('submit', async event => {
        const form = event.target.closest?.('form[data-submit]');
        if (!form) return;
        event.preventDefault();
        const result = await actions[form.dataset.submit]();
        if (result === false) return;                                     // validation message was shown
        if (form.dataset.submit !== 'trustedSettings') form.reset();
        closeDialog(form);
    });

    byId('askForm').addEventListener('submit', askJubito);
    byId('clearActivity').addEventListener('click', () => { activity.length = 0; renderActivity(); });
    byId('customizeHome').addEventListener('click', async () => { await loadXml(); renderHomeOptions(); openDialog('homeSettings'); });
    byId('homeFilter').addEventListener('input', renderHomeOptions);
    byId('newHomeInstruction').addEventListener('click', () => {
        byId('homeSettings').close(); addingToHome = true; openDialog('addInset');
    });
    byId('addInset').addEventListener('close', () => { addingToHome = false; });
    byId('weatherProvider').addEventListener('change', weatherFields);
    document.querySelectorAll('[data-section]').forEach(link => link.addEventListener('click', () => {
        show('page3', link.dataset.section); byId(link.dataset.section).scrollIntoView({ block: 'start' });
    }));
    byId('page3').querySelectorAll('details.group').forEach(group => group.addEventListener('toggle', () => {
        if (group.open) selectSettingsSection(group.id);
    }));

    byId('ddCategories').addEventListener('change', () => { selectedCategory = val('ddCategories'); renderCards(); refreshReferences(); });
    byId('filter-input').addEventListener('input', renderCards);

    byId('sliderSerial').addEventListener('change', () => send(byId('sliderSerial').checked ? 'judo serial open' : 'judo serial close'));
    byId('sliderSocket').addEventListener('change', () => send(byId('sliderSocket').checked ? 'judo socket open' : 'judo socket close'));

    byId('schedulePeriod').addEventListener('change', updateScheduleDivs);
    byId('scheduleDate').addEventListener('input', () => { dateIsToday = false; });
    byId('dateToday').addEventListener('click', () => {
        dateIsToday = true;
        const now = new Date();
        setVal('scheduleDate', now.getFullYear() + '-' + String(now.getMonth() + 1).padStart(2, '0') + '-' + String(now.getDate()).padStart(2, '0'));
    });
    byId('dateClear').addEventListener('click', () => { dateIsToday = false; setVal('scheduleDate', ''); });

    byId('radio-json').addEventListener('change', () => { byId('wsHiddenInputs').hidden = true; });
    byId('radio-xml').addEventListener('change', () => { byId('wsHiddenInputs').hidden = false; });
    byId('clearWsBtn').addEventListener('click', clearWsFields);

    byId('addEvalBtn').addEventListener('click', addEval);
    byId('clearEvalBtn').addEventListener('click', clearEvaluatorFields);
    byId('addAsteriskBtn').addEventListener('click', () => addToAction('insetAction', 'insetlistAsterisk'));
    byId('addFuncBtn').addEventListener('click', () => addToAction('insetAction', 'insetlistFunc'));
    byId('clearInsetBtn').addEventListener('click', clearInsetFields);

    const conn = byId('conn');
    onConnection(online => {
        conn.dataset.state = online ? 'online' : 'offline';
        conn.textContent = online ? 'Connected' : 'Offline';
    });
}

function init() {
    applyTheme(currentTheme());
    createGauges();
    wire();
    voice = wireVoice(text => { setVal('askInput', text); byId('askInput').focus(); addActivity('Voice command transcribed'); }, toast);
    terminal = wireTerminal(runRawText, () => voice.refreshMute());
    addActivity('Interface connected');
    send('judo weather settings').then(endpoint => { if (endpoint) weatherCredit(endpoint); });

    fillSelect('insetlistFunc', Object.entries(BUILTIN_FUNCTIONS), false);
    updateScheduleDivs();
    enumScheduleNames();
    loadTrusted();
    loadXml();

    route();
    refreshWeather();
    refreshGmail();
    startPolling();
}

// Kept for custom widgets and the examples on the Jubito blog, which call into pageObj.
window.pageObj = { runCommand, loadXml, clearPage, getData: refresh, getWeatherData: refreshWeather };

init();
