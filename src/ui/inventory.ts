export interface InventoryProjection {
  readonly revision: string;
  readonly items: readonly InventoryItem[];
  readonly slots: readonly EquipmentSlot[];
  readonly message: string;
}

export interface InventoryItem {
  readonly key: string;
  readonly definition: string;
  readonly label: string;
  readonly quantity: string;
  readonly weight: number;
  readonly value: number;
  readonly details: string;
  readonly icon: string | null;
  readonly gridSlot: number | null;
  readonly equippedSlots: readonly string[];
  readonly compatibleSlots: readonly string[];
}

export interface EquipmentSlot {
  readonly id: string;
  readonly label: string;
  readonly itemKey: string | null;
}

export type InventoryAction = {
  readonly action: 'inventory-move';
  readonly revision: string;
  readonly item: string;
  readonly targetGrid?: number;
  readonly targetEquipment?: string;
};

type MoveSource = { readonly key: string; readonly revision: string };

const GRID_SLOT_COUNT = 50;
const EQUIPMENT_SLOT_COUNT = 25;

/**
 * Presents an Engine-projected inventory without retaining any gameplay state.
 * The caller owns modal lifetime, interaction mode, and every claimed action.
 */
export function mountInventory(
  root: HTMLElement,
  claim: (action: InventoryAction) => void,
): { update(value: InventoryProjection): void; dispose(): void } {
  const shell = document.createElement('section');
  shell.className = 'dagger-inventory';
  shell.setAttribute('aria-label', 'Inventory and equipment');
  applyAuthoredArt(shell);

  const heading = document.createElement('h2');
  heading.className = 'dagger-inventory-title';
  heading.textContent = 'Inventory & equipment';
  const status = document.createElement('p');
  status.className = 'dagger-inventory-status';
  status.setAttribute('role', 'status');
  status.setAttribute('aria-live', 'polite');
  status.textContent = 'Awaiting inventory projection…';

  const body = document.createElement('div');
  body.className = 'dagger-inventory-body';
  const gridSection = document.createElement('section');
  gridSection.className = 'dagger-inventory-grid-section';
  const gridHeading = document.createElement('h3');
  gridHeading.textContent = 'Pack';
  const grid = document.createElement('div');
  grid.className = 'dagger-inventory-grid';
  grid.setAttribute('aria-label', 'Pack slots');
  const overflowHeading = document.createElement('h3');
  overflowHeading.className = 'dagger-inventory-overflow-title';
  overflowHeading.textContent = 'Pack overflow';
  const overflow = document.createElement('ul');
  overflow.className = 'dagger-inventory-overflow';
  overflow.setAttribute('aria-label', 'Pack overflow items');
  const equipmentSection = document.createElement('section');
  equipmentSection.className = 'dagger-inventory-equipment-section';
  const equipmentHeading = document.createElement('h3');
  equipmentHeading.textContent = 'Equipment';
  const equipment = document.createElement('div');
  equipment.className = 'dagger-inventory-equipment';
  equipment.setAttribute('aria-label', 'Equipment slots');
  const details = createDetails();

  gridSection.append(gridHeading, grid, overflowHeading, overflow);
  equipmentSection.append(equipmentHeading, equipment);
  body.append(gridSection, equipmentSection, details.element);
  shell.append(heading, body, status);
  root.append(shell);

  const gridButtons = Array.from({ length: GRID_SLOT_COUNT }, (_, index) => {
    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'dagger-inventory-grid-slot';
    button.dataset.inventoryGrid = String(index);
    button.setAttribute('aria-label', `Pack slot ${index + 1}, empty`);
    grid.append(button);
    return button;
  });
  const equipmentButtons = Array.from({ length: EQUIPMENT_SLOT_COUNT }, (_, index) => {
    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'dagger-inventory-equipment-slot';
    button.dataset.inventoryEquipmentIndex = String(index);
    button.setAttribute('aria-label', `Equipment slot ${index + 1}, empty`);
    equipment.append(button);
    return button;
  });

  let current: InventoryProjection | null = null;
  let lastRevision: string | null = null;
  let lastMessage: string | null = null;
  let selected: MoveSource | null = null;
  let dragging: MoveSource | null = null;
  let pending: InventoryProjection | null = null;
  let disposed = false;

  const sourceFor = (key: string): MoveSource | null => current === null ? null : { key, revision: current.revision };
  const move = (source: MoveSource, target: { readonly grid?: number; readonly equipment?: string }): void => {
    if ((target.grid === undefined) === (target.equipment === undefined)) return;
    claim(target.grid === undefined
      ? { action: 'inventory-move', revision: source.revision, item: source.key, targetEquipment: target.equipment }
      : { action: 'inventory-move', revision: source.revision, item: source.key, targetGrid: target.grid });
  };

  const select = (source: MoveSource): void => {
    selected = source;
    renderSelection();
  };

  const renderSelection = (): void => {
    const item = selected === null || current === null ? undefined : current.items.find((candidate) => candidate.key === selected!.key);
    details.render(item, current?.slots ?? []);
    for (const button of Array.from(shell.querySelectorAll<HTMLButtonElement>('[data-inventory-item]'))) {
      button.classList.toggle('is-selected', item !== undefined && button.dataset.inventoryItem === item.key);
    }
  };

  const render = (value: InventoryProjection): void => {
    current = value;
    lastRevision = value.revision;
    lastMessage = value.message;
    status.textContent = value.message;
    const byKey = new Map(value.items.map((item) => [item.key, item]));
    const byGrid = new Map(value.items.flatMap((item) => item.gridSlot === null || item.gridSlot < 0 || item.gridSlot >= GRID_SLOT_COUNT
      ? [] : [[item.gridSlot, item] as const]));
    const byEquipment = new Map(value.slots.filter((slot) => slot.itemKey !== null)
      .flatMap((slot) => {
        const item = slot.itemKey === null ? undefined : byKey.get(slot.itemKey);
        return item === undefined ? [] : [[slot.id, item] as const];
      }));

    gridButtons.forEach((button, index) => renderPlace(button, byGrid.get(index), `Pack slot ${index + 1}`));
    equipmentButtons.forEach((button, index) => {
      const slot = value.slots[index];
      const label = slot?.label ?? `Equipment slot ${index + 1}`;
      button.dataset.inventoryEquipment = slot?.id ?? '';
      button.disabled = slot === undefined;
      renderPlace(button, slot === undefined ? undefined : byEquipment.get(slot.id), label, label);
    });
    renderOverflow(overflow, value.items.filter((item) => item.gridSlot === null && item.equippedSlots.length === 0));
    if (selected !== null && !byKey.has(selected.key)) selected = null;
    renderSelection();
  };

  const finishDrag = (): void => {
    dragging = null;
    shell.classList.remove('is-dragging');
    shell.querySelectorAll('.is-drop-target').forEach((element) => element.classList.remove('is-drop-target'));
    if (pending !== null) {
      const next = pending;
      pending = null;
      render(next);
    }
  };

  const onDragStart = (event: DragEvent): void => {
    const button = event.target instanceof Element ? event.target.closest<HTMLButtonElement>('[data-inventory-item]') : null;
    const source = button?.dataset.inventoryItem === undefined ? null : sourceFor(button.dataset.inventoryItem);
    if (source === null || event.dataTransfer === null) {
      event.preventDefault();
      return;
    }
    dragging = source;
    select(source);
    shell.classList.add('is-dragging');
    event.dataTransfer.effectAllowed = 'move';
    event.dataTransfer.setData('text/plain', source.key);
  };
  const onDragEnd = (): void => finishDrag();
  const onDragOver = (event: DragEvent): void => {
    if (dragging === null) return;
    const target = event.target instanceof Element ? event.target.closest<HTMLButtonElement>('[data-inventory-grid], [data-inventory-equipment]') : null;
    if (target === null || target.disabled) return;
    event.preventDefault();
    event.dataTransfer!.dropEffect = 'move';
    target.classList.add('is-drop-target');
  };
  const onDragLeave = (event: DragEvent): void => {
    const target = event.target instanceof Element ? event.target.closest<HTMLElement>('.is-drop-target') : null;
    target?.classList.remove('is-drop-target');
  };
  const onDrop = (event: DragEvent): void => {
    const target = event.target instanceof Element ? event.target.closest<HTMLButtonElement>('[data-inventory-grid], [data-inventory-equipment]') : null;
    const source = dragging;
    if (target === null || source === null || target.disabled) return;
    event.preventDefault();
    if (target.dataset.inventoryGrid !== undefined) move(source, { grid: Number(target.dataset.inventoryGrid) });
    else if (target.dataset.inventoryEquipment) move(source, { equipment: target.dataset.inventoryEquipment });
    finishDrag();
  };
  const onClick = (event: MouseEvent): void => {
    const button = event.target instanceof Element ? event.target.closest<HTMLButtonElement>('button') : null;
    if (button === null || button.disabled) return;
    if (button.dataset.inventoryItem !== undefined) {
      const source = sourceFor(button.dataset.inventoryItem);
      if (source !== null) select(source);
      return;
    }
    if (selected === null) return;
    if (button.dataset.inventoryGrid !== undefined) move(selected, { grid: Number(button.dataset.inventoryGrid) });
    else if (button.dataset.inventoryEquipment) move(selected, { equipment: button.dataset.inventoryEquipment });
    else if (button.dataset.inventoryAction === 'move-grid') {
      move(selected, { grid: Number(details.gridTarget.value) });
    } else if (button.dataset.inventoryAction === 'move-equipment' && details.equipmentTarget.value) {
      move(selected, { equipment: details.equipmentTarget.value });
    }
  };

  shell.addEventListener('dragstart', onDragStart);
  shell.addEventListener('dragend', onDragEnd);
  shell.addEventListener('dragover', onDragOver);
  shell.addEventListener('dragleave', onDragLeave);
  shell.addEventListener('drop', onDrop);
  shell.addEventListener('click', onClick);

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
      if (dragging !== null) {
        pending = value;
        if (value.message !== lastMessage) {
          lastMessage = value.message;
          status.textContent = value.message;
        }
        return;
      }
      render(value);
    },
    dispose(): void {
      if (disposed) return;
      disposed = true;
      shell.removeEventListener('dragstart', onDragStart);
      shell.removeEventListener('dragend', onDragEnd);
      shell.removeEventListener('dragover', onDragOver);
      shell.removeEventListener('dragleave', onDragLeave);
      shell.removeEventListener('drop', onDrop);
      shell.removeEventListener('click', onClick);
      shell.remove();
    },
  };
}

