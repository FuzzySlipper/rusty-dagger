import { image } from './art.js';

/** One frame's rect in a glint's atlas, in pixels. */
export interface GlintFrame {
  readonly x: number;
  readonly y: number;
  readonly width: number;
  readonly height: number;
}

/**
 * One of the inventory window's looping glints: its atlas image and size, each frame's rect in it, how many frames it
 * turns a second, and where the classic frame sits in a frame (fractions, top-down), which the item's slot fills.
 */
export interface Glint {
  readonly media: string;
  readonly width: number;
  readonly height: number;
  readonly frames: readonly GlintFrame[];
  readonly framesPerSecond: number;
  readonly slot: GlintFrame;
}

/** The magic shimmer drawn over enchanted items, and the coin glint drawn behind stock the player may steal. */
export interface Glints {
  readonly magic: Glint;
  readonly coins: Glint;
}

export type GlintKind = keyof Glints;

const isRect = (value: unknown): value is GlintFrame => {
  if (!value || typeof value !== 'object') return false;
  const rect = value as Partial<GlintFrame>;
  return [rect.x, rect.y, rect.width, rect.height].every(number => typeof number === 'number' && Number.isFinite(number) && number >= 0)
    && rect.width! > 0 && rect.height! > 0;
};

const isGlint = (value: unknown): value is Glint => {
  if (!value || typeof value !== 'object') return false;
  const glint = value as Partial<Glint>;
  return typeof glint.media === 'string' && Number.isSafeInteger(glint.width) && glint.width! > 0
    && Number.isSafeInteger(glint.height) && glint.height! > 0
    && Array.isArray(glint.frames) && glint.frames.length > 0 && glint.frames.every(isRect)
    && typeof glint.framesPerSecond === 'number' && glint.framesPerSecond > 0 && isRect(glint.slot);
};

export function isGlints(value: unknown): value is Glints {
  return !!value && typeof value === 'object' && isGlint((value as Partial<Glints>).magic) && isGlint((value as Partial<Glints>).coins);
}

const KINDS: readonly GlintKind[] = ['magic', 'coins'];
let shown = '';
const timers = new Map<GlintKind, ReturnType<typeof setInterval>>();

/**
 * Shows the glints the projection names. One timer per glint steps a shared frame, and every glint element of that
 * kind draws it through CSS variables on the document, so a pack full of enchanted items turns as one.
 */
export function useGlints(value: Glints | null | undefined): void {
  const key = JSON.stringify(value ?? null);
  if (key === shown) return;
  shown = key;
  for (const timer of timers.values()) clearInterval(timer);
  timers.clear();
  const root = document.documentElement;
  for (const kind of KINDS) {
    const glint = value?.[kind];
    if (!glint) { root.style.removeProperty(`--glint-${kind}-image`); continue; }
    // The slot holds the classic frame; a refreshed frame painted past its edges spills over the slot by as much.
    root.style.setProperty(`--glint-${kind}-left`, `${-glint.slot.x / glint.slot.width * 100}%`);
    root.style.setProperty(`--glint-${kind}-top`, `${-glint.slot.y / glint.slot.height * 100}%`);
    root.style.setProperty(`--glint-${kind}-width`, `${100 / glint.slot.width}%`);
    root.style.setProperty(`--glint-${kind}-height`, `${100 / glint.slot.height}%`);
    root.style.setProperty(`--glint-${kind}-rendering`, glint.media.startsWith('refreshed.') ? 'auto' : 'pixelated');
    root.style.removeProperty(`--glint-${kind}-image`);
    let index = 0;
    let drawn: string | null = null;
    const step = (): void => {
      const source = image(glint.media);
      const frame = glint.frames[index % glint.frames.length]!;
      index += 1;
      if (source === null) return;
      const along = (offset: number, size: number, whole: number): string => `${whole > size ? offset / (whole - size) * 100 : 0}%`;
      // The image is set once it has arrived; each step after moves only the frame.
      if (drawn !== source) { root.style.setProperty(`--glint-${kind}-image`, `url("${source}")`); drawn = source; }
      root.style.setProperty(`--glint-${kind}-size`, `${glint.width / frame.width * 100}% ${glint.height / frame.height * 100}%`);
      root.style.setProperty(`--glint-${kind}-position`, `${along(frame.x, frame.width, glint.width)} ${along(frame.y, frame.height, glint.height)}`);
    };
    step();
    timers.set(kind, setInterval(step, 1000 / glint.framesPerSecond));
  }
}

/** A glint for an item's slot: the magic shimmer goes over the item, the coin glint behind it. */
export function glint(kind: GlintKind): HTMLElement {
  const element = document.createElement('span');
  element.className = `dagger-glint dagger-glint-${kind}`;
  element.setAttribute('aria-hidden', 'true');
  return element;
}
