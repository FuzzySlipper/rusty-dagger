# Rusty Dagger

Daggerfall was a 1996 classic CRPG by Bethesda Softworks. Daggerfall Unity remasters it.
Mods remix it. Rusty Dagger covers it.

Rusty Dagger is the reference repository and proving product for **WorldRpg**, an
opinionated construction kit and host for world-centric, real-time, first-person
systemic RPGs. Daggerfall is its compiled ruleset, compatibility corpus and
content source.

> **Engine guarantees. Kit shapes. Ruleset decides. Bundle assembles. Host launches.**

## Where things live

| What | Where |
| --- | --- |
| Rules for working in this repository | [AGENTS.md](AGENTS.md), and the `AGENTS.md` beside the code in [src/ui](src/ui/AGENTS.md) and the [Daggerfall ruleset](src/WorldRpg.Rulesets.Daggerfall/AGENTS.md) |
| Design and reference (architecture, combat, UI, world, coverage, import, art) | den-wiki, under `wiki/rusty-dagger`; how the ruleset is built (owners by domain folder, the update list, the order and its law) is `wiki/rusty-dagger/architecture` |
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
| `local/inputs/` | `arena2`, `fp-melee` (purchased art) | supplied, never regenerated: keep a copy |
| `local/data/` | `daggerfall-data.rpak`, `music.rpak`, `first-person.rpak`, `animations.rpak` | what the game opens; rebuilt by the import, `authoring/tools/music/build.sh`, `authoring/tools/fp-arms/build.sh` and `authoring/tools/people/splat-people.py`, or pulled by `scripts/data-sync.sh` |
| `local/build/` | import staging and records, the fp-arms build, verify stamps | regenerable |
| `local/art/` | `reference/` exports, `generated/` finals and studies, `review/` pages | the art pipeline's working files ([authoring/README.md](authoring/README.md)) |
| `local/scratch/` | experiments | deletable at any time |
| `local/evidence` | a link to the den NAS's `evidence/rusty-dagger/`, a folder per task (`ln -s /mnt/den-nas-backup/data/evidence/rusty-dagger local/evidence`) | kept off the checkout |

- Supply the game's `ARENA2` directory as `local/inputs/arena2` (or pass
  `--arena2` the folder Daggerfall is installed in). A link is fine.
- A checkout needs the data of its own importer revision: import once, and again
  after a pull that changes the importer revision. Its
  inputs are `src/Daggerfall.Import` and `src/Daggerfall.Import.Tool`, whose
  `inputs/` holds the source inventory and the list of classic art the art pack
  draws anew, the authored UI art with its manifest, and the base pack's
  terrain relief and rivers, which the atlas is drawn from
  (`src/ImporterRevision.targets` lists them). The import never
  reads the authored base payload, so editing it needs no import. The entry screen says when the data is missing, damaged or
  from another importer.

  ```bash
  dotnet run --project src/Daggerfall.Import.Tool -- import --classic-art
  ```

- It checks every input before writing anything, then packs
  `local/data/daggerfall-data.rpak`. The data container leaves out the classic media the art pack draws
  anew, which refreshed art never opens: the entries in
  `src/Daggerfall.Import.Tool/inputs/replaced-classic.json` (promotion writes it,
  so promoting a new entry moves the importer revision) and the sky's original
  panoramas. `--classic-art` packs them into `local/data/classic-art.rpak`, which
  `world.classic-art` needs; a player's import never builds it, and
  `daggerfall-data.rpak` is the same either way. The cinematics are left out, and the game plays them as skipped,
  unless you add `--cinematics` (it needs `ffmpeg` and `ffprobe` on `PATH`). Their
  bytes differ by machine, so data imported with them stays in your own checkout:
  the store refuses it.
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
  current. On den-agents `link` replaces `pull` (below), the shared checkout
  included, which den-win11 reads. When
  the store has no data for the revision, both say so and name the `route.py data`
  command. A box that runs the tests also needs the corpus the import suite reads:
  `rsync -a den-agents:dev/rusty-dagger/local/inputs/arena2 local/inputs/`.
  `--import-tree` makes `local/build/import` match the revision; from `local/data`
  nothing is deleted unless you add `--mirror`.
