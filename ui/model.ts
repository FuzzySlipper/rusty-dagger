/**
 * Read-only, bounded vocabulary rendered by the Dagger product UI.  Rust owns
 * the semantic projection; this module only rejects malformed optional fields
 * and supplies presentational fallbacks.
 */
export interface DaggerUiProjection {
  readonly hud: DaggerHud;
  readonly inventory: DaggerInventory;
  readonly character: DaggerCharacter;
  readonly loot: DaggerLoot | null;
  readonly encounter: DaggerEncounter | null;
  readonly notices: readonly DaggerNotice[];
}

export interface DaggerHud {
  readonly health: Meter;
  readonly stamina: Meter;
  readonly magicka: Meter;
  readonly level: number;
  readonly experience: number;
  readonly experienceToNext: number;
}

export interface Meter {
  readonly current: number;
  readonly maximum: number;
}

export interface DaggerInventory {
  readonly equipmentRevision: string;
  readonly gridRevision: string;
  readonly capacity: readonly Capacity[];
  readonly slots: readonly InventorySlot[];
  readonly equipment: readonly EquipmentSlot[];
  readonly receipt: DaggerReceipt | null;
}

export interface Capacity {
  readonly label: string;
  readonly used: number;
  readonly maximum: number | null;
}

export interface InventorySlot {
  readonly index: number;
  readonly item: DaggerItem | null;
}

export interface EquipmentSlot {
  readonly id: string;
  readonly item: DaggerItem | null;
}

export interface DaggerItem {
  readonly entity: string | null;
  readonly id: string;
  readonly quantity: number;
  readonly compatibleSlots: readonly string[];
  readonly detail: string | null;
}

export interface DaggerReceipt {
  readonly accepted: boolean;
  readonly message: string;
}

export interface DaggerLoot {
  readonly containerId: string;
  readonly revision: string;
  readonly items: readonly DaggerItem[];
  readonly message: string | null;
}

export interface DaggerCharacter {
  readonly attributes: readonly Readonly<{ readonly label: string; readonly value: string }> [];
  readonly skills: readonly Readonly<{ readonly label: string; readonly value: string }> [];
}

export interface DaggerEncounter {
  readonly name: string;
  readonly status: string;
  readonly objective: string;
}

export interface DaggerNotice {
  readonly id: string;
  readonly message: string;
  readonly kind: 'info' | 'warning' | 'success';
}

type RecordValue = Readonly<Record<string, unknown>>;
const EQUIPMENT_SLOTS = [
  'head', 'right-arm', 'chest-armor', 'left-arm', 'right-hand',
  'gloves', 'left-hand', 'legs-armor', 'feet',
] as const;

export function decodeDaggerUiProjection(value: unknown): DaggerUiProjection | null {
  const source = record(value);
  if (source === null) return null;
  const hud = decodeHud(source.hud);
  const inventory = decodeInventory(source.inventory);
  const character = decodeCharacter(source.character);
  if (hud === null || inventory === null || character === null) return null;
  return Object.freeze({
    hud,
    inventory,
    character,
    loot: decodeLoot(source.loot),
    encounter: decodeEncounter(source.encounter) ?? decodeEncounter(record(source.hud)?.encounter),
    notices: decodeNotices(source.notices).length > 0
      ? decodeNotices(source.notices)
      : decodeNotices(record(source.hud)?.notices),
  });
}

/**
 * The Product UI stream is deliberately split: every accepted envelope updates
 * only the slice it owns. This keeps rich Dagger readouts below the Runtime UI
 * bounds and avoids treating one UI projection as a second product-state API.
 */
export function emptyDaggerUiProjection(): DaggerUiProjection {
  return Object.freeze({
    hud: Object.freeze({ health: Object.freeze({ current: 0, maximum: 0 }), stamina: Object.freeze({ current: 0, maximum: 0 }), magicka: Object.freeze({ current: 0, maximum: 0 }), level: 0, experience: 0, experienceToNext: 0 }),
    inventory: Object.freeze({ equipmentRevision: '0', gridRevision: '0', capacity: Object.freeze([]), slots: Object.freeze([]), equipment: Object.freeze(EQUIPMENT_SLOTS.map((id) => Object.freeze({ id, item: null }))), receipt: null }),
    character: Object.freeze({ attributes: Object.freeze([]), skills: Object.freeze([]) }), loot: null, encounter: null, notices: Object.freeze([]),
  });
}

