# engine-render-check

Headless render proof of Privateer's Hold through the **real rusty-engine
renderer** — `@rusty-engine/renderer-three` (browser surface) plus
`@rusty-engine/renderer-host` texture-resource admission — consumed from the
public rusty-engine repo's `main` branch via pnpm Git-subdirectory
dependencies (the lockfile records what was last installed; `pnpm update`
moves it forward). This replaces the retired ad-hoc three.js GLB harness in
`render-check/` (do not invest in that one).

## Run

```bash
pnpm install        # once, inside engine-render-check/
node engine-render-check/check.mjs   # from the repo root; exits nonzero on failure
```

`check.mjs` does everything:

1. `dump-frame.mjs` spawns `target/debug/dagger-studio-adapter` over stdio
   (same protocol-14 line-delimited JSON as `scripts/check-adapter.py`:
   openProject -> readProject -> closeProject) and writes the `projection`
   RenderFrameDiff + `textureResources` manifest to `generated/`. The page
   never parses the project doc — the adapter readout is the only sanctioned
   project -> frame path. Build the adapter first if needed:
   `cargo build -p dagger-studio-adapter`.
2. An in-process vite dev server serves this directory plus `../content`
   (texture PNG bytes at `/textures/<name>`).
3. Headless Chromium (SwiftShader WebGL) mounts
   `mountRendererBrowserSurface(canvas, {frame, textureResourceSource, ...})`
   where the source comes from
   `loadRendererTextureResourceSource(manifest, resolver)` — byte-length and
   sha256 verified per resource, so the schemaVersion-1 silent average-color
   fallback cannot pass unnoticed. Two camera poses render: `overview`
   (bounds-derived, same math as the retired viewer) and `interior`
   (25.6,1.6,-25.6 looking down the -z block row).
4. Assertions: adapter frame carries exactly 8683 dungeon triangles /
   78 material groups; submission statistics `triangleCount >= 8683` (torch
   sprite billboards add 2 triangles each), `textureResourceCount >= 80`,
   `drawCallCount >= 78`; screenshot metrics (the
   `scripts/studio-frame-metrics.mjs` vocabulary) occupancy >= 0.02,
   uniqueColors >= 800, textureCells >= 1; zero console errors.
5. Screenshots are written to `privateers-hold-overview.png` and
   `privateers-hold-interior.png` (native drawing-buffer pixels captured
   in-page via readPixels + OffscreenCanvas, no CSS upscale interpolation).

Measured on the committed content (SwiftShader): overview triangles=8943
drawCalls=208 textures=114 occupancy=0.052 uniqueColors=10914 textureCells=2;
interior triangles=8715 drawCalls=94 occupancy=0.232 uniqueColors=5846
textureCells=10.

## Env overrides

- `RUSTY_STUDIO_ADAPTER` — adapter binary (default
  `target/debug/dagger-studio-adapter`).
- `RUSTY_RENDER_CHECK_CHROMIUM` — Chromium executable (default
  `/usr/bin/chromium`; `~/.cache/ms-playwright` builds also work).
- `RUSTY_RENDER_CHECK_PLAYWRIGHT` — playwright module specifier/URL to import
  instead of the local `@playwright/test` dependency. If a fresh
  `pnpm install` is impossible (no network), borrow the engine checkout's
  copy, exactly like `scripts/check-studio-browser.mjs` does:
  `RUSTY_RENDER_CHECK_PLAYWRIGHT=/home/dev/rusty-engine/studio/node_modules/@playwright/test/index.mjs`
  (the same path is also the automatic fallback when the local import fails).
- `RUSTY_RENDER_CHECK_PORT` — vite dev server port (default 4183).

## Files

- `check.mjs` — orchestrator: dump, serve, browse, assert, summarize.
- `dump-frame.mjs` — adapter stdio dump + camera-pose/expectation derivation.
- `index.html`, `main.js` — the renderer page (`?cam=overview|interior`).
- `package.json`, `pnpm-workspace.yaml`, `pnpm-lock.yaml` — engine deps
  tracking rusty-engine `main`; `allowBuilds` entries let the codeload
  `prepare` scripts (tsc build) and the esbuild postinstall run (the entries
  name the resolved tarball — refresh them if pnpm reports ignored builds
  after an update).
- `generated/` — gitignored dump + per-pose proof JSON.
- `privateers-hold-*.png` — committed proof screenshots (overwritten each run).

## Known engine limitations (upstream, do not paper over)

- **Sprites render untextured** (white quads). `renderer-three`
  `three-renderer.ts` `#createSprite` builds a `MeshBasicMaterial` with tint
  only — no `map` is ever bound (lines ~2025-2031); `defineSpriteAtlas` only
  registers UV rects (lines ~519-521) and `#applySpriteUv` touches UVs, not
  the material. The adapter also emits no `defineSpriteAtlas` ops. Torch
  billboards in the screenshots are therefore white; the texture assertions
  target the static mesh, which is fully textured.
- **Software pixel-ratio cap + canvas sizing feedback**: for SwiftShader the
  backing buffer is capped at 0.25x (`software-renderer-resolution.ts`), and
  `resize()` (`browser-surface.ts` ~410-427) calls `setSize(w, h, false)`
  while reading `canvas.clientWidth` — without an explicit CSS size the canvas
  collapses 4x per render. `index.html` pins the CSS size at 4x so the
  backing buffer is exactly 1280x960.
- No material-level UV transform for static meshes (tiling = mesh UVs outside
  [0,1] + wrap repeat) and no mipmaps (`generateMipmaps = false`,
  `three-renderer.ts` ~2485-2500) — expect some minification aliasing; the
  pixel thresholds above are calibrated to be robust to it.
- `mountRendererSurface` (the renderer-host game surface) admits no
  texture/mesh resource seam (`surface.ts` ~151-172), which is why this
  harness mounts the renderer-three browser surface directly.
- No static GLB path in the renderer (GLB only via the animated-mesh
  resource path); the dungeon mesh travels inline in the frame instead.
