import { fitPixelArt, image } from './art.js';
import type { Send } from './host.js';
import { isDeathProjection, type DeathProjection, type SaveSlotProjection } from './projection.js';
import { redraw } from './redraw.js';
import { screenForMode } from './screens.js';
import { slotRow } from './slots.js';

/**
 * The death screen the dead mode owns. It replaces the HUD rather than joining it; its choices,
 * camera, fade and sound cues arrive in the same projection as the image, and the ruleset decides
 * which choices death offers.
 */
export interface Death {
  readonly root: HTMLElement;
  update(dead: boolean, death: unknown, saveSlots: SaveSlotProjection): void;
  refreshArt(): void;
}

export function mountDeath(shell: HTMLElement, send: Send): Death {
  const root = shell.querySelector<HTMLElement>('.dagger-death')!;
  const screen = shell.querySelector<HTMLImageElement>('.dagger-death-screen')!;
  fitPixelArt(screen, root);
  const message = shell.querySelector<HTMLElement>('.dagger-death-message')!;
  // The saved games, as the menu lists them (character, place, date, play time); choosing one selects it.
  const slotList = shell.querySelector<HTMLOListElement>('.dagger-death-load-slots')!;
  let chosenSlot: string | null = null;
  const newGame = shell.querySelector<HTMLButtonElement>('.dagger-death-new')!;
  const load = shell.querySelector<HTMLButtonElement>('.dagger-death-load-button')!;
  const quit = shell.querySelector<HTMLButtonElement>('.dagger-death-quit')!;
  newGame.addEventListener('click', () => send({ action: 'death-new-game' }));
  load.addEventListener('click', () => { if (chosenSlot) send({ action: 'death-load-game', key: chosenSlot }); });
  quit.addEventListener('click', () => send({ action: 'death-quit' }));
  slotList.addEventListener('click', event => {
    const slot = (event.target as Element).closest<HTMLButtonElement>('[data-save-slot]');
    if (!slot || slot.disabled) return;
    chosenSlot = slot.dataset.saveSlot || null;
    draw();
  });

  let dead = false;
  let current: DeathProjection | null = null;
  let slots: SaveSlotProjection = { entries: [], diagnostic: null };

  const draw = (): void => {
    root.hidden = !dead;
    if (!dead) return;
    const death = current;
    message.textContent = death?.message ?? 'You have died.';
    root.dataset.cameraEffect = death?.cameraEffect ?? 'fall';
    root.dataset.fadeEffect = death?.fadeEffect ?? 'to-black';
    root.dataset.audioCue = death?.audioCue ?? 'player-death';
    // The ruleset owns which choices death offers; a snapshot without them offers none.
    const choices = death?.choices ?? [];
    const find = (action: string) => choices.find(choice => choice.action === action);
    const selected = death?.selected ?? null;
    const newChoice = find('death-new-game');
    const loadChoice = find('death-load-game');
    const quitChoice = find('death-quit');
    newGame.textContent = newChoice?.label ?? 'New game';
    load.textContent = loadChoice?.label ?? 'Load game';
    quit.textContent = quitChoice?.label ?? 'Quit to title';
    newGame.disabled = selected !== null || newChoice?.available !== true;
    quit.disabled = selected !== null || quitChoice?.available !== true;
    if (chosenSlot !== null && !slots.entries.some(entry => entry.key === chosenSlot)) chosenSlot = null;
    const loadAvailable = loadChoice?.available === true;
    const locked = selected !== null || !loadAvailable;
    redraw(slotList, JSON.stringify([slots.entries, chosenSlot, locked]), () => slotList.replaceChildren(...slots.entries.map(entry => {
      const row = slotRow(entry, entry.key === chosenSlot);
      row.querySelector('button')!.disabled = locked;
      return row;
    })));
    slotList.closest('section')!.hidden = slots.entries.length === 0;
    load.disabled = locked || chosenSlot === null;
    const art = image(death?.screen || screenForMode('dead')!);
    if (art !== null) screen.src = art;
  };

  return {
    root,
    update(isDead, death, saveSlots) {
      dead = isDead;
      current = isDeathProjection(death) ? death : null;
      slots = saveSlots;
      draw();
    },
    refreshArt: draw,
  };
}
