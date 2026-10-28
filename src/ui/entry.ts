import { fitPixelArt, image } from './art.js';
import { button, type Send } from './host.js';
import { isBundles, isSaveSlots, type BundleProjection, type DaggerHud, type SaveSlotEntry, type Unavailable } from './projection.js';
import { createCycler, type Cycler } from './cycler.js';
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
  /**
   * The shoulder buttons while creating a character: focus jumps to the first control of the
   * previous or next part (identity, class, background, attributes, skills, the commit row), so a pad
   * crosses the long form in a few presses.
   */
  stepPanel(direction: -1 | 1): void;
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
  // A cycler rather than a native select, so a pad steps it as easily as the mouse does.
  const bundle = document.createElement('div'); bundle.className = 'dagger-entry-bundle';
  const bundleLabel = document.createElement('span'); bundleLabel.textContent = 'Game';
  const bundleChoice = document.createElement('div'); bundleChoice.dataset.testid = 'entry-bundle';
  bundle.append(bundleLabel, bundleChoice); root.append(bundle);
  let bundleCycler: Cycler | null = null;
  // Choosing a game restarts the session behind the title, which hides the choice for a moment; the
  // step the player was pressing takes focus again when it returns, so a second press steps again.
  let bundleStep: string | null = null;
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
  // When no game can be played (the Daggerfall data is not imported, unreadable or stale), the title says
  // why and what fixes it, in place of everything that would start one.
  const unavailable = document.createElement('section'); unavailable.className = 'dagger-entry-unavailable dagger-frame';
  unavailable.dataset.testid = 'entry-unavailable'; unavailable.setAttribute('role', 'alert'); unavailable.hidden = true;
  const unavailableTitle = document.createElement('h2');
  const unavailableProblems = document.createElement('ul');
  // Import converts the player's own Daggerfall folder in place of the missing data: the host shows its folder
  // picker, the tool's output scrolls below while it runs, and the game opens once it has finished.
  const importStatus = document.createElement('p'); importStatus.className = 'dagger-entry-import-status'; importStatus.setAttribute('role', 'status');
  const importOutput = document.createElement('pre'); importOutput.className = 'dagger-entry-import-output'; importOutput.dataset.testid = 'entry-import-output';
  const importButton = button('Import'); importButton.className = 'dagger-entry-import'; importButton.dataset.testid = 'entry-import';
  importButton.addEventListener('click', () => send({ action: 'content-import' }));
  const importCancel = button('Cancel'); importCancel.className = 'dagger-entry-import-cancel'; importCancel.dataset.testid = 'entry-import-cancel';
  importCancel.addEventListener('click', () => send({ action: 'content-import-cancel' }));
  const importRow = document.createElement('div'); importRow.className = 'dagger-entry-import-row'; importRow.append(importButton, importCancel);
  unavailable.append(unavailableTitle, unavailableProblems, importStatus, importOutput, importRow);
  // The outcome of a choice sits above the controls, so a long one never covers them.
  actions.append(unavailable, outcome, saved, bundle, buttons);
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
  let blocked: Unavailable | null = null;

  const draw = (): void => {
    root.hidden = !title;
    if (!title) return;
    root.dataset.mode = mode ?? TITLE_MODE;
    const creating = mode !== null;
    if (creating) onCreationOpens();
    (creating ? root : creationHome).append(creation);
    creation.classList.toggle('dagger-entry-creation', creating);
    creation.classList.toggle('dagger-frame', creating);
    unavailable.hidden = blocked === null;
    if (blocked !== null) {
      const shown = blocked;
      redraw(unavailableProblems, JSON.stringify([shown.title, shown.problems]), () => {
        unavailableTitle.textContent = shown.title;
        unavailableProblems.replaceChildren(...shown.problems.map(problem => { const item = document.createElement('li'); item.textContent = problem; return item; }));
      });
      const importing = shown.import ?? null;
      const busy = importing !== null && (importing.state === 'choosing' || importing.state === 'running');
      importStatus.hidden = importing === null;
      importStatus.textContent = importing?.status ?? '';
      importOutput.hidden = importing === null || importing.output === '';
      if (importOutput.textContent !== (importing?.output ?? '')) {
        importOutput.textContent = importing?.output ?? '';
        importOutput.scrollTop = importOutput.scrollHeight;
      }
      const offered = importing !== null && !busy && importing.state !== 'installed';
      if (!offered && importButton.contains(document.activeElement) && importing?.state === 'running') importCancel.focus();
      importButton.hidden = !offered;
      importCancel.hidden = importing?.state !== 'running';
      if (offered && !importRow.contains(document.activeElement)) importButton.focus();
    }
    begin.hidden = creating || blocked !== null;
    // While creating, what blocks Start is shown beside it; the title's outcome plate stays for title choices.
    outcome.hidden = creating;
    create.hidden = creating || blocked !== null;
    resume.hidden = creating || blocked !== null || slots.length === 0;
    load.hidden = creating || blocked !== null || slots.length === 0;
    if (load.hidden) { saved.hidden = true; load.setAttribute('aria-expanded', 'false'); }
    redraw(saved, JSON.stringify(slots), () => saved.replaceChildren(...slots.map(entry => slotRow(entry, false))));
    const hideBundle = creating || blocked !== null || bundles === null || bundles.options.length < 2;
    if (hideBundle && !bundle.hidden && bundle.contains(document.activeElement)) bundleStep = document.activeElement?.getAttribute('aria-label') ?? null;
    bundle.hidden = hideBundle;
    if (bundles !== null) {
      const current = bundles;
      redraw(bundleChoice, JSON.stringify(current.options), () => {
        bundleCycler = createCycler('Game', current.options.map(option => ({ id: option.id, label: option.title })), current.current,
          id => send({ action: 'bundle-select', key: id }));
        bundleChoice.replaceChildren(bundleCycler.element);
      });
      bundleCycler?.set(current.current);
    }
    if (!bundle.hidden && bundleStep !== null) {
      const step = Array.from(bundle.querySelectorAll<HTMLButtonElement>('button')).find(button => button.getAttribute('aria-label') === bundleStep);
      step?.focus();
      bundleStep = null;
    }
    if (creating) bundleStep = null;
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
      if (title && !listed && !value.unavailable) { listed = true; send({ action: 'save-slots' }); }
      // The Host lists newest first.
      if (isSaveSlots(value.saveSlots)) slots = value.saveSlots.entries;
      mode = title && value.character?.creationAvailable && value.character.creation ? value.character.creation.editing
        ? value.character.creation.mode ?? null : value.character.creation.summary ? 'character-pick' : null : null;
      pickScreens = value.pickScreens ?? [];
      bundles = isBundles(value.bundles) ? value.bundles : null;
      blocked = value.unavailable ?? null;
      outcome.textContent = value.lastOutcome;
      draw();
    },
    refreshArt() { invalidate(parts); draw(); },
    stepPanel(direction) {
      if (mode === null) return;
      const parts = Array.from(creation.querySelectorAll<HTMLElement>('.dagger-creation-panel, .dagger-creation-actions'))
        .filter(part => !part.closest('[hidden]'));
      const first = (part: HTMLElement): HTMLElement | null => part.querySelector<HTMLElement>('button:not(:disabled), input:not(:disabled), [tabindex="0"]');
      if (parts.length === 0) return;
      const at = parts.findIndex(part => part.contains(document.activeElement));
      for (let step = 1; step <= parts.length; step++) {
        const part = parts[((at < 0 ? (direction > 0 ? -1 : 0) : at) + direction * step + parts.length * step) % parts.length]!;
        const target = first(part);
        if (target) { target.focus(); target.scrollIntoView?.({ block: 'nearest' }); return; }
      }
    },
  };
}
