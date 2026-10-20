import assert from 'node:assert/strict';
import { after, test } from 'node:test';
import { mkdtemp, readFile, readdir, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { pathToFileURL } from 'node:url';
import ts from 'typescript';
import { JSDOM } from 'jsdom';

// Execute the real UI modules in a DOM. The unrelated Engine debug panel is the
// only replaced module; no HUD rendering or action logic is duplicated here.
const output = await mkdtemp(join(tmpdir(), 'dagger-ui-tests-'));
await writeFile(join(output, 'package.json'), '{"type":"module"}');
for (const file of await readdir(new URL('../../src/ui/', import.meta.url))) {
  if (!file.endsWith('.ts') || file.endsWith('.d.ts')) continue;
  const source = await readFile(new URL(`../../src/ui/${file}`, import.meta.url), 'utf8');
  const compiled = ts.transpileModule(source, {
    compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ES2022 },
  }).outputText.replaceAll("'@rusty-engine/live-debug'", "'./debug-stub.js'");
  await writeFile(join(output, file.replace(/\.ts$/, '.js')), compiled);
}
await writeFile(join(output, 'debug-stub.js'), 'export async function mountLiveDebugPanel() { return { dispose() {} }; }');
const { mountProductUi } = await import(pathToFileURL(join(output, 'main.js')));
const art = await import(pathToFileURL(join(output, 'art.js')));
const skin = await import(pathToFileURL(join(output, 'skin.js')));
const uiScale = await import(pathToFileURL(join(output, 'scale.js')));

/** A PNG data URL whose header states `width` x `height`: enough for the skin, which reads only the header. */
function pngHeader(width, height) {
  const bytes = Buffer.alloc(33);
  Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]).copy(bytes, 0);
  bytes.writeUInt32BE(13, 8); bytes.write('IHDR', 12, 'latin1');
  bytes.writeUInt32BE(width, 16); bytes.writeUInt32BE(height, 20);
  return `data:image/png;base64,${bytes.toString('base64')}`;
}
after(() => rm(output, { recursive: true, force: true }));

function fixture() {
  const dom = new JSDOM('<!doctype html><div id="root"></div>', { url: 'http://localhost/' });
  for (const key of ['window', 'document', 'Element', 'HTMLElement', 'HTMLDialogElement', 'HTMLSelectElement', 'HTMLInputElement', 'Event', 'KeyboardEvent', 'MouseEvent', 'Option', 'MutationObserver']) {
    globalThis[key] = dom.window[key];
  }
  // JSDOM lays nothing out, so art sized to its box has no box to observe.
  globalThis.ResizeObserver = class { observe() {} disconnect() {} };
  dom.window.HTMLDialogElement.prototype.showModal = function () { this.open = true; };
  dom.window.HTMLDialogElement.prototype.close = function () { this.open = false; };
  // The desktop window cancels these at once, so game UI never asks through them.
  for (const name of ['alert', 'confirm', 'prompt']) dom.window[name] = () => { throw new Error(`window.${name} is not game UI`); };
  const root = document.getElementById('root');
  const actions = [];const modes=[];const focuses=[];const scales=[];
  let uiScale = 1;
  let receive;
  let observe = null;
  const mounted = mountProductUi(root, {
    // A controller with no device: the test speaks the Engine's interface observations directly.
    input: { subscribe(observer) { observe = observer; return () => { observe = null; }; } },
    ui: { setInteractionMode(mode) {modes.push(mode);}, focusGameplay() {focuses.push(modes.at(-1));},
      scale: () => uiScale, setScale(value) { uiScale = value; scales.push(value); } },
    projection: { subscribe(callback) { receive = callback; return () => {}; } },
    intents: { claim(_intent, value) { actions.push(value.data); } },
  });
  // Answer the drawn confirmation, which must be the one open question.
  const answer = (accept) => {
    const dialog = root.querySelector('.dagger-confirm');
    assert.equal(dialog?.open, true, 'A drawn confirmation is open.');
    dialog.querySelector(accept ? '.dagger-confirm-accept' : '.dagger-confirm-cancel').click();
    assert.equal(dialog.open, false);
  };
  return {
    root, actions, modes, focuses, scales, answer,
    pad(button) {
      observe?.({ context: 'interface', fact: { kind: 'controller-button', button, edge: 'pressed' } });
      observe?.({ context: 'interface', fact: { kind: 'controller-button', button, edge: 'released' } });
    },
    stick(axis, value) { observe?.({ context: 'interface', fact: { kind: 'controller-axis', axis, value } }); },
    publish(value = {}) {
      receive({ contract: 'dagger.ui.snapshot.v1', value: {
        resources: [], lastOutcome: '', messages: [], mode: 'playing',
        composition: { bundle: 'test', ruleset: 'test', contentPacks: [], tuning: 'test' },
        ...value,
      } });
    },
    receive(value) { receive({ contract: 'dagger.ui.snapshot.v1', value }); },
    dispose() { mounted.dispose(); dom.window.close(); },
  };
}

test('death projection renders semantic choices, selected save slots, and suppresses the menu', () => {
  const f = fixture();
  try {
    const death = (selected = null) => ({
      active: true, screen: 'screen.death', revision: '4', message: 'You have died.', controlsSuppressed: true,
      cameraEffect: 'fall', fadeEffect: 'to-black', audioCue: 'player-death', selected,
      choices: [
        { action: 'death-new-game', id: 'new-game', label: 'New game', available: selected === null },
        { action: 'death-load-game', id: 'load-game', label: 'Load game', available: selected === null },
        { action: 'death-quit', id: 'quit-to-title', label: 'Quit to title', available: selected === null },
      ],
    });
    f.publish({ mode: 'dead', death: death(), saveSlots: {
      entries: [{ key: 'slot-1', label: 'Before the dungeon', savedAtUtc: '2026-09-23T00:00:00Z', ruleset: 'test' }], diagnostic: null,
    } });
    const root = f.root.querySelector('.dagger-death');
    assert.equal(root.hidden, false);
    assert.equal(f.root.querySelector('.dagger-menu-toggle').hidden, true);
    // The ruleset's message is the screen's one heading; it is not repeated beneath itself.
    assert.equal(f.root.querySelector('#dagger-death-title').textContent, 'You have died.');
    assert.equal([...root.querySelectorAll('*')].filter(node => node.children.length === 0 && node.textContent === 'You have died.').length, 1);
    assert.equal(f.root.querySelector('.dagger-death-load-slot').options.length, 2);

    const select = f.root.querySelector('.dagger-death-load-slot');
    select.value = 'slot-1';
    select.dispatchEvent(new window.Event('change', { bubbles: true }));
    f.root.querySelector('.dagger-death-load-button').click();
    assert.deepEqual(f.actions.at(-1), { action: 'death-load-game', key: 'slot-1' });

    f.publish({ mode: 'dead', death: death('load-game') });
    assert.equal(f.root.querySelector('.dagger-death-new').disabled, true);
    assert.equal(f.root.querySelector('.dagger-death-quit').disabled, true);
    f.publish({ mode: 'playing', death: { ...death(), active: false, controlsSuppressed: false, cameraEffect: 'none', fadeEffect: 'none', audioCue: 'none' } });
    assert.equal(root.hidden, true);
  } finally { f.dispose(); }
});

test('death closes retained dialogue and exposes a real fade consumer', () => {
  const f = fixture();
  try {
    const dialogue = {
      revision: 'dialogue-4', targetLabel: 'A guard', greeting: 'Halt.', tone: 'normal',
      question: 'Who goes there?', reply: null, topics: [{ id: 'name', label: 'Ask name' }], diagnostics: [],
    };
    f.publish({ mode: 'playing', activation: { mode: 'talk', message: '', applied: true, dialogue } });
    const dialogueWindow = f.root.querySelector('.dagger-dialogue');
    assert.equal(dialogueWindow.open, true);
    const actionsBeforeDeath = f.actions.length;

    f.publish({ mode: 'dead', activation: { mode: 'talk', message: '', applied: true, dialogue }, death: {
      active: true, screen: 'screen.death', revision: '5', message: 'You have died.', controlsSuppressed: true,
      cameraEffect: 'fall', fadeEffect: 'to-black', audioCue: 'player-death', selected: null,
      choices: [{ action: 'death-new-game', id: 'new-game', label: 'New game', available: true }],
    } });

    assert.equal(dialogueWindow.open, false);
    assert.equal(f.root.querySelector('.dagger-death-fade').getAttribute('aria-hidden'), 'true');
    assert.equal(f.root.querySelector('.dagger-death').dataset.fadeEffect, 'to-black');
    f.root.querySelector('.dagger-dialogue-close').click();
    assert.equal(f.actions.length, actionsBeforeDeath, 'A retained dialogue close cannot cross the death gate.');
  } finally { f.dispose(); }
});

test('loot offers only its panel Exit, which closes the projected container', () => {
  const f = fixture();
  try {
    assert.equal(f.root.querySelector('.dagger-focus-close'), null, 'no unstyled duplicate close control is drawn over the HUD');
    const loot = (container) => ({ container, revision: 'r1', title: 'Corpse', empty: false, message: '', items: [] });
    f.publish({ mode: 'modal', loot: loot('first') });
    f.publish({ mode: 'modal', loot: loot('second') });
    const exit = f.root.querySelector('[data-action="loot-exit"]');
    assert.equal(exit.hidden, false);
    const before = f.actions.length;
    exit.click();
    assert.deepEqual(f.actions.slice(before).filter(action => action.action === 'loot-close'), [{ action: 'loot-close', container: 'second' }]);
  } finally { f.dispose(); }
});

test('quest message text keeps its line breaks but not the spaces classic text used to centre a line', async () => {
  const css = await readFile(new URL('../../src/ui/styles.css', import.meta.url), 'utf8');
  const rule = /\.dagger-quest-message p \{([^}]*)\}/.exec(css)?.[1];
  assert.ok(rule, 'the quest message paragraph rule was not found');
  assert.match(rule, /white-space:\s*pre-line/);
});

test('the death screen stacks above quest messages and the legal screen', async () => {
  const css = await readFile(new URL('../../src/ui/styles.css', import.meta.url), 'utf8');
  const layer = selector => Number(new RegExp(`${selector.replace(/[.()[\]:]/g, '\\$&')} \\{[^}]*z-index:\\s*(\\d+)`).exec(css)?.[1]);
  const death = layer('.dagger-death');
  assert.ok(death > layer('.dagger-quests:not(:empty)'), 'quest messages cover the death screen');
  assert.ok(death > layer('.dagger-legal:not([hidden])'), 'the legal screen covers the death screen');
});

test('status rows preserve owner-published order and disappear when removed', () => {
  const f = fixture();
  try {
    f.publish({ slots: [
      { owner: 'quest', id: 'later', label: 'Quest', detail: 'Return', order: 99 },
      { owner: 'effect', id: 'early', label: 'Effect', detail: 'Shield', order: 1 },
      { owner: 'quest', id: 'next', label: 'Quest', detail: 'Speak', order: 0 },
    ] });
    assert.deepEqual([...f.root.querySelector('.dagger-status').children].map(x => x.textContent),
      ['Quest: Return', 'Effect: Shield', 'Quest: Speak']);
    f.publish({ slots: [] });
    assert.equal(f.root.querySelector('.dagger-status').children.length, 0);
  } finally { f.dispose(); }
});

test('transport projection exposes land selection and admitted ship boarding', () => {
  const f = fixture();
  try {
    f.publish({ transport: {
      mode: 'foot', onShip: false, canRun: true, summary: 'Overland journeys take the ordinary time on foot. You can run.', travelModifier: 256, oceanMinutesPerMapPixel: 255,
      options: [
        { id: 'foot', mode: 'foot', available: true, selected: true, label: 'Foot', message: 'Walk.', travelModifier: 256 },
        { id: 'horse', mode: 'horse', available: true, selected: false, label: 'Horse', message: 'Ride.', travelModifier: 128 },
        { id: 'cart', mode: 'cart', available: false, selected: false, label: 'Cart', message: 'No cart.', travelModifier: 192 },
        { id: 'ship', mode: 'ship', available: true, selected: false, label: 'Ship', message: 'Board.', travelModifier: 256 },
      ],
      wagon: { exists: false, accessible: false, id: null, usedClassicUnits: 0, capacityClassicUnits: 300000, storeRevision: null, message: 'Unavailable.', items: [], refusedDefinitions: [] },
    } });
    f.root.querySelector('[data-action="transport"]').click();
    const horse = f.root.querySelector('[data-transport-mode="horse"]');
    horse.click();
    assert.deepEqual(f.actions.at(-1), { action: 'transport-select', mode: 'horse' });
    f.root.querySelector('[data-action="transport-toggle"]').click();
    assert.deepEqual(f.actions.at(-1), { action: 'transport-toggle' });
    const ship = f.root.querySelector('[data-transport-mode="ship"]');
    assert.equal(ship.disabled, false);
    ship.click();
    assert.equal(f.actions.at(-1)?.action, 'transport-board-ship');
  } finally { f.dispose(); }
});

test('rest panel sends semantic durations bounded by the ruleset and shows the elapsed outcome', () => {
  const f = fixture();
  try {
    const rest = { hasResult: false, revision: '0', mode: null, requestedSeconds: 0, elapsedSeconds: 0, recoveryHours: 0, healthRecovered: 0,
      fatigueRecovered: 0, spellPointsRecovered: 0, interruption: 'None', message: null, maximumRestHours: 99, maximumLoiterHours: 3 };
    f.publish({ rest });
    f.root.querySelector('[data-action="rest"]:not([data-rest-mode])').click();
    const panel = f.root.querySelector('.dagger-rest-root');
    assert.equal(panel.hidden, false);
    const hours = panel.querySelector('.dagger-rest-hours');
    assert.equal(hours.max, '99');
    hours.value = '2'; hours.dispatchEvent(new window.Event('change', { bubbles: true }));
    panel.querySelector('[data-rest-mode="timed"]').click();
    assert.deepEqual(f.actions.at(-1), { action: 'rest', mode: 'timed', hours: 2 });
    // Loiter has its own stepper, bounded by the ruleset's maximum: 12 hours is brought back to 3.
    const loiter = panel.querySelector('.dagger-loiter-hours');
    assert.equal(loiter.max, '3');
    loiter.value = '12'; loiter.dispatchEvent(new window.Event('change', { bubbles: true }));
    assert.equal(loiter.value, '3');
    assert.equal(panel.querySelector('[data-rest-amount="loiter"] [data-stepper="up"]').disabled, true, 'The stepper stops at the bound.');
    panel.querySelector('[data-rest-mode="loiter"]').click();
    assert.deepEqual(f.actions.at(-1), { action: 'rest', mode: 'loiter', hours: 3 });
    // A later projection keeps the player's choice while it still fits.
    f.publish({ rest: { ...rest, revision: '0b' } });
    assert.equal(hours.value, '2');
    panel.querySelector('[data-rest-mode="until-healed"]').click();
    assert.deepEqual(f.actions.at(-1), { action: 'rest', mode: 'until-healed' });
    f.publish({ rest: { ...rest, hasResult: true, revision: '1', mode: 'Timed', requestedSeconds: 7200,
      elapsedSeconds: 3600, recoveryHours: 1, healthRecovered: 3, fatigueRecovered: 8, spellPointsRecovered: 2, interruption: 'Encounter',
      message: 'Your rest was interrupted by an encounter.' } });
    assert.equal(panel.querySelector('.dagger-rest-status').textContent,
      'Your rest was interrupted by an encounter.');
  } finally { f.dispose(); }
});

test('accessible wagon projection sends revision guarded put and take selections', () => {
  const f = fixture();
  try {
    f.publish({
      inventory: {
        revision: '21:2:0', message: 'Inventory ready.', equipmentChange: null,
        items: [{ key: 'stack:pack.gold', definition: 'gold-piece', label: 'Gold piece', quantity: '4', weight: 0, value: 1, details: 'Coins', icon: null, condition: null, identified: true, gridSlot: 0, equippedSlots: [], compatibleSlots: [] }],
        slots: [],
      },
      transport: {
        mode: 'cart', onShip: false, canRun: true, summary: 'Overland journeys take the ordinary time on foot. You can run.', travelModifier: 192, oceanMinutesPerMapPixel: 255,
        options: [{ id: 'cart', mode: 'cart', available: true, selected: true, label: 'Cart', message: 'Cart.', travelModifier: 192 }],
        wagon: {
          exists: true, accessible: true, id: 7, usedClassicUnits: 12, capacityClassicUnits: 300000, storeRevision: '8', message: 'Available.',
          items: [{ key: 'stack:daggerfall.wagon.7.gold', definition: 'gold-piece', label: 'Gold piece', quantity: '2' }],
          refusedDefinitions: [],
        },
      },
    });
    f.root.querySelector('[data-action="transport"]').click();
    f.root.querySelector('[data-action="wagon-put"]').click();
    assert.deepEqual(f.actions.at(-1), { action: 'wagon-put', revision: '21:2:0', item: 'stack:pack.gold', amount: 4 });
    f.root.querySelector('[data-action="wagon-take"]').click();
    assert.deepEqual(f.actions.at(-1), { action: 'wagon-take', revision: '21:2:0', item: 'stack:daggerfall.wagon.7.gold', amount: 2 });
  } finally { f.dispose(); }
});

test('a drawn tooltip goes away when the slot it named is filled', async () => {
  const f = fixture();
  try {
    const empty = { revision: '1', message: '', equipmentChange: null, items: [], slots: [{ id: 'right-hand', label: 'Right hand', itemKey: null, group: 'weapons' }] };
    f.publish({ inventory: empty });
    f.root.querySelector('[data-action="inventory"]').click();
    const slot = f.root.querySelector('[data-inventory-equipment="right-hand"]');
    slot.dispatchEvent(new window.Event('pointerover', { bubbles: true }));
    const tip = [...document.querySelectorAll('.dagger-tooltip')].find(element => !element.hidden);
    assert.equal(tip?.textContent, 'Right hand: empty');
    f.publish({ inventory: { ...empty, revision: '2', slots: [{ id: 'right-hand', label: 'Right hand', itemKey: 'unique:1', group: 'weapons' }],
      items: [{ key: 'unique:1', definition: 'sword', label: 'Sword', quantity: '1', weight: 1, value: 1, details: '', icon: null, condition: null,
        identified: true, gridSlot: null, equippedSlots: ['right-hand'], compatibleSlots: ['right-hand'] }] } });
    await new Promise(resolve => setTimeout(resolve, 0));
    assert.equal(tip.hidden, true, 'The filled slot no longer has a tooltip of its own, so the drawn one hides.');
  } finally { f.dispose(); }
});

test('an item tooltip shows published labels and values without internal template identities', () => {
  const f = fixture();
  try {
    f.publish({ inventory: {
      revision: '1', message: '', equipmentChange: null, slots: [],
      items: [{ key: 'unique:7', definition: 'template-116-steel', label: 'Steel Broadsword', quantity: '1', weight: 12, weightKilograms: 3, value: 25,
        details: 'A steel blade.', icon: null, condition: { current: 30, maximum: 60, percentage: 50, broken: false }, identified: false,
        gridSlot: 0, equippedSlots: [], compatibleSlots: ['right-hand'], canEquip: false, equipRefusal: 'Your class cannot use long blade weapons.',
        stats: [{ label: 'Damage', text: '2–16', score: 9 }],
        comparison: { item: 'unique:3', label: 'Iron Dagger', slot: 'Right hand', stats: [{ label: 'Damage', text: '1–6', score: 3.5 }] } }],
    } });
    const item = f.root.querySelector('[data-inventory-item="unique:7"]');
    item.dispatchEvent(new window.Event('focusin', { bubbles: true }));
    const tip = f.root.querySelector('.dagger-inventory-tip');
    assert.equal(tip.hidden, false, 'Focus shows the tooltip, as hovering does.');
    assert.match(tip.textContent, /Steel Broadsword/);
    assert.match(tip.textContent, /Unidentified/);
    assert.match(tip.textContent, /Weight3 kg/);
    assert.match(tip.textContent, /Value25 gold/);
    assert.match(tip.textContent, /Condition 30 \/ 60/);
    assert.match(tip.textContent, /Damage2–16/);
    assert.match(tip.textContent, /Worn \(Right hand\): Iron Dagger/);
    assert.equal(tip.querySelector('.dagger-inventory-tip-comparison .is-better').textContent, 'Damage 1–6 · this is better');
    assert.match(tip.textContent, /Your class cannot use long blade weapons\./);
    assert.doesNotMatch(tip.textContent, /template-|unique:7/);
    assert.equal(f.root.querySelector('.dagger-inventory-details'), null, 'There is no details panel; the tooltip is the item view.');
    // A new projection redraws a shown tooltip from the new row, and hides it when the item has gone.
    f.publish({ inventory: { revision: '2', message: '', equipmentChange: null, slots: [],
      items: [{ key: 'unique:7', definition: 'template-116-steel', label: 'Steel Broadsword', quantity: '1', weight: 12, weightKilograms: 3, value: 40,
        details: 'A steel blade.', icon: null, condition: null, identified: true, gridSlot: 0, equippedSlots: [], compatibleSlots: [] }] } });
    assert.equal(tip.hidden, false);
    assert.match(tip.textContent, /Value40 gold/);
    f.publish({ inventory: { revision: '3', message: '', equipmentChange: null, slots: [], items: [] } });
    assert.equal(tip.hidden, true);
  } finally { f.dispose(); }
});

test('inventory renders the ruleset-owned completed equip cue without claiming a readiness gate', () => {
  const f = fixture();
  try {
    f.publish({ inventory: {
      revision: '4:1', message: 'Inventory updated.', items: [], slots: [],
      equipmentChange: { cue: 'equip', rightHandDelayMilliseconds: 5700, leftHandDelayMilliseconds: 4500 },
    } });
    assert.equal(f.root.querySelector('.dagger-inventory-status')?.textContent,
      'Inventory updated. Equipped.');
  } finally { f.dispose(); }
});

test('quick save and quick load send the product actions over the Host quick-save slot', () => {
  const f = fixture();
  try {
    f.publish({});
    f.root.querySelector('.dagger-menu-toggle').click();
    f.root.querySelector('[data-action="quick-save"]').click();
    assert.deepEqual(f.actions.at(-1), { action: 'save-game' });
    f.root.querySelector('[data-action="quick-load"]').click();
    assert.deepEqual(f.actions.at(-1), { action: 'load-game' });
    const menu = f.root.querySelector('.dagger-menu');
    assert.equal(menu.open, true);
    f.publish({ mode: 'playing' });
    assert.equal(menu.open, false);
  } finally { f.dispose(); }
});

test('the action bar offers what the ruleset allows for the item it opened on, and says why not', () => {
  const f = fixture();
  try {
    const row = (key, overrides) => ({ key, definition: key, label: key, quantity: '1', weight: 1, value: 1, details: '', icon: null,
      condition: null, identified: true, gridSlot: 0, equippedSlots: [], compatibleSlots: [], ...overrides });
    f.publish({ inventory: {
      revision: '5:1', message: '', equipmentChange: null,
      slots: [{ id: 'right-hand', label: 'Right hand', itemKey: null }, { id: 'head', label: 'Head', itemKey: null }],
      items: [
        row('unique:1', { gridSlot: 0, compatibleSlots: ['right-hand'], canUse: false, canEquip: false, equipRefusal: 'Your class cannot use long blade weapons.',
          dropRefusal: 'You cannot drop a quest item.' }),
        row('stack:potion', { gridSlot: 1, canUse: true, canEquip: false, stackable: true, quantity: '5', equipRefusal: 'This item cannot be equipped.' }),
      ],
    } });
    const bar = f.root.querySelector('.dagger-inventory-actions-bar');
    const command = name => bar.querySelector(`[data-inventory-action="${name}"]`);
    // Enter (a click no pointer made) opens the bar above the pack.
    assert.equal(bar.hidden, true);
    f.root.querySelector('[data-inventory-item="unique:1"]').click();
    assert.equal(bar.hidden, false);
    assert.equal(command('use').disabled, true);
    assert.equal(command('equip').disabled, true);
    assert.equal(command('drop').disabled, true);
    assert.equal(bar.querySelector('.dagger-inventory-actions-note').textContent,
      'Your class cannot use long blade weapons. You cannot drop a quest item.');
    command('cancel').click();
    assert.equal(bar.hidden, true);
    f.root.querySelector('[data-inventory-item="stack:potion"]').click();
    assert.equal(command('use').disabled, false);
    // A stack drops a chosen part of itself, bounded by what the stack holds.
    const quantity = bar.querySelector('.dagger-stepper input');
    assert.equal(bar.querySelector('.dagger-stepper').hidden, false);
    assert.equal(quantity.max, '5');
    assert.equal(quantity.value, '5');
    bar.querySelector('[data-stepper="up"]').click();
    assert.equal(quantity.value, '5', 'The stepper stops at the published bound.');
    bar.querySelector('[data-stepper="down"]').click();
    bar.querySelector('[data-stepper="down"]').click();
    command('drop').click();
    assert.deepEqual(f.actions.at(-1), { action: 'inventory-drop', revision: '5:1', item: 'stack:potion', amount: 3 });
    f.root.querySelector('[data-inventory-item="stack:potion"]').click();
    command('use').click();
    assert.deepEqual(f.actions.at(-1), { action: 'inventory-use', revision: '5:1', item: 'stack:potion' });
    assert.equal(bar.hidden, true, 'A command closes the bar.');
  } finally { f.dispose(); }
});

