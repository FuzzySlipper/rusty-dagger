/// The published UI art a snapshot carried, keyed by the media identity the pack owns.
export interface UiArtImage {
  readonly id: string;
  readonly image: string;
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
let revision = '';

/// Adopts a published art block and returns the revision now held, so a caller can tell whether the
/// snapshot brought art it did not have.
export function adopt(value: unknown): string {
  if (typeof value !== 'object' || value === null) return revision;
  const block = value as { revision?: unknown; images?: unknown };
  if (typeof block.revision !== 'string' || !Array.isArray(block.images)) return revision;
  const next = new Map<string, string>();
  for (const entry of block.images as readonly UiArtImage[]) {
    if (typeof entry?.id === 'string' && typeof entry?.image === 'string' && entry.image.length > 0) next.set(entry.id, entry.image);
  }

  images.clear();
  for (const [id, image] of next) images.set(id, image);
  revision = block.revision;
  return revision;
}

/// The revision of the art currently held, which is the empty string before any block arrives.
export function heldRevision(): string {
  return revision;
}

/// The data URL for one published media identity, or null when this session has not published it.
export function image(id: string | null): string | null {
  return id === null ? null : images.get(id) ?? null;
}

let lastReportedMissing = '\0unset';

/**
 * Reports missing frame art only when the missing set changes and is non-empty. Steady-state
 * renders stay silent (a hidden panel re-renders every snapshot; mounted panels repaint on
 * every projection), while newly missing art still warns once. Callers keep their
 * data-art-missing attributes current on every render regardless.
 */
export function reportMissingArt(context: string, missing: readonly string[]): void {
  const key = [...missing].sort().join('\0');
  if (key === lastReportedMissing) return;
  lastReportedMissing = key;
  if (missing.length !== 0) console.warn(`${context} frame art is not published by this session: ${[...missing].sort().join(', ')}`);
}
