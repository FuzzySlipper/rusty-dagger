#!/usr/bin/env node
import assert from 'node:assert/strict';
import { mkdir } from 'node:fs/promises';
import { resolve } from 'node:path';
import { chromium } from '@playwright/test';

const output = resolve(process.env.DAGGER_PRODUCT_BROWSER_OUT ?? 'artifacts/dagger-product');
await mkdir(output, { recursive: true });
const browser = await chromium.launch({
  headless: true,
  executablePath: process.env.DAGGER_PRODUCT_CHROMIUM ?? '/usr/bin/chromium',
  args: ['--no-sandbox'],
});

try {
  const context = await browser.newContext({ viewport: { width: 1280, height: 900 } });
  let page = await context.newPage();
  await page.goto(process.env.DAGGER_PRODUCT_URL ?? 'http://127.0.0.1:4274', { waitUntil: 'domcontentloaded' });
  await waitForConnection(page);

  await page.waitForFunction(() => document.body.dataset.daggerApplicationHost === 'ready');
  const initialHost = await applicationReadout(page);
  assert.equal(initialHost.state, 'ready');
  assert.equal(initialHost.contentRevision, 1);
  assert.ok(initialHost.resourceCount > 0);
  assert.ok(initialHost.resourceBytes > 0);
  assert.equal(await page.locator('canvas').count(), 1, 'Engine must own the sole product canvas');
  assert.equal(await page.locator('.product-shell').getAttribute('data-product-mode'), 'gameplay');
  assert.equal(await page.getByTestId('lab-page').getAttribute('aria-hidden'), 'true');
  await assertApplicationHostBounds(page, 1280, 900);
  await assertSupportedPresentationSizes(page);
  await assertDeveloperCommandConsole(page);
  const gameMenu = await assertGameMenu(page);
  const connectedPresentation = await assertConnectedDynamicPresentation(page);
  const semanticLook = await assertSemanticPointerDirections(page);
  const keyboardLook = await assertKeyboardLookDirections(page);
  const connectedDiagnostics = await assertConnectedDiagnosticKeys(page);
  assert.ok(await renderedPixelVariety(page), 'real Rust resource-backed scene did not render visible pixels');
  const initialCanvas = await page.locator('canvas').elementHandle();
  assert.ok(initialCanvas);
  // A gameplay canvas with pointer lock can retarget Playwright's button
  // click to the canvas. Release it through the public host UI port without
  // opening the Lab overlay, then wait for the readout before clicking.
  await page.evaluate(() => {
    const host = window.__daggerApplicationHost;
    if (host === undefined) throw new Error('application host missing');
    host.ui.setInteractionMode('interface');
  });
  await page.waitForFunction(
    () => {
      const readout = window.__daggerApplicationHost?.readout();
      return readout?.interactionMode === 'interface' && readout.pointerLocked === false;
    },
    undefined,
    { timeout: 30_000 },
  );
  const inventoryComposition = await assertInventoryGameWindow(page, output);
  await openGameMenu(page);
  await page.getByTestId('game-menu-refresh').click();
  await page.waitForFunction(
    () => window.__daggerApplicationHost?.readout().contentRevision === 2,
    undefined,
    { timeout: 120_000 },
  );
  assert.equal(
    await initialCanvas.evaluate((canvas) => canvas.isConnected),
    false,
    'atomic replacement did not retire the old canvas',
  );
  assert.equal(await page.locator('canvas').count(), 1, 'replacement created split renderer authority');
  await openInterface(page);
  assert.equal(await page.locator('.product-shell').getAttribute('data-product-mode'), 'lab');
  assert.equal(await page.getByTestId('lab-page').getAttribute('aria-hidden'), null);
  await assertApplicationHostBounds(page, 1280, 900, true);
  await assertStalePollFailureFence(page);

  // All gameplay expectations below derive from the admitted package and the
  // live readout — never from literals duplicated in this script.
  const initialProduct = await productReadout(page);
  const gameplayPackage = initialProduct.gameplayPackage;
  assert.equal(await page.getByTestId('max-health').innerText(), fixed(initialProduct.maxHealth));
  assert.equal(
    await page.getByTestId('player-stamina').innerText(),
    `${fixed(initialProduct.playerStats.currentStamina)} / ${fixed(initialProduct.playerStats.maxStamina)}`,
  );
  assert.equal(
    await page.getByTestId('player-magicka').innerText(),
    `${fixed(initialProduct.playerStats.currentMagicka)} / ${fixed(initialProduct.playerStats.maxMagicka)}`,
  );
  await page.getByTestId('definitions-panel').waitFor();
  const spawnPosition = await page.getByTestId('player-position').innerText();
  const contentRevisionBeforePlay = (await applicationReadout(page)).contentRevision;
  await page.getByTestId('return-to-play').click();
  await page.waitForFunction(() => document.querySelector('.product-shell')?.getAttribute('data-product-mode') === 'gameplay');
  // Remount deliberately releases interface capture; wait for the public host
  // to reacquire gameplay pointer lock before sampling physical input.
  await page.waitForFunction(
    () => {
      const readout = window.__daggerApplicationHost?.readout();
      return readout?.interactionMode === 'gameplay' && readout.pointerLocked === true;
    },
    undefined,
    { timeout: 30_000 },
  );
  assert.equal(await page.locator('.product-shell').getAttribute('data-product-mode'), 'gameplay');
  assert.equal(await page.getByTestId('lab-page').getAttribute('aria-hidden'), 'true');
  const inputCadence = await assertMouseLookDoesNotMultiplyMovementTicks(page);
  await pressPhysical(page, 'KeyR');
  await page.getByTestId('player-position').filter({ hasText: spawnPosition.replace('POSITION\n', '') }).waitFor();
  const connectedMove = await physicallyMove(page, spawnPosition);
  await pressPhysical(page, 'KeyR');
  await page.getByTestId('player-position').filter({ hasText: spawnPosition.replace('POSITION\n', '') }).waitFor();
  await openInterface(page);
  assert.equal((await applicationReadout(page)).contentRevision, contentRevisionBeforePlay);
  const interfacePosition = await page.getByTestId('player-position').innerText();
  const interfaceYaw = await page.locator('body').getAttribute('data-dagger-camera-yaw');
  await pressPhysical(page, 'KeyL');
  await page.waitForTimeout(300);
  assert.equal(
    await page.getByTestId('player-position').innerText(),
    interfacePosition,
    'interface mode leaked physical keyboard look into Rust gameplay authority',
  );
  assert.equal(
    await page.locator('body').getAttribute('data-dagger-camera-yaw'),
    interfaceYaw,
    'interface mode leaked physical keyboard look into the canonical camera',
  );

  // Browse a real committed enemy, inspect decoded reference and live patrol
  // state separately, then let Rust choose a grounded approach and physically
  // interact through the connected product's original browser events.
  assert.equal(await page.getByTestId('content-count').innerText(), '43 ENEMIES');
  await page.getByTestId('content-filter').fill('thief');
  await page.getByTestId('content-2001').click();
  await page.getByTestId('content-name').filter({ hasText: 'Thief' }).waitFor();
  assert.equal(await page.getByTestId('content-name').innerText(), 'Thief');
  assert.equal(await page.getByTestId('content-mobile-id').innerText(), '138');
  assert.equal(await page.getByTestId('content-authored-position').innerText(), '11.07, 33.02, -6.88');
  const thiefLivePosition = await page.getByTestId('content-live-position').innerText();
  assert.match(thiefLivePosition, /Authoritative live patrol position/i);
  // The Thief is a class-career combatant whose actor and live resources come
  // from the admitted package.
  const thiefEntity = initialProduct.content.find((entity) => entity.id === 2001);
  assert.ok(thiefEntity, 'Thief 2001 is present in the live readout');
  const thiefActor = actorForMobile(gameplayPackage, thiefEntity.reference.mobileId);
  assert.ok(thiefActor?.behavior, 'Thief mobile has an authored actor with behavior');
  assert.match(
    await page.getByTestId('content-gameplay-stats').innerText(),
    new RegExp(`armor ${thiefActor.armorValue} · ${thiefActor.behavior.action}`, 'i'),
  );
  const thiefLive = thiefEntity.live.resources;
  assert.ok(thiefLive, 'Thief has live resources');
  assert.equal(
    await page.getByTestId('content-live-resources').innerText(),
    `Live ${fixed(thiefLive.currentHealth)} H · ${fixed(thiefLive.currentStamina)} S · ${fixed(thiefLive.currentMagicka)} M`,
  );
  await page.getByTestId('jump-content').click();
  await page.getByTestId('content-detail').filter({ hasText: 'focused' }).waitFor();
  const jumpDeadline = Date.now() + 10_000;
  while (await page.getByTestId('player-position').innerText() === spawnPosition) {
    assert.ok(Date.now() < jumpDeadline, 'content jump did not reposition the authoritative player');
    await page.waitForTimeout(100);
  }
  const jumpPosition = await page.getByTestId('player-position').innerText();
  assert.notEqual(jumpPosition, spawnPosition);
  await pressPhysical(page, 'KeyR');
  await page.getByTestId('player-position').filter({ hasText: spawnPosition.replace('POSITION\n', '') }).waitFor();
  await openInterface(page);
  assert.equal(await page.getByTestId('combat-count').innerText(), '0 ATTACKS');

  // The Rat has authored gameplay in the committed package; the live enemy
  // carries the deterministic spawn roll.
  await page.getByTestId('content-filter').fill('rat');
  await page.getByTestId('content-2007').click();
  await page.getByTestId('content-name').filter({ hasText: 'Rat' }).waitFor();
  const ratEntity = initialProduct.content.find((entity) => entity.id === 2007);
  assert.ok(ratEntity, 'Rat 2007 is present in the live readout');
  const ratActor = actorForMobile(gameplayPackage, ratEntity.reference.mobileId);
  assert.ok(ratActor?.behavior, 'Rat mobile has an authored actor with behavior');
  assert.match(
    await page.getByTestId('content-gameplay-stats').innerText(),
    new RegExp(`armor ${ratActor.armorValue} · ${ratActor.behavior.action}`, 'i'),
  );
  const ratLive = ratEntity.live.resources;
  assert.ok(ratLive, 'Rat has live resources');
  assert.equal(
    await page.getByTestId('content-live-resources').innerText(),
    `Live ${fixed(ratLive.currentHealth)} H · ${fixed(ratLive.currentStamina)} S · ${fixed(ratLive.currentMagicka)} M`,
  );

  // The committed gameplay package renders read-only: every actor, action,
  // item, and encounter in the admitted package has a definition card.
  assert.notEqual((await page.getByTestId('package-fingerprint').innerText()).length, 0);
  assert.equal(gameplayPackage.actors.length > 0, true);
  assert.equal(gameplayPackage.actions.length > 0, true);
  for (const actor of gameplayPackage.actors) {
    await page.getByTestId(`definition-actor-${actor.id}`).waitFor();
  }
  for (const action of gameplayPackage.actions) {
    await page.getByTestId(`definition-action-${action.id}`).waitFor();
  }
  for (const item of gameplayPackage.items) {
    await page.getByTestId(`definition-item-${item.id}`).waitFor();
  }
  for (const encounter of gameplayPackage.encounters) {
    await page.getByTestId(`definition-encounter-${encounter.id}`).waitFor();
  }

  // Character panel: the live kill-XP progression state renders read-only,
  // every value sourced from the authoritative readout (never literals).
  const initialProgression = initialProduct.progression;
  assert.ok(initialProgression, 'progression readout is present');
  assert.equal(await page.getByTestId('progression-level').innerText(), String(initialProgression.level));
  assert.equal(await page.getByTestId('progression-xp').innerText(), String(initialProgression.xp));
  assert.equal(await page.getByTestId('progression-to-next').innerText(), String(initialProgression.xpToNextLevel));
  assert.equal(
    await page.getByTestId('progression-health').innerText(),
    `${fixed(initialProgression.currentHealth)} / ${fixed(initialProgression.maxHealth)}`,
  );
  assert.match(
    await page.getByTestId('progression-award-count').innerText(),
    new RegExp(`^${initialProgression.history.length} AWARDS$`, 'i'),
  );

  // One physical attack resolves through the authored action. This
  // diagnostic computes nothing itself: the record's own authoritative
  // resolution evidence (the emitted predicate decision, semantic events,
  // and action id) is the expectation, and the presentation must be
  // consistent with it.
  const packageActionIds = new Set(gameplayPackage.actions.map((action) => action.id));
  const combatA = await jumpAndPhysicallyAttack(
    page,
    2007,
    spawnPosition,
    ratLive,
    initialProduct.playerStats,
    packageActionIds,
  );

  // The persistent Rust session keeps input authority across a hard reload.
  await page.reload({ waitUntil: 'domcontentloaded' });
  await waitForConnection(page);
  await openLabFromGameplay(page);
  const reloadMove = await resetAndPhysicallyMove(page, spawnPosition);
  assert.equal(
    await page.evaluate(() => document.body.dataset.daggerProductInputError),
    undefined,
    'physical input after reload was rejected by the persistent Rust session',
  );

  await page.getByTestId('content-filter').fill('rat');
  await page.getByTestId('content-2007').click();
  await page.screenshot({ path: `${output}/explorer-desktop.png`, fullPage: true });

  // Sprite review tab: derived manifests publish through the tooling API, the
  // Rat atlas renders through the asset route, its attack animation advances
  // real frames at authored timing, and directional review reaches the back
  // orientation. The tab must not add a canvas (Engine owns the sole one).
  const spriteReview = await assertSpriteReviewTab(page, output);

  await page.setViewportSize({ width: 390, height: 844 });
  await page.getByTestId('definitions-panel').scrollIntoViewIfNeeded();
  await assertApplicationHostBounds(page, 390, 844, true);
  assert.equal(
    await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1),
    true,
    'narrow Dagger Lab overflows horizontally',
  );
  await page.screenshot({ path: `${output}/explorer-narrow.png`, fullPage: true });

  await page.evaluate(() => window.__daggerApplicationHost?.dispose());
  await page.locator('canvas').waitFor({ state: 'detached' });

  console.log(
    `DAGGER_CONNECTED_PRODUCT_BROWSER_OK lifecycle=reloaded/disposed/same-rust-session renderer=engine-application-host resources=${initialHost.resourceCount}/${initialHost.resourceBytes} replacement=atomic ui_input=arbitrated gameMenu=${JSON.stringify(gameMenu)} inventoryComposition=${JSON.stringify(inventoryComposition)} semanticLook=${JSON.stringify(semanticLook)} keyboardLook=${JSON.stringify(keyboardLook)} inputCadence=${JSON.stringify(inputCadence)} diagnostics=${JSON.stringify(connectedDiagnostics)} dynamicPresentation=${JSON.stringify(connectedPresentation)} content=thief-2001-class-career/rat-2007-mobile-0 definitions=package/actors/actions/items/encounters combatA=${JSON.stringify(combatA)} reloadMove=${JSON.stringify(reloadMove)} desktop=${output}/explorer-desktop.png narrow=${output}/explorer-narrow.png spriteReview=${JSON.stringify(spriteReview)}`,
  );
} finally {
  await browser.close();
}

