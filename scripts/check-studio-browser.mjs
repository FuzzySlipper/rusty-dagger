#!/usr/bin/env node
/**
 * Real browser proof for the textured Privateer's Hold Studio workflow.
 *
 * Proof contract (all thresholds calibrated against the exact pinned build and
 * the committed project; measured values are recorded in *-metrics.json):
 *
 * - Texture resource audit: every `/api/studio-render-resource` response during
 *   the run must be HTTP 200, its body SHA-256 must equal the admitted
 *   `contentHash` in the request URL, and every `sourcePath` must stay under
 *   `content/textures/`. At least 60 unique texture resources must be fetched
 *   (the renderer preloads the adapter manifest; today it fetches all 80).
 * - Focused canvas gates: changed_ratio >= 0.10 (focus re-frames the dungeon),
 *   occupancy >= 0.02 (meaningful project pixels vs the renderer clear color),
 *   uniqueColors >= 800 among geometry pixels (the average-color fallback
 *   renders ~100-500; the textured render measures ~2000-3000),
 *   textureCells >= 1 (6x6 grid cell dominated by geometry with luminance
 *   stddev >= 6: texel-frequency alternation flat shading cannot produce),
 *   huePixels >= 5000 (histogram sample floor).
 * - GLB comparison: the focused frame's 12-bin hue histogram must overlap the
 *   best of the three committed GLB render references (render-check/*.png) by
 *   histogramIntersection >= 0.40. Studio and the GLB viewer use different
 *   lighting rigs and framing, so this is a hue-signature tolerance, not pixel
 *   equality; measured 0.65 desktop / 0.70 narrow, while the untextured
 *   average-color frame scores ~0.25.
 */
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import {
  decodePng,
  differenceRatio,
  frameMetrics,
  histogramIntersection,
} from './studio-frame-metrics.mjs';

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

const GLB_REFERENCES = [
  'render-check/privateers-hold.png',
  'render-check/privateers-hold-top.png',
  'render-check/privateers-hold-interior.png',
];

const referenceHistograms = [];
for (const reference of GLB_REFERENCES) {
  const image = decodePng(await readFile(resolve(root, reference)));
  referenceHistograms.push({ reference, histogram: frameMetrics(image).hueHistogram });
}

await mkdir(output, { recursive: true });
const browser = await chromium.launch({
  headless: true,
  executablePath,
  args: ['--no-sandbox', '--use-gl=swiftshader', '--enable-unsafe-swiftshader'],
});

