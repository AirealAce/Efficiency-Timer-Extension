'use strict';

(function () {
  if (globalThis.TimerDiagnosticClient) return;
  // Best effort, with no retry loop or polling that could keep the worker alive.
  function errorKind(error) {
    const message = String(error && error.message || '');
    if (/receiving end does not exist|could not establish connection/i.test(message)) return 'missing_receiver';
    if (/context invalidated/i.test(message)) return 'context_invalidated';
    return 'unknown';
  }
  function event(name, details = {}) {
    try {
      chrome.runtime.sendMessage({
        action: 'recordDiagnostic', event: name, details: { ...details, clientAt: Date.now() }
      }, () => { void chrome.runtime.lastError; });
    } catch (_error) { /* An old page can outlive its extension context. */ }
  }
  function request(action, extra = {}) {
    return new Promise((resolve, reject) => {
      const timeout = setTimeout(() => reject(new Error('Diagnostics did not respond. Reload Reflection Timer in chrome://extensions, then reopen this page.')), 5000);
      try {
        chrome.runtime.sendMessage({ action, ...extra }, (response) => {
          clearTimeout(timeout);
          if (chrome.runtime.lastError || !response || !response.success) {
            reject(new Error('Diagnostics are unavailable. Reload Reflection Timer in chrome://extensions, then reopen this page.'));
          } else resolve(response);
        });
      } catch (_error) {
        clearTimeout(timeout);
        reject(new Error('The extension was reloaded. Reopen this page.'));
      }
    });
  }
  globalThis.TimerDiagnosticClient = { event, request, errorKind };
})();
