import { image } from './art.js';
import type { Encumbrance, InventoryItem } from './inventory.js';
import { redraw } from './redraw.js';
import { createStepper, type Stepper } from './stepper.js';

/**
 * The two-pane transfer layout: a container's items on one side and the player's pack on the
 * other, each row with its quantity and the one command that moves it across. Loot, corpses and
 * property storage use it. Every row, command and refusal comes from a projection; this only
 * draws them and reports which row was moved and how much of it.
 */
export interface TransferRow {
  readonly item: InventoryItem;
  /** The command that moves the row across, or null when this side moves nothing. */
  readonly command: string | null;
  /** Why the row cannot be moved now, when it cannot. */
  readonly refusal?: string | null;
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

export function mountTransfer(root: HTMLElement, handlers: {
  move(side: TransferSideName, key: string, amount: number): void;
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
    redraw(side.list, JSON.stringify([value, value.rows.map(row => image(row.item.icon) !== null)]), () => {
      for (const key of [...steppers.keys()]) if (key.startsWith(`${name}:`)) steppers.delete(key);
      side.list.replaceChildren(...(value.rows.length === 0 ? [emptyRow(value.empty)] : value.rows.map(row => {
        const entry = document.createElement('li');
        entry.className = 'dagger-transfer-row';
        entry.dataset.transferItem = row.item.key;
        const picture = document.createElement('span');
        picture.className = 'dagger-transfer-icon dagger-slot';
        const source = image(row.item.icon);
        if (source !== null) {
          const icon = document.createElement('img');
          icon.src = source;
          icon.alt = '';
          icon.draggable = false;
          picture.append(icon);
        }
        const text = document.createElement('div');
        const label = document.createElement('strong');
        label.textContent = row.item.stackable ? `${row.item.label} × ${row.item.quantity}` : row.item.label;
        const facts = document.createElement('p');
        facts.textContent = itemFacts(row.item);
        text.append(label, facts);
        if (row.refusal) {
          const refusal = document.createElement('p');
          refusal.className = 'dagger-transfer-refusal';
          refusal.textContent = row.refusal;
          text.append(refusal);
        }
        entry.append(picture, text);
        const count = Number(row.item.quantity);
        if (row.command !== null && row.item.stackable === true && Number.isSafeInteger(count) && count > 1) {
          const stepper = createStepper(`Quantity of ${row.item.label}`);
          stepper.limit(1, count, count);
          steppers.set(`${name}:${row.item.key}`, stepper);
          entry.append(stepper.element);
        }
        if (row.command !== null) {
          const button = document.createElement('button');
          button.type = 'button';
          button.dataset.transferMove = name;
          button.dataset.transferKey = row.item.key;
          button.textContent = row.command;
          button.disabled = row.refusal != null;
          button.setAttribute('aria-label', `${row.command} ${row.item.label}`);
          entry.append(button);
        }
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
    takeAll.disabled = !view.source.rows.some(row => row.command !== null && row.refusal == null);
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
    if (side === undefined || key === undefined) return;
    handlers.move(side, key, steppers.get(`${side}:${key}`)?.value() ?? Number(rowItem(side, key)?.quantity ?? 1));
  };
  const rowItem = (side: TransferSideName, key: string): InventoryItem | undefined =>
    current?.[side].rows.find(row => row.item.key === key)?.item;
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

function formatNumber(value: number): string {
  return Number.isFinite(value) ? new Intl.NumberFormat(undefined, { maximumFractionDigits: 2 }).format(value) : '—';
}
