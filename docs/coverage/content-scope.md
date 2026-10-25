# Daggerfall authored-content scope

An input to [the Daggerfall coverage plan](../daggerfall-coverage-plan.md) and
[the coverage scope contract](../daggerfall-task-preparation.md). This document inventories
the authored corpus and its current Rusty Dagger boundary. It is a planning inventory, not a
binary dump, a runtime source reader, or a claim that every listed record already imports.

The target is the supported classic game record set needed by the playable Daggerfall world:
world and location data; actors, races, careers, items and effects; text and books; main,
guild and miscellaneous services/presentation; quests; ordinary sound and story media. A
source file receives a planning disposition in the table below or in the companion
[source manifest](../../data/content-source-manifest.csv); archive-internal record dispositions
are assigned by the relevant import/publication tasks. “All content” is not a disposition.

The authored `daggerfall.base` payload owns the `newGame` starting-loadout section: playable-career weapons and spell keys, custom-class starting items, gender-specific clothing, spellbook and gold. These values resolve through the normalized item and spell catalogs; session construction consumes them once. The selected bundle owns the start site; a bundle may also select one `daggerfall.new-game-start` pack, which replaces only the start site, starting gold, opening quests and start hour (the developer `daggerfall.town-start` bundle uses it to begin in Charing).

Site packs may declare `world.variant` and `world.variantOf` (the base profile's
logical publication path) for a named location scenery variant. A selected bundle
must carry a base profile and every profile of each admitted location variant.
Quest `worldupdate location` selects that variant for the session; `variant -`
returns to the base. The existing site lifecycle applies the choice on the next
projection admission, including save restore. Scenery variants retain actor,
action, dungeon-map and inventory-container topology and their audio.
Changes to those owners, block/building variants and new-location variants are
currently reported as unsupported. Variant payloads and normalized closures
follow the same authored/generated ownership rules as other site packs.

## Evidence and boundaries

The companion CSV's **file-row disposition** is reconciled against the source
closure of `worldrpg/imports/privateers-hold/import-manifest.json` (in `local/import/staging/` and the data container).
`imported` means that publication names the source; `unused` means the supplied
source is outside that closure. Neither means that every record in the source is
implemented, or that other publications cannot use it. Family rows retain their
planning dispositions. After refreshing this bundle, run the import tool's
`source-manifest` command with `--publication` pointing to the bundle, `--output` naming the
manifest file to write and `--inventory data/content-source-manifest.csv --update-inventory`,
then run `plan` with that inventory to check drift. The reconciler changes only file
dispositions; historical scope notes and source counts remain inventory evidence.

The import also writes `local/import/records/source-coverage.json`. To refresh only that report, run
`source-coverage --arena2 local/arena2` through the import tool in the repository root (its other options
default to the staging tree and records). It joins all published producer citations, raw block/model
ledgers and original-quest comparisons with corpus receipts. Family citations, offline
record evidence and normalized pack references remain distinct. Pending, unused,
duplicate, malformed, source-gap and unsupported dispositions remain visible; a
missing disposition or required source without a reference makes reconciliation fail.
MIDI remains excluded. This report never certifies runtime behavior parity and is
generated local evidence, so it is ignored and never committed.

The supplied source corpus (`local/arena2`, read only) contains 1,590 files at its root and 90
files under `books/`, occupying about 517 MiB. Counts below are filesystem counts or archive-header counts unless explicitly marked
as a donor count. The raw files remain local provenance; this document does not copy their
copyrighted payloads into a pack.

The semantic donor is the frozen `/home/research/daggerfall-unity` checkout. The
relevant exact donor sources are
`Assets/Scripts/Utility/ContentReader.cs`, `Assets/Scripts/API/MapsFile.cs`,
`Assets/Scripts/API/ItemsFile.cs`, `Assets/Scripts/API/ClassFile.cs`,
`Assets/Scripts/API/DFCareer.cs`, `Assets/Scripts/Game/Entities/RaceTemplate.cs`,
`Assets/Scripts/Utility/EnemyBasics.cs`, `Assets/Scripts/API/BookFile.cs`,
`Assets/Scripts/API/TextFile.cs`, and `Assets/Scripts/Game/Questing/Parser.cs`. They establish
record relationships and classic semantics; their Unity bootstrap, singleton, serialization,
and distribution topology is excluded by the coverage plan. Donor consultation is evidence for
the proposed records, not permission to move donor ownership into Rusty Dagger.

Current Rusty Dagger content is under `content/worldrpg/`, the data container and `src/Daggerfall.Import/`. The
published baseline is the `daggerfall.base` pack plus one `daggerfall.privateers-hold` pack.
The current import manifest is
`worldrpg/imports/privateers-hold/import-manifest.json` (68 source inputs and 189
artifacts); its selected source list is deliberately narrow. The current base payload has 45
actor definitions and 31 item definitions. Privateer’s Hold has 42 placements and one
normalized dungeon world. Its normalized dungeon media has 81 materials, 33 billboards and 7
actor media entries. The classic media publication currently exposes 9 weapon images, 6
sound clips and selected UI/font/texture assets. These are anchors for remaining work, not the
coverage target.

Disposition terms used here:

| Disposition | Meaning for task planning |
| --- | --- |
| `current-partial` | A real importer, normalized artifact or pack admits a named subset. The family remains open until the target records are enumerated and resolved. |
| `current-structural` | A decoder or normalizer exists for a bounded record shape, but corpus discovery/publication is incomplete. |
| `pending-import` | The source is present and in scope, but no current Rusty importer/publication owns it. |
| `source-gap` | The donor identifies a required source whose bytes are absent from this local corpus. |
| `uninspected` | The file is present, but its record semantics or usable record count still needs a bounded existing-decoder inventory. |
| `excluded` | The coverage plan explicitly leaves the family or topology outside the product target. |

