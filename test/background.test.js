'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const TimerUtils = require('../timer-utils.js');

const backgroundSource = fs.readFileSync(path.join(__dirname, '..', 'background.js'), 'utf8');

function createEvent() {
  const listeners = [];
  return {
    addListener(listener) { listeners.push(listener); },
    listeners
  };
}

function createHarness(seed = {}, options = {}) {
  const stored = { ...seed };
  const clearedAlarms = [];
  const createdAlarms = [];
  const injectedScripts = [];
  const removedStorageKeys = [];
  let contentScriptInjected = false;
  let tabMessageCount = 0;
  const runtimeOnMessage = createEvent();
  const chrome = {
    storage: {
      local: {
        async get(keys) {
          return Object.fromEntries(keys.filter((key) => key in stored).map((key) => [key, stored[key]]));
        },
        async set(values) { Object.assign(stored, values); },
        async remove(keys) {
          for (const key of Array.isArray(keys) ? keys : [keys]) {
            removedStorageKeys.push(key);
            delete stored[key];
          }
        }
      }
    },
    alarms: {
      async create(name, options) { createdAlarms.push({ name, options }); },
      async clear(name) { clearedAlarms.push(name); return true; },
      onAlarm: createEvent()
    },
    runtime: {
      lastError: null,
      onMessage: runtimeOnMessage,
      onInstalled: createEvent(),
      sendMessage(_message, callback) { if (callback) callback(); }
    },
    tabs: {
      async query() { return options.tabs || []; },
      sendMessage(_tabId, _message, callback) {
        tabMessageCount += 1;
        if (options.missingReceiverUntilInjected && !contentScriptInjected) {
          chrome.runtime.lastError = { message: 'Could not establish connection. Receiving end does not exist.' };
          if (callback) callback();
          chrome.runtime.lastError = null;
          return;
        }
        if (callback) callback({ success: true });
      },
      onActivated: createEvent(),
      onUpdated: createEvent()
    },
    scripting: {
      async executeScript(details) {
        injectedScripts.push(details);
        if (options.injectionFails) {
          throw new Error('Cannot access this page');
        }
        contentScriptInjected = true;
        return [];
      }
    },
    notifications: {
      create() {},
      onClicked: createEvent()
    }
  };

  const context = {
    AbortController,
    URL,
    TimerUtils,
    chrome,
    console,
    fetch: async () => { throw new Error('Unexpected fetch'); },
    importScripts() {},
    setTimeout,
    clearTimeout
  };
  vm.runInNewContext(backgroundSource, context, { filename: 'background.js' });

  async function dispatch(message) {
    const listener = runtimeOnMessage.listeners[0];
    assert.ok(listener, 'background registered a message listener');
    return new Promise((resolve, reject) => {
      const timeout = setTimeout(() => reject(new Error('Message response timed out')), 1000);
      const keepAlive = listener(message, {}, (response) => {
        clearTimeout(timeout);
        resolve(response);
      });
      assert.equal(keepAlive, true);
    });
  }

  return {
    chrome,
    createdAlarms,
    clearedAlarms,
    dispatch,
    injectedScripts,
    removedStorageKeys,
    stored,
    get tabMessageCount() { return tabMessageCount; }
  };
}

test('background initializes a 25-minute timer and removes legacy secrets', async () => {
  const oldSheetUrl = 'https://docs.google.com/spreadsheets/d/synthetic-spreadsheet-id-for-tests/edit';
  const harness = createHarness({
    GOOGLE_SHEETS_PRIVATE_KEY: 'old-secret',
    sheetsLink: oldSheetUrl
  });
  const response = await harness.dispatch({ action: 'getTimerState' });
  assert.equal(response.success, true);
  assert.equal(response.apiVersion, 3);
  assert.equal(response.state.durationSeconds, 1500);
  assert.equal(response.state.remainingSeconds, 1500);
  assert.equal(response.state.isRunning, false);
  assert.ok(harness.removedStorageKeys.includes('GOOGLE_SHEETS_PRIVATE_KEY'));
  assert.equal('GOOGLE_SHEETS_PRIVATE_KEY' in harness.stored, false);
  assert.equal(harness.stored.sheetUrl, oldSheetUrl);
  assert.equal('sheetsLink' in harness.stored, false);
});

