import { image, reportMissingArt } from './art.js';

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

/** One level of the atlas: its size in pixels and its tiles, coarsest level first. */
export interface AtlasLevelProjection {
  readonly level: number;
  readonly width: number;
  readonly height: number;
  readonly columns: number;
  readonly rows: number;
}

/** A discovered place, at its world-map pixel; `tier` says from which zoom it is named (1 cities … 3 every place). */
export interface TravelMapPlaceProjection {
  readonly region: number;
  readonly index: number;
  readonly name: string;
  readonly kind: string;
  readonly category: string;
  readonly tier: number;
  readonly x: number;
  readonly y: number;
}

/** A name of the land or sea, written from the zoom its kind is shown from. */
export interface TravelMapNameProjection {
  readonly id: string;
  readonly name: string;
  readonly kind: string;
  readonly shownFrom: 'world' | 'region' | 'close';
  readonly x: number;
  readonly y: number;
}

export interface TravelMapProjection {
  readonly worldWidth: number;
  readonly worldHeight: number;
  /** The atlas drawn from the land: the page asks for the tiles it shows, and draws each granted one from its URL. Null draws the classic art. */
  readonly atlas: { readonly tileSize: number; readonly levels: readonly AtlasLevelProjection[]; readonly tiles: readonly { readonly id: string; readonly url: string }[] } | null;
  readonly classic: { readonly world: TravelMapImageProjection; readonly sheets: readonly { readonly region: number; readonly art: TravelMapImageProjection }[] } | null;
  readonly player: { readonly x: number; readonly y: number; readonly heading: number | null } | null;
  readonly regions: readonly { readonly region: number; readonly name: string; readonly x: number; readonly y: number; readonly discovered: number }[];
  /** Which places the map draws; the places and their legend travel only when this changes or the page asks again. */
  readonly placesRevision: string;
  readonly places?: readonly TravelMapPlaceProjection[];
  readonly legend?: readonly { readonly category: string; readonly label: string }[];
  readonly names: readonly TravelMapNameProjection[];
}

/** The route a quote measured, in world-map pixels. */
export interface AtlasRoute { readonly fromX: number; readonly fromY: number; readonly toX: number; readonly toY: number }

export function isTravelMap(value: unknown): value is TravelMapProjection {
  if (typeof value !== 'object' || value === null) return false;
  const map = value as Record<string, unknown>;
  return Number.isFinite(map.worldWidth) && Number.isFinite(map.worldHeight) && Array.isArray(map.regions) && typeof map.placesRevision === 'string'
    && (map.places === undefined || Array.isArray(map.places)) && Array.isArray(map.names)
    && (map.atlas !== null || (typeof map.classic === 'object' && map.classic !== null));
}

/**
 * The zoom at which each thing is shown, in screen pixels per world-map pixel. The whole bay fits at about one; a
 * region fills the map near four; close in starts near eight, where the atlas's finest level shows pixel for pixel
 * at four and is magnified past it.
 */
const ZOOM = { region: 2.6, close: 6, closest: 12, max: 16 } as const;
const STEP = 1.6;

/** Every tile the atlas may ask for at once, as the ruleset bounds it. */
const TILES_PER_REQUEST = 64;

export interface Atlas {
  readonly element: HTMLElement;
  /** A place the map holds, by its region:index key. */
  place(key: string): TravelMapPlaceProjection | undefined;
  update(map: TravelMapProjection | null, selected: string, route: AtlasRoute | null): void;
  /** Zoom in or out about the middle of the map. */
  zoom(direction: -1 | 1): void;
  /** Centre the map on a world-map pixel, zoomed in at least as far as a place is named. */
  centre(x: number, y: number, zoom?: number): void;
  /** Art the page now holds may fill tiles it was waiting for. */
  refreshArt(): void;
}