Per-file rows additionally use the dispositions the import tool computes, which are the
values of `SourceRecordDisposition` in `src/Daggerfall.Import/Publication/SourceManifest.cs`:

| Disposition | Meaning for a supplied record |
| --- | --- |
| `imported` | Decoded and published into the normalized pack. |
| `required-pending` | Admitted, and required by a named normalizer that has not consumed it yet. |
| `unused` | Supplied and admitted to a family, but no consumer claims it. |
| `duplicate` | Another inventory row already claimed the same supplied file. |
| `malformed` | Supplied, but reading or decoding it failed; the note carries the reason. |
| `unresolved` | Supplied with no documented inventory row; it still needs a disposition. |

`source-gap` keeps the family-level meaning above and additionally marks a documented file
this local corpus does not supply. A record that states a byte length always states the
digest of exactly those bytes, so a `source-gap` carries neither. Each run of
`daggerfall-import-tool source-manifest` reports where the documented column disagrees with
the tree, and `--update-inventory` is the only writer of that column.

Stable `CNT-...` IDs name source families or authored sets. They must not be reused when a
family is split into implementation tasks; child tasks should retain the parent ID and add a
stable suffix. Individual QBN, QRC and book identifiers are in the companion manifest so that
file identity is not lost in a family-level task.

## Manifest coverage and identity

The CSV contains **29 family-summary rows and 1,680 per-file rows**: all 1,590
root files and all 90 files under `books/` in the supplied Arena2 directory.
`row_type` distinguishes summaries from files; do not sum both as content counts.
File IDs have the form `CNT-017.file.A0C10Y02.QBN`, preserve exact source path
casing, and carry `family_id`, byte size, comparison stem and disposition.
For future reclassification, keep the established file ID stable and update its
family mapping; do not renumber or recreate it merely because ownership changes.

Per-file `uninspected` means metadata is inventoried but record interpretation,
publication status and required/unused classification are not established. It does
not mean that all current importer coverage is absent. Named family coverage above
remains the reuse starting point. MIDI is explicitly excluded. Residual files under
CNT-027 remain individually visible for task-local classification rather than being
silently omitted. This manifest contains metadata only, not game payloads.

## Source-family inventory

