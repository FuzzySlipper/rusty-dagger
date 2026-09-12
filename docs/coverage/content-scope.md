# Daggerfall authored-content scope

Prepared 2026-09-10 as an input to [the Daggerfall coverage plan](../daggerfall-coverage-plan.md)
and [the task-creation packet](../daggerfall-task-preparation.md). This document inventories
the authored corpus and its current Rusty Dagger boundary. It is a planning inventory, not a
binary dump, a runtime source reader, or a claim that every listed record already imports.

The target is the supported classic game record set needed by the playable Daggerfall world:
world and location data; actors, races, careers, items and effects; text and books; main,
guild and miscellaneous services/presentation; quests; ordinary sound and story media. A
source file receives a planning disposition in the table below or in the companion
[source manifest](content-source-manifest.csv); archive-internal record dispositions
are assigned by the relevant import/publication tasks. “All content” is not a disposition.

## Evidence and boundaries

The local source corpus was inspected read-only at `/home/dev/rusty-dagger/local/arena2` on
2026-09-10. It contains 1,590 files at its root and 90 files under `books/`, occupying about
517 MiB. Counts below are filesystem counts or archive-header counts unless explicitly marked
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

Current Rusty Dagger content is under `content/worldrpg/` and `src/Daggerfall.Import/`. The
published baseline is the `daggerfall.base` pack plus one `daggerfall.privateers-hold` pack.
The current import manifest is
`content/worldrpg/imports/privateers-hold/import-manifest.json` (68 source inputs and 189
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
| CNT-001 | `local/arena2/MAPS.BSA` (published input path `arena2/MAPS.BSA`) | 248 named records: 62 region groups × `MAPPITEM`, `MAPDITEM`, `MAPTABLE`, `MAPNAMES`; donor `MapsFile.RegionCount` derives the same 62 groups | `MapsDecoder` can read a bounded region/location set; the published world is only Privateer’s Hold | `current-structural`: enumerate every region, location, map ID/type, coordinates, blocks and discovery binding; publish normalized region/location records. |
| CNT-002 | `CLIMATE.PAK` | 14,771 bytes; existing `PakDecoder` knows the 1001 × 500 grid shape including sentinel cells | Selected Privateer’s Hold climate data only | `current-partial`: enumerate required climate cells and region/terrain interpretation; keep source-format decoding in `Daggerfall.Import`. |
| CNT-003 | `POLITIC.PAK` | 19,597 bytes; same PAK family and grid capacity | No current admitted source in the privateer import manifest | `pending-import`: enumerate political/faction map cells and their consumers. |
| CNT-004 | `WOODS.WLD` | 26,001,168 bytes; record count not asserted without an existing wilderness decoder | No current importer or published wilderness pack | `pending-import`/`uninspected`: inventory wilderness records, terrain transitions, seasonal data and references to maps/regions. |
| CNT-005 | `BLOCKS.BSA` | 1,295 named BSA records (`0x0100` header) | `RdbDecoder` and `DungeonNormalizer` handle bounded RDB facts and the Privateer’s Hold closure; RMB is not corpus-complete | `current-structural`: enumerate all RDB and RMB records, preserve block kind/letter/number, referenced objects, doors, triggers, textures and placements. |
| CNT-006 | `ARCH3D.BSA` | 10,251 numeric BSA records (`0x0200` header) | Meshes referenced by Privateer’s Hold are normalized; no all-world closure | `current-partial`: map every required mesh record to its normalized geometry/material references and disposition unused/duplicate records explicitly. |
| CNT-007 | `MONSTER.BSA` | 103 named BSA records (`0x0100` header), including `ASCR*.ANC` and `ENEMY*.CFG` families | Seven dungeon actor media entries; base payload has 45 actor definitions | `current-partial`: enumerate all archive records and links from actor definitions, animations, corpses, sounds and loot. |
| CNT-008 | Classic mobile actor definitions | Donor `EnemyBasics.Enemies` contains 63 static definitions; `MONSTER.BSA` is a separate source candidate, not proof of missing runtime fields | A smaller authored actor catalog is published in `daggerfall.base.json`; static `MobileSourceMetadata` is not a complete archive import | `current-partial`: reconcile every supported classic actor identity, authored combat/map/loot metadata and source media to one Daggerfall ruleset/content record. |
| CNT-009 | Race table | Donor `RaceTemplate.GetRaceDictionary` defines 8 races: Breton, Redguard, Nord, DarkElf, HighElf, WoodElf, Khajiit and Argonian | No complete race/content catalog is published | `pending-import`: preserve race identity, attributes, skills, resistances and presentation references; verify any classic source table used by the chosen import. |
| CNT-010 | Career/class records: `CLASS00.CFG`–`CLASS18.CFG` and `CLASSES.DAT` | 19 CFG files, each 74 bytes; `CLASSES.DAT` is 216 bytes. Donor `ClassFile` and `DFCareer` identify the class fields and career semantics | No complete career/class import | `pending-import`: enumerate all 19 class records, names/descriptions and custom-career constraints; connect to race, progression and starting loadout tasks. |
| CNT-011 | Classic item templates | Donor `ItemsFile` has 288 native `FALL.EXE` templates; no `FALL.EXE` is present in local Arena2. Donor `ItemBuilder` constructs instances by group/template index | Base payload has 31 item definitions and current inventory/equipment coordination | `source-gap` for the native template bytes, then `pending-import`: obtain an authorized source or explicit substitute provenance; enumerate all supported template records, materials, variants, enchantments, condition, stack and ownership fields. |
| CNT-012 | Spells and magic definitions: `SPELLS.STD` and `MAGIC.DEF` | `SPELLS.STD` exists locally (7,921 bytes); `MAGIC.DEF` exists locally (3,662 bytes). Donor `ItemsFile.cs` explicitly describes an enchantment parameter as a `SPELLS.STD` spell ID | No current importer for either file | `pending-import`: enumerate spell/effect records and links. The verified filename is `SPELLS.STD`; there is no local `SPELL.RSC`, so planning must not invent that path. Magic behavior/action leaves belong to `docs/coverage/magic-inventory.md`. |
| CNT-013 | Faction/social source: `FACTION.TXT` | 74,224 bytes; full record count not asserted | No current normalized faction catalog | `pending-import`: enumerate faction identities, relations, temple/guild bindings and text references; connect political map cells and social services. |
| CNT-014 | Names, biographies and rumors: `NAMEGEN.DAT`, `BIO.DAT`, `RUMOR.DAT`, `BIOG00I0.IMG`, `BIOG00T0.TXT`–`BIOG17T0.TXT` | `NAMEGEN.DAT` 8,378 bytes, `BIO.DAT` 1,727 bytes, `RUMOR.DAT` 3,506 bytes; 1 biography image and 18 biography text files | No complete importer/publication | `pending-import`: preserve lookup identities, macros and authored text references; connect names/biography/rumors to character, dialogue, faction and quest records. |
| CNT-015 | Books: `books/BOK*.TXT` | 90 supplied files, sparse IDs from `BOK00000.TXT` through `BOK00111.TXT`; donor `BookFile` reads headers/pages/tokens | No current book catalog or reader projection | `pending-import`: enumerate every supplied book file, header/page metadata and message mapping; publish text through the Daggerfall text owner without shipping a raw dump. Missing classic books outside this directory remain unresolved. |
| CNT-016 | Localized classic text: `TEXT.RSC` | 353,393 bytes; record count not asserted without the existing text reader | No current `TEXT.RSC` importer | `pending-import`: enumerate text record IDs/macros and consumers, including spell, career, item, book, dialogue, service and quest references. |
| CNT-017 | Classic quest source files: `.QBN` and `.QRC` | 306 QBN files and 303 QRC files; 302 case-insensitive stems are paired, four are QBN-only and one is QRC-only | No current QBN/QRC import in Rusty Dagger | `pending-import`: preserve each source identity and explicit mismatch disposition. Action/resource/opcode inventory is owned by [quest-content-inventory.md](quest-content-inventory.md), not duplicated here. |
| CNT-018 | Texture leaves: `TEXTURE.000`–`TEXTURE.511` | 472 leaves present; absent IDs are 021, 032, 034, 051, 052, 078, 187–189, 191–193, 196, 219–232, 243–244, 294, 367, 373, 421, 441, 471–472 and 496–499 | `TextureArchiveDecoder` and dungeon normalization admit a selected Privateer’s Hold closure plus selected classic media | `current-partial`: enumerate all 472 present leaves and referenced frames, retaining “not supplied” for the 40 absent IDs; do not synthesize missing archives. |
| CNT-019 | Weapon art: `WEAPON00.CIF`–`WEAPON11.CIF` | 12 CIF files | Nine weapon CIFs are admitted by current classic media import; 00, 03 and 11 remain outside it | `current-partial`: enumerate all 12 archives/records and map them to item/material/equipment presentation. |
| CNT-020 | Main/guild/misc UI and service art | `MAIN*.IMG` 6; `GILD*.IMG` 2; `BANK*.IMG` 4; `SHOP*.IMG` 9; `TALK*.IMG` 4; `REST*.IMG` 3; `INFO00I0.IMG` 1; `INVE*.IMG` 17 plus `INVE16I0.CIF` 1; `ITEM*.IMG` 2; `BOOK00I0.IMG` 1; `SCRL*` 12 (2 GFX and 10 IMG) | Current import selects four MAIN files plus INFO/INVE inputs and selected authored UI; guild/bank/shop/talk/rest/item/book/scroll families are not complete | `current-partial`: enumerate every file/record and bind it to thin DOM projections and semantic actions owned by the relevant gameplay task. Exact DFU window/widget topology is excluded. |
| CNT-021 | Character and NPC art | `BODY*` 32 IMG; `FACE00`–`FACE07` and `FACE10`–`FACE17` 16 CIF plus `FACES.CIF`; `CHAR*` 9 IMG; `CUST*` 10 IMG; `NITE*` 4 IMG; `SCBG*` 9 IMG; `CEL` 3 (`MAGE`, `ROGUE`, `WARRIOR`); `BSS` 3 | Current publication contains selected dungeon actor media and selected authored UI, not the full character/social set | `pending-import`/`uninspected`: preserve source identity and determine which records are required for race, gender, face, class, NPC, faction and story presentation. |
| CNT-022 | World map, automap, travel and town art | `FMAP*.IMG` 54 plus `FMAP_PAL.COL`; `AMAP*.IMG` 2; `TMAP00I0.IMG` 1; `TRAV*` 12; `TOWN00I0.IMG` 1 | No complete world/travel presentation import | `pending-import`: enumerate regional/map/travel records and semantic actions; map art does not replace world/map data. |
| CNT-023 | Ordinary sound: `DAGGER.SND` | 459 numeric BSA clips (`0x0200` header), 7,661,766 bytes | `SoundArchiveDecoder` admits the archive; publication currently exposes 6 typed clips for combat interactions | `current-partial`: enumerate every clip ID/ordinal and usage/disposition, with ordinary runtime audio included in the target. |
| CNT-024 | Fonts: `FONT0000.FNT`–`FONT0004.FNT` | 5 files; current `FntDecoder` uses the fixed 240-glyph/32-byte record shape | Only `FONT0003.FNT` is in the current classic media input | `current-partial`: enumerate all five glyph tables and their text/UI consumers. |
| CNT-025 | Original video/cinematic media: `ANIM0000.VID`–`ANIM0015.VID` and `DAG2.VID` | 17 VID files; `DAG2.VID` is retained as a distinct cinematic asset; its exact narrative binding remains to be established | No current video import or presentation path | `pending-import`: preserve metadata and story hooks for all 17 files, including explicit story/ending bindings where the donor calls for them; raw media shipping is a separate product decision. |
| CNT-026 | Daedric/artifact cinematic media: `AZURA.FLC`, `BOETHIAH.FLC`, `CLAVICUS.FLC`, `HERMAEUS.FLC`, `HIRCINE.FLC`, `MALACATH.FLC`, `MEHRUNES.FLC`, `MEPHALA.FLC`, `MERIDIA.FLC`, `MOLAGBAL.FLC`, `NAMIRA.FLC`, `NOCTURNA.FLC`, `PERYITE.FLC`, `SANGUINE.FLC`, `SHEOGRTH.FLC`, `VAERNIMA.FLC` | 16 FLC files | No current FLC import/presentation | `pending-import`: retain all source identities and determine story/artifact presentation contracts; do not silently omit them because they are not in the map-only inventory. |
| CNT-027 | Remaining classic media and tables | Root has 70 CIF, 263 IMG, 2 GFX, 3 CEL, 3 BSS, 6 RCI, 4 COL, 3 LGT, 3 RAW and other DAT/DEF/TDE/RSC/TBL files; exact per-family list is in the manifest | Only selected inputs are admitted | `uninspected`: enumerate each remaining family with an existing decoder or documented unsupported-source disposition. This row must not become a generic “all media” ticket; retain path-level IDs in the manifest. |
| CNT-028 | MIDI source: `MIDI.BSA` | 131 named records (`0x0100` header), 1,232,240 bytes | No runtime use | `excluded`: no MIDI decoder, synthesizer or `MIDI.BSA` playback. Ordinary audio and long-duration music looping remain in scope under CNT-023 and the coverage plan. |

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
which scene is an ending. Resolve narrative bindings from the retained callers. The 16 named Daedric FLC files remain explicit
artifact/cinematic identities. The main, guild and miscellaneous image groups in CNT-020 are
also required source families even where current runtime publication has no corresponding UI
surface. The implementation target is semantic content and thin DOM presentation; Unity window
classes, widget layouts, distribution helpers and runtime media bootstrap are excluded.

## Current baseline and missing work

The current importer has focused Arena2 decoders for named/numeric BSA archives, maps, RDB
blocks, ARCH3D meshes, PAK grids, texture leaves, weapon CIFs, fonts and DAGGER.SND. It uses
fixed source lists in `src/Daggerfall.Import.Tool/Program.cs`; this is why the 68-source manifest
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
