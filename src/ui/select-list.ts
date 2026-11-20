/**
 * The list every `<select>` in the window opens: the game's own and the Engine video panel's alike. The window's Servo
 * opens a select as a plain browser popup placed apart from its control; here a press on a select, Enter, Space,
 * Alt+Down or F4 on it, or a pad's accept opens a list in the kit's skin under the control instead. Choosing an option
 * sets the select and fires its `input` and `change`, as the browser's popup does, so whoever owns the select hears
 * the choice as ever and nothing about it changes but how it is drawn.
 *
 * The list is a modal surface of its own: the directions and arrows move through it, accept, Enter or a click chooses,
 * and Back, Escape or a press outside closes it with the choice unchanged.
 */
export interface SelectList {
  readonly dialog: HTMLDialogElement;
  isOpen(): boolean;
  open(select: HTMLSelectElement): void;
  /** Closes with the choice unchanged. */
  close(): void;
  dispose(): void;
}

/** Keys that open a focused select, as they open the browser's popup. */
const OPENING_KEYS = new Set(['Enter', 'NumpadEnter', 'Space', 'F4']);

const choosable = (target: EventTarget | null): HTMLSelectElement | null => {
  const select = target instanceof Element ? target.closest('select') : null;
  return select instanceof HTMLSelectElement && !select.disabled && !select.multiple && select.size <= 1 ? select : null;
};

/**
 * Mounts the list in `shell`, which holds it beside the game's other surfaces. `sync` tells navigation the topmost
 * surface changed.
 */
