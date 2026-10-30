> Reference copy from asset-pipeline, kept for its measurements and rationale. Paths and commands in it name the asset-pipeline layout; the current workflow is in [item-icons.md](../item-icons.md) and is the one to follow.

# Item art: literal prompts

The text below is actual generation input. The source/item contract and shared presentation clauses are expanded for every image; each saved study also has `PROMPTS.md` with every full prompt and a plain `jobs/<job-id>/prompt.txt` for copying. The JSON plans retain settings and provenance, but they are no longer the only way to read the prompts.

## Full positive prompt: Qwen painted-adventure dagger

This is the complete prompt sent for the ten-item baseline dagger. Direct Krea retro prepends `dark_fantasy_illustration,` to this same core prompt; the two-pass route uses the same contract while starting from the Qwen reconstruction.

```text
Task: Redraw a very small classic fantasy RPG item as newly illustrated high resolution art. Reconstruct plausible missing detail; do not enlarge or trace the pixel blocks.

Input image: content reference only. Preserve this object's identity, silhouette, orientation, relative dimensions and major colour regions. Replace the pixel rendering with the treatment below.

Subject and composition: One short complete dagger lying horizontally, point to the left, grip to the right, broad side view, overall aspect about 4.6:1. A straight double-edged steel blade with a sharp left point takes the left half; the blade is blue-grey with pale cool highlights. A short perpendicular antique-brass crossguard divides blade from a reddish dark-brown grip. The right end has a small brass pommel cap. Preserve the horizontal orientation, blade length relative to the hilt, slim outline and small simple fittings. No scabbard, hand, jewel, inscription or second blade.

Uncertain source detail: This is the complete world sprite rather than the split paper-doll hand layer. The tiny highlight marks do not establish blade engraving or grip decoration.

Permitted invention: Resolve blade bevels, restrained steel reflections, simple brass thickness and worn brown grip texture. Keep the silhouette slender and the fittings plain.

Style: Restrained hand-painted 1990s fantasy adventure item illustration, opaque gouache-like colour modelling, broad confident painted planes, sparse dry-brush marks, selective dark accents along occluding contours. Tactile and believable without photographic microtexture. Slightly irregular handmade edges, clear original local colours and quiet wear; descriptive rather than ornamental. Smoothly drawn high resolution shapes, no square pixel clusters.

Collection rules: Make this belong to one coherent set of grounded, slightly timeworn fantasy adventure inventory art. Keep the source's local colours: do not tint every material brown or add brass simply to match the UI. Use a restrained palette with distinct light, middle and dark families, clear silhouette and legible material masses. Detail hierarchy: silhouette first, a few large planes or folds second, sparse material marks third. Keep the original class of mundane object, its opening and visible components; no extra handles, gemstones, runes, faces, symbols, chains or elaborate fantasy embellishment. Do not convert this into pixel art, a photoreal product photograph, a glossy mobile-game loot icon or a dramatic scene. No text, labels, border, vignette, decorative frame, hand, person, pedestal, scenery or second object.

Presentation: One isolated item on a perfectly flat solid dark slate grey background, RGB 48,52,59. No floor, no background texture and no cast shadow outside the object. Full object visible with breathing room, centred. Longest extent roughly three quarters of a square canvas. Preserve the source view and rotation. Broad soft light from the upper left, restrained highlights and readable shadow planes. Draw actual new fine forms with clean deliberate edges rather than smoothing or retaining square source pixels.
```

## Deliberately long Qwen style contracts

These are literal style sections, inserted after the item-specific identity/uncertainty/detail clauses. The complete four-item prompts are approximately 1,600–1,750 words, including the shared clauses. These tests also use presentation wording that reinforces drawn value shapes, so comparison with the earlier baseline is a recipe comparison rather than an isolated prompt-length experiment.

### Long retro painted / no name

1240 words in this style section.

