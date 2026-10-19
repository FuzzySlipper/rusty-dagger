import type { RustyApplicationUiContext } from '@rusty-engine/product-ui';
import { UI_ACTION_CONTRACT } from './projection.js';

/** Sends one semantic action to the product under the UI action contract. */
export type Send = (data: Readonly<Record<string, unknown>>) => void;

export function sender(context: RustyApplicationUiContext): Send {
  return data => context.intents?.claim('dagger.ui', { kind: 'product-payload', contract: UI_ACTION_CONTRACT, data });
}

/** A button with its type set, so a click never submits a surrounding form. */
export function button(text: string): HTMLButtonElement {
  const element = document.createElement('button');
  element.type = 'button';
  element.textContent = text;
  return element;
}
