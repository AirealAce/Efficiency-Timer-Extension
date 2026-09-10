'use strict';

// No arbitrary strings enter the log. In particular, do not add URLs, titles,
// reflection text, request/response bodies, settings, or raw errors here.
(function (root) {
  const STORAGE_KEY = 'timerDiagnosticsV1';
  const MAX_EVENTS = 1200;
  const RETENTION_MS = 7 * 24 * 60 * 60 * 1000;
  const EVENTS = new Set([
    'worker.started', 'worker.restored', 'worker.failed', 'extension.installed',
    'command.received', 'command.finished', 'command.failed', 'timer.persisted',
    'timer.completed', 'timer.completionSkipped', 'schedule.started', 'schedule.saved', 'schedule.removed', 'schedule.skipped', 'alarm.fired',
    'event.failed', 'tab.activated', 'tab.updated', 'tab.removed', 'window.focused',
    'prompt.delivery', 'prompt.unavailable', 'prompt.injected', 'notification.result',
    'notification.clicked', 'sheets.started', 'sheets.finished', 'sheets.failed',
    'issue.marked', 'logging.enabled', 'popup.opened', 'popup.loaded', 'popup.failed',
    'popup.closed', 'popup.versionMismatch', 'popup.durationChanged', 'popup.stateReceived',
    'client.request', 'client.response', 'client.failure', 'content.ready',
    'page.visibility', 'page.shown', 'page.hidden', 'prompt.shown', 'prompt.skipped',
    'prompt.submit', 'prompt.saved', 'prompt.failed', 'sound.result', 'settings.opened'
  ]);
  const CLIENT_EVENTS = new Set([
    'popup.opened', 'popup.loaded', 'popup.failed', 'popup.closed', 'popup.versionMismatch',
    'popup.durationChanged', 'popup.stateReceived', 'client.request', 'client.response',
    'client.failure', 'page.visibility', 'page.shown', 'page.hidden', 'prompt.shown',
    'prompt.skipped', 'prompt.submit', 'prompt.saved', 'prompt.failed', 'sound.result',
    'settings.opened'
  ]);
  const ACTIONS = [
    'getTimerState', 'startTimer', 'pauseTimer', 'stopTimer', 'resumeTimer', 'resetTimer',
    'updateAutoRestart', 'scheduleTimer', 'clearScheduledTimer', 'showTestPrompt',
    'contentReady', 'dismissReflection', 'updateChatboxState', 'saveReflection',
    'updateGoogleSheet', 'testSheetsConnection', 'openSettings', 'showReflectionPrompt',
    'dismissReflectionPrompt', 'appendReflection', 'ping', 'saveScheduledSession', 'removeScheduledSession', 'updateVolume'
  ];
  const ENUMS = {
    action: ACTIONS,
    source: ['background', 'popup', 'settings', 'content', 'unknown'],
    errorKind: ['timeout', 'missing_receiver', 'context_invalidated', 'settings_required',
      'storage', 'permission', 'network', 'invalid_response', 'rejected', 'unknown'],
    pageKind: ['web', 'file', 'restricted', 'unknown'],
    status: ['loading', 'complete', 'unloaded'],
    visibility: ['visible', 'hidden'],
    outcome: ['success', 'failed', 'already_visible', 'hidden', 'muted', 'blocked'],
    alarm: ['completion', 'scheduled_start', 'other'],
    trigger: ['alarm', 'activation', 'navigation', 'notification', 'initialization'],
    reason: ['install', 'update', 'chrome_update', 'shared_module_update']
  };
  const NUMBERS = new Set([
    'tabId', 'windowId', 'frameId', 'requestedDurationSeconds', 'targetTime',
    'elapsedMs', 'lateByMs', 'scheduledTime', 'httpStatus', 'attempt', 'clientAt',
    'observedRemainingSeconds', 'observedEndTime', 'completionAlarmAt', 'scheduleAlarmAt',
    'alarmCount', 'activeTabId', 'expectedCompletionAt', 'expectedScheduleAt', 'scheduleId', 'sfxVolume', 'scheduledCount'
  ]);
  const BOOLEANS = new Set([
    'active', 'discarded', 'frozen', 'audible', 'isTest', 'hasPrompt', 'persisted',
    'success', 'missingReceiver', 'alreadyVisible', 'autoRestart', 'observedRunning',
    'observedPromptActive', 'inputsDirty', 'alarmsAvailable', 'tabsAvailable',
    'completionAlarmMissing', 'scheduleAlarmMissing', 'completionAlarmMismatch',
    'scheduleAlarmMismatch', 'stateInitialized'
  ]);

  function sanitizeDetails(value = {}) {
    const clean = {};
    if (!value || typeof value !== 'object') return clean;
    for (const [key, item] of Object.entries(value)) {
      if (NUMBERS.has(key) && typeof item === 'number' && Number.isFinite(item)) clean[key] = item;
      else if (BOOLEANS.has(key) && typeof item === 'boolean') clean[key] = item;
      else if (Object.hasOwn(ENUMS, key) && ENUMS[key].includes(item)) clean[key] = item;
    }
    return clean;
  }

  function sanitizeState(value = {}) {
    if (!value || typeof value !== 'object') value = {};
    const clean = {};
    for (const key of ['isRunning', 'autoRestart', 'promptActive']) {
      if (typeof value[key] === 'boolean') clean[key] = value[key];
    }
    for (const key of ['durationSeconds', 'remainingSeconds', 'endTime', 'sfxVolume']) {
      if (typeof value[key] === 'number' && Number.isFinite(value[key])) clean[key] = value[key];
      else if (value[key] === null) clean[key] = null;
    }
    if (value.scheduledTimer) {
      clean.scheduledTimer = sanitizeDetails({
        targetTime: value.scheduledTimer.targetTime,
        autoRestart: value.scheduledTimer.autoRestart
      });
      const duration = value.scheduledTimer.durationSeconds;
      if (typeof duration === 'number' && Number.isFinite(duration)) clean.scheduledTimer.durationSeconds = duration;
    } else clean.scheduledTimer = null;
    if (Array.isArray(value.scheduledTimers)) clean.scheduledCount = value.scheduledTimers.length;
    else if (Number.isSafeInteger(value.scheduledCount)) clean.scheduledCount = value.scheduledCount;
    return clean;
  }

  function classifyError(error) {
    if (error && error.code === 'SETTINGS_REQUIRED') return 'settings_required';
    const text = String(error && (error.message || error.name) || error || '');
    if (/timed? ?out|timeout|AbortError/i.test(text)) return 'timeout';
    if (/receiving end does not exist|could not establish connection/i.test(text)) return 'missing_receiver';
    if (/context invalidated/i.test(text)) return 'context_invalidated';
    if (/quota|storage/i.test(text)) return 'storage';
    if (/permission|cannot access|not allowed/i.test(text)) return 'permission';
    if (/fetch|network/i.test(text)) return 'network';
    if (/invalid response/i.test(text)) return 'invalid_response';
    return 'unknown';
  }

  function pageKind(url) {
    if (typeof url !== 'string' || !url) return 'unknown';
    if (/^https?:\/\//i.test(url)) return 'web';
    if (/^file:/i.test(url)) return 'file';
    return 'restricted';
  }

  function createRecorder(storage, { now = Date.now, maxEvents = MAX_EVENTS, retentionMs = RETENTION_MS } = {}) {
    let events = [];
    let enabled = true;
    let storageAvailable = true;
    let sequence = 0;
    let session = 1;
    let queue = Promise.resolve();
    const prune = () => { events = events.filter((event) => event.at >= now() - retentionMs).slice(-maxEvents); };
    const initialized = (async () => {
      try {
        const stored = (await storage.get([STORAGE_KEY]))[STORAGE_KEY];
        if (!stored) return;
        enabled = stored.enabled !== false;
        // Re-sanitize persisted records as well as new records before exporting.
        events = (Array.isArray(stored.events) ? stored.events : []).filter((event) =>
          event && EVENTS.has(event.event) && Number.isFinite(event.at) &&
          Number.isSafeInteger(event.sequence) && Number.isSafeInteger(event.session)
        ).map((event) => ({
          at: event.at, sequence: event.sequence, session: event.session, event: event.event,
          details: sanitizeDetails(event.details), timer: sanitizeState(event.timer)
        }));
        sequence = Math.max(0, ...events.map((event) => event.sequence));
        session = Math.max(0, ...events.map((event) => event.session)) + 1;
        prune();
      } catch (_error) { storageAvailable = false; enabled = false; }
    })();
    function enqueue(task) {
      const result = queue.then(() => initialized).then(task);
      // Diagnostics must never break timer actions, even when storage is full.
      queue = result.catch(() => { storageAvailable = false; });
      return queue;
    }
    async function persist() {
      await storage.set({ [STORAGE_KEY]: { enabled, events } });
      storageAvailable = true;
    }
    function status() {
      prune();
      return { enabled, storageAvailable, count: events.length, maxEvents, retentionDays: retentionMs / 86400000 };
    }
    return {
      record(event, details, timer) {
        if (!EVENTS.has(event)) return Promise.resolve(false);
        const entry = { at: now(), event, details: sanitizeDetails(details), timer: sanitizeState(timer) };
        return enqueue(async () => {
          if (!enabled) return false;
          events.push({ ...entry, sequence: ++sequence, session });
          prune();
          await persist();
          return true;
        });
      },
      setEnabled(value) {
        return enqueue(async () => { enabled = value === true; prune(); await persist(); });
      },
      clear() {
        return enqueue(async () => { events = []; await persist(); });
      },
      async report() {
        await queue;
        await initialized;
        return { ...status(), events: JSON.parse(JSON.stringify(events)) };
      }
    };
  }

  const api = { createRecorder, sanitizeDetails, sanitizeState, classifyError, pageKind, CLIENT_EVENTS, STORAGE_KEY };
  root.TimerDiagnostics = api;
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
})(globalThis);
