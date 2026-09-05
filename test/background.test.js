'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const TimerUtils = require('../timer-utils.js');
const TimerDiagnostics = require('../diagnostics.js');

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
  const activeAlarms = new Map();
  const injectedScripts = [];
  const removedStorageKeys = [];
  let contentScriptInjected = false;
  let tabMessageCount = 0;
  const runtimeOnMessage = createEvent();
  let optionsPageOpenCount = 0;
  const chrome = {
    storage: {
      local: {
        async get(keys) {
          return Object.fromEntries(keys.filter((key) => key in stored).map((key) => [key, stored[key]]));
        },
        async set(values) {
          if (options.diagnosticStorageFails && TimerDiagnostics.STORAGE_KEY in values) throw new Error('Storage quota exceeded');
          Object.assign(stored, structuredClone(values));
        },
        async remove(keys) {
          for (const key of Array.isArray(keys) ? keys : [keys]) {
            removedStorageKeys.push(key);
            delete stored[key];
          }
        }
      }
    },
    alarms: {
      async create(name, options) { createdAlarms.push({ name, options }); activeAlarms.set(name, { name, scheduledTime: options.when }); },
      async clear(name) { clearedAlarms.push(name); activeAlarms.delete(name); return true; },
      async getAll() { return [...activeAlarms.values()]; },
      onAlarm: createEvent()
    },
    runtime: {
      id: 'test-extension',
      getURL(file) { return `chrome-extension://test-extension/${file}`; },
      getManifest() { return { version: '2.4.0' }; },
      lastError: null,
      onMessage: runtimeOnMessage,
      onInstalled: createEvent(),
      async openOptionsPage() { optionsPageOpenCount += 1; },
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
      onUpdated: createEvent(),
      onRemoved: createEvent()
    },
    windows: { onFocusChanged: createEvent() },
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
    TimerDiagnostics,
    chrome,
    console,
    fetch: options.fetch || (async () => { throw new Error('Unexpected fetch'); }),
    importScripts() {},
    setTimeout,
    clearTimeout
  };
  vm.runInNewContext(backgroundSource, context, { filename: 'background.js' });

  async function dispatch(message, sender = { id: 'test-extension', url: 'chrome-extension://test-extension/popup.html' }) {
    const listener = runtimeOnMessage.listeners[0];
    assert.ok(listener, 'background registered a message listener');
    return new Promise((resolve, reject) => {
      const timeout = setTimeout(() => reject(new Error('Message response timed out')), 1000);
      const keepAlive = listener(message, sender, (response) => {
        clearTimeout(timeout);
        resolve(response);
      });
      assert.equal(keepAlive, true);
    });
  }

  return {
    chrome,
    activeAlarms,
    createdAlarms,
    clearedAlarms,
    dispatch,
    injectedScripts,
    removedStorageKeys,
    stored,
    get optionsPageOpenCount() { return optionsPageOpenCount; },
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
  assert.equal(response.apiVersion, 6);
  assert.equal(response.state.durationSeconds, 1500);
  assert.equal(response.state.remainingSeconds, 1500);
  assert.equal(response.state.isRunning, false);
  assert.ok(harness.removedStorageKeys.includes('GOOGLE_SHEETS_PRIVATE_KEY'));
  assert.equal('GOOGLE_SHEETS_PRIVATE_KEY' in harness.stored, false);
  assert.equal(harness.stored.sheetUrl, oldSheetUrl);
  assert.equal('sheetsLink' in harness.stored, false);
});

test('reflection submissions and connection tests default to date mode with local send time', async () => {
  const requests = [];
  const harness = createHarness({
    sheetName: 'Template', apiToken: 'test-token-with-at-least-16-characters',
    webAppUrl: 'https://script.google.com/macros/s/test-deployment/exec'
  }, {
    async fetch(_url, options) {
      requests.push(JSON.parse(options.body));
      return { ok: true, text: async () => JSON.stringify({ success: true }) };
    }
  });
  const beforeSend = Date.now();
  assert.equal((await harness.dispatch({ action: 'saveReflection', message: 'Today' })).success, true);
  assert.equal((await harness.dispatch({ action: 'testSheetsConnection' })).success, true);
  assert.equal(requests.length, 2);
  for (const payload of requests) {
    assert.equal(payload.sheetMode, 'date');
    assert.ok(new Date(payload.submittedAt).getTime() >= beforeSend);
    assert.equal(payload.timezoneOffsetMinutes, new Date(payload.submittedAt).getTimezoneOffset());
  }
});

test('explicit fixed-tab mode is preserved in the receiver request', async () => {
  let payload;
  const harness = createHarness({
    sheetMode: 'fixed', sheetName: 'Custom', apiToken: 'test-token-with-at-least-16-characters',
    webAppUrl: 'https://script.google.com/macros/s/test-deployment/exec'
  }, {
    async fetch(_url, options) {
      payload = JSON.parse(options.body);
      return { ok: true, text: async () => JSON.stringify({ success: true }) };
    }
  });
  assert.equal((await harness.dispatch({ action: 'testSheetsConnection' })).success, true);
  assert.equal(payload.sheetMode, 'fixed');
  assert.equal(payload.sheetName, 'Custom');
});

