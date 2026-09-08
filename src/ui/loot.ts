import type { InventoryItem } from './inventory.js';

export interface LootProjection {
  readonly container: string;
  readonly revision: string;
  readonly title: string;
  readonly items: readonly InventoryItem[];
  readonly message: string;
}
export type LootAction =
  | { readonly action: 'loot-take'; readonly container: string; readonly revision: string; readonly item: string }
  | { readonly action: 'loot-close'; readonly container: string };

/** Stable DOM rows render C# values; a click only claims the selected item and revision. */
export function mountLoot(root: HTMLElement, claim: (action: LootAction) => void): {
  update(value: LootProjection | null): void; dispose(): void;
} {
  const shell = document.createElement('section');
  shell.className = 'dagger-loot';
  const heading = document.createElement('h2');
  const rows = document.createElement('ul');
  rows.className = 'dagger-loot-items';
  const empty = document.createElement('p');
  empty.textContent = 'Empty. This container remains open until Exit.';
  const status = document.createElement('p');
  status.setAttribute('role', 'status');
  shell.append(heading, rows, empty, status);
  root.append(shell);
  let current: LootProjection | null = null;
  const entries = new Map<string, { element: HTMLLIElement; label: HTMLElement; detail: HTMLElement; button: HTMLButtonElement; icon: HTMLImageElement }>();
  const onClick = (event: MouseEvent): void => {
    const button = event.target instanceof Element ? event.target.closest<HTMLButtonElement>('[data-loot-item]') : null;
    if (button?.dataset.lootItem && current)
      claim({ action: 'loot-take', container: current.container, revision: current.revision, item: button.dataset.lootItem });
  };
  shell.addEventListener('click', onClick);
  return {
    update(value): void {
      if (value === null) { current = null; return; }
      heading.textContent = value.title;
      status.textContent = value.message;
      empty.hidden = value.items.length !== 0;
      if (current?.revision === value.revision) { current = value; return; }
      current = value;
      const keys = new Set(value.items.map(item => item.key));
      for (const [key, row] of entries) if (!keys.has(key)) { row.element.remove(); entries.delete(key); }
      for (const item of value.items) {
        let row = entries.get(item.key);
        if (!row) {
          const element = document.createElement('li');
          const icon = document.createElement('img');
          icon.alt = ''; icon.draggable = false;
          const text = document.createElement('div');
          const label = document.createElement('strong');
          const detail = document.createElement('p');
          text.append(label, detail);
          const button = document.createElement('button');
          button.type = 'button'; button.dataset.lootItem = item.key;
          element.append(icon, text, button);
          row = { element, icon, label, detail, button };
          entries.set(item.key, row); rows.append(element);
        }
        row.label.textContent = `${item.label} × ${item.quantity}`;
        row.detail.textContent = item.details;
        row.button.textContent = item.key.startsWith('stack:') ? 'Take 1' : 'Take';
        row.button.setAttribute('aria-label', `${row.button.textContent} ${item.label}`);
        row.icon.hidden = item.icon === null;
        if (item.icon) row.icon.src = new URL(item.icon, import.meta.url).href;
      }
    },
    dispose(): void { shell.removeEventListener('click', onClick); shell.remove(); },
  };
}
