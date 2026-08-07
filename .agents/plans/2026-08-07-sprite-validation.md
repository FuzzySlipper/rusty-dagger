# Plan: Task 6665 Sprite Art Validation + Extraction Quality

## Goal
Validate extracted Daggerfall sprite art (enemy atlases + billboard strips) for rendering quality before it reaches the flycam/runtime, improving extraction deterministically where possible and providing a deterministic flagger + human/LLM-friendly visual dump so uncertain cases can be reviewed without tediously opening every PNG.

## Success Criteria
- Deterministic checker runs as part of `scripts/regenerate.sh` (and `cargo test` where applicable) that surfaces dimension/scale/aspect/atlas-waste issues per mobile/billboard, with severity and actionable reason. No false passes on known Rat-like variance.
- Visual dump artifact that lets a human or LLM quickly audit flagged sprites: per-mobile grids (8 orientations × M frames) with dims/scale/worldSize overlays, plus billboard strip previews, highlighting flagged cells.
- Any extraction fix that removes a deterministic artifact is landed (e.g., missing scale handling, pivot, flip, trim). Remaining flag is documented as accepted variance pending upstream 6638 per-frame quad resize. Task 6665 no longer TBD.

## Context And Current Facts
- Extraction: `crates/dagger-import/src/main.rs:publish_enemy_atlases` packs 8×M frames per mobile id (MOVE_ANIMS 0-4 mirrored) into uniform cells sized to max w/h, bottom-center aligned (`dx=(cell_w-w)/2`, `dy=cell_h-h`). Flip baked per-pixel. World size via `arena2::mobile::record_world_size(width,height,scale_x,scale_y)` where `scale/256` (DFU BlocksFile.ScaleDivisor). Billboards: single PNG or horizontal strip atlas for multi-frame, UV rects + fps in manifest. Both decode via `arena2::texture::frame_pixels` (handles TEXTURE.nnn RLE) and `PAL.PAL` palette (index 0 transparent). Manifests: `content/textures/enemy-manifest.json` (per-frame worldSize, uvMin/Max) and `billboard-manifest.json` (worldSize). DFU source donor is `/home/research/daggerfall-unity` (EnemyBasics.cs, DaggerfallMobileUnit.cs, MeshReader.cs); rendering authority is Rust, JS thin bootstrap only.
- Current atlases (real data dump 2026-08-07): Rat 4480×67 64 frames (5 unique worldSizes; front 0.812×0.838 vs side 1.75×0.675), Imp 5504×129 32 frames (5 sizes), etc. All 8 mobiles show 4-5 distinct worldSizes across orientations. Billboard worldSizes 0.3–3.0m. Atlas waste is measurable (Rat cell 70×67 vs actual frames as small as 41×27).
- Runtime: `dagger-runtime::animation::AnimationService` does consolidated `evaluate(dt,camera)` producing `Vec<FrameUpdate>`; enemy orientation via `evaluate_directional`. Patrol drives positions. Fixed-quad rendering is known limitation: renderer picks front-record size, `design.md` calls it accepted pending upstream 6638 per-frame resize.
- Verification gates: `cargo test --workspace --locked`, `scripts/regenerate.sh` (→ dagger-walkthrough, dagger-navgrid, check-adapter), `engine-render-check/check.mjs` headless, `dump-frame.mjs` (already extracts sprite metadata for flycam). Consultant spawned (read-only) but not blocking.

## Constraints And Non-Goals
- Rust owns Daggerfall semantics; JS/TS never becomes second authority. Visual dump must be generated from Rust manifests, not JS math.
- Offline CLI only (`dagger-import`); no runtime seam, no side renderer. If engine lacks per-frame resize, file upstream task, don't workaround in flycam.
- Content is generated (`content/` via regenerate.sh); validation must be reproducible, not hand-edited.
- Non-goal: authoring original art or changing classic data; non-goal: full upstream 6638 resize (separate task).
- Non-goal: exhaustive visual-pixel LLM judge in this pass — first pass is deterministic metrics + focused visual artifact that secondarily enables LLM review.

## Key Decisions
1. **Where validation lives:** New Rust binary `dagger-validate-sprites` (in `crates/dagger-import` or new `crates/dagger-validate` if grows) + optional Python helper for HTML generation. Rationale: stays in Rust authority, testable, fits `cargo test`; JS would violate Rust-authority rule. Alternative (pure shell/python) rejected — would duplicate DFU math.
2. **Deterministic metrics (flagging uncertain):**
   - Per-mobile orientation variance: max width delta, height delta, worldSize delta, aspect ratio delta, scale delta across 8×M frames. Thresholds flag warn/error (e.g., >25% size delta = warn, >50% = error).
   - Frame-count consistency: all MOVE records for a mobile must have same frameCount; mismatch = error.
   - Atlas efficiency: cell area vs bounding-box of opaque pixels (or vs w×h) → waste ratio; high waste flags.
   - Scale presence: any non-zero scale flagged for human review (Rat -128, others -70/-67 etc).
   - Manifest ↔ PNG consistency: PNG dims vs manifest width/height, hash coverage.
   Rejected: single global threshold — needs per-mobile severity so small mobiles (Rat) don't drown others.
