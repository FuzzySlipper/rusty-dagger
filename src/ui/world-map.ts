import { isTravelMap, mountAtlas, type AtlasMarks, type AtlasRealm, type TravelMapProjection } from './atlas.js';
import type { ExpeditionFoundProjection, ExpeditionMarkProjection } from './expedition.js';
import { button } from './host.js';
import { redraw } from './redraw.js';

/**
 * The world map as the ruleset publishes it while the page shows it: the atlas, where the party (or the player) stands,
 * where the party heads, the pin and the last Find, whether the party is out on the travel map, and whether Travel
 * there can set it going.
 */
/**
 * The Realms view: each board region's shade by the arms it shows now, and the key of the crowns that hold regions.
 * The regions' shapes come only when the map opens or the page asks for its places again.
 */
export interface RealmsProjection {
  readonly regions: readonly { readonly region: number; readonly arms: string; readonly colour: string; readonly liege: boolean }[];
  readonly key: readonly { readonly arms: string; readonly name: string; readonly blazon: string; readonly colour: string }[];
  readonly outlines?: readonly { readonly region: number; readonly path: string }[];
}

export interface WorldMapProjection {
  readonly map: TravelMapProjection;
  readonly realms: RealmsProjection | null;
  readonly party: { readonly x: number; readonly y: number };
  readonly destination: ExpeditionMarkProjection | null;
  readonly pin: ExpeditionMarkProjection | null;
  readonly found: readonly ExpeditionFoundProjection[];
  readonly travelling: boolean;
  readonly canTravel: boolean;
}

export type WorldMapAction =
  | { readonly action: 'world-map'; readonly open: boolean }
  | { readonly action: 'world-map-find'; readonly text: string }
  | { readonly action: 'world-map-pin' }
  | { readonly action: 'world-map-pin' | 'world-map-travel'; readonly x: number; readonly y: number; readonly region?: number; readonly index?: number }
  | { readonly action: 'expedition-places' }
  | { readonly action: 'atlas-tiles'; readonly tiles: readonly [number, number, number][] };

export function isWorldMapProjection(value: unknown): value is WorldMapProjection {
  if (typeof value !== 'object' || value === null) return false;
  const record = value as Record<string, unknown>;
  const party = record.party as Record<string, unknown> | null;
  return isTravelMap(record.map) && typeof party === 'object' && party !== null && Number.isFinite(party.x) && Number.isFinite(party.y)
    && Array.isArray(record.found) && typeof record.canTravel === 'boolean';
}

/**
 * The world map: the atlas of the Iliac Bay to browse, apart from the travel map the party crosses. It opens from the
 * game menu and from the travel map, and the world stands still while it is up. Click a place or the land to pin it,
 * or find one by name; Travel there sets the party out for the pin (or the middle of the view), on the travel map,
 * which opens if it is closed. The page reports it open or closed, as it does the local map.
 */
