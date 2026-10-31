# Original directional sprite references

`export-directional-sprites` exports original character archives outside an
existing mobile reference corpus. Its default baseline is
`local/exports/daggerfall-mobile-idle-unmirrored-r01/`; it reads that corpus's
`manifest.json` without changing its selections or images.

```sh
dotnet run --project src/Daggerfall.Import.Tool -- export-directional-sprites
```

The default output is
`local/exports/daggerfall-remaining-directional-unmirrored-r01/`. Use `--arena2`,
`--baseline` and `--output` to relocate those directories. Output must be a fresh
directory so an export cannot overwrite references or image experiments.
Extracted source art remains ignored.

Open `index.html` for the visual catalog. `index.csv` lists each archive, display
family, outfit, source record/frame counts and reference directory;
`manifest.json` records coverage, aliases, decoding and selection conventions.
Each subject has its own manifest, five reference PNGs and every original
record/frame under `frames/record-NNN/frame-NNN.png`. Reference filenames match
the mobile export's sector convention: `front`, `front-right`, `right`,
`back-right`, `back`. They describe source sectors, not anatomical sides.
Nothing is mirrored, resized, cropped to a new silhouette or inferred.

The five references use records 0–4, frame 0. They are starting references;
they have not undergone the existing mobile corpus's visual pose selection.
All native animation phases remain available for later curation. Native
dimensions, offsets, palette and transparency are retained. Spectral archive
473 uses the existing `Arena2SpectralTextures` conversion, matching Ghost and
Wraith, rather than displaying shade indices as ordinary palette colours.

| Family | Original archives | Interpretation |
| --- | --- | --- |
| Walking townspeople | 381–398, 451–456 | 24 outfits: three display races, two genders, four outfits each. Five four-frame walk directions; record 5 has two front-only idle frames. |
| Alternate bear | 457 | Additional directional source art outside the mobile baseline. |
| Lysandus | 473 | A directional special-character exception; header spells the name `Lydandus`. |
| Mounted figures | 491–495 | Five directional rider source sets labelled by gender and armour weight in their archive headers. |
| City Watch | 399 | Already covered by `mobile-146`; linked rather than duplicated. |
| Alternate vampire file | 299 | Byte-identical to covered archive 283 in the original corpus. Recorded as an alias only when the supplied files actually match. |

Townspeople's display families and animation mapping come from Daggerfall
Unity's `Assets/Scripts/Internal/MobilePersonBillboard.cs` (`SetPerson`, texture
arrays, `MoveAnims`, `IdleAnims`). `MobilePersonNPC.cs` establishes the three
display races and four outfit variants. The exporter preserves archive-header
names separately because they sometimes disagree with the selected display
family, particularly archive 386.

The supplemental archives are source references. Their presence does not
establish a current gameplay consumer or justify adding them to the game's
actor catalog. Their additional records retain neutral source identities;
the export does not invent action names or playback timing for them.

Static shopkeepers, guild members, children and the usual named NPCs use
individual billboard records. Some have animation frames, which are not
additional camera directions. `RDBLayout.NPCFlatArchives` and `StaticNPC`
provide the donor distinction. Lysandus is the directional exception above.

`archive-audit.json` records baseline coverage and the relevant supplied
archive headers, record counts and frame counts. It also screens all supplied
texture archives for five animated records and explicitly accounts for
animated guild NPCs and lights that match this pattern without being
directional characters. Unclassified candidates or unreadable archives produce
warnings and remain in the report for inspection. This is a source-art audit,
not a claim that static NPC export or refreshed in-game art is complete.
The original `TEXTURE.215` (Town People) and `TEXTURE.217` (Farm People) files
are 46-byte stubs without image records; the audit reports both and continues.
