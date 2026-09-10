import {setText, formatClock, durationSeconds, reconcileRows} from './ui.js';

const $ = id => document.getElementById(id);
let view = new URLSearchParams(location.search).get('view') || 'main';
if (!['main', 'compact', 'reflection'].includes(view)) view = 'main';
document.body.dataset.view = view;
setText($('page-title'), view === 'main' ? 'Reflection Timer' : view === 'compact' ? 'Compact timer' : 'Your reflection');
const bridge = window.chrome?.webview;
const requests = new Map();
let requestSequence = 0, state, promptId, initial = true, durationDirty = false, loadedPrompt;
let saveDelay, saving = Promise.resolve(), queued = false, lastSavedDraft = '';

function send(action, data = {}) {
  return new Promise((resolve, reject) => {
    if (!bridge) { reject(new Error('Open this interface through the accessibility preview app.')); return; }
    const requestId = String(++requestSequence);
    const timeout = setTimeout(() => { requests.delete(requestId); reject(new Error('The app did not respond. Try again.')); }, 12000);
    requests.set(requestId, {resolve, reject, timeout});
    bridge.postMessage({requestId, action, data});
  });
}
function error(message) { setText($('error'), message); }
function announce(message) { setText($('status'), ''); setTimeout(() => setText($('status'), message), 60); }
function run(action) { error(''); Promise.resolve().then(action).catch(e => error(e.message)); }
function available(button, yes) { button.setAttribute('aria-disabled', String(!yes)); }
function bind(id, action) { $(id).addEventListener('click', () => { if ($(id).getAttribute('aria-disabled') !== 'true') run(action); }); }
function snapshot(clock, speak = false) {
  const text = `${clock.text} remaining. ${clock.status}.`;
  setText($('time-snapshot'), `Time checked: ${text}`);
  if (speak) announce(text);
}
function readDuration() { return durationSeconds(['hours','minutes','seconds'].map(id => $(id).value.trim())); }
function applyDuration(seconds) {
  $('hours').value = Math.floor(seconds / 3600); $('minutes').value = Math.floor(seconds / 60) % 60; $('seconds').value = seconds % 60;
}
function draft() { return {id: promptId, text: $('reflection-text').value, reason: $('early-reason').value}; }
function saveDraft() {
  clearTimeout(saveDelay);
  if (!loadedPrompt || queued) return saving;
  const data = draft(), signature = JSON.stringify(data);
  if (signature === lastSavedDraft) return saving;
  saving = saving.catch(() => {}).then(async () => {
    await send('draft', data); lastSavedDraft = signature; setText($('draft-status'), 'Draft saved locally.');
  });
  return saving;
}
function renderReflection() {
  const prompt = state.prompts.find(p => p.id === promptId);
  if (!prompt || loadedPrompt === prompt.id) return;
  loadedPrompt = prompt.id;
  setText($('reflection-heading'), prompt.isCheckIn ? 'Session check-in' : 'Session reflection');
  setText($('reflection-context'), `${prompt.endedEarly ? 'Session ended early. ' : ''}${prompt.actual} spent; ${prompt.allotted} allotted. ${prompt.completed}.`);
  $('reason-group').hidden = !prompt.endedEarly;
  $('reflection-text').value = prompt.draft; $('early-reason').value = prompt.earlyEndReason;
  lastSavedDraft = JSON.stringify(draft());
}
function createTableRow(columns) {
  const row = document.createElement('tr');
  columns.forEach((_, index) => { const cell = document.createElement(index === 0 ? 'th' : 'td'); if (index === 0) cell.scope = 'row'; row.append(cell); });
  return row;
}
function rowButton(cell, label, action) {
  const button = document.createElement('button'); button.type = 'button'; button.textContent = label;
  button.addEventListener('click', () => run(() => action(button.closest('tr').dataset.id, button)));
  cell.append(button); return button;
}
function tables() {
  reconcileRows($('schedule-rows'), state.schedules, () => {
    const row = createTableRow(['start','duration','repeat','low','status','action']);
    rowButton(row.cells[5], 'Remove', async (id, button) => {
      const next = button.closest('tr').nextElementSibling?.querySelector('button') || button.closest('tr').previousElementSibling?.querySelector('button') || $('schedule-start');
      await send('removeSchedule', {id}); next.focus();
    }); return row;
  }, (row, record) => {
    [record.start,record.duration,record.repeat,record.lowTime,record.status].forEach((text,index) => setText(row.cells[index],text));
    row.cells[5].firstChild.setAttribute('aria-label',`Remove session starting ${record.start}`);
  });
  $('schedule-empty').hidden = state.schedules.length !== 0;
  reconcileRows($('outbox-rows'), state.outbox, () => {
    const row = createTableRow(['saved','destination','status','attempts','actions']);
    rowButton(row.cells[4], 'Details', id => {
      const item = state.outbox.find(entry => entry.id === id);
      setText($('entry-meta'),`${item.saved}. ${item.duration}. ${item.status}.`); setText($('entry-text'),item.message);
      $('entry-dialog').showModal(); $('entry-title').focus();
    });
    rowButton(row.cells[4], 'Simulate success', id => send('simulate',{id})); return row;
  }, (row, record) => {
    [record.saved,record.destination,record.status,record.attempts].forEach((text,index) => setText(row.cells[index],text));
    row.cells[4].children[0].setAttribute('aria-label',`Details for entry saved ${record.saved}`);
    row.cells[4].children[1].setAttribute('aria-label',`Simulate success for entry saved ${record.saved}`);
  });
  $('outbox-empty').hidden = state.outbox.length !== 0;
}
function render(next) {
  const previous = state; state = next;
  if (view === 'reflection') { renderReflection(); return; }
  setText($('visual-clock'),formatClock(state.clock.seconds)); setText($('timer-state'),state.clock.status);
  if (!previous || previous.clock.status !== state.clock.status) snapshot(state.clock);
  const running = state.clock.status === 'Running';
  ['hours','minutes','seconds','threshold'].forEach(id => $(id).readOnly = running);
  // Readonly duration controls remain focusable. No tick changes their values.
  if (initial || (!durationDirty && state.timer.durationSeconds !== previous?.timer.durationSeconds)) applyDuration(state.timer.durationSeconds);
  if (initial) { $('repeat').checked = state.timer.autoRestart; $('low-time').checked = state.timer.enabled; $('threshold').value = state.timer.threshold; }
  setText($('toggle'),running ? 'Pause timer' : state.clock.status === 'Paused' && !durationDirty ? 'Resume timer' : 'Start timer');
  available($('end'),running); available($('check-in'),running || state.clock.status === 'Paused');
  if (view === 'main') {
    setText($('pending-count'),`${state.prompts.length} pending reflection${state.prompts.length === 1 ? '' : 's'}.`);
    reconcileRows($('pending-list'),state.prompts,record => {
      const li=document.createElement('li'), button=document.createElement('button'); button.type='button'; li.append(button);
      button.addEventListener('click',()=>run(()=>send('openReflection',{id:record.id}))); return li;
    },(li,record)=>setText(li.firstChild,`${record.isCheckIn ? 'Check-in' : 'Reflection'} from ${record.completed}`));
    tables();
  }
  initial = false;
}
bridge?.addEventListener('message', event => {
  const message = event.data;
  if (message.type === 'reply') {
    const pending=requests.get(message.requestId); if (!pending) return;
    clearTimeout(pending.timeout); requests.delete(message.requestId);
    if (message.error) pending.reject(new Error(message.error)); else pending.resolve();
  } else if (message.type === 'init') {
    promptId=message.promptId; render(message.state);
    // Initial focus is deliberate; subsequent updates never repeat this.
    (view === 'reflection' ? $('reflection-heading') : $('page-title')).focus();
  } else if (message.type === 'state') render(message.state);
  else if (message.type === 'clock') setText($('visual-clock'),formatClock(message.clock.seconds));
  else if (message.type === 'timeRead') snapshot(message.clock,true);
  else if (message.type === 'announcement') announce(message.message);
  else if (message.type === 'flush') {
    saveDraft().then(()=>send('flushed')).catch(e=>{ error(e.message); send('flushFailed').catch(()=>{}); });
  }
});
['hours','minutes','seconds'].forEach(id => $(id).addEventListener('input',()=>{ durationDirty=true; if(state?.clock.status !== 'Running') setText($('toggle'),'Start timer'); }));
$('timer-editor').addEventListener('submit',event=>{ event.preventDefault(); run(async()=>{
  const seconds = readDuration(), threshold = Number($('threshold').value);
  await send('toggle',{seconds,threshold,repeat:$('repeat').checked,lowTime:$('low-time').checked}); durationDirty=false; applyDuration(state.timer.durationSeconds); render(state);
}); });
bind('read-time',()=>send('readTime'));
bind('reset',async()=>{ await send('reset',{seconds:readDuration()}); durationDirty=false; applyDuration(state.timer.durationSeconds); render(state); });
bind('end',()=>send('end')); bind('check-in',()=>send('checkIn')); bind('practice',()=>send('testReflection'));
bind('open-compact',()=>send('compact')); bind('open-main',()=>send('main')); bind('close-compact',()=>send('close'));
bind('compact-mode',()=>{
  const tiny=document.body.dataset.tiny !== 'true'; document.body.dataset.tiny=String(tiny);
  $('compact-mode').setAttribute('aria-expanded',String(!tiny)); setText($('compact-mode'),tiny?'Show timer controls':'Time-only view');
});
$('schedule-form').addEventListener('submit',event=>{event.preventDefault();run(async()=>{
  const minutes=$('schedule-minutes').value.trim(); const seconds=durationSeconds(['0',minutes,'0']);
  await send('schedule',{start:$('schedule-start').value,seconds});
});});
['reflection-text','early-reason'].forEach(id=>$(id).addEventListener('input',()=>{
  setText($('draft-status'),'Saving draft…'); clearTimeout(saveDelay); saveDelay=setTimeout(()=>saveDraft().catch(e=>error(e.message)),300);
}));
$('reflection-form').addEventListener('submit',event=>{event.preventDefault();run(async()=>{
  if(!$('reflection-text').value.trim()) { $('reflection-text').setAttribute('aria-invalid','true'); $('reflection-text').focus(); throw new Error('Write a reflection before saving.'); }
  $('reflection-text').removeAttribute('aria-invalid'); await saveDraft(); queued=true;
  try { await send('queue',draft()); } catch(e) { queued=false; throw e; }
});});
bind('later',async()=>{await saveDraft();await send('close');});
run(()=>send('ready'));