| ID | Authored set and source identity | Available metadata | Current Rusty scope | Disposition and task expansion |
| --- | --- | --- | --- | --- |
| CNT-001 | `local/arena2/MAPS.BSA` (published input path `arena2/MAPS.BSA`) | 248 named records: 62 region groups × `MAPPITEM`, `MAPDITEM`, `MAPTABLE`, `MAPNAMES`; donor `MapsFile.RegionCount` derives the same 62 groups | All readable region/location tables are published with map IDs, types, coordinates, discovery, exterior layouts and dungeon placements; empty source tables retain explicit dispositions. Runtime assembly places catalog locations from normalized blocks. | `current-structural`: source coverage identifies each MAPS record; gameplay use and parity remain the responsibility of the owning feature. |
| CNT-002 | `CLIMATE.PAK` | 14,771 bytes; existing `PakDecoder` knows the 1001 × 500 grid shape including sentinel cells | The full climate grid and location climate interpretation are published and consumed by terrain, scenery, weather and seasons. | `current-structural`: retain sentinel cells and source-format interpretation in the importer. |
| CNT-003 | `POLITIC.PAK` | 19,597 bytes; same PAK family and grid capacity | The political grid is published and binds world cells to region and faction interpretation. | `current-structural`: runtime political and social behavior uses the published catalog. |
| CNT-004 | `WOODS.WLD` | 26,001,168 bytes; record count not asserted without an existing wilderness decoder | The wilderness heightmap and terrain records are decoded and published for runtime terrain assembly, with climate and location transitions. | `current-structural`: source terrain publication is distinct from visual and gameplay fidelity. |
| CNT-005 | `BLOCKS.BSA` | 1,295 named BSA records (`0x0100` header) | Every RMB exterior and building interior a location places and every RDB block is normalized per block (`world-blocks`, from the same block-level content the site closures place); 262 RMB records no location places are named, not normalized | `current-structural`: enumerate all RDB and RMB records, preserve block kind/letter/number, referenced objects, doors, triggers, textures and placements. |
| CNT-006 | `ARCH3D.BSA` | 10,251 numeric BSA records (`0x0200` header) | World media publishes all decodable source meshes once; world blocks and site closures reference those shared geometry and material artifacts. The geometry ledger records unusable and duplicate source records. | `current-structural`: preserve per-record source and failure dispositions; publication does not certify every model placement. |
| CNT-007 | `MONSTER.BSA` | 103 named BSA records (`0x0100` header), including `ASCR*.ANC` and `ENEMY*.CFG` families | The archive inventory records configurations and animation families with explicit dispositions. Runtime mobile presentation uses normalized texture sprites and the compiled mobile catalog rather than executing archive configuration files. | `current-partial`: retain archive records and actor/media links separately from runtime policy. |
| CNT-008 | Classic mobile actor definitions | Donor `EnemyBasics.Enemies` contains 63 static definitions; `MONSTER.BSA` is a separate source candidate, not proof of missing runtime fields | All 63 classic mobile definitions and their normalized combat, movement, media, corpse, sound and loot metadata are published; fixed and random markers select these identities. | `current-structural`: donor definitions remain content provenance, with ruleset-owned behavior. |
| CNT-009 | Race table | Donor `RaceTemplate.GetRaceDictionary` defines 8 races: Breton, Redguard, Nord, DarkElf, HighElf, WoodElf, Khajiit and Argonian | The eight race definitions and character presentation references are published for character creation and runtime policy. | `current-structural`: race identity and gameplay policy have explicit content owners. |
| CNT-010 | Career/class records: `CLASS00.CFG`–`CLASS18.CFG` and `CLASSES.DAT` | 19 CFG files, each 74 bytes; `CLASSES.DAT` is 216 bytes. Donor `ClassFile` and `DFCareer` identify the class fields and career semantics | All supplied class records and class tables are decoded into the career catalog; character creation and custom careers consume their normalized fields. | `current-structural`: preserve class identities, constraints and source provenance. |
| CNT-011 | Classic item templates | Donor `ItemsFile` has 288 native `FALL.EXE` templates; no `FALL.EXE` is present in local Arena2. Donor `ItemBuilder` constructs instances by group/template index | The item-template ledger records the donor resource substitute and native source gap; normalized templates support item generation, equipment, enchantments and presentation. | `source-gap` for absent native FALL.EXE bytes; published substitute provenance remains explicit and is not a claim to have imported those bytes. |
| CNT-012 | Spells and magic definitions: `SPELLS.STD` and `MAGIC.DEF` | `SPELLS.STD` exists locally (7,921 bytes); `MAGIC.DEF` exists locally (3,662 bytes). Donor `ItemsFile.cs` explicitly describes an enchantment parameter as a `SPELLS.STD` spell ID | Both files are decoded and published as classic spell and magic definitions; spells, enchantments and effect policy consume normalized identities. | `current-structural`: magic behavior remains tracked by [magic-inventory.md](magic-inventory.md); SPELLS.STD is the actual supplied filename. |
| CNT-013 | Faction/social source: `FACTION.TXT` | 74,224 bytes; full record count not asserted | The 366-faction catalog publishes identities, hierarchy, relations, guild and social groups, and presentation references. Political, dialogue and guild services consume these definitions. | `current-structural`: publication and individual social mechanics are separate claims. |
| CNT-014 | Names, biographies and rumors: `NAMEGEN.DAT`, `BIO.DAT`, `RUMOR.DAT`, `BIOG00I0.IMG`, `BIOG00T0.TXT`–`BIOG17T0.TXT` | `NAMEGEN.DAT` 8,378 bytes, `BIO.DAT` 1,727 bytes, `RUMOR.DAT` 3,506 bytes; 1 biography image and 18 biography text files | Names, biographies and rumors are decoded into normalized catalogs, with authored character vocabulary and source-backed text consumers. | `current-structural`: retain source identities and macro interpretation at their owning layer. |
| CNT-015 | Books: `books/BOK*.TXT` | 90 supplied files, sparse IDs from `BOK00000.TXT` through `BOK00111.TXT`; donor `BookFile` reads headers/pages/tokens | All 90 supplied books publish headers, pages and tokens through the text owner; the book/notebook projection presents them. | `current-structural`: absent classic books remain not supplied; do not synthesize missing source files. |
| CNT-016 | Localized classic text: `TEXT.RSC` | 353,393 bytes; record count not asserted without the existing text reader | TEXT.RSC records and macros are decoded and published for dialogue, services, quests and other text consumers. | `current-structural`: retain record IDs and authored text interpretation rather than importing donor widgets. |
| CNT-017 | Classic quest source files: `.QBN` and `.QRC` | 306 QBN files and 303 QRC files; 302 case-insensitive stems are paired, four are QBN-only and one is QRC-only | Original QBN/QRC records are inventoried offline with pairing and selection dispositions. Runtime quests compile the donor rewritten text corpus and quest tables, with explicit original-source handoffs. | `current-partial`: original binary execution is not claimed. [quest-content-inventory.md](quest-content-inventory.md) owns action/resource/opcode scope. |
| CNT-018 | Texture leaves: `TEXTURE.000`–`TEXTURE.511` | 472 leaves present; absent IDs are 021, 032, 034, 051, 052, 078, 187–189, 191–193, 196, 219–232, 243–244, 294, 367, 373, 421, 441, 471–472 and 496–499 | World media publishes every supplied texture archive record product-wide; actor, model, terrain and seasonal presentation select shared normalized resources. | `current-structural`: retain not-supplied dispositions for the 40 absent archives; do not synthesize missing input. |
| CNT-019 | Weapon art: `WEAPON00.CIF`–`WEAPON11.CIF` | 12 CIF files | All twelve weapon CIFs are published by the classic media importer | `current-partial`: enumerate all 12 archives/records and map them to item/material/equipment presentation. |
| CNT-020 | Main/guild/misc UI and service art | `MAIN*.IMG` 6; `GILD*.IMG` 2; `BANK*.IMG` 4; `SHOP*.IMG` 9; `TALK*.IMG` 4; `REST*.IMG` 3; `INFO00I0.IMG` 1; `INVE*.IMG` 17 plus `INVE16I0.CIF` 1; `ITEM*.IMG` 2; `BOOK00I0.IMG` 1; `SCRL*` 12 (2 GFX and 10 IMG) | Current import selects four MAIN files plus INFO/INVE inputs and selected authored UI; guild/bank/shop/talk/rest/item/book/scroll families are not complete | `current-partial`: enumerate every file/record and bind it to thin DOM projections and semantic actions owned by the relevant gameplay task. Exact DFU window/widget topology is excluded. |
| CNT-021 | Character and NPC art | `BODY*` 32 IMG; `FACE00`–`FACE07` and `FACE10`–`FACE17` 16 CIF plus `FACES.CIF`; `CHAR*` 9 IMG; `CUST*` 10 IMG; `NITE*` 4 IMG; `SCBG*` 9 IMG; `CEL` 3 (`MAGE`, `ROGUE`, `WARRIOR`); `BSS` 3; `KIDS00I0.CIF` child escort portraits | Character presentation and character-media publications cover normalized body, portrait and creation art; world media supplies NPC sprites. Unsupported source families retain explicit inventory dispositions. | `current-partial`: inspect the generated character-media and source-coverage records for exact source/artifact membership; publication does not imply every source has a gameplay caller. |
| CNT-022 | World map, automap, travel and town art | `FMAP*.IMG` 54 plus `FMAP_PAL.COL`; `AMAP*.IMG` 2; `TMAP00I0.IMG` 1; `TRAV*` 12; `TOWN00I0.IMG` 1 | Automap and FMAP art are published as `map.*` UI entries; the travel map draws `TRAV0I00` and the region `FMAP` sheets from the session's art block, with the ruleset placing each sheet over the world map; the automap, town caption and remaining travel chrome stay unread | `current-partial`: enumerate regional/map/travel records and semantic actions; map art does not replace world/map data. |
| CNT-023 | Ordinary sound: `DAGGER.SND` | 459 numeric BSA clips (`0x0200` header), 7,661,766 bytes | `SoundArchive` inventories every source record; shared publication admits every nonempty clip through normalized media identities, retaining both directory ordinals for effects and numeric IDs for quest sound lookup. The empty Invalid record (ordinal 5) has no playable samples and is deliberately unpublished. Publication makes the remaining clips available; concrete gameplay triggers are described by their owning feature rows. The product-wide world media publication carries those clips once, under the one audio bundle the Host declares; the classic media group publishes no audio, and the importer's catalog of all 459 records is a `classic-media` report, not published content | `current-partial`: enumerate every clip ID/ordinal and usage/disposition, with ordinary runtime audio included in the target. |
| CNT-024 | Fonts: `FONT0000.FNT`–`FONT0004.FNT` | 5 files; current `FntDecoder` uses the fixed 240-glyph/32-byte record shape | All five font files have published source consumers; thin DOM text presentation remains the product UI owner | `current-partial`: enumerate all five glyph tables and their text/UI consumers. |
| CNT-025 | Original video/cinematic media: `ANIM0000.VID`–`ANIM0015.VID` and `DAG2.VID` | 17 VID files; `DAG2.VID` is retained as a distinct cinematic asset and is the last of the donor's new-game opening sequence, which plays `ANIM0000.VID`, then `ANIM0011.VID`, then `DAG2.VID`; the remaining fourteen files are not yet bound to a caller | All 17 VID files are published as cinematics; the new-game opening sequence plays them through Engine video, and the remaining fourteen have no caller | `current-partial`: preserve metadata and story hooks for all 17 files, including explicit story/ending bindings where the donor calls for them; raw media shipping is a separate product decision. The new-game opening sequence is a resolved binding and is a cinematic rather than a published UI screen, so no screen mode owns it. |
| CNT-026 | Daedric/artifact cinematic media: `AZURA.FLC`, `BOETHIAH.FLC`, `CLAVICUS.FLC`, `HERMAEUS.FLC`, `HIRCINE.FLC`, `MALACATH.FLC`, `MEHRUNES.FLC`, `MEPHALA.FLC`, `MERIDIA.FLC`, `MOLAGBAL.FLC`, `NAMIRA.FLC`, `NOCTURNA.FLC`, `PERYITE.FLC`, `SANGUINE.FLC`, `SHEOGRTH.FLC`, `VAERNIMA.FLC` | 16 FLC files | All sixteen FLC files are converted and published as cinematics with faction and artifact/story binding metadata; runtime callers resolve those normalized resources. | `current-structural`: retain each source identity and binding; conversion alone does not certify every quest branch. |
| CNT-027 | Remaining classic media and tables | Root has 70 CIF, 263 IMG, 2 GFX, 3 CEL, 3 BSS, 6 RCI, 4 COL, 3 LGT, 3 RAW and other DAT/DEF/TDE/RSC/TBL files; exact per-family list is in the manifest | Only selected inputs are admitted | `uninspected`: enumerate each remaining family with an existing decoder or documented unsupported-source disposition. This row must not become a generic “all media” ticket; retain path-level IDs in the manifest. |
| CNT-028 | MIDI source: `MIDI.BSA` | 131 named records (`0x0100` header), 1,232,240 bytes | No runtime use | `excluded`: no MIDI decoder, synthesizer or `MIDI.BSA` playback. Ordinary audio and long-duration music looping remain in scope under CNT-023 and the coverage plan. |

