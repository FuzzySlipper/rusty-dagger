import type { UiArt } from './art.js';

/**
 * The screens a product mode owns, as the binding the mode and the artifact share.
 *
 * The mode names the screen the product is in and the artifact identity is the published art that
 * screen shows, so the two cannot drift apart in this file: a mode with no identity shows nothing, and
 * an identity no published artifact carries is a screen that never arrives. The projection carries the
 * mode as a string and this table is what turns it into a screen.
 */
export interface ModeScreen {
  /** The wire name the product publishes for the mode. */
  readonly mode: string;
  /** The published media identity of the screen that mode shows. */
  readonly screen: string;
}

/**
 * Every mode whose screen is a published artifact. A mode that is not here owns no screen, which is
 * the ordinary case: ordinary play, a pause and a modal draw the HUD rather than replacing it.
 */
export const MODE_SCREENS: readonly ModeScreen[] = [
  { mode: 'title', screen: 'screen.title' },
  { mode: 'dead', screen: 'screen.death' },
];

/** The screen a mode owns, or null when the mode draws the HUD rather than a screen of its own. */
export function screenForMode(mode: string): string | null {
  return MODE_SCREENS.find(entry => entry.mode === mode)?.screen ?? null;
}

/** The action a client sends to leave the entry screen, which is the product's decision to make. */
export const BEGIN_ACTION = 'begin';

/** The one source identity of a screen a mode shows, resolved from the art the product delivered. */
export function screenArt(art: UiArt | null, screen: string, lookup: (id: string) => string | null): string | null {
  return art === null ? null : lookup(screen);
}
