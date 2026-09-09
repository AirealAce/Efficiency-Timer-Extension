'use strict';

importScripts('timer-utils.js', 'diagnostics.js');

const TIMER_ALARM = 'reflectionTimerComplete';
const SCHEDULE_ALARM = 'reflectionTimerScheduledStart';
const TIMER_STATE_KEY = 'timerStateV2';
const SCHEDULE_STATE_KEY = 'scheduledSessionsV3';
const MAX_SCHEDULED_SESSIONS = 50;
const DEFAULT_DURATION_SECONDS = 25 * 60;
const DEFAULT_SHEET_URL = '';
const DEFAULT_SHEET_NAME = 'Template';
const MAX_REFLECTION_LENGTH = 5000;
const REQUEST_TIMEOUT_MS = 20_000;
const API_VERSION = 6;
const LEGACY_SECRET_KEYS = [
  'GOOGLE_SHEETS_CLIENT_EMAIL',
  'GOOGLE_SHEETS_PRIVATE_KEY',
  'SPREADSHEET_ID'
];

let timerState = createDefaultTimerState();
let scheduledTimers = [];
// Appointments and live timer mutations share one queue so they cannot race.
let scheduleQueue = Promise.resolve();
function queueSchedule(operation) {
  const result = scheduleQueue.then(operation);
  scheduleQueue = result.catch(() => {});
  return result;
}
let stateInitialized = false;
const diagnostics = TimerDiagnostics.createRecorder(chrome.storage.local);
function diagnose(event, details = {}, observedState = publicState()) {
  return diagnostics.record(event, { source: 'background', stateInitialized, ...details }, observedState);
}
void diagnose('worker.started');
const ready = initialize().then(() => {
  stateInitialized = true;
  void diagnose('worker.restored');
}).catch(async (error) => {
  void diagnose('worker.failed', { errorKind: TimerDiagnostics.classifyError(error) });
  console.error('[Reflection Timer] State initialization failed; restoring safe defaults.', error);
  timerState = createDefaultTimerState();
  scheduledTimers = [];
  await chrome.alarms.clear(TIMER_ALARM);
  await chrome.alarms.clear(SCHEDULE_ALARM);
  // Keep saved appointments on disk if initialization fails; a later worker
  // can recover them rather than erasing the user's schedule.
  await persistTimerState(false);
});

function diagnosticSource(sender) {
  if (sender.id !== chrome.runtime.id) return 'unknown';
  if (sender.url === chrome.runtime.getURL('popup.html')) return 'popup';
  if (sender.url === chrome.runtime.getURL('settings.html')) return 'settings';
  if (sender.tab) return 'content';
  return 'unknown';
}

function senderDetails(sender) {
  return {
    source: diagnosticSource(sender), tabId: sender.tab && sender.tab.id,
    windowId: sender.tab && sender.tab.windowId, frameId: sender.frameId
  };
}

async function diagnosticSnapshot() {
  const snapshot = { timer: TimerDiagnostics.sanitizeState(publicState()), stateInitialized };
  snapshot.scheduledSessions = scheduledTimers.map((entry) => TimerDiagnostics.sanitizeDetails({
    scheduleId: entry.id, targetTime: entry.targetTime, requestedDurationSeconds: entry.durationSeconds,
    autoRestart: entry.autoRestart, sfxVolume: entry.sfxVolume
  }));
  const current = snapshot.timer;
  const expectedSchedule = current.scheduledTimer;
  const checks = { expectedCompletionAt: current.endTime, expectedScheduleAt: expectedSchedule && expectedSchedule.targetTime };
  try {
    const alarms = await chrome.alarms.getAll();
    snapshot.alarms = alarms.filter((alarm) => [TIMER_ALARM, SCHEDULE_ALARM].includes(alarm.name))
      .map((alarm) => ({ alarm: alarm.name === TIMER_ALARM ? 'completion' : 'scheduled_start', scheduledTime: alarm.scheduledTime }));
    const completion = alarms.find((alarm) => alarm.name === TIMER_ALARM);
    const scheduled = alarms.find((alarm) => alarm.name === SCHEDULE_ALARM);
    Object.assign(checks, {
      alarmsAvailable: true, alarmCount: snapshot.alarms.length,
      completionAlarmAt: completion && completion.scheduledTime,
      scheduleAlarmAt: scheduled && scheduled.scheduledTime,
      completionAlarmMissing: Boolean(current.isRunning && !completion),
      scheduleAlarmMissing: Boolean(expectedSchedule && !scheduled),
      completionAlarmMismatch: Boolean(completion && (!current.isRunning || completion.scheduledTime !== current.endTime)),
      scheduleAlarmMismatch: Boolean(scheduled && (!expectedSchedule || scheduled.scheduledTime !== expectedSchedule.targetTime))
    });
  } catch (_error) { checks.alarmsAvailable = false; }
  try {
    const tabs = await chrome.tabs.query({ active: true, lastFocusedWindow: true });
    snapshot.activeTabs = tabs.map((tab) => TimerDiagnostics.sanitizeDetails({
      tabId: tab.id, windowId: tab.windowId, active: tab.active, discarded: tab.discarded,
      frozen: tab.frozen, status: tab.status, pageKind: TimerDiagnostics.pageKind(tab.url)
    }));
    Object.assign(checks, {
      tabsAvailable: true, activeTabId: tabs[0] && tabs[0].id,
      windowId: tabs[0] && tabs[0].windowId,
      pageKind: TimerDiagnostics.pageKind(tabs[0] && tabs[0].url)
    });
  } catch (_error) { checks.tabsAvailable = false; }
  snapshot.checks = TimerDiagnostics.sanitizeDetails(checks);
  return snapshot;
}

