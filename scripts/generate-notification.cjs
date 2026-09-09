// Original synthesized notification: no sampled soundtrack audio.
// Run from any directory with Node.js to reproduce notification.wav.
const fs = require('node:fs');
const path = require('node:path');
const rate = 44100;
const frequencies = [659.25, 523.25, 783.99, 659.25];
const segment = rate / 4;
const samples = frequencies.length * segment;
const data = Buffer.alloc(44 + samples * 2);
data.write('RIFF', 0); data.writeUInt32LE(data.length - 8, 4);
data.write('WAVEfmt ', 8); data.writeUInt32LE(16, 16);
data.writeUInt16LE(1, 20); data.writeUInt16LE(1, 22);
data.writeUInt32LE(rate, 24); data.writeUInt32LE(rate * 2, 28);
data.writeUInt16LE(2, 32); data.writeUInt16LE(16, 34);
data.write('data', 36); data.writeUInt32LE(samples * 2, 40);
for (let i = 0; i < samples; i++) {
  const t = (i % segment) / rate;
  const envelope = Math.min(1, t / .01) * Math.max(0, Math.min(1, (.20 - t) / .04));
  const sample = .18 * envelope * Math.sin(2 * Math.PI * frequencies[Math.floor(i / segment)] * t);
  data.writeInt16LE(Math.round(sample * 32767), 44 + i * 2);
}
fs.writeFileSync(path.join(__dirname, '..', 'notification.wav'), data);