test('inventory items drag with the pointer under capture, never a native drag session, and every cancel sends nothing', () => {
  const f = fixture();
  try {
    const row = (key, overrides) => ({ key, definition: key, label: key, quantity: '1', weight: 1, value: 1, details: '', icon: null,
      condition: null, identified: true, gridSlot: 0, equippedSlots: [], compatibleSlots: [], ...overrides });
    f.publish({ inventory: {
      revision: '6:1', message: '', equipmentChange: null,
      slots: [{ id: 'right-hand', label: 'Right hand', itemKey: null }, { id: 'head', label: 'Head', itemKey: null }],
      items: [row('unique:1', { gridSlot: 0, compatibleSlots: ['right-hand'], label: 'Steel sword' })],
    } });
    f.root.querySelector('[data-action="inventory"]').click();
    // A native drag hangs a host that renders the page off screen, so nothing in the panel offers one.
    assert.ok([...f.root.querySelectorAll('.dagger-inventory [draggable]')].every(element => element.draggable === false));
    const item = f.root.querySelector('[data-inventory-item="unique:1"]');
    const hand = f.root.querySelector('[data-inventory-equipment="right-hand"]');
    const head = f.root.querySelector('[data-inventory-equipment="head"]');
    let under = hand;
    document.elementFromPoint = () => under;
    // Under pointer capture every event of the press goes to the item that was pressed.
    const pointer = (type, x, y) => item.dispatchEvent(new window.MouseEvent(type, { bubbles: true, cancelable: true, button: 0, clientX: x, clientY: y }));
    const count = f.actions.length;

    // A press that travels no further than the threshold is not a drag.
    pointer('pointerdown', 10, 10);
    pointer('pointermove', 14, 13);
    assert.equal(f.root.querySelector('.dagger-drag-ghost'), null);
    pointer('pointerup', 14, 13);

    // Escape mid-drag releases the item and leaves the menu open.
    pointer('pointerdown', 10, 10);
    pointer('pointermove', 40, 40);
    const ghost = f.root.querySelector('.dagger-menu .dagger-drag-ghost');
    assert.ok(ghost, 'The ghost draws inside the open menu, above everything outside it.');
    assert.equal(ghost.textContent, 'Steel sword', 'An item with no picture drags its name.');
    assert.equal(hand.classList.contains('is-drop-target'), true);
    assert.equal(hand.classList.contains('is-drop-valid'), true);
    assert.equal(head.classList.contains('is-drop-invalid'), true, 'Published eligibility marks the slots that do not take it.');
    document.dispatchEvent(new KeyboardEvent('keydown', { code: 'Escape', bubbles: true, cancelable: true }));
    assert.equal(f.root.querySelector('.dagger-menu').open, true);
    pointer('pointerup', 40, 40);
    assert.equal(f.actions.length, count, 'An Escaped drag moves nothing.');
    assert.equal(hand.classList.contains('is-drop-target'), false);

    // Losing capture or window focus cancels too.
    pointer('pointerdown', 10, 10);
    pointer('pointermove', 40, 40);
    item.dispatchEvent(new window.Event('lostpointercapture'));
    pointer('pointerup', 40, 40);
    pointer('pointerdown', 10, 10);
    pointer('pointermove', 40, 40);
    window.dispatchEvent(new window.Event('blur'));
    pointer('pointerup', 40, 40);
    assert.equal(f.actions.length, count);

    // A drop on a slot that does not take the item sends nothing and says why.
    under = head;
    pointer('pointerdown', 10, 10);
    pointer('pointermove', 40, 40);
    pointer('pointerup', 40, 40);
    assert.equal(f.actions.length, count);
    assert.match(f.root.querySelector('.dagger-inventory-status').textContent, /Head does not take Steel sword/);

    // A drop on a slot that takes it sends exactly one move, and the release is not also a click.
    under = hand;
    pointer('pointerdown', 10, 10);
    pointer('pointermove', 40, 40);
    pointer('pointerup', 40, 40);
    item.click();
    assert.equal(f.actions.length, count + 1);
    assert.deepEqual(f.actions.at(-1), { action: 'inventory-move', revision: '6:1', item: 'unique:1', targetEquipment: 'right-hand' });
  } finally { delete document.elementFromPoint; f.dispose(); }
});

test('without a pointer an item is picked up and placed: Move holds it, accept on a slot places it, Back releases it', () => {
  const f = fixture();
  try {
    const row = (key, overrides) => ({ key, definition: key, label: key, quantity: '1', weight: 1, value: 1, details: '', icon: null,
      condition: null, identified: true, gridSlot: 0, equippedSlots: [], compatibleSlots: [], ...overrides });
    f.publish({ inventory: {
      revision: '7:1', message: '', equipmentChange: null,
      slots: [{ id: 'right-hand', label: 'Right hand', itemKey: null }, { id: 'head', label: 'Head', itemKey: null }],
      items: [row('unique:1', { gridSlot: 0, compatibleSlots: ['right-hand'] })],
    } });
    f.root.querySelector('[data-action="inventory"]').click();
    const item = f.root.querySelector('[data-inventory-item="unique:1"]');
    const hand = f.root.querySelector('[data-inventory-equipment="right-hand"]');
    const head = f.root.querySelector('[data-inventory-equipment="head"]');
    const empty = f.root.querySelector('[data-inventory-grid="4"]');
    const bar = f.root.querySelector('.dagger-inventory-actions-bar');
    const count = f.actions.length;
    const pickUp = () => {
      item.focus();
      f.pad('button-0');
      assert.equal(bar.hidden, false, 'Accept on an item opens its actions.');
      assert.equal(document.activeElement.closest('.dagger-inventory-actions-bar'), bar, 'Focus moves into the bar.');
      bar.querySelector('[data-inventory-action="move"]').click();
      assert.equal(bar.hidden, true);
    };
    pickUp();
    assert.equal(f.root.querySelector('.dagger-inventory').classList.contains('is-holding'), true);
    assert.match(f.root.querySelector('.dagger-inventory-status').textContent, /Holding unique:1: choose a slot to place it, or go back to release it/);
    assert.equal(hand.classList.contains('is-drop-valid'), true);
    // Back releases the held item before it would step out of the panel.
    f.pad('button-1');
    assert.equal(f.root.querySelector('.dagger-inventory').classList.contains('is-holding'), false);
    assert.equal(f.root.querySelector('.dagger-inventory-root').hidden, false);
    pickUp();
    head.focus();
    f.pad('button-0');
    assert.equal(f.actions.length, count, 'A slot that does not take the item receives nothing.');
    empty.focus();
    f.pad('button-0');
    assert.deepEqual(f.actions.at(-1), { action: 'inventory-move', revision: '7:1', item: 'unique:1', targetGrid: 4 });
    // Back on the open bar closes it, and only it.
    item.focus();
    f.pad('button-0');
    f.pad('button-1');
    assert.equal(bar.hidden, true);
    assert.equal(f.root.querySelector('.dagger-inventory-root').hidden, false);
    // A mouse click (one a pointer pressed) still picks up for pick and place.
    item.dispatchEvent(new window.MouseEvent('pointerdown', { bubbles: true, button: 0 }));
    item.dispatchEvent(new window.MouseEvent('pointerup', { bubbles: true, button: 0 }));
    item.click();
    assert.equal(bar.hidden, true);
    hand.click();
    assert.deepEqual(f.actions.at(-1), { action: 'inventory-move', revision: '7:1', item: 'unique:1', targetEquipment: 'right-hand' });
  } finally { f.dispose(); }
});

test('a double-click, a right-click and the controller X open the same action bar over the pack', () => {
  const f = fixture();
  try {
    const row = (key, overrides) => ({ key, definition: key, label: key, quantity: '1', weight: 1, value: 1, details: '', icon: null,
      condition: null, identified: true, gridSlot: 0, equippedSlots: [], compatibleSlots: [], ...overrides });
    f.publish({ inventory: {
      revision: '8:1', message: '', equipmentChange: null,
      slots: [{ id: 'right-hand', label: 'Right hand', itemKey: 'unique:2', group: 'weapons' }, { id: 'left-hand', label: 'Left hand', itemKey: null, group: 'weapons' }],
      items: [
        row('unique:1', { gridSlot: 0, compatibleSlots: ['right-hand', 'left-hand'], canEquip: true }),
        row('unique:2', { gridSlot: null, equippedSlots: ['right-hand'], compatibleSlots: ['right-hand'] }),
        row('stack:potion', { gridSlot: 1, canUse: true }),
      ],
    } });
    f.root.querySelector('[data-action="inventory"]').click();
    const bar = f.root.querySelector('.dagger-inventory-actions-bar');
    const visible = () => [...bar.querySelectorAll('button[data-inventory-action]')].filter(button => !button.hidden).map(button => button.textContent);
    const sword = f.root.querySelector('.dagger-inventory-grid [data-inventory-item="unique:1"]');
    // The bar sits directly above the pack grid.
    assert.equal(f.root.querySelector('.dagger-inventory-actions').nextElementSibling, f.root.querySelector('.dagger-inventory-pack'));
    // Right-click opens it; the window shows no native menu.
    const opened = new window.MouseEvent('contextmenu', { bubbles: true, cancelable: true, clientX: 30, clientY: 40 });
    sword.dispatchEvent(opened);
    assert.equal(opened.defaultPrevented, true);
    assert.equal(bar.hidden, false);
    assert.deepEqual(visible(), ['Equip', 'Use', 'Move', 'Drop', 'Cancel']);
    assert.equal(document.activeElement, bar.querySelector('[data-inventory-action="equip"]'), 'Focus moves into the bar.');
    // Escape closes it and leaves the menu open, with focus back on the item.
    document.dispatchEvent(new KeyboardEvent('keydown', { code: 'Escape', bubbles: true, cancelable: true }));
    assert.equal(bar.hidden, true);
    assert.equal(f.root.querySelector('.dagger-menu').open, true);
    assert.equal(document.activeElement, sword);
    // The controller's X opens it on the focused item; Equip prefers the free compatible slot.
    f.pad('button-2');
    assert.equal(bar.hidden, false);
    bar.querySelector('[data-inventory-action="equip"]').click();
    assert.deepEqual(f.actions.at(-1), { action: 'inventory-move', revision: '8:1', item: 'unique:1', targetEquipment: 'left-hand' });
    // A double-click opens it too: Use on the potion, Unequip on the worn sword to a free pack slot.
    f.root.querySelector('[data-inventory-item="stack:potion"]').dispatchEvent(new window.MouseEvent('dblclick', { bubbles: true }));
    bar.querySelector('[data-inventory-action="use"]').click();
    assert.deepEqual(f.actions.at(-1), { action: 'inventory-use', revision: '8:1', item: 'stack:potion' });
    f.root.querySelector('.dagger-inventory-equipment-section [data-inventory-item="unique:2"]').dispatchEvent(new window.MouseEvent('dblclick', { bubbles: true }));
    assert.deepEqual(visible(), ['Unequip', 'Use', 'Move', 'Drop', 'Cancel']);
    bar.querySelector('[data-inventory-action="unequip"]').click();
    assert.deepEqual(f.actions.at(-1), { action: 'inventory-move', revision: '8:1', item: 'unique:2', targetGrid: 2 });
    // Cancel sends nothing.
    const count = f.actions.length;
    sword.dispatchEvent(new window.MouseEvent('dblclick', { bubbles: true }));
    bar.querySelector('[data-inventory-action="cancel"]').click();
    assert.equal(f.actions.length, count);
    assert.equal(bar.hidden, true);
  } finally { f.dispose(); }
});

test('equipment lays out by published group around the doll, the pack filters by tab and sorts through the ruleset', () => {
  const f = fixture();
  try {
    const row = (key, overrides) => ({ key, definition: key, label: key, quantity: '1', weight: 1, value: 1, details: '', icon: null,
      condition: null, identified: true, gridSlot: 0, equippedSlots: [], compatibleSlots: [], ...overrides });
    art.adopt({ revision: 'doll', images: [{ id: 'character.background.breton', image: 'data:image/png;base64,AA==' }] });
    f.publish({
      inventory: {
        revision: '10:1', message: '', equipmentChange: null,
        categories: [{ id: 'weapons-armor', label: 'Weapons & armour' }, { id: 'ingredients', label: 'Ingredients' }],
        slots: [
          { id: 'head', label: 'Helm', itemKey: null, group: 'armour' },
          { id: 'cloak1', label: 'Cloak', itemKey: null, group: 'clothing' },
          { id: 'right-hand', label: 'Right hand', itemKey: null, group: 'weapons' },
          { id: 'ring0', label: 'Ring', itemKey: null, group: 'accessories' },
        ],
        items: [row('unique:1', { gridSlot: 0, category: 'weapons-armor' }), row('stack:herb', { gridSlot: 1, category: 'ingredients', stackable: true, quantity: '3' })],
        encumbrance: { currentClassicUnits: 4000, maximumClassicUnits: 20000, canMove: true, currentKilograms: 10, maximumKilograms: 50 },
        currency: { gold: '120', lettersOfCredit: '0', accountGold: '0' },
        bank: null,
      },
      character: { name: 'Aubk-i', attributes: [], skills: [], resources: [], progression: { level: 1, experience: 0 }, equipment: [],
        identity: { race: 'breton', raceLabel: 'Breton', donorRaceId: 1, portrait: '', gender: 'male', faceIndex: 0, career: 'Mage', media: [],
          selectedMedia: [{ layer: 'background', mediaId: 'character.background.breton' }] },
        paperDoll: { width: 110, height: 184, unplaced: [], layers: [],
          background: { kind: 'background', mediaId: 'character.background.breton', x: -8, y: -7, width: 125, height: 198, clipTop: null, clipBottom: null, mask: null, slot: null } } },
    });
    const slot = id => f.root.querySelector(`[data-inventory-equipment="${id}"]`);
    const columns = f.root.querySelectorAll('.dagger-inventory-slot-column');
    assert.equal(slot('head').parentElement, columns[0], 'Armour runs down one side of the doll.');
    assert.equal(slot('cloak1').parentElement, columns[1], 'Clothing and the hands run down the other.');
    assert.equal(slot('right-hand').classList.contains('starts-group'), true, 'The hands sit apart from the clothing.');
    assert.equal(slot('ring0').parentElement, f.root.querySelector('.dagger-inventory-accessories'));
    assert.equal(slot('head').dataset.tooltip, 'Helm: empty');
    const doll = f.root.querySelector('.dagger-inventory-doll-background img');
    assert.equal(doll.src, 'data:image/png;base64,AA==');
    assert.match(f.root.querySelector('.dagger-inventory-load').textContent, /Load 10 \/ 50 kg/);
    assert.equal(f.root.querySelector('.dagger-inventory-gold').textContent, '120 gold');
    assert.deepEqual([...f.root.querySelectorAll('[data-inventory-tab]')].map(tab => tab.textContent), ['All', 'Weapons & armour', 'Ingredients'],
      'No bank tab without a bank.');
    f.root.querySelector('[data-inventory-tab="ingredients"]').click();
    assert.equal(f.root.querySelector('[data-inventory-item="unique:1"]').classList.contains('is-filtered-out'), true);
    assert.equal(f.root.querySelector('[data-inventory-item="stack:herb"]').classList.contains('is-filtered-out'), false);
    assert.equal(f.root.querySelector('[data-inventory-item="stack:herb"] .dagger-inventory-quantity').textContent, '3');
    f.root.querySelector('[data-inventory-tab="all"]').click();
    assert.equal(f.root.querySelector('[data-inventory-item="unique:1"]').classList.contains('is-filtered-out'), false);
    f.root.querySelector('.dagger-inventory-sort').click();
    assert.deepEqual(f.actions.at(-1), { action: 'inventory-sort' });
  } finally { art.adopt({ revision: '', images: [] }); f.dispose(); }
});

test('the paper doll draws the ruleset\'s placed layers in order, clips the censor halves, masks beneath a helm, and takes worn art on demand', () => {
  const f = fixture();
  try {
    const layer = (kind, mediaId, x, y, width, height, extra = {}) => ({ kind, mediaId, x, y, width, height, clipTop: null, clipBottom: null, mask: null, slot: null, ...extra });
    const png = 'data:image/png;base64,AA==';
    const character = (layers) => ({ name: 'Aubk-i', attributes: [], skills: [], resources: [], progression: { level: 1, experience: 0 }, equipment: [],
      paperDoll: { width: 110, height: 184, unplaced: [], background: layer('background', 'bg', -8, -7, 125, 198), layers } });
    const bare = [
      layer('body', 'body.nude', 22, 33, 69, 145),
      layer('body', 'body.clothed', 22, 33, 69, 145, { clipTop: 0, clipBottom: 40 }),
      layer('body', 'body.clothed', 22, 33, 69, 145, { clipTop: 40, clipBottom: 145 }),
      layer('head', 'head', 46, 9, 20, 27),
    ];
    f.publish({ uiArtRevision: 'r1', uiArt: { revision: 'r1', images: [{ id: 'bg', image: png }, { id: 'head', image: png }] }, uiArtDemandedRevision: 'r1.0', inventory: { revision: '1', message: '', slots: [], items: [], categories: [] }, character: character(bare) });
    const box = () => f.root.querySelector('.dagger-inventory-doll');
    const background = box().querySelector('.dagger-inventory-doll-background img');
    assert.equal(background.style.left, 'calc(-8 * var(--doll-pixel))', 'The background crop is the projection\'s, not a CSS fit.');
    assert.equal(background.style.width, 'calc(125 * var(--doll-pixel))');
    assert.equal(box().style.width, 'calc(110 * var(--doll-pixel))');
    const drawn = () => [...box().querySelectorAll('.dagger-inventory-doll-layers img')].map(img => `${img.dataset.mediaId}${img.dataset.clip ? '@' + img.dataset.clip : ''}`);
    assert.deepEqual(drawn(), ['body.nude', 'body.clothed@0-40', 'body.clothed@40-145', 'head']);
    assert.equal(box().querySelector('[data-media-id="body.nude"]').dataset.artMissing, 'true', 'Art not yet delivered is held hidden.');
    assert.equal(box().querySelector('[data-clip="0-40"]').style.clipPath, 'inset(calc(0 * var(--doll-pixel)) 0 calc(105 * var(--doll-pixel)) 0)');
    assert.deepEqual(f.actions.at(-1), { action: 'art-request', revision: 'r1.0' }, 'A demanded block the DOM lacks is asked for by its revision.');

    // Equipping a helm and a sword: the doll changes, the helm's mask clears what was drawn beneath it, and
    // the worn art arrives as a demanded block on top of the held art.
    const helm = layer('item', 'paper-doll.251.27', 30, 4, 49, 37, { mask: 'paper-doll.251.27.mask', slot: 'head' });
    const sword = layer('item', 'paper-doll.234.12.iron', 33, 88, 47, 43, { slot: 'left-hand' });
    f.publish({ uiArtRevision: 'r1', uiArtDemandedRevision: 'r1.5', inventory: { revision: '2', message: '', slots: [], items: [], categories: [] },
      uiArtDemanded: { revision: 'r1.5', images: ['body.nude', 'body.clothed', helm.mediaId, helm.mask, sword.mediaId].map(id => ({ id, image: png })) },
      character: character([...bare, helm, sword]) });
    assert.deepEqual(drawn(), ['body.nude', 'body.clothed@0-40', 'body.clothed@40-145', 'head', 'paper-doll.251.27', 'paper-doll.234.12.iron']);
    const masked = box().querySelector('.dagger-inventory-doll-masked');
    assert.equal(masked.dataset.mask, 'paper-doll.251.27.mask');
    assert.deepEqual([...masked.querySelectorAll('img')].map(img => img.dataset.mediaId), ['body.nude', 'body.clothed', 'body.clothed', 'head'],
      'The mask covers every layer drawn before the helm and nothing after it.');
    assert.equal(masked.nextElementSibling.dataset.mediaId, 'paper-doll.251.27');
    assert.equal(box().querySelector('[data-media-id="paper-doll.234.12.iron"]').src, png);
    assert.equal(box().querySelector('[data-art-missing]'), null, 'Every demanded image is now held.');

    // A transformed lycanthrope's doll is its background alone.
    f.publish({ uiArtRevision: 'r1', uiArtDemandedRevision: 'r1.5', inventory: { revision: '3', message: '', slots: [], items: [], categories: [] }, character: character([]) });
    assert.deepEqual(drawn(), []);
  } finally { art.adopt({ revision: '', images: [] }); f.dispose(); }
});

test('the bank is its own tab, and each operation keeps to the amounts and eligibility the ruleset publishes', () => {
  const f = fixture();
  try {
    const action = (name, label, overrides) => ({ action: name, label, available: true, takesAmount: true, minimum: 1, maximum: 500, refusal: null, ...overrides });
    f.publish({ inventory: {
      revision: '11:1', message: '', equipmentChange: null, slots: [], items: [], categories: [],
      bank: { currentRegion: 17, currentRegionName: 'Daggerfall', currentBalance: '500', maximumNewLoan: '1000', loan: null,
        accounts: [{ region: 17, regionName: 'Daggerfall', gold: '500' }, { region: 20, regionName: 'Glenpoint', gold: '0' }],
        actions: [
          action('currency-deposit-gold', 'Deposit gold', { available: false, minimum: 0, maximum: 0, refusal: 'You carry no gold to deposit.' }),
          action('currency-withdraw-gold', 'Withdraw gold'),
          action('currency-deposit-letters', 'Deposit all letters of credit', { takesAmount: false, minimum: 0, maximum: 0 }),
          action('bank-transfer', 'Transfer to another region'),
        ] },
    } });
    const bankTab = f.root.querySelector('[data-inventory-tab="bank"]');
    assert.ok(bankTab);
    const bank = f.root.querySelector('.dagger-bank');
    assert.equal(bank.hidden, true);
    bankTab.click();
    assert.equal(bank.hidden, false);
    assert.equal(f.root.querySelector('.dagger-inventory-pack').hidden, true);
    assert.match(bank.textContent, /Daggerfall: 500 gold/);
    const row = name => bank.querySelector(`[data-bank-operation="${name}"]`);
    // A closed operation says why, and its own amount and button are disabled.
    assert.equal(row('currency-deposit-gold').querySelector('[data-bank-action]').disabled, true);
    assert.equal(row('currency-deposit-gold').querySelector('.dagger-bank-refusal').textContent, 'You carry no gold to deposit.');
    // Each operation has its own amount, bounded by the published limits.
    const withdraw = row('currency-withdraw-gold').querySelector('.dagger-stepper input');
    assert.equal(withdraw.max, '500');
    withdraw.value = '900';
    withdraw.dispatchEvent(new window.Event('change', { bubbles: true }));
    assert.equal(withdraw.value, '500', 'An amount past the published bound is brought back to it.');
    row('currency-withdraw-gold').querySelector('[data-bank-action]').click();
    assert.deepEqual(f.actions.at(-1), { action: 'currency-withdraw-gold', amount: 500 });
    const transfer = row('bank-transfer').querySelector('.dagger-stepper input');
    transfer.value = '50';
    transfer.dispatchEvent(new window.Event('change', { bubbles: true }));
    assert.deepEqual([...row('bank-transfer').querySelectorAll('option')].map(option => option.textContent), ['Glenpoint']);
    row('bank-transfer').querySelector('[data-bank-action]').click();
    assert.deepEqual(f.actions.at(-1), { action: 'bank-transfer', amount: 50, destination: 20 });
    assert.equal(row('currency-deposit-letters').querySelector('.dagger-stepper'), null, 'An operation without an amount has no stepper.');
    row('currency-deposit-letters').querySelector('[data-bank-action]').click();
    assert.deepEqual(f.actions.at(-1), { action: 'currency-deposit-letters' });
    // Leaving the bank takes its tab away and shows the pack again.
    f.publish({ inventory: { revision: '11:2', message: '', equipmentChange: null, slots: [], items: [], categories: [], bank: null } });
    assert.equal(f.root.querySelector('[data-inventory-tab="bank"]'), null);
    assert.equal(f.root.querySelector('.dagger-inventory-pack').hidden, false);
  } finally { f.dispose(); }
});

test('Escape toggles the menu from any panel, and a pointer lock lost in play opens it', () => {
  const f = fixture();
  try {
    f.publish({});
    const menu = f.root.querySelector('.dagger-menu');
    const escape = () => document.dispatchEvent(new KeyboardEvent('keydown', { code: 'Escape', bubbles: true, cancelable: true }));
    escape();
    assert.equal(menu.open, true);
    f.root.querySelector('[data-action="inventory"]').click();
    escape();
    assert.equal(menu.open, false, 'Escape closes the menu from inside a panel.');
    // Under pointer lock the browser spends Escape on releasing the lock; the release opens the menu.
    let locked = f.root;
    Object.defineProperty(document, 'pointerLockElement', { configurable: true, get: () => locked });
    document.dispatchEvent(new window.Event('pointerlockchange'));
    locked = null;
    document.dispatchEvent(new window.Event('pointerlockchange'));
    assert.equal(menu.open, true);
  } finally { delete document.pointerLockElement; f.dispose(); }
});

test('Escape backs out of the innermost surface and opens the menu only from play', () => {
  const f = fixture();
  try {
    const menu = f.root.querySelector('.dagger-menu');
    const escape = () => document.dispatchEvent(new KeyboardEvent('keydown', { code: 'Escape', bubbles: true, cancelable: true }));
    const empty = { deliveries: [], journal: { active: [], finished: [] }, pending: null };
    const dialogue = { revision: 'talk-3', targetLabel: 'A guard', greeting: 'Halt.', tone: 'normal', question: null, reply: null, topics: [], diagnostics: [] };

    // A conversation ends; the game menu does not open over it.
    f.publish({ activation: { mode: 'talk', message: '', applied: true, dialogue } });
    escape();
    assert.equal(menu.open, false);
    assert.deepEqual(f.actions.at(-1), { action: 'dialogue-close', revision: 'talk-3' });

    // A summoning offer keeps its provider's conversation open beneath it; Escape acts on neither.
    const summoning = { revision: 'talk-3', provider: 'Witch', quote: null, offerRevision: '7', prince: 'Azura', message: 'Bring the Ring.', diagnostics: [] };
    f.publish({ spells: { available: [], ready: null, result: '', summoning }, activation: { mode: 'talk', message: '', applied: true, dialogue } });
    const offer = f.root.querySelector('.dagger-daedric-offer');
    assert.equal(offer.open, true);
    const beforeOffer = f.actions.length;
    escape();
    assert.equal(f.actions.length, beforeOffer, 'Escape sends nothing while the offer needs an answer.');
    assert.equal(offer.open, true);
    assert.equal(f.root.querySelector('.dagger-dialogue').open, true);
    assert.equal(menu.open, false);
    f.publish({});

    // Surfaces that need an answer keep their own choices.
    const before = f.actions.length;
    const prompt = { instance: 'quest:1', message: 1010, delivery: 'prompt', text: 'Will you help?', signoff: null, diagnostics: [], promptId: 'prompt:1', options: [{ id: 3, label: 'Yes' }] };
    f.publish({ quests: { ...empty, deliveries: [prompt], pending: prompt } });
    escape();
    f.publish({ quests: empty, legal: { revision: 'case-1', phase: 'court', title: 'Court', message: 'Plead.', charges: ['Theft'], choices: [{ id: 'guilty', label: 'Guilty' }] } });
    escape();
    f.publish({ quests: empty, createItem: { revision: 'create-1', options: [{ id: 'sword', label: 'Sword' }] } });
    escape();
    assert.equal(menu.open, false, 'Escape leaves a required answer alone.');
    assert.equal(f.actions.length, before);

    // A spell's choice with a Cancel takes Escape as Cancel.
    f.publish({ identify: { revision: 'id-1', cost: 5, options: [{ id: 'ring', label: 'Ring' }] } });
    escape();
    assert.deepEqual(f.actions.at(-1), { action: 'identify-cancel', revision: 'id-1' });
    f.publish({ teleport: { revision: 'tp-1', anchorSet: false } });
    escape();
    assert.deepEqual(f.actions.at(-1), { action: 'teleport-select', revision: 'tp-1', key: 'cancel' });
    f.publish({});

    // The title screen has no game menu to open.
    f.publish({ mode: 'title' });
    escape();
    assert.equal(menu.open, false);

    // In play, Escape still opens and closes the menu.
    f.publish({});
    escape();
    assert.equal(menu.open, true);
    escape();
    assert.equal(menu.open, false);
  } finally { f.dispose(); }
});

