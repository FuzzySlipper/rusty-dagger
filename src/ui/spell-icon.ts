import { image } from './art.js';

/** Where spell icons are drawn from: one atlas image, its row length and how many icons it holds. */
export interface SpellIconAtlas { readonly media: string; readonly columns: number; readonly count: number; }

export function isSpellIconAtlas(value: unknown): value is SpellIconAtlas {
  if (!value || typeof value !== 'object') return false;
  const atlas = value as Partial<SpellIconAtlas>;
  return typeof atlas.media === 'string' && Number.isSafeInteger(atlas.columns) && atlas.columns! > 0
    && Number.isSafeInteger(atlas.count) && atlas.count! > 0;
}

/**
 * One spell's icon cut from the atlas the ruleset names, or its number when the art is not here.
 * The cell is a background position in the atlas, so every icon shares one delivered image.
 */
export function spellIcon(atlas: SpellIconAtlas | null | undefined, index: number): HTMLElement {
  const icon = document.createElement('span');
  icon.className = 'dagger-spell-glyph';
  icon.setAttribute('aria-hidden', 'true');
  const source = atlas ? image(atlas.media) : null;
  if (atlas && source !== null && index >= 0 && index < atlas.count) {
    const rows = Math.ceil(atlas.count / atlas.columns);
    const column = index % atlas.columns, row = Math.floor(index / atlas.columns);
    icon.style.backgroundImage = `url("${source}")`;
    icon.style.backgroundSize = `${atlas.columns * 100}% ${rows * 100}%`;
    icon.style.backgroundPosition = `${atlas.columns > 1 ? column / (atlas.columns - 1) * 100 : 0}% ${rows > 1 ? row / (rows - 1) * 100 : 0}%`;
  } else icon.textContent = String(index + 1);
  return icon;
}

/** Whether the atlas has arrived, so a drawing that shows icons redraws once it does. */
export function spellIconsReady(atlas: SpellIconAtlas | null | undefined): boolean {
  return atlas ? image(atlas.media) !== null : false;
}
