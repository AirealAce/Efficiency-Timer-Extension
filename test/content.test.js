'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const source = fs.readFileSync(path.join(__dirname, '..', 'content.js'), 'utf8');

function createHarness() {
  const elements = [];
  const messages = [];
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
  const document = { hidden: true, body, createElement: element,
    getElementById: (id) => elements.find((e) => e.id === id && !e.removed) };
  const chrome = {
    storage: { local: { async get() { return {}; } } },
    runtime: {
      onMessage: { addListener(fn) { listener = fn; } },
      sendMessage(message, callback) {
        messages.push(message);
        callback({ success: true, data: { sheet: message.isTest ? 'test' : '9/5/26' } });
      }
    }
  };
  vm.runInNewContext(source, { chrome, document, setTimeout() {} });
  return {
    elements, messages,
    show(isTest) { let result; listener({ action: 'showReflectionPrompt', isTest }, {}, (r) => { result = r; }); return result; }
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