async function handleDiagnostics(message, sender) {
  const source = diagnosticSource(sender);
  if (message.action === 'recordDiagnostic') {
    if (source === 'unknown' || !TimerDiagnostics.CLIENT_EVENTS.has(message.event)) return { success: false };
    await diagnose(message.event, { ...TimerDiagnostics.sanitizeDetails(message.details), ...senderDetails(sender) });
    return { success: true };
  }
  if (!['popup', 'settings'].includes(source)) return { success: false, error: 'Open extension settings to manage diagnostics.' };
  if (message.action === 'setDiagnosticsEnabled') {
    await diagnostics.setEnabled(message.enabled);
    if (message.enabled === true) await diagnose('logging.enabled', { source });
  } else if (message.action === 'clearDiagnostics') {
    await diagnostics.clear();
  }
  let marked;
  const snapshot = await diagnosticSnapshot(); // Read only: never complete, reset, or restart a timer here.
  if (message.action === 'markDiagnosticIssue') {
    marked = await diagnose('issue.marked', { source, stateInitialized: snapshot.stateInitialized, ...snapshot.checks }, snapshot.timer);
  }
  const log = await diagnostics.report();
  const version = chrome.runtime.getManifest().version;
  return {
    success: true, marked: marked === true,
    report: {
      formatVersion: 1, extensionVersion: /^\d+(\.\d+){1,3}$/.test(version) ? version : 'unknown',
      exportedAt: new Date().toISOString(), timezoneOffsetMinutes: new Date().getTimezoneOffset(),
      privacy: 'Local diagnostics only. No URLs, page titles, page contents, reflection text, tokens, or connection settings.',
      ...log, snapshot
    }
  };
}

function createNotification(id, options) {
  try {
    Promise.resolve(chrome.notifications.create(id, options)).then(() => {
      void diagnose('notification.result', { success: true });
    }).catch((error) => {
      void diagnose('notification.result', { success: false, errorKind: TimerDiagnostics.classifyError(error) });
    });
  } catch (error) {
    void diagnose('notification.result', { success: false, errorKind: TimerDiagnostics.classifyError(error) });
  }
}

function createDefaultTimerState() {
  return {
    isRunning: false,
    durationSeconds: DEFAULT_DURATION_SECONDS,
    remainingSeconds: DEFAULT_DURATION_SECONDS,
    endTime: null,
    autoRestart: false,
    sfxVolume: 50,
    promptActive: false,
    completedAt: null
  };
}

function normalizeTimerState(value) {
  const fallback = createDefaultTimerState();
  if (!value || typeof value !== 'object') {
    return fallback;
  }

  const durationSeconds = clampDuration(value.durationSeconds) || DEFAULT_DURATION_SECONDS;
  const remainingSeconds = Math.min(
    durationSeconds,
    clampDuration(value.remainingSeconds ?? durationSeconds)
  );
  const endTime = Number(value.endTime);
  const hasDeadline = Number.isFinite(endTime) && endTime > 0;

  return {
    isRunning: Boolean(value.isRunning && hasDeadline),
    durationSeconds,
    remainingSeconds,
    endTime: value.isRunning && hasDeadline ? endTime : null,
    autoRestart: Boolean(value.autoRestart),
    sfxVolume: normalizeVolume(value.sfxVolume),
    promptActive: Boolean(value.promptActive),
    completedAt: typeof value.completedAt === 'string' ? value.completedAt : null
  };
}

function migrateLegacyTimerState(value, autoRestart) {
  if (!value || typeof value !== 'object') {
    return null;
  }
  const durationSeconds = clampDuration(value.originalTime ?? value.timeLeft) || DEFAULT_DURATION_SECONDS;
  const remainingSeconds = Math.min(durationSeconds, clampDuration(value.timeLeft ?? durationSeconds));
  return {
    isRunning: Boolean(value.isRunning && remainingSeconds > 0),
    durationSeconds,
    remainingSeconds,
    endTime: value.isRunning && remainingSeconds > 0 ? Date.now() + (remainingSeconds * 1000) : null,
    autoRestart: Boolean(value.autoRestartEnabled ?? autoRestart),
    promptActive: Boolean(value.globalChatboxVisible),
    completedAt: null
  };
}

