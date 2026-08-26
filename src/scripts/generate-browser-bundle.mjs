import { mkdir, readFile, rm, writeFile } from 'node:fs/promises';
import { join } from 'node:path';
import { productBrowserBundleAssets } from '/home/dev/rusty-engine/render/artifacts/product-browser-host/index.js';

const [output] = process.argv.slice(2);
if (output === undefined) throw new Error('usage: generate-browser-bundle.mjs <output-directory>');

const engineHostModule = await readFile('/home/dev/rusty-engine/render/artifacts/product-browser-host/product-browser-host.js', 'utf8');
const assets = productBrowserBundleAssets({
  engineHostModule,
  uiModule: './ui/main.js',
  runtimeAdapterModule: './runtime-adapter.js',
  lifecycleMode: 'realtime',
  realtimeAdvanceOwner: 'browser',
  uiProjection: { expectedStream: 'dagger.hud', expectedContract: 'dagger.ui.snapshot.v1' },
});
await mkdir(join(output, 'ui'), { recursive: true });
await rm(join(output, 'renderer-preload.json'), { force: true });
for (const asset of assets) {
  const path = join(output, asset.name);
  await mkdir(join(path, '..'), { recursive: true });
  await writeFile(path, asset.content);
}
await writeFile(join(output, 'runtime-adapter.js'), 'export const PRODUCT_RUNTIME_HTTP_BASE_PATH = "/__rusty/product/runtime/";\n');
