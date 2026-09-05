'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const source = fs.readFileSync(path.join(__dirname, '..', 'diagnostics-ui.js'), 'utf8');
const clientSource = fs.readFileSync(path.join(__dirname, '..', 'diagnostic-client.js'), 'utf8');

function createHarness({ popup = false, fails = false, storageAvailable = true } = {}) {
  const elements = {};
  const requests = [];
  const events = [];
  const links = [];
  const blobs = [];
  const revoked = [];
  const timeouts = [];
  const report = { enabled: true, storageAvailable, count: 1, maxEvents: 1200, retentionDays: 7,
    events: [{ at: Date.now(), event: 'tab.activated', details: { tabId: 5 }, timer: { isRunning: true, remainingSeconds: 50 } }],
    snapshot: { timer: { isRunning: true, durationSeconds: 60 } }
  };
  const ids = ['markDiagnosticIssue', 'diagnosticsStatus', ...(popup ? [] : [
    'diagnosticsEnabled', 'diagnosticsSummary', 'diagnosticsRecent', 'exportDiagnostics', 'clearDiagnostics', 'refreshDiagnostics'
  ])];
  for (const id of ids) elements[id] = { listeners: {}, addEventListener(name, fn) { this.listeners[name] = fn; } };
  let initialize;
  const document = {
    getElementById(id) { return elements[id] || null; },
    addEventListener(_name, callback) { initialize = callback; },
    createElement() {
      const link = { click() { this.clicked = true; }, remove() { this.removed = true; } };
      links.push(link);
      return link;
    },
    body: { appendChild() {} }
  };
  const window = { confirm() { return true; } };
  vm.runInNewContext(source, { document, window, Blob,
    URL: { createObjectURL(blob) { blobs.push(blob); return 'blob:local-report'; }, revokeObjectURL(url) { revoked.push(url); } },
    setTimeout(callback) { timeouts.push(callback); },
    TimerDiagnosticClient: {
      event(name) { events.push(name); },
      async request(action, extra) {
        requests.push({ action, extra });
        if (fails) throw new Error('Reload Reflection Timer to use diagnostics.');
        if (action === 'setDiagnosticsEnabled') report.enabled = extra.enabled;
        if (action === 'clearDiagnostics') { report.count = 0; report.events = []; }
        return { success: true, marked: report.enabled && storageAvailable, report };
      }
    }
  });
  initialize();
  return { elements, requests, events, links, blobs, revoked, timeouts, window, report };
}

test('popup issue marker is explicit, works without settings DOM, and reports disabled logging', async () => {
  const harness = createHarness({ popup: true });
  assert.equal(harness.requests.length, 0, 'no polling or extra snapshot on popup open');
  await harness.elements.markDiagnosticIssue.listeners.click();
  assert.equal(harness.requests[0].action, 'markDiagnosticIssue');
  assert.match(harness.elements.diagnosticsStatus.textContent, /Issue marked/);
  harness.report.enabled = false;
  await harness.elements.markDiagnosticIssue.listeners.click();
  assert.match(harness.elements.diagnosticsStatus.textContent, /could not be recorded/);
});

test('settings render recent events and export only the report to a local JSON download', async () => {
  const harness = createHarness();
  await new Promise((resolve) => setImmediate(resolve));
  assert.match(harness.elements.diagnosticsSummary.textContent, /Recording · 1 \/ 1200/);
  assert.match(harness.elements.diagnosticsRecent.textContent, /tab\.activated · tab 5 · running/);
  await harness.elements.exportDiagnostics.listeners.click();
  assert.equal(harness.links.length, 1);
  assert.equal(harness.links[0].href, 'blob:local-report');
  assert.match(harness.links[0].download, /^reflection-timer-diagnostics-.*\.json$/);
  assert.equal(harness.links[0].clicked, true);
  assert.equal(harness.links[0].removed, true);
  assert.equal(harness.blobs[0].type, 'application/json');
  assert.deepEqual(JSON.parse(await harness.blobs[0].text()), harness.report);
  harness.timeouts[0]();
  assert.deepEqual(harness.revoked, ['blob:local-report']);
  assert.ok(harness.requests.every((request) => request.action === 'getDiagnostics'));
});

test('pause and clear controls never mutate a timer or save connection settings', async () => {
  const harness = createHarness();
  await new Promise((resolve) => setImmediate(resolve));
  harness.elements.diagnosticsEnabled.checked = false;
  await harness.elements.diagnosticsEnabled.listeners.change();
  assert.equal(harness.report.enabled, false);
  harness.window.confirm = () => false;
  await harness.elements.clearDiagnostics.listeners.click();
  assert.equal(harness.report.count, 1);
  harness.window.confirm = () => true;
  await harness.elements.clearDiagnostics.listeners.click();
  assert.equal(harness.report.count, 0);
  assert.equal(harness.report.snapshot.timer.isRunning, true);
  assert.deepEqual(harness.requests.map((request) => request.action), ['getDiagnostics', 'setDiagnosticsEnabled', 'clearDiagnostics']);
});

test('unavailable diagnostics give actionable guidance and re-enable controls', async () => {
  const harness = createHarness({ fails: true });
  await new Promise((resolve) => setImmediate(resolve));
  assert.match(harness.elements.diagnosticsStatus.textContent, /Reload Reflection Timer/);
  assert.equal(harness.elements.exportDiagnostics.disabled, false);
  const failedStorage = createHarness({ storageAvailable: false });
  await new Promise((resolve) => setImmediate(resolve));
  assert.match(failedStorage.elements.diagnosticsStatus.textContent, /storage is unavailable/);
});

test('diagnostic client is best effort on invalidated pages and controls time out without retrying', async () => {
  let timeoutCallback;
  let calls = 0;
  const context = { chrome: { runtime: { sendMessage() { calls += 1; throw new Error('Extension context invalidated.'); } } },
    setTimeout(callback) { timeoutCallback = callback; return 1; }, clearTimeout() {} };
  vm.runInNewContext(clientSource, context);
  assert.doesNotThrow(() => context.TimerDiagnosticClient.event('page.hidden'));
  assert.equal(context.TimerDiagnosticClient.errorKind(new Error('Extension context invalidated.')), 'context_invalidated');
  await assert.rejects(context.TimerDiagnosticClient.request('getDiagnostics'), /Reopen this page/);
  context.chrome.runtime.sendMessage = () => { calls += 1; };
  const pending = context.TimerDiagnosticClient.request('getDiagnostics');
  timeoutCallback();
  await assert.rejects(pending, /Diagnostics did not respond/);
  assert.equal(calls, 3);
});
