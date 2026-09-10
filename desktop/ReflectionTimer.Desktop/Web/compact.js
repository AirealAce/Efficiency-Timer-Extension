import {setText,formatClock,durationSeconds} from './ui.js';
const $=id=>document.getElementById(id),bridge=window.chrome?.webview,requests=new Map();
let sequence=0,state,dirty=false,tiny=false,revealed=false,lastRunning=false,lastDeadline;
function send(action,data={}){return new Promise((resolve,reject)=>{const requestId=String(++sequence);if(!bridge)return reject(new Error('Open the compact timer through Reflection Timer.'));const timeout=setTimeout(()=>{requests.delete(requestId);reject(new Error('The app did not respond.'));},35000);requests.set(requestId,{resolve,reject,timeout});bridge.postMessage({requestId,action,data});});}
function run(action){setText($('error'),'');Promise.resolve().then(action).catch(e=>setText($('error'),e.message));}
function bind(id,action){$(id).addEventListener('click',()=>{if($(id).getAttribute('aria-disabled')!=='true')run(action);});}
function announce(text){setText($('status'),'');setTimeout(()=>setText($('status'),text),50);}
function duration(){return durationSeconds(['hours','minutes','seconds'].map(id=>$(id).value.trim()));}
function fill(seconds){$('hours').value=Math.floor(seconds/3600);$('minutes').value=Math.floor(seconds/60)%60;$('seconds').value=seconds%60;}
function sharedDuration(parts){dirty=Array.isArray(parts);if(parts)['hours','minutes','seconds'].forEach((id,i)=>{if($(id).value!==parts[i])$(id).value=parts[i];});else if(state)fill(state.timer.durationSeconds);if(state?.clock.status!=='Running'){try{setText($('visual-clock'),formatClock(dirty?duration():state.clock.seconds));}catch{setText($('visual-clock'),'—');}}}
function resize(){if(state)send('compactSize',{width:Math.ceil(document.body.getBoundingClientRect().width),height:Math.ceil(document.body.getBoundingClientRect().height),tiny}).catch(e=>setText($('error'),e.message));}
function mode(value){tiny=value;document.body.dataset.tiny=String(value);$('shrink').setAttribute('aria-label',value?'Hide compact timer':'Shrink to time-only view');$('expand').setAttribute('aria-label',value?'Expand compact view':'Open main timer page');['shrink','expand'].forEach(id=>$(id).title=$(id).getAttribute('aria-label'));}
function focusDuration(){const id=['hours','minutes','seconds'].find(id=>Number($(id).value)>0)||'hours';$(id).focus();$(id).select();}
function expand(){revealed=true;mode(false);focusDuration();}
function snapshot(clock,speak=false){const text=`${clock.text} remaining. ${clock.status}.`;setText($('time-snapshot'),`Time checked: ${text}`);if(speak)announce(text);}
function render(next){const previous=state;state=next;document.documentElement.dataset.theme=String(state.theme??0);const running=state.clock.status==='Running';
  if(!previous||previous.clock.status!==state.clock.status)snapshot(state.clock);
  if(!previous||(!dirty&&state.timer.durationSeconds!==previous.timer.durationSeconds))fill(state.timer.durationSeconds);
  if(running!==lastRunning||lastDeadline!==state.timer.endTime){revealed=false;mode(running);}lastRunning=running;lastDeadline=state.timer.endTime;
  $('repeat').checked=state.timer.autoRestart;['hours','minutes','seconds'].forEach(id=>$(id).readOnly=running);
  const action=running?'Pause':state.clock.status==='Paused'&&!dirty?'Resume':'Start';$('toggle').setAttribute('aria-label',`${action} timer`);$('toggle').title=`${action} timer`;setText($('toggle').firstElementChild,running?'Ⅱ':'▶');
  $('end').setAttribute('aria-disabled',String(!running));$('reset').setAttribute('aria-disabled',String(!dirty&&state.clock.status==='Ready'));
  setText($('visual-clock'),formatClock(state.clock.seconds));
  if(dirty&&!running){try{setText($('visual-clock'),formatClock(duration()));}catch{setText($('visual-clock'),'—');}}
  document.body.style.setProperty('--tiny-width',`${Math.max(96,formatClock(state.timer.durationSeconds).length*15+16)}px`);
  if(tiny&&['hours','minutes','seconds','toggle','repeat','app','reset','end'].includes(document.activeElement.id))$('read-time').focus();
}
bridge?.addEventListener('message',event=>{const m=event.data;if(m.type==='reply'){const p=requests.get(m.requestId);if(!p)return;clearTimeout(p.timeout);requests.delete(m.requestId);m.error?p.reject(new Error(m.error)):p.resolve();}
  else if(m.type==='init'){render(m.state);if(m.state.durationDraft)sharedDuration(m.state.durationDraft);resize();}
  else if(m.type==='state')render(m.state);
  else if(m.type==='clock'){if(state?.clock.status==='Running'||!dirty)setText($('visual-clock'),formatClock(m.clock.seconds));}
  else if(m.type==='timeRead')snapshot(m.clock,true);
  else if(m.type==='announcement')announce(m.message);
  else if(m.type==='durationDraft')sharedDuration(m.parts);
  else if(m.type==='expandCompact')expand();
  else if(m.type==='shrinkCompact'){mode(true);$('read-time').focus();}
  else if(m.type==='measureCompact')resize();
});
['hours','minutes','seconds'].forEach(id=>$(id).addEventListener('input',()=>{const parts=['hours','minutes','seconds'].map(id=>$(id).value);sharedDuration(parts);run(()=>send('durationDraft',{parts}));$('reset').setAttribute('aria-disabled','false');}));
$('timer-editor').addEventListener('submit',event=>{event.preventDefault();run(async()=>{await send('toggle',{seconds:duration(),repeat:$('repeat').checked,lowTime:state.timer.enabled,threshold:state.timer.threshold});dirty=false;fill(state.timer.durationSeconds);});});
$('repeat').addEventListener('change',()=>run(()=>send('repeat',{enabled:$('repeat').checked})));
bind('read-time',()=>send('readTime'));bind('app',()=>send('main'));bind('end',()=>send('end'));
bind('reset',async()=>{await send('reset',{seconds:duration()});dirty=false;fill(state.timer.durationSeconds);});
bind('close',()=>send('close'));bind('shrink',()=>tiny?send('close'):mode(true));bind('expand',()=>tiny?expand():send('main'));
document.addEventListener('keydown',event=>{if(event.key==='Escape'&&revealed&&state.clock.status==='Running'){mode(true);$('read-time').focus();}});
window.addEventListener('blur',()=>{if(revealed&&state?.clock.status==='Running'){revealed=false;mode(true);}});
['hours','minutes','seconds'].forEach(id=>$(id).addEventListener('keydown',event=>{if(event.key==='Enter'){event.preventDefault();if(state?.clock.status!=='Running')$('timer-editor').requestSubmit();}}));
new ResizeObserver(resize).observe(document.body);
$('caption').addEventListener('pointerdown',event=>{if(event.button===0&&!event.target.closest('button'))run(()=>send('dragCompact'));});
$('read-time').addEventListener('pointerdown',event=>{if(event.button===0&&tiny)run(()=>send('dragCompact'));});
run(()=>send('ready'));
