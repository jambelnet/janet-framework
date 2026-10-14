// (c) J@mBeL.net 2010-2026. Part of jaNET Framework, GNU GPL version 3 or later; see LICENSE.
const validId = id => typeof id === 'string' && /^[a-f0-9]{32}$/.test(id);
export function cameraFeedUrl(id, stamp = Date.now()) {
    if (!validId(id)) throw new Error('Invalid camera identifier.');
    return '../api/cameras/' + id + '/feed?t=' + encodeURIComponent(stamp);
}
export function validateCameras(result, settings = false) {
    if (!Array.isArray(result?.cameras) || result.cameras.length > 8 || settings && typeof result.revision !== 'string') throw new Error('Invalid camera response.');
    const ids = new Set();
    for (const camera of result.cameras) {
        if (!validId(camera.id) || ids.has(camera.id) || typeof camera.name !== 'string' || !['auto', 'snapshot', 'mjpeg'].includes(camera.mode) ||
            !Number.isInteger(camera.refreshSeconds) || camera.refreshSeconds < 2 || camera.refreshSeconds > 60) throw new Error('Invalid camera profile.');
        if (settings && (typeof camera.url !== 'string' || typeof camera.username !== 'string' || camera.password !== '' ||
            typeof camera.passwordConfigured !== 'boolean' || typeof camera.showOnHome !== 'boolean')) throw new Error('Unsafe camera settings response.');
        if (!settings && ('url' in camera || 'username' in camera || 'password' in camera)) throw new Error('Unsafe camera widget response.');
        if (!settings && Object.keys(camera).some(key => !['id', 'name', 'mode', 'refreshSeconds', 'version'].includes(key))) throw new Error('Unsafe camera widget response.');
        ids.add(camera.id);
    }
    return result;
}
export function validateCameraSetup(values) {
    let url;
    try { url = new URL(values.url); } catch { throw new Error('Enter a valid camera feed URL.'); }
    if (!['http:', 'https:'].includes(url.protocol) || url.username || url.password || url.hash)
        throw new Error('Use an HTTP/HTTPS snapshot or MJPEG URL and separate credentials. RTSP requires a gateway.');
    if (!values.name.trim() || !['auto', 'snapshot', 'mjpeg'].includes(values.mode) || !Number.isInteger(values.refreshSeconds) || values.refreshSeconds < 2 || values.refreshSeconds > 60)
        throw new Error('Enter a camera name and a snapshot interval between 2 and 60 seconds.');
    return values;
}
export async function cameraRequest(action, values, signal) {
    if (!/^(settings|widgets|save|remove|[a-f0-9]{32}\/(probe))$/.test(action)) throw new Error('Invalid camera operation.');
    const abort = signal ? AbortSignal.any([signal, AbortSignal.timeout(15000)]) : AbortSignal.timeout(15000);
    const response = await fetch('../api/cameras/' + action, { cache: 'no-store', signal: abort,
        ...(values === undefined ? {} : { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(values) }) });
    if (response.status === 401) throw new Error('Authentication required. Sign in to jaNET before accessing cameras.');
    if (!response.headers.get('content-type')?.includes('application/json')) throw new Error('Camera support requires an updated jaNET server. Rebuild and restart jaNET.');
    const result = await response.json();
    if (!response.ok) throw new Error(result.error || 'Camera request failed (HTTP ' + response.status + ').');
    return result;
}

function iconButton(document, glyph, label, action) {
    const button = document.createElement('button'); button.type = 'button'; button.className = 'icon-btn';
    button.title = label; button.setAttribute('aria-label', label);
    const icon = document.createElement('i'); icon.dataset.lucide = glyph; button.append(icon); button.addEventListener('click', action);
    return button;
}

