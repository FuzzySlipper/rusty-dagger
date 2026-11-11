# Rusty Dagger

Rusty Dagger is the reference repository and proving product for **WorldRpg**, an
opinionated construction kit and host for world-centric, real-time, first-person
systemic RPGs. Daggerfall is its first compiled ruleset, compatibility corpus and
content source; it is not the WorldRpg architecture.

> **Engine guarantees. Kit shapes. Ruleset decides. Bundle assembles. Host launches.**

## Where things live

| What | Where |
| --- | --- |
| Rules for working in this repository | [AGENTS.md](AGENTS.md), and the `AGENTS.md` beside the code in [src/ui](src/ui/AGENTS.md) and the [Daggerfall ruleset](src/WorldRpg.Rulesets.Daggerfall/AGENTS.md) |
| Design and reference (architecture, combat, UI, world, coverage, import, art) | den-wiki, under `wiki/rusty-dagger` |
| Tasks, status, evidence and review | Den project `rusty-dagger` |
| Open questions and discussion | Crew board `rusty-dagger` |
| Art pipeline | [authoring/README.md](authoring/README.md); first-person arms in [scripts/fp-arms](scripts/fp-arms/README.md) |
| Agent playtesting | [docs/playtesting.md](docs/playtesting.md) |
| Review lanes and the reviewer packet | [docs/agent-review](docs/agent-review/README.md) |
| What is and isn't redistributed | [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) |

Engine setup, compilation and running follow the Den runbook
`rusty-engine/downstream-csharp-sdk-runbook`.

## Set up

The product consumes one immutable Engine SDK/runtime pair, pinned by
`<RustyEnginePackageVersion>` in `Directory.Build.props`. The Engine's `rusty`
command installs, updates and runs it. Get `rusty` once with the Engine bootstrap
(`curl -fsSL https://raw.githubusercontent.com/FuzzySlipper/rusty-engine/main/scripts/install-rusty.sh | bash`),
then in a clean checkout:

```bash
rusty status
rusty install
npm ci
```

`rusty update` moves the pin to the newest published pair and lists the release
notes to read; `rusty update --check` only reports. AGENTS.md says when and where
the pin may move.

## Game data

The game content is not in the repository: it is converted from your own copy of
Daggerfall into containers under `local/`, the one ignored folder. Its folders
say what may be deleted:

| Folder | Holds | Lifecycle |
| --- | --- | --- |
| `local/inputs/` | `arena2`, `Sound`, `fp-melee` (purchased art) | supplied, never regenerated: keep a copy |
| `local/data/` | `daggerfall-data.rpak`, `music.rpak`, `first-person.rpak` | what the game opens; rebuilt by the import and `scripts/fp-arms/build.sh` |
| `local/build/` | import staging and records, the fp-arms build, verify stamps | regenerable |
| `local/art/` | `reference/` exports, `generated/` finals and studies, `review/` pages | the art pipeline's working files ([authoring/README.md](authoring/README.md)) |
| `local/scratch/` | experiments | deletable at any time |
| `local/evidence` | a link to the den NAS's `evidence/rusty-dagger/`, a folder per task | kept off the checkout |

- Supply the game's `ARENA2` directory as `local/inputs/arena2` (or pass
  `--arena2` the folder Daggerfall is installed in) and the song folder as
  `local/inputs/Sound`. A link is fine.
- Import once, and again after a pull that changes `src/Daggerfall.Import*`,
  `src/WorldRpg.SpriteAuthoring`, `data/` or the authored UI art. The entry
  screen says when the data is missing, damaged or from another importer.

  ```bash
  dotnet run --project src/Daggerfall.Import.Tool -- import --music local/inputs/Sound
  ```

- It checks every input before writing anything, then packs
  `local/data/daggerfall-data.rpak` and `local/data/music.rpak`. `--music` is
  optional, and so are `ffmpeg` and `ffprobe` on `PATH`: without them cinematics
  play as skipped.
- To work on the importer, `import --only STEP,...` reruns named steps and repacks.
- On our own network, copy the containers from a machine that has imported; don't
  import again on a second machine.
- `local/data/first-person.rpak` is optional: 3D first-person arms built from
  purchased art by `scripts/fp-arms/build.sh`. Without it a 3D `viewmodel.style`
  draws the classic weapon sprites, with a warning.
- The Daggerfall Unity tables and quest scripts the import reads are tracked in
  `data/daggerfall-unity`; no donor checkout is needed.
- A checkout from before this layout moves its files once with
  `scripts/split-local.sh` (`--dry-run` first), while nothing imports, promotes
  art or runs the game from it or a worktree that links it.

A worktree links only the inputs and the containers of the main checkout, so its
build and art files stay its own; on den machines evidence links to the NAS:

```bash
main=$(git worktree list --porcelain | head -n 1 | cut -d' ' -f2)
mkdir -p local && ln -s "$main/local/inputs" "$main/local/data" local/
ln -s /mnt/den-nas-backup/data/evidence/rusty-dagger local/evidence
ln -s "$main/node_modules" node_modules
```

Tests that read the import's staging tree and records skip without them. While
the branch leaves the importer's inputs (the list above) alone, link those too:
`mkdir -p local/build && ln -s "$main/local/build/import" local/build/`.

The import refuses to write through any of these links, since it would replace
what every checkout opens. A worktree whose branch changes the importer's inputs
imports into its own: `rm local/data local/build/import && mkdir local/data`,
link back `first-person.rpak` if it needs one, then import. Build
`first-person.rpak` the same way.

Rules for the tracked side:

- Nothing converted from Daggerfall is ever committed. Git tracks `content/` and
  `data/`; an untracked file under `content/` fails the architecture tests.
- `daggerfall.base` is authored by hand and `daggerfall.imported` is generated.
  A payload section belongs to one of them: the reader refuses a section both carry.
- In `data/content-source-manifest.csv`, never renumber or recreate a `CNT-` ID;
  reclassify a file by changing its `family_id`. Only the tool's
  `source-manifest --update-inventory` writes the disposition column.
- Any edit under `data/` changes the importer revision, so every machine's data
  goes stale. Make one deliberately.

The wiki page `wiki/rusty-dagger/import-and-data-container` has the fuller account.

## Run

```bash
rusty dev --project ./src/WorldRpg.Host/WorldRpg.Host.csproj
```

- `rusty dev` streams to a browser page; add `--output window` for a window, at
  1920x1080 or larger.
- The title screen's **Game** selector chooses the bundle a new run starts under.
  A save loads only under the game it was made in. **Quick start** in character
  creation begins at once.
- **Developer: Charing town start** begins in Charing's streets at nine in the
  morning with 5000 gold and no opening quests, for town services.
- The menu's **Engine debug console** needs the host started with `--live-debug`.
- Engine contributors may pass `--engine-source /absolute/rusty-engine`; nothing
  else discovers an adjacent Engine checkout.

Controls and screens are described on the wiki page `wiki/rusty-dagger/game-ui`.

## Verify

```bash
./scripts/verify.sh
```

It installs the pinned pair and UI dependencies, runs the UI tests, builds the
products and the import tool, runs every test project under `tests/`, and stages
the CoreCLR product.

- `--play` also starts the product, presses Begin in a headless Chromium, creates
  a character and passes once the game reaches ordinary play with no error (about
  four minutes).
- `--aot` also runs the NativeAOT fidelity publish.
- `--import` runs the import suite even when nothing it reads has changed since
  its last green run on this machine.
- `--record` attaches the green run's summary to `HEAD` as a git note in
  `refs/notes/verify` and pushes it.

There is no hosted CI: most suites read the Arena2 corpus and the content made
from it, so the recorded local run is the gate's record. Without the data
container the script says so, skips the ruleset suite and refuses `--play`.

A plain `dotnet build` does not stage the product. To stage only:

```bash
dotnet msbuild src/WorldRpg.Host/WorldRpg.Host.csproj -t:StageRustyEngineCoreClrProduct -p:Configuration=Release
```

NativeAOT is a fidelity and release check, not the edit-run loop:

```bash
dotnet msbuild src/WorldRpg.Host/WorldRpg.Host.csproj -t:VerifyRustyEngineAot
```

## Release

`rusty build --release <dir>` (with `--aot` for NativeAOT on Linux) writes a
directory that runs where no .NET, `rusty` or checkout is installed:

```text
rusty-dagger[.exe]          the launcher a player starts; the game opens its own window
product.rpak                the product: UI, tracked content and refreshed art (no Daggerfall data)
coreclr/ | native/          the product's code
bin/, share/, dotnet/       the Engine host, its licences and the .NET runtime
tools/Daggerfall.Import.Tool/   the import, with the tracked files it reads
daggerfall-data.rpak, music.rpak   written by the import from the player's own files
```

The player imports once, from the game: while the data is missing, damaged or
from another importer, the entry screen says why and offers **Import**, runs the
shipped tool on the folder they pick and opens the game when it finishes. The
tool can also be run by hand, adding `--music <folder>` for the score.

## Acknowledgements

Rusty Dagger stands on the work of the [Daggerfall Unity](https://github.com/Interkarma/daggerfall-unity)
project (Daggerfall Workshop and its contributors). Its years of reverse-engineering of the
classic file formats, formulas and behaviour are the reference this project's importer and
ruleset are checked against, and several import tables and playlists come from it. It is used
under the MIT licence; see [third-party notices](THIRD_PARTY_NOTICES.md).
*The Elder Scrolls II: Daggerfall* is Bethesda Softworks' game, and its data is supplied by the player. Bethesda remains the credited author of the inspiration for ideas that were further developed here.