function migrateLegacySchedule(value, scheduleState, autoRestart) {
  if (!value || typeof value !== 'object') {
    return null;
  }
  const targetTime = new Date(value.targetTime || (scheduleState && scheduleState.targetTime)).getTime();
  const durationSeconds = typeof value.originalTime === 'object'
    ? TimerUtils.durationFromParts(value.originalTime)
    : clampDuration(value.durationSeconds);
  if (!Number.isFinite(targetTime) || durationSeconds <= 0) {
    return null;
  }
  return { targetTime, durationSeconds, autoRestart: Boolean(autoRestart) };
}

function normalizeScheduledTimer(value) {
  if (!value || typeof value !== 'object') {
    return null;
  }
  const targetTime = Number(value.targetTime);
  const durationSeconds = clampDuration(value.durationSeconds);
  if (!Number.isFinite(targetTime) || !Number.isFinite(new Date(targetTime).getTime()) || targetTime <= 0 || durationSeconds <= 0) {
    return null;
  }
  return {
    id: Number.isSafeInteger(value.id) && value.id > 0 ? value.id : targetTime,
    targetTime,
    durationSeconds,
    autoRestart: Boolean(value.autoRestart),
    sfxVolume: normalizeVolume(value.sfxVolume)
  };
}

function normalizeVolume(value, fallback = 50) {
  if (value === undefined || value === null || !Number.isFinite(Number(value))) return fallback;
  return Math.max(0, Math.min(100, Math.round(Number(value))));
}

function clampDuration(value) {
  const parsed = Number.parseInt(value, 10);
  if (!Number.isFinite(parsed) || parsed <= 0) {
    return 0;
  }
  return Math.min(parsed, TimerUtils.MAX_DURATION_SECONDS);
}

async function initialize() {
  const stored = await chrome.storage.local.get([
    TIMER_STATE_KEY,
    SCHEDULE_STATE_KEY,
    'scheduledTimerV2',
    'sfxVolume',
    'backgroundTimerState',
    'scheduledTimer',
    'scheduledState',
    'autoRestart',
    'sheetUrl',
    'sheetName',
    'sheetsLink'
  ]);

  timerState = stored[TIMER_STATE_KEY]
    ? normalizeTimerState(stored[TIMER_STATE_KEY])
    : (migrateLegacyTimerState(stored.backgroundTimerState, stored.autoRestart) || createDefaultTimerState());

  timerState.sfxVolume = normalizeVolume(stored[TIMER_STATE_KEY]?.sfxVolume, normalizeVolume(stored.sfxVolume));
  const legacySchedule = normalizeScheduledTimer(stored.scheduledTimerV2)
    || migrateLegacySchedule(stored.scheduledTimer, stored.scheduledState, stored.autoRestart);
  const candidates = Array.isArray(stored[SCHEDULE_STATE_KEY]) ? stored[SCHEDULE_STATE_KEY]
    : legacySchedule ? [{ ...legacySchedule, sfxVolume: normalizeVolume(stored.sfxVolume) }] : [];
  scheduledTimers = candidates.map(normalizeScheduledTimer).filter(Boolean).sort((a, b) => a.targetTime - b.targetTime);
  const ids = new Set();
  const times = new Set();
  scheduledTimers = scheduledTimers.filter((entry) => {
    if (ids.has(entry.id) || times.has(entry.targetTime)) return false;
    ids.add(entry.id);
    times.add(entry.targetTime);
    return true;
  }).slice(0, MAX_SCHEDULED_SESSIONS);
  // Persist the migrated list before removing the old single-entry format.
  await chrome.storage.local.set({ [SCHEDULE_STATE_KEY]: scheduledTimers });
  if (stored.scheduledTimerV2) {
    await chrome.storage.local.remove('scheduledTimerV2');
  }

  const configuredSheetUrl = TimerUtils.extractSpreadsheetId(stored.sheetUrl)
    ? stored.sheetUrl
    : (TimerUtils.extractSpreadsheetId(stored.sheetsLink) ? stored.sheetsLink : DEFAULT_SHEET_URL);
  const configuredSheetName = String(stored.sheetName || '').trim() || DEFAULT_SHEET_NAME;
  if (stored.sheetUrl !== configuredSheetUrl || stored.sheetName !== configuredSheetName) {
    await chrome.storage.local.set({
      sheetUrl: configuredSheetUrl,
      sheetName: configuredSheetName
    });
  }

  await chrome.storage.local.remove([
    ...LEGACY_SECRET_KEYS,
    'backgroundTimerState',
    'scheduledTimer',
    'scheduledState',
    'sheetsLink'
  ]);
  await persistTimerState(false);

  if (timerState.isRunning) {
    if (TimerUtils.getRemainingSeconds(timerState) === 0) {
      await completeTimer();
    } else {
      await chrome.alarms.create(TIMER_ALARM, { when: timerState.endTime });
    }
  }

  await startScheduledTimer();
}

