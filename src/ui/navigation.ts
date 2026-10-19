import type { RustyApplicationUiContext } from '@rusty-engine/product-ui';

/**
 * The one owner of keyboard and controller navigation in game UI.
 *
 * Every surface that takes the cursor registers here: the menu, the dialogue, the death and title
 * screens, the questions the product asks, the confirmation. The topmost open surface is the
 * focus scope. Inside it the controller's directions and the keyboard's arrows move focus to the
 * nearest control in that direction, so lists and slot grids both work with no surface keeping a
 * controller table of its own. Accept presses the focused control, Back and Escape go to the
 * surface, and the shoulder buttons step a surface's panels.
 *
 * A focused `<select>` or number field takes left and right as a change of its value, so a
 * controller can operate the choices and amounts game UI still draws with native controls.
 */
export interface NavigationSurface {
  /** Diagnostic name; also the `data-nav-surface` the root carries. */
  readonly name: string;
  readonly root: HTMLElement;
  /** Higher layers sit above lower ones when several are open. */
  readonly layer: number;
  isOpen(): boolean;
  /**
   * Escape, or the controller's Back. A surface without it is a question the product needs an
   * answer to: Escape and Back leave it alone.
   */
  back?(source: 'escape' | 'controller'): void;
  /** The shoulder buttons: step to the previous or next panel. */
  panel?(direction: -1 | 1): void;
  /** The control that takes focus when the surface opens, when its first control is not it. */
  initialFocus?(): HTMLElement | null;
}

export interface Navigation {
  register(surface: NavigationSurface): () => void;
  /** The open surface navigation acts in, or null in play. */
  topmost(): NavigationSurface | null;
  /** Re-read which surfaces are open; focus moves into one that has just become topmost. */
  refresh(): void;
  /** Move focus as a direction would. Exposed for surfaces that map their own keys. */
  move(direction: Direction): void;
  dispose(): void;
}

export type Direction = 'up' | 'down' | 'left' | 'right';

export const FOCUSABLE = 'button:not(:disabled),select:not(:disabled),input:not(:disabled),textarea:not(:disabled),a[href],[tabindex="0"]';

/** The Engine's normalized controller identities, read in the standard gamepad layout. */
export const CONTROLLER = Object.freeze({
  accept: 'button-0', back: 'button-1', menu: 'button-9',
  previousPanel: 'button-4', nextPanel: 'button-5',
  up: 'button-12', down: 'button-13', left: 'button-14', right: 'button-15',
  horizontalAxis: 'axis-0', verticalAxis: 'axis-1', axisThreshold: 0.55,
});

const ARROWS: Readonly<Record<string, Direction>> = { ArrowUp: 'up', ArrowDown: 'down', ArrowLeft: 'left', ArrowRight: 'right' };
const TEXT_ENTRY = 'input:not([type="number"]):not([type="range"]):not([type="checkbox"]):not([type="radio"]),textarea,[contenteditable="true"]';

export interface NavigationOptions {
  /** Escape or Back with no surface open: the product's play-time Escape. */
  readonly onPlayEscape?: () => void;
  /** A controller button no surface claimed, for surface-independent shortcuts. */
  readonly onButton?: (button: string) => void;
}

