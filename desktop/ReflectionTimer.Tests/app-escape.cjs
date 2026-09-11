// Synthetic bridge: exercise App keys without hiding the user's window.
module.exports=async function appEscape(context,initial,settings,check){
  async function open(initialize=true){
    const page=await context.newPage();
    await page.goto('https://reflection-timer.invalid/index.html?view=main');
    await page.waitForFunction(()=>window.previewMessages.some(m=>m.action==='ready'));
    if(initialize)await page.evaluate(({initial,settings})=>{
      window.previewDispatch({type:'init',state:initial});
      window.previewDispatch({type:'settings',settings:{...settings,sheetMode:'fixed'}});
    },{initial,settings});
    return page;
  }
  const closeCount=page=>page.evaluate(()=>window.previewMessages.filter(m=>m.action==='close').length);
  const tabs=[['timer',['#hours','#minutes','#seconds','#quit']],['schedules',['#schedule-start','#schedule-minutes']],
    ['outbox',['#retry-selected']],['settings',['#theme','#sheet-name','#save-settings']],['diagnostics',[]]];
  for(const [tab,controls] of tabs)for(const target of [`#tab-${tab}`,...controls,'background']){
    const page=await open();await page.locator(`#tab-${tab}`).click();
    const draft=tab==='timer'?['#minutes','42']:tab==='schedules'?['#schedule-minutes','31']:tab==='settings'?['#sheet-name','Unfinished local edit']:null;
    if(draft)await page.locator(draft[0]).fill(draft[1]);
    if(target==='background')await page.evaluate(()=>document.activeElement.blur());else await page.locator(target).focus();
    const before=await page.evaluate(()=>window.previewMessages.length);
    await page.keyboard.press('Escape');await page.waitForFunction(()=>window.previewMessages.some(m=>m.action==='close'));
    const actions=await page.evaluate(before=>window.previewMessages.slice(before).map(m=>m.action),before);
    check(actions.filter(a=>a==='close').length===1&&!actions.some(a=>['quit','toggle','reset','end','queue','skip','saveForLater','saveAppearance','connectionStore','settingsSaveComplete','navigateReflection'].includes(a)),`App Escape on ${tab} from ${target} requests only the normal hide action`);
    check(await page.locator('body').getAttribute('data-tab')===tab&&(!draft||await page.locator(draft[0]).inputValue()===draft[1]),`App Escape preserves the ${tab} tab and unfinished input`);
    await page.close();
  }
  const held=await open();await held.locator('#minutes').focus();
  await held.keyboard.down('Escape');await held.keyboard.down('Escape');await held.keyboard.down('Escape');await held.keyboard.up('Escape');
  await held.waitForFunction(()=>window.previewMessages.some(m=>m.action==='close'));
  check(await closeCount(held)===1,'Holding App Escape sends one hide request');await held.close();
  const modal=await open();
  await modal.evaluate(()=>document.querySelector('#entry-dialog').showModal());await modal.keyboard.press('Escape');
  await modal.waitForFunction(()=>!document.querySelector('dialog[open]'));
  check(await closeCount(modal)===0,'App Escape closes an open dialog before hiding App');
  await modal.keyboard.press('Escape');await modal.waitForFunction(()=>window.previewMessages.some(m=>m.action==='close'));
  check(await closeCount(modal)===1,'The next Escape hides App after its dialog closes');await modal.close();
  const retry=await open();await retry.locator('#tab-settings').click();await retry.locator('#sheet-name').fill('Keep this draft');
  await retry.evaluate(()=>{
    const normal=window.chrome.webview.postMessage;window.failHide=true;
    window.chrome.webview.postMessage=message=>{
      if(message.action!=='close'||!window.failHide)return normal(message);
      window.previewMessages.push(message);queueMicrotask(()=>window.previewDispatch({type:'reply',requestId:message.requestId,error:'Unable to hide App.'}));
    };
  });
  await retry.keyboard.press('Escape');await retry.waitForFunction(()=>document.querySelector('#error').textContent==='Unable to hide App.');
  check(await retry.locator('#sheet-name').inputValue()==='Keep this draft'&&await retry.locator('#sheet-name').isEditable(),'A failed hide retains an editable settings draft');
  await retry.evaluate(()=>window.failHide=false);await retry.keyboard.press('Escape');
  await retry.waitForFunction(()=>window.previewMessages.filter(m=>m.action==='close').length===2);
  check(await retry.locator('#sheet-name').inputValue()==='Keep this draft'&&await retry.locator('body').getAttribute('data-tab')==='settings','App Escape can retry without losing the selected tab or draft');
  await retry.close();
  const loading=await open(false);await loading.keyboard.press('Escape');
  check(await closeCount(loading)===0,'App Escape waits for initialization');await loading.close();
};
