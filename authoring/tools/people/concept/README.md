# Concept art for a Dagger character

How a Daggerfall townsperson becomes the high-resolution front painting the rest of the character pipeline starts from
(`01-concept/approved/front.png` in a character's folder). This is the route every villager front was made by.

The classic sprite is never shown to the image model. It is about 75 px tall, and a model given it copies its low-resolution
oddities: blocky folds, a pose that is a walking frame, 1995 CG shading. Instead the sprite is read once, by a person or an
agent, into a plain costume description, and the painting is drawn from that text alone. The painting agrees with the
classic on what she wears and what colour it is. It owes nothing to the sprite's pixels.

```
classic frames (5 views, 75 px)  ──1 describe──▶  costume brief (one sentence, briefs.json)
                                                        │
                                  ──2 front────▶  New B front painting, 832×1216 (front.py)
                                                        │
                                  ──3 review───▶  Patch marks it; edits only after his mark
                                                        │
                                                        ▼
                                  01-concept/approved/front.png  →  back painting, pair check, scaffold …
```

The sprite-anchored route (the sprite itself as the image input) is a separate experiment and is not part of this route.
See [`sprite-anchored-wip/`](sprite-anchored-wip/README.md).

## Run it for a new character

Run the commands from the Rusty Dagger repository root. Working images and their sidecars go under
`local/art/generated/studies/people-concept/`. That directory also holds the copied sample runs for 128 and 456
on the authoring host.

1. **Sheet of the classic frames.**

   ```sh
   python3 authoring/tools/people/concept/sheet.py 456
   ```

   The sheet shows the five directional frames (front, front-right, right, back-right, back), enlarged without smoothing.
   They come from rusty-dagger's reference export, `local/art/reference/daggerfall-remaining-directional-unmirrored-r01/townspeople/`.
   A character that is not a townsperson needs its own frames on a sheet like this one.

2. **Write the costume brief.** Follow [`describe.md`](describe.md): one sentence, neutral, read from all five views. Add it
   to `briefs.json` under the character's id with its pronoun, and add any guess to `uncertainty`. This is the step that
   carries the classic's design forward, so take the time to get it right. All 24 townspeople already have one.

3. **Front painting.**

   ```sh
   python3 authoring/tools/people/concept/front.py 456 --seeds 1,2,3
   python3 authoring/tools/people/concept/front.py 456 --print
   ```

   The output includes `front-s1.png`, `front.prompt.txt` and a graph JSON per image. Use `--comfy URL` to select
   the producer host. The script assembles the prompt from four parts. Only the brief changes from one character to the next:
   1. the style line;
   2. the brief;
   3. the turnaround stance: costume fitting / turnaround sheet front view, symmetrical, arms in a wide A, slate ground,
      light from overhead;
   4. the ink-and-gouache art-direction paragraph.

   It runs [`front-template.graph.json`](front-template.graph.json) on the GPU lab's ComfyUI, about 6 s an image:
   - `krea2_turbo_fp8_scaled`;
   - LoRAs Brom 0.35, Elmore 0.45 and RetroInk 0.45 ("New B");
   - 832×1216, 8 steps, cfg 1, euler/simple.

   This is the graph villager 456's approved front was made with. Checked 2026-11-20: `front.py 456` rebuilds her recorded
   prompt word for word, except "his body" in the light sentence, now "her body" (the recorded fronts all said "his").
   Seed 1 matches her approved design.

4. **Submit and review.** Compare the candidates against the classic sheet for garments, colours, patterns, hood,
   belt, pouch and footwear. Use the [CMS concept stage](../../../cms/README.md#concept-art) to submit candidates
   with their actual prompt, seed, and saved workflow graph. Use the CMS ID for the submission, such as
   `npc-456` for brief ID `456`. Download the selected image with `concept_cli.py approved` for the back view
   or scaffold.

What comes next (back painting, pair check, scaffold, orbit) is the character route on the wiki: `rusty-dagger/splat-characters`.

## Why each part is there

- **Brief, not sprite.** On 2026-11-08 the 24 townspeople were described from their classic frames for the parts-rig
  work, Rusty Dagger #10670 (rusty-dagger `local/evidence/10670-townsperson/cast/gen.py`). The same sentences then fed the
  New B style study (`live-evidence/townsperson-all24-ab-20261108/`). The classic townspeople are 1995 CG renders whose
  proportions and stances are not worth keeping, but whose costumes are.
- **New B** (`Brom 0.35 + Elmore 0.45 + RetroInk 0.45`) won the style matrix: Patch picked it over New A (0.40 each) across
  all 24. See crew thread den-01M6ZZ734GS6PM7AM72KF87451, section 4.
  - Brom alone, or Frazetta, crushes the shadows.
  - Elmore alone is too photographic.
  - RetroInk at 0.75 blacks out faces.
- **Turnaround wording** (2026-11-14). The first prompt asked for a stance in words and gave a turned "heroic" stance.
  "The front view of a costume fitting or character turnaround sheet, symmetrical neutral standing pose" squared 27 of 27
  test images, where literal pose wording changed nothing. Shading "the same on both sides of the figure" and light "from
  directly overhead" even out the side light. Patch chose the wide A arms it gives over arms at the hips, which gave film
  and ghosts in bakes. Details: `live-evidence/h3-orbit-control-20261114-r01/notes.txt`.
- **Slate ground, RGB 48,52,59.** A flat ground cuts out cleanly for the keyframes and the clay passes.
- **No post-processing.** The OKLab back-projection pass left a mottled sheen and was dropped.

## Files

| File | What |
|---|---|
| `briefs.json` | The costume brief per character, its pronoun, its classic folder and any uncertainty; the region phrases |
| `describe.md` | How to write a brief |
| `sheet.py` | Classic frames sheet for describing |
| `front.py` | Brief → prompt → front paintings on ComfyUI |
| `front-template.graph.json` | The ComfyUI graph (456's approved front, `live-evidence/h3-orbit-control-20261114-r01/village/456-stB.png.graph.json`) |
| `back_ph3.py` | Back-view candidates from a selected front image |
| `sprite-anchored-wip/` | The separate sprite-anchored experiment and its two-stage graph |
