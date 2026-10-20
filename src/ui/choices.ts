import { cssUrl, image } from './art.js';
import { button, type Send } from './host.js';
import type { DaggerHud } from './projection.js';
import { redraw } from './redraw.js';

/**
 * The choices the product asks the player to make over the world: a court's verdict and a spell
 * that needs a target (create item, identify, teleport, dispel). Each is a centred dialog; the
 * court and create item need an answer, and the others take Escape and Back as their Cancel.
 */
export interface Choices {
  readonly legal: HTMLElement;
  readonly createItem: HTMLElement;
  /** The spell choices that have a Cancel. */
  readonly cancellable: readonly HTMLElement[];
  update(value: DaggerHud): void;
  refreshArt(): void;
}

export function mountChoices(shell: HTMLElement, before: HTMLElement, send: Send): Choices {
  const legal = document.createElement('section');
  legal.className = 'dagger-legal'; legal.hidden = true; legal.setAttribute('role', 'dialog');
  legal.setAttribute('aria-modal', 'true'); legal.setAttribute('aria-label', 'Law and court'); shell.append(legal);
  // A spell that asks the caster to choose shows its choice as a centred panel over the world.
  const service = (name: string, label: string): HTMLElement => {
    const section = document.createElement('section');
    section.className = `dagger-spell-service dagger-frame dagger-${name}`; section.hidden = true;
    section.setAttribute('role', 'dialog'); section.setAttribute('aria-label', label);
    before.before(section);
    return section;
  };
  const createItem = service('create-item', 'Create item');
  const identify = service('identify', 'Identify');
  const teleport = service('teleport', 'Teleport');
  const dispel = service('dispel', 'Dispel magic');
  const heading = (text: string): HTMLHeadingElement => { const h = document.createElement('h2'); h.textContent = text; return h; };
  const cancel = (onClick: () => void): HTMLButtonElement => {
    const control = button('Cancel'); control.dataset.escape = ''; control.addEventListener('click', onClick); return control;
  };
  let last: DaggerHud | null = null;

  const drawLegal = (caseView: NonNullable<DaggerHud['legal']> | null): void => {
    legal.hidden = caseView === null;
    if (caseView === null) { redraw(legal, '', () => legal.replaceChildren()); return; }
    legal.dataset.phase = caseView.phase;
    const prisonArt = caseView.phase === 'prison' ? image('screen.prison') : null;
    legal.style.backgroundImage = prisonArt === null ? '' : cssUrl(prisonArt);
    redraw(legal, JSON.stringify(caseView), () => {
      const card = document.createElement('div'); card.className = 'dagger-legal-card dagger-frame';
      const charges = document.createElement('p'); charges.textContent = `Charges: ${caseView.charges.join(', ')}`;
      const message = document.createElement('p'); message.textContent = caseView.message;
      card.append(heading(caseView.title), charges, message);
      for (const choice of caseView.choices) {
        const control = button(choice.label);
        control.addEventListener('click', () => send({ action: 'legal-choice', revision: caseView.revision, key: choice.id }));
        card.append(control);
      }
      legal.replaceChildren(card);
    });
  };

  return {
    legal, createItem, cancellable: [identify, teleport, dispel],
    update(value) {
      last = value;
      drawLegal(value.legal ?? null);
      const createChoice = value.createItem ?? null;
      createItem.hidden = createChoice === null;
      redraw(createItem, JSON.stringify(createChoice), () => {
        createItem.replaceChildren();
        if (createChoice === null) return;
        createItem.append(heading('Create Item'));
        for (const option of createChoice.options) {
          const control = button(option.label);
          control.addEventListener('click', () => send({ action: 'create-item-select', revision: createChoice.revision, key: option.id }));
          createItem.append(control);
        }
      });
      const identifyChoice = value.identify ?? null;
      identify.hidden = identifyChoice === null;
      redraw(identify, JSON.stringify(identifyChoice), () => {
        identify.replaceChildren();
        if (identifyChoice === null) return;
        const choose = (key?: string): void => send(key
          ? { action: 'identify-select', revision: identifyChoice.revision, key }
          : { action: 'identify-cancel', revision: identifyChoice.revision });
        identify.append(heading(`Identify (${identifyChoice.cost} magicka per selection)`));
        for (const option of [...identifyChoice.options, ...(identifyChoice.options.length ? [{ id: 'all', label: 'All unidentified items' }] : [])]) {
          const control = button(option.label); control.addEventListener('click', () => choose(option.id)); identify.append(control);
        }
        identify.append(cancel(() => choose()));
      });
      const teleportChoice = value.teleport ?? null;
      teleport.hidden = teleportChoice === null;
      redraw(teleport, JSON.stringify(teleportChoice), () => {
        teleport.replaceChildren();
        if (teleportChoice === null) return;
        const choose = (key: string): void => send({ action: 'teleport-select', revision: teleportChoice.revision, key });
        teleport.append(heading('Teleport'));
        const anchor = button('Set anchor'); anchor.addEventListener('click', () => choose('anchor'));
        const recall = button('Recall to anchor'); recall.disabled = !teleportChoice.anchorSet; recall.addEventListener('click', () => choose('recall'));
        teleport.append(anchor, recall, cancel(() => choose('cancel')));
      });
      const dispelChoice = value.dispel ?? null;
      dispel.hidden = dispelChoice === null;
      redraw(dispel, JSON.stringify(dispelChoice), () => {
        dispel.replaceChildren();
        if (dispelChoice === null) return;
        const choose = (key?: string): void => send(key
          ? { action: 'dispel-select', revision: dispelChoice.revision, key }
          : { action: 'dispel-cancel', revision: dispelChoice.revision });
        dispel.append(heading('Dispel magic'));
        for (const option of dispelChoice.options) {
          const control = button(option.label); control.addEventListener('click', () => choose(option.id)); dispel.append(control);
        }
        dispel.append(cancel(() => choose()));
      });
    },
    refreshArt() { if (last) drawLegal(last.legal ?? null); },
  };
}