- The `data` lane runs on any free worker: without the cinematics every machine
  imports the same bytes. Only the lane publishes, and it refuses to replace a
  revision the store holds with other bytes. The store keeps every revision ever
  published (about 2.5 GB each); `scripts/data-sync.sh
  store` lists them. Old ones are pruned by hand, with Patch's say, by removing
  `revisions/<revision>`; no script deletes from the store.
- A branch that changes the importer's inputs lands like any other: commit, run
  `scripts/route.py data <commit> --notify ADDRESS --task N`, route its suites once
  that has published, and push. Every checkout then pulls. No data is moved by hand,
  and no checkout writes another's.
- Windows (den-win11) runs the game and never imports or verifies. Its default
  install is a native clone, `C:\dev\rusty-dagger`, set up as above, which pulls
  with `powershell -File scripts\data-sync.ps1`. The NAS store is not reachable
  from Windows, so it copies the clone's own revision from den-agents' per-revision
  copies (`link`, below) through `P:`. When den-agents holds no copy of that
  revision, it says so and copies nothing: run `scripts/data-sync.sh cache
  REVISION` on den-agents (from any checkout; it fetches the revision from the
  store), then pull again. `P:`, den-agents' home, is mapped only on the console
  desktop, so that pull and anything that opens a window run through
  `C:\Users\agent\crew\bin\run-on-desktop.ps1`. Running from a den-agents
  worktree over `P:` also works, for uncommitted work or a throwaway pin. Only the
  newest pairs publish win-x64 archives: `rusty install` says
  `RUSTY_INSTALL_NOT_PUBLISHED` for an older pin.
- Every run prints the container's importer revision beside the checkout's. When
  they differ the copy is stale for this tree: pull, and if the store has none,
  import it through the `data` lane.
- The terrain relief and rivers are base pack content in
  `content/worldrpg/packs/daggerfall.base/terrain/`, with `relief.json`, the
  digest of the WOODS.WLD they were baked from; the game warns when the
  player's differs. After changing the bake (`src/Daggerfall.Source.Tool/Relief/`)
  or its stages, bake again on den-k8 (about 7 minutes) with
  `dotnet run --project src/Daggerfall.Source.Tool -- relief-bake --arena2 local/inputs/arena2 --update`,
  commit the three files with the change, and route the `data` lane: the atlas
  is drawn from them, so they move the importer revision.
- A location the base pack lays out anew (its `locationOverrides` section, `world/location-overrides.json`,
  such as Hearthwing Manor as the Blades hall) is baked into
  `content/worldrpg/packs/daggerfall.base/locations/` with
  `dotnet run --project src/Daggerfall.Source.Tool -- location-bake --arena2 local/inputs/arena2 --pack content/worldrpg/packs/daggerfall.base`.
  Bake after editing a recipe and commit what it writes with it: the game
  refuses a recipe with no bake, and `LocationBakeTests` fails a bake its
  recipe no longer makes. A block a recipe adds must be one some classic
  location places, since only those are published.
- The store and `data-sync` carry `classic-art.rpak` with each revision's other
  containers, and `status` prints its revision: a developer's checkout has every
  container, so it runs exactly what a player has and can still compare.
- `local/data/first-person.rpak` is optional: 3D first-person arms built from
  purchased art by `authoring/tools/fp-arms/build.sh`. Without it a 3D `viewmodel.style`
  draws the classic weapon sprites, with a warning. It ships with the game packed,
  never as the raw art in `local/inputs/fp-melee`, so it is the one file in the
  store's `ship/` root (beside `lan/`), which holds what ships but lives outside
  GitHub, finals only. `pull` and `link` take it from there; after a rebuild,
  `scripts/data-sync.sh ship first-person.rpak` replaces it there for every checkout.
- `local/data/music.rpak` is the score, Patch's own arrangement, which ships with
  the game and sits beside `first-person.rpak` in the store's `ship/` root, pulled
  and linked the same way and replaced with `scripts/data-sync.sh ship music.rpak`. `authoring/tools/music/build.sh` packs it from
  the Ogg Vorbis finals in the store's `assets/music/`, one `TRACK.ogg` per line of
  `authoring/tools/music/sources.tsv`; `encode.sh` beside it writes them from the
  approved renders in Patch's `music-dev` repository. The tracks are named as the
  playlists name them (`dungeon-01`, `outside-night-03`).
