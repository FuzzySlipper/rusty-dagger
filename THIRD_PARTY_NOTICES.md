# Third-party notices

## The Elder Scrolls II: Daggerfall data (not redistributed)

The Daggerfall ruleset's content is derived from the game data of *The Elder Scrolls II: Daggerfall*
(Bethesda Softworks). This repository does not contain or redistribute that data, or any file
converted from it. The operator supplies their own copy of the game's `ARENA2` directory as
`local/arena2`, and the import command converts it into the data container
`local/daggerfall-data.rpak` on their machine (ignored by Git). See
[content scope](docs/coverage/content-scope.md) for what is generated and what is authored.

## What a release redistributes

A release (`rusty build --release`) carries the product, the Engine host with its own third-party
licences in `share/third-party/`, the .NET runtime (MIT, its licence and notices in `dotnet/`), and the
import tool with the tracked files it reads, among them the Daggerfall Unity files in
`tools/Daggerfall.Import.Tool/data/daggerfall-unity/` with their licence below. It carries nothing derived
from *The Elder Scrolls II: Daggerfall*: the player's import writes `daggerfall-data.rpak` and `music.rpak`
on their own machine.

## Music (not redistributed)

The score is packed from an operator-supplied folder of recordings named by the classic song
identifiers (`<track>.<ext>`, such as the donor installation's song folder `local/Sound`) into
`local/music.rpak`. It is not part of this repository and is not redistributed.

## Daggerfall Unity

[Daggerfall Unity](https://github.com/Interkarma/daggerfall-unity) is this project's behaviour and
content reference. No Daggerfall Unity code is compiled into the product. The Daggerfall Unity files the
import reads are copied unmodified into `data/daggerfall-unity/`, at their Daggerfall Unity paths, with its
licence beside them: the enemy table in `EnemyBasics.cs`, the race, item and maps sources, the item and
magic-item templates, the spell-cost tables in `FormulaHelper.cs`, the internal string and flat-caption
tables, the classic quest list and quest tables, and the rewritten classic quest scripts of
`StreamingAssets/Quests`. The values the import takes from them land in the data container it writes on the
player's machine. Daggerfall Unity is distributed under the following licence:

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

The authored sections of `content/worldrpg/payloads/daggerfall.base.json` (for example the random
encounter tables and the donor errata) transcribe values from Daggerfall Unity by hand and cite the
source file each came from.
