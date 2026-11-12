# Third-party notices

## The Elder Scrolls II: Daggerfall data (not redistributed)

The Daggerfall ruleset's content is derived from the game data of *The Elder Scrolls II: Daggerfall*
(Bethesda Softworks). This repository does not contain or redistribute that data, or any file
converted from it. The operator supplies their own copy of the game's `ARENA2` directory as
`local/inputs/arena2`, and the import command converts it into the data container
`local/data/daggerfall-data.rpak` on their machine (ignored by Git). The project wiki's
`import-and-data-container` page says what is generated and what is authored.

## What a release redistributes

A release (`rusty build --release`) carries the product, the Engine host with its own third-party
licences in `share/third-party/`, the .NET runtime (MIT, its licence and notices in `dotnet/`), the game
UI's typefaces with their licences (below), and the import tool with the tracked files it reads, among them
our tables taken from Daggerfall Unity (below). It carries no Daggerfall Unity file, and nothing derived
from *The Elder Scrolls II: Daggerfall*: the player's import writes `daggerfall-data.rpak` and `music.rpak`
on their own machine.

## Music (not redistributed)

The score is packed from an operator-supplied folder of recordings named by the classic song
identifiers (`<track>.<ext>`, such as the donor installation's song folder `local/inputs/Sound`) into
`local/data/music.rpak`. It is not part of this repository and is not redistributed.

## Typefaces

The game UI draws its text in typefaces it ships, cut to the weights and Latin characters it uses and converted to WOFF2,
in `src/ui/fonts/`. Both are licensed under the [SIL Open Font License 1.1](https://openfontlicense.org), whose
text sits beside them:

- **Alegreya** and **Alegreya SC**, Copyright 2011 The Alegreya Project Authors
  (https://github.com/huertatipografica/Alegreya): `Alegreya-OFL.txt`.
- **Cinzel**, Copyright 2020 The Cinzel Project Authors (https://github.com/NDISCOVER/Cinzel): `Cinzel-OFL.txt`.

## Daggerfall Unity

[Daggerfall Unity](https://github.com/Interkarma/daggerfall-unity) is this project's behaviour and
content reference. No Daggerfall Unity code is compiled into the product, and the repository carries no
Daggerfall Unity file. Values were taken once from its files into tables of our own, each naming the
Daggerfall Unity file and commit it came from:

- in `src/Daggerfall.Import.Tool/inputs/`, which the import reads and a release ships beside the import
  tool: the race exposure flags (`RaceTemplate.cs`), the item groups (`ItemEnums.cs`, `ItemHelper.cs`,
  `ItemsFile.cs`), the item and magic-item templates (`ItemTemplates.txt`, `MagicItemTemplates.txt`),
  the spell-cost tables (`FormulaHelper.cs`), the static mobile table (`EnemyBasics.cs`), the classic quest
  list and quest tables (`StreamingAssets/Tables`), and the rewritten classic quest scripts of
  `StreamingAssets/Quests`, each as a quest document of ours carrying the same text;
- in the `buildingNames`, `internalText` and `npcCaptions` sections of
  `content/worldrpg/packs/daggerfall.base/payload.json`: the region name banks and names (`MapsFile.cs`)
  and the internal string and flat-caption tables (`Internal_Strings.csv`, `Internal_Flats.csv`).

`daggerfall-source-tool donor-drift --donor DIR` compares them with a Daggerfall Unity checkout. Daggerfall
Unity is distributed under the following licence:

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
