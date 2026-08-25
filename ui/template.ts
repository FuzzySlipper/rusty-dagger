import type { DaggerItem, DaggerUiProjection, Meter } from './model.js';

export type DaggerUiMode = 'gameplay' | 'menu' | 'inventory' | 'loot' | 'character';

export interface DaggerUiViewState {
  readonly mode: DaggerUiMode;
  readonly selectedSlot: number | null;
  readonly selectedEquipment: string | null;
}

export function renderDaggerUi(projection: DaggerUiProjection | null, state: DaggerUiViewState): string {
  if (projection === null) return loadingShell();
  const overlay = state.mode === 'menu' ? menu() : state.mode === 'inventory' ? inventory(projection, state)
    : state.mode === 'loot' ? loot(projection) : state.mode === 'character' ? character(projection) : '';
  return `<section class="dagger-game-stage" aria-label="Privateer's Hold game viewport" ${overlay === '' ? '' : 'inert aria-hidden="true"'}>
    <p class="dagger-title" data-rusty-test="dagger-viewport-title">Privateer's Hold</p>
    ${projection.encounter === null ? '' : `<aside class="dagger-encounter" data-rusty-test="dagger-active-encounter"><span>${text(projection.encounter.name)} · ${text(projection.encounter.status)}</span><strong>${text(projection.encounter.objective)}</strong></aside>`}
    ${hud(projection)}
    <nav class="dagger-corner-actions" aria-label="Game panels" data-rusty-ui-interactive>
      <button type="button" data-dagger-action="open-menu" data-rusty-test="dagger-open-menu">Menu</button>
      <button type="button" data-dagger-action="open-inventory" data-rusty-test="dagger-open-inventory">Inventory</button>
      <button type="button" data-dagger-action="open-character" data-rusty-test="dagger-open-character">Character</button>
      <button type="button" data-dagger-action="open-loot" data-rusty-test="dagger-open-loot">Loot</button>
    </nav>
  </section>
  ${notices(projection)}${overlay}`;
}

function loadingShell(): string {
  return `<section class="dagger-game-stage" aria-label="Privateer's Hold game viewport"><p class="dagger-title" data-rusty-test="dagger-viewport-title">Privateer's Hold</p><p class="dagger-title" style="top:58px" role="status" data-rusty-test="dagger-projection-waiting">Waiting for Rust projection…</p></section>`;
}

function hud(projection: DaggerUiProjection): string {
  const { hud } = projection;
  return `<section class="dagger-hud" aria-label="Player status" data-rusty-test="dagger-hud">
    <div class="dagger-meters">${meter('Health', hud.health, 'health', 'dagger-health')}${meter('Stamina', hud.stamina, 'stamina', 'dagger-stamina')}${meter('Magicka', hud.magicka, 'magicka', 'dagger-magicka')}</div>
    <div class="dagger-progression" data-rusty-test="dagger-progression"><div><span>Level</span><strong>${hud.level}</strong></div><div><span>XP</span><strong>${hud.experience}</strong></div><div><span>XP to next</span><strong>${hud.experienceToNext}</strong></div></div>
  </section>`;
}

function meter(label: string, value: Meter, tone: string, hook: string): string {
  const maximum = value.maximum > 0 ? value.maximum : 0;
  const current = maximum === 0 ? 0 : Math.min(maximum, Math.max(0, value.current));
  const percentage = maximum === 0 ? 0 : (current / maximum) * 100;
  return `<div class="dagger-meter" data-rusty-test="${hook}"><label>${label}</label><span class="dagger-meter-track" role="progressbar" aria-label="${label}" aria-valuemin="0" aria-valuemax="${maximum}" aria-valuenow="${current}" aria-valuetext="${label} ${format(current)} of ${format(maximum)}"><span class="dagger-meter-fill ${tone}" style="width:${percentage.toFixed(2)}%"></span></span><strong>${format(current)} / ${format(maximum)}</strong></div>`;
}

function menu(): string {
  return dialog('Game menu', 'dagger-game-menu', `<div class="dagger-menu-actions" aria-label="Game menu actions">
    <button type="button" data-dagger-action="close" data-rusty-test="dagger-menu-return"><strong>Return to game</strong><small>Close</small></button>
    <button type="button" data-dagger-action="open-inventory" data-rusty-test="dagger-menu-inventory"><strong>Inventory</strong><small>I</small></button>
    <button type="button" data-dagger-action="open-character" data-rusty-test="dagger-menu-character"><strong>Character</strong><small>C</small></button>
    <button type="button" data-dagger-action="open-loot" data-rusty-test="dagger-menu-loot"><strong>Loot</strong><small>F</small></button>
  </div><div class="dagger-control-list" aria-label="Engine controls"><div><span>Move</span><strong>WASD</strong></div><div><span>Look</span><strong>Mouse</strong></div><div><span>Attack</span><strong>Space / click</strong></div><div><span>Panel</span><strong>UI controls</strong></div></div>`);
}

