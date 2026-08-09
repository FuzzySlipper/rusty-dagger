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
  await page.getByTestId('connection').waitFor({ timeout: 30_000 });
  try {
    await page.getByTestId('connection').filter({ hasText: 'Connected' }).waitFor({ timeout: 30_000 });
  } catch (error) {
    console.error(`DAGGER_LAB_BROWSER_STATE ${await page.locator('body').innerText()}`);
    throw error;
  }

  const initialHealth = await page.getByTestId('max-health').innerText();
  const initialHistory = await page.getByTestId('history-count').innerText();
  assert.equal(initialHealth, '85.00');
  assert.equal(initialHistory, '1 RECORDS');

  await page.getByTestId('worksheet-base').fill('20');
  await page.getByTestId('worksheet-endurance').fill('70');
  await page.getByTestId('worksheet-rate').fill('2');
  await page.getByTestId('evaluate').click();
  await page.getByTestId('worksheet-result').filter({ hasText: '160.00' }).waitFor();
  assert.equal(await page.getByTestId('max-health').innerText(), initialHealth);
  assert.equal(await page.getByTestId('history-count').innerText(), initialHistory);

  await page.getByTestId('worksheet-base').fill('-1');
  await page.getByTestId('evaluate').click();
  await page.getByTestId('worksheet-error').filter({ hasText: 'player.vitality.baseHealth' }).waitFor();
  assert.equal(await page.getByTestId('max-health').innerText(), initialHealth);
  assert.equal(await page.getByTestId('history-count').innerText(), initialHistory);
  await page.getByTestId('worksheet-base').fill('20');
  await page.getByTestId('evaluate').click();
  await page.getByTestId('worksheet-result').filter({ hasText: '160.00' }).waitFor();

  await page.getByTestId('movement-speed').fill('8');
  await page.getByTestId('endurance').fill('60');
  await page.getByTestId('apply').click();
  await page.getByTestId('live-speed').filter({ hasText: '8.00' }).waitFor();
  await page.getByTestId('max-health').filter({ hasText: '115.00' }).waitFor();
  await page.getByTestId('history-count').filter({ hasText: '2 records' }).waitFor();

  await page.getByTestId('endurance').fill('50');
  await page.getByTestId('apply').click();
  await page.getByTestId('max-health').filter({ hasText: '100.00' }).waitFor();
  await page.getByTestId('history-count').filter({ hasText: '3 records' }).waitFor();

  await page.getByTestId('history-filter').fill('#2');
  await page.getByTestId('history-2').click();
  await page.getByTestId('history-detail').filter({ hasText: 'Why record #2' }).waitFor();
  assert.equal(await page.getByTestId('trace-result').innerText(), '115.00');
  await page.getByTestId('history-filter').fill('');

  await page.getByTestId('play').click();
  const resetPosition = await page.getByTestId('player-position').innerText();
  await page.waitForTimeout(500);
  execFileSync('python3', ['scripts/x11-send-dagger-move.py'], { stdio: 'inherit' });
  const movementDeadline = Date.now() + 10_000;
  while (await page.getByTestId('player-position').innerText() === resetPosition) {
    if (Date.now() >= movementDeadline) break;
    await page.waitForTimeout(100);
  }
  const movedPosition = await page.getByTestId('player-position').innerText();
  assert.notEqual(movedPosition, resetPosition, 'physical W input did not change Rust position');
  await page.screenshot({ path: `${output}/workbench-desktop.png`, fullPage: true });

  await page.setViewportSize({ width: 390, height: 844 });
  await page.getByTestId('worksheet-result').scrollIntoViewIfNeeded();
  assert.equal(
    await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1),
    true,
    'narrow Dagger Lab overflows horizontally',
  );
  await page.getByTestId('history-detail').waitFor();
  await page.screenshot({ path: `${output}/workbench-narrow.png`, fullPage: true });

  await page.getByTestId('reset').click();
  const resetDeadline = Date.now() + 10_000;
  while (await page.getByTestId('player-position').innerText() !== resetPosition) {
    assert.ok(Date.now() < resetDeadline, 'reset did not restore the authoritative start position');
    await page.waitForTimeout(100);
  }
  await page.getByTestId('movement-speed').fill('0');
  await page.getByTestId('apply').click();
  await page.getByTestId('command-error').filter({ hasText: 'player.movement.speedUnitsPerSecond' }).waitFor();
  assert.equal(await page.getByTestId('live-speed').innerText(), '8.00');
  assert.equal(await page.getByTestId('history-count').innerText(), '3 RECORDS');

  console.log(
    `DAGGER_LAB_BROWSER_OK preview=160.00 active=100.00 history=3 inspected=#2 reset=${JSON.stringify(resetPosition)} moved=${JSON.stringify(movedPosition)} desktop=${output}/workbench-desktop.png narrow=${output}/workbench-narrow.png`,
  );
} finally {
  await browser.close();
}