function createDetails(): {
  readonly element: HTMLElement;
  readonly gridTarget: HTMLSelectElement;
  readonly equipmentTarget: HTMLSelectElement;
  render(item: InventoryItem | undefined, slots: readonly EquipmentSlot[]): void;
} {
  const element = document.createElement('section');
  element.className = 'dagger-inventory-details';
  const heading = document.createElement('h3');
  heading.textContent = 'Item details';
  const name = document.createElement('strong');
  const description = document.createElement('p');
  const metadata = document.createElement('p');
  metadata.className = 'dagger-inventory-metadata';
  const actions = document.createElement('div');
  actions.className = 'dagger-inventory-keyboard-actions';
  const gridTarget = targetSelect('Pack slot');
  for (let index = 0; index < GRID_SLOT_COUNT; index += 1) addOption(gridTarget, String(index), `Pack slot ${index + 1}`);
  const moveGrid = document.createElement('button');
  moveGrid.type = 'button';
  moveGrid.dataset.inventoryAction = 'move-grid';
  moveGrid.textContent = 'Move to pack slot';
  const equipmentTarget = targetSelect('Equipment slot');
  const moveEquipment = document.createElement('button');
  moveEquipment.type = 'button';
  moveEquipment.dataset.inventoryAction = 'move-equipment';
  moveEquipment.textContent = 'Equip in slot';
  actions.append(gridTarget, moveGrid, equipmentTarget, moveEquipment);
  element.append(heading, name, description, metadata, actions);

  return {
    element,
    gridTarget,
    equipmentTarget,
    render(item, slots): void {
      name.textContent = item?.label ?? 'Select an item';
      description.textContent = item?.details ?? 'Choose an item, then choose a pack or equipment destination.';
      metadata.textContent = item === undefined ? '' : `${item.quantity} · Weight ${formatNumber(item.weight)} · Value ${formatNumber(item.value)} · ${item.definition}`;
      const prior = equipmentTarget.value;
      equipmentTarget.replaceChildren();
      const compatible = item === undefined ? new Set<string>() : new Set(item.compatibleSlots);
      for (const slot of slots) {
        const option = new Option(slot.label, slot.id);
        option.textContent = compatible.has(slot.id) ? slot.label : `${slot.label} (may be rejected)`;
        equipmentTarget.append(option);
      }
      if (Array.from(equipmentTarget.options).some((option) => option.value === prior)) equipmentTarget.value = prior;
      moveGrid.disabled = item === undefined;
      gridTarget.disabled = item === undefined;
      equipmentTarget.disabled = item === undefined || equipmentTarget.options.length === 0;
      moveEquipment.disabled = equipmentTarget.disabled;
    },
  };
}

