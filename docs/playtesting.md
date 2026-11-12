# Adaptive playtesting

Dagger uses the shared Engine/Crew playtest tools; the tester chooses short actions from observations and C# stays
the gameplay owner. Follow Crew's
[agent prompt guide](https://github.com/FuzzySlipper/crew-services/blob/main/docs/playtest-agent-prompts.md) and
[integration guide](https://github.com/FuzzySlipper/crew-services/blob/main/docs/playtest-product-integration.md).
This file adds what is Dagger's. The developer tools panel is on the wiki page `wiki/rusty-dagger/game-ui`.

## Start and ownership

```bash
rusty install
rusty dev --project ./src/WorldRpg.Host/WorldRpg.Host.csproj --live-debug
```

- Register the host URL as a local Crew browser profile and use the `playtest` CLI (`playtest assist --help`).
- Give each concurrent tester its own native host and persistence directory: browsers on one host share the player,
  world, simulation mode and health. Stop only the sessions and hosts you own.
- Reconnecting does not reset gameplay. New Game and Load use the ordinary menu.
- A fresh game quickly: character creation's **Quick start**. No creation or cinematics at all: the title's
  **Developer: Charing town start** or **Developer: wilderness start**.

## Agent procedure

1. Finish entry and cinematics through the ordinary UI before expecting movement.
2. Set action-driven time. Look does not advance time.
3. Compare actual XYZ movement after short inputs; survey 4 or 8 views when facing a wall.
4. Attack eligibility does not establish hostility: check hostile, aim, reach and the refusal facts. An attack is a
   tap that the rules' attack clock plays out on later steps. `attack-animation-active` and `attack-cooldown` are
   separate guards; `attackReadyAtStep` is a cooldown threshold, not the end of the motion.
5. Read the binding from the next action query; never assume Primary stays bound. Raw DOM or settings input does not
   advance held time: advance briefly so C# consumes the pending UI action, then observe again.
6. On uncertain delivery, observe before acting again; never replay attack or use automatically. A delivered swing
   with no target is a gameplay outcome, not a transport failure.
7. Stop at death, the deadline, repeated movement without progress or a transport failure. Captures and receipts go
   with the owning Den record, never into the repository. Give an exit interview.

## Limits

- Floor height and a traversable route are not available. Never substitute a guessed floor or a straight line;
  capture a navigation stall for a follow-up.
- A visible target is not proof of collision clearance; moving door and support colliders are not in the
  visibility query. Interaction focus does not promise a door is unlocked.
- `playtest.action` preflight reads policy and resources; input admission still owns spends, cooldown and outcome.
- HUD outcome text is historical: compare the combat event's sequence and step, and the last use step, across
  receipts. Engine time only moves forward.

## Developer commands

Every developer command (`world.*`, `player.*`, `people.*`, `held.*`) carries its description in code:
`[DebugCommand]` in `src/WorldRpg.Kit/Developer*.cs`. A `--live-debug` host serves them as its debug catalogue,
which `rusty-live-debug --origin URL` (`help`, `complete world.`) and the in-game Engine debug console read.

- Positions are scene-local, as `navigation.targets` reports them. Quote names with spaces.
- They act through the owners ordinary play uses, so the next save holds the result, and they wait while a screen
  or question is open.
- Reaching a trainer: `world.enter Charing "Temple of Kynareth"`, `world.find training`, `world.goto Actor:<id>`.
- Comparing old and new art: `world.classic-art on` draws the classic art for the run, `off` the refreshed art.
  No settings screen offers it, and nothing keeps it past the run.
- `scripts/tour/tour.py` and `cost.py` drive them over many scenes (usage in their headers).

## Implementation ownership

`WorldRpgProduct` registers the Engine's debug module with current-session delegates. Kit owns the developer
modules over optional session interfaces and the read-only targeting mechanisms. Dagger supplies the live ruleset
facts: places, buildings and targets, control mappings, hostility, equipment and animation policy. Crew owns bounded
input, time coordination and capture. A query never activates a target, spends a resource, replaces the selected
target or overwrites the last gameplay evidence.
