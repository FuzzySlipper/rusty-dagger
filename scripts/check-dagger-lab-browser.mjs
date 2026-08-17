#!/usr/bin/env node
import assert from 'node:assert/strict';
import { mkdir } from 'node:fs/promises';
import { resolve } from 'node:path';
import { chromium } from '@playwright/test';

const output = resolve(process.env.DAGGER_LAB_BROWSER_OUT ?? 'artifacts/dagger-lab');
await mkdir(output, { recursive: true });
const browser = await chromium.launch({
  headless: true,
  executablePath: process.env.DAGGER_LAB_CHROMIUM ?? '/usr/bin/chromium',
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
  await assertFixedApplicationShell(page, 1280, 900);
  const connectedPresentation = await assertConnectedDynamicPresentation(page);
  const semanticLook = await assertSemanticPointerDirections(page);
  const connectedDiagnostics = await assertConnectedDiagnosticKeys(page);
  assert.ok(await renderedPixelVariety(page), 'real Rust resource-backed scene did not render visible pixels');
  if (process.env.DAGGER_SKIP_BROWSER_REMOUNT === '1') {
    // verify-native-host separately certifies stale_handle_replaced=true. Keep
    // this explicit opt-out for diagnostic/manual runs that isolate visible
    // product behavior from the browser remount proof.
    console.error('DAGGER_BROWSER_REMOUNT_SKIPPED reason=explicit_diagnostic_opt_out native_replacement_proof=required');
  } else {
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
    await page.getByTestId('refresh-scene').click();
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
  }
  await openInterface(page);
  assert.equal(await page.locator('.product-shell').getAttribute('data-product-mode'), 'lab');
  assert.equal(await page.getByTestId('lab-page').getAttribute('aria-hidden'), null);
  await assertFixedApplicationShell(page, 1280, 900, true);
  await assertStalePollFailureFence(page);

  assert.equal(await page.getByTestId('max-health').innerText(), '85.00');
  assert.equal(await page.getByTestId('player-stamina').innerText(), '90.00 / 90.00');
  assert.equal(await page.getByTestId('player-magicka').innerText(), '50.00 / 50.00');
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
  await pressPhysical(page, 'KeyW');
  await page.waitForTimeout(300);
  assert.equal(
    await page.getByTestId('player-position').innerText(),
    interfacePosition,
    'interface mode leaked physical input into Rust gameplay authority',
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
  // The Thief has no gameplay actor: it patrols but is not a combatant.
  assert.match(await page.getByTestId('content-gameplay-stats').innerText(), /no actor definition/i);
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
  assert.match(await page.getByTestId('content-gameplay-stats').innerText(), /armor 30 · rat-bite/i);
  assert.match(await page.getByTestId('content-live-resources').innerText(), /Live 14\.00 H · 0\.00 S · 0\.00 M/i);

  // The committed gameplay package renders read-only: actors, actions, items,
  // and encounters straight from Rust admission.
  assert.notEqual((await page.getByTestId('package-fingerprint').innerText()).length, 0);
  await page.getByTestId('definition-actor-player').waitFor();
  await page.getByTestId('definition-actor-rat').waitFor();
  await page.getByTestId('definition-actor-skeletal-warrior').waitFor();
  await page.getByTestId('definition-action-melee-attack').waitFor();
  await page.getByTestId('definition-item-iron-longsword').waitFor();
  await page.getByTestId('definition-encounter-rat-introduction').waitFor();

  // One physical attack resolves through the authored action: the
  // deterministic first swing misses (d100 54 vs 40), stamina still spends,
  // and the record explains itself.
  const combatA = await jumpAndPhysicallyAttack(
    page,
    2007,
    spawnPosition,
    'MISS',
    'miss',
    '14.00 → 14.00',
    '90.00 → 85.00',
    false,
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

  // Sprite review tab: derived manifests publish through the lab bridge, the
  // Rat atlas renders through the asset route, its attack animation advances
  // real frames at authored timing, and directional review reaches the back
  // orientation. The tab must not add a canvas (Engine owns the sole one).
  const spriteReview = await assertSpriteReviewTab(page, output);

  await page.setViewportSize({ width: 390, height: 844 });
  await page.getByTestId('definitions-panel').scrollIntoViewIfNeeded();
  await assertFixedApplicationShell(page, 390, 844, true);
  assert.equal(
    await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1),
    true,
    'narrow Dagger Lab overflows horizontally',
  );
  await page.screenshot({ path: `${output}/explorer-narrow.png`, fullPage: true });

  await page.evaluate(() => window.__daggerApplicationHost?.dispose());
  await page.locator('canvas').waitFor({ state: 'detached' });

  console.log(
    `DAGGER_CONNECTED_PRODUCT_BROWSER_OK lifecycle=reloaded/disposed/same-rust-session renderer=engine-application-host resources=${initialHost.resourceCount}/${initialHost.resourceBytes} replacement=atomic ui_input=arbitrated semanticLook=${JSON.stringify(semanticLook)} inputCadence=${JSON.stringify(inputCadence)} diagnostics=${JSON.stringify(connectedDiagnostics)} dynamicPresentation=${JSON.stringify(connectedPresentation)} content=thief-2001-no-actor/rat-2007-mobile-0 definitions=package/actors/actions/items/encounters combatA=${JSON.stringify(combatA)} reloadMove=${JSON.stringify(reloadMove)} desktop=${output}/explorer-desktop.png narrow=${output}/explorer-narrow.png spriteReview=${JSON.stringify(spriteReview)}`,
  );
} finally {
  await browser.close();
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
    (await fetch('/api/dagger-lab/sprites/index')).json(),
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
    (await fetch('/api/dagger-lab/sprites/index')).json(),
  );
  rat = manifestIndex.manifests['enemy-manifest.json'].enemies.find((e) => e.mobileId === 0);
  assert.equal(rat.states.attack.fps, Number(classicFps), 'classic fps was not restored');
  assert.equal(rat.pivot[0], Number(classicPivot), 'classic pivot was not restored');
  // Leave the committed manifest pristine: clear the edit marker and save.
  await page.getByTestId('sprite-clear-edits').click();
  await page.getByTestId('sprite-save').click();
  await page.getByTestId('sprite-save-final').filter({ hasText: 'restamped' }).waitFor({ timeout: 60_000 });
  manifestIndex = await page.evaluate(async () =>
    (await fetch('/api/dagger-lab/sprites/index')).json(),
  );
  rat = manifestIndex.manifests['enemy-manifest.json'].enemies.find((e) => e.mobileId === 0);
  assert.equal(rat.edited, undefined, 'edit marker was not cleared from the manifest');

  assert.equal(await page.locator('canvas').count(), 1, 'sprite review must not add a canvas');
  await page.screenshot({ path: `${output}/sprites-desktop.png`, fullPage: true });
  await page.getByTestId('tab-explorer').click();
  await page.getByTestId('definitions-panel').waitFor();
  return { entries: count, ratAttackFrames: `${firstFrame}->${playedFrame}`, manifestEdit: 'fps/pivot persisted+restored' };
}

async function waitForConnection(page) {
  await page.getByTestId('connection').waitFor({ timeout: 30_000 });
  try {
    await page.getByTestId('connection').filter({ hasText: 'Connected' }).waitFor({ timeout: 30_000 });
  } catch (error) {
    console.error(`DAGGER_LAB_BROWSER_STATE ${await page.locator('body').innerText()}`);
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
  await page.getByTestId('open-lab').click();
  await page.waitForFunction(
    () => document.querySelector('.product-shell')?.getAttribute('data-product-mode') === 'lab',
  );
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
  await page.route('**/api/dagger-lab', staleHandler);
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
  await page.unroute('**/api/dagger-lab', staleHandler);

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
  await page.route('**/api/dagger-lab', currentHandler);
  await currentPollIntercepted;
  await page.getByTestId('connection').filter({ hasText: 'Waiting for product host' }).waitFor();
  assert.ok(await page.locator('[role="alert"]').count() > 0, 'current poll failure was hidden');
  await page.unroute('**/api/dagger-lab', currentHandler);
  await page.getByTestId('connection').filter({ hasText: 'Connected' }).waitFor();
}

async function pressPhysical(page, code) {
  const key = code.startsWith('Key') ? code.slice(3).toLowerCase() : code;
  await page.keyboard.down(key);
  await page.waitForTimeout(80);
  await page.keyboard.up(key);
}

async function openInterface(page) {
  await page.keyboard.press('Escape');
  await page.waitForFunction(
    () => window.__daggerApplicationHost?.readout().interactionMode === 'interface',
  );
  await page.waitForFunction(() => document.querySelector('[data-testid="lab-page"]')?.classList.contains('is-open'));
  assert.equal(await page.locator('.product-shell').getAttribute('data-product-mode'), 'lab');
}

async function openLabFromGameplay(page) {
  await page.getByTestId('open-lab').click();
  await page.waitForFunction(() => document.querySelector('.product-shell')?.getAttribute('data-product-mode') === 'lab');
}

async function assertFixedApplicationShell(page, width, height, exerciseLabScroll = false) {
  const readBounds = () => page.evaluate(() => {
    const selectors = [
      '#application',
      '[data-rusty-application-host]',
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
        if (element === null) throw new Error(`fixed shell element missing: ${selector}`);
        const bounds = element.getBoundingClientRect();
        return [selector, { width: bounds.width, height: bounds.height }];
      })),
      renderer: (() => {
        const element = document.querySelector('[data-rusty-application-renderer]');
        if (element === null) throw new Error('fixed shell renderer is missing');
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
    assert.ok(Math.abs(bounds.width - width) <= 1, `${selector} width escaped fixed application bounds`);
    assert.ok(Math.abs(bounds.height - height) <= 1, `${selector} height escaped fixed application bounds`);
  }
  const rendererWidth = Math.min(width, height * 1.6);
  const rendererHeight = rendererWidth / 1.6;
  assert.ok(Math.abs(before.renderer.width - rendererWidth) <= 1, 'renderer escaped 8:5 width');
  assert.ok(Math.abs(before.renderer.height - rendererHeight) <= 1, 'renderer escaped 8:5 height');
  assert.ok(
    Math.abs(before.renderer.left - (width - rendererWidth) / 2) <= 1,
    'renderer is not horizontally centered',
  );
  assert.ok(
    Math.abs(before.renderer.top - (height - rendererHeight) / 2) <= 1,
    'renderer is not vertically centered',
  );
  if (!exerciseLabScroll) return;
  const scroller = page.getByTestId('lab-scroll');
  const scrollRange = await scroller.evaluate((element) => element.scrollHeight - element.clientHeight);
  assert.ok(scrollRange > 0, 'Lab workspace does not have an internal scroll range');
  await scroller.evaluate((element) => { element.scrollTop = element.scrollHeight; });
  await page.waitForTimeout(50);
  const after = await readBounds();
  assert.deepEqual(after, before, 'Lab scrolling changed fixed application or renderer bounds');
  await scroller.evaluate((element) => { element.scrollTop = 0; });
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

async function jumpAndPhysicallyAttack(
  page,
  contentId,
  spawnPosition,
  outcome,
  presentationOutcome,
  healthText,
  staminaText,
  expectCooldownRejection,
) {
  await page.getByTestId(`content-${contentId}`).click();
  await page.getByTestId('jump-content').click();
  await page.waitForFunction(
    (id) => document.querySelector(`[data-testid="content-${id}"]`)?.classList.contains('active'),
    contentId,
    { timeout: 10_000 },
  );
  const expectedTitle = contentId === 2007 ? 'Rat H' : 'SkeletalWarrior H';
  await runPhysicalAttack(page, contentId, expectedTitle, outcome, presentationOutcome);
  await page.getByTestId('combat-count').filter({ hasText: '1 attack' }).waitFor({ timeout: 10_000 });
  const record = page.getByTestId('combat-1');
  try {
    await record.filter({ hasText: outcome }).waitFor({ timeout: 10_000 });
  } catch (error) {
    throw new Error(
      `combat-1 did not show ${outcome}: ${JSON.stringify(await record.innerText())}`,
      { cause: error },
    );
  }
  await record.filter({ hasText: healthText }).waitFor();
  const text = await record.innerText();
  const acceptedAttempt = page.getByTestId('combat-attempt-1');
  await acceptedAttempt.filter({ hasText: 'ACCEPTED' }).waitFor();
  await acceptedAttempt.filter({ hasText: staminaText }).waitFor();
  const acceptedAttemptText = await acceptedAttempt.innerText();
  let cooldownRejection;
  if (expectCooldownRejection) {
    await runPhysicalAttack(page, contentId, expectedTitle, 'COOLDOWN', 'cooldown');
    // A loaded CI runner can delay the projection long enough for the physical
    // input helper to retry. Assert the authoritative cooldown outcome without
    // coupling the proof to the retry-dependent attempt sequence number.
    const rejectedAttempt = page
      .locator('[data-testid^="combat-attempt-"]')
      .filter({ hasText: 'REJECTED · cooldown' })
      .first();
    await rejectedAttempt.waitFor({ timeout: 10_000 });
    await rejectedAttempt.filter({ hasText: 'stamina 85.00 → 85.00' }).waitFor();
    cooldownRejection = await rejectedAttempt.innerText();
    assert.equal(await page.getByTestId('combat-count').innerText(), '1 ATTACKS');
    await page.screenshot({ path: `${output}/combat-cooldown-desktop.png`, fullPage: true });
  }
  assert.match(text, /d100 \d+/i);
  assert.match(text, /melee-attack/i);
  assert.match(text, /line of sight clear/i);
  if (outcome === 'HIT') {
    await page.screenshot({ path: `${output}/combat-hit-desktop.png`, fullPage: true });
  }
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
    cooldownRejection,
  };
}

async function runPhysicalAttack(page, contentId, expectedTitle, outcome, presentationOutcome) {
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
        ({ previous, expected, phase, durableOutcome }) => {
          const body = document.body.dataset;
          const presentationVisible = body.daggerMeleeSequence !== previous
            && body.daggerMeleeOutcome === expected
            && body.daggerMeleePhase === phase;
          const durableText = Array.from(document.querySelectorAll('[data-testid^="combat-"]'))
            .map((element) => element.textContent ?? '')
            .join('\n');
          const durableObserved = durableOutcome === 'COOLDOWN'
            ? durableText.includes('REJECTED · cooldown')
            : durableText.includes(durableOutcome);
          if (!presentationVisible && !durableObserved) return false;
          return presentationVisible ? 'presentation' : 'durable';
        },
        {
          previous: priorSequence,
          expected: presentationOutcome,
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
