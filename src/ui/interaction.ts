import type { RustyApplicationUiContext } from '@rusty-engine/product-ui';

/**
 * The one owner of interface versus gameplay.
 *
 * The Engine owns pointer lock and input; the page tells it, through `setInteractionMode`, whether
 * any current surface needs the cursor. Surfaces say so through the predicates they register,
 * and the projection says so for the questions the product asks. Leaving the last cursor surface
 * returns to play and takes gameplay focus back at once, so closing a menu or a dialogue re-locks
 * the pointer without a click.
 *
 * A pointer lock lost while the world has it (Escape, which the browser and the desktop shell
 * spend releasing the lock, or focus loss) is the player asking for the menu. Losing window focus
 * drops held state in whatever registered for it: a drag in progress, a held direction.
 */
export interface Interaction {
  /** A surface that needs the cursor while the predicate holds. */
  needsCursor(predicate: () => boolean): () => void;
  /** The projection's own answer: a mode or question that needs the cursor. */
  setProjectionNeedsCursor(value: boolean): void;
  /** Re-decide the mode now; `focus` asks for gameplay focus even if the mode did not change. */
  sync(focus?: boolean): void;
  mode(): 'interface' | 'gameplay' | null;
  /** Called when the window loses focus. */
  onBlur(listener: () => void): () => void;
  dispose(): void;
}

export interface InteractionOptions {
  /** A lock the world held was lost during play. */
  readonly onLockLostInPlay?: () => void;
}

export function mountInteraction(context: RustyApplicationUiContext, options: InteractionOptions = {}): Interaction {
  const predicates = new Set<() => boolean>();
  const blurListeners = new Set<() => void>();
  let projectionNeedsCursor = false;
  let mode: 'interface' | 'gameplay' | null = null;

  const wanted = (): boolean => projectionNeedsCursor || [...predicates].some(predicate => predicate());
  const sync = (focus = false): void => {
    const next = wanted() ? 'interface' : 'gameplay';
    const resuming = next === 'gameplay' && mode === 'interface';
    if (next !== mode) { mode = next; context.ui.setInteractionMode(next); }
    if (next === 'gameplay' && (focus || resuming)) context.ui.focusGameplay();
  };

  let pointerWasLocked = document.pointerLockElement !== null;
  const onPointerLockChange = (): void => {
    const locked = document.pointerLockElement !== null;
    // A surface opening takes focus from the world, and the host releases the lock inside that call,
    // before the mode is re-decided: a lock lost to a surface that already needs the cursor (a
    // conversation's modal opening) is not the player asking for the menu.
    if (pointerWasLocked && !locked && mode === 'gameplay' && !wanted()) options.onLockLostInPlay?.();
    pointerWasLocked = locked;
  };
  const onBlur = (): void => { for (const listener of blurListeners) listener(); };
  document.addEventListener('pointerlockchange', onPointerLockChange);
  window.addEventListener('blur', onBlur);

  return {
    needsCursor(predicate) { predicates.add(predicate); return () => { predicates.delete(predicate); }; },
    setProjectionNeedsCursor(value) { projectionNeedsCursor = value; },
    sync,
    mode: () => mode,
    onBlur(listener) { blurListeners.add(listener); return () => { blurListeners.delete(listener); }; },
    dispose() {
      document.removeEventListener('pointerlockchange', onPointerLockChange);
      window.removeEventListener('blur', onBlur);
      predicates.clear();
      blurListeners.clear();
    },
  };
}
