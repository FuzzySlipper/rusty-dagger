# Rusty Dagger authoring CMS

## Start the CMS

Run these commands from the Rusty Dagger repository root. Start the LAN instance with `den-serve`.

```sh
den-serve up rusty-dagger-authoring -repo "$PWD/authoring/cms"
```

Open the printed LAN URL. If the service is already running, `den-serve status rusty-dagger-authoring -repo "$PWD/authoring/cms"`
shows its URL. Use `den-serve restart rusty-dagger-authoring -repo "$PWD/authoring/cms"` after changing Python code.

## Review and link records

The CMS sorts every asset by what it shows. An asset id is `<prefix>-<key>`: the prefix names its category and the key is
its classic source key, except that people keep their `npc-` numbers. A record's finer kind (tree or rock, light or sign)
is a field, so re-sorting it never changes its id.

| Category | ID | Read from |
| --- | --- | --- |
| People | `npc-381` | the NPC chart (`export-npc`), including the enemy class sprite sets and mounted figures |
| Creatures | `creature-004`, `creature-204` | the classic mobile reference below mobile 128, and the consolidated domestic animals (`creature-202`–`208`) from `TEXTURE.201` |
| Nature | `nature-500-12`, `nature-213-2`, `nature-m60511` | the flora review, `export-scenery` and the ARCH3D remodels |
| Props | `prop-210-3`, `prop-m41000` | `export-scenery` and the ARCH3D remodels |
| Architecture | `arch-m444` | the ARCH3D remodels |
| Textures | `texture-002-1` | `export-surfaces`, the records a block or the terrain draws |
| Animations | `anim-stroll`, `anim-rig-walk` | the art pack's gsplat clips (shipped in `animations.rpak`) and parts rig clips |
| Audio | `sound-437`, `music-dungeon-01` | the import's classic sounds by archive ordinal, and the score's tracks |

An ARCH3D model's category and kind come from its id band in [model-categories.json](model-categories.json). A human
enemy class (mobiles 128–145) is the people record of the sprite set it draws, found by its `mobile-` id. Character stages
keep their pipeline ids (`381`, `383-A`, `128`), linked to people and creatures through those ids.
It discovers villager variants from NPC aliases.
Use **Link pipeline** on a character to add a relationship, then **Set as canonical** or **Set as final** to select
a depiction. These decisions are stored by stable ID in [links.json](links.json). Pipeline approvals stay in
the character stage files. A single approved front or game-ready output is shown by default when available.

On **People** (`#people`), filter by **Game usage** (named Cast, Spawners, Unused) and **Art status** (needs/has concept
or final asset). Use **Select** on two or more tiles (or **Merge with…** on a character page) to open the in-CMS
**Compare & combine** view, choose a keeper, and apply merges directly through `NpcReferenceMerges.json`. Use
**Retire record…** to hide bogus or obsolete `npc-###` entries (stored as `null` in `NpcReferenceMerges.json`, guarded by
active `daggerfall.people`, pipeline, and concept reference checks, and reversible from **Retired records**). Workbench
review links open the native media and ratings. Nature records of the climate sets show the Engine-rendered images of the
flora review.

Use **Mark** on a card or detail page, **Marked only** to browse the selection, and **Clear all marks** to reset that
catalog. Ratings and notes stay with the record when marks are cleared. **Review in CMS** opens locally filed
images and clips, with ratings and notes on each asset and on the stage. Use the saved stage decisions when
applying production changes through the existing character tools.

Review data is keyed by category and ID in `local/authoring/reviews.sqlite3`. Use the CLI on the CMS host, or
set `DAGGER_CMS_URL` or pass `--url` before the subcommand from another machine.

Use the catalog category and stable ID when looking up a note or rating: an asset category above (`people`,
`creatures`, `nature`, `props`, `architecture`, `textures`, `animations`, `audio`), or a character stage's.

| Category | Example ID |
| --- | --- |
| `people` | `npc-381` |
| `nature` | `nature-500-12` |
| `pipelines` | `452` |
| `pipeline-stages` | `452/01-concept` |
| `pipeline-assets` | `452/01-concept/approved/front.png` |

```sh
python3 authoring/cms/review_cli.py marked nature --ids
python3 authoring/cms/review_cli.py get people npc-381
python3 authoring/cms/review_cli.py set nature nature-500-12 --mark --note "Check this render in the next pass"
python3 authoring/cms/review_cli.py list pipeline-assets --prefix 452/01-concept/ --rating bad
```