try {
  for (const [name, width, height] of [['desktop', 1440, 900], ['narrow', 390, 844]]) {
    const page = await browser.newPage({ viewport: { width, height }, deviceScaleFactor: 1 });
    const resourceResponses = [];
    page.on('response', (response) => {
      if (new URL(response.url()).pathname === '/api/studio-render-resource') {
        resourceResponses.push(response);
      }
    });
    try {
      await page.goto(projectUrl, { waitUntil: 'domcontentloaded' });
      const shell = page.locator('main.studio-layout[data-project-assets]');
      await shell.waitFor({ state: 'visible', timeout: 20_000 });
      const assetCount = Number(await shell.getAttribute('data-project-assets'));
      assert.ok(
        assetCount >= 160,
        `expected the textured project (>= 160 assets incl. textures), got ${assetCount}`,
      );
      const viewport = page.locator('rusty-studio-viewport[data-renderer-status="ready"]');
      await viewport.waitFor({ state: 'visible', timeout: 20_000 });
      await page.getByText("Privateer's Hold", { exact: true }).first().waitFor({ state: 'visible' });
      const canvas = viewport.locator('canvas[aria-label="Shared Rusty renderer viewport"]');
      const bounds = await canvas.boundingBox();
      assert.ok(bounds !== null && bounds.width > 0 && bounds.height > 0, `${name} renderer canvas has no area`);

      // Let the renderer's texture-resource traffic settle before capturing so
      // both frames carry the textured state.
      let observed = -1;
      for (let idle = 0; idle < 40; idle += 1) {
        await page.waitForTimeout(500);
        if (resourceResponses.length > 0 && resourceResponses.length === observed && idle > 2) break;
        observed = resourceResponses.length;
      }

      const viewMenuButton = page.getByRole('button', { name: 'View', exact: true });
      await viewMenuButton.click();
      const gridToggle = page.getByLabel('Editor grid');
      if (await gridToggle.isChecked()) await gridToggle.uncheck();
      assert.equal(await gridToggle.isChecked(), false, `${name} editor grid did not turn off`);
      await viewMenuButton.click();
      await page.waitForTimeout(500);
      await canvas.screenshot({ path: `${output}/${name}-before-canvas.png` });

      const dungeon = page.locator('[data-entity-id="2"]');
      await dungeon.waitFor({ state: 'visible', timeout: 10_000 });
      await dungeon.dblclick();
      await page.locator('[data-entity-id="2"].is-selected').waitFor({ state: 'visible', timeout: 5_000 });
      await page.waitForTimeout(1_200);
      // Bring the focused dungeon fully into frame with the same normal
      // viewport orbit gesture a human uses (both layouts).
      const focusedBounds = await canvas.boundingBox();
      assert.ok(focusedBounds !== null, `${name} renderer canvas disappeared before orbit`);
      const startX = focusedBounds.x + 100;
      const startY = focusedBounds.y + focusedBounds.height / 2;
      await page.mouse.move(startX, startY);
      await page.mouse.down({ button: 'left' });
      await page.mouse.move(startX + 450, startY, { steps: 12 });
      await page.mouse.up({ button: 'left' });
      await page.waitForTimeout(1_200);
      await canvas.screenshot({ path: `${output}/${name}-canvas.png` });
      await page.screenshot({ path: `${output}/${name}.png`, fullPage: false });
      await writeFile(`${output}/${name}.html`, await page.content());

      // Frame metrics.
      const beforeImage = decodePng(await readFile(`${output}/${name}-before-canvas.png`));
      const afterImage = decodePng(await readFile(`${output}/${name}-canvas.png`));
      const changed = differenceRatio(beforeImage, afterImage);
      const metrics = frameMetrics(afterImage);
      const intersections = Object.fromEntries(
        referenceHistograms.map(({ reference, histogram }) => [
          reference,
          histogramIntersection(metrics.hueHistogram, histogram),
        ]),
      );
      const bestIntersection = Math.max(...Object.values(intersections));

      // Texture resource audit for this viewport.
      const uniqueResources = new Map();
      let statusFailures = 0;
      let hashFailures = 0;
      let outsideRoot = 0;
      for (const response of resourceResponses) {
        const url = new URL(response.url());
        const expected = url.searchParams.get('contentHash') ?? '';
        const sourcePath = url.searchParams.get('sourcePath') ?? '';
        if (!sourcePath.startsWith('content/textures/')) outsideRoot += 1;
        if (response.status() !== 200) {
          statusFailures += 1;
          continue;
        }
        const body = await response.body();
        const actual = `sha256:${createHash('sha256').update(body).digest('hex')}`;
        if (actual !== expected) hashFailures += 1;
        uniqueResources.set(expected, body.length);
      }

      const report = {
        viewport: { name, width, height },
        assetCount,
        changedRatio: changed,
        occupancy: metrics.occupancy,
        uniqueColors: metrics.uniqueColors,
        geometryCells: metrics.geometryCells,
        textureCells: metrics.textureCells,
        maxCellStddev: metrics.maxCellStddev,
        huePixels: metrics.huePixels,
        glbReferenceIntersections: intersections,
        bestGlbIntersection: bestIntersection,
        textureResources: {
          responses: resourceResponses.length,
          uniqueFetched: uniqueResources.size,
          statusFailures,
          hashFailures,
          outsideRoot,
        },
      };
      await writeFile(`${output}/${name}-metrics.json`, `${JSON.stringify(report, null, 1)}\n`);

      assert.ok(changed >= 0.10, `${name}: focusing the dungeon did not change enough of the renderer frame (${changed.toFixed(3)})`);
      assert.ok(metrics.occupancy >= 0.02, `${name}: focused frame has no meaningful project pixels (occupancy ${metrics.occupancy.toFixed(3)})`);
      assert.ok(metrics.uniqueColors >= 800, `${name}: focused frame is not richly textured (uniqueColors ${metrics.uniqueColors})`);
      assert.ok(metrics.textureCells >= 1, `${name}: no texel-frequency detail cells in the focused frame`);
      assert.ok(metrics.huePixels >= 5000, `${name}: too few geometry hue samples (${metrics.huePixels}) for the GLB comparison`);
      assert.ok(
        bestIntersection >= 0.40,
        `${name}: focused frame hue signature does not match the GLB references (best ${bestIntersection.toFixed(3)})`,
      );
      assert.ok(uniqueResources.size >= 60, `${name}: renderer fetched too few unique texture resources (${uniqueResources.size})`);
      assert.equal(statusFailures, 0, `${name}: ${statusFailures} texture resource responses were not HTTP 200`);
      assert.equal(hashFailures, 0, `${name}: ${hashFailures} texture resource bodies failed the admitted content hash`);
      assert.equal(outsideRoot, 0, `${name}: ${outsideRoot} texture resources resolved outside content/textures/`);

      console.log(
        `${name}: canvas ${Math.round(bounds.width)}x${Math.round(bounds.height)}; changed=${changed.toFixed(3)} `
        + `occupancy=${metrics.occupancy.toFixed(3)} uniqueColors=${metrics.uniqueColors} textureCells=${metrics.textureCells} `
        + `glbBest=${bestIntersection.toFixed(3)} textures=${uniqueResources.size} fetched/hash-ok`,
      );
    } finally {
      await page.close();
    }
  }
} finally {
  await browser.close();
}

console.log(`STUDIO BROWSER CHECK PASSED; focused screenshots, DOM captures, and metrics are in ${output}`);
