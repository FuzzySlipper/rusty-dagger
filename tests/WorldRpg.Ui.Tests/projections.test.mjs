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
    assert.equal(f.root.querySelector('.dagger-death-message').textContent, 'You have died.');
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

test('transport projection exposes land selection and admitted ship boarding', () => {
  const f = fixture();
  try {
    f.publish({ transport: {
      mode: 'foot', onShip: false, canRun: true, travelModifier: 256, oceanMinutesPerMapPixel: 255,
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

test('rest panel sends semantic durations and shows the ruleset elapsed outcome', () => {
  const f = fixture();
  try {
    f.publish();
    f.root.querySelector('[data-action="rest"]:not([data-rest-mode])').click();
    const panel = f.root.querySelector('.dagger-rest-root');
    assert.equal(panel.hidden, false);
    const hours = panel.querySelector('.dagger-rest-hours');
    hours.value = '2';
    panel.querySelector('[data-rest-mode="timed"]').click();
    assert.deepEqual(f.actions.at(-1), { action: 'rest', mode: 'timed', hours: 2 });
    hours.value = '-1';
    const count = f.actions.length;
    panel.querySelector('[data-rest-mode="loiter"]').click();
    assert.equal(f.actions.length, count);
    assert.match(panel.querySelector('.dagger-rest-status').textContent, /whole number/);
    panel.querySelector('[data-rest-mode="until-healed"]').click();
    assert.deepEqual(f.actions.at(-1), { action: 'rest', mode: 'until-healed' });
    f.publish({ rest: {
      hasResult: true, revision: '1', mode: 'Timed', requestedSeconds: 7200,
      elapsedSeconds: 3600, recoveryHours: 1, healthRecovered: 3,
      fatigueRecovered: 8, spellPointsRecovered: 2, interruption: 'Encounter',
      message: 'Your rest was interrupted by an encounter.',
    } });
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
        mode: 'cart', onShip: false, canRun: true, travelModifier: 192, oceanMinutesPerMapPixel: 255,
        options: [{ id: 'cart', mode: 'cart', available: true, selected: true, label: 'Cart', message: 'Cart.', travelModifier: 192 }],
        wagon: {
          exists: true, accessible: true, id: 7, usedClassicUnits: 12, capacityClassicUnits: 300000, storeRevision: '8', message: 'Available.',
          items: [{ key: 'stack:daggerfall.wagon.7.gold', definition: 'gold-piece', quantity: '2' }],
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

test('item details show published labels and values without internal template identities', () => {
  const f = fixture();
  try {
    f.publish({ inventory: {
      revision: '1', message: '', equipmentChange: null, slots: [],
      items: [{ key: 'unique:7', definition: 'template-116-steel', label: 'Steel Broadsword', quantity: '1', weight: 3, value: 25, details: 'A steel blade.', icon: null, condition: null, identified: true, gridSlot: 0, equippedSlots: [], compatibleSlots: [] }],
    } });
    f.root.querySelector('[data-inventory-item="unique:7"]').click();
    const details = f.root.querySelector('.dagger-inventory-details');
    assert.match(details.textContent, /Steel Broadsword/);
    assert.match(details.textContent, /Weight 3 · Value 25/);
    assert.doesNotMatch(details.textContent, /template-|unique:7/);
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
    const message = { instance: 'quest:1', message: 10, delivery: 'popup', text: 'Same text.', signoff: null,
      diagnostics: [], promptId: null, options: [], entryId: 'quest-message:1' };
    f.publish({ quests: { deliveries: [message, { ...message, entryId: 'quest-message:2' }], journal: [], pending: null } });
    const buttons = f.root.querySelectorAll('.dagger-quest-popup button');
    assert.equal(buttons.length, 2);
    buttons[1].click();
    assert.deepEqual(f.actions.at(-1), { action: 'quest-dismiss', questInstance: 'quest:1', questDelivery: 'quest-message:2' });
    f.publish({ quests: { deliveries: [message], journal: [], pending: null } });
    assert.equal(f.root.querySelectorAll('.dagger-quest-popup').length, 1);
  } finally { f.dispose(); }
});

test('a projected quest prompt renders once and returns the selected semantic choice', () => {
  const f = fixture();
  try {
    const prompt = { instance: 'quest:1', message: 1010, delivery: 'prompt', text: 'Will you help?', signoff: null, diagnostics: [], promptId: 'prompt:1', options: [{ id: 3, label: 'Yes' }, { id: 4, label: 'No' }] };
    f.publish({ quests: { deliveries: [prompt], journal: [], pending: prompt } });
    assert.equal(f.root.querySelector('.dagger-quest-prompt p').textContent, 'Will you help?');
    f.root.querySelector('.dagger-quest-prompt button').click();
    assert.deepEqual(f.actions.at(-1), { action: 'quest-choice', questInstance: 'quest:1', questMessage: 1010, questPrompt: 'prompt:1', questChoice: 3 });
    f.publish({ quests: { deliveries: [], journal: [], pending: null } });
    assert.equal(f.root.querySelector('.dagger-quest-prompt'), null);
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

test('title creation renders normalized questions and sends the selected background allocation', () => {
  const f = fixture();
  try {
    f.publish({ mode: 'title', character: {
      name: 'Nameless', attributes: [], skills: [], resources: [], progression: { level: 1, experience: 0 }, equipment: [], grantedSkills: [], creationAvailable: true,
      creation: { editing: true, current: { name: 'Nameless', race: 'breton', gender: 'male', faceIndex: 0, reflexes: 2, career: 'class00' },
        races: [{ id: 'breton', label: 'Breton', available: true, restriction: null }], careers: [{ id: 'class00', label: 'Mage', available: true, restriction: null }], faces: [{ index: 0, mediaId: 'character.head.male.00.0' }], reflexes: [{ value: 2, label: 'Average' }],
        background: { biographyClassIndex: 0, biography: ['A readable biography.'], attributeBonusPool: 6, remainingAttributePoints: 4, primarySkillPoints: 5, majorSkillPoints: 6, minorSkillPoints: 6,
          questions: [{ number: 1, text: 'Where did you study?', selectedLetter: 'a', answers: [{ letter: 'a', text: 'At home.' }, { letter: 'b', text: 'At court.' }] }],
          attributes: [{ id: 'strength', label: 'Strength', rolled: 50, allocated: 2, value: 52, canAllocate: true }],
          skills: [{ id: 'medical', tier: 'primary', rolled: 28, allocated: 1, biographyBonus: 0, value: 29, canAllocate: true }],
          startingGrants: [{ itemId: 'template-113-iron', label: 'Longsword', templateIndex: 113, quantity: 1, sourceEffect: 'IT 3 0 0' }], unsupportedEffects: ['The source retains this fatigue background effect without a gameplay consequence.'] },
      },
    } });
    assert.match(f.root.querySelector('[data-testid="character-biography"]').textContent, /readable biography/);
    assert.equal(f.root.querySelector('[data-testid="character-starting-grants"]').textContent, 'Starting grants: 1 × Longsword.');
    assert.match(f.root.querySelector('[data-testid="character-background-unsupported-effects"]').textContent, /fatigue background effect/);
    const pools = f.root.querySelector('[data-testid="character-allocation-pools"]');
    assert.match(pools.textContent, /^4 of 6 attribute points remain; 5\/6\/6/);
    const actionsBeforeTyping = f.actions.length;
    f.root.querySelector('[aria-label="Attributes strength"]').value = '6';
    f.root.querySelector('[aria-label="Attributes strength"]').dispatchEvent(new window.Event('input', { bubbles: true }));
    assert.match(pools.textContent, /^0 of 6 attribute points remain; 5\/6\/6/);
    f.root.querySelector('[aria-label="Skills medical"]').value = '6';
    f.root.querySelector('[aria-label="Skills medical"]').dispatchEvent(new window.Event('input', { bubbles: true }));
    assert.match(pools.textContent, /^0 of 6 attribute points remain; 0\/6\/6/);
    assert.equal(f.actions.length, actionsBeforeTyping, 'Draft counters do not submit gameplay changes.');
    f.root.querySelector('[data-testid="character-background-reroll"]').click();
    assert.deepEqual(f.actions.at(-1), { action: 'character-background-reroll', name: 'Nameless', race: 'breton', gender: 'male', faceIndex: 0, reflexes: 2, career: 'class00', backgroundAnswers: '1:a', attributeAllocations: 'strength:6', skillAllocations: 'medical:6' });
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
      name: 'Aubk-i', attributes: [], skills: [], equipment: [{ label: 'Iron Longsword', slots: ['Right Hand'], details: 'Condition: 2/2 (100%); Unidentified magical item', condition: { current: 2, maximum: 2, percentage: 100, broken: false }, identified: false }], resources: [], grantedSkills: [], creationAvailable: false,
      progression: { level: 2, experience: 750, skillProgress: 15, nextLevelSkillProgress: 17, pendingLevelUp: false },
      resistances: [{ id: 'resistance-fire', label: 'Resistance Fire', value: 15, permanent: 25 }],
      affiliations: [{ faction: 'Mephala', guildGroup: 'Daedra', rank: 1, reputation: 6, recognition: 3 }],
      history: { biography: ['A first retained account.'] },
    } });
    assert.match(f.root.querySelector('[aria-label="Player progression"]').textContent, /15 \/ 17 skill total/);
    assert.match(f.root.querySelector('[data-testid="character-sheet-resistance-resistance-fire"]').textContent, /15 live \/ 25 permanent/);
    assert.match(f.root.querySelector('[data-testid="character-sheet-affiliation-Mephala"]').textContent, /Rank 1.*Reputation 6.*Recognition 3/);
    assert.equal(f.root.querySelector('[data-testid="character-sheet-history-0"]').textContent, 'A first retained account.');
    const equipment = [...f.root.querySelectorAll('.dagger-character-section')].find(section => section.querySelector('h3')?.textContent === 'Equipped items');
    assert.match(equipment.textContent, /Unidentified magical item/);

    f.publish({ mode: 'playing', character: {
      name: 'Aubk-i', attributes: [], skills: [], equipment: [{ label: 'Dagger of Fire', slots: ['Right Hand'], details: 'Condition: 1/2 (50%); Enchantment: Fire', condition: { current: 1, maximum: 2, percentage: 50, broken: false }, identified: true }], resources: [], grantedSkills: [], creationAvailable: false,
      progression: { level: 2, experience: 900, skillProgress: 17, nextLevelSkillProgress: 17, pendingLevelUp: true },
      resistances: [{ id: 'resistance-fire', label: 'Resistance Fire', value: 25, permanent: 25 }],
      affiliations: [{ faction: 'Mephala', guildGroup: 'Daedra', rank: 2, reputation: 9, recognition: 4 }],
      history: { biography: ['A revised retained account.'] },
    } });
    assert.match(f.root.querySelector('[aria-label="Player progression"]').textContent, /17 \/ 17 skill total.*Level up ready/);
    assert.match(f.root.querySelector('[data-testid="character-sheet-resistance-resistance-fire"]').textContent, /^25$/);
    assert.match(f.root.querySelector('[data-testid="character-sheet-affiliation-Mephala"]').textContent, /Rank 2.*Reputation 9.*Recognition 4/);
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
          skills: ['mysticism', 'alteration', 'thaumaturgy', 'illusion', 'destruction', 'restoration', 'medical', 'short-blade', 'blunt-weapon', 'dragonish', 'daedric', 'dodging'], supportedAdvantages: ['increased-magery:1.5', 'acute-hearing', 'regenerate-health:immersed'], supportedDisadvantages: ['forbidden-material:steel', 'damage:sunlight', 'inability-to-regen'] },
      },
    } };
    f.publish(snapshot);
    assert.equal(f.root.querySelector('[data-testid="character-custom-class"]').hidden, false);
    assert.match(f.root.querySelector('[data-testid="character-custom-eligibility"]').textContent, /Choose each trained skill once/);
    f.root.querySelector('[data-testid="character-custom-update"]').click();
    assert.deepEqual(f.actions.at(-1), { action: 'character-update', name: 'Nameless', race: 'breton', gender: 'male', faceIndex: 0, reflexes: 2, career: 'custom',
      primarySkills: 'mysticism,alteration,thaumaturgy', majorSkills: 'illusion,destruction,restoration', minorSkills: 'medical,short-blade,blunt-weapon,dragonish,daedric,dodging', hitPointsPerLevel: 12, advantages: 'increased-magery:1.5', disadvantages: 'forbidden-material:steel' });
    assert.equal(f.root.querySelectorAll('[aria-label^="Advantages "]').length, 7);
    f.root.querySelector('[aria-label="Advantages 2"]').value = 'regenerate-health:immersed';
    f.root.querySelector('[aria-label="Disadvantages 2"]').value = 'damage:sunlight';
    f.publish(structuredClone(snapshot));
    f.root.querySelector('[data-testid="character-custom-update"]').click();
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


test('multi-choice prompt uses supplied stable identities and refreshes occurrence', () => {
  const f = fixture();
  try {
    const prompt = { instance: 'quest:2', message: 1072, delivery: 'prompt', text: 'Choose a direction.', signoff: null,
      diagnostics: [], promptId: 'quest:2/source/0/1', options: [{ id: 24, label: 'South' }, { id: 25, label: 'West' }, { id: 28, label: 'Southwest' }] };
    f.publish({ quests: { deliveries: [prompt], journal: [], pending: prompt } });
    const buttons = f.root.querySelectorAll('.dagger-quest-prompt button');
    assert.equal(buttons.length, 3);
    buttons[2].click();
    assert.deepEqual(f.actions.at(-1), { action: 'quest-choice', questInstance: 'quest:2', questMessage: 1072,
      questPrompt: 'quest:2/source/0/1', questChoice: 28 });
    f.publish({ quests: { deliveries: [], journal: [], pending: { ...prompt, promptId: 'quest:2/source/0/2' } } });
    f.root.querySelector('.dagger-quest-prompt button').click();
    assert.equal(f.actions.at(-1).questPrompt, 'quest:2/source/0/2');
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
        mode: 'cart', onShip: false, canRun: true, travelModifier: 192, oceanMinutesPerMapPixel: 255,
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
    const lodging = { key: '17/3/1/2/8', name: 'The Dancing Chasm', days: 1, price: 3, remainingHours: 23, canBook: true };
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
    const quote = { identity: 'live-quote', destination: 'Charing', minutes: 120, distance: 2, oceanPixels: 0,
      innCost: 5, shipCost: 0, totalCost: 5, canAfford: true,
      options: { cautious: true, inn: true, ship: false, hasHorse: false, hasCart: false, hasShip: false, availableGold: '50', availableGoldPieces: '50' } };
    const value = { destinations: [{ region: 17, index: 3, name: 'Charing', kind: 'Town' }], quote,
      executionAvailable: true, message: null, lastResult: null };
    view.update(value);
    root.querySelector('.dagger-travel-accept').click();
    assert.deepEqual(actions.at(-1), { action: 'travel-accept', key: 'live-quote', amount: 5 });
    root.querySelector('.dagger-travel-accept').click();
    assert.equal(actions.length, 1);
    const message = 'Arrived at Charing. Paid 5 gold; 7200 seconds elapsed.';
    view.update({ ...value, quote: null, executionAvailable: false,
      lastResult: { outcome: 'Arrived', paidGold: 5, elapsedSeconds: 7200, actualRegion: 17, actualIndex: 3, message }, message });
    assert.equal(root.querySelector('.dagger-travel-last-result').textContent, message);
    assert.equal(root.querySelector('.dagger-travel-accept').disabled, true);
    const interrupted = 'Travel interrupted (Encounter); you remain at the departure. Paid 5 gold; 60 seconds elapsed.';
    view.update({ ...value, executionAvailable: false, message: interrupted, lastResult: { message: interrupted } });
    assert.match(root.querySelector('.dagger-travel-last-result').textContent, /60 seconds/);
    assert.equal(root.querySelector('.dagger-travel-accept').disabled, true);
    view.dispose();
  } finally { f.dispose(); }
});

test('changing travel options disables acceptance until a fresh quote arrives, including a free journey', async () => {
  const { mountTravel } = await import(pathToFileURL(join(output, 'travel.js')));
  const f = fixture();
  try {
    const root = document.createElement('div'); f.root.append(root);
    const actions = []; const view = mountTravel(root, action => actions.push(action));
    const quote = { identity: 'old', destination: 'Known', minutes: 60, distance: 1, oceanPixels: 0, innCost: 0, shipCost: 0, totalCost: 0, canAfford: true,
      options: { cautious: true, inn: false, ship: false } };
    const value = { destinations: [{ region: 0, index: 1, name: 'Known', kind: 'Town' }], quote, executionAvailable: true, message: null, lastResult: null };
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

test('map controls change only presentation and retain live player position without discovering geometry', () => {
  const f = fixture();
  try {
    const map = { id: 'privateers', name: "Privateer's Hold", kind: 'dungeon', region: null, location: null,
      player: { x: 1, y: 2, z: 3, yaw: .5 }, labels: [],
      areas: [{ id: 'known-room', minX: 0, minZ: 0, maxX: 10, maxZ: 10, minY: 1, maxY: 3, kind: 1 },
        { id: 'known-upper', minX: 0, minZ: 0, maxX: 10, maxZ: 10, minY: 4, maxY: 6, kind: 1 }] };
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
    entry.querySelector('[data-testid="character-commit"]').click();
    assert.equal(f.actions.at(-1).action, 'character-commit');
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
    f.publish({ detectors: [{ source: 'cast.2', kind: 'magic', contacts: [{ kind: 'actor', id: '2000', distance: 4.5, bearingRadians: Math.PI / 2, items: [] }] },
      { source: 'cast.3', kind: 'treasure', contacts: [] }] });
    const panel = f.root.querySelector('.dagger-detectors');
    assert.equal(panel.hidden, false);
    assert.equal(panel.children.length, 2);
    assert.match(panel.textContent, /Detect magic: actor 2000, 4.5 m, bearing 90°/);
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
    let accepted=false; window.confirm=()=>accepted;
    const spells={available:[{key:'spell.023',name:'Heal',cost:15},{key:'spell.001',name:'Unavailable spell',cost:0,canCast:false}],
      ready:'spell.023',result:'',sale:{revision:'seller-1',provider:'Mage',offers:[
        {key:'spell.002',name:'Cure',castingCost:12,price:48,known:false},
        {key:'spell.023',name:'Heal',castingCost:15,price:60,known:true}]},
      information:{key:'spell.023',name:'Heal',target:'CasterOnly',element:4,details:['restoration: magnitude 1–10.']}};
    f.publish({spells,activation:{mode:'talk',message:'',applied:true,dialogue:{revision:'seller-1',targetLabel:'Mage',greeting:'Welcome',tone:'normal',question:null,reply:null,topics:[],diagnostics:[]}}});
    const sales=f.root.querySelector('.dagger-dialogue-spells');
    assert.equal(sales.querySelector('[data-action="spell-buy"][data-spell="spell.023"]').disabled,true);
    const buy=sales.querySelector('[data-action="spell-buy"][data-spell="spell.002"]');
    buy.click(); assert.equal(f.actions.some(action=>action.action==='spell-buy'),false);
    accepted=true; buy.click();assert.deepEqual(f.actions.at(-1),{action:'spell-buy',key:'spell.002',revision:'seller-1',amount:48,confirm:true});
    sales.querySelector('[data-action="spell-info"]').click();assert.deepEqual(f.actions.at(-1),{action:'spell-info',key:'spell.002'});
    f.root.querySelector('[data-action="spells"]').click();
    const book=f.root.querySelector('.dagger-spells-root');
    assert.equal(book.querySelector('[data-action="spell-ready"][data-spell="spell.001"]').disabled,true);
    assert.match(book.textContent,/Unavailable spell · Unavailable/);assert.match(book.textContent,/magnitude 1–10/);
    book.querySelector('[data-action="spell-delete"]').click();assert.deepEqual(f.actions.at(-1),{action:'spell-delete',key:'spell.023',confirm:true});
    accepted=false;book.querySelector('[data-action="spell-delete"]').click();
    assert.equal(f.actions.filter(action=>action.action==='spell-delete').length,1);
  } finally { f.dispose(); }
});


test('property projection sends live ownership and store actions through the inventory panel', () => {
  const f = fixture();
  try {
    const item = { key: 'stack:coins', definition: 'gold-piece', label: 'Gold', quantity: '4', weight: 0, value: 1,
      details: '', icon: null, condition: null, identified: true, gridSlot: 0, equippedSlots: [], compatibleSlots: [] };
    f.publish({ inventory: { revision: 'ui-revision', message: '', equipmentChange: null, items: [item], slots: [] },
      property: { bankAvailable: true, offers: [{ key: 'ship/small', name: 'Small ship', price: '100000', salePrice: '85000',
        owned: false, canBuy: true, canSell: false, canEnter: false }], storage: { key: 'house/17/4/A.RMB/0/0/1', revision: '12',
          items: [{ key: 'stack:stored', definition: 'gold-piece', quantity: '2' }] } } });
    f.root.querySelector('[data-action="inventory"]').click();
    const controls = [...f.root.querySelectorAll('.dagger-property-root button')];
    controls.find(button => button.textContent === 'Buy').click();
    assert.deepEqual(f.actions.at(-1), { action: 'property-buy', key: 'ship/small' });
    controls.find(button => button.textContent.startsWith('Store')).click();
    assert.deepEqual(f.actions.at(-1), { action: 'property-put', key: 'house/17/4/A.RMB/0/0/1', item: 'stack:coins', revision: '12', amount: 4 });
    controls.find(button => button.textContent.startsWith('Take')).click();
    assert.deepEqual(f.actions.at(-1), { action: 'property-take', key: 'house/17/4/A.RMB/0/0/1', item: 'stack:stored', revision: '12', amount: 2 });
    f.publish({ property: { bankAvailable: false, offers: [], storage: null } });
    assert.equal(f.root.querySelector('.dagger-property-root').hidden, true);
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

test('merchant rows keep unit and selected stack totals explicit and expose service-only actions', () => {
  const f = fixture();
  try {
    const stack = { key: 'stack:shop.arrows', definition: 'template-131', label: 'Arrows', quantity: '10', unitPrice: '7',
      currentCondition: 10, maximumCondition: 10, identified: true, stolen: false, canBuy: true, canSell: false };
    const player = { key: 'unique:17', definition: 'template-113', label: 'Worn sword', quantity: 1, unitPrice: 20,
      currentCondition: 3, maximumCondition: 10, identified: false, stolen: false, canBuy: false, canSell: false };
    const dialogue = (merchant) => ({ revision: 'merchant-1', targetLabel: 'Guild officer', greeting: 'Welcome.', tone: 'normal',
      question: null, reply: null, topics: [], diagnostics: [], merchant });
    const baseMerchant = { revision: 'merchant-1', provider: 'Guild officer', quality: 10, gold: '100', buyAvailable: true,
      sellAvailable: false, repairAvailable: false, identifyAvailable: false, result: '', stock: [stack], playerItems: [], repairs: [] };
    f.publish({ activation: { mode: 'talk', dialogue: dialogue(baseMerchant) } });

    const stockRow = [...f.root.querySelectorAll('.dagger-dialogue-merchant li')].find(row => row.textContent.includes('Arrows'));
    assert.ok(stockRow);
    assert.match(stockRow.textContent, /Unit price 7 gold each/);
    const quantity = stockRow.querySelector('.dagger-merchant-quantity');
    quantity.value = '3';
    quantity.dispatchEvent(new window.Event('input', { bubbles: true }));
    assert.match(stockRow.textContent, /Unit price 7 gold each/);
    stockRow.querySelector('button').click();
    assert.deepEqual(f.actions.at(-1), { action: 'merchant-buy', revision: 'merchant-1', item: 'stack:shop.arrows', amount: 3 });

    const serviceOnly = { ...baseMerchant, buyAvailable: false, repairAvailable: true, stock: [], playerItems: [player] };
    f.publish({ activation: { mode: 'talk', dialogue: dialogue(serviceOnly) } });
    const repairRow = [...f.root.querySelectorAll('.dagger-dialogue-merchant li')].find(row => row.textContent.includes('Worn sword'));
    assert.ok(repairRow);
    assert.ok([...repairRow.querySelectorAll('button')].some(button => button.textContent === 'Repair'));
    repairRow.querySelector('button').click();
    assert.deepEqual(f.actions.at(-1), { action: 'merchant-repair', revision: 'merchant-1', item: 'unique:17' });

    const identifyOnly = { ...serviceOnly, repairAvailable: false, identifyAvailable: true };
    f.publish({ activation: { mode: 'talk', dialogue: dialogue(identifyOnly) } });
    const identifyRow = [...f.root.querySelectorAll('.dagger-dialogue-merchant li')].find(row => row.textContent.includes('Worn sword'));
    assert.ok(identifyRow);
    assert.ok([...identifyRow.querySelectorAll('button')].some(button => button.textContent === 'Identify'));
    identifyRow.querySelector('button').click();
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


test('spellmaker edits supported settings, preserves typing on repeated projection, previews and confirms one semantic purchase', () => {
  const f=fixture();
  try {
    let confirmed=false;window.confirm=()=>confirmed;
    const settings={key:'free-action',type:26,subType:-1,durationBase:3,durationMod:7,durationPerLevel:2,
      chanceBase:1,chanceMod:1,chancePerLevel:1,magnitudeBaseLow:1,magnitudeBaseHigh:1,magnitudeLevelBase:1,magnitudeLevelHigh:1,magnitudePerLevel:1};
    const maker={revision:'maker-1',provider:'Mage',effects:[{key:'free-action',type:26,subType:-1,school:'restoration',duration:true,chance:false,magnitude:false,targets:31,elements:16}],
      draft:{name:'Freedom',element:4,rangeType:0,icon:68,effects:[settings]},quote:{key:'draft-2',gold:300,spellPoints:15,eligible:true,reason:null}};
    const publish=()=>f.publish({spells:{available:[],ready:null,result:'',maker},activation:{mode:'talk',message:'',applied:true,
      dialogue:{revision:'maker-1',targetLabel:'Mage',greeting:'Welcome',tone:'normal',question:null,reply:null,topics:[],diagnostics:[]}}});
    publish();const root=f.root.querySelector('.dagger-dialogue-spellmaker');
    assert.match(root.textContent,/300 gold · 15 magicka/);
    const buy=[...root.querySelectorAll('button')].find(b=>b.textContent==='Buy constructed spell');
    buy.click();assert.equal(f.actions.some(a=>a.action==='spellmaker-buy'),false);
    confirmed=true;buy.click();assert.deepEqual(f.actions.at(-1),{action:'spellmaker-buy',revision:'maker-1',key:'draft-2',amount:300,confirm:true});
    const form=root.querySelector('form');const name=form.querySelector('[name="name"]');name.value='New name';
    name.dispatchEvent(new window.Event('input',{bubbles:true}));assert.equal(buy.disabled,true);
    publish();assert.equal(root.querySelector('[name="name"]'),name);assert.equal(name.value,'New name');
    assert.equal(root.querySelector('[name="chanceBase"]'),null);
    form.dispatchEvent(new window.Event('submit',{bubbles:true,cancelable:true}));
    const action=f.actions.at(-1);assert.equal(action.action,'spellmaker-draft');assert.equal(action.revision,'maker-1');
    assert.deepEqual(JSON.parse(action.text),{name:'New name',element:4,rangeType:0,icon:68,effects:[settings]});
    assert.equal(root.querySelectorAll('[name="icon"] option').length,69);
    f.publish({spells:{available:[],ready:null,result:'',maker:null}});assert.equal(root.childElementCount,0);
  } finally {f.dispose();}
});

test('quest escort portraits use published art and remove only ended quest overlays', () => {
  const f = fixture();
  try {
    const face = { instance: 'first', symbol: 'contact', name: 'Existing Giver', mediaId: 'character.head.male.00.0' };
    const art = { revision: 'escort-art', images: [{ id: face.mediaId, image: 'data:image/png;base64,cG9ydHJhaXQ=' }] };
    f.publish({ uiArt: art, quests: { deliveries: [], journal: [], pending: null, escortFaces: [face, { ...face, instance: 'second' }] } });
    let portraits = f.root.querySelectorAll('.dagger-escort-faces img');
    assert.equal(portraits.length, 2);
    assert.equal(portraits[0].alt, face.name);
    assert.equal(portraits[0].src, art.images[0].image);
    f.publish({ uiArt: art, quests: { deliveries: [], journal: [], pending: null, escortFaces: [{ ...face, instance: 'second' }] } });
    portraits = f.root.querySelectorAll('.dagger-escort-faces img');
    assert.equal(portraits.length, 1);
    assert.equal(portraits[0].dataset.questInstance, 'second');
    f.publish({ uiArt: art, quests: { deliveries: [], journal: [], pending: null, escortFaces: [] } });
    assert.equal(f.root.querySelector('.dagger-escort-faces'), null);
  } finally { f.dispose(); }
});


test('racial form projection closes and suppresses inventory until human form returns', () => {
  const f = fixture();
  try {
    const character = { name: 'Aubk-i', attributes: [], skills: [], resources: [], progression: { level: 1, experience: 0 }, equipment: [],
      identity: { race: 'breton', donorRaceId: 1, portrait: '', gender: 'female', faceIndex: 0, career: 'mage', media: [], selectedMedia: [],
        racialOverride: { name: 'Werewolf', beastForm: true, suppressInventory: true } } };
    f.root.querySelector('[data-action="inventory"]').click();
    f.publish({ character });
    assert.equal(f.root.querySelector('[data-action="inventory"]').disabled, true);
    assert.match(f.root.querySelector('.dagger-character-overview').textContent, /Werewolf · Beast form/);
    assert.equal(f.root.querySelector('.dagger-menu').classList.contains('has-inventory'), false);
    character.identity.racialOverride.beastForm = false;
    character.identity.racialOverride.suppressInventory = false;
    f.publish({ character });
    assert.equal(f.root.querySelector('[data-action="inventory"]').disabled, false);
    f.root.querySelector('[data-action="inventory"]').click();
    assert.equal(f.root.querySelector('.dagger-menu').classList.contains('has-inventory'), true);
  } finally { f.dispose(); }
});
