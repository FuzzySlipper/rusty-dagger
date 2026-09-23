import type { InventoryItem } from './inventory.js';

export interface LootProjection {
  readonly container: string;
  readonly revision: string;
  readonly title: string;
  readonly items: readonly InventoryItem[];
  readonly message: string;
}
export type LootAction =
  | { readonly action: 'loot-take'; readonly container: string; readonly revision: string; readonly item: string; readonly amount?: number }
  | { readonly action: 'loot-close'; readonly container: string };

/** Stable DOM rows render C# values; a click only claims the selected item and revision. */
import { image, reportMissingArt } from './art.js';

export function mountLoot(root: HTMLElement, claim: (action: LootAction) => void): {
  update(value: LootProjection | null): void; refresh(): void; dispose(): void;
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
  const entries = new Map<string, { element: HTMLLIElement; label: HTMLElement; detail: HTMLElement; quantity: HTMLInputElement; button: HTMLButtonElement; icon: HTMLImageElement }>();
  const onClick = (event: MouseEvent): void => {
    const button = event.target instanceof Element ? event.target.closest<HTMLButtonElement>('[data-loot-item]') : null;
    if (button?.dataset.lootItem && current) {
      const quantity = button.closest('li')?.querySelector<HTMLInputElement>('[data-loot-quantity]');
      const amount = quantity == null ? 1 : Number(quantity.value);
      if (Number.isSafeInteger(amount) && amount > 0)
        claim({ action: 'loot-take', container: current.container, revision: current.revision, item: button.dataset.lootItem, amount });
    }
  };
  shell.addEventListener('click', onClick);
  const render = (value: LootProjection | null): void => {
    // A hidden panel has no frame to paint and nothing to diagnose: return before touching art.
    if (value === null) { current = null; return; }
    // The panel frame is published art like the inventory's; a session that cannot deliver it says
    // which identity is missing instead of showing an unaccounted fallback. The report fires only
    // when the missing set changes, so steady-state projections stay silent.
    const frame = image('inventory.skin.panel-slate.v1');
    shell.style.setProperty('--loot-panel-art', frame === null ? 'none' : `url("${frame}")`);
    if (frame === null) {
      shell.setAttribute('data-art-missing', 'inventory.skin.panel-slate.v1');
    } else {
      shell.removeAttribute('data-art-missing');
    }
    reportMissingArt('loot', frame === null ? ['inventory.skin.panel-slate.v1'] : []);

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
        const quantity = document.createElement('input');
        quantity.type = 'number'; quantity.min = '1'; quantity.step = '1'; quantity.value = '1';
        quantity.dataset.lootQuantity = item.key;
        quantity.setAttribute('aria-label', `Take quantity for ${item.label}`);
        const button = document.createElement('button');
        button.type = 'button'; button.dataset.lootItem = item.key;
        element.append(icon, text, quantity, button);
        row = { element, icon, label, detail, quantity, button };
        entries.set(item.key, row); rows.append(element);
      }
      row.label.textContent = `${item.label} × ${item.quantity}`;
      row.detail.textContent = item.details;
      row.quantity.hidden = !item.key.startsWith('stack:');
      row.quantity.max = item.quantity;
      if (Number(row.quantity.value) > Number(item.quantity)) row.quantity.value = item.quantity;
      row.button.textContent = item.key.startsWith('stack:') ? 'Take' : 'Take';
      row.button.setAttribute('aria-label', `${row.button.textContent} ${item.label}`);
      const iconSource = image(item.icon);
      row.icon.hidden = iconSource === null;
      if (iconSource !== null) row.icon.src = iconSource;
    }
  };
  return {
    update: render,
    // Rows keep their icons, so art that arrives later repaints the contents already shown.
    refresh(): void {
      if (current === null) return;
      const held = current;
      current = null;
      render(held);
    },
    dispose(): void { shell.removeEventListener('click', onClick); shell.remove(); },
  };
}
