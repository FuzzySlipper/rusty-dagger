import { heldDemandedRevision, heldRevision, image } from './art.js';
import type { PaperDoll, PaperDollLayer } from './character.js';
import { mountDrag, type Drag } from './drag.js';
import type { NavigationSurface } from './navigation.js';
import { redraw } from './redraw.js';
import { createStepper, type Stepper } from './stepper.js';
import { mountTransfer } from './transfer.js';

export interface InventoryProjection {
  readonly revision: string;
  readonly items: readonly InventoryItem[];
  readonly slots: readonly EquipmentSlot[];
  readonly message: string;
  readonly equipmentChange: EquipmentChange | null;
  readonly encumbrance?: Encumbrance | null;
  readonly currency?: CurrencyTotals | null;
  readonly bank?: BankProjection | null;
  /** The pack's tabs, in the order the ruleset gives them. */
  readonly categories?: readonly InventoryCategory[];
}

export interface InventoryCategory { readonly id: string; readonly label: string }

export interface Encumbrance {
  readonly currentClassicUnits: number;
  readonly maximumClassicUnits: number;
  readonly canMove: boolean;
  readonly currentKilograms?: number;
  readonly maximumKilograms?: number;
}

export interface CurrencyTotals {
  readonly gold: string;
  readonly lettersOfCredit: string;
  readonly accountGold: string;
}

export interface BankAccount {
  readonly region: number;
  readonly regionName: string;
  readonly gold: string;
}

/** One bank operation as the ruleset offers it now, with its amount bounds and why it is closed. */
export interface BankAction {
  readonly action: BankActionName;
  readonly label: string;
  readonly available: boolean;
  readonly takesAmount: boolean;
  readonly minimum: number;
  readonly maximum: number;
  readonly refusal: string | null;
}

export type BankActionName = 'currency-deposit-gold' | 'currency-withdraw-gold' | 'currency-deposit-letters' | 'currency-withdraw-letter'
  | 'bank-transfer' | 'bank-loan-issue' | 'bank-loan-repay-account' | 'bank-loan-repay-carried';

/** An owned property's storage while the player is inside it, as the ruleset publishes it. */
export interface PropertyStorage {
  readonly key: string;
  readonly revision: string;
  readonly items: readonly InventoryItem[];
  readonly storeRefusals: readonly { readonly item: string; readonly reason: string }[];
}

export interface BankProjection {
  readonly actions?: readonly BankAction[];
  readonly currentRegion: number;
  readonly currentRegionName: string;
  readonly currentBalance: string;
  readonly accounts: readonly BankAccount[];
  readonly maximumNewLoan: string;
  readonly loan: { readonly principal: string; readonly remaining: string; readonly dueMinute: number;
    readonly daysRemaining: number; readonly defaulted: boolean } | null;
}

/** A completed ruleset-owned equipment change. Delay fields remain available to a later readiness owner. */
export interface EquipmentChange {
  readonly cue: 'equip' | 'unequip' | 'transfer';
  readonly rightHandDelayMilliseconds: number;
  readonly leftHandDelayMilliseconds: number;
}

export interface InventoryItem {
  readonly key: string;
  readonly definition: string;
  readonly label: string;
  readonly quantity: string;
  readonly weight: number;
  readonly weightKilograms?: number;
  readonly value: number;
  readonly details: string;
  readonly icon: string | null;
  readonly condition: ItemCondition | null;
  readonly identified: boolean;
  readonly gridSlot: number | null;
  readonly equippedSlots: readonly string[];
  readonly compatibleSlots: readonly string[];
  /** Whether the ruleset has a use for this carried item. */
  readonly canUse?: boolean;
  readonly canUseMagic?: boolean;
  /** Whether some equipment slot would take this carried item now. */
  readonly canEquip?: boolean;
  /** The ruleset's reason this item cannot be equipped, when it cannot. */
  readonly equipRefusal?: string | null;
  /** The pack tab the item belongs to. */
  readonly category?: string;
  /** Whether the row is a stack, so part of it can be dropped or taken. */
  readonly stackable?: boolean;
  /** The ruleset's reason this item cannot be dropped, when it cannot. */
  readonly dropRefusal?: string | null;
  /** Its fighting figures (damage, armour), each with a score where higher is better. */
  readonly stats?: readonly ItemStat[];
  /** The worn item it would be compared with, when one is worn where it goes. */
  readonly comparison?: { readonly item: string; readonly label: string; readonly slot: string; readonly stats: readonly ItemStat[] } | null;
}

export interface ItemStat { readonly label: string; readonly text: string; readonly score: number }

export interface ItemCondition {
  readonly current: number;
  readonly maximum: number;
  readonly percentage: number;
  readonly broken: boolean;
}

export interface EquipmentSlot {
  readonly id: string;
  readonly label: string;
  readonly itemKey: string | null;
  /** Where the screen lays the slot out: armour, clothing, weapons or accessories. */
  readonly group?: string;
}

export type InventoryAction = {
  readonly action: 'inventory-move';
  readonly revision: string;
  readonly item: string;
  readonly targetGrid?: number;
  readonly targetEquipment?: string;
} | {
  readonly action: 'inventory-drop';
  readonly revision: string;
  readonly item: string;
  readonly amount: number;
} | {
  readonly action: 'inventory-inspect';
  readonly revision: string;
  readonly item: string;
} | {
  readonly action: 'inventory-use';
  readonly revision: string;
  readonly item: string;
} | {
  readonly action: 'inventory-sort';
} | {
  readonly action: 'property-put' | 'property-take';
  readonly key: string;
  readonly item: string;
  readonly revision: string;
  readonly amount: number;
} | {
  readonly action: 'currency-deposit-gold' | 'currency-withdraw-gold' | 'currency-withdraw-letter';
  readonly amount: number;
} | {
  readonly action: 'currency-deposit-letters';
} | {
  readonly action: 'bank-transfer';
  readonly amount: number;
  readonly destination: number;
} | {
  readonly action: 'bank-loan-issue' | 'bank-loan-repay-account' | 'bank-loan-repay-carried';
  readonly amount: number;
};

type MoveSource = { readonly key: string; readonly revision: string };