Set `DAGGER_CHAR_ROOT` to use a different character stage root. Keep the same character IDs and relative paths
so the CMS can find their reviews.

## Concept art

Use the [people concept recipes](../tools/people/concept/README.md) to prepare classic reference sheets, write
costume briefs, and generate front or back candidates.

Open **Concept art** from a people or creature record. You can drag and drop ComfyUI PNG/JPEG/WebP renders directly into the upload dropzone on that page (ComfyUI `prompt`, `seed`, `model`, `sampler`, and LoRA metadata are extracted automatically from PNG chunks, with editable fields and optional immediate `front`/`left`/`right`/`back` approval). Candidates are grouped by batch. Open one to inspect the original
image and its generation details, then choose **Approve as front**, **Approve as left**, **Approve as right**, or **Approve as back**. Each view has one
approval. The character page uses the front as its canonical depiction. All four approvals appear on the concept
art page. Previous candidates remain available.

Submit existing local generation outputs with `python3 authoring/cms/concept_cli.py add npc-381 batch.json` (each candidate may include `"approve": "front" | "left" | "right" | "back"`), or upload a single image directly without writing a manifest:

```sh
python3 authoring/cms/concept_cli.py upload npc-381 front.png --batch turnaround --approve front
python3 authoring/cms/concept_cli.py approve npc-381 front-a --view left
```

The manifest uses a batch name and distinct candidate names within that batch. Paths are relative to the manifest.

```json
{
  "batch": "tunic-study",
  "defaults": { "model": "local-model", "steps": 25 },
  "candidates": [
    { "image": "front-a.png", "prompt": "Exact generation prompt", "seed": 8031, "approve": "front" },
    { "image": "left-a.png", "prompt": "Exact left profile prompt", "seed": 8032, "approve": "left" }
  ]
}
```

Use `null` if the generator supplies no seed. Other fields, including negative prompts, samplers, and graph settings,
are saved with the candidate. Re-running the same submission keeps its existing candidate ID. The `approved`
command defaults to `front` (`--view front|left|right|back`) and returns the selected image's generation metadata. Pass `--image` to save
its original bytes for downstream work.

```sh
python3 authoring/cms/concept_cli.py approved npc-381 --image /tmp/npc-381-front.png
python3 authoring/cms/concept_cli.py approved npc-381 --view left --image /tmp/npc-381-left.png
python3 authoring/cms/concept_cli.py approved npc-381 --view right --image /tmp/npc-381-right.png
python3 authoring/cms/concept_cli.py approved npc-381 --view back --image /tmp/npc-381-back.png
```

Pass `--url` before the command when accessing the CMS from another machine.
Images and approvals live under `local/authoring/concepts/` on the CMS host.

## Game data (`daggerfall.people`)

The CMS reads and validates `content/worldrpg/packs/daggerfall.people/` directly through [people_pack.py](people_pack.py):

- **Cast individuals** (`#cast`, `#cast/<id>`): inspect all unique cast records from `cast/individuals.json`, cross-linked to their `npc-###` art record and concept-art/final status, with focused metadata and sprite editing.
- **Typed spawners** (`#spawners`, `#spawners/<id>`): inspect and edit `defaultPool`, per-region `regionalPools` (stored under canonical numeric region index keys such as `"17"` for Daggerfall or `"20"` for Sentinel, displaying region names from `daggerfall.base`), and spawner policies in `spawners.json`.
- **Block slots** (`#blocks`): server-filtered, paginated cross-reference over `blocks/*.json` (RMB interiors, RMB exteriors, RDB dungeons) by spawner, cast, building type, regional faction derivation, interaction, and effective harm, with direct **3D** links into the block viewer.
- **World blocks (3D)** (`#world-blocks`, `#world-block/<key>`): browse and inspect all staged RMB exteriors, RMB interiors, and RDB dungeons (`local/build/import/staging/worldrpg/imports/world-blocks`, falling back to `local/build/import/classic-art/worldrpg/imports/shared` for split classic meshes/textures) in a lazy-loaded Three.js viewport with direct `.rstatmsh` loading, **Classic vs. Refreshed art** toggle (`daggerfall.art-replacements` surfaces, scenery, and ARCH3D remodels), **Climate / Season** swaps and **Dungeon texture table** presets, **Actions** graph & **Collision** wireframe overlays, cutaway slice, and two-way cross-links to buildings, models, and `daggerfall.people` slots. Toggle **Edit 3D** to attach Three.js `TransformControls` (translate/rotate with grid snapping and undo/redo) to edit `daggerfall.people` slot positions and bindings (`blocks/*.json`) or author, preview, save, and bake `WorldBlockVariantRecipe` overlays (`worldVariants.blocks` in content packs such as `daggerfall.quests`, backed by `world-variant-bake`).
- **Towns & locations (3D)** (`#locations`, `#location/<region>/<index>`): browse all 15,251 staged locations and 4,232 multi-block dungeons from `daggerfall.imported.json`, compose full multi-block town grids (up to 8×8) and RDB dungeon grids in 3D with an interactive 2D block map, and jump straight from any block or building in a town into its solo block view or 3D interior.
- **World map (Atlas)** (`#world-map`, `#world-map/<region>`): interactive 2D zoomable slippy-tile atlas over the baked 3-level eroded relief & rivers pyramid (`local/build/import/staging/worldrpg/media/atlas`) with Painted Atlas ↔ Classic Map toggle, **Political / Diplomacy / Climate / Power / Density** overlays built from `POLITIC.PAK`, `CLIMATE.PAK`, and `daggerfall.base` faction hierarchies, filterable location pins, and 1-click drill-down into 3D towns and dungeons.
- **Reverse links on People (`npc-###`) pages**: each character detail page lists every cast individual, spawner pool, and block-slot count referencing its `(archive, record)` aliases.

