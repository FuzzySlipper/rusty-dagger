import type { InventoryProjection } from './inventory.js';
import { redraw } from './redraw.js';
import type {
  TransportAction, TransportItemProjection, TransportMode, TransportOptionProjection, TransportProjection, WagonProjection,
} from './projection.js';

export function mountTransport(root: HTMLElement, claim: (action: TransportAction) => void): {
  update(value: TransportProjection | null, inventory?: InventoryProjection): void;
  dispose(): void;
} {
  const shell = document.createElement('section');
  shell.className = 'dagger-transport';
  shell.setAttribute('aria-label', 'Travel and transport');
  const heading = document.createElement('h2');
  heading.textContent = 'Travel & transport';
  const status = document.createElement('p');
  status.className = 'dagger-transport-status';
  status.setAttribute('role', 'status');
  status.setAttribute('aria-live', 'polite');
  const summary = document.createElement('p');
  summary.className = 'dagger-transport-summary';
  const optionsHeading = document.createElement('h3');
  optionsHeading.textContent = 'Travel mode';
  const options = document.createElement('div');
  options.className = 'dagger-transport-options';
  const toggle = document.createElement('button');
  toggle.type = 'button';
  toggle.dataset.action = 'transport-toggle';
  toggle.textContent = 'Toggle mount';
  const wagon = document.createElement('section');
  wagon.className = 'dagger-transport-wagon';
  wagon.setAttribute('aria-label', 'Wagon storage');
  const wagonHeading = document.createElement('h3');
  wagonHeading.textContent = 'Wagon storage';
  const wagonStatus = document.createElement('p');
  wagonStatus.className = 'dagger-transport-wagon-status';
  wagonStatus.setAttribute('role', 'status');
  const wagonContentsHeading = document.createElement('h4');
  wagonContentsHeading.textContent = 'Stored items';
  const wagonContents = document.createElement('ul');
  wagonContents.className = 'dagger-transport-wagon-items';
  const packHeading = document.createElement('h4');
  packHeading.textContent = 'Store from pack';
  const packContents = document.createElement('ul');
  packContents.className = 'dagger-transport-pack-items';
  wagon.append(wagonHeading, wagonStatus, wagonContentsHeading, wagonContents, packHeading, packContents);
  shell.append(heading, status, summary, optionsHeading, options, toggle, wagon);
  root.append(shell);

  let current: TransportProjection | null = null;
  let currentInventory: InventoryProjection | undefined;
  let disposed = false;

  const itemQuantity = (quantity: string | number): string => String(quantity);
  const safeAmount = (quantity: string | number): number | undefined => {
    const parsed = typeof quantity === 'number' ? quantity : Number(quantity);
    return Number.isSafeInteger(parsed) && parsed > 0 ? parsed : undefined;
  };
  const revision = (): string => currentInventory?.revision ?? '';
  const sendItem = (action: 'wagon-put' | 'wagon-take', item: TransportItemProjection): void => {
    const currentRevision = revision();
    if (!currentRevision) return;
    const amount = item.key.startsWith('stack:') ? safeAmount(item.quantity) : undefined;
    claim(amount === undefined
      ? { action, revision: currentRevision, item: item.key }
      : { action, revision: currentRevision, item: item.key, amount });
  };
  const renderItem = (item: TransportItemProjection, action: 'wagon-put' | 'wagon-take', disabled: boolean): HTMLLIElement => {
    const row = document.createElement('li');
    const button = document.createElement('button');
    button.type = 'button';
    button.dataset.action = action;
    button.dataset.item = item.key;
    button.disabled = disabled;
    button.textContent = `${action === 'wagon-put' ? 'Store' : 'Take'} ${item.label} × ${itemQuantity(item.quantity)}`;
    button.addEventListener('click', () => sendItem(action, item));
    row.append(button);
    return row;
  };

  const render = (): void => {
    if (disposed) return;
    if (current === null) {
      redraw(options, '', () => options.replaceChildren());
      redraw(wagonContents, '', () => wagonContents.replaceChildren());
      redraw(packContents, '', () => packContents.replaceChildren());
      status.textContent = 'Transport projection unavailable.';
      summary.textContent = '';
      toggle.disabled = true;
      wagonStatus.textContent = 'Wagon storage is unavailable.';
      return;
    }
    const view = current;
    const mode = normalizeTransportMode(view.mode);
    const selected = view.options.find(option => option.selected)?.label ?? mode ?? 'Foot';
    status.textContent = view.onShip ? 'You are aboard a ship.' : `${selected} travel selected.`;
    summary.textContent = view.summary;
    toggle.disabled = view.onShip;
    toggle.textContent = view.onShip ? 'Leave ship before choosing a mount' : 'Toggle mount';
    if (view.onShip) delete toggle.dataset.tooltip; else toggle.dataset.tooltip = 'Switch between horse, cart and foot.';

    // The controls rebuild only when what they offer changes, so a focused mode or item keeps focus
    // through the snapshot every update brings.
    redraw(options, JSON.stringify([view.onShip, view.options]), () => {
      options.replaceChildren();
      for (const option of view.options) {
        const optionMode = normalizeTransportMode(option.mode);
        if (optionMode === null) continue;
        const button = document.createElement('button');
        button.type = 'button';
        button.dataset.transportMode = optionMode;
        button.textContent = `${option.label}${option.selected ? ' · selected' : ''}`;
        if (option.message) button.dataset.tooltip = option.message;
        if (optionMode === 'ship') {
          button.disabled = !view.onShip && !option.available;
          button.textContent = view.onShip ? 'Leave ship' : 'Board ship';
          if (!button.disabled) button.addEventListener('click', () => claim({
            action: current?.onShip ? 'transport-leave-ship' : 'transport-board-ship',
          }));
        } else {
          button.disabled = view.onShip || !option.available;
          if (!button.disabled) button.addEventListener('click', () => claim({ action: 'transport-select', mode: optionMode }));
        }
        options.append(button);
      }
    });
    const wagonView = view.wagon;
    wagonStatus.textContent = `${wagonView.message} ${formatClassicUnits(wagonView.usedClassicUnits)} / ${formatClassicUnits(wagonView.capacityClassicUnits)} classic weight units.`;
    const currentRevision = revision();
    const packItems = wagonView.accessible ? currentInventory?.items ?? [] : [];
    redraw(wagonContents, JSON.stringify([wagonView.accessible, wagonView.items, Boolean(currentRevision)]), () => {
      wagonContents.replaceChildren();
      if (!wagonView.accessible) return;
      for (const item of wagonView.items) wagonContents.append(renderItem(item, 'wagon-take', !currentRevision));
      if (wagonView.items.length === 0) {
        const empty = document.createElement('li');
        empty.textContent = 'The wagon is empty.';
        wagonContents.append(empty);
      }
    });
    redraw(packContents, JSON.stringify([wagonView.accessible, packItems, wagonView.refusedDefinitions, Boolean(currentRevision)]), () => {
      packContents.replaceChildren();
      if (!wagonView.accessible) return;
      for (const item of packItems) {
        const equipped = item.equippedSlots.length !== 0;
        const invalidQuantity = item.key.startsWith('stack:') && safeAmount(item.quantity) === undefined;
        const refused = wagonView.refusedDefinitions.includes(item.definition);
        packContents.append(renderItem(item, 'wagon-put', !currentRevision || equipped || invalidQuantity || refused));
      }
      if (packItems.length === 0) {
        const empty = document.createElement('li');
        empty.textContent = 'Your pack has no transferable items.';
        packContents.append(empty);
      }
    });
  };

  toggle.addEventListener('click', () => {
    if (!toggle.disabled) claim({ action: 'transport-toggle' });
  });
  return {
    update(value, inventory): void {
      if (disposed) return;
      current = value;
      currentInventory = inventory;
      render();
    },
    dispose(): void {
      if (disposed) return;
      disposed = true;
      shell.remove();
    },
  };
}