function publicState() {
  return {
    ...timerState,
    remainingSeconds: TimerUtils.getRemainingSeconds(timerState),
    scheduledTimer: scheduledTimers[0] || null, // Compatibility with older popups and diagnostic reports.
    scheduledTimers
  };
}

async function persistTimerState(shouldBroadcast = true, extra = {}) {
  await chrome.storage.local.set({ ...extra, [TIMER_STATE_KEY]: timerState });
  void diagnose('timer.persisted');
  if (shouldBroadcast) {
    broadcastRuntimeMessage({ action: 'timerStateChanged', state: publicState() });
  }
}

function broadcastRuntimeMessage(message) {
  chrome.runtime.sendMessage(message, () => {
    void chrome.runtime.lastError;
  });
}

async function startTimer(durationSeconds, autoRestart, options = {}) {
  const safeDuration = clampDuration(durationSeconds);
  if (safeDuration <= 0) {
    throw new Error('Choose a timer duration greater than zero.');
  }

  const fullDuration = clampDuration(options.fullDuration) || safeDuration;
  const shouldDismissPrompt = timerState.promptActive && options.keepPrompt !== true;
  timerState = {
    ...timerState,
    isRunning: true,
    durationSeconds: fullDuration,
    remainingSeconds: safeDuration,
    endTime: Date.now() + (safeDuration * 1000),
    autoRestart: Boolean(autoRestart),
    sfxVolume: normalizeVolume(options.sfxVolume, timerState.sfxVolume),
    promptActive: options.keepPrompt === true && timerState.promptActive,
    completedAt: options.keepPrompt === true ? timerState.completedAt : null
  };

  await chrome.alarms.clear(TIMER_ALARM);
  await chrome.alarms.create(TIMER_ALARM, { when: timerState.endTime });
  await persistTimerState(true, options.scheduledStart ? { [SCHEDULE_STATE_KEY]: scheduledTimers } : {});
  if (shouldDismissPrompt) {
    void dismissPromptInAllTabs().catch(() => {});
  }
  return publicState();
}

async function pauseTimer() {
  if (timerState.isRunning) {
    timerState.remainingSeconds = TimerUtils.getRemainingSeconds(timerState);
  }
  timerState.isRunning = false;
  timerState.endTime = null;
  await chrome.alarms.clear(TIMER_ALARM);
  await persistTimerState();
  return publicState();
}

async function resumeTimer(autoRestart) {
  const remaining = TimerUtils.getRemainingSeconds(timerState);
  if (remaining <= 0) {
    throw new Error('There is no paused timer to resume.');
  }
  return startTimer(remaining, autoRestart, { fullDuration: timerState.durationSeconds });
}

async function resetTimer(durationSeconds) {
  const replacementDuration = clampDuration(durationSeconds);
  const duration = replacementDuration || timerState.durationSeconds || DEFAULT_DURATION_SECONDS;
  timerState = {
    ...timerState,
    isRunning: false,
    durationSeconds: duration,
    remainingSeconds: duration,
    endTime: null,
    promptActive: false,
    completedAt: null
  };
  await chrome.alarms.clear(TIMER_ALARM);
  await persistTimerState();
  void dismissPromptInAllTabs().catch(() => {});
  return publicState();
}

async function completeTimer() {
  if (!timerState.isRunning) {
    void diagnose('timer.completionSkipped');
    return publicState();
  }

  void diagnose('timer.completed', { lateByMs: Math.max(0, Date.now() - timerState.endTime) });

  const durationSeconds = timerState.durationSeconds;
  const autoRestart = timerState.autoRestart;
  const completedAt = new Date().toISOString();

  timerState = {
    ...timerState,
    isRunning: false,
    remainingSeconds: 0,
    endTime: null,
    promptActive: true,
    completedAt
  };
  await persistTimerState();

  createNotification(`reflection-${Date.now()}`, {
    type: 'basic',
    iconUrl: 'extension_icon_128.png',
    title: 'Time is up',
    message: 'How did you spend this session? Open a regular webpage if the reflection box is not visible.',
    priority: 2
  });
  void showPromptInActiveTab().catch((error) => diagnose('event.failed', { errorKind: TimerDiagnostics.classifyError(error) }));

  if (autoRestart) {
    await startTimer(durationSeconds, true, {
      fullDuration: durationSeconds,
      keepPrompt: true
    });
  }
  return publicState();
}

