// (c) J@mBeL.net 2010-2026, John Ambeliotis. Part of jaNET Framework, GNU GPL version 3 or later; see LICENSE.
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

class Element {
    value = '';
    options = [];
    handlers = new Map();
    checked = false;
    hidden = false;
    disabled = false;
    textContent = '';
    addEventListener(name, handler) { this.handlers.set(name, handler); }
    setAttribute() {}
    querySelector() { return { setAttribute() {} }; }
    replaceChildren() { this.options = []; }
    append(option) { this.options.push(option); }
}

const elements = new Map();
const documentHandlers = new Map();
globalThis.document = {
    hidden: false,
    getElementById(id) { if (!elements.has(id)) elements.set(id, new Element()); return elements.get(id); },
    createElement() { return new Element(); },
    addEventListener(name, handler) { documentHandlers.set(name, handler); }
};
const spoken = [];
let cancels = 0;
globalThis.window = {
    speechSynthesis: {
        getVoices() { return [{ name: 'Local English', voiceURI: 'local-en', lang: 'en-US', localService: true }]; },
        cancel() { cancels++; },
        speak(utterance) { spoken.push(utterance); },
        addEventListener() {}
    },
    addEventListener() {}
};
globalThis.SpeechSynthesisUtterance = class { constructor(text) { this.text = text; } };
Object.defineProperty(globalThis, 'navigator', { value: { language: 'en-US', userActivation: { hasBeenActive: true } }, configurable: true });
globalThis.localStorage = { getItem() { return null; }, setItem() {} };
let tick;
globalThis.setInterval = callback => { tick = callback; return 1; };
globalThis.clearInterval = () => {};
let muted = false;
let audioPlayed = 0;
globalThis.Audio = class {
    async play() { audioPlayed++; }
    pause() {}
};
const messages = [];
globalThis.fetch = async url => {
    if (String(url).endsWith('/synthesize')) return { ok: true, async blob() { return new Blob(['wave-test']); } };
    const json = String(url).includes('/events')
        ? (() => {
            const query = new URL(String(url), 'http://localhost/www/').searchParams;
            return { session: 'test', lastId: messages.length, muted,
                events: query.get('session') === 'test' && !muted ? messages.filter(item => item.id > Number(query.get('after'))) : [] };
        })()
        : { muted, settings: { engine: 'auto' }, recognitionReady: false, synthesisReady: true };
    return { ok: true, async json() { return json; } };
};
const source = await readFile(new URL('../www/js/voice.js', import.meta.url), 'utf8');
const { wireVoice } = await import('data:text/javascript;base64,' + Buffer.from(source).toString('base64'));
const errors = [];
const voice = wireVoice(() => {}, text => errors.push(text));
const settle = async () => { for (let i = 0; i < 10; i++) await Promise.resolve(); };
await voice.eventsReady;
await voice.loadSettings();
assert.equal(elements.get('voiceEvents').checked, true);
assert.equal(elements.get('voiceReplies').checked, true);
const publish = text => messages.push({ id: messages.length + 1, text, created: new Date().toISOString() });
publish('Good morning.'); publish('Today is cloudy.');
await tick(); await settle();
assert.deepEqual(spoken.map(item => item.text), ['Good morning.']);
// A generic command response must not cut off the event greeting.
await voice.response('Operation completed: checkin.', true);
spoken[0].onend(); await settle();
assert.deepEqual(spoken.map(item => item.text), ['Good morning.', 'Today is cloudy.']);
spoken[1].onend(); await settle();
publish('Goodbye.'); await tick(); await settle();
assert.equal(spoken[2].text, 'Goodbye.');
publish('Pending greeting.'); await tick();
muted = true; await tick(); await settle();
assert.equal(elements.get('speakBtn').disabled, true);
muted = false; await tick(); await settle();
assert.equal(spoken.length, 3); // Unmuting does not replay discarded messages.
assert.equal(elements.get('speakBtn').disabled, false);
document.hidden = true; publish('While hidden.'); await tick();
document.hidden = false; await tick(); await settle();
assert.equal(spoken.length, 3);
navigator.userActivation.hasBeenActive = false;
publish('After interaction.'); await tick(); await settle();
assert.equal(spoken.length, 3);
navigator.userActivation.hasBeenActive = true;
documentHandlers.get('pointerdown')(); await settle();
assert.equal(spoken[3].text, 'After interaction.');
voice.stop(); await settle();
window.speechSynthesis.getVoices = () => [];
await voice.response('Use the configured server voice when this browser has no local voices.');
assert.equal(audioPlayed, 1);
voice.stop();
assert(cancels > 0);
assert.deepEqual(errors, []);
console.log('Browser event greetings: ordered local playback, responses, mute, hidden pages and interaction passed.');
