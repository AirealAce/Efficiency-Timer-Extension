'use strict';

document.addEventListener('DOMContentLoaded', () => {
  const byId = (id) => document.getElementById(id);
  const marker = byId('markDiagnosticIssue');
  const enabled = byId('diagnosticsEnabled');
  const status = byId('diagnosticsStatus');
  const summary = byId('diagnosticsSummary');
  const recent = byId('diagnosticsRecent');
  const exportButton = byId('exportDiagnostics');
  const clearButton = byId('clearDiagnostics');
  const refreshButton = byId('refreshDiagnostics');
  const controls = [marker, enabled, exportButton, clearButton, refreshButton].filter(Boolean);
  if (!marker || !status) return;

  function showStatus(text, error = false) {
    status.textContent = text;
    status.className = `status ${error ? 'error' : 'success'}`;
  }
  function render(report) {
    if (enabled) enabled.checked = report.enabled;
    if (summary) summary.textContent = `${report.enabled ? 'Recording' : 'Paused'} · ${report.count} / ${report.maxEvents} events · last ${report.retentionDays} days at most`;
    if (recent) recent.textContent = report.events.slice(-20).reverse().map((event) => {
      const tab = event.details.tabId ?? event.details.activeTabId;
      const timer = event.timer.isRunning ? `running (${event.timer.remainingSeconds}s left)` : event.timer.promptActive ? 'reflection waiting' : 'not running';
      return `${new Date(event.at).toLocaleString()} · ${event.event}${tab === undefined ? '' : ` · tab ${tab}`} · ${timer}`;
    }).join('\n') || 'No recorded events yet.';
    if (!report.storageAvailable) showStatus('Log storage is unavailable. Export what is available before reloading; new events may not survive a restart.', true);
  }
  async function run(action, extra = {}, onSuccess) {
    controls.forEach((control) => { control.disabled = true; });
    try {
      const result = await TimerDiagnosticClient.request(action, extra);
      render(result.report);
      if (onSuccess) onSuccess(result);
      return result;
    } catch (error) {
      if (summary) summary.textContent = 'Diagnostics unavailable.';
      showStatus(error.message, true);
      return null;
    } finally {
      controls.forEach((control) => { control.disabled = false; });
    }
  }

  marker.addEventListener('click', () => run('markDiagnosticIssue', {}, (result) => {
    showStatus(result.marked
      ? 'Issue marked. Export the diagnostic report in Settings and share it for debugging.'
      : 'Issue could not be recorded. Check that local logging is enabled and storage is available in Settings.', !result.marked);
  }));
  enabled?.addEventListener('change', () => run('setDiagnosticsEnabled', { enabled: enabled.checked }, (result) => {
    if (result.report.storageAvailable) showStatus(result.report.enabled ? 'Local logging enabled.' : 'Logging paused. Existing events remain until cleared or aged out.');
  }));
  refreshButton?.addEventListener('click', () => run('getDiagnostics', {}, (result) => {
    if (result.report.storageAvailable) showStatus('Diagnostics refreshed.');
  }));
  clearButton?.addEventListener('click', () => {
    if (!window.confirm('Clear the local diagnostic log? This does not change your timer, reflections, or settings.')) return;
    return run('clearDiagnostics', {}, (result) => {
      if (result.report.storageAvailable) showStatus('Local log cleared. New events will appear if logging is enabled.');
    });
  });
  exportButton?.addEventListener('click', () => run('getDiagnostics', {}, (result) => {
    const blob = new Blob([JSON.stringify(result.report, null, 2)], { type: 'application/json' });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = `reflection-timer-diagnostics-${new Date().toISOString().replace(/[:.]/g, '-')}.json`;
    document.body.appendChild(link);
    link.click();
    link.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
    showStatus(result.report.storageAvailable
      ? 'Report exported. It contains activity times and timer events; review it before sharing.'
      : 'Partial report exported. Log storage is unavailable; some events may be missing.', !result.report.storageAvailable);
  }));
  if (enabled) {
    TimerDiagnosticClient.event('settings.opened');
    void run('getDiagnostics');
  }
});