### Ordinary source sound consumers

The shared audio manifest retains source directory ordinals and numeric IDs; gameplay
uses the former and quest `play sound` uses the latter. Door open/close/bash,
accepted-distance footsteps (dungeon, wood, outdoors and winter snow), swimming
strokes and water entry, player pain, damaging and hard landings, ranged spell
impacts, newly available levels, guard arrest and item equipment changes use their
existing gameplay owners. Clips are opened and retired by the Engine audio service.
Footstep distance/volume and player pain probability are typed presentation tuning.
Restored state and ordinary projection reads do not replay these event cues.

Publication alone does not assign a gameplay trigger. Arena-prefixed remnants,
unnamed records and alternate cries without a retained donor call remain available
for explicit quest numeric references but have no invented ambient/gameplay trigger.
In particular `PlayerSwimming` (312) is not a replacement for the donor's splash
stroke (346); `HaltWeak` (302) and `Halt2` (457) do not replace the watch's `Halt`
(456). Book, crafting and gold UI cues remain unbound as recorded by F116. Empty
ordinal 5 is the only clip omitted from publication because it contains no samples.
The generated classic-media sound catalog lists every record's identity and whether
its bytes are published; it is not a claim that every record has an automatic trigger.

## Regions and map records

`MAPS.BSA` contains four named tables per region. The donor `MapsFile` source provides the
following stable region index/name mapping; indices are source identities and should survive
normalization. Location and map counts inside `MAPNAMES`/`MAPTABLE` are not asserted here
because this pass did not run a new binary parser. The existing `MapsDecoder` should emit those
records as a bounded manifest task.

