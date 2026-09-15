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
 * Every mode whose screen replaces the HUD. A mode that is not here draws the HUD, which is the
 * ordinary case: ordinary play, a pause and a modal add to it rather than standing in for it.
 */
/** The mode the entry screen is keyed by, shared by the table and the code that reads it. */
export const TITLE_MODE = 'title';

export const MODE_SCREENS: readonly ModeScreen[] = [
  { mode: TITLE_MODE, screen: 'screen.title' },
  { mode: 'dead', screen: 'screen.death' },
];

/**
 * The published screens no mode shows yet, named so the gap is a stated one rather than an omission.
 *
 * `screen.start-menu` is the donor's load, new and exit menu (`DaggerfallStartWindow`) and
 * `screen.prison` is the cell the donor shows while a prison sentence is served (`DaggerfallCourtWindow`,
 * days until freedom); neither is a state this product's lifecycle has, and neither is an opening
 * screen. `screen.character-generation` and `screen.pick.02` belong to a character-creation flow the
 * product does not have yet. All four are admitted, slotted and delivered to the DOM, and nothing
 * selects them. A delivered screen named in neither this list nor the table above is one the client
 * forgot, which is what the delivery test checks.
 *
 * No published identity is an intro: the donor opens a new game on a sequence of cinematics rather than
 * on a screen, so a mode for it would be one showing an artifact no publication carries. That sequence
 * and the completion that starts the game belong to cinematic playback
 * (`docs/coverage/content-scope.md`, CNT-025), not to this table.
 */
export const MODE_LESS_SCREENS: readonly string[] = [
  'screen.character-generation',
  'screen.pick.02',
  'screen.prison',
  'screen.start-menu',
];

/**
 * The screen a mode owns, or null when the mode draws the HUD rather than a screen of its own.
 *
 * An unlisted mode and a differently-cased name both return null: the mode strings are the product's
 * and this does not guess at one it was not given.
 */
export function screenForMode(mode: string): string | null {
  return MODE_SCREENS.find(entry => entry.mode === mode)?.screen ?? null;
}

/** The action a client sends to leave the entry screen, which is the product's decision to make. */
export const BEGIN_ACTION = 'begin';