Use `people_cli.py` from scripts or agents against `DAGGER_CMS_URL` (pass `--people-pack` to `server.py` when testing against a scratch copy):

```sh
python3 authoring/cms/people_cli.py overview
python3 authoring/cms/people_cli.py validate
python3 authoring/cms/people_cli.py cast king-gothryd
python3 authoring/cms/people_cli.py blocks --spawner banker --building-type 3
```

## Add a page

The CMS uses a Preact + TypeScript frontend under `authoring/cms/web/src/` bundled to `web/dist/app.js` with `esbuild`,
served directly by [server.py](server.py) with hash routes. Follow [ConceptsPage.tsx](web/src/pages/ConceptsPage.tsx)
for a detail view or [AssetListingPage.tsx](web/src/pages/AssetListingPage.tsx) for a catalog.

1. Give the page a stable hash route in [router.ts](web/src/router.ts) and add its navigation entry in
   [App.tsx](web/src/App.tsx). Link detail pages from their parent records and have `activeSection()` highlight the
   parent section in the navigation.
2. Put each view in its own component under `web/src/pages/` and reuse the shared primitives under `web/src/components/`
   (`ArtDataCard`/`ArtDataChip` for bidirectional art ↔ data links, `DataTable` for filterable/paginated tables,
   `DirtyBar`/`FieldRow`/`ValidationSummary` for structured forms, `Modal` for inspectors/pickers, and `ReviewPanel`/
   `TileReview` for shared marks, ratings, and notes). Read cached catalog data from `useApp()` ([state.tsx](web/src/state.tsx))
   and fetch detail or table endpoints with `apiRequest()` ([api.ts](web/src/api.ts)) using an `AbortController` so a
   slower response cannot overwrite a newer view.
3. Register new data routes in the owning domain module's `register_routes(router, workspace)` helper and wire it into
   `create_router()` in [server.py](server.py) via [router.py](router.py). Use [cache.py](cache.py) for mtime-cached
   read-heavy files and [packs.py](packs.py) (`PackStore` / `ValidationError`) for locked, validated atomic JSON writes
   to git-tracked content packs. [catalog.py](catalog.py) assembles the asset lists, [pipelines.py](pipelines.py) reads
   character stages, and [review_media.py](review_media.py) locates stage previews. Keep repo-rooted asset/pack paths
   and their browser mounts in [library.py](library.py).
4. Add only page-specific styles to [style.css](web/style.css). Open the route directly and through its parent link,
   then exercise its normal actions. Test writes on a loopback instance with scratch paths for `--links`, `--reviews`,
   and `--concept-root`. For merge tests, also pass a copy of the NPC chart and merge ledger with `--npc-root` and
   `--npc-merges`.

This starts an isolated instance on a free loopback port. Open the URL it prints.

```sh
scratch=$(mktemp -d)
python3 authoring/cms/server.py --port 0 --links "$scratch/links.json" \
  --reviews "$scratch/reviews.sqlite3" --concept-root "$scratch/concepts"
```

Run the CMS checks when changing code. Rebuild `web/dist/app.js` whenever `web/src/` changes.

```sh
python3 -m unittest discover -s authoring/cms
npm --prefix authoring/cms/web run check
npm --prefix authoring/cms/web run build
```
