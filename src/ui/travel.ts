export interface TravelDestinationProjection {
  readonly region: number;
  readonly index: number;
  readonly name: string;
  readonly kind: string;
}

export interface TravelQuoteProjection {
  readonly identity: string;
  readonly destination: string;
  readonly minutes: number;
  readonly distance: number;
  readonly oceanPixels: number;
  readonly innCost: number;
  readonly shipCost: number;
  readonly totalCost: number;
  readonly canAfford: boolean;
  readonly options: {
    readonly cautious: boolean;
    readonly inn: boolean;
    readonly ship: boolean;
    readonly hasHorse: boolean;
    readonly hasCart: boolean;
    readonly hasShip: boolean;
    readonly availableGold: string;
    readonly availableGoldPieces: string;
  };
}

export interface TravelProjection {
  readonly destinations: readonly TravelDestinationProjection[];
  readonly quote: TravelQuoteProjection | null;
  readonly message: string | null;
}

export type TravelAction =
  | { readonly action: 'travel-search'; readonly text: string }
  | { readonly action: 'travel-preview'; readonly region: number; readonly destination: number;
      readonly cautious: boolean; readonly inn: boolean; readonly ship: boolean };

export function isTravelProjection(value: unknown): value is TravelProjection {
  if (typeof value !== 'object' || value === null) return false;
  const record = value as Record<string, unknown>;
  return Array.isArray(record.destinations) && (record.quote === null || typeof record.quote === 'object')
    && (record.message === null || typeof record.message === 'string');
}

export function mountTravel(root: HTMLElement, claim: (action: TravelAction) => void): {
  update(value: TravelProjection | null): void;
  dispose(): void;
} {
  const shell = document.createElement('section');
  shell.className = 'dagger-travel';
  shell.setAttribute('aria-label', 'Travel route preview');
  const heading = document.createElement('h3');
  heading.textContent = 'Destination and route';
  const search = document.createElement('input');
  search.type = 'search';
  search.maxLength = 80;
  search.placeholder = 'Search discovered locations';
  search.setAttribute('aria-label', 'Search destinations');
  const searchButton = document.createElement('button');
  searchButton.type = 'button';
  searchButton.textContent = 'Search';
  searchButton.addEventListener('click', () => claim({ action: 'travel-search', text: search.value }));
  const destination = document.createElement('select');
  destination.setAttribute('aria-label', 'Destination');
  const cautious = checkbox('Cautious travel', true);
  const inn = checkbox('Stay at inns', false);
  const ship = checkbox('Travel by ship', false);
  const preview = document.createElement('button');
  preview.type = 'button';
  preview.textContent = 'Preview route';
  preview.addEventListener('click', () => {
    const selected = destination.selectedOptions[0];
    if (!selected) return;
    const region = Number(selected.dataset.region);
    const index = Number(selected.dataset.index);
    if (!Number.isInteger(region) || !Number.isInteger(index)) return;
    claim({ action: 'travel-preview', region, destination: index,
      cautious: cautious.input.checked, inn: inn.input.checked, ship: ship.input.checked });
  });
  const result = document.createElement('p');
  result.setAttribute('role', 'status');
  result.setAttribute('aria-live', 'polite');
  shell.append(heading, search, searchButton, destination, cautious.label, inn.label, ship.label, preview, result);
  root.append(shell);

  let lastIdentity: string | null = null;
  return {
    update(value): void {
      const previous = destination.value;
      destination.replaceChildren();
      for (const site of value?.destinations ?? []) {
        const option = document.createElement('option');
        option.value = `${site.region}:${site.index}`;
        option.dataset.region = String(site.region);
        option.dataset.index = String(site.index);
        option.textContent = `${site.name} · ${site.region}:${site.index}`;
        destination.append(option);
      }
      if (Array.from(destination.options).some(option => option.value === previous)) destination.value = previous;
      preview.disabled = destination.options.length === 0;
      const quote = value?.quote;
      if (quote) {
        if (quote.identity !== lastIdentity) {
          cautious.input.checked = quote.options.cautious;
          inn.input.checked = quote.options.inn;
          ship.input.checked = quote.options.ship;
          lastIdentity = quote.identity;
        }
        result.textContent = `${quote.destination}: ${quote.distance} map pixels, ${quote.minutes} minutes, `
          + `${quote.totalCost} gold (${quote.innCost} inn, ${quote.shipCost} ship). `
          + (quote.canAfford ? 'Affordable.' : 'Insufficient funds.')
          + (value?.message ? ` ${value.message}` : '');
      } else {
        lastIdentity = null;
        result.textContent = value?.message ?? 'Choose a discovered destination to preview the route.';
      }
    },
    dispose(): void { shell.remove(); },
  };
}

function checkbox(text: string, checked: boolean): { label: HTMLLabelElement; input: HTMLInputElement } {
  const label = document.createElement('label');
  const input = document.createElement('input');
  input.type = 'checkbox';
  input.checked = checked;
  label.append(input, text);
  return { label, input };
}