export function createCameraWidget(document, camera, request, visible, edit, pauseState = {}) {
    const card = document.createElement('article'); card.className = 'camera-widget';
    const head = document.createElement('div'); head.className = 'camera-widget-head';
    const title = document.createElement('h3'); title.textContent = camera.name;
    const controls = document.createElement('div'); controls.className = 'camera-widget-controls';
    const media = document.createElement('div'); media.className = 'camera-media';
    const image = document.createElement('img'); image.alt = camera.name; image.hidden = true; image.referrerPolicy = 'no-referrer';
    const placeholder = document.createElement('div'); placeholder.className = 'camera-placeholder';
    const symbol = document.createElement('i'); symbol.dataset.lucide = 'camera'; placeholder.append(symbol);
    const status = document.createElement('p'); status.className = 'camera-widget-status'; status.setAttribute('role', 'status'); status.textContent = 'Paused';
    let paused = !!pauseState.paused, alive = true, active = false, generation = 0, mode = null, timer = null, renewal = null, controller = null;
    const message = text => { if (status.textContent !== text) status.textContent = text; };
    function stop() {
        active = false; generation++; clearTimeout(timer); clearTimeout(renewal); timer = renewal = null; controller?.abort(); controller = null;
        image.onload = image.onerror = null; image.removeAttribute('src'); image.hidden = true; placeholder.hidden = false;
    }
    function later(delay, action) { clearTimeout(timer); timer = setTimeout(() => { timer = null; if (alive && !paused && visible()) action(); }, delay); }
    function frame() {
        if (!alive || paused || !visible()) return;
        image.onload = () => {
            if (!active || !alive || paused || !visible()) return;
            clearTimeout(timer); image.hidden = false; placeholder.hidden = true;
            message(mode === 'mjpeg' ? 'Live' : 'Snapshot');
            if (mode === 'snapshot') later(camera.refreshSeconds * 1000, frame);
        };
        image.onerror = () => {
            if (!active || !alive || paused || !visible()) return;
            image.hidden = true; placeholder.hidden = false; message('Feed unavailable. Retrying...');
            later(10000, () => { active = false; start(); });
        };
        image.src = cameraFeedUrl(camera.id);
        if (mode === 'mjpeg') { clearTimeout(renewal); renewal = setTimeout(() => { stop(); start(); }, 240000); }
        later(15000, () => {
            if (image.naturalWidth > 0 && mode === 'mjpeg') { image.hidden = false; placeholder.hidden = true; message('Live'); }
            else { image.removeAttribute('src'); image.onerror?.(); }
        });
    }
    async function start() {
        if (!alive || active || paused || !visible()) return;
        active = true; const current = ++generation; controller = new AbortController(); message('Connecting...');
        try {
            const result = await request(camera.id + '/probe', undefined, controller.signal);
            if (current !== generation || !alive || paused || !visible()) return;
            if (!['snapshot', 'mjpeg'].includes(result.mode)) throw new Error('Unsupported camera feed.');
            mode = result.mode; frame();
        } catch (error) {
            if (current !== generation || !alive || paused || !visible()) return;
            message(error.message); later(10000, () => { active = false; start(); });
        }
    }
    const pause = iconButton(document, paused ? 'play' : 'pause', paused ? 'Resume camera' : 'Pause camera', () => {
        paused = !paused; pauseState.changed?.(paused); stop(); message('Paused');
        pause.title = paused ? 'Resume camera' : 'Pause camera'; pause.setAttribute('aria-label', pause.title); pause.setAttribute('aria-pressed', String(paused));
        pause.replaceChildren(); const glyph = document.createElement('i'); glyph.dataset.lucide = paused ? 'play' : 'pause'; pause.append(glyph);
        globalThis.lucide?.createIcons(); if (!paused) start();
    });
    pause.setAttribute('aria-pressed', String(paused));
    const fullscreen = iconButton(document, 'maximize', 'Fullscreen camera', async () => {
            try { if (document.fullscreenElement === card) await document.exitFullscreen(); else await card.requestFullscreen(); }
            catch { message('Fullscreen is unavailable in this browser.'); }
        });
    function fullscreenState() {
        const full = document.fullscreenElement === card;
        fullscreen.title = full ? 'Exit fullscreen' : 'Fullscreen camera'; fullscreen.setAttribute('aria-label', fullscreen.title); fullscreen.setAttribute('aria-pressed', String(full));
        fullscreen.replaceChildren(); const glyph = document.createElement('i'); glyph.dataset.lucide = full ? 'minimize' : 'maximize'; fullscreen.append(glyph); globalThis.lucide?.createIcons();
    }
    document.addEventListener('fullscreenchange', fullscreenState);
    controls.append(iconButton(document, 'refresh-cw', 'Refresh camera', () => { stop(); paused = false; pauseState.changed?.(false); pause.title = 'Pause camera'; pause.setAttribute('aria-label', pause.title); pause.setAttribute('aria-pressed', 'false'); pause.replaceChildren(); const glyph = document.createElement('i'); glyph.dataset.lucide = 'pause'; pause.append(glyph); globalThis.lucide?.createIcons(); start(); }), pause,
        fullscreen, iconButton(document, 'settings', 'Camera settings', () => edit(camera.id)));
    head.append(title, controls); media.append(image, placeholder); card.append(head, media, status);
    function sync() {
        if (!alive) return;
        if (!visible() || paused) { if (active) stop(); message('Paused'); }
        else start();
    }
    return { element: card, sync, dispose() { alive = false; stop(); document.removeEventListener?.('fullscreenchange', fullscreenState); } };
}

