'use strict';

document.addEventListener('DOMContentLoaded', () => {
  const log = (event, details) => globalThis.TimerDiagnosticClient?.event(event, details);
  log('popup.opened');
  window.addEventListener?.('pagehide', () => log('popup.closed'));
  const EXPECTED_API_VERSION = 6;
  const MESSAGE_TIMEOUT_MS = 3000;
  const DEFAULT_DURATION_SECONDS = 25 * 60;
  const elements = {
    hours: document.getElementById('hours'),
    minutes: document.getElementById('minutes'),
    seconds: document.getElementById('seconds'),
    display: document.getElementById('display'),
    timerStatus: document.getElementById('timerStatus'),
    startPause: document.getElementById('startPause'),
    reset: document.getElementById('reset'),
    autoRestart: document.getElementById('autoRestart'),
    volume: document.getElementById('volume'),
    volumeValue: document.getElementById('volumeValue'),
    schedulePanel: document.getElementById('schedulePanel'),
    openSettings: document.getElementById('openSettings'),
    testPrompt: document.getElementById('testPrompt'),
    currentTime: document.getElementById('currentTime'),
    status: document.getElementById('status')
  };

  let state = null;
  let inputsDirty = false;
  let durationPresetUntouched = durationFromInputs() === DEFAULT_DURATION_SECONDS;
  const scheduleEditor = ScheduledSessions.createEditor({
    sendMessage,
    onState(nextState) { adoptState(nextState, { preserveInputs: true }); setStatus('Schedule saved.', 'success'); },
    onError(message) { setStatus(message, 'error'); }
  });

  function sendMessage(message) {
    const startedAt = Date.now();
    log('client.request', { action: message.action, requestedDurationSeconds: message.durationSeconds,
      autoRestart: message.autoRestart, scheduleId: message.id, sfxVolume: message.sfxVolume });
    return new Promise((resolve, reject) => {
      let settled = false;
      const timeoutId = setTimeout(() => {
        if (!settled) {
          settled = true;
          log('client.failure', { action: message.action, errorKind: 'timeout', elapsedMs: Date.now() - startedAt });
          reject(new Error('The timer service did not respond. Reload the extension once.'));
        }
      }, MESSAGE_TIMEOUT_MS);
      try {
        chrome.runtime.sendMessage(message, (response) => {
          const lastError = chrome.runtime.lastError;
          if (settled) return;
          settled = true;
          clearTimeout(timeoutId);
          if (lastError) {
            log('client.failure', { action: message.action, errorKind: globalThis.TimerDiagnosticClient?.errorKind(lastError) || 'unknown', elapsedMs: Date.now() - startedAt });
            reject(new Error(lastError.message));
          } else if (!response || response.success === false) {
            log('client.failure', { action: message.action, errorKind: 'rejected', elapsedMs: Date.now() - startedAt });
            reject(new Error((response && response.error) || 'The extension did not respond.'));
          } else {
            log('client.response', { action: message.action, success: true, elapsedMs: Date.now() - startedAt });
            resolve(response);
          }
        });
      } catch (error) {
        settled = true;
        clearTimeout(timeoutId);
        log('client.failure', { action: message.action, errorKind: globalThis.TimerDiagnosticClient?.errorKind(error) || 'unknown', elapsedMs: Date.now() - startedAt });
        reject(error);
      }
    });
  }

  function focusAndSelectHours() {
    if (!elements.hours.disabled) {
      elements.hours.focus({ preventScroll: true });
      elements.hours.select();
    }
  }

  function setStatus(message, type = '') {
    elements.status.textContent = message;
    elements.status.className = `status ${type}`.trim();
  }

  function durationFromInputs() {
    return TimerUtils.durationFromParts({
      hours: elements.hours.value,
      minutes: elements.minutes.value,
      seconds: elements.seconds.value
    });
  }

  function putDurationInInputs(totalSeconds) {
    const parts = TimerUtils.splitDuration(totalSeconds);
    elements.hours.value = String(parts.hours);
    elements.minutes.value = String(parts.minutes);
    elements.seconds.value = String(parts.seconds);
  }

  function formatClock(totalSeconds) {
    const seconds = Math.max(0, Number.parseInt(totalSeconds, 10) || 0);
    const hours = Math.floor(seconds / 3600);
    const minutes = Math.floor((seconds % 3600) / 60);
    const remainder = seconds % 60;
    return hours > 0
      ? `${hours}:${String(minutes).padStart(2, '0')}:${String(remainder).padStart(2, '0')}`
      : `${minutes}:${String(remainder).padStart(2, '0')}`;
  }

  function getDisplayedRemaining() {
    if (!state || inputsDirty) {
      return durationFromInputs();
    }
    return TimerUtils.getRemainingSeconds(state);
  }

  function updateTimerDisplay() {
    const remaining = getDisplayedRemaining();
    elements.display.textContent = formatClock(remaining);

    if (!state) {
      elements.timerStatus.textContent = 'Loading…';
      return;
    }
    if (state.isRunning) {
      const endTime = new Date(state.endTime).toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' });
      elements.timerStatus.textContent = `Running · ends ${endTime}`;
      elements.startPause.textContent = 'Pause';
    } else if (remaining > 0 && remaining < state.durationSeconds && !inputsDirty) {
      elements.timerStatus.textContent = 'Paused';
      elements.startPause.textContent = 'Resume';
    } else if (state.promptActive) {
      elements.timerStatus.textContent = 'Session complete · reflection waiting';
      elements.startPause.textContent = 'Start';
    } else {
      elements.timerStatus.textContent = 'Ready';
      elements.startPause.textContent = 'Start';
    }

    const inputDisabled = Boolean(state.isRunning);
    [elements.hours, elements.minutes, elements.seconds].forEach((input) => {
      input.disabled = inputDisabled;
    });
  }

  function updateScheduleDisplay() {
    scheduleEditor.render(state);
  }

  function adoptState(nextState, { preserveInputs = false } = {}) {
    state = nextState;
    if (!preserveInputs) {
      inputsDirty = false;
      if (state && state.durationSeconds) {
        putDurationInInputs(state.durationSeconds);
      }
      durationPresetUntouched = durationFromInputs() === DEFAULT_DURATION_SECONDS;
    }
    elements.autoRestart.checked = Boolean(state && state.autoRestart);
    if (Number.isFinite(state?.sfxVolume)) {
      elements.volume.value = String(state.sfxVolume);
      elements.volumeValue.textContent = `${state.sfxVolume}%`;
    }
    updateTimerDisplay();
    updateScheduleDisplay();
  }

  async function saveTimerInputs() {
    const values = {
      hours: Number(elements.hours.value) || 0,
      minutes: Number(elements.minutes.value) || 0,
      seconds: Number(elements.seconds.value) || 0
    };
    await chrome.storage.local.set({ timerValues: values });
  }

  async function load() {
    try {
      const [saved, response] = await Promise.all([
        chrome.storage.local.get(['timerValues', 'sfxVolume', 'autoRestart']),
        sendMessage({ action: 'getTimerState' })
      ]);
      if (response.apiVersion !== EXPECTED_API_VERSION || !response.state) {
        log('popup.versionMismatch');
        elements.timerStatus.textContent = 'Finishing update…';
        setStatus('Reopen the timer in a moment.', 'success');
        setTimeout(() => chrome.runtime.reload(), 250);
        return;
      }
      const preserveInputs = inputsDirty;
      adoptState(response.state, { preserveInputs });
      if (!preserveInputs && !response.state.isRunning && response.state.remainingSeconds === response.state.durationSeconds && saved.timerValues) {
        const savedDuration = TimerUtils.durationFromParts(saved.timerValues) || response.state.durationSeconds;
        putDurationInInputs(savedDuration);
        inputsDirty = durationFromInputs() !== response.state.durationSeconds;
        durationPresetUntouched = savedDuration === DEFAULT_DURATION_SECONDS;
      }
      if (saved.sfxVolume !== undefined && !Number.isFinite(response.state.sfxVolume)) {
        elements.volume.value = String(saved.sfxVolume);
      }
      elements.volumeValue.textContent = `${elements.volume.value}%`;
      scheduleEditor.setDefaultVolume(Number(saved.sfxVolume ?? 50));
      updateTimerDisplay();
      log('popup.loaded', { observedRunning: state.isRunning, observedRemainingSeconds: getDisplayedRemaining(),
        observedEndTime: state.endTime, observedPromptActive: state.promptActive, inputsDirty });
      if (document.activeElement === elements.hours) {
        elements.hours.select();
      }
    } catch (error) {
      log('popup.failed');
      elements.timerStatus.textContent = 'Unable to load';
      elements.startPause.disabled = true;
      elements.reset.disabled = true;
      setStatus(`${error.message} Open chrome://extensions and reload Reflection Timer.`, 'error');
    }
  }

  [elements.hours, elements.minutes, elements.seconds].forEach((input) => {
    input.addEventListener('input', () => {
      if (input === elements.hours && durationPresetUntouched) {
        elements.minutes.value = '0';
      }
      durationPresetUntouched = false;
      inputsDirty = true;
      updateTimerDisplay();
    });
    input.addEventListener('change', async () => {
      const max = Number(input.max);
      const value = Math.max(0, Number.parseInt(input.value, 10) || 0);
      input.value = String(Number.isFinite(max) ? Math.min(max, value) : value);
      inputsDirty = true;
      log('popup.durationChanged', { requestedDurationSeconds: durationFromInputs() });
      await saveTimerInputs();
      updateTimerDisplay();
    });
    input.addEventListener('keydown', (event) => {
      if (event.key === 'Enter') {
        elements.startPause.click();
      }
    });
  });

  elements.startPause.addEventListener('click', async () => {
    elements.startPause.disabled = true;
    setStatus('');
    try {
      let response;
      if (state && state.isRunning) {
        response = await sendMessage({ action: 'pauseTimer' });
      } else if (state && !inputsDirty && state.remainingSeconds > 0 && state.remainingSeconds < state.durationSeconds) {
        response = await sendMessage({ action: 'resumeTimer', autoRestart: elements.autoRestart.checked });
      } else {
        const durationSeconds = durationFromInputs();
        if (durationSeconds <= 0) {
          throw new Error('Choose a duration greater than zero.');
        }
        await saveTimerInputs();
        response = await sendMessage({
          action: 'startTimer',
          durationSeconds,
          autoRestart: elements.autoRestart.checked,
          sfxVolume: Number(elements.volume.value)
        });
      }
      adoptState(response.state);
    } catch (error) {
      setStatus(error.message, 'error');
    } finally {
      elements.startPause.disabled = false;
    }
  });

  elements.reset.addEventListener('click', async () => {
    try {
      const response = await sendMessage({
        action: 'resetTimer',
        durationSeconds: inputsDirty ? durationFromInputs() : undefined
      });
      adoptState(response.state);
      setStatus('Timer reset.', 'success');
    } catch (error) {
      setStatus(error.message, 'error');
    }
  });

  elements.autoRestart.addEventListener('change', async () => {
    const autoRestart = elements.autoRestart.checked;
    await chrome.storage.local.set({ autoRestart });
    try {
      const response = await sendMessage({ action: 'updateAutoRestart', autoRestart });
      state = response.state;
      updateTimerDisplay();
    } catch (error) {
      setStatus(error.message, 'error');
    }
  });

  elements.volume.addEventListener('input', () => {
    elements.volumeValue.textContent = `${elements.volume.value}%`;
  });
  elements.volume.addEventListener('change', async () => {
    try {
      const sfxVolume = Number(elements.volume.value);
      await chrome.storage.local.set({ sfxVolume });
      const response = await sendMessage({ action: 'updateVolume', sfxVolume });
      state = response.state;
    } catch (error) {
      setStatus(error.message, 'error');
    }
  });

  elements.testPrompt.addEventListener('click', async () => {
    try {
      const response = await sendMessage({ action: 'showTestPrompt' });
      if (response.success === false) {
        throw new Error(response.error);
      }
      window.close();
    } catch (error) {
      setStatus(error.message, 'error');
    }
  });

  elements.openSettings.addEventListener('click', () => chrome.runtime.openOptionsPage());

  chrome.runtime.onMessage.addListener((message) => {
    if (message.action === 'timerStateChanged' && message.state) {
      log('popup.stateReceived', { observedRunning: message.state.isRunning,
        observedRemainingSeconds: message.state.remainingSeconds, observedEndTime: message.state.endTime,
        observedPromptActive: message.state.promptActive });
      adoptState(message.state, { preserveInputs: !message.state.isRunning });
    }
  });

  function updateCurrentTime() {
    elements.currentTime.textContent = new Date().toLocaleTimeString([], {
      hour: 'numeric',
      minute: '2-digit',
      second: '2-digit'
    });
    updateTimerDisplay();
  }

  load();
  requestAnimationFrame(focusAndSelectHours);
  updateCurrentTime();
  setInterval(updateCurrentTime, 1000);
});