| Index | Region | Index | Region | Index | Region |
| ---: | --- | ---: | --- | ---: | --- |
| 0 | Alik’r Desert | 21 | Anticlere | 42 | Shalgora |
| 1 | Dragontail Mountains | 22 | Lainlyn | 43 | Abibon-Gora |
| 2 | Glenpoint Foothills | 23 | Wayrest | 44 | Kairou |
| 3 | Daggerfall Bluffs | 24 | Gen Tem High Rock village | 45 | Pothago |
| 4 | Yeorth Burrowland | 25 | Gen Rai Hammerfell village | 46 | Myrkwasa |
| 5 | Dwynnen | 26 | Orsinium Area | 47 | Ayasofya |
| 6 | Ravennian Forest | 27 | Skeffington Wood | 48 | Tigonus |
| 7 | Devilrock | 28 | Hammerfell bay coast | 49 | Kozanset |
| 8 | Malekna Forest | 29 | Hammerfell sea coast | 50 | Satakalaam |
| 9 | Isle of Balfiera | 30 | High Rock bay coast | 51 | Totambu |
| 10 | Bantha | 31 | High Rock sea coast | 52 | Mournoth |
| 11 | Dak’fron | 32 | Northmoor | 53 | Ephesus |
| 12 | Islands in the Western Iliac Bay | 33 | Menevia | 54 | Santaki |
| 13 | Tamarilyn Point | 34 | Alcaire | 55 | Antiphyllos |
| 14 | Lainlyn Cliffs | 35 | Koegria | 56 | Bergama |
| 15 | Bjoulsae River | 36 | Bhoriane | 57 | Gavaudon |
| 16 | Wrothgarian Mountains | 37 | Kambria | 58 | Tulune |
| 17 | Daggerfall | 38 | Phrygias | 59 | Glenumbra Moors |
| 18 | Glenpoint | 39 | Urvaius | 60 | Ilessan Hills |
| 19 | Betony | 40 | Ykalon | 61 | Cybiades |
| 20 | Sentinel | 41 | Daenia |  |  |

The donor also identifies RMB block prefixes (`TVRN`, `GENR`, `RESI`, `WEAP`, `ARMR`, `ALCH`,
`BANK`, `BOOK`, `CLOT`, `FURN`, `GEMS`, `LIBR`, `PAWN`, `TEMP`, `TEMP`, `PALA`, `FARM`, `DUNG`, `CAST`,
`MANR`, `SHRI`, `RUIN`, `SHCK`, `GRVE`, `FILL`, `KRAV`, `KDRA`, `KOWL`, `KMOO`, `KCAN`,
`KFLA`, `KHOR`, `KROS`, `KWHE`, `KSCA`, `KHAW`, `MAGE`, `THIE`, `DARK`, `FIGH`, `CUST`, `WALL`,
`MARK`, `SHIP`, `WITC`) and RDB block letters `N`, `W`, `L`, `S`, `B`, `M`. The block source
manifest must retain these source names and resolve duplicate `TEMP` entries by block index.

## QBN/QRC identity boundary

The local root has 306 QBN files and 303 QRC files. Case-insensitive stem comparison yields
302 paired stems:

- QBN-only: `80C00Y00`, `A0C00Y04`, `N0C00Y01`, `R0C40Y23`.
- QRC-only: `M0B40Y04`.
- Six QRC paths use lowercase extension and must retain their actual source path while their
  stable comparison ID is case-normalized: `N0B20Y25.qrc`, `N0B40Y22.qrc`, `N0B50Y20.qrc`,
  `N0B60Y24.qrc`, `N0B70Y21.qrc`, `N0C00Y23.qrc`.

The companion manifest contains every QBN/QRC filename and stem. CNT-017 owns source identity,
pairing and disposition only. `docs/coverage/quest-content-inventory.md` owns quest action,
resource and runtime-source inventories; content tasks should link the two IDs instead of
silently treating a paired filename as an implemented quest.

## Books, text and story presentation

The 90 local `books/BOK*.TXT` files are source identities, not proof that every classic book
message maps to a supplied file. A book task must use the existing donor `BookFile` semantics
(header, page count and token records), enumerate the message-to-file mapping and record absent
or malformed files. It must not paste book prose into this inventory.

`TEXT.RSC`, `BIOG*.TXT`, `FACTION.TXT`, `RUMOR.DAT`, `NAMEGEN.DAT`, `BIO.DAT`, `MAGIC.DEF` and
`SPELLS.STD` are separate source families. Their record counts and cross-references remain
task-local metadata work. In particular, `SPELLS.STD` is the verified classic spell filename;
`SPELL.RSC` is not present in the supplied source and must not appear as an assumed input.

Original presentation is part of the authored scope. `DAG2.VID` remains a distinct cinematic
identity alongside the 16 `ANIM*.VID` files; file presence alone does not establish
which scene is an ending. Resolve narrative bindings from the retained callers and state the resolved
ones here rather than leaving a filename guess. One binding is resolved: the donor's
`StartNewGameWizard.StartNewGame` opens a new game by playing `ANIM0000.VID`, `ANIM0011.VID` and
`DAG2.VID` in that order, where each cinematic ends at end of file or on its own skip input
(`DaggerfallVidPlayerWindow`) and the last one's completion starts the game; with videos disabled the
sequence is skipped and the game starts at once. That opening is a cinematic sequence rather than a
published screen, so no UI mode owns an intro identity. The 16 named Daedric FLC files remain explicit
artifact/cinematic identities. The main, guild and miscellaneous image groups in CNT-020 are
also required source families even where current runtime publication has no corresponding UI
surface. The implementation target is semantic content and thin DOM presentation; Unity window
classes, widget layouts, distribution helpers and runtime media bootstrap are excluded.

