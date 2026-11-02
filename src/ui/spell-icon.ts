import { image, smoothIfRefreshed } from './art.js';

/**
 * Where spell icons are drawn from: one atlas image, its row length and how many icons it holds, and with
 * refreshed art each cell's own image (null where a cell has none, which then comes from the atlas).
 */
export interface SpellIconAtlas {
  readonly media: string;
  readonly columns: number;
  readonly count: number;
  readonly images?: readonly (string | null)[] | null;
}

export function isSpellIconAtlas(value: unknown): value is SpellIconAtlas {
  if (!value || typeof value !== 'object') return false;
  const atlas = value as Partial<SpellIconAtlas>;
  return typeof atlas.media === 'string' && Number.isSafeInteger(atlas.columns) && atlas.columns! > 0
    && Number.isSafeInteger(atlas.count) && atlas.count! > 0
    && (atlas.images == null || Array.isArray(atlas.images) && atlas.images.every(entry => entry === null || typeof entry === 'string'));
}

/**
 * One spell's icon: its refreshed image when the ruleset names one, else its cell of the atlas, or its number
 * when the art is not here. An atlas cell is a background position, so every classic icon shares one image.
 */
export function spellIcon(atlas: SpellIconAtlas | null | undefined, index: number): HTMLElement {
  const icon = document.createElement('span');
  icon.className = 'dagger-spell-glyph';
  icon.setAttribute('aria-hidden', 'true');
  const own = atlas?.images?.[index] ?? null;
  const refreshed = image(own);
  const source = atlas ? image(atlas.media) : null;
  if (refreshed !== null) {
    icon.style.backgroundImage = `url("${refreshed}")`;
    icon.style.backgroundSize = 'contain';
    icon.style.backgroundRepeat = 'no-repeat';
    icon.style.backgroundPosition = 'center';
    smoothIfRefreshed(icon, own);
  } else if (atlas && source !== null && index >= 0 && index < atlas.count) {
    const rows = Math.ceil(atlas.count / atlas.columns);
    const column = index % atlas.columns, row = Math.floor(index / atlas.columns);
    icon.style.backgroundImage = `url("${source}")`;
    icon.style.backgroundSize = `${atlas.columns * 100}% ${rows * 100}%`;
    icon.style.backgroundPosition = `${atlas.columns > 1 ? column / (atlas.columns - 1) * 100 : 0}% ${rows > 1 ? row / (rows - 1) * 100 : 0}%`;
  } else icon.textContent = String(index + 1);
  return icon;
}

/** Whether the icons' art has arrived, so a drawing that shows icons redraws once it does. */
export function spellIconsReady(atlas: SpellIconAtlas | null | undefined): boolean {
  if (!atlas) return false;
  return image(atlas.media) !== null && (atlas.images ?? []).every(own => own === null || image(own) !== null);
}