async function assertInventoryGameWindow(page, output) {
  const stableBoxes = async () => page.evaluate(() => {
    const box = (selector) => {
      const element = document.querySelector(selector);
      if (element === null) throw new Error(`missing ${selector}`);
      const rect = element.getBoundingClientRect();
      return [Math.round(rect.x), Math.round(rect.y), Math.round(rect.width), Math.round(rect.height)];
    };
    return {
      dialog: box('[data-testid="inventory-dialog"]'),
      equipment: box('[data-testid="inventory-dialog-equipped"]'),
      carried: box('[data-testid="inventory-dialog-carried"]'),
      detail: box('[data-testid="inventory-dialog-detail"]'),
      feedback: box('[data-testid="inventory-dialog-feedback"]'),
      slots: [...document.querySelectorAll('.equipment-slot')].map((element) => {
        const rect = element.getBoundingClientRect();
        return [Math.round(rect.x), Math.round(rect.y), Math.round(rect.width), Math.round(rect.height)];
      }),
      cells: [...document.querySelectorAll('.inventory-grid-slot')].map((element) => {
        const rect = element.getBoundingClientRect();
        return [Math.round(rect.width), Math.round(rect.height)];
      }),
    };
  });
  await pressPhysical(page, 'KeyI');
  await page.getByTestId('inventory-dialog').waitFor();
  const before = await stableBoxes();
  assert.ok(before.slots.length > 0, 'inventory has no declared equipment slots');
  assert.equal(before.cells.length, 50, 'inventory must render the fixed 50-slot carried grid');
  assert.equal(new Set(before.cells.map(JSON.stringify)).size, 1, 'carried grid cells are not fixed bounds');
  const source = page.getByTestId('inventory-grid-slot-0');
  const target = page.getByTestId('inventory-grid-slot-1');
  const initialSlots = await Promise.all([source.innerText(), target.innerText()]);
  await source.dragTo(target);
  await page.waitForFunction(
    ({ sourceText, targetText }) =>
      document.querySelector('[data-testid="inventory-grid-slot-0"]')?.textContent !== sourceText ||
      document.querySelector('[data-testid="inventory-grid-slot-1"]')?.textContent !== targetText,
    { sourceText: initialSlots[0], targetText: initialSlots[1] },
  );
  const movedSlots = await Promise.all([source.innerText(), target.innerText()]);
  assert.deepEqual(movedSlots, [initialSlots[1], initialSlots[0]], 'inventory slot drag did not swap occupants');
  await page.getByTestId('inventory-exit').click();
  await page.getByTestId('inventory-dialog').waitFor({ state: 'detached' });
  await pressPhysical(page, 'KeyI');
  await page.getByTestId('inventory-dialog').waitFor();
  assert.deepEqual(
    await Promise.all([source.innerText(), target.innerText()]),
    movedSlots,
    'inventory grid placement did not survive closing and reopening the dialog',
  );
  await source.click();
  const afterSelection = await stableBoxes();
  assert.deepEqual(afterSelection, before, 'inventory selection changed stable region or cell geometry');
  const action = page.locator('.equipment-slot .inventory-action:not([disabled])').first();
  const actionAvailable = await action.count() > 0;
  if (actionAvailable) {
    await action.click();
    await page.getByTestId('inventory-dialog-receipt').waitFor();
    const afterAction = await stableBoxes();
    assert.deepEqual(
      { ...afterAction, cells: [...new Set(afterAction.cells.map(JSON.stringify))] },
      { ...before, cells: [...new Set(before.cells.map(JSON.stringify))] },
      'inventory equipment receipt changed stable region or fixed cell geometry',
    );
  }
  const aspectSweeps = [];
  for (const [width, height, label] of [[1024, 768, '4x3'], [1280, 800, '16x10'], [1280, 720, '16x9']]) {
    await page.setViewportSize({ width, height });
    await page.waitForFunction(
      ({ width, height }) => document.documentElement.clientWidth === width
        && document.documentElement.clientHeight === height,
      { width, height },
    );
    await assertApplicationHostBounds(page, width, height);
    const geometry = await stableBoxes();
    for (const [name, rect] of Object.entries(geometry)) {
      if (name === 'slots' || name === 'cells') continue;
      assert.ok(rect[2] > 0 && rect[3] > 0, `${label} inventory ${name} collapsed`);
    }
    const dialog = geometry.dialog;
    assert.ok(dialog[0] >= 0 && dialog[1] >= 0 && dialog[0] + dialog[2] <= width && dialog[1] + dialog[3] <= height, `${label} inventory dialog escaped its presentation frame`);
    await page.screenshot({ path: `${output}/inventory-game-window-${label}.png`, fullPage: true });
    aspectSweeps.push(label);
  }
  await page.setViewportSize({ width: 1280, height: 900 });
  await page.screenshot({ path: `${output}/inventory-game-window.png`, fullPage: true });
  await page.keyboard.press('Escape');
  await page.getByTestId('inventory-dialog').waitFor({ state: 'detached' });
  await page.evaluate(() => window.__daggerApplicationHost?.ui.setInteractionMode('interface'));
  await page.waitForFunction(
    () => window.__daggerApplicationHost?.readout().interactionMode === 'interface',
    undefined,
    { timeout: 30_000 },
  );
  return { stableSlots: before.slots.length, stableCells: before.cells.length, receiptChecked: actionAvailable, aspectSweeps };
}

