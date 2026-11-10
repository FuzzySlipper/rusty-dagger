# UI and 2D presentation art

Which art each family of 2D screen art draws in the product, and which screen draws it. Faces and paper-doll
bodies are the `paper-doll` area (#10495); inventory and spell icons are the `items` and `spells` areas. The
UI's structure and kit are described in [docs/game-ui.md](../../docs/game-ui.md).

Four dispositions:

- **Authored:** the stone and parchment kit in `content/worldrpg/media/ui/authored/` (recipe:
  `scripts/ui-art-kit.py`, `data/ui-authored-assets.json`, `docs/game-ui.md` "Authored UI art").
- **Refreshed:** a redrawn painting promoted into `content/worldrpg/media/refreshed/` (below); the classic art stays
  selectable with the art set.
- **Classic:** the operator's Arena2 art, published by the importer and drawn as square pixels.
- **None:** the DOM UI replaced the classic surface; the importer may still publish the file, but nothing draws it.

## Families

| Family | Sources | Disposition | Drawn by |
| --- | --- | --- | --- |
| Menus and service screens (main, guild, bank, shop, talk, rest, inventory, item, book, scroll) | `MAIN00I0`, `MAIN03–05I0`, `INVE00I0`, `INVE08–14I0`, `BOOK00I0`, `REST00–02I0`, `SHOP00I0`, `GILD00–01I0`, `BANK00I0` | Authored kit; the classic chrome is published and not drawn | `menu.ts`, `dialogue.ts`, `transfer.ts`, `inventory.ts`, `journal.ts`, `lodging.ts` |
| Character sheet chrome | `INFO00I0` | None (published, not drawn): the sheet is the kit frame | — |
| Title | `TITL00I0` | Refreshed: the painting alone, upright, the game's name set over it and a dimmed copy behind it | `entry.ts` (`screens.ts`) |
| Character creation backdrops | `CHGN00I0`, `PICK02I0` | Refreshed, behind the DOM creation form | `entry.ts` |
| Start menu | `PICK03I0` | None (published, unbound) | — |
| Prison | `PRIS00I0` | Refreshed | `choices.ts` (court, prison phase) |
| Death | `DIE_00I0` | Refreshed, without its lettering | `death.ts` |
| Race backgrounds | `SCBG*` | Refreshed, behind the refreshed paper doll | `inventory.ts`; `character.ts` for a racial form |
| Class portraits | `*.CEL` | None (published in the character projection, not drawn) | — |
| Character art, custom class, night | `CHAR*`, `CUST*`, `NITE*` | None (inventoried, not published to the UI) | — |
| Story sprites | `*.BSS` | None | — |
| Faction faces | `TFAC00I0.RCI` | Refreshed | `quests.ts` (escort portraits) |
| Child faces | `KIDS00I0.CIF` | Refreshed | `quests.ts` (escort portraits) |
| World travel map | `TRAV0I00` | Classic, with its own chrome and EXIT plate | `travel.ts` |
| Region travel sheets | `FMAP*` | Classic | `travel.ts` |
| Automap and town maps | `AMAP*`, `TMAP*`, `TOWN*`, `TRAV0I01–04` | None: the local map is a vector diagram | `maps.ts` |
| Compass | — | None: a DOM strip | `hud.ts` |
| Paintings | `*PAINT.CIF` | None | — |
| Fonts | `FONT0000–0004.FNT` | None (published as `font.classic.*`, not drawn); the DOM uses system fonts | — |


## Converting the screens, backgrounds and portraits

`authoring/tools/ui/export.py` (export-ui) writes the classic images that still draw into the `df-raw` layout, from
the import's published images under `local/import/staging` (so run the import first):

- `df-raw/ui/screens/<name>/`: the title, character creation's `CHGN00I0` and `PICK02I0`, prison and death;
- `df-raw/ui/faction-faces/tfac00i0/` and `df-raw/ui/child-faces/kids00i0/`: the escort portraits, record N being
  face N;
- `df-raw/paper-doll/backgrounds/scbgNNi0/`: the race backgrounds, placed in the frame the doll draws them in.

They convert with the other areas' recipe (`item-icons.md`): `ui` batches for the screens and portraits
(`specs/ui/`), and a `paper-doll` batch for the backgrounds. The backgrounds, the faction portraits and the screens
are framed or whole pictures, fitted afterwards by `tools/items/pictures.py`; the child faces are heads and keep the
cut-out. A screen the batch painted without its lettering is fitted from the picture before back-projection
(`--image original`), which would draw the lettering back in, and the title from its source edit (`--source`).

The screens are painted without lettering, so the page sets the game's name over the title and the death and
creation screens lose their baked-in words. The title is its painting alone: a source edit in
`gen-raw/source-edits/ui-screens` crops `screen.title` to columns 82–235, leaving out the stone surround, and copies
the other screens whole; the batch runs with `SOURCE=` at it.

The pulp-portrait style's call for "strands of hair and beard" puts stubble on women and children: their redo specs
(`ui-r1-*`, `ui-r3-faces`) drop it and state a smooth chin, jaw and upper lip, and the empty red hood (`ui-r2-hood`)
also drops the call for modelled features.

`promote.py --area ui` writes the accepted screens at their own size and the portraits at 256 pixels, and
`promote.py --area paper-doll` the backgrounds with the doll rect of the frame they fill. The HUD names the image each
screen draws (`screens`), so a refreshed screen fills the window, the upright title over a dimmed copy of itself,
and classic art stays selectable.
