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
  const initialCanvas = await page.locator('canvas').elementHandle();
  assert.ok(initialCanvas);
  await page.getByTestId('refresh-scene').click();
  await page.waitForFunction(() => window.__daggerApplicationHost?.readout().contentRevision === 2);
  assert.equal(
    await initialCanvas.evaluate((canvas) => canvas.isConnected),
    false,
    'atomic replacement did not retire the old canvas',
  );
  assert.equal(await page.locator('canvas').count(), 1, 'replacement created split renderer authority');
  await page.getByTestId('open-lab').click();
  await page.waitForFunction(() => document.querySelector('[data-testid="lab-page"]')?.classList.contains('is-open'));
  assert.equal(await page.locator('.product-shell').getAttribute('data-product-mode'), 'lab');
  assert.equal(await page.getByTestId('lab-page').getAttribute('aria-hidden'), null);
  await assertFixedApplicationShell(page, 1280, 900, true);
  await assertStalePollFailureFence(page);

  assert.equal(await page.getByTestId('max-health').innerText(), '85.00');
  assert.equal(await page.getByTestId('player-stamina').innerText(), '90.00 / 90.00');
  assert.equal(await page.getByTestId('player-magicka').innerText(), '50.00 / 50.00');
  assert.equal(await page.getByTestId('rat-max-health').innerText(), '3.00');
  assert.equal(await page.getByTestId('rat-max-stamina').innerText(), '10.00');
  assert.equal(await page.getByTestId('history-count').innerText(), '1 RECORDS');
  assert.equal(await page.getByTestId('profile-count').innerText(), '1 PROFILES');
  await page.getByTestId('active-profile').filter({ hasText: "Privateer's Hold starter" }).waitFor();
  const spawnPosition = await page.getByTestId('player-position').innerText();
  const contentRevisionBeforePlay = (await applicationReadout(page)).contentRevision;
  await page.getByTestId('return-to-play').click();
  await page.waitForFunction(() => document.querySelector('.product-shell')?.getAttribute('data-product-mode') === 'gameplay');
  assert.equal(await page.locator('.product-shell').getAttribute('data-product-mode'), 'gameplay');
  assert.equal(await page.getByTestId('lab-page').getAttribute('aria-hidden'), 'true');
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
  await page.getByTestId('edit-content-rules').click();
  assert.equal(await page.getByTestId('movement-speed').evaluate((element) => element === document.activeElement), true);
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
  await page.getByTestId('content-filter').fill('rat');
  await page.getByTestId('content-2007').click();
  await page.getByTestId('content-name').filter({ hasText: 'Rat' }).waitFor();
  assert.equal(await page.getByTestId('content-mobile-id').innerText(), '0');
  assert.match(await page.getByTestId('content-gameplay-stats').innerText(), /3\.00 health · 10\.00 stamina · 0\.00 magicka/i);
  assert.match(await page.getByTestId('content-live-resources').innerText(), /Live 3\.00 H · 10\.00 S · 0\.00 M/i);
  await page.getByTestId('content-filter').fill('skeletal');
  await page.getByTestId('content-2000').click();
  await page.getByTestId('content-name').filter({ hasText: 'SkeletalWarrior' }).waitFor();
  assert.match(await page.getByTestId('content-gameplay-stats').innerText(), /20\.00 health/i);
  await page.getByTestId('content-filter').fill('rat');
  await page.getByTestId('content-2007').click();

  // The worksheet calls the same Rust authority without applying or adding a
  // live history record.
  await fillExact(page, 'worksheet-base', '20');
  await fillExact(page, 'worksheet-endurance', '70');
  await fillExact(page, 'worksheet-rate', '2');
  await page.getByTestId('evaluate').click();
  await page.getByTestId('worksheet-result').filter({ hasText: '160.00' }).waitFor();
  assert.equal(await page.getByTestId('max-health').innerText(), '85.00');
  assert.equal(await page.getByTestId('history-count').innerText(), '1 RECORDS');

  await fillExact(page, 'worksheet-base', '-1');
  await page.getByTestId('evaluate').click();
  await page.getByTestId('worksheet-error').filter({ hasText: 'player.stats.resources.baseHealth' }).waitFor();
  assert.equal(await page.getByTestId('history-count').innerText(), '1 RECORDS');
  await fillExact(page, 'worksheet-base', '20');
  await page.getByTestId('evaluate').click();
  await page.getByTestId('worksheet-result').filter({ hasText: '160.00' }).waitFor();

  // Profile A is authored from the draft, saved locally, admitted by Rust,
  // reset, and physically played.
  await fillExact(page, 'movement-speed', '4');
  await fillExact(page, 'endurance', '50');
  await fillExact(page, 'rat-strength', '20');
  await fillExact(page, 'rat-base-health', '4');
  await fillExact(page, 'attack-range', '4');
  await fillExact(page, 'player-attack-cooldown', '4');
  await fillExact(page, 'player-stamina-cost', '5');
  await fillExact(page, 'hit-bonus', '-100');
  await fillExact(page, 'rat-defense', '200');
  await fillExact(page, 'enemy-detection-range', '0.5');
  await fillExact(page, 'enemy-patrol-speed', '0');
  await fillExact(page, 'enemy-attack-range', '0.4');
  await page.getByTestId('profile-name').fill('Measured pace');
  await page.getByTestId('save-as-profile').click();
  await page.getByTestId('profile-count').filter({ hasText: '2 profiles' }).waitFor();
  await page.getByTestId('activate-profile').click();
  await page.getByTestId('active-profile').filter({ hasText: 'Measured pace' }).waitFor();
  await page.getByTestId('live-speed').filter({ hasText: '4.00' }).waitFor();
  await page.getByTestId('max-health').filter({ hasText: '100.00' }).waitFor();
  await page.getByTestId('rat-max-health').filter({ hasText: '5.00' }).waitFor();
  await page.getByTestId('rat-max-stamina').filter({ hasText: '15.00' }).waitFor();
  await page.getByTestId('history-count').filter({ hasText: '2 records' }).waitFor();
  const profileAMove = await resetAndPhysicallyMove(page, spawnPosition);
  const profileACombat = await jumpAndPhysicallyAttack(
    page,
    2007,
    spawnPosition,
    'MISS',
    '5.00 → 5.00',
    '100.00 → 95.00',
    true,
  );

  // Profile B starts as a duplicate, is renamed and edited in place, then is
  // admitted and physically played as a meaningfully different alternative.
  await page.getByTestId('duplicate-profile').click();
  await page.getByTestId('profile-count').filter({ hasText: '3 profiles' }).waitFor();
  await page.getByTestId('profile-name').fill('Fast and hardy');
  await page.getByTestId('rename-profile').click();
  // This valid value cannot be represented exactly as Rust f32. Successful
  // admission must persist Rust's canonical document so polling and reload do
  // not discard the active profile identity.
  const authoredProfileBSpeed = 9.123456789;
  const admittedProfileBSpeed = Math.fround(authoredProfileBSpeed);
  await fillExact(page, 'movement-speed', String(authoredProfileBSpeed));
  await fillExact(page, 'endurance', '70');
  await fillExact(page, 'rat-strength', '30');
  await fillExact(page, 'rat-health-per-endurance', '0.3');
  await fillExact(page, 'hit-bonus', '100');
  await fillExact(page, 'base-damage', '10');
  await fillExact(page, 'player-attack-cooldown', '0.2');
  await fillExact(page, 'player-stamina-cost', '20');
  await fillExact(page, 'rat-defense', '0');
  await fillExact(page, 'rat-armor', '0');
  await page.getByTestId('content-filter').fill('skeletal');
  await page.getByTestId('content-2000').click();
  await fillExact(page, 'enemy-detection-range', '100');
  await fillExact(page, 'enemy-attack-range', '4');
  await fillExact(page, 'enemy-attack-cooldown', '0.5');
  await fillExact(page, 'enemy-attack-damage', '12');
  await page.getByTestId('save-profile').click();
  await page.getByTestId('activate-profile').click();
  await page.getByTestId('active-profile').filter({ hasText: 'Fast and hardy' }).waitFor();
  await page.getByTestId('live-speed').filter({ hasText: admittedProfileBSpeed.toFixed(2) }).waitFor();
  await page.getByTestId('max-health').filter({ hasText: '130.00' }).waitFor();
  await page.getByTestId('rat-max-health').filter({ hasText: '7.00' }).waitFor();
  await page.getByTestId('rat-max-stamina').filter({ hasText: '20.00' }).waitFor();
  await page.getByTestId('rat-derived-traces').filter({ hasText: 'enemy.mobile0.maxHealth' }).waitFor();
  await page.getByTestId('rat-derived-traces').filter({ hasText: 'healthPerEndurance = 0.30' }).waitFor();
  await page.getByTestId('history-count').filter({ hasText: '3 records' }).waitFor();
  const profileBMove = await resetAndPhysicallyMove(page, spawnPosition);
  await page.getByTestId('content-filter').fill('rat');
  const profileBCombat = await jumpAndPhysicallyAttack(
    page,
    2007,
    spawnPosition,
    'HIT',
    '7.00 → 0.00',
    '120.00 → 100.00',
    false,
  );
  await page.getByTestId('content-filter').fill('skeletal');
  const skeletonEncounter = await jumpAndObserveEnemyAttack(page, 2000, spawnPosition, 12);

  // Closing the product tab must not reset the Rust session. Reopen it in the
  // same browser profile and reattach to the
  // exact authoritative values, history, and player position left above.
  const beforeClosePosition = await page.getByTestId('player-position').innerText();
  await page.close();
  page = await context.newPage();
  await page.goto(process.env.DAGGER_PRODUCT_URL ?? 'http://127.0.0.1:4274', { waitUntil: 'domcontentloaded' });
  await waitForConnection(page);
  await openLabFromGameplay(page);
  await page.getByTestId('active-profile').filter({ hasText: 'Fast and hardy' }).waitFor();
  assert.equal(await page.getByTestId('live-speed').innerText(), admittedProfileBSpeed.toFixed(2));
  assert.equal(await page.getByTestId('max-health').innerText(), '130.00');
  assert.equal(await page.getByTestId('rat-max-health').innerText(), '7.00');
  assert.equal(await page.getByTestId('history-count').innerText(), '3 RECORDS');
  assert.equal(await page.getByTestId('player-position').innerText(), beforeClosePosition);

  await page.waitForTimeout(750);
  await page.getByTestId('active-profile').filter({ hasText: 'Fast and hardy' }).waitFor();
  const persistedProfileBSpeed = await page.evaluate(() => {
    const profiles = JSON.parse(
      localStorage.getItem('rusty-dagger.experiment-profiles') ?? '[]',
    );
    return profiles.find((profile) => profile.name === 'Fast and hardy')?.document.player
      .movement.speedUnitsPerSecond;
  });
  assert.equal(Math.fround(persistedProfileBSpeed), admittedProfileBSpeed);
  assert.notEqual(persistedProfileBSpeed, authoredProfileBSpeed);

  // Local profiles survive a page reload, while the active label is restored
  // only by matching the document the still-running Rust session reports.
  await page.reload({ waitUntil: 'domcontentloaded' });
  await waitForConnection(page);
  await openLabFromGameplay(page);
  await page.getByTestId('profile-count').filter({ hasText: '3 profiles' }).waitFor();
  await page.getByTestId('active-profile').filter({ hasText: 'Fast and hardy' }).waitFor();
  assert.equal(await page.getByTestId('live-speed').innerText(), admittedProfileBSpeed.toFixed(2));
  assert.equal(await page.getByTestId('max-health').innerText(), '130.00');

  // Invalid documents may be kept as drafts, but activating one must surface
  // the Rust author error and preserve the prior active session and history.
  await fillExact(page, 'movement-speed', '0');
  await page.getByTestId('profile-name').fill('Broken draft');
  await page.getByTestId('save-as-profile').click();
  await page.getByTestId('profile-count').filter({ hasText: '4 profiles' }).waitFor();
  await page.getByTestId('activate-profile').click();
  await page.getByTestId('command-error').filter({ hasText: 'player.movement.speedUnitsPerSecond' }).waitFor();
  assert.equal(await page.getByTestId('active-profile').innerText(), 'Fast and hardy');
  assert.equal(await page.getByTestId('live-speed').innerText(), admittedProfileBSpeed.toFixed(2));
  assert.equal(await page.getByTestId('history-count').innerText(), '3 RECORDS');

  page.once('dialog', (dialog) => dialog.accept());
  await page.getByTestId('delete-profile').click();
  await page.getByTestId('profile-count').filter({ hasText: '3 profiles' }).waitFor();
  assert.equal(await page.getByTestId('active-profile').innerText(), 'Fast and hardy');

  await page.getByTestId('history-filter').fill('#2');
  await page.getByTestId('history-2').click();
  await page.getByTestId('history-detail').filter({ hasText: 'Why record #2' }).waitFor();
  assert.equal(await page.getByTestId('trace-result').innerText(), '100.00');
  await page.getByTestId('history-filter').fill('');
  await page.getByTestId('content-filter').fill('rat');
  await page.getByTestId('content-2007').click();
  await page.getByTestId('content-name').filter({ hasText: 'Rat' }).waitFor();
  await page.screenshot({ path: `${output}/profiles-desktop.png`, fullPage: true });

  await page.setViewportSize({ width: 390, height: 844 });
  await page.getByTestId('profile-list').scrollIntoViewIfNeeded();
  await assertFixedApplicationShell(page, 390, 844, true);
  assert.equal(
    await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1),
    true,
    'narrow Dagger Lab overflows horizontally',
  );
  await page.getByTestId('history-detail').waitFor();
  await page.screenshot({ path: `${output}/profiles-narrow.png`, fullPage: true });

  await page.evaluate(() => window.__daggerApplicationHost?.dispose());
  await page.locator('canvas').waitFor({ state: 'detached' });

  console.log(
    `DAGGER_CONNECTED_PRODUCT_BROWSER_OK lifecycle=tab-closed-reopened/disposed/same-rust-session renderer=engine-application-host resources=${initialHost.resourceCount}/${initialHost.resourceBytes} replacement=atomic ui_input=arbitrated semanticLook=${JSON.stringify(semanticLook)} diagnostics=${JSON.stringify(connectedDiagnostics)} dynamicPresentation=${JSON.stringify(connectedPresentation)} content=rat-2007/mobile-0 ratA=5.00H/15.00S ratB=7.00H/20.00S ratTrace=enemy.mobile0.maxHealth combatA=${JSON.stringify(profileACombat)} combatB=${JSON.stringify(profileBCombat)} skeleton=${JSON.stringify(skeletonEncounter)} profiles=3 active="Fast and hardy" profileA=4.00/100.00 profileB=${admittedProfileBSpeed}/130.00 canonicalized_from=${authoredProfileBSpeed} preview=160.00 history=3 inspected=#2 connectedMove=${JSON.stringify(connectedMove)} profileAMove=${JSON.stringify(profileAMove)} profileBMove=${JSON.stringify(profileBMove)} desktop=${output}/profiles-desktop.png narrow=${output}/profiles-narrow.png`,
  );
} finally {
  await browser.close();
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
  const input = (pointerDelta) => page.evaluate(async (delta) => {
    const response = await fetch('/api/dagger-product/input', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ pressedCodes: [], pointerDelta: delta, buttons: 0 }),
    });
    if (!response.ok) throw new Error(`pointer input failed with ${String(response.status)}`);
    return response.json();
  }, pointerDelta);
  const angleDelta = (from, to) => ((to - from + 540) % 360) - 180;
  const before = await input([0, 0]);
  const right = await input([10, 0]);
  const left = await input([-10, 0]);
  const up = await input([0, -10]);
  const down = await input([0, 10]);
  assert.ok(angleDelta(before.camera.yawDegrees, right.camera.yawDegrees) < 0, 'mouse-right did not turn right');
  assert.ok(angleDelta(right.camera.yawDegrees, left.camera.yawDegrees) > 0, 'mouse-left did not turn left');
  assert.ok(up.camera.pitchDegrees > left.camera.pitchDegrees, 'mouse-up did not look up');
  assert.ok(down.camera.pitchDegrees < up.camera.pitchDegrees, 'mouse-down did not look down');
  return {
    right: angleDelta(before.camera.yawDegrees, right.camera.yawDegrees),
    left: angleDelta(right.camera.yawDegrees, left.camera.yawDegrees),
    up: up.camera.pitchDegrees - left.camera.pitchDegrees,
    down: down.camera.pitchDegrees - up.camera.pitchDegrees,
  };
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

  let interceptCurrentPoll = true;
  let currentPollStarted;
  const currentPollIntercepted = new Promise((resolve) => { currentPollStarted = resolve; });
  const currentHandler = async (route) => {
    if (interceptCurrentPoll && route.request().method() === 'GET') {
      interceptCurrentPoll = false;
      currentPollStarted();
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

async function fillExact(page, testId, value) {
  const input = page.getByTestId(testId);
  // A slow change-detection pass can restore the prior number between the
  // clear and replacement events used by Playwright. Only continue once the
  // blurred control confirms the authored value, with a bounded retry.
  for (let attempt = 1; attempt <= 3; attempt += 1) {
    await input.fill(value);
    await input.blur();
    await page.waitForTimeout(100);
    if (await input.inputValue() === value) return;
  }
  assert.equal(
    await input.inputValue(),
    value,
    `browser authoring did not commit the exact ${testId} value`,
  );
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
      '[data-rusty-application-renderer]',
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

async function jumpAndPhysicallyMove(page, contentId, spawnPosition) {
  await page.getByTestId(`content-${contentId}`).click();
  await page.getByTestId('jump-content').click();
  await page.waitForFunction(
    (id) => document.querySelector(`[data-testid="content-${id}"]`)?.classList.contains('active'),
    contentId,
    { timeout: 10_000 },
  );
  const jumpPosition = await page.getByTestId('player-position').innerText();
  assert.notEqual(jumpPosition, spawnPosition, 'content jump did not reposition the authoritative player');
  const expectedTitle = contentId === 2007 ? 'Rat H' : undefined;
  const move = await physicallyMove(page, jumpPosition, ['a', 'd', 's'], expectedTitle);
  await page.getByTestId('reset').click();
  await page.getByTestId('player-position').filter({ hasText: spawnPosition.replace('POSITION\n', '') }).waitFor();
  await openInterface(page);
  return move;
}

async function jumpAndPhysicallyAttack(
  page,
  contentId,
  spawnPosition,
  outcome,
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
  await runPhysicalAttack(page, contentId, 'Rat H', outcome);
  await page.getByTestId('combat-count').filter({ hasText: '1 attack' }).waitFor({ timeout: 10_000 });
  const record = page.getByTestId('combat-1');
  await record.filter({ hasText: outcome }).waitFor();
  await record.filter({ hasText: healthText }).waitFor();
  const text = await record.innerText();
  const acceptedAttempt = page.getByTestId('combat-attempt-1');
  await acceptedAttempt.filter({ hasText: 'ACCEPTED' }).waitFor();
  await acceptedAttempt.filter({ hasText: staminaText }).waitFor();
  const acceptedAttemptText = await acceptedAttempt.innerText();
  let cooldownRejection;
  if (expectCooldownRejection) {
    await runPhysicalAttack(page, contentId, 'Rat H', 'COOLDOWN');
    // A loaded CI runner can delay the projection long enough for the physical
    // input helper to retry. Assert the authoritative cooldown outcome without
    // coupling the proof to the retry-dependent attempt sequence number.
    const rejectedAttempt = page
      .locator('[data-testid^="combat-attempt-"]')
      .filter({ hasText: 'REJECTED · cooldown' })
      .first();
    await rejectedAttempt.waitFor({ timeout: 10_000 });
    await rejectedAttempt.filter({ hasText: 'stamina 95.00 → 95.00' }).waitFor();
    cooldownRejection = await rejectedAttempt.innerText();
    assert.equal(await page.getByTestId('combat-count').innerText(), '1 ATTACKS');
    await page.screenshot({ path: `${output}/combat-cooldown-desktop.png`, fullPage: true });
  }
  assert.match(text, /d100 \d+ .* defense/i);
  assert.match(text, /line of sight clear/i);
  if (outcome === 'HIT') {
    await page.screenshot({ path: `${output}/combat-hit-desktop.png`, fullPage: true });
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

async function runPhysicalAttack(page, contentId, expectedTitle, outcome) {
  for (let attempt = 1; attempt <= 3; attempt += 1) {
    try {
      await pressPhysical(page, 'Space');
      await page
        .locator('[data-testid^="combat-"]')
        .filter({ hasText: outcome })
        .first()
        .waitFor({ timeout: 5_000 });
      return;
    } catch (error) {
      if (attempt === 3) {
        throw error;
      }
      console.error(`physical Space action not observed after attempt ${attempt}; retrying`);
    }
  }
}

async function jumpAndObserveEnemyAttack(page, contentId, spawnPosition, damage) {
  await page.getByTestId(`content-${contentId}`).click();
  await page.getByTestId('jump-content').click();
  await page.waitForFunction(
    (id) => document.querySelector(`[data-testid="content-${id}"]`)?.classList.contains('active'),
    contentId,
    { timeout: 10_000 },
  );
  const attack = page
    .locator('[data-testid^="encounter-"]:not([data-testid="encounter-panel"])')
    .filter({ hasText: 'melee attack' })
    .filter({ hasText: `damage ${damage.toFixed(2)}` })
    .first();
  try {
    await attack.waitFor({ timeout: 20_000 });
  } catch (error) {
    const records = await page
      .locator('[data-testid^="encounter-"]:not([data-testid="encounter-panel"])')
      .allInnerTexts();
    const position = await page.getByTestId('player-position').innerText();
    throw new Error(
      `enemy ${contentId} did not produce damage ${damage.toFixed(2)} at ${JSON.stringify(position)}; records=${JSON.stringify(records)}`,
      { cause: error },
    );
  }
  await attack.filter({ hasText: 'LOS clear' }).waitFor();
  await pressPhysical(page, 'KeyA');
  const text = await attack.innerText();
  assert.match(text, /player \d+\.\d{2} → \d+\.\d{2}/i);
  await page.screenshot({ path: `${output}/skeleton-encounter-desktop.png`, fullPage: true });
  await openInterface(page);
  await page.getByTestId('reset').click();
  await page.getByTestId('player-position').filter({ hasText: spawnPosition.replace('POSITION\n', '') }).waitFor();
  await openInterface(page);
  return text;
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