async function assertSpriteReviewTab(page, output) {
  await page.getByTestId('tab-sprites').click();
  await page.getByTestId('sprite-list').waitFor();
  const count = Number.parseInt(await page.getByTestId('sprite-count').innerText(), 10);
  assert.ok(count > 40, `sprite index published too few entries: ${count}`);
  assert.equal(await page.locator('canvas').count(), 1, 'sprite review must not add a canvas');

  await page.getByTestId('sprite-filter').fill('rat');
  await page.getByTestId('sprite-entry-enemy-manifest-json-mobile-0').click();
  await page.getByTestId('sprite-title').filter({ hasText: 'Rat' }).waitFor();
  const background = await page
    .getByTestId('sprite-frame-pixels')
    .evaluate((element) => getComputedStyle(element).backgroundImage);
  assert.match(
    background,
    /sprites\/asset\/textures\/enemy-rat-atlas\.png/,
    'stage frame is not blitted from the lab asset route',
  );
  const frameTransform = await page
    .getByTestId('sprite-frame-pixels')
    .evaluate((element) => getComputedStyle(element).transform);
  assert.equal(
    frameTransform,
    'none',
    'upright stored atlas must need no display flip (Engine samples top-left image space)',
  );

  await page.getByTestId('sprite-anim-attack').click();
  await page.getByTestId('sprite-anim-name').filter({ hasText: 'attack' }).waitFor();
  const classicSequence = await page.getByTestId('sprite-sequence').innerText();
  assert.ok(
    classicSequence.includes('0 1 2 ⚔ 3 4 5'),
    'rat attack does not show the classic playback sequence with its damage beat',
  );
  const firstFrame = await page.getByTestId('sprite-frame-index').getAttribute('data-frame');
  await page.getByTestId('sprite-play').click();
  await page.waitForFunction(
    (initial) =>
      document.querySelector('[data-testid="sprite-frame-index"]')?.getAttribute('data-frame') !==
      initial,
    firstFrame,
  );
  const playedFrame = await page.getByTestId('sprite-frame-index').getAttribute('data-frame');
  assert.notEqual(playedFrame, firstFrame, 'attack animation did not advance frames');

  await page.getByTestId('sprite-orientation-4').click();
  await page.getByTestId('sprite-orientation-name').filter({ hasText: 'back' }).waitFor();

  // Manifest editing: adjust the Rat attack fps and pivot, save, confirm the
  // manifest persisted with an edit marker and project docs restamped, then
  // restore the classic values. Leaves the content tree as it found it.
  await page.getByTestId('sprite-anim-attack').click();
  await page.getByTestId('sprite-anim-name').filter({ hasText: 'attack' }).waitFor();
  const fpsInput = page.getByTestId('sprite-fps');
  const pivotInput = page.getByTestId('sprite-pivot-x');
  const classicFps = await fpsInput.inputValue();
  const classicPivot = await pivotInput.inputValue();
  await fpsInput.fill('12');
  await pivotInput.fill('0.6');
  await page.getByTestId('sprite-savebar').waitFor();
  await page.getByTestId('sprite-edited-badge').waitFor();
  await page.getByTestId('sprite-save').click();
  await page.getByTestId('sprite-save-final').filter({ hasText: 'restamped' }).waitFor({ timeout: 60_000 });
  let manifestIndex = await page.evaluate(async () =>
    (await fetch('/api/dagger-tools/sprites/index')).json(),
  );
  let rat = manifestIndex.manifests['enemy-manifest.json'].enemies.find((e) => e.mobileId === 0);
  assert.equal(rat.states.attack.fps, 12, 'edited attack fps did not persist to the manifest');
  assert.equal(rat.pivot[0], 0.6, 'edited pivot did not persist to the manifest');
  assert.equal(rat.edited, true, 'edit marker was not recorded');
  await fpsInput.fill(classicFps);
  await pivotInput.fill(classicPivot);
  await page.getByTestId('sprite-save').click();
  await page.getByTestId('sprite-save-final').filter({ hasText: 'restamped' }).waitFor({ timeout: 60_000 });
  manifestIndex = await page.evaluate(async () =>
    (await fetch('/api/dagger-tools/sprites/index')).json(),
  );
  rat = manifestIndex.manifests['enemy-manifest.json'].enemies.find((e) => e.mobileId === 0);
  assert.equal(rat.states.attack.fps, Number(classicFps), 'classic fps was not restored');
  assert.equal(rat.pivot[0], Number(classicPivot), 'classic pivot was not restored');
  // Leave the committed manifest pristine: clear the edit marker and save.
  await page.getByTestId('sprite-clear-edits').click();
  await page.getByTestId('sprite-save').click();
  await page.getByTestId('sprite-save-final').filter({ hasText: 'restamped' }).waitFor({ timeout: 60_000 });
  manifestIndex = await page.evaluate(async () =>
    (await fetch('/api/dagger-tools/sprites/index')).json(),
  );
  rat = manifestIndex.manifests['enemy-manifest.json'].enemies.find((e) => e.mobileId === 0);
  assert.equal(rat.edited, undefined, 'edit marker was not cleared from the manifest');

  assert.equal(await page.locator('canvas').count(), 1, 'sprite review must not add a canvas');
  await page.screenshot({ path: `${output}/sprites-desktop.png`, fullPage: true });
  await page.getByTestId('tab-explorer').click();
  await page.getByTestId('definitions-panel').waitFor();
  return { entries: count, ratAttackFrames: `${firstFrame}->${playedFrame}`, manifestEdit: 'fps/pivot persisted+restored' };
}

