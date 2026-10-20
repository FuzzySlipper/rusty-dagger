import { fitPixelArt, image } from './art.js';
import { button, type Send } from './host.js';
import { isBundles, type BundleProjection, type DaggerHud } from './projection.js';
import { invalidate, redraw } from './redraw.js';
import { BEGIN_ACTION, TITLE_MODE, screenForMode } from './screens.js';

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
  buttons.append(begin, create);
  actions.append(bundle, buttons);
  root.append(actions);

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
      title = value.mode === TITLE_MODE;
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