async function scheduleTimer(targetTimeValue, durationSeconds, autoRestart, sfxVolume, id) {
  const targetTime = new Date(targetTimeValue).getTime();
  const duration = clampDuration(durationSeconds);
  if (!Number.isFinite(targetTime) || targetTime <= Date.now()) {
    throw new Error('Choose a future start time.');
  }
  if (duration <= 0) {
    throw new Error('Choose a timer duration greater than zero.');
  }

  const editing = id !== undefined && id !== null;
  if (editing && !scheduledTimers.some((entry) => entry.id === id)) throw new Error('That scheduled session no longer exists. Add a new session instead.');
  if (!editing && scheduledTimers.length >= MAX_SCHEDULED_SESSIONS) throw new Error('You can schedule up to 50 sessions. Remove one before adding another.');
  if (scheduledTimers.some((entry) => entry.targetTime === targetTime && entry.id !== id)) throw new Error('Another session already starts at that time. Choose a different start time.');
  const entry = {
    id: editing ? id : Math.max(Date.now(), ...scheduledTimers.map((item) => item.id + 1)),
    targetTime, durationSeconds: duration, autoRestart: Boolean(autoRestart),
    sfxVolume: normalizeVolume(sfxVolume, timerState.sfxVolume)
  };
  const next = [...scheduledTimers.filter((item) => item.id !== entry.id), entry].sort((a, b) => a.targetTime - b.targetTime);
  await chrome.storage.local.set({ [SCHEDULE_STATE_KEY]: next });
  scheduledTimers = next;
  await armNextScheduledSession();
  void diagnose('schedule.saved', { scheduleId: entry.id, targetTime, requestedDurationSeconds: duration, autoRestart: entry.autoRestart, sfxVolume: entry.sfxVolume });
  broadcastRuntimeMessage({ action: 'timerStateChanged', state: publicState() });
  return publicState();
}

async function armNextScheduledSession() {
  await chrome.alarms.clear(SCHEDULE_ALARM);
  if (scheduledTimers[0]) await chrome.alarms.create(SCHEDULE_ALARM, { when: scheduledTimers[0].targetTime });
}

async function clearScheduledTimer(id) {
  // Legacy callers cancel the next entry, never the entire schedule list.
  const targetId = id ?? scheduledTimers[0]?.id;
  const next = scheduledTimers.filter((entry) => entry.id !== targetId);
  await chrome.storage.local.set({ [SCHEDULE_STATE_KEY]: next });
  scheduledTimers = next;
  await armNextScheduledSession();
  void diagnose('schedule.removed', { scheduleId: targetId });
  broadcastRuntimeMessage({ action: 'timerStateChanged', state: publicState() });
  return publicState();
}

async function startScheduledTimer() {
  const due = scheduledTimers.filter((entry) => entry.targetTime <= Date.now());
  if (!due.length) {
    await armNextScheduledSession();
    return publicState();
  }
  // After sleep/restart, run the most recent due appointment once, not a burst
  // of every missed session. Future appointments stay queued.
  const pending = due[due.length - 1];
  const next = scheduledTimers.filter((entry) => entry.targetTime > pending.targetTime);
  for (const skipped of due.slice(0, -1)) void diagnose('schedule.skipped', { scheduleId: skipped.id, targetTime: skipped.targetTime });
  void diagnose('schedule.started', { scheduleId: pending.id, lateByMs: Math.max(0, Date.now() - pending.targetTime) });
  // Persist removal and new timer together so a worker restart cannot replay it.
  const previous = scheduledTimers;
  const previousTimer = timerState;
  scheduledTimers = next;
  let state;
  try {
    state = await startTimer(pending.durationSeconds, pending.autoRestart, { sfxVolume: pending.sfxVolume, scheduledStart: true });
  } catch (error) {
    scheduledTimers = previous;
    timerState = previousTimer;
    await chrome.alarms.clear(TIMER_ALARM);
    if (timerState.isRunning) await chrome.alarms.create(TIMER_ALARM, { when: timerState.endTime });
    await chrome.alarms.create(SCHEDULE_ALARM, { when: Date.now() + 60000 });
    throw error;
  }
  await armNextScheduledSession();
  createNotification(`scheduled-${Date.now()}`, {
    type: 'basic',
    iconUrl: 'extension_icon_128.png',
    title: 'Timer started',
    message: 'Your scheduled focus timer is now running.'
  });
  return state;
}

async function getCurrentState() {
  if (timerState.isRunning && TimerUtils.getRemainingSeconds(timerState) === 0) {
    await completeTimer();
  }
  return publicState();
}

