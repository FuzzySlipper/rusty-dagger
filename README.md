# Rusty Dagger

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
generic command buses, service locators, or a replacement gameplay DSL.

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

The checked product consumes immutable `Rusty.Engine` package
`0.1.0-dev.11eb8178488c` from the installed `.runtime/sdk-feed` and the
matched `.runtime/runtime-pack`. Start a clean checkout with the pinned,
noninteractive pair install; it validates the release checksum, payloads, ABI,
package version, and Engine source revision before atomically replacing the
whole ignored pair.

```bash
./scripts/install-engine-pair.sh
```

This follows the operational runbook
`rusty-engine/downstream-csharp-sdk-runbook`. The repository neither builds
the pair nor copies an Engine checkout into product sources.

Ordinary edit-run development is CoreCLR through the packaged host:

```bash
npm ci
./.runtime/runtime-pack/bin/rusty dev \
  --project ./src/WorldRpg.Host/WorldRpg.Host.csproj \
  --runtime ./.runtime/runtime-pack
```

Use WASD to move and the mouse to look. **Z** draws or sheathes the equipped
weapon; empty hands use unarmed art. **Left mouse** or **V** swings once per
press while drawn, including empty space. Cooldown, stamina and the active swing
limit repeated attacks; Engine playback returns the weapon to ready. Weapon art
retains classic proportions and fits the bottom center of the view. Engine input capture keeps gameplay
controls out of menus, inventory and console interactions. The DOM renders the
`dagger.hud` projection. The SDK compiles the
product-owned DOM UI and atomically stages the loose Product bundle. The
runtime pack owns the host, browser shell, renderer, and browser transport.

NativeAOT is a separate fidelity/release check, not the edit-run loop:

```bash
dotnet msbuild src/WorldRpg.Host/WorldRpg.Host.csproj -t:VerifyRustyEngineAot
```

Engine contributors may use `rusty dev --engine-source /absolute/rusty-engine`.
That explicit opt-in supplies source references and a source runtime pack;
normal downstream builds never discover an adjacent checkout.

`WorldRpg.SpriteWorkbench` is a separate package-backed authoring product. The
**Sprite animation tool** menu entry gives its launch command and opens port 4175:

```sh
bash src/scripts/run-sprite-workbench.sh
```

The default uses Privateer's Hold and saves to `authoring/sprites/privateers-hold.json`.
To select another publication, writable authoring root, overlay, or port:

```sh
bash src/scripts/run-sprite-workbench.sh content/worldrpg/imports/privateers-hold /absolute/authoring-directory sprites/privateers-hold.json 4175
```

The tool shows the Engine preview alongside atlas/frame inspection, a draggable
pivot, directional review, frame rectangles, and resource/per-animation timing.
Apply updates the preview; Save persists the typed overlay; Discard restores the
saved preview. Reopening applies saved edits. Import regeneration consumes them
with `--sprite-authoring authoring --sprite-overlay sprites/privateers-hold.json`
on the existing import `write` command; generated media stays separate from authored files.

The **Engine debug console** menu entry mounts the upstream console. Start the
host with `--live-debug` (the repository development service already does).
Escape closes the console to the menu, then returns to gameplay.

In the game, **Escape** opens the menu; Escape in a submenu returns to the menu
before returning to play. The menu releases gameplay controls but does not pause
the world. Composition diagnostics are available there. **I** opens inventory and equipment: drag items between the 50-slot pack grid and
compatible equipment slots, or select an item and use the keyboard destination
controls. Drops use the current inventory revision; rejected drops preserve the
items and layout. Slot arrangement lasts for the session, matching the earlier
UI; saves retain items and equipment. **C** opens the read-only character sheet with live resources, attributes, skills,
progression and equipped items. **F** searches the aimed nearby corpse and opens
its loot without taking anything. **Take 1** transfers one stack unit; **Take**
transfers one unique item. Empty loot stays open until Exit. Transfers recheck
visibility, range and the displayed revision; rejected transfers leave contents
unchanged. Both panels are also available from the game menu.

## Content and migration boundary

The product is C#-only. Useful donor semantics have been translated into loaded
content, typed tuning, `Daggerfall.Import`, and compiled Daggerfall ruleset
policy; the former Rust workspace, TypeScript gameplay evaluator, and encounter
demonstration topology are not present as fallback paths.

Daggerfall/Arena2 source data remains operator-supplied. Preserve the checked
in imported/authored assets, attribution, and provenance when adapting content.

## Guidance and proof

Repository-specific instructions are in `AGENTS.md`. For setup, compilation,
and running, use the stable Den runbook
`rusty-engine/downstream-csharp-sdk-runbook`; the older
`rusty-engine/downstream-csharp-agent-brief` remains the ownership reference.

Run `./scripts/verify.sh` after installation for pair verification, pinned UI
dependency installation, focused package restore/build, architecture, CoreCLR
staging, and explicit NativeAOT fidelity proof. Hosted CI is not
declared until immutable Engine artifacts are published for clean runners; do
not replace it with a cloned Engine checkout or downstream provider build.
