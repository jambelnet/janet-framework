// (c) J@mBeL.net 2010-2026, John Ambeliotis. Part of jaNET Framework, licensed under the GNU GPL version 3 or later (see LICENSE).
// Z-Wave device list for a RaZberry controller (Z-Way HTTP API).
// The controller is expected on port 8083 of the same host that serves this page.

const root = location.protocol + '//' + location.hostname + ':8083/ZWaveAPI/';
const REFRESH_MS = 1500;

let addNodeActive = false;
let removeNodeActive = false;

const devicesEl = document.getElementById('devices');
const statusEl = document.getElementById('status');

// Commands only have to reach the controller; its answer is not needed (and is not readable cross-origin).
function runCommand(url) {
    return fetch(url, { mode: 'no-cors' }).catch(() => {});
}

function formatTime(seconds) {
    const d = new Date(seconds * 1000);
    const pad = n => String(n).padStart(2, '0');
    return d.getHours() + ':' + pad(d.getMinutes()) + ':' + pad(d.getSeconds());
}

function deviceRow(index, device) {
    const data = device.data ?? {};
    const name = data.givenName?.value || data.vendorString?.value || '';
    if (name === '') return null;

    const switchClass = device.instances?.[0]?.commandClasses?.[37]?.data;
    const sensorClass = device.instances?.[0]?.commandClasses?.[50]?.data?.[0];
    const isOn = switchClass?.level?.value === true || Number(switchClass?.level?.value) > 0;
    const base = root + 'Run/devices[' + index + '].instances[0].commandClasses[37].Set(';

    const li = document.createElement('li');
    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'card';
    button.disabled = !switchClass;
    button.addEventListener('click', () => runCommand(base + (isOn ? '0' : '255') + ')').then(refresh));

    const spacer = document.createElement('span');
    spacer.className = 'noimg';

    const body = document.createElement('span');
    const h = document.createElement('h3');
    h.textContent = '#' + index + ' ' + name;
    const p1 = document.createElement('p');
    const strong = document.createElement('strong');
    strong.textContent = 'Last Update: ' + (switchClass?.level?.updateTime ? formatTime(switchClass.level.updateTime) : '–');
    p1.append(strong);
    const p2 = document.createElement('p');
    p2.textContent = sensorClass
        ? 'Level: ' + sensorClass.val?.value + ' | Scale: ' + sensorClass.scaleString?.value
        : '';
    body.append(h, p1, p2);

    const value = document.createElement('span');
    value.className = 'value';
    value.textContent = switchClass ? (isOn ? 'On' : 'Off') : '';
    value.style.color = isOn ? 'var(--ok)' : 'var(--danger)';

    button.append(spacer, body, value);
    li.append(button);
    return li;
}

async function refresh() {
    try {
        const res = await fetch(root + 'Data');
        if (!res.ok) throw new Error('HTTP ' + res.status);
        const data = await res.json();
        const rows = Object.entries(data.devices ?? {}).map(([i, d]) => deviceRow(i, d)).filter(Boolean);
        devicesEl.replaceChildren(...rows);
        statusEl.hidden = rows.length > 0;
        statusEl.textContent = 'No devices found.';
    } catch {
        statusEl.hidden = false;
        statusEl.textContent = 'The Z-Way controller at ' + root + ' does not answer.';
    }
}

function networkNodes(kind) {
    if (kind === 'add') {
        addNodeActive = !addNodeActive;
        runCommand(root + 'Run/controller.AddNodeToNetwork(' + (addNodeActive ? 1 : 0) + ')');
        document.getElementById('addBtn').setAttribute('aria-pressed', String(addNodeActive));
    } else {
        removeNodeActive = !removeNodeActive;
        runCommand(root + 'Run/controller.RemoveNodeFromNetwork(' + (removeNodeActive ? 1 : 0) + ')');
        document.getElementById('removeBtn').setAttribute('aria-pressed', String(removeNodeActive));
    }
}

document.getElementById('addBtn').addEventListener('click', () => networkNodes('add'));
document.getElementById('removeBtn').addEventListener('click', () => networkNodes('remove'));
document.getElementById('refreshBtn').addEventListener('click', refresh);

refresh();
setInterval(() => { if (!document.hidden) refresh(); }, REFRESH_MS);
