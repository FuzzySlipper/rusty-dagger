/**
 * In-game drag and drop, and its keyboard and controller equivalent.
 *
 * Native HTML5 drag and drop is never used: the desktop window refuses it (rusty-engine #10045),
 * so this follows the Engine's pointer-event recipe (rusty-engine `docs/csharp-product-style.md`,
 * "Drag and drop"). A press on a source captures the pointer and becomes a drag only once it
 * travels, so a click stays a click; a ghost follows the pointer; the target under it is found
 * with `elementFromPoint`; and a release over a target sends one action. Cancelling (Escape, Back,
 * `pointercancel`, `lostpointercapture`, losing window focus) sends nothing.
 *
 * Without a pointer the same move is pick up and place: accepting a source holds it, focus moves
 * between targets, accepting a target places it, and Back releases it. While a source is held or
 * dragged every target shows whether it takes the source, from the eligibility the caller reads
 * out of the projection. The UI never moves anything itself: it redraws from what the product
 * publishes.
 */
export interface DragOptions<S> {
  /** The element the owner listens within. */
  readonly root: HTMLElement;
  readonly sourceSelector: string;
  readonly targetSelector: string;
  /** The source a control stands for, or null when it cannot be moved now. */
  source(element: HTMLElement): S | null;
  /** Whether a target takes a source, from published eligibility. */
  accepts(source: S, target: HTMLElement): boolean;
  /** Send the move. */
  drop(source: S, target: HTMLElement): void;
  /** A source was held or a drag began, so the caller can show it as selected. */
  onHold?(source: S): void;
  /** A held source was placed on a target that does not take it. */
  onRefused?(source: S, target: HTMLElement): void;
  /** A drag ended, by drop or cancel. */
  onEnd?(): void;
  /** The text a ghost shows when the source has no picture. */
  label?(element: HTMLElement): string;
}

export interface Drag<S> {
  /** The source held for pick-and-place, or being dragged. */
  held(): S | null;
  dragging(): boolean;
  /** Hold a source for pick-and-place, as accepting it would; a surface's own Move command uses it. */
  hold(source: S): void;
  /** Release a held source or cancel a drag; returns whether there was one. */
  release(): boolean;
  /** Re-mark which targets take the held source, after the targets were redrawn. */
  remark(): void;
  dispose(): void;
}

/** Pixels a press travels before it is a drag rather than a click. */
export const DRAG_START_PIXELS = 6;

export function mountDrag<S>(options: DragOptions<S>): Drag<S> {
  const { root } = options;
  const document_ = root.ownerDocument;
  let held: S | null = null;
  let dragging = false;
  let ghost: HTMLElement | null = null;
  let suppressClick = false;

  const targets = (): HTMLElement[] => Array.from(root.querySelectorAll<HTMLElement>(options.targetSelector));
  const targetAt = (x: number, y: number): HTMLElement | null => {
    const hit = document_.elementFromPoint(x, y);
    const target = hit instanceof Element ? hit.closest<HTMLElement>(options.targetSelector) : null;
    return target !== null && root.contains(target) && !(target as HTMLButtonElement).disabled ? target : null;
  };
  const mark = (): void => {
    const source = held;
    for (const target of targets()) {
      target.classList.toggle('is-drop-valid', source !== null && options.accepts(source, target));
      target.classList.toggle('is-drop-invalid', source !== null && !options.accepts(source, target));
    }
    root.classList.toggle('is-holding', source !== null);
  };
  const hover = (target: HTMLElement | null): void => {
    for (const element of root.querySelectorAll('.is-drop-target')) if (element !== target) element.classList.remove('is-drop-target');
    target?.classList.add('is-drop-target');
  };
  const hold = (source: S): void => { held = source; options.onHold?.(source); mark(); };
  const place = (target: HTMLElement): void => {
    const source = held;
    if (source === null) return;
    if (!options.accepts(source, target)) { options.onRefused?.(source, target); return; }
    held = null;
    mark();
    options.drop(source, target);
  };

  // The press in progress, followed by the shared press lifecycle; cancelling it ends it as a cancel.
  let press: { readonly element: HTMLElement; readonly source: S; readonly cancel: () => void } | null = null;
  const endDrag = (): void => {
    const wasDragging = dragging;
    press = null;
    dragging = false;
    ghost?.remove();
    ghost = null;
    root.classList.remove('is-dragging');
    hover(null);
    if (wasDragging) { held = null; mark(); options.onEnd?.(); }
  };
  const cancelDrag = (): void => { press?.cancel(); };

  const onPointerDown = (event: PointerEvent): void => {
    if (event.button !== 0 || press !== null) return;
    const element = event.target instanceof Element ? event.target.closest<HTMLElement>(options.sourceSelector) : null;
    if (element === null || !root.contains(element)) return;
    const source = options.source(element);
    if (source === null) return;
    const cancel = followPress(element, event, {
      start() {
        dragging = true;
        hold(source);
        root.classList.add('is-dragging');
        ghost = createGhost(element, options.label?.(element) ?? element.textContent?.trim() ?? '');
        // Inside the open modal that holds the source: a modal dialog draws in the top layer, above anything outside it.
        (root.closest('dialog[open]') ?? document_.body).append(ghost);
      },
      move(moved) {
        moved.preventDefault();
        if (ghost !== null) ghost.style.transform = `translate(${moved.clientX}px, ${moved.clientY}px)`;
        hover(targetAt(moved.clientX, moved.clientY));
      },
      end(released, dragged) {
        const target = released && dragged ? targetAt(released.clientX, released.clientY) : null;
        const holding = held;
        // The release that ends a drag is not also a click on whatever lies under it.
        if (released && dragged) suppressClick = true;
        endDrag();
        if (holding !== null && target !== null) {
          if (options.accepts(holding, target)) options.drop(holding, target);
          else options.onRefused?.(holding, target);
        }
      },
    });
    press = { element, source, cancel };
  };
  // A click, Enter or a controller's accept: on a source it holds it, on a target it places what is held.
  const onClick = (event: MouseEvent): void => {
    if (suppressClick) { suppressClick = false; event.stopImmediatePropagation(); return; }
    const element = event.target instanceof Element ? event.target : null;
    const sourceElement = element?.closest<HTMLElement>(options.sourceSelector) ?? null;
    if (sourceElement !== null && root.contains(sourceElement)) {
      const source = options.source(sourceElement);
      if (source !== null) hold(source);
      return;
    }
    const target = element?.closest<HTMLElement>(options.targetSelector) ?? null;
    if (target !== null && root.contains(target) && held !== null && !(target as HTMLButtonElement).disabled) place(target);
  };
  // Nothing under the owner starts a native drag session, whatever the element.
  const onNativeDrag = (event: DragEvent): void => event.preventDefault();
  root.addEventListener('pointerdown', onPointerDown);
  root.addEventListener('click', onClick, true);
  root.addEventListener('dragstart', onNativeDrag);

  return {
    held: () => held,
    dragging: () => dragging,
    hold,
    release() {
      if (press !== null && dragging) { cancelDrag(); return true; }
      if (held === null) return false;
      held = null;
      mark();
      return true;
    },
    remark: mark,
    dispose() {
      cancelDrag();
      root.removeEventListener('pointerdown', onPointerDown);
      root.removeEventListener('click', onClick, true);
      root.removeEventListener('dragstart', onNativeDrag);
    },
  };
}

