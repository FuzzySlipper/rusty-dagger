# Game UI

How rusty-dagger builds player-facing UI on Rusty Engine. It applies the
Engine's game-UI green path to this product and adds the conventions the
product chose on top. The Engine documents own the Engine rules; this file
cites them rather than restating them:

- [Game UI and diagnostic UI](https://github.com/FuzzySlipper/rusty-engine/blob/main/docs/csharp-product-style.md#game-ui-and-diagnostic-ui)
  and [Drag and drop](https://github.com/FuzzySlipper/rusty-engine/blob/main/docs/csharp-product-style.md#drag-and-drop)
- [Browser features in the window](https://github.com/FuzzySlipper/rusty-engine/blob/main/docs/desktop-shell.md#browser-features-in-the-window)
- [Gameplay cursor mode](https://github.com/FuzzySlipper/rusty-engine/blob/main/docs/csharp-product-project.md#gameplay-cursor-mode),
  [Pause and resume from product UI](https://github.com/FuzzySlipper/rusty-engine/blob/main/docs/csharp-product-project.md#pause-and-resume-from-product-ui)
  and [Controller input in product menus](https://github.com/FuzzySlipper/rusty-engine/blob/main/docs/csharp-product-project.md#controller-input-in-product-menus)

## Two kinds of UI

**Game UI** is what a player uses: the HUD, menus, inventory, character
sheet, dialogue, shops, maps, journal, settings, title, character creation
and death. It keeps to the green path and is checked in
`rusty dev --output window` before it is called done, because the window is
what ships. A browser tab is for iterating.

Each surface is its own module in `src/ui` (`menu.ts`, `dialogue.ts`,
`hud.ts`, `death.ts`, `entry.ts`, `choices.ts`, `dungeon-text.ts` beside the
panel modules); `main.ts` only mounts them and routes each snapshot.

**Diagnostic UI** is for developers and agents: the diagnostics and debug
panels, tuning and test controls. It may use any browser feature and may be
exercised only in a browser. It is kept apart in the page and visibly marked
as diagnostic, so its needs never shape a game surface.

## Ownership

- C# owns state, rules and eligibility. The ruleset publishes what a screen
  shows through its projections (`DaggerfallHudProjection` and the
  presentation owners beside it), including whether an action is allowed and
  why not.
- TypeScript in `src/ui` is thin DOM presentation. It reads the projection,
  draws it, and sends semantic actions with `context.intents.claim('dagger.ui',
  …)` under the `dagger.ui.action.v1` contract, which
  `DaggerfallSession.UiActions` admits.
- The UI never decides a rule, never moves state optimistically, and redraws
  from the next projection. When a screen needs a fact the projection lacks,
  the projection grows; TypeScript does not compute it.
- Text the player reads is player-facing: no exception text, parameter names
  or internal IDs. Refusals come worded from the ruleset.

## Input

**One interaction lifecycle** (`src/ui/interaction.ts`). Interface versus
gameplay mode is decided in one place: the projection says whether its mode
or question needs the cursor, and each surface registers a predicate with
`needsCursor`. Entering interface mode releases pointer lock
(`context.ui.setInteractionMode`). Leaving the last cursor surface re-takes
the lock with `context.ui.focusGameplay()`, so closing a menu needs no click.
A free-cursor screen in play (a map) uses `context.ui.setCursorMode` and
returns to `'pointer-lock'` when it closes.

**Escape and focus.** Under pointer lock, Escape ends the lock and the page
sees the lock end rather than the key; the product treats a lock lost during
play as a request for the menu. On the window's `blur`, held state is dropped
and any drag is cancelled.

**Every action three ways.** Each game-UI action works with mouse alone,
keyboard alone and controller alone:

- Mouse: DOM pointer events and `click`. No hover-only information: anything
  shown on hover is also shown on focus.
- Keyboard: Tab and Shift+Tab move between controls, arrows move inside lists
  and grids, Enter accepts, Escape backs out of the innermost surface. Focus
  is always visible and returns to where it was when a panel reopens.
- Controller: interface observations from `context.input.subscribe` while the
  page holds interface mode. Accept, back, previous and next panel, and
  directional movement through lists and grids map onto the same focus model
  as the keyboard. Hints name the bindings of the device in use.

**One focus-navigation owner** (`src/ui/navigation.ts`) serves every surface;
no screen keeps a controller table of its own. A surface that takes the
cursor registers with `navigation.register` in `main.ts`:

- `root` and `layer`: the topmost open surface is the focus scope. Directions
  and arrows move to the nearest control in that direction, so a list and a
  slot grid need nothing more. Tab stays inside the surface.
- `isOpen()`: when a surface becomes topmost it takes focus (its last-focused
  control, `initialFocus()`, `[autofocus]` or its first control), and focus
  goes back to where it was when the last surface closes.
- `back(source)`: Escape and the controller's Back. A surface without it is a
  question the product needs answered (death, title, a court, a summoning
  offer, a quest prompt), and neither press acts on it or on what it was asked
  over.
- `panel(direction)`: the shoulder buttons, which step the menu's panels.

A focused `<select>`, number or range field takes the controller's left and
right as a change of value, so native choices and amounts work from a pad.
Hints at the foot of the topmost surface name the keys or buttons of the
device in use for what that surface accepts.

Controller buttons follow the standard layout the Engine normalizes: A
(`button-0`) accepts, B (`button-1`) backs out, the D-pad (`button-12`–`15`)
and the left stick (`axis-0`, `axis-1`) move, LB and RB (`button-4`, `5`)
step panels. With the menu open, Start (`button-9`) steps out as Back does,
Select (`button-8`) opens the inventory, Y (`button-3`) the character, and X
(`button-2`) a context menu on the focused item.

## Drag and drop

Native HTML5 drag and drop is not used. The desktop window refuses it (the
owner's decision on rusty-engine #10045), and a test fails if anything in
`src/ui` makes an element draggable or handles a native drop. Every drag goes
through the one drag owner, `src/ui/drag.ts`, which follows the Engine's
pointer-event recipe:

- `pointerdown` on a source captures the pointer; the press becomes a drag
  only after it travels a few pixels, so a click stays a click.
- A ghost follows the pointer: the source's picture, or its name when it has
  none. It draws inside the open modal, above everything outside it.
- The target under the pointer is found with `document.elementFromPoint`.
  While a source is held or dragged, every target shows whether it takes it,
  from eligibility the caller reads out of the projection.
- A release over a target that takes the source sends one semantic action; a
  release over one that does not sends nothing and says why. Escape, Back,
  `pointercancel`, `lostpointercapture` and window `blur` cancel and send
  nothing. The UI redraws from what the product publishes; nothing moves
  locally.

Without a pointer the same move is pick up and place: accept (a click, Enter
or the controller's A) on a source holds it, accept on an empty target places
it, and Back or Escape releases it before doing anything else. Accepting
another source holds that one instead, so selecting still works with the
mouse; a drag or the details pane moves onto a filled slot.

Drag is never the only way to do something. The same moves are buttons in the
surface, and a drawn context menu (`src/ui/context-menu.ts`) on a right-click,
the menu key or Shift+F10, or the controller's X offers them as accelerators,
as a double-click does for the item's main action. The inventory is the worked
example (`src/ui/inventory.ts`).

## Controls

- **Modals** are `<dialog>` with `showModal()`. Every confirmation asks
  through the one drawn confirm (`src/ui/confirm.ts`): focus starts on Cancel,
  and Escape, death or its owner closing answer no. `alert`, `confirm` and
  `prompt` are never used.
- **Tooltips** come from `data-tooltip` (`src/ui/tooltip.ts`), drawn on hover
  and on focus inside the open modal. The `title` attribute is not used for
  information, since the window never shows it.
- **Context menus** call `preventDefault()` on `contextmenu` and open the
  drawn context menu, which registers with navigation above its surface.
- **Lists** scroll with the wheel and follow controller focus.
- **Choices** are `<select>` or focusable options. The window draws a
  `<select>` popup for the mouse, and navigation changes its value with left
  and right for a controller.
- **Numbers** are bounded by limits the projection publishes, so an invalid
  value cannot be entered and a controller steps them with left and right. A
  confirm step is never the first place a constraint appears.
- **Redraws keep focus.** A snapshot arrives every update. A surface rebuilds
  its controls through `redraw` (`src/ui/redraw.ts`), which skips content that
  did not change and, after a real rebuild, focuses the same control again.
- **Text entry** uses `<input>` and `<textarea>`.
- Everything stays in the one page: no `window.open`, `target="_blank"`,
  native file or colour pickers.

## Scale and art

**UI scale.** Game UI is sized in `rem`; only hairline borders and
outlines (1-2 px) stay in pixels. The Engine's UI scale
(`context.ui.setScale`, which sets the root font size and
`--rusty-ui-scale`) therefore scales all of it, and `src/ui/scale.ts` is
the one owner that calls it:

- The layout is designed for a 1920x1080 window in CSS pixels at UI scale 1.
- While the player has chosen automatic, the scale is the largest step at
  which that layout still fits the window. For example, 1 at 1080p, 1.25 at
  1440p and 2 at 4K (with the operating system at 100%), and 0.5 for a
  1280x720 window. It follows the window as it resizes.
- The player can choose a scale instead, in Settings ("Interface scale": a
  stepper plus Automatic). The choice is a player preference that
  `DaggerfallControlSettings.UiScale` persists, never TypeScript state.
  `controls-ui-scale` sets it, and no `scale` means automatic.
- The bounds and step (0.5 to 3 in steps of 0.25) are published with the
  controls (`controls.uiScaleRange`), so the stepper cannot leave them.

The kit has no pixel grid, so any step is fine for it. Classic art keeps its
own whole-pixel rule through `fitPixelArt`. The browser's default
fixed-pixel font for form controls is replaced by a rem size, so inputs,
selects and buttons scale too.

**Classic art** (title, death, character-sheet chrome, faces, item icons) is
derived from the operator's Arena2 files, never committed, and delivered in
the HUD projection's `uiArt` by `DaggerfallUiArt`. `src/ui/art.ts` adopts it,
and `fitPixelArt` draws a classic screen at the largest whole number of device
pixels per source pixel, unfiltered.

**Authored art** (frames, panels, buttons, slots, icons, cursors) is original
art made for this project; see "Authored UI art" below.

**Plain CSS** stays plain: colour, type, spacing, borders and layout that need
no picture.

**Cursors** are CSS image cursors from the kit
(`image-set(url(...) 2x) x y, fallback`) with the kit's hotspot; text
fields keep the text caret. The desktop window draws only the fallback
until the Engine maps image cursors (#10104).

## Skin

`src/ui/skin.ts` turns the published kit into CSS custom properties on the
document root each time art arrives. Every primitive in `src/ui/skin.css`
is drawn from them:

- An image `ui.<family>.<name>[.<state>].v2` is published as
  `--skin-<family>-<name>[-<state>]` (its `url()`).
- Alongside it go `-slice` (9-slice insets in image pixels), `-width` (the
  insets in rem at the image's display scale) and `-size` (the whole image
  in rem).
- Cursors become `--dagger-cursor`, `--dagger-cursor-pointer` and
  `--dagger-cursor-grab`, drawn at half size with the scaled hotspot.

Screens lay out; they do not set frame, button or slot art of their own. The
vocabulary:

| Hook | Draws |
| --- | --- |
| `.dagger-frame` | Stone window (9-slice, no fill) over the tiled stone ground: the menu, confirmations, spell choices, the court, death and character creation |
| `.dagger-frame[data-skin="parchment"]` | Parchment reading surface with its page filled and dark ink: dialogue, dungeon text, quest messages, the journal page and the book reader. Controls on it stay stone |
| `.dagger-frame-title` | Brass title plate |
| `.dagger-panel` | Recessed stone inset inside a frame, such as item details and character sections |
| `.dagger-divider` | Brass rule |
| Game-UI `button` | Stone button. Hover and focus are lit, pressed sinks, disabled is dimmed. Slots, tabs, map markers and context-menu entries are excluded |
| `[role="tab"]` | Stone tab; `aria-selected="true"` is lit |
| `.dagger-slot` | Stone slot. Focus, selection and a drop target are lit; `is-drop-invalid` is red |
| `[data-icon]` | A kit icon before the label: menu actions, activation modes |
| `[data-inventory-equipment]` | An empty equipment slot shows its silhouette |
| Scrollbars | Slate groove and brass handle |

Focus always also keeps a brass outline, so keyboard and controller focus is
visible on any ground.

Every rule keeps a plain-CSS look when its image is absent. `applySkin`
reports the primitives a session did not publish through `reportMissingArt`
(context `skin`), so missing art is named, never blank. The tooltip is a
small parchment note: a texture and a thin border, with no frame.

## Authored UI art

**Direction.** The kit is inked dark-fantasy illustration with a tight
palette, not pixel art on a grid (the owner's pick on #10120). Weathered stone
and brass is the chrome: HUD, inventory, character sheet, menus, buttons,
slots, title. Parchment is only for surfaces the player reads: dialogue,
books and letters, journal, quest and message pop-ups, tooltips. A parchment
surface sits inside or over stone chrome, takes dark ink text, and keeps stone
controls.

**Recipe.** `scripts/ui-art-kit.py` generates and processes the kit. Its
header and the Den document `rusty-dagger/ui-art-recipe` give the exact
recipe:

- Krea 2 turbo with the Retro Dark Fantasy Illustration LoRA at 0.8, drawn on
  a flat green ground;
- the green keyed out, and the image fitted to its published size;
- a 64-colour perceptual palette pass with a hard alpha edge, through the
  Krea 2 Pixel Art Refiner node at grid 1.

Button, tab and slot states are derived from one base image, so their slices
line up. The script needs the LAN GPU hosts, so it is an operator tool, not a
step of `scripts/regenerate-content.sh`. Its output is committed:

- the processed originals in `data/ui-original/`;
- their entries in `data/ui-authored-assets.json`, whose prompt is the exact
  generation prompt.

**Adding an asset.**
1. Add its source prompt and its published entry (ID, published size, display
   metadata, derived states) to `scripts/ui-art-kit.py`.
2. Run `generate` for that source, pick a seed, then run `process` and
   `manifest`.
3. Run `scripts/regenerate-content.sh`, which publishes the asset under
   `media/ui/authored/`.

`DaggerfallUiArt` delivers every image under that path in the HUD
projection's `uiArt`, with no list to update. `art.image(id)` returns its
data URL and `art.display(id)` returns how it is drawn.

**Scale rules.**
- An original is at most 256 pixels on a side. The importer then publishes it
  unresampled, so 9-slice insets stay exact. It refuses insets on an original
  it would resample.
- `display.scale` is CSS pixels per image pixel at UI scale 1. The kit uses
  0.5, so its art is 2× at UI scale 1 and stays sharp on 4K and high-DPI
  screens.
- The kit has no pixel grid, so any UI scale is fine. Draw it with ordinary
  smoothing, not `image-rendering: pixelated`; `pixelated` stays for classic
  art.

**9-slice metadata.** `display.slice` is the insets (top, right, bottom, left)
in image pixels, as CSS `border-image-slice` takes them. The drawn border
width is each inset times `display.scale` (`border-image-width`, in `rem` at
1 rem = 16 px). A frame without a fill leaves its centre to the panel
texture, `ui.stone.texture.v2` or `ui.parchment.texture.v2`, which tiles at its
own scale.

`display.hotspot` is a cursor's hotspot in image pixels. The CSS cursor
uses it times `display.scale`, with a 2× image through `image-set` or the
cursor's own size.

## Done

A game-UI change is done when:

- it works with mouse, keyboard and controller as above;
- focused UI tests in `tests/WorldRpg.Ui.Tests` (`node --test`, jsdom) cover
  its behaviour, with C# tests for any projection or action it adds;
- it has been checked in `rusty dev --output window`.
