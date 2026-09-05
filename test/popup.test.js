'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const TimerUtils = require('../timer-utils.js');

const popupSource = fs.readFileSync(path.join(__dirname, '..', 'popup.js'), 'utf8');
const diagnosticClientSource = fs.readFileSync(path.join(__dirname, '..', 'diagnostic-client.js'), 'utf8');

function createElement(id, document) {
  const listeners = new Map();
  return {
    id,
    value: id === 'minutes' ? '25' : (id === 'volume' ? '50' : '0'),
    max: id === 'hours' ? '8760' : '59',
    textContent: '',
    className: '',
    disabled: false,
    hidden: false,
    open: false,
    selectCount: 0,
    addEventListener(type, callback) { listeners.set(type, callback); },
    click() { listeners.get('click')?.({}); },
    focus() { document.activeElement = this; },
    select() { this.selectCount += 1; },
    listeners
  };
}

function createHarness(getStateResponse) {
  let domReady;
  let reloadCount = 0;
  const messages = [];
  const document = {
    activeElement: null,
    elements: new Map(),
    addEventListener(type, callback) {
      if (type === 'DOMContentLoaded') domReady = callback;
    },
    getElementById(id) { return this.elements.get(id); }
  };
  for (const id of [
    'hours', 'minutes', 'seconds', 'display', 'timerStatus', 'startPause', 'reset',
    'autoRestart', 'volume', 'volumeValue', 'startTime', 'schedule', 'cancelSchedule',
    'scheduleStatus', 'schedulePanel', 'openSettings', 'testPrompt', 'currentTime', 'status'
  ]) {
    document.elements.set(id, createElement(id, document));
  }
  document.elements.get('autoRestart').checked = false;

  const chrome = {
    runtime: {
      lastError: null,
      onMessage: { addListener() {} },
      openOptionsPage() {},
      reload() { reloadCount += 1; },
      sendMessage(message, callback) {
        messages.push(message);
        if (message.action === 'getTimerState') callback(getStateResponse);
        else callback({ success: true, state: getStateResponse.state });
      }
    },
    storage: {
      local: {
        async get() { return {}; },
        async set() {},
        async remove() {}
      }
    }
  };
  const context = {
    TimerUtils,
    chrome,
    console,
    document,
    window: { close() {} },
    requestAnimationFrame(callback) { callback(); },
    setInterval() { return 1; },
    setTimeout,
    clearTimeout
  };
  const sandbox = vm.createContext(context);
  vm.runInContext(diagnosticClientSource, sandbox);
  vm.runInContext(popupSource, sandbox, { filename: 'popup.js' });
  domReady();
  return {
    document,
    messages,
    element: (id) => document.elements.get(id),
    get reloadCount() { return reloadCount; }
  };
}

test('popup immediately focuses and selects the hours field', async () => {
  const harness = createHarness({
    success: true,
    apiVersion: 6,
    state: {
      isRunning: false,
      durationSeconds: 1500,
      remainingSeconds: 1500,
      endTime: null,
      autoRestart: false,
      promptActive: false,
      scheduledTimer: null
    }
  });
  await new Promise((resolve) => setTimeout(resolve, 0));
  assert.equal(harness.document.activeElement, harness.element('hours'));
  assert.ok(harness.element('hours').selectCount >= 1);
  assert.equal(harness.element('timerStatus').textContent, 'Ready');
});

test('typing hours clears the untouched 25-minute preset', async () => {
  const harness = createHarness({
    success: true,
    apiVersion: 6,
    state: {
      isRunning: false,
      durationSeconds: 1500,
      remainingSeconds: 1500,
      endTime: null,
      autoRestart: false,
      promptActive: false,
      scheduledTimer: null
    }
  });

  harness.element('hours').value = '1';
  harness.element('hours').listeners.get('input')({});

  assert.equal(harness.element('minutes').value, '0');
  assert.equal(harness.element('display').textContent, '1:00:00');

  await new Promise((resolve) => setTimeout(resolve, 0));
  assert.equal(harness.element('hours').value, '1');
  assert.equal(harness.element('minutes').value, '0');
});

test('popup reloads the extension when an old service worker responds', async () => {
  const harness = createHarness({ isRunning: false, timeLeft: 1500 });
  await new Promise((resolve) => setTimeout(resolve, 300));
  assert.equal(harness.element('timerStatus').textContent, 'Finishing update…');
  assert.equal(harness.reloadCount, 1);
  assert.ok(harness.messages.some((message) => message.action === 'recordDiagnostic' && message.event === 'popup.versionMismatch'));
});

test('popup records loading and committed duration changes, not each keystroke or display tick', async () => {
  const harness = createHarness({ success: true, apiVersion: 6, state: {
    isRunning: false, durationSeconds: 1500, remainingSeconds: 1500, endTime: null,
    autoRestart: false, promptActive: false, scheduledTimer: null
  } });
  await new Promise((resolve) => setImmediate(resolve));
  let events = harness.messages.filter((message) => message.action === 'recordDiagnostic');
  assert.ok(events.some((message) => message.event === 'popup.opened'));
  assert.ok(events.some((message) => message.event === 'popup.loaded'));
  const before = events.length;
  harness.element('hours').value = '1';
  harness.element('hours').listeners.get('input')({});
  assert.equal(harness.messages.filter((message) => message.action === 'recordDiagnostic').length, before);
  await harness.element('hours').listeners.get('change')({});
  events = harness.messages.filter((message) => message.action === 'recordDiagnostic');
  assert.equal(events.at(-1).event, 'popup.durationChanged');
  assert.equal(events.at(-1).details.requestedDurationSeconds, 3600);
});