function targetSelect(label: string): HTMLSelectElement {
  const select = document.createElement('select');
  select.setAttribute('aria-label', label);
  return select;
}

function addOption(select: HTMLSelectElement, value: string, label: string): void {
  select.append(new Option(label, value));
}

function renderPlace(
  button: HTMLButtonElement,
  item: InventoryItem | undefined,
  emptyLabel: string,
  visibleLabel?: string,
): void {
  button.replaceChildren();
  delete button.dataset.inventoryItem;
  button.draggable = item !== undefined;
  if (item === undefined) {
    button.setAttribute('aria-label', `${emptyLabel}, empty`);
    button.append(emptyMark());
    appendSlotLabel(button, visibleLabel);
    return;
  }
  button.dataset.inventoryItem = item.key;
  button.setAttribute('aria-label', `${emptyLabel}, ${item.label}, ${item.quantity}`);
  if (item.icon !== null) {
    const icon = document.createElement('img');
    icon.src = new URL(item.icon, import.meta.url).href;
    icon.alt = '';
    icon.draggable = false;
    button.append(icon);
  } else {
    const fallback = document.createElement('span');
    fallback.className = 'dagger-inventory-fallback-label';
    fallback.textContent = item.label;
    button.append(fallback);
  }
  const quantity = document.createElement('span');
  quantity.className = 'dagger-inventory-quantity';
  quantity.textContent = item.quantity;
  button.append(quantity);
  appendSlotLabel(button, visibleLabel);
}