export function normalizeTransportMode(value: string): TransportMode | null {
  const mode = value.toLowerCase();
  return mode === 'foot' || mode === 'horse' || mode === 'cart' || mode === 'ship' ? mode : null;
}

export function isTransportProjection(value: unknown): value is TransportProjection {
  if (typeof value !== 'object' || value === null || !('options' in value) || !Array.isArray(value.options)
    || !('wagon' in value) || !isWagonProjection(value.wagon)) return false;
  const candidate = value as Partial<TransportProjection>;
  return typeof candidate.mode === 'string'
    && typeof candidate.onShip === 'boolean'
    && typeof candidate.canRun === 'boolean'
    && typeof candidate.summary === 'string'
    && typeof candidate.travelModifier === 'number' && Number.isFinite(candidate.travelModifier)
    && typeof candidate.oceanMinutesPerMapPixel === 'number' && Number.isFinite(candidate.oceanMinutesPerMapPixel)
    && Array.isArray(candidate.options) && candidate.options.every(isTransportOptionProjection);
}

export function isTransportOptionProjection(value: unknown): value is TransportOptionProjection {
  if (typeof value !== 'object' || value === null) return false;
  const candidate = value as Partial<TransportOptionProjection>;
  return typeof candidate.id === 'string'
    && typeof candidate.mode === 'string'
    && typeof candidate.available === 'boolean'
    && typeof candidate.selected === 'boolean'
    && typeof candidate.label === 'string'
    && typeof candidate.message === 'string'
    && typeof candidate.travelModifier === 'number' && Number.isFinite(candidate.travelModifier);
}

export function isWagonProjection(value: unknown): value is WagonProjection {
  if (typeof value !== 'object' || value === null || !('items' in value) || !Array.isArray(value.items)) return false;
  const candidate = value as Partial<WagonProjection>;
  return typeof candidate.exists === 'boolean'
    && typeof candidate.accessible === 'boolean'
    && (candidate.id === null || typeof candidate.id === 'number')
    && typeof candidate.usedClassicUnits === 'number' && Number.isFinite(candidate.usedClassicUnits)
    && typeof candidate.capacityClassicUnits === 'number' && Number.isFinite(candidate.capacityClassicUnits)
    && (candidate.storeRevision === null || typeof candidate.storeRevision === 'string' || typeof candidate.storeRevision === 'number')
    && typeof candidate.message === 'string'
    && Array.isArray(candidate.items) && candidate.items.every(isTransportItemProjection)
    && Array.isArray(candidate.refusedDefinitions) && candidate.refusedDefinitions.every(value => typeof value === 'string');
}

export function isTransportItemProjection(value: unknown): value is TransportItemProjection {
  if (typeof value !== 'object' || value === null) return false;
  const candidate = value as Partial<TransportItemProjection>;
  return typeof candidate.key === 'string'
    && typeof candidate.definition === 'string'
    && typeof candidate.label === 'string'
    && (typeof candidate.quantity === 'string' || typeof candidate.quantity === 'number');
}


export function formatClassicUnits(value: number): string {
  return Number.isFinite(value) ? Math.max(0, value).toLocaleString() : 'unknown';
}

