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

**One interaction lifecycle.** Interface versus gameplay mode is decided in
one place, from the projection and the surfaces open in the page. Entering
interface mode releases pointer lock (`context.ui.setInteractionMode`).
Leaving it for play re-takes the lock with `context.ui.focusGameplay()` on the
projection that returns the product to play, so closing a menu needs no
click. A free-cursor screen in play (a map) uses `context.ui.setCursorMode`
and returns to `'pointer-lock'` when it closes.

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

One focus-navigation owner serves every surface; a screen registers its
focusable regions rather than keeping its own controller table.

## Drag and drop

Native HTML5 drag and drop is not used. The desktop window refuses it (the
owner's decision on rusty-engine #10045), so drag follows the Engine's
pointer-event recipe:

- Images and links in game UI are `draggable="false"`.
- `pointerdown` captures the pointer; the drag starts only after a small
  movement, so a click stays a click.
- A ghost with `pointer-events: none` follows the pointer, and the target is
  found with `document.elementFromPoint` and `closest('[data-slot]')`.
- While hovering, targets show valid or invalid from the eligibility the
  projection publishes.
- `pointerup` on a target sends one semantic action. `pointercancel`,
  `lostpointercapture`, Escape and `blur` cancel and send nothing.

Every drag has a keyboard and controller equivalent: accept picks the focused
item up, focus moves between targets, accept places it and back cancels. It
sends the same action. Drag is also never the only way to do something: the
same move is available from a button or context menu.

## Controls

- **Modals** are `<dialog>` with `showModal()`. Confirmations are drawn
  dialogs; `alert`, `confirm` and `prompt` are never used.
- **Tooltips** are drawn on `pointerenter`/`pointerleave` and on focus. The
  `title` attribute is not used for information, since the window never shows
  it.
- **Context menus** call `preventDefault()` on `contextmenu` and draw the menu.
- **Lists** scroll with the wheel and follow controller focus.
- **Choices** are drawn as focusable options (a list, cycler or option
  buttons), not `<select>`. The window can draw a `<select>` popup, but a
  controller cannot open or change one.
- **Numbers** use bounded steppers whose limits come from the projection, so
  an invalid value cannot be entered and a controller can change them. A
  confirm step is never the first place a constraint appears.
- **Redraws keep focus.** A projection update reconciles keyed nodes in place;
  it never rebuilds the control that holds focus, so keyboard and controller
  focus survives every update.
- **Text entry** uses `<input>` and `<textarea>`.
- Everything stays in the one page: no `window.open`, `target="_blank"`,
  native file or colour pickers.

## Scale and art

**UI scale.** Game UI is sized in `rem`, so the Engine's UI scale
(`context.ui.setScale`, which sets the root font size and
`--rusty-ui-scale`) scales all of it. The player's UI scale is a persisted
player preference, offered in Settings.

**Classic art** (title, death, character-sheet chrome, faces, item icons) is
derived from the operator's Arena2 files, never committed, and delivered in
the HUD projection's `uiArt` by `DaggerfallUiArt`. `src/ui/art.ts` adopts it,
and `fitPixelArt` draws a classic screen at the largest whole number of device
pixels per source pixel, unfiltered.

**Authored art** (frames, panels, buttons, slots, icons, cursors) is original
art made for this project:

- Each asset has an entry in `data/ui-authored-assets.json` (ID, published
  file, original in `data/ui-original/`, generator and prompt), as
  `data/README.md` describes.
- `scripts/regenerate-content.sh` publishes it through `Daggerfall.Import`, and
  `DaggerfallUiArt` delivers it beside the classic art.
- Pixel art is drawn with `image-rendering: pixelated`.

**Plain CSS** stays plain: colour, type, spacing, borders and layout that need
no picture.

**Cursors** are CSS image cursors (`cursor: url(...) x y, fallback`) with the
tip as hotspot; text fields keep the text caret.

## Done

A game-UI change is done when:

- it works with mouse, keyboard and controller as above;
- focused UI tests in `tests/WorldRpg.Ui.Tests` (`node --test`, jsdom) cover
  its behaviour, with C# tests for any projection or action it adds;
- it has been checked in `rusty dev --output window`.
