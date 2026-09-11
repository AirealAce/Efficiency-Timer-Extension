module.exports=async function reflectionNavigation(context,initial,check){
  const page=await context.newPage();
  const prompts=['first','middle','last'].map((id,i)=>({id,isCheckIn:false,endedEarly:i===1,showEarlyEndReason:i===1,draft:id+' saved response',earlyEndReason:i===1?'An interruption':'',actual:'5 seconds',allotted:'1 minute',completed:'Today'}));
  async function open(index){
    await page.goto('https://reflection-timer.invalid/index.html?view=reflection');
    await page.waitForFunction(()=>window.previewMessages.some(m=>m.action==='ready'));
    await page.evaluate(({state,promptId})=>window.previewDispatch({type:'init',state,promptId}),{state:{...initial,prompts},promptId:prompts[index].id});
  }
  for(const index of [0,1,2]){
    await open(index);
    check(await page.locator('#reflection-prev').getAttribute('aria-disabled')===String(index===0)&&await page.locator('#reflection-next').getAttribute('aria-disabled')===String(index===2),'Prev/Next bounds match pending reflection '+(index+1));
    check((await page.locator('#reflection-position').textContent()).startsWith(`Reflection ${index+1} of 3.`),'Navigation exposes its position to screen readers: '+(index+1));
  }
  await open(1);
  check(await page.locator('#reason-group').isVisible()&&await page.locator('#early-reason').inputValue()==='An interruption','An early-ended saved response shows its reason before navigation');
  await page.locator('#reflection-text').fill('Newest response before Prev');
  await page.locator('#early-reason').fill('Newest reason before Prev');
  await page.evaluate(()=>{
    const post=window.chrome.webview.postMessage;
    window.chrome.webview.postMessage=message=>{
      if(message.action!=='navigateReflection')return post(message);
      window.previewMessages.push(message);window.navigationRequest=message;
    };
  });
  await page.locator('#reflection-prev').click();
  await page.waitForFunction(()=>window.navigationRequest);
  const messages=await page.evaluate(()=>window.previewMessages);
  check(messages.findIndex(m=>m.action==='draft'&&m.data.text==='Newest response before Prev'&&m.data.reason==='Newest reason before Prev')<messages.findIndex(m=>m.action==='navigateReflection'&&m.data.direction===-1),'Prev durably saves both latest inputs before requesting another reflection');
  check(await page.locator('#reflection-text').evaluate(e=>e.readOnly)&&await page.locator('#reflection-next').getAttribute('aria-disabled')==='true'&&await page.locator('#later').getAttribute('aria-disabled')==='true','Navigation freezes editing and conflicting actions until the handoff finishes');
  await page.locator('#reflection-next').dispatchEvent('click');await page.evaluate(()=>window.previewDispatch({type:'reflectionShortcut'}));
  check(await page.evaluate(()=>window.previewMessages.filter(m=>m.action==='navigateReflection').length===1&&!window.previewMessages.some(m=>['queue','close','skip'].includes(m.action))),'Repeated navigation and comma cannot submit, close, or duplicate an in-flight handoff');
  await page.evaluate(()=>window.previewDispatch({type:'reply',requestId:window.navigationRequest.requestId,error:'Test navigation save failed.'}));
  await page.waitForFunction(()=>document.querySelector('#error').textContent==='Test navigation save failed.');
  check(await page.locator('#reflection-text').isEditable()&&await page.locator('#early-reason').inputValue()==='Newest reason before Prev'&&await page.locator('#reflection-next').getAttribute('aria-disabled')==='false','Navigation failure restores editing and controls without losing either field');
  await page.evaluate(state=>window.previewDispatch({type:'state',state}),{...initial,prompts:[prompts[1]]});
  check(await page.locator('#reflection-prev').getAttribute('aria-disabled')==='true'&&await page.locator('#reflection-next').getAttribute('aria-disabled')==='true','Both navigation buttons disable when only one unsent reflection remains');
  await page.goto('https://reflection-timer.invalid/index.html?view=main');
  await page.waitForFunction(()=>window.previewMessages.some(m=>m.action==='ready'));
  await page.evaluate(state=>window.previewDispatch({type:'init',state}),initial);
  await page.getByRole('tab',{name:'Settings',exact:true}).click();
  const checkbox=page.locator('#autoSendIncompleteReflections');
  check(await checkbox.isChecked(),'Auto-send incomplete reflections is checked by default');
  await checkbox.uncheck();
  await page.waitForFunction(()=>window.previewMessages.some(m=>m.action==='displayOption'&&m.data.option==='autoSendIncompleteReflections'&&m.data.value===0&&m.data.quiet));
  check(true,'Turning auto-send off immediately saves the preference silently');
  await checkbox.press('Control+Enter');
  await page.waitForFunction(()=>window.previewMessages.some(m=>m.action==='saveAppearance'&&m.data.autoSendIncompleteReflections===false));
  check(true,'Ctrl+Enter settings save retains the unchecked auto-send preference');
  await checkbox.check();
  await page.waitForFunction(()=>window.previewMessages.some(m=>m.action==='displayOption'&&m.data.option==='autoSendIncompleteReflections'&&m.data.value===1));
  check(true,'Auto-send can be turned back on without sending a reflection from Settings');
  await page.close();
};