export function reduceDaggerUiStream(
  current: DaggerUiProjection,
  stream: string,
  value: unknown,
): DaggerUiProjection {
  const source = record(value);
  if (source === null) return current;
  switch (stream) {
    case 'dagger.hud': {
      const hud = decodeHud(source);
      return hud === null ? current : Object.freeze({ ...current, hud, encounter: decodeEncounter(source.encounter), notices: decodeNotices(source.notices) });
    }
    case 'dagger.inventory': {
      const inventory = decodeInventorySlice(source, current.inventory);
      return Object.freeze({ ...current, inventory });
    }
    case 'dagger.equipment': {
      const inventory = decodeEquipmentSlice(source, current.inventory);
      return Object.freeze({ ...current, inventory });
    }
    case 'dagger.loot': return Object.freeze({ ...current, loot: decodeLoot(source) });
    case 'dagger.character': {
      const character = decodeCharacter(source);
      return character === null ? current : Object.freeze({ ...current, character });
    }
    default: return current;
  }
}

function decodeHud(value: unknown): DaggerHud | null {
  const source = record(value);
  if (source === null) return null;
  const health = meter(source.health);
  const stamina = meter(source.stamina);
  const magicka = meter(source.magicka);
  if (health === null || stamina === null || magicka === null) return null;
  return Object.freeze({
    health, stamina, magicka,
    level: natural(source.level), experience: natural(source.experience), experienceToNext: natural(source.experienceToNext),
  });
}

function decodeInventory(value: unknown): DaggerInventory | null {
  const source = record(value);
  if (source === null) return null;
  const slots = array(source.slots).slice(0, 50).map(decodeInventorySlot).filter(isPresent);
  const capacity = array(source.capacity).slice(0, 8).map(decodeCapacity).filter(isPresent);
  const equipmentById = new Map(array(source.equipment).slice(0, EQUIPMENT_SLOTS.length)
    .map(decodeEquipmentSlot).filter(isPresent).map((slot) => [slot.id, slot]));
  const equipment = EQUIPMENT_SLOTS.map((id) => equipmentById.get(id) ?? Object.freeze({ id, item: null }));
  return Object.freeze({
    equipmentRevision: revision(source.equipmentRevision),
    gridRevision: revision(source.gridRevision),
    capacity: Object.freeze(capacity), slots: Object.freeze(slots), equipment: Object.freeze(equipment),
    receipt: decodeReceipt(source.receipt),
  });
}

function decodeInventorySlice(source: RecordValue, current: DaggerInventory): DaggerInventory {
  const slots = array(source.slots).slice(0, 50).map(decodeInventorySlot).filter(isPresent);
  const capacity = array(source.capacity).slice(0, 8).map(decodeCapacity).filter(isPresent);
  return Object.freeze({ ...current, gridRevision: revision(source.gridRevision), slots: Object.freeze(slots), capacity: Object.freeze(capacity) });
}

function decodeEquipmentSlice(source: RecordValue, current: DaggerInventory): DaggerInventory {
  const equipmentById = new Map(array(source.equipment).slice(0, EQUIPMENT_SLOTS.length)
    .map(decodeEquipmentSlot).filter(isPresent).map((slot) => [slot.id, slot]));
  const equipment = EQUIPMENT_SLOTS.map((id) => equipmentById.get(id) ?? Object.freeze({ id, item: null }));
  return Object.freeze({ ...current, equipmentRevision: revision(source.equipmentRevision), equipment: Object.freeze(equipment), receipt: decodeReceipt(source.receipt) });
}

function decodeInventorySlot(value: unknown): InventorySlot | null {
  const source = record(value);
  if (source === null) return null;
  return Object.freeze({ index: natural(source.index), item: decodeItem(source.item) });
}

function decodeEquipmentSlot(value: unknown): EquipmentSlot | null {
  const source = record(value);
  if (source === null) return null;
  const id = shortText(source.id);
  return id === null ? null : Object.freeze({ id, item: decodeItem(source.item) });
}