## Current baseline and missing work

The current importer has focused Arena2 decoders for named/numeric BSA archives, maps, RDB
blocks, ARCH3D meshes, PAK grids, texture leaves, weapon CIFs, fonts and DAGGER.SND. It uses
fixed source lists in `src/Daggerfall.Import/Publication/Arena2SiteSources.cs`; this is why the 68-source manifest
does not represent the whole local corpus. Current output is a normalized Privateer’s Hold
closure and selected classic media, not a complete Daggerfall content pack.

The planning expansion is therefore finite and record-oriented:

1. Add source manifests using the IDs here and the companion CSV, preserving source path,
   archive kind, ordinal/numeric ID or filename, and a disposition for every discovered record.
2. Extend offline import for maps/locations, RMB/RDB blocks, outdoor terrain, climate/political
   grids, actor/media links, races/careers/items, factions, text/books, spells and QBN/QRC
   source identities. Runtime packs consume normalized records; they do not open Arena2 files.
3. Publish and consume the full required records through the existing bundle/ruleset/content
   owners. Content-family completion requires a pack consumer and any required save/UI binding;
   an individual decoder task may complete its own contract before those later consumers land.
4. Give CNT-020–CNT-026 semantic consumers and story/media lifecycle tasks, including main,
   guild, misc, book, ending and artifact presentation. Keep raw media provenance separate from
   whether a supported runtime format can be published.
5. Link CNT-017 to the quest inventory for each retained action/resource family. Do not create a
   second quest action list here.

## Caveats and explicit exclusions

- The supplied Arena2 directory is not proof of the complete retail/install corpus. `FALL.EXE`
  is absent, so the donor’s 288 native item-template count is a target/source-gap fact rather
  than a local payload count. Missing books, international text variants, optional source
  archives and malformed records require explicit dispositions when encountered.
- Archive headers establish the BSA record counts above; they do not establish semantic
  usability, deduplication, map reachability or publication completeness. Do not infer total
  location, dungeon, item, spell or text records from file size.
- QBN/QRC comparisons are case-insensitive for pairing but preserve actual path casing for
  provenance. The five unpaired stems must remain visible until a task resolves them.
- Unity content readers, `ContentReader` setup, Arena2 application-path configuration, Unity
  serialization/import/render bridges, donor singleton orchestration, editor/mod/distribution
  helpers and MIDI playback are excluded. Their content facts may be cited as provenance while
  normalized Rusty packs and existing Engine/product owners carry runtime behavior.
- No raw media or copyrighted prose needs to be copied into the repository for this planning
  artifact. A later media task must name the accepted runtime format, conversion/provenance and
  publication boundary.

## Generated and authored content

The game's files, the media extracted from them and the bulk generated payloads are not committed
and not redistributed; small derived facts (identifiers, names, numbers, short text, record JSON)
in tests, fixtures and documents are fine (`AGENTS.md`). Git tracks `content/` (descriptors,
hand-authored payloads, tuning and the authored UI art under `worldrpg/media/ui/authored/`) and the
importer's own inputs under `data/`; an architecture law fails a run that finds an untracked file under
`content/`. Everything derived from the operator's files is built on each checkout by one cross-platform
command, the import tool's `import`
(`dotnet run --project src/Daggerfall.Import.Tool -- import --music local/Sound`). It reads `local/arena2`
(or `--arena2`/`DAGGER_ARENA2`), the Daggerfall Unity checkout (`--donor`/`DAGGER_DONOR_ROOT`), an
optional music folder (`--music`) and FFmpeg, names every input that is absent before writing anything,
clears its staging tree, and runs the import tool's commands in their dependency order: classic media,
the imported payload's sections with the block document and the import records, the quest corpus
payloads, the product-wide world media publication, the per-block world publication, the site closures,
then the source coverage. It writes into `local/import/staging/` (at the content-root-relative paths the
product names) and `local/import/records/`, describes the run in `worldrpg/data-container.json` (the
importer revision, a hash over the importer's sources and `data/`, and the derived packs' descriptors), and
packs `local/daggerfall-data.rpak` from the staging tree and, with music, `local/music.rpak`: one
recording per track at its root, named `<track>.<ext>` by the classic track identifier, in any format the
Engine's audio opens, beside its own `worldrpg/data-container.json` (the importer revision, no packs).
`--only STEP,...` reruns named steps over the existing staging tree and repacks. Each command's staging and
record options default to the paths the import gives them, so a command run alone from the repository root
(`locations --arena2 local/arena2 --update`) updates the staging tree, and `pack` repacks it. Two runs at the
same importer source and FFmpeg build write identical bytes.

The product opens both containers beside itself (`local/` under `rusty dev`, the directory of
`product.rpak` in a release) and resolves the derived packs over them. Without the data container, with a
damaged one, or (in a release) with one another importer revision wrote, the entry screen states the
problem and the step that fixes it and offers nothing to play; under `rusty dev` a stale container is a
warning and play continues. A clone that has not imported builds, stages and runs the content-free suites;
`scripts/verify.sh` skips the ruleset suite with the reason printed and refuses `--play`, and the
content-dependent tests report themselves skipped.

Generated, by its path in the data container (`local/daggerfall-data.rpak`) unless noted:

- `worldrpg/imports/shared/` — the product-wide world media publication (`world-media`):
  every mesh the classic mesh archive serves, a material texture and a billboard atlas for every
  usable record of every supplied texture leaf, actor media for every mobile the imported `mobiles`
  catalog lets the runtime spawn, the runtime terrain textures, the classic sidecar with its audio
  clips (below `worldrpg/imports/shared/media/audio/clips`) and missile world visuals, and its own
  `import-manifest.json`. It is published once and scales to any number of closures.
