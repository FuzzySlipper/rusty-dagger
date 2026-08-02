#!/usr/bin/env node
/** Real browser proof for the focused Privateer's Hold Studio workflow. */
import assert from 'node:assert/strict';
import { mkdir, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';

const { chromium } = await import(
  process.env.RUSTY_STUDIO_PLAYWRIGHT
    ?? '/home/dev/rusty-engine/studio/node_modules/@playwright/test/index.mjs',
);

const base = (process.env.RUSTY_STUDIO_URL ?? 'http://127.0.0.1:4173').replace(/\/$/u, '');
const root = resolve(process.env.RUSTY_DAGGER_ROOT ?? new URL('..', import.meta.url).pathname);
const output = resolve(process.env.RUSTY_STUDIO_BROWSER_OUT ?? `/tmp/rusty-dagger-studio-check-${process.pid}`);
const project = 'content/projects/privateers-hold.project.json';
const projectUrl = `${base}/?root=${encodeURIComponent(root)}&project=${encodeURIComponent(project)}`;
const executablePath = process.env.RUSTY_STUDIO_CHROMIUM ?? '/usr/bin/chromium';

await mkdir(output, { recursive: true });
const browser = await chromium.launch({
  headless: true,
  executablePath,
  args: ['--no-sandbox', '--use-gl=swiftshader', '--enable-unsafe-swiftshader'],
});

try {
  for (const [name, width, height] of [['desktop', 1440, 900], ['narrow', 390, 844]]) {
    const page = await browser.newPage({ viewport: { width, height }, deviceScaleFactor: 1 });
    try {
      await page.goto(projectUrl, { waitUntil: 'domcontentloaded' });
      const shell = page.locator('main.studio-layout[data-project-assets="83"]');
      await shell.waitFor({ state: 'visible', timeout: 20_000 });
      const viewport = page.locator('rusty-studio-viewport[data-renderer-status="ready"]');
      await viewport.waitFor({ state: 'visible', timeout: 20_000 });
      await page.getByText("Privateer's Hold", { exact: true }).first().waitFor({ state: 'visible' });
      const canvas = viewport.locator('canvas[aria-label="Shared Rusty renderer viewport"]');
      const bounds = await canvas.boundingBox();
      assert.ok(bounds !== null && bounds.width > 0 && bounds.height > 0, `${name} renderer canvas has no area`);
      await page.waitForTimeout(750);
      await canvas.screenshot({ path: `${output}/${name}-before-canvas.png` });

      const dungeon = page.locator('[data-entity-id="2"]');
      await dungeon.waitFor({ state: 'visible', timeout: 10_000 });
      await dungeon.dblclick();
      await page.locator('[data-entity-id="2"].is-selected').waitFor({ state: 'visible', timeout: 5_000 });
      await page.waitForTimeout(1_200);
      await canvas.screenshot({ path: `${output}/${name}-canvas.png` });
      await page.screenshot({ path: `${output}/${name}.png`, fullPage: false });
      await writeFile(`${output}/${name}.html`, await page.content());
      console.log(`${name}: focused entity 2; canvas ${Math.round(bounds.width)}x${Math.round(bounds.height)}`);
    } finally {
      await page.close();
    }
  }
} finally {
  await browser.close();
}

console.log(`STUDIO BROWSER CAPTURE PASSED; artifacts are in ${output}`);
