'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const TimerUtils = require('../timer-utils.js');

const source = fs.readFileSync(path.join(__dirname, '..', 'settings.js'), 'utf8');

async function createHarness(seed = {}, result = {}) {
  let ready;
  const elements = new Map();
  const stored = {
    sheetUrl: 'https://docs.google.com/spreadsheets/d/synthetic-spreadsheet-id-for-tests/edit',
    webAppUrl: 'https://script.google.com/macros/s/test-deployment/exec',
    apiToken: 'test-token-with-at-least-16-characters',
    sheetName: 'Template', ...seed
  };
  const document = {
    addEventListener(_event, callback) { ready = callback; },
    getElementById(id) {
      if (!elements.has(id)) elements.set(id, {
        value: '', listeners: {}, disabled: false, required: false,
        addEventListener(event, callback) { this.listeners[event] = callback; }
      });
      return elements.get(id);
    }
  };
  const chrome = {
    storage: { local: {
      async get() { return stored; },
      async set(value) { Object.assign(stored, value); },
      async remove() {}
    } },
    runtime: {
      lastError: null,
      sendMessage(_message, callback) { callback({ success: true, data: result }); }
    }
  };
  vm.runInNewContext(source, { TimerUtils, document, chrome });
  ready();
  await Promise.resolve();
  return { stored, element: (id) => elements.get(id) };
}

test('settings default to automatic dates even with a legacy Template setting', async () => {
  const harness = await createHarness();
  assert.equal(harness.element('sheetMode').value, 'date');
  assert.equal(harness.element('sheetName').disabled, true);
  assert.equal(harness.element('sheetName').required, false);
  await harness.element('settingsForm').listeners.submit({ preventDefault() {} });
  assert.equal(harness.stored.sheetMode, 'date');
  assert.equal(harness.stored.apiToken, 'test-token-with-at-least-16-characters');
});

test('fixed-tab settings are retained and require a name', async () => {
  const harness = await createHarness({ sheetMode: 'fixed', sheetName: 'Custom' });
  assert.equal(harness.element('sheetName').disabled, false);
  assert.equal(harness.element('sheetName').required, true);
  await harness.element('settingsForm').listeners.submit({ preventDefault() {} });
  assert.equal(harness.stored.sheetName, 'Custom');
  assert.equal(harness.stored.sheetMode, 'fixed');
  harness.element('sheetName').value = '';
  await harness.element('settingsForm').listeners.submit({ preventDefault() {} });
  assert.match(harness.element('status').textContent, /Enter the target tab name/);
});

test('date connection test explains that a missing daily tab will be copied on first send', async () => {
  const harness = await createHarness({}, {
    target: 'Time (Daily) / 09/06/2026', willCreate: true, template: 'Temp'
  });
  await harness.element('test').listeners.click();
  assert.match(harness.element('status').textContent, /09\/06\/2026/);
  assert.match(harness.element('status').textContent, /created from Temp when you send your first reflection/);
});
