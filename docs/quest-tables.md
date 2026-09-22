# Quest tables and classic catalog

`Daggerfall.Import` reads the donor's global, static-message, place, sound,
disease and spell tables offline. The ordinary `quests` command publishes
`questTables`, the classic `questCatalog`, `questSources`, and the per-stem
`questOriginalSources` selection/provenance section in the base payload. The latter
accounts for every supplied classic QBN/QRC stem. It records actual QBN resource and
opcode message fields, QRC record IDs, offsets, decoded delimiters, and payload digests
against the rewritten text identities. QBN data remains offline evidence; it is not an
action decoder or an execution path. A stem without compiled rewritten text is explicitly
not enabled, including the four QBN-only and one QRC-only originals.

The rewritten text is read with Daggerfall Unity's
`Assets/Scripts/Game/Questing/Parser.cs`. The fixed QBN identity layout is
cross-checked against the independent Quester decompiler's
[`QbnReader.cs`](https://github.com/stellargames/Quester/blob/master/Quester/QbnReader.cs): only its header/section framing and QRC-reference fields are adopted here, then bounded against every supplied local QBN. The classic corpus remains the source of record for each published path, record offset, text, and digest.

The `compiled` source disposition means QRC messages and finite top-level QBN
blocks parsed without diagnostics. It does not mean a quest can execute: ordered
action-source lines still need the separately owned quest runtime and action
families. Diagnosed sources retain their file and physical line information.

```sh
dotnet run --project src/Daggerfall.Import.Tool -- quests \
  --arena2 local/arena2 \
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

Places retain every raw parameter and spelling. Permanent locations expose the
packed location key from `p1` and `p2`, plus the low byte of `p2` used for teleport
transfer. `Mantellan_Crux` resolves through `MantellanCrux` while its original
source row remains available. Sound names preserve indices, including `empty`
and storm variants; a symbol is not a claim that an audio asset is playable.
Disease IDs remain 0–16, and spell IDs remain sparse, with aliases such as
`HolyWord` and `HolyTouch` sharing 58. These are source identifiers, not new
effect implementations.

`ActorItemTables` carries item class/subclass parameters (including all 24
artifacts), faction/person parameters, and foe IDs. Source names are the primary
keys; numeric aliases remain legal. `Sorceror` and `Sorcerer` both name foe 131,
and Knight retains ID 145 alongside the source's missing-MONSTER.BSA warning.
Disabled faction rows, unresolved `?` values and source comments remain visible
as data; only active rows enter ordinary name lookups.

The classic catalog retains 187 active and 23 disabled rows in source order,
including five rows whose quest source is missing. It records group, membership,
rank/level/reputation threshold meaning, adult and one-time flags, source lines
and notes. Disabled Oblivion rows that omit membership preserve that absence.
The publication does not include DFU-only lists or discover quest packs, and
does not apply faction eligibility policy at runtime.
