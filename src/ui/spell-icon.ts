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

/** One targeting or element tile: its rect in the tile atlas and, with refreshed art, its own image. */
export interface SpellSelectorTile {
  readonly x: number;
  readonly y: number;
  readonly width: number;
  readonly height: number;
  readonly image: string | null;
}

/** Where the targeting and element tiles are drawn from: one atlas image, its size, and each tile by index. */
export interface SpellSelectors {
  readonly media: string;
  readonly width: number;
  readonly height: number;
  readonly targets: readonly SpellSelectorTile[];
  readonly elements: readonly SpellSelectorTile[];
}

const isTile = (tile: unknown): tile is SpellSelectorTile => {
  if (!tile || typeof tile !== 'object') return false;
  const value = tile as Partial<SpellSelectorTile>;
  return [value.x, value.y, value.width, value.height].every(number => Number.isSafeInteger(number) && number! >= 0)
    && value.width! > 0 && value.height! > 0 && (value.image === null || typeof value.image === 'string');
};

export function isSpellSelectors(value: unknown): value is SpellSelectors {
  if (!value || typeof value !== 'object') return false;
  const selectors = value as Partial<SpellSelectors>;
  return typeof selectors.media === 'string' && Number.isSafeInteger(selectors.width) && selectors.width! > 0
    && Number.isSafeInteger(selectors.height) && selectors.height! > 0
    && Array.isArray(selectors.targets) && selectors.targets.every(isTile)
    && Array.isArray(selectors.elements) && selectors.elements.every(isTile);
}

/**
 * A spell's targeting or element tile: its refreshed image when the ruleset names one, else its rect of the tile
 * atlas, or nothing drawn when the art is not here. The tile keeps its own proportions.
 */
export function spellSelector(selectors: SpellSelectors | null | undefined, kind: 'targets' | 'elements', index: number): HTMLElement {
  const tile = document.createElement('span');
  tile.className = `dagger-spell-tile dagger-spell-tile-${kind === 'targets' ? 'target' : 'element'}`;
  tile.setAttribute('aria-hidden', 'true');
  const cell = selectors?.[kind][index];
  if (!selectors || !cell) return tile;
  tile.style.setProperty('--tile-aspect', String(cell.width / cell.height));
  const refreshed = image(cell.image);
  const source = image(selectors.media);
  if (refreshed !== null) {
    tile.style.backgroundImage = `url("${refreshed}")`;
    tile.style.backgroundSize = 'contain';
    tile.style.backgroundRepeat = 'no-repeat';
    tile.style.backgroundPosition = 'center';
    smoothIfRefreshed(tile, cell.image);
  } else if (source !== null) {
    tile.style.backgroundImage = `url("${source}")`;
    tile.style.backgroundSize = `${selectors.width / cell.width * 100}% ${selectors.height / cell.height * 100}%`;
    const along = (offset: number, size: number, whole: number): string => `${whole > size ? offset / (whole - size) * 100 : 0}%`;
    tile.style.backgroundPosition = `${along(cell.x, cell.width, selectors.width)} ${along(cell.y, cell.height, selectors.height)}`;
  }
  return tile;
}

/** Whether the tiles' art has arrived, so a drawing that shows tiles redraws once it does. */
export function spellSelectorsReady(selectors: SpellSelectors | null | undefined): boolean {
  if (!selectors) return false;
  return image(selectors.media) !== null && [...selectors.targets, ...selectors.elements].every(tile => tile.image === null || image(tile.image) !== null);
}
