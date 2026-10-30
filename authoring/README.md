# Authoring

Art conversion work for the visual refresh: raw Daggerfall exports go in, reviewed final art comes out.
Generic image tooling stays in the asset-pipeline checkout; everything Dagger-specific lives here.

```
authoring/
  docs/        process docs (item-icons.md, conversion-plan.md); reference/ holds the asset-pipeline-era study docs
  tools/       scripts (items/ serves every area)
  specs/       tracked contract specs per area and batch (items, worn, paper-doll, spells, effects)
  ledger/      tracked status and notes per sprite
  df-raw/      importer exports of the original game's art: items/, spells/, paper-doll/, effects/   (ignored)
  gen-raw/     generated art: finals (one directory per area) and working runs (studies/)   (ignored)
  review/      generated review pages and spreadsheets            (ignored)
```

Status of each asset moves `df-raw` → `gen-raw` → (reviewed) shipped content, so a path tells you what a file is.
Start with [docs/item-icons.md](docs/item-icons.md). `authoring/` also holds the sprite workbench's operator
overlays (`authoring/sprites/`, see the root README), which are separate from the above.
