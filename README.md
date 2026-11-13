# Rusty Dagger

Rusty Dagger is the reference repository and proving product for **WorldRpg**, an
opinionated construction kit and host for world-centric, real-time, first-person
systemic RPGs. Daggerfall is its compiled ruleset, compatibility corpus and
content source.

> **Engine guarantees. Kit shapes. Ruleset decides. Bundle assembles. Host launches.**

## Where things live

| What | Where |
| --- | --- |
| Rules for working in this repository | [AGENTS.md](AGENTS.md), and the `AGENTS.md` beside the code in [src/ui](src/ui/AGENTS.md) and the [Daggerfall ruleset](src/WorldRpg.Rulesets.Daggerfall/AGENTS.md) |
| Design and reference (architecture, combat, UI, world, coverage, import, art) | den-wiki, under `wiki/rusty-dagger` |
| Tasks, status, evidence and review | Den project `rusty-dagger` |
| Open questions and discussion | Crew board `rusty-dagger` |
| Art pipeline | [authoring/README.md](authoring/README.md); first-person arms in [authoring/tools/fp-arms](authoring/tools/fp-arms/README.md) |
| Agent playtesting | [docs/playtesting.md](docs/playtesting.md) |
| Development tools: verify, play smoke, data sync, tour and cost | [scripts](scripts), usage in each tool's header |
| Review lanes and the reviewer packet | [docs/agent-review](docs/agent-review/README.md) |
| What is and isn't redistributed | [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) |

Engine setup, compilation and running follow the Den runbook
`rusty-engine/downstream-csharp-sdk-runbook`.

## Set up

Prerequisites: git, the .NET 10 SDK, Node 22 with npm, and python3, rsync and
zstd (which `scripts/data-sync.sh` uses). The product consumes one immutable
Engine SDK/runtime pair, pinned by `<RustyEnginePackageVersion>` in
`Directory.Build.props`. The Engine's `rusty` command installs, updates and runs
it. Get `rusty` once with the Engine bootstrap
(`curl -fsSL https://raw.githubusercontent.com/FuzzySlipper/rusty-engine/main/scripts/install-rusty.sh | bash`;
on Windows `irm https://raw.githubusercontent.com/FuzzySlipper/rusty-engine/main/scripts/install-rusty.ps1 | iex`),
and again when `rusty install` says `rusty` is older than the pinned pair, then:

```bash
git clone https://github.com/FuzzySlipper/rusty-dagger.git && cd rusty-dagger
rusty status
rusty install
npm ci
```

`rusty update` moves the pin to the newest published pair and lists the release
notes to read; `rusty update --check` only reports. AGENTS.md says when and where
the pin may move.

## Game data

The game content is not in the repository, apart from the terrain bake (below):
it is converted from your own copy of Daggerfall into containers under `local/`,
the one ignored folder. Its folders say what may be deleted:

| Folder | Holds | Lifecycle |
| --- | --- | --- |
| `local/inputs/` | `arena2`, `Sound`, `fp-melee` (purchased art) | supplied, never regenerated: keep a copy |
| `local/data/` | `daggerfall-data.rpak`, `music.rpak`, `first-person.rpak` | what the game opens; rebuilt by the import and `authoring/tools/fp-arms/build.sh`, or pulled whole by `scripts/data-sync.sh` |
| `local/build/` | import staging and records, the fp-arms build, verify stamps | regenerable |
| `local/art/` | `reference/` exports, `generated/` finals and studies, `review/` pages | the art pipeline's working files ([authoring/README.md](authoring/README.md)) |
| `local/scratch/` | experiments | deletable at any time |
| `local/evidence` | a link to the den NAS's `evidence/rusty-dagger/`, a folder per task (`ln -s /mnt/den-nas-backup/data/evidence/rusty-dagger local/evidence`) | kept off the checkout |

- Supply the game's `ARENA2` directory as `local/inputs/arena2` (or pass
  `--arena2` the folder Daggerfall is installed in) and the song folder as
  `local/inputs/Sound`. A link is fine.
- Import once, and again after a pull that changes the importer revision: its
  inputs are `src/Daggerfall.Import` and `src/Daggerfall.Import.Tool`, whose
  `inputs/` holds the source inventory and our tables (not the terrain bake in
  `inputs/relief/`, which cannot change what it writes), and the authored UI art
  with its manifest (`src/ImporterRevision.targets` lists them). The import never
  reads the authored base payload, so editing it needs no import. The entry screen says when the data is missing, damaged or
  from another importer.

  ```bash
  dotnet run --project src/Daggerfall.Import.Tool -- import --music local/inputs/Sound
  ```

- It checks every input before writing anything, then packs
  `local/data/daggerfall-data.rpak` and `local/data/music.rpak`. `--music` is
  optional, and so are `ffmpeg` and `ffprobe` on `PATH`: without them cinematics
  play as skipped.
- To work on the importer, `import --only STEP,...` reruns named steps and repacks.
  The result records that it is a repack over an earlier run, so the entry screen
  calls it stale (a warning under `rusty dev`) and `scripts/data-sync.sh push`
  refuses it; only a full import reads as current.
