# Third-party notices

## The Elder Scrolls II: Daggerfall data (not redistributed)

The Daggerfall ruleset's content is derived from the game data of *The Elder Scrolls II: Daggerfall*
(Bethesda Softworks). This repository does not contain or redistribute that data, or any file
converted from it. The operator supplies their own copy of the game's `ARENA2` directory as
`local/arena2`, and `scripts/regenerate-content.sh` converts it into the ignored generated content
under `content/` on their machine. See [content scope](docs/coverage/content-scope.md) for what is
generated and what is authored.

## Music (not redistributed)

The score is published from an operator-supplied folder of Ogg Vorbis song files named by the
classic song identifiers, `local/Sound` (the donor installation's song folder). It is not part of
this repository and is not redistributed; `scripts/regenerate-content.sh` publishes it into the
ignored generated content.

## Daggerfall Unity

[Daggerfall Unity](https://github.com/Interkarma/daggerfall-unity) is this project's behaviour and
content reference. It is not vendored and no Daggerfall Unity code is compiled into the product. Some
import steps read tables from an operator's Daggerfall Unity checkout (for example the enemy table in
`EnemyBasics.cs`, the item templates, the spell-cost tables in `FormulaHelper.cs`, the quest text and
quest tables, and the internal strings), and the values they produce land in the ignored generated
content. Daggerfall Unity is distributed under the following licence:

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
