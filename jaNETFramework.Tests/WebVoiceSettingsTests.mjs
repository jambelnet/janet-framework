// (c) J@mBeL.net 2010-2026. Part of jaNET Framework, GNU GPL version 3 or later.
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';

class Element {
    value = '';
    options = [];
    handlers = new Map();
    checked = false;
    hidden = false;
    disabled = false;
    open = true;
    textContent = '';
    addEventListener(name, handler) { this.handlers.set(name, handler); }
    setAttribute() {}
    querySelector() { return { setAttribute() {} }; }
    querySelectorAll() { return []; }
    replaceChildren() { this.options = []; this.value = ''; }
    append(option) { this.options.push(option); }
    close() { this.open = false; this.handlers.get('close')?.(); }
}

const source = await readFile(new URL('../www/js/voice.js', import.meta.url), 'utf8');
const defaultVoice = { name: 'Microsoft David', voiceURI: 'david', lang: 'en-US', localService: true, default: true };
const zira = { name: 'Microsoft Zira', voiceURI: 'zira', lang: 'en-US', localService: true };
let fixtureId = 0;

async function fixture({ saved = { playback: 'browser', voice: 'zira', rate: 1, replies: true, events: true }, output = 'browser', engine = 'auto', boxVoice = 'default', voicesFail = false, pageLanguage = 'en', language = 'en-US', languages = [language] } = {}) {
    const elements = new Map();
    const el = id => { if (!elements.has(id)) elements.set(id, new Element()); return elements.get(id); };
    const handlers = new Map();
    const spoken = [], notices = [], messages = [], saves = [];
    let available = [defaultVoice, zira], tick, remembered, cancels = 0, audioPlayed = 0;
    let storageFails = false, saveFails = false, muted = false;
    let settings = { engine, executable: '', voice: boxVoice, piperModel: '', recognitionModel: '', instructionOutput: output };
    globalThis.localStorage = { getItem() { return JSON.stringify(saved); }, setItem(key, value) { if (storageFails) throw new Error('Blocked'); remembered = JSON.parse(value); } };
    globalThis.document = { hidden: false, documentElement: { lang: pageLanguage }, getElementById: el, createElement() { return new Element(); }, addEventListener() {} };
    globalThis.window = { speechSynthesis: {
        getVoices() { return available; }, cancel() { cancels++; }, speak(utterance) { spoken.push(utterance); },
        addEventListener(name, handler) { handlers.set(name, handler); }
    }, addEventListener() {} };
    globalThis.SpeechSynthesisUtterance = class { constructor(text) { this.text = text; } };
    Object.defineProperty(globalThis, 'navigator', { value: { language, languages, userActivation: { hasBeenActive: true } }, configurable: true });
    globalThis.setInterval = callback => { tick = callback; return 1; };
    globalThis.clearInterval = () => {};
    globalThis.Audio = class { async play() { audioPlayed++; } pause() {} };
    globalThis.fetch = async (url, options) => {
        if (String(url).endsWith('/voices')) return { ok: !voicesFail, async json() { return voicesFail ? { error: 'Windows voices unavailable.' } : { voices: [
            { id: 'box-david', name: 'Microsoft David', language: 'en-US' }, { id: 'box-zira', name: 'Microsoft Zira', language: 'en-US' }
        ] }; } };
        if (String(url).endsWith('/settings')) {
            if (saveFails) return { ok: false, async json() { return { error: 'Settings could not be saved.' }; } };
            settings = JSON.parse(options.body); output = settings.instructionOutput; saves.push(settings);
            return { ok: true, async json() { return {}; } };
        }
        if (String(url).endsWith('/synthesize')) return { ok: true, async blob() { return new Blob(['wave']); } };
        const json = String(url).includes('/events') ? (() => {
            const query = new URL(String(url), 'http://localhost/www/').searchParams;
            return { session: 'test', lastId: messages.length, muted, output,
                events: query.has('after') ? messages.filter(item => item.id > Number(query.get('after'))) : [] };
        })() : { muted, settings, instructionOutput: output, synthesisReady: true, recognitionReady: false };
        return { ok: true, async json() { return json; } };
    };
    const module = await import('data:text/javascript;base64,' + Buffer.from(source + '\n// fixture ' + ++fixtureId).toString('base64'));
    const voice = module.wireVoice(() => {}, text => notices.push(text));
    await voice.eventsReady;
    await voice.loadSettings();
    const settle = async () => { for (let i = 0; i < 20; i++) await Promise.resolve(); };
    return { voice, el, spoken, notices, saves, settle, get remembered() { return remembered; }, get cancels() { return cancels; }, get audioPlayed() { return audioPlayed; },
        setVoices(voices) { available = voices; handlers.get('voiceschanged')?.(); }, setOutput(value) { output = value; },
        failStorage() { storageFails = true; }, failSave() { saveFails = true; },
        async save() { await el('voiceForm').handlers.get('submit')({ preventDefault() {} }); await settle(); },
        async publish(text) { messages.push({ id: messages.length + 1, text, created: new Date().toISOString() }); await tick(); await settle(); }
    };
}

