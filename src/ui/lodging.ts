import { createStepper } from './stepper.js';

export interface LodgingProjection {
  readonly key: string;
  readonly name: string;
  readonly days: number;
  readonly price: number;
  readonly remainingHours: number;
  readonly canBook: boolean;
  readonly maximumDays: number;
}
export type LodgingAction =
  | { readonly action: 'lodging-quote'; readonly key: string; readonly days: number }
  | { readonly action: 'lodging-book'; readonly key: string; readonly days: number; readonly amount: number };
export function isLodgingProjection(value: unknown): value is LodgingProjection {
  if (value === null || typeof value !== 'object') return false;
  const v = value as Record<string, unknown>;
  return typeof v.key === 'string' && typeof v.name === 'string' && typeof v.canBook === 'boolean'
    && Number.isSafeInteger(v.maximumDays) && (v.maximumDays as number) >= 1
    && Number.isSafeInteger(v.days) && (v.days as number) >= 1 && (v.days as number) <= (v.maximumDays as number)
    && Number.isSafeInteger(v.price) && (v.price as number) >= 0
    && Number.isSafeInteger(v.remainingHours) && (v.remainingHours as number) >= 0;
}
/** Tavern lodging: the extra days as a stepper bounded by the ruleset's maximum stay, quoted before booking. */
export function mountLodging(root: HTMLElement, claim: (action: LodgingAction) => void): {
  update(value: LodgingProjection | null): void; dispose(): void;
} {
  const shell = document.createElement('section');
  shell.className = 'dagger-lodging';
  shell.setAttribute('aria-label', 'Tavern lodging');
  shell.innerHTML = `<h3>Tavern lodging</h3><p class="dagger-lodging-status" role="status"></p>
    <div class="dagger-lodging-stay"><span>Additional days</span></div>
    <button class="dagger-lodging-quote" type="button">Get room quote</button>
    <p class="dagger-lodging-price"></p><button class="dagger-lodging-book" type="button">Book room</button>`;
  root.append(shell);
  const status = shell.querySelector<HTMLElement>('.dagger-lodging-status')!;
  const price = shell.querySelector<HTMLElement>('.dagger-lodging-price')!;
  const days = createStepper('Additional days');
  days.input.classList.add('dagger-lodging-days');
  days.limit(1, 1, 1);
  shell.querySelector('.dagger-lodging-stay')!.append(days.element);
  const quote = shell.querySelector<HTMLButtonElement>('.dagger-lodging-quote')!;
  const book = shell.querySelector<HTMLButtonElement>('.dagger-lodging-book')!;
  let current: LodgingProjection | null = null;
  // Booking takes the quoted price, so it is offered only for the stay that was quoted.
  const changed = (): void => { book.disabled = !current?.canBook || current.days !== days.value(); };
  const request = (): void => { if (current) claim({ action: 'lodging-quote', key: current.key, days: days.value() }); };
  const reserve = (): void => {
    if (!current || book.disabled) return;
    claim({ action: 'lodging-book', key: current.key, days: days.value(), amount: current.price });
  };
  days.input.addEventListener('input', changed);
  quote.addEventListener('click', request);
  book.addEventListener('click', reserve);
  shell.hidden = true;
  return {
    update(value) {
      const fresh = value?.key !== current?.key;
      current = value;
      shell.hidden = value === null;
      if (value) {
        // A new inn starts from its own quoted stay; the same inn keeps the player's choice.
        if (fresh) days.input.value = '';
        days.limit(1, value.maximumDays, value.days);
        status.textContent = `${value.name}: ${value.remainingHours > 0 ? `${value.remainingHours} paid hour(s) remaining.` : 'No current room booking.'}`;
        price.textContent = value.canBook ? `${value.days} additional day(s): ${value.price} gold.` : `A stay may total up to ${value.maximumDays} days.`;
      }
      changed();
    },
    dispose() { days.input.removeEventListener('input', changed); quote.removeEventListener('click', request); book.removeEventListener('click', reserve); shell.remove(); },
  };
}
