# Exterior horizon

Past the walked seven-by-seven window, the map itself stands on the skyline. The
walked ground keeps its meshes, collision and nature; the far ground is an Engine
heightfield in the backdrop that only draws.

| Part | Owner |
| --- | --- |
| The height function, shared by walked and far ground | `DaggerfallTerrainSurfaceBuilder.NormalizedHeight` |
| Far ground: rings, lift, colours | `DaggerfallHorizonGround` |
| Heightfield, backdrop link, following the player | `DaggerfallHorizon` |
| Haze, cloud layer, which sky panorama draws | `DaggerfallWeatherPresentation` |
| Panoramas with the painted horizon cleared | `SkyHorizonClearing` (importer) |
| Sun, ambient and how lights scale | `DaggerfallSiteLighting`, `siteLighting` in the tuning payload |
| Lift, haze, clouds and far colours | `horizon` in the tuning payload |
| What open ground is made of | `DaggerfallGroundBlend`, `ground` in the tuning payload |
| A ground archive's layers as one material | `DaggerfallGroundLayers` |

## How it draws

- **Rings.** Three rings reach about 200 km. Their cells are whole multiples of the
  walked ground's 6.4 m spacing, so every far sample is a vertex of the walked
  surface, before any location flattens it. The finest ring's tile is one map pixel.
- **Join.** The world draws over the backdrop, so only the walked ground shows
  inside the window. Far ground is sunk under the window and eases back out past it:
  at the walked ground's own height it would poke through wherever the two differ.
- **Lift.** The map's ranges rise only a degree or two over the skyline at true
  height. Ground above its valley floor (the lowest ground within about 20 km,
  smoothed) is multiplied by `lift`, fading in between `liftFromMetres` and
  `liftFullMetres`. The floor is fixed on the map, not measured from the player: a
  lift that follows the player moves every tile at once, and tiles sampled either
  side of the change tear arches into the ranges until they are all sampled again.
  The fade's centre follows every `followMetres`, small steps so tiles sampled either
  side of one barely differ, and a move resamples only the fade zone.
- **Sky.** The classic paints its horizon into the panorama's lower third, where it
  would float over the real skyline. Each panorama also ships cleared of it, and
  the Engine cloud layer stands in for the low cloud lost with it.
- **Fog.** The classic's clear fog ends at the window's edge and would hide the
  horizon, so clear weather uses thin exponential haze instead, its colour the cleared
  sky's own horizon. Rain, snow and fog keep their densities and hide the far ground:
  they close within a kilometre or so, and ground the fog has wholly taken would
  still stand out as a flat shape of the fog's colour. Fog also clears the cloud
  layer, which would darken the sky just above it.
- **Light.** The world's night lights keep far ground's bright vertex colours bright,
  so far ground takes the day's light as a material tint, down to `nightTint`.

The horizon stands only outdoors and only while classic art is off. Classic art
shows the painted sky, classic fog and no cloud layer.

## Light

Brightness comes from the lights themselves, with no tone mapping or exposure. A tone
curve crushes the low-key art of night and interiors, and exposure multiplies everything
in the world, unlit lamp art included, which bleaches it.

- **One sky.** The world has one sun and ambient, carried by the active site and kept
  when its location unloads behind the player. Resident locations bring only their lamps.
- **Scale.** The Engine divides light by pi, so `siteLighting` levels are multiplied per
  kind of place. Outdoors, ambient and sun together make pi at noon, which lands level
  ground at its texture's own value. Ambient carries most of it: an upright sprite takes
  little of an overhead sun, and would fall dark at noon.
- **Night.** The ambient cools toward `nightColor`, so a lamp's warmth stands out. A grade
  would cool the lamps too.
- **Sprites.** Flats, nature, people and items take the light around them
  (`SpriteLightingMode.Synthetic`), so they fall with the day. Lamps (archive 210) stay
  unlit, since a light source shows at its own brightness, and each carries an Engine
  point light from the importer. Sprite materials have no emission input, so a sprite
  glows only by being unlit.
- **Windows.** Building windows glow on the city lamps' hours through mesh emission. The
  importer publishes each window texture's glass as a mask beside it, and the location's
  material holds that mask as its emission map, so nightfall changes only the emission's
  level (`windowColor`, `windowLevel`). Refreshed walls keep the classic's window layout,
  so the classic's mask serves both art sets. Glass outshines the night rather than
  lighting the street: windows carry no lights.