test('saved Zira survives delayed enumeration, dialog reopen and an explicit Default selection', async () => {
    const f = await fixture();
    assert.equal(f.el('browserVoice').value, 'zira');
    await f.voice.response('Use the saved voice.');
    assert.equal(f.spoken.at(-1).voice.voiceURI, 'zira');
    f.voice.stop();
    f.el('browserVoice').value = 'david'; // An unsaved draft must not replace saved preferences.
    await f.voice.loadSettings();
    assert.equal(f.el('browserVoice').value, 'zira');
    f.setVoices([]);
    assert.equal(f.el('browserVoice').value, 'zira');
    assert.match(f.el('browserVoice').options.at(-1).textContent, /unavailable/);
    const speaking = f.voice.response('Wait for Zira to be enumerated.');
    setTimeout(() => f.setVoices([defaultVoice, zira]), 30);
    await speaking;
    assert.equal(f.spoken.at(-1).voice.voiceURI, 'zira');
    f.voice.stop();
    f.el('browserVoice').value = '';
    f.setVoices([zira, defaultVoice]);
    assert.equal(f.el('browserVoice').value, '');
    await f.save();
    assert.equal(f.remembered.voice, '');
    await f.voice.response('Use the matching English default, not the first voice.');
    assert.equal(f.spoken.at(-1).voice.voiceURI, 'david');
    f.voice.stop();
});

test('an unavailable selected voice never substitutes the default voice or server audio', async () => {
    const f = await fixture();
    f.setVoices([defaultVoice]);
    await f.voice.response('Do not use David in place of Zira.');
    assert.equal(f.spoken.length, 0);
    assert.equal(f.audioPlayed, 0);
    assert.match(f.notices.at(-1), /selected device voice is unavailable/);
    f.setVoices([]);
    await f.voice.response('Do not use server audio in place of Zira.');
    assert.equal(f.audioPlayed, 0);
    assert.equal(f.spoken.length, 0);
    f.voice.stop();
});

const automatic = { playback: 'browser', voice: '', rate: 1, replies: true, events: true };
const hindi = { name: 'Local Hindi', voiceURI: 'hi-in', lang: 'hi-IN', localService: true, default: true };
const indianEnglish = { name: 'English India', voiceURI: 'en-in', lang: 'en-IN', localService: true, default: true };
const americanEnglish = { ...defaultVoice, default: false };

test('automatic mobile speech uses the page language instead of an unrelated phone default', async () => {
    const f = await fixture({ saved: automatic, language: 'hi-IN', languages: ['hi-IN', 'en-US'] });
    f.setVoices([hindi, americanEnglish]);
    await f.voice.response('Read this in English.');
    assert.equal(f.spoken.at(-1).voice.voiceURI, 'david');
    assert.equal(f.spoken.at(-1).lang, 'en-US');
    assert.match(f.el('browserVoice').options[0].textContent, /Automatic.*Microsoft David.*en-US/);
    f.voice.stop();
});