```text
ART DIRECTION — RETRO DARK FANTASY, PAINTED ADVENTURE EQUIPMENT

The requested result is a finished original illustration, not an improved photograph and not enlarged pixel art. Imagine the object was drawn and painted for an early fantasy role-playing equipment catalogue, then digitised cleanly at high resolution. The visual pleasure comes from decisive shape design, economical ink, strong painted value groups and a few tactile descriptive marks. The object should feel handmade and slightly timeworn, but it must remain the specific ordinary object in the reference. Create the missing information through drawing decisions. Do not interpret higher resolution as permission to fill every surface with scratches, engraving, noise or ornamental lore.

SOURCE AUTHORITY AND WHAT TO CHANGE

Use the input image as an authority for the object's content and composition: its visible parts, silhouette, openings, orientation, relative proportions, large colour regions and material identity. Use this paragraph as the authority for how that content is drawn. Remove the visible square pixels completely. The reference is not the rendering style to reproduce. Preserve a thin blade as a thin blade, a squat chest as a squat chest, the arrangement of a flower's stem and leaves, and the unbroken outline of a bottle. Invent construction only inside those structural limits. A region that the pixels cannot resolve should become a simple plausible form rather than a new decorative feature.

THE CORE PAINT LANGUAGE

Construct each material from roughly four to six major interlocking colour masses: a local middle colour, one or two distinct light-facing planes, a broad form shadow, an occlusion accent and a few restrained edge highlights. Those numbers describe an economical hierarchy, not a pixel palette or a posterisation filter. Draw those masses intentionally so their borders explain the object's volume. Use stepped warm-to-cool hue changes within the original material colours, rather than a grey airbrushed gradient over everything. The light areas must have enough unbroken paint to look confident and calm. The shadow masses must connect into clear larger forms rather than dissolve into many tiny dark chips.

BRUSHWORK AND SURFACE

Use opaque matte gouache or thin acrylic-like paint with occasional dry-brush drag along a form. A brush mark should follow a blade bevel, a curved pot, a fold, a timber board, a petal or a metal rim. It should tell the viewer something about that material. Keep most of each large lit plane quiet. Show the handmade character through a slightly uneven contour, a broken highlight or a few directional paint strokes; do not cover the whole object with paper grain. Grain applied indiscriminately is not a substitute for painting. There should be no photographic pore detail, procedural grunge layer, sharpening halo or uniformly distributed mottling.

INK AND EDGE HIERARCHY

Use a dark umber or near-black contour selectively where one form passes in front of another. The deepest undercuts can carry a bold accent. Let a few light-facing exterior edges be expressed simply by the difference between the object's paint and the slate background. Internal lines should usually be thinner and shorter than the exterior occlusion marks. Break some strokes naturally. Do not surround every colour patch with the same heavy black line. Avoid clean vector strokes, perfect uniform outlines and the polished graphic language of a mobile loot icon. The drawing may be assured and crisp while still having irregular pressure and brush rhythm.

LIGHT AND VOLUME

Describe a broad light from the upper left as two or three deliberate painted decisions, not a simulated studio light rig. Use a clear lit face, a coherent shadow side and very small accents where the material needs them. Metal may have one bright edge or a broad flat reflection; it must not become a mirror of a nonexistent studio. Glass may have a restrained pale reflection and dark transparent interior, but not a photoreal caustic network. Do not add a rim-light halo, bloom, lens glare, cinematic fog or an exterior cast shadow. The painted volumes must remain readable on the flat slate ground without a floor.

LOCAL COLOUR DISCIPLINE

Keep the major colour relationships in the source. Blue-green glass remains blue-green; a blue gem remains blue; green leaves remain green and red petals remain red. A brown wooden chest with green fittings remains that combination. The illustration belongs to dark fantasy because of its drawing and restraint, not because everything is tinted sepia. Do not impose a global brown glaze or add gold edging to unrelated objects. Within each material, let the middle colour occupy a useful connected area. Allow a slightly cooler shadow and a warmer highlight only where this does not replace the source's palette. Avoid neon saturation, colour fringing and rainbow highlights.

MATERIAL TRANSLATION, USED ONLY WHEN THE SOURCE CONTAINS THAT MATERIAL

Steel is a few cool grey-blue planes, a narrow pale bevel and sparse dark wear. Brass and old metal are muted ochre or olive-gold planes, with a controlled warm glint and dark recesses. Wood has a few drawn grain strokes aligned with the actual board, not a photographic timber texture. Clay is matte, slightly uneven and rounded, with quiet firing colour rather than stone speckles. Cloth has broad folds and a little directional weave near an edge, never thousands of fibres. Paper has layered edges without legible invented writing. Petals are layered red shapes with a few shadow folds; leaves have one or two veins and the source's silhouette. Glass and gemstones use a very small number of purposeful transparent or faceted colour shapes.

DETAIL BUDGET AND AGE

Make silhouette and the main material masses work first. Next describe construction: the thickness of an existing lip, the join of a known fitting, the seam between lid and box, the shape of a pre-existing fold. Last add only a few small wear marks where handling would plausibly occur. Age does not mean damage on every square centimetre. Do not add new cracks, chips, rivets, straps, hinges, pins or stitches unless they are a modest resolution of something already present. A mundane item remains mundane. No jewels appear on a plain guard; no occult emblem appears on a blank book; no skull appears on a lantern; no decorative frame appears around the asset.

SCALE AND COHESION

Design the image to survive a reduction to a small inventory icon. At about 64 pixels the silhouette and main material divisions should still read. At 128 pixels the construction and the large painted planes should read. At full resolution the spare ink and brush marks should reward looking closer, without becoming the dominant subject. Every item in the set should share the same edge hierarchy, restrained value grouping and degree of simplification. A flower should not suddenly become a botanical photograph while a sword becomes a cartoon. Material response changes between items; the hand of the illustrator does not.

PRESENTATION AND FINAL EMPHASIS

Show only the referenced item, fully visible and centred, at the original view and rotation. Keep breathing room around it. The entire background is one flat solid slate grey, RGB 48,52,59, with no texture, vignette, floor, paper, caption, border or scenery. Do not draw a page from a manual: draw the equipment illustration that could have occupied that page. Deliver a clean high-resolution drawing with newly resolved form, opaque painted planes, selective broken ink and quiet surfaces. If any interpretation competes with these requirements, favour the source's identity and the deliberately painted medium. The result must visibly be illustrated rather than smoothly rendered, pixelated, photographed or over-decorated.
```