test('a drawn confirmation answers Escape and death with no, and returns focus to what asked', () => {
  const f = fixture();
  try {
    const spells = { available: [{ key: 'spell.023', name: 'Heal', cost: 15 }], ready: null, result: '', sale: null, information: null };
    f.publish({ spells });
    const menu = f.root.querySelector('.dagger-menu');
    const escape = () => document.dispatchEvent(new KeyboardEvent('keydown', { code: 'Escape', bubbles: true, cancelable: true }));
    f.root.querySelector('[data-action="spells"]').click();
    const forget = f.root.querySelector('[data-action="spell-delete"]');
    forget.focus();
    forget.click();
    const confirm = f.root.querySelector('.dagger-confirm');
    assert.equal(confirm.open, true);
    assert.equal(confirm.querySelector('.dagger-confirm-accept').textContent, 'Forget');
    assert.equal(confirm.querySelector('p').textContent, 'Forget Heal?', 'The question names the spell.');
    assert.equal(document.activeElement, confirm.querySelector('.dagger-confirm-cancel'), 'Focus starts on Cancel.');
    escape();
    assert.equal(confirm.open, false);
    assert.equal(menu.open, true, 'Escape answered the question, not the menu.');
    assert.equal(document.activeElement, forget);
    assert.equal(f.actions.some(action => action.action === 'spell-delete'), false);

    forget.click();
    f.publish({ spells, mode: 'dead', death: null });
    assert.equal(confirm.open, false, 'Death answers an open question with no.');
    assert.equal(f.actions.some(action => action.action === 'spell-delete'), false);
  } finally { f.dispose(); }
});

test('the spell choices are centred dialogs, and game UI explains itself with drawn tooltips, never title', () => {
  const f = fixture();
  try {
    f.publish({ identify: { revision: 'id-1', cost: 5, options: [{ id: 'ring', label: 'Ring' }] } });
    const identify = f.root.querySelector('.dagger-identify');
    assert.equal(identify.classList.contains('dagger-spell-service'), true);
    assert.equal(identify.getAttribute('role'), 'dialog');
    assert.equal(identify.getAttribute('aria-label'), 'Identify');

    f.publish({ spells: { available: [{ key: 'spell.023', name: 'Heal', cost: 15 }], ready: null, result: '', sale: null, information: null } });
    f.root.querySelector('[data-action="spells"]').click();
    const info = f.root.querySelector('[data-action="spell-info"]');
    info.dispatchEvent(new window.Event('focusin', { bubbles: true }));
    const tip = f.root.querySelector('.dagger-tooltip');
    assert.equal(tip.hidden, false);
    assert.equal(tip.textContent, 'Describe Heal');
    assert.equal(tip.parentElement, f.root.querySelector('.dagger-menu'), 'The tooltip draws inside the open modal.');
    info.dispatchEvent(new window.Event('focusout', { bubbles: true }));
    assert.equal(tip.hidden, true);
    assert.equal(f.root.querySelector('[title]'), null, 'No title attribute carries information.');
  } finally { f.dispose(); }
});

test('a controller reaches the dialogue: directions move focus, accept presses, Back ends the conversation', () => {
  const f = fixture();
  try {
    const dialogue = { revision: 'talk-5', targetLabel: 'A smith', greeting: 'Well met.', tone: 'normal', question: null, reply: null,
      topics: [{ id: 'news', label: 'News' }, { id: 'work', label: 'Work' }], diagnostics: [] };
    f.publish({ activation: { mode: 'talk', message: '', applied: true, dialogue } });
    const window_ = f.root.querySelector('.dagger-dialogue');
    assert.ok(window_.contains(document.activeElement), 'Focus moves into the conversation when it opens.');
    const topics = [...f.root.querySelectorAll('.dagger-dialogue-topics button')];
    topics[0].focus();
    f.pad('button-13');
    assert.equal(document.activeElement, topics[1]);
    f.pad('button-0');
    assert.deepEqual(f.actions.at(-1), { action: 'dialogue-topic', revision: 'talk-5', topic: 'work' });
    // Hints name the controller's buttons once it is in use, inside the surface that has the cursor.
    const hints = window_.querySelector('.dagger-input-hints');
    assert.equal(hints.dataset.device, 'controller');
    assert.match(hints.textContent, /A Select · B Back/);
    // A held stick moves once until it is released, and losing window focus releases it.
    f.stick('axis-1', -1);
    assert.equal(document.activeElement, topics[0]);
    f.stick('axis-1', -1);
    assert.equal(document.activeElement, topics[0]);
    window.dispatchEvent(new window.Event('blur'));
    f.stick('axis-1', -1);
    assert.notEqual(document.activeElement, topics[0], 'After blur the held direction counts as a new press.');
    f.pad('button-1');
    assert.deepEqual(f.actions.at(-1), { action: 'dialogue-close', revision: 'talk-5' });
  } finally { f.dispose(); }
});

test('a controller changes a choice with left and right, so it can load from the death screen', () => {
  const f = fixture();
  try {
    const choices = ['death-new-game', 'death-load-game', 'death-quit'].map((action, index) => ({ action, id: String(index), label: action, available: true }));
    f.publish({ mode: 'dead', death: { active: true, screen: 'screen.death', revision: '1', message: 'Dead.', controlsSuppressed: true,
      cameraEffect: 'none', fadeEffect: 'none', audioCue: 'none', selected: null, choices },
      saveSlots: { entries: [{ key: 'slot-1', label: 'Before', savedAtUtc: '2026-09-23T00:00:00Z', ruleset: 'test' }], diagnostic: null } });
    const root = f.root.querySelector('.dagger-death');
    assert.ok(root.contains(document.activeElement), 'Focus moves into the death screen when it appears.');
    assert.doesNotMatch(root.querySelector('.dagger-input-hints').textContent, /Back/, 'A required answer offers no Back.');
    const select = f.root.querySelector('.dagger-death-load-slot');
    select.focus();
    f.pad('button-15');
    assert.equal(select.value, 'slot-1');
    f.pad('button-1');
    assert.equal(root.hidden, false, 'Back leaves a required answer alone.');
    f.root.querySelector('.dagger-death-load-button').focus();
    f.pad('button-0');
    assert.deepEqual(f.actions.at(-1), { action: 'death-load-game', key: 'slot-1' });
  } finally { f.dispose(); }
});

test('the menu steps panels on the shoulder buttons, and Back and Start step out one panel before closing it', () => {
  const f = fixture();
  try {
    f.publish({ rest: { hasResult: false, revision: '0', mode: null, requestedSeconds: 0, elapsedSeconds: 0, recoveryHours: 0, healthRecovered: 0, fatigueRecovered: 0, spellPointsRecovered: 0, interruption: 'None', message: null, maximumRestHours: 99, maximumLoiterHours: 3 } });
    const menu = f.root.querySelector('.dagger-menu');
    document.dispatchEvent(new KeyboardEvent('keydown', { code: 'Escape', bubbles: true, cancelable: true }));
    assert.equal(menu.open, true);
    f.pad('button-5');
    assert.equal(f.root.querySelector('.dagger-inventory-root').hidden, false);
    f.pad('button-5');
    assert.equal(f.root.querySelector('.dagger-character-root').hidden, false);
    f.pad('button-4');
    assert.equal(f.root.querySelector('.dagger-inventory-root').hidden, false);
    f.pad('button-1');
    assert.equal(f.root.querySelector('.dagger-menu-home').hidden, false);
    assert.equal(document.activeElement, f.root.querySelector('.dagger-menu-home [data-action="inventory"]'), 'Focus returns to the panel it left.');
    // A focused amount steps with left and right.
    f.root.querySelector('[data-action="rest"]').click();
    const hours = f.root.querySelector('.dagger-rest-hours');
    hours.focus();
    f.pad('button-15');
    assert.equal(hours.value, '2');
    // Start steps out of a panel as Back does, then closes the menu from its home.
    f.pad('button-9');
    assert.equal(menu.open, true);
    f.pad('button-9');
    assert.equal(menu.open, false);
  } finally { f.dispose(); }
});

test('a quest prompt takes focus and the pad answers it; Back does not dismiss it', () => {
  const f = fixture();
  try {
    const prompt = { instance: 'quest:1', message: 1010, delivery: 'prompt', text: 'Will you help?', signoff: null, diagnostics: [], promptId: 'prompt:1', options: [{ id: 3, label: 'Yes' }, { id: 4, label: 'No' }] };
    f.publish({ quests: { deliveries: [prompt], journal: { active: [], finished: [] }, pending: prompt } });
    const yes = f.root.querySelector('.dagger-quest-prompt button');
    assert.equal(document.activeElement, yes);
    const before = f.actions.length;
    f.pad('button-1');
    assert.equal(f.actions.length, before);
    f.pad('button-0');
    assert.deepEqual(f.actions.at(-1), { action: 'quest-choice', questInstance: 'quest:1', questMessage: 1010, questPrompt: 'prompt:1', questChoice: 3 });
  } finally { f.dispose(); }
});

test('a snapshot that arrives every update keeps the focused control, and a changed list keeps the player in place', () => {
  const f = fixture();
  try {
    const spell = (key, name) => ({ key, name, cost: 5 });
    const spells = available => ({ available, ready: null, result: '', sale: null, information: null });
    f.publish({ spells: spells([spell('spell.1', 'Heal'), spell('spell.2', 'Light')]) });
    f.root.querySelector('[data-action="spells"]').click();
    const info = f.root.querySelector('[data-action="spell-info"][data-spell="spell.2"]');
    info.focus();
    f.publish({ spells: spells([spell('spell.1', 'Heal'), spell('spell.2', 'Light')]) });
    assert.equal(document.activeElement, info, 'An unchanged snapshot leaves the control in place.');
    f.publish({ spells: spells([spell('spell.0', 'Cure'), spell('spell.1', 'Heal'), spell('spell.2', 'Light')]) });
    assert.equal(document.activeElement.dataset.spell, 'spell.2', 'A rebuilt list focuses the same spell again.');
    assert.equal(document.activeElement.dataset.action, 'spell-info');

    // The menu is modal; close it before talking, as play would.
    document.dispatchEvent(new KeyboardEvent('keydown', { code: 'Escape', bubbles: true, cancelable: true }));
    const dialogue = topics => ({ revision: 'talk-1', targetLabel: 'A smith', greeting: 'Hello.', tone: 'normal', question: null, reply: null, topics, diagnostics: [] });
    f.publish({ activation: { mode: 'talk', message: '', applied: true, dialogue: dialogue([{ id: 'news', label: 'News' }, { id: 'work', label: 'Work' }]) } });
    const work = f.root.querySelector('.dagger-dialogue-topics [data-topic="work"]');
    work.focus();
    f.publish({ activation: { mode: 'talk', message: '', applied: true, dialogue: dialogue([{ id: 'news', label: 'News' }, { id: 'work', label: 'Work' }]) } });
    assert.equal(document.activeElement, work);
  } finally { f.dispose(); }
});

test('dismissing one of several stacked messages leaves focus in the ones that remain', () => {
  const f = fixture();
  try {
    const popup = { instance: 'quest:1', message: 10, delivery: 'popup', heading: 'Message', text: 'You wake.', signoff: null,
      diagnostics: [], promptId: null, options: [], entryId: 'quest-message:1' };
    const prompt = { instance: 'quest:2', message: 1010, delivery: 'prompt', text: 'Tutorial?', signoff: null, diagnostics: [], promptId: 'prompt:1', options: [{ id: 3, label: 'Yes' }, { id: 4, label: 'No' }] };
    const empty = { journal: { active: [], finished: [] } };
    f.publish({ quests: { ...empty, deliveries: [popup, prompt], pending: prompt } });
    f.root.querySelector('.dagger-quest-popup button').click();
    f.publish({ quests: { ...empty, deliveries: [prompt], pending: prompt } });
    assert.ok(f.root.querySelector('.dagger-quests').contains(document.activeElement), 'Focus returns to the remaining prompt.');
  } finally { f.dispose(); }
});

test('reopening a menu panel puts focus back on the control it last held', () => {
  const f = fixture();
  try {
    f.publish({ rest: { hasResult: false, revision: '0', mode: null, requestedSeconds: 0, elapsedSeconds: 0, recoveryHours: 0, healthRecovered: 0, fatigueRecovered: 0, spellPointsRecovered: 0, interruption: 'None', message: null, maximumRestHours: 99, maximumLoiterHours: 3 } });
    document.dispatchEvent(new KeyboardEvent('keydown', { code: 'Escape', bubbles: true, cancelable: true }));
    f.root.querySelector('.dagger-menu-home [data-action="rest"]').click();
    const hours = f.root.querySelector('.dagger-rest-hours');
    hours.focus();
    f.pad('button-1');
    assert.equal(f.root.querySelector('.dagger-menu-home').hidden, false);
    f.root.querySelector('.dagger-menu-home [data-action="rest"]').click();
    assert.equal(document.activeElement, hours, 'Reopening Rest returns to the hours field.');
    // Switching away by the shoulder buttons and back does the same.
    f.pad('button-5');
    f.pad('button-4');
    assert.equal(document.activeElement, hours);
    // Leaving through the drawn Back button, focused and accepted, keeps the place too.
    const back = f.root.querySelector('.dagger-menu-panel > [data-action="back"]');
    back.focus();
    f.pad('button-0');
    assert.equal(f.root.querySelector('.dagger-menu-home').hidden, false);
    f.root.querySelector('.dagger-menu-home [data-action="rest"]').click();
    assert.equal(document.activeElement, hours, 'Back is never remembered as the panel position.');
  } finally { f.dispose(); }
});

test('transport controls keep focus through an unchanged snapshot, and a focus fallback never lands on a hidden control', () => {
  const f = fixture();
  try {
    const transport = { mode: 'foot', onShip: false, canRun: true, summary: 'On foot.', travelModifier: 256, oceanMinutesPerMapPixel: 255,
      options: [
        { id: 'foot', mode: 'foot', available: true, selected: true, label: 'Foot', message: 'Walk.', travelModifier: 256 },
        { id: 'horse', mode: 'horse', available: true, selected: false, label: 'Horse', message: 'Ride.', travelModifier: 128 },
      ],
      wagon: { exists: false, accessible: false, id: null, usedClassicUnits: 0, capacityClassicUnits: 300000, storeRevision: null, message: 'Unavailable.', items: [], refusedDefinitions: [] } };
    f.publish({ transport });
    document.dispatchEvent(new KeyboardEvent('keydown', { code: 'Escape', bubbles: true, cancelable: true }));
    f.root.querySelector('.dagger-menu-home [data-action="transport"]').click();
    const foot = f.root.querySelector('[data-transport-mode="foot"]');
    foot.focus();
    f.publish({ transport });
    assert.equal(document.activeElement, foot, 'An identical snapshot leaves the mode button in place.');
    f.pad('button-13');
    f.publish({ transport });
    f.pad('button-0');
    assert.deepEqual(f.actions.at(-1), { action: 'transport-select', mode: 'horse' }, 'A snapshot between direction and accept does not lose the place.');
    // A control removed under focus never sends focus to a hidden one.
    document.activeElement.remove();
    f.publish({ transport });
    assert.equal(document.activeElement.closest('[hidden]'), null);
  } finally { f.dispose(); }
});

test('book page controls send the notebook revision current when they are pressed', () => {
  const f = fixture();
  try {
    const book = { id: 59, title: 'A book', author: 'An author', page: 0, pageCount: 2, text: 'First page.' };
    f.publish({ notebook: { revision: '9', book, notes: [] } });
    f.root.querySelector('[data-action="journal"]').click();
    const next = f.root.querySelector('[data-testid="book-reader-next"]');
    next.focus();
    f.publish({ notebook: { revision: '10', book, notes: [{ id: 'note:1', text: 'Added.' }] } });
    f.root.querySelector('[data-testid="book-reader-next"]').click();
    assert.deepEqual(f.actions.at(-1), { action: 'notebook-page', revision: '10', page: 1 });
    assert.equal(document.activeElement.dataset.testid, 'book-reader-next', 'A rebuild keeps focus on Next.');
  } finally { f.dispose(); }
});

test('actions opened on a worn item from the Bank or Storage tab bring the pack back, so the bar is never invisible', () => {
  const f = fixture();
  try {
    const sword = { key: 'unique:2', definition: 'sword', label: 'Sword', quantity: '1', weight: 1, value: 1, details: '', icon: null, condition: null,
      identified: true, gridSlot: null, equippedSlots: ['right-hand'], compatibleSlots: ['right-hand'] };
    const storage = { key: 'house/1', revision: '3', items: [], storeRefusals: [] };
    f.publish({ inventory: { revision: '12:1', message: '', equipmentChange: null, categories: [], items: [sword],
      slots: [{ id: 'right-hand', label: 'Right hand', itemKey: 'unique:2', group: 'weapons' }],
      bank: { currentRegion: 17, currentRegionName: 'Daggerfall', currentBalance: '0', maximumNewLoan: '0', loan: null, accounts: [], actions: [] } },
      property: { bankAvailable: true, offers: [], storage } });
    f.root.querySelector('[data-action="inventory"]').click();
    const bar = f.root.querySelector('.dagger-inventory-actions-bar');
    const worn = f.root.querySelector('.dagger-inventory-equipment-section [data-inventory-item="unique:2"]');
    for (const tab of ['bank', 'storage']) {
      f.root.querySelector(`[data-inventory-tab="${tab}"]`).click();
      assert.equal(f.root.querySelector('.dagger-inventory-pack').hidden, true);
      worn.focus();
      worn.click();
      assert.equal(f.root.querySelector('[data-inventory-tab="all"]').getAttribute('aria-selected'), 'true', `From ${tab}, the pack comes back.`);
      assert.equal(f.root.querySelector('.dagger-inventory-actions').hidden, false);
      assert.equal(bar.closest('[hidden]'), null, 'The bar that takes navigation is on screen.');
      assert.equal(document.activeElement.closest('.dagger-inventory-actions-bar'), bar);
      f.pad('button-1');
      assert.equal(bar.hidden, true);
    }
    // A bar left open while its row is hidden would hold navigation; switching tabs closes it instead.
    worn.click();
    f.root.querySelector('[data-inventory-tab="bank"]').click();
    assert.equal(bar.hidden, true);
  } finally { f.dispose(); }
});

test('an inventory action bar closes with its panel, so navigation belongs to what is visible', () => {
  const f = fixture();
  try {
    const row = (key, overrides) => ({ key, definition: key, label: key, quantity: '1', weight: 1, value: 1, details: '', icon: null,
      condition: null, identified: true, gridSlot: 0, equippedSlots: [], compatibleSlots: [], ...overrides });
    f.publish({ inventory: { revision: '9:1', message: '', equipmentChange: null, slots: [], items: [row('stack:potion', { canUse: true })] } });
    document.dispatchEvent(new KeyboardEvent('keydown', { code: 'Escape', bubbles: true, cancelable: true }));
    f.root.querySelector('.dagger-menu-home [data-action="inventory"]').click();
    const bar = f.root.querySelector('.dagger-inventory-actions-bar');
    f.root.querySelector('[data-inventory-item="stack:potion"]').focus();
    f.pad('button-2');
    assert.equal(bar.hidden, false);
    f.pad('button-3');
    assert.equal(f.root.querySelector('.dagger-character-root').hidden, false);
    assert.equal(bar.hidden, true, 'Switching panel closes the action bar.');
    // Back now acts on the visible panel.
    f.pad('button-1');
    assert.equal(f.root.querySelector('.dagger-menu-home').hidden, false);
    // Closing the whole menu closes it too.
    f.root.querySelector('.dagger-menu-home [data-action="inventory"]').click();
    f.root.querySelector('[data-inventory-item="stack:potion"]').focus();
    f.pad('button-2');
    document.dispatchEvent(new KeyboardEvent('keydown', { code: 'Escape', bubbles: true, cancelable: true }));
    document.dispatchEvent(new KeyboardEvent('keydown', { code: 'Escape', bubbles: true, cancelable: true }));
    assert.equal(f.root.querySelector('.dagger-menu').open, false);
    assert.equal(bar.hidden, true);
  } finally { f.dispose(); }
});

test('book reader and notebook render product state and send revision-guarded semantic actions', () => {
  const f = fixture();
  try {
    const notebook = { revision: '9', book: { id: 59, title: 'A retained book', author: 'An author', page: 0, pageCount: 2, text: 'First page.' },
      notes: [{ id: 'note:1', text: 'Existing note.' }, { id: 'note:2', text: 'Another note.' }] };
    f.publish({ notebook });
    f.root.querySelector('[data-action="journal"]').click();
    assert.equal(f.root.querySelector('[data-testid="book-reader-title"]').textContent, 'A retained book');
    assert.equal(f.root.querySelector('[data-testid="book-reader-page"]').textContent, 'First page.');
    f.root.querySelector('[data-testid="book-reader-next"]').click();
    assert.deepEqual(f.actions.at(-1), { action: 'notebook-page', revision: '9', page: 1 });
    const add = f.root.querySelector('[aria-label="New notebook note"]');
    add.value = 'New note.';
    add.closest('form').dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true }));
    assert.deepEqual(f.actions.at(-1), { action: 'notebook-add', revision: '9', text: 'New note.' });
    const first = f.root.querySelector('[data-testid="notebook-note-note:1"]');
    first.querySelector('textarea').value = 'Edited note.';
    first.querySelector('button').click();
    assert.deepEqual(f.actions.at(-1), { action: 'notebook-edit', revision: '9', note: 'note:1', text: 'Edited note.' });
    first.querySelectorAll('button')[3].click();
    assert.deepEqual(f.actions.at(-1), { action: 'notebook-move', revision: '9', note: 'note:1', destination: 1 });
  } finally { f.dispose(); }
});

test('quest journal shows active quests grouped by title and deadline, and a finished-quests page', () => {
  const f = fixture();
  try {
    const entry = (instance, message, text) => ({ entryId: `quest-journal/${instance}/${message}`, instance, message, delivery: 'journal',
      heading: 'Journal', text, signoff: null, promptId: null, options: [], diagnostics: [] });
    const journal = {
      active: [
        { instance: 'timed', title: 'Timed errand', deadline: 'Due by Tirdas the 3rd of Hearthfire, 3E405 at 00:00 (2 days left)',
          entries: [entry('timed', 0, 'Bring the ring within 2 days.'), entry('timed', 1, 'The ring is in the crypt.')] },
        { instance: 'plain', title: 'Quest', deadline: null, entries: [entry('plain', 0, 'Speak to the smith.')] },
      ],
      finished: [{ instance: 'short', title: 'Short errand', succeeded: false, status: 'Ended on Fredas the 4th of Hearthfire, 3E405',
        entries: [entry('short', 0, 'The errand is done.')] }],
    };
    f.publish({ quests: { deliveries: [], journal, pending: null, escortFaces: [] } });
    f.root.querySelector('[data-action="journal"]').click();
    const page = f.root.querySelector('.dagger-quest-journal-page');
    const groups = [...page.querySelectorAll('.dagger-quest-journal-group')];
    assert.deepEqual(groups.map(group => group.querySelector('h3').textContent), ['Timed errand', 'Quest']);
    assert.equal(groups[0].querySelector('.dagger-quest-journal-deadline').textContent, journal.active[0].deadline);
    assert.equal(groups[1].querySelector('.dagger-quest-journal-deadline'), null);
    assert.deepEqual([...groups[0].querySelectorAll('li')].map(item => item.textContent),
      ['Bring the ring within 2 days.', 'The ring is in the crypt.']);
    // Journal entries belong to the journal panel, not the transient quest message stack.
    assert.equal(f.root.querySelector('.dagger-quests').textContent.includes('Speak to the smith.'), false);

    f.root.querySelector('[data-journal-page="finished"]').click();
    assert.equal(f.root.querySelector('[data-journal-page="finished"]').getAttribute('aria-selected'), 'true');
    const finished = page.querySelector('.dagger-quest-journal-group');
    assert.equal(finished.dataset.questInstance, 'short');
    assert.equal(finished.querySelector('h3').textContent, 'Short errand');
    assert.equal(finished.querySelector('.dagger-quest-journal-status').textContent, 'Ended on Fredas the 4th of Hearthfire, 3E405');
    assert.equal(finished.querySelector('li').textContent, 'The errand is done.');

    // The selected page survives a fresh projection; an empty page says so.
    f.publish({ quests: { deliveries: [], journal: { active: journal.active, finished: [] }, pending: null, escortFaces: [] } });
    assert.equal(page.querySelector('.dagger-quest-journal-empty').textContent, 'No finished quests.');
  } finally { f.dispose(); }
});

test('repeated notebook projections retain note drafts and the focused textarea', () => {
  const f = fixture();
  try {
    const notebook = { revision: '12', book: null, notes: [{ id: 'note:1', text: 'Published note.' }] };
    f.publish({ notebook });
    f.root.querySelector('[data-action="journal"]').click();
    const row = f.root.querySelector('[data-testid="notebook-note-note:1"]');
    const edit = row.querySelector('textarea');
    edit.focus();
    edit.value = 'Unsubmitted draft.';

    f.publish({ notebook });

    assert.equal(f.root.querySelector('[data-testid="notebook-note-note:1"] textarea'), edit);
    assert.equal(edit.value, 'Unsubmitted draft.');
    assert.equal(document.activeElement, edit);
  } finally { f.dispose(); }
});

test('mode screens follow projection and a mode without a backdrop hides both', () => {
  const f = fixture();
  try {
    const death = f.root.querySelector('.dagger-death');
    const title = f.root.querySelector('.dagger-entry');
    const art = { revision: 'screen-test', images: [
      { id: 'screen.title', image: 'data:image/png;base64,dGl0bGU=' },
      { id: 'screen.death', image: 'data:image/png;base64,ZGVhdGg=' },
    ] };
    f.publish({ mode: 'title', uiArt: art, uiArtRevision: art.revision });
    assert.equal(title.hidden, false);
    assert.equal(death.hidden, true);
    assert.equal(f.root.querySelector('.dagger-entry-screen').getAttribute('src'), art.images[0].image);
    f.publish({ mode: 'dead' });
    assert.equal(title.hidden, true);
    assert.equal(death.hidden, false);
    assert.equal(f.root.querySelector('.dagger-death-screen').getAttribute('src'), art.images[1].image);
    for (const mode of ['playing', 'modal']) {
      f.publish({ mode });
      assert.equal(title.hidden, true);
      assert.equal(death.hidden, true);
    }
  } finally { f.dispose(); }
});

test('view is cleared when the product no longer supplies it', () => {
  const f = fixture();
  try {
    f.publish({ view: { yawRadians: 0, pitchRadians: 0, interaction: 'aiming' } });
    assert.equal(f.root.querySelector('.dagger-view').textContent, 'Facing north · level');
    f.publish({ view: { yawRadians: Math.PI / 2, pitchRadians: 0.5, interaction: 'aiming' } });
    assert.equal(f.root.querySelector('.dagger-view').textContent, 'Facing east · looking up');
    f.publish();
    assert.equal(f.root.querySelector('.dagger-view').textContent, '');
  } finally { f.dispose(); }
});

