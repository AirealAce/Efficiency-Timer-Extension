const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.join(__dirname, '..');

test('the extension notification is packaged and exposed to content scripts', () => {
  const manifest = JSON.parse(fs.readFileSync(path.join(root, 'manifest.json')));
  const source = fs.readFileSync(path.join(root, 'content.js'), 'utf8');
  const resource = source.match(/new Audio\(chrome\.runtime\.getURL\('([^']+)'\)\)/)?.[1];
  assert.ok(resource, 'content script must name its notification resource');
  assert.ok(manifest.web_accessible_resources.some(entry => entry.resources.includes(resource)));
  const audio = fs.readFileSync(path.join(root, resource));
  assert.equal(audio.toString('ascii', 0, 4), 'RIFF');
  assert.equal(audio.toString('ascii', 8, 12), 'WAVE');
  assert.equal(audio.readUInt32LE(4), audio.length - 8);
  assert.equal(audio.readUInt16LE(20), 1, 'PCM audio');
  assert.ok(audio.subarray(44).some(value => value !== 0), 'notification must contain sound');
});