### Long old-RPG ink / no name

1303 words in this style section.

```text
ART DIRECTION — OLD PEN-AND-PAPER FANTASY EQUIPMENT ILLUSTRATION, WITH RESTRAINED LOCAL COLOUR

Draw the existing object as an accomplished pen-and-ink fantasy illustration from the era of early tabletop role-playing manuals and British adventure gamebooks, then finish that drawing with a few opaque, muted colour shapes. It should have the specificity, economy and handmade rhythm of traditional book illustration. This is a fresh drawing of the same item, with missing detail invented through observation and craft. It is not a photograph, a realistic 3D render, a vector icon, a modern game splash image, an AI-smoothed enlargement or a recreation of the source pixel grid. The drawing must remain unmistakably drawn even before the viewer notices its surface marks.

CONTENT AUTHORITY

The reference supplies the subject, its relative dimensions, view, orientation, colour regions, existing attachments, silhouette and negative spaces. The style instructions supply the language of marks and painted values. Do not confuse those roles. Translate the old pixels into intelligible forms, but do not transfer their square steps into the final contour. Keep the opening of a bottle, the outline of a guard, the fold of a bandage, the placement of a flower and the broad division of a chest exactly meaningful to the same object. If a feature is uncertain, choose the simplest credible construction consistent with the visible pixels. Inventing a rune or another part does not count as resolving detail.

DRAWING BEFORE SHADING

Build a confident underlying drawing with a few purposeful curves and angular changes. Let the contour describe thickness, tension, edge wear and the overlap of parts. Avoid a mechanically smooth computer-generated perimeter. A thin blade needs a controlled long edge; a petal needs a softer turning line; cloth needs a kink where it folds; a metal lip needs a convincing ellipse. Those differences should be evident in the drawing itself rather than in an overlay of generic texture. Preserve the large silhouette and resist the temptation to replace an odd old object with a more familiar generic fantasy item.

LINE WEIGHT

Use dark umber ink tending toward black in the deepest crevices. Establish three practical line scales: the strongest short accents mark occlusion and the shadow side of the main contour; medium lines mark construction and overlaps; the finest broken strokes explain a little surface or turning form. Do not use the same thick outline everywhere. Do not give every internal colour patch a closed black border. A line may taper, pause, skip or break where the light-facing surface can carry itself. Keep variation intentional. The result should look authored by a person with a pen and brush, not generated by an edge-detection filter.

HATCHING GRAMMAR

Use small groups of directional hatching only where they help the form. Parallel strokes should wrap around a curved vessel, follow the plane of a wooden board, turn around a metal boss or tuck into a fold. Cross-hatch sparingly in a few deep shadow regions. Leave broad lit areas mostly unhatched. Never spread a dense regular mesh over the entire object. Hatching is not noise, shading dots or a screen-tone pattern. Separate groups of marks with quiet space so they remain readable as deliberate decisions. A tiny scratch should not become the same scale or density as a structural seam. No evenly distributed gritty stippling.

COLOUR AS A SECOND SYSTEM

Retain the actual local colours in the source. Think of a clean traditional ink drawing finished with flat opaque gouache or controlled coloured washes, rather than a monochrome drawing on yellowed paper. Colour regions should support the drawing without obscuring it. Use a coherent middle value, a small number of painted light shapes, one broad shadow group and a few highlights. A wash may have a slightly irregular hand-painted edge, but it must not become a smooth airbrushed gradient. Red remains a real muted red, green remains green, and blue glass remains blue-green. No universal sepia filter. Do not make every object the colour of old parchment.

VALUE GROUPS AND LIGHT

Use the suggestion of upper-left light to organise the drawing. Keep the light and middle groups connected. Let the main shadow be a clearly shaped area rather than dozens of scattered dark patches. The brightest marks are small and material-specific. Distinguish metal with a firm pale edge and cool plane; distinguish cloth with a broader quiet light fold; distinguish glass with a few drawn reflections and the dark interior showing through. Every material must share the same illustrative hand. Avoid specular rendering, studio softboxes, rim lights, bloom, soft external ground shadows and cinematic contrast that swallows the source's colour.

MATERIAL MARKS

Resolve only the materials actually present in the object. For steel use cool planar grey-blue and a few fine dark scratches aligned to the blade, without chrome reflections. For wood use a handful of fine irregular grain lines that follow the form. For old brass use a restrained olive or ochre middle tone and a pale edge, not a golden glow. For clay use slight irregularities in the drawn lip and quiet curved body marks. For cloth use fold lines and a hint of weave near a turned edge, not photoreal fibres. For glass use the shape of the vessel, broad cyan reflections and dark translucent spaces, not a glass shader. For plants use a few expressive veins, curved petal edges and natural branching, never a generic ornamental flower motif.

AGE AND RESTRAINT

A worn object can have a softened edge or a few handling marks without being covered in damage. Select wear according to the source's material and construction. Do not add a hundred cracks, rust flecks or holes to communicate age. Do not add decorative text, a jewel, heraldry, skull, face, religious symbol, chain or strap merely because the genre is fantasy. The artistic character should come from mark-making and value choices. Keep the item's existing social status: a plain object stays plain rather than becoming a legendary enchanted treasure. A mundane book is not automatically a spellbook; a lamp is not automatically a haunted shrine.

IMAGE DESIGN FOR A COLLECTION

Make the shapes legible at the size of an inventory illustration. The largest decisions are silhouette and arrangement. The next decisions are the main divisions of materials and construction. Fine hatching and a few surface marks come last. At a small display size the image should still read through a handful of light and dark groups. At native resolution the viewer should find controlled craft instead of a sudden field of unrelated micro-detail. Apply the same line-weight hierarchy, painted value discipline and quiet-space ratio across metal, wood, glass and organic subjects. Do not change medium when the material changes.

SPATIAL RULES

Keep the original reference view and rotation. Show the entire object without cropping, centred with a margin, its longest extent around three quarters of the square canvas. Preserve any meaningful hole or gap inside the object. The background is a completely flat solid slate grey, RGB 48,52,59. There is no sheet of paper behind the object, no page decoration, no hand, person, tabletop, frame, typography, label, number or dramatic scene. The medium can resemble old book illustration without depicting a book page. Do not draw adjacent equipment to make it feel like a manual spread.

PRIORITY CHECK

The final result should immediately communicate the same object, the original major colours, confident broken pen contours, selective form-following hatching and limited hand-painted colour masses. Keep the lit areas quiet, the shadow groups coherent and the detail meaningful. The named genre is a drawing discipline, not a licence to add its usual motifs. If a familiar fantasy stereotype conflicts with the reference, choose the reference. If photographic texture conflicts with the drawn medium, choose the drawing. If detail weakens readability, omit it. Produce one clean high-resolution illustration with visibly new resolved information and a consistent old tabletop-fantasy visual language.
```

