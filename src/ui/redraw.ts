/**
 * Redraws that keep focus. A projection arrives every update, and a surface that rebuilt its
 * controls each time would throw away the keyboard or controller focus on every one. `redraw`
 * rebuilds a root only when what it shows changed; when it does rebuild, the control that held
 * focus is found again in the new content by its identity and focused, so a list that gains an
 * entry keeps the player's place.
 */
const signatures = new WeakMap<Element, string>();

/** A control's identity across rebuilds: its tag, its `data-*` values and its accessible name. */
function identity(element: Element): string {
  const data = element instanceof HTMLElement ? Object.entries(element.dataset).sort().map(([key, value]) => `${key}=${value}`).join('&') : '';
  return `${element.tagName}|${data}|${element.getAttribute('aria-label') ?? element.textContent?.trim() ?? ''}`;
}

/** Rebuild `root` with `build` unless `signature` matches the last one drawn. Returns whether it rebuilt. */
export function redraw(root: Element, signature: string, build: () => void): boolean {
  if (signatures.get(root) === signature) return false;
  signatures.set(root, signature);
  const focused = document.activeElement instanceof HTMLElement && root.contains(document.activeElement) ? document.activeElement : null;
  const key = focused ? identity(focused) : null;
  const index = focused ? Array.from(root.querySelectorAll(focused.tagName)).indexOf(focused) : -1;
  build();
  if (key === null) return true;
  const candidates = Array.from(root.querySelectorAll<HTMLElement>(focused!.tagName));
  const same = candidates.find(candidate => identity(candidate) === key);
  // The same control if it is still there, otherwise whatever now sits where it was.
  const target = same ?? candidates[Math.min(index, candidates.length - 1)] ?? null;
  target?.focus({ preventScroll: true });
  return true;
}

/** Forget what was drawn, so the next `redraw` rebuilds (for art that arrived after its state). */
export function invalidate(root: Element): void {
  signatures.delete(root);
}