async function assertDeveloperCommandConsole(page) {
  const shell = page.locator('[data-rusty-developer-command-shell="v1"]');
  await shell.getByRole('button', { name: 'Dagger developer commands' }).click();
  const command = shell.locator('select[aria-label="Developer command"]');
  await command.waitFor();
  await command.locator('option').first().waitFor({ state: 'attached' });
  const commandIds = await command.locator('option').evaluateAll((options) =>
    options.map((option) => option.value),
  );
  for (const id of [
    'standard.inspect.entity',
    'standard.inspect.mechanics',
    'standard.admin.track.set',
    'dagger.scenario.prepare',
    'dagger.scenario.melee',
    'dagger.scenario.advance',
    'dagger.scenario.progression',
  ]) {
    assert.ok(commandIds.includes(id), `developer command discovery omitted ${id}`);
  }
  for (const [id, lane] of [
    ['standard.admin.track.set', 'admin'],
    ['dagger.scenario.prepare', 'admin'],
    ['dagger.scenario.melee', 'play'],
    ['dagger.scenario.advance', 'play'],
    ['dagger.scenario.progression', 'admin'],
  ]) {
    const label = await command.locator(`option[value="${id}"]`).innerText();
    assert.match(label, new RegExp(`\\(${lane}\\)`), `${id} lost its visible ${lane} identity`);
  }
  assert.equal(
    await command.locator('option[value="standard.inspect.mechanics"]').isDisabled(),
    true,
    'the shell must not dispatch a standard command without an exact host wire codec',
  );

  await runDeveloperCommand(page, shell, 'standard.inspect.entity', { entity: '1' });

  // Setup is visibly admin-only. From here, ordinary `advance` lets the
  // admitted rat use its normal combat timing against the player; neither
  // this test nor the browser writes a health value directly.
  await runDeveloperCommand(page, shell, 'dagger.scenario.prepare', { target: 'rat' });
  const beforeDamage = await productReadout(page);
  await runDeveloperCommand(page, shell, 'dagger.scenario.advance', { ticks: 32 });
  const afterDamage = await productReadout(page);
  assert.ok(
    afterDamage.currentHealth < beforeDamage.currentHealth,
    'bounded production advance did not record player health loss in the Rust readout',
  );

  // Restoration is deliberately a visibly privileged standard command, not
  // a substitute for ordinary combat. Its target value comes from the prior
  // Rust readout and the next readout proves the Engine track mutation.
  await runDeveloperCommand(page, shell, 'standard.admin.track.set', {
    operation: 'dagger-browser-health-restore',
    source: {
      kind: 'request',
      operation: 'dagger-browser-health-restore',
      instance: 'dagger-browser-health-restore',
    },
    entity: '1',
    track: 'health',
    value: Math.trunc(beforeDamage.currentHealth),
    policy: 'clampToBounds',
  });
  const adminReceipt = (await developerCommandHistory(
    shell.locator('[data-developer-command-history]'),
  )).at(-1)?.outcome?.value;
  assert.equal(adminReceipt?.operation, 'dagger-browser-health-restore');
  assert.equal(adminReceipt?.entity, '1');
  assert.equal(adminReceipt?.track, 'health');
  assert.ok(Array.isArray(adminReceipt?.observedRevisions));
  assert.ok(typeof adminReceipt?.catalogVersion === 'string');
  const afterRestore = await productReadout(page);
  assert.equal(
    afterRestore.currentHealth,
    beforeDamage.currentHealth,
    'standard admin restoration did not restore the prior Rust-owned health value',
  );

  const staminaBeforeMelee = afterRestore.playerStats.currentStamina;
  await runDeveloperCommand(page, shell, 'dagger.scenario.melee', { swings: 1 });
  const afterMelee = await productReadout(page);
  assert.ok(
    afterMelee.playerStats.currentStamina < staminaBeforeMelee,
    'production melee did not deplete stamina in the Rust readout',
  );

  // This admin demonstration resets and defeats the committed sequence via
  // real melee contacts. The resulting Dagger history is the authoritative
  // kill-XP/level proof, not a DOM-derived counter.
  const levelBeforeProgression = afterMelee.progression.level;
  await runDeveloperCommand(page, shell, 'dagger.scenario.progression', {});
  const afterProgression = await productReadout(page);
  assert.ok(
    afterProgression.progression.level > levelBeforeProgression,
    'production progression scenario did not cross a level in the Rust readout',
  );
  assert.ok(
    afterProgression.progression.history.length >= 3,
    'production progression scenario did not retain its committed kill-XP history',
  );
  assert.ok(
    afterProgression.notices.some((notice) => notice.kind === 'level-up'),
    'production level transition did not emit its Rust-owned notice',
  );

  const history = JSON.parse(await shell.locator('[data-developer-command-history]').innerText());
  const expectedHistory = [
    ['standard.inspect.entity', 'inspect'],
    ['dagger.scenario.prepare', 'admin'],
    ['dagger.scenario.advance', 'play'],
    ['standard.admin.track.set', 'admin'],
    ['dagger.scenario.melee', 'play'],
    ['dagger.scenario.progression', 'admin'],
  ];
  assert.deepEqual(
    history.slice(-expectedHistory.length).map((entry) => [entry.request.command, entry.lane, entry.outcome.kind]),
    expectedHistory.map(([id, lane]) => [id, lane, 'success']),
    'the public Engine command history did not retain the intended admin/play sequence',
  );
  await shell.getByRole('button', { name: 'Dagger developer commands' }).click();
}

async function runDeveloperCommand(page, shell, id, payload) {
  const command = shell.locator('select[aria-label="Developer command"]');
  const history = shell.locator('[data-developer-command-history]');
  const previous = await developerCommandHistory(history);
  const previousCorrelation = previous.at(-1)?.request?.correlation ?? null;
  await command.selectOption(id);
  if (id === 'standard.admin.track.set') {
    await shell.getByLabel('Developer command parameters').fill(JSON.stringify(payload));
  } else {
    for (const [name, value] of Object.entries(payload)) {
      const field = shell.locator(`[data-command-field="${name}"]`);
      if (await field.evaluate((element) => element instanceof HTMLSelectElement)) {
        await field.selectOption(String(value));
      } else if (typeof value === 'string') await field.fill(value);
      else await field.fill(String(value));
    }
  }
  await shell.getByRole('button', { name: 'Run' }).click();
  await page.waitForFunction(
    ({ length, commandId, priorCorrelation }) => {
      const text = document.querySelector('[data-developer-command-history]')?.textContent;
      if (text === undefined || text === '') return false;
      const entries = JSON.parse(text);
      const entry = entries.at(-1);
      return entries.length > length
        && entry?.request?.command === commandId
        && entry?.request?.correlation !== priorCorrelation
        && entry?.outcome?.kind === 'success';
    },
    { length: previous.length, commandId: id, priorCorrelation: previousCorrelation },
    { timeout: 30_000 },
  );
  await shell.locator('[data-developer-command-status]').filter({ hasText: 'Success' }).waitFor();
}

async function developerCommandHistory(history) {
  const text = await history.innerText();
  return text === '' ? [] : JSON.parse(text);
}