function inventory(projection: DaggerUiProjection, state: DaggerUiViewState): string {
  const selected = selectedItem(projection, state);
  const capacity = projection.inventory.capacity.map((entry) => `<div><span>${text(entry.label)}</span><strong>${format(entry.used)} / ${entry.maximum === null ? '—' : format(entry.maximum)}</strong></div>`).join('');
  const equipment = projection.inventory.equipment.map((slot) => {
    const selectedClass = state.selectedEquipment === slot.id ? ' ready' : '';
    return `<article class="dagger-equipment-slot${selectedClass}" data-rusty-test="dagger-equipment-${text(slot.id)}"><p>${text(slot.id)}</p>${slot.item === null ? `<span class="empty">Empty</span>` : `<button type="button" data-dagger-action="select-equipment" data-slot="${text(slot.id)}" aria-pressed="${state.selectedEquipment === slot.id}" data-rusty-test="dagger-equipped-${text(slot.id)}">${text(slot.item.id)}</button>`}${state.selectedSlot === null ? '' : `<button type="button" data-dagger-action="drop-on-equipment" data-slot="${text(slot.id)}" data-rusty-test="dagger-equip-selected-${text(slot.id)}">Equip selected</button>`}</article>`;
  }).join('');
  const slots = projection.inventory.slots.map((slot) => `<button type="button" class="${state.selectedSlot === slot.index ? 'selected' : ''}" data-dagger-action="select-slot" data-index="${slot.index}" aria-label="${slot.item === null ? `Empty inventory slot ${slot.index + 1}` : text(slot.item.id)}" aria-pressed="${state.selectedSlot === slot.index}" data-rusty-test="dagger-inventory-slot-${slot.index}">${slot.item === null ? '' : `${text(slot.item.id)} ×${slot.item.quantity}`}</button>`).join('');
  const receipt = projection.inventory.receipt === null ? '' : `<div class="dagger-receipt ${projection.inventory.receipt.accepted ? '' : 'rejected'}" aria-live="polite" data-rusty-test="dagger-inventory-receipt"><span>${projection.inventory.receipt.accepted ? 'Latest inventory receipt' : 'Inventory rejected'}</span><strong>${text(projection.inventory.receipt.message)}</strong></div>`;
  const unequip = state.selectedEquipment === null ? '' : `<button type="button" data-dagger-action="unequip" data-slot="${text(state.selectedEquipment)}" data-rusty-test="dagger-unequip-selected">Unequip selected</button>`;
  return dialog('Inventory', 'dagger-inventory-dialog', `<p class="dagger-help">Inventory placement and equipment remain Rust-authoritative. Select a carried item, then select an equipment slot or another grid slot.</p><div class="dagger-capacity" data-rusty-test="dagger-inventory-capacity">${capacity}</div><div class="dagger-inventory-layout"><section class="dagger-panel"><p class="dagger-kicker">Equipment</p><h3>Paper-doll slots</h3><div class="dagger-equipment">${equipment}</div></section><section class="dagger-panel"><p class="dagger-kicker">Carried</p><h3>Item grid</h3><div class="dagger-grid" role="listbox" aria-label="Carried inventory" data-rusty-test="dagger-inventory-grid">${slots}</div></section><aside class="dagger-panel dagger-detail" aria-live="polite" data-rusty-test="dagger-inventory-detail">${itemDetail(selected)}${unequip}</aside></div>${receipt}`);
}

function loot(projection: DaggerUiProjection): string {
  const current = projection.loot;
  if (current === null) return dialog('Loot', 'dagger-loot-dialog', `<p class="dagger-help" data-rusty-test="dagger-loot-unavailable">No opened loot container is in the current Rust projection.</p>`);
  const actions = current.items.map((item) => `<button type="button" data-dagger-action="take-loot" data-entity="${text(item.entity ?? '')}" data-item="${text(item.id)}" data-rusty-test="dagger-loot-take-${text(item.entity ?? item.id)}"><span>${text(item.id)} ×${item.quantity}</span><strong>Take</strong></button>`).join('') || '<p class="dagger-help">Empty. This container remains open until Exit.</p>';
  return dialog('Loot', 'dagger-loot-dialog', `<p class="dagger-help">${text(current.containerId)} · take operations are claims against the Rust-owned container revision.</p>${current.message === null ? '' : `<p class="dagger-receipt" role="status">${text(current.message)}</p>`}<div class="dagger-loot-list" data-rusty-test="dagger-loot-list">${actions}</div>`);
}