function sendTabMessage(tabId, message) {
  return new Promise((resolve) => {
    chrome.tabs.sendMessage(tabId, message, (response) => {
      if (chrome.runtime.lastError) {
        const error = chrome.runtime.lastError.message;
        void diagnose('prompt.delivery', { tabId, action: message.action, isTest: message.isTest === true,
          success: false, errorKind: TimerDiagnostics.classifyError(error) });
        resolve({
          success: false,
          error,
          missingReceiver: /receiving end does not exist|could not establish connection/i.test(error)
        });
      } else {
        void diagnose('prompt.delivery', { tabId, action: message.action, isTest: message.isTest === true,
          success: !response || response.success !== false, alreadyVisible: Boolean(response && response.alreadyVisible) });
        resolve(response || { success: true });
      }
    });
  });
}

async function injectContentScript(tabId) {
  try {
    await chrome.scripting.executeScript({
      target: { tabId },
      files: ['diagnostic-client.js', 'content.js']
    });
    void diagnose('prompt.injected', { tabId, success: true });
    return true;
  } catch (error) {
    void diagnose('prompt.injected', { tabId, success: false, errorKind: TimerDiagnostics.classifyError(error) });
    console.warn('[Reflection Timer] Could not attach the reflection prompt to the active page.', error);
    return false;
  }
}

async function findActiveSupportedTab() {
  const focusedTabs = await chrome.tabs.query({ active: true, lastFocusedWindow: true });
  return focusedTabs.find((tab) => tab.id && TimerUtils.isSupportedPageUrl(tab.url)) || null;
}

async function showPromptInActiveTab(isTest = false) {
  const tab = await findActiveSupportedTab();
  if (!tab) {
    void diagnose('prompt.unavailable', { isTest });
    return { success: false, error: 'Open a normal website (not a chrome:// page), then try again.' };
  }
  const promptMessage = {
    action: 'showReflectionPrompt',
    isTest,
    durationSeconds: timerState.durationSeconds,
    sfxVolume: timerState.sfxVolume,
    completedAt: timerState.completedAt || new Date().toISOString()
  };
  const firstAttempt = await sendTabMessage(tab.id, promptMessage);
  if (firstAttempt.success || !firstAttempt.missingReceiver) {
    return firstAttempt;
  }

  const injected = await injectContentScript(tab.id);
  if (injected) {
    const secondAttempt = await sendTabMessage(tab.id, promptMessage);
    if (secondAttempt.success) {
      return secondAttempt;
    }
  }

  return {
    success: false,
    error: 'Chrome cannot show the prompt on this page. Open or refresh a normal website, then try again.'
  };
}

async function dismissPromptInAllTabs() {
  const tabs = await chrome.tabs.query({});
  await Promise.all(tabs
    .filter((tab) => tab.id && TimerUtils.isSupportedPageUrl(tab.url))
    .map((tab) => sendTabMessage(tab.id, { action: 'dismissReflectionPrompt' })));
}

async function dismissReflection() {
  timerState.promptActive = false;
  timerState.completedAt = null;
  await persistTimerState();
  void dismissPromptInAllTabs().catch(() => {});
  return { success: true };
}

async function callSheetsWebApp(action, extra = {}) {
  const startedAt = Date.now();
  void diagnose('sheets.started', { action, isTest: extra.isTest === true });
  const config = await chrome.storage.local.get(['sheetUrl', 'webAppUrl', 'apiToken', 'sheetName', 'sheetMode']);
  const sheetUrl = String(config.sheetUrl || DEFAULT_SHEET_URL).trim();
  const webAppUrl = String(config.webAppUrl || '').trim();
  const apiToken = String(config.apiToken || '').trim();
  const sheetName = String(config.sheetName || DEFAULT_SHEET_NAME).trim();
  const sheetMode = config.sheetMode === 'fixed' ? 'fixed' : 'date';
  const missingSettings = [];

  if (!TimerUtils.extractSpreadsheetId(sheetUrl)) {
    missingSettings.push('Google Sheet URL');
  }
  if (!TimerUtils.isValidWebAppUrl(webAppUrl)) {
    missingSettings.push('Apps Script deployment URL');
  }
  if (apiToken.length < 16) {
    missingSettings.push('Reflection API token');
  }
  if (sheetMode === 'fixed' && !sheetName) {
    missingSettings.push('target tab');
  }
  if (missingSettings.length > 0) {
    void diagnose('sheets.failed', { action, isTest: extra.isTest === true, errorKind: 'settings_required' });
    const error = new Error(`Finish Google Sheets setup in extension settings: ${missingSettings.join(' and ')}.`);
    error.code = 'SETTINGS_REQUIRED';
    throw error;
  }

  const controller = new AbortController();
  const timeoutId = setTimeout(() => controller.abort(), REQUEST_TIMEOUT_MS);
  const submittedAt = new Date();
  let httpStatus;
  let failureKind;
  try {
    const response = await fetch(webAppUrl, {
      method: 'POST',
      headers: { 'Content-Type': 'text/plain;charset=utf-8' },
      body: JSON.stringify({
        action,
        token: apiToken,
        sheetUrl,
        sheetName,
        sheetMode,
        submittedAt: submittedAt.toISOString(),
        timezoneOffsetMinutes: submittedAt.getTimezoneOffset(),
        ...extra
      }),
      cache: 'no-store',
      redirect: 'follow',
      signal: controller.signal
    });
    httpStatus = response.status;
    const responseText = await response.text();
    let data;
    try {
      data = JSON.parse(responseText);
    } catch (_error) {
      failureKind = 'invalid_response';
      throw new Error('The Apps Script returned an invalid response. Redeploy the latest script version.');
    }
    if (!response.ok || !data.success) {
      failureKind = 'rejected';
      throw new Error(data.error || `Google Apps Script request failed (${response.status}).`);
    }
    void diagnose('sheets.finished', { action, isTest: extra.isTest === true, httpStatus, elapsedMs: Date.now() - startedAt });
    return data;
  } catch (error) {
    void diagnose('sheets.failed', { action, isTest: extra.isTest === true, httpStatus,
      elapsedMs: Date.now() - startedAt, errorKind: failureKind || TimerDiagnostics.classifyError(error) });
    if (error && error.name === 'AbortError') {
      throw new Error('The Google Sheets request timed out.');
    }
    throw error;
  } finally {
    clearTimeout(timeoutId);
  }
}

