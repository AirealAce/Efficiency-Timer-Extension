'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { createRecorder, sanitizeDetails, sanitizeState, classifyError, STORAGE_KEY } = require('../diagnostics.js');

function memoryStorage(seed = {}) {
  let data = structuredClone(seed);
  return {
    async get() { return structuredClone(data); },
    async set(value) { data = { ...data, ...structuredClone(value) }; },
    get data() { return structuredClone(data); }
  };
}

test('diagnostics reject arbitrary strings, nested payloads, URLs, reflections and secrets', async () => {
  const storage = memoryStorage();
  const log = createRecorder(storage);
  await log.record('command.received', {
    source: 'popup', action: 'saveReflection', requestedDurationSeconds: 42, isTest: true,
    message: 'PRIVATE REFLECTION', apiToken: 'PRIVATE TOKEN', url: 'https://private.example',
    pageKind: 'https://private.example', errorKind: 'PRIVATE ERROR', nested: { token: 'PRIVATE TOKEN' },
    tabId: 'PRIVATE TAB', elapsedMs: Infinity
  }, { isRunning: true, durationSeconds: 42, apiToken: 'PRIVATE TOKEN', completedAt: 'PRIVATE REFLECTION' });
  await log.record('PRIVATE EVENT', { action: 'PRIVATE ACTION' });
  const report = await log.report();
  assert.equal(report.count, 1);
  assert.doesNotMatch(JSON.stringify(report), /PRIVATE|private\.example|Infinity/);
  assert.deepEqual(report.events[0].details, { source: 'popup', action: 'saveReflection', requestedDurationSeconds: 42, isTest: true });
  assert.equal(report.events[0].timer.isRunning, true);
});

test('concurrent records are ordered, bounded, detached from callers and survive worker restarts', async () => {
  const storage = memoryStorage();
  const log = createRecorder(storage, { maxEvents: 10 });
  const timer = { isRunning: true, remainingSeconds: 50 };
  await Promise.all(Array.from({ length: 30 }, (_, tabId) => log.record('tab.activated', { tabId }, timer)));
  timer.remainingSeconds = 0;
  const report = await log.report();
  assert.equal(report.count, 10);
  assert.deepEqual(report.events.map((event) => event.details.tabId), [20, 21, 22, 23, 24, 25, 26, 27, 28, 29]);
  assert.equal(report.events[0].timer.remainingSeconds, 50);
  report.events[0].details.token = 'should not persist';
  const restored = createRecorder(storage, { maxEvents: 10 });
  await restored.record('worker.started');
  const next = await restored.report();
  assert.equal(next.events.at(-1).session, 2);
  assert.equal(next.events.at(-1).sequence, 31);
  assert.doesNotMatch(JSON.stringify(next), /should not persist/);
});

test('retention is enforced on records and exports without a keep-alive timer', async () => {
  let time = 1000;
  const storage = memoryStorage();
  const log = createRecorder(storage, { now: () => time, retentionMs: 100 });
  await log.record('worker.started');
  time = 1050;
  await log.record('popup.opened');
  time = 1101;
  assert.deepEqual((await log.report()).events.map((event) => event.event), ['popup.opened']);
  time = 1200;
  assert.equal((await log.report()).count, 0);
});

test('disable and clear serialize with in-flight records and remain disabled across restarts', async () => {
  const storage = memoryStorage();
  const log = createRecorder(storage);
  const writes = [log.record('worker.started'), log.setEnabled(false), log.record('popup.opened'), log.clear(), log.record('popup.loaded')];
  await Promise.all(writes);
  assert.equal((await log.report()).count, 0);
  const next = createRecorder(storage);
  assert.equal(await next.record('worker.started'), false);
  assert.equal((await next.report()).enabled, false);
  await next.setEnabled(true);
  await next.record('logging.enabled');
  assert.equal((await next.report()).count, 1);
});

test('storage failure is nonfatal and reports durability loss', async () => {
  const log = createRecorder({ async get() { return {}; }, async set() { throw new Error('quota'); } });
  await assert.doesNotReject(() => log.record('worker.started'));
  const report = await log.report();
  assert.equal(report.storageAvailable, false);
  assert.equal(report.count, 1);
  const unreadable = createRecorder({ async get() { throw new Error('unavailable'); } });
  await unreadable.record('worker.started');
  assert.equal((await unreadable.report()).enabled, false);
  assert.equal((await unreadable.report()).storageAvailable, false);
});

test('persisted events are sanitized again, including null or corrupt state', async () => {
  const storage = memoryStorage({ [STORAGE_KEY]: { events: [
    { at: Date.now(), sequence: 1, session: 1, event: 'worker.started', details: { token: 'PRIVATE' }, timer: null, token: 'PRIVATE' },
    { at: Date.now(), sequence: 2, session: 1, event: 'PRIVATE EVENT', details: {} }
  ] } });
  const report = await createRecorder(storage).report();
  assert.equal(report.count, 1);
  assert.doesNotMatch(JSON.stringify(report), /PRIVATE/);
  assert.deepEqual(sanitizeState(null), { scheduledTimer: null });
  assert.deepEqual(sanitizeDetails(null), {});
  assert.deepEqual(sanitizeDetails(JSON.parse('{"constructor":"PRIVATE","__proto__":"PRIVATE","toString":"PRIVATE"}')), {});
  const schedule = { scheduledTimer: { targetTime: 123456789, durationSeconds: 600, autoRestart: true } };
  assert.deepEqual(sanitizeState(sanitizeState(schedule)), sanitizeState(schedule), 'schedule details must survive sanitizing persisted records again');
});

test('errors become useful fixed categories, never raw messages', () => {
  assert.equal(classifyError(new Error('Could not establish connection. Receiving end does not exist.')), 'missing_receiver');
  assert.equal(classifyError(new Error('Extension context invalidated.')), 'context_invalidated');
  assert.equal(classifyError({ name: 'AbortError' }), 'timeout');
  assert.equal(classifyError({ code: 'SETTINGS_REQUIRED', message: 'PRIVATE' }), 'settings_required');
  assert.equal(classifyError(new Error('PRIVATE response from server')), 'unknown');
});
