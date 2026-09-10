import {setText, formatClock, durationSeconds, reconcileRows} from './ui.js';
import {settingsUI, localDateTime} from './settings.js';
import {arrangeApp} from './layout.js';

const $ = id => document.getElementById(id);
let view = new URLSearchParams(location.search).get('view') || 'main';
if (!['main', 'compact', 'reflection'].includes(view)) view = 'main';
document.body.dataset.view = view;
setText($('page-title'), view === 'main' ? 'Reflection Timer' : view === 'compact' ? 'Compact timer' : 'How did you spend your time?');
const layout=arrangeApp(view);
const bridge = window.chrome?.webview;
const requests = new Map();
let requestSequence = 0, state, promptId, initial = true, durationDirty = false, loadedPrompt;
let saveDelay, saving = Promise.resolve(), queued = false, lastSavedDraft = '', scheduleEdit, deliveryDecision;

function send(action, data = {}) {
  return new Promise((resolve, reject) => {
    if (!bridge) { reject(new Error('Open this interface through the accessibility preview app.')); return; }
    const requestId = String(++requestSequence);
    const nativeDialog=['browseSound','browseLowSound','setupScript','exportDiagnostics','importSchedules'].includes(action);
    const timeout = nativeDialog ? undefined : setTimeout(() => { requests.delete(requestId); reject(new Error('The app did not respond. Check its status before trying again.')); }, 35000);
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
function sharedDuration(parts){
  durationDirty=Array.isArray(parts);
  if(parts)['hours','minutes','seconds'].forEach((id,i)=>{if($(id).value!==parts[i])$(id).value=parts[i];});else if(state)applyDuration(state.timer.durationSeconds);
  if(state?.clock.status!=='Running'){setText($('toggle'),durationDirty?'Start':state?.clock.status==='Paused'?'Resume':'Start');try{setText($('visual-clock'),formatClock(durationDirty?readDuration():state.clock.seconds));}catch{setText($('visual-clock'),'—');}}
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
  button.addEventListener('click', () => { if(button.getAttribute('aria-disabled')!=='true') run(() => action(button.closest('tr').dataset.id, button)); });
  cell.append(button); return button;
}
function reviewDelivery(id, action) {
  deliveryDecision={id,action};
  setText($('delivery-explanation'),action==='retry'
    ? 'This receiver reported an uncertain write. Check your Google sheet first. Retrying creates a new request and could duplicate an entry already saved there.'
    : 'Check that this exact reflection is already in your Google sheet. Confirming removes it from pending delivery without sending it.');
  setText($('delivery-confirm'),action==='retry'?'I checked the sheet — retry as a new request':'I found this entry in my sheet — mark already sent');
  $('delivery-dialog').showModal();$('delivery-title').focus();
}
function clearScheduleEdit() {
  scheduleEdit=undefined;setText($('schedule-legend'),'Add a session');setText($('schedule-save'),'Add session');$('schedule-cancel').hidden=true;
  settings.resetScheduleLow();run(()=>send('editScheduleDraft',{id:null}));
}
function tables() {
  reconcileRows($('schedule-rows'), state.schedules, () => {
    const row = createTableRow(['start','duration','repeat','low','status','cutoff','volume','action']);
    rowButton(row.cells[7], 'Remove', async (id, button) => {
      const next = button.closest('tr').nextElementSibling?.querySelector('button') || button.closest('tr').previousElementSibling?.querySelector('button') || $('schedule-start');
      await send('removeSchedule', {id}); next.focus();
    });
    rowButton(row.cells[7], 'Edit', async id=>{
      const record=state.schedules.find(s=>s.id===id);scheduleEdit=id;
      $('schedule-start').value=record.editStart; $('schedule-hours').value=Math.floor(record.durationSeconds/3600);$('schedule-minutes').value=Math.floor(record.durationSeconds/60)%60;$('schedule-seconds').value=record.durationSeconds%60;
      $('schedule-repeat').checked=record.repeat==='On';$('schedule-low').checked=record.lowTime==='On';
      $('schedule-volume').value=record.volume;$('schedule-cutoff').value=localDateTime(record.autoRestartUntil);
      $('schedule-cutoff-enabled').checked=!!record.autoRestartUntil;$('schedule-cutoff').disabled=!record.autoRestartUntil;
      settings.resetScheduleLow();await send('editScheduleDraft',{id});
      setText($('schedule-legend'),'Edit scheduled session');setText($('schedule-save'),'Save scheduled changes');$('schedule-cancel').hidden=false;$('schedule-start').focus();
    });
    ['Start now','Wait','Skip'].forEach((label,decision)=>rowButton(row.cells[7],label,async id=>{await send('resolveSchedule',{id,decision});$('schedule-start').focus();}));
    return row;
  }, (row, record) => {
    [record.start,record.duration,record.repeat,record.lowTime,record.status,record.autoRestartUntil?new Date(record.autoRestartUntil).toLocaleString():'Off',`${record.volume}%`].forEach((text,index) => setText(row.cells[index],text));
    row.cells[7].firstChild.setAttribute('aria-label',`Remove session starting ${record.start}`);
    row.cells[7].children[1].setAttribute('aria-label',`Edit session starting ${record.start}`);
    [...row.cells[7].children].slice(2).forEach(button=>{button.hidden=record.status!=='Needs choice';});
  });
  $('schedule-empty').hidden = state.schedules.length !== 0;
  reconcileRows($('outbox-rows'), state.outbox, () => {
    const row = createTableRow(['saved','destination','status','attempts','actions']);
    rowButton(row.cells[4], 'Details', id => {
      const item = state.outbox.find(entry => entry.id === id);
      setText($('entry-meta'),`${item.saved}. ${item.duration}. ${item.status}.${item.error ? ' Delivery issue: '+item.error+'. Check your sheet before retrying an uncertain write.' : ''}`); setText($('entry-text'),item.message);
      $('entry-dialog').showModal(); $('entry-title').focus();
    });
    rowButton(row.cells[4], 'Simulate success', id => send('simulate',{id}));
    rowButton(row.cells[4], 'Retry delivery', id => {
      if(['write_uncertain','id_conflict'].includes(state.outbox.find(o=>o.id===id).error)) reviewDelivery(id,'retry');
      else return send('retry',{id});
    });
    rowButton(row.cells[4], 'Mark already sent',id=>reviewDelivery(id,'markSent')); return row;
  }, (row, record) => {
    [record.saved,record.destination,record.status,record.attempts].forEach((text,index) => setText(row.cells[index],text));
    row.cells[4].children[0].setAttribute('aria-label',`Details for entry saved ${record.saved}`);
    row.cells[4].children[1].setAttribute('aria-label',`Simulate success for entry saved ${record.saved}`);
    row.cells[4].children[1].hidden=record.localOnly===false;
    const retry=row.cells[4].children[2];retry.hidden=record.localOnly!==false;
    retry.setAttribute('aria-label',`Retry delivery for entry saved ${record.saved}`); available(retry,!['Sent','Sending'].includes(record.status));
    row.cells[4].children[3].hidden=record.localOnly!==false || record.status!=='NeedsReview';
    row.cells[4].children[3].setAttribute('aria-label',`Mark already sent for entry saved ${record.saved}`);
  });
  $('outbox-empty').hidden = state.outbox.length !== 0;
}
function render(next) {
  const previous = state; state = next;
  settings.state(state);
  if (view === 'reflection') { renderReflection(); return; }
  setText($('visual-clock'),formatClock(state.clock.seconds)); setText($('timer-state'),state.clock.status);
  if (!previous || previous.clock.status !== state.clock.status) snapshot(state.clock);
  const running = state.clock.status === 'Running';
  ['hours','minutes','seconds'].forEach(id => $(id).readOnly = running);
  // Readonly duration controls remain focusable. No tick changes their values.
  if (initial || (!durationDirty && state.timer.durationSeconds !== previous?.timer.durationSeconds)) applyDuration(state.timer.durationSeconds);
  $('repeat').checked = state.timer.autoRestart;
  setText($('toggle'),running ? 'Pause' : state.clock.status === 'Paused' && !durationDirty ? 'Resume' : 'Start');
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
  if(durationDirty&&!running){try{setText($('visual-clock'),formatClock(readDuration()));}catch{setText($('visual-clock'),'—');}}
}
bridge?.addEventListener('message', event => {
  const message = event.data;
  if (message.type === 'reply') {
    const pending=requests.get(message.requestId); if (!pending) return;
    clearTimeout(pending.timeout); requests.delete(message.requestId);
    if (message.error) pending.reject(new Error(message.error)); else pending.resolve();
  } else if (message.type === 'init') {
    promptId=message.promptId; render(message.state);
    if(view!=='reflection'&&message.state.durationDraft)sharedDuration(message.state.durationDraft);
    // Initial focus is deliberate; subsequent updates never repeat this.
    if(view==='reflection'){$('reflection-text').focus();$('reflection-text').selectionStart=$('reflection-text').value.length;}
    else {const id=['hours','minutes','seconds'].find(id=>Number($(id).value)>0)||'hours';$(id).focus();$(id).select();}
    settings.load();
  } else if (message.type === 'state') render(message.state);
  else if (message.type === 'clock') {if(state?.clock.status==='Running'||!durationDirty)setText($('visual-clock'),formatClock(message.clock.seconds));}
  else if (message.type === 'timeRead') snapshot(message.clock,true);
  else if (message.type === 'announcement') announce(message.message);
  else if (message.type === 'durationDraft'&&view!=='reflection')sharedDuration(message.parts);
  else if (message.type === 'focusTimer') {layout.select('timer');const id=['hours','minutes','seconds'].find(id=>Number($(id).value)>0)||'hours';$(id).focus();$(id).select();}
  else if (message.type === 'flush') {
    saveDraft().then(()=>send('flushed')).catch(e=>{ error(e.message); send('flushFailed').catch(()=>{}); });
  } else settings.message(message);
});
const settings=settingsUI({send,run,bind,view,announce});
bind('delivery-confirm',async()=>{
  const decision=deliveryDecision;await send(decision.action,{id:decision.id,confirmed:true});$('delivery-dialog').close();
  const target=$('outbox-rows').querySelector(`[data-id="${CSS.escape(decision.id)}"] button`) || $('send-pending');target.focus();
});
['hours','minutes','seconds'].forEach(id => $(id).addEventListener('input',()=>{const parts=['hours','minutes','seconds'].map(id=>$(id).value);sharedDuration(parts);run(()=>send('durationDraft',{parts}));}));
$('timer-editor').addEventListener('submit',event=>{ event.preventDefault(); run(async()=>{
  const seconds = readDuration(), threshold = Number($('threshold').value);
  await send('toggle',{seconds,threshold,repeat:$('repeat').checked,lowTime:$('low-time').checked}); durationDirty=false; applyDuration(state.timer.durationSeconds); render(state);
}); });
bind('read-time',()=>send('readTime'));
bind('reset',async()=>{ await send('reset',{seconds:readDuration()}); durationDirty=false; applyDuration(state.timer.durationSeconds); render(state); });
bind('end',()=>send('end')); bind('check-in',()=>send('checkIn')); bind('practice',()=>send('testReflection'));
$('repeat').addEventListener('change',()=>run(()=>send('repeat',{enabled:$('repeat').checked})));
bind('open-compact',()=>send('toggleCompact')); bind('open-main',()=>send('main')); bind('close-compact',()=>send('close'));
if(view==='main'){
  bind('show-pending',()=>state.prompts.length?send('openReflection',{id:state.prompts[0].id}):announce('No pending reflections.'));
  bind('timer-mark-issue',()=>send('markIssue'));bind('quit',()=>send('quit'));
  $('quick-schedule-form').addEventListener('submit',event=>{event.preventDefault();run(()=>send('schedule',{start:$('quick-start').value,seconds:readDuration(),repeat:$('repeat').checked,lowOptions:settings.timerLow(),fromTimer:true,volume:state.appVolume??50,cutoff:$('cutoff-enabled').checked?$('cutoff').value:''}));});
}
bind('compact-mode',()=>{
  const tiny=document.body.dataset.tiny !== 'true'; document.body.dataset.tiny=String(tiny);
  $('compact-mode').setAttribute('aria-expanded',String(!tiny)); setText($('compact-mode'),tiny?'Show timer controls':'Time-only view');
});
$('schedule-form').addEventListener('submit',event=>{event.preventDefault();run(async()=>{
  const seconds=durationSeconds(['schedule-hours','schedule-minutes','schedule-seconds'].map(id=>$(id).value.trim()));
  await send('schedule',{id:scheduleEdit,start:$('schedule-start').value,seconds,repeat:$('schedule-repeat').checked,lowTime:$('schedule-low').checked,
    lowOptions:settings.scheduleLow(),volume:Number($('schedule-volume').value),cutoff:$('schedule-cutoff-enabled').checked?$('schedule-cutoff').value:''});clearScheduleEdit();
});});
$('schedule-cutoff-enabled').addEventListener('change',()=>{$('schedule-cutoff').disabled=!$('schedule-cutoff-enabled').checked;if($('schedule-cutoff-enabled').checked&&!$('schedule-cutoff').value)$('schedule-cutoff').value=localDateTime(new Date($('schedule-start').value||Date.now()).getTime()+3600000);});
bind('schedule-cancel',()=>{clearScheduleEdit();$('schedule-start').focus();});
['reflection-text','early-reason'].forEach(id=>$(id).addEventListener('input',()=>{
  setText($('draft-status'),'Saving draft…'); clearTimeout(saveDelay); saveDelay=setTimeout(()=>saveDraft().catch(e=>error(e.message)),300);
}));
$('reflection-form').addEventListener('submit',event=>{event.preventDefault();run(async()=>{
  if(!$('reflection-text').value.trim()) { $('reflection-text').setAttribute('aria-invalid','true'); $('reflection-text').focus(); throw new Error('Write a reflection before saving.'); }
  $('reflection-text').removeAttribute('aria-invalid'); await saveDraft(); queued=true;
  try { await send('queue',draft()); } catch(e) { queued=false; throw e; }
});});
bind('later',async()=>{await saveDraft();await send('close');});
bind('skip-reflection',async()=>{clearTimeout(saveDelay);await saving.catch(()=>{});queued=true;try{await send('skip',{id:promptId});}catch(e){queued=false;throw e;}});
['reflection-text','early-reason'].forEach(id=>$(id).addEventListener('keydown',event=>{if(event.ctrlKey&&event.key==='Enter'){event.preventDefault();$('reflection-form').requestSubmit();}}));
['hours','minutes','seconds'].forEach(id=>$(id).addEventListener('keydown',event=>{if(event.key==='Enter'){event.preventDefault();if(state?.clock.status!=='Running')$('timer-editor').requestSubmit();}}));
run(()=>send('ready'));