async function waitForConnection(page) {
  await page.getByTestId('connection').waitFor({ timeout: 30_000 });
  try {
    await page.getByTestId('connection').filter({ hasText: 'Connected' }).waitFor({ timeout: 30_000 });
  } catch (error) {
    console.error(`DAGGER_PRODUCT_BROWSER_STATE ${await page.locator('body').innerText()}`);
    throw error;
  }
}

async function applicationReadout(page) {
  return page.evaluate(() => {
    if (window.__daggerApplicationHost === undefined) throw new Error('application host missing');
    return window.__daggerApplicationHost.readout();
  });
}

async function renderedPixelVariety(page) {
  return page.evaluate(() => {
    const host = window.__daggerApplicationHost;
    const canvas = document.querySelector('canvas');
    if (host === undefined || canvas === null) return false;
    host.renderer.renderOnce(250);
    const gl = canvas.getContext('webgl2');
    if (gl === null) return false;
    const pixels = new Uint8Array(canvas.width * canvas.height * 4);
    gl.readPixels(0, 0, canvas.width, canvas.height, gl.RGBA, gl.UNSIGNED_BYTE, pixels);
    const colors = new Set();
    const pixelCount = canvas.width * canvas.height;
    const stride = Math.max(1, Math.floor(pixelCount / 4096));
    for (let pixel = 0; pixel < pixelCount; pixel += stride) {
      const offset = pixel * 4;
      colors.add(`${pixels[offset]},${pixels[offset + 1]},${pixels[offset + 2]}`);
      if (colors.size >= 3) return true;
    }
    return false;
  });
}

async function assertConnectedDynamicPresentation(page) {
  await page.waitForFunction(() => document.body.dataset.daggerAnimatedEnvironmentHandle !== undefined);
  await page.waitForFunction(() => document.body.dataset.daggerMovedEnemyHandle !== undefined);
  await page.waitForFunction(() => Number(document.body.dataset.daggerDynamicFrameSequence ?? '0') >= 2);
  assert.ok(Number(await page.locator('body').getAttribute('data-dagger-dynamic-op-count')) > 0);
  assert.equal(await page.locator('body').getAttribute('data-dagger-product-input-error'), null);
  return {
    changedEnvironment: Number(await page.locator('body').getAttribute('data-dagger-animated-environment-handle')),
    movedEnemy: Number(await page.locator('body').getAttribute('data-dagger-moved-enemy-handle')),
  };
}

async function assertSemanticPointerDirections(page) {
  const camera = async () => ({
    yawDegrees: Number(await page.locator('body').getAttribute('data-dagger-camera-yaw')),
    pitchDegrees: Number(await page.locator('body').getAttribute('data-dagger-camera-pitch')),
  });
  const move = async (movementX, movementY) => {
    const beforeSequence = Number(await page.locator('body').getAttribute('data-dagger-input-sequence'));
    const beforeCamera = await camera();
    await page.evaluate(
      ({ x, y }) => {
        const event = new MouseEvent('mousemove', { bubbles: true });
        Object.defineProperties(event, {
          movementX: { value: x },
          movementY: { value: y },
        });
        window.dispatchEvent(event);
      },
      { x: movementX, y: movementY },
    );
    await page.waitForFunction(
      ({ sequence, yawDegrees, pitchDegrees }) =>
        Number(document.body.dataset.daggerInputSequence ?? '0') > sequence &&
        (Number(document.body.dataset.daggerCameraYaw) !== yawDegrees ||
          Number(document.body.dataset.daggerCameraPitch) !== pitchDegrees),
      { sequence: beforeSequence, ...beforeCamera },
      { timeout: 5_000 },
    );
    return camera();
  };
  const angleDelta = (from, to) => ((to - from + 540) % 360) - 180;
  await openLabFromGameplay(page);
  await page.getByTestId('return-to-play').click();
  await page.waitForFunction(() => document.pointerLockElement !== null);
  await page.mouse.move(640, 450, { steps: 1 });
  await page.waitForTimeout(80);
  await pressPhysical(page, 'KeyR');
  await page.waitForTimeout(80);
  const before = await camera();
  const right = await move(20, 0);
  const left = await move(-20, 0);
  const up = await move(0, -20);
  const down = await move(0, 20);
  assert.ok(
    angleDelta(before.yawDegrees, right.yawDegrees) > 0,
    `mouse-right did not turn right: before=${JSON.stringify(before)} after=${JSON.stringify(right)}`,
  );
  assert.ok(
    angleDelta(right.yawDegrees, left.yawDegrees) < 0,
    `mouse-left did not turn left: before=${JSON.stringify(right)} after=${JSON.stringify(left)}`,
  );
  assert.ok(
    up.pitchDegrees > left.pitchDegrees,
    `mouse-up did not look up: before=${JSON.stringify(left)} after=${JSON.stringify(up)}`,
  );
  assert.ok(
    down.pitchDegrees < up.pitchDegrees,
    `mouse-down did not look down: before=${JSON.stringify(up)} after=${JSON.stringify(down)}`,
  );
  return {
    right: angleDelta(before.yawDegrees, right.yawDegrees),
    left: angleDelta(right.yawDegrees, left.yawDegrees),
    up: up.pitchDegrees - left.pitchDegrees,
    down: down.pitchDegrees - up.pitchDegrees,
  };
}

async function assertKeyboardLookDirections(page) {
  const camera = async () => ({
    yawDegrees: Number(await page.locator('body').getAttribute('data-dagger-camera-yaw')),
    pitchDegrees: Number(await page.locator('body').getAttribute('data-dagger-camera-pitch')),
  });
  const hold = async (code) => {
    const key = code.slice(3).toLowerCase();
    const beforeSequence = Number(await page.locator('body').getAttribute('data-dagger-input-sequence'));
    const before = await camera();
    await page.keyboard.down(key);
    await page.waitForFunction(
      ({ sequence, yawDegrees, pitchDegrees }) =>
        Number(document.body.dataset.daggerInputSequence ?? '0') > sequence &&
        (Number(document.body.dataset.daggerCameraYaw) !== yawDegrees ||
          Number(document.body.dataset.daggerCameraPitch) !== pitchDegrees),
      { sequence: beforeSequence, ...before },
      { timeout: 5_000 },
    );
    const heldSequence = Number(
      await page.locator('body').getAttribute('data-dagger-sampled-input-sequence'),
    );
    await page.keyboard.up(key);
    await page.waitForFunction(
      (sequence) => Number(document.body.dataset.daggerSampledInputSequence ?? '0') > sequence,
      heldSequence,
      { timeout: 5_000 },
    );
    const releaseSequence = Number(await page.locator('body').getAttribute('data-dagger-sampled-input-sequence'));
    await page.waitForFunction(
      (sequence) => Number(document.body.dataset.daggerInputSequence ?? '0') >= sequence,
      releaseSequence,
      { timeout: 5_000 },
    );
    const after = await camera();
    await page.waitForTimeout(120);
    assert.deepEqual(await camera(), after, `${code} continued turning after key release`);
    return { before, after };
  };
  const angleDelta = (from, to) => ((to - from + 540) % 360) - 180;
  const right = await hold('KeyL');
  const left = await hold('KeyJ');
  const up = await hold('KeyU');
  const down = await hold('KeyO');
  assert.ok(angleDelta(right.before.yawDegrees, right.after.yawDegrees) > 0, 'L did not turn right');
  assert.ok(angleDelta(left.before.yawDegrees, left.after.yawDegrees) < 0, 'J did not turn left');
  assert.ok(up.after.pitchDegrees > up.before.pitchDegrees, 'U did not look up');
  assert.ok(down.after.pitchDegrees < down.before.pitchDegrees, 'O did not look down');
  return { yaw: 'J/L', pitch: 'U/O', release: 'stopped' };
}

