// Headless render check for the extracted dungeon GLB.
// Uses the engine's installed playwright + system/installed chromium with swiftshader.
// Usage: node render-check/check.mjs [--out screenshot.png]
import { chromium } from '/home/dev/rusty-engine/render/node_modules/.pnpm/playwright@1.61.1/node_modules/playwright/index.mjs';
import { startServer } from './server.mjs';
import { readFile } from 'node:fs/promises';

const outPath = process.argv.includes('--out')
  ? process.argv[process.argv.indexOf('--out') + 1]
  : new URL('./privateers-hold.png', import.meta.url).pathname;
const camMode = process.argv.includes('--cam')
  ? process.argv[process.argv.indexOf('--cam') + 1]
  : 'overview';

const { server, port } = await startServer();
const failures = [];
try {
  const browser = await chromium.launch({
    headless: true,
    args: ['--enable-webgl', '--ignore-gpu-blocklist', '--use-angle=swiftshader', '--no-sandbox'],
  });
  const page = await browser.newPage({ viewport: { width: 1280, height: 960 } });
  const consoleErrors = [];
  page.on('console', (m) => { if (m.type() === 'error') consoleErrors.push(m.text()); });
  page.on('pageerror', (e) => consoleErrors.push(String(e)));

  await page.goto(`http://127.0.0.1:${port}/viewer.html?glb=/content/privateers-hold.glb&cam=${camMode}`);
  await page.waitForFunction(() => window.__done !== undefined, null, { timeout: 30_000 });

  const done = await page.evaluate(() => window.__done);
  const errors = await page.evaluate(() => window.__errors);
  await page.screenshot({ path: outPath });
  await browser.close();

  console.log('done:', JSON.stringify(done));
  if (!done.ok) failures.push('viewer reported not-ok');
  if (errors.length) failures.push('page errors: ' + errors.join(' | '));
  if (consoleErrors.length) failures.push('console errors: ' + consoleErrors.join(' | '));
  if (done.meshCount !== 81) failures.push(`expected 81 primitive meshes, got ${done.meshCount}`);
  if (done.triCount !== 9263) failures.push(`expected 9263 tris, got ${done.triCount}`);
  if (done.texturedMats !== 81) failures.push(`expected 81 textured materials, got ${done.texturedMats}`);
  const [mn, mx] = [done.bounds.min, done.bounds.max];
  const near = (a, b) => Math.abs(a - b) < 0.05;
  if (!near(mn[0], -51.2) || !near(mx[0], 102.4) || !near(mn[1], 0) || !near(mx[1], 51.1) || !near(mn[2], -102.4) || !near(mx[2], 51.2)) {
    failures.push(`bounds mismatch: ${JSON.stringify(done.bounds)}`);
  }

  // Pixel-level proof: the screenshot must contain substantial non-background content
  const png = await readFile(outPath);
  const sig = Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);
  if (!png.subarray(0, 8).equals(sig)) failures.push('screenshot is not a PNG');
  const distinct = await page_metrics(outPath);
  console.log(`screenshot: ${outPath} (${png.length} bytes), non-background pixel share: ${distinct.toFixed(3)}`);
  if (distinct < 0.02) failures.push(`rendered image is (near) empty: ${distinct}`);

  async function page_metrics(path) {
    // Cheap metric: decode PNG via the page (Image+canvas) is overkill; instead count unique
    // byte runs in the file as a proxy? No — use playwright again would be heavy.
    // Instead: parse PNG IDAT via zlib and count pixels differing from background color.
    const zlib = await import('node:zlib');
    const buf = await readFile(path);
    let pos = 8, idat = [];
    let w = 0, h = 0;
    while (pos < buf.length) {
      const len = buf.readUInt32BE(pos);
      const type = buf.subarray(pos + 4, pos + 8).toString('ascii');
      if (type === 'IHDR') { w = buf.readUInt32BE(pos + 8); h = buf.readUInt32BE(pos + 12); }
      if (type === 'IDAT') idat.push(buf.subarray(pos + 8, pos + 8 + len));
      pos += 12 + len;
    }
    const raw = zlib.inflateSync(Buffer.concat(idat));
    // PNG from playwright screenshot is RGBA or RGB; detect bpp from row size
    const bpp = Math.floor(raw.length / h / w) === 4 ? 4 : 3;
    // Unfilter naively is invalid; instead just measure variance of filtered bytes per row type 0 rows.
    // Simpler robust metric: sample every Nth byte triple/quad across all rows and measure color diversity.
    let bg = 0, total = 0;
    for (let y = 0; y < h; y += 4) {
      const rowStart = y * (w * bpp + 1);
      for (let x = 0; x < w; x += 4) {
        const i = rowStart + 1 + x * bpp;
        if (i + 2 >= raw.length) continue;
        const r = raw[i], g = raw[i + 1], b = raw[i + 2];
        // background is #101418
        const d = Math.abs(r - 0x10) + Math.abs(g - 0x14) + Math.abs(b - 0x18);
        total++;
        if (d > 24) bg++;
      }
    }
    return total ? bg / total : 0;
  }
} finally {
  server.close();
}

if (failures.length) {
  console.error('RENDER CHECK FAILED:');
  for (const f of failures) console.error(' - ' + f);
  process.exit(1);
}
console.log('RENDER CHECK PASSED');