export function mountNavigation(shell: HTMLElement, context: RustyApplicationUiContext, options: NavigationOptions = {}): Navigation {
  const surfaces: NavigationSurface[] = [];
  const lastFocus = new Map<NavigationSurface, HTMLElement>();
  const returnFocus = new Map<NavigationSurface, HTMLElement | null>();
  let current: NavigationSurface | null = null;
  let held: { horizontal: number; vertical: number } = { horizontal: 0, vertical: 0 };
  let device: 'keyboard' | 'controller' = 'keyboard';
  // Hints name the controls of the device in use for what the topmost surface accepts. They sit
  // inside that surface, because a modal dialog draws in the top layer above anything outside it.
  const hints = document.createElement('p');
  hints.className = 'dagger-input-hints';
  hints.setAttribute('aria-hidden', 'true');
  const drawHints = (): void => {
    const top = topmost();
    if (top === null) { hints.remove(); return; }
    const parts = device === 'controller'
      ? [['A', 'Select'], ...(top.back ? [['B', 'Back']] : []), ...(top.panel ? [['LB / RB', 'Panels']] : [])]
      : [['Enter', 'Select'], ...(top.back ? [['Esc', 'Back']] : []), ['Tab', 'Next']];
    const text = parts.map(([key, label]) => `${key} ${label}`).join(' · ');
    if (hints.textContent !== text) hints.replaceChildren(...parts.flatMap(([key, label], index) => {
      const glyph = document.createElement('kbd'); glyph.textContent = key!;
      return [...(index > 0 ? [' · '] : []), glyph, ` ${label}`];
    }));
    hints.dataset.device = device;
    if (hints.parentElement !== top.root) top.root.append(hints);
  };
  const useDevice = (next: 'keyboard' | 'controller'): void => {
    shell.classList.toggle('controller-navigation', next === 'controller');
    if (next !== device) { device = next; drawHints(); }
  };

  const open = (): NavigationSurface[] => surfaces.filter(surface => surface.isOpen()).sort((a, b) => b.layer - a.layer);
  const topmost = (): NavigationSurface | null => open()[0] ?? null;
  const visible = (element: HTMLElement): boolean => element.getClientRects().length > 0 || !hasLayout();
  const focusables = (scope: HTMLElement): HTMLElement[] =>
    Array.from(scope.querySelectorAll<HTMLElement>(FOCUSABLE)).filter(element => visible(element) && !element.closest('[hidden]'));

  const focusInto = (surface: NavigationSurface): void => {
    const remembered = lastFocus.get(surface);
    const target = remembered?.isConnected && surface.root.contains(remembered) && remembered.matches(FOCUSABLE) ? remembered
      : surface.initialFocus?.() ?? surface.root.querySelector<HTMLElement>('[autofocus]') ?? focusables(surface.root)[0] ?? null;
    target?.focus({ preventScroll: false });
  };

  const refresh = (): void => {
    const top = topmost();
    drawHints();
    if (top === current) return;
    const previous = current;
    current = top;
    // A surface that closes gives focus back to what held it before it opened, when that is
    // still on screen; a surface that opens takes focus unless focus is already inside it.
    if (previous && !previous.isOpen()) {
      const back = returnFocus.get(previous);
      returnFocus.delete(previous);
      if (top === null && back?.isConnected) back.focus({ preventScroll: true });
    }
    if (top && !top.root.contains(document.activeElement)) {
      if (!returnFocus.has(top)) returnFocus.set(top, document.activeElement instanceof HTMLElement ? document.activeElement : null);
      focusInto(top);
    }
  };

  const onFocusIn = (event: FocusEvent): void => {
    const target = event.target instanceof HTMLElement ? event.target : null;
    if (!target) return;
    for (const surface of surfaces) if (surface.root.contains(target)) lastFocus.set(surface, target);
  };

  const move = (direction: Direction): void => {
    const scope = topmost()?.root;
    if (!scope) return;
    const items = focusables(scope);
    if (items.length === 0) return;
    const active = document.activeElement instanceof HTMLElement && scope.contains(document.activeElement) ? document.activeElement : null;
    if (!active) { items[direction === 'up' || direction === 'left' ? items.length - 1 : 0]?.focus(); return; }
    (nearest(active, items, direction) ?? active).focus();
  };

  /** A focused choice or amount takes left and right as a change of its value. */
  const adjust = (direction: -1 | 1): boolean => {
    const active = document.activeElement;
    if (active instanceof HTMLSelectElement && !active.disabled && active.options.length > 0) {
      const next = Math.min(active.options.length - 1, Math.max(0, active.selectedIndex + direction));
      if (next !== active.selectedIndex) {
        active.selectedIndex = next;
        active.dispatchEvent(new Event('input', { bubbles: true }));
        active.dispatchEvent(new Event('change', { bubbles: true }));
      }
      return true;
    }
    if (active instanceof HTMLInputElement && !active.disabled && (active.type === 'number' || active.type === 'range')) {
      const before = active.value;
      try { if (direction > 0) active.stepUp(); else active.stepDown(); } catch { return true; }
      if (active.value !== before) {
        active.dispatchEvent(new Event('input', { bubbles: true }));
        active.dispatchEvent(new Event('change', { bubbles: true }));
      }
      return true;
    }
    return false;
  };

  const back = (source: 'escape' | 'controller'): void => {
    const top = topmost();
    if (top) top.back?.(source);
    else options.onPlayEscape?.();
  };

  const onKeyDown = (event: KeyboardEvent): void => {
    if (event.defaultPrevented) return;
    useDevice('keyboard');
    const top = topmost();
    if (event.code === 'Escape') {
      event.preventDefault();
      event.stopPropagation();
      if (!event.repeat) back('escape');
      return;
    }
    if (!top) return;
    const target = event.target instanceof Element ? event.target : null;
    if (event.code === 'Tab') {
      // Tab stays inside the surface that holds the cursor, as a modal dialog's focus does.
      const items = focusables(top.root);
      const index = items.indexOf(document.activeElement as HTMLElement);
      if (index < 0 || (!event.shiftKey && index === items.length - 1) || (event.shiftKey && index === 0)) {
        event.preventDefault();
        event.stopPropagation();
        items[event.shiftKey ? items.length - 1 : 0]?.focus();
      }
      return;
    }
    if (event.ctrlKey || event.altKey || event.metaKey || target?.closest(`${TEXT_ENTRY},select`)) return;
    const direction = ARROWS[event.code];
    if (direction === undefined || !top.root.contains(target)) return;
    if ((direction === 'left' || direction === 'right') && target?.matches('input[type="number"],input[type="range"]')) return;
    event.preventDefault();
    move(direction);
  };

  const press = (button: string): void => {
    const top = topmost();
    if (button === CONTROLLER.accept && top) {
      const active = document.activeElement instanceof HTMLElement && top.root.contains(document.activeElement) ? document.activeElement : null;
      if (!active || !active.matches(FOCUSABLE)) { move('down'); return; }
      if (active instanceof HTMLSelectElement || active.matches(TEXT_ENTRY)) return;
      active.click();
    } else if (button === CONTROLLER.back && top) back('controller');
    else if ((button === CONTROLLER.previousPanel || button === CONTROLLER.nextPanel) && top?.panel)
      top.panel(button === CONTROLLER.previousPanel ? -1 : 1);
    else if (top && (button === CONTROLLER.up || button === CONTROLLER.down)) move(button === CONTROLLER.up ? 'up' : 'down');
    else if (top && (button === CONTROLLER.left || button === CONTROLLER.right)) {
      if (!adjust(button === CONTROLLER.left ? -1 : 1)) move(button === CONTROLLER.left ? 'left' : 'right');
    } else options.onButton?.(button);
  };

  const unsubscribe = context.input?.subscribe(({ fact }) => {
    useDevice('controller');
    if (fact.kind === 'controller-axis' && (fact.axis === CONTROLLER.verticalAxis || fact.axis === CONTROLLER.horizontalAxis)) {
      const vertical = fact.axis === CONTROLLER.verticalAxis;
      const sign = Math.abs(fact.value) < CONTROLLER.axisThreshold ? 0 : Math.sign(fact.value);
      const previous = vertical ? held.vertical : held.horizontal;
      held = vertical ? { ...held, vertical: sign } : { ...held, horizontal: sign };
      if (sign === 0 || sign === previous || !topmost()) return;
      if (vertical) move(sign < 0 ? 'up' : 'down');
      else if (!adjust(sign < 0 ? -1 : 1)) move(sign < 0 ? 'left' : 'right');
      return;
    }
    if (fact.kind === 'controller-button' && fact.edge === 'pressed') press(fact.button);
  }) ?? null;

  const onBlur = (): void => { held = { horizontal: 0, vertical: 0 }; };
  document.addEventListener('keydown', onKeyDown, true);
  document.addEventListener('focusin', onFocusIn);
  window.addEventListener('blur', onBlur);

  return {
    register(surface) {
      surface.root.dataset.navSurface = surface.name;
      surfaces.push(surface);
      return () => {
        const index = surfaces.indexOf(surface);
        if (index >= 0) surfaces.splice(index, 1);
        lastFocus.delete(surface);
        returnFocus.delete(surface);
        if (current === surface) current = null;
      };
    },
    topmost,
    refresh,
    move,
    dispose() {
      unsubscribe?.();
      document.removeEventListener('keydown', onKeyDown, true);
      document.removeEventListener('focusin', onFocusIn);
      window.removeEventListener('blur', onBlur);
      hints.remove();
      surfaces.length = 0;
    },
  };
}