async function assertMouseLookDoesNotMultiplyMovementTicks(page) {
  const submittedFrames = [];
  const countInput = async (route) => {
    if (route.request().method() === 'POST') {
      submittedFrames.push(route.request().postDataJSON());
    }
    await route.continue();
  };
  await page.route('**/api/dagger-product/input', countInput);
  const sample = async (withMouseLook) => {
    const firstFrame = submittedFrames.length;
    await page.keyboard.down('w');
    if (withMouseLook) {
      await page.mouse.move(641, 450, { steps: 1 });
    }
    await page.waitForTimeout(320);
    await page.keyboard.up('w');
    await page.waitForTimeout(80);
    const releaseSequence = Number(
      await page.locator('body').getAttribute('data-dagger-sampled-input-sequence'),
    );
    await page.waitForFunction(
      (sequence) => Number(document.body.dataset.daggerInputSequence ?? '0') >= sequence,
      releaseSequence,
      { timeout: 10_000 },
    );
    const frames = submittedFrames.slice(firstFrame);
    const movementFrames = frames.filter(
      (frame) => frame.pressedCodes.includes('KeyW') || frame.pressedEdges.includes('KeyW'),
    );
    return {
      requests: frames.length,
      movementRequests: movementFrames.length,
      movementSeconds: movementFrames.reduce((total, frame) => total + frame.stepSeconds, 0),
    };
  };
  const forwardOnly = await sample(false);
  const forwardAndLook = await sample(true);
  await page.unroute('**/api/dagger-product/input', countInput);
  assert.ok(
    forwardOnly.movementSeconds > 0,
    `held movement did not submit a frame: ${JSON.stringify(forwardOnly)}`,
  );
  assert.ok(
    forwardAndLook.movementRequests <= forwardOnly.movementRequests + 2,
    `mouse events multiplied movement frames: W=${JSON.stringify(forwardOnly)} W+look=${JSON.stringify(forwardAndLook)}`,
  );
  return { forwardOnly, forwardAndLook };
}

async function assertConnectedDiagnosticKeys(page) {
  await page.waitForFunction(() => document.body.dataset.daggerPatrolDebug === 'false');
  const sequenceBefore = Number(await page.locator('body').getAttribute('data-dagger-dynamic-frame-sequence'));
  await pressPhysical(page, 'KeyG');
  await page.waitForFunction(() => document.body.dataset.daggerPatrolDebug === 'true');
  await pressPhysical(page, 'KeyN');
  await page.waitForFunction(() => document.body.dataset.daggerNavDebug === 'true');
  await page.waitForFunction(
    (before) => Number(document.body.dataset.daggerDynamicFrameSequence ?? '0') > before,
    sequenceBefore,
  );
  assert.equal(await page.locator('body').getAttribute('data-dagger-product-input-error'), null);
  await pressPhysical(page, 'KeyG');
  await page.waitForFunction(() => document.body.dataset.daggerPatrolDebug === 'false');
  await pressPhysical(page, 'KeyN');
  await page.waitForFunction(() => document.body.dataset.daggerNavDebug === 'false');
  return { patrol: 'G', navgrid: 'N', lifecycle: 'on/off' };
}

async function assertStalePollFailureFence(page) {
  let releaseStalePoll;
  let stalePollStarted;
  const stalePollReleased = new Promise((resolve) => { releaseStalePoll = resolve; });
  const stalePollIntercepted = new Promise((resolve) => { stalePollStarted = resolve; });
  let interceptStalePoll = true;
  const staleHandler = async (route) => {
    if (interceptStalePoll && route.request().method() === 'GET') {
      interceptStalePoll = false;
      stalePollStarted();
      await stalePollReleased;
      await route.abort('failed');
      return;
    }
    await route.continue();
  };
  await page.route('**/api/dagger-product/readout', staleHandler);
  await stalePollIntercepted;
  await page.getByTestId('reset').click();
  await page.getByTestId('connection').filter({ hasText: 'Connected' }).waitFor();
  releaseStalePoll();
  await page.waitForTimeout(500);
  assert.equal(
    await page.locator('[role="alert"]').filter({ hasText: 'Http failure' }).count(),
    0,
    'stale poll rejection overwrote a newer successful command',
  );
  await page.unroute('**/api/dagger-product/readout', staleHandler);

  let currentPollObserved = false;
  let currentPollStarted;
  const currentPollIntercepted = new Promise((resolve) => { currentPollStarted = resolve; });
  const currentHandler = async (route) => {
    if (route.request().method() === 'GET') {
      if (!currentPollObserved) {
        currentPollObserved = true;
        currentPollStarted();
      }
      await route.abort('failed');
      return;
    }
    await route.continue();
  };
  await page.route('**/api/dagger-product/readout', currentHandler);
  await currentPollIntercepted;
  await page.getByTestId('connection').filter({ hasText: 'Waiting for product host' }).waitFor();
  assert.ok(await page.locator('[role="alert"]').count() > 0, 'current poll failure was hidden');
  await page.unroute('**/api/dagger-product/readout', currentHandler);
  await page.getByTestId('connection').filter({ hasText: 'Connected' }).waitFor();
}

async function pressPhysical(page, code) {
  const key = code.startsWith('Key') ? code.slice(3).toLowerCase() : code;
  await page.keyboard.down(key);
  await page.waitForTimeout(80);
  await page.keyboard.up(key);
}

async function openInterface(page) {
  await openGameMenu(page);
  await page.getByTestId('game-menu-lab').click();
  await page.waitForFunction(() => document.querySelector('[data-testid="lab-page"]')?.classList.contains('is-open'));
  assert.equal(await page.locator('.product-shell').getAttribute('data-product-mode'), 'lab');
}

async function openGameMenu(page) {
  await page.keyboard.press('Escape');
  await page.waitForFunction(
    () => window.__daggerApplicationHost?.readout().interactionMode === 'interface',
  );
  await page.getByTestId('game-menu').waitFor();
}

async function openLabFromGameplay(page) {
  await openGameMenu(page);
  await page.getByTestId('game-menu-lab').click();
  await page.waitForFunction(() => document.querySelector('.product-shell')?.getAttribute('data-product-mode') === 'lab');
}

async function assertGameMenu(page) {
  await openGameMenu(page);
  assert.equal(await page.locator('.game-controls').count(), 0, 'valid key strip must only appear in the Escape menu');
  assert.equal(await page.getByTestId('open-lab').count(), 0, 'top action buttons must be replaced by the Escape menu');
  for (const action of ['game-menu-return', 'game-menu-inventory', 'game-menu-character', 'game-menu-loot', 'game-menu-lab', 'game-menu-refresh']) {
    assert.equal(await page.getByTestId(action).count(), 1, `missing Escape menu action ${action}`);
  }
  assert.equal(await page.getByTestId('game-menu-settings-slot').count(), 1, 'missing later Settings insertion point');
  assert.ok((await page.getByTestId('game-menu-help').innerText()).includes('WASD'), 'Escape menu must own control help');

  await page.keyboard.press('Escape');
  await page.waitForFunction(() => document.querySelector('[data-testid="game-menu"]') === null);
  await page.waitForFunction(() => window.__daggerApplicationHost?.readout().interactionMode === 'gameplay');

  // Playwright does not synthesize operating-system key auto-repeat for a held
  // key, so open from gameplay and dispatch the repeat keydown it would produce.
  await page.keyboard.down('Escape');
  await page.getByTestId('game-menu').waitFor();
  await page.waitForTimeout(80);
  await page.evaluate(() => window.dispatchEvent(new KeyboardEvent('keydown', {
    code: 'Escape',
    repeat: true,
    bubbles: true,
    cancelable: true,
  })));
  await page.getByTestId('game-menu').waitFor();
  await page.keyboard.up('Escape');
  await page.keyboard.press('Escape');
  await page.waitForFunction(() => document.querySelector('[data-testid="game-menu"]') === null);
  await page.waitForFunction(() => window.__daggerApplicationHost?.readout().interactionMode === 'gameplay');
  await openGameMenu(page);
  await page.getByTestId('game-menu-lab').click();
  await page.getByTestId('lab-page').filter({ has: page.getByTestId('return-to-play') }).waitFor();
  await page.keyboard.press('Escape');
  await page.waitForFunction(() => document.querySelector('.product-shell')?.getAttribute('data-product-mode') === 'gameplay');
  return { actions: 6, help: 'escape-only', settingsSlot: true, labEscape: 'closed-first' };
}

