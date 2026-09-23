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
        background: { biographyClassIndex: 0, biography: ['A readable biography.'], attributeBonusPool: 6, remainingAttributePoints: 6, primarySkillPoints: 6, majorSkillPoints: 6, minorSkillPoints: 6,
          questions: [{ number: 1, text: 'Where did you study?', selectedLetter: 'a', answers: [{ letter: 'a', text: 'At home.' }, { letter: 'b', text: 'At court.' }] }],
          attributes: [{ id: 'strength', label: 'Strength', rolled: 50, allocated: 0, value: 50, canAllocate: true }],
          skills: [{ id: 'medical', tier: 'primary', rolled: 28, allocated: 0, biographyBonus: 0, value: 28, canAllocate: true }],
          startingGrants: [{ itemId: 'template-113-iron', templateIndex: 113, quantity: 1, sourceEffect: 'IT 3 0 0' }], unsupportedEffects: ['The source retains this fatigue background effect without a gameplay consequence.'] },
      },
    } });
    assert.match(f.root.querySelector('[data-testid="character-biography"]').textContent, /readable biography/);
    assert.match(f.root.querySelector('[data-testid="character-starting-grants"]').textContent, /template-113-iron/);
    assert.match(f.root.querySelector('[data-testid="character-background-unsupported-effects"]').textContent, /fatigue background effect/);
    f.root.querySelector('[aria-label="Attributes strength"]').value = '6';
    f.root.querySelector('[aria-label="Skills medical"]').value = '6';
    f.root.querySelector('[data-testid="character-background-reroll"]').click();
    assert.deepEqual(f.actions.at(-1), { action: 'character-background-reroll', name: 'Nameless', race: 'breton', gender: 'male', faceIndex: 0, reflexes: 2, career: 'class00', backgroundAnswers: '1:a', attributeAllocations: 'strength:6', skillAllocations: 'medical:6' });
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