- **Lamps.** A refreshed flat that gives light takes the light around it like any flat, and
  its flame or glass shines through sprite emission. `promote.py` cuts the emission map from
  the flat's own crunched atlas, in the same layout, so no second painting exists. Outdoors
  the level (`lampLevel`) follows the city lamps' hours; under a roof it never drops. Classic
  lamps stay unlit, showing whole at their own brightness as the donor's do. Emission lights
  nothing else: the placed point lights do that.
- **First person.** The viewmodel layer doesn't take the scene's lights, so the view takes a
  tint from the light the eye stands in (`DaggerfallViewLight`): the sky plus any lamp, glowing
  mobile, spell or magic candle in reach, by the renderer's own falloff. `viewFull` is the light
  that draws the art at its own value, the clear noon sky, and `viewFloor` keeps the hands
  readable in the dark. It is sampled every few updates and moves in steps. Held meshes multiply
  it into their material factors, over any metal. A sprite running a playback can't be replaced
  with a new tint, and lighting a screen-space sprite turns it white, so the classic hand holds
  it as a paused Engine tint tween.

## Open ground

Where refreshed art is drawn, open ground blends four layers through an Engine terrain
layer material instead of picking a tile per quad. Classic art keeps the tiles, and so
does an archive the art set doesn't cover whole. A location's authored tiles and quads
touching water stay tiles too, in the same mesh.

- **Weights.** Each surface vertex weighs grass, dirt, rock and snow. The classic's
  ground noise still lays out grass and dirt, real slope bares the rock, and real height
  brings the snow, which a rock face holds less of.
- **Crunch.** `contrast` narrows every transition toward a pixel edge, and textures stay
  nearest-filtered, so layers meet as chunky pixels rather than a soft fade.
- **Rock.** A layer has to be rock for a slope to bare it. The temperate and mountain
  winters are snow on every record, so they wear the summer's stone, and the swamp's
  third record is lush grass, so it wears the mountains'. Borrowed rock shows only where
  the slope earns it: the noise bares only an archive's own.
- **Cliffs.** Textures project from three planes (`triplanarSharpness`), since a single
  projection smears down a steep face. `repeatMetres` must fill a cell a whole number of
  times, or textures break at every cell's edge.
- **Far ground.** At that range a texture is smaller than a pixel, and a heightfield
  takes colours or layer weights, not both, so far ground keeps vertex colours and its
  climates. Those colours are the walked ground's own as noon lights it, and snow comes
  from the same `snowFromMetres` and `snowFullMetres`. Far slopes are measured over
  coarser cells and read flatter, so the far ground has its own rock thresholds.

## Relief

Where the data carries `worldrpg/terrain/relief.bin`, the walked and far ground both
stand on it, through the same height function. Without it they stand on WOODS.

- **Bake.** The import's `relief` step (`ReliefBake`) runs stream-power erosion over
  the map: the coast is fixed, climate sets the rain, and uplift is calibrated back to
  WOODS's own heights, so the land keeps its shape and gains valleys and ridges. Flow
  leaves by a random slope weighted by steepness, which keeps valleys off the grid's
  axes. All its noise comes from coordinate hashes, so a bake depends only on its input.
- **Locations.** Every location gets a calmer pad in the bake, so its flattening
  doesn't stand on a cliff. Location heights are sampled at runtime, so they follow.
- **Coast.** The bake's share falls to nothing within 300 m of the sea, so the classic
  height returns there and shores stay where they were. The band is narrow because
  the classic's terrace cliffs turn into sheer walls once the horizon lifts them.
- **Detail.** The lattice holds nothing under about 800 m. Kit's `ErosionFilter` cuts
  gullies below that and breaks high ground into peaks. Each sampling skips what it
  can't hold, so far rings don't alias. A walked surface reads the relief from a
  25.6 m patch rather than per vertex, since the filter is its costly part.

## Tuning by eye

`world.horizon-set LIFT FOG` changes the lift and haze for one run, `world.ground-blend
CONTRAST METRES SHARPNESS` the ground's blend, and `world.hour HOUR` sets the light. Over bare WOODS its stepped heights make ranges into
mesas at any lift; the relief makes them peaks.
