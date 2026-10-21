import { followPress } from './drag.js';
import { BACK_EVENT, DIRECTION_EVENT, type Direction } from './navigation.js';

/** A top-down diagram of admitted map data. Controls own presentation only. */
export interface MapProjection {
  readonly id: string; readonly name: string; readonly kind: 'city' | 'dungeon' | 'interior';
  readonly region: number | null; readonly location: number | null;
  readonly player: { readonly x: number; readonly y: number; readonly z: number; readonly yaw: number };
  readonly areas: readonly { readonly id: string; readonly minX: number; readonly minZ: number; readonly maxX: number; readonly maxZ: number; readonly minY: number; readonly maxY: number; readonly kind: number; readonly category: string }[];
  readonly labels: readonly { readonly id: string; readonly name: string; readonly x: number; readonly y: number; readonly z: number; readonly selected: boolean; readonly note?: boolean }[];
  /** Whether this map keeps player notes; a note is placed at the player's position. */
  readonly canAddNote?: boolean;
  /** The compass word for the way the player faces. */
  readonly facing?: string;
  /** The area categories this map draws, with the legend's words for each. */
  readonly legend?: readonly { readonly category: string; readonly label: string }[];
}
export type MapAction =
  | { readonly action: 'map-building'; readonly region: number; readonly destination: number; readonly item: string }
  | { readonly action: 'map-note-add'; readonly text: string }
  | { readonly action: 'map-note-edit'; readonly note: string; readonly text: string }
  | { readonly action: 'map-note-remove'; readonly note: string };
export function isMapProjection(value: unknown): value is MapProjection {
  if (!value || typeof value !== 'object') return false;
  const map = value as Partial<MapProjection>;
  return typeof map.id === 'string' && typeof map.name === 'string' && (map.kind === 'city' || map.kind === 'dungeon' || map.kind === 'interior')
    && !!map.player && [map.player.x, map.player.y, map.player.z, map.player.yaw].every(Number.isFinite)
    && Array.isArray(map.areas) && map.areas.every(area => typeof area.id === 'string' && [area.minX, area.minZ, area.maxX, area.maxZ, area.minY, area.maxY, area.kind].every(Number.isFinite) && typeof area.category === 'string')
    && Array.isArray(map.labels) && map.labels.every(label => typeof label.id === 'string' && typeof label.name === 'string' && [label.x, label.y, label.z].every(Number.isFinite))
    && (map.kind !== 'city' || (Number.isInteger(map.region) && Number.isInteger(map.location)));
}
const ns = 'http://www.w3.org/2000/svg';
// Presentation colours for the semantic area categories the ruleset publishes.
const areaColours: Record<string, string> = { door: '#c49b55', room: '#718694', surface: '#5f7a69', tavern: '#d99845', guild: '#9879b8', shop: '#b8705a', common: '#697f94' };
const colour = (category: string): string => areaColours[category] ?? areaColours.common!;
/** The diagram's half-width in its own units; the view box is twice this square. */
const HALF = 160;
/** How far one pan step moves, in diagram units. */
const PAN_STEP = 24;
const ZOOM_STEP = 1.5;
const MIN_ZOOM = .01, MAX_ZOOM = 100;
function svgElement(name: string, attributes: Record<string, string>): SVGElement {
  const element = document.createElementNS(ns, name);
  for (const [key, value] of Object.entries(attributes)) element.setAttribute(key, value);
  return element;
}
const PLAYER_ARROW = 'M 0 -6 L 4 5 L 0 2 L -4 5 Z';

/**
 * The local map: a pannable, zoomable diagram of what the player has explored, with a legend, named
 * points and the player's arrow.
 *
 * The mouse drags it to pan (pointer events with capture, as every drag in game UI) and the wheel
 * zooms about the cursor. Keyboard and controller pan it from the diagram itself: accepting the
 * focused diagram starts panning, the directions then move the view, the triggers or Page Up and Down
 * zoom, and accepting again or Back stops. Points stay focusable as controls of their own.
 */