export function mountSelectList(shell: HTMLElement, sync: () => void): SelectList {
  const doc = shell.ownerDocument;
  const dialog = doc.createElement('dialog');
  dialog.className = 'dagger-select-list';
  const list = doc.createElement('div');
  list.setAttribute('role', 'listbox');
  dialog.append(list);
  shell.append(dialog);

  let owner: HTMLSelectElement | null = null;
  let ownerDialog: HTMLDialogElement | null = null;

  const close = (): void => {
    const select = owner;
    owner = null;
    ownerDialog?.removeEventListener('close', close);
    ownerDialog = null;
    if (dialog.open) dialog.close();
    list.replaceChildren();
    if (select?.isConnected) select.focus({ preventScroll: true });
    sync();
  };

  const choose = (index: number): void => {
    const select = owner;
    if (!select?.isConnected || select.disabled) { close(); return; }
    const option = select.options[index];
    if (!option || option.disabled) return;
    const changed = select.selectedIndex !== index;
    close();
    if (!changed) return;
    select.selectedIndex = index;
    const view = select.ownerDocument.defaultView;
    select.dispatchEvent(new (view?.Event ?? Event)('input', { bubbles: true }));
    select.dispatchEvent(new (view?.Event ?? Event)('change', { bubbles: true }));
  };

  /** Under the control, or above it where the window has no room below; never past the window's edges. */
  const place = (select: HTMLSelectElement): void => {
    const view = doc.defaultView;
    const width = view?.innerWidth ?? 0, height = view?.innerHeight ?? 0;
    const anchor = select.getBoundingClientRect();
    dialog.style.minWidth = `${anchor.width}px`;
    const below = height - anchor.bottom, above = anchor.top;
    const room = Math.max(below, above) - 8;
    dialog.style.maxHeight = room > 0 ? `${room}px` : '';
    const box = dialog.getBoundingClientRect();
    const top = box.height <= below - 8 || below >= above ? anchor.bottom : anchor.top - box.height;
    dialog.style.top = `${Math.max(0, top)}px`;
    dialog.style.left = `${Math.max(0, Math.min(anchor.left, width - box.width))}px`;
  };

  const open = (select: HTMLSelectElement): void => {
    if (owner === select && dialog.open) return;
    if (owner) close();
    owner = select;
    const rows: HTMLElement[] = [];
    let current: HTMLElement | null = null;
    list.setAttribute('aria-label', select.getAttribute('aria-label') ?? select.labels?.[0]?.textContent?.trim() ?? '');
    for (const child of Array.from(select.children)) {
      const group = child.tagName === 'OPTGROUP' ? child as HTMLOptGroupElement : null;
      if (group) {
        const heading = doc.createElement('div');
        heading.className = 'dagger-select-group';
        heading.setAttribute('role', 'presentation');
        heading.textContent = group.label;
        rows.push(heading);
      }
      for (const element of group ? Array.from(group.children) : [child]) {
        if (element.tagName !== 'OPTION') continue;
        const option = element as HTMLOptionElement;
        if (option.hidden) continue;
        const row = doc.createElement('div');
        row.className = 'dagger-select-option';
        row.setAttribute('role', 'option');
        row.textContent = option.label || option.textContent;
        const index = option.index;
        const disabled = option.disabled || group?.disabled === true;
        row.setAttribute('aria-selected', String(index === select.selectedIndex));
        if (disabled) row.setAttribute('aria-disabled', 'true');
        else {
          row.tabIndex = 0;
          row.addEventListener('click', () => choose(index));
        }
        if (index === select.selectedIndex) current = row;
        rows.push(row);
      }
    }
    list.replaceChildren(...rows);
    // A select inside a modal (the menu) goes when that modal closes: the list closes with it.
    ownerDialog = select.closest('dialog');
    ownerDialog?.addEventListener('close', close);
    if (!dialog.open) dialog.showModal();
    place(select);
    const first = current && current.tabIndex === 0 ? current : list.querySelector<HTMLElement>('[role="option"][tabindex="0"]');
    first?.focus();
    sync();
  };

  // A press on a select opens the list in place of the window's popup; any later press outside the list closes it.
  // A press can arrive as a pointer event, a mouse event or both, one after the other: it is taken once.
  let pressTaken = false;
  const onPress = (event: MouseEvent): void => {
    const repeat = pressTaken && event.type === 'mousedown';
    pressTaken = event.type === 'pointerdown';
    const inside = event.target instanceof Element && event.target !== dialog && dialog.contains(event.target);
    if (inside) return;
    const select = dialog.open ? null : choosable(event.target);
    if (!dialog.open && !select) return;
    event.preventDefault();
    event.stopPropagation();
    if (repeat) return;
    if (dialog.open) close();
    else if (select && event.button === 0) {
      select.focus({ preventScroll: true });
      open(select);
    }
  };
  const onRelease = (): void => { pressTaken = false; };
  // A click is what runs a select's own popup: it never reaches one. A click with no press behind it (a pad's
  // accept, `click()`) opens the list.
  const onClick = (event: MouseEvent): void => {
    const select = choosable(event.target);
    if (!select) return;
    event.preventDefault();
    if (event.detail === 0 && !dialog.open) open(select);
  };
  const onKeyDown = (event: KeyboardEvent): void => {
    if (dialog.open) {
      const row = event.target instanceof HTMLElement ? event.target.closest<HTMLElement>('.dagger-select-option') : null;
      if (row && (event.code === 'Enter' || event.code === 'NumpadEnter' || event.code === 'Space')) {
        event.preventDefault();
        event.stopPropagation();
        row.click();
      } else if (event.code === 'Home' || event.code === 'End') {
        event.preventDefault();
        const rows = list.querySelectorAll<HTMLElement>('[role="option"][tabindex="0"]');
        rows[event.code === 'Home' ? 0 : rows.length - 1]?.focus();
      }
      return;
    }
    const select = choosable(event.target);
    if (!select || event.ctrlKey || event.metaKey) return;
    if (OPENING_KEYS.has(event.code) || (event.altKey && (event.code === 'ArrowDown' || event.code === 'ArrowUp'))) {
      event.preventDefault();
      event.stopPropagation();
      open(select);
    }
  };
  dialog.addEventListener('cancel', event => { event.preventDefault(); close(); });
  doc.addEventListener('pointerdown', onPress, true);
  doc.addEventListener('mousedown', onPress, true);
  doc.addEventListener('pointerup', onRelease, true);
  doc.addEventListener('click', onClick, true);
  doc.addEventListener('keydown', onKeyDown, true);

  return {
    dialog,
    isOpen: () => dialog.open,
    open,
    close: () => { if (dialog.open) close(); },
    dispose() {
      doc.removeEventListener('pointerdown', onPress, true);
      doc.removeEventListener('mousedown', onPress, true);
      doc.removeEventListener('pointerup', onRelease, true);
      doc.removeEventListener('click', onClick, true);
      doc.removeEventListener('keydown', onKeyDown, true);
      ownerDialog?.removeEventListener('close', close);
      dialog.remove();
    },
  };
}
