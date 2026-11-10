# Authoring

Art conversion work for the visual refresh: raw Daggerfall exports go in, reviewed final art comes out.
Generic image tooling stays in the asset-pipeline checkout; everything Dagger-specific lives here.

```
authoring/
  docs/        process docs (item-icons.md, conversion-plan.md, weapons.md, mounts.md, people-animation.md, ui-and-2d.md); reference/ holds the asset-pipeline-era study docs
  tools/       scripts (items/ serves every area)
  specs/       tracked contract specs per area and batch (items, worn, paper-doll, spells, effects)
  ledger/      tracked status and notes per sprite
  df-raw/      importer exports of the original game's art: items/, spells/, paper-doll/, effects/, flats/   (ignored)
  gen-raw/     generated art: finals (one directory per area) and working runs (studies/)   (ignored)
  review/      generated review pages and spreadsheets            (ignored)
```

Status of each asset moves `df-raw` → `gen-raw` → (reviewed) shipped content, so a path tells you what a file is.
`tools/items/promote.py --area A` writes an area's ledger-accepted finals into the tracked refreshed-art pack
(`content/worldrpg/media/refreshed/`, see `docs/coverage/content-scope.md`).
The first-person weapon and mount models are authored here (`tools/weapons`, `tools/mounts`). The arms container that
packs them, `scripts/fp-arms/`, is a product build step instead: it converts the purchased arm pack from the ignored
`local/fp-melee/` into `local/first-person.rpak`, rather than converting art ([scripts/fp-arms](../scripts/fp-arms/README.md)).
Start with [docs/item-icons.md](docs/item-icons.md). `authoring/` also holds the sprite workbench's operator
overlays (`authoring/sprites/`, see the root README), which are separate from the above.
