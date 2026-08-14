// Sprite review contract: structural types over the derived content manifests
// served by the lab bridge (`/api/dagger-lab/sprites/index`). The manifests
// differ in shape (enemy atlases, billboards, combat weapon/effects, plain
// textures), so normalization is duck-typed: anything with an image path plus
// optional frame rects and animation timings becomes a reviewable entry.

export interface SpriteIndex {
  readonly manifests: Record<string, unknown>;
  readonly files: Record<string, readonly string[]>;
}

export interface SpriteFrameRect {
  readonly frame: number;
  readonly uvMin: readonly [number, number];
  readonly uvMax: readonly [number, number];
  readonly sourceSize?: readonly number[] | undefined;
  readonly sourceOffset?: readonly number[] | undefined;
}

export interface SpriteAnimation {
  readonly name: string;
  readonly fps: number;
  readonly loop: boolean;
  readonly frameStart: number;
  readonly framesPerOrientation: number;
  readonly orientationCount: number;
  readonly notes: string;
}

export interface SpriteEntry {
  readonly key: string;
  readonly manifest: string;
  readonly label: string;
  readonly detail: string;
  /** Path relative to the served content root (e.g. `textures/enemy-0-atlas.png`). */
  readonly imagePath: string;
  /** Atlas pixel dimensions; 0 when the manifest does not record them. */
  readonly imageWidth: number;
  readonly imageHeight: number;
  readonly frames: readonly SpriteFrameRect[];
  readonly animations: readonly SpriteAnimation[];
  readonly worldSize?: readonly number[] | undefined;
  readonly pivot?: readonly number[] | undefined;
  /**
   * Sprite atlases are written bottom-up at import time (Engine's UV
   * convention samples v=0 at the first PNG row), so a viewer with top-left
   * origin must flip them for display. Plain dungeon textures are mesh-mapped
   * and shown as stored.
   */
  readonly flipY: boolean;
}

type JsonObject = Record<string, unknown>;

export function normalizeSpriteIndex(index: SpriteIndex): SpriteEntry[] {
  const entries: SpriteEntry[] = [];
  for (const [manifest, raw] of Object.entries(index.manifests)) {
    if (!isObject(raw)) continue;
    for (const item of arrayOfObjects(raw['enemies'])) {
      entries.push(enemyEntry(manifest, item));
    }
    for (const item of arrayOfObjects(raw['billboards'])) {
      entries.push(billboardEntry(manifest, item));
    }
    if (isObject(raw['weapon'])) {
      entries.push(weaponEntry(manifest, raw['weapon']));
    }
    for (const item of arrayOfObjects(raw['effects'])) {
      entries.push(effectEntry(manifest, item));
    }
    for (const item of arrayOfObjects(raw['textures'])) {
      entries.push(plainTextureEntry(manifest, item));
    }
  }
  return entries.sort((left, right) => left.key.localeCompare(right.key));
}

function enemyEntry(manifest: string, raw: JsonObject): SpriteEntry {
  const frames = frameRects(raw['frames']);
  const mobileId = number(raw['mobileId']);
  const name = string(raw['name']) ?? `mobile ${mobileId ?? '?'}`;
  return {
    key: `${manifest}:mobile-${mobileId ?? name}`,
    manifest,
    label: name,
    detail: `mobile ${mobileId ?? '?'} · archive ${number(raw['archive']) ?? '?'}`,
    imagePath: contentPath(string(raw['path']) ?? ''),
    imageWidth: number(raw['width']) ?? 0,
    imageHeight: number(raw['height']) ?? 0,
    frames,
    animations: stateAnimations(raw['states'], frames.length),
    worldSize: numberArray(raw['normalizedSize']),
    flipY: true,
  };
}

function billboardEntry(manifest: string, raw: JsonObject): SpriteEntry {
  const frames = frameRects(raw['frames']);
  const archive = number(raw['archive']);
  const record = number(raw['record']);
  const frameCount = number(raw['frameCount']) ?? 0;
  const animations: SpriteAnimation[] =
    frameCount > 1 && frames.length > 0
      ? [
          {
            name: 'playback',
            fps: number(raw['fps']) ?? 0,
            loop: true,
            frameStart: 0,
            framesPerOrientation: frameCount,
            orientationCount: 1,
            notes: 'horizontal strip',
          },
        ]
      : [];
  return {
    key: `${manifest}:billboard-${archive}-${record}`,
    manifest,
    label: `billboard ${archive}-${record}`,
    detail: `archive ${archive ?? '?'} record ${record ?? '?'}`,
    imagePath: contentPath(string(raw['path']) ?? ''),
    imageWidth: number(raw['width']) ?? 0,
    imageHeight: number(raw['height']) ?? 0,
    frames: frames.length > 0 ? frames : wholeFrame(raw),
    animations,
    worldSize: numberArray(raw['worldSize']),
    flipY: true,
  };
}

