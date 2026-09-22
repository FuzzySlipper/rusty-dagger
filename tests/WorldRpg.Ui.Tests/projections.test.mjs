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
after(() => rm(output, { recursive: true, force: true }));

function fixture() {
  const dom = new JSDOM('<!doctype html><div id="root"></div>', { url: 'http://localhost/' });
  for (const key of ['window', 'document', 'Element', 'HTMLElement', 'HTMLDialogElement', 'KeyboardEvent', 'Option']) {
    globalThis[key] = dom.window[key];
  }
  dom.window.HTMLDialogElement.prototype.showModal = function () { this.open = true; };
  dom.window.HTMLDialogElement.prototype.close = function () { this.open = false; };
  const root = document.getElementById('root');
  const actions = [];
  let receive;
  const mounted = mountProductUi(root, {
    ui: { setInteractionMode() {}, focusGameplay() {} },
    projection: { subscribe(callback) { receive = callback; return () => {}; } },
    intents: { claim(_intent, value) { actions.push(value.data); } },
  });
  return {
    root, actions,
    publish(value = {}) {
      receive({ contract: 'dagger.ui.snapshot.v1', value: {
        resources: [], lastOutcome: '', mode: 'playing',
        composition: { bundle: 'test', ruleset: 'test', contentPacks: [], tuning: 'test' },
        ...value,
      } });
    },
    dispose() { mounted.dispose(); dom.window.close(); },
  };
}

test('focus close follows the current projected interaction and null clears its token', () => {
  const f = fixture();
  try {
    const close = f.root.querySelector('.dagger-focus-close');
    f.publish({ mode: 'modal', focus: { interaction: 'loot', container: 'first', close: 'loot-close' } });
    assert.equal(close.hidden, false);
    f.publish({ mode: 'modal', focus: { interaction: 'loot', container: 'second', close: 'loot-close' } });
    close.click();
    assert.deepEqual(f.actions.at(-1), { action: 'loot-close', container: 'second' });
    const count = f.actions.length;
    f.publish({ focus: null });
    assert.equal(close.hidden, true);
    close.click();
    assert.equal(f.actions.length, count, 'Even a queued click must not send the old token.');
  } finally { f.dispose(); }
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
      },
    } });
    const name = f.root.querySelector('[aria-label="Character name"]'); name.value = 'Aubk-i';
    f.root.querySelector('[data-testid="character-commit"]').click();
    assert.deepEqual(f.actions.at(-1), { action: 'character-commit', name: 'Aubk-i', race: 'breton', gender: 'male', faceIndex: 0, reflexes: 2, career: 'class00' });
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

test('pending level up shows permanent and live values and sends guarded semantic choices', () => {
  const f = fixture();
  try {
    f.publish({ mode: 'playing', character: {
      name: 'Aubk-i', attributes: [{ id: 'strength', label: 'Strength', value: 48, permanent: 50 }], skills: [], resources: [], progression: { level: 1, experience: 0 }, equipment: [], grantedSkills: [], creationAvailable: false, creation: null,
      levelUp: { level: 2, bonusPool: 4, remainingPoints: 4, healthGain: 6, canCommit: false,
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
      levelUp: { level: 2, bonusPool: 4, remainingPoints: 0, healthGain: 6, canCommit: true,
        attributes: [{ id: 'strength', label: 'Strength', permanent: 50, live: 48, pending: 4, canAllocate: false }] },
    } });
    f.root.querySelector('[data-testid="character-level-up-commit"]').click();
    assert.deepEqual(f.actions.at(-1), { action: 'character-level-commit' });
  } finally { f.dispose(); }
});

test('custom class editor sends typed skills traits and exposes eligibility reasons', () => {
  const f = fixture();
  try {
    f.publish({ mode: 'title', character: {
      name: 'Nameless', attributes: [], skills: [], resources: [], progression: { level: 1, experience: 0 }, equipment: [], grantedSkills: [], creationAvailable: true,
      creation: {
        editing: true, current: { name: 'Nameless', race: 'breton', gender: 'male', faceIndex: 0, reflexes: 2, career: 'custom' },
        races: [{ id: 'breton', label: 'Breton', available: true, restriction: null }], careers: [{ id: 'custom', label: 'Custom class', available: true, restriction: null }],
        faces: [{ index: 0, mediaId: 'character.head.male.00.0' }], reflexes: [{ value: 2, label: 'Average' }],
        custom: { name: 'Nightblade', primarySkills: ['mysticism', 'alteration', 'thaumaturgy'], majorSkills: ['illusion', 'destruction', 'restoration'], minorSkills: ['medical', 'short-blade', 'blunt-weapon', 'dragonish', 'daedric', 'dodging'], hitPointsPerLevel: 12,
          advantages: [{ id: 'increased-magery', target: '1.5' }], disadvantages: [{ id: 'forbidden-material', target: 'steel' }], eligibility: ['Choose each trained skill once.'],
          skills: ['mysticism', 'alteration', 'thaumaturgy', 'illusion', 'destruction', 'restoration', 'medical', 'short-blade', 'blunt-weapon', 'dragonish', 'daedric', 'dodging'], supportedAdvantages: ['increased-magery'], supportedDisadvantages: ['forbidden-material'] },
      },
    } });
    assert.equal(f.root.querySelector('[data-testid="character-custom-class"]').hidden, false);
    assert.match(f.root.querySelector('[data-testid="character-custom-eligibility"]').textContent, /Choose each trained skill once/);
    f.root.querySelector('[data-testid="character-custom-update"]').click();
    assert.deepEqual(f.actions.at(-1), { action: 'character-update', name: 'Nameless', race: 'breton', gender: 'male', faceIndex: 0, reflexes: 2, career: 'custom',
      primarySkills: 'mysticism,alteration,thaumaturgy', majorSkills: 'illusion,destruction,restoration', minorSkills: 'medical,short-blade,blunt-weapon,dragonish,daedric,dodging', hitPointsPerLevel: 12, advantages: 'increased-magery:1.5', disadvantages: 'forbidden-material:steel' });
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
    settings.querySelector('input').checked = true;
    rebind.click();
    document.dispatchEvent(new KeyboardEvent('keydown', { code: 'KeyS', bubbles: true, cancelable: true }));
    assert.equal(f.actions.at(-1).confirm, true);
    const count = f.actions.length;
    rebind.click();
    document.dispatchEvent(new KeyboardEvent('keydown', { code: 'Escape', bubbles: true, cancelable: true }));
    assert.equal(f.actions.length, count);
    assert.equal(settings.hidden, false);
    [...settings.querySelectorAll('button')].find(button => button.textContent === 'Reset bindings').click();
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
    f.publish({ mode: 'title' });
    assert.equal(select.disabled, true);
  } finally { f.dispose(); }
});
