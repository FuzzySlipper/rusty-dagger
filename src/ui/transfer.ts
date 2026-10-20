import { image } from './art.js';
import type { Encumbrance, InventoryItem } from './inventory.js';
import { redraw } from './redraw.js';
import { createStepper, type Stepper } from './stepper.js';

/**
 * The two-pane transfer layout: a container or a shop on one side and the player's pack on the
 * other, each row with its quantity and the commands that act on it. Loot, corpses, property
 * storage and merchants use it. Every row, command, bound and refusal comes from a projection;
 * this only draws them and reports which command was chosen on which row, and how much of it.
 */
export interface TransferCommand {
  readonly id: string;
  readonly label: string;
  readonly disabled?: boolean;
  /** Set apart from the other commands, as a crime is from a purchase. */
  readonly danger?: boolean;
  /** Whether the command moves a chosen amount; a command on a whole row leaves the stepper alone. */
  readonly takesAmount?: boolean;
}

export interface TransferRow {
  readonly key: string;
  readonly label: string;
  readonly icon: string | null;
  /** Shown after the name for a stack; a row of one shows no count. */
  readonly quantity: string | null;
  /** The most of this row one command moves; above one, the row has a quantity stepper. */
  readonly maximumQuantity: number;
  /** Weight, value, wear and the like, as one line of player-facing text. */
  readonly facts: string;
  /** A unit price in gold; the row then shows the total for the chosen amount. */
  readonly unitPrice?: number;
  /** Why the row's commands are closed, when they are. */
  readonly refusal?: string | null;
  readonly commands: readonly TransferCommand[];
}

export interface TransferSide {
  readonly title: string;
  readonly rows: readonly TransferRow[];
  readonly empty: string;
}

export interface TransferView {
  readonly source: TransferSide;
  readonly pack: TransferSide;
  /** The label of a command that moves every source row at once, or null when there is none. */
  readonly takeAll: string | null;
  readonly encumbrance?: Encumbrance | null;
}

export type TransferSideName = 'source' | 'pack';

/** An inventory row on the transfer layout, with one command (or none, for a pane that only shows). */
export function inventoryTransferRow(item: InventoryItem, command: string | null, refusal?: string | null): TransferRow {
  const count = Number(item.quantity);
  return {
    key: item.key, label: item.label, icon: item.icon,
    quantity: item.stackable ? item.quantity : null,
    maximumQuantity: item.stackable === true && Number.isSafeInteger(count) ? count : 1,
    facts: itemFacts(item), refusal: refusal ?? null,
    commands: command === null ? [] : [{ id: 'move', label: command, disabled: refusal != null, takesAmount: true }],
  };
}

