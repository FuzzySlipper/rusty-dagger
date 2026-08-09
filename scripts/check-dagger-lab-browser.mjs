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
  const page = await browser.newPage({ viewport: { width: 1280, height: 860 } });
  await page.goto('http://127.0.0.1:4274', { waitUntil: 'domcontentloaded' });
  await page.getByTestId('connection').waitFor({ timeout: 30_000 });
  try {
    await page.getByTestId('connection').filter({ hasText: 'Connected' }).waitFor({ timeout: 30_000 });
  } catch (error) {
    console.error(`DAGGER_LAB_BROWSER_STATE ${await page.locator('body').innerText()}`);
    throw error;
  }

  await page.getByTestId('movement-speed').fill('8');
  await page.getByTestId('endurance').fill('60');
  await page.getByTestId('apply').click();
  await page.getByTestId('live-speed').filter({ hasText: '8.00' }).waitFor();
  await page.getByTestId('max-health').filter({ hasText: '115.00' }).waitFor();
  await page.getByTestId('trace-result').filter({ hasText: '115.00' }).waitFor();

  await page.getByTestId('reset').click();
  const resetPosition = await page.getByTestId('player-position').innerText();
  await page.waitForTimeout(1_500);
  execFileSync('python3', ['scripts/x11-send-dagger-move.py'], { stdio: 'inherit' });
  const movementDeadline = Date.now() + 10_000;
  while (await page.getByTestId('player-position').innerText() === resetPosition) {
    if (Date.now() >= movementDeadline) break;
    await page.waitForTimeout(100);
  }
  const movedPosition = await page.getByTestId('player-position').innerText();
  assert.notEqual(movedPosition, resetPosition, 'physical W input did not change Rust position');
  await page.screenshot({ path: `${output}/live-after-move.png`, fullPage: true });

  await page.getByTestId('reset').click();
  const resetDeadline = Date.now() + 10_000;
  while (await page.getByTestId('player-position').innerText() !== resetPosition) {
    assert.ok(Date.now() < resetDeadline, 'reset did not restore the authoritative start position');
    await page.waitForTimeout(100);
  }
  await page.getByTestId('movement-speed').fill('0');
  await page.getByTestId('apply').click();
  await page.getByRole('alert').filter({ hasText: 'player.movement.speedUnitsPerSecond' }).waitFor();
  assert.equal(await page.getByTestId('live-speed').innerText(), '8.00');

  console.log(`DAGGER_LAB_BROWSER_OK speed=8.00 maxHealth=115.00 reset=${JSON.stringify(resetPosition)} moved=${JSON.stringify(movedPosition)} screenshot=${output}/live-after-move.png`);
} finally {
  await browser.close();
}
