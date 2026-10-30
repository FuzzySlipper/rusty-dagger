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
| `authoring/tools/items/` | Batch runner, spec scaffolder, post-process, finalize, flame compositing, ledger builder | tracked |

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
Do not move a finished batch's finals by hand: `finalize.py` copies the cut-outs into `gen-raw/<area>/`.

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

## Review ledger

`review_ledger.py --area A` joins the export, the specs, the post-process metrics and `gen-raw/A/` into one row per
image key and writes `review/index.html` (items) or `review/A/index.html` (cards, filters, detail) and the matching `*-ledger.csv`.
Status is derived (`unconverted`, `converted`, or `needs-owner-review` for a flagged guess) unless
`ledger/<area>-notes.json` sets `accepted`, `redo` or `skipped` with a note. Rows whose extra frames exist are
shown as separate frames.

## Related

- Recipe measurements and rationale: [reference/item-art-studies.md](reference/item-art-studies.md),
  [reference/item-art-prompts.md](reference/item-art-prompts.md), [reference/dagger-retro.md](reference/dagger-retro.md).
- Task context lives in Den (tasks 10494–10496); this directory holds the process, not the status.
