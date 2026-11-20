import { isTravelMap, mountAtlas, type AtlasMarks, type TravelMapProjection } from './atlas.js';
import { button } from './host.js';
import { redraw } from './redraw.js';

/** A point on the expedition map, and the place there when the ruleset names one. */
export interface ExpeditionMarkProjection { readonly x: number; readonly y: number; readonly name: string | null }

/** One of the player's vitals and its pool. */
export interface ExpeditionVitalProjection { readonly current: number; readonly maximum: number }

/** A place the last Find matched. */
export interface ExpeditionFoundProjection {
  readonly region: number; readonly index: number; readonly name: string; readonly kind: string; readonly x: number; readonly y: number;
}

export type ExpeditionMode = 'cautious' | 'reckless' | 'halt' | 'camp';

/**
 * The expedition map as the ruleset publishes it: the atlas, the party between pixels and where it heads, the pin, how
 * it goes and how fast time runs, the land, weather and hour, the ship, lodging and vitals, what Leave and Enter do,
 * a waiting encounter or a guild teleport's choice, the map's line and the last Find.
 */
export interface ExpeditionProjection {
  readonly map: TravelMapProjection;
  readonly party: { readonly x: number; readonly y: number; readonly heading: number | null };
  readonly destination: ExpeditionMarkProjection | null;
  readonly pin: ExpeditionMarkProjection | null;
  readonly mode: ExpeditionMode;
  readonly speed: number;
  readonly speeds: number;
  readonly ship: 'none' | 'hired' | 'owned';
  readonly swimming: boolean;
  readonly canBoard: boolean;
  readonly shipFare: number;
  readonly date: string;
  readonly time: string;
  readonly weather: string;
  readonly land: string;
  readonly here: string | null;
  readonly lodging: number | null;
  readonly remaining: string | null;
  readonly transport: string;
  readonly health: ExpeditionVitalProjection;
  readonly fatigue: ExpeditionVitalProjection;
  readonly magicka: ExpeditionVitalProjection;
  readonly canLeave: boolean;
  readonly canEnter: boolean;
  readonly enterLabel: string;
  readonly encounter: boolean;
  readonly teleport: boolean;
  readonly teleportPlace: string | null;
  readonly message: string | null;
  readonly found: readonly ExpeditionFoundProjection[];
}

export type ExpeditionAction =
  | { readonly action: 'expedition-open' | 'expedition-places' | 'expedition-stop' | 'expedition-ship' | 'expedition-leave' | 'expedition-enter' }
  | { readonly action: 'expedition-speed'; readonly speed: number }
  | { readonly action: 'expedition-mode'; readonly mode: ExpeditionMode }
  | { readonly action: 'expedition-go' | 'expedition-pin'; readonly x: number; readonly y: number; readonly region?: number; readonly index?: number }
  | { readonly action: 'expedition-pin' }
  | { readonly action: 'expedition-find'; readonly text: string }
  | { readonly action: 'atlas-tiles'; readonly tiles: readonly [number, number, number][] };

export function isExpeditionProjection(value: unknown): value is ExpeditionProjection {
  if (typeof value !== 'object' || value === null) return false;
  const record = value as Record<string, unknown>;
  const party = record.party as Record<string, unknown> | null;
  return isTravelMap(record.map) && typeof party === 'object' && party !== null && Number.isFinite(party.x) && Number.isFinite(party.y)
    && typeof record.mode === 'string' && Number.isInteger(record.speed) && Number.isInteger(record.speeds)
    && typeof record.enterLabel === 'string' && Array.isArray(record.found);
}

const MODES: readonly { readonly mode: ExpeditionMode; readonly label: string; readonly tooltip: string }[] = [
  { mode: 'cautious', label: 'Cautiously', tooltip: 'Travel at a steady pace.' },
  { mode: 'reckless', label: 'Recklessly', tooltip: 'Travel faster, tiring twice as quickly.' },
  { mode: 'halt', label: 'Halt', tooltip: 'Stop by the way while time passes.' },
  { mode: 'camp', label: 'Camp', tooltip: 'Make camp and recover, or take lodging in a settlement.' },
];

const SPEEDS = ['Pause', 'Slow', 'Fast', 'Fastest'];

const TRANSPORT: Readonly<Record<string, string>> = { foot: 'on foot', horse: 'on horseback', cart: 'with the cart' };