const GRID_SLOT_COUNT = 50;
/** Slot groups the doll's two columns hold; every other group lays out in the accessories row. */
const LEFT_GROUPS = new Set(['armour']);
const RIGHT_GROUPS = new Set(['clothing', 'weapons']);
const ALL_TAB = 'all';
const BANK_TAB = 'bank';
const STORAGE_TAB = 'storage';

/**
 * The inventory: equipment around a paper doll, and the pack as a grid of slots with category tabs.
 *
 * Hovering or focusing an item shows its tooltip. Double-clicking it, pressing Enter or the
 * controller's A on it, its menu key or the controller's X opens the item's action bar above the
 * pack (equip, use, move, drop, cancel); Cancel, Escape and Back close it. Items also drag with
 * the pointer into pack and equipment slots through the shared drag owner. Every action is the
 * ruleset's: this keeps no gameplay state and claims each action with the revision it was shown at.
 */
export function mountInventory(
  root: HTMLElement,
  claim: (action: InventoryAction) => void,
  register?: (surface: NavigationSurface) => () => void,
): {
  update(value: InventoryProjection): void;
  updatePaperDoll(doll: PaperDoll | null | undefined): void;
  updateStorage(value: PropertyStorage | null): void;
  refresh(): void;
  release(): boolean;
  openActions(): boolean;
  closeActions(): void;
  dispose(): void;
} {
  const disposers: (() => void)[] = [];
  const shell = document.createElement('section');
  shell.className = 'dagger-inventory';
  shell.setAttribute('aria-label', 'Inventory and equipment');

  const heading = document.createElement('h2');
  heading.className = 'dagger-inventory-title dagger-frame-title';
  heading.textContent = 'Inventory & equipment';
  const status = document.createElement('p');
  status.className = 'dagger-inventory-status';
  status.setAttribute('role', 'status');
  status.setAttribute('aria-live', 'polite');
  status.textContent = 'Awaiting inventory projection…';

  const body = document.createElement('div');
  body.className = 'dagger-inventory-body';

  // Equipment: armour down one side of the doll, clothing and the hands down the other, the paired
  // accessories in a row beneath.
  const equipmentSection = document.createElement('section');
  equipmentSection.className = 'dagger-inventory-equipment-section dagger-panel';
  equipmentSection.setAttribute('aria-label', 'Equipment');
  const paperDoll = document.createElement('div');
  paperDoll.className = 'dagger-inventory-paperdoll';
  const leftColumn = slotColumn('Armour');
  const rightColumn = slotColumn('Clothing and hands');
  const doll = document.createElement('div');
  doll.className = 'dagger-inventory-doll';
  doll.setAttribute('aria-hidden', 'true');
  const dollBackground = document.createElement('div');
  dollBackground.className = 'dagger-inventory-doll-background';
  // The figure and the worn items, as the ruleset places them.
  const dollLayers = document.createElement('div');
  dollLayers.className = 'dagger-inventory-doll-layers';
  doll.append(dollBackground, dollLayers);
  paperDoll.append(leftColumn, doll, rightColumn);
  const accessories = document.createElement('div');
  accessories.className = 'dagger-inventory-accessories';
  accessories.setAttribute('aria-label', 'Accessories');
  equipmentSection.append(paperDoll, accessories);

  // The pack: tabs and purse, the action bar's reserved row, then the grid.
  const packSection = document.createElement('section');
  packSection.className = 'dagger-inventory-pack-section';
  const packHeader = document.createElement('div');
  packHeader.className = 'dagger-inventory-pack-header';
  const tabs = document.createElement('div');
  tabs.className = 'dagger-inventory-tabs';
  tabs.setAttribute('role', 'tablist');
  tabs.setAttribute('aria-label', 'Pack categories');
  const purse = document.createElement('div');
  purse.className = 'dagger-inventory-purse';
  const gold = document.createElement('span');
  gold.className = 'dagger-inventory-gold';
  const load = document.createElement('div');
  load.className = 'dagger-inventory-load';
  const loadText = document.createElement('span');
  const loadMeter = document.createElement('span');
  loadMeter.className = 'dagger-inventory-load-meter';
  const loadFill = document.createElement('span');
  loadMeter.append(loadFill);
  load.append(loadText, loadMeter);
  purse.append(gold, load);
  packHeader.append(tabs, purse);

  const actions = createActionBar();
  const packPane = document.createElement('div');
  packPane.className = 'dagger-inventory-pack';
  const grid = document.createElement('div');
  grid.className = 'dagger-inventory-grid';
  grid.setAttribute('aria-label', 'Pack slots');
  const overflow = document.createElement('div');
  overflow.className = 'dagger-inventory-overflow';
  overflow.setAttribute('aria-label', 'Pack overflow');
  const packFooter = document.createElement('div');
  packFooter.className = 'dagger-inventory-pack-footer';
  const sort = document.createElement('button');
  sort.type = 'button';
  sort.className = 'dagger-inventory-sort';
  sort.dataset.inventoryAction = 'sort';
  sort.textContent = 'Sort pack';
  const hints = document.createElement('p');
  hints.className = 'dagger-inventory-hints';
  hints.append(hint('keyboard', 'Double-click or Enter: item actions · Drag onto a slot to equip'),
    hint('controller', 'A: item actions · B: back · LB / RB: panels'));
  packFooter.append(hints, sort);
  packPane.append(grid, overflow, packFooter);
  const bankControls = createBankControls(claim);
  bankControls.element.hidden = true;
  const storagePane = document.createElement('div');
  storagePane.className = 'dagger-inventory-storage';
  storagePane.hidden = true;
  packSection.append(packHeader, actions.element, packPane, bankControls.element, storagePane);
  let storage: PropertyStorage | null = null;
  const storageTransfer = mountTransfer(storagePane, {
    move(side, key, amount) {
      if (storage === null) return;
      claim({ action: side === 'source' ? 'property-take' : 'property-put', key: storage.key, item: key, revision: storage.revision, amount });
    },
  });
  const renderStorage = (): void => {
    if (storage === null || current === null) { storageTransfer.update(null); return; }
    const refusals = new Map(storage.storeRefusals.map(refusal => [refusal.item, refusal.reason]));
    storageTransfer.update({
      source: { title: 'Stored here', empty: 'Nothing is stored here.', rows: storage.items.map(item => ({ item, command: 'Take' })) },
      pack: { title: 'Your pack', empty: 'Your pack is empty.',
        rows: current.items.map(item => ({ item, command: 'Store', refusal: refusals.get(item.key) ?? null })) },
      takeAll: null,
      encumbrance: current.encumbrance ?? null,
    });
  };

  const tip = document.createElement('div');
  tip.className = 'dagger-inventory-tip';
  tip.setAttribute('role', 'tooltip');
  tip.hidden = true;

  body.append(equipmentSection, packSection);
  shell.append(heading, body, status, tip);
  root.append(shell);

  const gridButtons = Array.from({ length: GRID_SLOT_COUNT }, (_, index) => {
    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'dagger-inventory-grid-slot dagger-slot';
    button.dataset.inventoryGrid = String(index);
    button.setAttribute('aria-label', `Pack slot ${index + 1}, empty`);
    grid.append(button);
    return button;
  });
  const equipmentButtons = new Map<string, HTMLButtonElement>();
  let slotLayout = '';

  let current: InventoryProjection | null = null;
  let lastRevision: string | null = null;
  let lastMessage: string | null = null;
  let pending: InventoryProjection | null = null;
  let tab = ALL_TAB;
  let disposed = false;

  const sourceFor = (key: string): MoveSource | null => current === null ? null : { key, revision: current.revision };
  const itemFor = (key: string | undefined): InventoryItem | undefined => key === undefined ? undefined : current?.items.find(item => item.key === key);
  const slotLabel = (id: string): string => current?.slots.find(slot => slot.id === id)?.label ?? id;
  const move = (source: MoveSource, target: { readonly grid?: number; readonly equipment?: string }): void => {
    if ((target.grid === undefined) === (target.equipment === undefined)) return;
    claim(target.grid === undefined
      ? { action: 'inventory-move', revision: source.revision, item: source.key, targetEquipment: target.equipment }
      : { action: 'inventory-move', revision: source.revision, item: source.key, targetGrid: target.grid });
  };

  /** Lays the equipment slots out by group; only a change in the published slots rebuilds them. */
  const layoutSlots = (slots: readonly EquipmentSlot[]): void => {
    const signature = JSON.stringify(slots.map(slot => [slot.id, slot.group ?? '']));
    if (signature === slotLayout) return;
    slotLayout = signature;
    for (const button of equipmentButtons.values()) button.remove();
    equipmentButtons.clear();
    leftColumn.replaceChildren();
    rightColumn.replaceChildren();
    accessories.replaceChildren();
    let previousRightGroup: string | null = null;
    for (const slot of slots) {
      const button = document.createElement('button');
      button.type = 'button';
      button.className = 'dagger-inventory-equipment-slot dagger-slot';
      button.dataset.inventoryEquipment = slot.id;
      button.dataset.slotGroup = slot.group ?? 'accessories';
      equipmentButtons.set(slot.id, button);
      const group = slot.group ?? 'accessories';
      if (LEFT_GROUPS.has(group)) leftColumn.append(button);
      else if (RIGHT_GROUPS.has(group)) {
        // The hands sit apart from the clothing above them.
        if (previousRightGroup !== null && previousRightGroup !== group) button.classList.add('starts-group');
        previousRightGroup = group;
        rightColumn.append(button);
      } else accessories.append(button);
    }
  };

  const render = (value: InventoryProjection): void => {
    current = value;
    lastRevision = value.revision;
    lastMessage = value.message;
    status.textContent = equipmentStatus(value);
    renderPurse(value);
    renderTabs(value);
    bankControls.render(value.bank ?? null);
    renderStorage();
    const byKey = new Map(value.items.map(item => [item.key, item]));
    const byGrid = new Map(value.items.flatMap(item => item.gridSlot === null || item.gridSlot < 0 || item.gridSlot >= GRID_SLOT_COUNT
      ? [] : [[item.gridSlot, item] as const]));

    gridButtons.forEach((button, index) => renderPlace(button, byGrid.get(index), `Pack slot ${index + 1}`));
    layoutSlots(value.slots);
    for (const slot of value.slots) {
      const button = equipmentButtons.get(slot.id)!;
      renderPlace(button, slot.itemKey === null ? undefined : byKey.get(slot.itemKey), slot.label);
      // An empty slot names itself in the drawn tooltip; a filled one shows its item's.
      if (slot.itemKey === null) button.dataset.tooltip = `${slot.label}: empty`;
      else delete button.dataset.tooltip;
    }
    const overflowItems = value.items.filter(item => item.gridSlot === null && item.equippedSlots.length === 0);
    redraw(overflow, JSON.stringify([overflowItems, overflowItems.map(item => image(item.icon) !== null)]), () => {
      overflow.replaceChildren(...overflowItems.map((item, index) => {
        const button = document.createElement('button');
        button.type = 'button';
        button.className = 'dagger-inventory-grid-slot dagger-slot is-overflow';
        renderPlace(button, item, `Pack overflow ${index + 1}`);
        return button;
      }));
    });
    overflow.hidden = overflowItems.length === 0;
    applyFilter();
    const held = drag.held();
    if (held !== null && !byKey.has(held.key)) drag.release();
    actions.refresh();
    drag.remark();
    if (shownTipFor !== null && !shownTipFor.isConnected) hideTip();
  };

  const renderPurse = (value: InventoryProjection): void => {
    const currency = value.currency ?? null;
    gold.hidden = currency === null;
    gold.textContent = currency === null ? '' : `${currency.gold} gold`;
    if (currency !== null) gold.dataset.tooltip = `Gold ${currency.gold} · Letters of credit ${currency.lettersOfCredit} · In accounts ${currency.accountGold}`;
    const weight = value.encumbrance ?? null;
    load.hidden = weight === null;
    if (weight === null) return;
    const now = weight.currentKilograms ?? weight.currentClassicUnits;
    const most = weight.maximumKilograms ?? weight.maximumClassicUnits;
    const unit = weight.currentKilograms === undefined ? '' : ' kg';
    loadText.textContent = `Load ${formatNumber(now)} / ${formatNumber(most)}${unit}`;
    load.classList.toggle('is-over', !weight.canMove);
    load.dataset.tooltip = weight.canMove ? 'What you carry against what you can carry.' : 'Over capacity: you cannot move until you carry less.';
    loadFill.style.width = `${Math.min(100, most > 0 ? (now / most) * 100 : 0)}%`;
  };

  const renderTabs = (value: InventoryProjection): void => {
    const entries: readonly InventoryCategory[] = [{ id: ALL_TAB, label: 'All' }, ...(value.categories ?? []),
      ...(storage !== null ? [{ id: STORAGE_TAB, label: 'Storage' }] : []),
      ...(value.bank ? [{ id: BANK_TAB, label: 'Bank' }] : [])];
    if (!entries.some(entry => entry.id === tab)) tab = ALL_TAB;
    redraw(tabs, JSON.stringify([entries, tab]), () => {
      tabs.replaceChildren(...entries.map(entry => {
        const button = document.createElement('button');
        button.type = 'button';
        button.setAttribute('role', 'tab');
        button.dataset.inventoryTab = entry.id;
        button.dataset.icon = `category-${entry.id}`;
        button.setAttribute('aria-selected', String(entry.id === tab));
        button.textContent = entry.label;
        return button;
      }));
    });
  };

  const selectTab = (id: string): void => {
    if (id === tab) return;
    tab = id;
    actions.close();
    if (current !== null) renderTabs(current);
    applyFilter();
  };

  /** A category tab dims the items outside it; every item keeps its place in the pack. */
  const applyFilter = (): void => {
    const banking = tab === BANK_TAB;
    packPane.hidden = banking || tab === STORAGE_TAB;
    actions.element.hidden = packPane.hidden;
    bankControls.element.hidden = !banking;
    storagePane.hidden = tab !== STORAGE_TAB;
    for (const button of [...gridButtons, ...overflow.querySelectorAll<HTMLButtonElement>('button')]) {
      const item = itemFor(button.dataset.inventoryItem);
      button.classList.toggle('is-filtered-out', item !== undefined && tab !== ALL_TAB && item.category !== tab);
    }
  };

  // Opening the action bar on an accepted item: a click no pointer made is Enter or the controller's
  // A. These listen before the drag owner's, which would otherwise take the accept as a pick-up.
  let pointerPressed = false;
  const onPointerDown = (): void => { pointerPressed = true; };
  function onClickCapture(event: MouseEvent): void {
    const pointer = pointerPressed;
    pointerPressed = false;
    const element = event.target instanceof Element ? event.target.closest<HTMLElement>('[data-inventory-item]') : null;
    if (pointer || element === null || drag.held() !== null || element.dataset.inventoryItem === undefined) return;
    event.stopImmediatePropagation();
    actions.open(element.dataset.inventoryItem);
  }
  shell.addEventListener('pointerdown', onPointerDown, true);
  shell.addEventListener('click', onClickCapture, true);

  // Items move by pointer drag or by pick and place, through the shared drag owner. Eligibility is
  // the projection's: any pack slot, or an equipment slot the item lists as compatible while the
  // ruleset lets it be equipped.
  const accepts = (source: MoveSource, target: HTMLElement): boolean => {
    if (target.dataset.inventoryGrid !== undefined) return true;
    const slot = target.dataset.inventoryEquipment;
    const item = itemFor(source.key);
    return slot !== undefined && slot !== '' && item !== undefined && item.compatibleSlots.includes(slot)
      && (item.canEquip !== false || item.equippedSlots.length > 0);
  };
  const drag: Drag<MoveSource> = mountDrag<MoveSource>({
    root: shell,
    sourceSelector: '[data-inventory-item]',
    // A place that holds an item is that item when accepted, so only an empty slot receives a placed item by
    // accept; a drag can still drop onto a filled one.
    targetSelector: '[data-inventory-grid], [data-inventory-equipment]',
    source: element => element.dataset.inventoryItem === undefined ? null : sourceFor(element.dataset.inventoryItem),
    accepts,
    drop: (source, target) => {
      if (target.dataset.inventoryGrid !== undefined) move(source, { grid: Number(target.dataset.inventoryGrid) });
      else if (target.dataset.inventoryEquipment) move(source, { equipment: target.dataset.inventoryEquipment });
    },
    onHold: source => {
      hideTip();
      markSelected(source.key);
      status.textContent = `Holding ${itemFor(source.key)?.label ?? 'an item'}: choose a slot to place it, or go back to release it.`;
    },
    onRefused: (source, target) => {
      const item = itemFor(source.key);
      status.textContent = item?.equipRefusal ?? `${slotLabel(target.dataset.inventoryEquipment ?? '') || 'That slot'} does not take ${item?.label ?? 'that item'}.`;
    },
    onEnd: () => {
      markSelected(null);
      if (pending === null) return;
      const next = pending;
      pending = null;
      render(next);
    },
    label: element => itemFor(element.dataset.inventoryItem)?.label ?? '',
  });

  const markSelected = (key: string | null): void => {
    for (const button of shell.querySelectorAll<HTMLButtonElement>('[data-inventory-item]'))
      button.classList.toggle('is-selected', key !== null && button.dataset.inventoryItem === key);
  };

  /** The slot an Equip command fills: an empty compatible one first, else the first compatible. */
  const equipSlot = (item: InventoryItem): string | null => {
    if (item.canEquip !== true || current === null) return null;
    const compatible = current.slots.filter(slot => item.compatibleSlots.includes(slot.id));
    return (compatible.find(slot => slot.itemKey === null) ?? compatible[0])?.id ?? null;
  };
  const freeGrid = (): number | null => {
    const used = new Set(current?.items.map(item => item.gridSlot) ?? []);
    for (let index = 0; index < GRID_SLOT_COUNT; index += 1) if (!used.has(index)) return index;
    return null;
  };

  /** The action bar above the pack, for the one item it was opened on. */
  function createActionBar(): { element: HTMLElement; open(key: string): boolean; close(): void; refresh(): void; isOpen(): boolean } {
    const element = document.createElement('div');
    element.className = 'dagger-inventory-actions';
    element.setAttribute('role', 'toolbar');
    element.setAttribute('aria-label', 'Item actions');
    const idle = document.createElement('p');
    idle.className = 'dagger-inventory-actions-idle';
    idle.textContent = 'Choose an item for its actions.';
    const bar = document.createElement('div');
    bar.className = 'dagger-inventory-actions-bar';
    bar.hidden = true;
    const picture = document.createElement('img');
    picture.alt = '';
    picture.draggable = false;
    const name = document.createElement('strong');
    const command = (action: string, label: string): HTMLButtonElement => {
      const button = document.createElement('button');
      button.type = 'button';
      button.dataset.inventoryAction = action;
      button.textContent = label;
      return button;
    };
    const equip = command('equip', 'Equip');
    const unequip = command('unequip', 'Unequip');
    const use = command('use', 'Use');
    const moveItem = command('move', 'Move');
    const quantity = createStepper('Drop quantity');
    const drop = command('drop', 'Drop');
    const cancel = command('cancel', 'Cancel');
    const note = document.createElement('p');
    note.className = 'dagger-inventory-actions-note';
    bar.append(picture, name, equip, unequip, use, moveItem, quantity.element, drop, cancel, note);
    element.append(idle, bar);

    let key: string | null = null;
    let returnTo: HTMLElement | null = null;
    const draw = (): boolean => {
      const item = itemFor(key ?? undefined);
      if (item === undefined) return false;
      const source = image(item.icon);
      picture.hidden = source === null;
      if (source !== null && picture.src !== source) picture.src = source;
      name.textContent = item.stackable ? `${item.label} × ${item.quantity}` : item.label;
      const equipped = item.equippedSlots.length > 0;
      equip.hidden = equipped;
      unequip.hidden = !equipped;
      equip.disabled = equipSlot(item) === null;
      unequip.disabled = freeGrid() === null;
      use.disabled = item.canUse !== true;
      const count = Number(item.quantity);
      const partial = item.stackable === true && Number.isSafeInteger(count) && count > 1;
      quantity.element.hidden = !partial;
      if (partial) quantity.limit(1, count, count);
      drop.disabled = item.dropRefusal != null;
      const reasons = [equipped || item.canEquip === true || item.compatibleSlots.length === 0 ? null : item.equipRefusal ?? null, item.dropRefusal ?? null]
        .filter((reason): reason is string => reason !== null);
      note.textContent = reasons.join(' ');
      note.hidden = reasons.length === 0;
      return true;
    };
    const close = (): void => {
      if (key === null) return;
      key = null;
      bar.hidden = true;
      idle.hidden = false;
      markSelected(null);
      const back = returnTo;
      returnTo = null;
      // Focus that was in the bar goes back to the item it acted on (or its slot, once the item moved).
      const active = document.activeElement;
      if (back?.isConnected && (active === null || active === document.body || element.contains(active))) back.focus({ preventScroll: true });
    };
    const run = (action: string): void => {
      const item = itemFor(key ?? undefined);
      const source = item === undefined ? null : sourceFor(item.key);
      if (action === 'cancel' || item === undefined || source === null) { close(); return; }
      if (action === 'move') {
        // Pick-and-place from here on: focus stays on the item, and a slot accepted next takes it.
        close();
        drag.hold(source);
        return;
      }
      if (action === 'equip') { const slot = equipSlot(item); if (slot !== null) move(source, { equipment: slot }); }
      else if (action === 'unequip') { const cell = freeGrid(); if (cell !== null) move(source, { grid: cell }); }
      else if (action === 'use') claim({ action: 'inventory-use', revision: source.revision, item: source.key });
      else if (action === 'drop') claim({ action: 'inventory-drop', revision: source.revision, item: source.key,
        amount: quantity.element.hidden ? 1 : quantity.value() });
      close();
    };
    bar.addEventListener('click', event => {
      const button = event.target instanceof Element ? event.target.closest<HTMLButtonElement>('[data-inventory-action]') : null;
      if (button !== null && !button.disabled) run(button.dataset.inventoryAction!);
    });
    const onPointerDown = (event: PointerEvent): void => {
      if (key !== null && event.target instanceof Node && !element.contains(event.target)
        && !(event.target instanceof Element && event.target.closest<HTMLElement>('[data-inventory-item]')?.dataset.inventoryItem === key)) close();
    };
    document.addEventListener('pointerdown', onPointerDown, true);
    const isOpen = (): boolean => key !== null && !shell.closest('[hidden]') && (shell.closest('dialog')?.open ?? true);
    const unregister = register?.({ name: 'inventory-actions', root: bar, layer: 95, isOpen, back: close });
    disposers.push(() => { unregister?.(); document.removeEventListener('pointerdown', onPointerDown, true); });

    return {
      element,
      open(next) {
        if (drag.release() && current !== null) status.textContent = equipmentStatus(current);
        hideTip();
        key = next;
        if (!draw()) { key = null; return false; }
        returnTo = Array.from(shell.querySelectorAll<HTMLElement>('[data-inventory-item]')).find(element => element.dataset.inventoryItem === next) ?? null;
        markSelected(next);
        idle.hidden = true;
        bar.hidden = false;
        bar.querySelector<HTMLButtonElement>('button:not(:disabled):not([hidden])')?.focus();
        return true;
      },
      close,
      refresh() { if (key !== null && !draw()) close(); },
      isOpen: () => key !== null,
    };
  }

  // Tooltips: an item shows its name, description, condition, weight and value on hover and on focus.
  let shownTipFor: HTMLElement | null = null;
  const showTip = (element: HTMLElement): void => {
    const item = itemFor(element.dataset.inventoryItem);
    if (item === undefined || drag.held() !== null || actions.isOpen()) return;
    shownTipFor = element;
    tip.replaceChildren(...itemTooltip(item, slotLabel));
    tip.hidden = false;
    const rect = element.getBoundingClientRect();
    const width = tip.offsetWidth;
    const height = tip.offsetHeight;
    const right = rect.right + 8;
    const left = right + width > window.innerWidth - 4 ? Math.max(4, rect.left - 8 - width) : right;
    tip.style.left = `${left}px`;
    tip.style.top = `${Math.max(4, Math.min(rect.top, window.innerHeight - height - 4))}px`;
  };
  const hideTip = (): void => { shownTipFor = null; tip.hidden = true; };
  const tipOwner = (event: Event): HTMLElement | null =>
    event.target instanceof Element ? event.target.closest<HTMLElement>('[data-inventory-item]') : null;
  const onTipOver = (event: Event): void => { const owner = tipOwner(event); if (owner !== null && owner !== shownTipFor) showTip(owner); };
  const onTipOut = (event: Event): void => {
    const owner = tipOwner(event);
    const next = (event as PointerEvent | FocusEvent).relatedTarget as Node | null;
    if (owner !== null && owner === shownTipFor && !owner.contains(next)) hideTip();
  };

  // The other ways to the action bar: a double-click, a right-click, the menu key or Shift+F10, and
  // the controller's X through `openActions`.
  const openOn = (event: Event): void => {
    const element = tipOwner(event);
    if (element?.dataset.inventoryItem === undefined) return;
    event.preventDefault();
    actions.open(element.dataset.inventoryItem);
  };
  const onMenuKey = (event: KeyboardEvent): void => {
    if (event.code === 'ContextMenu' || (event.shiftKey && event.code === 'F10')) openOn(event);
  };
  const onClick = (event: MouseEvent): void => {
    const button = event.target instanceof Element ? event.target.closest<HTMLButtonElement>('button') : null;
    if (button === null || button.disabled) return;
    if (button.dataset.inventoryTab !== undefined) { selectTab(button.dataset.inventoryTab); return; }
    if (button === sort) { claim({ action: 'inventory-sort' }); return; }
  };

  shell.addEventListener('click', onClick);
  shell.addEventListener('dblclick', openOn);
  shell.addEventListener('contextmenu', openOn);
  shell.addEventListener('keydown', onMenuKey);
  shell.addEventListener('pointerover', onTipOver);
  shell.addEventListener('pointerout', onTipOut);
  shell.addEventListener('focusin', onTipOver);
  shell.addEventListener('focusout', onTipOut);

  /** A length in the doll's source pixels; the doll draws each one as a whole number of device pixels at whole UI scales. */
  const dollPixels = (value: number): string => `calc(${value} * var(--doll-pixel))`;
  const placeLayer = (layer: PaperDollLayer): HTMLImageElement => {
    const art = document.createElement('img');
    art.alt = '';
    art.draggable = false;
    art.dataset.dollLayer = layer.kind;
    art.dataset.mediaId = layer.mediaId;
    if (layer.slot !== null) art.dataset.slot = layer.slot;
    const source = image(layer.mediaId);
    if (source === null) art.dataset.artMissing = 'true';
    else art.src = source;
    art.style.left = dollPixels(layer.x);
    art.style.top = dollPixels(layer.y);
    art.style.width = dollPixels(layer.width);
    art.style.height = dollPixels(layer.height);
    if (layer.clipTop !== null && layer.clipBottom !== null) {
      art.style.clipPath = `inset(${dollPixels(layer.clipTop)} 0 ${dollPixels(layer.height - layer.clipBottom)} 0)`;
      art.dataset.clip = `${layer.clipTop}-${layer.clipBottom}`;
    }
    return art;
  };
  const drawDoll = (value: PaperDoll | null): void => {
    dollBackground.replaceChildren();
    dollLayers.replaceChildren();
    doll.classList.toggle('has-background', value !== null);
    if (value === null) return;
    doll.style.width = dollPixels(value.width);
    doll.style.height = dollPixels(value.height);
    dollBackground.append(placeLayer(value.background));
    let drawn: HTMLElement = dollLayers;
    for (const layer of value.layers) {
      const mask = layer.mask === null ? null : image(layer.mask);
      if (layer.mask !== null) {
        // What is drawn so far, less the mask's opaque pixels: the mask placed where the layer is, excluded
        // from a full cover.
        const masked = document.createElement('div');
        masked.className = 'dagger-inventory-doll-masked';
        masked.dataset.mask = layer.mask;
        masked.append(...drawn.childNodes);
        if (mask === null) masked.dataset.artMissing = 'true';
        else {
          const style = masked.style;
          const images = `url("${mask}"), linear-gradient(#000, #000)`;
          const position = `${dollPixels(layer.x)} ${dollPixels(layer.y)}, 0 0`;
          const size = `${dollPixels(layer.width)} ${dollPixels(layer.height)}, 100% 100%`;
          style.setProperty('mask-image', images);
          style.setProperty('mask-position', position);
          style.setProperty('mask-size', size);
          style.setProperty('mask-repeat', 'no-repeat');
          style.setProperty('mask-composite', 'exclude');
          style.setProperty('-webkit-mask-image', images);
          style.setProperty('-webkit-mask-position', position);
          style.setProperty('-webkit-mask-size', size);
          style.setProperty('-webkit-mask-repeat', 'no-repeat');
          style.setProperty('-webkit-mask-composite', 'xor');
        }
        drawn.append(masked);
      }
      drawn.append(placeLayer(layer));
    }
    doll.title = value.unplaced.length === 0 ? '' : `Not drawn: ${value.unplaced.join(', ')}`;
  };

  return {
    update(value: InventoryProjection): void {
      if (disposed) return;
      if (value.revision === lastRevision) {
        if (value.message !== lastMessage) {
          lastMessage = value.message;
          status.textContent = value.message;
        }
        return;
      }
      if (drag.dragging()) {
        pending = value;
        if (value.message !== lastMessage) {
          lastMessage = value.message;
          status.textContent = value.message;
        }
        return;
      }
      render(value);
    },
    /**
     * The paper doll the character projection composes: the background, then each layer in draw order, every
     * one placed in source pixels. A layer with a mask clears what was drawn beneath it, which is drawn as the
     * layers so far wrapped in a CSS mask that excludes the mask's opaque pixels.
     */
    updatePaperDoll(value): void {
      if (disposed) return;
      // Art for a layer can arrive after the doll that names it, so the held art is part of what is drawn.
      redraw(doll, `${JSON.stringify(value ?? null)}|${heldRevision()}|${heldDemandedRevision()}`, () => drawDoll(value ?? null));
    },
    // Published art can arrive after the state that names it, and the revision check above would
    // otherwise leave the panel painted without it.
    refresh(): void {
      if (disposed || current === null) return;
      render(current);
      storageTransfer.refresh();
    },
    /** The owned property's storage while the player is inside it; it becomes the Storage tab. */
    updateStorage(value: PropertyStorage | null): void {
      if (disposed) return;
      storage = value;
      if (current === null) return;
      renderTabs(current);
      applyFilter();
      renderStorage();
    },
    /** Close the action bar, or release a held or dragged item; returns whether there was one, so Back does that first. */
    release(): boolean {
      if (actions.isOpen()) { actions.close(); return true; }
      return drag.release();
    },
    closeActions: () => actions.close(),
    /** The controller's X: the action bar for the focused item. */
    openActions(): boolean {
      const focused = shell.ownerDocument.activeElement;
      const key = focused instanceof HTMLElement && shell.contains(focused) ? focused.dataset.inventoryItem : undefined;
      return key !== undefined && actions.open(key);
    },
    dispose(): void {
      if (disposed) return;
      disposed = true;
      drag.dispose();
      storageTransfer.dispose();
      for (const dispose of disposers) dispose();
      shell.removeEventListener('pointerdown', onPointerDown, true);
      shell.removeEventListener('click', onClickCapture, true);
      shell.removeEventListener('click', onClick);
      shell.removeEventListener('dblclick', openOn);
      shell.removeEventListener('contextmenu', openOn);
      shell.removeEventListener('keydown', onMenuKey);
      shell.removeEventListener('pointerover', onTipOver);
      shell.removeEventListener('pointerout', onTipOut);
      shell.removeEventListener('focusin', onTipOver);
      shell.removeEventListener('focusout', onTipOut);
      shell.remove();
    },
  };
}