export function wireCameras(document, request, notify, visible, confirmRemoval = () => globalThis.confirm('Remove this camera from Jubito?')) {
    const $ = id => document.getElementById(id), dialog = $('cameraSettings'), form = $('cameraForm');
    const fields = { name: 'cameraName', url: 'cameraUrl', mode: 'cameraMode', refreshSeconds: 'cameraRefresh', showOnHome: 'cameraHome', username: 'cameraUsername', password: 'cameraPassword', clearPassword: 'cameraClearPassword' };
    const inputs = [...form.querySelectorAll('input,select,button')].filter(el => !('close' in el.dataset));
    let settings = null, selected = '', ready = false, busy = false, generation = 0, baseline = '', loadGeneration = 0, pending = null, lastLoad = -Infinity, widgets = [];
    const pausedCameras = new Set();
    function values() {
        return { id: selected, name: $('cameraName').value, url: $('cameraUrl').value, mode: $('cameraMode').value,
            refreshSeconds: Number($('cameraRefresh').value), showOnHome: $('cameraHome').checked, username: $('cameraUsername').value,
            password: $('cameraPassword').value, clearPassword: $('cameraClearPassword').checked, revision: settings?.revision ?? '' };
    }
    function gates() {
        form.setAttribute('aria-busy', String(busy));
        inputs.forEach(el => el.disabled = !ready || busy);
        $('cameraRetry').disabled = busy;
        const dirty = baseline !== JSON.stringify(values());
        $('cameraTest').disabled = !ready || busy || !selected || dirty;
        $('cameraRemove').disabled = !ready || busy || !selected;
    }
    function select(id = '') {
        selected = id; const camera = settings?.cameras.find(c => c.id === id);
        const defaults = { name: '', url: '', mode: 'auto', refreshSeconds: 5, showOnHome: true, username: '', password: '', clearPassword: false };
        for (const [key, field] of Object.entries(fields)) {
            if (key === 'showOnHome' || key === 'clearPassword') $(field).checked = camera?.[key] ?? defaults[key];
            else $(field).value = camera?.[key] ?? defaults[key];
        }
        $('cameraPassword').placeholder = camera?.passwordConfigured ? 'Saved; leave blank to keep unchanged' : '';
        baseline = JSON.stringify(values()); gates(); renderList();
    }
    function renderList() {
        $('cameraProfiles').replaceChildren(...(settings?.cameras ?? []).map(camera => {
            const button = document.createElement('button'); button.type = 'button'; button.className = 'camera-profile';
            button.disabled = busy; button.setAttribute('aria-pressed', String(camera.id === selected));
            const label = document.createElement('strong'); label.textContent = camera.name;
            const sub = document.createElement('span'); sub.textContent = camera.showOnHome ? 'On Home' : 'Hidden from Home'; button.append(label, sub);
            button.addEventListener('click', () => { select(camera.id); $('cameraSettingsStatus').textContent = ''; }); return button;
        }));
        $('cameraProfilesEmpty').hidden = !!settings?.cameras.length;
    }
    async function open(id = '') {
        const current = ++generation; ready = false; busy = true; gates(); $('cameraRetry').hidden = true; $('cameraSettingsStatus').textContent = 'Loading saved cameras...';
        renderList();
        if (!dialog.open) dialog.showModal();
        try {
            const result = validateCameras(await request('settings'), true);
            if (current !== generation || !dialog.open) return;
            settings = result; ready = true; busy = false; select(settings.cameras.some(c => c.id === id) ? id : settings.cameras[0]?.id ?? '');
            $('cameraSettingsStatus').textContent = '';
        } catch (error) {
            if (current !== generation || !dialog.open) return;
            $('cameraSettingsStatus').textContent = error.message; $('cameraRetry').hidden = false;
        } finally { if (current === generation && dialog.open) { busy = false; gates(); } }
    }
    async function run(action, message) {
        if (!ready || busy) return;
        const current = generation; busy = true; gates(); renderList();
        $('cameraSettingsStatus').textContent = message;
        try { await action(() => current === generation && dialog.open); }
        catch (error) { if (current === generation && dialog.open) { $('cameraSettingsStatus').textContent = error.message; notify(error.message); } }
        finally { if (current === generation && dialog.open) { busy = false; gates(); renderList(); } }
    }
    function applyWidgets(cameras) {
        widgets.forEach(widget => widget.dispose());
        widgets = cameras.map(camera => createCameraWidget(document, camera, request, visible, open, {
            paused: pausedCameras.has(camera.id), changed(value) { if (value) pausedCameras.add(camera.id); else pausedCameras.delete(camera.id); }
        }));
        $('cameraWidgets').replaceChildren(...widgets.map(widget => widget.element));
        $('homeCameras').hidden = !widgets.length; $('homeCamerasStatus').hidden = true;
        globalThis.lucide?.createIcons(); widgets.forEach(widget => widget.sync());
    }
    async function refresh(force = false) {
        widgets.forEach(widget => widget.sync());
        if (!visible() || pending || !force && Date.now() - lastLoad < 60000) return;
        const current = loadGeneration; lastLoad = Date.now();
        const operation = (async () => {
            try {
                const result = validateCameras(await request('widgets'));
                if (current !== loadGeneration) return;
                const fingerprint = JSON.stringify(result.cameras);
                if (fingerprint !== $('cameraWidgets').dataset.fingerprint) { $('cameraWidgets').dataset.fingerprint = fingerprint; applyWidgets(result.cameras); }
            } catch (error) {
                if (current !== loadGeneration) return;
                widgets.forEach(widget => widget.dispose()); widgets = []; $('cameraWidgets').replaceChildren();
                delete $('cameraWidgets').dataset.fingerprint;
                $('homeCameras').hidden = false; $('homeCamerasStatus').hidden = false; $('homeCamerasStatus').textContent = error.message;
            }
        })();
        pending = operation; try { await operation; } finally { if (current === loadGeneration) pending = null; }
    }
    function invalidate() {
        loadGeneration++; pending = null; lastLoad = -Infinity; widgets.forEach(widget => widget.dispose()); widgets = [];
        $('cameraWidgets').replaceChildren(); delete $('cameraWidgets').dataset.fingerprint; return refresh();
    }
    form.addEventListener('input', gates); form.addEventListener('change', gates);
    form.addEventListener('submit', event => {
        event.preventDefault(); run(async current => {
            const result = await request('save', validateCameraSetup(values()));
            invalidate();
            if (current()) { settings = validateCameras(result.settings, true); select(result.id); $('cameraSettingsStatus').textContent = result.message; notify(result.message); }
        }, 'Saving camera...');
    });
    $('cameraNew').addEventListener('click', () => { if (ready && !busy) { select(); $('cameraSettingsStatus').textContent = ''; $('cameraName').focus(); } });
    $('cameraTest').addEventListener('click', () => run(async current => {
        const result = await request(selected + '/probe'); if (current()) $('cameraSettingsStatus').textContent = result.message;
    }, 'Testing connection...'));
    $('cameraRemove').addEventListener('click', () => {
        if (!ready || busy || !selected || !confirmRemoval()) return;
        run(async current => {
            const result = await request('remove', { id: selected, revision: settings.revision }); invalidate();
            if (current()) { settings = validateCameras(result.settings, true); select(settings.cameras[0]?.id ?? ''); $('cameraSettingsStatus').textContent = result.message; }
        }, 'Removing camera...');
    });
    $('cameraRetry').addEventListener('click', () => open(selected));
    dialog.addEventListener('close', () => { if (!dialog.open) { generation++; ready = false; busy = false; $('cameraPassword').value = ''; } });
    document.addEventListener('visibilitychange', () => refresh());
    return { open, refresh, invalidate };
}
