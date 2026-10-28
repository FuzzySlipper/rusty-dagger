# Rusty Dagger

For agent-driven gameplay checks, see [adaptive playtesting](docs/playtesting.md).

Rusty Dagger is the reference repository and proving product for WorldRpg.

WorldRpg is an opinionated construction kit and host for world-centric,
real-time, first-person systemic RPGs. The world is the durable center of
gravity. Story, quests, progression, combat, and characters are mechanisms for
inhabiting and unfolding the world, not a mandatory linear product spine.

Daggerfall is the first compiled ruleset, compatibility corpus, content source,
and game-bundle family. It is not the implicit WorldRpg architecture.

The working formula is: **Engine guarantees. Kit shapes. Ruleset decides.
Bundle assembles. Host launches.**

Ownership:

- Rusty Engine guarantees reusable infrastructure and admitted update services.
- WorldRpg.Kit defines the reusable world-RPG composition grammar and the
  ordinary mechanisms needed to construct a world RPG.
- WorldRpg.Host owns the product lifecycle, built-in ruleset registry, shipped
  bundles, launcher, defaults, and session selection.
- WorldRpg.Rulesets.Daggerfall owns all Daggerfall-specific semantics, formulas,
  identities, policies, presentation meaning, and content interpretation.
- Content packs own authored definitions, assets, worlds, placements, quests,
  and scenario state.
- Daggerfall.Import owns Arena2 and Daggerfall Unity source knowledge.
- `WorldRpg.Host` is the ordinary product entry. The packaged SDK generates
  CoreCLR and NativeAOT composition beneath ignored `obj` output.

Code-bearing rulesets are compiled into the product. Content packs, validated
typed tuning profiles, and game bundles are loaded at runtime. Do not introduce
dynamic managed plug-in loading, reflection discovery, runtime C# compilation,
generic command buses, ambient dependency lookup, or a replacement gameplay DSL.
Named explicitly composed Kit services and typed participant-contributed
RuleEvents are appropriate for modular gameplay.

Reusable mechanisms, and mechanisms whose placement is genuinely uncertain,
begin in WorldRpg.Kit. Daggerfall retains only its identities, formulas, attack
and reward policy, content interpretation, presentation meaning, and source
quirks. The canary validates this seam later; it is not a promotion gate.

Daggerfall-specific assumptions are forbidden in WorldRpg.Kit. Concrete
ruleset references are permitted in WorldRpg.Host only at the explicit built-in
composition root.

Adjustable ruleset values belong in discoverable validated typed tuning handles;
authored actor, item, and world values belong in content packs; algorithmic
invariants stay beside their algorithms; source quirks belong in Daggerfall.Import;
and default bundle selection belongs in WorldRpg.Host.

There is one Rusty Engine-admitted update. WorldRpg does not create a parallel
loop, clock, timer, browser authority, or renderer.

For every task, identify:

- owning layer;
- new assumptions introduced;
- whether Daggerfall vocabulary is permitted;
- whether the change is code, tuning, content, import, or infrastructure;
- dependency changes;
- focused proof for the owning mechanism and ruleset policy.

## Current gameplay shape

The Kit uses composed actors over canonical class components and named Combat,
Targeting, Ai, Loot, inventory/equipment, and progression services.
`DaggerActorFactory` assembles Daggerfall actors, while `DaggerfallState.Kit`
provides the session's discoverable gameplay owners. `DaggerCombatRules` supplies
Daggerfall formulas, eligibility, timing, and content meaning. Ordinary gameplay
uses direct live state; typed RuleEvents and notifications serve interactions with
real contributors.

`DaggerSessionPersistence` captures current source-generated state through the
Host persistence API. It carries charged cooldowns but rebuilds or drops held
input, AI/perception work, native continuation, presentation, and an in-flight
attack. The game menu's **Save game** and **Load game** panels manage named
save slots, and the death screen offers loading a saved game. Development
supports one current schema only: no versions, migration paths, compatibility
fingerprints, or unknown-field preservation. See
[actors and live mechanics](docs/actors-and-mechanics.md), the
[code and ownership map](docs/code-migration-map.md), and the
[combat behavior baseline](docs/combat-behavior-baseline.md).

## Ownership

> The product decides. The Engine guarantees.

C# owns gameplay state, entities, services, orchestration, content meaning, and
product policy. Engine supplies reusable infrastructure through generated,
direct, named service APIs.

Engine owns rendering resources, retained projection, frame construction,
backend realization, and canvas lifecycle. C# publishes product facts; it does
not build another renderer. TypeScript owns DOM UI only and must not acquire
authoritative gameplay state or render non-UI game elements.