function slotColumn(label: string): HTMLElement {
  const column = document.createElement('div');
  column.className = 'dagger-inventory-slot-column';
  column.setAttribute('aria-label', label);
  return column;
}

function hint(device: 'keyboard' | 'controller', text: string): HTMLElement {
  const span = document.createElement('span');
  span.dataset.device = device;
  span.textContent = text;
  return span;
}

function equipmentStatus(value: InventoryProjection): string {
  const change = value.equipmentChange ?? null;
  return change === null ? value.message : `${value.message} ${change.cue === 'equip' ? 'Equipped.'
    : change.cue === 'unequip' ? 'Unequipped.'
      : 'Equipment transferred.'}`;
}

/** The tooltip's content for one item, all of it from the projection. */
function itemTooltip(item: InventoryItem, slotLabel: (id: string) => string): HTMLElement[] {
  const parts: HTMLElement[] = [];
  const name = document.createElement('strong');
  name.textContent = item.label;
  parts.push(name);
  if (!item.identified) parts.push(line('dagger-inventory-tip-warning', 'Unidentified'));
  if (item.details) parts.push(line('dagger-inventory-tip-details', item.details));
  const facts = document.createElement('dl');
  const fact = (term: string, value: string): void => {
    const dt = document.createElement('dt'); dt.textContent = term;
    const dd = document.createElement('dd'); dd.textContent = value;
    facts.append(dt, dd);
  };
  if (item.stackable) fact('Quantity', item.quantity);
  for (const stat of item.stats ?? []) fact(stat.label, stat.text);
  fact('Weight', item.weightKilograms === undefined ? formatNumber(item.weight) : `${formatNumber(item.weightKilograms)} kg`);
  fact('Value', `${formatNumber(item.value)} gold`);
  if (item.equippedSlots.length > 0) fact('Worn', item.equippedSlots.map(slotLabel).join(', '));
  parts.push(facts);
  // Wear means something only where there is more than one step of it: a coin is not worn down.
  if (item.condition !== null && item.condition.maximum > 1) {
    const condition = document.createElement('div');
    condition.className = 'dagger-inventory-tip-condition';
    const bar = document.createElement('span');
    bar.className = 'dagger-inventory-condition-bar';
    const fill = document.createElement('span');
    fill.style.width = `${Math.max(0, Math.min(100, item.condition.percentage))}%`;
    bar.append(fill);
    const text = document.createElement('span');
    text.textContent = item.condition.broken ? 'Broken' : `Condition ${item.condition.current} / ${item.condition.maximum}`;
    condition.classList.toggle('is-broken', item.condition.broken);
    condition.append(bar, text);
    parts.push(condition);
  }
  // Against what is worn where it goes: each shared figure with whether this one is better or worse.
  if (item.comparison) {
    const comparison = document.createElement('div');
    comparison.className = 'dagger-inventory-tip-comparison';
    const heading = document.createElement('p');
    heading.textContent = `Worn (${item.comparison.slot}): ${item.comparison.label}`;
    comparison.append(heading);
    for (const worn of item.comparison.stats) {
      const mine = item.stats?.find(stat => stat.label === worn.label);
      const row = document.createElement('p');
      const difference = mine === undefined ? 0 : Math.sign(mine.score - worn.score);
      row.className = difference > 0 ? 'is-better' : difference < 0 ? 'is-worse' : 'is-same';
      row.textContent = `${worn.label} ${worn.text}${mine === undefined ? '' : difference > 0 ? ' · this is better' : difference < 0 ? ' · this is worse' : ' · the same'}`;
      comparison.append(row);
    }
    parts.push(comparison);
  }
  // Why a piece of equipment cannot be worn; an item no slot takes says nothing.
  if (item.equippedSlots.length === 0 && item.compatibleSlots.length > 0 && item.canEquip !== true && item.equipRefusal) parts.push(line('dagger-inventory-tip-warning', item.equipRefusal));
  return parts;
}

