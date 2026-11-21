# Writing a costume brief

The brief is the only thing from the classic sprite that reaches the painting. It is one sentence describing the person and
what they wear, as you would tell an illustrator who has never seen the game. It is written from the classic sheet
(`sheet.py`), all five views.

The 24 townspeople briefs in `briefs.json` are the worked examples. This page is drawn from them and from what went wrong
when they were drawn.

## Shape

```
<who>, <age>, <build>, with <hair and face>, in <garments, top to bottom, each with its colour and pattern>,
<belt and what hangs from it>, <legwear> and <footwear>
```

Examples from `briefs.json`:
- **385:** "A medieval European townsman in his forties with red-brown hair and a full beard, in a teal long-sleeved tunic
  to mid-thigh with a yellow zigzag hem, an orange hood worn down as a collar with a dagged edge, a brown belt with a
  leather pouch, black-and-white striped hose and green leather shoes".
- **456:** "A medieval European townswoman in her twenties with long blonde hair, in a white blouse with wide bell sleeves,
  a long skirt whose one side is blue and the other a black-and-white chequer, with a brown hem, and rust leather shoes".

- **Who.** Start with the region phrase for the classic's race (`region_phrases` in `briefs.json`):
  - Breton: "A medieval European townsman / townswoman";
  - Nord: "A northern townsman / townswoman";
  - Redguard: "A dark-skinned townsman / townswoman of a hot desert kingdom".

  Then the age as a decade ("in her thirties"), build only when it stands out ("heavy-set", "burly", "slender"), and hair
  and beard. The classic folder name gives race and sex: `texture-456-breton-female-outfit-4`.
- **Garments.** Go from top to bottom: hood, upper garment, sleeves, belt, lower garment, hem, legs, feet. Give each one:
  - its colour, in plain words (teal, rust-red, cream, sea-green);
  - its length ("to mid-thigh", "to the knee", "to her ankles");
  - its pattern ("yellow zigzag band", "vertical stripes", "chequer", "plaid", "dagged edge", "band of yellow flame shapes").
- **Hoods.** Always say "worn up" or "worn down as a collar". Say whether a cloak hangs down the back.
- **Things that hang.** Pouches, cases and knives: say what each one is. Give its side only if the front and back frames
  agree on it ("a leather pouch at his right hip"). A side that is wrong, or is in one view only, comes back as two objects
  in the orbit.

## Leave out

- **Style, pose, light, background and framing.** `front.py` adds these, the same for everyone.
- **The sprite's low-resolution oddities.** That means blocky folds, stair-stepped edges, CG plastic shading, and the
  walking-frame stance. Describe the garment the pixels stand for.
- **Things the classic doesn't show.** Don't add jewellery, weapons, scars or props that aren't there.
- **Exact matching.** Colours and garments follow the classic loosely. The brief is a costume brief, not a pixel record,
  and a clearer reading beats a faithful muddle.

## When the classic is unclear

Pick the plausible reading and write it down. Add the guess to the entry's `uncertainty` (for example, "pouch or
case at the left hip; a guess for Patch's review") so it is looked at in review rather than baked in silently.

## Wordings that matter to the painter

Found while drawing the 24 with New B (crew thread den-01M6ZZ734GS6PM7AM72KF87451):
- **Bare-chested men** (382, 384) need "bare-chested" or "wearing no shirt, bare chest" near the start. Otherwise the
  painter turns the kilt into a tunic.
- **A short hood with no cape** (398) needs "short hood, no cape". Otherwise Elmore draws it out into a full cloak.
- **Long sleeves**, wide or fitted, are drawn as asked, so choose deliberately. 456's "wide bell sleeves" were later edited to
  fitted ones (2026-11-19) because they hang in the splat bake.
- **Long skirts and cloaks** work, but the cloak is later baked as a separate layer: "a cloak hanging down her back".
- **A cloak's colour should match what is under it**, or the garment under it should be dark. The cloak layer's edges and the
  places it lifts are smoothed over the body. On a matched or dark undergarment that hides; on a contrasting one it shows
  every seam. 452's blue cloak on a white blouse could not be made to work, and she ships without it (Patch 2026-11-21: "it
  really should be matched colors or at least dark colored shirt to make the smoothing easier to pull off"). If the classic
  has a contrasting cloak, give the brief a darker undergarment, or a shorter cloak that stays in the body bake.

## Recording it

Add the entry to `briefs.json`:

```json
"456": {"texture": 456, "classic": "texture-456-breton-female-outfit-4", "pronoun": "She",
        "brief": "A medieval European townswoman …", "uncertainty": "", "source": "who wrote it, when"}
```

`front.py ID --print` shows the full prompt the brief goes into. A character from outside the townspeople set (a class or
enemy sprite) takes its own id, `"texture": null`, and its classic folder.