- `local/data/animations.rpak` holds the animation clips, which ship with the game
  but never live in Git: `authoring/tools/people/splat-people.py` packs the
  townspeople's from their NAS sources, the art pack's payload names each by its
  path in the container, and it sits in the store's `ship/` root, pulled and linked
  the same way and replaced with `scripts/data-sync.sh ship animations.rpak`.
  Without it the townspeople draw their sprites, with a warning.
- The values taken from Daggerfall Unity and the classic tables are authored
  data: the classic tables and the game's text are sections of the
  `daggerfall.base` pack's payload, and the quests are the `daggerfall.quests`
  pack, one record per quest in its `quests/` folder. The ruleset reads them at
  load, so an edit plays without an import, and none is an import input. They
  are ours to edit in place (DEC-13); the credit to Daggerfall Unity stays in
  `THIRD_PARTY_NOTICES.md`, and nothing checks them against a Daggerfall Unity
  checkout.

A worktree links the main checkout's inputs, so its build and art files stay its
own, and links its data to its own importer revision; on den machines evidence
links to the NAS:

```bash
main=$(git worktree list --porcelain | head -n 1 | cut -d' ' -f2)
mkdir -p local && ln -s "$main/local/inputs" local/
scripts/data-sync.sh link --import-tree
ln -s /mnt/den-nas-backup/data/evidence/rusty-dagger local/evidence
ln -s "$main/node_modules" node_modules
```

`link` points `local/data` and `local/build/import` at the machine's one
read-only copy of that revision (`~/.cache/rusty-dagger/data/<revision>`, made from
the store on first use), so no checkout copies 4 GB or can write what another
reads. Run it again after a checkout that moves the importer revision, and for a
branch that changes the importer's inputs once the `data` lane has published it.
Tests that read the staging tree and records skip without them. The copies are a
cache, about 4 GB a revision: remove any but the one den-agents' shared checkout
links to (`readlink ~/dev/rusty-dagger/local/data`), with `chmod -R u+w` first. A
worktree that linked a removed one runs `link` again; Windows asks for `cache`. Build
`first-person.rpak` or `animations.rpak` in a folder of the checkout's own.

Rules for the tracked side:

- Only raw media from Daggerfall stays out of Git and out of releases: sprites
  and textures, and sounds and video while they are Daggerfall originals. A fact derived from the game or from Daggerfall Unity (tables,
  formulas, timings, positions, quest text, collision, navigation, ARCH3D
  geometry) may be extracted once into authored data and edited in place, as
  the terrain relief in the base pack was.
  Credit to Bethesda Softworks and Daggerfall Unity stays. This is DEC-13 on
  the wiki page `wiki/rusty-dagger/coverage-scope-and-decisions`. An untracked
  file under `content/` fails the architecture tests.
- A content pack is one folder, `content/worldrpg/packs/<id>/`: its `pack.json`,
  the `payload.json` it names and any media it owns. A tuning profile is
  `content/worldrpg/tuning/<id>/` the same way; bundles stay in `bundles/`. All the
  game's own art is in `packs/daggerfall.art-replacements/`: the refreshed art in
  `media/<area>/`, the authored UI kit in `authored-ui/` (its
  `ui-authored-assets.json` is written by `authoring/tools/ui/ui-art-kit.py
  manifest`) and the held definition and models in `first-person/`.
- `daggerfall.base` is authored and `daggerfall.imported` is generated. A
  payload section belongs to one of them: the reader refuses a section both carry.
- Nothing records where a fact came from or checks it against arena2 or
  Daggerfall Unity (DEC-13). The `CNT-` families on the wiki page
  `wiki/rusty-dagger/content-sources` are reference, not a register the import
  enforces.
- An edit to an importer revision input, including the files in `inputs/`, makes every machine's data stale until the `data` lane has published
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
- **Developer: combat start** begins in Privateer's Hold as a warrior with the
  broadsword drawn and an enemy warrior and barbarian close by, for combat.