function appendSlotLabel(button: HTMLButtonElement, label: string | undefined): void {
  if (label === undefined) return;
  const caption = document.createElement('span');
  caption.className = 'dagger-inventory-equipment-label';
  caption.textContent = label;
  button.append(caption);
}

function renderOverflow(container: HTMLElement, items: readonly InventoryItem[]): void {
  container.replaceChildren(...items.map((item) => {
    const row = document.createElement('li');
    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'dagger-inventory-overflow-item';
    button.dataset.inventoryItem = item.key;
    button.draggable = true;
    button.setAttribute('aria-label', `${item.label}, ${item.quantity}, pack overflow`);
    if (item.icon !== null) {
      const icon = document.createElement('img');
      icon.src = new URL(item.icon, import.meta.url).href;
      icon.alt = '';
      icon.draggable = false;
      button.append(icon);
    }
    const name = document.createElement('strong');
    name.textContent = item.label;
    const quantity = document.createElement('span');
    quantity.textContent = item.quantity;
    const details = document.createElement('small');
    details.textContent = item.details;
    button.append(name, quantity, details);
    row.append(button);
    return row;
  }));
}

function emptyMark(): HTMLElement {
  const mark = document.createElement('span');
  mark.className = 'dagger-inventory-empty-mark';
  mark.setAttribute('aria-hidden', 'true');
  return mark;
}

function applyAuthoredArt(element: HTMLElement): void {
  const art = (name: string): string => `url("${new URL(`./inventory-art/authored/${name}`, import.meta.url).href}")`;
  element.style.setProperty('--inventory-panel-art', art('inventory-skin-panel-slate-v1.png'));
  element.style.setProperty('--inventory-title-art', art('inventory-titlebar-slate-v1.png'));
  element.style.setProperty('--inventory-slot-art', art('inventory-grid-slot-slate-v1.png'));
}

function formatNumber(value: number): string {
  return Number.isFinite(value) ? new Intl.NumberFormat(undefined, { maximumFractionDigits: 2 }).format(value) : '—';
}