function line(className: string, text: string): HTMLElement {
  const element = document.createElement('p');
  element.className = className;
  element.textContent = text;
  return element;
}

/**
 * The bank tab: the accounts, the loan, and one row per operation the ruleset publishes, each with
 * its own amount bounded by the published limits and the reason it is closed when it is.
 */
function createBankControls(claim: (action: InventoryAction) => void): { readonly element: HTMLElement; render(value: BankProjection | null): void } {
  const section = document.createElement('section');
  section.className = 'dagger-bank dagger-panel';
  section.setAttribute('aria-label', 'Regional bank');
  const heading = document.createElement('h3');
  heading.textContent = 'Regional bank';
  const active = document.createElement('p');
  active.className = 'dagger-bank-active';
  const loan = document.createElement('p');
  loan.className = 'dagger-bank-loan';
  const operations = document.createElement('div');
  operations.className = 'dagger-bank-operations';
  const balances = document.createElement('ul');
  balances.className = 'dagger-bank-balances';
  balances.setAttribute('aria-label', 'Regional account balances');
  section.append(heading, active, loan, operations, balances);
  const amounts = new Map<string, Stepper>();
  let destination: HTMLSelectElement | null = null;
  let shown: BankProjection | null = null;

  operations.addEventListener('click', event => {
    const button = event.target instanceof Element ? event.target.closest<HTMLButtonElement>('[data-bank-action]') : null;
    const offered = shown?.actions?.find(action => action.action === button?.dataset.bankAction);
    if (button === null || button.disabled || offered === undefined || !offered.available) return;
    const amount = amounts.get(offered.action)?.value() ?? 0;
    if (offered.action === 'currency-deposit-letters') claim({ action: offered.action });
    else if (offered.action === 'bank-transfer') {
      if (destination !== null && destination.value !== '') claim({ action: offered.action, amount, destination: Number(destination.value) });
    } else claim({ action: offered.action, amount });
  });

  return {
    element: section,
    render(value): void {
      shown = value;
      if (value === null) {
        active.textContent = '';
        loan.textContent = '';
        balances.replaceChildren();
        operations.replaceChildren();
        amounts.clear();
        destination = null;
        return;
      }
      active.textContent = `${value.currentRegionName}: ${value.currentBalance} gold`;
      loan.textContent = value.loan === null
        ? `No outstanding loan. Maximum new loan ${value.maximumNewLoan} gold.`
        : value.loan.defaulted
          ? `Defaulted loan: ${value.loan.remaining} gold remains.`
          : `Loan of ${value.loan.principal} gold: ${value.loan.remaining} gold remains; ${value.loan.daysRemaining} days until due.`;
      const accounts = value.accounts.filter(account => account.region !== value.currentRegion);
      redraw(operations, JSON.stringify([value.actions ?? [], accounts.map(account => [account.region, account.regionName])]), () => {
        const priorDestination = destination?.value ?? null;
        amounts.clear();
        destination = null;
        operations.replaceChildren(...(value.actions ?? []).map(offered => {
          const row = document.createElement('div');
          row.className = 'dagger-bank-operation';
          row.dataset.bankOperation = offered.action;
          const label = document.createElement('span');
          label.className = 'dagger-bank-operation-label';
          label.textContent = offered.label;
          row.append(label);
          if (offered.action === 'bank-transfer') {
            const select = document.createElement('select');
            select.setAttribute('aria-label', 'Transfer destination region');
            select.append(...accounts.map(account => new Option(account.regionName, String(account.region))));
            if (priorDestination !== null && accounts.some(account => String(account.region) === priorDestination)) select.value = priorDestination;
            select.disabled = !offered.available;
            destination = select;
            row.append(select);
          }
          if (offered.takesAmount) {
            const stepper = createStepper(`${offered.label}: amount`);
            stepper.limit(offered.minimum, Math.max(offered.minimum, offered.maximum), offered.minimum);
            stepper.input.disabled = !offered.available;
            amounts.set(offered.action, stepper);
            row.append(stepper.element);
          }
          const button = document.createElement('button');
          button.type = 'button';
          button.dataset.bankAction = offered.action;
          button.textContent = offered.takesAmount ? 'Confirm' : 'Deposit';
          button.setAttribute('aria-label', offered.label);
          button.disabled = !offered.available;
          row.append(button);
          if (offered.refusal) {
            const reason = document.createElement('p');
            reason.className = 'dagger-bank-refusal';
            reason.textContent = offered.refusal;
            row.append(reason);
          }
          return row;
        }));
      });
      balances.replaceChildren(...value.accounts.filter(account => account.gold !== '0' || account.region === value.currentRegion).map((account) => {
        const row = document.createElement('li');
        row.textContent = `${account.regionName}: ${account.gold} gold`;
        if (account.region === value.currentRegion) row.className = 'is-current';
        return row;
      }));
    },
  };
}

