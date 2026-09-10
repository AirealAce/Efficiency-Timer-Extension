// Keep the original five App tabs. Native HTML supplies the reading structure
// underneath the familiar layout; it must not add App controls to compact mode.
export function arrangeApp(view) {
  const $=id=>document.getElementById(id);
  if(view!=='main') return {select(){}};
  const nav=document.querySelector('nav'),main=$('main');nav.replaceChildren();nav.setAttribute('role','tablist');nav.setAttribute('aria-label','App views');
  const definitions=[['timer','Timer'],['schedules','Scheduling session times'],['outbox','Outbox'],['settings','Settings'],['diagnostics','Diagnostics']];
  const panels=new Map(),buttons=new Map(),scroll=new Map();let current='timer';
  for(const [id,label] of definitions){const button=document.createElement('button');button.type='button';button.id=`tab-${id}`;button.textContent=label;button.setAttribute('role','tab');button.setAttribute('aria-controls',`panel-${id}`);nav.append(button);buttons.set(id,button);
    const panel=document.createElement('div');panel.id=`panel-${id}`;panel.setAttribute('role','tabpanel');panel.setAttribute('aria-labelledby',button.id);panels.set(id,panel);
    if(id!=='settings')panel.append($(id));else panel.append($('appearance'),$('audio'),$('connection'));
    main.append(panel);button.addEventListener('click',()=>select(id));
    button.addEventListener('keydown',event=>{const offset=event.key==='ArrowRight'?1:event.key==='ArrowLeft'?-1:0;let next;
      if(offset)next=definitions[(definitions.findIndex(x=>x[0]===id)+offset+definitions.length)%definitions.length][0];
      else if(event.key==='Home')next=definitions[0][0];else if(event.key==='End')next=definitions.at(-1)[0];
      if(next){event.preventDefault();select(next);buttons.get(next).focus();}
    });
  }
  function select(id){if(!panels.has(id))return;scroll.set(current,main.scrollTop);for(const [key,panel] of panels){panel.hidden=key!==id;const button=buttons.get(key);button.setAttribute('aria-selected',String(key===id));button.tabIndex=key===id?0:-1;}current=id;main.scrollTop=scroll.get(id)||0;}
  document.querySelector('.page-header>.main-only').classList.add('sr-only');
  document.querySelector('.eyebrow').classList.add('sr-only');
  const timer=$('timer');$('timer-heading').classList.add('sr-only');$('timer-state').after($('time-snapshot'));
  const notice=document.createElement('p');notice.className='timer-notice';notice.textContent='Desktop timer active · Use only one timer app per session';timer.prepend(notice);
  $('visual-clock').after($('timer-state'));$('time-snapshot').classList.add('sr-only');
  timer.querySelector('.hint').classList.add('sr-only');$('read-time').classList.add('sr-only');
  const editor=$('timer-editor'),actions=editor.querySelector('.actions'),options=editor.querySelector('.options');editor.querySelector('legend').classList.add('sr-only');$('duration-help').classList.add('sr-only');
  editor.after(options);$('repeat').closest('label').after($('cutoff-form'));$('end').hidden=true;$('check-in').classList.add('sr-only');
  const quick=document.createElement('form');quick.id='quick-schedule-form';quick.className='option-row';quick.innerHTML='<label>Start timer at<input id="quick-start" type="datetime-local" required></label><button type="submit">Schedule session</button>';
  const later=new Date(Date.now()+3600000);quick.querySelector('input').value=new Date(later-later.getTimezoneOffset()*60000).toISOString().slice(0,16);
  editor.after(quick);const help=document.createElement('p');help.textContent='Uses the duration and options on this page. View or cancel it in Scheduling session times.';quick.after(help);
  options.after($('volume-form'));
  const pending=$('pending');pending.querySelector('h2').classList.add('sr-only');pending.querySelectorAll('p')[1].hidden=true;$('pending-list').hidden=true;
  const buttonsRow=document.createElement('div');buttonsRow.className='actions';buttonsRow.append($('practice'));
  const pendingButton=document.createElement('button');pendingButton.id='show-pending';pendingButton.type='button';pendingButton.textContent='Pending reflections';buttonsRow.append(pendingButton);
  const mark=document.createElement('button');mark.id='timer-mark-issue';mark.type='button';mark.textContent='Mark issue';buttonsRow.append(mark);pending.prepend(buttonsRow);
  timer.append(pending,$('open-compact'));
  const quit=document.createElement('button');quit.id='quit';quit.type='button';quit.textContent='Quit desktop app';
  const hint=document.createElement('p');hint.textContent='Closing this window keeps the timer running in the tray. Right-click its tray icon to quit.';timer.append(hint,quit);
  const guide=document.createElement('details'),summary=document.createElement('summary');summary.textContent='Accessibility preview review guide';guide.append(summary,$('review'));panels.get('diagnostics').append(guide);
  ['schedule-heading','outbox-heading','diagnostics-heading'].forEach(id=>$(id).classList.add('sr-only'));
  $('appearance-heading').textContent='App theme and display';$('audio-heading').textContent='Audio';
  $('practice').textContent='Test reflection prompt';$('open-compact').textContent='Show / hide floating timer';
  $('reset').textContent='Reset';
  select('timer');return {select};
}
