# Import-side data

Checked-in data read by `Daggerfall.Import.Tool` and its tests. None of it is
loaded by the runtime product.

| File | Contents | Readers |
| --- | --- | --- |
| `content-source-manifest.csv` | Per-file disposition of the operator-supplied Arena2 corpus, described in [content scope](../docs/coverage/content-scope.md). | The tool's `--inventory` argument (`source-manifest --update-inventory` rewrites it); `tests/Daggerfall.Import.Tests`. |
| `ui-authored-assets.json` | Authored UI asset manifest: each asset's ID, published output name (`file`, under `media/ui/authored/`), original file (`sourceFile`, the same name in `content/worldrpg/media/ui/authored/`), generator, prompt and optional `display` (CSS `scale` per image pixel, 9-slice `slice` insets, cursor `hotspot`). Written by `scripts/ui-art-kit.py manifest`. | The tool's `--ui-authored-assets` argument, with `--ui-original content/worldrpg/media/ui/authored`. |
| `sprite-names.json` | Historical sprite naming evidence with no active consumer. | None. |

The authored UI images themselves are product content, tracked in
`content/worldrpg/media/ui/authored/`: the ruleset reads them in place, and the
import (`dotnet run --project src/Daggerfall.Import.Tool -- import --music
local/Sound`) lists them in the classic media inventory without copying them.
They are AI-generated originals made for this project, already processed to
their published size. `ui-authored-assets.json` records each
one's generator, seed and processing, and its exact prompt, which asks for original
art not copied from any game. `scripts/ui-art-kit.py` and the "Authored UI art"
section of `docs/game-ui.md` give the recipe.
