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
Daggerfall. Supply the game's `ARENA2` directory as `local/arena2` and the song
folder as `local/Sound` (a link is fine; `local/` is ignored), have a Daggerfall
Unity checkout (default `/home/research/daggerfall-unity`, or `DAGGER_DONOR_ROOT`)
and FFmpeg on `PATH`, then generate the content once per checkout, and again after
an importer change:

```bash
scripts/regenerate-content.sh
```

Ordinary edit-run development is CoreCLR through the pinned runtime:

```bash
npm ci
rusty dev --project ./src/WorldRpg.Host/WorldRpg.Host.csproj
```

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

Engine contributors may use `rusty dev --engine-source /absolute/rusty-engine`.
That explicit opt-in supplies source references and a source runtime pack;
normal downstream builds never discover an adjacent checkout.

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
saved preview. Reopening applies saved edits. Import regeneration consumes them
with `scripts/regenerate-content.sh --sprite-authoring authoring`, which passes each site's
`sprites/SITE.json` to the import `write` command; generated media stays separate from authored files.

The **Engine debug console** menu entry mounts the upstream console. Start the
host with `--live-debug` (the repository development service already does).
Escape closes the console to the menu, then returns to gameplay.

In the game, **Escape** opens the menu; Escape in a submenu returns to the menu
before returning to play. Control settings offer rebinding, explicit conflict swaps and reset;
preferences persist across restart and loading a save. Character choices are available
from the character sheet at the title screen, and commit name, race, gender, face,
reflexes and predefined career. Once play begins the sheet is read-only.
The menu releases gameplay controls but does not pause
the world. Composition diagnostics are available there. **I** opens inventory and equipment: drag items between the 50-slot pack grid and
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
committed. `scripts/regenerate-content.sh` rebuilds every derived file (under `content/` and
`import-records/`, all ignored by Git) from `local/arena2`, the donor checkout and `local/Sound`;
[content scope](docs/coverage/content-scope.md) lists what is generated and what is authored.
Preserve the authored assets, attribution, and provenance when adapting content.

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
- `--record` attaches the green run's summary to `HEAD` as a git note in
  `refs/notes/verify` and pushes it; read it with
  `git log --notes=verify` after `git fetch origin refs/notes/verify:refs/notes/verify`.

There is no hosted CI: most suites read the operator's Arena2 corpus and the content
generated from it, which a clean runner does not have, so the recorded local run is the
gate's record. Without the generated content the script says so, skips the ruleset suite and
product staging with the reason printed, and refuses `--play` and `--aot`.

To regenerate only the staged Product, use:

```sh
dotnet msbuild src/WorldRpg.Host/WorldRpg.Host.csproj -t:StageRustyEngineCoreClrProduct -p:Configuration=Release
```

A plain `dotnet build` compiles assemblies without invoking that staging target.
For real-host verification, use ordinary `rusty dev`; the host's `--exercise`
option expects Engine fixture content and callbacks, not this product.
