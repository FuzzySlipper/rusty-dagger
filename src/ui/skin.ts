import { display, image, reportMissingArt, type UiArtDisplay } from './art.js';

/**
 * The skin: the published UI kit as CSS custom properties on the document root, set once each time
 * the art arrives. `skin.css` draws every primitive (frame, panel, title plate, divider, button, tab,
 * slot, scrollbar, cursor, icons) from these properties and keeps a plain-CSS look when one is absent,
 * so a screen never sets frame art of its own (`docs/game-ui.md`, "Skin").
 *
 * A kit image `ui.<family>.<name>[.<state>].v2` becomes `--skin-<family>-<name>[-<state>]` (its
 * `url()`), with `-slice` (its 9-slice insets in image pixels, for `border-image-slice`), `-width`
 * (those insets in rem at its display scale, for `border-image-width` and the border) and `-size`
 * (its whole size in rem, for backgrounds, icons and textures).
 */

/** The images the primitives draw; a missing one is reported and its primitive keeps the plain look. */
export const SKIN_IMAGES = Object.freeze([
  'ui.stone.window.v2', 'ui.stone.panel.v2', 'ui.stone.title.v2', 'ui.stone.divider.v2', 'ui.stone.texture.v2',
  'ui.stone.button.v2', 'ui.stone.button.focus.v2', 'ui.stone.button.pressed.v2', 'ui.stone.button.disabled.v2',
  'ui.stone.tab.v2', 'ui.stone.tab.active.v2', 'ui.stone.scroll-track.v2', 'ui.stone.scroll-thumb.v2',
  'ui.stone.slot.v2', 'ui.stone.slot.focus.v2', 'ui.stone.slot.invalid.v2',
  'ui.parchment.window.v2', 'ui.parchment.texture.v2',
  'ui.cursor.normal.v2', 'ui.cursor.pointer.v2', 'ui.cursor.grab.v2',
]);

/** The root font size the Engine sets at UI scale 1; rem values are CSS pixels over this. */
const REM_PIXELS = 16;

/** The CSS custom property a kit image is published under, or null for an identity outside the kit. */
export function skinProperty(id: string): string | null {
  const match = /^ui\.([a-z0-9-]+(?:\.[a-z0-9-]+)*)\.v\d+$/.exec(id);
  return match ? `--skin-${match[1]!.replace(/\./g, '-')}` : null;
}

/** The width and height a PNG data URL states in its header, or null when it is not one. */
export function pngSize(source: string): { width: number; height: number } | null {
  const comma = source.indexOf(',');
  if (!source.startsWith('data:image/png;base64,') || comma < 0) return null;
  let header: string;
  try { header = atob(source.slice(comma + 1, comma + 1 + 44)); } catch { return null; }
  if (header.length < 24 || header.slice(12, 16) !== 'IHDR') return null;
  const word = (offset: number): number =>
    ((header.charCodeAt(offset) << 24) | (header.charCodeAt(offset + 1) << 16) | (header.charCodeAt(offset + 2) << 8) | header.charCodeAt(offset + 3)) >>> 0;
  return { width: word(16), height: word(20) };
}

const rem = (pixels: number): string => `${+(pixels / REM_PIXELS).toFixed(4)}rem`;

/**
 * A kit cursor as a CSS cursor value: the 2x image at its display size with the hotspot scaled to
 * match, falling back to `fallback` where image cursors are not drawn (the desktop window today, #10104).
 */
function cursorValue(source: string, drawn: UiArtDisplay, fallback: string): string {
  const [x, y] = drawn.hotspot ?? [0, 0];
  const density = +(1 / drawn.scale).toFixed(4);
  const hotspot = `${Math.round(x! * drawn.scale)} ${Math.round(y! * drawn.scale)}`;
  return `image-set(url("${source}") ${density}x) ${hotspot}, ${fallback}`;
}

const CURSORS: readonly (readonly [string, string, string])[] = [
  ['ui.cursor.normal.v2', '--dagger-cursor', 'default'],
  ['ui.cursor.pointer.v2', '--dagger-cursor-pointer', 'pointer'],
  ['ui.cursor.grab.v2', '--dagger-cursor-grab', 'grabbing'],
];

/** Every property the last application set, so a new art block clears what it no longer carries. */
const applied = new Set<string>();

/**
 * Publishes the held kit as custom properties on `target` (the document root by default) and
 * reports any primitive image this session did not publish. Call after each adopted art block.
 */
export function applySkin(target: HTMLElement = document.documentElement, ids: Iterable<string> = kitIds()): void {
  const next = new Map<string, string>();
  for (const id of ids) {
    const property = skinProperty(id);
    const source = image(id);
    if (property === null || source === null) continue;
    next.set(property, `url("${source}")`);
    const drawn = display(id);
    const scale = drawn?.scale ?? 0.5;
    const size = pngSize(source);
    if (size !== null) next.set(`${property}-size`, `${rem(size.width * scale)} ${rem(size.height * scale)}`);
    if (drawn?.slice) {
      next.set(`${property}-slice`, drawn.slice.join(' '));
      next.set(`${property}-width`, drawn.slice.map(inset => rem(inset * scale)).join(' '));
    }
  }
  for (const [id, property, fallback] of CURSORS) {
    const source = image(id);
    const drawn = display(id);
    if (source !== null && drawn !== null) next.set(property, cursorValue(source, drawn, fallback));
  }
  // A face colour a skinned primitive drops: the plain-CSS fill would show through its art's corners.
  if (next.has('--skin-stone-button')) next.set('--skin-button-face', 'transparent');
  if (next.has('--skin-stone-slot')) next.set('--skin-slot-face', 'transparent');

  for (const property of applied) if (!next.has(property)) target.style.removeProperty(property);
  applied.clear();
  for (const [property, value] of next) { target.style.setProperty(property, value); applied.add(property); }
  target.toggleAttribute('data-skin', next.size > 0);
  // The equipment silhouettes this art block holds: an empty slot drops its plain marker only for these,
  // so a slot whose silhouette is missing still shows what it holds.
  const silhouettes = ICONS.filter(id => id.startsWith('ui.icon.equip.') && image(id) !== null)
    .map(id => id.slice('ui.icon.equip.'.length).replace(/\.v\d+$/, ''));
  if (silhouettes.length > 0) target.dataset.skinEquip = silhouettes.join(' ');
  else delete target.dataset.skinEquip;
  reportMissingArt('skin', kitIds().filter(id => image(id) === null));
}

/** The kit identities this session holds: the primitives plus every icon the art block carries. */
function kitIds(): string[] {
  return [...SKIN_IMAGES, ...ICONS];
}

/** The icons `skin.css` places by semantic hook (menu actions, item categories, activation modes, equipment slots). */
export const ICONS = Object.freeze([
  ...['inventory', 'character', 'spells', 'magic-items', 'journal', 'notebook', 'map', 'rest', 'travel', 'save', 'load', 'settings']
    .map(name => `ui.icon.menu.${name}.v2`),
  ...['weapons-armor', 'magic-items', 'clothing-misc', 'ingredients', 'gold', 'all'].map(name => `ui.icon.category.${name}.v2`),
  ...['steal', 'grab', 'info', 'talk'].map(name => `ui.icon.activation.${name}.v2`),
  ...['head', 'arm', 'cloak', 'chest-clothes', 'chest-armor', 'gloves', 'weapon', 'shield', 'legs-armor', 'legs-clothes', 'feet',
    'bracer', 'amulet', 'bracelet', 'ring', 'mark', 'crystal'].map(name => `ui.icon.equip.${name}.v2`),
]);
