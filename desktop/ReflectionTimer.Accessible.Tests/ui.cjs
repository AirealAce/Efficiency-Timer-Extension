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
    const page = await browser.newPage({viewport:{width:980,height:820}});
    const failures=[]; page.on('pageerror', e=>failures.push(e.message));
    await page.route('**/*', async route=>{
      const name=new URL(route.request().url()).pathname.slice(1);
      if (!['index.html','app.js','app.css','ui.js'].includes(name)) return route.abort();
      await route.fulfill({body:await fs.readFile(path.join(web,name)),contentType:name.endsWith('.js')?'text/javascript':name.endsWith('.css')?'text/css':'text/html'});
    });
    await page.addInitScript(() => {
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
      schedules:[{id:'s1',start:'09/17/2026 10:00 AM',duration:'15 minutes',repeat:'Off',lowTime:'On',status:'Scheduled'}],
      outbox:[{id:'o1',saved:'09/10/2026 10:00 AM',destination:'Local preview only',status:'Pending',attempts:0,message:'A sample reflection.',duration:'15 minutes'}]};
    await page.goto('https://reflection-timer.invalid/index.html?view=main');
    await page.waitForFunction(()=>window.previewMessages.some(m=>m.action==='ready'));
    await page.evaluate(state=>window.previewDispatch({type:'init',state}),initial);
    check(await page.getByRole('heading',{name:'Reflection Timer',exact:true}).count()===1,'Document exposes its main heading');
    check(await page.getByRole('textbox',{name:'Minutes',exact:true}).inputValue()==='15','Duration has a native label');
    await page.getByRole('textbox',{name:'Minutes',exact:true}).fill('12');
    await page.evaluate(()=>{
      window.clockMutations=[];window.focusEvents=[];
      document.addEventListener('focusin',e=>window.focusEvents.push(e.target.id));
      new MutationObserver(records=>window.clockMutations.push(...records.map(r=>r.target.parentElement?.id||r.target.id))).observe(document.body,{subtree:true,childList:true,characterData:true});
      for(let seconds=899;seconds>779;seconds--) window.previewDispatch({type:'clock',clock:{seconds,text:'Tick',status:'Running'}});
    });
    check(await page.locator('#minutes').evaluate(e=>document.activeElement===e)&&await page.locator('#minutes').inputValue()==='12','120 countdown ticks preserve input focus and draft');
    check(await page.locator('#status').textContent()===''&&await page.locator('#time-snapshot').textContent()==='Time checked: 15 minutes remaining. Ready.','Ticks do not update live speech or accessible time snapshot');
    check(await page.locator('#visual-clock').getAttribute('aria-hidden')==='true','Animated countdown is separate from the readable time snapshot');
    check((await page.evaluate(()=>window.focusEvents)).length===0,'Ticks generate no DOM focus changes');
    await page.getByRole('button',{name:'Read remaining time',exact:true}).click();
    check(await page.evaluate(()=>window.previewMessages.at(-1).action)==='readTime','Remaining time is requested explicitly');
    await page.evaluate(()=>window.previewDispatch({type:'timeRead',clock:{seconds:780,text:'13 minutes',status:'Running'}}));
    await page.waitForFunction(()=>document.getElementById('status').textContent.includes('13 minutes'));
    check((await page.locator('#status').textContent())==='13 minutes remaining. Running.','Time request provides human-readable status');
    check(await page.getByRole('table',{name:'Scheduled sessions',exact:true}).count()===1 && await page.getByRole('columnheader',{name:'Duration',exact:true}).count()===1,'Schedule has native table and header semantics');
    const success=page.getByRole('button',{name:'Simulate success for entry saved 09/10/2026 10:00 AM'});
    await success.focus();
    await page.evaluate(()=>{ window.savedRow=document.querySelector('#outbox-rows tr');window.savedCell=window.savedRow.cells[2];window.savedButton=document.activeElement; });
    const changed=structuredClone(initial);changed.outbox[0].status='Simulated success';
    await page.evaluate(state=>window.previewDispatch({type:'state',state}),changed);
    check(await page.evaluate(()=>window.savedRow===document.querySelector('#outbox-rows tr')&&window.savedCell===window.savedRow.cells[2]&&window.savedButton===document.activeElement),'Status update preserves row, cell, and focused action identity');
    check((await page.locator('#outbox-rows').textContent()).includes('Simulated success'),'Changed table cell is updated');
    await page.getByRole('button',{name:'Details for entry saved 09/10/2026 10:00 AM'}).click();
    check(await page.getByRole('dialog',{name:'Saved reflection'}).isVisible(),'Details opens a labeled native dialog');
    await page.keyboard.press('Escape');
    check(await page.getByRole('button',{name:'Details for entry saved 09/10/2026 10:00 AM'}).evaluate(e=>document.activeElement===e),'Dialog returns focus to invoking button');
    await page.setViewportSize({width:420,height:750});
    check(await page.evaluate(()=>document.documentElement.scrollWidth<=window.innerWidth),'Narrow view reflows without horizontal page overflow');
    await page.goto('https://reflection-timer.invalid/index.html?view=compact');
    await page.waitForFunction(()=>window.previewMessages.some(m=>m.action==='ready'));
    await page.evaluate(state=>window.previewDispatch({type:'init',state}),initial);
    const mode=page.getByRole('button',{name:'Time-only view',exact:true});await mode.click();
    check(await page.getByRole('textbox',{name:'Minutes',exact:true}).isHidden()&&await page.getByRole('button',{name:'Show timer controls'}).evaluate(e=>document.activeElement===e),'Time-only mode preserves focus on its visible keyboard toggle');
    await page.getByRole('button',{name:'Show timer controls'}).click();
    check(await page.getByRole('textbox',{name:'Minutes',exact:true}).isVisible(),'Compact controls can be restored without hover');
    await page.goto('https://reflection-timer.invalid/index.html?view=reflection');
    await page.waitForFunction(()=>window.previewMessages.some(m=>m.action==='ready'));
    const reflection=structuredClone(initial);reflection.prompts=[{id:'p1',isCheckIn:false,endedEarly:false,draft:'',earlyEndReason:'',actual:'2 minutes',allotted:'15 minutes',completed:'Today'}];
    await page.evaluate(state=>window.previewDispatch({type:'init',state,promptId:'p1'}),reflection);
    await page.getByRole('textbox',{name:'Your reflection',exact:true}).fill('Unsaved final keystroke');
    await page.evaluate(()=>window.previewDispatch({type:'flush'}));
    await page.waitForFunction(()=>window.previewMessages.some(m=>m.action==='flushed'));
    const messages=await page.evaluate(()=>window.previewMessages);
    check(messages.findIndex(m=>m.action==='draft'&&m.data.text==='Unsaved final keystroke')<messages.findIndex(m=>m.action==='flushed'),'Closing flush saves the newest draft before acknowledging');
    check(failures.length===0,'No browser JavaScript errors');
    console.log(`${count} browser checks passed.`);
  } finally { await browser.close(); }
})().catch(error=>{console.error(error);process.exitCode=1;});
