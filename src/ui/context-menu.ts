import type { NavigationSurface } from './navigation.js';

/**
 * A drawn context menu. The desktop window shows no native one, so a right-click, the keyboard's
 * menu key (or Shift+F10) and the controller's X open this instead. It is an accelerator: every
 * entry is also reachable from the surface's own controls. It registers with navigation above the
 * surface it opens over, so arrows move through its entries and Escape or Back closes it.
 */
export interface ContextMenuEntry {
  readonly label: string;
  readonly disabled?: boolean;
  run(): void;
}

export interface ContextMenu {
  readonly root: HTMLElement;
  /** Open beside an element (the keyboard and controller) or at a pointer position. */
  open(at: HTMLElement | { readonly x: number; readonly y: number }, entries: readonly ContextMenuEntry[]): void;
  close(): void;
  isOpen(): boolean;
  dispose(): void;
}

export function mountContextMenu(host: HTMLElement, register?: (surface: NavigationSurface) => () => void): ContextMenu {
  const root = document.createElement('div');
  root.className = 'dagger-context-menu';
  root.setAttribute('role', 'menu');
  root.hidden = true;
  host.append(root);
  let returnFocus: HTMLElement | null = null;

  const close = (): void => {
    if (root.hidden) return;
    root.hidden = true;
    root.replaceChildren();
    if (returnFocus?.isConnected) returnFocus.focus({ preventScroll: true });
    returnFocus = null;
  };
  const onPointerDown = (event: PointerEvent): void => {
    if (!root.hidden && !(event.target instanceof Node && root.contains(event.target))) close();
  };
  document.addEventListener('pointerdown', onPointerDown, true);
  const unregister = register?.({ name: 'context-menu', root, layer: 95, isOpen: () => !root.hidden, back: close });

  return {
    root,
    open(at, entries) {
      returnFocus = at instanceof HTMLElement ? at : document.activeElement instanceof HTMLElement ? document.activeElement : null;
      root.replaceChildren(...entries.map(entry => {
        const item = document.createElement('button');
        item.type = 'button';
        item.setAttribute('role', 'menuitem');
        item.textContent = entry.label;
        item.disabled = entry.disabled === true;
        item.addEventListener('click', () => { close(); entry.run(); });
        return item;
      }));
      const rect = at instanceof HTMLElement ? at.getBoundingClientRect() : null;
      root.style.left = `${rect ? rect.left : at instanceof HTMLElement ? 0 : at.x}px`;
      root.style.top = `${rect ? rect.bottom : at instanceof HTMLElement ? 0 : at.y}px`;
      root.hidden = false;
      root.querySelector<HTMLButtonElement>('button:not(:disabled)')?.focus();
    },
    close,
    isOpen: () => !root.hidden,
    dispose() {
      close();
      unregister?.();
      document.removeEventListener('pointerdown', onPointerDown, true);
      root.remove();
    },
  };
}