- `WORLDRPG_BUNDLE=<bundle id>` on the launch (for example
  `WORLDRPG_BUNDLE=daggerfall.combat-start rusty dev ...`) starts under that
  bundle in place of the remembered choice; Begin then starts play.
- The menu's **Engine debug console** needs the host started with `--live-debug`.
- Engine contributors may pass `--engine-source /absolute/rusty-engine`; nothing
  else discovers an adjacent Engine checkout.

Controls and screens are described on the wiki page `wiki/rusty-dagger/game-ui`.

The political board game runs on its own, without the game or its data, and
prints each week's moves (`--format jsonl` for one JSON object per turn):

```bash
dotnet run --project src/WorldRpg.Politics.Cli -- run --years 10
```

`--format outputs` prints each week's outputs for the world (conditions, region
and seat facts, moves and the chronicle) instead of the trace. `--nudges FILE`
queues the world's nudges, one `{"turn": N, "nudge": {...}}` object a line, and
`--save FILE` / `--load FILE` write the board at the end and resume from it.

`balance` plays seeds 1 to N in parallel and reports the table's balance: each
seat's prestige spread, the rate of allegiance changes, wars and feuds, and the
Empire's reserves and interventions.

```bash
dotnet run -c Release --project src/WorldRpg.Politics.Cli -- balance --seeds 8 --years 50
```

## Verify

```bash
./scripts/verify.sh
```

It installs the pinned pair and UI dependencies, runs the UI tests, builds the
products and the import tool, runs every test project under `tests/`, and stages
the CoreCLR product.

Only verify needs these; a machine that only runs the game can skip them:

- `local/inputs/arena2` and `local/build/import` (Game data says how a box gets
  them). The import suite's cinematic conversion tests run where `ffmpeg` and
  `ffprobe` are on `PATH` and skip by name where they are not.
- For `--play`, a Chromium: `chromium` on `PATH`, or `RUSTY_CHROMIUM_PATH` naming
  one, such as Playwright's (`npx playwright install chromium`; add `--with-deps`
  once, with sudo, for its system libraries).
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

`scripts/route.py main` is one round of `main`'s own check. When the newest
`main` commit has no result, it waits up to an hour for two free workers (one
for itself, one left for split runs), then gives the verify lane to whichever
commit is newest at that moment. It records passes in `refs/notes/verify` and
failures in `refs/notes/verify-failures`; a failure note's first line says
whether the product failed (`scripts/verify.sh`) or the tooling did (the
router's own `scripts/test_*.py`, which the verify lane runs as a separate step,
or the lane and router around it). A round that finds another waiting or running
does nothing. A round started from a Claude Code session also posts the first
failure after a pass to the crew board. On den-agents,
`rusty-dagger-main-check.timer` runs a round every half hour using the
installed build config, so a commit has a recorded round within about three
hours while the workers are free. Until crew-services #10797, the timer's rounds
cannot post, so no post does not mean green. To read the latest round (passed,
failed and which part, waiting, did not start and why, superseded) from any
checkout on den-agents:

```bash
scripts/route.py main-status
```

It exits 1 and says OVERDUE when a commit has gone three hours on `main` without
a recorded round; the journal says why
(`journalctl --user -u rusty-dagger-main-check.service`).

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
daggerfall-data.rpak            written by the import from the player's own files
music.rpak, first-person.rpak, animations.rpak   the score, the 3D arms and the animation clips, built on a development checkout and placed beside
```

The player imports once, from the game: while the data is missing, damaged or
from another importer, the entry screen says why and offers **Import**, runs the
shipped tool on the folder they pick and opens the game when it finishes. The
tool can also be run by hand.

## Acknowledgements

Rusty Dagger stands on the work of the [Daggerfall Unity](https://github.com/Interkarma/daggerfall-unity)
project (Daggerfall Workshop and its contributors). Its years of reverse-engineering of the
classic file formats, formulas and behaviour are the reference this project's importer and
ruleset are checked against, and several import tables and playlists come from it. It is used
under the MIT licence; see [third-party notices](THIRD_PARTY_NOTICES.md).
*The Elder Scrolls II: Daggerfall* is Bethesda Softworks' game, and its data is supplied by the player. Bethesda remains the credited author of the inspiration for ideas that were further developed here.