## Named-artist variants

The two named variants prepend the following literal cue to the same long old-RPG ink style section above. The Gary Chalk and John Blanche inputs differ only in the artist name at this point. The names are experimental style cues; this is not an attribution of the retro LoRA or its training data.

### Same ink prompt + Gary Chalk

```text
Named visual style cue: Gary Chalk. Let this cue influence the line economy, painted value grouping, edge rhythm and mark-making of the existing object. Borrow no characters, compositions, insignia or new subject matter.
```

### Same ink prompt + John Blanche

```text
Named visual style cue: John Blanche. Let this cue influence the line economy, painted value grouping, edge rhythm and mark-making of the existing object. Borrow no characters, compositions, insignia or new subject matter.
```

Gary Chalk is credited with the early Lone Wolf illustrations and Talisman work by the authorized [Project Aon archive](https://www.projectaon.org/en/Main/GaryChalk). That makes him a useful gamebook-illustration reference to test. John Blanche supplies a rougher dark-fantasy comparison; his [official licensed gallery](https://www.warhammerart.com/collections/john-blanche-artwork) describes the restricted ochre/rust/brown/black colour system and provides examples. The source item still controls colour and content, so the cue must not add unrelated emblems or recolour everything brown.

## Specs and exact per-image exports

- [Ten-item baseline and direct Krea recipes](../tools/item-art-study/items-ten.json)
- [Ten-item Qwen then retro recipe](../tools/item-art-study/items-ten-retro.json)
- [Long Qwen style contracts and named variants](../tools/item-art-study/items-ten-qwen-long.json)

Generate readable exports from any existing study without running inference:

```bash
python3 tools/item-art-study/run.py prompts /absolute/study-directory
```

Open `PROMPTS.md` in that directory for the index and complete expanded text. Each indexed `prompt.txt` is the literal positive prompt without explanatory annotations. The shared workflow and generation commands are in the [item art study guide](item-art-studies.md).