async function saveReflection(message, isTest = false) {
  const cleanMessage = String(message || '').trim();
  if (!cleanMessage) {
    throw new Error('Write a reflection before submitting.');
  }
  if (cleanMessage.length > MAX_REFLECTION_LENGTH) {
    throw new Error(`Keep the reflection under ${MAX_REFLECTION_LENGTH.toLocaleString()} characters.`);
  }

  const submittedAt = new Date();
  const result = await callSheetsWebApp('appendReflection', {
    message: cleanMessage,
    isTest: isTest === true,
    submittedAt: submittedAt.toISOString(),
    durationSeconds: timerState.durationSeconds,
    timezoneOffsetMinutes: submittedAt.getTimezoneOffset()
  });
  if (!isTest) await dismissReflection();
  return result;
}

chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  const diagnosticAction = ['recordDiagnostic', 'getDiagnostics', 'markDiagnosticIssue', 'setDiagnosticsEnabled', 'clearDiagnostics'].includes(message && message.action);
  const trackCommand = !diagnosticAction && !['getTimerState', 'contentReady'].includes(message && message.action);
  const startedAt = Date.now();
  const details = { ...senderDetails(sender), action: message && message.action,
    isTest: Boolean(message && message.isTest), requestedDurationSeconds: message && message.durationSeconds,
    autoRestart: message && message.autoRestart, scheduleId: message && message.id, sfxVolume: message && message.sfxVolume };
  if (trackCommand) void diagnose('command.received', details);
  (async () => {
    if (diagnosticAction) return handleDiagnostics(message, sender);
    await ready;
    switch (message && message.action) {
      case 'getTimerState':
        return { success: true, apiVersion: API_VERSION, state: await queueSchedule(() => getCurrentState()) };
      case 'startTimer':
        return { success: true, state: await queueSchedule(() => startTimer(message.durationSeconds, message.autoRestart, { sfxVolume: message.sfxVolume })) };
      case 'pauseTimer':
      case 'stopTimer':
        return { success: true, state: await queueSchedule(() => pauseTimer()) };
      case 'resumeTimer':
        return { success: true, state: await queueSchedule(() => resumeTimer(message.autoRestart)) };
      case 'resetTimer':
        return { success: true, state: await queueSchedule(() => resetTimer(message.durationSeconds)) };
      case 'updateAutoRestart':
        return queueSchedule(async () => {
          timerState.autoRestart = Boolean(message.autoRestart);
          await persistTimerState();
          return { success: true, state: publicState() };
        });
      case 'updateVolume':
        return queueSchedule(async () => {
          timerState.sfxVolume = normalizeVolume(message.sfxVolume, timerState.sfxVolume);
          await persistTimerState();
          return { success: true, state: publicState() };
        });
      case 'scheduleTimer':
      case 'saveScheduledSession':
        return {
          success: true,
          state: await queueSchedule(() => scheduleTimer(message.targetTime, message.durationSeconds, message.autoRestart, message.sfxVolume, message.id))
        };
      case 'clearScheduledTimer':
      case 'removeScheduledSession':
        return { success: true, state: await queueSchedule(() => clearScheduledTimer(message.id)) };
      case 'showTestPrompt':
        return showPromptInActiveTab(true);
      case 'contentReady':
        void diagnose('content.ready', { ...senderDetails(sender), active: Boolean(sender.tab && sender.tab.active) });
        if (timerState.promptActive && sender.tab && sender.tab.active && sender.tab.id) {
          return sendTabMessage(sender.tab.id, {
            action: 'showReflectionPrompt',
            isTest: false,
            durationSeconds: timerState.durationSeconds,
            sfxVolume: timerState.sfxVolume,
            completedAt: timerState.completedAt
          });
        }
        return { success: true };
      case 'dismissReflection':
      case 'updateChatboxState':
        return dismissReflection();
      case 'saveReflection':
      case 'updateGoogleSheet':
        return { success: true, data: await saveReflection(message.message, message.isTest === true) };
      case 'testSheetsConnection':
        return { success: true, data: await callSheetsWebApp('ping') };
      case 'openSettings':
        await chrome.runtime.openOptionsPage();
        return { success: true };
      default:
        throw new Error('Unknown extension action.');
    }
  })().then((response) => {
    if (trackCommand) void diagnose('command.finished', { ...details, success: response.success !== false, elapsedMs: Date.now() - startedAt });
    sendResponse(response);
  }).catch((error) => {
    if (!diagnosticAction) void diagnose('command.failed', { ...details, elapsedMs: Date.now() - startedAt, errorKind: TimerDiagnostics.classifyError(error) });
    console.error('[Reflection Timer]', error);
    sendResponse({
      success: false,
      error: error.message || 'Unexpected extension error.',
      errorCode: error.code || null
    });
  });
  return true;
});