const WEATHER: Readonly<Record<string, string>> = {
  sunny: 'Clear', cloudy: 'Cloudy', overcast: 'Overcast', fog: 'Fog', rain: 'Rain', thunder: 'Thunderstorm', snow: 'Snow',
};

/**
 * The expedition map: the party crosses the atlas while time runs. Click the land to set out, or choose a place; the
 * top bar shows the hour and weather and sets the speed, the side panel finds and pins places and boards ships, and the
 * bottom bar chooses how the party goes, shows its vitals and leaves the map, on foot from where it stands. The camera
 * follows the party until the view is moved by hand; I'm at follows it again.
 */
export function mountExpedition(parent: HTMLElement, claim: (action: ExpeditionAction) => void): {
  readonly root: HTMLElement;
  update(value: ExpeditionProjection | null): void;
  isOpen(): boolean;
  /** Escape or Back: leave when the party still stands with the player, else nothing. */
  back(): void;
  /** The triggers or Page Up and Down zoom the map. */
  page(direction: -1 | 1): void;
  pageHint(): string | null;
  initialFocus(): HTMLElement | null;
  refreshArt(): void;
  dispose(): void;
} {
  const root = document.createElement('section');
  root.className = 'dagger-expedition dagger-frame';
  root.setAttribute('role', 'dialog');
  root.setAttribute('aria-label', 'Travel map');
  root.hidden = true;

  // The top bar: the hour and weather, and how fast time runs.
  const top = document.createElement('header');
  top.className = 'dagger-expedition-top dagger-panel';
  const clock = document.createElement('p');
  clock.className = 'dagger-expedition-clock';
  const speeds = document.createElement('div');
  speeds.className = 'dagger-expedition-speeds';
  speeds.setAttribute('role', 'group');
  speeds.setAttribute('aria-label', 'Time');
  top.append(clock, speeds);

  // The side panel: Find, I'm at, the ship and the pin.
  const side = document.createElement('aside');
  side.className = 'dagger-expedition-side dagger-panel';
  const findForm = document.createElement('form');
  findForm.className = 'dagger-expedition-find';
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
  found.className = 'dagger-expedition-found';
  found.setAttribute('role', 'group');
  found.setAttribute('aria-label', 'Places found');
  const imAt = button("I'm at");
  imAt.dataset.tooltip = 'Centre on the party and follow it.';
  const centreGo = button('Go to centre');
  centreGo.dataset.tooltip = 'Set out for the middle of the map, where the view is centred.';
  const ship = button('Board ship');
  const pinHeading = document.createElement('h3');
  pinHeading.textContent = 'Pin';
  const pinLabel = document.createElement('p');
  pinLabel.className = 'dagger-expedition-pin-label';
  const setPin = button('Set pin');
  setPin.dataset.tooltip = 'Pin where the party heads, or the middle of the map.';
  const goToPin = button('Go to');
  goToPin.dataset.tooltip = 'Centre the map on the pin.';
  const travelToPin = button('Travel');
  travelToPin.dataset.tooltip = 'Set out for the pin.';
  const clearPin = button('Clear');
  const pinButtons = document.createElement('div');
  pinButtons.className = 'dagger-expedition-pin';
  pinButtons.append(setPin, goToPin, travelToPin, clearPin);
  side.append(findForm, found, imAt, centreGo, ship, pinHeading, pinLabel, pinButtons);

  // The bottom bar: how the party goes, its vitals, the line, and leaving.
  const bottom = document.createElement('footer');
  bottom.className = 'dagger-expedition-bottom dagger-panel';
  const modes = document.createElement('div');
  modes.className = 'dagger-expedition-modes';
  modes.setAttribute('role', 'group');
  modes.setAttribute('aria-label', 'How the party goes');
  const vitals = document.createElement('div');
  vitals.className = 'dagger-expedition-vitals';
  const status = document.createElement('div');
  status.className = 'dagger-expedition-status';
  const journey = document.createElement('p');
  journey.className = 'dagger-expedition-journey';
  const message = document.createElement('p');
  message.className = 'dagger-expedition-message';
  message.setAttribute('role', 'status');
  message.setAttribute('aria-live', 'polite');
  status.append(journey, message);
  const exits = document.createElement('div');
  exits.className = 'dagger-expedition-exits';
  const leave = button('Close map');
  leave.dataset.escape = '';
  const enter = button('Enter');
  enter.className = 'dagger-expedition-enter';
  exits.append(leave, enter);
  bottom.append(modes, vitals, status, exits);

  let current: ExpeditionProjection | null = null;
  let following = true;
  const atlas = mountAtlas(tiles => claim({ action: 'atlas-tiles', tiles }),
    (region, index) => {
      const place = atlas.place(`${region}:${index}`);
      if (place) claim({ action: 'expedition-go', x: place.x, y: place.y, region, index });
    },
    () => claim({ action: 'expedition-places' }),
    { pick: (x, y) => claim({ action: 'expedition-go', x, y }), panned: () => { following = false; } });
  const body = document.createElement('div');
  body.className = 'dagger-expedition-body';
  body.append(atlas.element, side);
  root.append(top, body, bottom);
  parent.append(root);

  findForm.addEventListener('submit', event => {
    event.preventDefault();
    claim({ action: 'expedition-find', text: findText.value });
  });
  imAt.addEventListener('click', () => {
    following = true;
    if (current) atlas.centre(current.party.x, current.party.y);
  });
  centreGo.addEventListener('click', () => {
    const [x, y] = atlas.centrePixel();
    claim({ action: 'expedition-go', x, y });
  });
  ship.addEventListener('click', () => claim({ action: 'expedition-ship' }));
  setPin.addEventListener('click', () => {
    const destination = current?.destination;
    const [x, y] = destination ? [destination.x, destination.y] : atlas.centrePixel();
    claim({ action: 'expedition-pin', x, y });
  });
  goToPin.addEventListener('click', () => {
    if (!current?.pin) return;
    following = false;
    atlas.centre(current.pin.x + .5, current.pin.y + .5);
  });
  travelToPin.addEventListener('click', () => {
    const pin = current?.pin;
    if (!pin) return;
    const place = current?.pin?.name ? current.found.find(entry => entry.x === pin.x && entry.y === pin.y) : undefined;
    claim(place ? { action: 'expedition-go', x: pin.x, y: pin.y, region: place.region, index: place.index }
      : { action: 'expedition-go', x: pin.x, y: pin.y });
  });
  clearPin.addEventListener('click', () => claim({ action: 'expedition-pin' }));
  leave.addEventListener('click', () => claim({ action: 'expedition-leave' }));
  enter.addEventListener('click', () => claim({ action: 'expedition-enter' }));

  const vital = (name: string, value: ExpeditionVitalProjection): HTMLElement => {
    const bar = document.createElement('div');
    bar.className = 'dagger-expedition-vital';
    bar.dataset.vital = name.toLowerCase();
    bar.setAttribute('role', 'meter');
    bar.setAttribute('aria-label', name);
    bar.setAttribute('aria-valuemin', '0');
    bar.setAttribute('aria-valuemax', String(Math.round(value.maximum)));
    bar.setAttribute('aria-valuenow', String(Math.round(value.current)));
    bar.dataset.tooltip = `${name}: ${Math.round(value.current)} of ${Math.round(value.maximum)}`;
    const fill = document.createElement('span');
    fill.style.height = `${value.maximum > 0 ? Math.max(0, Math.min(100, value.current / value.maximum * 100)) : 0}%`;
    bar.append(fill);
    return bar;
  };

  const journeyLine = (view: ExpeditionProjection): string => {
    if (view.teleport) return view.teleportPlace ? `The guild will teleport you to ${view.teleportPlace}.` : 'Choose a place you know for the guild to teleport you.';
    const where = view.here ? `At ${view.here}` : view.swimming ? 'In the water' : `In the ${view.land.toLowerCase()}`;
    const aboard = view.ship === 'owned' ? ', aboard your ship' : view.ship === 'hired' ? ', aboard a hired ship'
      : view.swimming ? '' : `, ${TRANSPORT[view.transport] ?? 'on foot'}`;
    const going = view.destination === null ? ''
      : view.mode === 'halt' ? ' · halted' : view.mode === 'camp' ? '' : ` · heading for ${view.destination.name ?? 'the marked spot'}`
        + (view.remaining ? `, about ${view.remaining} away` : '');
    const camp = view.mode === 'camp' ? view.lodging !== null ? ` · lodging, ${view.lodging} gold a night` : ' · in camp' : '';
    return `${where}${aboard}${going}${camp}.`;
  };

  const render = (view: ExpeditionProjection): void => {
    clock.textContent = `${view.time} on ${view.date} · ${WEATHER[view.weather] ?? view.weather}`;
    redraw(speeds, JSON.stringify([view.speed, view.speeds, view.teleport, view.encounter]), () => {
      speeds.replaceChildren();
      for (let speed = 0; speed <= view.speeds; speed++) {
        const control = button(SPEEDS[speed] ?? `Speed ${speed}`);
        control.dataset.speed = String(speed);
        control.setAttribute('aria-pressed', String(view.speed === speed));
        control.disabled = view.teleport || (view.encounter && speed > 0);
        control.addEventListener('click', () => claim({ action: 'expedition-speed', speed }));
        speeds.append(control);
      }
    });
    redraw(modes, JSON.stringify([view.mode, view.lodging, view.swimming, view.teleport, view.encounter]), () => {
      modes.replaceChildren();
      for (const entry of MODES) {
        const control = button(entry.mode === 'camp' && view.lodging !== null ? `Camp · ${view.lodging} gold` : entry.label);
        control.dataset.mode = entry.mode;
        control.dataset.tooltip = entry.tooltip;
        control.setAttribute('aria-pressed', String(view.mode === entry.mode));
        control.disabled = view.teleport || view.encounter || (entry.mode === 'camp' && view.swimming);
        control.addEventListener('click', () => claim({ action: 'expedition-mode', mode: entry.mode }));
        modes.append(control);
      }
    });
    redraw(vitals, JSON.stringify([view.health, view.fatigue, view.magicka].map(value => [Math.round(value.current), Math.round(value.maximum)])), () =>
      vitals.replaceChildren(vital('Health', view.health), vital('Fatigue', view.fatigue), vital('Magicka', view.magicka)));
    redraw(found, JSON.stringify(view.found), () => {
      found.replaceChildren();
      for (const place of view.found.slice(0, 8)) {
        const control = button(`${place.name} · ${place.kind}`);
        control.dataset.region = String(place.region);
        control.dataset.index = String(place.index);
        control.addEventListener('click', () => {
          following = false;
          atlas.centre(place.x + .5, place.y + .5);
          claim({ action: 'expedition-pin', x: place.x, y: place.y, region: place.region, index: place.index });
        });
        found.append(control);
      }
    });
    journey.textContent = journeyLine(view);
    message.textContent = view.message ?? '';
    root.dataset.encounter = String(view.encounter);
    ship.textContent = view.ship === 'none' ? view.shipFare > 0 ? `Board ship · ${view.shipFare} gold` : 'Board ship' : 'Go ashore';
    ship.disabled = view.teleport || view.encounter || (view.ship === 'none' && !view.canBoard);
    ship.dataset.tooltip = view.ship === 'none' ? 'Board a ship at a port near the sea.' : 'Leave the ship where it has landed.';
    pinLabel.textContent = view.pin ? view.pin.name ?? `The marked spot (${view.pin.x}, ${view.pin.y})` : 'No pin set.';
    goToPin.disabled = travelToPin.disabled = clearPin.disabled = view.pin === null;
    travelToPin.disabled ||= view.teleport || view.encounter;
    centreGo.disabled = view.encounter;
    leave.textContent = view.teleport ? 'Cancel' : 'Close map';
    leave.hidden = !view.canLeave;
    enter.textContent = view.enterLabel;
    enter.disabled = !view.canEnter;
    const marks: AtlasMarks = {
      party: { x: view.party.x, y: view.party.y, label: 'Your party', heading: view.party.heading },
      destination: view.destination ? { x: view.destination.x + .5, y: view.destination.y + .5, label: view.destination.name ?? 'Destination' } : null,
      pin: view.pin ? { x: view.pin.x + .5, y: view.pin.y + .5, label: view.pin.name ? `Pin: ${view.pin.name}` : 'Pin' } : null,
    };
    const route = view.destination ? { fromX: view.party.x, fromY: view.party.y, toX: view.destination.x, toY: view.destination.y } : null;
    atlas.update(view.map, '', route, marks);
    if (following) atlas.follow(view.party.x, view.party.y);
  };

  return {
    root,
    update(value): void {
      const opened = current === null && value !== null;
      current = value;
      root.hidden = value === null;
      if (value === null) { atlas.update(null, '', null); following = true; return; }
      if (opened) following = true;
      render(value);
    },
    isOpen: () => current !== null,
    back(): void { if (current?.canLeave) claim({ action: 'expedition-leave' }); },
    page(direction): void { atlas.zoom(direction); },
    pageHint: () => current ? 'Zoom' : null,
    initialFocus: () => current?.canEnter && !current.canLeave ? enter : atlas.element,
    refreshArt(): void { atlas.refreshArt(); },
    dispose(): void { root.remove(); },
  };
}