- `worldrpg/imports/**` (other directories) — the Privateer's Hold, Castle Necromoghan,
  cemetery, tomb, ship and Charing exterior/interior closures (`write`, `rmb-spatial`, with
  `--shared` naming the world media publication). Each carries only what is its own: the normalized
  document, spatial and resource artifacts, its geometry index and a dungeon sidecar that binds its
  material slots and names the product-wide entries it admits.
- `worldrpg/media/**` — classic media, character media, the paper doll's worn-item images, the sky
  and cinematics (`classic-media`, `character-presentation`, `paper-doll-media`, `sky-media`,
  `cinematic-media`). The classic inventory lists the authored UI kit too, whose images are tracked
  product content at the same paths. The classic group carries no audio; the world media publication and the site
  closures reference its images. `paper-doll-media` writes `media/character/paper-doll/`: every
  TEXTURE record a worn-item template reaches (by gender and body morphology, every variant, a cloak's
  interior, a one-handed weapon's right-hand record) in every dye it can be drawn in, with mask index
  0xFF transparent and a separate mask image where a record has one, plus every record of the male and
  female artifact archives (TEXTURE.432/433), undyed, which artifacts are drawn from.
- `worldrpg/payloads/daggerfall.imported.json` — the `daggerfall.imported` pack's payload:
  `catalogs`, `itemTemplateLedger`, `characterPresentation`, `locations`, `magic`, `mobiles`, `text`,
  `names`, `rumors`, `biographies`, `books`, `climate`, `politic`, `factions`, `terrain`,
  `itemTemplates`, `paperDoll`, `questTables`, `questCatalog`, `questSources`, `cinematics` and `buildingNames`.
  `characterPresentation` places each body and head in the 110x184 paper doll (its header offset less
  the donor's paper-doll origin) and states the doll's frame and background crop; `paperDoll` names
  each worn-item record's image per dye, its placement and its mask.
  The `locations` section states each location's `climate` (the CLIMATE.PAK value at its own map pixel,
  as `MapsFile` reads a location's climate) and, for each dungeon, which block placement is the `start`
  block and its classic `textureTable` (DEC-11.dungeon-textures: the six wall/floor archives and the door
  offset, seeded from `dungeonLocationId` and the location's climate, with `remaps` stating every archive
  the table redraws and the archive it is drawn from). The `climate` section states each
  value's `climateBase` and the exterior climate/season texture `swaps` (`ClimateSwaps.ApplyClimate`:
  every archive and record range a climate base or season draws from another archive). A per-location
  assembly reads these with the per-block facts below.
  The `catalogs` section includes the forty class questions from TEXT.RSC resource 9000,
  their donor answer archetypes and the sixty-six CLASSES.DAT recommendation rows. The ruleset
  consumes these normalized values in the existing character draft; it never opens source files.
  The `magic` section includes `enchantmentSettings`: the retained item-maker families transcribed
  in the importer from the donor effect classes, with costs, display/text keys, parameter variants
  and source-class provenance. Runtime enchantment consumers resolve this loaded catalog.
- `worldrpg/payloads/daggerfall.blocks.json` (`blocks --buildings`: the type, faction and
  name seed of every RMB building slot, which is all the `daggerfall.blocks` pack's runtime reader
  needs) and `worldrpg/payloads/daggerfall.quests.*.json` (`fighters-quest-corpus`,
  `classic-quest-corpora`).
- `local/import/records/` (not packed) — importer records nothing at runtime reads, kept outside the
  data container: `daggerfall.import-records.json` (the mesh inventory `geometry` and the original quest-source
  selections `questOriginalSources` the quest corpus payloads are built from),
  `daggerfall.blocks.json` (`blocks --document`: the complete block document, which `geometry` reads
  for mesh use sites), `sites/SITE.sources.json` (each site closure's source manifest: the supplied
  corpus scanned against the documented inventory, written by `write` and `rmb-spatial` through
  `--source-manifest`), `world-media.sources.json` (the same for the world media publication) and
  `world-blocks.sources.json` (the same for the per-block world publication).
- `worldrpg/imports/world-blocks/` — the per-block world publication (`world-blocks`): normalized facts for every RMB exterior
  (658), every building interior those blocks declare (4,521) and every RDB block (187) a location places,
  each in its own frame. A block document (`rmb/BLOCK/exterior.json`, `rmb/BLOCK/interior-N.json`,
  `rdb/BLOCK.json`) places the world media publication's meshes by `geometry/mesh-N` id and states the
  block's doors, start/enter and quest markers, people, furniture, lights, billboards, fixed mobiles,
  treasure and random-enemy markers (an enemy marker's encounter slot, spawn distance and reaction, and
  its action node when it is one), action graph, ground tiles, clear automap cells, RDB water level and
  ambient area. An RDB model also states what a placing location cannot derive without the mesh bytes:
  a static placement's block-frame bounds and map visibility samples, and a moving placement's
  model-local bounds and, for an action model, its model-local collision triangles. The doors that change
  worlds are typed as the donor's MeshReader types their planes (`transitionDoors`: a building door seen
  from an interior leads out, a dungeon entrance plane on an RMB exterior leads in, a dungeon exit plane in
  an RDB block leads out), each with its plane's centre, normal and extent. An RMB exterior states every
  start marker it places (`startMarkers`) and its city gates (`gates`, ARCH3D 446 open and 447 closed): a
  gate's planes are left out of the block's static collision and each of its states carries its mesh and
  model-local collision. Its `.rspatial`
  beside it is the block's collision and navigation in the Engine's binary form, ready to place beside its
  neighbours by whole navigation cells. `blocks.json` indexes every block and names the RMB records no
  location places. Identities are the site closures' with the grid position left out (`door/BLOCK-rmb/N`
  where a site says `door/BLOCK-rmb/X/Y/N`), and a location supplies its position, climate, texture
  table, start block and dungeon type. The site closures' normalizers read each block through the same
  block-level content. Hundreds of megabytes in thousands of files, nothing of it is read at startup: the
  ruleset reads the index and the documents of the blocks one location places when that location is
  first needed.

