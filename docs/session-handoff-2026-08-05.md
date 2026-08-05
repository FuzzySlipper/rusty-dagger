# Session handoff — 2026-08-05 (nav grid task 6639)

Written from session context at the user's request (approaching a subscription
limit; a different agent may pick this up). Not re-verified against artifacts
— trust the repo and Den over anything here that disagrees.

## Operating rules in force (user-established, don't reopen)

- Work happens in `/home/dev/rusty-dagger`. Other repos are read-only;
  upstream engine changes go via Den tasks in project `rusty-engine` (their
  agent handles them). DFU semantics reference: `/home/research/daggerfall-unity`.
- **YOLO git mode: commit and push freely, everything, straight to `main`.**
  Commit messages start "Task NNNN: ..." for task work.
- **rusty-engine renderer is the ONLY render path.** When the engine lacks
  something: file an upstream Den task and stop. No side renderers, no local
  workarounds.
- **No pinning/provenance ceremony.** Engine deps track `main`; fix forward on
  upstream drift.
- Den (`mcp__den__*` MCP tools) is source of truth for task state. Reviews:
  implement → `request_review` (exact full SHAs + tests run) → the user
  manually relays to the review agent and back.
- User's general operating guidance lives at
  `/home/research/system-prompts/software-engineer.md` (their usual harness is
  broken; they asked for it to be treated as a system prompt).

## Gates (from AGENTS.md)

`cargo test --workspace --locked` · `scripts/regenerate.sh` (extract → engine
import → project doc → dagger-walkthrough → **dagger-navgrid --write** →
check-adapter.py) · `cargo run -p dagger-runtime --bin dagger-navgrid -- --check`
· `node engine-render-check/check.mjs` · `python3 scripts/check-adapter.py` ·
studio gates (`check-studio-host.mjs` + `check-studio-browser.sh`, need
`serve-studio.sh` running) only for studio-visible changes.

## Task 6639 (nav grid + flycam gizmo) — COMPLETE, in review

Committed and pushed as **`b6dfa74dab3dc8e857a3c47eba01965624d23d38`** (base
`146f9c5d73cd183c3c7eed1191ade01b46395d89`). **Review round 4049** was
requested via Den on 2026-08-05; the user relays it to the review agent.
All gates green, including the new `engine-render-check/check-flycam-navgrid.mjs`
headless screenshot proof.

### What was built

- `dagger-runtime::navgrid` — walkable grid derivation from the admitted
  dungeon trimesh (projection construction only, NOT a pathfinder): ray-down
  sweep per 0.5m column, re-cast below each hit for multi-level columns,
  walkable = normal.y ≥ 0.7 + 2m headroom + enclosed overhead (down-facing
  surface within 64m). 0.25m level quantization. Result: 50,857 cells, 5 RDB
  blocks, derives in <1s.
- `dagger-navgrid` bin — headless proof + writes committed
  `content/projects/privateers-hold.navgrid.json` (regenerate.sh keeps it
  fresh). Asserts: start room floor y=32 + spawn ledge y=38.4 in the SAME
  column (56,-25) (7 stacked levels there), rock columns (18,-170)/(38,-185)
  unwalkable, ≥4 RDB blocks, and ground-support answers for all 43 spawns.
- **Finding: all 43 enemy spawns float (0.5–1.8m); every one lands on a
  walkable cell within 12m.** Landing data is in the artifact's `spawns` array
  — this feeds patrol grounding.
- Flycam `N` toggle — bounded 2048-handle pool, cells within 10m and ±6m of
  camera level, debounced rebuild on move. `check-flycam-navgrid.mjs` proves
  it headless (cyan coverage off=0/on=620k, zero console errors).
- Fixed latent flycam bug: vite middleware-mode ignores `hmr: false` and
  always binds ws port 24678 — second instances collided (was the source of
  console error spam when a check ran alongside the user's den-serve
  instance). Each serve-flycam instance now allocates its own HMR port.

### Upstream tasks filed (rusty-engine project)

- **6642** — `NavProjection` from host-derived walkable cells (minimal seam:
  a constructor; or fuller engine-owned trimesh→projection deriver). Evidence:
  svc-pathfinding lib.rs:337 (voxel-only build), lib.rs:36-70 (private
  fields). Per user request, cited existing pure-Rust crates to avoid
  reinvention: `rerecast` (Rust Recast port), `landmass` (+`landmass_rerecast`
  bridge), `pathfinding` (generic A*/BFS). svc-pathfinding has NO pathfinding
  library under the hood — hand-rolled BFS over `VoxelWorld`.
- **6643** — step-aware vertical neighbor policy (evidence: `nav_neighbors`
  lib.rs:652-659 is same-Y only; stairs break planar BFS).
- Neither blocks 6639 (its acceptance needs no path queries). They DO shape
  6641 (patrol).

## The NPC arc (next work, in priority order per user)

1. **6639 — DONE, in review.**
2. **6640 — sprite animation service** (planned). Animated env sprites
   (torches!) + directional enemy animations. User wants a service-based
   design (one consolidated applyFrame per tick, not per-entity polling;
   think ahead to offscreen-sprite update gating). Idle animations first;
   atlases need anim frames added in dagger-import.
3. **6641 — NPC patrol** (planned, depends on both). Ground NPCs at load via
   6639 support answers (landing data already in navgrid.json), deterministic
   seeded random-walk near spawn, move/idle states, direction tracking so
   directional sprites follow travel direction. Check DFU for behavior
   reference (`/home/research/daggerfall-unity`). Path queries should consume
   whatever seam lands upstream in 6642/6643 — check those tasks' status
   first; if unlanded, patrol can start with direct/greedy movement using
   ground support (user pre-approved filing more upstream tasks as limits
   are hit).

## Known limitations / traps (don't re-learn)

- Grid has **no wall-adjacency modeling** — cells say "can stand here", not
  "can move between". Connectivity is the upstream seam's job (6642/6643).
  A 0.5m column sweep can't see walls between columns.
- Backface-culled trimesh raycast (`solid=false` in svc-collision): down-rays
  only hit up-facing surfaces; up-rays only hit down-facing ones (ceilings).
  An upper room's floor slab is INVISIBLE to up-rays from below (backface) —
  the enclosure check relies on ceilings, which works for this mesh.
- The start room is a ~30m open vertical shaft with rooms stacked in the same
  XZ columns — any "ceiling within Nm" interior check must tolerate that
  (hence 64m = full mesh height).
- Imp resize-on-orbit wobble = per-record scale factors; accepted limitation
  pending upstream 6638 (per-frame sprite resize). Don't chase it.
- Sprite PNGs are stored bottom-up; `flip_rgba_rows` in dagger-import handles
  it. Enemy atlases are 8 uniform full-height cells, bottom-center aligned.
- Vite dev server (middlewareMode) always injects its HMR client and binds a
  ws port; `hmr: false` does NOT stop it (vite 6.4.3). serve-flycam allocates
  a free HMR port per instance.
- `decodePng` in scripts/studio-frame-metrics.mjs returns `{width,height,rgb}`
  (3 channels).
- The review agent's findings come back through the USER (no automated
  relay). When a review verdict arrives, respond via
  `respond_to_review_finding` / `set_review_finding_status` as appropriate,
  fix-forward on main, and re-request review with new SHAs.

## State

- Worktree clean at `b6dfa74` (all committed + pushed, including the new
  navgrid.json artifact and flycam-navgrid proof screenshots).
- Den task 6639 status: `review` (round 4049 open).
- Upstream rusty-engine tasks open that matter to us: 6638 (per-frame sprite
  resize), 6642 + 6643 (pathfinding seams).
