(function exposeTimerUtils(root, factory) {
  const api = factory();
  if (typeof module === 'object' && module.exports) {
    module.exports = api;
  } else {
    root.TimerUtils = api;
  }
}(typeof globalThis !== 'undefined' ? globalThis : this, () => {
  'use strict';

  const MAX_DURATION_SECONDS = 365 * 24 * 60 * 60;

  function toNonNegativeInteger(value) {
    const parsed = Number.parseInt(value, 10);
    return Number.isFinite(parsed) && parsed > 0 ? parsed : 0;
  }

  function durationFromParts(parts = {}) {
    const hours = toNonNegativeInteger(parts.hours);
    const minutes = Math.min(59, toNonNegativeInteger(parts.minutes));
    const seconds = Math.min(59, toNonNegativeInteger(parts.seconds));
    return Math.min(MAX_DURATION_SECONDS, (hours * 3600) + (minutes * 60) + seconds);
  }

  function splitDuration(totalSeconds) {
    const safeTotal = Math.max(0, Math.min(MAX_DURATION_SECONDS, toNonNegativeInteger(totalSeconds)));
    return {
      hours: Math.floor(safeTotal / 3600),
      minutes: Math.floor((safeTotal % 3600) / 60),
      seconds: safeTotal % 60
    };
  }

  function getRemainingSeconds(state, now = Date.now()) {
    if (!state || !state.isRunning || !Number.isFinite(Number(state.endTime))) {
      return Math.max(0, toNonNegativeInteger(state && state.remainingSeconds));
    }
    return Math.max(0, Math.ceil((Number(state.endTime) - now) / 1000));
  }

  function extractSpreadsheetId(value) {
    const input = String(value || '').trim();
    if (/^[A-Za-z0-9_-]{20,}$/.test(input)) {
      return input;
    }
    const match = input.match(/\/spreadsheets\/d\/([A-Za-z0-9_-]{20,})/);
    return match ? match[1] : null;
  }

  function isValidWebAppUrl(value) {
    try {
      const url = new URL(String(value || '').trim());
      return url.protocol === 'https:'
        && url.hostname === 'script.google.com'
        && /^\/macros\/s\/[^/]+\/exec\/?$/.test(url.pathname);
    } catch (_error) {
      return false;
    }
  }

  function isSupportedPageUrl(value) {
    try {
      const protocol = new URL(String(value || '')).protocol;
      return protocol === 'http:' || protocol === 'https:' || protocol === 'file:';
    } catch (_error) {
      return false;
    }
  }

  return {
    MAX_DURATION_SECONDS,
    durationFromParts,
    extractSpreadsheetId,
    getRemainingSeconds,
    isSupportedPageUrl,
    isValidWebAppUrl,
    splitDuration,
    toNonNegativeInteger
  };
}));
