import { button } from './host.js';
import { BACK_EVENT, DIRECTION_EVENT, type Direction } from './navigation.js';
import { redraw } from './redraw.js';

/** The name the page anchors the travel map's view under, where the ruleset's map camera draws. */
export const TRAVEL_MAP_ANCHOR = 'travel-map';

/** A point on the expedition map, and the place there when the ruleset names one. */
export interface ExpeditionMarkProjection { readonly x: number; readonly y: number; readonly name: string | null }

/** One of the player's vitals and its pool. */
export interface ExpeditionVitalProjection { readonly current: number; readonly maximum: number }

/** A place the last Find matched. */
export interface ExpeditionFoundProjection {
  readonly region: number; readonly index: number; readonly name: string; readonly kind: string; readonly x: number; readonly y: number;
}

/** A band within the party's sight: what it is and how many, where it stands, its category/faction/clash inspection metadata, and whether it camps or has seen the party. */
export interface ExpeditionBandProjection {
  readonly id?: number;
  readonly name: string;
  readonly kind: string;
  readonly x: number;
  readonly y: number;
  readonly camp: boolean;
  readonly spotted: boolean;
  readonly category?: string;
  readonly faction?: string | null;
  readonly clashSides?: string | null;
  readonly origin?: string | null;
  readonly destination?: string | null;
  readonly detail?: string | null;
  readonly isClash?: boolean;
  readonly hostile?: boolean;
}

/** A name over the travel map, where the view puts it: across and down the view from its top left, 0 to 1. */
/** An encounter the party must enter to meet: who, and what Enter does. */
export interface ExpeditionEncounterProjection { readonly title: string; readonly line: string }

export interface ExpeditionLabelProjection {
  readonly name: string;
  readonly kind: string;
  readonly across: number;
  readonly down: number;
  readonly opacity?: number;
}

export type ExpeditionMode = 'cautious' | 'reckless' | 'halt' | 'camp';

/**
 * The expedition's travel map as the ruleset publishes it about the world the Engine draws: the names over it and the
 * way its view faces, where the party heads, the pin, how it goes and how fast time runs, the land, weather and hour,
 * the ship, lodging and vitals, what Leave and Enter do, a waiting encounter or a guild teleport's choice, the map's
 * line and the last Find, and the bands within the party's sight, which the map draws and the panel lists.
 */
export interface ExpeditionProjection {
  readonly labels: readonly ExpeditionLabelProjection[];
  readonly view: { readonly heading: number; readonly following: boolean };
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
  readonly encounter: ExpeditionEncounterProjection | null;
  readonly teleport: boolean;
  readonly teleportPlace: string | null;
  readonly message: string | null;
  readonly found: readonly ExpeditionFoundProjection[];
  readonly bands: readonly ExpeditionBandProjection[];
}

export type ExpeditionAction =
  | { readonly action: 'expedition-open' | 'expedition-stop' | 'expedition-ship' | 'expedition-leave' | 'expedition-enter' | 'expedition-follow' }
  | { readonly action: 'expedition-speed'; readonly speed: number }
  | { readonly action: 'expedition-mode'; readonly mode: ExpeditionMode }
  | { readonly action: 'expedition-go' | 'expedition-pin'; readonly x: number; readonly y: number; readonly region?: number; readonly index?: number }
  | { readonly action: 'expedition-pin' }
  | { readonly action: 'expedition-find'; readonly text: string }
  | { readonly action: 'expedition-look'; readonly x: number; readonly y: number }
  | { readonly action: 'expedition-go-at' | 'expedition-pin-at'; readonly point: readonly [number, number] }
  /** Moves the view: [turn degrees clockwise, tilt degrees steeper, right and forward in eye distances, zoom steps nearer]. */
  | { readonly action: 'expedition-view'; readonly view: readonly [number, number, number, number, number] };