- On our own network only den-agents imports; every other machine copies its
  containers with `scripts/data-sync.sh` and never imports. They are derived from
  arena2, so they never leave the network.

  ```bash
  scripts/data-sync.sh pull                                        # the containers, from the NAS store
  scripts/data-sync.sh pull den-agents:dev/rusty-dagger --import-tree   # a box that runs the tests
  scripts/data-sync.sh status                                      # is this copy stale?
  ```

  The store holds only `local/data`. A box that runs the tests also needs
  `local/build/import` (the staging, records and music records the importer and UI
  tests read), which only den-agents' checkout has: pull it from there with
  `--import-tree`, which brings the containers too, and copy the corpus the import
  suite reads: `rsync -a den-agents:dev/rusty-dagger/local/inputs/arena2 local/inputs/`.
  Pull right after cloning or checking out, and check that `status` reads current.
  Over ssh only changed 8 KiB blocks travel, so a refresh after an import sends
  tens of megabytes; to or from the store (`/mnt/den-nas-backup/data/rusty-dagger/lan`)
  changed files go whole. After an import den-agents runs
  `scripts/data-sync.sh push`, which keeps the store current. Nothing is deleted
  unless you add `--mirror`.
- Windows (den-win11) runs the game and never imports or verifies. Its default
  install is a native clone, `C:\dev\rusty-dagger`, set up as above, whose
  containers come whole from den-agents' checkout through `P:` with
  `powershell -File scripts\data-sync.ps1`. `P:`, den-agents' home, is mapped only
  on the console desktop, so that pull and anything that opens a window run
  through `C:\Users\agent\crew\bin\run-on-desktop.ps1`. Running from a den-agents
  worktree over `P:` also works, for uncommitted work or a throwaway pin. Only the
  newest pairs publish win-x64 archives: `rusty install` says
  `RUSTY_INSTALL_NOT_PUBLISHED` for an older pin.
- Every run prints the container's importer revision beside the checkout's. When
  they differ the copy is stale for this tree (or the tree is ahead of the last
  import): pull again once den-agents has imported that revision.
- The terrain bake is tracked in `src/Daggerfall.Import.Tool/inputs/relief/`
  with the record of what it was baked from. The import's `relief` step takes it
  when the record matches the arena2 and this build's bake; otherwise it bakes
  (14 minutes or more), says which input differed and rewrites those files.
  **Refresh** it with any change to `src/Daggerfall.Import/Relief/` or the bake's
  stages: run the full import on den-agents and commit the rewritten files with
  that change. `KeptReliefTests` fails while they are stale. Git stores the
  21 MB again only when the bytes change.
- `local/data/first-person.rpak` is optional: 3D first-person arms built from
  purchased art by `authoring/tools/fp-arms/build.sh`. Without it a 3D `viewmodel.style`
  draws the classic weapon sprites, with a warning.
- The values the import takes from Daggerfall Unity are tables of our own in
  `src/Daggerfall.Import.Tool/inputs/`, the quest scripts among them as quest
  documents, each naming the Daggerfall Unity file and commit it was taken
  from; the import reads no Daggerfall Unity file and needs no donor checkout.
  `dotnet run --project src/Daggerfall.Source.Tool -- donor-drift --donor DIR`
  reports where they differ from a Daggerfall Unity checkout.

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
`first-person.rpak` the same way. The import takes the tracked terrain bake, so
there is nothing to copy for it.

Rules for the tracked side:

- Nothing converted from Daggerfall is ever committed, but for the terrain bake
  in `src/Daggerfall.Import.Tool/inputs/relief/`, tracked by Patch's decision
  (#10758), which is no licence for any other. An untracked file under
  `content/` fails the architecture tests.
- A content pack is one folder, `content/worldrpg/packs/<id>/`: its `pack.json`,
  the `payload.json` it names and any media it owns. A tuning profile is
  `content/worldrpg/tuning/<id>/` the same way; bundles stay in `bundles/`. All the
  game's own art is in `packs/daggerfall.art-replacements/`: the refreshed art in
  `media/<area>/`, the authored UI kit in `authored-ui/` (its
  `ui-authored-assets.json` is written by `authoring/tools/ui/ui-art-kit.py
  manifest`) and the held definition and models in `first-person/`.
- `daggerfall.base` is authored and `daggerfall.imported` is generated. Two base
  sections (`buildingNames`, `internalText`) were taken from Daggerfall Unity,
  need nothing from arena2, and are checked by `donor-drift`.
  A payload section belongs to one of them: the reader refuses a section both carry.
- In `src/Daggerfall.Import.Tool/inputs/content-source-manifest.csv`, never renumber or recreate a `CNT-` ID;
  reclassify a file by changing its `family_id`. Only `Daggerfall.Source.Tool`'s
  `source-manifest --update-inventory` writes the disposition column.
- An edit to an importer revision input, including any of our tables in
  `inputs/`, makes every machine's data stale. Make one deliberately. An import step
  never reads the authored base payload: a join between authored and imported
  sections belongs in the ruleset's content reader. A command that does not feed the containers goes in
  `Daggerfall.Source.Tool`, never `Daggerfall.Import.Tool`, whose every file but the terrain bake is an input.

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

Only verify needs these; a machine that only runs the game can skip them:

- `local/inputs/arena2` and `local/build/import` (Game data says how a box that
  does not import gets them), and `ffmpeg` on `PATH`: the import suite converts a
  cinematic with it.
- For `--play`, a Chromium: `chromium` on `PATH`, or `RUSTY_CHROMIUM_PATH` naming
  one, such as Playwright's (`npx playwright install chromium`; add `--with-deps`
  once, with sudo, for its system libraries).
- `DAGGER_DONOR_ROOT` naming a Daggerfall Unity checkout
  (`git clone https://github.com/Interkarma/daggerfall-unity`). Without it the
  tests that compare our tables with the donor skip, with the reason.
- python3 with numpy, scipy and Pillow for the art pipeline's tests, which skip
  without them.

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
tools/Daggerfall.Import.Tool/   the import, with the tracked files it reads (no Daggerfall Unity file)
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
