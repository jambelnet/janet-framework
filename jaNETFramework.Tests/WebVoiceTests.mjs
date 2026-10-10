// (c) J@mBeL.net 2010-2026, John Ambeliotis. Part of jaNET Framework, GNU GPL version 3 or later; see LICENSE.
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
const source = await readFile(new URL('../www/js/voice.js', import.meta.url), 'utf8');
const { encodeWave } = await import('data:text/javascript;base64,' + Buffer.from(source).toString('base64'));
const view = new DataView(encodeWave(new Float32Array([-2, -1, -.5, 0, .5, 1, 2])));
assert.equal(view.byteLength, 58);
assert.equal(view.getUint32(24, true), 16000);
assert.equal(view.getUint16(22, true), 1);
assert.equal(view.getUint16(34, true), 16);
assert.equal(view.getUint32(40, true), 14);
assert.deepEqual(Array.from({ length: 7 }, (_, index) => view.getInt16(44 + index * 2, true)), [-32768, -32768, -16384, 0, 16384, 32767, 32767]);
assert(!source.includes('SpeechRecognition('));
assert(source.includes('if (cancel || generation !== captureGeneration || document.hidden)'));
assert(source.includes('checkActive();'));
assert(!source.includes('stopVoiceBtn'));
assert(source.includes("playbackActive ? stop() : say(responseText)"));
assert(source.includes("byId('cancelCaptureBtn').addEventListener('click', cancelCapture)"));
console.log('Voice WAV encoding, clipping, local recognition, and cancellation checks passed.');
