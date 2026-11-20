# Third-party notices

## The Elder Scrolls II: Daggerfall data (not redistributed)

The Daggerfall ruleset's content is derived from the game data of *The Elder Scrolls II: Daggerfall*
(Bethesda Softworks). Rusty Dagger is inspired by that game, not a port of it, and plays only from the
player's own copy. This repository does not contain or redistribute the game's raw media: its sprites
and textures, and its sounds and video. Facts derived from the game, such as tables,
formulas, timings, positions, quest text, collision, navigation and model geometry, are taken once into
authored data of ours and edited in place (decision DEC-13 on the project wiki's
`coverage-scope-and-decisions` page). The terrain bake in `content/worldrpg/packs/daggerfall.base/terrain/`
is such data: eroded relief and rivers computed from the heightmap in `WOODS.WLD`, the climate and the
locations, which the game reads from the base pack. The operator supplies their own copy of the game's `ARENA2` directory as
`local/inputs/arena2`, and the import command converts it into the data container
`local/data/daggerfall-data.rpak` on their machine (ignored by Git). The project wiki's
`import-and-data-container` page says what is generated and what is authored.

## What a release redistributes

A release (`rusty build --release`) carries the product, the Engine host with its own third-party
licences in `share/third-party/`, the .NET runtime (MIT, its licence and notices in `dotnet/`), the game
UI's typefaces with their licences (below), and the import tool with the tracked files it reads, among them
our tables taken from Daggerfall Unity (below) and the terrain bake (above). It carries no Daggerfall
Unity file and none of the game's raw media: the player's import writes `daggerfall-data.rpak` on their
own machine.

## Music

The score is Patch's own orchestral arrangement of the Daggerfall soundtrack, which Eric Heberling
composed for Bethesda Softworks. It ships with the game as `music.rpak`, built outside this repository
(`authoring/tools/music`); no recording of the original game's music is part of it.

## Typefaces

The game UI draws its text in typefaces it ships as WOFF2 in `src/ui/fonts/`, each weight it uses its own file. All are licensed under the [SIL Open Font License 1.1](https://openfontlicense.org), whose
text sits beside them:

- **Alegreya** and **Alegreya SC**, Copyright 2011 The Alegreya Project Authors
  (https://github.com/huertatipografica/Alegreya): `Alegreya-OFL.txt`.
- **Almendra**, Copyright (c) 2011-2012 Ana Sanfelippo, with Reserved Font Name "Almendra"
  (https://github.com/google/fonts/tree/main/ofl/almendra): `Almendra-OFL.txt`. Its bold weight ships whole,
  only compressed to WOFF2, so it keeps its name.

## Daggerfall Unity

[Daggerfall Unity](https://github.com/Interkarma/daggerfall-unity) is this project's behaviour and
content reference. No Daggerfall Unity code is compiled into the product, and the repository carries no
Daggerfall Unity file. Values were taken once from its files into data of our own:

- in the `itemTemplates`, `magic`, `mobiles`, `catalogs` and `factions` sections of
  `content/worldrpg/packs/daggerfall.base/payload.json`: the race exposure flags (`RaceTemplate.cs`), the item
  groups (`ItemEnums.cs`, `ItemHelper.cs`, `ItemsFile.cs`), the item and magic-item templates
  (`ItemTemplates.txt`, `MagicItemTemplates.txt`), the spell-cost tables (`FormulaHelper.cs`), the static mobile
  table (`EnemyBasics.cs`) and the flat-caption table (`Internal_Flats.csv`);
- in `content/worldrpg/packs/daggerfall.quests/`: the classic quest list and quest tables (`StreamingAssets/Tables`)
  and the rewritten classic quest scripts of `StreamingAssets/Quests`, each as a quest record of ours carrying
  the same lines, the pack's description naming the commit;
- in the `buildingNames` and `internalText` sections of `content/worldrpg/packs/daggerfall.base/payload.json`:
  the region name banks and names (`MapsFile.cs`) and the internal string table (`Internal_Strings.csv`).

They are ours to edit in place (DEC-13), and this notice credits Daggerfall Unity for them. Daggerfall Unity
is distributed under the following licence:

```text
MIT License

Copyright (c) 2009-2023 Daggerfall Workshop

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

The authored sections of `content/worldrpg/packs/daggerfall.base/payload.json` (for example the random
encounter tables and the donor errata) transcribe values from Daggerfall Unity by hand and cite the
source file each came from.
