import { redraw } from './redraw.js';
import type {
  TransportAction, TransportItemProjection, TransportMode, TransportOptionProjection, TransportProjection, WagonProjection,
} from './projection.js';

/**
 * The transport strip under the travel map: how the player goes about (on foot, on horse or with the cart), boarding or
 * leaving their ship, and the wagon's load. What the wagon holds is stored and taken in the inventory, beside the pack.
 */
export function mountTransport(root: HTMLElement, claim: (action: TransportAction) => void): {
  update(value: TransportProjection | null): void;
  dispose(): void;
} {
  const shell = document.createElement('section');
  shell.className = 'dagger-transport';
  shell.setAttribute('aria-label', 'Transport');
  const options = document.createElement('div');
  options.className = 'dagger-transport-options';
  options.setAttribute('role', 'group');
  options.setAttribute('aria-label', 'How you travel');
  const status = document.createElement('p');
  status.className = 'dagger-transport-status';
  status.setAttribute('role', 'status');
  status.setAttribute('aria-live', 'polite');
  const wagon = document.createElement('p');
  wagon.className = 'dagger-transport-wagon';
  shell.append(options, status, wagon);
  root.append(shell);

  let current: TransportProjection | null = null;
  let disposed = false;

  const render = (): void => {
    if (disposed) return;
    if (current === null) {
      redraw(options, '', () => options.replaceChildren());
      status.textContent = wagon.textContent = '';
      return;
    }
    const view = current;
    status.textContent = view.summary;
    // The controls rebuild only when what they offer changes, so a focused mode keeps focus through every snapshot.
    redraw(options, JSON.stringify([view.onShip, view.options]), () => {
      options.replaceChildren();
      for (const option of view.options) {
        const optionMode = normalizeTransportMode(option.mode);
        if (optionMode === null) continue;
        const button = document.createElement('button');
        button.type = 'button';
        button.dataset.transportMode = optionMode;
        button.textContent = option.label;
        button.setAttribute('aria-pressed', String(option.selected && !view.onShip));
        if (option.message) button.dataset.tooltip = option.message;
        if (optionMode === 'ship') {
          button.disabled = !view.onShip && !option.available;
          button.textContent = view.onShip ? 'Leave ship' : 'Board ship';
          button.setAttribute('aria-pressed', String(view.onShip));
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
    const load = view.wagon;
    wagon.hidden = !load.exists;
    wagon.textContent = !load.exists ? '' : `Wagon ${formatKilograms(load.usedKilograms)} / ${formatKilograms(load.capacityKilograms)} kg`
      + (load.accessible ? ' · store and take in your inventory' : ` · ${load.message}`);
  };

  return {
    update(value): void {
      if (disposed) return;
      current = value;
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
    && typeof candidate.usedKilograms === 'number' && Number.isFinite(candidate.usedKilograms)
    && typeof candidate.capacityKilograms === 'number' && Number.isFinite(candidate.capacityKilograms)
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


export function formatKilograms(value: number): string {
  return Number.isFinite(value) ? Math.max(0, value).toLocaleString(undefined, { maximumFractionDigits: 2 }) : 'unknown';
}

