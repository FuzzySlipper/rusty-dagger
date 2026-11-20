/**
 * Range sliders that move. The window's Servo (0.7.0) draws an `<input type="range">` but gives it no pointer or key
 * handling: a press only focuses it and the arrows do nothing, so neither the game's sliders nor the Engine video
 * panel's could be changed. Here every range in the window takes a press and a drag along it, and the left and right
 * arrows, Home and End when focused, as a browser's own slider does: each move sets the value and fires `input`, and
 * letting go (or a key) fires `change`, so whoever owns the slider hears it as ever. A controller's left and right step
 * a focused range through navigation.
 */
export function mountRangeInput(doc: Document): { dispose(): void } {
  let held: { input: HTMLInputElement; start: string } | null = null;

  const fire = (input: HTMLInputElement, type: 'input' | 'change'): void => {
    const view = input.ownerDocument.defaultView;
    input.dispatchEvent(new (view?.Event ?? Event)(type, { bubbles: true }));
  };
  const range = (target: EventTarget | null): HTMLInputElement | null =>
    target instanceof HTMLInputElement && target.type === 'range' && !target.disabled && !target.readOnly ? target : null;
  /** The value under the pointer, across the slider's width, on the slider's nearest step. */
  const follow = (input: HTMLInputElement, x: number): void => {
    const box = input.getBoundingClientRect();
    if (box.width <= 0) return;
    const min = Number(input.min || 0), max = Number(input.max || 100);
    const step = input.step === 'any' ? 0 : Number(input.step || 1);
    const fraction = Math.min(1, Math.max(0, (x - box.left) / box.width));
    const value = min + fraction * (max - min);
    const stepped = step > 0 ? Math.min(max, min + Math.round((value - min) / step) * step) : value;
    const before = input.value;
    // Rounded past the float noise of the steps (0.1 * 3), as the slider would show it.
    input.value = String(Number(stepped.toFixed(9)));
    if (input.value !== before) fire(input, 'input');
  };
  const release = (): void => {
    if (!held) return;
    const { input, start } = held;
    held = null;
    if (input.value !== start) fire(input, 'change');
  };

  // Mouse events, which the window fires for every press, rather than pointer events.
  const onDown = (event: MouseEvent): void => {
    const input = range(event.target);
    if (!input || event.button !== 0) return;
    // Taken here, the press neither selects text nor reaches a handling the slider does not have.
    event.preventDefault();
    input.focus({ preventScroll: true });
    held = { input, start: input.value };
    follow(input, event.clientX);
  };
  const onMove = (event: MouseEvent): void => {
    if (!held) return;
    // The button came up outside the window: the drag ended there.
    if ((event.buttons & 1) === 0) { release(); return; }
    event.preventDefault();
    follow(held.input, event.clientX);
  };
  const onKeyDown = (event: KeyboardEvent): void => {
    const input = range(event.target);
    if (!input || event.ctrlKey || event.altKey || event.metaKey) return;
    const before = input.value;
    if (event.code === 'ArrowLeft') input.stepDown();
    else if (event.code === 'ArrowRight') input.stepUp();
    else if (event.code === 'Home') input.value = input.min || '0';
    else if (event.code === 'End') input.value = input.max || '100';
    else return;
    event.preventDefault();
    if (input.value !== before) { fire(input, 'input'); fire(input, 'change'); }
  };
  doc.addEventListener('mousedown', onDown, true);
  doc.addEventListener('mousemove', onMove, true);
  doc.addEventListener('mouseup', release, true);
  doc.addEventListener('keydown', onKeyDown, true);
  return {
    dispose() {
      doc.removeEventListener('mousedown', onDown, true);
      doc.removeEventListener('mousemove', onMove, true);
      doc.removeEventListener('mouseup', release, true);
      doc.removeEventListener('keydown', onKeyDown, true);
    },
  };
}
