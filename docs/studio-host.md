# Rusty Dagger Studio host

The `dagger-studio-adapter` and `scripts/studio-host.mjs` pair are the
downstream Studio boundary for Privateer's Hold. The adapter is the authority
for project admission and readback; the Node process is only an HTTP transport,
bounded host-file/settings service, static-file server, and adapter lifecycle
owner. It does not import loading-bay gameplay or invent a TypeScript mutation
authority.

## Exact runtime

The checked-in workspace pins Rusty Engine at
`d52c9b0f3287f21eea81d465871978a117750d0c`. Build the matching Engine Studio
static application first, or point `RUSTY_ENGINE_STUDIO_STATIC_ROOT` at an
exact compatible build:

```sh
RUSTY_ENGINE_STUDIO_STATIC_ROOT=/home/dev/rusty-engine/studio/dist/apps/studio-app/browser \
  scripts/serve-studio.sh
```

The default host is `127.0.0.1:4173`. The startup page can be opened directly
with the canonical project:

```
http://127.0.0.1:4173/?root=/home/dev/rusty-dagger&project=content/projects/privateers-hold.project.json
```

`/api/studio-status` reports the exact Engine pin, consumer commit, adapter
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
against the startup URL above. The proof should show the Privateer's Hold
title, 83 authored assets, 73 scene nodes, a complete retained render frame,
and the managed host status. The authored dungeon is intentionally a large
world mesh; double-click `privateers-hold-dungeon` in the visible hierarchy to
focus the shared viewport on it before taking a visual capture. A renderer context failure under
`--disable-gpu` is not a product result; use the normal host or an explicit
SwiftShader WebGL mode when the environment has no hardware GPU.

`python3 scripts/check-adapter.py` remains the stdio open/read/close proof.
`RUSTY_STUDIO_ADAPTER` is retained only as an explicit diagnostic escape hatch;
normal regeneration always builds and checks the local adapter.