export function mountMap(root: HTMLElement, send: (action: MapAction) => void, travel: () => void): {
  update(value: MapProjection | null): void;
  /** The triggers or Page Up and Down: zoom out (-1) or in (1). */
  zoom(direction: -1 | 1): void;
  dispose(): void;
} {
  const shell = document.createElement('section'); shell.className = 'dagger-map';
  const heading = document.createElement('h2');
  const controls = document.createElement('div'); controls.className = 'dagger-map-controls';
  const status = document.createElement('p'); status.className = 'dagger-map-status'; status.setAttribute('role', 'status');
  const levelLabel = document.createElement('label'); levelLabel.textContent = 'Height slice (metres) ';
  const level = document.createElement('input'); level.type = 'number'; level.step = '0.5'; level.className = 'dagger-map-level';
  const allLabel = document.createElement('label'); allLabel.textContent = 'All levels ';
  const all = document.createElement('input'); all.type = 'checkbox'; all.checked = true; allLabel.append(all); levelLabel.append(level);
  const namesLabel = document.createElement('label'); namesLabel.textContent = 'All names ';
  const names = document.createElement('input'); names.type = 'checkbox'; names.className = 'dagger-map-names'; namesLabel.append(names);
  // The diagram is one control: focus it to pan with keys or a pad; its points are controls inside it.
  const view = document.createElement('div'); view.className = 'dagger-map-view'; view.tabIndex = 0;
  view.setAttribute('role', 'group');
  const svg = svgElement('svg', { viewBox: `${-HALF} ${-HALF} ${HALF * 2} ${HALF * 2}`, role: 'img', 'aria-label': 'Explored map and current player position' });
  svg.classList.add('dagger-map-diagram');
  const viewport = svgElement('g', {}); svg.append(viewport);
  const hint = document.createElement('p'); hint.className = 'dagger-map-hint';
  view.append(svg, hint);
  const legend = document.createElement('ul'); legend.className = 'dagger-map-legend'; legend.setAttribute('aria-label', 'Map legend');
  const buildings = document.createElement('div'); buildings.className = 'dagger-map-buildings';
  const target = document.createElement('p'); target.className = 'dagger-map-target';
  const noteForm = document.createElement('form'); noteForm.className = 'dagger-map-note';
  const noteText = document.createElement('input'); noteText.maxLength = 256; noteText.autocomplete = 'off'; noteText.setAttribute('aria-label', 'Map note text');
  const noteAdd = document.createElement('button'); noteAdd.type = 'submit'; noteAdd.textContent = 'Add note here';
  noteForm.append(noteText, noteAdd);
  noteForm.addEventListener('submit', event => {
    event.preventDefault();
    const text = noteText.value.trim();
    if (!text) { noteText.focus(); return; }
    send({ action: 'map-note-add', text });
    noteText.value = '';
  });
  let drawn = '';
  let current: MapProjection | null = null, identity = '', centerX = 0, centerZ = 0, zoom = 1, rotation = 0, playerUp = false;
  let panning = false;
  const angle = (): number => playerUp && current ? -current.player.yaw * 180 / Math.PI : rotation;
  /** A move on screen, in diagram units, as a move over the map: undo the view's rotation and zoom. */
  const toWorld = (dx: number, dy: number): [number, number] => {
    const radians = -angle() * Math.PI / 180;
    return [(dx * Math.cos(radians) - dy * Math.sin(radians)) / zoom, (dx * Math.sin(radians) + dy * Math.cos(radians)) / zoom];
  };
  const showAll = (): boolean => current?.kind === 'city' || all.checked;
  const describe = (value: MapProjection): string => {
    const kind = value.kind === 'city' ? 'City' : value.kind === 'interior' ? 'Explored interior' : 'Explored dungeon';
    const offset = Math.round((Number(level.value) - value.player.y) * 2) / 2;
    const levels = value.kind === 'city' ? null : showAll() ? 'all levels'
      : offset === 0 ? 'your level' : `${Math.abs(offset)} m ${offset > 0 ? 'above' : 'below'} you`;
    return [kind, levels, value.facing ? `facing ${value.facing}` : null].filter(Boolean).join(' · ');
  };
  /** Place the view and keep every mark upright and the same size on screen whatever the zoom and turn. */
  const applyView = (): void => {
    if (!current) return;
    const turn = angle();
    viewport.setAttribute('transform', `scale(${zoom}) rotate(${turn}) translate(${-centerX} ${-centerZ})`);
    for (const mark of viewport.querySelectorAll('[data-mark]')) mark.setAttribute('transform', `rotate(${-turn}) scale(${1 / zoom})`);
    viewport.querySelector('[data-player]')?.setAttribute('transform',
      `translate(${current.player.x} ${current.player.z}) rotate(${current.player.yaw * 180 / Math.PI}) scale(${1.6 / zoom})`);
    status.textContent = describe(current);
  };
  const point = (label: MapProjection['labels'][number]): SVGElement => {
    const group = svgElement('g', { class: 'dagger-map-point', transform: `translate(${label.x} ${label.z})`, 'data-label-id': label.id });
    if (label.selected) group.classList.add('is-selected');
    if (label.note) group.classList.add('is-note');
    const mark = svgElement('g', { 'data-mark': 'true' });
    const dot = svgElement('circle', { r: '3.2' });
    const text = svgElement('text', { x: '6', y: '4', class: 'dagger-map-name' });
    text.textContent = label.name;
    mark.append(dot, text); group.append(mark);
    return group;
  };
  function draw(): void {
    // A point or building the player had focused keeps focus through the rebuild, as when choosing it
    // republishes the map with it selected.
    const focused = document.activeElement instanceof Element && shell.contains(document.activeElement) ? document.activeElement : null;
    const key = focused?.getAttribute('data-label-id') ?? focused?.getAttribute('data-building') ?? null;
    const inDiagram = focused !== null && viewport.contains(focused);
    build();
    if (key === null) return;
    const attribute = inDiagram ? 'data-label-id' : 'data-building';
    const again = Array.from((inDiagram ? viewport : buildings).querySelectorAll<HTMLElement | SVGElement>(`[${attribute}]`))
      .find(element => element.getAttribute(attribute) === key);
    again?.focus({ preventScroll: true });
  }
  function build(): void {
    drawn = '';
    viewport.replaceChildren(); buildings.replaceChildren(); legend.replaceChildren();
    if (!current) { status.textContent = 'No map is available in this interior.'; return; }
    const value = current;
    const slice = Number(level.value), every = showAll();
    for (const area of value.areas) {
      if (!every && (area.minY > slice || area.maxY < slice)) continue;
      const rect = svgElement('rect', { x: String(area.minX), y: String(area.minZ), width: String(Math.max(.1, area.maxX - area.minX)), height: String(Math.max(.1, area.maxZ - area.minZ)), 'data-id': area.id, 'data-kind': String(area.kind), 'data-category': area.category, fill: colour(area.category) });
      viewport.append(rect);
    }
    for (const label of value.labels) {
      if (!every && Math.abs(label.y - slice) > 1) continue;
      const mark = point(label);
      viewport.append(mark);
      if (value.kind === 'city' && value.region !== null && value.location !== null) {
        const button = document.createElement('button'); button.type = 'button'; button.textContent = label.name; button.dataset.building = label.id;
        button.setAttribute('aria-pressed', String(label.selected));
        const region = value.region, destination = value.location;
        const select = (): void => send({ action: 'map-building', region, destination, item: label.id });
        button.addEventListener('click', select);
        mark.setAttribute('role', 'button'); mark.setAttribute('tabindex', '0'); mark.setAttribute('aria-label', `${label.name}: show directions`);
        mark.addEventListener('click', () => { if (!dragged) select(); });
        mark.addEventListener('keydown', event => { if (event instanceof KeyboardEvent && (event.key === 'Enter' || event.key === ' ')) { event.preventDefault(); select(); } });
        buildings.append(button);
      } else {
        mark.setAttribute('tabindex', '0'); mark.setAttribute('aria-label', label.note ? `Your note: ${label.name}` : label.name);
        const text = document.createElement('p'); text.textContent = label.name;
        if (label.note) {
          text.dataset.note = label.id;
          const remove = document.createElement('button'); remove.type = 'button'; remove.textContent = 'Remove note';
          remove.addEventListener('click', () => send({ action: 'map-note-remove', note: label.id }));
          // Editing rewrites the note's text in place; the discovery owner keeps where it was placed.
          const edit = document.createElement('form'); edit.className = 'dagger-map-note-edit';
          const editText = document.createElement('input'); editText.maxLength = 256; editText.autocomplete = 'off'; editText.value = label.name;
          editText.setAttribute('aria-label', `Edit note ${label.name}`);
          const save = document.createElement('button'); save.type = 'submit'; save.textContent = 'Save note';
          edit.append(editText, save);
          edit.addEventListener('submit', event => {
            event.preventDefault();
            const value = editText.value.trim();
            if (!value) { editText.focus(); return; }
            send({ action: 'map-note-edit', note: label.id, text: value });
          });
          text.append(' ', remove, edit);
        }
        buildings.append(text);
      }
    }
    viewport.append(svgElement('path', { d: PLAYER_ARROW, 'data-player': 'true', class: 'dagger-map-player' }));
    // The legend names what this map draws: the ruleset's area categories, then the marks.
    const entry = (swatch: string, text: string, style?: string): HTMLLIElement => {
      const item = document.createElement('li');
      const key = document.createElement('span'); key.className = `dagger-map-key dagger-map-key-${swatch}`;
      if (style) key.style.background = style;
      item.append(key, text);
      return item;
    };
    legend.append(...(value.legend ?? []).map(row => entry('area', row.label, colour(row.category))), entry('player', 'You'));
    if (value.labels.some(label => !label.note)) legend.append(entry('point', value.kind === 'city' ? 'Known building' : 'Marker'));
    if (value.labels.some(label => label.selected)) legend.append(entry('selected', 'Directions target'));
    if (value.labels.some(label => label.note)) legend.append(entry('note', 'Your note'));
    drawn = JSON.stringify([value.id, value.kind, value.areas, value.labels, value.region, value.location, value.legend]);
    const chosen = value.labels.find(label => label.selected)?.name;
    target.textContent = chosen ? `Directions target: ${chosen}` : '';
    applyView();
  }

  const setPanning = (on: boolean): void => {
    panning = on;
    view.classList.toggle('is-panning', on);
    view.setAttribute('aria-pressed', String(on));
    view.setAttribute('aria-label', on ? 'Map view, panning' : 'Map view');
    hint.textContent = on ? 'Directions pan · triggers or Page Up and Down zoom · accept or Back to stop'
      : 'Drag to pan · wheel to zoom · accept to pan with keys or a controller';
  };
  setPanning(false);
  const pan = (direction: Direction): void => {
    const [dx, dy] = direction === 'left' ? [-PAN_STEP, 0] : direction === 'right' ? [PAN_STEP, 0] : direction === 'up' ? [0, -PAN_STEP] : [0, PAN_STEP];
    const [wx, wz] = toWorld(dx, dy);
    centerX += wx; centerZ += wz; applyView();
  };
  /** Zoom by a factor, keeping the map under a diagram point (the centre by default) where it is. */
  const zoomBy = (factor: number, sx = 0, sy = 0): void => {
    const next = Math.min(MAX_ZOOM, Math.max(MIN_ZOOM, zoom * factor));
    if (next === zoom) return;
    const [ax, az] = toWorld(sx, sy);
    const atX = centerX + ax, atZ = centerZ + az;
    zoom = next;
    const [bx, bz] = toWorld(sx, sy);
    centerX = atX - bx; centerZ = atZ - bz;
    applyView();
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
  // A controller's accept clicks the focused control; a mouse click on the diagram is not a request to pan.
  view.addEventListener('click', event => { if (event.target === view && event.detail === 0) setPanning(!panning); });
  view.addEventListener('keydown', event => {
    if (event.target !== view) return;
    if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); setPanning(!panning); }
    else if (event.key === '+' || event.key === '=') { event.preventDefault(); zoomBy(ZOOM_STEP); }
    else if (event.key === '-' || event.key === '_') { event.preventDefault(); zoomBy(1 / ZOOM_STEP); }
  });
  view.addEventListener('blur', () => setPanning(false));

  /** A client point as a point of the diagram's view box. */
  const diagramPoint = (clientX: number, clientY: number): [number, number] => {
    const rect = svg.getBoundingClientRect();
    if (rect.width === 0 || rect.height === 0) return [0, 0];
    const size = Math.min(rect.width, rect.height);
    return [(clientX - rect.left - rect.width / 2) * HALF * 2 / size, (clientY - rect.top - rect.height / 2) * HALF * 2 / size];
  };
  svg.addEventListener('wheel', event => {
    if (!current) return;
    event.preventDefault();
    const [sx, sy] = diagramPoint(event.clientX, event.clientY);
    zoomBy(event.deltaY < 0 ? ZOOM_STEP : 1 / ZOOM_STEP, sx, sy);
  }, { passive: false });
  // Dragging pans through the shared press lifecycle (capture on press; release, cancel, a lost capture
  // or the window losing focus end it); a press that does not travel stays a click on a point.
  let dragged = false;
  svg.addEventListener('pointerdown', event => {
    if (event.button !== 0 || !current) return;
    const from = { x: event.clientX, y: event.clientY, centerX, centerZ };
    dragged = false;
    followPress(svg, event, {
      start() { dragged = true; view.classList.add('is-dragging'); },
      move(moved) {
        const [x0, y0] = diagramPoint(from.x, from.y), [x1, y1] = diagramPoint(moved.clientX, moved.clientY);
        const [wx, wz] = toWorld(x1 - x0, y1 - y0);
        centerX = from.centerX - wx; centerZ = from.centerZ - wz;
        applyView();
      },
      end(_released, wasDrag) {
        view.classList.remove('is-dragging');
        // The click that ends a drag arrives after this; it is the drag's, not a choice of a point.
        if (wasDrag) setTimeout(() => { dragged = false; }, 0);
      },
    });
  });

  const button = (label: string, action: () => void): void => {
    const element = document.createElement('button'); element.type = 'button'; element.textContent = label;
    element.addEventListener('click', () => { action(); draw(); }); controls.append(element);
  };
  button('Zoom in', () => { zoom = Math.min(MAX_ZOOM, zoom * ZOOM_STEP); }); button('Zoom out', () => { zoom = Math.max(MIN_ZOOM, zoom / ZOOM_STEP); });
  button('Rotate left', () => { playerUp = false; rotation -= 45; }); button('Rotate right', () => { playerUp = false; rotation += 45; });
  button('North up', () => { playerUp = false; rotation = 0; }); button('Player up', () => { playerUp = true; });
  button('Center on me', () => { if (current) { centerX = current.player.x; centerZ = current.player.z; level.value = String(current.player.y); } });
  button('Level up', () => { all.checked = false; level.value = String(Number(level.value) + .5); });
  button('Level down', () => { all.checked = false; level.value = String(Number(level.value) - .5); });
  button('Travel destinations', travel);
  level.addEventListener('change', () => { all.checked = false; draw(); }); all.addEventListener('change', draw);
  names.addEventListener('change', () => view.classList.toggle('shows-names', names.checked));
  controls.append(levelLabel, allLabel, namesLabel);
  shell.append(heading, status, view, legend, controls, target, noteForm, buildings); root.append(shell);
  return {
    update(value): void {
      current = value; controls.hidden = buildings.hidden = legend.hidden = value === null; view.hidden = value === null;
      heading.textContent = value ? `${value.name} map` : 'Map';
      if (value && identity !== `${value.kind}:${value.id}`) {
        identity = `${value.kind}:${value.id}`; centerX = value.player.x; centerZ = value.player.z; rotation = 0; playerUp = false; all.checked = true; level.value = String(value.player.y);
        const extent = Math.max(40, ...value.areas.flatMap(area => [Math.abs(area.minX - centerX), Math.abs(area.maxX - centerX), Math.abs(area.minZ - centerZ), Math.abs(area.maxZ - centerZ)]));
        zoom = Math.max(MIN_ZOOM, 140 / extent);
      }
      if (value === null) setPanning(false);
      noteForm.hidden = value?.canAddNote !== true;
      levelLabel.hidden = value?.kind === 'city' || !value; allLabel.hidden = value?.kind === 'city' || !value;
      const signature = value ? JSON.stringify([value.id, value.kind, value.areas, value.labels, value.region, value.location, value.legend]) : '';
      if (value && drawn === signature) applyView();
      else draw();
    },
    zoom(direction): void { if (current) zoomBy(direction > 0 ? ZOOM_STEP : 1 / ZOOM_STEP); },
    dispose(): void { shell.remove(); },
  };
}
