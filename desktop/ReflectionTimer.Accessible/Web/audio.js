import {setOptions,setText} from './ui.js';
// Each original sound event keeps its own visible editor and saved settings.
export function mountAudio({send,run}) {
  const template=document.getElementById('sound-form'),editors=[];let settings;
  const names=['Session end','Success','Failure','Low time'];
  for(let kind=0;kind<4;kind++){
    const form=template.cloneNode(true);form.id=`sound-form-${kind}`;
    form.querySelectorAll('[id]').forEach(node=>{const old=node.id;node.id=`${old}-${kind}`;form.querySelectorAll('[aria-describedby]').forEach(control=>{if(control.getAttribute('aria-describedby')===old)control.setAttribute('aria-describedby',node.id);});});
    const field=id=>form.querySelector(`#${id}-${kind}`);
    field('sound-kind').closest('label').remove();
    const fieldset=document.createElement('fieldset'),legend=document.createElement('legend');legend.textContent=names[kind];fieldset.append(legend,...form.childNodes);form.append(fieldset);
    const save=form.querySelector('button[type=submit]');save.hidden=true;
    template.before(form);
    const editor={kind,form,field,dirty:false,revision:0,saving:Promise.resolve()};editors.push(editor);
    const data=()=>({kind,track:field('sound-track').value==='custom'?0:Number(field('sound-track').value),keepCustom:field('sound-track').value==='custom',
      behavior:Number(field('sound-behavior').value),volume:Number(field('sound-volume').value),fade:field('sound-fade').checked,fadeSeconds:Number(field('sound-fade-seconds').value),quiet:true});
    function saveSound(){const value=data(),revision=editor.revision;editor.saving=editor.saving.catch(()=>{}).then(()=>send('saveSound',value)).then(()=>{if(editor.revision===revision)editor.dirty=false;});return editor.saving;}
    editor.save=saveSound;
    form.addEventListener('input',()=>{editor.dirty=true;editor.revision++;});
    form.addEventListener('change',event=>{editor.dirty=true;editor.revision++;run(async()=>{await saveSound();if(event.target===field('sound-track'))await send('previewSound',{kind,quiet:true});});});
    form.addEventListener('submit',event=>{event.preventDefault();run(saveSound);});
    field('browse-sound').addEventListener('click',()=>run(async()=>{if(editor.dirty)await saveSound();await send('browseSound',{kind});renderEditor(editor);}));
    field('preview-sound').addEventListener('click',()=>run(async()=>{if(editor.dirty)await saveSound();await send('previewSound',{kind});}));
    field('stop-sound').addEventListener('click',()=>run(()=>send('stopSound')));
  }
  template.remove();
  function renderEditor(editor){if(!settings||editor.dirty)return;const sound=settings.sounds.find(s=>s.kind===editor.kind),field=editor.field;
    const choices=settings.tracks.map(t=>({label:t.id===0?`Default · ${sound.defaultName}`:t.name,value:t.id}));
    if(sound.custom)choices.push({label:sound.customName?`Custom MP3 · ${sound.customName}`:'Custom MP3',value:'custom'});
    setOptions(field('sound-track'),choices,sound.custom?'custom':sound.track);
    field('sound-behavior').value=sound.behavior;field('sound-volume').value=sound.volume;field('sound-fade').checked=sound.fadeOutEnabled;field('sound-fade-seconds').value=sound.fadeOutAfterSeconds;
    setText(field('sound-default'),`Default: ${sound.defaultName}`);
  }
  return {render(value){settings=value;editors.forEach(renderEditor);},async flush(){for(const editor of editors){if(editor.dirty)await editor.save();else await editor.saving;}}};
}
