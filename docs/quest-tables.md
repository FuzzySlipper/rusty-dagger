# Quest global and static-message tables

`Daggerfall.Import` reads the donor's `Quests-GlobalVars.txt` and
`Quests-StaticMessages.txt` offline. The ordinary `quests` command publishes
`questTables` alongside `questSources` in the base payload:

```sh
dotnet run --project src/Daggerfall.Import.Tool -- quests \
  --quest-text /home/research/daggerfall-unity/Assets/StreamingAssets/Quests \
  --tables /home/research/daggerfall-unity/Assets/StreamingAssets/Tables \
  --pack content/worldrpg/payloads/daggerfall.base.json \
  --inventory docs/coverage/content-source-manifest.csv --update
```

Omit `--update` to inspect without writing. Each table keeps its logical source
path, source-byte digest and length, plus every row's original spelling, numeric
ID and one-based source line. The globals contain 66 aliases for 64 slots;
`TookTheCure` shares slot 5 with `Unused1`, and `OpenedShapeshifters` shares slot
10 with `Unused2`. Static messages retain 17 source rows for 13 numeric IDs,
including the case aliases at 1006–1009. Lookup is case-insensitive and returns
the canonical numeric identity; publication does not deduplicate source rows.

The quest source importer consumes these tables instead of a copied name list.
Bracketed fixed message headers resolve their ID from the table by name; bare
message headers retain their explicit numeric ID.
The ruleset admits them through `DaggerfallBaseContent`; `QuestSources.Tables`
provides the named lookup surfaces. `DaggerActorFactory` supplies the admitted
global aliases to `DaggerfallVariableStore`, whose existing save records still
store numeric addresses and values. No donor save-file format is imported.
