# Import-side data

Checked-in data read by the import (`Daggerfall.Import.Tool`), `Daggerfall.Source.Tool` and their tests. None
of it is loaded by the runtime product. Every file here but this README is an importer revision input
(`src/ImporterRevision.targets`), so an edit makes every machine's imported data stale until den-agents
imports again.

| File | Contents | Readers |
| --- | --- | --- |
| `content-source-manifest.csv` | Per-file disposition of the operator-supplied Arena2 corpus, described on the wiki page `rusty-dagger/content-sources`. | The import's `--inventory` argument; `Daggerfall.Source.Tool`'s `source-manifest --update-inventory` rewrites it; `tests/Daggerfall.Import.Tests`. |
| `ui-authored-assets.json` | Authored UI asset manifest: each asset's ID, published output name (`file`, under `packs/daggerfall.art-replacements/authored-ui/`), original file (`sourceFile`, the same name in `content/worldrpg/packs/daggerfall.art-replacements/authored-ui/`), generator, prompt and optional `display` (CSS `scale` per image pixel, 9-slice `slice` insets, cursor `hotspot`). Written by `authoring/tools/ui/ui-art-kit.py manifest`. | The import's `--ui-authored-assets` argument, with `--ui-original content/worldrpg/packs/daggerfall.art-replacements/authored-ui`. |
| `daggerfall-unity/` | The Daggerfall Unity tables and quest scripts the import reads; see its README. | The import's donor arguments; tests. |

The authored UI images themselves are product content, tracked in the art pack's folder,
`content/worldrpg/packs/daggerfall.art-replacements/authored-ui/`: the ruleset reads them in place, and the
import lists them in the classic media inventory without copying them. They are
AI-generated originals made for this project, already processed to their
published size. `ui-authored-assets.json` records each one's generator, seed and
processing, and its exact prompt, which asks for original art not copied from
any game. The "Authored art" section of `src/ui/AGENTS.md` gives the recipe.