test('test submissions forward isTest without dismissing a pending real reflection', async () => {
  let payload;
  const harness = createHarness({
    timerStateV2: { durationSeconds: 1500, promptActive: true },
    apiToken: 'test-token-with-at-least-16-characters',
    webAppUrl: 'https://script.google.com/macros/s/test-deployment/exec'
  }, {
    async fetch(_url, options) {
      payload = JSON.parse(options.body);
      return { ok: true, text: async () => JSON.stringify({ success: true, sheet: 'test' }) };
    }
  });
  const result = await harness.dispatch({ action: 'saveReflection', message: 'Test', isTest: true });
  assert.equal(result.success, true);
  assert.equal(payload.isTest, true);
  assert.equal(harness.stored.timerStateV2.promptActive, true);
});

test('background persists the known Sheet and target tab as safe defaults', async () => {
  const harness = createHarness();

  await harness.dispatch({ action: 'getTimerState' });

  assert.equal(
    harness.stored.sheetUrl,
    'https://docs.google.com/spreadsheets/d/synthetic-spreadsheet-id-for-tests/edit'
  );
  assert.equal(harness.stored.sheetName, 'Template');
});

test('incomplete Sheets setup returns one actionable configuration error', async () => {
  const harness = createHarness();

  const response = await harness.dispatch({ action: 'testSheetsConnection' });

  assert.equal(response.success, false);
  assert.equal(response.errorCode, 'SETTINGS_REQUIRED');
  assert.match(response.error, /Apps Script deployment URL/i);
  assert.match(response.error, /Reflection API token/i);
  assert.doesNotMatch(response.error, /Google Sheet URL/i);
});