test('automatic speech matches the browser English region before an English India default', async () => {
    const british = { name: 'English UK', voiceURI: 'en-gb', lang: 'en-GB', localService: true };
    for (const [language, expected] of [['en-US', 'david'], ['en-GB', 'en-gb']]) {
        const f = await fixture({ saved: automatic, language });
        f.setVoices([indianEnglish, british, americanEnglish]);
        await f.voice.response('Use the requested English accent.');
        assert.equal(f.spoken.at(-1).voice.voiceURI, expected);
        f.voice.stop();
    }
});

test('automatic speech honors an explicit page locale and normalized mobile voice tags', async () => {
    const f = await fixture({ saved: automatic, pageLanguage: 'en-GB' });
    f.setVoices([hindi, americanEnglish, { name: 'English UK', voiceURI: 'en-gb', lang: 'en_GB', localService: true }]);
    await f.voice.response('Use the page locale.');
    assert.equal(f.spoken.at(-1).voice.voiceURI, 'en-gb');
    assert.equal(f.spoken.at(-1).lang, 'en-GB');
    f.voice.stop();
});

test('automatic speech waits for a matching voice even if another language is already loaded', async () => {
    const f = await fixture({ saved: automatic });
    f.setVoices([hindi]);
    const speaking = f.voice.response('Wait for the English voice.');
    setTimeout(() => f.setVoices([hindi, americanEnglish]), 30);
    await speaking;
    assert.equal(f.spoken.at(-1).voice.voiceURI, 'david');
    f.voice.stop();
});

test('automatic speech reports a missing matching language rather than using an unrelated voice', async () => {
    const f = await fixture({ saved: automatic });
    f.setVoices([hindi]);
    await f.voice.response('Do not switch to Hindi.');
    assert.equal(f.spoken.length, 0);
    assert.equal(f.audioPlayed, 0);
    assert.match(f.notices.at(-1), /No local voice matches.*en/);
    f.voice.stop();
});

test('a deliberately selected device voice overrides automatic language matching', async () => {
    const f = await fixture({ saved: { ...automatic, voice: 'hi-in' } });
    f.setVoices([americanEnglish, hindi]);
    await f.voice.response('Honor the selected voice.');
    assert.equal(f.spoken.at(-1).voice.voiceURI, 'hi-in');
    f.voice.stop();
});

test('box-owned instructions are not spoken in the browser, even with automatic replies enabled', async () => {
    const f = await fixture({ output: 'box' });
    assert.equal(f.el('instructionSpeechOutput').value, 'box');
    await f.publish('Greeting spoken on the box.');
    await f.voice.response('Greeting spoken on the box.', true);
    await f.voice.response('Direct function replies must not unexpectedly use the default browser voice.');
    assert.equal(f.spoken.length, 0);
    f.el('voiceEvents').checked = false;
    await f.save();
    await f.voice.response('Still spoken only on the box.', true);
    assert.equal(f.spoken.length, 0);
    await f.voice.loadSettings();
    f.el('voiceEvents').checked = true;
    f.el('browserVoice').value = 'zira';
    f.el('browserVoice').handlers.get('change')();
    assert.equal(f.el('instructionSpeechOutput').value, 'browser');
    await f.save();
    assert.equal(f.saves.at(-1).instructionOutput, 'browser');
    await f.publish('Greeting spoken only with Zira.');
    assert.deepEqual(f.spoken.map(item => item.text), ['Greeting spoken only with Zira.']);
    assert.equal(f.spoken[0].voice.voiceURI, 'zira');
    f.voice.stop();
});