async function assertApplicationHostBounds(page, width, height, exerciseLabScroll = false) {
  const expected = expectedPresentationFrame(width, height);
  await page.waitForFunction(
    ({ expectedWidth, expectedHeight }) => {
      const frame = document.querySelector('[data-rusty-application-presentation-frame]');
      if (frame === null) return false;
      const bounds = frame.getBoundingClientRect();
      return Math.abs(bounds.width - expectedWidth) <= 1 && Math.abs(bounds.height - expectedHeight) <= 1;
    },
    { expectedWidth: expected.width, expectedHeight: expected.height },
  );
  const readBounds = () => page.evaluate(() => {
    const selectors = [
      '#application',
      '[data-rusty-application-host]',
    ];
    const frameSelectors = [
      '[data-rusty-application-presentation-frame]',
      '[data-rusty-application-ui]',
      'dagger-root',
      '.product-shell',
    ];
    return {
      document: {
        clientWidth: document.documentElement.clientWidth,
        clientHeight: document.documentElement.clientHeight,
        scrollWidth: document.documentElement.scrollWidth,
        scrollHeight: document.documentElement.scrollHeight,
        scrollX: window.scrollX,
        scrollY: window.scrollY,
      },
      elements: Object.fromEntries(selectors.map((selector) => {
        const element = document.querySelector(selector);
        if (element === null) throw new Error(`application-host element missing: ${selector}`);
        const bounds = element.getBoundingClientRect();
        return [selector, { width: bounds.width, height: bounds.height }];
      })),
      frameElements: Object.fromEntries(frameSelectors.map((selector) => {
        const element = document.querySelector(selector);
        if (element === null) throw new Error(`application presentation frame element missing: ${selector}`);
        const bounds = element.getBoundingClientRect();
        return [selector, { left: bounds.left, top: bounds.top, width: bounds.width, height: bounds.height }];
      })),
      renderer: (() => {
        const element = document.querySelector('[data-rusty-application-renderer]');
        if (element === null) throw new Error('application-host renderer is missing');
        const bounds = element.getBoundingClientRect();
        return { left: bounds.left, top: bounds.top, width: bounds.width, height: bounds.height };
      })(),
    };
  });
  const before = await readBounds();
  assert.equal(before.document.clientWidth, width);
  assert.equal(before.document.clientHeight, height);
  assert.ok(before.document.scrollWidth <= width + 1, 'application document scrolls horizontally');
  assert.ok(before.document.scrollHeight <= height + 1, 'application document grows with Lab content');
  assert.equal(before.document.scrollX, 0);
  assert.equal(before.document.scrollY, 0);
  for (const [selector, bounds] of Object.entries(before.elements)) {
    assert.ok(Math.abs(bounds.width - width) <= 1, `${selector} width escaped application-host bounds`);
    assert.ok(Math.abs(bounds.height - height) <= 1, `${selector} height escaped application-host bounds`);
  }
  for (const [selector, bounds] of Object.entries(before.frameElements)) {
    assert.ok(Math.abs(bounds.width - expected.width) <= 1, `${selector} width escaped shared presentation frame`);
    assert.ok(Math.abs(bounds.height - expected.height) <= 1, `${selector} height escaped shared presentation frame`);
    assert.ok(Math.abs(bounds.left - expected.left) <= 1, `${selector} is not horizontally centered in the presentation frame`);
    assert.ok(Math.abs(bounds.top - expected.top) <= 1, `${selector} is not vertically centered in the presentation frame`);
  }
  assert.ok(Math.abs(before.renderer.width - expected.width) <= 1, 'renderer escaped shared presentation frame width');
  assert.ok(Math.abs(before.renderer.height - expected.height) <= 1, 'renderer escaped shared presentation frame height');
  assert.ok(Math.abs(before.renderer.left - expected.left) <= 1, 'renderer is not horizontally centered in shared presentation frame');
  assert.ok(Math.abs(before.renderer.top - expected.top) <= 1, 'renderer is not vertically centered in shared presentation frame');
  if (!exerciseLabScroll) return;
  const scroller = page.getByTestId('lab-scroll');
  const scrollRange = await scroller.evaluate((element) => element.scrollHeight - element.clientHeight);
  assert.ok(scrollRange > 0, 'Lab workspace does not have an internal scroll range');
  await scroller.evaluate((element) => { element.scrollTop = element.scrollHeight; });
  await page.waitForTimeout(50);
  const after = await readBounds();
  assert.deepEqual(after, before, 'Lab scrolling changed application-host or renderer bounds');
  await scroller.evaluate((element) => { element.scrollTop = 0; });
}

function expectedPresentationFrame(width, height) {
  const minimum = 4 / 3;
  const maximum = 16 / 9;
  const aspect = width / height;
  const frame = aspect < minimum
    ? { width, height: width / minimum }
    : aspect > maximum
      ? { width: height * maximum, height }
      : { width, height };
  return { ...frame, left: (width - frame.width) / 2, top: (height - frame.height) / 2 };
}

async function assertSupportedPresentationSizes(page) {
  for (const viewport of [
    { width: 1024, height: 768, label: 'minimum 4:3' },
    { width: 1280, height: 800, label: 'interior 16:10' },
    { width: 1280, height: 720, label: 'maximum 16:9' },
    { width: 390, height: 844, label: 'narrow out-of-range' },
    { width: 2560, height: 900, label: 'wide out-of-range' },
  ]) {
    await page.setViewportSize(viewport);
    await page.waitForFunction(
      ({ width, height }) => document.documentElement.clientWidth === width
        && document.documentElement.clientHeight === height,
      viewport,
    );
    await assertApplicationHostBounds(page, viewport.width, viewport.height);
  }
  await page.setViewportSize({ width: 1280, height: 900 });
  await page.waitForFunction(
    () => document.documentElement.clientWidth === 1280 && document.documentElement.clientHeight === 900,
  );
  await assertApplicationHostBounds(page, 1280, 900);
}

async function resetAndPhysicallyMove(page, spawnPosition) {
  await page.getByTestId('play').click();
  const resetDeadline = Date.now() + 10_000;
  while (await page.getByTestId('player-position').innerText() !== spawnPosition) {
    assert.ok(Date.now() < resetDeadline, 'Reset & Play did not restore the authoritative start');
    await page.waitForTimeout(100);
  }
  const resetPosition = spawnPosition;
  await page.waitForTimeout(500);
  const movement = await physicallyMove(page, resetPosition);
  await openInterface(page);
  return movement;
}

// Derivation helpers: every gameplay expectation in this diagnostic comes
// from the admitted package or the live readout, never from literals.
function fixed(value) {
  return value.toFixed(2);
}

async function productReadout(page) {
  return page.evaluate(async () => {
    const response = await fetch('/api/dagger-product/readout', { cache: 'no-store' });
    if (!response.ok) throw new Error(`product readout failed: ${response.status}`);
    return response.json();
  });
}

function actorForMobile(gameplayPackage, mobileId) {
  return gameplayPackage.actors.find((actor) => actor.mobileId === mobileId);
}

