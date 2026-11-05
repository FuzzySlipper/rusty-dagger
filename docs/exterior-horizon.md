# Exterior horizon

Past the walked seven-by-seven window, the map itself stands on the skyline. The
walked ground keeps its tiled meshes, collision and nature; the far ground is an
Engine heightfield in the backdrop that only draws.

| Part | Owner |
| --- | --- |
| The height function, shared by walked and far ground | `DaggerfallTerrainSurfaceBuilder.NormalizedHeight` |
| Far ground: rings, lift, colours | `DaggerfallHorizonGround` |
| Heightfield, backdrop link, following the player | `DaggerfallHorizon` |
| Haze, cloud layer, which sky panorama draws | `DaggerfallWeatherPresentation` |
| Panoramas with the painted horizon cleared | `SkyHorizonClearing` (importer) |
| Lift, haze, clouds and far colours | `horizon` in the tuning payload |

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

`world.horizon-set LIFT FOG` changes the lift and haze for one run, and
`world.hour HOUR` sets the light. Over bare WOODS its stepped heights make ranges into
mesas at any lift; the relief makes them peaks.
