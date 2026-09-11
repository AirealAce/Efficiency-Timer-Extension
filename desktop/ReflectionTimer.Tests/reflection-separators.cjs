module.exports=async function reflectionSeparators(context,initial,settings,check){
  const page=await context.newPage();
  try{
    await page.goto('https://reflection-timer.invalid/index.html?view=main');
    await page.waitForFunction(()=>window.previewMessages.some(m=>m.action==='ready'));
    await page.evaluate(({initial,settings})=>{
      window.previewDispatch({type:'init',state:initial});window.previewDispatch({type:'settings',settings});
    },{initial,settings});
    await page.getByRole('tab',{name:'Settings',exact:true}).click();
    const choice=page.getByRole('combobox',{name:'Continue saved reflections with',exact:true});
    check(await choice.inputValue()==='3'&&await choice.locator('option').count()===4,'Saved reflection separator defaults to Newline and exposes four named options');
    await choice.focus();await choice.press('ArrowUp');
    await page.waitForFunction(()=>window.previewMessages.some(m=>m.action==='displayOption'&&m.data.option==='reflectionSeparator'&&m.data.value===2));
    await page.waitForFunction(()=>document.querySelector('[data-select-announcement]').textContent.includes('Bullet'));
    check(await choice.inputValue()==='2'&&await choice.evaluate(e=>document.activeElement===e),'Separator arrow selection saves immediately and uses shared screen-reader feedback');
    await page.keyboard.press('Control+Enter');
    await page.waitForFunction(()=>window.previewMessages.some(m=>m.action==='saveAppearance'&&m.data.reflectionSeparator===2));
    check(true,'Save settings includes the selected reflection separator');
    for(const separator of ['',', ','\n- ','\n']){
      await page.goto('https://reflection-timer.invalid/index.html?view=reflection');
      await page.waitForFunction(()=>window.previewMessages.some(m=>m.action==='ready'));
      const prompt={id:'continuation',isCheckIn:false,endedEarly:false,draft:'First response'+separator,earlyEndReason:'',actual:'1 minute',allotted:'15 minutes',completed:'Today'};
      const state={...initial,prompts:[prompt]};
      await page.evaluate(state=>window.previewDispatch({type:'init',state,promptId:'continuation'}),state);
      const field=page.locator('#reflection-text');
      check(await field.evaluate(e=>e===document.activeElement&&e.selectionStart===e.value.length&&e.selectionEnd===e.value.length),'Reopened reflection places the caret after '+JSON.stringify(separator));
      await page.keyboard.type('Second response');
      await page.locator('#later').click();
      await page.waitForFunction(()=>window.previewMessages.some(m=>m.action==='saveForLater'));
      check(await page.evaluate(text=>window.previewMessages.findLast(m=>m.action==='saveForLater').data.text===text,'First response'+separator+'Second response'),'Save forwards the complete continued response for '+JSON.stringify(separator));
      await page.evaluate(state=>window.previewDispatch({type:'showReflection',state,promptId:'continuation'}),state);
      check(await field.isEditable()&&await field.evaluate(e=>e.selectionStart===e.value.length&&e.selectionEnd===e.value.length),'Cached reflection reopens with an editable caret after '+JSON.stringify(separator));
    }
    await page.evaluate(()=>{
      const post=window.chrome.webview.postMessage;
      window.chrome.webview.postMessage=message=>{
        if(message.action==='saveForLater'){
          window.previewMessages.push(message);
          queueMicrotask(()=>window.previewDispatch({type:'reply',requestId:message.requestId,error:'Storage unavailable'}));
        }else post(message);
      };
    });
    await page.locator('#reflection-text').fill('Keep this after a failed save');
    await page.locator('#later').click();
    await page.waitForFunction(()=>document.querySelector('#error').textContent==='Storage unavailable');
    check(await page.locator('#reflection-text').isEditable()&&await page.locator('#reflection-text').inputValue()==='Keep this after a failed save','Failed Save keeps the response editable and retains the latest text');
  }finally{await page.close();}
};