function decodeCapacity(value: unknown): Capacity | null {
  const source = record(value);
  if (source === null) return null;
  const label = shortText(source.label);
  return label === null ? null : Object.freeze({ label, used: finite(source.used), maximum: source.maximum === null ? null : finite(source.maximum) });
}

function decodeItem(value: unknown): DaggerItem | null {
  const source = record(value);
  if (source === null) return null;
  const id = shortText(source.id);
  if (id === null) return null;
  const compatibleSlots = array(source.compatibleSlots).map(shortText).filter(isPresent).slice(0, EQUIPMENT_SLOTS.length);
  return Object.freeze({
    entity: source.entity === null || source.entity === undefined ? null : revision(source.entity),
    id, quantity: Math.max(1, natural(source.quantity)), compatibleSlots: Object.freeze(compatibleSlots),
    detail: source.detail === null || source.detail === undefined ? null : shortText(source.detail),
  });
}

function decodeLoot(value: unknown): DaggerLoot | null {
  const source = record(value);
  if (source === null) return null;
  const containerId = shortText(source.containerId);
  if (containerId === null) return null;
  return Object.freeze({
    containerId, revision: revision(source.revision), items: Object.freeze(array(source.items).slice(0, 64).map(decodeItem).filter(isPresent)),
    message: source.message === null || source.message === undefined ? null : shortText(source.message),
  });
}

function decodeReceipt(value: unknown): DaggerReceipt | null {
  const source = record(value);
  if (source === null) return null;
  const message = shortText(source.message);
  return message === null ? null : Object.freeze({ accepted: source.accepted === true, message });
}

function decodeCharacter(value: unknown): DaggerCharacter | null {
  const source = record(value);
  if (source === null) return null;
  return Object.freeze({ attributes: pairs(source.attributes), skills: pairs(source.skills) });
}

function decodeEncounter(value: unknown): DaggerEncounter | null {
  const source = record(value);
  if (source === null) return null;
  const name = shortText(source.name); const status = shortText(source.status); const objective = shortText(source.objective);
  return name === null || status === null || objective === null ? null : Object.freeze({ name, status, objective });
}

function decodeNotices(value: unknown): readonly DaggerNotice[] {
  return Object.freeze(array(value).slice(-7).map((candidate, index) => {
    const source = record(candidate);
    if (source === null) return null;
    const message = shortText(source.message);
    if (message === null) return null;
    const kind = source.kind === 'warning' || source.kind === 'success' ? source.kind : 'info';
    return Object.freeze({ id: source.id === undefined ? String(index) : revision(source.id), message, kind });
  }).filter(isPresent));
}

function pairs(value: unknown): readonly Readonly<{ readonly label: string; readonly value: string }>[] {
  return Object.freeze(array(value).slice(0, 32).map((candidate) => {
    const source = record(candidate);
    const label = source === null ? null : shortText(source.label);
    const entry = source === null ? null : shortText(source.value);
    return label === null || entry === null ? null : Object.freeze({ label, value: entry });
  }).filter(isPresent));
}

function meter(value: unknown): Meter | null {
  const source = record(value);
  return source === null ? null : Object.freeze({ current: finite(source.current), maximum: Math.max(0, finite(source.maximum)) });
}
function record(value: unknown): RecordValue | null { return typeof value === 'object' && value !== null && !Array.isArray(value) ? value as RecordValue : null; }
function array(value: unknown): readonly unknown[] { return Array.isArray(value) ? value : []; }
function finite(value: unknown): number { return typeof value === 'number' && Number.isFinite(value) ? value : 0; }
function natural(value: unknown): number { return Math.max(0, Math.floor(finite(value))); }
function revision(value: unknown): string {
  if (typeof value === 'string' && /^[0-9]{1,20}$/.test(value)) return value;
  return typeof value === 'number' && Number.isSafeInteger(value) && value >= 0 ? String(value) : '0';
}
function shortText(value: unknown): string | null { return typeof value === 'string' && value.length > 0 && value.length <= 160 ? value : null; }
function isPresent<T>(value: T | null): value is T { return value !== null; }
