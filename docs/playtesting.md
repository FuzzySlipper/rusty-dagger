# Adaptive playtesting

Dagger uses the shared Engine/Crew playtest tools for its live 3D world. The
tester chooses short actions from observations; C# remains the gameplay owner.
Use Crew's [agent prompt guide](https://github.com/FuzzySlipper/crew-services/blob/main/docs/playtest-agent-prompts.md)
and [integration guide](https://github.com/FuzzySlipper/crew-services/blob/main/docs/playtest-product-integration.md).

## Start and ownership

Install the pinned Engine pair with `rusty install`, then start the host with
`--live-debug`, as the repository development service (`.den-serve.json`) does:

```bash
rusty dev --project ./src/WorldRpg.Host/WorldRpg.Host.csproj --live-debug
```

Register the host URL as a local Crew browser profile and use the installed
`playtest` CLI. Run `playtest assist --help` for current syntax.

Give each concurrent tester a separate native host and persistence directory.
Browsers connected to one host share player, world, simulation mode and health.
Reconnecting does not reset gameplay. New Game and Load use the ordinary menu;
the debug callbacks resolve the replacement session on every call.
To start a fresh game quickly, open character creation and press **Quick start**:
it keeps the draft's identity and career, completes the background without
asking, and begins the game.
Only stop browser sessions and hosts the tester owns.

## Agent procedure

1. Discover capabilities, capture the original screen, and observe live mode.
   Complete entry/cinematics through ordinary UI before expecting movement.
2. Set action-driven time. An `act` call delivers the current physical binding,
   advances the requested bounded window, then holds simulation. Look does not
   advance time and does not add a weapon-swing gesture.
3. Choose a nearby named target. Compare actual XYZ movement after short inputs.
   Use free look and a 4/8-view survey when facing a wall or losing orientation.
4. Check alive, hostile, attack eligibility, aim, reach and current refusal facts.
   Attack eligibility alone does not establish hostility. Attack timing is a
   bounded observation window, recalculated from live equipment, speed, authored
   animation and cooldown. Inspect again if still recovering. The reasons
   `attack-animation-active` and `attack-cooldown` identify separate guards;
   `attackReadyAtStep` is a cooldown threshold, not a promise that animation has
   finished.
5. Use ordinary UI for controls, inventory and loot. Rebinding Attack to KeyQ in
   Settings must be visible in the next action query; never assume Primary stays
   bound. The harness supports ordinary pointer buttons as well as keyboard keys.
   Raw DOM/settings input does not automatically advance held time; use a short
   explicit advance to let C# consume the pending UI action, then reobserve.
6. On uncertain delivery, observe before acting again. Do not automatically replay
   attack/use. A delivered swing with no target is a gameplay outcome, distinct
   from a disconnected browser or missing pointer lock.
7. Stop at death, the mission deadline, repeated movement without progress, or an
   unrecoverable transport failure. Preserve original captures and receipts with
   the owning Den record, not in the repository, then give an exit interview:
   progress, blockers, confusing fields, workarounds, missing tools, and the next
   most useful improvement.

## Current facts and limits

- `playtest.observe`: live mode, simulation stamp, character center and camera
  viewpoint, facing, grounded/stance/velocity, resources, equipped weapon,
  bindings, recent combat event and last use step, loot and compact target lists.
- `playtest.action`: current physical binding, tap/hold, duration and availability.
  Preflight reads policy and resources; normal input admission still owns spends,
  cooldown and the final outcome.
- `playtest.look`: relative yaw/pitch through the normal look rules. Changes aim
  and camera without advancing gameplay.
- `navigation.targets`: loaded actors and activation objects with stable IDs,
  3D positions, policy aim points, turn/pitch guidance, distances, attack visibility
  and Engine interaction focus rejection facts. Positions are discoveries, not
  walking routes. Visual angles use the camera; gameplay angles use the ordinary
  character-center targeting origin.

Visibility currently follows gameplay's retained-geometry perception query.
Call-local moving door/support colliders are not included. A visible target is
not proof of collision clearance or a traversable path. Interaction focus facts
also do not promise that a door is unlocked or that the current activation mode
will succeed; normal owner policy runs when the action is delivered.

Floor height and a traversable route are explicitly unavailable. This adapter
has not yet connected the shared probe/grid/capsule/jump helpers to Dagger's full
controller environment, which includes moving supports and special locomotion.
Do not substitute a guessed floor or straight line route. Capture navigation
stalls for a focused follow-up.

HUD outcome text is historical. Compare recent combat event sequence/step and
last use step across receipts. These are practical freshness hints, not proof of
exact timing or deterministic replay. Engine time only advances forward; there
is no rewind, second simulation clock, or scripted combat-roll prediction.

## Developer commands

Developer movement for playtests and manual checks. These are live-debug commands
only (the in-game **Engine debug console**, `rusty-live-debug`, or a playtest's
debug execution); no game surface offers them. Each lands the player through the
same site and movement owners ordinary play uses, so the next save holds the
result. Positions are scene-local, as `navigation.targets` and `playtest.observe`
report them; names with spaces are quoted.

| Command | What it does |
|---|---|
| `world.where` | Current place, site, profile, building, feet and centre, facing, and the nearest targets with their ids. |
| `world.teleport X Y Z` | Stands the player on feet `X Y Z` in the current scene. A spot with no floor within three metres below is refused (an interior is a box in empty space), and so is one the standing body would overlap, with the Engine's clearance facts. |
| `world.teleport-force X Y Z` | The same, placed even with no floor or where the body overlaps collision. |
| `world.move DX DY DZ` | Moves by an offset from the current feet, refusing a spot as `world.teleport` does. |
| `world.forward METRES` | Moves along the facing on the ground plane, refusing a spot as `world.teleport` does. |
| `world.travel PLACE` | Lands at a map place, named (ranked as the travel search ranks: exact, prefix, contains, region) or as `region/index`. It lands as guild teleport does, with no time, cost or encounter. Several matches are listed with their ids. |
| `world.enter PLACE BUILDING` | Travels there if needed, stands at the building's door, and lands inside as the door would, without its greeting, hours or trespass. The building is named or given as `block-x/block-y/index`. Leaving returns to the doorstep. |
| `world.find SERVICE` | The people of the current place offering a service (`training`, `food-drink`, `buy-spells`, `shop`, …): those loaded now with their target ids, and those placed in its buildings, each with the `world.enter` line that reaches it. |
| `world.goto TARGET` | Stands within reach of a loaded person, door or container, facing it. The target is a `navigation.targets` id or label. A door or portal is approached from its outside; other targets from the side nearest the player. The spot must fit the body, have floor below, and have a clear line from the eye to the target. |

For example, to reach the Temple of Kynareth's trainer from anywhere:
`world.enter Charing "Temple of Kynareth"`, then `world.find training`, then
`world.goto Actor:<id>`.

## Developer tools

The **`** key in play, or the game menu's **Developer tools**, opens the Engine's
developer shell docked beside play. It opens only when the host runs with
`--live-debug`; otherwise a note says so. It is diagnostic UI: it lives outside the
game's shell and skin, holds the cursor while open, and gives play its input back
when closed. Its tabs:

- **Dagger:**
  - **Time:** Pause and Resume switch the Engine's held time
    (`engine.time.mode manual` / `realtime`). Step advances it by 0.1 s or 1 s
    (`engine.time.advance`).
  - **Free camera:** flies the Engine's observer (`engine.renderer.camera`) from the
    player's eye with W/S/A/D, R/F (up/down), Q/E (turn) and T/G (look), or the
    buttons. The player never moves. Turning it off, or closing the tools, gives the
    camera back.
  - **Collision:** draws the Engine's collision wires at a radius, and lists the
    colliders it drew (below).
  - **Overlays:** a checkbox per set. A ruleset set is built only while it is on
    (`world.overlay-on` / `world.overlay-off`) and drawn by
    `engine.inspect.overlay show set NAME`.
- **Console, View, Scene, Metrics:** the Engine's own panels. The View pane frames
  the inspection targets `player`, `dialogue` (the person spoken to) and `door` (the
  nearest door or portal).

| Overlay set | What it draws (within 60 m) |
|---|---|
| `interaction` | The activation query's ray, and each target's point, reach sphere and focus reason (green Ready, red Occluded or OutOfReach, grey otherwise). |
| `people` | Each loaded person's height, facing, name, role and services. |
| `doors` | Door and portal points and outward normals, locks, and whether a building's entry rule opens it now. |
| `actors` | Creatures' melee reach, hearing radius (hostile), behaviour state and next navigation waypoint. |
| `markers` | The profile's dungeon enemy and treasure markers, quest markers, anchors and street population. |
| `player` | The standing capsule, the height a step clears, and the floor below (red when there is none). |
| `probe` | The last `spatial.probe` rays or `spatial.clearance` body, from the console. |
| `inspect.targets` | The inspection targets' markers. |

**Collision** in the Dagger panel draws the Engine's retained collision as wires
within a radius of the observer, or of the player's camera without one
(`engine.inspect.overlay show collision RADIUS`; colours and limits in the Engine's
`docs/playtest-inspection.md`). **List colliders here** shows what it drew
(`engine.inspect.collision RADIUS`): each collider's kind and content artifact. A
place's whole exterior, or a whole interior, is one artifact (for example
`charing/exterior/collision-navigation.rspatial`), so the list names the place,
not a building. The drawing stays on after the tools close, as overlay sets do, and
the panel opens showing what the Engine draws.

## Implementation ownership

`WorldRpgProduct` implements `IDebugCommandModuleSource` and registers the shared
Engine module with current-session delegates. The developer commands are Kit's
`DeveloperWorldDebugModule` over the session's `IDeveloperWorldSession`; Dagger
resolves places, buildings and targets. The overlay sets are Kit's
`DeveloperInspectionDebugModule` over `IDeveloperInspectionSession`, with the
Engine's `InspectionTargetsDebugModule` and `SpatialClearanceDebugModule`; Dagger
builds each set from its owners at presentation. The panel is `src/ui/developer.ts`. Kit defines the optional session
interface and read-only targeting mechanisms. Dagger supplies live ruleset facts,
control mappings, hostility, equipment and animation policy. Crew owns bounded
input, time coordination and capture. Querying does not activate targets, spend
resources, replace selected targets, or overwrite last gameplay evidence.
