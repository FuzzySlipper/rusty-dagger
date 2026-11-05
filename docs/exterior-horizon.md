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
  inside the window. Far ground there stands at true height, so it can't rise
  above the walked ground it lies under.
- **Lift.** The map's ranges rise only a degree or two over the skyline at true
  height. Ground above the player's own is multiplied by `lift`, fading in between
  `liftFromMetres` and `liftFullMetres`. Lift is measured from the player, so a
  plateau the player stands on doesn't rear up around them. The centre follows
  every `followMetres` and the base every `baseStepMetres`; a move resamples the
  fade zone, a new base every tile.
- **Sky.** The classic paints its horizon into the panorama's lower third, where it
  would float over the real skyline. Each panorama also ships cleared of it, and
  the Engine cloud layer stands in for the low cloud lost with it.
- **Fog.** The classic's clear fog ends at the window's edge and would hide the
  horizon, so clear weather uses thin exponential haze instead. Rain, snow and fog
  keep their densities, which close over the far ground as they should.

The horizon stands only outdoors and only while classic art is off. Classic art
shows the painted sky, classic fog and no cloud layer.

## Tuning by eye

`world.horizon-set LIFT FOG` changes the lift and haze for one run, and
`world.hour HOUR` sets the light. Lifts of 6 to 10 give layered ranges; WOODS's
stepped heights make them mesas at any lift.