function character(projection: DaggerUiProjection): string {
  const attributes = statList(projection.character.attributes, 'dagger-character-attribute');
  const skills = statList(projection.character.skills, 'dagger-character-skill');
  return dialog('Character sheet', 'dagger-character-dialog', `<p class="dagger-help">Live Rust-authoritative profile. These values are observational.</p><div class="dagger-character-summary" data-rusty-test="dagger-character-summary"><div><span>Level</span><strong>${projection.hud.level}</strong></div><div><span>Total XP</span><strong>${projection.hud.experience}</strong></div><div><span>XP to next</span><strong>${projection.hud.experienceToNext}</strong></div></div><div class="dagger-character-columns"><section class="dagger-panel"><h3>Live condition</h3><dl class="dagger-stat-list">${statList([{ label: 'Health', value: `${format(projection.hud.health.current)} / ${format(projection.hud.health.maximum)}` }, { label: 'Stamina', value: `${format(projection.hud.stamina.current)} / ${format(projection.hud.stamina.maximum)}` }, { label: 'Magicka', value: `${format(projection.hud.magicka.current)} / ${format(projection.hud.magicka.maximum)}` }], 'dagger-character-vital')}</dl></section><section class="dagger-panel"><h3>Attributes</h3><dl class="dagger-stat-list">${attributes}</dl></section><section class="dagger-panel skills"><h3>Skills</h3><dl class="dagger-stat-list">${skills}</dl></section></div>`);
}

function dialog(title: string, hook: string, body: string): string {
  return `<section class="dagger-layer" data-rusty-ui-interactive data-rusty-test="${hook}-layer"><section class="dagger-dialog ${hook === 'dagger-game-menu' ? 'small' : ''}" role="dialog" aria-modal="true" aria-labelledby="${hook}-title" data-rusty-test="${hook}"><header><div><p class="dagger-kicker">Privateer's Hold</p><h2 id="${hook}-title">${title}</h2></div><button type="button" data-dagger-action="close" aria-label="Close ${title}" data-rusty-test="${hook}-close">Exit ×</button></header>${body}</section></section>`;
}

function notices(projection: DaggerUiProjection): string { return projection.notices.length === 0 ? '' : `<section class="dagger-notices" aria-live="polite" role="status" data-rusty-test="dagger-notices">${projection.notices.map((notice) => `<p class="dagger-notice ${notice.kind}" data-rusty-test="dagger-notice-${text(notice.id)}">${text(notice.message)}</p>`).join('')}</section>`; }
function selectedItem(projection: DaggerUiProjection, state: DaggerUiViewState): DaggerItem | null { return projection.inventory.slots.find((slot) => slot.index === state.selectedSlot)?.item ?? projection.inventory.equipment.find((slot) => slot.id === state.selectedEquipment)?.item ?? null; }
function itemDetail(item: DaggerItem | null): string { return item === null ? '<p>Select a carried or equipped item to inspect it.</p>' : `<p class="dagger-kicker">Selected item</p><h3>${text(item.id)}</h3><dl><div><dt>Quantity</dt><dd>${item.quantity}</dd></div><div><dt>Slots</dt><dd>${item.compatibleSlots.length === 0 ? 'Not equippable' : text(item.compatibleSlots.join(', '))}</dd></div>${item.detail === null ? '' : `<div><dt>Detail</dt><dd>${text(item.detail)}</dd></div>`}</dl>`; }
function statList(entries: readonly Readonly<{ readonly label: string; readonly value: string }>[], hook: string): string { return entries.map((entry, index) => `<div data-rusty-test="${hook}-${index}"><dt>${text(entry.label)}</dt><dd>${text(entry.value)}</dd></div>`).join('') || '<div><dt>State</dt><dd>Not projected</dd></div>'; }
function format(value: number): string { return Number.isInteger(value) ? String(value) : value.toFixed(2); }
function text(value: string): string { return value.replace(/[&<>'"]/g, (character) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', "'": '&#39;', '"': '&quot;' })[character] ?? character); }
