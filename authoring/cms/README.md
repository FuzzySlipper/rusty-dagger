# Rusty Dagger authoring CMS

## Start the CMS

Run these commands from the Rusty Dagger repository root. Start the LAN instance with `den-serve`.

```sh
den-serve up rusty-dagger-authoring -repo "$PWD/authoring/cms"
```

Open the printed LAN URL. If the service is already running, `den-serve status rusty-dagger-authoring -repo "$PWD/authoring/cms"`
shows its URL. Use `den-serve restart rusty-dagger-authoring -repo "$PWD/authoring/cms"` after changing Python code.

## Review and link records

The CMS reads the NPC and classic mobile reference exports, the flora review catalog,
and the character stages in `local/art/generated/people/dagger-characters/`. It discovers villager variants from NPC aliases.
Use **Link pipeline** on a character to add a relationship, then **Set as canonical** or **Set as final** to select
a depiction. These decisions are stored by stable ID in [links.json](links.json). Pipeline approvals stay in
the character stage files. A single approved front or game-ready output is shown by default when available.

**NPC compare & merge** opens the existing comparison viewer. Choose the keeper and click **Apply merge** to update
the same ledger and rebuild the reference pages from the host. Workbench review links open the native
media and ratings. The Flora pages use the Engine-rendered review images from the existing flora exporter.

Use **Mark** on a card or detail page, **Marked only** to browse the selection, and **Clear all marks** to reset that
catalog. Ratings and notes stay with the record when marks are cleared. **Review in CMS** opens locally filed
images and clips, with ratings and notes on each asset and on the stage. Use the saved stage decisions when
applying production changes through the existing character tools.

Review data is keyed by category and ID in `local/authoring/reviews.sqlite3`. Use the CLI on the CMS host, or
set `DAGGER_CMS_URL` or pass `--url` before the subcommand from another machine.

Use the catalog category and stable ID when looking up a note or rating.

| Category | Example ID |
| --- | --- |
| `npcs` | `npc-381` |
| `mobiles` | `mobile-128` |
| `flora` | `500-12` |
| `pipelines` | `452` |
| `pipeline-stages` | `452/01-concept` |
| `pipeline-assets` | `452/01-concept/approved/front.png` |

```sh
python3 authoring/cms/review_cli.py marked flora --ids
python3 authoring/cms/review_cli.py get npcs npc-381
python3 authoring/cms/review_cli.py set flora 500-12 --mark --note "Check this render in the next pass"
python3 authoring/cms/review_cli.py list pipeline-assets --prefix 452/01-concept/ --rating bad
```

Set `DAGGER_CHAR_ROOT` to use a different character stage root. Keep the same character IDs and relative paths
so the CMS can find their reviews.

## Concept art

Use the [people concept recipes](../tools/people/concept/README.md) to prepare classic reference sheets, write
costume briefs, and generate front or back candidates.

Open **Concept art** from an NPC or mobile record. Candidates are grouped by batch. Open one to inspect the original
image and its generation details, then choose **Approve as front** or **Approve as back**. Each view has one
approval. The character page uses the front as its canonical depiction. Both approvals appear on the concept
art page. Previous candidates remain available.

Submit existing local generation outputs with `python3 authoring/cms/concept_cli.py add npc-381 batch.json`.
The manifest uses a batch name and distinct candidate names within that batch. Paths are relative to the manifest.

```json
{
  "batch": "tunic-study",
  "defaults": { "model": "local-model", "steps": 25 },
  "candidates": [
    { "image": "front-a.png", "prompt": "Exact generation prompt", "seed": 8031 },
    { "image": "front-b.png", "prompt": "Exact revised prompt", "seed": 8032 }
  ]
}
```

Use `null` if the generator supplies no seed. Other fields, including negative prompts, samplers, and graph settings,
are saved with the candidate. Re-running the same submission keeps its existing candidate ID. The `approved`
command defaults to the front and returns the selected image's generation metadata. Pass `--image` to save
its original bytes for downstream work.

```sh
python3 authoring/cms/concept_cli.py approved npc-381 --image /tmp/npc-381-front.png
python3 authoring/cms/concept_cli.py approved npc-381 --view back --image /tmp/npc-381-back.png
```

Pass `--url` before the command when accessing the CMS from another machine.
Images and approvals live under `local/authoring/concepts/` on the CMS host.

## Add a page

The CMS has one browser shell and hash routes. Follow [the concept page](web/concepts.js) for a detail view or
`listing()` in [app.js](web/app.js) for a catalog.

1. Give the page a stable hash route. For a top-level section, add an `href="#page"` link with `data-page="page"`
   in [index.html](web/index.html). Link detail pages from their parent records and have them highlight the parent
   section in the navigation.
2. Add a branch in `render()` in [app.js](web/app.js). Use the data already loaded from `/api/catalog` when it
   contains the needed fields. Otherwise, fetch a detail endpoint with `request()`. Check the captured view
   against `currentView` after an awaited fetch so a slower result cannot replace a page the user navigated to.
   Put a substantial renderer in its own module under `web/` and import it from `app.js`. For a new shared
   catalog, add its rows to `Catalog.overview()` and give `listing()` a title and card renderer. Add it to
   `sections()` and `searchResults()` if it should appear in the overview and global search.
3. Put any new data route in [server.py](server.py). Read or update records through their owning adapter.
   [catalog.py](catalog.py) assembles the asset lists, [pipelines.py](pipelines.py) reads character stages, and
   [review_media.py](review_media.py) locates stage previews. Keep asset paths and their browser mounts in
   [library.py](library.py). For a new reviewable record type, pass its category and stable ID to `tileReview()`
   or `reviewPanel()` in [reviews.js](web/reviews.js) to share marks, ratings, and notes.
4. Add only the page-specific styles to [style.css](web/style.css). Open the route directly and through its
   parent link, then exercise its normal actions. Test writes on a loopback instance with scratch paths for
   `--links`, `--reviews`, and `--concept-root`. For merge tests, also pass a copy of the NPC chart and merge
   ledger with `--npc-root` and `--npc-merges`.

This starts an isolated instance on a free loopback port. Open the URL it prints.

```sh
scratch=$(mktemp -d)
python3 authoring/cms/server.py --port 0 --links "$scratch/links.json" \
  --reviews "$scratch/reviews.sqlite3" --concept-root "$scratch/concepts"
```

Run the CMS checks when changing code.

```sh
python3 -m unittest discover -s authoring/cms
node --check authoring/cms/web/app.js
```