test('start, pause, and resume use deadline-backed state', async () => {
  const harness = createHarness();
  const started = await harness.dispatch({ action: 'startTimer', durationSeconds: 90, autoRestart: true });
  assert.equal(started.success, true);
  assert.equal(started.state.isRunning, true);
  assert.equal(started.state.durationSeconds, 90);
  assert.ok(started.state.endTime > Date.now());

  const paused = await harness.dispatch({ action: 'pauseTimer' });
  assert.equal(paused.state.isRunning, false);
  assert.ok(paused.state.remainingSeconds >= 89 && paused.state.remainingSeconds <= 90);

  const resumed = await harness.dispatch({ action: 'resumeTimer', autoRestart: false });
  assert.equal(resumed.state.isRunning, true);
  assert.equal(resumed.state.durationSeconds, 90);
  assert.equal(resumed.state.autoRestart, false);
});

test('clearing a scheduled start only clears the extension schedule alarm', async () => {
  const harness = createHarness();
  const targetTime = new Date(Date.now() + 60_000).toISOString();
  const scheduled = await harness.dispatch({
    action: 'scheduleTimer',
    targetTime,
    durationSeconds: 300,
    autoRestart: false
  });
  assert.equal(scheduled.success, true);
  assert.equal(scheduled.state.scheduledTimer.durationSeconds, 300);

  const cleared = await harness.dispatch({ action: 'clearScheduledTimer' });
  assert.equal(cleared.state.scheduledTimer, null);
  assert.ok(harness.clearedAlarms.every((name) => typeof name === 'string'));
  assert.ok(harness.clearedAlarms.includes('reflectionTimerScheduledStart'));
});

test('background discards malformed stored schedule state during startup', async () => {
  const harness = createHarness({ scheduledTimerV2: { targetTime: 'not-a-time' } });
  const response = await harness.dispatch({ action: 'getTimerState' });
  assert.equal(response.success, true);
  assert.equal(response.state.scheduledTimer, null);
  assert.equal('scheduledTimerV2' in harness.stored, false);
});

test('test prompt injects the content script when an older tab has no receiver', async () => {
  const harness = createHarness({}, {
    tabs: [{ id: 42, active: true, url: 'https://example.com/' }],
    missingReceiverUntilInjected: true
  });

  const response = await harness.dispatch({ action: 'showTestPrompt' });

  assert.equal(response.success, true);
  assert.equal(harness.tabMessageCount, 2);
  assert.equal(harness.injectedScripts.length, 1);
  assert.equal(harness.injectedScripts[0].target.tabId, 42);
  assert.equal(harness.injectedScripts[0].files.length, 1);
  assert.equal(harness.injectedScripts[0].files[0], 'content.js');
});

test('test prompt returns useful guidance when Chrome blocks the page', async () => {
  const harness = createHarness({}, {
    tabs: [{ id: 42, active: true, url: 'https://chromewebstore.google.com/' }],
    missingReceiverUntilInjected: true,
    injectionFails: true
  });

  const response = await harness.dispatch({ action: 'showTestPrompt' });

  assert.equal(response.success, false);
  assert.match(response.error, /open or refresh a normal website/i);
  assert.doesNotMatch(response.error, /receiving end does not exist/i);
});

test('test prompt explains that Chrome internal pages are unsupported', async () => {
  const harness = createHarness({}, {
    tabs: [{ id: 42, active: true, url: 'chrome://extensions/' }]
  });

  const response = await harness.dispatch({ action: 'showTestPrompt' });

  assert.equal(response.success, false);
  assert.match(response.error, /not a chrome:\/\/ page/i);
  assert.equal(harness.tabMessageCount, 0);
  assert.equal(harness.injectedScripts.length, 0);
});
