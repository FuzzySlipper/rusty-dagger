import type { InventoryItem, InventoryProjection } from './inventory.js';
import { inventoryTransferRow, mountTransfer } from './transfer.js';

export interface LootProjection {
  readonly container: string;
  readonly revision: string;
  readonly title: string;
  readonly items: readonly InventoryItem[];
  readonly message: string;
}
export type LootAction =
  | { readonly action: 'loot-take'; readonly container: string; readonly revision: string; readonly item: string; readonly amount?: number }
  | { readonly action: 'loot-take-all'; readonly container: string; readonly revision: string }
  | { readonly action: 'loot-close'; readonly container: string };

/**
 * A corpse, chest or pile beside the player's pack on the two-pane transfer layout. Take moves a
 * row (a chosen part of a stack), Take All moves everything; each claims the revision shown.
 */
export function mountLoot(root: HTMLElement, claim: (action: LootAction) => void): {
  update(value: LootProjection | null, inventory?: InventoryProjection | null): void; refresh(): void; dispose(): void;
} {
  const shell = document.createElement('section');
  shell.className = 'dagger-loot';
  const heading = document.createElement('h2');
  const status = document.createElement('p');
  status.className = 'dagger-loot-status';
  status.setAttribute('role', 'status');
  root.append(shell);
  shell.append(heading);
  let current: LootProjection | null = null;
  const transfer = mountTransfer(shell, {
    command(side, key, _command, amount) {
      if (side === 'source' && current !== null)
        claim({ action: 'loot-take', container: current.container, revision: current.revision, item: key, amount });
    },
    takeAll() {
      if (current !== null) claim({ action: 'loot-take-all', container: current.container, revision: current.revision });
    },
  });
  shell.append(status);
  let pack: InventoryProjection | null = null;

  const render = (): void => {
    // The frame and its buttons are the skin's (skin.css), so a hidden panel has nothing to paint.
    if (current === null) { transfer.update(null); return; }
    heading.textContent = current.title;
    status.textContent = current.message;
    transfer.update({
      source: { title: 'Contents', empty: 'Nothing left to take.', rows: current.items.map(item => inventoryTransferRow(item, 'Take')) },
      pack: { title: 'Your pack', empty: 'Your pack is empty.',
        rows: (pack?.items ?? []).filter(item => item.equippedSlots.length === 0).map(item => inventoryTransferRow(item, null)) },
      takeAll: 'Take all',
      encumbrance: pack?.encumbrance ?? null,
    });
  };
  return {
    update(value, inventory) {
      current = value;
      if (inventory !== undefined) pack = inventory;
      render();
    },
    refresh() { transfer.refresh(); },
    dispose(): void { transfer.dispose(); shell.remove(); },
  };
}
