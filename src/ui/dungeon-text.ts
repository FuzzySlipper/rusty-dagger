import type { Send } from './host.js';
import type { DaggerHud } from './projection.js';

/** Text the dungeon shows the player (an inscription, a riddle), with an answer box when it asks one. */
export interface DungeonText {
  readonly root: HTMLElement;
  /** Continue, or Cancel when the text asks for an answer. */
  close(): void;
  update(value: DaggerHud): void;
}

export function mountDungeonText(shell: HTMLElement, send: Send): DungeonText {
  const root = shell.querySelector<HTMLElement>('.dagger-dungeon-text')!;
  const body = shell.querySelector<HTMLElement>('.dagger-dungeon-text-body')!;
  const form = shell.querySelector<HTMLFormElement>('.dagger-dungeon-text-answer')!;
  const answer = form.querySelector<HTMLInputElement>('input')!;
  const closeButton = shell.querySelector<HTMLButtonElement>('.dagger-dungeon-text-close')!;
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
