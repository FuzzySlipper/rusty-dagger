import { buttonLabel, keyLabel } from './controls.js';
import type { Send } from './host.js';
import type { DaggerHud } from './projection.js';
import { redraw } from './redraw.js';
import { spellIcon } from './spell-icon.js';

/**
 * The heads-up display over the world: the title line, the compass with detected contacts, vitals,
 * breath, recent messages, what the player holds ready (spell, weapon, activation mode), the
 * interaction prompt, effects, status rows and the cinematic's skip. It takes no focus in play.
 * While a cinematic plays the shell draws the video above this page, so any key or button press
 * skips it, as in the classic game.
 *
 * Every word comes from the projection (mode names, the compass heading, prompts, detector distances
 * and directions); the HUD only places marks by the angles and amounts it is given.
 */
export interface Hud {
  cinematicPlaying(): boolean;
  update(value: DaggerHud): void;
  dispose(): void;
}

/** How much of the horizon the compass strip shows, either side of straight ahead. */
const COMPASS_HALF_SPAN = Math.PI / 2;
/** How many of the nearest detected contacts are named on the compass. */
const NAMED_CONTACTS = 3;
const CARDINALS = ['N', 'NE', 'E', 'SE', 'S', 'SW', 'W', 'NW'];

/** An angle wrapped into (-π, π]. */
function wrap(radians: number): number {
  const turn = Math.PI * 2;
  const wrapped = ((radians % turn) + turn) % turn;
  return wrapped > Math.PI ? wrapped - turn : wrapped;
}

/** Where a bearing falls on the strip, as a percentage across it, for a view facing `yaw`; null when out of view. */
function compassOffset(bearing: number, yaw: number): number | null {
  const delta = wrap(bearing - yaw);
  return Math.abs(delta) > COMPASS_HALF_SPAN ? null : 50 + delta / COMPASS_HALF_SPAN * 50;
}

