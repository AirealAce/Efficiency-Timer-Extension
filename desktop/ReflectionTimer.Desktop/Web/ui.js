export function setText(element, value) {
  const text = String(value ?? '');
  if (element.textContent !== text) element.textContent = text;
}
export function setOptions(select, choices, value) {
  const signature=JSON.stringify(choices);
  if(select.dataset.choices!==signature){select.replaceChildren(...choices.map(c=>new Option(c.label,String(c.value))));select.dataset.choices=signature;}
  if(select.value!==String(value))select.value=String(value);
}
export function formatClock(seconds) {
  const h = Math.floor(seconds / 3600), m = Math.floor(seconds / 60) % 60, s = seconds % 60;
  return h > 0 ? `${h}:${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}` : `${m}:${String(s).padStart(2, '0')}`;
}
export function durationSeconds(values) {
  const result = durationPreviewSeconds(values);
  if (result < 1) throw new Error('Enter a duration between one second and one year.');
  return result;
}
// An empty unit is zero while editing; a zero preview is valid, but cannot start a session.
export function durationPreviewSeconds(values) {
  const parts = values.map(v => v.trim() || '0');
  if (parts.some(v => !/^\d+$/.test(v))) throw new Error('Enter whole, non-negative numbers for hours, minutes, and seconds.');
  const result = Number(parts[0]) * 3600 + Number(parts[1]) * 60 + Number(parts[2]);
  if (!Number.isSafeInteger(result) || result > 31536000) throw new Error('Enter a duration between one second and one year.');
  return result;
}
export function normalizeEmptyDuration(input, changed) {
  input.addEventListener('blur', () => {
    if (input.value === '' && !input.validity.badInput) { input.value = '0'; changed(); }
  });
}
// One deliberate Enter (or button submit) means one toggle, even while the
// native app is replying. Holding Enter must not alternate pause and resume.
export function bindTimerEditor(form, inputs, run, toggle) {
  let pending = false;
  form.addEventListener('submit', event => {
    event.preventDefault();
    if (pending) return;
    pending = true;
    run(async () => {
      try { await toggle(); }
      finally { pending = false; }
    });
  });
  inputs.forEach(input => input.addEventListener('keydown', event => {
    if (event.key !== 'Enter' || event.isComposing) return;
    event.preventDefault();
    if (!event.repeat) form.requestSubmit();
  }));
}
// Keep existing rows, cells, and buttons attached. Updates must not replace the
// focused element or the objects a screen reader is currently navigating.
export function reconcileRows(container, records, create, update) {
  const existing = new Map([...container.children].map(row => [row.dataset.id, row]));
  const wanted = new Set(records.map(record => record.id));
  for (const [id, row] of existing) if (!wanted.has(id)) row.remove();
  records.forEach((record, index) => {
    let row = existing.get(record.id);
    if (!row) { row = create(record); row.dataset.id = record.id; }
    const atIndex = container.children[index];
    if (atIndex !== row) container.insertBefore(row, atIndex ?? null);
    update(row, record);
  });
}
