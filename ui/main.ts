import type { RustyApplicationUiContext, RustyApplicationUiOwner } from '@rusty-engine/application-host';
import { claimDigital, claimEquip, claimInventoryMove, claimLootItem, claimLootStack, claimUnequip } from './intents.js';
import { decodeDaggerUiProjection, emptyDaggerUiProjection, type DaggerUiProjection } from './model.js';
import { daggerProductUiStyles } from './styles.js';
import { renderDaggerUi, type DaggerUiMode } from './template.js';

interface LocalViewState {
  readonly mode: DaggerUiMode;
  readonly selectedSlot: number | null;
  readonly selectedEquipment: string | null;
}

/** Mount Dagger's rich DOM presentation beside the Engine-owned sole canvas. */
export function mountProductUi(root: HTMLElement, context: RustyApplicationUiContext): RustyApplicationUiOwner {
  let projection: DaggerUiProjection = emptyDaggerUiProjection();
  let state: LocalViewState = { mode: 'gameplay', selectedSlot: null, selectedEquipment: null };
  const stylesheet = document.createElement('style');
  stylesheet.textContent = daggerProductUiStyles;
  root.replaceChildren(stylesheet);
  root.classList.add('dagger-product-ui');

  const render = (): void => {
    const frame = document.createElement('div');
    frame.className = 'dagger-product-ui';
    frame.innerHTML = renderDaggerUi(projection, state);
    root.replaceChildren(stylesheet, frame);
  };
  const setMode = (mode: DaggerUiMode): void => {
    state = { ...state, mode };
    context.ui.setInteractionMode(mode === 'gameplay' ? 'gameplay' : 'modal');
    if (mode === 'gameplay') context.ui.focusGameplay();
    render();
  };
  const action = (element: HTMLElement): void => {
    const kind = element.getAttribute('data-dagger-action');
    switch (kind) {
      case 'open-menu': setMode('menu'); return;
      case 'open-inventory': setMode('inventory'); return;
      case 'open-character': setMode('character'); return;
      case 'open-loot':
        claimDigital(context, 'dagger.loot.open');
        setMode('loot');
        return;
      case 'close':
        if (state.mode === 'loot') claimDigital(context, 'dagger.loot.close');
        setMode('gameplay');
        return;
      case 'select-slot': selectSlot(element); return;
      case 'select-equipment':
        state = { ...state, selectedEquipment: element.getAttribute('data-slot'), selectedSlot: null };
        render();
        return;
      case 'drop-on-equipment': equipSelected(element); return;
      case 'unequip': unequipSelected(element); return;
      case 'take-loot': takeLoot(element); return;
      default: return;
    }
  };
  const selectSlot = (element: HTMLElement): void => {
    const index = parseIndex(element.getAttribute('data-index'));
    if (index === null) return;
    if (state.selectedSlot !== null) {
      claimInventoryMove(context, state.selectedSlot, index, projection.inventory.gridRevision);
      state = { ...state, selectedSlot: null, selectedEquipment: null };
    } else {
      state = { ...state, selectedSlot: index, selectedEquipment: null };
    }
    render();
  };
  const equipSelected = (element: HTMLElement): void => {
    const slot = element.getAttribute('data-slot');
    const selected = projection.inventory.slots.find((entry) => entry.index === state.selectedSlot)?.item;
    if (slot === null || selected?.entity === null || selected?.entity === undefined) return;
    claimEquip(context, selected.entity, slot, projection.inventory.equipmentRevision);
    state = { ...state, selectedSlot: null };
    render();
  };
  const unequipSelected = (element: HTMLElement): void => {
    const slot = element.getAttribute('data-slot');
    const selected = projection.inventory.equipment.find((entry) => entry.id === slot)?.item;
    if (slot === null) return;
    claimUnequip(context, slot, selected?.entity ?? null, projection.inventory.equipmentRevision);
  };
  const takeLoot = (element: HTMLElement): void => {
    const loot = projection.loot;
    if (loot === null) return;
    const itemId = element.getAttribute('data-item');
    const entity = element.getAttribute('data-entity');
    if (itemId === null) return;
    if (entity === null || entity === '') claimLootStack(context, loot.containerId, loot.revision, itemId, 1);
    else claimLootItem(context, loot.containerId, loot.revision, entity);
  };
  const abort = new AbortController();
  root.addEventListener('click', (event) => {
    const target = event.target instanceof Element ? event.target.closest<HTMLElement>('[data-dagger-action]') : null;
    if (target !== null && root.contains(target)) action(target);
  }, { signal: abort.signal });
  const unsubscribe = context.projection?.subscribe((envelope) => {
    if (envelope === null) {
      projection = emptyDaggerUiProjection();
    } else if (envelope.stream === 'dagger.ui' && envelope.contract === 'dagger.ui.v1') {
      // The host admits this one declared compact aggregate; UI never asks Rust
      // for the old unbounded ProductReadout or synthesizes missing state.
      projection = decodeDaggerUiProjection(envelope.value) ?? emptyDaggerUiProjection();
    }
    render();
  }) ?? (() => undefined);
  render();
  return {
    dispose: (): void => {
      abort.abort();
      unsubscribe();
      root.classList.remove('dagger-product-ui');
      root.replaceChildren();
    },
  };
}

function parseIndex(value: string | null): number | null {
  if (value === null || !/^(0|[1-9][0-9]{0,2})$/.test(value)) return null;
  const index = Number(value);
  return Number.isSafeInteger(index) && index < 50 ? index : null;
}
