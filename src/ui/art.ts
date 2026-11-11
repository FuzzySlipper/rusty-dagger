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
  /// Refreshed art, drawn smoothed; classic art keeps its pixels square.
  readonly smooth?: boolean;
}

export interface UiArt {
  readonly revision: string;
  readonly images: readonly UiArtImage[];
}

/// The demanded block: the images from `from` on, so a page that holds the earlier ones takes only what is new.
export interface UiArtDemanded extends UiArt {
  readonly from: number;
}

/// The one player action this module sends: the DOM has no art for the revision a snapshot named.
export type ArtRequestAction = {
  readonly action: 'art-request';
  readonly revision: string;
};

const images = new Map<string, string>();
const displays = new Map<string, UiArtDisplay>();
const smoothed = new Set<string>();
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
  const nextSmoothed = new Set<string>();
  for (const entry of block.images as readonly UiArtImage[]) {
    if (typeof entry?.id === 'string' && typeof entry?.image === 'string' && entry.image.length > 0) {
      next.set(entry.id, entry.image);
      if (entry.smooth === true) nextSmoothed.add(entry.id);
      const drawn = readDisplay(entry.display);
      if (drawn !== null) nextDisplays.set(entry.id, drawn);
    }
  }

  images.clear();
  displays.clear();
  smoothed.clear();
  for (const id of nextSmoothed) smoothed.add(id);
  for (const [id, image] of next) images.set(id, image);
  for (const [id, drawn] of nextDisplays) displays.set(id, drawn);
  revision = block.revision;
  // The demanded images were part of the map just replaced, so the DOM no longer holds them.
  demandedRevision = '';
  return revision;
}

