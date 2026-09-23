# Daggerfall world corpus closure

Generated from the supplied Arena2 corpus by `DungeonCorpusClosureBuilder`. Source payloads are not copied here.

- Regions: 62
- Locations: 15251
- Dungeon locations: 3959
- Exterior locations inspected: 15251
- Referenced RMB records: 658
- Referenced RDB records: 179

## Disposition summary

| Disposition | Entries |
| --- | ---: |
| Valid | 54605 |
| Unresolved | 11153 |
| Malformed | 3 |
| Duplicate | 14 |
| Unused | 14007 |

## Kind summary

| Kind | Entries |
| --- | ---: |
| MapTable | 248 |
| Location | 15251 |
| DungeonLink | 14978 |
| ExteriorBlockLink | 15909 |
| DungeonBlockLink | 179 |
| Block | 1295 |
| MeshLink | 2340 |
| Mesh | 10251 |
| TextureLink | 1364 |
| TextureRecord | 6718 |
| TextureLeaf | 512 |
| ActionLink | 10737 |

## Source ID examples

The complete deterministic manifest is [`dungeon-corpus-closure.tsv`](dungeon-corpus-closure.tsv); this page keeps bounded examples readable while the API and TSV retain every source ID.

| Disposition | Kind | Source ID | Source | Reason |
| --- | --- | --- | --- | --- |
|Valid|MapTable|maps/region/0/table/MAPDITEM.000|MAPS.BSA|A 152-entry dungeon table at 1216 bytes within 45704.|
|Valid|MapTable|maps/region/0/table/MAPNAMES.000|MAPS.BSA|The declared location count reads as it stands.|
|Valid|MapTable|maps/region/0/table/MAPPITEM.000|MAPS.BSA|An offset table of 344 entries at 1376 bytes within 360616.|
|Valid|MapTable|maps/region/0/table/MAPTABLE.000|MAPS.BSA|One 17-byte entry for each of the region's 344 locations.|
|Valid|MapTable|maps/region/1/table/MAPDITEM.001|MAPS.BSA|A 310-entry dungeon table at 2480 bytes within 92760.|
|Valid|MapTable|maps/region/1/table/MAPNAMES.001|MAPS.BSA|The declared location count reads as it stands.|
|Valid|MapTable|maps/region/1/table/MAPPITEM.001|MAPS.BSA|An offset table of 912 entries at 3648 bytes within 932414.|
|Valid|MapTable|maps/region/1/table/MAPTABLE.001|MAPS.BSA|One 17-byte entry for each of the region's 912 locations.|
|Valid|MapTable|maps/region/10/table/MAPDITEM.010|MAPS.BSA|A 0-entry dungeon table at 0 bytes within 4.|
|Valid|MapTable|maps/region/11/table/MAPDITEM.011|MAPS.BSA|A 129-entry dungeon table at 1032 bytes within 39067.|
|Valid|MapTable|maps/region/11/table/MAPNAMES.011|MAPS.BSA|The declared location count reads as it stands.|
|Valid|MapTable|maps/region/11/table/MAPPITEM.011|MAPS.BSA|An offset table of 197 entries at 788 bytes within 148371.|
|Unresolved|MapTable|maps/region/10/table/MAPNAMES.010|MAPS.BSA|The table has no bytes; the donor's MapsFile treats a region with any zero-length table as unreadable and discards the region.|
|Unresolved|MapTable|maps/region/10/table/MAPPITEM.010|MAPS.BSA|The table has no bytes; the donor's MapsFile treats a region with any zero-length table as unreadable and discards the region.|
|Unresolved|MapTable|maps/region/10/table/MAPTABLE.010|MAPS.BSA|The table has no bytes; the donor's MapsFile treats a region with any zero-length table as unreadable and discards the region.|
|Unresolved|MapTable|maps/region/12/table/MAPNAMES.012|MAPS.BSA|The table has no bytes; the donor's MapsFile treats a region with any zero-length table as unreadable and discards the region.|
|Unresolved|MapTable|maps/region/12/table/MAPPITEM.012|MAPS.BSA|The table has no bytes; the donor's MapsFile treats a region with any zero-length table as unreadable and discards the region.|
|Unresolved|MapTable|maps/region/12/table/MAPTABLE.012|MAPS.BSA|The table has no bytes; the donor's MapsFile treats a region with any zero-length table as unreadable and discards the region.|
|Unresolved|MapTable|maps/region/13/table/MAPNAMES.013|MAPS.BSA|The table has no bytes; the donor's MapsFile treats a region with any zero-length table as unreadable and discards the region.|
|Unresolved|MapTable|maps/region/13/table/MAPPITEM.013|MAPS.BSA|The table has no bytes; the donor's MapsFile treats a region with any zero-length table as unreadable and discards the region.|
|Unresolved|MapTable|maps/region/13/table/MAPTABLE.013|MAPS.BSA|The table has no bytes; the donor's MapsFile treats a region with any zero-length table as unreadable and discards the region.|
|Unresolved|MapTable|maps/region/14/table/MAPNAMES.014|MAPS.BSA|The table has no bytes; the donor's MapsFile treats a region with any zero-length table as unreadable and discards the region.|
|Unresolved|MapTable|maps/region/14/table/MAPPITEM.014|MAPS.BSA|The table has no bytes; the donor's MapsFile treats a region with any zero-length table as unreadable and discards the region.|
|Unresolved|MapTable|maps/region/14/table/MAPTABLE.014|MAPS.BSA|The table has no bytes; the donor's MapsFile treats a region with any zero-length table as unreadable and discards the region.|
|Malformed|TextureLeaf|texture-leaf/215|Arena2 TEXTURE leaf corpus|TEXTURE.215 at byte 46: texture record 0 header exceeds source length 46|
|Malformed|TextureLeaf|texture-leaf/217|Arena2 TEXTURE leaf corpus|TEXTURE.217 at byte 46: texture record 0 header exceeds source length 46|
|Malformed|TextureLeaf|texture-leaf/436|Arena2 TEXTURE leaf corpus|The archive declares no addressable frames.|
|Duplicate|Mesh|mesh/162/record/9742|ARCH3D.BSA|ARCH3D numeric ID 162 resolves to earlier directory record 5610.|
|Duplicate|Mesh|mesh/5090/record/10238|ARCH3D.BSA|ARCH3D numeric ID 5090 resolves to earlier directory record 5007.|
|Duplicate|Mesh|mesh/5090/record/8903|ARCH3D.BSA|ARCH3D numeric ID 5090 resolves to earlier directory record 5007.|
|Duplicate|Mesh|mesh/5090/record/9787|ARCH3D.BSA|ARCH3D numeric ID 5090 resolves to earlier directory record 5007.|
|Duplicate|Mesh|mesh/5090/record/9824|ARCH3D.BSA|ARCH3D numeric ID 5090 resolves to earlier directory record 5007.|
|Duplicate|Mesh|mesh/5090/record/9826|ARCH3D.BSA|ARCH3D numeric ID 5090 resolves to earlier directory record 5007.|
|Duplicate|Mesh|mesh/63506/record/7926|ARCH3D.BSA|ARCH3D numeric ID 63506 resolves to earlier directory record 808.|
|Duplicate|Mesh|mesh/64400/record/6213|ARCH3D.BSA|ARCH3D numeric ID 64400 resolves to earlier directory record 5208.|
|Duplicate|Mesh|mesh/64401/record/7667|ARCH3D.BSA|ARCH3D numeric ID 64401 resolves to earlier directory record 5660.|
|Duplicate|Mesh|mesh/64402/record/7856|ARCH3D.BSA|ARCH3D numeric ID 64402 resolves to earlier directory record 5667.|
|Duplicate|Mesh|mesh/64403/record/6363|ARCH3D.BSA|ARCH3D numeric ID 64403 resolves to earlier directory record 6218.|
|Duplicate|Mesh|mesh/64404/record/5976|ARCH3D.BSA|ARCH3D numeric ID 64404 resolves to earlier directory record 5807.|
|Unused|Block|block/AAANEW.RMB|BLOCKS.BSA|The block is supplied but no readable MAPS location references it.|
|Unused|Block|block/ALCHAS00.RMB|BLOCKS.BSA|The block is supplied but no readable MAPS location references it.|
|Unused|Block|block/ALCHAS01.RMB|BLOCKS.BSA|The block is supplied but no readable MAPS location references it.|
|Unused|Block|block/ALCHAS02.RMB|BLOCKS.BSA|The block is supplied but no readable MAPS location references it.|
|Unused|Block|block/ALCHAS03.RMB|BLOCKS.BSA|The block is supplied but no readable MAPS location references it.|
|Unused|Block|block/ALCHBS00.RMB|BLOCKS.BSA|The block is supplied but no readable MAPS location references it.|
|Unused|Block|block/ALCHBS01.RMB|BLOCKS.BSA|The block is supplied but no readable MAPS location references it.|
|Unused|Block|block/ALCHGS00.RMB|BLOCKS.BSA|The block is supplied but no readable MAPS location references it.|
|Unused|Block|block/ALCHGS01.RMB|BLOCKS.BSA|The block is supplied but no readable MAPS location references it.|
|Unused|Block|block/ARMRAS00.RMB|BLOCKS.BSA|The block is supplied but no readable MAPS location references it.|
|Unused|Block|block/ARMRAS01.RMB|BLOCKS.BSA|The block is supplied but no readable MAPS location references it.|
|Unused|Block|block/ARMRAS02.RMB|BLOCKS.BSA|The block is supplied but no readable MAPS location references it.|
