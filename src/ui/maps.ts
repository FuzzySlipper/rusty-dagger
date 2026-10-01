/** A top-down diagram of admitted map data. Controls own presentation only. */
export interface MapProjection {
  readonly id: string; readonly name: string; readonly kind: 'city' | 'dungeon';
  readonly region: number | null; readonly location: number | null;
  readonly player: { readonly x: number; readonly y: number; readonly z: number; readonly yaw: number };
  readonly areas: readonly { readonly id: string; readonly minX: number; readonly minZ: number; readonly maxX: number; readonly maxZ: number; readonly minY: number; readonly maxY: number; readonly kind: number }[];
  readonly labels: readonly { readonly id: string; readonly name: string; readonly x: number; readonly y: number; readonly z: number; readonly selected: boolean }[];
}
export interface MapAction { readonly action: 'map-building'; readonly region: number; readonly destination: number; readonly item: string; }
export function isMapProjection(value: unknown): value is MapProjection {
  if (!value || typeof value !== 'object') return false;
  const map = value as Partial<MapProjection>;
  return typeof map.id === 'string' && typeof map.name === 'string' && (map.kind === 'city' || map.kind === 'dungeon')
    && !!map.player && [map.player.x, map.player.y, map.player.z, map.player.yaw].every(Number.isFinite)
    && Array.isArray(map.areas) && map.areas.every(area => typeof area.id === 'string' && [area.minX, area.minZ, area.maxX, area.maxZ, area.minY, area.maxY, area.kind].every(Number.isFinite))
    && Array.isArray(map.labels) && map.labels.every(label => typeof label.id === 'string' && typeof label.name === 'string' && [label.x, label.y, label.z].every(Number.isFinite))
    && (map.kind !== 'city' || (Number.isInteger(map.region) && Number.isInteger(map.location)));
}
const ns = 'http://www.w3.org/2000/svg';
function svgElement(name: string, attributes: Record<string, string>): SVGElement {
  const element = document.createElementNS(ns, name);
  for (const [key, value] of Object.entries(attributes)) element.setAttribute(key, value);
  return element;
}
export function mountMap(root: HTMLElement, send: (action: MapAction) => void, travel: () => void): { update(value: MapProjection | null): void; dispose(): void } {
  const shell = document.createElement('section'); shell.className = 'dagger-map';
  const heading = document.createElement('h2');
  const controls = document.createElement('div'); controls.className = 'dagger-map-controls';
  const status = document.createElement('p'); status.setAttribute('role', 'status');
  const levelLabel = document.createElement('label'); levelLabel.textContent = 'Height slice (metres) ';
  const level = document.createElement('input'); level.type = 'number'; level.step = '0.5'; level.className = 'dagger-map-level';
  const allLabel = document.createElement('label'); allLabel.textContent = 'All levels ';
  const all = document.createElement('input'); all.type = 'checkbox'; all.checked = true; allLabel.append(all); levelLabel.append(level);
  const svg = svgElement('svg', { viewBox: '-160 -160 320 320', role: 'img', 'aria-label': 'Explored map and current player position' });
  svg.classList.add('dagger-map-diagram');
  const viewport = svgElement('g', {}); svg.append(viewport);
  const buildings = document.createElement('div'); buildings.className = 'dagger-map-buildings';
  const target = document.createElement('p'); target.className = 'dagger-map-target';
  let drawn = '';
  let current: MapProjection | null = null, identity = '', centerX = 0, centerZ = 0, zoom = 1, rotation = 0, playerUp = false;
  function draw(): void {
    drawn = '';
    viewport.replaceChildren(); buildings.replaceChildren();
    if (!current) { status.textContent = 'No map is available in this interior.'; return; }
    const value = current;
    const angle = playerUp ? -value.player.yaw * 180 / Math.PI : rotation;
    viewport.setAttribute('transform', `scale(${zoom}) rotate(${angle}) translate(${-centerX} ${-centerZ})`);
    const slice = Number(level.value), showAll = value.kind === 'city' || all.checked;
    for (const area of value.areas) {
      if (!showAll && (area.minY > slice || area.maxY < slice)) continue;
      const rect = svgElement('rect', { x: String(area.minX), y: String(area.minZ), width: String(Math.max(.1, area.maxX - area.minX)), height: String(Math.max(.1, area.maxZ - area.minZ)), 'data-id': area.id, 'data-kind': String(area.kind), fill: value.kind === 'dungeon' ? (area.kind === 2 ? '#c49b55' : '#718694') : (area.kind === 16 ? '#d99845' : area.kind === 12 || area.kind === 15 ? '#9879b8' : '#697f94') });
      viewport.append(rect);
    }
    for (const label of value.labels) {
      if (!showAll && Math.abs(label.y - slice) > 1) continue;
      const point = svgElement('circle', { cx: String(label.x), cy: String(label.z), r: String(3 / zoom), fill: label.selected ? '#ffe077' : '#d5e2e7', 'data-label-id': label.id });
      const title = svgElement('title', {}); title.textContent = label.name; point.append(title); viewport.append(point);
      if (value.kind === 'city' && value.region !== null && value.location !== null) {
        const button = document.createElement('button'); button.type = 'button'; button.textContent = label.name; button.dataset.building = label.id;
        button.setAttribute('aria-pressed', String(label.selected));
        const region = value.region, destination = value.location;
        button.addEventListener('click', () => send({ action: 'map-building', region, destination, item: label.id }));
        point.setAttribute('role', 'button'); point.setAttribute('tabindex', '0'); point.setAttribute('aria-label', label.name);
        const select = (): void => send({ action: 'map-building', region, destination, item: label.id });
        point.addEventListener('click', select); point.addEventListener('keydown', event => { if (event instanceof KeyboardEvent && (event.key === 'Enter' || event.key === ' ')) { event.preventDefault(); select(); } });
        buildings.append(button);
      } else {
        const text = document.createElement('p'); text.textContent = label.name; buildings.append(text);
      }
    }
    const player = svgElement('path', { d: 'M 0 -6 L 4 5 L 0 2 L -4 5 Z', fill: '#73ff98', transform: `translate(${value.player.x} ${value.player.z}) rotate(${value.player.yaw * 180 / Math.PI}) scale(${1 / zoom})`, 'data-player': 'true' });
    viewport.append(player);
    status.textContent = `${value.kind === 'dungeon' ? 'Explored dungeon' : 'City'} · ${showAll ? 'all levels' : `height ${slice} m`} · player ${value.player.x.toFixed(1)}, ${value.player.y.toFixed(1)}, ${value.player.z.toFixed(1)} m`;
    drawn = JSON.stringify([value.id, value.kind, value.areas, value.labels, value.region, value.location]);
    target.textContent = value.labels.find(label => label.selected)?.name ? `Directions target: ${value.labels.find(label => label.selected)!.name}` : '';
  }
  const button = (label: string, action: () => void): void => {
    const element = document.createElement('button'); element.type = 'button'; element.textContent = label;
    element.addEventListener('click', () => { action(); draw(); }); controls.append(element);
  };
  button('Pan left', () => { centerX -= 20 / zoom; }); button('Pan right', () => { centerX += 20 / zoom; });
  button('Pan north', () => { centerZ -= 20 / zoom; }); button('Pan south', () => { centerZ += 20 / zoom; });
  button('Zoom in', () => { zoom = Math.min(100, zoom * 1.5); }); button('Zoom out', () => { zoom = Math.max(.01, zoom / 1.5); });
  button('Rotate left', () => { playerUp = false; rotation -= 45; }); button('Rotate right', () => { playerUp = false; rotation += 45; });
  button('North up', () => { playerUp = false; rotation = 0; }); button('Player up', () => { playerUp = true; });
  button('Center player', () => { if (current) { centerX = current.player.x; centerZ = current.player.z; level.value = String(current.player.y); } });
  button('Level up', () => { all.checked = false; level.value = String(Number(level.value) + .5); });
  button('Level down', () => { all.checked = false; level.value = String(Number(level.value) - .5); });
  button('Travel destinations', travel);
  level.addEventListener('change', () => { all.checked = false; draw(); }); all.addEventListener('change', draw);
  controls.append(levelLabel, allLabel); shell.append(heading, controls, status, svg, target, buildings); root.append(shell);
  return {
    update(value): void {
      current = value; controls.hidden = buildings.hidden = value === null; svg.style.display = value === null ? 'none' : ''; heading.textContent = value ? `${value.name} map` : 'Map';
      if (value && identity !== `${value.kind}:${value.id}`) {
        identity = `${value.kind}:${value.id}`; centerX = value.player.x; centerZ = value.player.z; rotation = 0; playerUp = false; all.checked = true; level.value = String(value.player.y);
        const extent = Math.max(40, ...value.areas.flatMap(area => [Math.abs(area.minX - centerX), Math.abs(area.maxX - centerX), Math.abs(area.minZ - centerZ), Math.abs(area.maxZ - centerZ)]));
        zoom = Math.max(.01, 140 / extent);
      }
      levelLabel.hidden = value?.kind !== 'dungeon'; allLabel.hidden = value?.kind !== 'dungeon';
      const signature = value ? JSON.stringify([value.id, value.kind, value.areas, value.labels, value.region, value.location]) : '';
      if (value && drawn === signature) {
        const angle = playerUp ? -value.player.yaw * 180 / Math.PI : rotation;
        viewport.setAttribute('transform', `scale(${zoom}) rotate(${angle}) translate(${-centerX} ${-centerZ})`);
        viewport.querySelector('[data-player]')?.setAttribute('transform', `translate(${value.player.x} ${value.player.z}) rotate(${value.player.yaw * 180 / Math.PI}) scale(${1 / zoom})`);
        const showAll = value.kind === 'city' || all.checked;
        status.textContent = `${value.kind === 'dungeon' ? 'Explored dungeon' : 'City'} · ${showAll ? 'all levels' : `height ${level.value} m`} · player ${value.player.x.toFixed(1)}, ${value.player.y.toFixed(1)}, ${value.player.z.toFixed(1)} m`;
      } else draw();
    },
    dispose(): void { shell.remove(); },
  };
}