test('quest messages dismiss by durable entry identity even when their text is identical', () => {
  const f = fixture();
  try {
    const message = { instance: 'quest:1', message: 10, delivery: 'popup', heading: 'Message', text: 'Same text.', signoff: null,
      diagnostics: ['%qdt has no context.'], promptId: null, options: [], entryId: 'quest-message:1' };
    f.publish({ quests: { deliveries: [message, { ...message, entryId: 'quest-message:2' }], journal: { active: [], finished: [] }, pending: null } });
    const buttons = f.root.querySelectorAll('.dagger-quest-popup button');
    assert.equal(buttons.length, 2);
    assert.equal(f.root.querySelector('.dagger-quest-popup strong').textContent, 'Message');
    assert.doesNotMatch(f.root.querySelector('.dagger-quest-popup').textContent, /has no context/);
    buttons[1].click();
    assert.deepEqual(f.actions.at(-1), { action: 'quest-dismiss', questInstance: 'quest:1', questDelivery: 'quest-message:2' });
    f.publish({ quests: { deliveries: [message], journal: { active: [], finished: [] }, pending: null } });
    assert.equal(f.root.querySelectorAll('.dagger-quest-popup').length, 1);
  } finally { f.dispose(); }
});

test('a projected quest prompt renders once and returns the selected semantic choice', () => {
  const f = fixture();
  try {
    const prompt = { instance: 'quest:1', message: 1010, delivery: 'prompt', text: 'Will you help?', signoff: null, diagnostics: [], promptId: 'prompt:1', options: [{ id: 3, label: 'Yes' }, { id: 4, label: 'No' }] };
    f.publish({ quests: { deliveries: [prompt], journal: { active: [], finished: [] }, pending: prompt } });
    assert.equal(f.root.querySelector('.dagger-quest-prompt p').textContent, 'Will you help?');
    f.root.querySelector('.dagger-quest-prompt button').click();
    assert.deepEqual(f.actions.at(-1), { action: 'quest-choice', questInstance: 'quest:1', questMessage: 1010, questPrompt: 'prompt:1', questChoice: 3 });
    f.publish({ quests: { deliveries: [], journal: { active: [], finished: [] }, pending: null } });
    assert.equal(f.root.querySelector('.dagger-quest-prompt'), null);
  } finally { f.dispose(); }
});

test('the title screen offers the games the product publishes and sends the one chosen', () => {
  const f = fixture();
  try {
    const select = () => f.root.querySelector('[data-testid="entry-bundle"]');
    f.publish({ mode: 'title', bundles: { current: 'daggerfall.classic', options: [
      { id: 'daggerfall.classic', title: 'Classic Daggerfall' }, { id: 'daggerfall.contact', title: 'Contact + mitigation' }] } });
    assert.equal(select().parentElement.hidden, false);
    assert.deepEqual([...select().options].map(option => [option.value, option.textContent]),
      [['daggerfall.classic', 'Classic Daggerfall'], ['daggerfall.contact', 'Contact + mitigation']]);
    assert.equal(select().value, 'daggerfall.classic');
    select().value = 'daggerfall.contact';
    select().dispatchEvent(new window.Event('change', { bubbles: true }));
    assert.deepEqual(f.actions.at(-1), { action: 'bundle-select', key: 'daggerfall.contact' });

    // One game is no choice, and a game that has begun offers none.
    f.publish({ mode: 'title', bundles: { current: 'daggerfall.classic', options: [{ id: 'daggerfall.classic', title: 'Classic Daggerfall' }] } });
    assert.equal(select().parentElement.hidden, true);
    f.publish({ mode: 'title', bundles: null });
    assert.equal(select().parentElement.hidden, true);
  } finally { f.dispose(); }
});

test('title character choices are projected and committed through semantic actions', () => {
  const f = fixture();
  try {
    f.publish({ mode: 'title', character: {
      name: 'Nameless', attributes: [], skills: [], resources: [], progression: { level: 1, experience: 0 }, equipment: [], grantedSkills: [],
      creationAvailable: true,
      creation: {
        editing: false, current: { name: 'Nameless', race: 'breton', gender: 'male', faceIndex: 0, reflexes: 2, career: 'class00' },
        races: [{ id: 'breton', label: 'Breton', available: true, restriction: null }],
        careers: [{ id: 'class00', label: 'Mage', available: true, restriction: null }],
        faces: [{ index: 0, mediaId: 'character.head.male.00.0' }], reflexes: [{ value: 2, label: 'Average' }],
      },
    } });
    f.root.querySelector('[data-testid="character-begin"]').click();
    assert.deepEqual(f.actions.at(-1), { action: 'character-begin' });
    f.publish({ mode: 'title', character: {
      name: 'Nameless', attributes: [], skills: [], resources: [], progression: { level: 1, experience: 0 }, equipment: [], grantedSkills: [],
      creationAvailable: true,
      creation: {
        editing: true, current: { name: 'Nameless', race: 'breton', gender: 'male', faceIndex: 0, reflexes: 2, career: 'class00' },
        races: [{ id: 'breton', label: 'Breton', available: true, restriction: null }], careers: [{ id: 'class00', label: 'Mage', available: true, restriction: null }],
        faces: [{ index: 0, mediaId: 'character.head.male.00.0' }], reflexes: [{ value: 2, label: 'Average' }],
        background: { biographyClassIndex: 0, biography: ['A readable biography.'], attributeBonusPool: 2, remainingAttributePoints: 0, primarySkillPoints: 0, majorSkillPoints: 0, minorSkillPoints: 0,
          questions: [], attributes: [{ id: 'strength', label: 'Strength', rolled: 50, allocated: 2, value: 52, maximum: 100, canAllocate: false }],
          skills: [], startingGrants: [], unsupportedEffects: [] },
      },
    } });
    // Every dropdown is labelled, and the controls refuse a commit until the draft is complete.
    for (const label of ['Name', 'Race', 'Gender', 'Face', 'Reflexes', 'Career']) assert.ok([...f.root.querySelectorAll('label')].some(item => item.firstChild?.textContent === label), label);
    const name = f.root.querySelector('[aria-label="Character name"]'); const commit = f.root.querySelector('[data-testid="character-commit"]');
    name.value = ''; name.dispatchEvent(new window.Event('input', { bubbles: true }));
    assert.equal(commit.disabled, true);
    assert.match(f.root.querySelector('[data-testid="character-commit-blocked"]').textContent, /Enter a name/);
    name.value = 'Aubk-i'; name.dispatchEvent(new window.Event('input', { bubbles: true }));
    assert.equal(commit.disabled, false);
    commit.click();
    assert.deepEqual(f.actions.at(-1), { action: 'character-commit', name: 'Aubk-i', race: 'breton', gender: 'male', faceIndex: 0, reflexes: 2, career: 'class00', backgroundAnswers: '', attributeAllocations: 'strength:2', skillAllocations: '' });
  } finally { f.dispose(); }
});

test('played characters do not expose character creation controls', () => {
  const f = fixture();
  try {
    f.publish({ mode: 'playing', character: {
      name: 'Aubk-i', attributes: [], skills: [], resources: [], progression: { level: 1, experience: 0 }, equipment: [], grantedSkills: [],
      creationAvailable: false,
      creation: {
        editing: false, current: { name: 'Aubk-i', race: 'breton', gender: 'male', faceIndex: 0, reflexes: 2, career: 'class00' },
        races: [{ id: 'breton', label: 'Breton', available: true, restriction: null }],
        careers: [{ id: 'class00', label: 'Mage', available: true, restriction: null }],
        faces: [{ index: 0, mediaId: 'character.head.male.00.0' }], reflexes: [{ value: 2, label: 'Average' }],
      },
    } });
    assert.equal(f.root.querySelector('[data-testid="character-begin"]'), null);
    assert.equal(f.root.querySelector('[data-testid="character-commit"]'), null);
  } finally { f.dispose(); }
});

test('title creation renders normalized questions and sends the selected background allocation', () => {
  const f = fixture();
  try {
    f.publish({ mode: 'title', character: {
      name: 'Nameless', attributes: [], skills: [], resources: [], progression: { level: 1, experience: 0 }, equipment: [], grantedSkills: [], creationAvailable: true,
      creation: { editing: true, current: { name: 'Nameless', race: 'breton', gender: 'male', faceIndex: 0, reflexes: 2, career: 'class00' },
        races: [{ id: 'breton', label: 'Breton', available: true, restriction: null }], careers: [{ id: 'class00', label: 'Mage', available: true, restriction: null }], faces: [{ index: 0, mediaId: 'character.head.male.00.0' }], reflexes: [{ value: 2, label: 'Average' }],
        background: { biographyClassIndex: 0, biography: ['A readable biography.'], attributeBonusPool: 6, remainingAttributePoints: 4, primarySkillPoints: 5, majorSkillPoints: 6, minorSkillPoints: 6,
          questions: [{ number: 1, text: 'Where did you study?', selectedLetter: 'a', answers: [{ letter: 'a', text: 'At home.' }, { letter: 'b', text: 'At court.' }] }],
          attributes: [{ id: 'strength', label: 'Strength', rolled: 50, allocated: 2, value: 52, maximum: 100, canAllocate: true }],
          skills: [{ id: 'medical', label: 'Medical', tier: 'primary', rolled: 28, allocated: 1, biographyBonus: 0, value: 29, canAllocate: true }],
          startingGrants: [{ itemId: 'template-113-iron', label: 'Longsword', templateIndex: 113, quantity: 1, sourceEffect: 'IT 3 0 0' }], unsupportedEffects: ['The source retains this fatigue background effect without a gameplay consequence.'] },
      },
    } });
    assert.match(f.root.querySelector('[data-testid="character-biography"]').textContent, /readable biography/);
    assert.equal(f.root.querySelector('[data-testid="character-starting-grants"]').textContent, 'Starting grants: 1 × Longsword.');
    assert.match(f.root.querySelector('[data-testid="character-background-unsupported-effects"]').textContent, /fatigue background effect/);
    const pools = f.root.querySelector('[data-testid="character-allocation-pools"]');
    const commit = f.root.querySelector('[data-testid="character-commit"]');
    const add = label => f.root.querySelector(`[aria-label="Add a point to ${label}"]`);
    const remove = label => f.root.querySelector(`[aria-label="Remove a point from ${label}"]`);
    assert.equal(pools.textContent, '4 of 6 bonus points left');
    assert.equal(commit.disabled, true);
    assert.match(f.root.querySelector('[data-testid="character-commit-blocked"]').textContent, /Spend 4 more bonus points\. Spend 5 more primary skill points\./);
    const actionsBeforeSteps = f.actions.length;
    for (let step = 0; step < 6; step++) add('Strength').click();
    // The pool holds four more points: the steps past it are refused by the control itself.
    assert.equal(f.root.querySelector('[aria-label="Attributes strength"]').textContent, '+6');
    assert.equal(add('Strength').disabled, true);
    assert.equal(pools.textContent, '0 of 6 bonus points left');
    remove('Strength').click(); add('Strength').click();
    for (let step = 0; step < 7; step++) add('Medical').click();
    assert.equal(f.root.querySelector('[aria-label="Skills medical"]').textContent, '+6');
    assert.equal(add('Medical').disabled, true);
    assert.match(f.root.querySelector('h4[data-tier="primary"]').textContent, /0 of 6 points left/);
    assert.equal(commit.disabled, true, 'The major and minor pools remain unspent.');
    assert.equal(f.actions.length, actionsBeforeSteps, 'Draft counters do not submit gameplay changes.');
    f.root.querySelector('[aria-label="Background question 1"]').value = 'b';
    f.root.querySelector('[aria-label="Background question 1"]').dispatchEvent(new window.Event('change', { bubbles: true }));
    assert.deepEqual(f.actions.at(-1), { action: 'character-update', name: 'Nameless', race: 'breton', gender: 'male', faceIndex: 0, reflexes: 2, career: 'class00', backgroundAnswers: '1:b', attributeAllocations: 'strength:6', skillAllocations: 'medical:6' });
    f.root.querySelector('[data-testid="character-background-reroll"]').click();
    assert.deepEqual(f.actions.at(-1), { action: 'character-background-reroll', name: 'Nameless', race: 'breton', gender: 'male', faceIndex: 0, reflexes: 2, career: 'class00', backgroundAnswers: '1:b', attributeAllocations: 'strength:6', skillAllocations: 'medical:6' });
  } finally { f.dispose(); }
});

test('quick start sends the draft identity and career without background fields, even with points unspent', () => {
  const f = fixture();
  try {
    f.publish({ mode: 'title', character: {
      name: 'Nameless', attributes: [], skills: [], resources: [], progression: { level: 1, experience: 0 }, equipment: [], grantedSkills: [], creationAvailable: true,
      creation: { editing: true, current: { name: '', race: 'breton', gender: 'female', faceIndex: 0, reflexes: 2, career: 'class00' },
        races: [{ id: 'breton', label: 'Breton', available: true, restriction: null }], careers: [{ id: 'class00', label: 'Mage', available: true, restriction: null }], faces: [{ index: 0, mediaId: 'character.head.female.00.0' }], reflexes: [{ value: 2, label: 'Average' }],
        background: null },
    } });
    const quickStart = f.root.querySelector('[data-testid="character-quick-start"]');
    assert.equal(quickStart.disabled, false);
    assert.equal(f.root.querySelector('[data-testid="character-commit"]').disabled, true, 'An ordinary commit still needs a name and a background.');
    quickStart.click();
    assert.deepEqual(f.actions.at(-1), { action: 'character-quick-start', name: '', race: 'breton', gender: 'female', faceIndex: 0, reflexes: 2, career: 'class00' });
  } finally { f.dispose(); }
});

test('loot sits beside the pack: Take moves a chosen part of a stack or a whole item, and Take All takes everything', () => {
  const f = fixture();
  try {
    const row = (key, overrides) => ({ key, definition: key, label: key, quantity: '1', weight: 1, value: 1, details: '', icon: null,
      condition: null, identified: true, gridSlot: null, equippedSlots: [], compatibleSlots: [], ...overrides });
    f.publish({ mode: 'modal',
      inventory: { revision: '1', message: '', equipmentChange: null, slots: [], items: [row('stack:gold', { label: 'Gold', quantity: '25', stackable: true, gridSlot: 0 })],
        encumbrance: { currentClassicUnits: 4000, maximumClassicUnits: 20000, canMove: true, currentKilograms: 10, maximumKilograms: 50 } },
      loot: { container: '2000:1', revision: '3', title: 'Rat — loot', message: 'Choose an item to take, or take everything.',
        items: [row('stack:5', { label: 'Arrows', quantity: '12', stackable: true, weightKilograms: 1.2, value: 12 }), row('unique:9', { label: 'Dagger' })] } });
    const panel = f.root.querySelector('.dagger-loot');
    const source = panel.querySelector('[data-transfer-side="source"]');
    const pack = panel.querySelector('[data-transfer-side="pack"]');
    assert.equal(document.activeElement, source.querySelector('[data-transfer-key="stack:5"]'), 'The loot screen opens on its first Take.');
    assert.match(pack.textContent, /Gold × 25/, 'The pack pane shows what the player carries.');
    assert.equal(pack.querySelector('button'), null, 'Nothing is put into a corpse.');
    assert.match(panel.querySelector('.dagger-transfer-load').textContent, /Load 10 \/ 50 kg/);
    assert.match(source.querySelector('[data-transfer-item="stack:5"]').textContent, /1\.2 kg · 12 gold/);
    const quantity = source.querySelector('[data-transfer-item="stack:5"] .dagger-stepper input');
    assert.equal(quantity.max, '12');
    assert.equal(source.querySelector('[data-transfer-item="unique:9"] .dagger-stepper'), null, 'A unique item is taken whole.');
    quantity.value = '4';
    quantity.dispatchEvent(new window.Event('change', { bubbles: true }));
    source.querySelector('[data-transfer-key="stack:5"]').click();
    assert.deepEqual(f.actions.at(-1), { action: 'loot-take', container: '2000:1', revision: '3', item: 'stack:5', amount: 4 });
    source.querySelector('[data-transfer-key="unique:9"]').click();
    assert.deepEqual(f.actions.at(-1), { action: 'loot-take', container: '2000:1', revision: '3', item: 'unique:9', amount: 1 });
    panel.querySelector('.dagger-transfer-take-all').click();
    assert.deepEqual(f.actions.at(-1), { action: 'loot-take-all', container: '2000:1', revision: '3' });
    f.publish({ mode: 'modal', loot: { container: '2000:1', revision: '4', title: 'Rat — loot', message: 'Took all 2 items.', items: [] } });
    assert.equal(panel.querySelector('.dagger-transfer-take-all').disabled, true);
    assert.match(source.textContent, /Nothing left to take/);
  } finally { f.dispose(); }
});

test('an empty loot panel presents its owner message exactly once', () => {
  const f = fixture();
  try {
    const message = 'Empty. This container remains open until Exit.';
    f.publish({ mode: 'modal', loot: { container: '2000:1', revision: '1', title: 'Rat — loot', items: [], message } });
    const panel = f.root.querySelector('.dagger-loot');
    assert.equal(panel.textContent.split(message).length - 1, 1);
    f.publish({ mode: 'modal', loot: { container: '2000:1', revision: '2', title: 'Rat — loot', items: [], message: 'Loot changed. Choose the item again.' } });
    assert.match(panel.textContent, /Loot changed/);
    assert.doesNotMatch(panel.textContent, /Empty\./);
  } finally { f.dispose(); }
});

test('frame art awaits the first publication and reports unchanged missing sets once per panel', () => {
  const warnings = [];
  const warn = console.warn;
  console.warn = message => warnings.push(message);
  try {
    art.adopt({ revision: '', images: [] });
    art.reportMissingArt('test-inventory', ['panel', 'slot']);
    assert.equal(warnings.length, 0);
    art.adopt({ revision: 'partial-publication', images: [] });
    for (let frame = 0; frame < 4; frame++) {
      art.reportMissingArt('test-inventory', ['slot', 'panel']);
      art.reportMissingArt('test-loot', ['panel']);
    }
    assert.equal(warnings.length, 2);
    art.reportMissingArt('test-inventory', []);
    art.reportMissingArt('test-inventory', ['slot']);
    assert.equal(warnings.length, 3);
  } finally { console.warn = warn; art.adopt({ revision: '', images: [] }); }
});

test('kit art keeps how each image is drawn and drops a malformed statement', () => {
  try {
    art.adopt({ revision: 'kit', images: [
      { id: 'ui.stone.window.v2', image: 'data:image/png;base64,AA==', display: { scale: 0.5, slice: [64, 64, 64, 64], hotspot: null } },
      { id: 'ui.cursor.grab.v2', image: 'data:image/png;base64,AA==', display: { scale: 0.5, slice: null, hotspot: [32, 32] } },
      { id: 'ui.stone.bad.v2', image: 'data:image/png;base64,AA==', display: { scale: 0.5, slice: [1, 2, 3], hotspot: [-1, 0] } },
      { id: 'ui.stone.unscaled.v2', image: 'data:image/png;base64,AA==', display: { scale: 0 } },
      { id: 'screen.title', image: 'data:image/png;base64,AA==' },
    ] });
    assert.deepEqual(art.display('ui.stone.window.v2'), { scale: 0.5, slice: [64, 64, 64, 64], hotspot: null });
    assert.deepEqual(art.display('ui.cursor.grab.v2'), { scale: 0.5, slice: null, hotspot: [32, 32] });
    assert.deepEqual(art.display('ui.stone.bad.v2'), { scale: 0.5, slice: null, hotspot: null });
    assert.equal(art.display('ui.stone.unscaled.v2'), null);
    assert.equal(art.display('screen.title'), null);
    assert.notEqual(art.image('screen.title'), null);
    art.adopt({ revision: 'next', images: [] });
    assert.equal(art.display('ui.stone.window.v2'), null);
  } finally { art.adopt({ revision: '', images: [] }); }
});

test('pending level up shows permanent and live values and sends guarded semantic choices', () => {
  const f = fixture();
  try {
    f.publish({ mode: 'playing', character: {
      name: 'Aubk-i', attributes: [{ id: 'strength', label: 'Strength', value: 48, permanent: 50 }], skills: [], resources: [], progression: { level: 1, experience: 0 }, equipment: [], grantedSkills: [], creationAvailable: false, creation: null,
      levelUp: { title: 'Level up', level: 2, bonusPool: 4, remainingPoints: 4, healthGain: 6, canCommit: false,
        attributes: [{ id: 'strength', label: 'Strength', permanent: 50, live: 48, pending: 0, canAllocate: true }, { id: 'intelligence', label: 'Intelligence', permanent: 100, live: 100, pending: 0, canAllocate: false }] },
    } });
    assert.match(f.root.querySelector('[data-testid="character-sheet-attribute-strength"]').textContent, /48 live \/ 50 permanent/);
    assert.match(f.root.querySelector('[data-testid="character-level-up-summary"]').textContent, /4 of 4 points remain; health gain 6/);
    f.root.querySelector('[data-testid="character-level-up-strength"]').click();
    assert.deepEqual(f.actions.at(-1), { action: 'character-level-allocate', attribute: 'strength' });
    assert.equal(f.root.querySelector('[data-testid="character-level-up-intelligence"]').disabled, true);
    assert.equal(f.root.querySelector('[data-testid="character-level-up-commit"]').disabled, true);
    f.publish({ mode: 'playing', character: {
      name: 'Aubk-i', attributes: [{ id: 'strength', label: 'Strength', value: 48, permanent: 50 }], skills: [], resources: [], progression: { level: 1, experience: 0 }, equipment: [], grantedSkills: [], creationAvailable: false, creation: null,
      levelUp: { title: 'Level up', level: 2, bonusPool: 4, remainingPoints: 0, healthGain: 6, canCommit: true,
        attributes: [{ id: 'strength', label: 'Strength', permanent: 50, live: 48, pending: 4, canAllocate: false }] },
    } });
    f.root.querySelector('[data-testid="character-level-up-commit"]').click();
    assert.deepEqual(f.actions.at(-1), { action: 'character-level-commit' });
  } finally { f.dispose(); }
});

test('character sheet refreshes owner-published progression, resistance, affiliation, and retained history', () => {
  const f = fixture();
  try {
    f.publish({ mode: 'playing', character: {
      name: 'Aubk-i', attributes: [], skills: [], equipment: [{ label: 'Iron Longsword', slots: ['Right Hand'], details: 'Condition: 2/2 (100%); Unidentified magical item', condition: { current: 2, maximum: 2, percentage: 100, broken: false }, identified: false }], resources: [], grantedSkills: [{ id: 'short-blade', tier: 'minor', label: 'Short Blade', tierLabel: 'Minor skill' }], creationAvailable: false,
      progression: { level: 2, experience: 750, skillProgress: 15, nextLevelSkillProgress: 17, pendingLevelUp: false },
      resistances: [{ id: 'resistance-fire', label: 'Resistance Fire', value: 15, permanent: 25 }],
      affiliations: [{ faction: 'Mephala', guildGroup: 'Daedra', rank: 1, reputation: 6, recognition: 3 }],
      history: { biography: ['A first retained account.'] },
    } });
    assert.match(f.root.querySelector('[aria-label="Player progression"]').textContent, /15 \/ 17 skill total/);
    assert.match(f.root.querySelector('[data-testid="character-sheet-resistance-resistance-fire"]').textContent, /15 live \/ 25 permanent/);
    assert.match(f.root.querySelector('[data-testid="character-sheet-affiliation-Mephala"]').textContent, /Rank 1.*Reputation 6.*Recognition 3/);
    assert.equal(f.root.querySelector('[data-testid="character-sheet-history-0"]').textContent, 'A first retained account.');
    const granted = f.root.querySelector('[data-testid="character-sheet-granted-short-blade"]');
    assert.equal(granted.textContent, 'Minor skill');
    assert.match(granted.parentElement.textContent, /Short Blade/);
    const equipment = [...f.root.querySelectorAll('.dagger-character-section')].find(section => section.querySelector('h3')?.textContent === 'Equipped items');
    assert.match(equipment.textContent, /Unidentified magical item/);

    f.publish({ mode: 'playing', character: {
      name: 'Aubk-i', attributes: [], skills: [], equipment: [{ label: 'Dagger of Fire', slots: ['Right Hand'], details: 'Condition: 1/2 (50%); Enchantment: Fire', condition: { current: 1, maximum: 2, percentage: 50, broken: false }, identified: true }], resources: [], grantedSkills: [], creationAvailable: false,
      progression: { level: 2, experience: 900, skillProgress: 17, nextLevelSkillProgress: 17, pendingLevelUp: true },
      resistances: [{ id: 'resistance-fire', label: 'Resistance Fire', value: 25, permanent: 25 }],
      affiliations: [{ faction: 'Mephala', guildGroup: 'Daedra', rank: 2, reputation: 9, recognition: 4, rankTitle: 'Journeyman' }],
      legalStandings: [{ region: 17, regionName: 'Daggerfall', reputation: -25, standing: 'a criminal', banished: true, current: true, label: 'Daggerfall: a criminal, banished' }],
      history: { biography: ['A revised retained account.'] },
    } });
    assert.match(f.root.querySelector('[aria-label="Player progression"]').textContent, /17 \/ 17 skill total.*Level up ready/);
    assert.match(f.root.querySelector('[data-testid="character-sheet-resistance-resistance-fire"]').textContent, /^25$/);
    assert.match(f.root.querySelector('[data-testid="character-sheet-affiliation-Mephala"]').textContent, /Journeyman.*Reputation 9.*Recognition 4/);
    const legal = f.root.querySelector('[data-testid="character-sheet-legal-17"]');
    assert.match(legal.parentElement.textContent, /Daggerfall: a criminal, banished/);
    assert.equal(f.root.querySelector('[data-testid="character-sheet-history-0"]').textContent, 'A revised retained account.');
    assert.match(equipment.textContent, /Dagger of Fire.*Condition: 1\/2 \(50%\).*Enchantment: Fire/);
  } finally { f.dispose(); }
});