export function isExpeditionProjection(value: unknown): value is ExpeditionProjection {
  if (typeof value !== 'object' || value === null) return false;
  const record = value as Record<string, unknown>;
  const view = record.view as Record<string, unknown> | null;
  return Array.isArray(record.labels) && typeof view === 'object' && view !== null && Number.isFinite(view.heading)
    && typeof record.mode === 'string' && Number.isInteger(record.speed) && Number.isInteger(record.speeds)
    && typeof record.enterLabel === 'string' && Array.isArray(record.found) && Array.isArray(record.bands);
}

const MODES: readonly { readonly mode: ExpeditionMode; readonly label: string; readonly tooltip: string }[] = [
  { mode: 'cautious', label: 'Cautiously', tooltip: 'Travel at a steady pace.' },
  { mode: 'reckless', label: 'Recklessly', tooltip: 'Travel faster, tiring twice as quickly.' },
  { mode: 'halt', label: 'Halt', tooltip: 'Stop by the way and wait, neither tiring nor recovering.' },
  { mode: 'camp', label: 'Camp', tooltip: 'Make camp and recover, or take lodging in a settlement.' },
];

const SPEEDS = ['Pause', 'Slow', 'Fast', 'Fastest'];

const TRANSPORT: Readonly<Record<string, string>> = { foot: 'on foot', horse: 'on horseback', cart: 'with the cart' };

const WEATHER: Readonly<Record<string, string>> = {
  sunny: 'Clear', cloudy: 'Cloudy', overcast: 'Overcast', fog: 'Fog', rain: 'Rain', thunder: 'Thunderstorm', snow: 'Snow',
};

/** How far one step of the keys or the pad moves the view: a quarter of an eye distance, fifteen degrees, five of tilt. */
const PAN_STEP = .25, TURN_STEP = 15, TILT_STEP = 5;
/** How far a drag across the whole view moves it: the land under the pointer stays about under it. */
const DRAG_PAN = .9, DRAG_TURN = 180, DRAG_TILT = 60;
/** A press that moves less than this many pixels is a click. */
const CLICK_SLOP = 5;

/**
 * The expedition's travel map: the party crosses the land the Engine draws while time runs. The ruleset's map camera
 * draws into the view between the bars; the page only says where the view is and sends what the pointer, the keys and
 * the pad ask of it. A click sets out for the land under the pointer (Alt or the secondary button pins it), a drag
 * pans the land, a secondary drag turns and tilts it and the wheel zooms; the view's own buttons turn, tilt and zoom,
 * and, focused, the keys pan, turn, tilt and zoom it, or accepting it lets the pad's directions pan it. The top bar
 * shows the hour and weather and sets the speed, the side panel finds and pins places, opens the world map and boards
 * ships, and the bottom bar chooses how the party goes, shows its vitals and leaves the map. The view follows the
 * party until it is moved by hand; I'm at follows it again.
 */