Rust admits product updates. Dagger may order ordinary C# services with the
optional `Rusty.Engine.Application` pipeline or implement
`IEngineProduct.Update` directly. Dagger does not start a second loop, timer,
thread, or clock.

If a required capability is absent from the safe Engine API, the correct result
is a narrow upstream Engine request and an honest stop. Do not recreate Engine
machinery in C#, TypeScript, downstream Rust, a fake proof path, or a parallel
host merely to finish a task.

## Develop and verify the current product

The product consumes one immutable Engine SDK/runtime pair, pinned by
`<RustyEnginePackageVersion>` in `Directory.Build.props`; do not restate a
version here. The Engine's `rusty` command installs, updates and runs it. Get
`rusty` once with the Engine bootstrap
(`curl -fsSL https://raw.githubusercontent.com/FuzzySlipper/rusty-engine/main/scripts/install-rusty.sh | bash`),
then start a clean checkout with:

```bash
rusty status
rusty install
```

To take the newest published Engine pair, which is the ordinary way to pick up
newer Engine state:

```bash
rusty update
```

It installs the pair, rewrites the pin, and lists the release notes to read.
`rusty update --check` reports what is available without changing anything.

The game content is not in the repository: it is converted from your own copy of
Daggerfall. Supply the game's `ARENA2` directory as `local/arena2` (or pass
`--arena2` the folder Daggerfall is installed in, which holds it) and the song
folder as `local/Sound` (a link is fine; `local/` is ignored), then import the
content once per checkout, and again after an importer change (the entry screen
says when the data is stale). The Daggerfall Unity tables and quest scripts it
reads are tracked in `data/daggerfall-unity`. FFmpeg on `PATH` is optional: without
it the cinematics are not converted and the game plays without them.

```bash
dotnet run --project src/Daggerfall.Import.Tool -- import --music local/Sound
```

It runs on Linux and Windows, checks every input before writing anything, and packs
the converted data into `local/daggerfall-data.rpak` and the music into
`local/music.rpak` (one recording per classic track, `<track>.<ext>`, in any format
the Engine plays; `--music` is optional). The product opens both beside itself:
from `local/` under `rusty dev`, beside `product.rpak` in a release. To work on the
importer, rerun one command (its staging paths are the defaults) or `import --only
STEP`, then `pack`.

Ordinary edit-run development is CoreCLR through the pinned runtime:

```bash
npm ci
rusty dev --project ./src/WorldRpg.Host/WorldRpg.Host.csproj
```

The title screen's **Game** selector chooses the game bundle a new run starts
under (classic Daggerfall, or a rules variant such as contact + mitigation),
and the choice is remembered for the next start. A save loads only under the
game it was made in. Character creation's **Quick start** begins a new game at
once from the draft's choices. For playing and capturing town services, the
developer game **Developer: Charing town start** begins in Charing's streets at
nine in the morning with 5000 gold and no opening quests; its
`daggerfall.new-game-start` pack overrides only the base pack's start site, gold,
quests and start hour.

Without the generated content, staging the product stops with a message naming
the script. See [third-party notices](THIRD_PARTY_NOTICES.md) for what is and is
not redistributed.

Use WASD to move and the mouse to look. **Z** draws or sheathes the equipped
weapon; empty hands use unarmed art. **Left mouse** swings once per press by
default while drawn, including empty space. Controls can rebind the attack.
Cooldown, stamina and the active swing
limit repeated attacks; Engine playback returns the weapon to ready.

Stamina recovers at five points per second after two seconds without an admitted
swing, including from exhaustion. The Daggerfall tuning payloads control this
automatic recovery; it is separate from classic rest-based fatigue recovery.

Weapon art retains classic proportions and fits the bottom and authored side of
the view. Engine input capture keeps gameplay
controls out of menus, inventory and console interactions. The DOM renders the
`dagger.hud` projection. The SDK compiles the
product-owned DOM UI and atomically stages the loose Product bundle. The
runtime pack owns the host, browser shell, renderer, and browser transport.

NativeAOT is a separate fidelity/release check, not the edit-run loop.
`./scripts/verify.sh --aot` runs it as part of verification, or invoke it
directly:

```bash
dotnet msbuild src/WorldRpg.Host/WorldRpg.Host.csproj -t:VerifyRustyEngineAot
```

### A release for players

`rusty build --release <dir>` (with `--aot` for NativeAOT on Linux) writes a
directory that runs where no .NET, `rusty` or checkout is installed:

```text
rusty-dagger[.exe]          the launcher a player starts; the game opens its own window
product.rpak                the product: UI, tracked content (no Daggerfall data)
coreclr/ | native/          the product's code
bin/, share/, dotnet/       the Engine host, its licences and the .NET runtime
tools/Daggerfall.Import.Tool/   the import, with the tracked files it reads
daggerfall-data.rpak, music.rpak   written by the import from the player's own files
```

The player imports once, from the game: while the data is missing, damaged or
from another importer, the entry screen says why and offers **Import**. It shows
the desktop's folder picker for the folder Daggerfall is installed in (or its
`ARENA2` folder), runs the shipped import tool there with its output on screen,
and opens the game when it has finished, with no restart. The tool can also be
run by hand, or a folder dropped onto
`tools/Daggerfall.Import.Tool/Daggerfall.Import.Tool`, adding `--music <folder>`
for the score. It writes the containers beside `product.rpak` and keeps nothing
else.
A release build outside `rusty dev` opens a window; the dev loop keeps streaming
to the browser page unless given `--output window`.

Engine contributors may use `rusty dev --engine-source /absolute/rusty-engine`.
That explicit opt-in supplies source references and a source runtime pack;
normal downstream builds never discover an adjacent checkout.

For image-generation reference sets, the importer can export every item template
and artifact, and the spell icons with their casting and world-effect companions:

```sh
dotnet run --project src/Daggerfall.Import.Tool -- export-items
dotnet run --project src/Daggerfall.Import.Tool -- export-spells
```

These write `local/exports/daggerfall-items-r01/` and
`local/exports/daggerfall-spells-r01/`. Each has an `index.html` visual catalog,
CSV index and JSON manifests; each item or spell has its own directory with
native-resolution PNGs and source record/frame identities. Items include player
body and clothing variants, dyes, masks, artifacts and applicable normal/enchanted
first-person animation frames. Spells link all selectable icons to the independent
element and target choices that select casting hands and world effects. Missing
source art is recorded explicitly. Pass `--output` with a fresh directory for a
new export; existing experiments are preserved. `--arena2` relocates the source,
and `export-items --donor` relocates the Daggerfall Unity input tables. The
extracted images stay in ignored `local/`.

`WorldRpg.SpriteWorkbench` is a separate package-backed, developer-only authoring
product. It reads the neutral inspection document the importer's `sprite-inspection`
command writes (the launch script stages it) and never references the importer.
The **Sprite animation tool** menu entry gives its launch command and opens port 4175:

```sh
bash src/scripts/run-sprite-workbench.sh
```

The default uses Privateer's Hold and saves to `authoring/sprites/privateers-hold.json`;
`authoring/` is operator-local output and ignored by Git. To select another
publication, writable authoring root, overlay, or port, pass them in order or set
`RUSTY_WORKBENCH_PUBLICATION`, `RUSTY_WORKBENCH_AUTHORING`, `RUSTY_WORKBENCH_OVERLAY`
or `RUSTY_WORKBENCH_PORT`; an omitted argument falls back to its variable, then the default:

```sh
bash src/scripts/run-sprite-workbench.sh content/worldrpg/imports/privateers-hold /absolute/authoring-directory sprites/privateers-hold.json 4175
RUSTY_WORKBENCH_PORT=4176 RUSTY_WORKBENCH_OVERLAY=sprites/rats.json bash src/scripts/run-sprite-workbench.sh
```

The tool shows the Engine preview alongside atlas/frame inspection, a draggable
pivot, directional review, frame rectangles, and resource/per-animation timing.
Apply updates the preview; Save persists the typed overlay; Discard restores the
saved preview. Reopening applies saved edits. The import consumes them with
`import --sprite-authoring authoring`, which passes each site's `sprites/SITE.json` to
its `write` step; generated media stays separate from authored files.