3. **Visual dump shape:** Generated HTML in `content/validation/sprites/` (or `engine-render-check/generated/sprite-validation/`) with per-mobile page: 8×M grid (CSS grid, images sliced via canvas or pre-cropped PNGs), caption overlay (record, flip, dims, scale, worldSize, fps), flagged cells red border + tooltip reason. Billboard page similar (strip preview). Index page lists flagged only. Enables LLM: single HTML/PNG per mobile is image-readable.
4. **Extraction improvements to audit before validation:** Verify against DFU for (a) scale divisor correctness (done, 256), (b) bottom-center pivot vs DFU's `pivot [0.5,0]`, (c) flip baking vs DFU UV flip, (d) transparent-index handling (palette 0), (e) whether DFU trims transparent border before sizing (check `TextureFile.Read*` + `DaggerfallMobileUnit` mesh bounds). Fix any deterministic drift found; if variance is classic-authored, keep as flagged accepted.
5. **Integration point:** `scripts/regenerate.sh` runs `dagger-import` then `dagger-validate-sprites --manifest content/textures/enemy-manifest.json --billboard-manifest content/textures/billboard-manifest.json --out content/validation/sprites.json --html content/validation/sprites/`. Fails closed on error-level flags, warns on warn-level (non-zero exit for CI optional). `cargo test` covers metric logic.

## Recommended Approach
Phase 1 — audit extraction deterministically against DFU, fix any low-hanging math drift (scale, pivot, flip, trim). Phase 2 — implement validator binary with metrics + JSON output + deterministic unit tests on real manifests. Phase 3 — generate visual dump HTML (Rust or small Python script templated from manifests + PNGs) that surfaces flagged cells. Phase 4 — wire into regenerate.sh and add `cargo test` proof. All changes stay in Rust crates; JS changes only if needed to display validation overlay in flycam (thin bootstrap).

## Work Plan
1. **DFU audit & extraction patch (1–2 files)**
   - Inspect `DaggerfallMobileUnit.cs: ~L700-800` mesh creation, `MobilePersonBillboard.cs`, `MeshReader.cs GetScaledBillboardSize`, `TextureFile.cs` trimming. Compare to `dagger-import/main.rs` cell packing, `record_world_size`, `flip_rgba_rows`, pivot.
   - Patch any drift (e.g., trim, scale, offset). Add `arena2` test vectors for flagged mobiles if needed.
   - Surfaces: `crates/arena2/src/texture.rs`, `crates/arena2/src/mobile.rs`, `crates/dagger-import/src/main.rs`.

2. **Validator binary (new bin)**
   - Create `crates/dagger-import/src/bin/dagger-validate-sprites.rs` (or `crates/dagger-validate` if >300 lines). Reads `enemy-manifest.json` + `billboard-manifest.json` + optional raw TEXTURE introspection for ground truth.
   - Implements metrics above, severity thresholds, JSON report `{ version, mobiles:[{id,name,metrics,flags}], billboards:[...], summary:{warn,error} }}` + human-readable stdout (tables).
   - Unit tests: synthetic manifests + real `content/textures/*-manifest.json` golden.

3. **Visual dump generator**
   - Script `scripts/generate-sprite-validation.py` or Rust HTML emitter that reads manifests + PNGs, slices atlas via Pillow or renders via embedded base64, writes `content/validation/sprites/index.html` + per-mobile pages. Highlights flags, includes animated JS preview (cycles M frames per orientation).
   - Reuses existing `png` crate encoding if Rust, else Python plumbing (acceptable per Code style: Python only for script plumbing).

4. **Pipeline integration & docs**
   - Extend `scripts/regenerate.sh` to invoke validator + HTML after `publish_*`. Add threshold config (e.g., `content/validation/thresholds.json` if tunable).
   - Update `docs/daggerfall-formats.md` if any format nuance discovered, `docs/design.md` sprite section, and task 6665 acceptance.
   - Add headless check `python3 scripts/check-sprite-validation.py` or `cargo run -p dagger-import --bin dagger-validate-sprites -- --check`.

## Validation Plan
- Deterministic: `cargo test --workspace --locked` (covers mobile math, validator metrics, manifest parsing); `python3 <<` quick dump of real archives showing metrics match manual DFU calc; `cargo run -p dagger-import --bin dagger-validate-sprites -- --manifest content/textures/enemy-manifest.json --check` must flag Rat and others with expected warnings (no false zero).
- Live/render: Run `scripts/regenerate.sh` and verify `content/validation/sprites/*.html` opens and flagged cells align with observed flycam popping (use `engine-render-check/check.mjs` for baseline). Optionally `node engine-render-check/dump-frame.mjs` still passes; flycam visual pop check is manual but validator directs attention to 2–3 worst mobiles.
- Gate: validator fails closed on error-level; CI `cargo fmt --check` + `cargo test`.

## Risks / Rollback
- Threshold tuning could flag everything (noise) or nothing (miss). Mitigate: thresholds derived from measured variance (Rat 80% delta is known), severity bands, and per-mobile reporting; HTML shows raw numbers so human can recalibrate.
- Extraction patch may shift worldSize and break existing project spawn offsets. Rollback: revert commit, manifests regenerate. No engine seam break (manifest is additive).
- Visual dump size: atlases up to 4.5M PNGs; HTML with base64 slices could balloon. Mitigate: reference PNG + CSS `object-fit` slicing vs duplicating slices; cap HTML at flagged mobiles if needed.
- Upstream 6638 dependency: validator will note fixed-quad limitation as accepted, not blocked. No renderer change in this task.

## Open Questions
- Do we trim transparent borders before sizing in DFU's runtime mesh, and should validator compare opaque bounds vs record dims? Needs DFU code read (DaggerfallMobileUnit mesh bounds).
- Should validator also check billboard animation frameCounts (torch 4-5 frames) for tiling artifacts vs dungeon UV work (task 6602)? Out of scope but noted.
- Threshold values for warn/error — propose 25%/50% but confirm with sample run on real manifests.
