import type { Send } from './host.js';
import { letterPickerButton } from './letter-picker.js';
import type { DaggerHud } from './projection.js';

/** Text the dungeon shows the player (an inscription, a riddle), with an answer box when it asks one. */
export interface DungeonText {
  readonly root: HTMLElement;
  /** Continue, or Cancel when the text asks for an answer. */
  close(): void;
  /** Where focus starts when the text appears: the answer field when one is asked (a pad's accept there opens the letter picker), else Continue. */
  initialFocus(): HTMLElement | null;
  update(value: DaggerHud): void;
}

export function mountDungeonText(shell: HTMLElement, send: Send): DungeonText {
  const root = shell.querySelector<HTMLElement>('.dagger-dungeon-text')!;
  const body = shell.querySelector<HTMLElement>('.dagger-dungeon-text-body')!;
  const form = shell.querySelector<HTMLFormElement>('.dagger-dungeon-text-answer')!;
  const answer = form.querySelector<HTMLInputElement>('input')!;
  const closeButton = shell.querySelector<HTMLButtonElement>('.dagger-dungeon-text-close')!;
  // A pad cannot type: the letter picker writes the answer into the field.
  const pick = letterPickerButton(answer, 'Enter the answer with the letter picker');
  form.querySelector('button[type="submit"]')!.before(pick);
  let current: NonNullable<DaggerHud['dungeonText']> | null = null;
  form.addEventListener('submit', event => {
    event.preventDefault();
    if (!current?.requiresAnswer) return;
    send({ action: 'dungeon-text-answer', revision: current.revision, item: current.actionId, text: answer.value });
  });
  const close = (): void => {
    if (current) send({ action: 'dungeon-text-close', revision: current.revision, item: current.actionId });
  };
  closeButton.addEventListener('click', close);
  return {
    root,
    close,
    initialFocus: () => current?.requiresAnswer ? answer : closeButton,
    update(value) {
      const text = value.dungeonText ?? null;
      // A new text starts with an empty answer; the same one keeps what the player is typing.
      if (text?.revision !== current?.revision || text?.actionId !== current?.actionId) answer.value = '';
      current = text;
      root.hidden = text === null || value.mode === 'dead';
      if (text === null) return;
      body.textContent = text.text;
      form.hidden = !text.requiresAnswer;
      closeButton.textContent = text.requiresAnswer ? 'Cancel' : 'Continue';
    },
  };
}
