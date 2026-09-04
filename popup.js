'use strict';

document.addEventListener('DOMContentLoaded', () => {
  const EXPECTED_API_VERSION = 3;
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
    startTime: document.getElementById('startTime'),
    schedule: document.getElementById('schedule'),
    cancelSchedule: document.getElementById('cancelSchedule'),
    scheduleStatus: document.getElementById('scheduleStatus'),
    schedulePanel: document.getElementById('schedulePanel'),
    openSettings: document.getElementById('openSettings'),
    testPrompt: document.getElementById('testPrompt'),
    currentTime: document.getElementById('currentTime'),
    status: document.getElementById('status')
  };

  let state = null;
  let inputsDirty = false;
  let durationPresetUntouched = durationFromInputs() === DEFAULT_DURATION_SECONDS;

  function sendMessage(message) {
    return new Promise((resolve, reject) => {
      let settled = false;
      const timeoutId = setTimeout(() => {
        if (!settled) {
          settled = true;
          reject(new Error('The timer service did not respond. Reload the extension once.'));
        }
      }, MESSAGE_TIMEOUT_MS);
      chrome.runtime.sendMessage(message, (response) => {
        if (settled) {
          return;
        }
        settled = true;
        clearTimeout(timeoutId);
        if (chrome.runtime.lastError) {
          reject(new Error(chrome.runtime.lastError.message));
        } else if (!response || response.success === false) {
          reject(new Error((response && response.error) || 'The extension did not respond.'));
        } else {
          resolve(response);
        }
      });
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
    const scheduled = state && state.scheduledTimer;
    elements.cancelSchedule.hidden = !scheduled;
    if (scheduled) {
      const when = new Date(scheduled.targetTime).toLocaleString([], {
        weekday: 'short',
        month: 'short',
        day: 'numeric',
        hour: 'numeric',
        minute: '2-digit'
      });
      elements.scheduleStatus.textContent = `Scheduled for ${when}.`;
      elements.schedulePanel.open = true;
      elements.schedule.textContent = 'Reschedule';
    } else {
      elements.scheduleStatus.textContent = '';
      elements.schedule.textContent = 'Schedule';
    }
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
      if (saved.sfxVolume !== undefined) {
        elements.volume.value = String(saved.sfxVolume);
      }
      elements.volumeValue.textContent = `${elements.volume.value}%`;
      if (saved.autoRestart !== undefined && !response.state.isRunning) {
        elements.autoRestart.checked = Boolean(saved.autoRestart);
      }
      updateTimerDisplay();
      if (document.activeElement === elements.hours) {
        elements.hours.select();
      }
    } catch (error) {
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
          autoRestart: elements.autoRestart.checked
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
    await chrome.storage.local.set({ sfxVolume: Number(elements.volume.value) });
  });

  elements.schedule.addEventListener('click', async () => {
    try {
      const targetTime = new Date(elements.startTime.value);
      if (!elements.startTime.value || !Number.isFinite(targetTime.getTime())) {
        throw new Error('Choose a start date and time.');
      }
      const durationSeconds = durationFromInputs();
      const response = await sendMessage({
        action: 'scheduleTimer',
        targetTime: targetTime.toISOString(),
        durationSeconds,
        autoRestart: elements.autoRestart.checked
      });
      adoptState(response.state);
      setStatus('Start time scheduled.', 'success');
    } catch (error) {
      setStatus(error.message, 'error');
    }
  });

  elements.cancelSchedule.addEventListener('click', async () => {
    try {
      const response = await sendMessage({ action: 'clearScheduledTimer' });
      state = response.state;
      updateScheduleDisplay();
      setStatus('Scheduled start cancelled.', 'success');
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
      state = message.state;
      updateTimerDisplay();
      updateScheduleDisplay();
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

  const defaultStart = new Date(Date.now() + (60 * 60 * 1000));
  defaultStart.setSeconds(0, 0);
  const localDefault = new Date(defaultStart.getTime() - (defaultStart.getTimezoneOffset() * 60_000));
  elements.startTime.value = localDefault.toISOString().slice(0, 16);

  load();
  requestAnimationFrame(focusAndSelectHours);
  updateCurrentTime();
  setInterval(updateCurrentTime, 1000);
});