export interface PressHandlers {
  /** The press has travelled past {@link DRAG_START_PIXELS}: from now on it is a drag. */
  start?(event: PointerEvent): void;
  /** The pointer moved during a drag (only after `start`). */
  move?(event: PointerEvent): void;
  /**
   * The press is over: released (with the release event) or cancelled (null) by `pointercancel`, a
   * lost capture, the window losing focus or the returned cancel. `dragged` says whether it was a drag.
   */
  end(released: PointerEvent | null, dragged: boolean): void;
}

/**
 * The one pointer-press lifecycle every in-game drag follows (rusty-engine `docs/csharp-product-style.md`,
 * "Drag and drop"): the pointer is captured on press, so the release arrives wherever it happens; a
 * press becomes a drag only once it travels; and release, `pointercancel`, a lost capture or the window
 * losing focus all end it, so no press outlives its button. Returns a cancel.
 */
export function followPress(element: Element, down: PointerEvent, handlers: PressHandlers): () => void {
  const pointer = down.pointerId;
  const window_ = element.ownerDocument.defaultView;
  let dragging = false;
  let done = false;
  const finish = (released: PointerEvent | null): void => {
    if (done) return;
    done = true;
    element.removeEventListener('pointermove', onMove as EventListener);
    element.removeEventListener('pointerup', onUp as EventListener);
    element.removeEventListener('pointercancel', onCancel);
    element.removeEventListener('lostpointercapture', onCancel);
    window_?.removeEventListener('blur', onCancel);
    if (element.hasPointerCapture?.(pointer)) element.releasePointerCapture(pointer);
    handlers.end(released, dragging);
  };
  const onMove = (event: PointerEvent): void => {
    if (event.pointerId !== pointer) return;
    // A move with no button held is a press whose release went unseen: it is over, not a drag.
    if (down.buttons !== 0 && event.buttons === 0) { finish(null); return; }
    if (!dragging) {
      if (Math.hypot(event.clientX - down.clientX, event.clientY - down.clientY) <= DRAG_START_PIXELS) return;
      dragging = true;
      handlers.start?.(event);
    }
    handlers.move?.(event);
  };
  const onUp = (event: PointerEvent): void => { if (event.pointerId === pointer) finish(event); };
  const onCancel = (): void => finish(null);
  element.setPointerCapture?.(pointer);
  element.addEventListener('pointermove', onMove as EventListener);
  element.addEventListener('pointerup', onUp as EventListener);
  element.addEventListener('pointercancel', onCancel);
  element.addEventListener('lostpointercapture', onCancel);
  window_?.addEventListener('blur', onCancel);
  return () => finish(null);
}

/** The ghost: the source's picture when it has one, otherwise its name, never empty. */
function createGhost(element: HTMLElement, label: string): HTMLElement {
  const ghost = element.ownerDocument.createElement('div');
  ghost.className = 'dagger-drag-ghost';
  ghost.setAttribute('aria-hidden', 'true');
  const picture = element.querySelector('img');
  if (picture?.src) {
    const image = element.ownerDocument.createElement('img');
    image.src = picture.src;
    image.alt = '';
    image.draggable = false;
    ghost.append(image);
  } else ghost.textContent = label;
  return ghost;
}
