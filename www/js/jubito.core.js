// (c) J@mBeL.net 2010-2026, John Ambeliotis. Part of jaNET Framework, licensed under the GNU GPL version 3 or later (see LICENSE).
// Jubito web UI - the application layer that sits on top of the jaNET Framework judo API.
// Plain ES modules, no libraries. See css/app.css for the layout and index.html for the markup.

import { runText, runJson, loadInstructionSets, onConnection } from './api.js';
import { createGauge } from './gauge.js';

const byId = id => document.getElementById(id);
const val = id => byId(id).value;
const setVal = (id, value) => { byId(id).value = value; };

const VIEWS = ['page0', 'page1', 'page2', 'page3'];
const HOME_INTERVAL = 5000;
const GMAIL_INTERVAL = 30000;
const WEATHER_INTERVAL = 60000;
const DEGREE = String.fromCharCode(176);      // the degree sign, written as a code so that no file encoding can damage it

let currentView = 'page0';
let gauges = {};
let instructionSets = [];     // from /api/instructions
let selectedCategory = null;
const cmdHistory = [];        // terminal command history
let historyIndex = 0;

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

function showResult(text, title = 'Response') {
    byId('responseTitle').textContent = title;
    byId('responseText').textContent = text;
    const dialog = byId('responseDialog');
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
    if (data !== null) showResult(data);
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

function show(id) {
    currentView = id;
    for (const view of VIEWS) byId(view).hidden = view !== id;
    document.querySelectorAll('[data-nav]').forEach(a => {
        if (a.dataset.nav === id) a.setAttribute('aria-current', 'page');
        else a.removeAttribute('aria-current');
    });
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
    if (currentView === 'page0') refreshHome();
    else if (currentView === 'page1') refreshReferences();
    else if (currentView === 'page3') refreshSettingsState();
}

async function refreshHome() {
    runJson('%day%&%date%&%calendaryear%&%time24%').then(data => {
        byId('header').hidden = false;
        byId('time').textContent = data.time24.Value;
        byId('date').textContent = data.day.Value + '\n' + data.date.Value + ', ' + data.calendaryear.Value;

    }).catch(() => {});

    runJson('%salute%&%whoami%&%whereami%').then(data => {
        byId('userstat').textContent =
            'Good ' + data.salute.Value + ' ' + data.whoami.Value + '\nYour status is set to ' + data.whereami.Value;
    }).catch(() => {});

    // Indoor widget
    // If you have a temperature/humidity sensor attached to your arduino, see the tutorial below:
    // http://jubitoblog.blogspot.com/2014/06/arduino-temperature-and-humidity-using.html
    runJson('judo%20serial%20send%20dhttemp&judo%20serial%20send%20humid').then(data => {
        const temp = data.judo_serial_send_dhttemp.Value;
        if (temp !== 'Serial port state: False') {
            const humid = data.judo_serial_send_humid.Value;
            byId('indoordiv').hidden = false;
            byId('indoor').textContent = 'Indoor: ' + temp + DEGREE + 'C ' + humid + '%';
            gauges.indoortemp.refresh(temp);
            gauges.indoorhumid.refresh(humid);
        }
    }).catch(() => {});
}

async function refreshGmail() {
    try {
        const data = await runJson('%gmailcount%&%gmailreader%');
        const count = clean(data.gmailcount.Value);
        const badge = byId('gmail-badge');
        badge.textContent = count === '0' ? '' : count;
        byId('gmailreader').textContent = data.gmailreader.Value;
    } catch { /* offline indicator is handled by the api module */ }
}

async function refreshWeather() {
    try {
        const data = await runJson('%currentcity%&%todayconditions%&%weathericon%&%currenttemperature%&%currenthumidity%&%currentpressure%');
        if (data.currenttemperature.Value.length <= 0) {
            byId('conditions').textContent = 'Unavailable';
            byId('location').hidden = true;
            byId('weather-ico').hidden = true;
        } else {
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
    } catch { /* ignore, retried on the next interval */ }
}

async function refreshReferences() {
    for (const el of document.querySelectorAll('#customul [data-ref]')) {
        const text = await send('{mute}' + el.dataset.ref).catch(() => null);
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
    refreshReferences();
}

function renderCards() {
    const filter = byId('filter-input').value.trim().toLowerCase();
    const list = byId('customul');
    const items = instructionSets.filter(s =>
        s.header !== null && !s.id.includes('*') && s.categ === selectedCategory &&
        (filter === '' || (s.header + ' ' + s.shortdescr + ' ' + s.descr).toLowerCase().includes(filter)));

    list.replaceChildren(...items.map(s => {
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
            const spacer = document.createElement('span');
            spacer.className = 'noimg';
            button.append(spacer);
        }

        const body = document.createElement('span');
        const h = document.createElement('h3');
        h.textContent = s.header;
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
        if (s.ref !== null) value.dataset.ref = s.ref;
        button.append(value);

        li.append(button);
        return li;
    }));

    byId('customul-empty').hidden = items.length > 0;
}

/* ------------------------------------------------------------------ commands */

// Runs a command; the answer goes to the terminal on its page and to a dialog everywhere else.
async function runCommand(cmd) {
    if (!cmd) return;
    const data = await send(cmd);
    if (data === null) return;

    if (currentView === 'page2') {
        byId('response-p2').textContent = data !== '' ? data : 'Operation completed.';
    } else if (data !== '') {
        showResult(data);
    } else if (currentView !== 'page0') {
        showResult('Operation completed.');
    }
    if (currentView === 'page0') refreshHome();
}

function clearPage() {
    setVal('textinput1', '');
    byId('response-p2').textContent = '';
    byId('textinput1').focus();
}

function submitTerminal() {
    const text = val('textinput1');
    if (text.trim() !== '') {
        if (cmdHistory[cmdHistory.length - 1] !== text) cmdHistory.push(text);
        historyIndex = cmdHistory.length;
    }
    runCommand(encodeURIComponent(text));
}

function terminalKeys(event) {
    if (event.key === 'ArrowUp' && historyIndex > 0) {
        setVal('textinput1', cmdHistory[--historyIndex]);
        event.preventDefault();
    } else if (event.key === 'ArrowDown' && historyIndex < cmdHistory.length) {
        historyIndex++;
        setVal('textinput1', cmdHistory[historyIndex] ?? '');
        event.preventDefault();
    }
}

/* ------------------------------------------------------------------ settings forms */

const enc = encodeURIComponent;
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
        await sendAndShow('judo gmail set ' + val('gmailUsername') + ' ' + enc(val('gmailPassword')));
    },
    async smtpSettings() {
        await sendAndShow('judo smtp set ' + val('smtpHost') + ' ' + val('smtpUsername') + ' ' + enc(val('smtpPassword')) +
            ' ' + val('smtpPort') + ' ' + byId('chkSmtpSSL').checked);
    },
    async pop3Settings() {
        await sendAndShow('judo pop3 set ' + val('pop3Host') + ' ' + val('pop3Username') + ' ' + enc(val('pop3Password')) +
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
        await sendAndShow('judo weather set <lock>' + val('weatherURI') + '</lock>');
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
        let cmd;
        if (val('insetCateg') !== '' && val('insetHeader') !== '') {
            cmd = 'judo inset add ' + id + ' <lock>' + val('insetAction') + '</lock> `' + val('insetCateg') + '` `' + val('insetHeader') +
                '` `' + val('insetShortDescr') + '` `' + val('insetDescr') + '` `' + val('insetThumbnail') + '` `' + val('insetlistReference').replace('*', '') + '`';
        } else {
            cmd = 'judo inset add ' + id + ' <lock>' + val('insetAction') + '</lock>';
        }
        const data = await send(cmd);
        if (data === null) return false;
        if (data.includes('Element added.')) clearInsetFields();
        toast(data);
        await loadXml();
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
    document.addEventListener('click', event => {
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

    byId('cmdform').addEventListener('submit', event => { event.preventDefault(); submitTerminal(); });
    byId('textinput1').addEventListener('keydown', terminalKeys);
    byId('clearBtn').addEventListener('click', clearPage);

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
