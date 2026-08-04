# Rusty Dagger Studio host

The `dagger-studio-adapter` and `scripts/studio-host.mjs` pair are the
downstream Studio boundary for Privateer's Hold. The adapter is the authority
for project admission and readback; the Node process is only an HTTP transport,
bounded host-file/settings service, static-file server, and adapter lifecycle
owner. It does not import loading-bay gameplay or invent a TypeScript mutation
authority.

## Runtime

The host serves any local Rusty Engine Studio production build — the engine
moves fast, so drift is fixed forward rather than gated behind exact-revision
provenance checks. The conventional default is the sibling checkout's build at
`/home/dev/rusty-engine/studio/dist/apps/studio-app/browser`; point
`RUSTY_ENGINE_STUDIO_STATIC_ROOT` at any other build:

```sh
RUSTY_ENGINE_STUDIO_STATIC_ROOT=/home/dev/rusty-engine-head/studio/dist/apps/studio-app/browser \
  scripts/serve-studio.sh
```

Build the static app from an engine checkout with:

```sh
cd /home/dev/rusty-engine/studio
npx --yes pnpm@11.7.0 install --frozen-lockfile --prefer-offline
npx --yes pnpm@11.7.0 run build
```

The default host is `127.0.0.1:4173`.
The startup page can be opened directly
with the canonical project:

```
http://127.0.0.1:4173/?root=/home/dev/rusty-dagger&project=content/projects/privateers-hold.project.json
```

`/api/studio-status` reports the consumer commit, adapter
identity/protocol, and adapter binary hash. The host serves only normalized
static paths and bounded regular render resources whose admitted SHA-256 hash
matches the request. Host-file browsing excludes symlinks and caps one
response at 512 entries. User settings are stored outside the repository under
the per-project XDG config key and are written atomically with an expected-hash
check.

## Focused checks

These checks are intentionally smaller than the full Studio CI suite:

```sh
cargo build -p dagger-studio-adapter
python3 scripts/check-adapter.py
node scripts/check-studio-host.mjs # while scripts/serve-studio.sh is running
scripts/check-studio-browser.sh   # Chromium + SwiftShader, same host
```

For a human-visible browser proof, use Chromium at desktop and narrow sizes
against the startup URL above. `scripts/check-studio-browser.sh` opens the
project, waits for the renderer's texture-resource traffic to settle, disables
the editor grid through the visible View menu, double-clicks
`privateers-hold-dungeon` through the normal visible hierarchy, and performs a
bounded normal viewport orbit before capturing the renderer canvas, the full
page, and the DOM. Beyond the DOM/readout assertions (title, >= 160 authored
assets, ready renderer, pre/post-focus pixel change), the proof now audits the
textured render itself (pure-Node PNG metrics in
`scripts/studio-frame-metrics.mjs`; no PIL):

- **Texture fetch/hash audit**: every `/api/studio-render-resource` response in
  the run must be HTTP 200 whose body SHA-256 equals the admitted
  `contentHash`; every `sourcePath` must stay under `content/textures/`; at
  least 60 unique texture resources must be fetched (today all 80).
- **Non-flat-texture assertions**: occupancy >= 0.02 vs the renderer clear
  color, >= 800 unique geometry colors and >= 1 texel-frequency grid cell
  (luminance stddev >= 6) — gates the average-color fallback (~100-500
  colors, no such cells) cannot pass.
- **GLB comparison**: the focused frame's 12-bin hue histogram must overlap
  the best of the three committed GLB render references (`render-check/*.png`)
  by histogram intersection >= 0.40. Studio and the GLB viewer use different
  lighting rigs and framing, so this is a hue-signature tolerance rather than
  pixel equality; measured ~0.65 desktop / ~0.70 narrow, while the untextured
  average-color frame scores ~0.25.

Per-viewport metric reports land in `*-metrics.json` next to the screenshots
in the artifact directory. The authored dungeon is intentionally a large world
mesh; the focused canvas captures are the useful visual artifact. A renderer
context failure under `--disable-gpu` is not a product result; use the normal
host or an explicit SwiftShader WebGL mode when the environment has no
hardware GPU.

`python3 scripts/check-adapter.py` remains the stdio open/read/close proof.
`RUSTY_STUDIO_ADAPTER` is retained only as an explicit diagnostic escape hatch;
normal regeneration always builds and checks the local adapter.
