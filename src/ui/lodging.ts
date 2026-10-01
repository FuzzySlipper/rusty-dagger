export interface LodgingProjection {
  readonly key: string;
  readonly name: string;
  readonly days: number;
  readonly price: number;
  readonly remainingHours: number;
  readonly canBook: boolean;
}
export type LodgingAction =
  | { readonly action: 'lodging-quote'; readonly key: string; readonly days: number }
  | { readonly action: 'lodging-book'; readonly key: string; readonly days: number; readonly amount: number };
export function isLodgingProjection(value: unknown): value is LodgingProjection {
  if (value === null || typeof value !== 'object') return false;
  const v = value as Record<string, unknown>;
  return typeof v.key === 'string' && typeof v.name === 'string' && typeof v.canBook === 'boolean'
    && Number.isSafeInteger(v.days) && (v.days as number) >= 1 && (v.days as number) <= 350
    && Number.isSafeInteger(v.price) && (v.price as number) >= 0
    && Number.isSafeInteger(v.remainingHours) && (v.remainingHours as number) >= 0;
}
export function mountLodging(root: HTMLElement, claim: (action: LodgingAction) => void): {
  update(value: LodgingProjection | null): void; dispose(): void;
} {
  const shell = document.createElement('section');
  shell.className = 'dagger-lodging';
  shell.setAttribute('aria-label', 'Tavern lodging');
  shell.innerHTML = `<h3>Tavern lodging</h3><p class="dagger-lodging-status" role="status"></p>
    <label>Additional days <input class="dagger-lodging-days" type="number" min="1" max="350" step="1" value="1"></label>
    <button class="dagger-lodging-quote" type="button">Get room quote</button>
    <p class="dagger-lodging-price"></p><button class="dagger-lodging-book" type="button">Book room</button>`;
  root.append(shell);
  const status = shell.querySelector<HTMLElement>('.dagger-lodging-status')!;
  const price = shell.querySelector<HTMLElement>('.dagger-lodging-price')!;
  const days = shell.querySelector<HTMLInputElement>('.dagger-lodging-days')!;
  const quote = shell.querySelector<HTMLButtonElement>('.dagger-lodging-quote')!;
  const book = shell.querySelector<HTMLButtonElement>('.dagger-lodging-book')!;
  let current: LodgingProjection | null = null;
  const selectedDays = (): number => days.value.trim() === '' ? NaN : Number(days.value);
  const valid = (): boolean => Number.isSafeInteger(selectedDays()) && selectedDays() >= 1 && selectedDays() <= 350;
  const changed = (): void => { book.disabled = !current?.canBook || !valid() || current.days !== selectedDays(); };
  const request = (): void => {
    if (!current) return;
    if (!valid()) { price.textContent = 'Enter a whole number from 1 to 350 days.'; days.focus(); return; }
    claim({ action: 'lodging-quote', key: current.key, days: selectedDays() });
  };
  const reserve = (): void => {
    if (!current || book.disabled) return;
    claim({ action: 'lodging-book', key: current.key, days: selectedDays(), amount: current.price });
  };
  days.addEventListener('input', changed);
  quote.addEventListener('click', request);
  book.addEventListener('click', reserve);
  shell.hidden = true;
  return {
    update(value) {
      if (value?.key !== current?.key) days.value = String(value?.days ?? 1);
      current = value;
      shell.hidden = value === null;
      if (value) {
        status.textContent = `${value.name}: ${value.remainingHours > 0 ? `${value.remainingHours} paid hour(s) remaining.` : 'No current room booking.'}`;
        price.textContent = value.canBook ? `${value.days} additional day(s): ${value.price} gold.` : 'A stay may total up to 350 days.';
      }
      changed();
    },
    dispose() { days.removeEventListener('input', changed); quote.removeEventListener('click', request); book.removeEventListener('click', reserve); shell.remove(); },
  };
}