test('custom class editor sends typed skills traits and exposes eligibility reasons', () => {
  const f = fixture();
  try {
    const snapshot = { mode: 'title', character: {
      name: 'Nameless', attributes: [], skills: [], resources: [], progression: { level: 1, experience: 0 }, equipment: [], grantedSkills: [], creationAvailable: true,
      creation: {
        editing: true, current: { name: 'Nameless', race: 'breton', gender: 'male', faceIndex: 0, reflexes: 2, career: 'custom' },
        races: [{ id: 'breton', label: 'Breton', available: true, restriction: null }], careers: [{ id: 'custom', label: 'Custom class', available: true, restriction: null }],
        faces: [{ index: 0, mediaId: 'character.head.male.00.0' }], reflexes: [{ value: 2, label: 'Average' }],
        custom: { name: 'Nightblade', primarySkills: ['mysticism', 'alteration', 'thaumaturgy'], majorSkills: ['illusion', 'destruction', 'restoration'], minorSkills: ['medical', 'short-blade', 'blunt-weapon', 'dragonish', 'daedric', 'dodging'], hitPointsPerLevel: 12,
          advantages: [{ id: 'increased-magery', target: '1.5' }], disadvantages: [{ id: 'forbidden-material', target: 'steel' }], eligibility: ['Choose each trained skill once.'],
          skills: [['mysticism', 'Mysticism'], ['alteration', 'Alteration'], ['thaumaturgy', 'Thaumaturgy'], ['illusion', 'Illusion'], ['destruction', 'Destruction'], ['restoration', 'Restoration'], ['medical', 'Medical'], ['short-blade', 'Short Blade'], ['blunt-weapon', 'Blunt Weapon'], ['dragonish', 'Dragonish'], ['daedric', 'Daedric'], ['dodging', 'Dodging']].map(([id, label]) => ({ id, label })), supportedAdvantages: ['increased-magery:1.5', 'acute-hearing', 'regenerate-health:immersed'], supportedDisadvantages: ['forbidden-material:steel', 'damage:sunlight', 'inability-to-regen'] },
      },
    } };
    f.publish(snapshot);
    assert.equal(f.root.querySelector('[data-testid="character-custom-class"]').hidden, false);
    assert.match(f.root.querySelector('[data-testid="character-custom-eligibility"]').textContent, /Choose each trained skill once/);
    // The skill choices read as the ruleset names them while their values stay skill ids.
    const minorSkill = f.root.querySelector('[aria-label="Minor skills 2"]');
    assert.equal(minorSkill.value, 'short-blade');
    assert.equal(minorSkill.selectedOptions[0].textContent, 'Short Blade');
    assert.ok(![...minorSkill.options].some(option => option.textContent.includes('-')), 'no option shows a raw skill id');
    // A custom-class choice is sent as it changes, so the eligibility shown is always the ruleset's.
    minorSkill.dispatchEvent(new window.Event('change', { bubbles: true }));
    assert.deepEqual(f.actions.at(-1), { action: 'character-update', name: 'Nameless', race: 'breton', gender: 'male', faceIndex: 0, reflexes: 2, career: 'custom',
      primarySkills: 'mysticism,alteration,thaumaturgy', majorSkills: 'illusion,destruction,restoration', minorSkills: 'medical,short-blade,blunt-weapon,dragonish,daedric,dodging', hitPointsPerLevel: 12, advantages: 'increased-magery:1.5', disadvantages: 'forbidden-material:steel' });
    assert.equal(f.root.querySelectorAll('[aria-label^="Advantages "]').length, 7);
    f.root.querySelector('[aria-label="Advantages 2"]').value = 'regenerate-health:immersed';
    f.root.querySelector('[aria-label="Disadvantages 2"]').value = 'damage:sunlight';
    f.root.querySelector('[aria-label="Disadvantages 2"]').dispatchEvent(new window.Event('change', { bubbles: true }));
    assert.equal(f.actions.at(-1).advantages, 'increased-magery:1.5,regenerate-health:immersed');
    assert.equal(f.actions.at(-1).disadvantages, 'forbidden-material:steel,damage:sunlight');

  } finally { f.dispose(); }
});

test('the visible mode follows the product across play, modal, pause, and death', () => {
  const f = fixture();
  try {
    for (const [mode, expected] of [['playing', 'Exploring'], ['modal', 'Interaction'], ['paused', 'Paused'], ['dead', 'Defeated']]) {
      f.publish({ mode });
      assert.equal(f.root.querySelector('.dagger-title strong').textContent, expected);
    }
  } finally { f.dispose(); }
});

test('the HUD title names the site the session projects rather than a fixed label', () => {
  const f = fixture();
  try {
    f.publish({ mode: 'playing', site: { name: 'Castle Necromoghan' } });
    assert.equal(f.root.querySelector('.dagger-title .dagger-site').textContent, 'Castle Necromoghan');
    f.publish({ mode: 'playing', site: { name: 'Charing' } });
    assert.equal(f.root.querySelector('.dagger-title .dagger-site').textContent, 'Charing');
    f.publish({ mode: 'playing', site: null });
    assert.equal(f.root.querySelector('.dagger-title .dagger-site').textContent, '');
  } finally { f.dispose(); }
});

test('save slots list, name new saves, and demand explicit overwrite and deletion confirmation', () => {
  const f = fixture();
  try {
    const menu = f.root.querySelector('.dagger-menu');
    const click = action => f.root.querySelector(`[data-action="${action}"]`).click();
    f.publish({ saveSlots: { entries: [{ key: 'slot-1', label: 'Before the dungeon', savedAtUtc: '2026-09-22T00:00:00.0000000Z', ruleset: 'daggerfall' }], diagnostic: null } });
    f.root.querySelector('.dagger-menu-toggle').click();
    assert.equal(menu.open, true);
    click('save-game');
    assert.deepEqual(f.actions.at(-1), { action: 'save-slots' });
    const select = f.root.querySelector('.dagger-save-slots-select');
    const label = f.root.querySelector('.dagger-save-slots-label');
    label.value = 'After the dungeon';
    click('save-slot');
    assert.deepEqual(f.actions.at(-1), { action: 'save-slot', key: undefined, label: 'After the dungeon', confirm: false });
    select.value = 'slot-1';
    select.dispatchEvent(new window.Event('change'));
    const beforeConfirm = f.actions.length;
    click('save-slot');
    assert.equal(f.actions.length, beforeConfirm);
    assert.equal(f.root.querySelector('[data-action="save-slot"]').textContent, 'Confirm overwrite');
    click('save-slot');
    assert.deepEqual(f.actions.at(-1), { action: 'save-slot', key: 'slot-1', label: 'Before the dungeon', confirm: true });
    click('back');
    click('load-game');
    click('delete-slot');
    assert.equal(f.actions.at(-1).action, 'save-slots');
    assert.equal(f.root.querySelector('[data-action="delete-slot"]').textContent, 'Confirm delete');
    click('delete-slot');
    assert.deepEqual(f.actions.at(-1), { action: 'delete-slot', key: 'slot-1', confirm: true });
  } finally { f.dispose(); }
});

test('control settings capture, explicit swap, cancel and reset use semantic actions', () => {
  const f = fixture();
  try {
    f.publish({ controls: { diagnostic: '', bindings: [
      { id: 'move.forward', category: 'movement', keys: ['KeyW'], fixed: false },
      { id: 'menu', category: 'interface', keys: ['Escape'], fixed: true },
    ] } });
    f.root.querySelector('[data-action="settings"]').click();
    const settings = f.root.querySelector('.dagger-controls-root');
    assert.equal(settings.hidden, false);
    const rebind = [...settings.querySelectorAll('button')].find(button => button.textContent === 'Rebind');
    rebind.click();
    document.dispatchEvent(new KeyboardEvent('keydown', { code: 'KeyQ', bubbles: true, cancelable: true }));
    assert.deepEqual(f.actions.at(-1), { action: 'controls-rebind', item: 'move.forward', key: 'KeyQ', confirm: false });
    settings.querySelector('[aria-label="Swap conflicting binding"]').checked = true;
    rebind.click();
    document.dispatchEvent(new KeyboardEvent('keydown', { code: 'KeyS', bubbles: true, cancelable: true }));
    assert.equal(f.actions.at(-1).confirm, true);
    const count = f.actions.length;
    rebind.click();
    document.dispatchEvent(new KeyboardEvent('keydown', { code: 'Escape', bubbles: true, cancelable: true }));
    assert.equal(f.actions.length, count);
    assert.equal(settings.hidden, false);
    [...settings.querySelectorAll('button')].find(button => button.textContent === 'Reset controls').click();
    assert.deepEqual(f.actions.at(-1), { action: 'controls-reset' });
  } finally { f.dispose(); }
});

test('activation selection follows product mode and emits only semantic mode changes', () => {
  const f = fixture();
  try {
    f.publish({ activation: { mode: 'info', message: 'Information', applied: false } });
    const select = f.root.querySelector('.dagger-activation-mode');
    assert.equal(select.value, 'info');
    select.value = 'bash';
    select.dispatchEvent(new window.Event('change', { bubbles: true }));
    assert.deepEqual(f.actions.at(-1), { action: 'activation-mode', mode: 'bash' });
    f.publish({ activation: { mode: 'grab', message: '', applied: false } });
    assert.equal(select.value, 'grab');
    // The menu that holds the selector is a modal mode, where the choice still applies.
    f.publish({ mode: 'modal' });
    assert.equal(select.disabled, false);
    f.publish({ mode: 'title' });
    assert.equal(select.disabled, true);
  } finally { f.dispose(); }
});


test('multi-choice prompt uses supplied stable identities and refreshes occurrence', () => {
  const f = fixture();
  try {
    const prompt = { instance: 'quest:2', message: 1072, delivery: 'prompt', text: 'Choose a direction.', signoff: null,
      diagnostics: [], promptId: 'quest:2/source/0/1', options: [{ id: 24, label: 'South' }, { id: 25, label: 'West' }, { id: 28, label: 'Southwest' }] };
    f.publish({ quests: { deliveries: [prompt], journal: { active: [], finished: [] }, pending: prompt } });
    const buttons = f.root.querySelectorAll('.dagger-quest-prompt button');
    assert.equal(buttons.length, 3);
    buttons[2].click();
    assert.deepEqual(f.actions.at(-1), { action: 'quest-choice', questInstance: 'quest:2', questMessage: 1072,
      questPrompt: 'quest:2/source/0/1', questChoice: 28 });
    f.publish({ quests: { deliveries: [], journal: { active: [], finished: [] }, pending: { ...prompt, promptId: 'quest:2/source/0/2' } } });
    f.root.querySelector('.dagger-quest-prompt button').click();
    assert.equal(f.actions.at(-1).questPrompt, 'quest:2/source/0/2');
  } finally { f.dispose(); }
});

test('quest popup and prompt retain pressed buttons across snapshots and keep interface input', () => {
  const f = fixture();
  try {
    const popup = { instance: 'quest:2', entryId: 'opening:1', message: 1, delivery: 'popup', heading: 'Welcome',
      text: 'You awaken in the hold.', signoff: null, diagnostics: [] };
    const prompt = { instance: 'quest:2', message: 2, delivery: 'prompt', text: 'Continue?', signoff: null,
      diagnostics: [], promptId: 'opening:2', options: [{ id: 1, label: 'Yes' }, { id: 0, label: 'No' }] };
    const quests = { deliveries: [popup], journal: { active: [], finished: [] }, pending: prompt };
    f.publish({ quests });
    assert.equal(f.modes.at(-1), 'interface');
    const dismiss = f.root.querySelector('.dagger-quest-message button');
    const no = [...f.root.querySelectorAll('.dagger-quest-prompt button')].find(button => button.textContent === 'No');
    no.dispatchEvent(new window.MouseEvent('pointerdown', { bubbles: true }));
    f.publish({ quests: structuredClone(quests), lastOutcome: 'Another admitted update' });
    assert.equal(f.root.querySelector('.dagger-quest-message button'), dismiss);
    assert.equal([...f.root.querySelectorAll('.dagger-quest-prompt button')].find(button => button.textContent === 'No'), no);
    assert.equal(no.isConnected, true);
    no.dispatchEvent(new window.MouseEvent('pointerup', { bubbles: true }));
    no.click();
    assert.equal(f.actions.at(-1).questChoice, 0);
    assert.equal(f.modes.at(-1), 'interface');
    // Removing a sibling popup must not detach the surviving prompt either.
    f.publish({ quests: { ...quests, deliveries: [] } });
    assert.equal(f.root.querySelector('.dagger-quest-prompt button').parentElement, no.parentElement);
    assert.equal(no.isConnected, true);
    f.publish({ quests: { ...quests, deliveries: [], pending: null } });
    assert.equal(f.modes.at(-1), 'gameplay');
  } finally { f.dispose(); }
});

test('cinematic and menu retain interface input until the last cursor surface closes', () => {
  const f = fixture();
  try {
    f.publish({ cinematic: { source: 'ANIM0000.VID', skipLabel: 'Skip' } });
    assert.equal(f.modes.at(-1), 'interface');
    f.publish({ cinematic: { source: 'ANIM0000.VID', skipLabel: 'Skip' } });
    assert.deepEqual(f.modes, ['interface']);
    f.publish({ cinematic: null });
    assert.equal(f.modes.at(-1), 'gameplay');
    f.root.querySelector('.dagger-menu-toggle').click();
    assert.equal(f.modes.at(-1), 'interface');
    f.publish();
    assert.equal(f.modes.at(-1), 'interface');
    f.root.querySelector('.dagger-menu-toggle').click();
    assert.equal(f.modes.at(-1), 'gameplay');
  } finally { f.dispose(); }
});

test('closing a menu that held the world re-takes gameplay focus once the world resumes, without a click', () => {
  const f = fixture();
  try {
    f.publish();
    assert.deepEqual(f.focuses, []);
    // The menu opens and the product holds the world in a modal while it is open.
    f.root.querySelector('.dagger-menu-toggle').click();
    f.publish({ mode: 'modal' });
    assert.equal(f.modes.at(-1), 'interface');
    // Closing it still waits on the product: the projection says modal until the world resumes.
    f.root.querySelector('.dagger-menu-toggle').click();
    assert.equal(f.modes.at(-1), 'interface');
    assert.deepEqual(f.focuses, []);
    f.publish({ mode: 'playing' });
    assert.equal(f.modes.at(-1), 'gameplay');
    assert.deepEqual(f.focuses, ['gameplay']);
    // Further playing snapshots ask for nothing more.
    f.publish({ mode: 'playing' });
    assert.deepEqual(f.focuses, ['gameplay']);

    // A close while another surface still needs the cursor stays in interface mode and asks for no focus.
    const popup = { instance: 'quest:2', entryId: 'opening:1', message: 1, delivery: 'popup', heading: 'Welcome',
      text: 'You awaken.', signoff: null, diagnostics: [] };
    f.root.querySelector('.dagger-menu-toggle').click();
    f.publish({ mode: 'modal', quests: { deliveries: [popup], journal: { active: [], finished: [] }, pending: null } });
    f.root.querySelector('.dagger-menu-toggle').click();
    f.publish({ mode: 'playing', quests: { deliveries: [popup], journal: { active: [], finished: [] }, pending: null } });
    assert.equal(f.modes.at(-1), 'interface');
    assert.deepEqual(f.focuses, ['gameplay']);
    // The popup's dismissal is the last surface: play resumes and takes focus.
    f.publish({ mode: 'playing', quests: { deliveries: [], journal: { active: [], finished: [] }, pending: null } });
    assert.deepEqual(f.focuses, ['gameplay', 'gameplay']);
  } finally { f.dispose(); }
});

test('the snapshot the C# projection publishes is a HUD the UI renders, dialogue included', async () => {
  // Written by the ruleset suite from a real session (HudSnapshotContractTests); both sides read this file.
  const snapshot = JSON.parse(await readFile(new URL('./fixtures/hud-snapshot.json', import.meta.url), 'utf8'));
  const { isHud } = await import(pathToFileURL(join(output, 'main.js')));
  assert.equal(isHud(snapshot), true, 'The published snapshot must satisfy the UI reader.');
  const f = fixture();
  try {
    f.receive(snapshot);
    const dialogueWindow = f.root.querySelector('.dagger-dialogue');
    assert.equal(dialogueWindow.open, true);
    assert.equal(f.root.querySelector('.dagger-dialogue-target').textContent, snapshot.activation.dialogue.targetLabel);
    assert.equal(f.root.querySelectorAll('.dagger-dialogue-topics button').length, snapshot.activation.dialogue.topics.length);
    assert.equal(f.root.querySelector('.dagger-calendar').textContent, `${snapshot.calendar.date} · ${snapshot.calendar.time}`);
  } finally { f.dispose(); }
});

test('active effects render as a structured list and a playing cinematic offers its skip action', () => {
  const f = fixture();
  try {
    f.publish({
      effects: [{ id: 'effect.7', name: 'Shield', spell: 'Ward', source: 'Iron Longsword', remainingSeconds: 3600,
        remaining: '1 hour remaining', detail: 'Shield. From Iron Longsword. 1 hour remaining.' }],
      cinematic: { source: 'ANIM0000.VID', skipLabel: 'Skip' },
    });
    const effects = f.root.querySelector('.dagger-effects');
    assert.equal(effects.hidden, false);
    const row = effects.querySelector('li[data-effect="effect.7"]');
    assert.match(row.textContent, /Ward: Shield/);
    assert.match(row.textContent, /From Iron Longsword\. 1 hour remaining\./);
    const skip = f.root.querySelector('.dagger-cinematic-skip');
    assert.equal(skip.hidden, false);
    skip.click();
    assert.deepEqual(f.actions.at(-1), { action: 'cinematic-skip' });
    // The shell draws the video above the UI, so any key press skips it and goes no further.
    const before = f.actions.length;
    const escape = new KeyboardEvent('keydown', { code: 'Escape', bubbles: true, cancelable: true });
    document.dispatchEvent(escape);
    assert.equal(f.actions.length, before + 1);
    assert.deepEqual(f.actions.at(-1), { action: 'cinematic-skip' });
    assert.equal(escape.defaultPrevented, true);
    f.publish({ effects: [], cinematic: null });
    assert.equal(effects.hidden, true);
    assert.equal(skip.hidden, true);
    const later = f.actions.length;
    document.dispatchEvent(new KeyboardEvent('keydown', { code: 'KeyX', bubbles: true, cancelable: true }));
    assert.ok(f.actions.slice(later).every(action => action.action !== 'cinematic-skip'));
  } finally { f.dispose(); }
});

test('wagon put is refused for the definitions the ruleset names', () => {
  const f = fixture();
  try {
    f.publish({
      inventory: {
        revision: '3:1:0', message: 'Inventory ready.', equipmentChange: null,
        items: [{ key: 'unique:9', definition: 'cart-deed', label: 'Cart', quantity: '1', weight: 0, value: 1, details: '', icon: null, condition: null, identified: true, gridSlot: 0, equippedSlots: [], compatibleSlots: [] }],
        slots: [],
      },
      transport: {
        mode: 'cart', onShip: false, canRun: true, summary: 'Overland journeys take the ordinary time on foot. You can run.', travelModifier: 192, oceanMinutesPerMapPixel: 255,
        options: [{ id: 'cart', mode: 'cart', available: true, selected: true, label: 'Cart', message: 'Cart.', travelModifier: 192 }],
        wagon: {
          exists: true, accessible: true, id: 7, usedClassicUnits: 0, capacityClassicUnits: 300000, storeRevision: '8', message: 'Available.',
          items: [], refusedDefinitions: ['cart-deed'],
        },
      },
    });
    f.root.querySelector('[data-action="transport"]').click();
    assert.equal(f.root.querySelector('[data-action="wagon-put"]').disabled, true);
  } finally { f.dispose(); }
});

test('a load from the menu closes the menu once the loaded session is playing, and a failed load keeps it open', () => {
  const f = fixture();
  try {
    const menu = f.root.querySelector('.dagger-menu');
    const click = action => f.root.querySelector(`[data-action="${action}"]`).click();
    const saveSlots = { entries: [{ key: 'slot-1', label: 'Before the dungeon', savedAtUtc: '2026-09-22T00:00:00.0000000Z', ruleset: 'daggerfall' }], diagnostic: null };
    f.publish({ mode: 'playing', saveSlots });
    f.root.querySelector('.dagger-menu-toggle').click();
    f.publish({ mode: 'modal', saveSlots });
    click('load-game');
    const select = f.root.querySelector('.dagger-save-slots-select');
    select.value = 'slot-1';
    select.dispatchEvent(new window.Event('change'));
    click('load-slot');
    assert.deepEqual(f.actions.at(-1), { action: 'load-slot', key: 'slot-1' });
    f.publish({ mode: 'modal', saveSlots, lastOutcome: 'Load failed: the save is unreadable.' });
    assert.equal(menu.open, true);
    click('load-slot');
    f.publish({ mode: 'playing', saveSlots, lastOutcome: 'Game loaded.' });
    assert.equal(menu.open, false);
    assert.deepEqual(f.actions.at(-1), { action: 'menu', open: false });
  } finally { f.dispose(); }
});

test('an open menu stays open while the product plays when no load was asked for', () => {
  const f = fixture();
  try {
    const menu = f.root.querySelector('.dagger-menu');
    f.publish({ mode: 'playing' });
    f.root.querySelector('.dagger-menu-toggle').click();
    f.publish({ mode: 'playing' });
    assert.equal(menu.open, true);
  } finally { f.dispose(); }
});

test('the game menu tells the product when it opens and closes, because an open menu holds the world', () => {
  const f = fixture();
  try {
    f.publish({ mode: 'playing' });
    const toggle = f.root.querySelector('.dagger-menu-toggle');
    toggle.click();
    assert.deepEqual(f.actions.at(-1), { action: 'menu', open: true });
    toggle.click();
    assert.deepEqual(f.actions.at(-1), { action: 'menu', open: false });
  } finally { f.dispose(); }
});


test('Oghma uses the shared attribute controls with its own title and zero health reward', () => {
  const f = fixture();
  try {
    f.publish({ mode: 'playing', character: {
      name: 'Aubk-i', attributes: [], skills: [], resources: [], progression: { level: 1, experience: 0 }, equipment: [], grantedSkills: [], creationAvailable: false, creation: null,
      levelUp: { title: 'Oghma Infinium', level: 1, bonusPool: 30, remainingPoints: 23, healthGain: 0, canCommit: false,
        attributes: [{ id: 'strength', label: 'Strength', permanent: 50, live: 50, pending: 7, canAllocate: true }] },
    } });
    assert.match(f.root.querySelector('[data-testid="character-level-up-summary"]').textContent, /Oghma Infinium.*23 of 30 points remain; health gain 0/);
    f.root.querySelector('[data-testid="character-level-up-strength"]').click();
    assert.deepEqual(f.actions.at(-1), { action: 'character-level-allocate', attribute: 'strength' });
    assert.equal(f.root.querySelector('[data-testid="character-level-up-commit"]').disabled, true);
  } finally { f.dispose(); }
});

test('tavern lodging renders paid hours and quotes before booking changed duration', () => {
  const f = fixture();
  try {
    const lodging = { key: '17/3/1/2/8', name: 'The Dancing Chasm', days: 1, price: 3, remainingHours: 23, canBook: true, maximumDays: 350 };
    f.publish({ lodging });
    f.root.querySelector('[data-action="rest"]').click();
    assert.equal(f.root.querySelector('.dagger-lodging').hidden, false);
    assert.match(f.root.querySelector('.dagger-lodging-status').textContent, /23 paid hour/);
    const days = f.root.querySelector('.dagger-lodging-days');
    days.value = '2';
    days.dispatchEvent(new window.Event('input', { bubbles: true }));
    assert.equal(f.root.querySelector('.dagger-lodging-book').disabled, true);
    f.root.querySelector('.dagger-lodging-quote').click();
    assert.deepEqual(f.actions.at(-1), { action: 'lodging-quote', key: lodging.key, days: 2 });
    f.publish({ lodging: { ...lodging, days: 2, price: 7 } });
    f.root.querySelector('.dagger-lodging-book').click();
    assert.deepEqual(f.actions.at(-1), { action: 'lodging-book', key: lodging.key, days: 2, amount: 7 });
    f.publish({ lodging: { ...lodging, days: 2, price: 0 } });
    f.root.querySelector('.dagger-lodging-book').click();
    assert.equal(f.actions.at(-1).amount, 0);
    f.publish({ lodging: { ...lodging, canBook: false } });
    assert.equal(f.root.querySelector('.dagger-lodging-book').disabled, true);
    f.publish({ lodging: null });
    assert.equal(f.root.querySelector('.dagger-lodging').hidden, true);
  } finally { f.dispose(); }
});

test('travel accepts only the current quote and shows actual paid arrival or interruption', async () => {
  const { mountTravel } = await import(pathToFileURL(join(output, 'travel.js')));
  const f = fixture();
  try {
    const root = document.createElement('div'); f.root.append(root);
    const actions = []; const view = mountTravel(root, action => actions.push(action));
    const quote = { identity: 'live-quote', destination: 'Charing', minutes: 120, duration: '2 hours', distance: 2, oceanPixels: 0,
      innCost: 5, shipCost: 0, totalCost: 5, canAfford: true,
      options: { cautious: true, inn: true, ship: false, hasHorse: false, hasCart: false, hasShip: false, availableGold: '50', availableGoldPieces: '50' } };
    const value = { destinations: [{ region: 17, index: 3, name: 'Charing', kind: 'Town', regionName: 'Daggerfall' }], quote,
      executionAvailable: true, message: null, lastResult: null };
    view.update(value);
    assert.equal(root.querySelector('option').textContent, 'Charing · Town · Daggerfall');
    assert.match(root.querySelector('[role="status"]').textContent, /^Charing: about 2 hours before any arrival-time adjustment, 5 gold/);
    root.querySelector('.dagger-travel-accept').click();
    assert.deepEqual(actions.at(-1), { action: 'travel-accept', key: 'live-quote', amount: 5 });
    root.querySelector('.dagger-travel-accept').click();
    assert.equal(actions.length, 1);
    const message = 'Arrived at Charing. Paid 5 gold; 2 hours passed.';
    view.update({ ...value, quote: null, executionAvailable: false,
      lastResult: { paidGold: 5, elapsedSeconds: 7200, message }, message: null });
    assert.equal(root.querySelector('.dagger-travel-last-result').textContent, message);
    // The ruleset publishes a finished journey once, as its result, so the status line does not repeat it.
    assert.doesNotMatch(root.querySelector('[role="status"]').textContent, /Arrived/);
    assert.equal(root.querySelector('.dagger-travel-accept').disabled, true);
    const interrupted = 'Your journey was interrupted by an encounter. Paid 5 gold; 1 minute passed.';
    view.update({ ...value, quote: null, executionAvailable: false, message: null, lastResult: { message: interrupted } });
    assert.match(root.querySelector('.dagger-travel-last-result').textContent, /1 minute passed/);
    assert.equal(root.querySelector('.dagger-travel-accept').disabled, true);
    view.dispose();
  } finally { f.dispose(); }
});

