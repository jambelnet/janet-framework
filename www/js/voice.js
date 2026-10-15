// (c) J@mBeL.net 2010-2026, John Ambeliotis. Part of jaNET Framework, GNU GPL version 3 or later; see LICENSE.

const byId = id => document.getElementById(id);
const PREFS = 'jubito-voice';
let prefs = { playback: 'browser', voice: '', rate: 1, replies: true, events: true };
try { prefs = { ...prefs, ...JSON.parse(localStorage.getItem(PREFS) ?? '{}') }; } catch { }

export function encodeWave(samples) {
    const buffer = new ArrayBuffer(44 + samples.length * 2);
    const view = new DataView(buffer);
    const word = (offset, text) => [...text].forEach((char, index) => view.setUint8(offset + index, char.charCodeAt(0)));
    word(0, 'RIFF'); view.setUint32(4, buffer.byteLength - 8, true); word(8, 'WAVE'); word(12, 'fmt ');
    view.setUint32(16, 16, true); view.setUint16(20, 1, true); view.setUint16(22, 1, true);
    view.setUint32(24, 16000, true); view.setUint32(28, 32000, true); view.setUint16(32, 2, true); view.setUint16(34, 16, true);
    word(36, 'data'); view.setUint32(40, samples.length * 2, true);
    samples.forEach((sample, index) => view.setInt16(44 + index * 2, Math.round(Math.max(-1, Math.min(1, sample)) * (sample < 0 ? 32768 : 32767)), true));
    return buffer;
}

async function request(path, value, binary = false, signal) {
    const response = await fetch('../api/speech' + path, value === undefined ? { cache: 'no-store', signal } : {
        method: 'POST', headers: { 'Content-Type': binary ? 'audio/wav' : 'application/json' },
        body: binary ? value : JSON.stringify(value), cache: 'no-store', signal
    });
    if (!response.ok) {
        const error = await response.json().catch(() => ({}));
        throw new Error(error.error ?? 'Voice request failed (' + response.status + ').');
    }
    return response;
}

