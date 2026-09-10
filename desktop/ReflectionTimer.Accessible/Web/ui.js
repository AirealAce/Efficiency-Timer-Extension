export function setText(element, value) {
  const text = String(value ?? '');
  if (element.textContent !== text) element.textContent = text;
}
export function formatClock(seconds) {
  const h = Math.floor(seconds / 3600), m = Math.floor(seconds / 60) % 60, s = seconds % 60;
  return h > 0 ? `${h}:${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}` : `${m}:${String(s).padStart(2, '0')}`;
}
export function durationSeconds(values) {
  if (values.some(v => !/^\d+$/.test(v))) throw new Error('Enter whole, non-negative numbers for hours, minutes, and seconds.');
  const result = Number(values[0]) * 3600 + Number(values[1]) * 60 + Number(values[2]);
  if (!Number.isSafeInteger(result) || result < 1 || result > 31536000) throw new Error('Enter a duration between one second and one year.');
  return result;
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
