import {setText} from './ui.js';
import {mountAudio} from './audio.js';
import {mountLowTime} from './low-time.js';

export function settingsUI({send, run, bind, view, announce}) {
  const $ = id => document.getElementById(id);
  let settings;
  const audio=mountAudio({send,run});
  const lowTime=mountLowTime({send,run});
  const dirty = new Set();
  const dirtyFields=new Map();
  for (const id of ['appearance-form','volume-form','connection-form','cutoff-form']) {
    for(const name of ['input','change'])$(id).addEventListener(name,event=>{dirty.add(id);if(!dirtyFields.has(id))dirtyFields.set(id,new Set());dirtyFields.get(id).add(event.target.id);});
  }
  function cleanField(form,field){dirtyFields.get(form)?.delete(field);if(!dirtyFields.get(form)?.size)dirty.delete(form);}
  function connection() {
    return {sheetUrl:$('sheet-url').value,webAppUrl:$('receiver-url').value,token:$('connection-token').value,
      sheetMode:$('sheet-mode').value,sheetName:$('sheet-name').value,enabled:$('connection-enabled').checked};
  }
  function populateConnection(c) {
    $('sheet-url').value=c.sheetUrl; $('receiver-url').value=c.webAppUrl;
    $('sheet-mode').value=c.sheetMode; $('sheet-name').value=c.sheetName;
  }
  function populate(form) {
    if(!settings || dirty.has(form)) return;
    if(form==='appearance-form') {
      ['theme','placement','popup','overlap'].forEach(id=>$(id).value=settings[id]);
      $('show-compact').checked=settings.showFloatingTimer; $('logging').checked=settings.loggingEnabled;
      $('default-threshold').value=settings.threshold;
    } else if(form==='volume-form') $('app-volume').value=settings.volume;
    else if(form==='connection-form') {
      populateConnection(settings); $('connection-token').value=''; $('connection-enabled').checked=settings.connected;
      setText($('token-help'),settings.hasToken?'Leave blank to keep the saved token. A token is saved.':'Leave blank to keep the saved token. No saved token yet.');
      $('restore-setup').setAttribute('aria-disabled',String(!settings.hasDraft));
    }
  }
  function submit(form, action, data) {
    $(form).addEventListener('submit',event=>{event.preventDefault();run(async()=>{
      await send(action,data()); dirty.delete(form);dirtyFields.delete(form);populate(form);
    });});
  }
  const appearance=()=>({theme:Number($('theme').value),placement:Number($('placement').value),popup:Number($('popup').value),
    overlap:Number($('overlap').value),threshold:Number($('default-threshold').value),logging:$('logging').checked,showCompact:$('show-compact').checked,quiet:true});
  submit('appearance-form','saveAppearance',appearance);
  submit('volume-form','volume',()=>({volume:Number($('app-volume').value)}));
  submit('connection-form','connectionSave',connection);
  async function saveSettings(){
    if(!$('appearance-form').reportValidity())return;
    await audio.flush();await send('saveAppearance',appearance());dirty.delete('appearance-form');dirtyFields.delete('appearance-form');
    if(dirty.has('volume-form')){await send('volume',{volume:Number($('app-volume').value),quiet:true});dirty.delete('volume-form');dirtyFields.delete('volume-form');}
    if(dirty.has('connection-form')){await send('connectionSave',connection());dirty.delete('connection-form');dirtyFields.delete('connection-form');populate('connection-form');}
    announce('Settings saved.');
  }
  bind('save-settings',saveSettings);
  if(view==='main')document.addEventListener('keydown',event=>{if(event.ctrlKey&&event.key==='Enter'&&!document.querySelector('dialog[open]')){event.preventDefault();run(saveSettings);}});
  const cutoffData=()=>({cutoff:$('cutoff-enabled').checked?$('cutoff').value:'',quiet:true});
  submit('cutoff-form','setCutoff',cutoffData);
  $('cutoff-enabled').addEventListener('change',()=>run(async()=>{$('cutoff').disabled=!$('cutoff-enabled').checked;if($('cutoff-enabled').checked&&(!$('cutoff').value||new Date($('cutoff').value).getTime()<=Date.now()))$('cutoff').value=localDateTime(Date.now()+3600000);await send('setCutoff',cutoffData());dirty.delete('cutoff-form');dirtyFields.delete('cutoff-form');}));
  $('cutoff').addEventListener('change',()=>run(async()=>{if($('cutoff-enabled').checked){await send('setCutoff',cutoffData());dirty.delete('cutoff-form');dirtyFields.delete('cutoff-form');}}));
  ['theme','placement','popup','overlap'].forEach(id=>$(id).addEventListener('change',()=>run(async()=>{await send('displayOption',{option:id,value:Number($(id).value),quiet:true});cleanField('appearance-form',id);})));
  $('show-compact').addEventListener('change',()=>run(async()=>{await send('displayOption',{option:'showCompact',value:$('show-compact').checked?1:0,quiet:true});cleanField('appearance-form','show-compact');}));
  $('app-volume').addEventListener('change',()=>run(async()=>{await send('volume',{volume:Number($('app-volume').value),quiet:true});cleanField('volume-form','app-volume');}));
  bind('pause-delivery',async()=>{await send('connectionPause');$('connection-enabled').checked=false;});
  bind('save-script',()=>send('setupScript',connection()));
  bind('new-token',()=>send('setupNewToken',connection()));bind('restore-setup',()=>send('setupRestore'));
  submit('import-form','setupImport',()=>({code:$('setup-import').value}));
  bind('export-setup',()=>send('setupExport'));
  bind('hide-setup',()=>{$('setup-export').value='';$('setup-export-group').hidden=true;$('export-setup').focus();});
  bind('mark-issue',()=>send('markIssue')); bind('show-diagnostics',()=>send('diagnostics')); bind('export-diagnostics',()=>send('exportDiagnostics'));
  bind('send-pending',()=>send('sendPending'));
  bind('import-schedules',()=>send('importSchedules'));bind('open-sheet',()=>send('openSheet'));
  bind('clear-diagnostics',()=>{$('clear-log-dialog').showModal();$('clear-log-title').focus();});
  bind('clear-log-confirm',async()=>{await send('clearDiagnostics',{confirmed:true});$('clear-log-dialog').close();$('clear-diagnostics').focus();});
  return {
    scheduleLow:()=>lowTime.scheduleData(),timerLow:()=>lowTime.timerData(),resetScheduleLow:()=>lowTime.resetSchedule(),
    load() { if(view==='main') run(()=>send('settingsLoad')); },
    state(state) {
      lowTime.state(state);
      document.documentElement.dataset.theme=String(state.theme??0);
      if(!dirty.has('appearance-form') && state.showFloatingTimer!==undefined) $('show-compact').checked=state.showFloatingTimer;
      if(!dirty.has('volume-form') && state.appVolume!==undefined) $('app-volume').value=state.appVolume;
      setText($('delivery-status'),state.connected?'Sheets delivery is enabled. Connected pending entries send automatically.':'Sheets delivery is off. Pending entries stay saved.');
      setText($('reflection-delivery'),state.connected?'Saving queues this reflection for automatic Sheets delivery. Practice reflections use the receiver’s test tab.':'Saving keeps this reflection in the preview Outbox. Sheets delivery is off.');
      if(!dirty.has('cutoff-form')) {$('cutoff').value=localDateTime(state.timer.autoRestartUntil);$('cutoff-enabled').checked=!!state.timer.autoRestartUntil;$('cutoff').disabled=!state.timer.autoRestartUntil;}
    },
    message(message) {
      if(view!=='main') return;
      if(message.type==='settings') { settings=message.settings; ['appearance-form','volume-form','connection-form'].forEach(populate);audio.render(settings);lowTime.settings(settings); }
      else if(message.type==='scheduledLow')lowTime.scheduled(message.low);
      else if(message.type==='setupImported') {
        populateConnection(message.connection); $('connection-token').value=message.connection.apiToken;
        $('connection-enabled').checked=false; dirty.add('connection-form'); $('setup-import').value='';
        $('sheet-url').focus();
      } else if(message.type==='setupCode') {
        $('setup-export-group').hidden=false; $('setup-export').value=message.code; $('setup-export').focus();
      } else if(message.type==='diagnostics') {
        setText($('diagnostic-report'),JSON.stringify(message.report,null,2)); $('diagnostic-result').hidden=false; $('diagnostic-title').focus();
      }
    }
  };
}
export function localDateTime(milliseconds) {
  if(!milliseconds) return '';
  const date=new Date(milliseconds);return new Date(date-date.getTimezoneOffset()*60000).toISOString().slice(0,16);
}