/**
 * The travel map: the atlas drawn from the land (or the classic map art), pannable and zoomable, with the places the
 * player knows, the names of the land and sea, the regions, the route and the player over it. Labels and markers are
 * drawn at screen size over the scaled map, so text stays crisp at every zoom; only what is in view is made. Places are
 * named from the zoom their tier is named at, and every place's name shows when it is pointed at or focused.
 *
 * Choosing a place asks the ruleset for the route, as the destination list does.
 */
export function mountAtlas(requestTiles: (tiles: readonly [number, number, number][]) => void,
  choose: (region: number, index: number, keyed: boolean) => void, askPlaces: () => void): Atlas {
  const element = document.createElement('div');
  element.className = 'dagger-atlas';
  // The map surface takes focus itself, so the arrows pan it and + and - zoom it.
  element.tabIndex = 0;
  element.setAttribute('role', 'application');
  element.setAttribute('aria-label', 'Travel map');
  const ground = document.createElement('div');
  ground.className = 'dagger-atlas-ground';
  const marks = document.createElement('div');
  marks.className = 'dagger-atlas-marks';
  const SVG = 'http://www.w3.org/2000/svg';
  const routeLayer = document.createElementNS(SVG, 'svg');
  routeLayer.setAttribute('class', 'dagger-atlas-route');
  routeLayer.setAttribute('aria-hidden', 'true');
  const controls = document.createElement('div');
  controls.className = 'dagger-atlas-controls';
  const zoomIn = document.createElement('button');
  zoomIn.type = 'button'; zoomIn.textContent = '+'; zoomIn.setAttribute('aria-label', 'Zoom in');
  const zoomOut = document.createElement('button');
  zoomOut.type = 'button'; zoomOut.textContent = '−'; zoomOut.setAttribute('aria-label', 'Zoom out');
  const here = document.createElement('button');
  here.type = 'button'; here.textContent = 'You'; here.setAttribute('aria-label', 'Centre on where you are');
  controls.append(zoomIn, zoomOut, here);
  const readout = document.createElement('p');
  readout.className = 'dagger-atlas-readout';
  readout.setAttribute('aria-live', 'polite');
  const legend = document.createElement('ul');
  legend.className = 'dagger-atlas-legend';
  legend.setAttribute('aria-label', 'Map legend');
  element.append(ground, routeLayer, marks, controls, readout, legend);

  let map: TravelMapProjection | null = null;
  // The places as last sent, and the revision they are; a snapshot that names another revision without them asks again.
  let places: readonly TravelMapPlaceProjection[] = [];
  let legendRows: NonNullable<TravelMapProjection['legend']> = [];
  let placesRevision = '';
  let askedRevision = '';
  let selectedKey = '';
  let route: AtlasRoute | null = null;
  // The view: world-map pixel at the map's centre, and screen pixels per world-map pixel.
  let cx = 500, cy = 250, scale = 0;
  let fitted = false;
  let frame = 0;
  const asked = new Set<string>();

  // The map's size, read from layout once per draw: reading it while markers are being added would lay the page out
  // again for every place.
  let measured = { width: 1200, height: 700 };
  const measure = (): void => { measured = { width: element.clientWidth || 1200, height: element.clientHeight || 700 }; };
  const size = (): { width: number; height: number } => measured;
  const fitScale = (): number => map ? Math.min(size().width / map.worldWidth, size().height / map.worldHeight) : 1;
  const clampView = (): void => {
    if (!map) return;
    scale = Math.min(ZOOM.max, Math.max(fitScale(), scale));
    const { width, height } = size();
    const halfW = width / 2 / scale, halfH = height / 2 / scale;
    cx = map.worldWidth <= halfW * 2 ? map.worldWidth / 2 : Math.min(map.worldWidth - halfW, Math.max(halfW, cx));
    cy = map.worldHeight <= halfH * 2 ? map.worldHeight / 2 : Math.min(map.worldHeight - halfH, Math.max(halfH, cy));
  };
  const toScreen = (x: number, y: number): [number, number] => {
    const { width, height } = size();
    return [(x - cx) * scale + width / 2, (y - cy) * scale + height / 2];
  };
  const schedule = (): void => {
    if (frame) return;
    const run = (): void => { frame = 0; draw(); };
    frame = typeof window.requestAnimationFrame === 'function' ? window.requestAnimationFrame(run) : window.setTimeout(run, 16);
  };
  const setView = (x: number, y: number, zoom: number): void => { cx = x; cy = y; scale = zoom; clampView(); schedule(); };

  const label = (marker: HTMLElement | null): void => { readout.textContent = marker?.dataset.label ?? (selectedKey ? readout.dataset.chosen ?? '' : ''); };
  marks.addEventListener('focusin', event => label((event.target as HTMLElement).closest<HTMLElement>('[data-label]')));
  marks.addEventListener('pointerover', event => label((event.target as HTMLElement).closest<HTMLElement>('[data-label]')));
  marks.addEventListener('focusout', () => label(null));
  // A focused place out of view brings the view to it, so arrows and a pad can walk the map place by place.
  marks.addEventListener('focusin', event => {
    const marker = (event.target as HTMLElement).closest<HTMLElement>('[data-x]');
    if (!marker) return;
    const [sx, sy] = toScreen(Number(marker.dataset.x), Number(marker.dataset.y));
    const { width, height } = size();
    if (sx < 40 || sy < 40 || sx > width - 40 || sy > height - 40) setView(Number(marker.dataset.x), Number(marker.dataset.y), scale);
  });

  // Drag to pan; the wheel zooms about the pointer.
  let drag: { x: number; y: number; cx: number; cy: number; moved: boolean } | null = null;
  element.addEventListener('pointerdown', event => {
    if ((event.target as HTMLElement).closest('button')) return;
    drag = { x: event.clientX, y: event.clientY, cx, cy, moved: false };
    element.setPointerCapture?.(event.pointerId);
  });
  element.addEventListener('pointermove', event => {
    if (!drag) return;
    const dx = event.clientX - drag.x, dy = event.clientY - drag.y;
    if (Math.abs(dx) + Math.abs(dy) > 3) drag.moved = true;
    if (drag.moved) setView(drag.cx - dx / scale, drag.cy - dy / scale, scale);
  });
  const endDrag = (): void => { drag = null; };
  element.addEventListener('pointerup', endDrag);
  element.addEventListener('pointercancel', endDrag);
  element.addEventListener('wheel', event => {
    event.preventDefault();
    const box = element.getBoundingClientRect();
    const px = event.clientX - box.left - size().width / 2, py = event.clientY - box.top - size().height / 2;
    const next = Math.min(ZOOM.max, Math.max(fitScale(), scale * (event.deltaY < 0 ? STEP : 1 / STEP)));
    // The world point under the pointer stays under it.
    setView(cx + px / scale - px / next, cy + py / scale - py / next, next);
  }, { passive: false });
  element.addEventListener('keydown', event => {
    if (event.target !== element) return;
    const pan = 80 / scale;
    const moves: Record<string, [number, number]> = { ArrowLeft: [-pan, 0], ArrowRight: [pan, 0], ArrowUp: [0, -pan], ArrowDown: [0, pan] };
    if (moves[event.key]) { setView(cx + moves[event.key]![0], cy + moves[event.key]![1], scale); event.preventDefault(); event.stopPropagation(); }
    else if (event.key === '+' || event.key === '=') { atlas.zoom(1); event.preventDefault(); }
    else if (event.key === '-' || event.key === '_') { atlas.zoom(-1); event.preventDefault(); }
  });
  zoomIn.addEventListener('click', () => atlas.zoom(1));
  zoomOut.addEventListener('click', () => atlas.zoom(-1));
  here.addEventListener('click', () => { if (map?.player) atlas.centre(map.player.x + .5, map.player.y + .5, Math.max(scale, ZOOM.region)); });

  // The ground: atlas tiles at the level the zoom wants, or the classic world image and, close in, its region sheets.
  const tiles = new Map<string, HTMLImageElement>();
  const drawGround = (): void => {
    if (!map) { ground.replaceChildren(); tiles.clear(); return; }
    const { width, height } = size();
    const left = cx - width / 2 / scale, top = cy - height / 2 / scale;
    // Atlas tiles are placed on whole screen pixels, each meeting the next exactly, since a scaled layer leaves a seam
    // of background between tiles that end between pixels. The classic images scale with the layer.
    ground.style.transform = map.atlas ? 'none' : `translate(${-left * scale}px, ${-top * scale}px) scale(${scale})`;
    ground.style.width = map.atlas ? '100%' : `${map.worldWidth}px`;
    ground.style.height = map.atlas ? '100%' : `${map.worldHeight}px`;
    const screenX = (x: number): number => Math.round((x - left) * scale), screenY = (y: number): number => Math.round((y - top) * scale);
    const want = new Set<string>();
    const missing: [number, number, number][] = [];
    if (map.atlas) {
      const atlas = map.atlas;
      const granted = new Map(atlas.tiles.map(tile => [tile.id, tile.url]));
      // The coarsest level that still has a pixel for every screen pixel, else the finest.
      const level = atlas.levels.find(entry => entry.width >= map!.worldWidth * scale) ?? atlas.levels.at(-1)!;
      const unit = map.worldWidth / level.width * atlas.tileSize;
      const c0 = Math.max(0, Math.floor(left / unit)), c1 = Math.min(level.columns - 1, Math.floor((left + width / scale) / unit));
      const r0 = Math.max(0, Math.floor(top / unit)), r1 = Math.min(level.rows - 1, Math.floor((top + height / scale) / unit));
      for (let r = r0; r <= r1; r++)
        for (let c = c0; c <= c1; c++) {
          const id = `atlas.${level.level}.${c}.${r}`;
          want.add(id);
          let tile = tiles.get(id);
          if (!tile) {
            tile = document.createElement('img');
            tile.alt = '';
            tile.className = 'dagger-atlas-tile';
            tile.dataset.mediaId = id;
            tiles.set(id, tile);
            ground.append(tile);
          }
          const x0 = screenX(c * unit), y0 = screenY(r * unit);
          Object.assign(tile.style, { left: `${x0}px`, top: `${y0}px`, width: `${screenX((c + 1) * unit) - x0}px`, height: `${screenY((r + 1) * unit) - y0}px` });
          const source = granted.get(id);
          if (source && tile.getAttribute('src') !== source) tile.src = source;
          if (!source && !asked.has(id)) { asked.add(id); missing.push([level.level, c, r]); }
        }
    } else if (map.classic) {
      const pieces = [{ id: 'world', art: map.classic.world }, ...(scale >= ZOOM.region ? map.classic.sheets.map(sheet => ({ id: `sheet:${sheet.art.image}`, art: sheet.art })) : [])];
      for (const piece of pieces) {
        const art = piece.art;
        if (art.right < left || art.left > left + width / scale || art.bottom < top || art.top > top + height / scale) continue;
        want.add(piece.id);
        let picture = tiles.get(piece.id);
        if (!picture) {
          picture = document.createElement('img');
          picture.alt = '';
          picture.className = 'dagger-atlas-classic';
          Object.assign(picture.style, { left: `${art.left}px`, top: `${art.top}px`, width: `${art.right - art.left}px`, height: `${art.bottom - art.top}px` });
          tiles.set(piece.id, picture);
          ground.append(picture);
        }
        const source = image(art.image);
        if (source && picture.getAttribute('src') !== source) picture.src = source;
        reportMissingArt('Travel map', source ? [] : [art.image]);
      }
    }
    for (const [id, tile] of tiles) if (!want.has(id)) { tile.remove(); tiles.delete(id); }
    for (let i = 0; i < missing.length; i += TILES_PER_REQUEST) requestTiles(missing.slice(i, i + TILES_PER_REQUEST));
  };

  const marker = (className: string, x: number, y: number, text: string | null, labelText: string): HTMLElement => {
    const [sx, sy] = toScreen(x, y);
    const node = document.createElement('span');
    node.className = className;
    node.style.left = `${sx}px`;
    node.style.top = `${sy}px`;
    node.dataset.label = labelText;
    if (text !== null) node.dataset.name = text;
    return node;
  };

  const drawMarks = (): void => {
    const focusedKey = marks.contains(document.activeElement) ? (document.activeElement as HTMLElement).dataset.key ?? null : null;
    marks.replaceChildren();
    routeLayer.replaceChildren();
    if (!map) return;
    const { width, height } = size();
    const inView = (x: number, y: number, margin = 24): boolean => {
      const [sx, sy] = toScreen(x, y);
      return sx > -margin && sy > -margin && sx < width + margin && sy < height + margin;
    };
    const shown = (from: TravelMapNameProjection['shownFrom']): boolean =>
      from === 'world' || (from === 'region' ? scale >= ZOOM.region : scale >= ZOOM.close);
    // Region names across the whole bay and a region; close in, the places speak for themselves.
    if (scale < ZOOM.close)
      for (const region of map.regions) if (inView(region.x, region.y, 120)) {
        const node = marker('dagger-atlas-region', region.x, region.y, region.name, `${region.name}: ${region.discovered} known ${region.discovered === 1 ? 'place' : 'places'}`);
        node.textContent = region.name;
        marks.append(node);
      }
    for (const name of map.names) if (shown(name.shownFrom) && inView(name.x, name.y, 200)) {
      const node = marker('dagger-atlas-name', name.x, name.y, name.name, name.name);
      node.dataset.kind = name.kind;
      node.textContent = name.name;
      marks.append(node);
    }
    // Places: a region shows its cities, close in its towns and great places, and closest every place. A name is written
    // where it does not cover one already written, the most important first; the rest are named when pointed at.
    const written: [number, number, number, number][] = [];
    const room = (sx: number, sy: number, text: string): boolean => {
      const box: [number, number, number, number] = [sx + 6, sy - 9, sx + 10 + text.length * 7.5, sy + 9];
      if (written.some(([l, t, r, b]) => box[0] < r && box[2] > l && box[1] < b && box[3] > t)) return false;
      written.push(box);
      return true;
    };
    const marked = (tier: number): boolean => scale >= (tier === 1 ? ZOOM.region : tier === 2 ? ZOOM.close : ZOOM.closest);
    if (scale >= ZOOM.region || selectedKey)
      for (const place of places) {
        const key = `${place.region}:${place.index}`;
        const chosen = key === selectedKey;
        if (!chosen && (!marked(place.tier) || !inView(place.x + .5, place.y + .5))) continue;
        const [px, py] = toScreen(place.x + .5, place.y + .5);
        const named = chosen || room(px, py, place.name);
        const [sx, sy] = toScreen(place.x + .5, place.y + .5);
        const button = document.createElement('button');
        button.type = 'button';
        button.className = 'dagger-atlas-place';
        button.dataset.category = place.category;
        button.dataset.tier = String(place.tier);
        button.dataset.key = key;
        button.dataset.x = String(place.x + .5);
        button.dataset.y = String(place.y + .5);
        button.dataset.label = `${place.name} (${place.kind})`;
        button.setAttribute('aria-label', button.dataset.label);
        if (named) button.dataset.name = place.name;
        if (chosen) { button.setAttribute('aria-pressed', 'true'); readout.dataset.chosen = button.dataset.label; }
        button.style.left = `${sx}px`;
        button.style.top = `${sy}px`;
        // A click with no pointer (Enter, Space or a pad's accept) is a keyed choice.
        button.addEventListener('click', event => choose(place.region, place.index, event.detail === 0));
        marks.append(button);
      }
    if (route) {
      const [x1, y1] = toScreen(route.fromX + .5, route.fromY + .5), [x2, y2] = toScreen(route.toX + .5, route.toY + .5);
      routeLayer.setAttribute('viewBox', `0 0 ${width} ${height}`);
      const line = document.createElementNS(SVG, 'line');
      for (const [name, value] of [['x1', x1], ['y1', y1], ['x2', x2], ['y2', y2]] as const) line.setAttribute(name, String(value));
      // The window's Servo draws SVG from its markup alone, without the page's stylesheet.
      for (const [name, value] of [['stroke', '#7a1f12'], ['stroke-width', '2.5'], ['stroke-dasharray', '7 5'], ['stroke-linecap', 'round']] as const) line.setAttribute(name, value);
      routeLayer.append(line);
    }
    if (map.player) {
      const node = marker('dagger-atlas-player', map.player.x + .5, map.player.y + .5, null, 'You are here');
      node.setAttribute('role', 'img');
      node.setAttribute('aria-label', 'You are here');
      if (map.player.heading !== null) node.style.setProperty('--heading', `${map.player.heading}deg`);
      marks.append(node);
    }
    if (focusedKey) Array.from(marks.querySelectorAll<HTMLElement>('[data-key]')).find(node => node.dataset.key === focusedKey)?.focus({ preventScroll: true });
  };

  const drawLegend = (): void => {
    legend.replaceChildren();
    if (!map) return;
    const key = (swatch: string, text: string, category?: string): HTMLLIElement => {
      const item = document.createElement('li');
      const mark = document.createElement('span');
      mark.className = `dagger-atlas-key dagger-atlas-key-${swatch}`;
      if (category) mark.dataset.category = category;
      item.append(mark, text);
      return item;
    };
    legend.append(key('player', 'You are here'));
    if (route) legend.append(key('route', 'Route'));
    for (const row of legendRows) legend.append(key('place', row.label, row.category));
  };

  let signature = '';
  const draw = (): void => {
    measure();
    clampView();
    element.dataset.zoom = scale >= ZOOM.close ? 'close' : scale >= ZOOM.region ? 'region' : 'world';
    drawGround();
    drawMarks();
  };

  const atlas: Atlas = {
    element,
    place: key => places.find(entry => `${entry.region}:${entry.index}` === key),
    update(next, selected, nextRoute): void {
      // Held most important first, the order names are written in.
      if (next?.places) { places = [...next.places].sort((a, b) => a.tier - b.tier); legendRows = next.legend ?? []; placesRevision = next.placesRevision; }
      else if (next && next.placesRevision !== placesRevision && next.placesRevision !== askedRevision) { askedRevision = next.placesRevision; askPlaces(); }
      if (!next) { places = []; legendRows = []; placesRevision = askedRevision = ''; }
      // The places are compared by their revision, so a snapshot without them is the same map as the one that brought them.
      const changed = JSON.stringify([next && { ...next, places: undefined, legend: undefined }, placesRevision, selected, nextRoute]);
      element.hidden = next === null;
      if (changed === signature) { drawGround(); return; }
      const opened = map === null && next !== null;
      signature = changed;
      map = next; selectedKey = selected; route = nextRoute;
      if (!map) { fitted = false; asked.clear(); draw(); drawLegend(); return; }
      measure();
      // The map opens on the player, near enough to see their region.
      if (opened || !fitted) {
        fitted = true;
        const player = map.player;
        if (player) setView(player.x + .5, player.y + .5, ZOOM.region); else setView(map.worldWidth / 2, map.worldHeight / 2, fitScale());
      } else { clampView(); }
      drawLegend();
      draw();
    },
    zoom(direction): void {
      if (!map) return;
      setView(cx, cy, scale * (direction > 0 ? STEP : 1 / STEP));
    },
    centre(x, y, zoom): void { setView(x, y, Math.max(scale, zoom ?? ZOOM.close)); },
    refreshArt(): void { if (map) drawGround(); },
  };
  return atlas;
}
