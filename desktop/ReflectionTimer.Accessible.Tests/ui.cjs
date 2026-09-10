// Run with Playwright on NODE_PATH. The browser receives synthetic state only.
const { chromium } = require('playwright');
const fs = require('node:fs/promises');
const path = require('node:path');
const assert = require('node:assert/strict');
const web = path.resolve(__dirname, '../ReflectionTimer.Accessible/Web');
(async () => {
  const browser = await chromium.launch({channel:'msedge',headless:true});
  let count=0;
  const check = (condition, message) => { assert.ok(condition,message); count++; console.log('PASS '+message); };
  try {
    const context=await browser.newContext({viewport:{width:940,height:780}});
    const failures=[];context.on('page',p=>p.on('pageerror',e=>failures.push(e.message)));
    let page = await context.newPage();
    await page.context().route('**/*', async route=>{
      const name=new URL(route.request().url()).pathname.slice(1);
      if (!['index.html','app.js','app.css','ui.js','settings.js','setup.js','audio.js','low-time.js','layout.js','compact.html','compact.js','compact.css'].includes(name)) return route.abort();
      await route.fulfill({body:await fs.readFile(path.join(web,name)),contentType:name.endsWith('.js')?'text/javascript':name.endsWith('.css')?'text/css':'text/html'});
    });
    await page.context().addInitScript(() => {
      window.previewMessages=[];
      const listeners=[];
      window.previewDispatch = data => listeners.forEach(fn=>fn({data}));
      window.chrome ||= {};
      window.chrome.webview={addEventListener:(name,fn)=>listeners.push(fn),postMessage:message=>{
        window.previewMessages.push(message);
        queueMicrotask(()=>window.previewDispatch({type:'reply',requestId:message.requestId}));
      }};
    });
    const initial={clock:{seconds:900,text:'15 minutes',status:'Ready'},timer:{durationSeconds:900,autoRestart:false,enabled:true,threshold:15},prompts:[],
      schedules:[{id:'s1',start:'09/17/2026 10:00 AM',editStart:'2026-09-17T10:00',durationSeconds:900,volume:50,duration:'15 minutes',repeat:'Off',lowTime:'On',status:'Scheduled'}],
      outbox:[{id:'o1',saved:'09/10/2026 10:00 AM',destination:'Local preview only',status:'Pending',attempts:0,message:'A sample reflection.',duration:'15 minutes'}]};
    await page.goto('https://reflection-timer.invalid/index.html?view=main');
    await page.waitForFunction(()=>window.previewMessages.some(m=>m.action==='ready'));
    await page.evaluate(state=>window.previewDispatch({type:'init',state}),initial);
    async function capture(name,target=page){if(process.env.REFLECTION_PREVIEW_SCREENSHOTS){await fs.mkdir(process.env.REFLECTION_PREVIEW_SCREENSHOTS,{recursive:true});await target.locator('body').screenshot({path:path.join(process.env.REFLECTION_PREVIEW_SCREENSHOTS,name+'.png')});}}
    await capture('App-view');
    check(await page.locator('#quick-schedule-form').evaluate(row=>{const [label,input,button]=[row.querySelector('label'),row.querySelector('input'),row.querySelector('button')].map(e=>e.getBoundingClientRect());return label.right<=input.left&&input.right<=button.left&&Math.abs((label.top+label.bottom-input.top-input.bottom)/2)<2&&Math.abs((button.top+button.bottom-input.top-input.bottom)/2)<2;}),'Start timer at keeps its label, input, and button on one centered horizontal row');
    for(const name of ['Timer','Scheduler','Outbox','Settings','Diagnostics']){await page.getByRole('tab',{name,exact:true}).click();check(await page.getByRole('button',{name:'Save settings',exact:true}).isVisible()===(name==='Settings'),'Save settings visibility matches original on '+name);}
    await page.getByRole('tab',{name:'Timer',exact:true}).click();
    check(await page.getByRole('tab').allTextContents().then(t=>JSON.stringify(t)===JSON.stringify(['Timer','Scheduler','Outbox','Settings','Diagnostics'])),'App exposes the five tabs with the renamed Scheduler');
    for(const width of [940,739,420,336]){
      await page.setViewportSize({width,height:642});
      for(const name of ['Timer','Scheduler','Outbox','Settings','Diagnostics']){
        await page.getByRole('tab',{name,exact:true}).click();
        check(await page.locator('nav[role=tablist]').evaluate(nav=>{
          const bounds=nav.getBoundingClientRect();
          return nav.scrollWidth<=nav.clientWidth&&[...nav.children].every(button=>{
            const box=button.getBoundingClientRect(),range=document.createRange();range.selectNodeContents(button);
            const text=range.getBoundingClientRect();
            return box.left>=0&&box.right<=innerWidth&&box.top>=bounds.top&&box.bottom<=bounds.bottom&&text.left>box.left&&text.right<box.right&&text.top>=box.top&&text.bottom<=box.bottom;
          });
        }),`All tab captions fit at ${width}px with ${name} selected`);
      }
      await capture('Diagnostics-'+width);
    }
    await page.getByRole('tab',{name:'Timer',exact:true}).focus();await page.keyboard.press('End');
    check(await page.getByRole('tab',{name:'Diagnostics',exact:true}).evaluate(e=>e===document.activeElement&&e.getAttribute('aria-selected')==='true'),'End reaches and selects Diagnostics on the wrapped tab row');
    await page.keyboard.press('Home');
    check(await page.getByRole('tab',{name:'Timer',exact:true}).evaluate(e=>e===document.activeElement&&e.getAttribute('aria-selected')==='true'),'Home returns to Timer on the wrapped tab row');
    for(const [name,list] of [['Outbox','#outbox .table-scroll'],['Diagnostics','#diagnostic-recent']]){
      await page.getByRole('tab',{name,exact:true}).click();
      await page.setViewportSize({width:940,height:900});const before=(await page.locator(list).boundingBox()).height;
      await page.setViewportSize({width:940,height:1200});const after=(await page.locator(list).boundingBox()).height;
      check(after-before>=290,name+' list uses spare height when the window grows');
      check(await page.locator('.messages').evaluate(e=>e.getBoundingClientRect().height===0),name+' does not reserve an empty footer');
      await capture(name+'-tall');
    }
    await page.getByRole('tab',{name:'Settings',exact:true}).click();
    check(await page.locator('#save-settings').evaluate(e=>{const button=e.getBoundingClientRect(),main=document.querySelector('main').getBoundingClientRect();return button.top>=main.bottom&&button.bottom<=innerHeight;}),'Settings keeps Save settings below the scrolling content');
    await page.setViewportSize({width:940,height:780});
    await page.getByRole('tab',{name:'Timer',exact:true}).click();
    check(await page.getByRole('heading',{name:'Reflection Timer',exact:true}).count()===1,'Document exposes its main heading');
    check(await page.getByRole('spinbutton',{name:'Minutes',exact:true}).inputValue()==='15','Duration has a native label');
    await page.getByRole('spinbutton',{name:'Minutes',exact:true}).fill('12');
    await page.evaluate(()=>{
      window.clockMutations=[];window.focusEvents=[];
      document.addEventListener('focusin',e=>window.focusEvents.push(e.target.id));
      new MutationObserver(records=>window.clockMutations.push(...records.map(r=>r.target.parentElement?.id||r.target.id))).observe(document.body,{subtree:true,childList:true,characterData:true});
      for(let seconds=899;seconds>779;seconds--) window.previewDispatch({type:'clock',clock:{seconds,text:'Tick',status:'Running'}});
    });
    check(await page.locator('#minutes').evaluate(e=>document.activeElement===e)&&await page.locator('#minutes').inputValue()==='12','120 countdown ticks preserve input focus and draft');
    check(await page.locator('#visual-clock').textContent()==='12:00','Idle visual preview retains the edited duration during background clock messages');
    check(await page.locator('#status').textContent()===''&&await page.locator('#time-snapshot').textContent()==='Time checked: 15 minutes remaining. Ready.','Ticks do not update live speech or accessible time snapshot');
    check(await page.locator('#visual-clock').getAttribute('aria-hidden')==='true','Animated countdown is separate from the readable time snapshot');
    check((await page.evaluate(()=>window.focusEvents)).length===0,'Ticks generate no DOM focus changes');
    await page.getByRole('button',{name:'Read remaining time',exact:true}).focus();await page.keyboard.press('Enter');
    check(await page.evaluate(()=>window.previewMessages.at(-1).action)==='readTime','Remaining time is requested explicitly');
    await page.evaluate(()=>window.previewDispatch({type:'timeRead',clock:{seconds:780,text:'13 minutes',status:'Running'}}));
    await page.waitForFunction(()=>document.getElementById('status').textContent.includes('13 minutes'));
    check((await page.locator('#status').textContent())==='13 minutes remaining. Running.','Time request provides human-readable status');
    await page.getByRole('tab',{name:'Scheduler',exact:true}).click();
    check(await page.getByRole('table',{name:'Scheduled sessions',exact:true}).count()===1 && await page.getByRole('columnheader',{name:'Duration',exact:true}).count()===1,'Schedule has native table and header semantics');
    await page.getByRole('tab',{name:'Outbox',exact:true}).click();
    const success=page.getByRole('radio',{name:'Select entry saved 09/10/2026 10:00 AM'});
    await success.focus();
    await page.evaluate(()=>{ window.savedRow=document.querySelector('#outbox-rows tr');window.savedCell=window.savedRow.cells[2];window.savedButton=document.activeElement; });
    const changed=structuredClone(initial);changed.outbox[0].status='Simulated success';
    await page.evaluate(state=>window.previewDispatch({type:'state',state}),changed);
    check(await page.evaluate(()=>window.savedRow===document.querySelector('#outbox-rows tr')&&window.savedCell===window.savedRow.cells[2]&&window.savedButton===document.activeElement),'Status update preserves row, cell, and focused action identity');
    check((await page.locator('#outbox-rows').textContent()).includes('Simulated success'),'Changed table cell is updated');
    check(await page.locator('#outbox-detail').textContent().then(text=>text.includes('A sample reflection.')),'Selected Outbox text is readable on the page');
    check(await page.locator('#outbox .actions button').allTextContents().then(labels=>JSON.stringify(labels)===JSON.stringify(['Send pending now','Retry selected…','Already in Sheet','Open Google Sheet'])),'Outbox actions match the original shared button row');
    check(await page.locator('#outbox-rows button').count()===0&&await page.locator('#outbox thead th').count()===4,'Outbox preserves its original columns without per-row action buttons');
    const settings={sheetUrl:'',webAppUrl:'',sheetMode:'date',sheetName:'Reflections',hasToken:false,connected:false,volume:50,threshold:15,
      showFloatingTimer:true,placement:4,popup:4,theme:0,overlap:2,loggingEnabled:true,
      tracks:[{id:0,name:'Default'},{id:3,name:'Level up'},{id:9,name:'None'}],
      sounds:[0,1,2,3].map(kind=>({kind,track:0,behavior:0,volume:100,fadeOutEnabled:false,fadeOutAfterSeconds:10,custom:false,defaultName:'Bundled default'}))};
    await page.getByRole('tab',{name:'Settings',exact:true}).click();
    await page.evaluate(settings=>window.previewDispatch({type:'settings',settings}),settings);
    check(await page.getByRole('combobox',{name:'Compact timer position',exact:true}).inputValue()==='4'&&await page.getByRole('checkbox',{name:'Show compact floating timer (always on top)',exact:true}).isChecked(),'Display controls expose the intended defaults');
    check(await page.locator('#audio fieldset legend').allTextContents().then(names=>JSON.stringify(names)===JSON.stringify(['Success messages','Failure messages','Low on time audio','Session end · time limit reached'])),'All four original audio event sections are present together');
    await page.evaluate(()=>{window.savedAudioOption=document.querySelector('#sound-track-0 option');});
    await page.evaluate(settings=>window.previewDispatch({type:'settings',settings}),settings);
    check(await page.evaluate(()=>window.savedAudioOption===document.querySelector('#sound-track-0 option')),'Background settings updates preserve audio option identity for the reader');
    check(await page.getByRole('checkbox',{name:'Start in the tray when I sign in to Windows',exact:true}).count()===1&&await page.getByRole('checkbox',{name:'Record local diagnostic events',exact:true}).count()===1,'Settings includes startup and diagnostic preferences');
    await page.getByRole('button',{name:'Guided setup / another PC',exact:true}).click();
    check(await page.getByRole('dialog',{name:'Your timer. Your spreadsheet.',exact:true}).isVisible()&&await page.getByRole('tab',{name:'1 · Your sheet',exact:true}).isVisible(),'Guided setup opens its own labeled three-step dialog');
    await page.getByRole('textbox',{name:'Google Sheets URL',exact:true}).fill('https://docs.google.com/spreadsheets/d/setup-draft/edit');
    await page.evaluate(()=>window.previewDispatch({type:'focusTimer'}));
    check(await page.getByRole('textbox',{name:'Google Sheets URL',exact:true}).evaluate(e=>document.activeElement===e),'Opening App does not move focus behind a setup dialog');
    await page.getByRole('button',{name:'Next',exact:true}).click();
    check(await page.getByRole('button',{name:'Save private receiver script',exact:true}).isVisible(),'Guided setup exposes receiver generation on its Google setup step');
    await page.getByRole('button',{name:'Next',exact:true}).click();
    check(await page.getByRole('button',{name:'Save & test connection',exact:true}).isVisible(),'Guided setup exposes connection verification on its Connect step');
    await page.getByRole('button',{name:'Close setup',exact:true}).click();
    check(await page.locator('#sheet-url').inputValue().then(value=>value.includes('setup-draft'))&&await page.locator('#sheet-url').evaluate(e=>e.form.id==='connection-form')&&await page.locator('#setup-import').evaluate(e=>e.form.id==='import-form'),'Closing setup preserves drafts and restores each form association');
    await page.getByRole('textbox',{name:'Google Sheets URL',exact:true}).fill('https://docs.google.com/spreadsheets/d/my-unsaved-draft/edit');
    const eventEditor=page.getByRole('group',{name:'Session end · time limit reached',exact:true});
    await eventEditor.getByRole('checkbox',{name:'SessionEnd fade out after',exact:true}).check();
    await eventEditor.getByRole('spinbutton',{name:'SessionEnd fade out after seconds',exact:true}).fill('37');
    await page.evaluate(settings=>window.previewDispatch({type:'settings',settings}),settings);
    check((await page.getByRole('textbox',{name:'Google Sheets URL',exact:true}).inputValue()).includes('my-unsaved-draft')&&await eventEditor.getByRole('spinbutton',{name:'SessionEnd fade out after seconds',exact:true}).inputValue()==='37','Background settings responses preserve unsaved connection and audio edits');
    await eventEditor.getByRole('spinbutton',{name:'SessionEnd fade out after seconds',exact:true}).press('Tab');
    await page.waitForFunction(()=>window.previewMessages.some(m=>m.action==='saveSound'));
    check(await page.evaluate(()=>window.previewMessages.findLast(m=>m.action==='saveSound').data.fadeSeconds)===37,'Audio form sends the entered fade duration');
    await page.getByRole('spinbutton',{name:'Default low-time threshold in seconds',exact:true}).fill('18');
    await page.keyboard.press('Control+Enter');
    await page.waitForFunction(()=>window.previewMessages.some(m=>m.action==='saveAppearance'));
    check(await page.evaluate(()=>window.previewMessages.findLast(m=>m.action==='saveAppearance').data.threshold)===18,'Original Save settings keyboard shortcut saves entered preferences');
    check(await page.getByRole('slider',{name:'Settings app sound volume',exact:true}).isVisible(),'Audio includes the original master App sound slider in Settings');
    await page.getByRole('slider',{name:'Settings app sound volume',exact:true}).press('Home');
    await page.getByRole('slider',{name:'Settings app sound volume',exact:true}).press('ArrowRight');
    await page.waitForFunction(()=>window.previewMessages.some(m=>m.action==='volume'&&m.data.volume===1));
    check(await page.locator('#app-volume').inputValue()==='1'&&await page.locator('#settings-volume').inputValue()==='1','Settings and Timer App sound sliders share the same value');
    for(const [name,key] of [['Success messages','Success'],['Failure messages','Failure'],['Low on time audio','LowTime'],['Session end · time limit reached','SessionEnd']]){
      const group=page.getByRole('group',{name,exact:true});
      check(await group.getByRole('combobox',{name:key+' playback behavior',exact:true}).locator('option').allTextContents().then(labels=>JSON.stringify(labels)===JSON.stringify(['Disruptive','Assertive','Polite']))&&await group.getByRole('slider',{name:key+' audio volume',exact:true}).count()===1&&await group.getByRole('button',{name:'Preview audio',exact:true}).count()===1&&await group.getByRole('button',{name:'Choose MP3…',exact:true}).count()===1,'Complete original audio controls for '+name);
    }
    const successAudio=page.getByRole('group',{name:'Success messages',exact:true});
    await successAudio.getByRole('slider',{name:'Success audio volume',exact:true}).press('Home');
    await successAudio.getByRole('slider',{name:'Success audio volume',exact:true}).press('ArrowRight');
    await successAudio.getByRole('combobox',{name:'Success playback behavior',exact:true}).selectOption('2');
    await successAudio.getByRole('checkbox',{name:'Success fade out after',exact:true}).check();
    check(await successAudio.getByRole('spinbutton',{name:'Success fade out after seconds',exact:true}).isEnabled(),'Fade duration is enabled only when its checkbox is selected');
    await page.waitForFunction(()=>window.previewMessages.some(m=>m.action==='saveSound'&&m.data.kind===1&&m.data.behavior===2&&m.data.volume===1&&m.data.fade));
    check(true,'Playback, per-event volume, and fade choices autosave together to the correct event');
    await page.getByRole('button',{name:'Stop all app audio',exact:true}).click();
    check(await page.evaluate(()=>window.previewMessages.at(-1).action)==='stopSound','Shared Stop all app audio button reaches the audio backend');
    await page.getByRole('tab',{name:'Timer',exact:true}).click();
    const savesBefore=await page.evaluate(()=>window.previewMessages.filter(m=>m.action==='saveAppearance').length);
    await page.keyboard.press('Control+Enter');
    check(await page.evaluate(()=>window.previewMessages.filter(m=>m.action==='saveAppearance').length)===savesBefore,'Save settings shortcut is inactive outside Settings');
    await page.locator('#timer-low-inherit').uncheck();
    await page.locator('#threshold').fill('27');
    await page.evaluate(state=>window.previewDispatch({type:'state',state}),initial);
    check(await page.locator('#threshold').inputValue()==='27','Background timer state preserves an unfinished low-time threshold');
    await page.locator('#threshold').press('Tab');
    await page.locator('#timer-low-track').selectOption('3');
    await page.waitForFunction(()=>window.previewMessages.some(m=>m.action==='lowTime'&&m.data.track===3));
    check(await page.evaluate(()=>{const options=window.previewMessages.findLast(m=>m.action==='lowTime').data;return !options.inherit&&options.threshold===27&&options.track===3;}),'Session low-time changes retain the explicit threshold and sound');
    await page.getByRole('tab',{name:'Scheduler',exact:true}).click();
    check(await page.locator('#schedules thead th').allTextContents().then(labels=>JSON.stringify(labels)===JSON.stringify(['Start time','Duration','Auto-start','Auto-start cutoff','Sound','Low on time','Status'])),'Scheduling columns match the original');
    check(await page.locator('#schedules>.actions button').allTextContents().then(labels=>JSON.stringify(labels)===JSON.stringify(['Edit selected','Remove selected','Import extension schedules…']))&&await page.locator('#schedule-rows button').count()===0,'Scheduling actions are below the table and act on the selected entry');
    await page.getByRole('button',{name:'Edit selected',exact:true}).click();
    check(await page.locator('#schedule-start').evaluate(e=>document.activeElement===e)&&await page.locator('#schedule-minutes').inputValue()==='15','Schedule editing moves focus to a populated labeled form');
    await page.locator('#schedule-hours').fill('1');await page.locator('#schedule-seconds').fill('9');
    await page.getByRole('button',{name:'Save changes',exact:true}).click();
    check(await page.evaluate(()=>window.previewMessages.findLast(m=>m.action==='schedule').data.id)==='s1','Editing a schedule retains its stable ID');
    check(await page.evaluate(()=>window.previewMessages.findLast(m=>m.action==='schedule').data.seconds)===4509,'Schedule preserves separate hours, minutes, and seconds');
    const connected=structuredClone(initial);connected.connected=true;connected.outbox[0].localOnly=false;connected.outbox[0].status='NeedsReview';connected.outbox[0].error='write_uncertain';
    await page.evaluate(state=>window.previewDispatch({type:'state',state}),connected);
    await page.getByRole('tab',{name:'Outbox',exact:true}).click();
    await page.getByRole('button',{name:'Retry selected…',exact:true}).click();
    check(await page.getByRole('dialog',{name:'Review delivery',exact:true}).isVisible()&&!await page.evaluate(()=>window.previewMessages.some(m=>m.action==='retry')),'An uncertain write opens review before sending a retry');
    await page.keyboard.press('Escape');
    check(await page.getByRole('button',{name:'Retry selected…',exact:true}).evaluate(e=>document.activeElement===e),'Cancelling a delivery review returns focus');
    await page.getByRole('tab',{name:'Diagnostics',exact:true}).click();
    await page.evaluate(()=>window.previewDispatch({type:'diagnostics',report:{privacy:'Synthetic diagnostic test',events:[]}}));
    check(await page.locator('#diagnostic-summary').textContent().then(text=>text.includes('/1200 events'))&&await page.getByRole('button',{name:'Refresh',exact:true}).isVisible(),'Diagnostics restores its readable event summary, history, and Refresh action');
    await capture('Diagnostics');
    for(const [tab,name] of [['Scheduler','Scheduler'],['Outbox','Outbox'],['Settings','Settings']]){await page.getByRole('tab',{name:tab,exact:true}).click();await page.locator('main').evaluate(e=>e.scrollTop=0);await capture(name);}
    if(process.env.REFLECTION_PREVIEW_SCREENSHOTS){for(const id of ['sound-form-1','sound-form-2','sound-form-3','sound-form-0','startup']){await page.locator('#'+id).screenshot({path:path.join(process.env.REFLECTION_PREVIEW_SCREENSHOTS,id+'.png')});}}
    await page.setViewportSize({width:420,height:750});
    check(await page.evaluate(()=>document.documentElement.scrollWidth<=window.innerWidth),'Narrow view reflows without horizontal page overflow');
    await page.goto('https://reflection-timer.invalid/compact.html');
    await page.waitForFunction(()=>window.previewMessages.some(m=>m.action==='ready'));
    await page.evaluate(state=>window.previewDispatch({type:'init',state}),initial);
    check(await page.getByRole('spinbutton').count()===3&&await page.getByRole('checkbox').count()===1&&await page.getByRole('button').count()===8,'Compact exposes only the original fields, Auto-start, App, transport, and caption actions');
    await capture('Compact-view');
    const mode=page.getByRole('button',{name:'Shrink to time-only view',exact:true});await mode.click();
    check(await page.getByRole('spinbutton',{name:'Minutes',exact:true}).isHidden(),'Time-only and compact controls cannot appear together');
    await page.getByRole('button',{name:'Expand compact view',exact:true}).focus();await page.keyboard.press('Enter');
    check(await page.getByRole('spinbutton',{name:'Minutes',exact:true}).isVisible(),'Original compact controls can be restored with the keyboard');
    const running=structuredClone(initial);running.clock.status='Running';running.timer.endTime=123456789;
    await page.evaluate(state=>window.previewDispatch({type:'state',state}),running);
    check(await page.getByRole('spinbutton',{name:'Minutes',exact:true}).isHidden()&&await page.locator('body').evaluate(e=>e.getBoundingClientRect().width<=100),'Starting automatically restores the original small time-only view');
    await page.mouse.move(400,700);await page.locator('#read-time').focus();
    await capture('Time-only-view');
    const quietBefore=await page.locator('#time-snapshot').textContent();
    await page.evaluate(()=>{for(let seconds=899;seconds>779;seconds--)window.previewDispatch({type:'clock',clock:{seconds}});});
    check(await page.locator('#read-time').evaluate(e=>e===document.activeElement)&&await page.locator('#time-snapshot').textContent()===quietBefore&&await page.locator('#read-time').getAttribute('aria-label')==='Read remaining time','Focused time-only clock keeps a stable accessible name and quiet reading snapshot during ticks');
    const compactWindow=page,appWindow=await page.context().newPage();
    await appWindow.goto('https://reflection-timer.invalid/index.html?view=main');await appWindow.waitForFunction(()=>window.previewMessages.some(m=>m.action==='ready'));await appWindow.evaluate(state=>window.previewDispatch({type:'init',state}),initial);
    page=await page.context().newPage();
    await page.setViewportSize({width:544,height:401});
    await page.goto('https://reflection-timer.invalid/index.html?view=reflection');
    await page.waitForFunction(()=>window.previewMessages.some(m=>m.action==='ready'));
    const reflection=structuredClone(initial);reflection.prompts=[{id:'p1',isCheckIn:false,endedEarly:false,draft:'',earlyEndReason:'',actual:'2 minutes',allotted:'15 minutes',completed:'Today'}];
    await page.evaluate(state=>window.previewDispatch({type:'init',state,promptId:'p1'}),reflection);
    await page.getByRole('textbox',{name:'Your reflection',exact:true}).fill('Unsaved final keystroke');
    await capture('Session-end-view');
    check(await page.getByRole('button',{name:'Save & send',exact:true}).evaluate(e=>e.getBoundingClientRect().bottom<=innerHeight),'Session-end actions fit the original window size');
    await compactWindow.getByRole('button',{name:'Expand compact view',exact:true}).focus();await compactWindow.keyboard.press('Enter');
    check(await compactWindow.getByRole('spinbutton',{name:'Minutes',exact:true}).isVisible()&&await appWindow.getByRole('tab',{name:'Timer',exact:true}).isVisible()&&await page.getByRole('textbox',{name:'Your reflection',exact:true}).inputValue()==='Unsaved final keystroke','Changing Compact/Time-only mode leaves the separate App and session-end pages intact');
    await page.evaluate(()=>window.previewDispatch({type:'flush'}));
    await page.waitForFunction(()=>window.previewMessages.some(m=>m.action==='flushed'));
    const messages=await page.evaluate(()=>window.previewMessages);
    check(messages.findIndex(m=>m.action==='draft'&&m.data.text==='Unsaved final keystroke')<messages.findIndex(m=>m.action==='flushed'),'Closing flush saves the newest draft before acknowledging');
    check(failures.length===0,'No browser JavaScript errors');
    console.log(`${count} browser checks passed.`);
  } finally { await browser.close(); }
})().catch(error=>{console.error(error);process.exitCode=1;});
