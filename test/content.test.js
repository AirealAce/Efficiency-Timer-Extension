'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const source = fs.readFileSync(path.join(__dirname, '..', 'content.js'), 'utf8');
const diagnosticClientSource = fs.readFileSync(path.join(__dirname, '..', 'diagnostic-client.js'), 'utf8');

function createHarness(savedVolume = 50) {
  const elements = [];
  const messages = [];
  const playedVolumes = [];
  let listener;
  function element(tag) {
    const value = { tag, children: [], listeners: {}, value: '', textContent: '',
      style: { setProperty() {} }, classList: { add() {} },
      setAttribute(name, v) { this[name] = v; },
      append(...children) { this.children.push(...children); },
      appendChild(child) { this.children.push(child); },
      attachShadow() { this.shadow = element('shadow'); return this.shadow; },
      addEventListener(event, callback) { this.listeners[event] = callback; },
      remove() { this.removed = true; }, focus() {}
    };
    elements.push(value);
    return value;
  }
  const body = element('body');
  const documentEvents = {};
  const pageEvents = {};
  const document = { hidden: true, body, createElement: element,
    addEventListener(name, callback) { documentEvents[name] = callback; },
    getElementById: (id) => elements.find((e) => e.id === id && !e.removed) };
  const chrome = {
    storage: { local: { async get() { return { sfxVolume: savedVolume }; } } },
    runtime: {
      getURL(file) { return `chrome-extension://test-extension/${file}`; },
      onMessage: { addListener(fn) { listener = fn; } },
      sendMessage(message, callback) {
        messages.push(message);
        callback({ success: true, data: { sheet: message.isTest ? 'test' : '9/5/26' } });
      }
    }
  };
  const context = vm.createContext({ chrome, document,
    Audio: class { async play() { playedVolumes.push(this.volume); } },
    setTimeout() {}, addEventListener(name, callback) { pageEvents[name] = callback; } });
  vm.runInContext(diagnosticClientSource, context);
  vm.runInContext(source, context);
  return {
    elements, messages, document, documentEvents, pageEvents, playedVolumes,
    show(isTest, options = {}) { let result; listener({ action: 'showReflectionPrompt', isTest, ...options }, {}, (r) => { result = r; }); return result; }
  };
}

test('test dialog labels the test tab and passes test mode all the way to saving', async () => {
  const harness = createHarness();
  harness.show(true);
  assert.ok(harness.elements.some((e) => e.textContent.includes('saved to the test tab')));
  harness.elements.find((e) => e.tag === 'textarea').value = 'Test message';
  await harness.elements.find((e) => e.className === 'submit').listeners.click();
  const saved = harness.messages.find((m) => m.action === 'saveReflection');
  assert.equal(saved.isTest, true);
  assert.ok(harness.elements.some((e) => e.textContent === 'Saved to test.'));
});

test('normal prompts keep normal routing and are never silently relabeled as tests', async () => {
  const harness = createHarness();
  harness.show(false);
  const blocked = harness.show(true);
  assert.equal(blocked.success, false);
  assert.match(blocked.error, /real reflection is already open/);
  harness.elements.find((e) => e.tag === 'textarea').value = 'Real message';
  await harness.elements.find((e) => e.className === 'submit').listeners.click();
  assert.equal(harness.messages.find((m) => m.action === 'saveReflection').isTest, false);
});

test('prompt and visibility diagnostics never capture reflection text or typing', async () => {
  const harness = createHarness();
  harness.show(true);
  const textarea = harness.elements.find((e) => e.tag === 'textarea');
  textarea.value = 'PRIVATE REFLECTION TO EXCLUDE';
  const countBeforeTyping = harness.messages.filter((m) => m.action === 'recordDiagnostic').length;
  textarea.listeners.input();
  assert.equal(harness.messages.filter((m) => m.action === 'recordDiagnostic').length, countBeforeTyping);
  harness.documentEvents.visibilitychange();
  harness.pageEvents.pageshow({ persisted: true });
  harness.pageEvents.pagehide({ persisted: false });
  await harness.elements.find((e) => e.className === 'submit').listeners.click();
  const events = harness.messages.filter((m) => m.action === 'recordDiagnostic');
  for (const event of ['prompt.shown', 'sound.result', 'page.visibility', 'page.shown', 'page.hidden', 'prompt.submit', 'prompt.saved']) {
    assert.ok(events.some((m) => m.event === event), event);
  }
  assert.doesNotMatch(JSON.stringify(events), /PRIVATE REFLECTION/);
  const visibility = events.find((m) => m.event === 'page.visibility');
  assert.equal(visibility.details.hasPrompt, true);
  assert.equal(visibility.details.visibility, 'hidden');
});

test('per-session sound overrides a pages cached global volume, including mute', async () => {
  const audible = createHarness(10);
  audible.document.hidden = false;
  audible.show(false, { sfxVolume: 80 });
  await new Promise((resolve) => setImmediate(resolve));
  assert.deepEqual(audible.playedVolumes, [0.8]);
  const muted = createHarness(100);
  muted.document.hidden = false;
  muted.show(false, { sfxVolume: 0 });
  await new Promise((resolve) => setImmediate(resolve));
  assert.deepEqual(muted.playedVolumes, []);
  assert.ok(muted.messages.some((message) => message.event === 'sound.result' && message.details.outcome === 'muted'));
});