test('the reflection prompt can open extension settings', async () => {
  const harness = createHarness();

  const response = await harness.dispatch({ action: 'openSettings' });

  assert.equal(response.success, true);
  assert.equal(harness.optionsPageOpenCount, 1);
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
  assert.equal(harness.injectedScripts[0].files.join(','), 'diagnostic-client.js,content.js');
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

test('diagnostic issue markers include actual alarms and tab-switch evidence without changing timer state', async () => {
  const harness = createHarness({}, { tabs: [{ id: 7, windowId: 3, active: true, url: 'https://private.example/path?secret=123', title: 'PRIVATE TITLE' }] });
  const started = await harness.dispatch({ action: 'startTimer', durationSeconds: 600, autoRestart: false });
  harness.chrome.tabs.onActivated.listeners[0]({ tabId: 7, windowId: 3 });
  harness.chrome.tabs.onUpdated.listeners[0](7, { status: 'complete', url: 'https://private.example/path' }, {
    id: 7, active: true, windowId: 3, discarded: false, frozen: false, url: 'https://private.example/path'
  });
  harness.chrome.windows.onFocusChanged.listeners[0](3);
  const before = JSON.stringify(harness.stored.timerStateV2);
  const createdBefore = harness.createdAlarms.length;
  const marked = await harness.dispatch({ action: 'markDiagnosticIssue' });
  assert.equal(marked.marked, true);
  assert.equal(JSON.stringify(harness.stored.timerStateV2), before);
  assert.equal(harness.createdAlarms.length, createdBefore);
  assert.equal(marked.report.snapshot.checks.completionAlarmMissing, false);
  assert.equal(marked.report.snapshot.checks.completionAlarmAt, started.state.endTime);
  assert.equal(marked.report.snapshot.checks.activeTabId, 7);
  for (const name of ['worker.started', 'worker.restored', 'command.received', 'command.finished', 'tab.activated', 'tab.updated', 'window.focused', 'issue.marked']) {
    assert.ok(marked.report.events.some((event) => event.event === name), name);
  }
  assert.doesNotMatch(JSON.stringify(marked.report), /private\.example|PRIVATE TITLE|secret=123/);
  harness.activeAlarms.clear();
  const missing = await harness.dispatch({ action: 'getDiagnostics' });
  assert.equal(missing.report.snapshot.checks.completionAlarmMissing, true);
  assert.equal(harness.createdAlarms.length, createdBefore, 'export must not silently repair missing alarms');
});

test('export cannot include configuration, reflection contents, or server errors', async () => {
  const secret = 'PRIVATE_TOKEN_1234567890';
  const harness = createHarness({ apiToken: secret, webAppUrl: 'https://script.google.com/macros/s/PRIVATE_DEPLOYMENT/exec', sheetName: 'PRIVATE_SHEET' }, {
    async fetch() { return { ok: false, status: 403, async text() { return JSON.stringify({ success: false, error: 'PRIVATE_SERVER_RESPONSE' }); } }; }
  });
  await harness.dispatch({ action: 'saveReflection', message: 'PRIVATE_REFLECTION', isTest: true });
  const { report } = await harness.dispatch({ action: 'getDiagnostics' });
  assert.doesNotMatch(JSON.stringify(report), /PRIVATE_|script\.google|spreadsheets\/d/);
  const failed = report.events.find((event) => event.event === 'sheets.failed');
  assert.equal(failed.details.errorKind, 'rejected');
  assert.equal(failed.details.httpStatus, 403);
  assert.equal(failed.details.isTest, true);
});

test('missing content receiver and successful reinjection are visible in the diagnostic timeline', async () => {
  const harness = createHarness({}, { tabs: [{ id: 42, url: 'https://example.com', active: true }], missingReceiverUntilInjected: true });
  await harness.dispatch({ action: 'showTestPrompt' });
  const { report } = await harness.dispatch({ action: 'getDiagnostics' });
  assert.ok(report.events.some((event) => event.event === 'prompt.delivery' && event.details.errorKind === 'missing_receiver'));
  assert.ok(report.events.some((event) => event.event === 'prompt.injected' && event.details.success));
  assert.ok(report.events.some((event) => event.event === 'prompt.delivery' && event.details.success));
});

test('content scripts can log sanitized events but cannot read, erase, or change diagnostics', async () => {
  const harness = createHarness();
  await harness.dispatch({ action: 'getTimerState' });
  const sender = { id: 'test-extension', url: 'https://example.com', tab: { id: 42, windowId: 8 }, frameId: 0 };
  const accepted = await harness.dispatch({ action: 'recordDiagnostic', event: 'page.visibility', details: {
    visibility: 'hidden', hasPrompt: true, token: 'PRIVATE', source: 'settings', tabId: 123
  } }, sender);
  assert.equal(accepted.success, true);
  for (const action of ['getDiagnostics', 'markDiagnosticIssue', 'clearDiagnostics', 'setDiagnosticsEnabled']) {
    const denied = await harness.dispatch({ action, enabled: false }, sender);
    assert.equal(denied.success, false);
    assert.equal(denied.report, undefined);
  }
  const unknown = await harness.dispatch({ action: 'recordDiagnostic', event: 'issue.marked' }, sender);
  assert.equal(unknown.success, false);
  const external = await harness.dispatch({ action: 'getDiagnostics' }, { id: 'other-extension', url: 'chrome-extension://test-extension/popup.html' });
  assert.equal(external.success, false);
  const { report } = await harness.dispatch({ action: 'getDiagnostics' });
  const event = report.events.find((entry) => entry.event === 'page.visibility');
  assert.equal(event.details.source, 'content');
  assert.equal(event.details.tabId, 42);
  assert.equal(event.details.visibility, 'hidden');
  assert.doesNotMatch(JSON.stringify(report), /PRIVATE/);
});

test('logging storage failure does not prevent timer actions and is reported honestly', async () => {
  const harness = createHarness({}, { diagnosticStorageFails: true });
  const started = await harness.dispatch({ action: 'startTimer', durationSeconds: 120 });
  assert.equal(started.success, true);
  assert.equal(started.state.isRunning, true);
  const result = await harness.dispatch({ action: 'markDiagnosticIssue' });
  assert.equal(result.marked, false);
  assert.equal(result.report.storageAvailable, false);
  assert.equal((await harness.dispatch({ action: 'pauseTimer' })).state.isRunning, false);
});

test('disabled logs stay disabled after worker restarts and clearing does not erase timer/settings', async () => {
  const harness = createHarness({ apiToken: 'PRIVATE_TOKEN' });
  await harness.dispatch({ action: 'startTimer', durationSeconds: 120 });
  await harness.dispatch({ action: 'setDiagnosticsEnabled', enabled: false });
  await harness.dispatch({ action: 'clearDiagnostics' });
  const restored = createHarness(harness.stored);
  await restored.dispatch({ action: 'getTimerState' });
  const result = await restored.dispatch({ action: 'markDiagnosticIssue' });
  assert.equal(result.report.enabled, false);
  assert.equal(result.report.count, 0);
  assert.equal(result.marked, false);
  assert.equal(result.report.snapshot.timer.isRunning, true);
  assert.equal(restored.stored.apiToken, 'PRIVATE_TOKEN');
});

test('alarm lateness and completion are recorded without changing the normal completion workflow', async () => {
  const harness = createHarness({}, { tabs: [{ id: 9, url: 'https://example.com', active: true }] });
  await harness.dispatch({ action: 'startTimer', durationSeconds: 120 });
  harness.chrome.alarms.onAlarm.listeners[0]({ name: 'reflectionTimerComplete', scheduledTime: Date.now() - 4000 });
  await new Promise((resolve) => setImmediate(resolve));
  const { report } = await harness.dispatch({ action: 'getDiagnostics' });
  const event = report.events.find((entry) => entry.event === 'alarm.fired');
  assert.ok(event.details.lateByMs >= 4000);
  assert.equal(event.details.alarm, 'completion');
  assert.ok(report.events.some((entry) => entry.event === 'timer.completed'));
  assert.equal(report.snapshot.timer.isRunning, false);
  assert.equal(report.snapshot.timer.promptActive, true);
});