async function jumpAndPhysicallyAttack(
  page,
  contentId,
  spawnPosition,
  targetLive,
  playerStats,
  packageActionIds,
) {
  await page.getByTestId(`content-${contentId}`).click();
  await page.getByTestId('jump-content').click();
  await page.waitForFunction(
    (id) => document.querySelector(`[data-testid="content-${id}"]`)?.classList.contains('active'),
    contentId,
    { timeout: 10_000 },
  );
  const expectedTitle = 'Rat H';
  await runPhysicalAttack(page, contentId, expectedTitle, /HIT|MISS/, /hit|miss/);
  await page.getByTestId('combat-count').filter({ hasText: '1 attack' }).waitFor({ timeout: 10_000 });
  const text = await page.getByTestId('combat-1').innerText();

  // The authoritative predicate decision is emitted in the record: the roll,
  // the threshold Rust evaluated, and its verdict. The presentation must be
  // consistent with that evidence — nothing is recomputed here.
  const rollMatch = /d100 (\d+)/.exec(text);
  assert.ok(rollMatch, `combat record does not display its d100 roll: ${JSON.stringify(text)}`);
  const roll = Number(rollMatch[1]);
  const decisionMatch = /(\d+) Lte (\d+) = (true|false)/.exec(text);
  assert.ok(decisionMatch, `combat record does not emit the predicate decision: ${JSON.stringify(text)}`);
  assert.equal(Number(decisionMatch[1]), roll, 'displayed roll matches the evaluated predicate input');
  const decisionHit = decisionMatch[3] === 'true';

  const damageMatch = /(\d+\.\d+) damage/.exec(text);
  assert.ok(damageMatch, `combat record does not display damage: ${JSON.stringify(text)}`);
  const damage = Number(damageMatch[1]);
  const healthMatch = /Health (\d+\.\d+) → (\d+\.\d+)/.exec(text);
  assert.ok(healthMatch, `combat record does not display health: ${JSON.stringify(text)}`);
  assert.equal(Number(healthMatch[1]), targetLive.currentHealth, 'health before matches the live readout');
  const damageEventMatch = /DamageApplied \{ target: "[^"]+", amount: (\d+) \}/.exec(text);
  if (decisionHit) {
    assert.ok(damage > 0, 'authoritative decision is true but no damage applied');
    assert.ok(damageEventMatch, 'a hit did not emit the DamageApplied event');
    assert.equal(Number(damageEventMatch[1]), damage, 'displayed damage matches the emitted event');
    assert.equal(Number(healthMatch[2]), targetLive.currentHealth - damage, 'health after reflects the emitted damage');
    assert.match(text, /HIT/);
    await page.screenshot({ path: `${output}/combat-hit-desktop.png`, fullPage: true });
  } else {
    assert.equal(damage, 0, 'authoritative decision is false but damage applied');
    assert.equal(damageEventMatch, null, 'a miss must not emit DamageApplied');
    assert.equal(Number(healthMatch[2]), targetLive.currentHealth, 'a miss left health untouched');
    assert.match(text, /MISS/);
  }
  const actionIdMatch = /#\d+ · ([a-z0-9-]+) →/i.exec(text);
  assert.ok(actionIdMatch, 'combat record does not display its action id');
  const recordActionId = actionIdMatch[1].toLowerCase();
  assert.ok(packageActionIds.has(recordActionId), `record action ${recordActionId} is not in the admitted package`);
  assert.match(text, /line of sight clear/i);

  const acceptedAttempt = page.getByTestId('combat-attempt-1');
  await acceptedAttempt.filter({ hasText: 'ACCEPTED' }).waitFor();
  const acceptedAttemptText = await acceptedAttempt.innerText();
  const staminaMatch = /stamina (\d+\.\d+) → (\d+\.\d+) · cost (\d+\.\d+)/.exec(acceptedAttemptText);
  assert.ok(staminaMatch, `attempt record does not display stamina: ${JSON.stringify(acceptedAttemptText)}`);
  assert.equal(Number(staminaMatch[1]), playerStats.currentStamina, 'stamina before matches the live readout');
  assert.equal(
    Number(staminaMatch[2]),
    playerStats.currentStamina - Number(staminaMatch[3]),
    'attempt stamina delta matches its displayed cost',
  );
  const spentEventMatch = /TrackSpent \{ actor: "player", track: "stamina", amount: (\d+) \}/.exec(text);
  assert.ok(spentEventMatch, 'the TrackSpent event was not emitted');
  assert.equal(Number(spentEventMatch[1]), Number(staminaMatch[3]), 'attempt cost matches the emitted TrackSpent event');
  // Melee phases advance on sampled gameplay frames. Hold a bounded movement
  // input while waiting so cleanup does not depend on runner latency or an
  // incidental pending key event.
  await page.keyboard.down('a');
  try {
    await page.waitForFunction(
      () => document.body.dataset.daggerMeleeSequence === undefined,
      undefined,
      { timeout: 10_000 },
    );
  } finally {
    await page.keyboard.up('a');
  }
  await pressPhysical(page, 'KeyR');
  await page.getByTestId('player-position').filter({ hasText: spawnPosition.replace('POSITION\n', '') }).waitFor();
  await openInterface(page);
  assert.equal(await page.getByTestId('combat-count').innerText(), '0 ATTACKS');
  return {
    resolution: text,
    acceptedAttempt: acceptedAttemptText,
  };
}

async function runPhysicalAttack(page, contentId, expectedTitle, outcome, presentationOutcome) {
  const outcomePattern = outcome instanceof RegExp ? outcome : new RegExp(outcome, 'i');
  const presentationPattern = presentationOutcome instanceof RegExp
    ? presentationOutcome
    : new RegExp(`^${presentationOutcome}$`, 'i');
  const expectedPhase = presentationOutcome === 'cooldown' ? 'rejected' : 'contact';
  for (let attempt = 1; attempt <= 3; attempt += 1) {
    try {
      const priorSequence = await page.locator('body').getAttribute('data-dagger-melee-sequence');
      const priorInputSequence = Number(
        await page.locator('body').getAttribute('data-dagger-input-sequence'),
      );
      await pressPhysical(page, 'Space');
      await page.waitForFunction(
        (sequence) => Number(document.body.dataset.daggerInputSequence ?? '0') > sequence,
        priorInputSequence,
        { timeout: 10_000 },
      );
      assert.match(
        await page.locator('body').getAttribute('data-dagger-last-sampled-pressed-edges') ?? '',
        /(?:^|,)Space(?:,|$)/,
        'physical Space edge was not retained by the browser sampler',
      );
      const observation = await page.waitForFunction(
        ({ previous, presentationSource, phase, durableSource }) => {
          const body = document.body.dataset;
          const presentationPattern = new RegExp(presentationSource, 'i');
          const durablePattern = new RegExp(durableSource, 'i');
          const presentationVisible = body.daggerMeleeSequence !== previous
            && presentationPattern.test(body.daggerMeleeOutcome ?? '')
            && body.daggerMeleePhase === phase;
          const durableText = Array.from(document.querySelectorAll('[data-testid^="combat-"]'))
            .map((element) => element.textContent ?? '')
            .join('\n');
          const durableObserved = durablePattern.test(durableText);
          if (!presentationVisible && !durableObserved) return false;
          return presentationVisible ? 'presentation' : 'durable';
        },
        {
          previous: priorSequence,
          presentationSource: presentationPattern.source,
          durableSource: outcomePattern.source,
          phase: expectedPhase,
          durableOutcome: outcome,
        },
        { timeout: 10_000 },
      );
      const observationKind = await observation.jsonValue();
      if (observationKind === 'presentation') {
        // The authoritative phase and retained renderer frame share one Rust
        // tick but arrive across the HTTP/app-host boundary. Give the browser
        // one visible cadence to paint the just-observed phase before capture.
        await page.waitForTimeout(80);
        await page.screenshot({
          path: `${output}/melee-${presentationOutcome}-${expectedPhase}.png`,
          fullPage: true,
        });
      } else {
        // On heavily loaded software-rendered CI, the 100 ms browser projection
        // can miss the bounded Rust presentation while still observing the
        // durable authoritative resolution produced by that physical input.
        console.error(`physical Space ${presentationOutcome} presentation elapsed before browser projection`);
      }
      await page
        .locator('[data-testid^="combat-"]')
        .filter({ hasText: outcome })
        .first()
        .waitFor({ timeout: 5_000 });
      if (presentationOutcome !== 'cooldown' && observationKind === 'presentation') {
        await page.waitForFunction(
          () => Number(document.body.dataset.daggerPresentationOpCount ?? '0') >= 1,
          undefined,
          { timeout: 5_000 },
        );
        assert.equal(
          await page.locator('body').getAttribute('data-dagger-audio-resume-error'),
          null,
          'physical attack must resume the Engine audio host from its user gesture',
        );
      }
      return;
    } catch (error) {
      if (attempt === 3) {
        console.error(`physical Space final diagnostics inputError=${String(await page.locator('body').getAttribute('data-dagger-product-input-error'))} stateError=${String(await page.locator('body').getAttribute('data-dagger-product-state-error'))} edges=${String(await page.locator('body').getAttribute('data-dagger-last-sampled-pressed-edges'))} combatCount=${String(await page.getByTestId('combat-count').innerText())} records=${JSON.stringify(await page.locator('[data-testid^="combat-"]').allInnerTexts())}`);
        throw error;
      }
      console.error(`physical Space action not observed after attempt ${attempt}; retrying`);
    }
  }
}

async function physicallyMove(page, resetPosition, keys = ['w', 'w', 'w'], expectedTitle) {
  let movedPosition = resetPosition;
  for (const key of keys) {
    if (movedPosition !== resetPosition) break;
    await pressPhysical(page, `Key${key.toUpperCase()}`);
    const movementDeadline = Date.now() + 5_000;
    while (movedPosition === resetPosition && Date.now() < movementDeadline) {
      await page.waitForTimeout(100);
      movedPosition = await page.getByTestId('player-position').innerText();
    }
  }
  assert.notEqual(movedPosition, resetPosition, 'physical W input did not change Rust position');
  return { resetPosition, movedPosition };
}