export function mountTransfer(root: HTMLElement, handlers: {
  command(side: TransferSideName, key: string, command: string, amount: number): void;
  takeAll?(): void;
}): { update(view: TransferView | null): void; refresh(): void; dispose(): void } {
  const element = document.createElement('div');
  element.className = 'dagger-transfer';
  const header = document.createElement('div');
  header.className = 'dagger-transfer-header';
  const load = document.createElement('span');
  load.className = 'dagger-transfer-load';
  const takeAll = document.createElement('button');
  takeAll.type = 'button';
  takeAll.className = 'dagger-transfer-take-all';
  takeAll.dataset.transferAction = 'take-all';
  header.append(load, takeAll);
  const panes = document.createElement('div');
  panes.className = 'dagger-transfer-panes';
  const source = pane('source');
  const pack = pane('pack');
  panes.append(source.element, pack.element);
  element.append(header, panes);
  root.append(element);

  const steppers = new Map<string, Stepper>();
  let current: TransferView | null = null;

  const draw = (side: ReturnType<typeof pane>, name: TransferSideName, value: TransferSide): void => {
    side.title.textContent = value.title;
    redraw(side.list, JSON.stringify([value, value.rows.map(row => image(row.icon) !== null)]), () => {
      for (const key of [...steppers.keys()]) if (key.startsWith(`${name}:`)) steppers.delete(key);
      side.list.replaceChildren(...(value.rows.length === 0 ? [emptyRow(value.empty)] : value.rows.map(row => {
        const entry = document.createElement('li');
        entry.className = 'dagger-transfer-row';
        entry.dataset.transferItem = row.key;
        const picture = document.createElement('span');
        picture.className = 'dagger-transfer-icon dagger-slot';
        const source = image(row.icon);
        if (source !== null) {
          const icon = document.createElement('img');
          icon.src = source;
          icon.alt = '';
          icon.draggable = false;
          picture.append(icon);
        }
        const text = document.createElement('div');
        const label = document.createElement('strong');
        label.textContent = row.quantity === null ? row.label : `${row.label} × ${row.quantity}`;
        const facts = document.createElement('p');
        facts.textContent = row.facts;
        text.append(label, facts);
        if (row.refusal) {
          const refusal = document.createElement('p');
          refusal.className = 'dagger-transfer-refusal';
          refusal.textContent = row.refusal;
          text.append(refusal);
        }
        entry.append(picture, text);
        const amounts = document.createElement('div');
        amounts.className = 'dagger-transfer-amount';
        let stepper: Stepper | null = null;
        if (row.maximumQuantity > 1 && row.commands.some(command => command.takesAmount)) {
          stepper = createStepper(`Quantity of ${row.label}`);
          stepper.limit(1, row.maximumQuantity, row.unitPrice === undefined ? row.maximumQuantity : 1);
          steppers.set(`${name}:${row.key}`, stepper);
          amounts.append(stepper.element);
        }
        if (row.unitPrice !== undefined) {
          const total = document.createElement('span');
          total.className = 'dagger-transfer-total';
          const show = (): void => { total.textContent = `${formatNumber(row.unitPrice! * (stepper?.value() ?? 1))} gold`; };
          stepper?.input.addEventListener('input', show);
          stepper?.input.addEventListener('change', show);
          show();
          amounts.append(total);
        }
        if (amounts.childElementCount > 0) entry.append(amounts);
        const commands = document.createElement('div');
        commands.className = 'dagger-transfer-commands';
        for (const command of row.commands) {
          const button = document.createElement('button');
          button.type = 'button';
          button.dataset.transferMove = name;
          button.dataset.transferKey = row.key;
          button.dataset.transferCommand = command.id;
          button.textContent = command.label;
          button.disabled = command.disabled === true;
          button.classList.toggle('is-danger', command.danger === true);
          button.setAttribute('aria-label', `${command.label} ${row.label}`);
          commands.append(button);
        }
        if (row.commands.length > 0) entry.append(commands);
        return entry;
      })));
    });
  };

  const render = (view: TransferView | null): void => {
    current = view;
    element.hidden = view === null;
    if (view === null) return;
    draw(source, 'source', view.source);
    draw(pack, 'pack', view.pack);
    takeAll.hidden = view.takeAll === null;
    takeAll.textContent = view.takeAll ?? '';
    takeAll.disabled = !view.source.rows.some(row => row.commands.some(command => !command.disabled));
    const weight = view.encumbrance ?? null;
    load.hidden = weight === null;
    if (weight !== null) {
      load.textContent = weight.currentKilograms === undefined
        ? `Load ${weight.currentClassicUnits} / ${weight.maximumClassicUnits}`
        : `Load ${formatNumber(weight.currentKilograms)} / ${formatNumber(weight.maximumKilograms ?? 0)} kg`;
      load.classList.toggle('is-over', !weight.canMove);
    }
  };

  const onClick = (event: MouseEvent): void => {
    const button = event.target instanceof Element ? event.target.closest<HTMLButtonElement>('button') : null;
    if (button === null || button.disabled) return;
    if (button === takeAll) { handlers.takeAll?.(); return; }
    const side = button.dataset.transferMove as TransferSideName | undefined;
    const key = button.dataset.transferKey;
    const command = button.dataset.transferCommand;
    if (side === undefined || key === undefined || command === undefined) return;
    const row = current?.[side].rows.find(candidate => candidate.key === key);
    const takesAmount = row?.commands.find(candidate => candidate.id === command)?.takesAmount === true;
    handlers.command(side, key, command, takesAmount ? steppers.get(`${side}:${key}`)?.value() ?? 1 : 1);
  };
  element.addEventListener('click', onClick);

  return {
    update: render,
    // Rows keep their icons, so art that arrives later repaints the rows already shown.
    refresh() { if (current !== null) render(current); },
    dispose() { element.removeEventListener('click', onClick); element.remove(); },
  };
}

function pane(name: TransferSideName): { element: HTMLElement; title: HTMLElement; list: HTMLElement } {
  const element = document.createElement('section');
  element.className = 'dagger-transfer-pane dagger-panel';
  element.dataset.transferSide = name;
  const title = document.createElement('h3');
  const list = document.createElement('ul');
  list.className = 'dagger-transfer-list';
  element.append(title, list);
  return { element, title, list };
}

function emptyRow(text: string): HTMLElement {
  const row = document.createElement('li');
  row.className = 'dagger-transfer-empty';
  row.textContent = text;
  return row;
}

/** The facts a row shows under its name: weight, value and wear, all from the projection. */
export function itemFacts(item: InventoryItem): string {
  const parts = [
    item.weightKilograms === undefined ? null : `${formatNumber(item.weightKilograms)} kg`,
    `${formatNumber(item.value)} gold`,
    item.condition !== null && item.condition.maximum > 1
      ? item.condition.broken ? 'broken' : `condition ${item.condition.percentage}%` : null,
    item.identified ? null : 'unidentified',
  ];
  return parts.filter((part): part is string => part !== null).join(' · ');
}

export function formatNumber(value: number): string {
  return Number.isFinite(value) ? new Intl.NumberFormat(undefined, { maximumFractionDigits: 2 }).format(value) : '—';
}