test('travel map draws the published world and region art from the projected map and previews a chosen destination', async () => {
  const { mountTravel } = await import(pathToFileURL(join(output, 'travel.js')));
  const f = fixture();
  const warn = console.warn; const warnings = []; console.warn = message => warnings.push(message);
  try {
    // The world view, regions and sheet frame are the ones a real session published (HudSnapshotContractTests);
    // the fixture session discovers no destination on its sheet, so two dots are written into that shape here.
    const snapshot = JSON.parse(await readFile(new URL('./fixtures/hud-snapshot.json', import.meta.url), 'utf8'));
    const published = snapshot.travel.map;
    assert.ok(published.world && published.sheet && published.regions.length > 0);
    const worldImage = 'data:image/png;base64,d29ybGQ=';
    const sheetImage = 'data:image/png;base64,c2hlZXQ=';
    art.adopt({ revision: 'travel-map-art', images: [{ id: published.world.image, image: worldImage }, { id: published.sheet.art.image, image: sheetImage }] });
    const root = document.createElement('div'); f.root.append(root);
    const actions = []; const view = mountTravel(root, action => actions.push(action));
    const value = { destinations: [], quote: null, executionAvailable: false, message: null, lastResult: null, map: null };
    const percent = element => [parseFloat(element.style.left), parseFloat(element.style.top)];
    const near = (actual, expected) => assert.ok(Math.abs(actual - expected) < 1e-9, `${actual} is not ${expected}`);

    view.update(value);
    assert.equal(root.querySelector('.dagger-travel-map-canvas').hidden, true);
    root.querySelector('.dagger-travel-map-toggle').click();
    assert.deepEqual(actions.at(-1), { action: 'travel-map', open: true });

    // World view: the published overview with one marker per region, placed by the projected world pixels.
    const world = { ...published, sheet: null };
    view.update({ ...value, map: world });
    assert.equal(root.querySelector('.dagger-travel-map-canvas').hidden, false);
    assert.equal(root.querySelector('.dagger-travel-map-canvas img').src, worldImage);
    assert.equal(root.querySelector('.dagger-travel-map-world').hidden, true);
    const regions = root.querySelectorAll('.dagger-travel-map-region');
    assert.equal(regions.length, world.regions.length);
    const first = world.regions[0];
    const [left, top] = percent(regions[0]);
    near(left, (first.x - world.world.left) / (world.world.right - world.world.left) * 100);
    near(top, (first.y - world.world.top) / (world.world.bottom - world.world.top) * 100);
    assert.match(regions[0].dataset.tooltip, new RegExp(`^${first.name}: ${first.discovered} known`));
    assert.ok(root.querySelector('.dagger-travel-map-player'));
    const picker = root.querySelector('.dagger-travel-map-regions');
    assert.equal(picker.options.length, world.regions.length + 1);
    assert.equal(picker.value, '');
    picker.value = String(world.regions[1].region); picker.dispatchEvent(new window.Event('change'));
    assert.deepEqual(actions.at(-1), { action: 'travel-map', open: true, region: world.regions[1].region, page: 0 });
    regions[0].click();
    assert.deepEqual(actions.at(-1), { action: 'travel-map', open: true, region: first.region, page: 0 });

    // Region sheet: the published sheet art with a dot per discovered destination, coloured by its family.
    const frame = published.sheet.art;
    const destinations = [
      { index: 5, name: 'Hand-drawn Hamlet', kind: 'Town', category: 'town', x: frame.left + 70, y: frame.top + 52 },
      { index: 9, name: 'Hand-drawn Keep', kind: 'Dungeon', category: 'dungeon', x: frame.left + 200, y: frame.top + 100 },
    ];
    const open = { ...published, sheet: { ...published.sheet, destinations } };
    view.update({ ...value, map: open });
    assert.equal(root.querySelector('.dagger-travel-map-canvas img').src, sheetImage);
    assert.equal(root.querySelector('.dagger-travel-map-canvas').style.aspectRatio, `${frame.width} / ${frame.height}`);
    assert.equal(picker.value, String(published.sheet.region));
    assert.equal(root.querySelector('.dagger-travel-map-world').hidden, false);
    assert.equal(root.querySelector('.dagger-travel-map-next').hidden, published.sheet.pages < 2);
    assert.match(root.querySelector('.dagger-travel-map-caption').textContent, new RegExp(published.sheet.name));
    const dots = root.querySelectorAll('.dagger-travel-map-dot');
    assert.equal(dots.length, 2);
    assert.deepEqual([dots[0].dataset.category, dots[1].dataset.category], ['town', 'dungeon']);
    assert.equal(dots[0].dataset.tooltip, 'Hand-drawn Hamlet (Town)');
    assert.equal(dots[0].getAttribute('aria-label'), 'Hand-drawn Hamlet (Town)');
    const [dotLeft, dotTop] = percent(dots[0]);
    near(dotLeft, 70 / (frame.right - frame.left) * 100);
    near(dotTop, 52 / (frame.bottom - frame.top) * 100);

    // Choosing a dot drives the ordinary preview, and the quote it answers with is the one accepted.
    dots[1].click();
    assert.deepEqual(actions.at(-1), { action: 'travel-preview', region: published.sheet.region, destination: 9,
      cautious: true, inn: false, ship: false });
    const quote = { identity: 'map-quote', destination: 'Hand-drawn Keep', minutes: 60, duration: '1 hour', distance: 3, oceanPixels: 0,
      innCost: 0, shipCost: 0, totalCost: 4, canAfford: true,
      options: { cautious: true, inn: false, ship: false, hasHorse: false, hasCart: false, hasShip: false, availableGold: '9', availableGoldPieces: '9' } };
    view.update({ ...value, map: open, quote, executionAvailable: true });
    assert.equal(root.querySelector('.dagger-travel-map-dot[data-index="9"]').getAttribute('aria-pressed'), 'true');
    root.querySelector('.dagger-travel-accept').click();
    assert.deepEqual(actions.at(-1), { action: 'travel-accept', key: 'map-quote', amount: 4 });
    root.querySelector('.dagger-travel-map-world').click();
    assert.deepEqual(actions.at(-1), { action: 'travel-map', open: true });

    // Art this session has not published is marked rather than drawn from anywhere else.
    art.adopt({ revision: 'travel-map-partial', images: [{ id: published.world.image, image: worldImage }] });
    view.update({ ...value, map: { ...open, sheet: { ...open.sheet, destinations: destinations.slice(0, 1) } } });
    assert.equal(root.querySelector('.dagger-travel-map-canvas').dataset.artMissing, frame.image);
    assert.ok(warnings.some(message => message.includes(frame.image)));
    root.querySelector('.dagger-travel-map-toggle').click();
    assert.deepEqual(actions.at(-1), { action: 'travel-map', open: false });
    view.dispose();
  } finally { console.warn = warn; art.adopt({ revision: '', images: [] }); f.dispose(); }
});

test('hiding the travel panel closes an open travel map so its sheet stops being published', async () => {
  const f = fixture();
  try {
    const snapshot = JSON.parse(await readFile(new URL('./fixtures/hud-snapshot.json', import.meta.url), 'utf8'));
    const travel = { destinations: [], quote: null, executionAvailable: false, message: null, lastResult: null, map: snapshot.travel.map };
    const closes = () => f.actions.filter(action => action.action === 'travel-map' && action.open === false).length;
    f.publish({ travel });
    f.root.querySelector('[data-action="transport"]').click();
    assert.equal(f.root.querySelector('.dagger-travel-map-canvas').hidden, false);
    assert.equal(closes(), 0);
    // Leaving the panel for the menu home closes the map once, even before the ruleset answers.
    f.root.querySelector('[data-action="back"]').click();
    assert.deepEqual(f.actions.filter(action => action.action === 'travel-map').at(-1), { action: 'travel-map', open: false });
    assert.equal(closes(), 1);
    f.root.querySelector('[data-action="back"]').click();
    assert.equal(closes(), 1);
    // Switching straight to another panel closes it too.
    f.publish({ travel });
    f.root.querySelector('[data-action="transport"]').click();
    f.root.querySelector('[data-action="map"]').click();
    assert.equal(closes(), 2);
    // A closed map needs no word when the panel is left.
    f.publish({ travel: { ...travel, map: null } });
    f.root.querySelector('[data-action="transport"]').click();
    f.root.querySelector('[data-action="back"]').click();
    assert.equal(closes(), 2);
  } finally { f.dispose(); }
});

test('changing travel options disables acceptance until a fresh quote arrives, including a free journey', async () => {
  const { mountTravel } = await import(pathToFileURL(join(output, 'travel.js')));
  const f = fixture();
  try {
    const root = document.createElement('div'); f.root.append(root);
    const actions = []; const view = mountTravel(root, action => actions.push(action));
    const quote = { identity: 'old', destination: 'Known', minutes: 60, duration: '1 hour', distance: 1, oceanPixels: 0, innCost: 0, shipCost: 0, totalCost: 0, canAfford: true,
      options: { cautious: true, inn: false, ship: false } };
    const value = { destinations: [{ region: 0, index: 1, name: 'Known', kind: 'Town', regionName: 'Alik\'r Desert' }], quote, executionAvailable: true, message: null, lastResult: null };
    view.update(value);
    const cautious = root.querySelector('input[type="checkbox"]'); cautious.checked = false;
    cautious.dispatchEvent(new window.Event('change'));
    assert.equal(root.querySelector('.dagger-travel-accept').disabled, true);
    view.update(value); // An unrelated publication of the old quote cannot re-admit changed controls.
    assert.equal(root.querySelector('.dagger-travel-accept').disabled, true);
    cautious.checked = true; cautious.dispatchEvent(new window.Event('change'));
    assert.equal(root.querySelector('.dagger-travel-accept').disabled, false); // Returning to the quoted choices is valid.
    cautious.checked = false; cautious.dispatchEvent(new window.Event('change'));
    view.update({ ...value, quote: { ...quote, identity: 'fresh', options: { ...quote.options, cautious: false } } });
    root.querySelector('.dagger-travel-accept').click();
    assert.deepEqual(actions.at(-1), { action: 'travel-accept', key: 'fresh', amount: 0 });
    view.dispose();
  } finally { f.dispose(); }
});

for (const kind of ['dungeon', 'interior']) test(`${kind} map controls change only presentation and retain live player position without discovering geometry`, () => {
  const f = fixture();
  try {
    const map = { id: 'privateers', name: "Privateer's Hold", kind, region: null, location: null,
      player: { x: 1, y: 2, z: 3, yaw: .5 }, labels: [],
      areas: [{ id: 'known-room', minX: 0, minZ: 0, maxX: 10, maxZ: 10, minY: 1, maxY: 3, kind: 1, category: 'room' },
        { id: 'known-upper', minX: 0, minZ: 0, maxX: 10, maxZ: 10, minY: 4, maxY: 6, kind: 1, category: 'room' }] };
    f.publish({ map }); f.root.querySelector('[data-action="map"]').click();
    assert.deepEqual(f.actions.at(-1), { action: 'map-open', open: true });
    const group = f.root.querySelector('.dagger-map-diagram > g');
    const initial = group.getAttribute('transform'); const count = f.actions.length;
    const press = label => Array.from(f.root.querySelectorAll('.dagger-map-controls button')).find(button => button.textContent === label).click();
    press('Pan right'); assert.notEqual(group.getAttribute('transform'), initial);
    press('Zoom in'); press('Rotate right'); press('Level up');
    assert.equal(group.querySelectorAll('rect').length, 1);
    assert.equal(group.querySelector('rect').dataset.id, 'known-room');
    const height = f.root.querySelector('.dagger-map-level'); height.value = '5'; height.dispatchEvent(new window.Event('change', { bubbles: true }));
    assert.equal(group.querySelector('rect').dataset.id, 'known-upper');
    assert.equal(f.actions.length, count);
    f.publish({ map: { ...map, player: { ...map.player, x: 8 } } });
    assert.match(group.querySelector('[data-player]').getAttribute('transform'), /translate\(8 3\)/);
    assert.equal(group.querySelectorAll('rect').length, 1);
    f.root.querySelector('[data-action="back"]').click();
    assert.deepEqual(f.actions.at(-1), { action: 'map-open', open: false });
  } finally { f.dispose(); }
});

test('dungeon map notes are added at the player, edited and removed through the discovery owner', () => {
  const f = fixture();
  try {
    const map = { id: 'privateers', name: "Privateer's Hold", kind: 'dungeon', region: null, location: null, canAddNote: true,
      player: { x: 1, y: 2, z: 3, yaw: 0 }, areas: [],
      labels: [{ id: 'note-1', name: 'Stairs down', x: 1, y: 2, z: 3, selected: false, note: true },
        { id: 'entrance', name: 'Entrance', x: 0, y: 2, z: 0, selected: false, note: false }] };
    f.publish({ map }); f.root.querySelector('[data-action="map"]').click();
    const form = f.root.querySelector('.dagger-map-note');
    assert.equal(form.hidden, false);
    form.querySelector('input').value = '  Lever room ';
    form.dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true }));
    assert.deepEqual(f.actions.at(-1), { action: 'map-note-add', text: 'Lever room' });
    const note = f.root.querySelector('[data-note="note-1"]');
    const edit = note.querySelector('.dagger-map-note-edit');
    assert.equal(edit.querySelector('input').value, 'Stairs down');
    edit.querySelector('input').value = ' Stairs down, then left ';
    edit.dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true }));
    assert.deepEqual(f.actions.at(-1), { action: 'map-note-edit', note: 'note-1', text: 'Stairs down, then left' });
    note.querySelector('button').click();
    assert.deepEqual(f.actions.at(-1), { action: 'map-note-remove', note: 'note-1' });
    assert.equal(f.root.querySelector('.dagger-map-buildings').textContent.match(/Remove note/g).length, 1);
    f.publish({ map: { ...map, kind: 'city', region: 17, location: 45, canAddNote: false, labels: [] } });
    assert.equal(form.hidden, true);
  } finally { f.dispose(); }
});

test('city map selection sends the actual location and placed building identity to the shared owner', () => {
  const f = fixture();
  try {
    const map = { id: '17/45', name: 'Charing', kind: 'city', region: 17, location: 45,
      player: { x: 0, y: 0, z: 0, yaw: 0 }, areas: [],
      labels: [{ id: '0/0/3', name: 'City Wall', x: 1, y: 0, z: 2, selected: false },
        { id: '1/0/3', name: 'City Wall', x: 103.4, y: 0, z: 2, selected: false }] };
    f.publish({ map });
    const buttons = f.root.querySelectorAll('.dagger-map-buildings button');
    assert.equal(buttons.length, 2); buttons[1].click();
    assert.deepEqual(f.actions.at(-1), { action: 'map-building', region: 17, destination: 45, item: '1/0/3' });
    f.publish({ map: { ...map, labels: map.labels.map(label => ({ ...label, selected: label.id === '1/0/3' })) } });
    assert.equal(f.root.querySelector('[data-building="1/0/3"]').getAttribute('aria-pressed'), 'true');
    assert.match(f.root.querySelector('.dagger-map-target').textContent, /City Wall/);
  } finally { f.dispose(); }
});


test('character draft screens consume the published mode art and every pick part with semantic actions', async () => {
  const { MODE_SCREENS, screenForMode } = await import(pathToFileURL(join(output, 'screens.js')));
  const inventory = JSON.parse(await readFile(new URL('../../content/worldrpg/media/classic-media-inventory.json', import.meta.url), 'utf8'));
  for (const mode of ['character-generation', 'character-pick']) {
    const id = screenForMode(mode);
    assert.ok(MODE_SCREENS.some(row => row.mode === mode && row.screen === id));
    assert.ok(inventory.artifacts.some(artifact => artifact.mediaId === id), `${mode} must name a published artifact`);
  }
  assert.deepEqual(inventory.artifacts.filter(artifact => artifact.slot === 'pick').map(artifact => artifact.mediaId), ['screen.pick.02']);
  const f = fixture();
  try {
    const ids = ['screen.title', 'screen.character-generation', 'screen.pick.02'];
    const images = await Promise.all(ids.map(async id => {
      const artifact = inventory.artifacts.find(artifact => artifact.mediaId === id);
      const bytes = await readFile(new URL('../../content/' + artifact.path, import.meta.url));
      return { id, image: 'data:image/png;base64,' + bytes.toString('base64') };
    }));
    // A second admitted pick part exercises the binding set, using actual artifact bytes.
    images.push({ id: 'screen.pick.background', image: images[2].image });
    const art = { revision: 'character-screen-fixture', images };
    const character = { name: 'Nameless', attributes: [], skills: [], resources: [], progression: { level: 1, experience: 0 }, equipment: [], grantedSkills: [], creationAvailable: true };
    const creation = { editing: true, mode: 'character-pick', classQuestionsAvailable: true, classQuiz: null,
      current: { name: 'Nameless', race: 'breton', gender: 'male', faceIndex: 0, reflexes: 2, career: 'class00' },
      races: [{ id: 'breton', label: 'Breton', available: true, restriction: null }], careers: [{ id: 'class00', label: 'Mage', available: true, restriction: null }],
      faces: [{ index: 0, mediaId: 'character.head.male.00.0' }], reflexes: [{ value: 2, label: 'Average' }] };
    const entry = f.root.querySelector('.dagger-entry');
    f.publish({ mode: 'title', character: { ...character, creation: { ...creation, editing: false, mode: null } }, uiArt: art, uiArtRevision: art.revision });
    f.root.querySelector('[data-testid="entry-create-character"]').click();
    assert.deepEqual(f.actions.at(-1), { action: 'character-begin' });
    f.publish({ mode: 'title', character: { ...character, creation }, pickScreens: ['screen.pick.02', 'screen.pick.background'] });
    assert.equal(entry.dataset.mode, 'character-pick');
    assert.equal(entry.querySelector('.dagger-entry-screen').src, images[2].image);
    assert.equal(entry.querySelector('[data-media-id="screen.pick.background"]').src, images[3].image);
    assert.equal(entry.querySelectorAll('[data-testid="character-commit"]').length, 1);
    assert.equal(f.root.querySelectorAll('[data-testid="character-commit"]').length, 1, 'The one character editor is moved, not copied.');
    assert.equal(entry.querySelector('.dagger-entry-begin').hidden, true);
    entry.querySelector('[data-testid="character-class-questions"]').click();
    assert.deepEqual(f.actions.at(-1), { action: 'character-class-questions', name: 'Nameless', race: 'breton', gender: 'male', faceIndex: 0, reflexes: 2, career: 'class00' });
    f.publish({ mode: 'title', character: { ...character, creation: { ...creation, mode: 'character-generation', classQuiz: {
      answered: 0, total: 10, question: { number: 22, text: 'A source question', answers: [{ index: 0, text: 'First answer' }, { index: 1, text: 'Second answer' }, { index: 2, text: 'Third answer' }] },
    } } } });
    assert.equal(entry.dataset.mode, 'character-generation');
    assert.equal(entry.querySelector('.dagger-entry-screen').src, images[1].image);
    assert.equal(entry.querySelector('[data-media-id="screen.pick.background"]'), null);
    entry.querySelector('[data-testid="class-answer-1"]').click();
    assert.deepEqual(f.actions.at(-1), { action: 'character-class-answer', question: 22, answer: 1 });
    const back = [...entry.querySelectorAll('button')].find(button => button.textContent === 'Choose a class instead'); back.click();
    assert.deepEqual(f.actions.at(-1), { action: 'character-class-back' });
    f.publish({ mode: 'title', character: { ...character, creation }, pickScreens: ['screen.pick.02'] });
    assert.equal(entry.dataset.mode, 'character-pick');
    // Without a rolled background the commit is refused by the control, which says why.
    assert.equal(entry.querySelector('[data-testid="character-commit"]').disabled, true);
    assert.match(entry.querySelector('[data-testid="character-commit-blocked"]').textContent, /Roll a background/);
    f.publish({ mode: 'title', character: { ...character, creation: { ...creation, editing: false, mode: null } } });
    assert.equal(entry.dataset.mode, 'title');
    assert.equal(entry.querySelector('.dagger-entry-screen').src, images[0].image);
    f.publish({ mode: 'playing', character: { ...character, creationAvailable: false } });
    assert.equal(entry.hidden, true);
  } finally { f.dispose(); }
});


test('teleport presents the paid anchor choice and sends current semantic actions', () => {
  const f = fixture();
  try {
    f.publish({mode:'modal',teleport:{revision:'cast.12',anchorSet:false}});
    const panel=f.root.querySelector('.dagger-teleport');
    assert.equal(panel.hidden,false);
    let buttons=panel.querySelectorAll('button');
    assert.equal(buttons[1].disabled,true);
    buttons[0].click();
    assert.deepEqual(f.actions.at(-1),{action:'teleport-select',revision:'cast.12',key:'anchor'});
    f.publish({teleport:{revision:'cast.13',anchorSet:true}});
    buttons=panel.querySelectorAll('button');assert.equal(buttons[1].disabled,false);
    buttons[1].click();assert.deepEqual(f.actions.at(-1),{action:'teleport-select',revision:'cast.13',key:'recall'});
    buttons[2].click();assert.deepEqual(f.actions.at(-1),{action:'teleport-select',revision:'cast.13',key:'cancel'});
    f.publish({teleport:null});assert.equal(panel.hidden,true);assert.equal(panel.children.length,0);
  } finally {f.dispose();}
});

test('dispel choice renders published bundles and sends current select and cancel actions', () => {
  const f = fixture();
  try {
    f.publish({ mode: 'modal', dispel: { revision: 'cast.8', options: [{ id: 'cast.2', label: 'True invisibility' }] } });
    const panel = f.root.querySelector('.dagger-dispel');
    assert.equal(panel.hidden, false);
    const buttons = panel.querySelectorAll('button');
    assert.equal(buttons[0].textContent, 'True invisibility');
    buttons[0].click();
    assert.deepEqual(f.actions.at(-1), { action: 'dispel-select', revision: 'cast.8', key: 'cast.2' });
    buttons[1].click();
    assert.deepEqual(f.actions.at(-1), { action: 'dispel-cancel', revision: 'cast.8' });
    f.publish({ dispel: null });
    assert.equal(panel.hidden, true);
    assert.equal(panel.querySelectorAll('button').length, 0);
  } finally { f.dispose(); }
});


test('detectors render only resolved contacts and clear retired sources without scanning', () => {
  const f = fixture();
  try {
    f.publish({ detectors: [{ source: 'cast.2', kind: 'magic', contacts: [{ kind: 'actor', id: '2000', label: 'Imp', distance: 4.5, bearingRadians: Math.PI / 2, items: [] }] },
      { source: 'cast.3', kind: 'treasure', contacts: [] }] });
    const panel = f.root.querySelector('.dagger-detectors');
    assert.equal(panel.hidden, false);
    assert.equal(panel.children.length, 2);
    assert.match(panel.textContent, /Detect magic: Imp, 4.5 m, bearing 90°/);
    assert.match(panel.textContent, /Detect treasure: none nearby/);
    f.publish({ detectors: [] });
    assert.equal(panel.hidden, true);
    assert.equal(panel.children.length, 0);
    assert.equal(f.actions.length, 0);
  } finally { f.dispose(); }
});

test('spell selection uses projected known rows and sends ready unready and cast actions', () => {
  const f=fixture();
  try {
    const spells={available:[{key:'spell.023',name:'Troll\'s Blood',cost:12}],ready:null,result:''};
    f.publish({spells});
    f.root.querySelector('[data-action="spells"]').click();
    const panel=f.root.querySelector('.dagger-spells-root');
    assert.equal(panel.hidden,false);
    assert.equal(f.root.querySelector('#dagger-menu-title').textContent,'Known spells');
    assert.equal(panel.querySelector('[data-action="spell-cast"]').disabled,true);
    panel.querySelector('[data-spell="spell.023"]').click();
    assert.deepEqual(f.actions.at(-1),{action:'spell-ready',key:'spell.023'});
    assert.equal(panel.querySelector('[data-action="spell-cast"]').disabled,true);
    f.publish({spells:{...spells,ready:'spell.023',result:'Ready'}});
    assert.match(panel.textContent,/Ready/);assert.match(panel.textContent,/12 magicka/);
    panel.querySelector('[data-action="spell-unready"]').click();
    assert.deepEqual(f.actions.at(-1),{action:'spell-unready'});
    panel.querySelector('[data-action="spell-cast"]').click();
    assert.deepEqual(f.actions.slice(-2),[{action:'menu',open:false},{action:'spell-cast'}]);
    f.publish({spells:{...spells,available:[],ready:null,result:'UnknownSpell'}});
    assert.equal(panel.querySelector('[data-spell]'),null);assert.match(panel.textContent,/No available known spells/);
    f.publish({spells:{available:[{key:'bad',name:'Bad',cost:NaN}],ready:'bad',result:''}});
    assert.equal(panel.querySelector('[data-spell]'),null);
  } finally {f.dispose();}
});

test('identify formats authoritative choices and sends a semantic batch selection without optimistic item changes', () => {
  const f=fixture();
  try {
    f.publish({identify:{revision:'cast:9',cost:5,options:[{id:'409',label:'Unidentified sword'}]}});
    const panel=f.root.querySelector('.dagger-identify');
    assert.equal(panel.hidden,false);
    assert.match(panel.querySelector('h2').textContent,/5 magicka/);
    [...panel.querySelectorAll('button')].find(button=>button.textContent==='All unidentified items').click();
    assert.deepEqual(f.actions.at(-1),{action:'identify-select',revision:'cast:9',key:'all'});
    assert.equal(panel.querySelectorAll('button').length,3);
    [...panel.querySelectorAll('button')].find(button=>button.textContent==='Cancel').click();
    assert.deepEqual(f.actions.at(-1),{action:'identify-cancel',revision:'cast:9'});
    f.publish({identify:null});assert.equal(panel.hidden,true);assert.equal(panel.children.length,0);
  } finally {f.dispose();}
});


test('committed character summary stays at the entry screen and sends launch restart and abandon actions', () => {
  const f = fixture();
  try {
    f.publish({ mode: 'title', character: {
      name: 'New adventurer', attributes: [], skills: [], resources: [], progression: { level: 1, experience: 0 }, equipment: [], grantedSkills: [], creationAvailable: true,
      creation: { editing: false, current: { name: 'New adventurer', race: 'breton', gender: 'male', faceIndex: 0, reflexes: 2, career: 'class00' },
        races: [], careers: [], faces: [], reflexes: [], summary: ['New adventurer — Mage.', '100 gold plus biography grants.', 'Spell: Shock'] },
    } });
    const summary = f.root.querySelector('[data-testid="new-game-summary"]');
    assert.ok(summary.closest('.dagger-entry'));
    assert.match(summary.textContent, /Spell: Shock/);
    f.root.querySelector('[data-testid="new-game-launch"]').click(); assert.deepEqual(f.actions.at(-1), { action: 'begin' });
    f.root.querySelector('[data-testid="character-begin"]').click(); assert.deepEqual(f.actions.at(-1), { action: 'character-begin' });
    f.root.querySelector('[data-testid="new-game-abandon"]').click(); assert.deepEqual(f.actions.at(-1), { action: 'character-cancel' });
  } finally { f.dispose(); }
});

test('spell seller and spellbook use confirmed semantic changes and source settings', () => {
  const f=fixture();
  try {
    const spells={available:[{key:'spell.023',name:'Heal',cost:15},{key:'spell.001',name:'Unavailable spell',cost:0,canCast:false}],
      ready:'spell.023',result:'',sale:{revision:'seller-1',provider:'Mage',offers:[
        {key:'spell.002',name:'Cure',castingCost:12,price:48,known:false},
        {key:'spell.023',name:'Heal',castingCost:15,price:60,known:true}]},
      information:{key:'spell.023',name:'Heal',target:'Self',element:'Magic',details:['restoration: magnitude 1–10.']}};
    f.publish({spells,activation:{mode:'talk',message:'',applied:true,dialogue:{revision:'seller-1',targetLabel:'Mage',greeting:'Welcome',tone:'normal',question:null,reply:null,topics:[],diagnostics:[]}}});
    const sales=f.root.querySelector('.dagger-dialogue-spells');
    assert.equal(sales.querySelector('[data-action="spell-buy"][data-spell="spell.023"]').disabled,true);
    const buy=sales.querySelector('[data-action="spell-buy"][data-spell="spell.002"]');
    buy.click(); assert.match(f.root.querySelector('.dagger-confirm').textContent,/Buy this spell for 48 gold\?/);
    f.answer(false); assert.equal(f.actions.some(action=>action.action==='spell-buy'),false);
    buy.click(); f.answer(true); assert.deepEqual(f.actions.at(-1),{action:'spell-buy',key:'spell.002',revision:'seller-1',amount:48,confirm:true});
    sales.querySelector('[data-action="spell-info"]').click();assert.deepEqual(f.actions.at(-1),{action:'spell-info',key:'spell.002'});
    f.root.querySelector('[data-action="spells"]').click();
    const book=f.root.querySelector('.dagger-spells-root');
    assert.equal(book.querySelector('[data-action="spell-ready"][data-spell="spell.001"]').disabled,true);
    assert.match(book.textContent,/Unavailable spell · Unavailable/);assert.match(book.textContent,/magnitude 1–10/);
    assert.match(book.textContent,/Target: Self; element: Magic/);
    book.querySelector('[data-action="spell-delete"]').click();f.answer(true);assert.deepEqual(f.actions.at(-1),{action:'spell-delete',key:'spell.023',confirm:true});
    book.querySelector('[data-action="spell-delete"]').click();f.answer(false);
    assert.equal(f.actions.filter(action=>action.action==='spell-delete').length,1);
  } finally { f.dispose(); }
});


