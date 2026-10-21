import type { SaveSlotEntry } from './projection.js';
/** One saved game in the slot list: its name, who and where, the world's date, when it was saved and the time played. */
export function slotRow(entry: SaveSlotEntry, selected: boolean): HTMLLIElement {
  const item = document.createElement('li');
  const choose = document.createElement('button');
  choose.type = 'button';
  choose.className = 'dagger-save-slot';
  choose.dataset.saveSlot = entry.key;
  choose.setAttribute('aria-pressed', String(selected));
  const name = document.createElement('strong'); name.textContent = entry.label;
  const who = document.createElement('span'); who.className = 'dagger-save-slot-who';
  who.textContent = [entry.character, entry.level != null ? `level ${entry.level}` : null, entry.location].filter(Boolean).join(' · ');
  const when = document.createElement('span'); when.className = 'dagger-save-slot-when';
  const saved = new Date(entry.savedAtUtc);
  when.textContent = [entry.worldTime, Number.isNaN(saved.getTime()) ? null : `saved ${saved.toLocaleString()}`,
    entry.playSeconds != null ? `played ${formatPlayTime(entry.playSeconds)}` : null].filter(Boolean).join(' · ');
  choose.append(name, ...(who.textContent ? [who] : []), when);
  item.append(choose);
  return item;
}

export function newSlotRow(selected: boolean): HTMLLIElement {
  const item = document.createElement('li');
  const choose = document.createElement('button');
  choose.type = 'button';
  choose.className = 'dagger-save-slot is-new';
  choose.dataset.saveSlot = '';
  choose.setAttribute('aria-pressed', String(selected));
  choose.textContent = 'New saved game';
  item.append(choose);
  return item;
}

/** Play time as hours and minutes. */
export function formatPlayTime(seconds: number): string {
  const minutes = Math.floor(Math.max(0, seconds) / 60);
  const hours = Math.floor(minutes / 60);
  return hours > 0 ? `${hours} h ${String(minutes % 60).padStart(2, '0')} min` : `${minutes} min`;
}