export function mountHud(shell: HTMLElement, send: Send): Hud {
  const title = shell.querySelector<HTMLElement>('.dagger-title strong')!;
  const siteName = shell.querySelector<HTMLElement>('.dagger-title .dagger-site')!;
  const calendar = shell.querySelector<HTMLElement>('.dagger-calendar')!;
  const compass = shell.querySelector<HTMLElement>('.dagger-compass')!;
  const strip = compass.querySelector<HTMLElement>('.dagger-compass-strip')!;
  const contactLayer = compass.querySelector<HTMLElement>('.dagger-compass-contacts')!;
  const view = shell.querySelector<HTMLParagraphElement>('.dagger-view')!;
  const prompt = shell.querySelector<HTMLParagraphElement>('.dagger-prompt')!;
  const vitals = shell.querySelector<HTMLElement>('.dagger-vitals')!;
  const outcome = shell.querySelector<HTMLOListElement>('.dagger-outcome')!;
  const breath = shell.querySelector<HTMLElement>('.dagger-breath')!;
  const readySpell = shell.querySelector<HTMLElement>('.dagger-ready-spell')!;
  const weapon = shell.querySelector<HTMLElement>('.dagger-weapon')!;
  const activationIndicator = shell.querySelector<HTMLElement>('.dagger-activation-indicator')!;
  const detectors = shell.querySelector<HTMLElement>('.dagger-detectors')!;
  const status = shell.querySelector<HTMLElement>('.dagger-status')!;
  const effects = shell.querySelector<HTMLElement>('.dagger-effects')!;
  const effectList = effects.querySelector<HTMLUListElement>('ul')!;
  const cinematicSkip = shell.querySelector<HTMLButtonElement>('.dagger-cinematic-skip')!;
  const skip = (): void => send({ action: 'cinematic-skip' });
  cinematicSkip.addEventListener('click', skip);
  let cinematicPlaying = false;
  const skipOnPress = (event: KeyboardEvent | PointerEvent): void => {
    if (!cinematicPlaying || (event instanceof KeyboardEvent && event.repeat)) return;
    event.preventDefault();
    event.stopImmediatePropagation();
    skip();
  };
  document.addEventListener('keydown', skipOnPress, true);
  document.addEventListener('pointerdown', skipOnPress, true);

  // The compass's fixed marks: the eight winds and a tick every fifteen degrees between them.
  const marks: { element: HTMLElement; bearing: number }[] = [];
  for (let degrees = 0; degrees < 360; degrees += 15) {
    const mark = document.createElement('span');
    const cardinal = degrees % 45 === 0 ? CARDINALS[degrees / 45]! : null;
    mark.className = cardinal ? `dagger-compass-wind${cardinal.length === 1 ? ' is-cardinal' : ''}` : 'dagger-compass-tick';
    if (cardinal) mark.textContent = cardinal;
    strip.append(mark);
    marks.push({ element: mark, bearing: degrees * Math.PI / 180 });
  }
  const contacts: { element: HTMLElement; bearing: number }[] = [];
  const place = (element: HTMLElement, bearing: number, yaw: number): void => {
    const offset = compassOffset(bearing, yaw);
    element.hidden = offset === null;
    if (offset !== null) element.style.left = `${offset}%`;
  };

  return {
    cinematicPlaying: () => cinematicPlaying,
    update(value) {
      title.textContent = value.mode === 'paused' ? 'Paused' : value.mode === 'dead' ? 'Defeated'
        : value.mode === 'title' ? 'Title' : value.mode === 'modal' ? 'Interaction' : 'Exploring';
      if (value.mode === 'playing' && value.timeControl?.enabled) title.textContent = `Action-driven · ${value.timeControl.held ? 'Paused' : 'Acting'}`;
      siteName.textContent = value.site?.name ?? '';
      const date = value.calendar ?? null;
      calendar.textContent = date === null ? '' : `${date.date} · ${date.time}`;
      calendar.hidden = date === null;

      // Vitals: a bar per resource, its fill the published amount over its maximum.
      redraw(vitals, JSON.stringify(value.resources), () => vitals.replaceChildren(...value.resources.map(resource => {
        const row = document.createElement('p');
        row.className = 'dagger-vital';
        row.dataset.vital = resource.id;
        const label = document.createElement('span');
        label.className = 'dagger-vital-label';
        label.textContent = resource.label;
        const bar = document.createElement('span');
        bar.className = 'dagger-bar';
        bar.setAttribute('role', 'meter');
        bar.setAttribute('aria-label', resource.label);
        bar.setAttribute('aria-valuemin', '0');
        bar.setAttribute('aria-valuemax', String(resource.maximum));
        bar.setAttribute('aria-valuenow', String(resource.current));
        const fill = document.createElement('span');
        fill.style.width = `${resource.maximum > 0 ? Math.max(0, Math.min(1, resource.current / resource.maximum)) * 100 : 0}%`;
        bar.append(fill);
        const amount = document.createElement('strong');
        amount.dataset.resource = resource.id;
        amount.textContent = `${resource.current} / ${resource.maximum}`;
        row.append(label, bar, amount);
        return row;
      })));
      redraw(outcome, JSON.stringify(value.messages), () => outcome.replaceChildren(...value.messages.map(message => {
        const line = document.createElement('li'); line.textContent = message; return line;
      })));
      breath.hidden = !value.breath;
      if (value.breath) {
        const bar = breath.querySelector<HTMLElement>('.dagger-bar')!;
        bar.setAttribute('role', 'meter');
        bar.setAttribute('aria-label', 'Breath');
        bar.setAttribute('aria-valuemax', String(value.breath.maximum));
        bar.setAttribute('aria-valuenow', String(value.breath.current));
        bar.querySelector('span')!.style.width = `${value.breath.maximum > 0 ? Math.max(0, Math.min(1, value.breath.current / value.breath.maximum)) * 100 : 0}%`;
        breath.querySelector('strong')!.textContent = `${value.breath.current} / ${value.breath.maximum}`;
        breath.classList.toggle('low', value.breath.low);
      }

      // What the player holds ready: the readied spell, the weapon drawn or sheathed, the activation mode.
      const playing = value.mode === 'playing';
      const spells = value.spells ?? null;
      const ready = spells?.ready ? spells.available.find(spell => spell.key === spells.ready) ?? null : null;
      readySpell.hidden = ready === null || !playing;
      redraw(readySpell, JSON.stringify([ready, spells?.icons ?? null]), () => {
        const icon = readySpell.querySelector('.dagger-ready-spell-icon')!;
        icon.replaceChildren(...(ready && typeof ready.icon === 'number' ? [spellIcon(spells?.icons, ready.icon)] : []));
        readySpell.querySelector('.dagger-ready-spell-name')!.textContent = ready?.name ?? '';
      });
      weapon.hidden = !value.weapon || !playing;
      weapon.classList.toggle('is-sheathed', value.weapon?.drawn === false);
      weapon.querySelector('.dagger-weapon-state')!.textContent = value.weapon?.state ?? '';
      weapon.querySelector('.dagger-weapon-name')!.textContent = value.weapon?.name ?? '';
      activationIndicator.textContent = value.activation ? value.activation.label ?? value.activation.mode : '';
      activationIndicator.hidden = !playing || !value.activation;

      // The prompt names what use does to the aimed target and the control that does it on the device in use.
      const aimed = playing ? value.prompt ?? null : null;
      prompt.hidden = aimed === null;
      if (aimed) {
        const use = value.controls?.bindings.find(binding => binding.id === 'interact');
        const controller = value.inputDevice === 'controller';
        const control = controller ? (use?.buttons?.[0] !== undefined ? buttonLabel(use.buttons[0]) : null) : (use?.keys[0] ? keyLabel(use.keys[0]) : null);
        const key = prompt.querySelector('kbd')!;
        key.textContent = control ?? '';
        key.hidden = control === null;
        prompt.dataset.device = controller ? 'controller' : 'keyboard';
        prompt.querySelector('span')!.textContent = `${aimed.action} ${aimed.target}`;
      }

      // The compass: winds and ticks slide with the yaw; each detected contact is a mark at its bearing.
      const yaw = value.view?.yawRadians ?? null;
      compass.hidden = yaw === null || !playing;
      view.textContent = value.view?.heading ? `Facing ${value.view.heading}` : '';
      view.hidden = compass.hidden;
      const sources = value.detectors ?? [];
      redraw(contactLayer, JSON.stringify(sources), () => {
        contacts.length = 0;
        const all = sources.flatMap(source => source.contacts.map(contact => ({ source, contact })));
        const named = new Set(all.slice().sort((a, b) => a.contact.distance - b.contact.distance).slice(0, NAMED_CONTACTS).map(row => row.contact));
        contactLayer.replaceChildren(...all.map(({ source, contact }) => {
          const mark = document.createElement('span');
          mark.className = 'dagger-compass-contact';
          mark.dataset.detector = source.kind;
          mark.dataset.contact = contact.id;
          if (named.has(contact)) {
            const label = document.createElement('span');
            label.textContent = `${contact.label} · ${contact.distanceLabel ?? ''}`.trim();
            mark.append(label);
          }
          contacts.push({ element: mark, bearing: contact.bearingRadians });
          return mark;
        }));
        // Read aloud as a list, since the marks themselves are drawn only.
        detectors.replaceChildren(...sources.map(source => {
          const row = document.createElement('p');
          row.dataset.source = source.source;
          row.textContent = `${source.label ?? source.kind}: ` + (source.contacts.length
            ? source.contacts.map(contact => `${contact.label}, ${contact.distanceLabel ?? ''} ${contact.direction ?? ''}`.trim()).join('; ')
            : 'none nearby');
          return row;
        }));
      });
      detectors.hidden = sources.length === 0;
      if (yaw !== null) {
        for (const mark of marks) place(mark.element, mark.bearing, yaw);
        for (const contact of contacts) place(contact.element, contact.bearing, yaw);
      }

      const activeEffects = value.effects ?? [];
      effects.hidden = activeEffects.length === 0;
      redraw(effectList, JSON.stringify(activeEffects), () => effectList.replaceChildren(...activeEffects.map(effect => {
        const item = document.createElement('li');
        item.dataset.effect = effect.id;
        const name = document.createElement('strong');
        name.textContent = effect.spell === effect.name ? effect.name : `${effect.spell}: ${effect.name}`;
        const detail = document.createElement('span');
        detail.textContent = effect.detail;
        item.append(name, ' ', detail);
        return item;
      })));
      const cinematic = value.cinematic ?? null;
      cinematicPlaying = cinematic !== null;
      cinematicSkip.hidden = cinematic === null;
      // The hint names what skips on the device in use: a pad's A, or any key or click.
      const skipKey = document.createElement('kbd');
      skipKey.textContent = value.inputDevice === 'controller' ? 'A' : 'Any key';
      redraw(cinematicSkip, `${cinematic?.skipLabel ?? ''}|${skipKey.textContent}`, () => cinematicSkip.replaceChildren(skipKey, ` ${cinematic?.skipLabel ?? 'Skip'}`));
      redraw(status, JSON.stringify(value.slots ?? []), () => status.replaceChildren(...(value.slots ?? []).map(row => {
        const item = document.createElement('p');
        const label = document.createElement('strong');
        label.textContent = row.label;
        item.append(label, `: ${row.detail}`);
        return item;
      })));
      status.hidden = (value.slots ?? []).length === 0;
    },
    dispose() {
      document.removeEventListener('keydown', skipOnPress, true);
      document.removeEventListener('pointerdown', skipOnPress, true);
    },
  };
}
