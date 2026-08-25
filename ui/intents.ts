import type { RustyApplicationRuntimeIntentValue, RustyApplicationUiContext } from '@rusty-engine/application-host';

/** Dagger semantic commands remain Rust-owned; this is only the direct-UI claim wire. */
type ProductPayload = {
  readonly kind: 'product-payload';
  readonly contract: string;
  readonly data: Readonly<Record<string, string | number | boolean | null>>;
};

export function claimDigital(context: RustyApplicationUiContext, intent: string): void {
  context.intents?.claim(intent, { kind: 'digital', active: true });
}

export function claimEquip(
  context: RustyApplicationUiContext,
  item: string,
  slot: string,
  expectedEquipmentRevision: string,
): void {
  const revision = expectedRevision(expectedEquipmentRevision);
  if (revision === null) return;
  claimPayload(context, 'dagger.equipment.equip', 'dagger.equipment.equip.v1', { item, slot, expectedEquipmentRevision: revision });
}

export function claimUnequip(
  context: RustyApplicationUiContext,
  slot: string,
  expectedItem: string | null,
  expectedEquipmentRevision: string,
): void {
  const revision = expectedRevision(expectedEquipmentRevision);
  if (revision === null) return;
  claimPayload(context, 'dagger.equipment.unequip', 'dagger.equipment.unequip.v1', { slot, expectedItem, expectedEquipmentRevision: revision });
}

export function claimInventoryMove(
  context: RustyApplicationUiContext,
  sourceSlot: number,
  targetSlot: number,
  expectedGridRevision: string,
): void {
  const revision = expectedRevision(expectedGridRevision);
  if (revision === null || sourceSlot === targetSlot) return;
  claimPayload(context, 'dagger.inventory.move', 'dagger.inventory.move.v1', { sourceSlot, targetSlot, expectedGridRevision: revision });
}

export function claimLootStack(
  context: RustyApplicationUiContext,
  containerId: string,
  expectedInventoryRevision: string,
  item: string,
  quantity: number,
): void {
  const revision = expectedRevision(expectedInventoryRevision);
  if (revision === null || !Number.isSafeInteger(quantity) || quantity < 1) return;
  claimPayload(context, 'dagger.loot.transfer-stack', 'dagger.loot.transfer-stack.v1', { containerId, expectedInventoryRevision: revision, item, quantity });
}

export function claimLootItem(
  context: RustyApplicationUiContext,
  containerId: string,
  expectedInventoryRevision: string,
  item: string,
): void {
  const revision = expectedRevision(expectedInventoryRevision);
  if (revision === null) return;
  claimPayload(context, 'dagger.loot.transfer-item', 'dagger.loot.transfer-item.v1', { containerId, expectedInventoryRevision: revision, item });
}

function claimPayload(
  context: RustyApplicationUiContext,
  intent: string,
  contract: string,
  data: ProductPayload['data'],
): void {
  // ProductPayload is an explicitly declared Runtime Intent value kind. The
  // assertion keeps this UI source compatible with a previously materialized
  // application-host declaration while preserving the one claim transport.
  const value: ProductPayload = Object.freeze({ kind: 'product-payload', contract, data: Object.freeze({ ...data }) });
  context.intents?.claim(intent, value as unknown as RustyApplicationRuntimeIntentValue);
}

function expectedRevision(value: string): number | null {
  const revision = Number(value);
  return Number.isSafeInteger(revision) && revision >= 0 ? revision : null;
}
