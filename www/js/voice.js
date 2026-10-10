// (c) J@mBeL.net 2010-2026, John Ambeliotis. Part of jaNET Framework, GNU GPL version 3 or later; see LICENSE.

const byId = id => document.getElementById(id);
const PREFS = 'jubito-voice';
let prefs = { playback: 'browser', voice: '', rate: 1, replies: false };
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
    const status = text => { byId('voiceStatus').textContent = text; };

    function playbackControl(active) {
        playbackActive = active;
        const button = byId('speakBtn');
        button.hidden = !active && !responseText;
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

    function voices() {
        const select = byId('browserVoice');
        const selected = select.value || prefs.voice;
        const available = (window.speechSynthesis?.getVoices() ?? []).filter(voice => voice.localService);
        select.replaceChildren();
        const defaultVoice = document.createElement('option');
        defaultVoice.value = ''; defaultVoice.textContent = available.length ? 'Default local voice' : 'No local voice available';
        select.append(defaultVoice);
        available.forEach(voice => {
            const option = document.createElement('option');
            option.value = voice.voiceURI; option.textContent = voice.name + ' (' + voice.lang + ')'; select.append(option);
        });
        if ([...select.options].some(option => option.value === selected)) select.value = selected;
    }

    function stop() {
        playbackGeneration++;
        speechAbort?.abort(); speechAbort = null;
        window.speechSynthesis?.cancel();
        audio?.pause(); audio = null;
        if (audioUrl) URL.revokeObjectURL(audioUrl);
        audioUrl = null;
        playbackControl(false);
        if (!recording && !starting && !processing) status('Ready');
    }

    async function refreshMute() {
        try {
            const config = await (await request('')).json();
            if (typeof config.muted !== 'boolean') throw new Error('Reload jaNET to update speech controls.');
            muted = config.muted;
            if (muted && playbackActive) stop();
            else playbackControl(playbackActive);
            return true;
        } catch { return false; }
    }

    async function say(text, test = false) {
        stop();
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
        const playback = test ? byId('voicePlayback').value : prefs.playback;
        const rate = test ? Number(byId('voiceRate').value) : prefs.rate;
        const voiceId = test ? byId('browserVoice').value : prefs.voice;
        try {
            status('Speaking...');
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
                const available = window.speechSynthesis.getVoices().filter(voice => voice.localService);
                if (!available.length) throw new Error('No local device voice is available. Install an OS voice, or select server audio.');
                const utterance = new SpeechSynthesisUtterance(text.slice(0, 500));
                utterance.voice = available.find(voice => voice.voiceURI === voiceId) ?? available.find(voice => voice.lang.startsWith(navigator.language.split('-')[0])) ?? available[0];
                utterance.lang = utterance.voice.lang; utterance.rate = rate;
                utterance.onend = () => { if (generation === playbackGeneration) stop(); };
                utterance.onerror = event => {
                    if (generation !== playbackGeneration) return;
                    stop(); if (event.error !== 'interrupted' && event.error !== 'canceled') status('Speech playback failed: ' + event.error);
                };
                window.speechSynthesis.speak(utterance);
            }
        } catch (error) { if (generation === playbackGeneration && error.name !== 'AbortError') { stop(); status(error.message); if (test) byId('voiceSetupStatus').textContent = error.message; notify(error.message); } }
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
        const server = byId('voicePlayback').value === 'server';
        byId('browserVoice').disabled = server;
        const engine = byId('speechEngine').value;
        byId('speechExecutable').disabled = engine === 'off';
        byId('speechVoice').disabled = engine !== 'espeak'; byId('piperModel').disabled = engine !== 'piper';
    }

    async function loadSettings() {
        byId('voicePlayback').value = prefs.playback; byId('voiceRate').value = prefs.rate;
        byId('voiceRateValue').value = Number(prefs.rate).toFixed(1); byId('voiceReplies').checked = prefs.replies; voices();
        try {
            const config = await (await request('')).json();
            const map = { speechEngine: 'engine', speechExecutable: 'executable', speechVoice: 'voice', piperModel: 'piperModel', recognitionModel: 'recognitionModel' };
            Object.entries(map).forEach(([id, key]) => { byId(id).value = config.settings[key]; });
            byId('voiceSetupStatus').textContent = (config.recognitionReady ? 'Recognition model ready.' : 'No recognition model installed.') + ' ' +
                (config.synthesisReady ? 'Server speech engine ready.' : 'Server engine not installed/configured.');
        } catch (error) { byId('voiceSetupStatus').textContent = error.message; }
        fields();
    }

    async function saveServer() {
        return request('/settings', { engine: byId('speechEngine').value, executable: byId('speechExecutable').value,
            voice: byId('speechVoice').value, piperModel: byId('piperModel').value, recognitionModel: byId('recognitionModel').value });
    }

    byId('voiceForm').addEventListener('submit', async event => {
        event.preventDefault();
        try {
            await saveServer(); prefs = { playback: byId('voicePlayback').value, voice: byId('browserVoice').value, rate: Number(byId('voiceRate').value), replies: byId('voiceReplies').checked };
            try { localStorage.setItem(PREFS, JSON.stringify(prefs)); } catch { }
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
        try { if (byId('voicePlayback').value === 'server') await saveServer(); await say('Hello. Jubito is ready.', true); }
        catch (error) { byId('voiceSetupStatus').textContent = error.message; }
    });
    byId('micBtn').addEventListener('click', record);
    const cancelCapture = () => { captureGeneration++; recognitionAbort?.abort(); stop(); if (recording) finish(true); };
    byId('cancelCaptureBtn').addEventListener('click', cancelCapture);
    byId('speakBtn').addEventListener('click', () => playbackActive ? stop() : say(responseText));
    byId('voiceRate').addEventListener('input', () => { byId('voiceRateValue').value = Number(byId('voiceRate').value).toFixed(1); });
    byId('voicePlayback').addEventListener('change', fields); byId('speechEngine').addEventListener('change', fields);
    window.speechSynthesis?.addEventListener('voiceschanged', voices); voices();
    document.addEventListener('visibilitychange', () => { if (document.hidden) cancelCapture(); });
    window.addEventListener('pagehide', cancelCapture);
    refreshMute();
    return { loadSettings, refreshMute, async response(text) { stop(); responseText = text; playbackControl(false); if (prefs.replies) await say(text); else await refreshMute(); },
        clearResponse() { stop(); responseText = ''; playbackControl(false); }, stop };
}