export function wireVoice(onTranscript, notify) {
    let recording = null;
    let starting = false;
    let captureGeneration = 0;
    let processing = false;
    let audio = null;
    let audioUrl = null;
    let speechAbort = null;
    let recognitionAbort = null;
    let responseText = '';
    let playbackActive = false;
    let playbackGeneration = 0;
    let muted = false;
    let synthesisReady = false;
    let instructionOutput = 'box';
    let eventSettingsGeneration = 0;
    let eventCursor = null;
    let pollingEvents = false;
    let eventPlayback = false;
    let startingEvent = false;
    const pendingEvents = [];
    const status = text => { byId('voiceStatus').textContent = text; };

    function playbackControl(active) {
        playbackActive = active;
        const button = byId('speakBtn');
        button.hidden = byId('askResponse').hidden || !responseText.trim();
        button.disabled = muted;
        button.title = muted ? 'Speech muted. Run unmute to enable it.' : active ? 'Stop reading' : 'Read response aloud';
        button.setAttribute('aria-label', button.title);
        button.querySelector('use').setAttribute('href', active ? '#i-stop' : '#i-volume');
    }

    function microphoneControl(active) {
        const button = byId('micBtn');
        button.setAttribute('aria-pressed', String(active));
        button.title = active ? 'Finish recording and transcribe' : 'Record a voice command';
        button.setAttribute('aria-label', button.title);
        button.querySelector('use').setAttribute('href', active ? '#i-stop' : '#i-mic');
    }

    function chooseDeviceVoice(available, voiceId = '') {
        if (voiceId) return available.find(voice => voice.voiceURI === voiceId);
        const locale = value => String(value ?? '').replace(/_/g, '-').toLowerCase();
        const pageLocale = locale(document.documentElement?.lang || navigator.language || 'en');
        const language = pageLocale.split('-')[0];
        const matching = available.filter(voice => locale(voice.lang).split('-')[0] === language);
        // A generic page language uses the browser's regional preference, not
        // an unrelated phone default or whichever voice enumerated first.
        const preferences = [pageLocale, ...(navigator.languages ?? []), navigator.language].map(locale)
            .filter(value => value.startsWith(language + '-'));
        for (const preference of preferences) {
            const exact = matching.filter(voice => locale(voice.lang) === preference);
            if (exact.length) return exact.find(voice => voice.default) ?? exact[0];
        }
        return matching.find(voice => locale(voice.lang) === pageLocale) ?? matching.find(voice => voice.default) ?? matching[0];
    }

    function voices(preferred) {
        const select = byId('browserVoice');
        const selected = preferred ?? select.value;
        const available = (window.speechSynthesis?.getVoices() ?? []).filter(voice => voice.localService);
        select.replaceChildren();
        const defaultVoice = document.createElement('option');
        const automatic = chooseDeviceVoice(available);
        defaultVoice.value = ''; defaultVoice.textContent = automatic ? 'Automatic: ' + automatic.name + ' (' + automatic.lang + ')' :
            available.length ? 'No matching local voice (' + (document.documentElement?.lang || navigator.language || 'en') + ')' : 'No local voice available';
        select.append(defaultVoice);
        available.forEach(voice => {
            const option = document.createElement('option');
            option.value = voice.voiceURI; option.textContent = voice.name + ' (' + voice.lang + ')'; select.append(option);
        });
        if (selected && !available.some(voice => voice.voiceURI === selected)) {
            const missing = document.createElement('option');
            missing.value = selected; missing.textContent = selected + ' (unavailable)'; select.append(missing);
        }
        select.value = selected;
    }

    function stop() {
        playbackGeneration++;
        speechAbort?.abort(); speechAbort = null;
        window.speechSynthesis?.cancel();
        audio?.pause(); audio = null;
        if (audioUrl) URL.revokeObjectURL(audioUrl);
        audioUrl = null;
        eventPlayback = false;
        playbackControl(false);
        if (!recording && !starting && !processing) status('Ready');
        queueMicrotask(playNextEvent);
    }

    async function playNextEvent() {
        if (startingEvent || playbackActive || recording || starting || processing || muted || document.hidden || !prefs.events || instructionOutput !== 'browser') return;
        while (pendingEvents.length && Date.now() - pendingEvents[0].created > 60000) pendingEvents.shift();
        if (!pendingEvents.length || navigator.userActivation?.hasBeenActive === false) return;
        startingEvent = true;
        const message = pendingEvents.shift();
        try { await say(message.text, false, true); }
        finally { startingEvent = false; }
    }

    async function pollEvents() {
        if (pollingEvents) return;
        pollingEvents = true;
        const generation = eventSettingsGeneration;
        try {
            const query = eventCursor ? '?session=' + encodeURIComponent(eventCursor.session) + '&after=' + eventCursor.lastId : '';
            const result = await (await request('/events' + query)).json();
            if (generation !== eventSettingsGeneration) return;
            if (typeof result.session !== 'string' || !Number.isSafeInteger(result.lastId) || !Array.isArray(result.events)) return;
            eventCursor = { session: result.session, lastId: result.lastId };
            muted = result.muted;
            instructionOutput = result.output === 'browser' ? 'browser' : 'box';
            if (muted) { pendingEvents.length = 0; if (playbackActive) stop(); }
            else if (instructionOutput === 'browser' && prefs.events && !document.hidden) {
                for (const message of result.events) {
                    if (typeof message.text === 'string' && message.text.trim()) pendingEvents.push({ text: message.text, created: Date.parse(message.created) });
                }
                if (pendingEvents.length > 32) pendingEvents.splice(0, pendingEvents.length - 32);
                await playNextEvent();
            }
            else { pendingEvents.length = 0; if (eventPlayback) stop(); }
            playbackControl(playbackActive);
        } catch { /* A missing connection must not cancel microphone or manual playback. */ }
        finally { pollingEvents = false; }
    }

    async function refreshMute() {
        try {
            const config = await (await request('')).json();
            if (typeof config.muted !== 'boolean') throw new Error('Reload jaNET to update speech controls.');
            muted = config.muted;
            synthesisReady = config.synthesisReady === true;
            instructionOutput = config.instructionOutput === 'browser' ? 'browser' : 'box';
            if (muted) pendingEvents.length = 0;
            if (muted && playbackActive) stop();
            else playbackControl(playbackActive);
            return true;
        } catch { return false; }
    }

    async function say(text, test = false, event = false) {
        stop();
        eventPlayback = event;
        const generation = playbackGeneration;
        if (!await refreshMute()) {
            const message = 'Cannot check speech state. Check the jaNET connection.';
            status(message); if (test) byId('voiceSetupStatus').textContent = message; return;
        }
        if (generation !== playbackGeneration) return;
        if (muted) {
            const message = 'Speech muted. Run unmute to enable it.';
            status(message); if (test) byId('voiceSetupStatus').textContent = message; return;
        }
        if (!text.trim()) { status('No response to read.'); return; }
        const destination = test ? byId('instructionSpeechOutput').value : instructionOutput;
        const selectedPlayback = destination === 'box' ? 'server' : test ? byId('voicePlayback').value : prefs.playback;
        const rate = test ? Number(byId('voiceRate').value) : prefs.rate;
        const voiceId = test ? byId('browserVoice').value : prefs.voice;
        try {
            if (event && instructionOutput !== 'browser') { stop(); return; }
            let deviceVoices = (window.speechSynthesis?.getVoices() ?? []).filter(voice => voice.localService);
            // Browsers can enumerate installed voices after the first page paint.
            for (let attempt = 0; selectedPlayback === 'browser' && attempt < 15 &&
                !chooseDeviceVoice(deviceVoices, voiceId); attempt++) {
                await new Promise(resolve => setTimeout(resolve, 100));
                if (generation !== playbackGeneration) return;
                deviceVoices = (window.speechSynthesis?.getVoices() ?? []).filter(voice => voice.localService);
            }
            if (selectedPlayback === 'browser' && voiceId && !deviceVoices.some(voice => voice.voiceURI === voiceId))
                throw new Error('The selected device voice is unavailable. Choose an installed voice in Voice settings.');
            const playback = selectedPlayback === 'browser' && !voiceId && !deviceVoices.length && synthesisReady ? 'server' : selectedPlayback;
            status('Speaking...');
            eventPlayback = event;
            playbackControl(true);
            if (playback === 'server') {
                const controller = new AbortController(); speechAbort = controller;
                const response = await fetch('../api/speech/synthesize', {
                    method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ text: text.slice(0, 500) }), signal: controller.signal
                });
                if (!response.ok) throw new Error((await response.json()).error);
                const blob = await response.blob();
                if (controller.signal.aborted) return;
                audioUrl = URL.createObjectURL(blob); audio = new Audio(audioUrl); audio.playbackRate = rate;
                audio.onended = () => { if (generation === playbackGeneration) stop(); };
                audio.onerror = () => { if (generation === playbackGeneration) { stop(); status('Audio playback failed.'); } };
                await audio.play();
            } else {
                if (!window.speechSynthesis) throw new Error('This browser does not support speech playback. Select server audio.');
                const available = deviceVoices;
                if (!available.length) throw new Error('No local device voice is available. Install an OS voice, or select server audio.');
                const utterance = new SpeechSynthesisUtterance(text.slice(0, 500));
                utterance.voice = chooseDeviceVoice(available, voiceId);
                if (!utterance.voice) throw new Error('No local voice matches the page language (' + (document.documentElement?.lang || navigator.language || 'en') + '). Choose an installed device voice in Voice settings, or select server audio.');
                utterance.lang = utterance.voice.lang.replace(/_/g, '-'); utterance.rate = rate;
                utterance.onend = () => { if (generation === playbackGeneration) stop(); };
                utterance.onerror = event => {
                    if (generation !== playbackGeneration) return;
                    stop(); if (event.error !== 'interrupted' && event.error !== 'canceled') status('Speech playback failed: ' + event.error);
                };
                window.speechSynthesis.speak(utterance);
            }
        } catch (error) { if (generation === playbackGeneration && error.name !== 'AbortError') { pendingEvents.length = 0; stop(); status(error.message); if (test) byId('voiceSetupStatus').textContent = error.message; notify(error.message); } }
    }

    async function finish(cancel = false) {
        const clip = recording;
        if (!clip) return;
        const generation = captureGeneration;
        recording = null;
        processing = true; byId('micBtn').disabled = true;
        clearTimeout(clip.timer);
        clip.node.disconnect(); clip.source.disconnect(); clip.silent.disconnect();
        clip.stream.getTracks().forEach(track => track.stop());
        await clip.context.close().catch(() => {});
        microphoneControl(false);
        if (cancel || generation !== captureGeneration || document.hidden) { processing = false; byId('micBtn').disabled = false; byId('cancelCaptureBtn').hidden = true; status('Ready'); return; }
        const controller = new AbortController(); recognitionAbort = controller;
        try {
            status('Transcribing locally...');
            const count = clip.chunks.reduce((sum, chunk) => sum + chunk.length, 0);
            if (!count) throw new Error('No audio was recorded.');
            const samples = new Float32Array(count);
            let offset = 0;
            clip.chunks.forEach(chunk => { samples.set(chunk, offset); offset += chunk.length; });
            const offline = new OfflineAudioContext(1, Math.max(1, Math.round(count * 16000 / clip.context.sampleRate)), 16000);
            const input = offline.createBuffer(1, count, clip.context.sampleRate); input.copyToChannel(samples, 0);
            const source = offline.createBufferSource(); source.buffer = input; source.connect(offline.destination); source.start();
            const resampled = await offline.startRendering();
            if (controller.signal.aborted) throw new DOMException('Recording cancelled.', 'AbortError');
            const result = await (await request('/recognize', encodeWave(resampled.getChannelData(0)), true, controller.signal)).json();
            if (controller.signal.aborted) return;
            if (!result.text) { status('No speech detected.'); return; }
            onTranscript(result.text); status('Review your command, then send.');
        } catch (error) { if (error.name === 'AbortError') status('Ready'); else { status(error.message); notify(error.message); } }
        finally { recognitionAbort = null; processing = false; byId('micBtn').disabled = false; byId('cancelCaptureBtn').hidden = true; }
    }

    async function record() {
        if (starting || processing) return;
        if (recording) { await finish(); return; }
        starting = true;
        byId('cancelCaptureBtn').hidden = false;
        status('Preparing microphone...');
        const generation = ++captureGeneration;
        let stream;
        let context;
        const checkActive = () => {
            if (generation !== captureGeneration || document.hidden) throw new DOMException('Recording cancelled.', 'AbortError');
        };
        try {
            if (!window.isSecureContext || !navigator.mediaDevices?.getUserMedia) throw new Error('Microphone access needs trusted HTTPS or localhost.');
            const config = await (await request('')).json();
            checkActive();
            if (!config.recognitionReady) throw new Error('Download a recognition model in Voice settings first.');
            stop();
            stream = await navigator.mediaDevices.getUserMedia({ audio: { channelCount: 1, echoCancellation: true, noiseSuppression: true }, video: false });
            checkActive();
            context = new AudioContext(); await context.audioWorklet.addModule(new URL('./voice-recorder.js', import.meta.url));
            checkActive();
            const source = context.createMediaStreamSource(stream);
            const node = new AudioWorkletNode(context, 'voice-recorder');
            const silent = context.createGain(); silent.gain.value = 0;
            const chunks = [];
            let frames = 0;
            node.port.onmessage = event => {
                if (recording?.context !== context || frames >= context.sampleRate * 20) return;
                const chunk = event.data.slice(0, context.sampleRate * 20 - frames);
                chunks.push(chunk); frames += chunk.length;
            };
            source.connect(node); node.connect(silent); silent.connect(context.destination); await context.resume();
            checkActive();
            recording = { stream, context, source, node, silent, chunks, timer: setTimeout(() => finish(), 20000) };
            microphoneControl(true); status('Listening...');
        } catch (error) {
            stream?.getTracks().forEach(track => track.stop()); await context?.close();
            if (error.name === 'AbortError') { status('Ready'); return; }
            const message = error.name === 'NotAllowedError' ? 'Microphone permission was denied.' : error.message;
            status(message); notify(message);
        } finally { starting = false; if (!recording && !processing) byId('cancelCaptureBtn').hidden = true; }
    }

    function fields() {
        const box = byId('instructionSpeechOutput').value === 'box';
        const server = byId('voicePlayback').value === 'server';
        byId('browserSpeechFields').hidden = box;
        byId('boxSpeechFields').hidden = !box && !server;
        byId('browserVoice').disabled = server;
        const engine = byId('speechEngine').value;
        byId('speechExecutableFields').hidden = engine === 'windows' || engine === 'off';
        byId('windowsVoiceFields').hidden = engine !== 'windows';
        byId('speechVoiceFields').hidden = !['auto', 'espeak'].includes(engine);
        byId('piperModelFields').hidden = engine !== 'piper';
        byId('speechExecutable').disabled = engine === 'off' || engine === 'windows';
        byId('speechVoice').disabled = !['auto', 'windows', 'espeak'].includes(engine); byId('piperModel').disabled = engine !== 'piper';
    }

    let settingsReady = false;
    let settingsGeneration = 0;
    let windowsVoices = null;

    async function loadWindowsVoices(generation) {
        const select = byId('windowsVoice');
        select.disabled = true;
        if (!windowsVoices) {
            const result = await (await request('/voices')).json();
            if (!Array.isArray(result.voices) || !result.voices.length || result.voices.some(voice =>
                !voice || typeof voice.id !== 'string' || !voice.id || typeof voice.name !== 'string' || !voice.name || typeof voice.language !== 'string'))
                throw new Error('Installed Windows voices could not be loaded. Close and reopen Voice settings to retry.');
            windowsVoices = result.voices;
        }
        if (generation !== settingsGeneration) return;
        select.replaceChildren();
        const defaultVoice = document.createElement('option');
        defaultVoice.value = 'default'; defaultVoice.textContent = 'Windows default'; select.append(defaultVoice);
        windowsVoices.forEach(voice => {
            const option = document.createElement('option');
            option.value = voice.id; option.textContent = voice.name + ' (' + voice.language + ')'; select.append(option);
        });
        const saved = byId('speechVoice').value;
        const installed = windowsVoices.find(voice => voice.id === saved || voice.name === saved);
        if (!installed && saved && saved !== 'default') {
            const existing = document.createElement('option');
            existing.value = saved; existing.textContent = saved + ' (saved voice / language)'; select.append(existing);
        }
        select.value = installed?.id ?? (saved || 'default');
        select.disabled = false;
    }
    const settingsControls = [...byId('voiceForm').querySelectorAll('input,select,button')].filter(control => !('close' in control.dataset));
    byId('voiceSettings').addEventListener('close', () => {
        if (!byId('voiceSettings').open) { settingsGeneration++; settingsReady = false; }
    });

    async function loadSettings() {
        const generation = ++settingsGeneration;
        settingsReady = false; settingsControls.forEach(control => { control.disabled = true; });
        byId('voiceSetupStatus').textContent = 'Loading saved voice settings...';
        byId('voicePlayback').value = prefs.playback; byId('voiceRate').value = prefs.rate;
        byId('voiceRateValue').value = Number(prefs.rate).toFixed(1); byId('voiceReplies').checked = prefs.replies; byId('voiceEvents').checked = prefs.events; voices(prefs.voice);
        try {
            const config = await (await request('')).json();
            const map = { speechEngine: 'engine', speechExecutable: 'executable', speechVoice: 'voice', piperModel: 'piperModel', recognitionModel: 'recognitionModel' };
            if (generation !== settingsGeneration) return;
            if (Object.values(map).some(key => typeof config.settings?.[key] !== 'string')) throw new Error('Incomplete voice settings response.');
            Object.entries(map).forEach(([id, key]) => { byId(id).value = config.settings[key]; });
            byId('instructionSpeechOutput').value = config.instructionOutput === 'browser' ? 'browser' : 'box';
            fields();
            if (config.settings.engine === 'windows') await loadWindowsVoices(generation);
            if (generation !== settingsGeneration) return;
            settingsControls.forEach(control => { control.disabled = false; }); settingsReady = true;
            byId('voiceSetupStatus').textContent = (config.recognitionReady ? 'Recognition model ready.' : 'No recognition model installed.') + ' ' +
                (config.synthesisReady ? 'Server speech engine ready.' : 'Server engine not installed/configured.');
            fields();
        } catch (error) {
            if (generation === settingsGeneration) byId('voiceSetupStatus').textContent = 'Saved voice settings could not be loaded. Close and reopen to retry. ' + error.message;
        }
    }

    async function saveServer() {
        return request('/settings', { engine: byId('speechEngine').value, executable: byId('speechExecutable').value,
            voice: byId('speechVoice').value, piperModel: byId('piperModel').value, recognitionModel: byId('recognitionModel').value,
            instructionOutput: byId('instructionSpeechOutput').value });
    }

    byId('voiceForm').addEventListener('submit', async event => {
        event.preventDefault();
        if (!settingsReady) return;
        try {
            await saveServer(); prefs = { playback: byId('voicePlayback').value, voice: byId('browserVoice').value, rate: Number(byId('voiceRate').value), replies: byId('voiceReplies').checked, events: byId('voiceEvents').checked };
            eventSettingsGeneration++; pendingEvents.length = 0; stop();
            instructionOutput = byId('instructionSpeechOutput').value;
            try { localStorage.setItem(PREFS, JSON.stringify(prefs)); }
            catch { throw new Error('Server settings saved, but this browser could not remember its voice. Enable site storage and save again.'); }
            byId('voiceSettings').close(); notify('Voice settings saved.');
        } catch (error) { byId('voiceSetupStatus').textContent = error.message; }
    });
    byId('downloadVoiceModel').addEventListener('click', async () => {
        const button = byId('downloadVoiceModel'); button.disabled = true;
        byId('voiceSetupStatus').textContent = 'Downloading recognition model...';
        try {
            const result = await (await request('/model', { language: byId('modelLanguage').value })).json();
            byId('recognitionModel').value = result.status.settings.recognitionModel;
            byId('voiceSetupStatus').textContent = 'Recognition model installed.';
            notify('Recognition model installed.');
        }
        catch (error) { byId('voiceSetupStatus').textContent = error.message; }
        finally { button.disabled = false; }
    });
    byId('testVoice').addEventListener('click', async () => {
        if (!settingsReady) return;
        try { if (byId('instructionSpeechOutput').value === 'box' || byId('voicePlayback').value === 'server') await saveServer(); await say('Hello. Jubito is ready.', true); }
        catch (error) { byId('voiceSetupStatus').textContent = error.message; }
    });
    byId('micBtn').addEventListener('click', record);
    const cancelCapture = () => { pendingEvents.length = 0; captureGeneration++; recognitionAbort?.abort(); stop(); if (recording) finish(true); };
    byId('cancelCaptureBtn').addEventListener('click', cancelCapture);
    byId('speakBtn').addEventListener('click', () => { pendingEvents.length = 0; return playbackActive ? stop() : say(responseText); });
    byId('voiceRate').addEventListener('input', () => { byId('voiceRateValue').value = Number(byId('voiceRate').value).toFixed(1); });
    byId('voicePlayback').addEventListener('change', () => { byId('instructionSpeechOutput').value = 'browser'; fields(); });
    byId('instructionSpeechOutput').addEventListener('change', fields);
    byId('windowsVoice').addEventListener('change', () => { byId('speechVoice').value = byId('windowsVoice').value; });
    byId('speechEngine').addEventListener('change', async () => {
        fields();
        if (byId('speechEngine').value !== 'windows') { settingsReady = true; return; }
        const generation = settingsGeneration;
        settingsReady = false;
        try { await loadWindowsVoices(generation); if (generation === settingsGeneration) settingsReady = true; }
        catch (error) { if (generation === settingsGeneration && byId('speechEngine').value === 'windows') byId('voiceSetupStatus').textContent = error.message; }
    });
    byId('browserVoice').addEventListener('change', () => { byId('instructionSpeechOutput').value = 'browser'; });
    window.speechSynthesis?.addEventListener('voiceschanged', () => voices()); voices(prefs.voice);
    document.addEventListener('visibilitychange', () => { if (document.hidden) cancelCapture(); });
    window.addEventListener('pagehide', cancelCapture);
    document.addEventListener('pointerdown', () => queueMicrotask(playNextEvent));
    document.addEventListener('keydown', () => queueMicrotask(playNextEvent));
    const eventsReady = pollEvents();
    const eventsTimer = setInterval(pollEvents, 1000);
    window.addEventListener('pagehide', () => clearInterval(eventsTimer));
    refreshMute();
    return { loadSettings, refreshMute, eventsReady, async response(text, spokenByServer = false) { if (!eventPlayback) stop(); responseText = text; playbackControl(playbackActive); await refreshMute(); if (instructionOutput === 'browser' && prefs.replies && !eventPlayback && !(spokenByServer && prefs.events)) await say(text); },
        clearResponse() { stop(); responseText = ''; playbackControl(false); }, stop() { pendingEvents.length = 0; stop(); } };
}