function weaponEntry(manifest: string, raw: JsonObject): SpriteEntry {
  const animations: SpriteAnimation[] = [];
  for (const action of arrayOfObjects(raw['animations'])) {
    animations.push({
      name: string(action['action']) ?? 'action',
      fps: number(action['fps']) ?? 0,
      loop: false,
      frameStart: number(action['frameStart']) ?? 0,
      framesPerOrientation: number(action['frameCount']) ?? 0,
      orientationCount: 1,
      notes: [string(action['alignment']), action['screenOffset'] !== undefined ? `offset ${action['screenOffset']}` : undefined]
        .filter((note) => note !== undefined)
        .join(' · '),
    });
  }
  return {
    key: `${manifest}:${string(raw['id']) ?? 'weapon'}`,
    manifest,
    label: string(raw['id']) ?? 'weapon',
    detail: 'weapon viewmodel',
    imagePath: contentPath(string(raw['path']) ?? ''),
    imageWidth: number(raw['width']) ?? 0,
    imageHeight: number(raw['height']) ?? 0,
    frames: frameRects(raw['frames']),
    animations,
    pivot: numberArray(raw['pivot']),
    flipY: true,
  };
}

function effectEntry(manifest: string, raw: JsonObject): SpriteEntry {
  const frames = frameRects(raw['frames']);
  return {
    key: `${manifest}:${string(raw['id']) ?? 'effect'}`,
    manifest,
    label: string(raw['id']) ?? 'effect',
    detail: 'combat effect',
    imagePath: contentPath(string(raw['path']) ?? ''),
    imageWidth: number(raw['width']) ?? 0,
    imageHeight: number(raw['height']) ?? 0,
    frames,
    animations:
      frames.length > 0
        ? [
            {
              name: 'playback',
              fps: number(raw['fps']) ?? 0,
              loop: raw['loop'] === true,
              frameStart: 0,
              framesPerOrientation: frames.length,
              orientationCount: 1,
              notes: '',
            },
          ]
        : [],
    pivot: numberArray(raw['pivot']),
    flipY: true,
  };
}

function plainTextureEntry(manifest: string, raw: JsonObject): SpriteEntry {
  const path = string(raw['path']) ?? '';
  return {
    key: `${manifest}:${path}`,
    manifest,
    label: path.replace(/\.png$/, ''),
    detail: 'dungeon texture',
    imagePath: contentPath(path),
    imageWidth: number(raw['width']) ?? 0,
    imageHeight: number(raw['height']) ?? 0,
    frames: wholeFrame(raw),
    animations: [],
    flipY: false,
  };
}

/// Enemy manifests pack states as `frameStart` + `framesPerOrientation` over a
/// state-major atlas. The orientation count is derived from the distance to
/// the next state's frameStart (or the total frame count), never hardcoded.
function stateAnimations(states: unknown, frameCount: number): SpriteAnimation[] {
  if (!isObject(states)) return [];
  const parsed: SpriteAnimation[] = [];
  for (const [name, raw] of Object.entries(states)) {
    if (!isObject(raw)) continue;
    parsed.push({
      name,
      fps: number(raw['fps']) ?? 0,
      loop: raw['loop'] === true,
      frameStart: number(raw['frameStart']) ?? 0,
      framesPerOrientation: number(raw['framesPerOrientation']) ?? 1,
      orientationCount: 1,
      notes: '',
    });
  }
  parsed.sort((left, right) => left.frameStart - right.frameStart);
  return parsed.map((state, index) => {
    const next = parsed[index + 1]?.frameStart ?? frameCount;
    const span = Math.max(state.framesPerOrientation, next - state.frameStart);
    const orientationCount =
      state.framesPerOrientation > 0
        ? Math.max(1, Math.round(span / state.framesPerOrientation))
        : 1;
    return { ...state, orientationCount };
  });
}

/// Manifest paths are inconsistent: combat audio carries a repo-relative
/// `content/...` prefix while atlas paths are bare filenames under
/// `content/textures/`. Normalize to content-root-relative.
function contentPath(path: string): string {
  if (path.startsWith('content/')) return path.slice('content/'.length);
  return path.includes('/') ? path : `textures/${path}`;
}

function frameRects(raw: unknown): SpriteFrameRect[] {
  const rects: SpriteFrameRect[] = [];
  for (const item of arrayOfObjects(raw)) {
    const uvMin = numberArray(item['uvMin']);
    const uvMax = numberArray(item['uvMax']);
    if (uvMin === undefined || uvMax === undefined || uvMin.length < 2 || uvMax.length < 2) {
      continue;
    }
    rects.push({
      frame: number(item['frame']) ?? rects.length,
      // Length is guarded above; the tuple cast keeps noUncheckedIndexedAccess happy.
      uvMin: [uvMin[0]!, uvMin[1]!],
      uvMax: [uvMax[0]!, uvMax[1]!],
      sourceSize: numberArray(item['sourceSize']),
      sourceOffset: numberArray(item['sourceOffset']),
    });
  }
  return rects;
}

function wholeFrame(raw: JsonObject): SpriteFrameRect[] {
  if (number(raw['width']) === undefined) return [];
  return [{ frame: 0, uvMin: [0, 0], uvMax: [1, 1] }];
}

function arrayOfObjects(raw: unknown): JsonObject[] {
  return Array.isArray(raw) ? raw.filter(isObject) : [];
}

function isObject(value: unknown): value is JsonObject {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function number(value: unknown): number | undefined {
  return typeof value === 'number' && Number.isFinite(value) ? value : undefined;
}

function string(value: unknown): string | undefined {
  return typeof value === 'string' && value.length > 0 ? value : undefined;
}

function numberArray(value: unknown): number[] | undefined {
  if (!Array.isArray(value)) return undefined;
  const numbers = value.filter((item): item is number => typeof item === 'number');
  return numbers.length > 0 ? numbers : undefined;
}
