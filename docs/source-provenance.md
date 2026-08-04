# Source provenance

This repository is a downstream Rusty Engine consumer. Engine crates are
consumed as git dependencies on the public repository
(`https://github.com/FuzzySlipper/rusty-engine`) and locked by `Cargo.lock`;
the render-check harness tracks the repo's `main` branch via pnpm.
Bumping forward is an ordinary dependency update, not a provenance ritual.

The Privateer's Hold mesh, material catalog, and project document are
generated from the local Daggerfall/Arena2 source described in
`docs/daggerfall-formats.md`. The source game data is not committed. The
runtime in `crates/dagger-runtime` is owned by this repository and consumes the
generic `engine-spatial`, `entity-state`, and `svc-collision` contracts
directly; it does not depend on `rusty-engine-demo` or `loading-bay-game`.

## Collision authority

The collision authority is the dungeon static mesh itself (rusty-engine task
6516, consumed here as task 6522). `dagger-import --format mesh-json` emits
`collision: "trimesh"`; the imported static-mesh artifact carries
`collision.kind: "trimesh"`; and `dagger-runtime` admission decodes the mesh's
full inline triangle payload (floors, walls, ceilings, ramps) into a
`StaticMeshColliderAsset` and registers one instance at identity via
`replace_static_mesh_colliders`. The hidden material-voxel `gameplayProxy`
stopgap is retired — the generated project document carries no
`voxelEnvironment`. `svc-collision` (parry3d) is the sole collision authority.

`voxelEnvironment` is accepted by admission only as an optional *additive*
authority (used by the walkthrough's adversarial controller probes); it is not
required, and a project with neither a trimesh mesh nor any voxels fails
closed.

The `dagger-walkthrough` command proves the admitted committed project through
authoritative readback: settle onto genuine trimesh support, a multi-level
descent through the doorless-reachable region, adversarial wall blocking, and
fail-closed outcomes (no support outside the trimesh, stripped-authority
rejection). The full start-room → border-block route is gated on Daggerfall
doors (task 6525 — the start room's exit is a door baked into the static
mesh); once doors open, `dagger-derive-route`
(`crates/dagger-runtime/src/bin/dagger-derive-route.rs`) derives it against
the real `DaggerRuntime`. The retired `scripts/find-route.py` (a parallel,
approximate collision model) and the committed `*.route.json` are gone.

## Studio host

The Studio adapter and bounded browser host landed in Den task 6564.
`scripts/check-adapter.py` is the stdio open/read/close proof against the
local adapter; `scripts/serve-studio.sh` + `scripts/check-studio-host.mjs` +
`scripts/check-studio-browser.sh` cover the HTTP host and the real-Chromium
textured render against a local Studio static build (see
`docs/studio-host.md`).
