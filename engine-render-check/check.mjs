#!/usr/bin/env node
/**
 * Headless render proof of Privateer's Hold through the REAL rusty-engine
 * renderer (@rusty-engine/renderer-three browser surface + renderer-host
 * texture resource admission), replacing the retired ad-hoc three.js harness
 * in render-check/.
 *
 * One command runs everything and exits nonzero on failure:
 *
 *     node engine-render-check/check.mjs
 *
 * Pipeline: dump the protocol-14 readout from target/debug/dagger-studio-adapter
 * (dump-frame.mjs) -> serve this dir + ../content via an in-process vite dev
 * server -> headless Chromium (SwiftShader WebGL) renders two camera poses ->
 * submission statistics + screenshot pixel metrics are asserted.
 *
 * Env overrides:
 * - RUSTY_STUDIO_ADAPTER       adapter binary (default target/debug/dagger-studio-adapter)
 * - RUSTY_RENDER_CHECK_CHROMIUM  Chromium executable (default /usr/bin/chromium)
 * - RUSTY_RENDER_CHECK_PLAYWRIGHT  playwright module URL to import instead of
 *   the local dependency (e.g. borrow /home/dev/rusty-engine/studio/node_modules/@playwright/test/index.mjs)
 */
import { readFile, writeFile } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { createServer } from 'vite';
import { dumpFrame } from './dump-frame.mjs';
import { decodePng, frameMetrics } from '../scripts/studio-frame-metrics.mjs';

const HERE = dirname(fileURLToPath(import.meta.url));
const ROOT = resolve(HERE, '..');
const PORT = Number(process.env.RUSTY_RENDER_CHECK_PORT ?? 4183);

const { chromium } = await import(
  process.env.RUSTY_RENDER_CHECK_PLAYWRIGHT ?? '@playwright/test'
).catch(async () => {
  // Fallback: borrow the engine checkout's playwright (see README note).
  return import(pathToFileURL(
    '/home/dev/rusty-engine/studio/node_modules/@playwright/test/index.mjs',
  ).href);
});

const { poses, expectations } = await dumpFrame();
console.log(
  `adapter readout: triangles=${expectations.triangles} `
  + `materialGroups=${expectations.materialGroups} textureResources=${expectations.textureResources}`,
);

const server = await createServer({
  root: HERE,
  logLevel: 'warn',
  publicDir: resolve(ROOT, 'content'),
  server: { host: '127.0.0.1', port: PORT, strictPort: true, fs: { allow: [ROOT] } },
});
await server.listen();

const executablePath = process.env.RUSTY_RENDER_CHECK_CHROMIUM ?? '/usr/bin/chromium';
const browser = await chromium.launch({
  headless: true,
  executablePath,
  args: ['--no-sandbox', '--enable-webgl', '--ignore-gpu-blocklist', '--use-angle=swiftshader', '--enable-unsafe-swiftshader'],
});

const failures = [];
const check = (condition, message) => {
  if (!condition) failures.push(message);
};

try {
  for (const cam of ['overview', 'interior']) {
    const page = await browser.newPage({ viewport: { width: 1280, height: 960 }, deviceScaleFactor: 1 });
    const consoleErrors = [];
    page.on('console', (message) => {
      if (message.type() === 'error') consoleErrors.push(message.text());
    });
    page.on('pageerror', (error) => consoleErrors.push(String(error)));
    try {
      await page.goto(`http://127.0.0.1:${PORT}/?cam=${cam}`, { waitUntil: 'domcontentloaded' });
      await page.waitForFunction(
        () => window.__proof?.ready === true || window.__failure !== undefined,
        undefined,
        { timeout: 60_000 },
      );
      const failure = await page.evaluate(() => window.__failure ?? null);
      check(failure === null, `${cam}: page failed to mount renderer: ${failure}`);
      const proof = await page.evaluate(() => window.__proof ?? null);

      let metrics = null;
      const shot = resolve(HERE, `privateers-hold-${cam}.png`);
      if (proof !== null && typeof proof.framePng === 'string') {
        // Native drawing-buffer pixels captured in-page (no CSS upscale).
        await writeFile(shot, Buffer.from(proof.framePng.split(',', 2)[1], 'base64'));
        metrics = frameMetrics(decodePng(await readFile(shot)));
      } else {
        check(false, `${cam}: page produced no frame capture`);
      }
      await writeFile(
        resolve(HERE, 'generated', `proof-${cam}.json`),
        `${JSON.stringify({ proof: { ...proof, framePng: undefined }, metrics, consoleErrors }, null, 1)}\n`,
      );

      if (proof !== null && metrics !== null) {
        const stats = proof.statistics;
        // The dungeon mesh is a single static-mesh instance, so its 8683
        // triangles are drawn whole whenever it passes frustum culling; torch
        // sprite billboards add 2 triangles each on top.
        check(
          proof.expectations.triangles === 8683,
          `${cam}: adapter frame carries ${proof.expectations.triangles} mesh triangles != 8683 (committed dungeon mesh)`,
        );
        check(
          stats.triangleCount >= 8683,
          `${cam}: triangleCount ${stats.triangleCount} < 8683 (dungeon mesh not fully drawn)`,
        );
        check(
          stats.textureResourceCount >= 80,
          `${cam}: textureResourceCount ${stats.textureResourceCount} < 80 (texture fallback suspected)`,
        );
        check(
          stats.textureResourceCount >= proof.expectations.textureResources,
          `${cam}: admitted textures ${stats.textureResourceCount} < manifest ${proof.expectations.textureResources}`,
        );
        check(
          stats.drawCallCount >= proof.expectations.materialGroups,
          `${cam}: drawCallCount ${stats.drawCallCount} < ${proof.expectations.materialGroups} material groups`,
        );
        check(
          metrics.occupancy >= 0.02,
          `${cam}: occupancy ${metrics.occupancy.toFixed(3)} < 0.02 (no meaningful project pixels)`,
        );
        check(
          metrics.uniqueColors >= 800,
          `${cam}: uniqueColors ${metrics.uniqueColors} < 800 (flat average-color fallback suspected)`,
        );
        check(
          metrics.textureCells >= 1,
          `${cam}: no texel-frequency detail cells (textureCells=0)`,
        );
        console.log(
          `${cam}: triangles=${stats.triangleCount} drawCalls=${stats.drawCallCount} `
          + `textures=${stats.textureResourceCount} materials=${stats.materialResourceCount} `
          + `lights=${proof.retainedLightCount} occupancy=${metrics.occupancy.toFixed(3)} `
          + `uniqueColors=${metrics.uniqueColors} textureCells=${metrics.textureCells} `
          + `maxCellStddev=${metrics.maxCellStddev}`,
        );
      }
      check(
        consoleErrors.length === 0,
        `${cam}: ${consoleErrors.length} console errors: ${consoleErrors.slice(0, 3).join(' | ')}`,
      );
    } finally {
      await page.close();
    }
  }
} finally {
  await browser.close();
  await server.close();
}

if (failures.length > 0) {
  console.log('ENGINE RENDER CHECK FAILED:');
  for (const failure of failures) console.log(` - ${failure}`);
  process.exit(1);
}
console.log(
  'ENGINE RENDER CHECK PASSED; screenshots: '
  + 'engine-render-check/privateers-hold-overview.png, engine-render-check/privateers-hold-interior.png',
);
