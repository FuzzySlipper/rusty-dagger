/**
 * The one drawn confirmation every game surface asks through. The desktop window cancels
 * `window.confirm` at once, so a purchase gated on it could never complete there; this is a
 * `<dialog>` shown with `showModal()`, which works in the window and in a browser alike.
 *
 * It opens above whichever modal asked (the menu or the dialogue) and belongs to that owner: when
 * the owner closes, the question goes with it. Cancel, Escape and an owner closing all answer no.
 */
export interface ConfirmQuestion {
  readonly message: string;
  /** The accepting button's words, naming what it does ("Buy", "Forget"). */
  readonly accept: string;
  readonly onAccept: () => void;
}

export interface ConfirmPrompt {
  readonly dialog: HTMLDialogElement;
  ask(question: ConfirmQuestion): void;
  /** Answer no, as Cancel does. */
  cancel(): void;
  /** Answer no when the surface that asked is no longer open. */
  dropIfOwnerClosed(): void;
  readonly open: () => boolean;
}

export function mountConfirm(host: HTMLElement): ConfirmPrompt {
  const dialog = document.createElement('dialog');
  dialog.className = 'dagger-confirm';
  dialog.setAttribute('aria-labelledby', 'dagger-confirm-message');
  const message = document.createElement('p');
  message.id = 'dagger-confirm-message';
  const accept = document.createElement('button');
  accept.type = 'button';
  accept.className = 'dagger-confirm-accept';
  const cancelButton = document.createElement('button');
  cancelButton.type = 'button';
  cancelButton.className = 'dagger-confirm-cancel';
  cancelButton.textContent = 'Cancel';
  const choices = document.createElement('div');
  choices.className = 'dagger-confirm-choices';
  choices.append(accept, cancelButton);
  dialog.append(message, choices);
  host.append(dialog);

  let pending: (() => void) | null = null;
  let owner: HTMLDialogElement | null = null;
  let returnFocus: HTMLElement | null = null;
  const finish = (accepted: boolean): void => {
    const run = accepted ? pending : null;
    pending = null;
    owner = null;
    if (dialog.open) dialog.close();
    if (returnFocus?.isConnected) returnFocus.focus();
    returnFocus = null;
    run?.();
  };
  accept.addEventListener('click', () => finish(true));
  cancelButton.addEventListener('click', () => finish(false));
  dialog.addEventListener('cancel', event => { event.preventDefault(); finish(false); });

  return {
    dialog,
    ask(question) {
      returnFocus = document.activeElement instanceof HTMLElement ? document.activeElement : null;
      owner = returnFocus?.closest('dialog') ?? null;
      pending = question.onAccept;
      message.textContent = question.message;
      accept.textContent = question.accept;
      if (!dialog.open) dialog.showModal();
      // Focus starts on Cancel, so a stray accept press never spends gold.
      cancelButton.focus();
    },
    cancel: () => finish(false),
    dropIfOwnerClosed() { if (dialog.open && owner !== null && !owner.open) finish(false); },
    open: () => dialog.open,
  };
}
