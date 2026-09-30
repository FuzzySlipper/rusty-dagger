# Import-side data

Checked-in data read by `Daggerfall.Import.Tool` and its tests. None of it is
loaded by the runtime product.

| File | Contents | Readers |
| --- | --- | --- |
| `content-source-manifest.csv` | Per-file disposition of the operator-supplied Arena2 corpus, described in [content scope](../docs/coverage/content-scope.md). | The tool's `--inventory` argument (`source-manifest --update-inventory` rewrites it); `tests/Daggerfall.Import.Tests`. |
| `dungeon-corpus-closure.tsv` | Every source ID of the world corpus closure summarised in [dungeon corpus closure](../docs/coverage/dungeon-corpus-closure.md). | Reference data for that report. |
| `ui-authored-assets.json` | Authored UI asset manifest: each asset's ID, published output name (`file`, under `media/ui/authored/`), original in `ui-original/` (`sourceFile`), generator and prompt. | The tool's `--ui-authored-assets` argument, with `--ui-original data/ui-original`. |
| `ui-original/` | The original PNGs named by `sourceFile`. | As above. |
| `sprite-names.json` | Historical sprite naming evidence with no active consumer. | None. |

The images in `ui-original/` are AI-generated originals made for this project
with the generators and prompts recorded in `ui-authored-assets.json`, which ask
for original art not copied from any game.
