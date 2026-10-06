import type { InventoryProjection } from './inventory.js';

export interface PropertyProjection {
  readonly bankAvailable: boolean;
  readonly offers: readonly {
    readonly key: string; readonly name: string; readonly price: string; readonly salePrice: string;
    readonly owned: boolean; readonly canBuy: boolean; readonly canSell: boolean; readonly canEnter: boolean; readonly enterable: boolean;
  }[];
  readonly storage: { readonly key: string; readonly revision: string;
    readonly items: readonly { readonly key: string; readonly definition: string; readonly label: string; readonly quantity: string }[] } | null;
}
type PropertyAction = { readonly action: 'property-buy' | 'property-sell' | 'property-enter'; readonly key: string }
  | { readonly action: 'property-put' | 'property-take'; readonly key: string; readonly item: string; readonly revision: string; readonly amount: number };

export function mountProperty(root: HTMLElement, claim: (action: PropertyAction) => void): {
  update(value: PropertyProjection | null, inventory?: InventoryProjection): void;
} {
  function button(label: string, enabled: boolean, action: PropertyAction): HTMLButtonElement {
    const control = document.createElement('button');
    control.type = 'button'; control.textContent = label; control.disabled = !enabled;
    control.addEventListener('click', () => claim(action));
    return control;
  }
  return { update(value, inventory): void {
    root.replaceChildren();
    if (!value) { root.hidden = true; return; }
    root.hidden = !value.bankAvailable && value.storage === null && !value.offers.some(offer => offer.owned);
    const heading = document.createElement('h3'); heading.textContent = 'Property'; root.append(heading);
    for (const offer of value.offers) {
      if (!value.bankAvailable && !offer.owned) continue;
      const row = document.createElement('div');
      const name = document.createElement('span'); name.textContent = `${offer.name}${offer.owned ? ' · owned' : ''} · ${offer.price} gold`;
      row.append(name);
      if (!offer.owned) row.append(button('Buy', offer.canBuy, { action: 'property-buy', key: offer.key }));
      else {
        row.append(button(`Sell · ${offer.salePrice} gold`, offer.canSell, { action: 'property-sell', key: offer.key }));
        if (offer.enterable) row.append(button('Enter house', offer.canEnter, { action: 'property-enter', key: offer.key }));
      }
      root.append(row);
    }
    const storage = value.storage;
    if (!storage) return;
    const title = document.createElement('h4'); title.textContent = 'Property storage'; root.append(title);
    for (const item of storage.items) {
      const amount = Number(item.quantity);
      root.append(button(`Take ${item.quantity} ${item.label}`, Number.isSafeInteger(amount) && amount > 0,
        { action: 'property-take', key: storage.key, item: item.key, revision: storage.revision, amount }));
    }
    // The Engine store revision is common to player and property inventories; the property
    // projection names it directly rather than borrowing the UI's presentation revision.
    for (const item of inventory?.items ?? []) {
      const amount = Number(item.quantity);
      root.append(button(`Store ${item.quantity} ${item.label}`, item.equippedSlots.length === 0 && Number.isSafeInteger(amount) && amount > 0,
        { action: 'property-put', key: storage.key, item: item.key, revision: storage.revision, amount }));
    }
  } };
}
