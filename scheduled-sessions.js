'use strict';

// Independent editor: never reads duration/repeat controls from the live timer.
globalThis.ScheduledSessions = {
  createEditor({ sendMessage, onState, onError }) {
    const ids = ['startTime', 'scheduleHours', 'scheduleMinutes', 'scheduleSeconds', 'scheduleAutoRestart',
      'scheduleVolume', 'scheduleVolumeValue', 'schedule', 'cancelScheduleEdit', 'scheduleEditorTitle',
      'scheduleStatus', 'scheduledSessions'];
    const elements = Object.fromEntries(ids.map((id) => [id, document.getElementById(id)]));
    let entries = [];
    let editingId = null;
    let busy = false;
    let dirty = false;
    let untouchedPreset = true;
    let defaultVolume = 50;
    const fields = [elements.scheduleHours, elements.scheduleMinutes, elements.scheduleSeconds];

    function duration() {
      return TimerUtils.durationFromParts({ hours: fields[0].value, minutes: fields[1].value, seconds: fields[2].value });
    }
    function putDuration(seconds) {
      const parts = TimerUtils.splitDuration(seconds);
      fields[0].value = String(parts.hours);
      fields[1].value = String(parts.minutes);
      fields[2].value = String(parts.seconds);
    }
    function localInput(time) {
      const date = new Date(time);
      return new Date(date.getTime() - date.getTimezoneOffset() * 60000).toISOString().slice(0, 16);
    }
    function resetEditor() {
      editingId = null;
      dirty = false;
      untouchedPreset = true;
      elements.scheduleEditorTitle.textContent = 'Add a session';
      elements.schedule.textContent = 'Add session';
      elements.cancelScheduleEdit.hidden = true;
      elements.startTime.value = localInput(Date.now() + 3600000);
      putDuration(1500);
      elements.scheduleAutoRestart.checked = false;
      elements.scheduleVolume.value = String(defaultVolume);
      elements.scheduleVolumeValue.textContent = `${defaultVolume}%`;
    }
    function edit(entry) {
      editingId = entry.id;
      dirty = true;
      untouchedPreset = false;
      elements.scheduleEditorTitle.textContent = 'Edit session';
      elements.schedule.textContent = 'Save changes';
      elements.cancelScheduleEdit.hidden = false;
      elements.startTime.value = localInput(entry.targetTime);
      putDuration(entry.durationSeconds);
      elements.scheduleAutoRestart.checked = entry.autoRestart;
      elements.scheduleVolume.value = String(entry.sfxVolume);
      elements.scheduleVolumeValue.textContent = `${entry.sfxVolume}%`;
      elements.startTime.focus();
    }
    function create(tag, text, className) {
      const element = document.createElement(tag);
      element.textContent = text;
      if (className) element.className = className;
      return element;
    }
    function formatDuration(seconds) {
      const parts = TimerUtils.splitDuration(seconds);
      return `${parts.hours ? `${parts.hours}h ` : ''}${parts.minutes}m${parts.seconds ? ` ${parts.seconds}s` : ''}`;
    }
    function renderList() {
      elements.scheduledSessions.replaceChildren();
      elements.scheduleStatus.textContent = entries.length ? `${entries.length} scheduled session${entries.length === 1 ? '' : 's'}` : 'No sessions scheduled yet.';
      for (const entry of entries) {
        const when = new Date(entry.targetTime).toLocaleString([], { month: 'short', day: 'numeric', year: 'numeric', hour: 'numeric', minute: '2-digit' });
        const item = create('li', '', 'scheduled-session');
        const time = create('time', when);
        time.dateTime = new Date(entry.targetTime).toISOString();
        const detail = create('p', `${formatDuration(entry.durationSeconds)} · Repeat ${entry.autoRestart ? 'on' : 'off'} · Sound ${entry.sfxVolume}%`, 'hint');
        const actions = create('div', '', 'schedule-entry-actions');
        const editButton = create('button', 'Edit', 'text-button');
        const removeButton = create('button', 'Remove', 'text-button');
        editButton.type = removeButton.type = 'button';
        editButton.disabled = removeButton.disabled = busy;
        editButton.setAttribute('aria-label', `Edit session starting ${when}`);
        removeButton.setAttribute('aria-label', `Remove session starting ${when}`);
        editButton.addEventListener('click', () => edit(entry));
        removeButton.addEventListener('click', () => run(async () => {
          const response = await sendMessage({ action: 'removeScheduledSession', id: entry.id });
          if (editingId === entry.id) resetEditor();
          onState(response.state);
        }));
        actions.append(editButton, removeButton);
        item.append(time, detail, actions);
        elements.scheduledSessions.append(item);
      }
    }
    function setBusy(value) {
      busy = value;
      for (const element of [...fields, elements.startTime, elements.scheduleAutoRestart, elements.scheduleVolume, elements.schedule, elements.cancelScheduleEdit]) element.disabled = value;
      renderList();
    }
    async function run(operation) {
      if (busy) return;
      setBusy(true);
      try { await operation(); }
      catch (error) { onError(error.message); }
      finally { setBusy(false); }
    }

    fields.forEach((input) => {
      input.addEventListener('input', () => {
        if (input === fields[0] && untouchedPreset) fields[1].value = '0';
        dirty = true;
        untouchedPreset = false;
      });
      input.addEventListener('change', () => {
        input.value = String(Math.max(0, Math.min(Number(input.max), Number.parseInt(input.value, 10) || 0)));
      });
    });
    for (const input of [elements.startTime, elements.scheduleAutoRestart, elements.scheduleVolume]) input.addEventListener('input', () => { dirty = true; });
    elements.scheduleVolume.addEventListener('input', () => {
      dirty = true;
      elements.scheduleVolumeValue.textContent = `${elements.scheduleVolume.value}%`;
    });
    elements.cancelScheduleEdit.addEventListener('click', resetEditor);
    elements.schedule.addEventListener('click', () => run(async () => {
      const targetTime = new Date(elements.startTime.value).getTime();
      if (!Number.isFinite(targetTime) || targetTime <= Date.now()) throw new Error('Choose a future session start date and time.');
      const durationSeconds = duration();
      if (!durationSeconds) throw new Error('Choose a scheduled duration greater than zero.');
      const response = await sendMessage({
        action: 'saveScheduledSession', id: editingId, targetTime, durationSeconds,
        autoRestart: elements.scheduleAutoRestart.checked, sfxVolume: Number(elements.scheduleVolume.value)
      });
      resetEditor();
      onState(response.state);
    }));
    resetEditor();
    return {
      render(state) {
        entries = state?.scheduledTimers || (state?.scheduledTimer ? [state.scheduledTimer] : []);
        if (editingId !== null && !entries.some((entry) => entry.id === editingId)) {
          resetEditor();
          onError('The session you were editing started or was removed. You can add a new session.');
        }
        renderList();
      },
      setDefaultVolume(volume) {
        if (Number.isFinite(volume)) defaultVolume = volume;
        if (!dirty && editingId === null) {
          elements.scheduleVolume.value = String(defaultVolume);
          elements.scheduleVolumeValue.textContent = `${defaultVolume}%`;
        }
      }
    };
  }
};