test('property offers stay in the inventory panel, and its storage is a Storage tab on the transfer layout', () => {
  const f = fixture();
  try {
    const row = (key, overrides) => ({ key, definition: key, label: key, quantity: '1', weight: 0, value: 1, details: '', icon: null,
      condition: null, identified: true, gridSlot: 0, equippedSlots: [], compatibleSlots: [], ...overrides });
    f.publish({ inventory: { revision: 'ui-revision', message: '', equipmentChange: null, slots: [],
        items: [row('stack:coins', { label: 'Gold', quantity: '4', stackable: true }), row('unique:3', { label: 'Sword', equippedSlots: ['right-hand'], gridSlot: null })] },
      property: { bankAvailable: true, offers: [{ key: 'ship/small', name: 'Small ship', price: '100000', salePrice: '85000',
        owned: false, canBuy: true, canSell: false, canEnter: false, enterable: false }], storage: { key: 'house/17/4/A.RMB/0/0/1', revision: '12',
          items: [row('stack:stored', { label: 'Gold piece', quantity: '2', stackable: true })],
          storeRefusals: [{ item: 'unique:3', reason: 'Unequip that item before storing it.' }] } } });
    f.root.querySelector('[data-action="inventory"]').click();
    [...f.root.querySelectorAll('.dagger-property-root button')].find(button => button.textContent === 'Buy').click();
    assert.match(f.root.querySelector('.dagger-confirm').textContent, /Buy Small ship for 100000 gold\?/);
    f.answer(true);
    assert.deepEqual(f.actions.at(-1), { action: 'property-buy', key: 'ship/small' });
    f.root.querySelector('[data-inventory-tab="storage"]').click();
    const storage = f.root.querySelector('.dagger-inventory-storage');
    assert.equal(storage.hidden, false);
    assert.equal(f.root.querySelector('.dagger-inventory-pack').hidden, true);
    const stored = storage.querySelector('[data-transfer-side="source"]');
    const pack = storage.querySelector('[data-transfer-side="pack"]');
    // A part of a carried stack is stored; the worn sword says why it is not offered.
    const amount = pack.querySelector('[data-transfer-item="stack:coins"] .dagger-stepper input');
    amount.value = '3';
    amount.dispatchEvent(new window.Event('change', { bubbles: true }));
    pack.querySelector('[data-transfer-key="stack:coins"]').click();
    assert.deepEqual(f.actions.at(-1), { action: 'property-put', key: 'house/17/4/A.RMB/0/0/1', item: 'stack:coins', revision: '12', amount: 3 });
    assert.equal(pack.querySelector('[data-transfer-key="unique:3"]').disabled, true);
    assert.match(pack.querySelector('[data-transfer-item="unique:3"]').textContent, /Unequip that item before storing it\./);
    stored.querySelector('[data-transfer-key="stack:stored"]').click();
    assert.deepEqual(f.actions.at(-1), { action: 'property-take', key: 'house/17/4/A.RMB/0/0/1', item: 'stack:stored', revision: '12', amount: 2 });
    f.publish({ property: { bankAvailable: false, offers: [], storage: null } });
    assert.equal(f.root.querySelector('.dagger-property-root').hidden, true);
    assert.equal(f.root.querySelector('[data-inventory-tab="storage"]'), null);
    assert.equal(f.root.querySelector('.dagger-inventory-pack').hidden, false);
  } finally { f.dispose(); }
});

test('live banking dialogue emits bank-open with its actual revision', () => {
  const f = fixture();
  try {
    const dialogue = { revision: 'bank-7', targetLabel: 'Bank teller', greeting: 'Welcome', tone: 'normal',
      topics: [], diagnostics: [], bankAvailable: true };
    f.publish({ activation: { mode: 'talk', dialogue } });
    f.root.querySelector('[data-bank-open]').click();
    assert.deepEqual(f.actions.at(-1), { action: 'bank-open', revision: 'bank-7' });
    f.publish({ activation: { mode: 'talk', dialogue: { ...dialogue, bankAvailable: false } } });
    assert.equal(f.root.querySelector('[data-bank-open]'), null);
  } finally { f.dispose(); }
});

test('the conversation groups its topics under the published headings, keeps a short log, and summoning pays only after the confirm', () => {
  const f = fixture();
  try {
    const groups = [{ id: 'talk', label: 'Ask about' }, { id: 'quests', label: 'Your business' }, { id: 'places', label: 'Where is…' }, { id: 'services', label: 'Services' }];
    const dialogue = (revision, question, reply) => ({ revision, targetLabel: 'Hrolf', greeting: 'Well met.', tone: 'normal', question, reply, diagnostics: [],
      bankAvailable: true, topicGroups: groups, topics: [
        { id: 'news', label: 'Any news?', group: 'talk' }, { id: 'directions', label: 'The Odd Blades', key: 'place.7', group: 'places' },
        { id: 'donate', label: 'Donate gold', group: 'services' }] });
    const summoning = { revision: 'summon-1', provider: 'Hrolf', offerRevision: null, prince: null, message: null, diagnostics: [],
      quote: { key: 'azura', name: 'Azura', quest: 'Q0C00Y00', gold: 2000, eligible: true, reason: null } };
    const publish = (value) => f.publish({ activation: { mode: 'talk', dialogue: value }, spells: { available: [], ready: null, result: '', summoning } });
    publish(dialogue('d-1', null, null));
    const sections = [...f.root.querySelectorAll('.dagger-dialogue-topic-group')];
    assert.deepEqual(sections.map(section => section.querySelector('h3').textContent), ['Ask about', 'Where is…', 'Services'], 'Empty groups are left out.');
    assert.equal(sections[1].querySelector('[data-topic="directions"]').dataset.key, 'place.7');
    assert.ok(sections[2].querySelector('[data-bank-open]'), 'The bank sits with the services.');
    // Donation takes its amount from the stepper.
    f.root.querySelector('.dagger-dialogue-donation input').value = '50';
    f.root.querySelector('.dagger-dialogue-donation input').dispatchEvent(new window.Event('change', { bubbles: true }));
    f.root.querySelector('[data-topic="donate"]').click();
    assert.deepEqual(f.actions.at(-1), { action: 'dialogue-topic', revision: 'd-1', topic: 'donate', amount: 50 });
    // Each exchange joins the log, newest last, and a repeated projection adds nothing.
    publish(dialogue('d-2', 'Any news?', 'They say the harbour flooded.'));
    publish(dialogue('d-2', 'Any news?', 'They say the harbour flooded.'));
    publish(dialogue('d-3', 'Where is The Odd Blades?', 'North of the temple.'));
    const log = [...f.root.querySelectorAll('.dagger-dialogue-log li')];
    assert.deepEqual(log.map(entry => entry.querySelector('.dagger-dialogue-reply').textContent), ['They say the harbour flooded.', 'North of the temple.']);
    assert.equal(log[1].classList.contains('is-latest'), true);
    // Summoning pays only through the confirm, which names the loss on failure.
    const pay = [...f.root.querySelectorAll('.dagger-dialogue-summoning button')].find(button => button.textContent === 'Pay and summon');
    const count = f.actions.length;
    pay.click();
    assert.match(f.root.querySelector('.dagger-confirm').textContent, /Pay 2000 gold to summon Azura\? The payment is lost if the summoning fails\./);
    f.answer(false);
    assert.equal(f.actions.length, count);
    pay.click();
    f.answer(true);
    assert.deepEqual(f.actions.at(-1), { action: 'daedra-summon', revision: 'summon-1', key: 'azura', amount: 2000, confirm: true });
    // A new conversation starts with an empty log.
    f.publish({ activation: { mode: 'talk', dialogue: null } });
    publish({ ...dialogue('d-9', null, null), targetLabel: 'Someone else' });
    assert.equal(f.root.querySelectorAll('.dagger-dialogue-log li').length, 0);
  } finally { f.dispose(); }
});

test('the shop is the transfer layout: line totals, confirms for large trades and theft, and service-only rows', () => {
  const f = fixture();
  try {
    const stack = { key: 'stack:shop.arrows', definition: 'template-131', label: 'Arrows', quantity: '100', maximumQuantity: 100, unitPrice: '7',
      currentCondition: 10, maximumCondition: 10, identified: true, stolen: false, canBuy: true, canSell: false };
    const player = { key: 'unique:17', definition: 'template-113', label: 'Worn sword', quantity: 1, maximumQuantity: 1, unitPrice: 20,
      currentCondition: 3, maximumCondition: 10, identified: false, stolen: false, canBuy: false, canSell: false };
    const dialogue = (merchant) => ({ revision: 'merchant-1', targetLabel: 'Guild officer', greeting: 'Welcome.', tone: 'normal',
      question: null, reply: null, topics: [], diagnostics: [], merchant });
    const baseMerchant = { revision: 'merchant-1', provider: 'Guild officer', quality: 10, gold: '100', buyAvailable: true,
      sellAvailable: false, repairAvailable: false, identifyAvailable: false, result: '', largeTradeGold: 500, stock: [stack], playerItems: [], repairs: [] };
    f.publish({ activation: { mode: 'talk', dialogue: dialogue(baseMerchant) } });

    const shop = f.root.querySelector('.dagger-dialogue-merchant');
    const stockRow = shop.querySelector('[data-transfer-side="source"] [data-transfer-item="stack:shop.arrows"]');
    assert.match(stockRow.textContent, /7 gold each/);
    const quantity = stockRow.querySelector('.dagger-stepper input');
    assert.equal(quantity.max, '100', 'The bound is the projected maximum.');
    assert.equal(stockRow.querySelector('.dagger-transfer-total').textContent, '7 gold');
    quantity.value = '3';
    quantity.dispatchEvent(new window.Event('change', { bubbles: true }));
    assert.equal(stockRow.querySelector('.dagger-transfer-total').textContent, '21 gold', 'The line total follows the amount.');
    // A small purchase goes straight through.
    stockRow.querySelector('[data-transfer-command="merchant-buy"]').click();
    assert.deepEqual(f.actions.at(-1), { action: 'merchant-buy', revision: 'merchant-1', item: 'stack:shop.arrows', amount: 3 });
    // A purchase at or over the ruleset's large-trade amount asks first, and No buys nothing.
    quantity.value = '80';
    quantity.dispatchEvent(new window.Event('change', { bubbles: true }));
    const count = f.actions.length;
    stockRow.querySelector('[data-transfer-command="merchant-buy"]').click();
    assert.match(f.root.querySelector('.dagger-confirm').textContent, /Buy 80 × Arrows for 560 gold\?/);
    f.answer(false);
    assert.equal(f.actions.length, count);
    stockRow.querySelector('[data-transfer-command="merchant-buy"]').click();
    f.answer(true);
    assert.deepEqual(f.actions.at(-1), { action: 'merchant-buy', revision: 'merchant-1', item: 'stack:shop.arrows', amount: 80 });
    // Stealing is set apart and always asks.
    const steal = stockRow.querySelector('[data-transfer-command="merchant-shoplift"]');
    assert.equal(steal.classList.contains('is-danger'), true);
    quantity.value = '1';
    quantity.dispatchEvent(new window.Event('change', { bubbles: true }));
    steal.click();
    assert.match(f.root.querySelector('.dagger-confirm').textContent, /Steal Arrows\? If you are caught, it is a crime\./);
    f.answer(true);
    assert.deepEqual(f.actions.at(-1), { action: 'merchant-shoplift', revision: 'merchant-1', item: 'stack:shop.arrows', amount: 1 });

    const serviceOnly = { ...baseMerchant, buyAvailable: false, repairAvailable: true, stock: [], playerItems: [{ ...player, canRepair: true, canIdentify: false, repairCost: '45', identifyCost: null }],
      repairs: [{ requestId: 'repair-1', durableItemId: '9', definition: 'iron-dagger', dueMinute: 900, ready: true, label: 'Iron Dagger', status: 'Ready' }] };
    f.publish({ activation: { mode: 'talk', dialogue: dialogue(serviceOnly) } });
    const repairRow = shop.querySelector('[data-transfer-side="pack"] [data-transfer-item="unique:17"]');
    assert.match(repairRow.textContent, /condition 3\/10/);
    assert.equal(repairRow.querySelector('.dagger-stepper'), null, 'A single item has no quantity.');
    const repair = repairRow.querySelector('[data-transfer-command="merchant-repair"]');
    assert.equal(repair.textContent, 'Repair · 45 gold');
    repair.click();
    assert.deepEqual(f.actions.at(-1), { action: 'merchant-repair', revision: 'merchant-1', item: 'unique:17' });
    assert.match(shop.querySelector('.dagger-shop-repairs').textContent, /Iron Dagger · Ready/);
    shop.querySelector('[data-repair="repair-1"]').click();
    assert.deepEqual(f.actions.at(-1), { action: 'merchant-collect-repair', revision: 'merchant-1', key: 'repair-1' });

    const identifyOnly = { ...serviceOnly, repairAvailable: false, identifyAvailable: true, repairs: [], playerItems: [{ ...player, canRepair: false, canIdentify: true, identifyCost: '0' }] };
    f.publish({ activation: { mode: 'talk', dialogue: dialogue(identifyOnly) } });
    const identify = shop.querySelector('[data-transfer-command="merchant-identify"]');
    assert.equal(identify.textContent, 'Identify · 0 gold');
    identify.click();
    assert.deepEqual(f.actions.at(-1), { action: 'merchant-identify', revision: 'merchant-1', item: 'unique:17' });
  } finally { f.dispose(); }
});

test('create item presents authoritative paid choices and submits selection without cancellation or optimistic inventory', () => {
  const f = fixture();
  try {
    f.publish({mode:'modal', createItem:{revision:'cast.42', options:[{id:'steel-102',label:'Steel Cuirass'},{id:'robes',label:'Robes'}]}});
    const panel=f.root.querySelector('.dagger-create-item');
    assert.equal(panel.hidden,false); assert.equal(panel.querySelectorAll('button').length,2);
    panel.querySelectorAll('button')[1].click();
    assert.deepEqual(f.actions.at(-1),{action:'create-item-select',revision:'cast.42',key:'robes'});
    assert.equal(panel.hidden,false);
    f.publish({createItem:null}); assert.equal(panel.hidden,true); assert.equal(panel.children.length,0);
  } finally { f.dispose(); }
});


test('spellmaker draws its fields from the published limits, picks an icon, keeps typing on repeated projection, previews and confirms one purchase', () => {
  const f=fixture();
  try {
    const settings={key:'free-action',type:26,subType:-1,durationBase:3,durationMod:7,durationPerLevel:2,
      chanceBase:1,chanceMod:1,chancePerLevel:1,magnitudeBaseLow:1,magnitudeBaseHigh:1,magnitudeLevelBase:1,magnitudeLevelHigh:1,magnitudePerLevel:1};
    const targets=['Self','Touch','Single target at range','Area around self','Area at range'].map((label,value)=>({value,label}));
    const elements=['Fire','Cold','Poison','Shock','Magic'].map((label,value)=>({value,label}));
    const limits={nameLength:31,icons:69,effects:3,duration:60,chance:100,magnitude:100,perLevels:20};
    const maker={revision:'maker-1',provider:'Mage',targets,elements,limits,effects:[{key:'free-action',name:'Free action',type:26,subType:-1,school:'restoration',duration:true,chance:false,magnitude:false,targets:31,elements:16}],
      draft:{name:'Freedom',element:4,rangeType:0,icon:68,effects:[settings]},quote:{key:'draft-2',gold:300,spellPoints:15,eligible:true,reason:null}};
    const publish=()=>f.publish({spells:{available:[],ready:null,result:'',maker},activation:{mode:'talk',message:'',applied:true,
      dialogue:{revision:'maker-1',targetLabel:'Mage',greeting:'Welcome',tone:'normal',question:null,reply:null,topics:[],diagnostics:[]}}});
    publish();const root=f.root.querySelector('.dagger-dialogue-spellmaker');
    assert.match(root.textContent,/300 gold · 15 magicka/);
    const buy=[...root.querySelectorAll('button')].find(b=>b.textContent==='Buy constructed spell');
    buy.click();f.answer(false);assert.equal(f.actions.some(a=>a.action==='spellmaker-buy'),false);
    buy.click();f.answer(true);assert.deepEqual(f.actions.at(-1),{action:'spellmaker-buy',revision:'maker-1',key:'draft-2',amount:300,confirm:true});
    const form=root.querySelector('form');const name=form.querySelector('[name="name"]');
    assert.equal(name.maxLength,31);
    name.value='New name';name.dispatchEvent(new window.Event('input',{bubbles:true}));assert.equal(buy.disabled,true);
    publish();assert.equal(root.querySelector('[name="name"]'),name);assert.equal(name.value,'New name');
    // The published limits shape the form: three effect slots, 69 icons, and only the settings this effect has, each bounded.
    assert.equal(root.querySelectorAll('.dagger-maker-effect').length,3);
    assert.equal(root.querySelectorAll('[data-spell-icon]').length,69);
    assert.equal(root.querySelector('[data-spell-icon="68"]').getAttribute('aria-checked'),'true');
    const durationBase=root.querySelector('[aria-label="Effect 1: Duration base"]');
    assert.equal(durationBase.max,'60');assert.equal(durationBase.value,'3');
    assert.equal(root.querySelector('[aria-label="Effect 1: Duration per levels"]').max,'20');
    assert.equal(root.querySelector('[aria-label="Effect 1: Chance base"]'),null);
    root.querySelector('[data-spell-icon="4"]').click();
    assert.equal(root.querySelector('[data-spell-icon="4"]').getAttribute('aria-checked'),'true');
    form.dispatchEvent(new window.Event('submit',{bubbles:true,cancelable:true}));
    const action=f.actions.at(-1);assert.equal(action.action,'spellmaker-draft');assert.equal(action.revision,'maker-1');
    assert.deepEqual(JSON.parse(action.text),{name:'New name',element:4,rangeType:0,icon:4,effects:[settings]});
    assert.equal(root.querySelector('[aria-label="Element"] option[value="0"]').textContent,'Fire');
    assert.equal(root.querySelector('[aria-label="Element"]').selectedOptions[0].textContent,'Magic');
    assert.ok([...root.querySelectorAll('option')].some(option=>option.textContent==='restoration: Free action'));
    f.publish({spells:{available:[],ready:null,result:'',maker:null}});assert.equal(root.childElementCount,0);
  } finally {f.dispose();}
});

test('quest escort portraits use published art and remove only ended quest overlays', () => {
  const f = fixture();
  try {
    const face = { instance: 'first', symbol: 'contact', name: 'Existing Giver', mediaId: 'character.head.male.00.0' };
    const art = { revision: 'escort-art', images: [{ id: face.mediaId, image: 'data:image/png;base64,cG9ydHJhaXQ=' }] };
    f.publish({ uiArt: art, quests: { deliveries: [], journal: { active: [], finished: [] }, pending: null, escortFaces: [face, { ...face, instance: 'second' }] } });
    let portraits = f.root.querySelectorAll('.dagger-escort-faces img');
    assert.equal(portraits.length, 2);
    assert.equal(portraits[0].alt, face.name);
    assert.equal(portraits[0].src, art.images[0].image);
    f.publish({ uiArt: art, quests: { deliveries: [], journal: { active: [], finished: [] }, pending: null, escortFaces: [{ ...face, instance: 'second' }] } });
    portraits = f.root.querySelectorAll('.dagger-escort-faces img');
    assert.equal(portraits.length, 1);
    assert.equal(portraits[0].dataset.questInstance, 'second');
    f.publish({ uiArt: art, quests: { deliveries: [], journal: { active: [], finished: [] }, pending: null, escortFaces: [] } });
    assert.equal(f.root.querySelector('.dagger-escort-faces'), null);
  } finally { f.dispose(); }
});


test('racial form projection closes and suppresses inventory until human form returns', () => {
  const f = fixture();
  try {
    const character = { name: 'Aubk-i', attributes: [], skills: [], resources: [], progression: { level: 1, experience: 0 }, equipment: [],
      identity: { race: 'breton', raceLabel: 'Breton', donorRaceId: 1, portrait: '', gender: 'female', faceIndex: 0, career: 'mage', media: [], selectedMedia: [],
        racialOverride: { name: 'Werewolf', beastForm: true, suppressInventory: true } } };
    f.root.querySelector('[data-action="inventory"]').click();
    character.identity.selectedMedia = [{layer:'background',mediaId:'beast.body'},{layer:'head',mediaId:'beast.head'}];
    const uiArt = {revision:'beast-art',images:[{id:'beast.body',image:'data:image/png;base64,Ym9keQ=='},{id:'beast.head',image:'data:image/png;base64,aGVhZA=='}]};
    f.publish({ character, uiArt });
    assert.deepEqual([...f.root.querySelectorAll('.dagger-character-art img')].map(img=>img.dataset.mediaId), ['beast.body','beast.head']);
    assert.equal(f.root.querySelector('[data-action="inventory"]').disabled, true);
    assert.match(f.root.querySelector('.dagger-character-overview').textContent, /Werewolf · Beast form/);
    assert.equal(f.root.querySelector('.dagger-menu').classList.contains('has-inventory'), false);
    character.identity.racialOverride.beastForm = false;
    character.identity.racialOverride.suppressInventory = false;
    character.identity.selectedMedia = [];
    f.publish({ character });
    assert.equal(f.root.querySelectorAll('.dagger-character-art img').length,0);
    assert.equal(f.root.querySelector('[data-action="inventory"]').disabled, false);
    f.root.querySelector('[data-action="inventory"]').click();
    assert.equal(f.root.querySelector('.dagger-menu').classList.contains('has-inventory'), true);
  } finally { f.dispose(); }
});

test('character entry displays the authoritative refusal above its opaque screen', () => {
  const f = fixture();
  try {
    const refusal = 'Character choice was not accepted: Allocate all attribute bonus points before committing.';
    f.publish({ mode: 'title', lastOutcome: refusal });
    const entry = f.root.querySelector('.dagger-entry');
    assert.equal(entry.hidden, false);
    assert.equal(entry.querySelector('.dagger-entry-outcome').textContent, refusal);
    assert.equal(entry.querySelector('.dagger-entry-outcome').getAttribute('role'), 'status');
  } finally { f.dispose(); }
});

test('legal interaction shows charges and submits only authoritative arrest trial and prison choices', () => {
  const f = fixture();
  try {
    const legal = {revision:'charge:Arrest:1',phase:'arrest',title:'Halt! City watch',charges:['Theft'],message:'Surrender or resist.',
      choices:[{id:'yield',label:'Surrender'},{id:'resist',label:'Resist arrest'},{id:'escape',label:'Run for it'}]};
    f.publish({mode:'modal',legal});
    const panel=f.root.querySelector('.dagger-legal');
    assert.equal(panel.hidden,false); assert.equal(panel.getAttribute('aria-modal'),'true');
    assert.match(panel.textContent,/Theft/); assert.equal(panel.querySelectorAll('button').length,3);
    panel.querySelector('button').click();
    assert.deepEqual(f.actions.at(-1),{action:'legal-choice',revision:legal.revision,key:'yield'});
    assert.equal(panel.dataset.phase,'arrest');
    f.publish({mode:'modal',legal:{...legal,revision:'charge:Court:1',phase:'court',title:'Before the court',
      choices:[{id:'guilty',label:'Plead guilty'},{id:'etiquette',label:'Debate'},{id:'streetwise',label:'Lie'}]}});
    panel.querySelectorAll('button')[2].click();
    assert.deepEqual(f.actions.at(-1),{action:'legal-choice',revision:'charge:Court:1',key:'streetwise'});
    f.publish({mode:'modal',legal:{...legal,revision:'charge:Prison:1',phase:'prison',title:'In prison',choices:[{id:'continue',label:'Continue sentence'}]}});
    assert.equal(panel.dataset.phase,'prison'); panel.querySelector('button').click();
    assert.deepEqual(f.actions.at(-1),{action:'legal-choice',revision:'charge:Prison:1',key:'continue'});
    f.publish({legal:null}); assert.equal(panel.hidden,true); assert.equal(panel.children.length,0);
  } finally {f.dispose();}
});

test('potion maker selects an owned recipe or an ingredient experiment through the dialogue', () => {
  const f=fixture();
  try {
    const potionMaker={revision:'mix-1',provider:'Apothecary',eligible:true,minimumIngredients:1,maximumIngredients:8,
      ingredients:[{template:62,name:'Aloe',quantity:2},{template:31,name:'Petals',quantity:1}],
      recipes:[{key:1,name:'Test recipe',ingredients:[62,31],available:true}]};
    const publish=()=>f.publish({spells:{available:[],ready:null,result:'',potionMaker},activation:{mode:'talk',message:'',applied:true,
      dialogue:{revision:'mix-1',targetLabel:'Apothecary',greeting:'Welcome',tone:'normal',question:null,reply:null,topics:[],diagnostics:[]}}});
    publish();const root=f.root.querySelector('.dagger-dialogue-potionmaker');const recipe=root.querySelector('select');
    recipe.value='1';recipe.dispatchEvent(new window.Event('change'));
    const aloe=root.querySelector('[aria-label="Aloe"]');assert.equal(aloe.value,'1');publish();assert.equal(root.querySelector('[aria-label="Aloe"]'),aloe);
    root.querySelector('form').dispatchEvent(new window.Event('submit',{cancelable:true}));
    assert.deepEqual(f.actions.at(-1),{action:'potion-mix',revision:'mix-1',text:'[62,31]'});
    aloe.value='2';aloe.dispatchEvent(new window.Event('input'));assert.equal(recipe.value,'');
    root.querySelector('form').dispatchEvent(new window.Event('submit',{cancelable:true}));assert.equal(f.actions.at(-1).text,'[62,62,31]');
    // Each ingredient's stepper is bounded by what is carried and the ruleset's most per mix.
    assert.equal(aloe.max,'2');assert.match(root.querySelector('.dagger-potion-count').textContent,/of 8 ingredients/);
    f.publish({spells:{available:[],ready:null,result:'',potionMaker:{...potionMaker,eligible:false}}});assert.equal(root.querySelector('form'),null);
  } finally {f.dispose();}
});

