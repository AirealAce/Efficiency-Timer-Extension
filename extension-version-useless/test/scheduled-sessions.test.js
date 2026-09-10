'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const TimerUtils = require('../timer-utils.js');
const source = fs.readFileSync(path.join(__dirname, '..', 'scheduled-sessions.js'), 'utf8');

function createHarness() {
  const elements = {};
  const requests = [];
  const errors = [];
  let entries = [];
  let rejectSave = false;
  function element(id) {
    return { id, value: '', checked: false, max: id === 'scheduleHours' ? '8760' : '59', children: [], listeners: {},
      textContent: '', hidden: false, disabled: false,
      append(...children) { this.children.push(...children); },
      replaceChildren(...children) { this.children = children; },
      setAttribute(key, value) { this[key] = value; },
      addEventListener(name, callback) { this.listeners[name] = callback; },
      focus() { this.focused = true; }
    };
  }
  const context = { TimerUtils, document: {
    getElementById(id) { return elements[id] ||= element(id); },
    createElement(tag) { return element(tag); }
  } };
  vm.runInNewContext(source, context);
  const editor = context.ScheduledSessions.createEditor({
    async sendMessage(message) {
      requests.push(message);
      if (rejectSave) throw new Error('Unable to save this session.');
      if (message.action === 'saveScheduledSession') {
        const id = message.id ?? (entries.length + 1);
        entries = [...entries.filter((entry) => entry.id !== id), { ...message, id }].sort((a, b) => a.targetTime - b.targetTime);
      } else if (message.action === 'removeScheduledSession') entries = entries.filter((entry) => entry.id !== message.id);
      return { state: { scheduledTimers: entries } };
    },
    onState(state) { editor.render(state); },
    onError(message) { errors.push(message); }
  });
  editor.render({ scheduledTimers: entries });
  return { editor, elements, requests, errors,
    setEntries(next) { entries = next; editor.render({ scheduledTimers: entries }); },
    set rejectSave(value) { rejectSave = value; },
    get entries() { return entries; }
  };
}

test('new scheduled session uses only its own start, duration, repeat and sound fields', async () => {
  const harness = createHarness();
  const e = harness.elements;
  e.scheduleHours.value = '1';
  e.scheduleHours.listeners.input();
  assert.equal(e.scheduleMinutes.value, '0', 'typing hours clears the untouched 25-minute preset');
  e.scheduleSeconds.value = '30';
  e.scheduleAutoRestart.checked = true;
  e.scheduleVolume.value = '0';
  const expectedStart = new Date(e.startTime.value).getTime();
  await e.schedule.listeners.click();
  assert.equal(harness.requests.length, 1);
  assert.equal(harness.requests[0].action, 'saveScheduledSession');
  assert.equal(harness.requests[0].durationSeconds, 3630);
  assert.equal(harness.requests[0].targetTime, expectedStart);
  assert.equal(harness.requests[0].autoRestart, true);
  assert.equal(harness.requests[0].sfxVolume, 0);
  assert.equal(e.scheduledSessions.children.length, 1);
  assert.match(e.scheduledSessions.children[0].children[1].textContent, /Repeat on · Sound 0%/);
});

test('edit loads one entry and saves it by stable ID without replacing other entries', async () => {
  const harness = createHarness();
  const future = Date.now() + 3600000;
  harness.setEntries([
    { id: 42, targetTime: future, durationSeconds: 4500, autoRestart: true, sfxVolume: 87 },
    { id: 99, targetTime: future + 3600000, durationSeconds: 600, autoRestart: false, sfxVolume: 20 }
  ]);
  const e = harness.elements;
  e.scheduledSessions.children[0].children[2].children[0].listeners.click();
  assert.equal(e.scheduleEditorTitle.textContent, 'Edit session');
  assert.equal(e.scheduleHours.value, '1');
  assert.equal(e.scheduleMinutes.value, '15');
  assert.equal(e.scheduleAutoRestart.checked, true);
  assert.equal(e.scheduleVolume.value, '87');
  e.scheduleHours.value = '2';
  e.scheduleHours.listeners.input();
  assert.equal(e.scheduleMinutes.value, '15', 'editing an explicit duration must not wipe its minutes');
  await e.schedule.listeners.click();
  assert.equal(harness.requests[0].id, 42);
  assert.equal(harness.entries.length, 2);
  assert.equal(harness.entries.find((entry) => entry.id === 42).durationSeconds, 8100);
  assert.equal(harness.entries.find((entry) => entry.id === 99).durationSeconds, 600);
  assert.equal(e.scheduleEditorTitle.textContent, 'Add a session');
});

test('cancel edit does not save and removing the edited entry targets only its ID', async () => {
  const harness = createHarness();
  harness.setEntries([{ id: 7, targetTime: Date.now() + 3600000, durationSeconds: 600, autoRestart: false, sfxVolume: 50 }]);
  const e = harness.elements;
  e.scheduledSessions.children[0].children[2].children[0].listeners.click();
  e.cancelScheduleEdit.listeners.click();
  assert.equal(harness.requests.length, 0);
  e.scheduledSessions.children[0].children[2].children[0].listeners.click();
  await e.scheduledSessions.children[0].children[2].children[1].listeners.click();
  assert.equal(harness.requests[0].action, 'removeScheduledSession');
  assert.equal(harness.requests[0].id, 7);
  assert.equal(harness.entries.length, 0);
  assert.equal(harness.errors.length, 0);
});

test('timer broadcasts and async preference loading preserve an in-progress schedule draft', () => {
  const harness = createHarness();
  const e = harness.elements;
  e.scheduleMinutes.value = '47';
  e.scheduleMinutes.listeners.input();
  e.scheduleVolume.value = '32';
  e.scheduleVolume.listeners.input();
  harness.editor.setDefaultVolume(90);
  harness.editor.render({ isRunning: true, durationSeconds: 3600, autoRestart: false, sfxVolume: 10, scheduledTimers: [] });
  assert.equal(e.scheduleMinutes.value, '47');
  assert.equal(e.scheduleVolume.value, '32');
  assert.equal(e.scheduleHours.disabled, false);
});

test('expired or zero-duration forms and server failures keep the draft for correction', async () => {
  const harness = createHarness();
  const e = harness.elements;
  e.startTime.value = '2000-01-01T12:00';
  await e.schedule.listeners.click();
  assert.match(harness.errors.at(-1), /future/);
  assert.equal(harness.requests.length, 0);
  e.cancelScheduleEdit.listeners.click();
  e.scheduleMinutes.value = '0';
  await e.schedule.listeners.click();
  assert.match(harness.errors.at(-1), /greater than zero/);
  e.scheduleMinutes.value = '15';
  harness.rejectSave = true;
  await e.schedule.listeners.click();
  assert.match(harness.errors.at(-1), /Unable to save/);
  assert.equal(e.scheduleMinutes.value, '15');
  assert.equal(e.schedule.disabled, false);
});

test('an appointment that starts during editing is not silently recreated', () => {
  const harness = createHarness();
  harness.setEntries([{ id: 7, targetTime: Date.now() + 3600000, durationSeconds: 600, autoRestart: true, sfxVolume: 70 }]);
  harness.elements.scheduledSessions.children[0].children[2].children[0].listeners.click();
  harness.setEntries([]);
  assert.equal(harness.elements.scheduleEditorTitle.textContent, 'Add a session');
  assert.match(harness.errors.at(-1), /started or was removed/);
  assert.equal(harness.requests.length, 0);
});
