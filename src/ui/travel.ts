import { image, reportMissingArt } from './art.js';

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

/** A published map image and the world-map rectangle, in world-map pixels, its corners show. */
export interface TravelMapImageProjection {
  readonly image: string;
  readonly width: number;
  readonly height: number;
  readonly left: number;
  readonly top: number;
  readonly right: number;
  readonly bottom: number;
}

/** A discovered destination the open region sheet draws, at its world-map pixel. */
export interface TravelMapDestinationProjection {
  readonly index: number;
  readonly name: string;
  readonly kind: string;
  readonly category: string;
  readonly x: number;
  readonly y: number;
}

export interface TravelMapProjection {
  readonly world: TravelMapImageProjection;
  readonly player: { readonly x: number; readonly y: number } | null;
  readonly regions: readonly {
    readonly region: number; readonly name: string; readonly x: number; readonly y: number; readonly discovered: number;
  }[];
  readonly sheet: {
    readonly region: number;
    readonly name: string;
    readonly page: number;
    readonly pages: number;
    readonly art: TravelMapImageProjection;
    readonly destinations: readonly TravelMapDestinationProjection[];
  } | null;
}

export interface TravelProjection {
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
  | { readonly action: 'travel-map'; readonly open: true; readonly region: number; readonly page: number };

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

function isMapImage(value: unknown): value is TravelMapImageProjection {
  if (typeof value !== 'object' || value === null) return false;
  const image = value as Record<string, unknown>;
  return typeof image.image === 'string' && [image.width, image.height, image.left, image.top, image.right, image.bottom]
    .every(Number.isFinite) && (image.right as number) > (image.left as number) && (image.bottom as number) > (image.top as number);
}

function isTravelMap(value: unknown): value is TravelMapProjection {
  if (typeof value !== 'object' || value === null) return false;
  const map = value as Record<string, unknown>;
  const sheet = map.sheet as Record<string, unknown> | null;
  return isMapImage(map.world) && Array.isArray(map.regions)
    && (map.player === null || (typeof map.player === 'object' && map.player !== null))
    && (sheet === null || (typeof sheet === 'object' && isMapImage(sheet.art) && Array.isArray(sheet.destinations)
      && Number.isInteger(sheet.region) && Number.isInteger(sheet.page) && Number.isInteger(sheet.pages)));
}

/** Where a world-map pixel falls on a published image, as percentages of its width and height. */
function place(element: HTMLElement, frame: TravelMapImageProjection, x: number, y: number): boolean {
  const left = (x - frame.left) / (frame.right - frame.left) * 100;
  const top = (y - frame.top) / (frame.bottom - frame.top) * 100;
  element.style.left = `${left}%`;
  element.style.top = `${top}%`;
  return left >= 0 && left <= 100 && top >= 0 && top <= 100;
}

/**
 * The travel map: the world overview with the regions it offers and, once a region is open, that
 * region's published sheet with a dot for each discovered destination the ruleset places on it.
 * Choosing a dot asks the ruleset for the same route preview the destination list does.
 */
function mountTravelMap(claim: (action: TravelAction) => void, choose: (region: number, index: number) => void): {
  readonly element: HTMLElement;
  update(map: TravelMapProjection | null, selected: string): void;
  close(): void;
} {
  const element = document.createElement('div');
  element.className = 'dagger-travel-map';
  const toolbar = document.createElement('div');
  toolbar.className = 'dagger-travel-map-toolbar';
  const toggle = document.createElement('button');
  toggle.type = 'button';
  toggle.className = 'dagger-travel-map-toggle';
  const world = document.createElement('button');
  world.type = 'button';
  world.className = 'dagger-travel-map-world';
  world.textContent = 'World map';
  world.addEventListener('click', () => claim({ action: 'travel-map', open: true }));
  const previous = document.createElement('button');
  previous.type = 'button';
  previous.className = 'dagger-travel-map-previous';
  previous.textContent = 'Previous sheet';
  const next = document.createElement('button');
  next.type = 'button';
  next.className = 'dagger-travel-map-next';
  next.textContent = 'Next sheet';
  const caption = document.createElement('span');
  caption.className = 'dagger-travel-map-caption';
  const regionPicker = document.createElement('select');
  regionPicker.className = 'dagger-travel-map-regions';
  regionPicker.setAttribute('aria-label', 'Region');
  regionPicker.addEventListener('change', () => {
    const region = Number(regionPicker.value);
    if (regionPicker.value === '') claim({ action: 'travel-map', open: true });
    else if (Number.isInteger(region)) claim({ action: 'travel-map', open: true, region, page: 0 });
  });
  toolbar.append(toggle, regionPicker, world, previous, next, caption);
  const canvas = document.createElement('div');
  canvas.className = 'dagger-travel-map-canvas';
  const picture = document.createElement('img');
  picture.alt = '';
  const layer = document.createElement('div');
  layer.className = 'dagger-travel-map-layer';
  canvas.append(picture, layer);
  element.append(toolbar, canvas);

  let current: TravelMapProjection | null = null;
  let drawn = '';
  toggle.addEventListener('click', () => claim({ action: 'travel-map', open: current === null }));
  const turn = (step: number): void => {
    const sheet = current?.sheet;
    if (!sheet || sheet.pages < 2) return;
    claim({ action: 'travel-map', open: true, region: sheet.region, page: (sheet.page + step + sheet.pages) % sheet.pages });
  };
  previous.addEventListener('click', () => turn(-1));
  next.addEventListener('click', () => turn(1));

  return {
    element,
    close(): void {
      // Only an open map rides the snapshot, so a closed one needs no word; a repeated hide before the
      // ruleset answers sends nothing more.
      if (current === null) return;
      current = null;
      claim({ action: 'travel-map', open: false });
    },
    update(map, selected): void {
      current = map;
      toggle.textContent = map ? 'Close map' : 'Open travel map';
      canvas.hidden = regionPicker.hidden = map === null;
      world.hidden = !map?.sheet;
      previous.hidden = next.hidden = !map?.sheet || map.sheet.pages < 2;
      // A snapshot arrives every update; the dots are rebuilt only when what they show changes.
      const signature = map === null ? '' : `${JSON.stringify(map)}|${selected}|${image(map.sheet?.art.image ?? map.world.image) === null}`;
      if (signature === drawn) return;
      drawn = signature;
      layer.replaceChildren();
      if (!map) { caption.textContent = ''; picture.removeAttribute('src'); regionPicker.replaceChildren(); return; }
      const overview = document.createElement('option');
      overview.value = '';
      overview.textContent = 'Whole world';
      regionPicker.replaceChildren(overview, ...map.regions.map(region => {
        const option = document.createElement('option');
        option.value = String(region.region);
        option.textContent = `${region.name} (${region.discovered} known)`;
        return option;
      }));
      regionPicker.value = map.sheet ? String(map.sheet.region) : '';
      const frame = map.sheet?.art ?? map.world;
      const source = image(frame.image);
      if (source) { picture.src = source; canvas.removeAttribute('data-art-missing'); }
      else { picture.removeAttribute('src'); canvas.dataset.artMissing = frame.image; }
      reportMissingArt('Travel map', source ? [] : [frame.image]);
      canvas.style.aspectRatio = `${frame.width} / ${frame.height}`;
      if (map.player) {
        const marker = document.createElement('span');
        marker.className = 'dagger-travel-map-player';
        marker.title = 'You are here';
        if (place(marker, frame, map.player.x, map.player.y)) layer.append(marker);
      }
      const sheet = map.sheet;
      if (!sheet) {
        caption.textContent = 'Choose a region.';
        for (const region of map.regions) {
          const button = document.createElement('button');
          button.type = 'button';
          button.className = 'dagger-travel-map-region';
          button.dataset.region = String(region.region);
          button.setAttribute('aria-label', region.name);
          button.title = `${region.name}: ${region.discovered} known ${region.discovered === 1 ? 'destination' : 'destinations'}`;
          button.addEventListener('click', () => claim({ action: 'travel-map', open: true, region: region.region, page: 0 }));
          if (place(button, frame, region.x, region.y)) layer.append(button);
        }
        return;
      }
      caption.textContent = sheet.pages > 1 ? `${sheet.name}, sheet ${sheet.page + 1} of ${sheet.pages}` : sheet.name;
      if (sheet.destinations.length === 0) caption.textContent += ': no known destinations here';
      for (const destination of sheet.destinations) {
        const dot = document.createElement('button');
        dot.type = 'button';
        dot.className = 'dagger-travel-map-dot';
        dot.dataset.category = destination.category;
        dot.dataset.region = String(sheet.region);
        dot.dataset.index = String(destination.index);
        dot.title = `${destination.name} (${destination.kind})`;
        dot.setAttribute('aria-label', dot.title);
        if (`${sheet.region}:${destination.index}` === selected) dot.setAttribute('aria-pressed', 'true');
        dot.addEventListener('click', () => choose(sheet.region, destination.index));
        if (place(dot, frame, destination.x, destination.y)) layer.append(dot);
      }
    },
  };
}

export function mountTravel(root: HTMLElement, claim: (action: TravelAction) => void): {
  update(value: TravelProjection | null): void;
  /** The travel workflow is no longer shown: an open travel map is closed so it stops being published. */
  hide(): void;
  dispose(): void;
} {
  const shell = document.createElement('section');
  shell.className = 'dagger-travel';
  shell.setAttribute('aria-label', 'Travel');
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
  destination.addEventListener('change', () => { selectedKey = destination.value; selectedOnMap = false; });
  let current: TravelProjection | null = null;
  let selectionChanged = false;
  let submittedIdentity: string | null = null;
  let quotedDestination = '';
  const accept = document.createElement('button'); accept.type = 'button'; accept.className = 'dagger-travel-accept'; accept.textContent = 'Begin journey'; accept.disabled = true;
  accept.addEventListener('click', () => {
    if (selectionChanged || !current?.executionAvailable || !current.quote) return;
    const quote = current.quote; submittedIdentity = quote.identity; accept.disabled = true; current = null;
    claim({ action: 'travel-accept', key: quote.identity, amount: quote.totalCost });
  });
  const selectionMoved = (): void => {
    const quote = current?.quote;
    selectionChanged = !quote || selectedKey !== quotedDestination || cautious.input.checked !== quote.options.cautious
      || inn.input.checked !== quote.options.inn || ship.input.checked !== quote.options.ship;
    accept.disabled = selectionChanged || !current?.executionAvailable || !quote || quote.identity === submittedIdentity;
  };
  for (const input of [destination, cautious.input, inn.input, ship.input]) input.addEventListener('change', selectionMoved);
  const map = mountTravelMap(claim, (region, index) => {
    selectedKey = `${region}:${index}`;
    selectedOnMap = true;
    destination.value = selectedKey;
    if (destination.value !== selectedKey) destination.selectedIndex = -1;
    selectionMoved();
    requestPreview(region, index);
  });
  const lastResult = document.createElement('p'); lastResult.className = 'dagger-travel-last-result';
  const result = document.createElement('p');
  result.setAttribute('role', 'status');
  result.setAttribute('aria-live', 'polite');
  shell.append(heading, map.element, search, searchButton, destination, cautious.label, inn.label, ship.label, preview, accept, result, lastResult);
  root.append(shell);

  let lastIdentity: string | null = null;
  return {
    update(value): void {
      current = value;
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
      map.update(value?.map ?? null, selectedKey);
      const quote = value?.quote;
      if (quote) {
        if (quote.identity !== lastIdentity) {
          selectionChanged = false;
          quotedDestination = selectedKey;
          cautious.input.checked = quote.options.cautious;
          inn.input.checked = quote.options.inn;
          ship.input.checked = quote.options.ship;
          lastIdentity = quote.identity;
        }
        result.textContent = `${quote.destination}: about ${quote.duration}, `
          + `${quote.totalCost} gold (${quote.innCost} inn, ${quote.shipCost} ship). `
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
    hide(): void { map.close(); },
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