/// Adopts the block of images a projection demanded - art read on first use, such as the paper doll's worn
/// items or the atlas's tiles - into the held art, and returns the demanded revision now held. The block is
/// cumulative, so adopting it never removes an image. It carries the images from `from` on: a block that
/// continues from a count this page does not hold is not adopted, and the revision check asks for it whole.
export function adoptDemanded(value: unknown): string {
  if (typeof value !== 'object' || value === null) return demandedRevision;
  const block = value as Partial<Record<keyof UiArtDemanded, unknown>> & Partial<UiArtDemanded>;
  if (typeof block.revision !== 'string' || !Array.isArray(block.images)) return demandedRevision;
  const from = typeof block.from === 'number' && Number.isInteger(block.from) && block.from > 0 ? block.from : 0;
  const publication = block.revision.slice(0, block.revision.lastIndexOf('.'));
  if (from > 0 && demandedRevision !== `${publication}.${from}`) return demandedRevision;
  for (const entry of block.images as readonly UiArtImage[]) {
    if (typeof entry?.id === 'string' && typeof entry?.image === 'string' && entry.image.length > 0) {
      images.set(entry.id, entry.image);
      if (entry.smooth === true) smoothed.add(entry.id);
    }
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

/// The image each full screen is drawn with in the art set drawn, as the HUD names it: a refreshed painting, or the
/// classic screen itself (also when the HUD has named none).
let screens: Readonly<Record<string, string>> = {};
export function adoptScreens(value: unknown): void {
  if (typeof value === 'object' && value !== null && Object.values(value).every(id => typeof id === 'string')) screens = value as Record<string, string>;
}
export function drawnScreen(id: string): string {
  return screens[id] ?? id;
}
/// Whether an image is refreshed art, drawn smoothed and at the size of the place it fills.
export function isRefreshed(id: string | null): boolean {
  return id !== null && smoothed.has(id);
}

/// Draws an image smoothed when it is refreshed art, overriding the square pixels the classic art's styles ask for.
export function smoothIfRefreshed(element: HTMLElement, id: string | null): void {
  element.style.imageRendering = id !== null && smoothed.has(id) ? 'auto' : '';
}

/// How refreshed art is drawn in a dye or metal: the colour, a brightness the multiply cannot reach, and the mask of
/// what it recolours.
export interface ArtTint { readonly color: string; readonly brightness: number; readonly mask: string; }

const load = (source: string): Promise<HTMLImageElement> => new Promise((resolve, reject) => {
  const picture = document.createElement('img');
  picture.onload = () => resolve(picture);
  picture.onerror = () => reject(new Error('The image did not decode.'));
  picture.src = source;
});

/**
 * Neutral clothing in a dye, or bare steel in a metal: where the mask is opaque (the cloth or steel the dye or metal
 * recolours) each pixel is brightened and multiplied by the colour, and the rest keeps its own colour. A canvas makes
 * it as one image; with no canvas to draw on, the neutral image stands (null).
 */
async function dyed(source: string, mask: string, color: string, brightness: number): Promise<string | null> {
  const [art, cloth] = await Promise.all([load(source), load(mask)]);
  const canvas = document.createElement('canvas');
  canvas.width = art.naturalWidth;
  canvas.height = art.naturalHeight;
  const context = canvas.getContext('2d', { willReadFrequently: true });
  if (context === null) return null;
  context.drawImage(cloth, 0, 0, canvas.width, canvas.height);
  const weights = context.getImageData(0, 0, canvas.width, canvas.height).data;
  context.clearRect(0, 0, canvas.width, canvas.height);
  context.drawImage(art, 0, 0);
  const pixels = context.getImageData(0, 0, canvas.width, canvas.height);
  const tint = [1, 3, 5].map(at => parseInt(color.slice(at, at + 2), 16) / 255 * brightness);
  const data = pixels.data;
  for (let at = 0; at < data.length; at += 4) {
    const weight = weights[at + 3] / 255;
    for (let channel = 0; channel < 3; channel++) data[at + channel] = Math.min(255, data[at + channel] * (1 - weight + weight * tint[channel]));
  }
  context.putImageData(pixels, 0, 0);
  return canvas.toDataURL('image/png');
}

const tintedImages = new Map<string, Promise<string | null>>();
/// The latest draw asked of each element, so a tinted image that finishes after the element was redrawn is dropped.
const latestDraw = new WeakMap<HTMLImageElement, object>();

/**
 * Draws a published image on an element: smoothed when it is refreshed art, and in a dye or metal when it carries a
 * tint. A tinted image is held hidden (`data-art-missing`) until its mask has arrived and the tinted image is made,
 * once per image and tint; one that does not decode leaves the neutral image standing rather than nothing, and one that
 * finishes after the element was drawn again is dropped. Returns whether the image is published.
 */
export function drawArt(element: HTMLImageElement, id: string | null, tint: ArtTint | null = null): boolean {
  const request = {};
  latestDraw.set(element, request);
  const source = image(id);
  smoothIfRefreshed(element, id);
  const mask = tint === null ? null : image(tint.mask);
  const key = tint === null ? '' : `${id}|${tint.mask}|${tint.color}|${tint.brightness}`;
  if (tint !== null) element.dataset.dye = tint.color;
  if (source === null) { element.removeAttribute('src'); element.dataset.artMissing = 'true'; return false; }
  if (tint === null) {
    if (element.src !== source) element.src = source;
    delete element.dataset.artMissing;
    return true;
  }
  element.removeAttribute('src');
  element.dataset.artMissing = 'true';
  if (mask === null) return true;
  if (!tintedImages.has(key)) tintedImages.set(key, dyed(source, mask, tint.color, tint.brightness));
  void tintedImages.get(key)!.catch(() => null).then(result => {
    if (latestDraw.get(element) !== request) return;
    element.src = result ?? source;
    delete element.dataset.artMissing;
  });
  return true;
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
  const fit = (): void => {
    // A refreshed painting is not pixel art: it fills its box as the stylesheet lays it out.
    if (art.dataset.refreshed !== undefined) { art.style.imageRendering = ''; art.style.width = ''; art.style.height = ''; return; }
    art.style.imageRendering = 'pixelated';
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

/// Shows a full screen in the art set drawn: the image the HUD names for it, marked when it is a refreshed painting.
export function showScreen(art: HTMLImageElement, id: string): void {
  const drawn = drawnScreen(id);
  const source = image(drawn);
  art.dataset.screen = id;
  if (isRefreshed(drawn)) art.dataset.refreshed = ''; else delete art.dataset.refreshed;
  // A refreshed and a classic screen are different images, so the switch loads one and fitPixelArt lays it out again.
  if (source !== null && art.src !== source) art.src = source;
}
