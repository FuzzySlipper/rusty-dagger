# Authoring

Art for the visual refresh: raw Daggerfall exports go in, reviewed finals come out. Generic image tooling stays in
the asset-pipeline checkout (`ASSET_PIPELINE`, default `~/dev/asset-pipeline`); everything Dagger-specific is here.

This file holds only what goes wrong at the keyboard. How the pipeline works and why is on the wiki:
`wiki/rusty-dagger/art-conversion`, `wiki/rusty-dagger/parts-rig-people` and `wiki/rusty-dagger/first-person-held-art`.
Each tool's usage is in its own header.

```
authoring/
  tools/    shared/ (what every 2D area runs: batches, cut-out, finals, matte, ledger, promote), paper-doll/ (bodies,
            heads, worn layers, doll preview), flats/, surfaces/, terrain/, groundcover/, trees/, rocks/ (rocks, logs and
            dead trees, models and flats), hanging/ (the roots, chains, cage and lanterns that swing), ui/ (screen
            export, UI art kit), weapons/, mounts/, fp-arms/ (first-person.rpak), music/ (music.rpak)
  specs/    tracked contract specs: items, worn, paper-doll, spells, effects, flats, people, ui, weapons
  ledger/   <area>-notes.json: status and notes per image key, the only hand-edited tracking input

local/art/  (ignored, beside the other working files in local/; see the README)
  reference/  importer exports of the classic art
  generated/  finals (one directory per area), studies/, source-edits/
  review/     generated review pages and ledgers
```

`authoring/` holds tracked files only. A path says what a file is: `local/art/reference` → `local/art/generated` →
(accepted) the art pack, `content/worldrpg/packs/daggerfall.art-replacements/`. Never commit anything from
`local/art`.

## Reference exports

```sh
dotnet run --project src/Daggerfall.Source.Tool -- export-items
```

- Also `export-spells`, `export-paper-doll`, `export-effects`, `export-flats`, `export-people`, `export-surfaces` and
  `export-terrain`. Each writes `local/art/reference/<name>/`: an `index.html` catalogue, a CSV index, JSON manifests and
  native-resolution PNGs with their source record and frame. Missing source art is recorded, not skipped.
- `--output DIR` writes somewhere else. Use a fresh directory for a new export: existing ones are kept experiments.
- `--arena2 DIR` relocates the source (default `local/inputs/arena2`); `export-items --inputs DIR` relocates the
  importer's item tables (default `src/Daggerfall.Import.Tool/inputs`).
- `export-npc` builds the unified NPC chart in `local/art/reference/npc/`. Open `index.html` directly.
  [NpcReferenceIds.txt](../src/Daggerfall.Source.Tool/ReferenceExports/NpcReferenceIds.txt) fixes the three-digit assignments
  and groups alternate animations. Keep assigned numbers when editing the list, and leave retired numbers reserved.
  Named NPCs use 001–100, billboards 101–380, and portraits without a known sprite 400–450. Wandering NPCs keep their
  original three-digit numbers. Search accepts old references such as `mobile-452`, `npc-flat-23355`, or `182-59`.
  `--characters DIR` relocates the optional links to the asset-pipeline character outputs.
- `export-directional-sprites` writes `local/art/reference/daggerfall-remaining-directional-unmirrored-r01/`, compared
  against `--baseline` (default `local/art/reference/daggerfall-mobile-idle-unmirrored-r01`).
- `tools/ui/export.py` writes the screens and portraits from an import's staging, so run the import first.

## Items and the other 2D areas

- Write the contracts first, then `make_spec.py`, `run_batch.sh`, `review_ledger.py`, in that order.
- Keep a batch on one host: hosts are not bit-identical. 24–50 frames a batch. A new art type gets a reviewed pilot
  batch before its area is queued.
- Use 10 Qwen / 5 retro steps at a 1024 canvas. Never a 768 canvas: it changes the piece.
- Ask Patch before queueing long batches on his 5090.
- Never move finals by hand: `finalize.py` writes them, with the rect sidecar a paper-doll or worn final needs.
- Generate each unique sprite once. Draw the pixels when they contradict the template's name, and flag it.
- An ambiguous subject ends its `uncertainty` with exactly `This is a guess for owner review.`: the ledger keys on
  that sentence.
- Frames 1+ of an animated sprite go through a `frames-*` batch.
- Gore becomes clean gothic bones in the same outline; plain blood smears stay. Check a `flat_specs.py` caption
  against the classic before accepting its final.
- An edited source goes under `local/art/generated/source-edits/NAME/` in the same layout; run the batch with `SOURCE=` at it.
- Paper doll: `landmarks.py check` on bodies and heads before dressing or promoting. `heads.py`, then `skin.py`,
  then `doll_preview.py`.
