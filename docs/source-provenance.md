# Source provenance

This repository is a downstream Rusty Engine consumer. The generic engine
dependency is pinned in `Cargo.toml`, `Cargo.lock`, and `engine-source.json` to
the exact public revision:

`https://github.com/FuzzySlipper/rusty-engine@880a119466faebbf19ed05e39206ff4ba87237a2`

The Privateer's Hold mesh, material catalog, gameplay proxy, project document,
and route are generated from the local Daggerfall/Arena2 source described in
`docs/daggerfall-formats.md`. The source game data is not committed. The
runtime in `crates/dagger-runtime` is owned by this repository and consumes the
generic `engine-spatial` and `entity-state` contracts directly; it does not
depend on `rusty-engine-demo` or `loading-bay-game`.

The hidden material-voxel `gameplayProxy` remains the current collision
authority until the upstream triangle-mesh collision task lands. The visible
mesh and the proxy are intentionally separate authored artifacts. The
`dagger-walkthrough` command proves the admitted committed project, route,
grounding, traversal, and invalid-proxy outcomes using those real artifacts.

The Studio adapter/browser host is a separate downstream follow-up (Den task
6564). Until it lands, `scripts/check-adapter.py` is an explicitly configured
integration probe and is not part of the runtime crate's dependency graph.
