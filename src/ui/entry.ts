import { fitPixelArt, image } from './art.js';
import { button, type Send } from './host.js';
import { isBundles, isSaveSlots, type BundleProjection, type DaggerHud, type SaveSlotEntry } from './projection.js';
import { invalidate, redraw } from './redraw.js';
import { BEGIN_ACTION, TITLE_MODE, screenForMode } from './screens.js';
import { slotRow } from './slots.js';

/**
 * The title screen and the character-creation screens the title mode owns. The mode decides which
 * screen is up and the artifact it names is what it shows, the way the death screen is the dead
 * mode's. Character creation is drawn by the character module; while it is open its element moves
 * here from the menu's character panel.
 */
export interface Entry {
  readonly root: HTMLElement;
  isTitle(): boolean;
  update(value: DaggerHud): void;
  refreshArt(): void;
}

export function mountEntry(shell: HTMLElement, send: Send, creation: HTMLElement, onCreationOpens: () => void): Entry {
  const root = shell.querySelector<HTMLElement>('.dagger-entry')!;
  const screen = shell.querySelector<HTMLImageElement>('.dagger-entry-screen')!;
  fitPixelArt(screen, root);
  const creationHome = creation.parentElement!;
  const begin = shell.querySelector<HTMLButtonElement>('.dagger-entry-begin')!;
  begin.addEventListener('click', () => send({ action: BEGIN_ACTION }));
  const outcome = document.createElement('p'); outcome.className = 'dagger-entry-outcome'; outcome.setAttribute('role', 'status'); root.append(outcome);
  const parts = document.createElement('div'); parts.className = 'dagger-entry-parts'; root.append(parts);
  const create = button('Create character');
  create.className = 'dagger-entry-create'; create.dataset.testid = 'entry-create-character'; root.append(create);
  create.addEventListener('click', () => send({ action: 'character-begin' }));
  // The game a new run starts under: offered only when there is a choice, and only before creation opens.
  const bundle = document.createElement('label'); bundle.className = 'dagger-entry-bundle'; bundle.textContent = 'Game ';
  const bundleSelect = document.createElement('select'); bundleSelect.dataset.testid = 'entry-bundle'; bundle.append(bundleSelect); root.append(bundle);
  bundleSelect.addEventListener('change', () => send({ action: 'bundle-select', key: bundleSelect.value }));
  // The game choice and the two buttons stack in one column at the foot of the screen, so the
  // choice can never cover a button however tall the skin draws them.
  const actions = document.createElement('div'); actions.className = 'dagger-entry-actions';
  const buttons = document.createElement('div'); buttons.className = 'dagger-entry-buttons';
  // Continue loads the newest saved game; Load lists them all. Both show only when there are saves.
  const resume = button('Continue'); resume.className = 'dagger-entry-continue'; resume.dataset.testid = 'entry-continue';
  const load = button('Load'); load.className = 'dagger-entry-load'; load.dataset.testid = 'entry-load';
  load.setAttribute('aria-expanded', 'false');
  const saved = document.createElement('ol'); saved.className = 'dagger-entry-saves dagger-save-slots-list dagger-frame'; saved.hidden = true;
  saved.setAttribute('aria-label', 'Saved games');
  buttons.append(resume, begin, create, load);
  actions.append(saved, bundle, buttons);
  root.append(actions);
  let slots: readonly SaveSlotEntry[] = [];
  let listed = false;
  resume.addEventListener('click', () => { const newest = slots[0]; if (newest) send({ action: 'load-slot', key: newest.key }); });
  load.addEventListener('click', () => {
    saved.hidden = !saved.hidden;
    load.setAttribute('aria-expanded', String(!saved.hidden));
    if (!saved.hidden) saved.querySelector<HTMLButtonElement>('button')?.focus();
  });
  saved.addEventListener('click', event => {
    const chosen = (event.target as HTMLElement).closest<HTMLButtonElement>('[data-save-slot]');
    if (chosen?.dataset.saveSlot) send({ action: 'load-slot', key: chosen.dataset.saveSlot });
  });

  let title = false;
  let mode: string | null = null;
  let pickScreens: readonly string[] = [];
  let bundles: BundleProjection | null = null;

  const draw = (): void => {
    root.hidden = !title;
    if (!title) return;
    root.dataset.mode = mode ?? TITLE_MODE;
    const creating = mode !== null;
    if (creating) onCreationOpens();
    (creating ? root : creationHome).append(creation);
    creation.classList.toggle('dagger-entry-creation', creating);
    creation.classList.toggle('dagger-frame', creating);
    begin.hidden = creating;
    create.hidden = creating;
    resume.hidden = creating || slots.length === 0;
    load.hidden = creating || slots.length === 0;
    if (load.hidden) { saved.hidden = true; load.setAttribute('aria-expanded', 'false'); }
    redraw(saved, JSON.stringify(slots), () => saved.replaceChildren(...slots.map(entry => slotRow(entry, false))));
    bundle.hidden = creating || bundles === null || bundles.options.length < 2;
    if (bundles !== null && document.activeElement !== bundleSelect) {
      const current = bundles;
      redraw(bundleSelect, JSON.stringify(current.options), () =>
        bundleSelect.replaceChildren(...current.options.map(option => new Option(option.title, option.id))));
      bundleSelect.value = current.current;
    }
    const primary = screenForMode(mode ?? TITLE_MODE)!;
    const art = image(primary);
    if (art !== null) screen.src = art;
    const shown = mode === 'character-pick' ? pickScreens.filter(id => id !== primary) : [];
    redraw(parts, JSON.stringify([shown, shown.map(id => image(id) !== null)]), () => parts.replaceChildren(...shown.flatMap(id => {
      const source = image(id); if (source === null) return [];
      const part = document.createElement('img'); part.alt = ''; part.dataset.mediaId = id;
      // A pick part is drawn at the scale its screen is, so the two pieces of art keep one pixel size.
      part.addEventListener('load', () => { part.style.width = `${part.naturalWidth * screen.width / Math.max(1, screen.naturalWidth)}px`; });
      part.src = source; return [part];
    })));
  };

  return {
    root,
    isTitle: () => title,
    update(value) {
      const wasTitle = title;
      title = value.mode === TITLE_MODE;
      // The title asks for the saved games each time it appears, so Continue names the newest.
      if (title && !wasTitle) { listed = false; }
      if (title && !listed) { listed = true; send({ action: 'save-slots' }); }
      // The Host lists newest first.
      if (isSaveSlots(value.saveSlots)) slots = value.saveSlots.entries;
      mode = title && value.character?.creationAvailable && value.character.creation ? value.character.creation.editing
        ? value.character.creation.mode ?? null : value.character.creation.summary ? 'character-pick' : null : null;
      pickScreens = value.pickScreens ?? [];
      bundles = isBundles(value.bundles) ? value.bundles : null;
      outcome.textContent = value.lastOutcome;
      draw();
    },
    refreshArt() { invalidate(parts); draw(); },
  };
}
