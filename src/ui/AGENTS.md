# Game UI rules

On top of the root `AGENTS.md` and the Engine's game-UI rules.

## Ownership

- C# decides. Draw the projection, send semantic actions, redraw from the next snapshot. Never decide a rule or move state optimistically; a missing fact means the projection grows.
- No exception text, parameter names or internal IDs in player-facing text.
- Diagnostic UI (`developer.ts`) mounts beside the game's shell and never shapes a game surface.

## The window

- Servo: no `::backdrop` (dim with `--dagger-scrim`), no `:has()`; focus rings also match `.keyed-navigation :focus`.
- Inline SVG: paint, `display` and `font-family` in attributes, no `vector-effect`, hover and focus state set by script; pointer events reach only the `<svg>`.
- Never `alert`, `confirm`, `prompt`, `title` for information, `window.open`, `target="_blank"`, native file or colour pickers, or native HTML5 drag.

## Input

- Every action works by mouse, keyboard and controller, each alone. Nothing is hover-only or drag-only.
- A cursor surface registers with `interaction.ts` and `navigation.register`. No screen keeps a controller table.
- Drags use `drag.ts`, confirmations `confirm.ts`, tooltips `data-tooltip`, amounts `stepper.ts` with the projection's limits.
- Rebuild controls through `redraw`, so focus survives the per-update snapshot.

## Scale and skin

- Size in `rem`, hairlines excepted. Only `scale.ts` calls `context.ui.setScale`. Lay out for 1920x1080 at scale 1.
- Screens lay out; they set no frame, button or slot art. Every skin rule keeps a plain-CSS look for a missing image.

| Hook | Draws |
| --- | --- |
| `.dagger-frame` | Stone window |
| `.dagger-frame[data-skin="parchment"]` | Parchment to read; controls stay stone |
| `.dagger-frame-title` | Brass title plate |
| `.dagger-panel` | Recessed stone inset |
| `.dagger-divider` | Brass rule |
| `button`, `[role="tab"]`, `select` | Stone button, tab, brass plate |
| `.dagger-slot` | Stone slot; `is-drop-invalid` is red |
| `[data-icon]` | Kit icon before the label |
| `[data-inventory-equipment]` | Empty slot's silhouette |

## Authored art

1. Add the source prompt and published entry to `scripts/ui-art-kit.py`.
2. Run its `generate`, pick a seed, then `process` and `manifest`.
3. Run the import: `dotnet run --project src/Daggerfall.Import.Tool -- import --music local/inputs/Sound`.

- An original is at most 256 px a side, published unresampled. `display.scale` is 0.5; `display.slice` is top, right, bottom, left in image pixels.
- Draw kit art smoothed. `image-rendering: pixelated` and `fitPixelArt` are for classic art, which is never committed.

## Done

- `node --test` tests in `tests/WorldRpg.Ui.Tests` cover it, with C# tests for any projection or action added.
- Checked in `rusty dev --output window` at 1920x1080 or larger, by mouse, keyboard and controller.

Fuller account: `wiki/rusty-dagger/game-ui`.
