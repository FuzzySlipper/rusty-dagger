import type { Send } from './host.js';
import type { DaggerHud } from './projection.js';

/**
 * The command radial: play's quick commands on a ring around the place, the time and what the player holds ready.
 * Its key (or the pad's Y) opens it, as the ruleset publishes a `radial` panel request; it holds the world as the
 * menu does. Pointing at a command selects it, and a click, Enter or A accepts it; the arrows, the stick and the
 * D-pad move round the ring; Escape, B or the key pressed again close it, and releasing the opening key, held, over
 * a command accepts it. The commands are the DOM's own list of what other controls already do: each sends the same
 * semantic action or opens the same menu panel, so the radial decides nothing.
 */
export interface Radial {
  readonly root: HTMLDialogElement;
  isOpen(): boolean;
  open(): void;
  close(): void;
  update(value: DaggerHud): void;
  /** A key the page sees while the radial is open: its own key closes it as it comes up, or accepts on a held release. */
  key(event: KeyboardEvent): boolean;
  dispose(): void;
}

interface Command {
  readonly id: string;
  readonly icon: string;
  label(value: DaggerHud | null): string;
  run(): void;
  current?(value: DaggerHud | null): boolean;
}

/** A key held longer than this and released over a command accepts it; a quicker press leaves the radial open. */
const HOLD_MS = 350;
/**
 * Play can act on the radial's key, and the radial open, before the page receives that same press: a press of the key
 * this soon after the radial opened is the one that opened it, not one that closes it.
 */
const OPENING_PRESS_MS = 200;

/** What the radial asks of the page: its actions, a menu panel, and the input mode re-decided as it opens and closes. */
export interface RadialHost {
  readonly send: Send;
  readonly runMenuAction: (action: string) => void;
  /** The radial opened or closed; `focus` asks for gameplay focus back. */
  readonly sync: (focus?: boolean) => void;
}

export function mountRadial(shell: HTMLElement, host: RadialHost): Radial {
  const { send, runMenuAction, sync } = host;
  const root = shell.querySelector<HTMLDialogElement>('.dagger-radial')!;
  const ring = root.querySelector<HTMLElement>('.dagger-radial-ring')!;
  const centre = root.querySelector<HTMLElement>('.dagger-radial-centre')!;
  const label = root.querySelector<HTMLElement>('.dagger-radial-label')!;
  let latest: DaggerHud | null = null;
  let openedAt = 0;
  let pointed = false;
  let closing = false;

  // Closing back to play takes gameplay focus at once, as the menu does, so the next key reaches play.
  const close = (focus = true): void => {
    if (!root.open) return;
    root.close();
    send({ action: 'menu', open: false });
    sync(focus);
  };
  const thenClose = (run: () => void) => (): void => { close(); run(); };
  const mode = (id: string): Command => ({
    id: `mode-${id}`, icon: id,
    label: value => value?.activation?.modes?.find(entry => entry.id === id)?.label ?? id,
    current: value => value?.activation?.mode === id,
    run: thenClose(() => send({ action: 'activation-mode', mode: id })),
  });
  // A panel command hands over to the menu, which keeps the cursor.
  const panel = (id: string, icon: string, words: string): Command =>
    ({ id, icon, label: () => words, run: () => { close(false); runMenuAction(id); } });
  const commands: readonly Command[] = [
    { id: 'weapon', icon: 'category-weapons-armor',
      label: value => value?.weapon?.drawn ? 'Sheathe weapon' : 'Ready weapon',
      run: thenClose(() => send({ action: 'weapon-toggle' })) },
    mode('grab'), mode('info'), mode('talk'), mode('steal'),
    panel('spells', 'spells', 'Spells'),
    panel('inventory', 'inventory', 'Inventory'),
    panel('character', 'character', 'Character'),
    panel('journal', 'journal', 'Journal'),
    panel('map', 'map', 'Map'),
    panel('transport', 'travel', 'Travel'),
    panel('rest', 'rest', 'Rest'),
    panel('menu', 'settings', 'Game menu'),
  ];

  const buttons = commands.map((command, index) => {
    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'dagger-radial-command';
    button.dataset.command = command.id;
    button.dataset.icon = command.icon;
    button.setAttribute('role', 'menuitem');
    // Clockwise from the top, as a clock face reads.
    const angle = index / commands.length * 2 * Math.PI;
    button.style.setProperty('--x', String(Math.sin(angle)));
    button.style.setProperty('--y', String(-Math.cos(angle)));
    button.addEventListener('click', () => command.run());
    button.addEventListener('focus', () => { label.textContent = button.getAttribute('aria-label'); });
    button.addEventListener('pointerenter', () => { pointed = true; button.focus(); });
    ring.append(button);
    return button;
  });

  // Pointing anywhere along a command's bearing selects it, so the pointer need not land on the command itself.
  root.addEventListener('pointermove', event => {
    const box = ring.getBoundingClientRect();
    const dx = event.clientX - (box.left + box.width / 2), dy = event.clientY - (box.top + box.height / 2);
    if (Math.hypot(dx, dy) < centre.offsetWidth / 2) return;
    const turn = (Math.atan2(dx, -dy) / (2 * Math.PI) + 1) % 1;
    const index = Math.round(turn * commands.length) % commands.length;
    pointed = true;
    if (document.activeElement !== buttons[index]) buttons[index]!.focus();
  });

  const draw = (): void => {
    buttons.forEach((button, index) => {
      const command = commands[index]!;
      const words = command.label(latest);
      button.setAttribute('aria-label', words);
      button.classList.toggle('is-current', command.current?.(latest) === true);
    });
    const focused = buttons.find(button => button === document.activeElement);
    label.textContent = focused?.getAttribute('aria-label') ?? '';
  };

  const radialKeys = (): readonly string[] => latest?.controls?.bindings.find(binding => binding.id === 'radial')?.keys ?? [];

  return {
    root,
    isOpen: () => root.open,
    open() {
      if (root.open) return;
      openedAt = performance.now();
      pointed = false;
      closing = false;
      draw();
      root.showModal();
      sync();
      send({ action: 'menu', open: true });
    },
    close: () => close(),
    update(value) {
      latest = value;
      if (root.open && value.mode !== 'playing' && value.mode !== 'modal') close();
      draw();
    },
    key(event) {
      if (!root.open || !radialKeys().includes(event.code)) return false;
      // The key closes the radial as it comes up, not as it goes down: the page takes the whole press, so play
      // never receives a release without its press, which would cost the key's next press.
      if (event.type === 'keydown') { if (!event.repeat && performance.now() - openedAt > OPENING_PRESS_MS) closing = true; return true; }
      if (event.type !== 'keyup') return false;
      const focused = buttons.indexOf(document.activeElement as HTMLButtonElement);
      if (!closing && pointed && focused >= 0 && performance.now() - openedAt > HOLD_MS) commands[focused]!.run();
      else if (closing) close();
      closing = false;
      return true;
    },
    dispose() { if (root.open) root.close(); },
  };
}