The **Engine debug console** menu entry mounts the upstream console. Start the
host with `--live-debug` (the repository development service already does).
The console also takes the developer movement commands (`world.travel`,
`world.enter`, `world.goto`, `world.teleport`, …) listed in
[adaptive playtesting](docs/playtesting.md#developer-commands).
Escape closes the console to the menu, then returns to gameplay.

In the game, **Escape** opens the menu; Escape in a submenu returns to the menu
before returning to play. Control settings offer rebinding, explicit conflict swaps and reset;
preferences persist across restart and loading a save. Character choices are available
from the character sheet at the title screen, and commit name, race, gender, face,
reflexes and predefined career. Once play begins the sheet is read-only.
The menu pauses the world and releases gameplay controls. Its **Action-driven time**
toggle optionally pauses during idle play and looking around, advancing time for movement
and deliberate actions. Swings, projectiles, door motion and jumps finish before time holds.
The HUD shows **Paused** or **Acting** while enabled; the choice persists with player
preferences. Turn it off to return to continuous realtime play. Composition diagnostics
are available in the menu. **I** opens inventory and equipment: drag items between the 50-slot pack grid and
compatible equipment slots, or select an item and use the keyboard destination
controls. Drops use the current inventory revision; rejected drops preserve the
items and layout. Slot arrangement lasts for the session, matching the earlier
UI; saves retain items and equipment. **C** opens the character sheet with live resources, attributes, skills,
progression and equipped items. **F** searches the aimed nearby corpse and opens
its loot without taking anything. **Take 1** transfers one stack unit; **Take**
transfers one unique item. Empty loot stays open until Exit. Transfers recheck
visibility, range and the displayed revision; rejected transfers leave contents
unchanged. Both panels are also available from the game menu.

Controller menus use the Engine-owned interface input observations: **Start**
opens the menu, **D-pad up/down** or a fresh **left-stick up/down** deflection
moves visible focus, **A** activates the focused control, and **B/Start** returns
from a submenu or closes the main menu. **Back** opens inventory and **Y** opens
the character sheet. **A** also activates Begin on the title screen. Menu
selection has no repeat timer; neutralize the stick before another step.

## Content and migration boundary

The product is C#-only. Useful donor semantics have been translated into loaded
content, typed tuning, `Daggerfall.Import`, and compiled Daggerfall ruleset
policy; the former Rust workspace, TypeScript gameplay evaluator, and encounter
demonstration topology are not present as fallback paths.

Daggerfall/Arena2 source data remains operator-supplied, and nothing converted from it is
committed. The import command rebuilds every derived file into the containers and records
under `local/` (ignored by Git) from `local/arena2`, the donor checkout and a music folder;
`content/` holds only tracked files. [Content scope](docs/coverage/content-scope.md) lists
what is generated and what is authored.
Preserve the authored assets, attribution, and provenance when adapting content.

## Acknowledgements

Rusty Dagger stands on the work of the [Daggerfall Unity](https://github.com/Interkarma/daggerfall-unity)
project (Daggerfall Workshop and its contributors). Its years of reverse-engineering of the
classic file formats, formulas and behaviour are the reference this project's importer and
ruleset are checked against, and several import tables and playlists come from it. It is used
under the MIT licence; see [third-party notices](THIRD_PARTY_NOTICES.md). *The Elder Scrolls II:
Daggerfall* is Bethesda Softworks' game, and its data is supplied by the player.

## Guidance and proof

Repository-specific instructions are in `AGENTS.md`. For setup, compilation,
and running, use the stable Den runbook
`rusty-engine/downstream-csharp-sdk-runbook`; the older
`rusty-engine/downstream-csharp-agent-brief` remains the ownership reference.

Run `./scripts/verify.sh` after installation. It installs the pinned pair and
UI dependencies, runs the UI tests, builds the products and the import tool,
runs every test project under `tests/` (an architecture law keeps that list
complete), and stages the CoreCLR product. Options:

- `--play` also starts the product on its runtime, presses Begin in a headless
  Chromium, creates and commits a character, begins the new game and passes
  once the game reaches ordinary play with no error or
  terminal diagnostic (about four minutes, most of it the opening cinematics);
- `--aot` also runs the NativeAOT fidelity publish;
- `--import` runs the import suite even when nothing it reads (the importer revision's
  inputs, the tracked `content/`, its own sources, the Engine pin, the corpus, the donor
  checkout, or the data container, staging tree and records) changed since its last green
  run on this machine, which it otherwise skips with the reason printed (the stamp is
  `local/verify/import-suite.stamp`);
- `--record` attaches the green run's summary to `HEAD` as a git note in
  `refs/notes/verify` and pushes it; read it with
  `git log --notes=verify` after `git fetch origin refs/notes/verify:refs/notes/verify`.

There is no hosted CI: most suites read the operator's Arena2 corpus and the content
generated from it, which a clean runner does not have, so the recorded local run is the
gate's record. Without the data container the script says so, skips the ruleset suite with the
reason printed, and refuses `--play`.

To regenerate only the staged Product, use:

```sh
dotnet msbuild src/WorldRpg.Host/WorldRpg.Host.csproj -t:StageRustyEngineCoreClrProduct -p:Configuration=Release
```

A plain `dotnet build` compiles assemblies without invoking that staging target.
For real-host verification, use ordinary `rusty dev`; the host's `--exercise`
option expects Engine fixture content and callbacks, not this product.
