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
| Development tools: verify, routed suites, play smoke, data sync, tour and cost | [scripts](scripts), usage in each tool's header |
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
| `local/data/` | `daggerfall-data.rpak`, `music.rpak`, `first-person.rpak` | what the game opens; rebuilt by the import and `authoring/tools/fp-arms/build.sh`, or pulled by `scripts/data-sync.sh` |
| `local/build/` | import staging and records, the fp-arms build, verify stamps | regenerable |
| `local/art/` | `reference/` exports, `generated/` finals and studies, `review/` pages | the art pipeline's working files ([authoring/README.md](authoring/README.md)) |
| `local/scratch/` | experiments | deletable at any time |
| `local/evidence` | a link to the den NAS's `evidence/rusty-dagger/`, a folder per task (`ln -s /mnt/den-nas-backup/data/evidence/rusty-dagger local/evidence`) | kept off the checkout |

- Supply the game's `ARENA2` directory as `local/inputs/arena2` (or pass
  `--arena2` the folder Daggerfall is installed in) and the song folder as
  `local/inputs/Sound`. A link is fine.
- A checkout needs the data of its own importer revision: import once, and again
  after a pull that changes the importer revision. Its
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
  calls it stale (a warning under `rusty dev`) and `scripts/data-sync.sh publish`
  refuses it; only a full import reads as current.
- On our own network nobody imports by hand: imports run on a build worker through
  the router, and every machine pulls the data of its commit's importer revision
  from the NAS store (`/mnt/den-nas-backup/data/rusty-dagger/lan`), which keeps one
  set per revision: the containers, the import tree and their checksums. The data
  is derived from arena2, so it never leaves the network.

  ```bash
  scripts/data-sync.sh pull                  # the containers, at this checkout's importer revision
  scripts/data-sync.sh pull --import-tree    # also local/build/import, for a box that runs the tests
  scripts/data-sync.sh status                # is this copy current, and does the store hold this revision?
  scripts/route.py data [REF]                # import a commit on a worker and publish its data (from den-agents)
  ```

  Pull right after cloning, checking out or pulling, and check that `status` reads
  current; that includes den-agents' shared checkout, which den-win11 reads. When
  the store has no data for the revision, both say so and name the `route.py data`
  command. A box that runs the tests also needs the corpus the import suite reads:
  `rsync -a den-agents:dev/rusty-dagger/local/inputs/arena2 local/inputs/`.
  Nothing is deleted unless you add `--mirror`.
- The `data` lane runs only on den-k8 (`scripts/lane.sh` names it), since the
  cinematics' bytes depend on the machine and its ffmpeg, which the importer
  revision does not cover. Only the lane publishes, and it refuses to replace a
  revision the store holds with other bytes. The store keeps every revision ever
  published (about 2.5 GB each); `scripts/data-sync.sh
  store` lists them. Old ones are pruned by hand, with Patch's say, by removing
  `revisions/<revision>`; no script deletes from the store.
- A branch that changes the importer's inputs lands like any other: commit, run
  `scripts/route.py data <commit> --notify ADDRESS --task N`, route its suites once
  that has published, and push. Every checkout then pulls. No data is moved by hand,
  and no checkout writes another's.
- Windows (den-win11) runs the game and never imports or verifies. Its default
  install is a native clone, `C:\dev\rusty-dagger`, set up as above, whose
  containers come whole from den-agents' shared checkout through `P:` with
  `powershell -File scripts\data-sync.ps1`, which copies only data at the clone's
  own importer revision (the NAS store is not reachable from Windows). `P:`,
  den-agents' home, is mapped only on the console desktop, so that pull and anything that opens a window run
  through `C:\Users\agent\crew\bin\run-on-desktop.ps1`. Running from a den-agents
  worktree over `P:` also works, for uncommitted work or a throwaway pin. Only the
  newest pairs publish win-x64 archives: `rusty install` says
  `RUSTY_INSTALL_NOT_PUBLISHED` for an older pin.
- Every run prints the container's importer revision beside the checkout's. When
  they differ the copy is stale for this tree: pull, and if the store has none,
  import it through the `data` lane.
- The terrain bake is tracked in `src/Daggerfall.Import.Tool/inputs/relief/`
  with the record of what it was baked from. The import's `relief` step takes it
  when the record matches the arena2 and this build's bake; otherwise it bakes
  (14 minutes or more), says which input differed and rewrites those files.
  **Refresh** it with any change to `src/Daggerfall.Import/Relief/` or the bake's
  stages: the `data` lane's import rebakes, says where it left the rewritten
  files on the worker, and you commit them with that change. `KeptReliefTests` fails while they are stale. Git stores the
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

The import and `scripts/data-sync.sh pull` refuse to write through any of these
links, since that would replace what every checkout opens. A worktree whose branch
changes the importer's inputs gets its own data once the `data` lane has
published its commit: `rm local/data local/build/import && scripts/data-sync.sh
pull --import-tree`. Build `first-person.rpak` in a folder of its own the same way.

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
  `inputs/`, makes every machine's data stale until the `data` lane has published
  it. Make one deliberately. An import step
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

- `local/inputs/arena2` and `local/build/import` (Game data says how a box gets
  them), and `ffmpeg` on `PATH`: the import suite converts a
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

On our network a task lands on its focused checks. Whole suites and the full
verify run on the build workers (den-k8, den-nimo, den-m5), not on den-agents
while one of them can take them, and nobody waits for them. From a worktree on
den-agents, for a commit (`HEAD` by default, pushed or not; uncommitted changes
are not in it):

```bash
scripts/route.py ruleset --notify ADDRESS --task N   # the ruleset suite in parts across the workers
scripts/route.py import-suite                        # the import suite, waiting for it; `verify` is verify.sh --import --play
```

With `--notify` (your crew address, given at your session's start) or `--task`
it returns at once, and the result, with each part's log and the tests that
failed, arrives at that address and on that Den task. It first says what each
worker is doing, waits until one frees, and keeps long lanes (verify, import-suite,
`--whole`; not `data`) off the last worker the router can use, so that a split run can always
start. The lanes are
[scripts/lane.sh](scripts/lane.sh), run through the build router
(`den-tool build run`). A lane pulls the data of the commit's importer revision
from the store, and refuses when the store has none: route `data` for that commit
first. A test host that
holds more than three quarters of its worker's memory (at most 32 GiB) is stopped,
and the run names the tests it was running.

`scripts/route.py main` gives the newest `main` commit the verify lane when a
worker is free. It records passes in `refs/notes/verify` and failures in
`refs/notes/verify-failures`. A round started from a Claude Code session also
posts the first failure after a pass to the crew board. On den-agents,
`rusty-dagger-main-check.timer` runs this round every half hour using the
installed build config. Read its journal with
`journalctl --user -u rusty-dagger-main-check.service`. Until crew-services
#10797, the timer's rounds cannot post, so their failures are only in the note:
no post does not mean green. The newest `main` commits with a failure note (the
fetch finds no ref while no round has ever failed):

```bash
git fetch origin refs/notes/verify-failures:refs/notes/verify-failures
git log -10 --notes=verify-failures --format='%h %s%n%N' origin/main
```

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
