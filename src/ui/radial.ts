import type { Send } from './host.js';
import type { DaggerHud, RadialCommand } from './projection.js';
import { redraw } from './redraw.js';

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

function isRadialCommands(value: unknown): value is readonly RadialCommand[] {
  return Array.isArray(value) && value.every(command => typeof command === 'object' && command !== null
    && typeof command.id === 'string' && typeof command.label === 'string' && typeof command.available === 'boolean'
    && typeof command.current === 'boolean' && (command.reason === null || typeof command.reason === 'string'));
}

/** Each command's icon: how the radial draws a command, not what it is. */
const ICONS: Readonly<Record<string, string>> = {
  weapon: 'category-weapons-armor', 'mode-grab': 'grab', 'mode-info': 'info', 'mode-talk': 'talk', 'mode-steal': 'steal',
  spells: 'spells', inventory: 'inventory', character: 'character', journal: 'journal', map: 'map', transport: 'travel', rest: 'rest',
  menu: 'settings',
};

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
  const reason = document.createElement('p');
  reason.className = 'dagger-radial-reason';
  label.after(reason);
  let commands: readonly RadialCommand[] = [];
  let buttons: HTMLButtonElement[] = [];

  // Each command sends what its own control sends; one the rules refuse now does nothing, and the centre says why.
  const run = (command: RadialCommand | undefined): void => {
    if (command === undefined || !command.available) return;
    if (command.id === 'weapon') { close(); send({ action: 'weapon-toggle' }); }
    else if (command.id.startsWith('mode-')) { close(); send({ action: 'activation-mode', mode: command.id.slice('mode-'.length) }); }
    // A panel command hands over to the menu, which keeps the cursor.
    else { close(false); runMenuAction(command.id); }
  };

  const name = (button: Element | null): void => {
    const command = commands[buttons.indexOf(button as HTMLButtonElement)];
    label.textContent = command?.label ?? '';
    reason.textContent = command !== undefined && !command.available ? command.reason ?? '' : '';
  };

  const build = (): void => { redraw(ring, JSON.stringify(commands.map(command => command.id)), () => {
    buttons = commands.map((command, index) => {
      const button = document.createElement('button');
      button.type = 'button';
      button.className = 'dagger-radial-command';
      button.dataset.command = command.id;
      if (ICONS[command.id]) button.dataset.icon = ICONS[command.id];
      button.setAttribute('role', 'menuitem');
      // Clockwise from the top, as a clock face reads.
      const angle = index / commands.length * 2 * Math.PI;
      button.style.setProperty('--x', String(Math.sin(angle)));
      button.style.setProperty('--y', String(-Math.cos(angle)));
      button.addEventListener('click', () => run(commands[index]));
      button.addEventListener('focus', () => name(button));
      button.addEventListener('pointerenter', () => { pointed = true; button.focus(); });
      return button;
    });
    ring.replaceChildren(...buttons);
  }); };

  // The command whose stretch of the ring a point lies on: anywhere along its bearing outside the centre, and for a
  // click, inside the ring too.
  const commandAt = (event: MouseEvent, onRing: boolean): number | null => {
    const box = ring.getBoundingClientRect();
    const dx = event.clientX - (box.left + box.width / 2), dy = event.clientY - (box.top + box.height / 2);
    const distance = Math.hypot(dx, dy);
    if (buttons.length === 0 || distance < centre.offsetWidth / 2 || (onRing && distance > box.width / 2)) return null;
    const turn = (Math.atan2(dx, -dy) / (2 * Math.PI) + 1) % 1;
    return Math.round(turn * buttons.length) % buttons.length;
  };
  // Pointing anywhere along a command's bearing selects it, so the pointer need not land on the command itself.
  root.addEventListener('pointermove', event => {
    const index = commandAt(event, false);
    if (index === null) return;
    pointed = true;
    if (document.activeElement !== buttons[index]) buttons[index]!.focus();
  });
  // A click anywhere on that stretch of the ring accepts it; a click on the command itself is its own button's.
  root.addEventListener('click', event => {
    if (event.target instanceof Element && event.target.closest('.dagger-radial-command')) return;
    const index = commandAt(event, true);
    if (index !== null) run(commands[index]);
  });

  const draw = (): void => {
    commands = isRadialCommands(latest?.radial) ? latest.radial : [];
    build();
    buttons.forEach((button, index) => {
      const command = commands[index]!;
      button.setAttribute('aria-label', command.label);
      button.setAttribute('aria-disabled', String(!command.available));
      button.classList.toggle('is-current', command.current);
      button.classList.toggle('is-unavailable', !command.available);
    });
    name(document.activeElement);
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
      if (!closing && pointed && focused >= 0 && performance.now() - openedAt > HOLD_MS) run(commands[focused]);
      else if (closing) close();
      closing = false;
      return true;
    },
    dispose() { if (root.open) root.close(); },
  };
}
