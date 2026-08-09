#!/usr/bin/env node
import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
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
  const page = await browser.newPage({ viewport: { width: 1280, height: 900 } });
  await page.goto('http://127.0.0.1:4274', { waitUntil: 'domcontentloaded' });
  await waitForConnection(page);

  assert.equal(await page.getByTestId('max-health').innerText(), '85.00');
  assert.equal(await page.getByTestId('history-count').innerText(), '1 RECORDS');
  assert.equal(await page.getByTestId('profile-count').innerText(), '1 PROFILES');
  await page.getByTestId('active-profile').filter({ hasText: "Privateer's Hold starter" }).waitFor();
  const spawnPosition = await page.getByTestId('player-position').innerText();

  // Browse a real committed enemy, inspect decoded reference and live patrol
  // state separately, then let Rust choose a grounded approach and physically
  // interact from the native game window.
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
  await page.getByTestId('reset').click();
  await page.getByTestId('player-position').filter({ hasText: spawnPosition.replace('POSITION\n', '') }).waitFor();
  await page.getByTestId('content-filter').fill('');

  // The worksheet calls the same Rust authority without applying or adding a
  // live history record.
  await page.getByTestId('worksheet-base').fill('20');
  await page.getByTestId('worksheet-endurance').fill('70');
  await page.getByTestId('worksheet-rate').fill('2');
  await page.getByTestId('evaluate').click();
  await page.getByTestId('worksheet-result').filter({ hasText: '160.00' }).waitFor();
  assert.equal(await page.getByTestId('max-health').innerText(), '85.00');
  assert.equal(await page.getByTestId('history-count').innerText(), '1 RECORDS');

  await page.getByTestId('worksheet-base').fill('-1');
  await page.getByTestId('evaluate').click();
  await page.getByTestId('worksheet-error').filter({ hasText: 'player.vitality.baseHealth' }).waitFor();
  assert.equal(await page.getByTestId('history-count').innerText(), '1 RECORDS');
  await page.getByTestId('worksheet-base').fill('20');
  await page.getByTestId('evaluate').click();
  await page.getByTestId('worksheet-result').filter({ hasText: '160.00' }).waitFor();

  // Profile A is authored from the draft, saved locally, admitted by Rust,
  // reset, and physically played.
  await page.getByTestId('movement-speed').fill('4');
  await page.getByTestId('endurance').fill('50');
  await page.getByTestId('profile-name').fill('Measured pace');
  await page.getByTestId('save-as-profile').click();
  await page.getByTestId('profile-count').filter({ hasText: '2 profiles' }).waitFor();
  await page.getByTestId('activate-profile').click();
  await page.getByTestId('active-profile').filter({ hasText: 'Measured pace' }).waitFor();
  await page.getByTestId('live-speed').filter({ hasText: '4.00' }).waitFor();
  await page.getByTestId('max-health').filter({ hasText: '100.00' }).waitFor();
  await page.getByTestId('history-count').filter({ hasText: '2 records' }).waitFor();
  const profileAMove = await resetAndPhysicallyMove(page, spawnPosition);
  const profileAContentMove = await jumpAndPhysicallyMove(page, 2001, spawnPosition);

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
  await page.getByTestId('movement-speed').fill(String(authoredProfileBSpeed));
  await page.getByTestId('endurance').fill('70');
  await page.getByTestId('save-profile').click();
  await page.getByTestId('activate-profile').click();
  await page.getByTestId('active-profile').filter({ hasText: 'Fast and hardy' }).waitFor();
  await page.getByTestId('live-speed').filter({ hasText: admittedProfileBSpeed.toFixed(2) }).waitFor();
  await page.getByTestId('max-health').filter({ hasText: '130.00' }).waitFor();
  await page.getByTestId('history-count').filter({ hasText: '3 records' }).waitFor();
  const profileBMove = await resetAndPhysicallyMove(page, spawnPosition);

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
  await page.getByTestId('profile-count').filter({ hasText: '3 profiles' }).waitFor();
  await page.getByTestId('active-profile').filter({ hasText: 'Fast and hardy' }).waitFor();
  assert.equal(await page.getByTestId('live-speed').innerText(), admittedProfileBSpeed.toFixed(2));
  assert.equal(await page.getByTestId('max-health').innerText(), '130.00');

  // Invalid documents may be kept as drafts, but activating one must surface
  // the Rust author error and preserve the prior active session and history.
  await page.getByTestId('movement-speed').fill('0');
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
  await page.getByTestId('content-filter').fill('thief');
  await page.getByTestId('content-2001').click();
  await page.getByTestId('content-name').filter({ hasText: 'Thief' }).waitFor();
  await page.screenshot({ path: `${output}/profiles-desktop.png`, fullPage: true });

  await page.setViewportSize({ width: 390, height: 844 });
  await page.getByTestId('profile-list').scrollIntoViewIfNeeded();
  assert.equal(
    await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1),
    true,
    'narrow Dagger Lab overflows horizontally',
  );
  await page.getByTestId('history-detail').waitFor();
  await page.screenshot({ path: `${output}/profiles-narrow.png`, fullPage: true });

  console.log(
    `DAGGER_LAB_BROWSER_OK content=thief-2001/mobile-138 profileAContentMove=${JSON.stringify(profileAContentMove)} profiles=3 active="Fast and hardy" profileA=4.00/100.00 profileB=${admittedProfileBSpeed}/130.00 canonicalized_from=${authoredProfileBSpeed} preview=160.00 history=3 inspected=#2 profileAMove=${JSON.stringify(profileAMove)} profileBMove=${JSON.stringify(profileBMove)} desktop=${output}/profiles-desktop.png narrow=${output}/profiles-narrow.png`,
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

async function resetAndPhysicallyMove(page, spawnPosition) {
  await page.getByTestId('play').click();
  const resetDeadline = Date.now() + 10_000;
  while (await page.getByTestId('player-position').innerText() !== spawnPosition) {
    assert.ok(Date.now() < resetDeadline, 'Reset & Play did not restore the authoritative start');
    await page.waitForTimeout(100);
  }
  const resetPosition = spawnPosition;
  await page.waitForTimeout(500);
  return physicallyMove(page, resetPosition);
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
  const move = await physicallyMove(page, jumpPosition, ['a', 'd', 's']);
  await page.getByTestId('reset').click();
  await page.getByTestId('player-position').filter({ hasText: spawnPosition.replace('POSITION\n', '') }).waitFor();
  return move;
}

async function physicallyMove(page, resetPosition, keys = ['w', 'w', 'w']) {
  let movedPosition = resetPosition;
  for (const key of keys) {
    if (movedPosition !== resetPosition) break;
    execFileSync('python3', ['scripts/x11-send-dagger-move.py', key], { stdio: 'inherit' });
    const movementDeadline = Date.now() + 5_000;
    while (movedPosition === resetPosition && Date.now() < movementDeadline) {
      await page.waitForTimeout(100);
      movedPosition = await page.getByTestId('player-position').innerText();
    }
  }
  assert.notEqual(movedPosition, resetPosition, 'physical W input did not change Rust position');
  return { resetPosition, movedPosition };
}