function renderPlace(button: HTMLButtonElement, item: InventoryItem | undefined, placeLabel: string): void {
  button.replaceChildren();
  delete button.dataset.inventoryItem;
  delete button.dataset.category;
  // Items are dragged with pointer events: a native drag session hangs a host that renders the page
  // off screen and never services it, so no element offers one.
  button.draggable = false;
  button.dataset.inventoryDraggable = String(item !== undefined);
  button.classList.toggle('is-broken', item?.condition?.broken === true);
  button.classList.toggle('is-unidentified', item !== undefined && !item.identified);
  if (item === undefined) {
    button.setAttribute('aria-label', `${placeLabel}, empty`);
    button.append(emptyMark());
    return;
  }
  button.dataset.inventoryItem = item.key;
  if (item.category) button.dataset.category = item.category;
  button.setAttribute('aria-label', `${placeLabel}, ${item.label}${item.stackable ? `, ${item.quantity}` : ''}`);
  const iconSource = image(item.icon);
  if (iconSource !== null) {
    // The icon is a published media identity; its bytes came from admitted content with the snapshot.
    const icon = document.createElement('img');
    icon.src = iconSource;
    icon.alt = '';
    icon.draggable = false;
    button.append(icon);
  } else {
    const fallback = document.createElement('span');
    fallback.className = 'dagger-inventory-fallback-label';
    fallback.textContent = item.label;
    button.append(fallback);
  }
  if (item.stackable || item.quantity !== '1') {
    const quantity = document.createElement('span');
    quantity.className = 'dagger-inventory-quantity';
    quantity.textContent = item.quantity;
    button.append(quantity);
  }
  if (item.condition !== null && item.condition.maximum > 1 && item.condition.percentage < 100) {
    const bar = document.createElement('span');
    bar.className = 'dagger-inventory-condition-bar';
    const fill = document.createElement('span');
    fill.style.width = `${Math.max(0, Math.min(100, item.condition.percentage))}%`;
    bar.append(fill);
    button.append(bar);
  }
}

function emptyMark(): HTMLElement {
  const mark = document.createElement('span');
  mark.className = 'dagger-inventory-empty-mark';
  mark.setAttribute('aria-hidden', 'true');
  return mark;
}

function formatNumber(value: number): string {
  return Number.isFinite(value) ? new Intl.NumberFormat(undefined, { maximumFractionDigits: 2 }).format(value) : '—';
}