/** Whether the page lays anything out; a DOM with no layout navigates in document order. */
function hasLayout(): boolean {
  return document.documentElement.getBoundingClientRect().width > 0;
}

/**
 * The control nearest `from` in a direction: the closest centre ahead of it, with distance across
 * the direction weighted so a control in line wins over a nearer one off to the side. With no
 * layout, up and left are the previous control in document order and down and right the next.
 */
function nearest(from: HTMLElement, items: readonly HTMLElement[], direction: Direction): HTMLElement | null {
  const index = items.indexOf(from);
  if (!hasLayout()) {
    const step = direction === 'up' || direction === 'left' ? -1 : 1;
    return items[(index + step + items.length) % items.length] ?? null;
  }
  const origin = from.getBoundingClientRect();
  const ox = origin.left + origin.width / 2, oy = origin.top + origin.height / 2;
  let best: HTMLElement | null = null;
  let bestScore = Infinity;
  for (const item of items) {
    if (item === from) continue;
    const rect = item.getBoundingClientRect();
    const x = rect.left + rect.width / 2, y = rect.top + rect.height / 2;
    const along = direction === 'up' ? oy - y : direction === 'down' ? y - oy : direction === 'left' ? ox - x : x - ox;
    if (along <= 1) continue;
    const across = direction === 'up' || direction === 'down' ? Math.abs(x - ox) : Math.abs(y - oy);
    const score = along + across * 3;
    if (score < bestScore) { bestScore = score; best = item; }
  }
  if (best) return best;
  // Past the last control in a direction, a vertical list wraps the way the old menu cycling did.
  if (direction === 'up' || direction === 'down') return items[(index + (direction === 'up' ? -1 : 1) + items.length) % items.length] ?? null;
  return null;
}
