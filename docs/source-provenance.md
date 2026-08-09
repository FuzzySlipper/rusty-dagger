# Source provenance

This repository is a downstream Rusty Engine consumer. Exactly one normal
dependency, the public `rusty-engine` Rust facade, follows the provider's
public `main` branch and is resolved by `Cargo.lock`. Owner namespaces remain
visible under `rusty_engine::<owner>`. `scripts/check-engine-freshness.py`
fails loudly when the lock is no longer current; moving forward is an ordinary
dependency update, not a pin-maintenance ritual.

The Engine renderer implementation, TypeScript packages, Three backend,
webview bridge, bootstrap document, and generated private artifact remain
upstream-owned. Rusty Dagger neither imports nor copies them. Its native
diagnostic calls Engine's Rust host adapter through the facade; Engine owns
the sensitive Rust-to-renderer relationship privately.

The Privateer's Hold mesh, material catalog, and project document are
generated from the local Daggerfall/Arena2 source described in
`docs/daggerfall-formats.md`. The source game data is not committed. The
runtime in `crates/dagger-runtime` is owned by this repository and consumes
the generic `rusty_engine::engine_spatial`, `rusty_engine::entity_state`, and
`rusty_engine::svc_collision` namespaces through the facade; it does not
depend on `rusty-engine-demo` or `loading-bay-game`.

The enemy atlas PNGs and manifests are generated, not hand-edited. Task 6707
changed their durable extraction layout from an unbounded single row to a
deterministic multi-row grid capped by Engine's public 4096-pixel texture
dimension, then regenerated the checked project from local Arena2 data.

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