export function mountWorldMap(parent: HTMLElement, send: (action: WorldMapAction) => void): {
  readonly root: HTMLElement;
  open(): void;
  close(): void;
  update(value: WorldMapProjection | null): void;
  isOpen(): boolean;
  back(): void;
  page(direction: -1 | 1): void;
  pageHint(): string | null;
  initialFocus(): HTMLElement | null;
  refreshArt(): void;
  dispose(): void;
} {
  const root = document.createElement('section');
  root.className = 'dagger-world-map dagger-frame';
  root.setAttribute('role', 'dialog');
  root.setAttribute('aria-label', 'World map');
  root.hidden = true;
  const header = document.createElement('header');
  header.className = 'dagger-world-map-header';
  const title = document.createElement('h2');
  title.className = 'dagger-frame-title';
  title.textContent = 'World map';
  const status = document.createElement('p');
  status.className = 'dagger-world-map-status';
  const closeButton = button('Close');
  closeButton.dataset.escape = '';
  header.append(title, status, closeButton);

  const side = document.createElement('aside');
  side.className = 'dagger-world-map-side dagger-panel';
  const findForm = document.createElement('form');
  findForm.className = 'dagger-world-map-find';
  const findText = document.createElement('input');
  findText.type = 'search';
  findText.maxLength = 80;
  findText.placeholder = 'Find a place or region';
  findText.setAttribute('aria-label', 'Find a place');
  const findButton = document.createElement('button');
  findButton.type = 'submit';
  findButton.textContent = 'Find';
  findForm.append(findText, findButton);
  const found = document.createElement('div');
  found.className = 'dagger-world-map-found';
  found.setAttribute('role', 'group');
  found.setAttribute('aria-label', 'Places found');
  const pinHeading = document.createElement('h3');
  pinHeading.textContent = 'Pin';
  const pinLabel = document.createElement('p');
  pinLabel.className = 'dagger-world-map-pin-label';
  const setPin = button('Pin centre');
  setPin.dataset.tooltip = 'Pin the middle of the view.';
  const goToPin = button('Go to');
  goToPin.dataset.tooltip = 'Centre the map on the pin.';
  const clearPin = button('Clear');
  const pinButtons = document.createElement('div');
  pinButtons.className = 'dagger-world-map-pin';
  pinButtons.append(setPin, goToPin, clearPin);
  const travel = button('Travel there');
  travel.className = 'dagger-world-map-travel';
  // The Realms view is the page's own toggle: it shades the regions by liege from what the projection already holds.
  const realmsBox = document.createElement('div');
  realmsBox.className = 'dagger-world-map-realms';
  const realmsLabel = document.createElement('label');
  const realmsToggle = document.createElement('input');
  realmsToggle.type = 'checkbox';
  realmsLabel.append(realmsToggle, 'Realms');
  realmsLabel.dataset.tooltip = 'Shade each region by the crown it answers to.';
  const realmKey = document.createElement('ul');
  realmKey.className = 'dagger-world-map-realm-key';
  realmKey.setAttribute('aria-label', 'Realms key');
  realmKey.hidden = true;
  realmsBox.append(realmsLabel, realmKey);
  side.append(findForm, found, pinHeading, pinLabel, pinButtons, travel, realmsBox);

  let current: WorldMapProjection | null = null;
  let outlines = new Map<number, string>();
  let shown = false;
  let centred = false;
  const atlas = mountAtlas(tiles => send({ action: 'atlas-tiles', tiles }),
    (region, index) => {
      const place = atlas.place(`${region}:${index}`);
      if (place) send({ action: 'world-map-pin', x: place.x, y: place.y, region, index });
    },
    () => send({ action: 'expedition-places' }),
    { pick: (x, y) => send({ action: 'world-map-pin', x, y }) });
  const body = document.createElement('div');
  body.className = 'dagger-world-map-body';
  body.append(atlas.element, side);
  root.append(header, body);
  // Over the travel map and under every dialog: Servo has no top layer, so order alone stacks them.
  const firstDialog = parent.querySelector('dialog');
  if (firstDialog) firstDialog.before(root); else parent.append(root);

  const placeAt = (x: number, y: number): ExpeditionFoundProjection | undefined => current?.found.find(entry => entry.x === x && entry.y === y);
  const report = (open: boolean): void => {
    if (shown === open) return;
    shown = open;
    root.hidden = !open;
    if (!open) { centred = false; atlas.update(null, '', null); }
    send({ action: 'world-map', open });
  };
  findForm.addEventListener('submit', event => {
    event.preventDefault();
    send({ action: 'world-map-find', text: findText.value });
  });
  setPin.addEventListener('click', () => {
    const [x, y] = atlas.centrePixel();
    send({ action: 'world-map-pin', x, y });
  });
  goToPin.addEventListener('click', () => { if (current?.pin) atlas.centre(current.pin.x + .5, current.pin.y + .5); });
  clearPin.addEventListener('click', () => send({ action: 'world-map-pin' }));
  travel.addEventListener('click', () => {
    const pin = current?.pin;
    const [x, y] = pin ? [pin.x, pin.y] : atlas.centrePixel();
    const place = pin?.name ? placeAt(pin.x, pin.y) : undefined;
    send(place ? { action: 'world-map-travel', x, y, region: place.region, index: place.index } : { action: 'world-map-travel', x, y });
    report(false);
  });
  closeButton.addEventListener('click', () => report(false));
  const drawRealms = (state: WorldMapProjection | null): void => {
    const realms = state?.realms ?? null;
    const on = realmsToggle.checked && realms !== null;
    const regions: AtlasRealm[] = on ? realms!.regions.flatMap(region => {
      const path = outlines.get(region.region);
      return path ? [{ region: region.region, arms: region.arms, path, colour: region.colour, liege: region.liege }] : [];
    }) : [];
    atlas.realms(on ? regions : null);
    realmKey.hidden = !on;
    redraw(realmKey, JSON.stringify(on ? realms!.key : []), () => {
      realmKey.replaceChildren();
      if (!on) return;
      const line = (colour: string | null, text: string, tooltip: string | null, arms?: string): HTMLLIElement => {
        const item = document.createElement('li');
        if (arms) item.dataset.arms = arms;
        const swatch = document.createElement('span');
        swatch.className = 'dagger-world-map-realm-swatch';
        swatch.style.background = colour ?? 'transparent';
        item.append(swatch, text);
        if (tooltip) item.dataset.tooltip = tooltip;
        return item;
      };
      for (const crown of realms!.key) realmKey.append(line(crown.colour, crown.name, crown.blazon, crown.arms));
      realmKey.append(line(null, 'Answers to no crown', 'Left unshaded.'));
    });
  };
  realmsToggle.addEventListener('change', () => drawRealms(current));

  const render = (state: WorldMapProjection): void => {
    status.textContent = state.travelling ? 'Your party is out on the travel map; time stands still while you browse.'
      : 'Browse the Iliac Bay: pin a place, then travel there.';
    redraw(found, JSON.stringify(state.found), () => {
      found.replaceChildren();
      for (const place of state.found.slice(0, 8)) {
        const control = button(`${place.name} · ${place.kind}`);
        control.dataset.region = String(place.region);
        control.dataset.index = String(place.index);
        control.addEventListener('click', () => {
          atlas.centre(place.x + .5, place.y + .5);
          send({ action: 'world-map-pin', x: place.x, y: place.y, region: place.region, index: place.index });
        });
        found.append(control);
      }
    });
    pinLabel.textContent = state.pin ? state.pin.name ?? `The marked spot (${state.pin.x}, ${state.pin.y})` : 'No pin set.';
    goToPin.disabled = clearPin.disabled = state.pin === null;
    travel.disabled = !state.canTravel;
    travel.dataset.tooltip = state.pin ? 'Set out for the pin on the travel map.' : 'Set out for the middle of the view on the travel map.';
    const marks: AtlasMarks = {
      party: { x: state.party.x, y: state.party.y, label: state.travelling ? 'Your party' : 'You are here', heading: null },
      destination: state.destination ? { x: state.destination.x + .5, y: state.destination.y + .5, label: state.destination.name ?? 'Destination' } : null,
      pin: state.pin ? { x: state.pin.x + .5, y: state.pin.y + .5, label: state.pin.name ? `Pin: ${state.pin.name}` : 'Pin' } : null,
    };
    const route = state.destination ? { fromX: state.party.x, fromY: state.party.y, toX: state.destination.x, toY: state.destination.y } : null;
    atlas.update(state.map, '', route, marks);
    drawRealms(state);
    if (!centred) { centred = true; atlas.centre(state.party.x, state.party.y); }
  };

  return {
    root,
    open: () => report(true),
    close: () => report(false),
    update(value): void {
      current = value;
      // The shapes come once a map opening, whether or not the page draws that snapshot.
      if (value?.realms?.outlines) outlines = new Map(value.realms.outlines.map(outline => [outline.region, outline.path]));
      if (shown && value) render(value);
    },
    isOpen: () => shown,
    back: () => report(false),
    page(direction): void { atlas.zoom(direction); },
    pageHint: () => shown ? 'Zoom' : null,
    initialFocus: () => atlas.element,
    refreshArt(): void { atlas.refreshArt(); },
    dispose(): void { root.remove(); },
  };
}
