# Art conversion (items and the other areas)

Converts the Daggerfall item export's `world` role (TEXTURE.2xx inventory and dropped-item art) into the
repository's own icons. The accepted recipe is **Qwen painted reconstruction, then Krea 2 turbo retro at
denoise 0.35, then broad back-projection at 0.5 (`bp-broad-half`)**, then a transparent cut-out.

## Where things live

| Path | What | Git |
| --- | --- | --- |
| `authoring/df-raw/items/` | The importer's item export (`export-items`): per-item pages, `index.csv`, native source PNGs | ignored |
| `authoring/df-raw/spells/`, `paper-doll/`, `effects/` | `export-spells`, `export-paper-doll` (BODY, FACE, were and vampire heads) and `export-effects` (TEXTURE.380 and 434): the source art for the other visual-refresh tasks | ignored |
| `authoring/gen-raw/<area>/` | Finals, 1024² RGBA. `items` and `worn` name a final `texture-<archive>-record-<record>-frame-<frame>.png`; the other areas use `<subject directory>--<file stem>.png` (`tools/items/art_key.py`). A redone image overwrites its file | ignored |
| `authoring/gen-raw/studies/` | Working runs (Qwen, retro, back-projection, cut-out, compare sheets) | ignored |
| `authoring/review/` | Generated review pages (`index.html` for items, `<area>/index.html` for the others), `*-ledger.csv`, thumbnails | ignored |
| `authoring/specs/<area>/` | Contract specs, one pair per batch (`items-icons-NAME.json` for items, `<area>-NAME.json` otherwise, each with `-retro.json`), and `templates/` per art type | tracked |
| `authoring/ledger/<area>-notes.json` | The only hand-edited tracking input: status and notes per image key | tracked |
| `authoring/tools/items/` | Batch runner, spec scaffolder, post-process, finalize, flame compositing, ledger builder, paper-doll preview, proportion check, dress-on-body worn items, shared body shapes, head trimming, shared skin | tracked |

`local/` no longer holds any of this. The generic ComfyUI generator (`tools/lora-training/generate.py`),
the LAN environment script and `asset-resources` stay in the asset-pipeline checkout (`ASSET_PIPELINE`,
default `~/dev/asset-pipeline`); host and model notes are in its `docs/asset-hosts.md`.

## Areas

| Area | Source (`df-raw/`) | Templates |
| --- | --- | --- |
| `items` | `items/` world sprites (inventory and dropped-item art) | `items-equip-world.json` |
| `worn` | `items/` player layers (worn clothing, armor, weapons, artifacts; one row per archive, record, frame, shared by every body morph variant) | `worn` |
| `paper-doll` | `paper-doll/` bodies, faces, creature dolls | `bodies`, `faces` |
| `spells` | `spells/` selector icons, targeting icons and element projectile/impact frames (the per-spell copies and casting hands are not converted) | `icons`, `fx` (from `effects`) |
| `effects` | `effects/` TEXTURE.380 and 434 | `fx` |

## Run a batch

Write the contracts first (below), then scaffold the pair of specs from a list of items and run it:

```sh
authoring/tools/items/make_spec.py AREA NAME items.json --template KIND     # writes specs/AREA/...-NAME(.json|-retro.json)
AREA=spells authoring/tools/items/run_batch.sh NAME den-m5 m5-comfy         # AREA defaults to items; or den-nimo nimo-comfy
python3 authoring/tools/items/review_ledger.py --area spells                # rebuild authoring/review/
```

Both stages run on the named host. den-m5 and den-nimo each have the Qwen-Image-2.1 and Krea 2 weights, so two
batches can run side by side, one per host. Different hosts are not bit-identical; keep a batch on one host.
Do not move a finished batch's finals by hand: `finalize.py` copies the cut-outs into `gen-raw/<area>/`, with each paper-doll or worn final's rect sidecar.

## Contracts

A spec copies the style card, collection rules, presentation, seed and routes from
`specs/items/items-equip-world.json` and replaces `items`. Each item has `id`, `directory`, `image`, `name`,
`content`, `uncertainty` and `invention`.

- Generate each unique sprite once. Templates that share it are named in `uncertainty`; the engine recolours it.
- Score identity before polish. State unresolved features as interpretations and forbid unsupported gems,
  symbols, handles, text and extra parts.
- Keep descriptions short for tiny sprites. Where a material or object is genuinely ambiguous, say so in
  `uncertainty` and end it with "This is a guess for owner review." The ledger turns that phrase into the
  `needs-owner-review` status and a "guess" tag.
