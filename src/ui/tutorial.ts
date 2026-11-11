import { helpParagraphs, isTutorial } from './help.js';
import type { Send } from './host.js';
import type { DaggerHud } from './projection.js';
import { redraw } from './redraw.js';

export interface Tutorial {
  readonly root: HTMLDialogElement;
  isOpen(): boolean;
  /** Reads on: the page goes and the tutorial's next follows after a little play. */
  readOn(): void;
  update(value: DaggerHud): void;
  dispose(): void;
}

/**
 * The tutorial page that is up, read on parchment over the held world: which page of how many, the page in the
 * ruleset's words with the player's own controls, and the choice to read on or end the tutorial. The ruleset owns the
 * tutorial and when its next page comes; this card shows the page it publishes and sends the choice made on it.
 */
export function mountTutorial(shell: HTMLElement, send: Send): Tutorial {
  const root = document.createElement('dialog');
  root.className = 'dagger-tutorial dagger-frame';
  root.dataset.skin = 'parchment';
  root.setAttribute('aria-labelledby', 'dagger-tutorial-title');
  const progress = document.createElement('p');
  progress.className = 'dagger-tutorial-progress';
  const title = document.createElement('h3');
  title.id = 'dagger-tutorial-title';
  const body = document.createElement('div');
  body.className = 'dagger-tutorial-page';
  const actions = document.createElement('div');
  actions.className = 'dagger-tutorial-actions';
  const end = document.createElement('button');
  end.type = 'button';
  end.className = 'dagger-tutorial-end';
  end.textContent = 'End tutorial';
  const next = document.createElement('button');
  next.type = 'button';
  next.className = 'dagger-tutorial-next';
  actions.append(end, next);
  root.append(progress, title, body, actions);
  shell.append(root);

  let shown: number | null = null;
  const readOn = (): void => { if (shown !== null) send({ action: 'tutorial-continue', page: shown }); };
  next.addEventListener('click', readOn);
  end.addEventListener('click', () => { if (shown !== null) send({ action: 'tutorial-end' }); });
  // Escape (or B) reads on rather than ending the tutorial: putting a page aside is not giving the tutorial up.
  root.addEventListener('cancel', event => { event.preventDefault(); readOn(); });

  return {
    root,
    isOpen: () => root.open,
    readOn,
    update(value) {
      const page = isTutorial(value.tutorial) ? value.tutorial.page : null;
      const dead = value.mode !== 'playing' && value.mode !== 'modal';
      if (page === null || dead) {
        shown = null;
        if (root.open) root.close();
        return;
      }
      const device = value.inputDevice ?? 'keyboard';
      redraw(root, JSON.stringify([page, device, value.controls?.bindings ?? null]), () => {
        progress.textContent = `Tutorial · ${page.index + 1} of ${page.count}`;
        title.textContent = page.content.title;
        body.replaceChildren(...helpParagraphs(page.content, value.controls ?? null, device));
        next.textContent = page.index + 1 === page.count ? 'Finish' : 'Read on';
      });
      const changed = shown !== page.index;
      shown = page.index;
      if (!root.open) root.showModal();
      if (changed) next.focus();
    },
    dispose() { if (root.open) root.close(); root.remove(); },
  };
}