- A worn record the classic draws with a mask (a hood, cloak or helmet over hair: the import's paper-doll catalogue
  names its `maskMediaId`) is promoted with its own: give its key a `cut` row in `specs/worn/hair-masks.json` (null if
  the refreshed item hides nothing), run `tools/paper-doll/hairmask.py KEY`, and judge `hairmask.py --sheet OUT.png KEY`
  against today's doll before promoting. The mask is drawn over the item's worn final, so draw it again after redrawing
  the item: promotion refuses a mask drawn for other art, refuses a masking record without one, and keeps the one it has
  on a re-promote; the game refuses a masking record whose entry states none.
- `matte.py` only once an area's finals are accepted. Never despeckle effects that carry embers.
- `promote.py` takes only finals the ledger marks `accepted`. From a task worktree, promote your own finals with
  `--key` (an art key, a record, an item, an effect or a sprite mesh's `ARCHIVE-RECORD`): it replaces those entries
  and keeps the rest. Only the shared checkout holds every final, so a whole-area run anywhere else refuses rather than
  drop what it lacks. The import must have run; effects, flats and people also read `local/art/reference`, which a
  worktree links from the main checkout.
- Effects, item icons, the clothed bodies, flats, people, sprite meshes and trees carry their own `placement` (an effect's size in metres,
  pivot, frames, rate and loop; an icon's ground height in metres; a body's waist row; a flat's or person's size, pivot and
  foot, and a flat's rate; a sprite mesh's world size in metres; a spell tile's aspect; a tree's height and foot in metres, measured by `trees.py` at its flat's size, so promote the flat first; a rebuilt tree keeps its placement, and `trees.py --recompute A-R` measures it again). A new entry takes it from the classic once;
  after that, promotion keeps what the payload holds, so promote with `main` first. A redone effect with other frames
  is refused until its `sequence` fits: set it with `--set ID NAME VALUE` (a JSON value, marked set by hand), or
  take every value from the classic again with `--recompute ID`. Promotion says when new art kept an old placement.
- A window surface (a record the import cuts classic glass for: `texture-A-R-glass.png` in its materials) is promoted with
  its own glass: name the refreshed panes' colours, and a box if the wall shares one, in `tools/surfaces/glass.json`
  (`glass.py --suggest KEY` proposes them), run `tools/surfaces/glass.py KEY`, and judge `glass.py --sheet OUT.png KEY`
  by day, night and winter before promoting. A winter record names its own. The glass is cut from the surface's final
  (a new window's too), so cut it again after redrawing the surface: promotion refuses glass cut from other art or a
  window without glass, and keeps the glass it has with an unchanged surface; the game refuses a refreshed window whose
  entry states none.
- Promotion writes into the art pack's folder and rewrites
  `src/Daggerfall.Import.Tool/inputs/replaced-classic.json`, the classic identities the pack replaces, which a
  player's import leaves out: commit both with
  `git add -A content/worldrpg/packs/daggerfall.art-replacements src/Daggerfall.Import.Tool/inputs/replaced-classic.json`.
  Nothing there is ignored, so no file needs `git add -f`. The list changes only when an entry is added or withdrawn;
  then the importer revision moves, and the change lands as an importer change (README, Game data): `route.py data`
  first, through the seat.

## Weapon models

- The icon wins over the classic first-person frame.
- Look at `joined.png` and at every turnaround attempt. A changed prompt is a new rev; build each rev into a new
  folder.
- Adopting a model: copy it to `content/worldrpg/packs/daggerfall.art-replacements/first-person/weapons/NAME.glb`, `rusty asset check`, rerun
  `arcs.py`, `authoring/tools/fp-arms/build.sh`, then `authoring/tools/fp-arms/sheet.sh` in a 1920×1080 window run.
- Film a chain with `held.motion.play` and `engine.time.advance`; `sheet.sh` jumps between moments and gives a chain
  nothing to follow.
- Models are in the held-look frame: grip at the origin, length along -Z, the striking edge on +Y.

## Mounts

- `horse.py`, then `rusty asset check`, then `player.ride horse` in the game.
- The reins meet the hands: `REINS_AHEAD` (`horse.py`), `FIST` and `REINS_END` (`cart.py`) change together with
  `RIDE_HAND` in `authoring/tools/fp-arms/build.py`.

## Animated people

- Fix a part that goes wrong by changing what is generated or how it is rigged, never by repainting the part.
- Don't run the Krea retro pass again on a rig source: the face and held objects drift.
- Record each figure's rig source in `specs/people/rig/<figure>.json`: input, models, sampler, seeds, the full prompt.
- Don't pass a garment's `--guide` outline as its compositing mask.
- Check rig-c's proposed draw order: it can put the face over the eyes and leave the nose out.
- Body clips leave the eye, iris and brow bones and `head` unkeyed; the attention layer owns them.
- Judge a render of the built rig, not `out/recomposite_rig.png` or `out/idle.gif`. Stop on any FAIL in `rig.log`:
  `rp rig` can refuse and still exit 0.
- Fix a fault once in the shared route, never in one figure's folder.