The base definitions come from two packs. `daggerfall.base` is authored and tracked: its payload
`content/worldrpg/payloads/daggerfall.base.json` carries `ruleset`, `vocabulary`,
`armorValuesByMaterial`, `actors`, `items`, `equipmentSlots`, `actions`, `lootTables`,
`hudResources`, `donorErrata`, `questJournalTitles` (journal titles over a quest's internal donor
name), `encounters` and `assembledSites` (the world
appearance and navigation grid every location assembled from its blocks shares, and how near its
transition doors a player must stand and how far in front of a door they land on coming out; some sections are
transcribed from the donor by hand, each citing its source); no command writes it. `daggerfall.imported` is generated. The
ruleset's base reader joins the two payloads section by section and refuses a section both carry.
The commands that build from authored sections (`catalogs` reads the vocabulary, actors and items;
`item-template-ledger` the items; `mobile-catalog` and `mobile-ledger` the actors) take the
authored payload as `--authored` and write only the imported one (`--pack`).

The `mobiles` section carries the donor's loot table keys as `EnemyBasics` states them, including the
City Watch (146), which has none: its corpse generates no table loot, as the donor's empty key selects
the all-zero loot matrix.

Authored and tracked: the pack, bundle and tuning descriptors (`content/worldrpg/content-packs/`,
`content/worldrpg/bundles/`, `content/worldrpg/tuning/`), the tuning payloads
(`content/worldrpg/tuning-payloads/`), the authored base payload, the site payloads
(`content/worldrpg/payloads/daggerfall.{privateers-hold,castle-necromoghan,charing-exterior,charing-interior-1-1-0}.json`
and the other site payloads beside them), the authored UI art (`content/worldrpg/media/ui/authored/`) and the importer's tracked inputs. A site payload states the profile id
it overrides (`world.profile`, for example `17/4/exterior` or `17/4/interior-3-4-0`) and the ruleset
uses its closure for that id only. Its `world.transitions` are its portals; a closure states its source exits
there (the door planes the per-block publication types), with `normal` naming the side of the plane it is used
from and `arrivalAnchor` naming the destination landing used when there is no entrance to return through. A
portal with a `normal` is a Kit surface target: interaction sights it just off its plane, from that side only. Every other location profile is assembled from the per-block
publication when first needed. The tracked importer inputs are the source inventory and `data/ui-authored-assets.json`,
which describes the authored UI art the importer lists in the classic inventory. No sprite overlay is tracked; `--sprite-authoring DIR` applies an
operator's `sprites/SITE.json` overlays to the dungeon sites. Third-party origins are in
`THIRD_PARTY_NOTICES.md`.

The Host stages the whole `content/` directory as the runtime content root, and nothing in it is
copied per site. A site closure names a shared artifact by the same closure-relative path it would
have carried; the ruleset resolves such a path in the site's own `import-manifest.json`, then in the
world media publication's, then in the classic media group's inventory, and verifies the digest
there. The importer builds each site's complete closure in memory, then writes only what differs
from the product-wide media: a body the product-wide media carries with other bytes, or a body it does
not carry at all, stops the write rather than being copied. Authored sprite overlays stay per site:
an overlaid descriptor, billboard or actor entry differs from the product-wide one, so the site keeps
that entry (and a classic sidecar that differs) in its own closure, while the atlas bytes, which an
overlay never changes, stay shared. The sprite tools read a site closure together with the
product-wide media (`--shared`, `--classic-group`) and see the same complete closure the site's
regeneration validates overlays against.

Every published source path is written in one vocabulary (`PublishedSourcePath`): an Arena2 file is
`arena2/NAME` and a Daggerfall Unity file `daggerfall-unity/PATH`, as the documented inventory spells
its rows. A section states the files it read as `PublishedSource` values (path, digest, byte length),
and catalogs cite the documented path a record came from; the packs carry no inventory row ids.
The importer's records and the ruleset's readers are separate code, because the runtime ruleset may
not reference the offline importer; `NormalizedContractRoundTripTests` in the ruleset suite reads
every imported section and site closure document strictly through the importer's record, rewrites it
with the importer's writer, and has the ruleset read the result.

Each site closure's `import-manifest.json` records its provenance: `importerRevision` (the last
commit that changed `src/Daggerfall.Import`, `src/Daggerfall.Import.Tool` or
`src/WorldRpg.SpriteAuthoring`, suffixed `-dirty` for uncommitted changes there), `command` (the tool
command line that writes it, which names the import record its source manifest went to), `sources`,
and `authoredOverlays` (each applied sprite overlay with
its digest). `normalized.json` carries the same revision. Each cinematic artifact in the imported
payload records the FFmpeg build that encoded it, because another build writes different VP9/Opus
bytes for the same source.

### Contextual music publication

The music catalogue publishes operator-supplied Ogg tracks for retained standard and alternate
dungeon, outdoor day/night/weather, tavern, shop, guild, temple-alignment, palace and ordinary
interior playlists. A tavern follows the shared calendar day's song. Combat does not interrupt
the environmental score, matching the donor song manager; there is no invented battle track
or MIDI playback. Only the currently playing music clip remains open, and its voice retires
before the clip is released. Publication size is not an Engine live-resource budget.

The source folder's malformed `song_dungeon5).ogg` is deliberately omitted: it does not name
the donor track `song_dungeon5`. Other dungeon tracks remain available. Unused donor playlists
and tracks outside the retained contexts are not added solely because files exist.
