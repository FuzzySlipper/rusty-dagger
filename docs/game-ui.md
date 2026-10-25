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
as diagnostic, so its needs never shape a game surface. Dagger's developer tools
(`developer.ts`, [adaptive playtesting](playtesting.md#developer-tools)) mount beside
the game's shell, not inside it.

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
- `panel(direction)`: the shoulder buttons, which step the menu's panels (and
  the parts of character creation on the title, and a person's service pages).
- `page(direction)` with `pageHint()`: the triggers, or Page Up and Page
  Down, turn what the surface shows (a journal's quests, a book's pages, the
  local map's zoom, the travel map's sheets). The hint names what they do
  there, and is absent when they do nothing.

A control that uses the directions itself claims them: navigation first
offers each direction to the focused control as a cancellable
`dagger-direction` event, and Back as `dagger-back`, and moves focus or steps
back only when nothing claimed it. The local map's panning mode and the travel
map's sheets use this. Moving into a row of tabs lands on the tab shown, and
tabs follow focus.

A focused number or range field takes the controller's left and right as a
change of value. A pad cannot type, so the controller's accept on a text field
opens the letter picker (below). Hints at the foot of the topmost surface name
the keys or buttons of the device in use for what that surface accepts.

Controller buttons follow the standard layout the Engine normalizes: A
(`button-0`) accepts, B (`button-1`) backs out, the D-pad (`button-12`–`15`)
and the left stick (`axis-0`, `axis-1`) move, LB and RB (`button-4`, `5`)
step panels, LT and RT (`button-6`, `7`) turn pages. With the menu open, Start (`button-9`) steps out as Back does,
Select (`button-8`) opens the inventory, Y (`button-3`) the character, and X
(`button-2`) the actions of the focused item.

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

Without a pointer the same move is pick up and place: holding a source (a
mouse click on it, or a surface's own Move command through `Drag.hold`),
then accepting an empty target places it, and Back or Escape releases it
before doing anything else. Accepting another source holds that one instead;
a drag moves onto a filled slot.

A press that is not an item drag, such as panning the local map, uses the
same lifecycle through `followPress` in `drag.ts`. It captures on press,
becomes a drag past the same threshold, and ends on release,
`pointercancel`, a lost capture, window `blur`, or a move with no button
held. So no press outlives its button.

Drag is never the only way to do something. Every move is also a command in
the surface. The inventory is the worked example; see below.

## The inventory: the worked example

`src/ui/inventory.ts` is the reference item screen, and later item screens
follow its shape.

- **Layout.** Equipment surrounds a paper doll on the left: armour down one
  side, clothing and the hands down the other, the paired accessories in a row
  beneath. The ruleset publishes each slot's `group` and name
  (`DaggerfallInventoryPresentation`), so the screen holds no Daggerfall slot
  table. The doll draws the race background from the character projection;
  the figure and worn items draw in its layer box once the ruleset publishes
  them as placed layers. The pack fills the right: category tabs, a reserved
  row for the action bar, then a grid of large slots, with gold and load
  beneath.
- **Tabs and sort.** The ruleset publishes the tabs (`categories`) and each
  row's `category`. A tab dims the items outside it, and every item keeps its
  place. Sort pack sends `inventory-sort`, and the ruleset lays the pack out
  by tab and name.
- **Tooltips, not a details pane.** Hovering or focusing an item shows a
  parchment tooltip with its name, description, quantity, weight, value,
  condition and why it cannot be equipped, all from the projection. An empty
  equipment slot names itself through `data-tooltip`.
- **The action bar.** A double-click, a right-click, Enter or the
  controller's A on an item (a click no pointer made), the menu key or
  Shift+F10, or the controller's X open the item's actions in the row above
  the grid: Equip or Unequip, Use, Move, Drop and Cancel. Each is enabled from
  the projection (`canEquip`, `canUse`, `dropRefusal`), and the bar says why
  one is not. A stack drops a chosen part of itself through the quantity
  stepper. The bar registers with navigation above the panel, so arrows stay
  in it and Cancel, Escape or Back close it and return focus to the item.
- **Drag.** Items drag between pack cells and equipment slots, and Move holds
  one for pick and place.
- **The bank** is its own tab while a bank serves the player. The ruleset
  publishes each operation (`bank.actions`) with whether it is available now,
  the bounds of its amount and why it is closed; each row has its own amount
  stepper held to those bounds.
- **Containers** use the two-pane transfer layout (`src/ui/transfer.ts`): the
  container on one side, the player's pack on the other, each row with its
  facts, a quantity stepper for a stack and the command that moves it. Loot
  and corpses offer Take and Take All (`loot-take-all`, which the ruleset
  applies row by row and stops at the first refusal). An owned property's
  storage is the inventory's Storage tab, with Take and Store; the ruleset
  publishes the items the store refuses and why (`storeRefusals`).

The other item and service screens follow the same shape; a person's services are
described under the person screen below. A trade at or over the ruleset's
`largeTradeGold` and every theft ask through the drawn confirm; so do training, a
paid summoning and buying or selling property. The spellmaker and potion maker draw
every field from published limits (the spellmaker's `limits`, the potion maker's
ingredient count), and rest and lodging durations are steppers bounded by the
ruleset's maximums.

The spellbook puts school tabs (the ruleset's `schools`) over the known spells, each row its
icon, name and cost; choosing a row describes the spell beside the list, with Set active,
Cast, Move up and down, and Forget (confirmed). Spell icons are cells of one atlas the
ruleset names (`icons`: its media, row length and count), drawn by `src/ui/spell-icon.ts`
for the spellbook and the spellmaker alike, and the recast and abort keys are read from the
bound controls. Magic items list each usable item with its icon, charges (its condition)
and description. The character sheet groups skills under the ruleset's `skillGroups`, each
with a bar of its uses toward the next rise; affiliations and legal standing are rows with
their supporting facts; worn equipment shows its wear and whether it is identified. The
spell choices (create item, identify, teleport, dispel) draw every option, and whether it
is open, from the ruleset, and each takes Escape or Back as its Cancel.

The quantity stepper (`src/ui/stepper.ts`) is the shared bounded amount: −
and + beside a number field, with its limits from the projection. The
keyboard's and controller's left and right step it.

## The person screen

Talking to a person (`dialogue.ts`) opens one page at a time. The ruleset publishes
the person's `services` in order, each with its id and name, and `opensOn`: `talk`
for a person who offers nothing else, otherwise `services`, a chooser that lists
them as the donor's service window does. Each service has its own page:

- **Talk:** the greeting, tone, conversation log and topics, with the bank and
  temple donation among the services topics. The topics are grouped under the
  ruleset's `topicGroups`, and the log keeps what was asked and answered while the
  conversation lasts. The "Where is" directory's lists (each topic's `category`:
  the kinds of building the donor lists, the people, the region) are closed,
  counted sections until the player opens one.
- **Trade:** the transfer layout at the page's full height, named for what the
  provider deals in ("Buy, sell and repair", "Identify"). The stock has Buy and
  Steal (set apart); the player's items have Sell, Repair and Identify, with a line
  total for the chosen amount.
- **Training, food and drink, spells for sale, and the item, potion and spell
  makers** each have a page. A summoner's page quotes the summoning, and an offer
  opens as its own dialog above it.

Back or Escape steps from a page to the chooser, on the service just left. From
the chooser, or from a talk-only person's Talk, it ends the conversation. LB and RB
step between the pages, and the tab row over a page does the same for the
pointer. Each page opens on its content: Talk on its first topic, the others on
their first control. The page shown is all the DOM keeps; the services, their
names and order, prices, eligibility and refusals are the ruleset's.

## The loading screen

Slow work the player starts is acknowledged on a loading screen (`loading.ts`)
instead of a frozen frame. The slow cases are:

- beginning a new game;
- loading a save or quick load;
- choosing another game at the title;
- a journey;
- an entrance into a building or a dungeon.

The Kit's `LoadingScreen` stages the work. The Host owns the one screen across
session replacement, and a session given it through `GameSessionContext.Loading`
stages its own journeys and entrances on it. The update that accepts the request
publishes the screen and runs nothing slow. Each later update runs one labelled
phase, such as reading the destination, the days on the road, or entering. The
session neither steps nor reads input meanwhile. After the last phase the screen
stays up for one more update, so the first frame of the loaded world is drawn
before play shows. A phase that throws clears the screen, and the work's own
failure report says why ("Load failed: …"); the player stays where they were.

A phase may hand work to something running beside the update and wait for it.
The phase then stays on the screen, asked once per update, until the work is
done. A journey's or entrance's collision works this way. Each staged phase
holds the terrain window it admits and the towns it places (the town it arrives
in and its neighbours in the window) until the phase's own synchronous collision
and content calls are done, so none of them waits. It then gives them to the
Engine as two deferred batches, the towns' placements and the window, which the
Engine prepares off the update: it bakes the window's collision and composes
the towns' collision and navigation. A dungeon's or a building's placement is
small and is still made at once. The arriving player is stood on the ground, and
actors placed with the arrival are settled on the floor, only once both batches
are live, and the next phase runs only then. A refused batch fails the work and
says so. The world then stays held, as in a held mode: each update hands the
Engine what it refused again off the update, and play resumes once it is live
and the player stands on it. So does a change that fails with its rollback's
collision still being prepared.

The state travels in the session's snapshot as `loading`: title, phase label,
step, steps and `fraction`. The fraction is the share of phases finished, plus
the measured share of a waiting phase: the batches the Engine has finished, plus
the share of the batch it is preparing (the towns' navigation cells composed of
those planned, or the window's instances baked of those planned). Work of one
phase measures nothing, and neither does a batch the Engine is still validating
or parsing. Then the fraction is null and the bar is a moving band with the
phase label, never a made-up percentage.

The screen is a modal dialog above every surface, kit-skinned (frame, title plate,
panel bar). It holds focus and the cursor, and every key and pad press, Escape
included, is dropped until it closes. When it closes, play takes focus and the
pointer back without a click.

A direct Host call (`Begin()`, a launcher, a test) or a developer command has no
update to acknowledge in, so it runs the same phases at once.

## The other screens

- **Journal** (`journal.ts`): Quests, Finished, Notes and Book are tabs over
  their panels. Each quest entry takes focus, and up and down scroll an entry
  taller than its page. The notebook key and an opened book open the journal on
  their own tab.
- **Local map** (`maps.ts`): dragging pans (through `followPress`) and the
  wheel zooms about the cursor. Accepting the focused diagram starts panning
  with the directions. The legend and the facing come from the projection.
- **Travel** (`travel.ts`): the travel map names the focused or hovered marker
  beside it and in a readout, draws the quoted route, and keeps focus on the map
  as it redraws. A journey asks through the drawn confirm.
- **HUD** (`hud.ts`): bars for vitals and breath; a compass strip with the
  ruleset's heading and the Detect spells' contacts as marks; the readied spell,
  the weapon drawn or sheathed and the activation mode; and a prompt naming what
  use would do to the aimed target, worded by that target's owner, with the use
  binding of the device in use. It takes no focus in play.
- **Title, creation and death** (`entry.ts`, `character.ts`, `death.ts`): the
  title's game choice is a cycler. Creation sends every change at once and draws
  the ruleset's answer, including what blocks Start and whether Quick start can
  begin. The choices scroll inside their frame; Cancel, Quick start, Start and
  the reasons Start is refused stand below it, always in view. Death lists the saved games as the menu does and takes focus when it
  appears. A pad press, like any key, skips a cinematic.

## Controls

- **Modals** are `<dialog>` with `showModal()`. Every confirmation asks
  through the one drawn confirm (`src/ui/confirm.ts`): focus starts on Cancel,
  and Escape, death or its owner closing answer no. `alert`, `confirm` and
  `prompt` are never used.
- **Tooltips** come from `data-tooltip` (`src/ui/tooltip.ts`), drawn on hover
  and on focus inside the open modal. The `title` attribute is not used for
  information, since the window never shows it.
- **Context menus** call `preventDefault()` on `contextmenu` and open the
  surface's own drawn commands (the inventory's action bar), which register
  with navigation above the surface.
- **Lists** scroll with the wheel and follow controller focus.
- **Choices** are cyclers (`src/ui/cycler.ts`): ‹ value › with both steps
  focusable buttons, stepping through the options the projection publishes and
  skipping unavailable ones. A pad steps them with accept as easily as the
  mouse clicks them. Character creation, the title's game choice and the
  custom-class fields use them. The remaining native `<select>`s (the crafting
  fields, travel's region and destination, the activation mode, the dialogue
  tone) draw the window's own popup for the mouse, and navigation changes their
  value with left and right for a controller.
- **Numbers** are bounded by limits the projection publishes, so an invalid
  value cannot be entered and a controller steps them with left and right. A
  confirm step is never the first place a constraint appears.
- **Redraws keep focus.** A snapshot arrives every update. A surface rebuilds
  its controls through `redraw` (`src/ui/redraw.ts`), which skips content that
  did not change and, after a real rebuild, focuses the same control again.
- **Text entry** uses `<input>` and `<textarea>`. For a pad, the letter
  picker (`src/ui/letter-picker.ts`) edits any text field: a modal grid of
  letters with Delete, Done and Cancel that writes back into the field as typing
  does. It opens from a ✎ button beside the field (`letterPickerButton`) or from
  the controller's accept on the focused field. The character name and a
  riddle's answer use it.
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
fields keep the text caret. The desktop window draws them, as a browser tab
does.

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
| `.dagger-panel` | Recessed stone inset inside a frame, such as the equipment panel, the bank and character sections |
| `.dagger-divider` | Brass rule |
| Game-UI `button` | Stone button. Hover and focus are lit, pressed sinks, disabled is dimmed. Slots, tabs and map markers are excluded |
| `[role="tab"]` | Stone tab; `aria-selected="true"` is lit |
| `.dagger-slot` | Stone slot. Focus, selection and a drop target are lit; `is-drop-invalid` is red |
| `[data-icon]` | A kit icon before the label: menu actions, activation modes, inventory tabs |
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
step of the import. Its output is committed:

- the processed originals in `content/worldrpg/media/ui/authored/`, which are
  product content the ruleset reads in place;
- their entries in `data/ui-authored-assets.json`, whose prompt is the exact
  generation prompt.

**Adding an asset.**
1. Add its source prompt and its published entry (ID, published size, display
   metadata, derived states) to `scripts/ui-art-kit.py`.
2. Run `generate` for that source, pick a seed, then run `process` and
   `manifest`.
3. Run the import (`dotnet run --project src/Daggerfall.Import.Tool -- import
   --music local/Sound`). The classic media inventory in the data container
   then lists the asset at `worldrpg/media/ui/authored/` with its digest and
   display metadata. The import does not copy the file; it refuses an original
   it would resample, because the product reads the tracked file.

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