chrome.alarms.onAlarm.addListener((alarm) => {
  void diagnose('alarm.fired', { alarm: alarm.name === TIMER_ALARM ? 'completion' : alarm.name === SCHEDULE_ALARM ? 'scheduled_start' : 'other',
    scheduledTime: alarm.scheduledTime, lateByMs: Math.max(0, Date.now() - alarm.scheduledTime) });
  ready.then(() => queueSchedule(async () => {
    if (alarm.name === TIMER_ALARM) {
      // A completion event queued for the previous timer must not end a new
      // scheduled session that has just taken over.
      if (timerState.isRunning && TimerUtils.getRemainingSeconds(timerState) > 0) {
        void diagnose('timer.completionSkipped');
        await chrome.alarms.create(TIMER_ALARM, { when: timerState.endTime });
      } else await completeTimer();
    } else if (alarm.name === SCHEDULE_ALARM) {
      await startScheduledTimer();
    }
  })).catch((error) => {
    void diagnose('event.failed', { trigger: 'alarm', errorKind: TimerDiagnostics.classifyError(error) });
    console.error('[Reflection Timer] Alarm failed:', error);
  });
});

chrome.tabs.onActivated.addListener((info) => {
  void diagnose('tab.activated', { tabId: info.tabId, windowId: info.windowId });
  ready.then(() => {
    if (timerState.promptActive) {
      return showPromptInActiveTab();
    }
    return null;
  }).catch((error) => diagnose('event.failed', { trigger: 'activation', errorKind: TimerDiagnostics.classifyError(error) }));
});

chrome.tabs.onUpdated.addListener((tabId, changeInfo, tab) => {
  if (changeInfo.status || typeof changeInfo.discarded === 'boolean' || typeof changeInfo.frozen === 'boolean') {
    void diagnose('tab.updated', { tabId, windowId: tab.windowId, active: tab.active,
      status: changeInfo.status, discarded: tab.discarded, frozen: tab.frozen,
      pageKind: TimerDiagnostics.pageKind(tab.url) });
  }
  if (changeInfo.status === 'complete' && tab.active) {
    ready.then(() => {
      if (timerState.promptActive) {
        return showPromptInActiveTab();
      }
      return null;
    }).catch((error) => diagnose('event.failed', { trigger: 'navigation', errorKind: TimerDiagnostics.classifyError(error) }));
  }
});

chrome.notifications.onClicked.addListener(() => {
  void diagnose('notification.clicked');
  ready.then(() => showPromptInActiveTab()).catch((error) => diagnose('event.failed', { trigger: 'notification', errorKind: TimerDiagnostics.classifyError(error) }));
});

chrome.tabs.onRemoved.addListener((tabId, info) => {
  void diagnose('tab.removed', { tabId, windowId: info.windowId });
});

chrome.windows.onFocusChanged.addListener((windowId) => {
  void diagnose('window.focused', { windowId });
});

chrome.runtime.onInstalled.addListener((details) => {
  void diagnose('extension.installed', { reason: details.reason });
  chrome.storage.local.remove(LEGACY_SECRET_KEYS);
});

globalThis.addEventListener?.('unhandledrejection', (event) => {
  void diagnose('event.failed', { errorKind: TimerDiagnostics.classifyError(event.reason) });
});
globalThis.addEventListener?.('error', (event) => {
  void diagnose('event.failed', { errorKind: TimerDiagnostics.classifyError(event.error) });
});
