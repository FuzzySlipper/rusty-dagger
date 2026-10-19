import type { Send } from './host.js';
import { viewSummary, type DaggerHud } from './projection.js';
import { redraw } from './redraw.js';

/**
 * The heads-up display over the world: the title line, vitals, breath, recent messages, the
 * activation mode, effects, detection, status rows and the cinematic's skip. It takes no focus in
 * play. While a cinematic plays the shell draws the video above this page, so any key or button
 * press skips it, as in the classic game.
 */
export interface Hud {
  cinematicPlaying(): boolean;
  update(value: DaggerHud): void;
  dispose(): void;
}

const ACTIVATION_LABELS: Readonly<Record<string, string>> = { grab: 'Grab', info: 'Information', talk: 'Talk', steal: 'Steal / Lockpick', bash: 'Bash' };

export function mountHud(shell: HTMLElement, send: Send): Hud {
  const title = shell.querySelector<HTMLElement>('.dagger-title strong')!;
  const siteName = shell.querySelector<HTMLElement>('.dagger-title .dagger-site')!;
  const calendar = shell.querySelector<HTMLElement>('.dagger-calendar')!;
  const vitals = shell.querySelector<HTMLElement>('.dagger-vitals')!;
  const outcome = shell.querySelector<HTMLOListElement>('.dagger-outcome')!;
  const breath = shell.querySelector<HTMLLabelElement>('.dagger-breath')!;
  const activationIndicator = shell.querySelector<HTMLElement>('.dagger-activation-indicator')!;
  const view = shell.querySelector<HTMLParagraphElement>('.dagger-view')!;
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
      redraw(vitals, JSON.stringify(value.resources), () => vitals.replaceChildren(...value.resources.map(resource => {
        const row = document.createElement('p');
        const label = document.createElement('span');
        const amount = document.createElement('strong');
        label.textContent = resource.label;
        amount.dataset.resource = resource.id;
        amount.textContent = `${resource.current} / ${resource.maximum}`;
        row.append(label, amount);
        return row;
      })));
      redraw(outcome, JSON.stringify(value.messages), () => outcome.replaceChildren(...value.messages.map(message => {
        const line = document.createElement('li'); line.textContent = message; return line;
      })));
      breath.hidden = !value.breath;
      if (value.breath) {
        const meter = breath.querySelector('meter')!;
        meter.max = value.breath.maximum; meter.value = value.breath.current;
        meter.setAttribute('aria-label', 'Breath');
        breath.querySelector('span')!.textContent = `${value.breath.current} / ${value.breath.maximum}`;
        breath.classList.toggle('low', value.breath.low);
      }
      activationIndicator.textContent = value.activation ? ACTIVATION_LABELS[value.activation.mode] ?? value.activation.mode : '';
      activationIndicator.hidden = value.mode !== 'playing' || !value.activation;
      view.textContent = value.view ? viewSummary(value.view) : '';
      detectors.hidden = !value.detectors?.length;
      redraw(detectors, JSON.stringify(value.detectors ?? []), () => detectors.replaceChildren(...(value.detectors ?? []).map(source => {
        const row = document.createElement('p');
        row.dataset.source = source.source;
        row.textContent = `Detect ${source.kind}: ` + (source.contacts.length ? source.contacts.map(contact => {
          const bearing = Math.round(contact.bearingRadians * 180 / Math.PI);
          return `${contact.label}, ${contact.distance.toFixed(1)} m, bearing ${bearing}°`;
        }).join('; ') : 'none nearby');
        return row;
      })));
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
      cinematicSkip.textContent = cinematic?.skipLabel ?? 'Skip';
      redraw(status, JSON.stringify(value.slots ?? []), () => status.replaceChildren(...(value.slots ?? []).map(row => {
        const item = document.createElement('p'); item.textContent = `${row.label}: ${row.detail}`; return item;
      })));
    },
    dispose() {
      document.removeEventListener('keydown', skipOnPress, true);
      document.removeEventListener('pointerdown', skipOnPress, true);
    },
  };
}
