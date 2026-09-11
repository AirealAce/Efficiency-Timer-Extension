// Synthetic bridge only: no production prompts, timer state, audio, or Sheets writes.
module.exports=async function reflectionSave(context,initial,check){
  async function open({initialize=true,view='reflection'}={}){
    const page=await context.newPage();
    await page.goto('https://reflection-timer.invalid/index.html?view='+view);
    await page.waitForFunction(()=>window.previewMessages.some(m=>m.action==='ready'));
    if(initialize)await page.evaluate(state=>window.previewDispatch({type:'init',state,promptId:'save-test'}),{
      ...initial,prompts:[{id:'save-test',isCheckIn:false,endedEarly:true,draft:'',earlyEndReason:'',actual:'2 minutes',allotted:'15 minutes',completed:'Today'}]
    });
    return page;
  }
  const actions=page=>page.evaluate(()=>window.previewMessages.map(m=>m.action));
  const shortcut=page=>page.evaluate(()=>window.previewDispatch({type:'reflectionShortcut'}));
  async function writeAndSave(page,withShortcut){
    // One browser turn ensures the final keystrokes are not autosaved first.
    await page.evaluate(withShortcut=>{
      for(const [id,value] of [['reflection-text','Newest reflection'],['early-reason','Newest early-end reason']]){
        const input=document.getElementById(id);input.value=value;input.dispatchEvent(new Event('input',{bubbles:true}));
      }
      document.querySelector('#reflection-text').focus();
      if(withShortcut)window.previewDispatch({type:'reflectionShortcut'});else document.querySelector('#later').click();
    },withShortcut);
  }
  for(const withShortcut of [false,true]){
    const page=await open();
    check(await page.getByRole('button',{name:'Save',exact:true}).count()===1&&await page.getByRole('button',{name:'Later',exact:true}).count()===0,'Reflection action is named Save, separate from Save & send');
    await writeAndSave(page,withShortcut);
    await page.waitForFunction(()=>window.previewMessages.some(m=>m.action==='close'));
    const messages=await page.evaluate(()=>window.previewMessages);
    const saved=messages.findIndex(m=>m.action==='draft'&&m.data.id==='save-test'&&m.data.text==='Newest reflection'&&m.data.reason==='Newest early-end reason');
    check(saved>=0&&saved<messages.findIndex(m=>m.action==='close'),(withShortcut?'Comma':'Save button')+' saves the latest reflection and early-end reason before closing');
    check(!messages.some(m=>['queue','skip','checkIn','toggle','startOrEnd'].includes(m.action)),'Draft save does not submit, discard, open another prompt, or change the timer');
    await shortcut(page);
    check((await actions(page)).filter(a=>a==='close').length===1,'Repeated Save while closing cannot duplicate the close request');
    await page.close();
  }
  const focusPage=await open();
  await focusPage.locator('#later').focus();await shortcut(focusPage);
  check(await focusPage.locator('#reflection-text').evaluate(e=>e===document.activeElement)&&!(await actions(focusPage)).includes('close'),'Comma from a reflection button focuses the first text box without saving');
  await focusPage.evaluate(()=>document.activeElement.blur());await shortcut(focusPage);
  check(await focusPage.locator('#reflection-text').evaluate(e=>e===document.activeElement)&&!(await actions(focusPage)).includes('close'),'Comma from the reflection page background focuses the first text box');
  await focusPage.locator('#early-reason').fill('Reason saved from second box');await shortcut(focusPage);
  await focusPage.waitForFunction(()=>window.previewMessages.some(m=>m.action==='close'));
  check(await focusPage.evaluate(()=>window.previewMessages.some(m=>m.action==='draft'&&m.data.reason==='Reason saved from second box')),'Comma from the second text box saves both fields and closes');
  await focusPage.close();
  const page=await open();
  await page.evaluate(()=>{
    window.normalPost=window.chrome.webview.postMessage;
    window.failDraft=true;
    window.chrome.webview.postMessage=message=>{
      if(message.action!=='draft')return window.normalPost(message);
      window.previewMessages.push(message);
      if(window.failDraft)queueMicrotask(()=>window.previewDispatch({type:'reply',requestId:message.requestId,error:'Draft storage unavailable.'}));
      else window.pendingDraft=message;
    };
  });
  await writeAndSave(page,true);
  await page.waitForFunction(()=>document.querySelector('#error').textContent==='Draft storage unavailable.');
  check(!(await actions(page)).includes('close')&&await page.locator('#reflection-text').isEditable()&&await page.locator('#early-reason').inputValue()==='Newest early-end reason','Failed draft save retains editable text and reason without closing');
  await page.evaluate(()=>window.failDraft=false);
  await shortcut(page);
  await page.waitForFunction(()=>window.pendingDraft);
  await shortcut(page);await page.locator('#reflection-text').press('Control+Enter');
  check(await page.locator('#reflection-text').evaluate(e=>e.readOnly)&&await page.locator('#later').getAttribute('aria-disabled')==='true'&&!(await actions(page)).some(a=>['close','queue'].includes(a)),'Pending Save blocks duplicate shortcuts and Ctrl+Enter until durable storage succeeds');
  check((await actions(page)).filter(a=>a==='draft').length===2,'Retrying Save writes once after the original failed attempt');
  await page.evaluate(()=>window.previewDispatch({type:'reply',requestId:window.pendingDraft.requestId}));
  await page.waitForFunction(()=>window.previewMessages.some(m=>m.action==='close'));
  check((await actions(page)).filter(a=>a==='close').length===1,'Successful retry closes only after the delayed draft is acknowledged');
  await page.evaluate(()=>window.previewDispatch({type:'reflectionCloseFailed',message:'Close flush failed.'}));
  check(await page.locator('#reflection-text').isEditable()&&await page.locator('#error').textContent()==='Close flush failed.','Native close-flush failure restores editing with an error');
  await page.close();

  const guarded=await open();
  await guarded.evaluate(()=>{const dialog=document.createElement('dialog');dialog.textContent='Modal test';document.body.append(dialog);dialog.showModal();});
  await shortcut(guarded);
  check(!(await actions(guarded)).includes('close'),'Comma does not save through an open modal dialog');
  await guarded.evaluate(()=>{document.querySelector('dialog[open]').close();window.previewDispatch({type:'flush',freeze:true});});
  await guarded.waitForFunction(()=>window.previewMessages.some(m=>m.action==='flushed'));
  await shortcut(guarded);
  check(!(await actions(guarded)).includes('close'),'Comma does not interfere with automatic session-end submission');
  await guarded.evaluate(()=>window.previewDispatch({type:'resumeReflection'}));
  await guarded.getByRole('button',{name:'Save',exact:true}).focus();await guarded.keyboard.press('Enter');
  await guarded.waitForFunction(()=>window.previewMessages.some(m=>m.action==='close'));
  check(!(await actions(guarded)).includes('queue'),'Keyboard Save also preserves a blank draft without sending it');
  await guarded.close();

  for(const options of [{initialize:false},{view:'main'}]){
    const other=await open(options);await shortcut(other);
    check(!(await actions(other)).includes('close'),'Save message is ignored outside an initialized reflection');
    await other.close();
  }
};
