// Real browser keyboard events with synthetic native replies; no installed data.
module.exports = async function selectAnnouncements(context, initial, settings, check) {
  const page = await context.newPage();
  try {
    await page.goto('https://reflection-timer.invalid/index.html?view=main');
    await page.waitForFunction(() => window.previewMessages.some(m => m.action === 'ready'));
    await page.evaluate(({initial, settings}) => {
      window.previewDispatch({type:'init', state:initial});
      window.previewDispatch({type:'settings', settings});
      window.selectionUpdates=[];
      new MutationObserver(records => {
        for (const record of records) {
          const region=record.target.closest?.('[data-select-announcement]');
          if(region?.textContent)window.selectionUpdates.push(region.textContent);
        }
      }).observe(document.body,{childList:true,subtree:true});
    }, {initial, settings});
    await page.getByRole('tab',{name:'Settings',exact:true}).click();
    const status = page.locator('body > [data-select-announcement]');
    const theme = page.locator('#theme');
    await theme.focus();
    check(await status.textContent()==='', 'Focusing a dropdown does not repeat its native announcement');
    const bounds=await theme.boundingBox();
    await theme.press('ArrowDown');
    await page.waitForFunction(()=>document.querySelector('[data-select-announcement]').textContent==='Light selected.');
    check(await theme.inputValue()==='1'&&await theme.evaluate(e=>e===document.activeElement),'Theme arrow selection retains the focused native dropdown');
    check(JSON.stringify(await theme.boundingBox())===JSON.stringify(bounds),'Selection feedback does not move or resize the controls');
    check(await page.evaluate(()=>window.selectionUpdates.filter(t=>t==='Light selected.').length)===1,'Native input/change events produce one selection status');
    const cdp=await context.newCDPSession(page), tree=await cdp.send('Accessibility.getFullAXTree');
    check(tree.nodes.some(n=>n.role?.value==='status'&&n.properties?.some(p=>p.name==='live'&&p.value.value==='polite'))&&tree.nodes.some(n=>n.name?.value==='Light selected.'),'The selected name is exposed through a polite browser accessibility live region');
    await cdp.detach();
    await page.evaluate(()=>window.selectionUpdates=[]);
    await theme.press('ArrowDown');await theme.press('ArrowDown');
    await page.waitForFunction(()=>document.querySelector('[data-select-announcement]').textContent==='Glamour selected.');
    check(await page.evaluate(()=>JSON.stringify(window.selectionUpdates)===JSON.stringify(['Glamour selected.'])),'Rapid arrows coalesce to the latest selected name');
    await page.evaluate(()=>window.selectionUpdates=[]);
    await theme.press('ArrowDown');await page.waitForTimeout(210);
    check(await page.evaluate(()=>window.selectionUpdates.length===0),'An arrow at the end of a dropdown adds no repeated announcement');

    // Exercise existing static, cloned audio, and dynamically constructed controls.
    for(const id of ['placement','popup','sound-track-1','sound-behavior-1','sound-track-2','sound-behavior-2','sound-track-3','sound-behavior-3','sound-track-0','sound-behavior-0','sheet-mode']){
      const select=page.locator('#'+id);await select.focus();
      const next=await select.evaluate(e=>e.selectedIndex<e.options.length-1?'ArrowDown':'ArrowUp');
      await select.press(next);
      const label=await select.evaluate(e=>e.selectedOptions[0].label.trim());
      await page.waitForFunction(label=>document.querySelector('body > [data-select-announcement]').textContent===label+' selected.',label);
      check(await select.evaluate(e=>document.activeElement===e),id+' receives shared selection feedback without losing focus');
    }
    for(const [tab,id] of [['Timer','timer-low-track'],['Scheduler','schedule-low-track']]){
      await page.getByRole('tab',{name:tab,exact:true}).click();
      const select=page.locator('#'+id);await select.focus();await select.press('ArrowDown');
      const label=await select.evaluate(e=>e.selectedOptions[0].label.trim());
      await page.waitForFunction(label=>document.querySelector('body > [data-select-announcement]').textContent===label+' selected.',label);
      check(true,id+' receives feedback although it was created after shared initialization');
    }
    await page.getByRole('tab',{name:'Settings',exact:true}).click();await theme.focus();
    await page.evaluate(()=>window.selectionUpdates=[]);
    await page.evaluate(settings=>window.previewDispatch({type:'settings',settings}),settings);
    await page.waitForTimeout(210);
    check(await page.evaluate(()=>window.selectionUpdates.length===0),'Programmatic settings refreshes remain silent');
    await theme.press('ArrowUp');await page.keyboard.press('Tab');await page.waitForTimeout(210);
    check(await status.textContent()==='', 'Leaving a dropdown cancels queued selection feedback');
    await page.getByRole('tab',{name:'Timer',exact:true}).click();
    await page.locator('#minutes').press('ArrowUp');await page.waitForTimeout(210);
    check(await status.textContent()==='', 'Number inputs retain their existing speech without an extra dropdown announcement');

    // A future selector, including one in a modal, needs no per-control wiring.
    await page.evaluate(()=>{
      const dialog=document.createElement('dialog');dialog.id='select-test-dialog';
      dialog.innerHTML='<label>Future choice<select><option>First</option><option label="Visible second name">Internal text</option></select></label><button>Leave</button>';
      document.body.append(dialog);dialog.showModal();
    });
    const dialog=page.locator('#select-test-dialog');
    await dialog.locator('select').press('ArrowDown');
    await page.waitForFunction(()=>document.querySelector('#select-test-dialog [data-select-announcement]')?.textContent==='Visible second name selected.');
    check(await dialog.locator('[data-select-announcement]').count()===1,'A newly added modal dropdown announces its visible option label inside the active dialog');
    await dialog.locator('select').press('ArrowUp');await dialog.locator('select').press('ArrowDown');
    await page.waitForFunction(()=>document.querySelector('#select-test-dialog [data-select-announcement]').textContent==='Visible second name selected.');
    check(true,'Returning quickly to the previously spoken option announces it again');
    await page.evaluate(()=>document.querySelector('#select-test-dialog').close());

    // Three rapid edits with delayed native replies used to clear dirty state
    // after the first reply, allowing the second reply to overwrite the third.
    await page.getByRole('tab',{name:'Settings',exact:true}).click();
    await page.evaluate(settings=>{
      window.selectSaved=structuredClone(settings);window.selectReplies=[];
      const original=window.chrome.webview.postMessage;
      window.chrome.webview.postMessage=message=>{
        if(message.action!=='displayOption')return original(message);
        window.previewMessages.push(message);window.selectReplies.push(message);
      };
      window.finishSelectReply=()=>{
        const message=window.selectReplies.shift();
        window.selectSaved[message.data.option]=message.data.value;
        window.previewDispatch({type:'settings',settings:structuredClone(window.selectSaved)});
        window.previewDispatch({type:'reply',requestId:message.requestId});
      };
      document.querySelector('#theme').value='0';
    },settings);
    await theme.focus();await theme.press('ArrowDown');await theme.press('ArrowDown');await theme.press('ArrowDown');
    for(let i=0;i<3;i++){
      await page.waitForFunction(()=>window.selectReplies.length>0);
      await page.evaluate(()=>window.finishSelectReply());
      check(await theme.inputValue()==='3','Delayed settings reply '+(i+1)+' preserves the latest theme selection');
    }
    await page.waitForFunction(()=>document.querySelector('body > [data-select-announcement]').textContent==='Glamour selected.');
    check(await page.evaluate(()=>window.selectSaved.theme===3),'Rapid dropdown changes persist the final selected theme');
  } finally {await page.close();}
};
