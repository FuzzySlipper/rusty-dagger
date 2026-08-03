# Session handoff — 2026-08-02

Dump of everything recoverable about this project, written before a deliberate
session restart. Reconstructed from the three durable sources of truth — Den
(`rusty-dagger` project), this repo, and `docs/` — not from conversation
memory, which is by design not a source of truth here ("lessons in code, not
in conversation", docs/design.md). If anything below disagrees with Den or the
tree, Den and the tree win.

## Project in one paragraph

Rusty Dagger ports the **Privateer's Hold experience** (Daggerfall's starting
dungeon) and every system needed to support it into Rusty Engine, using the
original Bethesda data files as a read-local-only content source. It is the
first stage of a longer arc toward an original Daggerfall-ish successor game,
built per the **successor pattern**: systems live behind small crate
boundaries so they port out cleanly; there is no rush to the headline
deliverable at the cost of tangled systems. Full durable intent:
[docs/design.md](design.md). Format reference:
[docs/daggerfall-formats.md](daggerfall-formats.md).

## Current state snapshot (verified at dump time)

- Branch `main` at `fefe8b4` ("Make narrow Studio dungeon capture visible"),
  in sync with `origin/main`.
- **Uncommitted changes**: 13 files under `crates/arena2/src` and
  `crates/dagger-import/src` (+239/−85). Verified to be **purely a `cargo fmt`
  run** — every hunk is line-wrapping / struct-literal expansion / blank-line
  removal; no semantic changes. `cargo fmt --all -- --check` passes only
  *with* these changes applied, and `cargo test --workspace --locked` passes
  (17 tests: 9 arena2, 3 dagger-import, 5 dagger-runtime). **Recommendation:
  commit them as a formatting commit** (e.g. "Apply cargo fmt to
  arena2/dagger-import") rather than discard, otherwise the tree stays
  fmt-dirty. These edits pre-date the 6563/6564 review loops and were
  explicitly excluded from those review ranges ("unrelated pre-existing
  arena2/dagger-import edits").
- Den board: 5 done, 1 review, 8 planned, 0 blocked/in-progress.

## Den task board (project `rusty-dagger`)

### Done
- **6518** Studio-openable project doc for Privateer's Hold
  (`content/projects/privateers-hold.project.json`, schemaVersion 24 family).
- **6519** Companion-reuse survey →
  [docs/companion-reuse.md](companion-reuse.md) (loading-bay FP controller +
  doors, engine-ui kit, collision doctrine; roguelike/d20/view assessed).
- **6520** First-person walk-through: spawn at start marker, traverse
  Privateer's Hold with collision. (Parent of 6563.)
- **6563** Self-contained downstream runtime at exact Engine pin
  `d52c9b0f3287f21eea81d465871978a117750d0c`; `dagger-runtime` crate owns
  admission + FP controller; `dagger-walkthrough` is the real-project
  headless proof. Reviewed looks_good at `11073bb` (round 4; step-up
  retry/rollback findings R6563-1/4/5 verified fixed). (Parent of 6564.)
- **6564** Rusty-dagger-owned protocol-14 Studio adapter + bounded browser
  host (`dagger-studio-adapter`, `scripts/studio-host.mjs`,
  `scripts/serve-studio.sh`). Reviewed looks_good at exact `fefe8b4`
  (round 3, finalized 2026-08-02 ~23:00 UTC). Fixed along the way:
  R6564-1 (static root now bound to immutable Engine `d52c9b0` artifact
  provenance `c0359039…6494a2`, unproven roots fail startup) and R6564-2
  (real Chromium desktop 1440x900 + narrow 390x844 captures with actual
  dungeon geometry, changed/foreground pixel thresholds, hierarchy
  double-click focus, grid disabled via visible UI).

### In review — attention
- **6524** Lights: RDB light objects → scene point lights. Committed as
  `7166653` and status is `review`, but Den shows **zero review rounds and
  zero findings** — the review never formally happened. Next session should
  either request a review round for it or confirm the state and move it.

### Planned (Den `next_task` order starts at 6521)
- **6521** (p3) Consume upstream rusty-engine **6515** (static-mesh UVs):
  emit UV stream in mesh-json, drop the average-color workaround, match the
  textured GLB.
- **6522** (p3) Consume upstream rusty-engine **6516** (trimesh collision):
  triangle-accurate traversal; doors stay out of the static trimesh.
- **6523** (p3) Billboards: parse RDB flat resources (type 0x03), render as
  Y-facing billboards with transparency (DFU `RDBLayout.AddFlats`).
- **6525** (p3) Action doors: parse RDB model action records
  (`has_action` already flagged in arena2::rdb), sliding doors as separate
  scene nodes. Has 1 (finished) dependency.
- **6529** (p3) Modularity gate: recurring structural check — split
  arena2/dagger-import into per-system crates as features land.
- **6526** (p4) Water: per-block water level planes (DFU `AddWater`).
- **6527** (p4) Classic per-location dungeon texture table: port DFRandom;
  Privateer's Hold MapId 187853213 must reproduce the DFU/classic table.
- **6528** (p4) Automap: block/door/start-marker metadata → toggleable
  overlay.

## System map (crates)

- `crates/arena2` — pure read-only readers of classic data files (BSA, MAPS,
  RDB, ARCH3D, TEXTURE, PAL, PAK). All format claims proven by tests against
  the real data (`local/arena2`, overridable via `ARENA2_DIR`; canonical copy
  `/home/research/daggerfall-files`).
- `crates/dagger-import` — CLI glue: MAPS layout → RDB objects → ARCH3D
  meshes → world-space triangles grouped by texture; emits GLB (default,
  textured) or engine mesh-json (untextured average-color stopgap until 6521).
- `crates/dagger-runtime` — Daggerfall-owned project admission, portable FP
  controller (opt-in fall/settle + bounded step-up; Engine motion system is
  the sole collision authority), `dagger-walkthrough` headless proof.
- `crates/dagger-studio-adapter` — protocol-14 read-only Studio adapter;
  reuses dagger-runtime admission; mutations fail closed with typed
  `unsupported_operation`.
- `scripts/studio-host.mjs` / `serve-studio.sh` — bounded HTTP bridge serving
  the exact pinned Engine Studio static build (provenance-verified).
- `scripts/generate-project.py` — generates the project doc, including the
  hidden `gameplayProxy` material-voxel collision environment rasterized from
  the dungeon mesh (stopgap until upstream trimesh, 6516).
- `scripts/find-route.py` — derives the verified route
  (`content/projects/privateers-hold.route.json`) from the proxy voxels.
- `scripts/regenerate.sh` — runs the whole chain: extract → engine import →
  project doc.
- `render-check/` — headless three.js GLTFLoader + playwright render
  verification; screenshots in `render-check/*.png` are durable artifacts.
- Planned when code needs a home: `dagger-content`, `dagger-world`,
  `dagger-export` (see 6529).

## Key facts & conventions

- **Engine pin**: `d52c9b0f3287f21eea81d465871978a117750d0c` (public Rusty
  Engine repo; recorded in `engine-source.json`). No sibling path deps, no
  loading-bay-game imports — rusty-dagger is a self-contained downstream.
- **Extraction truth**: 5 RDB blocks (S0000999 start + 4 border), 365 model
  instances, 18,811 verts / 9,263 tris, 81 unique textures; bounds
  X[-51.2,102.4] Y[0,51.1] Z[-102.4,51.2] m, glTF right-handed space.
- **Geometry conventions** (from DFU): GlobalScale 0.025 raw→m, mesh 1/256
  sub-units, UVs 1/16 texel sub-units, rotations 1/2048-turn negated
  (T·Rz·Rx·Ry), RDB block side 2048 raw (51.2 m), DF Y-down → (x,−y,z);
  left-handed→right-handed: negate Z, reverse fan winding.
- **Collision stopgap**: proxy voxel columns (0.5 m) keep every walkable
  level; spawn support at start-marker layer 38.4 m; settle substeps 0.1 m;
  step-up retries are pre-motion-atomic and cannot climb taller-than-step
  obstacles. Known limits: walls contribute no voxels; raised solids are
  top-surface-only (hollow undersides).
- **Walkthrough route**: start block (0,−1) → border block (1,−1), ~25 m,
  descending ~6.5 m, support asserted per action.
- **Texture table**: default identity {119,120,122,123,124,168}; door archive
  74 → 74+climateBase (Privateer's Hold = Woodlands → Temperate →
  TEXTURE.374); TEXTURE.000/.001 are virtual solid-colour archives.
- **Provenance/licensing**: Bethesda game data is read locally and **never
  committed**; derived assets in `content/` are local-dev only. Format
  semantics ported from Daggerfall Unity (MIT) with per-claim citations in
  docs/daggerfall-formats.md.

## Commands

```sh
scripts/regenerate.sh                     # extract → engine import → project doc
cargo test --workspace --locked           # 17 tests, all against real data
cargo run -p dagger-runtime --bin dagger-walkthrough
node render-check/check.mjs [--cam overview|top|interior] [--out shot.png]
python3 scripts/check-adapter.py          # local adapter; env override diagnostic-only
scripts/serve-studio.sh                   # human-visible Studio host (pinned Engine build)
node scripts/check-studio-host.mjs        # focused HTTP/adapter check (host running)
scripts/check-studio-browser.sh           # real Chromium desktop+narrow proof (host running)
cargo run -p dagger-import -- --help      # direct extraction CLI
```