export function mountExpedition(parent: HTMLElement, claim: (action: ExpeditionAction) => void, openMenu: () => void,
  anchor: (name: string, element: Element) => () => void, openWorldMap: () => void): {
  readonly root: HTMLElement;
  update(value: ExpeditionProjection | null): void;
  isOpen(): boolean;
  /** Escape or Back: leave when the party still stands with the player, else the game menu (to save, say). */
  back(): void;
  /** The triggers or Page Up and Down zoom the map. */
  page(direction: -1 | 1): void;
  pageHint(): string | null;
  initialFocus(): HTMLElement | null;
  /** The centred panel a waiting encounter raises over the map, its own navigation surface. */
  readonly encounter: { readonly root: HTMLElement; isOpen(): boolean; initialFocus(): HTMLElement };
  dispose(): void;
} {
  const root = document.createElement('section');
  root.className = 'dagger-expedition';
  root.setAttribute('role', 'dialog');
  root.setAttribute('aria-label', 'Travel map');
  root.hidden = true;

  // The top bar: the hour and weather, and how fast time runs.
  const top = document.createElement('header');
  top.className = 'dagger-expedition-top dagger-frame';
  const clock = document.createElement('p');
  clock.className = 'dagger-expedition-clock';
  const speeds = document.createElement('div');
  speeds.className = 'dagger-expedition-speeds';
  speeds.setAttribute('role', 'group');
  speeds.setAttribute('aria-label', 'Time');
  top.append(clock, speeds);

  // The view: where the map camera draws, the names over the land, and the view's own controls.
  const view = document.createElement('div');
  view.className = 'dagger-expedition-view';
  view.tabIndex = 0;
  view.setAttribute('role', 'application');
  const labels = document.createElement('div');
  labels.className = 'dagger-expedition-labels';
  labels.setAttribute('aria-hidden', 'true');
  const crosshair = document.createElement('div');
  crosshair.className = 'dagger-expedition-crosshair';
  crosshair.setAttribute('aria-hidden', 'true');
  const hint = document.createElement('p');
  hint.className = 'dagger-expedition-hint';
  const controls = document.createElement('div');
  controls.className = 'dagger-expedition-view-controls';
  controls.setAttribute('role', 'group');
  controls.setAttribute('aria-label', 'View');
  const viewButton = (text: string, label: string, move: readonly [number, number, number, number, number]): HTMLButtonElement => {
    const control = button(text);
    control.setAttribute('aria-label', label);
    control.dataset.tooltip = label;
    control.addEventListener('click', () => claim({ action: 'expedition-view', view: move }));
    return control;
  };
  const compass = document.createElement('span');
  compass.className = 'dagger-expedition-compass';
  compass.setAttribute('aria-hidden', 'true');
  compass.textContent = 'N';
  controls.append(viewButton('⟲', 'Turn the view left', [-TURN_STEP, 0, 0, 0, 0]), compass, viewButton('⟳', 'Turn the view right', [TURN_STEP, 0, 0, 0, 0]),
    viewButton('▲', 'Look down more steeply', [0, TILT_STEP, 0, 0, 0]), viewButton('▼', 'Look out more level', [0, -TILT_STEP, 0, 0, 0]),
    viewButton('+', 'Zoom in', [0, 0, 0, 0, 1]), viewButton('−', 'Zoom out', [0, 0, 0, 0, -1]));
  view.append(labels, crosshair, controls, hint);

  // The side panel: Find, I'm at, the world map, the ship and the pin.
  const side = document.createElement('aside');
  side.className = 'dagger-expedition-side dagger-frame';
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
  const worldMap = button('World map');
  worldMap.dataset.tooltip = 'Browse the atlas of the Iliac Bay, find and pin places, and travel to them.';
  const centreGo = button('Go to centre');
  centreGo.dataset.tooltip = 'Set out for the land under the middle of the view.';
  const ship = button('Board ship');
  const pinHeading = document.createElement('h3');
  pinHeading.textContent = 'Pin';
  const pinLabel = document.createElement('p');
  pinLabel.className = 'dagger-expedition-pin-label';
  const setPin = button('Set pin');
  setPin.dataset.tooltip = 'Pin where the party heads, or the land under the middle of the view.';
  const goToPin = button('Go to');
  goToPin.dataset.tooltip = 'Look at the pin.';
  const travelToPin = button('Travel');
  travelToPin.dataset.tooltip = 'Set out for the pin.';
  const clearPin = button('Clear');
  const pinButtons = document.createElement('div');
  pinButtons.className = 'dagger-expedition-pin';
  pinButtons.append(setPin, goToPin, travelToPin, clearPin);
  const sightHeading = document.createElement('h3');
  sightHeading.textContent = 'In sight';
  const inSight = document.createElement('div');
  inSight.className = 'dagger-expedition-in-sight';
  inSight.setAttribute('role', 'group');
  inSight.setAttribute('aria-label', 'Bands in sight');
  const bandDetail = document.createElement('div');
  bandDetail.className = 'dagger-expedition-band-detail dagger-panel';
  bandDetail.setAttribute('aria-live', 'polite');
  bandDetail.hidden = true;
  side.append(findForm, found, imAt, worldMap, centreGo, ship, pinHeading, pinLabel, pinButtons, sightHeading, inSight, bandDetail);

  // The bottom bar: how the party goes, its vitals, the line, and leaving.
  const bottom = document.createElement('footer');
  bottom.className = 'dagger-expedition-bottom dagger-frame';
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
  // The game menu over the map: its saves, settings and the rest, while the map holds the world.
  const menuButton = button('Menu');
  const leave = button('Close map');
  leave.dataset.escape = '';
  const enter = button('Enter');
  enter.className = 'dagger-expedition-enter';
  exits.append(menuButton, leave, enter);
  bottom.append(modes, vitals, status, exits);

  // A waiting encounter must be entered: a centred panel over the dimmed map names it, its Enter focused. The bottom
  // bar's Enter stays for a band come upon unseen, which the party may also pass by.
  const encounterPanel = document.createElement('div');
  encounterPanel.className = 'dagger-expedition-encounter';
  encounterPanel.hidden = true;
  const encounterFrame = document.createElement('div');
  encounterFrame.className = 'dagger-frame dagger-expedition-encounter-frame';
  encounterFrame.setAttribute('role', 'alertdialog');
  encounterFrame.setAttribute('aria-modal', 'true');
  const encounterTitle = document.createElement('h2');
  encounterTitle.className = 'dagger-frame-title';
  encounterTitle.id = 'dagger-expedition-encounter-title';
  encounterFrame.setAttribute('aria-labelledby', encounterTitle.id);
  const encounterLine = document.createElement('p');
  encounterLine.className = 'dagger-expedition-encounter-line';
  const encounterEnter = button('Enter');
  encounterEnter.className = 'dagger-expedition-encounter-enter';
  encounterFrame.append(encounterTitle, encounterLine, encounterEnter);
  encounterPanel.append(encounterFrame);

  let current: ExpeditionProjection | null = null;
  let inspectedBandKey: string | null = null;
  const bandKey = (band: ExpeditionBandProjection): string =>
    band.id !== undefined ? `id:${band.id}` : `${band.kind}:${band.name}:${band.origin ?? ''}:${band.destination ?? ''}:${band.detail ?? ''}`;
  const bandRouteLine = (band: ExpeditionBandProjection): string | null => {
    if (band.origin && band.destination) return `Route: ${band.origin} to ${band.destination}`;
    if (band.destination) return `Bound for: ${band.destination}`;
    if (band.origin) return `From: ${band.origin}`;
    return null;
  };
  const bandTooltip = (band: ExpeditionBandProjection): string => {
    const parts: string[] = [];
    if (band.category) parts.push(band.category);
    if (band.faction) parts.push(band.faction);
    if (band.clashSides) parts.push(`Clash: ${band.clashSides}`);
    const route = bandRouteLine(band);
    if (route) parts.push(route);
    if (band.detail) parts.push(band.detail);
    parts.push('Look at them.');
    return parts.join(' · ');
  };
  const renderBandDetail = (band: ExpeditionBandProjection | null): void => {
    bandDetail.hidden = band === null;
    if (!band) { bandDetail.replaceChildren(); return; }
    const header = document.createElement('p');
    header.className = 'dagger-expedition-band-detail-title';
    if (band.category) {
      const badge = document.createElement('span');
      badge.className = 'dagger-expedition-band-badge';
      badge.dataset.kind = band.kind;
      badge.textContent = band.category;
      header.append(badge, ' ');
    }
    header.append(band.name);
    const lines: HTMLElement[] = [header];
    const addLine = (cls: string, text: string | null | undefined): void => {
      if (!text) return;
      const p = document.createElement('p');
      p.className = cls;
      p.textContent = text;
      lines.push(p);
    };
    const statusText = band.isClash ? 'Active clash' : band.spotted ? 'Giving chase' : band.camp ? 'Camped' : band.hostile ? 'Hostile' : 'On the road';
    addLine('dagger-expedition-band-detail-meta', band.faction ? `${band.faction} · ${statusText}` : statusText);
    if (band.clashSides) addLine('dagger-expedition-band-detail-sides', `Sides: ${band.clashSides}`);
    addLine('dagger-expedition-band-detail-route', bandRouteLine(band));
    addLine('dagger-expedition-band-detail-note', band.detail);
    bandDetail.replaceChildren(...lines);
  };
  const body = document.createElement('div');
  body.className = 'dagger-expedition-body';
  body.append(view, side);
  root.append(top, body, bottom, encounterPanel);
  // Over the HUD and under every dialog (the menu, a quest's question, a confirmation): the window's Servo has no top
  // layer, so a dialog stays above the map only by coming after it.
  const firstDialog = parent.querySelector('dialog');
  if (firstDialog) firstDialog.before(root); else parent.append(root);
  // The map camera draws where the view stands on the page, following it on every layout.
  const unanchor = anchor(TRAVEL_MAP_ANCHOR, view);

  const move = (turn: number, tilt: number, right: number, forward: number, zoom: number): void => {
    const clamp = (value: number, limit: number): number => Math.max(-limit, Math.min(limit, value));
    claim({ action: 'expedition-view', view: [clamp(turn, 180), clamp(tilt, 90), clamp(right, 4), clamp(forward, 4), clamp(zoom, 8)] });
  };
  /** A point of the view, across and down from its top left, 0 to 1. */
  const pointOf = (event: { readonly clientX: number; readonly clientY: number }): [number, number] => {
    const box = view.getBoundingClientRect();
    const fit = (value: number): number => Math.max(0, Math.min(1, value));
    return [fit(box.width > 0 ? (event.clientX - box.left) / box.width : .5), fit(box.height > 0 ? (event.clientY - box.top) / box.height : .5)];
  };

  // The pointer: a click sets out or pins, a drag pans, a secondary drag turns and tilts, the wheel zooms.
  let press: { x: number; y: number; button: number; moved: boolean; pointer: number } | null = null;
  view.addEventListener('pointerdown', event => {
    if ((event.target as HTMLElement).closest('button')) return;
    press = { x: event.clientX, y: event.clientY, button: event.button, moved: false, pointer: event.pointerId };
    view.setPointerCapture?.(event.pointerId);
    event.preventDefault();
  });
  view.addEventListener('pointermove', event => {
    if (!press) return;
    const dx = event.clientX - press.x, dy = event.clientY - press.y;
    if (!press.moved && Math.hypot(dx, dy) < CLICK_SLOP) return;
    press.moved = true;
    const box = view.getBoundingClientRect();
    const width = Math.max(box.width, 1), height = Math.max(box.height, 1);
    if (press.button === 2) move(dx / width * DRAG_TURN, dy / height * DRAG_TILT, 0, 0, 0);
    else move(0, 0, -dx / height * DRAG_PAN, dy / height * DRAG_PAN, 0);
    press.x = event.clientX;
    press.y = event.clientY;
  });
  const endPress = (event: PointerEvent): void => {
    const ended = press;
    press = null;
    if (!ended || ended.moved || event.type === 'pointercancel') return;
    if (ended.button === 2 || event.altKey) claim({ action: 'expedition-pin-at', point: pointOf(event) });
    else if (ended.button === 0) claim({ action: 'expedition-go-at', point: pointOf(event) });
  };
  view.addEventListener('pointerup', endPress);
  view.addEventListener('pointercancel', endPress);
  view.addEventListener('contextmenu', event => event.preventDefault());
  view.addEventListener('wheel', event => {
    event.preventDefault();
    move(0, 0, 0, 0, event.deltaY < 0 ? 1 : -1);
  }, { passive: false });

  // The keys and the pad, on the focused view: WASD pan, Q and E turn, R and F tilt, + and - zoom. Accepting it lets the
  // arrows and the pad's directions pan it, until accept or Back; otherwise they move focus, as everywhere.
  let panning = false;
  const setPanning = (on: boolean): void => {
    panning = on;
    view.classList.toggle('is-panning', on);
    view.setAttribute('aria-pressed', String(on));
    view.setAttribute('aria-label', on ? 'Travel map, moving the view' : 'Travel map');
    hint.textContent = on ? 'Directions pan · triggers or Page Up and Down zoom · accept or Back to stop'
      : 'Click to set out · Alt or right click to pin · drag to pan · right drag to turn · wheel to zoom';
  };
  setPanning(false);
  const pan = (direction: Direction): void => {
    const [right, forward] = direction === 'left' ? [-PAN_STEP, 0] : direction === 'right' ? [PAN_STEP, 0] : direction === 'up' ? [0, PAN_STEP] : [0, -PAN_STEP];
    move(0, 0, right, forward, 0);
  };
  view.addEventListener(DIRECTION_EVENT, event => {
    if (!panning || document.activeElement !== view) return;
    event.preventDefault();
    pan((event as CustomEvent<Direction>).detail);
  });
  view.addEventListener(BACK_EVENT, event => {
    if (!panning || document.activeElement !== view) return;
    event.preventDefault();
    setPanning(false);
  });
  // A controller's accept clicks the focused control; a mouse click on the view is a choice of land, not a request to pan.
  view.addEventListener('click', event => { if (event.target === view && event.detail === 0) setPanning(!panning); });
  const KEYS: Readonly<Record<string, () => void>> = {
    KeyW: () => pan('up'), KeyS: () => pan('down'), KeyA: () => pan('left'), KeyD: () => pan('right'),
    KeyQ: () => move(-TURN_STEP, 0, 0, 0, 0), KeyE: () => move(TURN_STEP, 0, 0, 0, 0),
    KeyR: () => move(0, TILT_STEP, 0, 0, 0), KeyF: () => move(0, -TILT_STEP, 0, 0, 0),
    Equal: () => move(0, 0, 0, 0, 1), NumpadAdd: () => move(0, 0, 0, 0, 1), Minus: () => move(0, 0, 0, 0, -1), NumpadSubtract: () => move(0, 0, 0, 0, -1),
  };
  view.addEventListener('keydown', event => {
    if (event.target !== view) return;
    if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); setPanning(!panning); return; }
    const key = KEYS[event.code];
    if (key) { event.preventDefault(); key(); }
  });
  view.addEventListener('blur', () => setPanning(false));

  findForm.addEventListener('submit', event => {
    event.preventDefault();
    claim({ action: 'expedition-find', text: findText.value });
  });
  imAt.addEventListener('click', () => claim({ action: 'expedition-follow' }));
  worldMap.addEventListener('click', openWorldMap);
  centreGo.addEventListener('click', () => claim({ action: 'expedition-go-at', point: [.5, .5] }));
  ship.addEventListener('click', () => claim({ action: 'expedition-ship' }));
  setPin.addEventListener('click', () => {
    const destination = current?.destination;
    claim(destination ? { action: 'expedition-pin', x: destination.x, y: destination.y } : { action: 'expedition-pin-at', point: [.5, .5] });
  });
  goToPin.addEventListener('click', () => {
    if (current?.pin) claim({ action: 'expedition-look', x: current.pin.x, y: current.pin.y });
  });
  travelToPin.addEventListener('click', () => {
    const pin = current?.pin;
    if (!pin) return;
    const place = current?.pin?.name ? current.found.find(entry => entry.x === pin.x && entry.y === pin.y) : undefined;
    claim(place ? { action: 'expedition-go', x: pin.x, y: pin.y, region: place.region, index: place.index }
      : { action: 'expedition-go', x: pin.x, y: pin.y });
  });
  clearPin.addEventListener('click', () => claim({ action: 'expedition-pin' }));
  menuButton.addEventListener('click', openMenu);
  leave.addEventListener('click', () => claim({ action: 'expedition-leave' }));
  enter.addEventListener('click', () => claim({ action: 'expedition-enter' }));
  encounterEnter.addEventListener('click', () => claim({ action: 'expedition-enter' }));

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

  const journeyLine = (state: ExpeditionProjection): string => {
    if (state.teleport) return state.teleportPlace ? `The guild will teleport you to ${state.teleportPlace}.` : 'Choose a place you know for the guild to teleport you.';
    const where = state.here ? `At ${state.here}` : state.swimming ? 'In the water' : `In the ${state.land.toLowerCase()}`;
    const aboard = state.ship === 'owned' ? ', aboard your ship' : state.ship === 'hired' ? ', aboard a hired ship'
      : state.swimming ? '' : `, ${TRANSPORT[state.transport] ?? 'on foot'}`;
    const going = state.destination === null ? ''
      : state.mode === 'halt' ? ' · halted' : state.mode === 'camp' ? '' : ` · heading for ${state.destination.name ?? 'the marked spot'}`
        + (state.remaining ? `, about ${state.remaining} away` : '');
    const camp = state.mode === 'camp' ? state.lodging !== null ? ` · lodging, ${state.lodging} gold a night` : ' · in camp' : '';
    return `${where}${aboard}${going}${camp}.`;
  };

  const render = (state: ExpeditionProjection): void => {
    clock.textContent = `${state.time} on ${state.date} · ${WEATHER[state.weather] ?? state.weather}`;
    const encounter = state.encounter !== null;
    redraw(speeds, JSON.stringify([state.speed, state.speeds, state.teleport, encounter]), () => {
      speeds.replaceChildren();
      for (let speed = 0; speed <= state.speeds; speed++) {
        const control = button(SPEEDS[speed] ?? `Speed ${speed}`);
        control.dataset.speed = String(speed);
        control.setAttribute('aria-pressed', String(state.speed === speed));
        control.disabled = state.teleport || (encounter && speed > 0);
        control.addEventListener('click', () => claim({ action: 'expedition-speed', speed }));
        speeds.append(control);
      }
    });
    redraw(modes, JSON.stringify([state.mode, state.lodging, state.swimming, state.teleport, encounter]), () => {
      modes.replaceChildren();
      for (const entry of MODES) {
        const control = button(entry.mode === 'camp' && state.lodging !== null ? `Camp · ${state.lodging} gold` : entry.label);
        control.dataset.mode = entry.mode;
        control.dataset.tooltip = entry.tooltip;
        control.setAttribute('aria-pressed', String(state.mode === entry.mode));
        control.disabled = state.teleport || encounter || (entry.mode === 'camp' && state.swimming);
        control.addEventListener('click', () => claim({ action: 'expedition-mode', mode: entry.mode }));
        modes.append(control);
      }
    });
    redraw(vitals, JSON.stringify([state.health, state.fatigue, state.magicka].map(value => [Math.round(value.current), Math.round(value.maximum)])), () =>
      vitals.replaceChildren(vital('Health', state.health), vital('Fatigue', state.fatigue), vital('Magicka', state.magicka)));
    redraw(found, JSON.stringify(state.found), () => {
      found.replaceChildren();
      // A few: the world map lists more, and the panel keeps room for the pin and the bands in sight.
      for (const place of state.found.slice(0, 4)) {
        const control = button(`${place.name} · ${place.kind}`);
        control.dataset.region = String(place.region);
        control.dataset.index = String(place.index);
        control.addEventListener('click', () => {
          claim({ action: 'expedition-look', x: place.x, y: place.y });
          claim({ action: 'expedition-pin', x: place.x, y: place.y, region: place.region, index: place.index });
        });
        found.append(control);
      }
    });
    // The bands the party sees, each looked at by choosing it: the map draws them, and keys and the pad reach them here.
    const bandSignature = state.bands.map(band => [
      band.id ?? '', band.name, band.kind, band.category ?? '', band.faction ?? '', band.clashSides ?? '',
      band.origin ?? '', band.destination ?? '', band.detail ?? '',
      band.camp, band.spotted, band.isClash ?? false, band.hostile ?? false,
      Math.floor(band.x), Math.floor(band.y),
    ]);
    let restoringInSightFocus = false;
    try {
      restoringInSightFocus = true;
      redraw(inSight, JSON.stringify(bandSignature), () => {
        inSight.replaceChildren();
        if (state.bands.length === 0) {
          const none = document.createElement('p');
          none.className = 'dagger-expedition-none';
          none.textContent = 'No one in sight.';
          inSight.append(none);
        }
        for (const band of state.bands) {
          const key = bandKey(band);
          const stateTag = band.isClash ? ' · active clash' : band.spotted ? ' · giving chase' : band.camp ? ' · camped' : '';
          const control = button('');
          if (band.category) {
            const badge = document.createElement('span');
            badge.className = 'dagger-expedition-band-badge';
            badge.dataset.kind = band.kind;
            badge.textContent = band.category;
            control.append(badge, ' ');
          }
          control.append(`${band.name}${stateTag}`);
          control.dataset.redrawKey = key;
          control.dataset.kind = band.kind;
          control.dataset.spotted = String(band.spotted);
          control.dataset.clash = String(Boolean(band.isClash));
          control.dataset.hostile = String(Boolean(band.hostile));
          control.dataset.tooltip = bandTooltip(band);
          const inspect = (): void => {
            inspectedBandKey = key;
            renderBandDetail(band);
          };
          control.addEventListener('focus', () => {
            if (!restoringInSightFocus) inspect();
          });
          control.addEventListener('pointerenter', inspect);
          control.addEventListener('click', () => {
            inspect();
            claim({ action: 'expedition-look', x: Math.floor(band.x), y: Math.floor(band.y) });
          });
          inSight.append(control);
        }
      });
    } finally {
      restoringInSightFocus = false;
    }
    const inspected = inspectedBandKey ? state.bands.find(band => bandKey(band) === inspectedBandKey) ?? null : null;
    if (!inspected) inspectedBandKey = null;
    renderBandDetail(inspected);
    journey.textContent = journeyLine(state);
    message.textContent = state.message ?? '';
    root.dataset.encounter = String(encounter);
    ship.textContent = state.ship === 'none' ? state.shipFare > 0 ? `Board ship · ${state.shipFare} gold` : 'Board ship' : 'Go ashore';
    ship.disabled = state.teleport || encounter || (state.ship === 'none' && !state.canBoard);
    ship.dataset.tooltip = state.ship === 'none' ? 'Board a ship at a port near the sea.' : 'Leave the ship where it has landed.';
    pinLabel.textContent = state.pin ? state.pin.name ?? `The marked spot (${state.pin.x}, ${state.pin.y})` : 'No pin set.';
    goToPin.disabled = travelToPin.disabled = clearPin.disabled = state.pin === null;
    travelToPin.disabled ||= state.teleport || encounter;
    centreGo.disabled = encounter;
    leave.textContent = state.teleport ? 'Cancel' : 'Close map';
    leave.hidden = !state.canLeave;
    enter.textContent = state.enterLabel;
    enter.disabled = !state.canEnter;
    encounterPanel.hidden = state.encounter === null;
    encounterTitle.textContent = state.encounter?.title ?? '';
    encounterLine.textContent = state.encounter?.line ?? '';
    encounterEnter.textContent = state.enterLabel;
    encounterEnter.disabled = !state.canEnter;
    imAt.setAttribute('aria-pressed', String(state.view.following));
    compass.style.transform = `rotate(${-state.view.heading}deg)`;
    redraw(labels, JSON.stringify(state.labels.map(label => [label.name, label.kind, Math.round(label.across * 1000), Math.round(label.down * 1000),
      Math.round((label.opacity ?? 1) * 100)])), () => {
      labels.replaceChildren();
      for (const label of state.labels) {
        const tag = document.createElement('span');
        tag.className = 'dagger-expedition-label';
        tag.dataset.kind = label.kind;
        tag.textContent = label.name;
        tag.style.left = `${label.across * 100}%`;
        tag.style.top = `${label.down * 100}%`;
        if (label.opacity !== undefined && label.opacity < 1) tag.style.opacity = String(label.opacity);
        labels.append(tag);
      }
    });
  };

  return {
    root,
    update(value): void {
      current = value;
      root.hidden = value === null;
      if (value === null) { setPanning(false); return; }
      render(value);
    },
    isOpen: () => current !== null,
    back(): void {
      if (current?.canLeave) claim({ action: 'expedition-leave' });
      else if (current) openMenu();
    },
    page(direction): void { move(0, 0, 0, 0, direction); },
    pageHint: () => current ? 'Zoom' : null,
    initialFocus: () => current?.canEnter && !current.canLeave ? enter : view,
    encounter: { root: encounterPanel, isOpen: () => current?.encounter != null, initialFocus: () => encounterEnter },
    dispose(): void { unanchor(); root.remove(); },
  };
}