- If the source pixels disagree with the template's name (a tomato in a "wine rack" slot), depict the pixels and
  flag the mismatch.
- Animated sprites: convert frame 0 as a normal item. Convert frames 1+ with an `items-icons-frames-*.json`
  batch; only the flame differs, so `composite.py` keeps frame 0's body and takes just the flame region.

## Transparency

`postprocess.py cutout` turns the flat background into alpha: the background is the border colour, enclosed
ground regions are kept or removed by the source sprite's own alpha (so a glass jar stays solid and a chain loop
stays open), and the edge is softened and decontaminated.

## Clean-up and source edits

- `tools/items/despeckle.py FINAL.png` removes small detached flecks from a final (not for effects that carry embers).
- To convert from an edited source (for example neutralising a dye-placeholder colour), write the edited copy with its manifest under `gen-raw/source-edits/NAME/` in the same directory layout and run the batch with `SOURCE=` pointing at it.
- `paper-doll/templates/bodies-detailed.json` is the more detailed pulp-fantasy style for figures; `bodies.json` is the plainer painted style.
- `tools/items/doll_preview.py` composites paper dolls from `paper-doll` and `worn` finals, each at the doll-space rect its sidecar states, beside the classic doll and an overlay. It reports each layer's doll-space rect and its overlap (IoU) with the source silhouette. Use it to check that a body, head or worn set fits together before review: `--sheet bodies`, `--sheet faces`, or one doll with `--item KEY` repeated in draw order. Output goes to `review/paper-doll/`.
- `tools/items/dress.py OUT --race R --gender G --spec specs/worn/dress-ROLE.json --each --source gen-raw/source-edits/worn-neutral-mens` dresses a body morph's worn library on its base body (Qwen, Patch's 5090 by default). It writes doll-space layers to `OUT/layers` for `doll_preview.py --worn OUT/layers`.
  - A spec lists each item with what it is (`name`, with its material where the classic pixels are ambiguous), and optionally `reach`, `head` (dress it over head `--face`: helms), `behind` (drawn behind the body: cloak linings) and `metal` (steel and mail pieces). `--item KEY --name WHAT` does the same for a few items.
  - `--each` paints every item alone on the bare body, so the layers combine freely. Without it each item goes on over the last, as one outfit.
  - `--source` reads the dye-neutral clothing sources first, so cloth is painted pale grey for the game's tint.
  - Cloth and leather are dressed on the body (and head) recoloured to a violet key. Skin left inside a piece (an open shirt's chest, a hand on trousers, a thigh above a boot) is then told from the piece and left out of the layer, so each race's own skin shows through. Metal pieces and cloak linings are dressed on the body's own skin: Qwen paints steel beside violet skin, and a lining in its shadow, with a violet cast that keys away as skin.
  - Each painted piece is first keyed against the figure beneath. When that keeps nearly all of the classic piece, it is the layer, with no further Qwen call. Otherwise the piece is lifted off through a Qwen isolation on flat green. An isolation that keeps too little or too much of the piece, or a translucent ghost of it, is retried with the next seed, then on flat blue (brown and tan pieces such as trousers survive blue when Qwen erases them on green). When every isolation fails, the difference key stands, with holes where the piece matches what it covers. Look at the isolations anyway.
  - Dress on bodies that pass `landmarks.py check`. On a body whose feet reach past the classic boots, Qwen paints open-toed sabatons around the bare toes.
- `tools/items/landmarks.py standard` measures Daggerfall's paper-doll proportions from the classic sprites into the tracked `specs/paper-doll/proportions.json`. For each body, unclothed (I0) and clothed (I1), it records the top, shoulder line and width, armpits, hands, waist and its width, crotch, knees, ankles, soles and toe tips. For each head record it records the crown, bottom and sides, all in doll units. Waist, knees and ankles are the narrowest rows of a gradual narrowing, so each also records its span, half the rows within half a unit of the narrowest, which the check adds to the tolerance. A clothed body's knees and ankles are its unclothed body's, since a loincloth hides where the legs part. `landmarks.py check [--paper-doll DIR ...]` measures the generated bodies (both layers) and heads the same way. It fails any landmark, or any outline band (neck and shoulders, arms and torso, hips and thighs, shins, feet), that lies more than two doll units from its classic. `doll_preview.py` prints the same check for each doll it draws. A body is checked by morph: its body landmarks and outline against the classic body of the morph's base race, whose shape every race of the morph shares (`bodies.py`), and its head box against its own race's head. Every doll layer is drawn to these proportions, so run the check on bodies and heads before dressing or promoting them.
- `tools/items/bodies.py OUT` rebuilds the generated bodies so that every race of a morph shares one shape on Daggerfall's proportions, as the classic bodies do. There is one base per morph and gender: Breton for human, Dark Elf for elf.
  - The base I0 is conformed: the classic sprite is repainted by Qwen in the style of the current generated body, inside a mask of the classic silhouette grown by one doll unit. The figure is opaque within the silhouette less a unit, and the classic's enclosed gaps stay open. Khajiit and Argonian prompts name the tail's path, or Qwen leaves it out.
  - Another race's I0 is the base recoloured to its own body's OKLab colour.
  - Every I1 is its I0 with the classic underwear painted on by Qwen. The stray pixels where classic I1 and I0 also differ are left out. The elf, Khajiit and Argonian men wear a loincloth with a flap hanging to mid-thigh; the prompt names it, or Qwen paints a brief.
  - Each final carries a sidecar with the base's doll-space rect. Worn items dressed on the base (`dress.py --paper-doll`) then fit every race of the morph.
- `tools/items/heads.py OUT` trims each generated head to its classic silhouette grown by a doll unit, so a head carries no more neck or hair than Daggerfall's. A head smaller than its classic one can't be fixed by trimming and needs regenerating. `heads.py OUT --worn KEY ...` trims held weapons and artifacts the same way, so the gap the classic layer leaves at the grip, where the body's hand shows through, stays open: Qwen draws the grip whole. `heads.py OUT --creature vamp00i0` does the same for a creature file's records.
- `tools/items/pictures.py BP_DIR SPEC` fits a generated framed picture (the beast-form dolls, whole paintings drawn in place of the doll's background) to its classic picture's rect: the cut-out can't tell the grey stone frame from the grey generation background, so it crops the back-projected image to the picture, stretches it to the classic aspect and writes it opaque with a sidecar rect.
- `tools/items/skin.py OUT` gives each race's generated heads the skin of its generated body, as Daggerfall's shared palette ramp does.
  - The skin is the classic head's pixels in the body's colours, kept where the generated colour is close to that region's core tone, so hair is left out.
  - Each skin pixel keeps its shading and local hue offset (blush, lips), takes the body's colour at its lightness, and has its lightness moved so the head's mean skin lightness matches the body's.
  - Hair, eyes, paint and headgear keep their colours. Run it after `heads.py` and before a doll preview.
- `doll_preview.py` and `landmarks.py` take `--paper-doll` (and `doll_preview.py` takes `--worn`) more than once; the first directory holding a final wins.

## Review ledger

`review_ledger.py --area A` joins the export, the specs, the post-process metrics and `gen-raw/A/` into one row per
image key and writes `review/index.html` (items) or `review/A/index.html` (cards, filters, detail) and the matching `*-ledger.csv`.
Status is derived (`unconverted`, `converted`, or `needs-owner-review` for a flagged guess) unless
`ledger/<area>-notes.json` sets `accepted`, `redo` or `skipped` with a note. Rows whose extra frames exist are
shown as separate frames.

## Promotion

`promote.py --area A` (areas `paper-doll` and `worn` so far) writes every final that `ledger/A-notes.json` marks
`accepted` into the tracked `daggerfall.art-replacements` pack: a WebP per final under
`content/worldrpg/media/refreshed/A/` and an entry in `content/worldrpg/payloads/daggerfall.art-replacements.json`
naming the classic media id it stands in for. A paper-doll final keeps the doll-space rect `doll_preview.py`
draws it at (its sidecar's), moved to its cropped content. Clothing, and armour or weapons the classic also draws in other metals,
get a dye mask (the neutral cloth or bare steel), and the `worn` area states each classic dye's and metal's tint,
measured against the import's published dyed images (so the import must have run). Every paper-doll and worn final states
its rect in a sidecar: `finalize.py --study` writes it from the generation's recorded input transform (the source's
whole-number scale and canvas placement, at the spec's canvas size), and `dress.py`, `bodies.py` and `pictures.py`
write the rect they draw in. A final without one is refused. `tools/items/test_registration.py` checks a final made
at a non-default canvas size and occupancy lands where its generation put it. Rerunning an area replaces that area's files and entries, so a redo
reaches the game by being accepted and promoted again. Nothing unaccepted or from `studies/` is promoted.

## Related

- Recipe measurements and rationale: [reference/item-art-studies.md](reference/item-art-studies.md),
  [reference/item-art-prompts.md](reference/item-art-prompts.md), [reference/dagger-retro.md](reference/dagger-retro.md).
- Task context lives in Den (tasks 10494–10496); this directory holds the process, not the status.