test('saving a voice change cancels active and queued old speech and remembers the new voice', async () => {
    const f = await fixture({ saved: { playback: 'browser', voice: 'david', rate: 1, replies: true, events: true } });
    await f.publish('Old voice in progress.');
    await f.publish('Old voice waiting.');
    assert.equal(f.spoken.length, 1);
    const before = f.cancels;
    f.el('browserVoice').value = 'zira';
    await f.save();
    assert(f.cancels > before);
    assert.equal(f.remembered.voice, 'zira');
    assert.equal(f.spoken.length, 1);
    f.spoken[0].onend(); await f.settle(); // A stale completion cannot start old queued audio.
    await f.publish('New voice only.');
    assert.equal(f.spoken.length, 2);
    assert.equal(f.spoken[1].voice.voiceURI, 'zira');
    f.voice.stop();
});

test('Test voice previews the selected Windows box engine, not the unrelated device voice', async () => {
    const f = await fixture({ output: 'box', saved: { playback: 'browser', voice: 'david', rate: 1, replies: true, events: true } });
    f.el('speechEngine').value = 'windows';
    f.el('speechVoice').value = 'Microsoft Zira';
    await f.el('testVoice').handlers.get('click')();
    assert.equal(f.saves.at(-1).engine, 'windows');
    assert.equal(f.saves.at(-1).voice, 'Microsoft Zira');
    assert.equal(f.saves.at(-1).instructionOutput, 'box');
    assert.equal(f.audioPlayed, 1);
    assert.equal(f.spoken.length, 0);
    f.voice.stop();
});

test('Windows box voice selection replaces the saved language and reopens on the exact installed voice', async () => {
    const f = await fixture({ output: 'box', engine: 'windows', boxVoice: 'en' });
    assert.equal(f.el('browserSpeechFields').hidden, true);
    assert.equal(f.el('boxSpeechFields').hidden, false);
    assert.equal(f.el('windowsVoiceFields').hidden, false);
    assert.equal(f.el('speechVoiceFields').hidden, true);
    assert.equal(f.el('windowsVoice').value, 'en');
    assert(f.el('windowsVoice').options.some(option => option.value === 'box-zira' && option.textContent.startsWith('Microsoft Zira')));
    f.el('windowsVoice').value = 'box-zira';
    f.el('windowsVoice').handlers.get('change')();
    await f.save();
    assert.equal(f.saves.at(-1).voice, 'box-zira');
    assert.equal(f.saves.at(-1).instructionOutput, 'box');
    await f.voice.loadSettings();
    assert.equal(f.el('windowsVoice').value, 'box-zira');
    await f.el('testVoice').handlers.get('click')();
    assert.equal(f.audioPlayed, 1);
    assert.equal(f.spoken.length, 0);
    f.voice.stop();
});

test('a saved Windows voice name resolves to its installed ID without overwriting it with a language', async () => {
    const f = await fixture({ output: 'box', engine: 'windows', boxVoice: 'Microsoft Zira' });
    assert.equal(f.el('windowsVoice').value, 'box-zira');
    await f.save();
    assert.equal(f.saves.at(-1).voice, 'Microsoft Zira');
    f.voice.stop();
});

test('Windows voice discovery failure does not save a default over the existing voice', async () => {
    const f = await fixture({ output: 'box', engine: 'windows', boxVoice: 'Microsoft Zira', voicesFail: true });
    await f.save();
    assert.equal(f.saves.length, 0);
    assert.match(f.el('voiceSetupStatus').textContent, /Windows voices unavailable/);
    f.voice.stop();
});

test('failed settings saves preserve the selected voice and blocked storage is not reported as success', async () => {
    const f = await fixture();
    f.failSave();
    f.el('browserVoice').value = 'david';
    await f.save();
    assert.equal(f.remembered, undefined);
    await f.voice.response('Keep the previously saved Zira voice.');
    assert.equal(f.spoken.at(-1).voice.voiceURI, 'zira');
    f.voice.stop();
    const blocked = await fixture();
    blocked.failStorage();
    await blocked.save();
    assert.match(blocked.el('voiceSetupStatus').textContent, /could not remember its voice/);
    assert.equal(blocked.el('voiceSettings').open, true);
    assert(!blocked.notices.includes('Voice settings saved.'));
    blocked.voice.stop();
});
