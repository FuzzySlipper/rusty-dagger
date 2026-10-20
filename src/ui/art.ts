/// How an authored kit image is drawn, as its author stated it in data/ui-authored-assets.json: CSS pixels
/// per image pixel at UI scale 1, 9-slice insets (top, right, bottom, left) and a cursor hotspot (x, y), both in
/// image pixels. A frame's CSS `border-image-slice` takes the insets as they are and its `border-image-width`
/// is the insets times the scale.
export interface UiArtDisplay {
  readonly scale: number;
  /// Four insets: top, right, bottom, left.
  readonly slice: readonly number[] | null;
  /// Two values: x, y.
  readonly hotspot: readonly number[] | null;
}

/// The published UI art a snapshot carried, keyed by the media identity the pack owns.
export interface UiArtImage {
  readonly id: string;
  readonly image: string;
  readonly display?: UiArtDisplay;
}

export interface UiArt {
  readonly revision: string;
  readonly images: readonly UiArtImage[];
}

/// The one player action this module sends: the DOM has no art for the revision a snapshot named.
export type ArtRequestAction = {
  readonly action: 'art-request';
  readonly revision: string;
};

const images = new Map<string, string>();
const displays = new Map<string, UiArtDisplay>();
let revision = '';
let demandedRevision = '';

const numbers = (value: unknown, count: number): number[] | null =>
  Array.isArray(value) && value.length === count && value.every(entry => Number.isFinite(entry) && entry >= 0) ? value as number[] : null;

function readDisplay(value: unknown): UiArtDisplay | null {
  if (typeof value !== 'object' || value === null) return null;
  const block = value as { scale?: unknown; slice?: unknown; hotspot?: unknown };
  if (typeof block.scale !== 'number' || !(block.scale > 0)) return null;
  return {
    scale: block.scale,
    slice: numbers(block.slice, 4),
    hotspot: numbers(block.hotspot, 2),
  };
}

/// Adopts a published art block and returns the revision now held, so a caller can tell whether the
/// snapshot brought art it did not have.
export function adopt(value: unknown): string {
  if (typeof value !== 'object' || value === null) return revision;
  const block = value as { revision?: unknown; images?: unknown };
  if (typeof block.revision !== 'string' || !Array.isArray(block.images)) return revision;
  const next = new Map<string, string>();
  const nextDisplays = new Map<string, UiArtDisplay>();
  for (const entry of block.images as readonly UiArtImage[]) {
    if (typeof entry?.id === 'string' && typeof entry?.image === 'string' && entry.image.length > 0) {
      next.set(entry.id, entry.image);
      const drawn = readDisplay(entry.display);
      if (drawn !== null) nextDisplays.set(entry.id, drawn);
    }
  }

  images.clear();
  displays.clear();
  for (const [id, image] of next) images.set(id, image);
  for (const [id, drawn] of nextDisplays) displays.set(id, drawn);
  revision = block.revision;
  // The demanded images were part of the map just replaced, so the DOM no longer holds them.
  demandedRevision = '';
  return revision;
}

/// Adopts the block of images a projection demanded - art read on first use, such as the paper doll's worn
/// items - into the held art, and returns the demanded revision now held. The block is cumulative, so
/// adopting it never removes an image.
export function adoptDemanded(value: unknown): string {
  if (typeof value !== 'object' || value === null) return demandedRevision;
  const block = value as { revision?: unknown; images?: unknown };
  if (typeof block.revision !== 'string' || !Array.isArray(block.images)) return demandedRevision;
  for (const entry of block.images as readonly UiArtImage[]) {
    if (typeof entry?.id === 'string' && typeof entry?.image === 'string' && entry.image.length > 0) images.set(entry.id, entry.image);
  }
  demandedRevision = block.revision;
  return demandedRevision;
}

/// The revision of the demanded images currently held, which is the empty string before any block arrives.
export function heldDemandedRevision(): string {
  return demandedRevision;
}

/// The revision of the art currently held, which is the empty string before any block arrives.
export function heldRevision(): string {
  return revision;
}

/// The data URL for one published media identity, or null when this session has not published it.
export function image(id: string | null): string | null {
  return id === null ? null : images.get(id) ?? null;
}

/// How a published kit image is drawn, or null when the image is not authored kit art or not published.
export function display(id: string | null): UiArtDisplay | null {
  return id === null ? null : displays.get(id) ?? null;
}

const lastReportedMissing = new Map<string, string>();

/**
 * Reports missing frame art only when the missing set changes and is non-empty. Steady-state
 * renders stay silent (a hidden panel re-renders every snapshot; mounted panels repaint on
 * every projection), while newly missing art still warns once. Callers keep their
 * data-art-missing attributes current on every render regardless.
 */
export function reportMissingArt(context: string, missing: readonly string[]): void {
  // Mount precedes the first art publication; awaiting that block is ordinary startup.
  if (revision.length === 0) return;
  const key = [...missing].sort().join('\0');
  if (key === lastReportedMissing.get(context)) return;
  lastReportedMissing.set(context, key);
  if (missing.length !== 0) console.warn(`${context} frame art is not published by this session: ${[...missing].sort().join(', ')}`);
}

/// A published image as a CSS `url()` value. The source is quoted and escaped, so a character that ends
/// the string or the declaration cannot change what the property says.
export function cssUrl(source: string): string {
  return `url("${source.replace(/["\\\n\r\f]/g, character => `\\${character.charCodeAt(0).toString(16)} `)}")`;
}

/**
 * Sizes a classic-art image to fill as much of `box` as a whole number of device pixels per source
 * pixel allows, so the art is enlarged without filtering or uneven pixels. Art larger than its box is
 * shrunk to fit instead, as no whole scale would. Call once per image; the size follows the box and
 * each newly loaded source.
 */
export function fitPixelArt(art: HTMLImageElement, box: HTMLElement): void {
  art.style.imageRendering = 'pixelated';
  const fit = (): void => {
    const width = art.naturalWidth, height = art.naturalHeight;
    if (width === 0 || height === 0 || box.clientWidth === 0 || box.clientHeight === 0) return;
    const ratio = window.devicePixelRatio || 1;
    const room = Math.min(box.clientWidth * ratio / width, box.clientHeight * ratio / height);
    const scale = room >= 1 ? Math.floor(room) / ratio : room / ratio;
    art.style.width = `${width * scale}px`;
    art.style.height = `${height * scale}px`;
  };
  art.addEventListener('load', fit);
  new ResizeObserver(fit).observe(box);
  fit();
}