test('item maker edits named settings, shows forced payloads, and confirms the quoted purchase', () => {
  const f=fixture();
  try {
    const setting={key:'enchantment.15.18',name:'Soul Bound: Ghost',cost:-300,forced:['Feather Weight','Item Deteriorates','Low Damage']};
    const itemMaker={revision:'enchant-1',provider:'Enchanter',eligible:true,items:[{key:'stack:rubies',name:'Ruby',capacity:1800,quantity:3}],settings:[setting],
      draft:{item:'stack:rubies',name:'Ghost ruby',settings:[setting.key]},quote:{key:'draft-1',capacity:1800,power:-300,gold:1000,eligible:true,reason:null,payloads:[setting,{key:'forced',name:'Feather Weight',cost:100,forced:[]}]}};
    const publish=()=>f.publish({spells:{available:[],ready:null,result:'',itemMaker},activation:{mode:'talk',message:'',applied:true,
      dialogue:{revision:'enchant-1',targetLabel:'Enchanter',greeting:'Welcome',tone:'normal',question:null,reply:null,topics:[],diagnostics:[]}}});
    publish();const root=f.root.querySelector('.dagger-dialogue-itemmaker');assert.match(root.textContent,/-300\/1800 power · 1000 gold/);assert.match(root.textContent,/Feather Weight/);
    const buy=[...root.querySelectorAll('button')].find(b=>b.textContent==='Enchant item');buy.click();f.answer(false);assert.equal(f.actions.length,0);
    buy.click();f.answer(true);assert.deepEqual(f.actions.at(-1),{action:'itemmaker-buy',revision:'enchant-1',key:'draft-1',amount:1000,confirm:true});
    const name=root.querySelector('[name="enchanted-name"]');name.value='Changed';name.dispatchEvent(new window.Event('input',{bubbles:true}));assert.equal(buy.disabled,true);
    publish();assert.equal(root.querySelector('[name="enchanted-name"]'),name);
    root.querySelector('form').dispatchEvent(new window.Event('submit',{cancelable:true}));assert.deepEqual(JSON.parse(f.actions.at(-1).text),{item:'stack:rubies',name:'Changed',settings:[setting.key]});
  } finally {f.dispose();}
});

test('summoning offers retain semantic accept and refuse actions and close with the projection', async () => {
  const f=fixture();
  try {
    const { renderSummoning, isSummoningProjection }=await import(pathToFileURL(join(output,'main.js')));
    const view={revision:'dialogue.3',provider:'Witch',quote:null,offerRevision:'7',prince:'Clavicus Vile',message:'Bring the Masque.',diagnostics:['%qdt has no context.']};
    assert.equal(isSummoningProjection(view),true);
    f.publish({spells:{available:[],ready:null,result:'',summoning:view}});
    assert.equal(f.root.querySelector('.dagger-daedric-offer').open,true);
    assert.equal(f.modes.at(-1),'interface');
    assert.doesNotMatch(f.root.querySelector('.dagger-daedric-offer').textContent,/has no context/);
    assert.match(f.root.querySelector('.dagger-text-diagnostics').textContent,/%qdt has no context/);
    f.publish({spells:{available:[],ready:null,result:'',summoning:null}});
    assert.equal(f.root.querySelector('.dagger-daedric-offer').open,false);
    assert.equal(f.modes.at(-1),'gameplay');
    const root=document.createElement('div');const actions=[];
    renderSummoning(root,view,action=>actions.push(action));
    assert.match(root.textContent,/Bring the Masque/);
    root.querySelectorAll('button')[0].click();root.querySelectorAll('button')[1].click();
    assert.deepEqual(actions,[{action:'daedra-answer',revision:'7',confirm:true},{action:'daedra-answer',revision:'7',confirm:false}]);
    renderSummoning(root,{...view,offerRevision:null,quote:{key:'7:1:2:4',name:'Hircine',quest:'X0C00Y00',gold:200000,eligible:false,reason:'Today is not a Daedric summoning day.'}},action=>actions.push(action));
    assert.match(root.textContent,/not a Daedric summoning day/);assert.equal(root.querySelector('button').disabled,true);
    renderSummoning(root,null,()=>{});assert.equal(root.childElementCount,0);
  }finally{f.dispose();}
});

test('vampire character projection displays clan and published media while retaining inventory access', () => {
  const f = fixture();
  try {
    const character = { name:'Aubk-i',attributes:[],skills:[],resources:[],progression:{level:1,experience:0},equipment:[],
      identity:{race:'dark-elf',raceLabel:'Dark Elf',donorRaceId:4,portrait:'',gender:'female',faceIndex:0,career:'mage',media:[],
        selectedMedia:[{layer:'background',mediaId:'vampire.background'},{layer:'head.female.0',mediaId:'vampire.head'}],
        racialOverride:{name:'Vampire',vampireClan:'Lyrezi',beastForm:false,suppressInventory:false}}};
    f.publish({character,uiArt:{revision:'vampire',images:[{id:'vampire.background',image:'data:image/png;base64,Ym9keQ=='},{id:'vampire.head',image:'data:image/png;base64,aGVhZA=='}]}});
    assert.match(f.root.querySelector('.dagger-character-overview').textContent,/Vampire · Lyrezi/);
    assert.match(f.root.querySelector('.dagger-character-overview').textContent,/Dark Elf/);
    assert.doesNotMatch(f.root.querySelector('.dagger-character-overview').textContent,/dark-elf/);
    assert.equal(f.root.querySelector('[data-action="inventory"]').disabled,false);
    assert.deepEqual([...f.root.querySelectorAll('.dagger-character-art img')].map(img=>img.dataset.mediaId),['vampire.background','vampire.head']);
    character.identity.racialOverride=null;character.identity.selectedMedia=[];f.publish({character});
    assert.doesNotMatch(f.root.querySelector('.dagger-character-overview').textContent,/Vampire/);
  } finally { f.dispose(); }
});

test('quest offer preserves its identity in accept and decline actions', () => {
  const f = fixture();
  try {
    f.publish({ mode: 'modal', quests: { deliveries: [], journal: { active: [], finished: [] }, pending: null, escortFaces: [],
      offer: { instance: 'work:7', text: 'Retrieve the selected heirloom for this provider.', diagnostics: [] } } });
    const article = [...f.root.querySelectorAll('.dagger-quest-message')].find(x => x.textContent.includes('Retrieve the selected'));
    assert.ok(article);
    const buttons = [...article.querySelectorAll('button')];
    buttons.find(b => b.textContent === 'Accept').click();
    assert.deepEqual(f.actions.at(-1), { action: 'quest-offer-answer', questInstance: 'work:7', confirm: true });
    buttons.find(b => b.textContent === 'Decline').click();
    assert.deepEqual(f.actions.at(-1), { action: 'quest-offer-answer', questInstance: 'work:7', confirm: false });
  } finally { f.dispose(); }
});


test('guild topics keep their semantic action and confirmation key', () => {
  const f = fixture();
  try {
    for (const [id, key, label] of [['join', null, 'Join guild'], ['join', 'yes', 'Yes, join'], ['join', 'no', 'No'], ['teleport', null, 'Guild teleportation'], ['spymaster', null, 'Consult spymaster']]) {
      f.publish({ activation: { mode: 'talk', modes: [], target: null, message: '', dialogue: {
        revision: 'guild-1', targetLabel: 'Guild provider', greeting: 'Welcome', tone: 'normal', question: '', reply: '', diagnostics: [],
        topics: [{ id, key, label }], questContacts: [] } } });
      f.root.querySelector('.dagger-dialogue-topics button').click();
      assert.equal(f.actions.at(-1).action, 'dialogue-topic');
      assert.equal(f.actions.at(-1).topic, id);
      assert.equal(f.actions.at(-1).key ?? null, key);
      assert.equal(f.actions.at(-1).revision, 'guild-1');
    }
  } finally { f.dispose(); }
});

test('guild teleport hides travel options and confirms a zero-cost destination once', async () => {
  const { mountTravel } = await import(pathToFileURL(join(output, 'travel.js')));
  const f = fixture();
  try {
    const root = document.createElement('div'); f.root.append(root);
    const actions = []; const view = mountTravel(root, action => actions.push(action));
    view.update({ guildTeleport: true, destinations: [{ region: 17, index: 3, name: 'Charing', kind: 'Town', regionName: 'Daggerfall' }],
      map: null, quote: { identity: 'guild-destination', destination: 'Charing', minutes: 0, duration: '0 minutes', distance: 0, oceanPixels: 0,
        innCost: 0, shipCost: 0, totalCost: 0, canAfford: true,
        options: { cautious: true, inn: false, ship: false, hasHorse: false, hasCart: false, hasShip: false, availableGold: '0', availableGoldPieces: '0' } },
      executionAvailable: true, message: null, lastResult: null });
    assert.equal(root.querySelector('h3').textContent, 'Guild teleportation');
    assert.equal(root.querySelector('.dagger-travel-accept').textContent, 'Teleport');
    assert.ok([...root.querySelectorAll('label')].filter(label => label.querySelector('input[type="checkbox"]')).every(label => label.hidden));
    root.querySelector('.dagger-travel-accept').click();
    root.querySelector('.dagger-travel-accept').click();
    assert.deepEqual(actions, [{ action: 'travel-accept', key: 'guild-destination', amount: 0 }]);
    view.dispose();
  } finally { f.dispose(); }
});

for (const path of ['Escape', 'toggle', 'panel request']) test(`menu open via ${path} reports the world hold once`, () => {
  const f = fixture();
  try {
    f.publish();
    if (path === 'Escape') document.dispatchEvent(new KeyboardEvent('keydown', { code: 'Escape', bubbles: true, cancelable: true }));
    else if (path === 'toggle') f.root.querySelector('.dagger-menu-toggle').click();
    else f.publish({ panelRequest: { panel: 'transport', revision: '5' } });
    assert.equal(f.root.querySelector('.dagger-menu').open, true);
    assert.deepEqual(f.actions.filter(action => action.action === 'menu'), [{ action: 'menu', open: true }]);
    f.publish({ mode: 'modal', panelRequest: path === 'panel request' ? { panel: 'transport', revision: '5' } : null });
    assert.deepEqual(f.actions.filter(action => action.action === 'menu'), [{ action: 'menu', open: true }]);
    f.root.querySelector('[data-action="resume"]').click();
    assert.deepEqual(f.actions.filter(action => action.action === 'menu').at(-1), { action: 'menu', open: false });
  } finally { f.dispose(); }
});


test('identical courier letters share one footer and dismiss all source deliveries', () => {
  const f = fixture();
  try {
    const letter = { instance: 'quest:1', message: 1026, delivery: 'letter', heading: 'Letter', text: 'Meet me in Daggerfall.',
      signoff: 'Brisienna', diagnostics: [], entryId: 'letter:1' };
    const second = { ...letter, instance: 'quest:2', entryId: 'letter:2' };
    const other = { ...letter, instance: 'quest:3', entryId: 'letter:3', text: 'Another message.' };
    const quests = { deliveries: [letter, second, other], journal: { active: [], finished: [] }, pending: null };
    f.publish({ quests });
    const letters = f.root.querySelectorAll('.dagger-quest-letter');
    assert.equal(letters.length, 2);
    assert.equal(letters[0].lastElementChild.tagName, 'BUTTON');
    assert.equal(letters[0].lastElementChild.previousElementSibling.textContent, 'Brisienna');
    const button = letters[0].lastElementChild;
    f.publish({ quests });
    assert.equal(f.root.querySelector('.dagger-quest-letter button'), button);
    button.click();
    assert.deepEqual(f.actions.slice(-2), [
      { action: 'quest-dismiss', questInstance: 'quest:1', questDelivery: 'letter:1' },
      { action: 'quest-dismiss', questInstance: 'quest:2', questDelivery: 'letter:2' },
    ]);
  } finally { f.dispose(); }
});

test('ship option refreshes the quote and explains routes without sea passage', async () => {
  const { mountTravel } = await import(pathToFileURL(join(output, 'travel.js')));
  const f = fixture();
  try {
    const root = document.createElement('div'); f.root.append(root);
    const actions = []; const view = mountTravel(root, action => actions.push(action));
    const quote = { identity: 'land', destination: 'Charing', duration: '2 hours', oceanPixels: 0,
      innCost: 0, shipCost: 0, totalCost: 0, canAfford: true, options: { cautious: true, inn: false, ship: false } };
    const value = { destinations: [{ region: 17, index: 3, name: 'Charing', kind: 'Town', regionName: 'Daggerfall' }],
      quote, executionAvailable: true, message: null, lastResult: null };
    view.update(value);
    const ship = root.querySelectorAll('input[type=checkbox]')[2];
    ship.click();
    assert.deepEqual(actions.at(-1), { action: 'travel-preview', region: 17, destination: 3, cautious: true, inn: false, ship: true });
    assert.equal(root.querySelector('.dagger-travel-accept').disabled, true);
    view.update({ ...value, quote: { ...quote, identity: 'land-ship', options: { ...quote.options, ship: true } } });
    assert.match(root.querySelector('[role=status]').textContent, /entirely over land/);
    assert.equal(root.querySelector('.dagger-travel-accept').disabled, false);
    view.dispose();
  } finally { f.dispose(); }
});

test('arrival projection immediately replaces the departure place and calendar', () => {
  const f = fixture();
  try {
    f.publish({ site: { name: "Privateer's Hold" }, calendar: { date: '1 Morning Star', time: '00:00' } });
    f.publish({ site: { name: 'Daggerfall' }, calendar: { date: '9 Morning Star', time: '05:00' }, panelRequest: { revision: 1, panel: 'resume' } });
    assert.equal(f.root.querySelector('.dagger-site').textContent, 'Daggerfall');
    assert.equal(f.root.querySelector('.dagger-calendar').textContent, '9 Morning Star · 05:00');
  } finally { f.dispose(); }
});

test('HUD projects the timed message stack, breath and activation mode without local timers', () => {
  const f = fixture();
  try {
    f.publish({ messages: ['Door unlocked.', 'Your skill improved.'], lastOutcome: 'Your skill improved.',
      breath: { current: 8, maximum: 30, low: true }, activation: { mode: 'steal', message: '', applied: true } });
    assert.deepEqual([...f.root.querySelectorAll('.dagger-outcome li')].map(line => line.textContent), ['Door unlocked.', 'Your skill improved.']);
    assert.equal(f.root.querySelector('.dagger-breath').hidden, false);
    assert.equal(f.root.querySelector('.dagger-breath meter').value, 8);
    assert.equal(f.root.querySelector('.dagger-breath meter').max, 30);
    assert.equal(f.root.querySelector('.dagger-breath').classList.contains('low'), true);
    assert.equal(f.root.querySelector('.dagger-activation-indicator').textContent, 'Steal / Lockpick');
    f.publish({ messages: ['Your skill improved.'], lastOutcome: 'Your skill improved.', breath: null,
      activation: { mode: 'talk', message: '', applied: true } });
    assert.deepEqual([...f.root.querySelectorAll('.dagger-outcome li')].map(line => line.textContent), ['Your skill improved.']);
    assert.equal(f.root.querySelector('.dagger-breath').hidden, true);
    assert.equal(f.root.querySelector('.dagger-activation-indicator').textContent, 'Talk');
    f.publish({ messages: [] });
    assert.equal(f.root.querySelectorAll('.dagger-outcome li').length, 0);
  } finally { f.dispose(); }
});


test('the skin publishes the kit as custom properties and reports the primitives it lacks', () => {
  const f = fixture();
  const warnings = [];
  const warn = console.warn;
  console.warn = message => warnings.push(message);
  try {
    const kit = [
      { id: 'ui.stone.window.v2', image: pngHeader(256, 256), display: { scale: 0.5, slice: [36, 36, 36, 36], hotspot: null } },
      { id: 'ui.stone.button.focus.v2', image: pngHeader(256, 72), display: { scale: 0.5, slice: [18, 22, 18, 22], hotspot: null } },
      { id: 'ui.stone.texture.v2', image: pngHeader(256, 256), display: { scale: 0.5, slice: null, hotspot: null } },
      { id: 'ui.cursor.grab.v2', image: pngHeader(64, 64), display: { scale: 0.5, slice: null, hotspot: [32, 32] } },
      { id: 'ui.icon.menu.map.v2', image: pngHeader(128, 128), display: { scale: 0.5, slice: null, hotspot: null } },
      { id: 'screen.title', image: pngHeader(320, 200) },
    ];
    f.publish({ uiArt: { revision: 'kit-1', images: kit } });
    const root = document.documentElement.style;
    assert.equal(root.getPropertyValue('--skin-stone-window'), `url("${kit[0].image}")`);
    assert.equal(root.getPropertyValue('--skin-stone-window-slice'), '36 36 36 36');
    assert.equal(root.getPropertyValue('--skin-stone-window-width'), '1.125rem 1.125rem 1.125rem 1.125rem');
    assert.equal(root.getPropertyValue('--skin-stone-window-size'), '8rem 8rem');
    assert.equal(root.getPropertyValue('--skin-stone-button-focus-width'), '.5625rem .6875rem .5625rem .6875rem'.replaceAll('.', '0.'));
    assert.equal(root.getPropertyValue('--skin-stone-texture-size'), '8rem 8rem');
    assert.equal(root.getPropertyValue('--skin-stone-texture-slice'), '');
    assert.equal(root.getPropertyValue('--skin-icon-menu-map'), `url("${kit[4].image}")`);
    // The 2x cursor is drawn at half size with its hotspot halved, falling back to the system cursor.
    assert.equal(root.getPropertyValue('--dagger-cursor-grab'), `image-set(url("${kit[3].image}") 2x) 16 16, grabbing`);
    assert.equal(root.getPropertyValue('--skin-button-face'), '');
    assert.equal(document.documentElement.hasAttribute('data-skin'), true);
    // Classic art is not kit art and publishes no skin property.
    assert.equal(skin.skinProperty('screen.title'), null);
    // The primitives this block lacks are named once; their CSS keeps its plain look.
    assert.equal(warnings.length, 1);
    assert.match(warnings[0], /^skin frame art is not published by this session: /);
    assert.match(warnings[0], /ui\.stone\.button\.v2/);
    assert.doesNotMatch(warnings[0], /ui\.stone\.window\.v2/);
    // A later block that drops an image clears its properties.
    f.publish({ uiArt: { revision: 'kit-2', images: kit.slice(1) } });
    assert.equal(root.getPropertyValue('--skin-stone-window'), '');
    assert.equal(root.getPropertyValue('--skin-stone-window-slice'), '');
  } finally { console.warn = warn; f.dispose(); art.adopt({ revision: '', images: [] }); skin.applySkin(); document.documentElement.removeAttribute('style'); }
});

test('the interface scale fits the reference layout to the window unless the player chose one', () => {
  const range = { minimum: 0.5, maximum: 3, step: 0.25 };
  assert.equal(uiScale.chooseScale(1920, 1080, null, range), 1);
  assert.equal(uiScale.chooseScale(2560, 1440, null, range), 1.25);
  assert.equal(uiScale.chooseScale(3840, 2160, null, range), 2);
  assert.equal(uiScale.chooseScale(1280, 720, null, range), 0.5);
  assert.equal(uiScale.chooseScale(3440, 1440, null, range), 1.25, 'an ultrawide window is bounded by its height');
  assert.equal(uiScale.chooseScale(200, 100, null, range), 0.5, 'the bounds hold');
  assert.equal(uiScale.chooseScale(1920, 1080, 1.5, range), 1.5, "the player's choice wins");
  assert.equal(uiScale.chooseScale(1920, 1080, 9, range), 3);
  const f = fixture();
  try {
    window.innerWidth = 3840; window.innerHeight = 2160;
    f.publish({ controls: { diagnostic: '', bindings: [], uiScale: null, uiScaleRange: range } });
    assert.deepEqual(f.scales, [2]);
    f.publish({ controls: { diagnostic: '', bindings: [], uiScale: 1.5, uiScaleRange: range } });
    assert.deepEqual(f.scales, [2, 1.5]);
    // Settings steps the scale through the ruleset and offers automatic back.
    f.root.querySelector('[data-action="settings"]').click();
    const group = f.root.querySelector('.dagger-controls-scale');
    assert.equal(group.hidden, false);
    assert.equal(group.querySelector('output').textContent, '1.5×');
    group.querySelector('[aria-label="Larger interface"]').click();
    assert.deepEqual(f.actions.at(-1), { action: 'controls-ui-scale', scale: 1.75 });
    group.querySelector('[aria-label="Smaller interface"]').click();
    assert.deepEqual(f.actions.at(-1), { action: 'controls-ui-scale', scale: 1.25 });
    [...group.querySelectorAll('button')].find(button => button.textContent === 'Automatic').click();
    assert.deepEqual(f.actions.at(-1), { action: 'controls-ui-scale' });
    f.publish({ controls: { diagnostic: '', bindings: [], uiScale: 3, uiScaleRange: range } });
    assert.equal(group.querySelector('[aria-label="Larger interface"]').disabled, true);
  } finally { f.dispose(); }
});

test('look settings claim complete preferences and refresh after reload', () => {
  const f = fixture();
  try {
    f.publish({ controls: { diagnostic: '', bindings: [], lookSensitivity: 1.5, invertVertical: false } });
    f.root.querySelector('[data-action="settings"]').click();
    const sensitivity = f.root.querySelector('[aria-label="Mouse sensitivity multiplier"]');
    const invert = f.root.querySelector('[aria-label="Invert vertical look"]');
    assert.equal(sensitivity.value, '1.5'); assert.equal(invert.checked, false);
    sensitivity.value = '2'; invert.checked = true;
    [...f.root.querySelectorAll('button')].find(b => b.textContent === 'Apply look settings').click();
    assert.deepEqual(f.actions.at(-1), { action: 'controls-look', sensitivity: 2, invert: true });
    f.publish({ controls: { diagnostic: 'Saved', bindings: [], lookSensitivity: 2, invertVertical: true } });
    assert.equal(sensitivity.value, '2'); assert.equal(invert.checked, true);
  } finally { f.dispose(); }
});

test('magic-item shortcut lists only eligible carried items and uses the current inventory owner', () => {
  const f = fixture();
  try {
    const row = (key, canUseMagic) => ({ key, definition: key, label: key, quantity: '1', weight: 1, value: 1, details: '', icon: null,
      condition: null, identified: true, gridSlot: 0, equippedSlots: [], compatibleSlots: [], canUseMagic });
    const state = { panelRequest: { panel: 'magic-items', revision: '1' }, inventory: {
      revision: '6:1', message: '', equipmentChange: null, slots: [], items: [row('potion', true), row('book', false)] } };
    f.publish(state);
    const root = f.root.querySelector('.dagger-magic-items');
    assert.equal(root.hidden, false);
    const button = root.querySelector('button');
    assert.equal(button.textContent, 'potion');
    assert.equal(root.querySelectorAll('button').length, 1);
    f.publish(state);
    assert.equal(root.querySelector('button'), button);
    button.click();
    assert.deepEqual(f.actions.findLast(action => action.action === 'inventory-use'), { action: 'inventory-use', revision: '6:1', item: 'potion' });
    assert.equal(f.root.querySelector('.dagger-menu').open, false);
  } finally { f.dispose(); }
});

test('training asks through the drawn confirm with the ruleset price and hours; a changed offer pays nothing; tavern sends its quoted item', () => {
  const f = fixture();
  try {
    const dialogue = { revision: 'trainer-1', targetLabel: 'Trainer', greeting: 'Welcome', tone: 'normal',
      question: null, reply: null, topics: [], diagnostics: [],
      training: { providerFaction: 1, membershipFaction: 1, member: true, rank: 0, price: 100, durationSeconds: 10800, durationHours: 3,
        cooldownReadySecond: 0, skills: [{ id: 'medical', label: 'Medical', permanentValue: 10, maximumValue: 50, trainable: true },
          { id: 'archery', label: 'Archery', permanentValue: 50, maximumValue: 50, trainable: false }] },
      tavern: { hungry: true, menu: [{ id: 'wine', label: 'Wine', price: 3 }] } };
    const publish = () => f.publish({ mode: 'playing', activation: { mode: 'talk', message: '', applied: true, dialogue } });
    publish();
    assert.equal(f.root.querySelector('[data-training-skill="archery"]').disabled, true, 'The ruleset says this skill is mastered here.');
    assert.match(f.root.querySelector('.dagger-dialogue-training-summary').textContent, /100 gold for 3 hours/);
    f.root.querySelector('[data-training-skill="medical"]').click();
    assert.equal(f.actions.length, 0);
    assert.match(f.root.querySelector('.dagger-confirm').textContent, /Train Medical for 100 gold\? It takes 3 hours\./);
    publish(); // A repeated projection must not dismiss the pending choice.
    f.answer(false);
    assert.equal(f.actions.length, 0);
    f.root.querySelector('[data-training-skill="medical"]').click();
    f.answer(true);
    assert.deepEqual(f.actions.pop(), { action: 'training-commit', revision: 'trainer-1', key: 'medical', amount: 100, confirm: true });
    f.root.querySelector('[data-training-skill="medical"]').click(); dialogue.revision = 'trainer-2'; publish();
    f.answer(true); assert.equal(f.actions.length, 0, 'An offer that changed while asked pays nothing.');
    f.root.querySelector('[data-tavern-item]').click();
    assert.deepEqual(f.actions.pop(), { action: 'tavern-buy', revision: 'trainer-2', key: 'wine', amount: 3 });
    dialogue.tavern.hungry = false; publish(); assert.equal(f.root.querySelector('[data-tavern-item]').disabled, true);
  } finally { f.dispose(); }
});

test('spellbook move and sort request canonical order without changing the projection locally', () => {
  const f = fixture();
  try {
    f.publish({ spells: { available: [{ key: 'b', name: 'Beta', cost: 5 }, { key: 'a', name: 'Alpha', cost: 8 }], ready: null, result: '' } });
    const move = f.root.querySelector('[data-action="spell-swap"][data-spell="b"][data-swap-with="a"]');
    move.click(); assert.deepEqual(f.actions.pop(), { action: 'spell-swap', key: 'b', item: 'a' });
    assert.equal(f.root.querySelector('[data-action="spell-ready"]').dataset.spell, 'b');
    f.root.querySelector('[data-action="spell-sort"]').click(); assert.deepEqual(f.actions.pop(), { action: 'spell-sort' });
    f.publish({ spells: { available: [{ key: 'a', name: 'Alpha', cost: 8 }, { key: 'b', name: 'Beta', cost: 5 }], ready: null, result: 'Sorted' } });
    assert.equal(f.root.querySelector('[data-action="spell-ready"]').dataset.spell, 'a');
  } finally { f.dispose(); }
});


test('action-driven time toggle reflects the product preference and held status', () => {
  const f = fixture();
  try {
    f.publish({ timeControl: { enabled: false, held: false } });
    const toggle = f.root.querySelector('[data-action="action-driven-time"]');
    assert.equal(toggle.getAttribute('aria-pressed'), 'false');
    toggle.click();
    assert.deepEqual(f.actions.at(-1), { action: 'action-driven-time', enabled: true });
    f.publish({ timeControl: { enabled: true, held: true } });
    assert.equal(toggle.getAttribute('aria-pressed'), 'true');
    assert.match(f.root.textContent, /Action-driven · Paused/);
    f.publish({ timeControl: { enabled: true, held: false } });
    assert.match(f.root.textContent, /Action-driven · Acting/);
    toggle.click();
    assert.deepEqual(f.actions.at(-1), { action: 'action-driven-time', enabled: false });
  } finally { f.dispose(); }
});
