import type { PropertyStorage } from './inventory.js';
import { redraw } from './redraw.js';

export interface PropertyProjection {
  readonly bankAvailable: boolean;
  readonly offers: readonly {
    readonly key: string; readonly name: string; readonly price: string; readonly salePrice: string;
    readonly owned: boolean; readonly canBuy: boolean; readonly canSell: boolean; readonly canEnter: boolean; readonly enterable: boolean;
  }[];
  /** The owned property's storage while the player is inside it; the inventory's Storage tab draws it. */
  readonly storage: PropertyStorage | null;
}
type PropertyAction = { readonly action: 'property-buy' | 'property-sell' | 'property-enter'; readonly key: string };

export function mountProperty(root: HTMLElement, claim: (action: PropertyAction) => void): {
  update(value: PropertyProjection | null): void;
} {
  function button(label: string, enabled: boolean, action: PropertyAction): HTMLButtonElement {
    const control = document.createElement('button');
    control.type = 'button'; control.textContent = label; control.disabled = !enabled;
    control.addEventListener('click', () => claim(action));
    return control;
  }
  // Property redraws only when what it offers changes, so a focused Buy or Sell keeps focus.
  return { update(value): void {
    redraw(root, JSON.stringify([value?.bankAvailable, value?.offers]), () => draw(value));
  } };
  function draw(value: PropertyProjection | null): void {
    root.replaceChildren();
    if (!value) { root.hidden = true; return; }
    root.hidden = !value.bankAvailable && !value.offers.some(offer => offer.owned);
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
  }
}
