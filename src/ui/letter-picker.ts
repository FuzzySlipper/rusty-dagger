import type { NavigationSurface } from './navigation.js';

/**
 * The letter picker: how a controller types. It opens over whatever asked, edits a copy of a text
 * field's value on a grid of letters (A–Z, space, apostrophe, hyphen), with Delete, Done and Cancel,
 * and writes the result back into the field as typing would, firing `input` and `change`. So any text
 * field (a character's name, a riddle's answer) takes a pad's answer through the field's own handling.
 *
 * It is a navigation surface above everything else: directions move over the grid, accept presses a
 * key, Back cancels. A keyboard can use it too, but types into the field directly.
 */
export interface LetterPicker {
  readonly root: HTMLDialogElement;
  /** Edit this field; Done writes back, Cancel or Back leaves it as it was. */
  edit(field: HTMLInputElement | HTMLTextAreaElement, label?: string): void;
  isOpen(): boolean;
  cancel(): void;
}

const LETTERS = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ'.split('');
const MARKS = [' ', "'", '-'];

export function mountLetterPicker(shell: HTMLElement, register: (surface: NavigationSurface) => () => void): LetterPicker {
  // A modal dialog, so it draws in the top layer above a menu or a conversation that asked.
  const root = document.createElement('dialog');
  root.className = 'dagger-letter-picker dagger-frame';
  const title = document.createElement('h2');
  title.className = 'dagger-letter-picker-title';
  const shown = document.createElement('output');
  shown.className = 'dagger-letter-picker-text';
  shown.setAttribute('aria-live', 'polite');
  const grid = document.createElement('div');
  grid.className = 'dagger-letter-picker-grid';
  const key = (text: string, label: string, press: () => void): HTMLButtonElement => {
    const button = document.createElement('button');
    button.type = 'button';
    button.textContent = text;
    button.setAttribute('aria-label', label);
    button.addEventListener('click', press);
    return button;
  };
  let field: HTMLInputElement | HTMLTextAreaElement | null = null;
  let text = '';
  let returnFocus: HTMLElement | null = null;
  // Letters follow what is already written: capital at the start of a word, small after.
  const typed = (letter: string): string => text.length === 0 || /[\s'-]$/.test(text) ? letter : letter.toLowerCase();
  const show = (): void => { shown.textContent = text.length > 0 ? text : ' '; };
  const add = (letter: string): void => {
    if (field && field.maxLength > 0 && text.length >= field.maxLength) return;
    text += letter;
    show();
  };
  grid.append(
    ...LETTERS.map(letter => key(letter, `Letter ${letter}`, () => add(typed(letter)))),
    ...MARKS.map(mark => key(mark === ' ' ? 'Space' : mark, mark === ' ' ? 'Space' : mark === "'" ? 'Apostrophe' : 'Hyphen', () => add(mark))),
  );
  const actions = document.createElement('div');
  actions.className = 'dagger-letter-picker-actions';
  const close = (accept: boolean): void => {
    const target = field;
    field = null;
    if (root.open) root.close();
    if (accept && target) {
      target.value = text;
      target.dispatchEvent(new Event('input', { bubbles: true }));
      target.dispatchEvent(new Event('change', { bubbles: true }));
    }
    if (returnFocus?.isConnected) returnFocus.focus();
    returnFocus = null;
  };
  actions.append(
    key('Delete', 'Delete the last letter', () => { text = text.slice(0, -1); show(); }),
    key('Done', 'Done', () => close(true)),
    key('Cancel', 'Cancel', () => close(false)),
  );
  root.append(title, shown, grid, actions);
  shell.append(root);
  // Escape is Back here, as navigation routes it; the dialog's own cancel would close it unasked.
  root.addEventListener('cancel', event => { event.preventDefault(); close(false); });
  register({ name: 'letter-picker', root, layer: 110, isOpen: () => root.open, back: () => close(false),
    initialFocus: () => grid.querySelector('button') });

  return {
    root,
    edit(target, label) {
      returnFocus = document.activeElement instanceof HTMLElement ? document.activeElement : target;
      field = target;
      text = target.value;
      title.textContent = label ?? target.getAttribute('aria-label') ?? 'Enter text';
      show();
      if (!root.open) root.showModal();
      grid.querySelector<HTMLButtonElement>('button')?.focus();
    },
    isOpen: () => root.open,
    cancel: () => close(false),
  };
}

/** The event a control raises to have the picker edit a field; the shell's owner answers it. */
export const EDIT_TEXT_EVENT = 'dagger-edit-text';

/** Ask whoever mounted the picker to edit this field. */
export function requestTextEdit(from: Element, field: HTMLInputElement | HTMLTextAreaElement, label: string): void {
  const Event = from.ownerDocument.defaultView?.CustomEvent ?? CustomEvent;
  from.dispatchEvent(new Event(EDIT_TEXT_EVENT, { bubbles: true, detail: { field, label } }));
}

/** A button beside a text field that opens the picker for it, for a pad that cannot type into the field. */
export function letterPickerButton(field: HTMLInputElement | HTMLTextAreaElement, label: string): HTMLButtonElement {
  const button = document.createElement('button');
  button.type = 'button';
  button.className = 'dagger-letter-picker-open';
  button.textContent = '✎';
  button.setAttribute('aria-label', label);
  button.dataset.tooltip = label;
  button.addEventListener('click', () => requestTextEdit(button, field, label));
  return button;
}
