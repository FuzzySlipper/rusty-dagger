import { buttonLabel, keyLabel, type ControlsProjection } from './controls.js';
import { redraw } from './redraw.js';

/** A stretch of a help paragraph: words, or the control bound to a player action. */
export type HelpSegment = { readonly text: string } | { readonly action: string };

export interface HelpPage {
  readonly title: string;
  readonly paragraphs: readonly (readonly HelpSegment[])[];
}

export interface Help {
  update(pages: readonly HelpPage[], controls: ControlsProjection | null, device: 'keyboard' | 'controller'): void;
  /** The triggers or Page Up and Down: the next or previous page. */
  page(direction: -1 | 1): void;
  pageHint(): string | null;
}

export function isHelpPages(value: unknown): value is readonly HelpPage[] {
  return Array.isArray(value) && value.every(page => typeof page === 'object' && page !== null
    && typeof page.title === 'string' && Array.isArray(page.paragraphs)
    && page.paragraphs.every((paragraph: unknown) => Array.isArray(paragraph) && paragraph.every(segment => typeof segment === 'object' && segment !== null
      && (typeof segment.text === 'string' || typeof segment.action === 'string'))));
}

/**
 * The game's help: its pages as a column of tabs beside the page shown, read on parchment. The words are the
 * ruleset's; each control a paragraph names is drawn as the key, or the controller button, the player has bound to
 * it on the device in use, so the help never names a default the player has changed.
 */
export function mountHelp(root: HTMLElement): Help {
  const shell = document.createElement('section');
  shell.className = 'dagger-help';
  shell.setAttribute('aria-label', 'Help');
  const tabs = document.createElement('div');
  tabs.className = 'dagger-help-tabs';
  tabs.setAttribute('role', 'tablist');
  tabs.setAttribute('aria-label', 'Help pages');
  tabs.setAttribute('aria-orientation', 'vertical');
  const reading = document.createElement('article');
  reading.className = 'dagger-help-page dagger-frame';
  reading.dataset.skin = 'parchment';
  reading.setAttribute('role', 'tabpanel');
  shell.append(tabs, reading);
  root.append(shell);

  let pages: readonly HelpPage[] = [];
  let controls: ControlsProjection | null = null;
  let device: 'keyboard' | 'controller' = 'keyboard';
  let shown = 0;

  const control = (action: string): HTMLElement => {
    const binding = controls?.bindings.find(candidate => candidate.id === action);
    const chip = document.createElement('kbd');
    const label = device === 'controller'
      ? (binding?.buttons?.[0] !== undefined ? buttonLabel(binding.buttons[0]) : null)
      : (binding?.keys[0] ? keyLabel(binding.keys[0]) : null);
    // An action left unbound on this device is named by what it does, so the sentence still reads.
    chip.textContent = label ?? binding?.label ?? action;
    chip.classList.toggle('is-unbound', label === null);
    return chip;
  };

  const draw = (): void => {
    redraw(tabs, JSON.stringify(pages.map(page => page.title)), () => {
      tabs.replaceChildren(...pages.map((page, index) => {
        const tab = document.createElement('button');
        tab.type = 'button';
        tab.setAttribute('role', 'tab');
        tab.dataset.helpPage = String(index);
        tab.textContent = page.title;
        tab.addEventListener('click', () => select(index));
        // Tabs follow focus, so arrows and a pad show each page as they reach its tab.
        tab.addEventListener('focus', () => { if (shown !== index) select(index); });
        return tab;
      }));
    });
    // The tab shown is marked on the same buttons, so turning a page keeps focus where it is.
    for (const tab of tabs.querySelectorAll<HTMLButtonElement>('[data-help-page]')) {
      const selected = tab.dataset.helpPage === String(shown);
      tab.setAttribute('aria-selected', String(selected));
      tab.tabIndex = selected ? 0 : -1;
    }
    const page = pages[shown];
    redraw(reading, JSON.stringify([page ?? null, device, controls?.bindings ?? null]), () => {
      if (!page) { reading.replaceChildren(); return; }
      const title = document.createElement('h3');
      title.textContent = page.title;
      reading.replaceChildren(title, ...page.paragraphs.map(paragraph => {
        const text = document.createElement('p');
        text.append(...paragraph.map(segment => 'action' in segment ? control(segment.action) : document.createTextNode(segment.text)));
        return text;
      }));
    });
  };

  const select = (index: number): void => {
    shown = Math.max(0, Math.min(pages.length - 1, index));
    draw();
  };

  return {
    update(value, bound, inputDevice) {
      pages = value; controls = bound; device = inputDevice;
      if (shown >= pages.length) shown = 0;
      draw();
    },
    page(direction) {
      if (pages.length === 0) return;
      select((shown + direction + pages.length) % pages.length);
      tabs.querySelector<HTMLButtonElement>(`[data-help-page="${shown}"]`)?.focus();
    },
    pageHint() { return pages.length > 1 ? 'Page' : null; },
  };
}
