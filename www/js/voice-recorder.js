// (c) J@mBeL.net 2010-2026, John Ambeliotis. Part of jaNET Framework, GNU GPL version 3 or later; see LICENSE.
class VoiceRecorder extends AudioWorkletProcessor {
    process(inputs) {
        if (inputs[0]?.[0]) this.port.postMessage(inputs[0][0]);
        return true;
    }
}
registerProcessor('voice-recorder', VoiceRecorder);
