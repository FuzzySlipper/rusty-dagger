import { existsSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import { basename, dirname, resolve } from 'node:path';

const repo = resolve(import.meta.dirname, '../..');
const engine = resolve(process.env.RUSTY_ENGINE_ROOT ?? resolve(repo, '../rusty-engine'));
const { productBrowserBundleAssets, PRODUCT_BROWSER_LOCAL_RUNTIME_BASE_PATH } = await import(resolve(engine, 'render/artifacts/product-browser-host/index.js'));
const output = resolve(process.argv[2] ?? `${repo}/.sprite-workbench-bundle`);
const ui = resolve(repo, 'src/sprite-ui');
const tsc = process.env.RUSTY_DAGGER_TSC ?? [resolve(engine, 'studio/node_modules/typescript/bin/tsc'), resolve(engine, 'node_modules/typescript/bin/tsc')].find(existsSync) ?? 'tsc';
if (!basename(output).startsWith('rusty-dagger-sprite-workbench')) throw new Error('output must be a dedicated rusty-dagger-sprite-workbench directory');
rmSync(output, { recursive: true, force: true });
mkdirSync(resolve(output, 'ui'), { recursive: true });
execFileSync(process.execPath, [tsc, '-p', resolve(ui, 'tsconfig.json'), '--noEmit', 'false', '--outDir', resolve(output, 'ui')], { stdio: 'inherit' });
const assets = productBrowserBundleAssets({
  engineHostModule: readFileSync(resolve(engine, 'render/artifacts/product-browser-host/product-browser-host.js'), 'utf8'),
  uiModule: './ui/workbench.js', runtimeAdapterModule: './runtime-adapter.js', lifecycleMode: 'realtime', realtimeAdvanceOwner: 'browser',
  uiProjection: { expectedStream: 'worldrpg.sprite-workbench', expectedContract: 'worldrpg.sprite-workbench.snapshot.v1' },
});
for (const asset of assets) { const target = resolve(output, asset.name); mkdirSync(dirname(target), { recursive: true }); writeFileSync(target, asset.content); }
// The Engine owns the browser-to-local-runtime route family.  This tiny
// generated descriptor only binds the generated host bundle to that published
// Engine asset; it is not a downstream bridge or transport implementation.
writeFileSync(resolve(output, 'runtime-adapter.js'), `export const PRODUCT_RUNTIME_HTTP_BASE_PATH = ${JSON.stringify(PRODUCT_BROWSER_LOCAL_RUNTIME_BASE_PATH)};\n`);
console.log(output);
