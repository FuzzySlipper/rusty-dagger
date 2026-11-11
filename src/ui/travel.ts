import { isTravelMap, mountAtlas, type AtlasRoute, type TravelMapProjection } from './atlas.js';
import type { ConfirmQuestion } from './confirm.js';

export interface TravelDestinationProjection {
  readonly region: number;
  readonly index: number;
  readonly name: string;
  readonly kind: string;
  readonly regionName: string;
}

export interface TravelQuoteProjection {
  readonly identity: string;
  readonly destination: string;
  /** The route the quote measured, in world-map pixels. */
  readonly route?: AtlasRoute;
  readonly minutes: number;
  readonly duration: string;
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

/** A completed journey; the ruleset's message names what it cost and where it ended. */
export interface TravelResultProjection {
  readonly paidGold: number; readonly elapsedSeconds: number; readonly message: string;
}

export interface TravelProjection {
  readonly guildTeleport?: boolean;
  readonly destinations: readonly TravelDestinationProjection[];
  readonly map: TravelMapProjection | null;
  readonly quote: TravelQuoteProjection | null;
  readonly message: string | null;
  readonly executionAvailable: boolean;
  readonly lastResult: TravelResultProjection | null;
}

export type TravelAction =
  | { readonly action: 'travel-search'; readonly text: string }
  | { readonly action: 'travel-preview'; readonly region: number; readonly destination: number;
      readonly cautious: boolean; readonly inn: boolean; readonly ship: boolean }
  | { readonly action: 'travel-accept'; readonly key: string; readonly amount: number }
  | { readonly action: 'travel-map'; readonly open: boolean }
  | { readonly action: 'atlas-tiles'; readonly tiles: readonly [number, number, number][] };

export function isTravelProjection(value: unknown): value is TravelProjection {
  if (typeof value !== 'object' || value === null) return false;
  const record = value as Record<string, unknown>;
  return Array.isArray(record.destinations) && (record.quote === null || typeof record.quote === 'object')
    && (record.map === null || isTravelMap(record.map))
    && (record.message === null || typeof record.message === 'string')
    && typeof record.executionAvailable === 'boolean'
    && (record.lastResult === null || (typeof record.lastResult === 'object' && record.lastResult !== null
      && typeof (record.lastResult as Record<string, unknown>).message === 'string'));
}

/**
 * The travel screen: the map you travel from, with a journey column beside it. The map fills most of the panel; the
 * column finds a place by name, says how the journey goes and what it costs, and begins it. Choosing a place on the map
 * or in the list asks the ruleset for the route, and the quote it answers with is drawn on the map.
 */
export function mountTravel(root: HTMLElement, claim: (action: TravelAction) => void, ask: (question: ConfirmQuestion) => void): {
  update(value: TravelProjection | null): void;
  /** The triggers or Page Up and Down zoom the map. */
  page(direction: -1 | 1): void;
  /** What turning a page does now, or null. */
  pageHint(): string | null;
  /** The travel screen is shown: the map opens, so the ruleset publishes it. */
  show(): void;
  /** The travel workflow is no longer shown: an open travel map is closed so it stops being published. */
  hide(): void;
  /** Art the page now holds may fill map tiles it was waiting for. */
  refreshArt(): void;
  dispose(): void;
} {
  const shell = document.createElement('section');
  shell.className = 'dagger-travel';
  shell.setAttribute('aria-label', 'Travel');
  const column = document.createElement('div');
  column.className = 'dagger-travel-journey';
  const heading = document.createElement('h3');
  heading.textContent = 'Journey';
  const search = document.createElement('input');
  search.type = 'search';
  search.maxLength = 80;
  search.placeholder = 'Search places or regions';
  search.setAttribute('aria-label', 'Search destinations');
  const searchButton = document.createElement('button');
  searchButton.type = 'submit';
  searchButton.textContent = 'Search';
  // A form, so Enter in the box searches as the button does.
  const searchForm = document.createElement('form');
  searchForm.className = 'dagger-travel-search';
  searchForm.append(search, searchButton);
  searchForm.addEventListener('submit', event => {
    event.preventDefault();
    // A new search hands the choice back to the list, whose first result it then selects.
    selectedOnMap = false;
    claim({ action: 'travel-search', text: search.value });
  });
  const destination = document.createElement('select');
  destination.setAttribute('aria-label', 'Destination');
  const cautious = checkbox('Cautious travel', true);
  const inn = checkbox('Stay at inns', false);
  const ship = checkbox('Travel by ship', false);
  const preview = document.createElement('button');
  preview.type = 'button';
  preview.textContent = 'Preview route';
  // The destination being previewed, as region:index, chosen from the list or from the map. A list
  // choice follows the list as a search replaces it; a map choice stays until another is made.
  let selectedKey = '';
  let selectedOnMap = false;
  const requestPreview = (region: number, index: number): void => {
    claim({ action: 'travel-preview', region, destination: index,
      cautious: cautious.input.checked, inn: inn.input.checked, ship: ship.input.checked });
  };
  preview.addEventListener('click', () => {
    const [region, index] = selectedKey.split(':').map(Number);
    if (!Number.isInteger(region) || !Number.isInteger(index)) return;
    requestPreview(region, index);
  });
  destination.addEventListener('change', () => {
    selectedKey = destination.value; selectedOnMap = false;
    const place = map.place(selectedKey);
    if (place) map.centre(place.x + .5, place.y + .5);
  });
  let current: TravelProjection | null = null;
  let selectionChanged = false;
  let submittedIdentity: string | null = null;
  let quotedDestination = '';
  const accept = document.createElement('button'); accept.type = 'button'; accept.className = 'dagger-travel-accept'; accept.textContent = 'Begin journey'; accept.disabled = true;
  // A journey spends gold and days, so it is asked about first, naming what the quote says it costs.
  accept.addEventListener('click', () => {
    if (selectionChanged || !current?.executionAvailable || !current.quote) return;
    const quote = current.quote;
    const teleport = current.guildTeleport === true;
    const cost = quote.totalCost === 0 ? 'at no cost' : `for ${quote.totalCost} gold`;
    ask({
      message: teleport ? `Teleport to ${quote.destination} ${cost}?` : `Travel to ${quote.destination}? About ${quote.duration}, ${cost}.`,
      accept: teleport ? 'Teleport' : 'Travel',
      onAccept: () => {
        // The quote answered for may have been replaced while the question was open.
        if (selectionChanged || current?.quote?.identity !== quote.identity || !current.executionAvailable) return;
        submittedIdentity = quote.identity; accept.disabled = true; current = null;
        claim({ action: 'travel-accept', key: quote.identity, amount: quote.totalCost });
      },
    });
  });
  const selectionMoved = (): void => {
    const quote = current?.quote;
    selectionChanged = !quote || selectedKey !== quotedDestination || cautious.input.checked !== quote.options.cautious
      || inn.input.checked !== quote.options.inn || ship.input.checked !== quote.options.ship;
    accept.disabled = selectionChanged || !current?.executionAvailable || !quote || quote.identity === submittedIdentity;
  };
  for (const input of [destination, cautious.input, inn.input, ship.input]) input.addEventListener('change', () => {
    selectionMoved();
    const [region, index] = selectedKey.split(':').map(Number);
    if (Number.isInteger(region) && Number.isInteger(index)) requestPreview(region, index);
  });
  // A place chosen by keys or a pad sends focus on to Begin journey once its quote arrives, so the
  // player does not have to step back out through every place on the sheet; a mouse leaves focus be.
  let goOnQuote = false;
  let mapOpen = false;
  const map = mountAtlas(tiles => claim({ action: 'atlas-tiles', tiles }), (region, index, keyed) => {
    goOnQuote = keyed;
    selectedKey = `${region}:${index}`;
    selectedOnMap = true;
    destination.value = selectedKey;
    if (destination.value !== selectedKey) destination.selectedIndex = -1;
    selectionMoved();
    requestPreview(region, index);
  }, () => { if (mapOpen) claim({ action: 'travel-map', open: true }); });
  const lastResult = document.createElement('p'); lastResult.className = 'dagger-travel-last-result';
  const result = document.createElement('p');
  result.setAttribute('role', 'status');
  result.setAttribute('aria-live', 'polite');
  // The journey column, top to bottom: find a place, choose it, how to go, what it costs, then go.
  destination.classList.add('dagger-travel-destination');
  const options = document.createElement('div');
  options.className = 'dagger-travel-options';
  options.append(cautious.label, inn.label, ship.label);
  const go = document.createElement('div');
  go.className = 'dagger-travel-go';
  go.append(preview, accept);
  result.className = 'dagger-travel-quote';
  column.append(heading, searchForm, destination, options, result, go, lastResult);
  shell.append(map.element, column);
  // Choosing where to go is what the panel is for, so it comes before the transport settings.
  root.prepend(shell);

  let lastIdentity: string | null = null;
  return {
    update(value): void {
      current = value;
      heading.textContent = value?.guildTeleport ? 'Guild teleportation' : 'Journey';
      accept.textContent = value?.guildTeleport ? 'Teleport' : 'Begin journey';
      for (const option of [cautious, inn, ship]) option.label.hidden = value?.guildTeleport === true;
      destination.replaceChildren();
      for (const site of value?.destinations ?? []) {
        const option = document.createElement('option');
        option.value = `${site.region}:${site.index}`;
        option.dataset.region = String(site.region);
        option.dataset.index = String(site.index);
        option.textContent = `${site.name} · ${site.kind} · ${site.regionName}`;
        destination.append(option);
      }
      if (!selectedOnMap && !Array.from(destination.options).some(option => option.value === selectedKey))
        selectedKey = destination.options[0]?.value ?? '';
      destination.value = selectedKey;
      if (destination.value !== selectedKey) destination.selectedIndex = -1;
      preview.disabled = selectedKey === '';
      map.update(mapOpen ? value?.map ?? null : null, selectedKey, value?.quote?.route ?? null);
      const quote = value?.quote;
      if (quote) {
        if (quote.identity !== lastIdentity) {
          selectionChanged = false;
          quotedDestination = selectedKey;
          cautious.input.checked = quote.options.cautious;
          inn.input.checked = quote.options.inn;
          ship.input.checked = quote.options.ship;
          lastIdentity = quote.identity;
          if (goOnQuote && value?.executionAvailable && quote.identity !== submittedIdentity) queueMicrotask(() => { if (!accept.disabled) accept.focus(); });
          goOnQuote = false;
        }
        result.textContent = `${quote.destination}: about ${quote.duration} before any arrival-time adjustment, `
          + `${quote.totalCost} gold (${quote.innCost} inn, ${quote.shipCost} ship). `
          + (quote.options.ship && quote.oceanPixels === 0 ? 'This route is entirely over land; ship travel adds no sea passage or fare. ' : '')
          + (quote.canAfford ? 'Affordable.' : 'Insufficient funds.')
          + (value?.message ? ` ${value.message}` : '');
      } else {
        selectionChanged = false;
        submittedIdentity = null;
        lastIdentity = null;
        result.textContent = value?.message ?? 'Choose a discovered destination to preview the route.';
      }
      accept.disabled = selectionChanged || !value?.executionAvailable || !quote || quote.identity === submittedIdentity;
      lastResult.textContent = value?.lastResult?.message ?? '';
    },
    show(): void {
      if (mapOpen) return;
      mapOpen = true;
      claim({ action: 'travel-map', open: true });
    },
    hide(): void {
      // Only an open map rides the snapshot, so a closed one needs no word.
      if (!mapOpen) return;
      mapOpen = false;
      claim({ action: 'travel-map', open: false });
    },
    page(direction): void { map.zoom(direction); },
    pageHint(): string | null { return mapOpen ? 'Zoom' : null; },
    refreshArt(): void { map.refreshArt(); },
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
