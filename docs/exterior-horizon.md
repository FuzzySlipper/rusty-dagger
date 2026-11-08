# Exterior horizon

Exterior ground is one function drawn by two Engine heightfields. Near ground stands in
the world from the player's feet to the edge of the walked seven-by-seven window; far
ground stands in the backdrop past it, so the map itself reaches the skyline. Refreshed
nature grows over near ground as an Engine vegetation field; classic art keeps the donor's
nature in the walked window's cells. Neither heightfield collides: a heightfield collider
reads the same function, and it is what the player and anyone in the wilderness stand on.
The far plane clips the whole world, so outdoors the camera draws out to `drawnMetres`,
past near ground's corners; under a roof it keeps `camera.farPlane`.

| Part | Owner |
| --- | --- |
| The height function, shared by walked and far ground | `DaggerfallTerrainSurfaceBuilder.NormalizedHeight` |
| Ground at a vertex with its location's flattening, shared by near and far ground | `DaggerfallHorizonGround.Ground` |
| What ground is at a point: height, rings, lift, paint | `DaggerfallHorizonGround` |
| Both heightfields, backdrop link, following the player | `DaggerfallHorizon` |
| What exterior characters stand on | `DaggerfallExteriorGroundCollider` |
| Haze, cloud layer, which sky draws, falling weather, wet ground and snow | `DaggerfallWeatherPresentation` |
| Moons | `DaggerfallMoons`, `SkyMediaPublication` (importer) |
| Panoramas with the painted horizon cleared and the zenith closed | `SkyHorizonClearing` (importer) |
| Sun, ambient and how lights scale | `DaggerfallSiteLighting`, `siteLighting` in the tuning payload |
| Lift, haze and clouds | `horizon` in the tuning payload |
| What open ground is made of | `DaggerfallGroundBlend`, `ground` in the tuning payload |
| A ground archive's layers as materials | `DaggerfallGroundLayers` |
| What a location's tiles are at their corners | `DaggerfallGroundKinds` |
| The seabed, and which map pixels hold sea | `DaggerfallHorizonGround` |
| The sea's sheets and how they follow the player | `DaggerfallHorizon` |
| Rivers' courses and what they cut into the ground | `DaggerfallTerrainRivers` |
| River water about the player | `DaggerfallRiverWater` |
| How the sea draws | `water` in the tuning payload, `DaggerfallWaterTuning` |

## What it stands on

The collider uses near ground's finest cells in near ground's frame, so feet and the drawn
surface agree, and it reaches as far as that finest ring. It holds ground about the player
and every living actor outside a town's people, so an enemy left behind keeps its floor.
The people of a town stand on the town's collision-only ground plane instead, which lies
level with the flattened ground and keeps them standing past the collider's reach, in a
neighbouring location the player has not reached. Tiles arrive over updates, so arrival and
load wait for the ground about every one of those people, not only the player, and play holds
until it stands: anyone who fell meanwhile would have the ground install around them, which
the Engine refuses. Travel makes a new collider; an Engine origin rebase carries the old one.

## How it draws

- **Rings.** Near ground samples half a tile (3.2 m) out to about 400 m, so every tile
  has a sample of its own and a path one tile wide keeps its shape; the walked spacing to
  about 1.2 km, as far as a tree's foot shows; and four times that to the window's edge.
  Far ground's three rings reach about 200 km. Their cells are whole multiples of the
  walked ground's 6.4 m spacing, so every sample is a vertex of the walked surface.
- **Near ground** draws that surface as the walked window collides with it, levelled under
  locations, and between vertices a straight line. It stands in the origin's own frame:
  the map's corner is too far off to keep a texel exact in the shader. A new origin builds
  it again, and the loading screen holds until it has settled, so nobody watches it
  stream in coarsest first.
- **Join.** The world draws over the backdrop, so only near ground shows inside the
  window. Far ground is sunk under it and eases back out past it: at its own height it
  would poke through wherever its coarser cells stand above the near ground.
- **Lift.** The map's ranges rise only a degree or two over the skyline at true
  height. Ground above its valley floor (the lowest ground within about 20 km,
  smoothed) is multiplied by `lift`, fading in along a straight line between
  `liftFromMetres` and `liftFullMetres`. An eased fade loses height faster than walking
  toward a range gains it, so the range would shrink on approach. The floor is fixed on the map, not measured from the player: a
  lift that follows the player moves every tile at once, and tiles sampled either
  side of the change tear arches into the ranges until they are all sampled again.
  The fade's centre follows every `followMetres`, small steps so tiles sampled either
  side of one barely differ, and a move resamples only the fade zone. Far tiles commit a
  few at a time, since a step samples hundreds again.
- **Sky.** The classic paints its horizon into the panorama's lower third, where it
  would float over the real skyline. Each panorama also ships cleared of it, and
  the Engine cloud layer stands in for the low cloud lost with it, drawn in cells anchored
  to the clouds (`cloudPixelMetres`, `cloudShadeSteps`) so it sits with the pixel art.
  Every column of a panorama meets overhead, so the cleared sky also closes its top rows
  on each row's mean colour. A night sky's painted stars come out at every height: a panorama's
  texels stretch into pale quads high up, so the Engine draws the stars as points instead, from a
  generated catalogue. They wheel about their pole with the day and come out as the light goes.
- **Moonlight.** The night's ambient follows the moons: all of it under a full Masser and
  Secunda both overhead, down to `moonlessNight` with neither, each by its phase (`masserLight`
  shares them out) while it stands above the horizon. Thick weather closes over them, so those
  nights are dark too, and classic art keeps its one level. A bright moon washes out the
  faintest stars and lights the clouds.
- **Moons.** Masser and Secunda are the classic's art for each of the lunar cycle's 32
  days, published as an atlas each, and the frame comes from the calendar's own cycle, so
  the sky shows the phase lycanthropes and enchantments answer to. A moon falls behind the
  sun a day at a time from new, so a full moon rises at dusk. Their paths lean south
  (`masserTiltDegrees`, `secundaTiltDegrees`), and they stand with the cleared sky only:
  the classic's painted sky has none, and thick weather closes over them.
- **Fog.** The classic's clear fog ends at the window's edge and would hide the
  horizon, so clear weather uses thin exponential haze instead, its colour the cleared
  sky's own horizon. Rain, snow and fog keep their densities and hide the far ground:
  they close within a kilometre or so, and ground the fog has wholly taken would
  still stand out as a flat shape of the fog's colour. Their fog is the weather's own
  [air](#weather).

Near ground stands whenever the player is outdoors, whatever the art. Far ground stands only
while classic art is off: classic art shows the painted sky, classic fog and no cloud layer.

## Light

Brightness comes from the lights themselves, not the camera: exposure multiplies
everything in the world, glowing art included, which bleaches it. The tone operator is
`Rolloff`, which leaves everything below a highlight exact, where `Neutral` and
`AcesFilmic` crush the low-key art of night and interiors. Only what emits above 1 rolls
off, and that is what `bloom` spreads.

- **One sky.** The world has one sun and ambient, carried by the active site and kept
  when its location unloads behind the player. Resident locations bring only their lamps.
- **Scale.** The Engine divides light by pi, so `siteLighting` levels are multiplied per
  kind of place. Outdoors, ambient and sun together make pi at noon, which lands level
  ground at its texture's own value. Ambient carries most of it: an upright sprite takes
  little of an overhead sun, and would fall dark at noon.
- **Night.** The ambient cools toward `nightColor`, so a lamp's warmth stands out. A grade
  would cool the lamps too. The sun warms toward `lowSunColor` near the horizon.
- **Sun shadows.** The sun always asks for its shadow (`sunShadowRange`,
  `sunShadowResolution`). It draws only when the player turns on Shadows in Settings'
  video options, which are off by default. With ambient carrying most of the light, the
  shadows are faint: a sun-heavy balance would deepen them, but it darkens upright sprites
  across their faces. Static flats neither cast nor receive; enemies cast as before.
- **Sprites.** Flats, nature, people and items take the light around them
  (`SpriteLightingMode.Synthetic`), so they fall with the day.
  Each kind of flat in the `flats` tuning (nature, decorations, actors, items) stands
  upright and faces by `heading`, taking the camera's heading as the original game's flats
  do, so walking past a tree doesn't turn it, or by `position`, turning toward the eye.
- **Flora in the wind.** Wild nature and placed flats from the nature archives lean in the
  day's wind from a planted base (`floraWindBend`, metres per metre of height at full
  wind), each by where it stands, and by whole texels (`floraWindWholeTexels`) so the
  pixel art steps rather than slides. Lamps, signs and the rest stand still.
- **Windows.** Building windows glow on the city lamps' hours through mesh emission. The
  importer publishes each window texture's glass as a mask beside it, and the location's
  material holds that mask as its emission map, so nightfall changes only the emission's
  level (`windowColor`, `windowLevel`). Refreshed walls keep the classic's window layout,
  so the classic's mask serves both art sets. Glass outshines the night rather than
  lighting the street: windows carry no lights.
- **Lamps.** A flat that gives light takes the light around it like any flat, and shines
  over it through sprite emission. Which classic flats give light follows the donor's
  emissive list: the fireplace, lamps, lanterns, torches, candles, braziers, the glowing
  statue, the brewing potion, the Mantellan Crux's fire and the magic decorations, and the
  magic candle of the Light spell. A refreshed flat shines through an emission map
  `promote.py` cuts from its own crunched atlas, in the same layout, so only its flame or
  glass glows; a classic one, or a refreshed one with no map, through its own texture, as
  the donor reuses a lamp's colours. Outdoors the level (`lampLevel`) follows the city
  lamps' hours; under a roof it never drops. Emission lights nothing else: the placed point
  lights do that.
- **Glowing creatures and spells.** A creature with a glow colour (the fire and frost
  daedra, the fire atronach) takes the light like any enemy and shines over it through its
  own texture at `creatureGlow`, as do its remains; its point light still lights the room.
  Fire, shock and magic effects, missiles and impacts, shine over their colours at
  `spellGlow`, above 1 so they bloom. Ghosts and wraiths shine at `creatureGlow` through a
  glow the importer publishes beside their atlas in its layout (`{sprite}-glow`), since
  only the source still marks their eyes: the eyes red, the body its own grey times its
  brightness to the 1.9, so bones and skulls stand out of a dim shroud.
- **First person.** The viewmodel layer doesn't take the scene's lights, so the view takes a
  tint from the light the eye stands in (`DaggerfallViewLight`): the sky plus any lamp, glowing
  mobile, spell or magic candle in reach, by the renderer's own falloff. `viewFull` is the light
  that draws the art at its own value, the clear noon sky, and `viewFloor` keeps the hands
  readable in the dark. It is sampled every few updates and moves in steps. Held meshes multiply
  it into their material factors, over any metal. A sprite running a playback can't be replaced
  with a new tint, and lighting a screen-space sprite turns it white, so the classic hand holds
  it as a paused Engine tint tween.

## Weather

Weather is facts first. `DaggerfallWeatherConditions` states what is falling and how hard,
the wind, and how wet and snowed open ground is, per climate. Wet and snow build and fade
with world time through `DaggerfallWeatherState`, so ground stays soaked after the rain,
and snow lies through winter and melts after it. They save with the game. Presentation
draws these facts and rules should read them too, through
`DaggerfallSession.CurrentConditions`, rather than asking which sky is up.

- **Falling.** Rain and snow are the Engine's precipitation, drawn as hard blocks of
  `precipitationPixels` screen pixels: a flake is a block, or a few as it nears the eye, and
  rain a stepped line, so neither swells soft past the pixel art. The day's wind slants them. They ease
  with the weather and at a shelter's edge; through a door they are there or gone at once.
  Their colour follows the daylight, down to `nightFall`.
- **Ground.** `wetLook` and `snowLook` map the facts to the Engine's wetness and snow.
  `snowLook` stops short of the Engine's full cover, which would bury the art in one flat
  colour: the ground shows through in drifts stepped at the ground's own texel size, and
  winter freezes the puddles.
- **Air.** Over the cleared sky, fog, rain, storms and snow close the sky in a colour of
  their own (`airColors`), which falls with the daylight and flares with lightning. Fog
  takes the same colour, so the world fades exactly into it in every climate, whatever
  a day's panorama would have shown under the clouds.
- **Sprites stand in the fog.** World sprites have hard edges and write depth
  (`SpriteAlphaMode.Mask`). The Engine fogs by depth, so a blended sprite takes the fog of
  what is behind it, and a tree against the sky would stay sharp in mist. Only glowing and
  translucent sprites blend.
- **Wind** turns each day (`windDegrees` in the save), and drives the clouds' drift,
  the rain's slant and the flora's lean. Cloud colour is by weather too (`cloudColors`), so foul skies close
  dark.
- **Classic art** keeps its painted sky under every weather, with the same falling, ground
  and wind.

## Ground

Every vertex names a material and weighs that material's four layers. Refreshed surfaces wear
the layers where the art set has them, and the classic's where it doesn't.

- **Open ground** weighs grass, dirt, rock and snow. The classic's ground noise still lays
  out grass and dirt, real slope bares the rock, and real height brings the snow, which a
  rock face holds less of. Far ground reads the same rules, with rock beginning at a gentler
  slope because its coarse cells smooth one.
- **Crunch.** `contrast` narrows every transition toward a pixel edge, and textures stay
  nearest-filtered, so layers meet as chunky pixels rather than a soft fade.
- **Rock.** A layer has to be rock for a slope to bare it. The temperate and mountain
  winters are snow on every record, so they wear the summer's stone, and the swamp's third
  record is lush ground, so it wears the mountains'.
- **Nature** grows from soil, so it thins by the share of the ground that is soil:
  there is a treeline at the snow, and bare rock stays bare.
- **Refreshed nature** is `DaggerfallFloraField`, a vegetation field in near ground's frame
  sampled at the walked spacing (`DaggerfallHorizonGround.SampleFlora`). Each record of a
  climate's nature archive is a sprite layer growing on that archive's ground class alone,
  every record as often as the others. A vertex's cover is the donor's chance by the
  ground's look (`grassChance`, `dirtChance` and `stoneChance` over the blend's grass, dirt
  and rock, none on snow), thinned by its map pixel's height and in deserts
  (`desertShare`), and none on a beach or bank or in a location's clearing. So dirt
  patches thin nature where they show, not where the classic's tiles were. `density`
  multiplies the donor's count (three times by default, which reads as forest where the
  donor's read as parkland), and `climateDensity` gives each named climate its own share of
  that. `world.flora DENSITY` changes it for a run. A record is drawn out to `reachPerMetre` times its height, at most
  `reach`, and shrinks away over its last `fade`, so the edge never pops. Winter swaps every
  climate for its winter set and samples the ground again. Past near ground's reach the
  field would have to stand in the backdrop with far ground, which vegetation fields can't
  do yet.
- **Ground cover** is refreshed grass near the player (`DaggerfallGroundCover`), two ways at
  once. Shells are a stack of `shellLayers` layers `shellGap` apart over near ground, out to
  `shellReach`, on near ground's own finest vertices and diagonals so they never part from
  it. Each layer is cut out where the shell texture's blade height (its alpha) falls below
  its own, so pixel blades rise out of the ground, darker toward their roots. The grass's
  share of the ground's look thins them, beaches and banks stay bare, and the
  blades shorten away over the last `shellFade`. A location's own ground grows cover from
  its tiles (grass and dirt; paving, ruts and water stay bare) at `townShare` of its
  share, rising to full over `townRamp` metres beyond its flattened ground. Their shader slides the higher layers with
  the wind by whole texels (`shellSway` at the top). Tufts are three crossed quads of pixel
  grass, a layer per climate (`tuftDensity` to `tuftReach`), their tips fluttering, in a
  vegetation field of their own whose cover is the grass's share, thinned in towns the same
  way, rather than flora's, which keeps out of every clearing. The player chooses in the
  video settings whether ground cover grows and how far it reaches (clutter distance, 0.5
  to 2 times the shells' and tufts' reach and fade).
  Both wear the climate's grass: the art set's `groundCover` names each ground archive's
  grass colour, measured from its refreshed grass surface by
  `authoring/tools/groundcover/groundcover.py`, which also makes the textures. The deserts'
  sand and every winter set have none, so they grow no grass. A tuft's foot is as light as
  the ground, so it stands in it rather than over it.
- **Relief** lifts every other layer of open ground the same way: dirt, rock, the desert's
  sands, the snow and a swamp's lush ground. Its texture is the layer's own refreshed ground
  texture, laid as the ground lays it (across the map's z, down its x, one repeat to
  `repeatMetres`), so its texels stand on the ground's texels; its alpha is each texel's
  height. The tool sets the heights by kind: dirt rises in clods with its green texels
  standing as stray grass, rock in pebbles, sand and snow in broad heaps, and lush ground as
  tall grass that sways. Each kind's look (in the art set beside its texture) sets how its
  lowest layer is shaded, how far its top sways and how many of its top texels glint: snow's
  hollows go cold blue, its loose top drifts downwind by whole texels and its crests glint
  as the view moves; and lying snow buries every relief but the snow's own.
- **Where ground kinds meet,** a stack's weight at each vertex thins it. The shells' shader
  makes every layer's cut itself, so with `shellRagged` at 1 a thinning layer keeps its
  texels at full height in clumps of a few texels that drop out one by one, and one ground
  gives way to the next in patches rather than shrinking evenly (0 shrinks it evenly). The
  shadow takes the even cut. A stack draws only the layers its tallest texel reaches and
  only the triangles where some of it remains. `world.groundcover` draws only the kinds it names
  (grass, relief, tufts) for a run.
- **Tree meshes** stand in for the big tree sprites of every climate that the art set's
  `trees` names (`DaggerfallTreeMesh`): conifers as a fir or pine, layered flat-topped
  trees as a cork oak, round crowns as a beech or chestnut, palms as a palm, slender bare
  trees as a dead tree, a winter set's trees as its summer ones'. Trees whose shapes are
  their own (vine-wrapped and bulb-trunked swamp plants, cacti, massive gnarled bare trees)
  stay sprites, with the bushes, rocks and small plants. They are rusty-craftsurvive's scripted trees: low-poly trunks and
  branches in tiling bark, and leaf cards whose vertex alpha is how much each flutters,
  copied with the cards and branches below the sprite's crown left out so its bare trunk
  shows, and each bark triangle turned to face along its normals (the source winds its
  tubes inward). `authoring/tools/trees/trees.py` copies them and repaints their textures in
  the sprite's own colours: each texel takes the colour of the sprite's texel at the same
  rank of brightness, leaves from its crown and bark from its trunk (both without the
  outline's darkest). A leaf is one flat tone, so leaves take the crown's leading hue (green,
  or an autumn tree's orange) and its shadows, other hues at most a tenth of them, and no
  snow. A winter tree's leaves carry snow instead through a shader (`snow.wgsl`): where a
  card faces up, drifts of smooth noise over the leaf texture's texels are drawn in the
  sprite's snow colour, cut at the texture's pixels and leaving its dark outlines bare, over
  a share of the upward faces measured from how much of the sprite's crown is snow; leaves leaning toward the darks that lit cards lose, each leaf's edge a step
  darker as the sprites outline theirs. A mesh is drawn at its sprite's drawn height.
  Bark bends with the wind as the sprites do; leaves flutter too. Past
  `treeImpostorHeights` times its height (where it stands about as tall on screen as the
  card's 64 texels) the renderer draws a card it bakes of the mesh, and a mesh casts shade
  where a sprite cannot. Large trees are meshes, where the art set has one, because a big sprite reads as a standee
  at near and middle range; bushes, rocks and small plants stay sprites. Flora's
  `treeMeshes` turns them off, and `world.trees sprite` for a run.
- **Classic nature** stays the donor's: one record chosen at random on a tile, by the
  classic's tile kinds, in the walked window's cells.
- **Swamps** weigh lush ground in place of snow, below the snow.
- **Towns** paint from their tiles. Every ground record is a corner pattern of six kinds:
  water, dirt, grass, stone, paving and ruts. The terrain lookup fixes the frame: a record's
  image has x running right and y up, a rotation is a quarter turn anticlockwise and a flip a
  half turn. Paving edges (47 and 55) divide a tile along a diagonal, so their one odd corner
  is half their area. A vertex on a tile's corner shares it with three others, and a sample
  inside a tile reads that tile alone.
- **The shore** follows the land's real contour. The classic's beach band is dirt, and it runs
  on down as the seabed. A location's water tiles paint as water on its ground.
- **Wet weather** darkens ground through the Engine's wetness, not the classic's rain archives.

## Sea

The sea has one surface, the ocean clamp's height, and ground rises through it, so a shore is
wherever land meets the water. It fills the map's long channels, like the one past Rlorht Manor,
and any channel that reaches the coast: higher up, [rivers](#rivers) stand at their own surfaces.
A location's own water tiles on higher ground stay painted, since the classic's water volumes
stop at the sea's surface and nobody can swim in them.

- **Seabed.** Near ground and the collider run on down under the sea, falling away from land
  and levelling off about 12 m down. The depth comes from how far a point is from land. The
  classic's heights under its clamp can't serve: nothing ever showed them, and they jump
  metres at map pixel edges. Far ground stops at the surface.
- **Sheets.** Each map pixel where near ground found seabed gets one flat sheet of Engine
  water, out to a pixel short of near ground's reach, so a sheet never hangs over far land.
  Inland no sheet stands and no view pays for water. Past the map's north and west edges the
  sea runs on, sheets included. Sheets are meshes because a heightfield's
  tiles hang skirts, which show through water.
- **Look.** A sheet wears the climate's own water art, repeating as ground does, under
  `water`'s depth tint, with the Engine's own ripples and shore foam. Water's colours are its
  own, so the sky's light tints them. It is one-sided: seen from under the surface,
  foam would hang overhead.
- **Far sea.** Past the sheets the sea is one flat colour (`sea`), on near ground's outer ring
  and on far ground. It glows that colour under the same tint, so the world's lights can't
  part it from the water it meets.
- **Swimming.** The cell's water volumes reach the same surface, and a swimmer floats at it,
  riders included, since the seabed would otherwise walk a horse under. The head stays above
  water, so nobody drowns at sea.

## Relief

Where the data carries `worldrpg/terrain/relief.bin`, the walked and far ground both
stand on it, through the same height function. Without it they stand on WOODS.

- **Bake.** The import's `relief` step (`ReliefBake`) runs stream-power erosion over
  the map: the coast is fixed, climate sets the rain, and uplift is calibrated back to
  WOODS's own heights, so the land keeps its shape and gains valleys and ridges. A node's
  water is shared among all its lower neighbours by slope. A single receiver cuts every
  hillside into ribs along the lattice, whatever spacing it has. All its noise comes from
  coordinate hashes, so a bake depends only on its input.
- **Cascade.** The landscape evolves on a coarse lattice, where the calibration closes on
  the classic heights, then carries on to finer ones. Each starts from the last and cuts what
  that one could not hold, so the cost of the finest lattice buys only its last few steps.
- **Locations.** Every location gets a calmer pad in the bake, so its flattening
  doesn't stand on a cliff. Location heights are sampled at runtime, so they follow.
- **Coast.** The bake's share falls to nothing within 300 m of the sea, so the classic
  height returns there and shores stay where they were. The band is narrow because
  the classic's terrace cliffs turn into sheer walls once the horizon lifts them.
- **Published form.** Heights are 16-bit eighths of a metre over the whole map, and shares
  stay on the coarsest lattice. Holding detail only where it shows saves little: erosion
  reaches nearly every land tile, and the sea is only a third of the map.
- **Detail.** The lattice holds valleys and ridges down to about two nodes. Kit's
  `ErosionFilter` cuts gullies below that. Each sampling skips what it can't hold, so far
  rings don't alias. A walked surface reads the relief from a 25.6 m patch rather than per
  vertex, since the filter is its costly part.

## Rivers

The classic map has no rivers: WOODS holds a sea, a few ponds and short inlets, and the
region maps paint coasts alone. Every river is the bake's own drainage, published as
`worldrpg/terrain/rivers.bin`.

- **Tracing.** Water is routed down the finished lattice, each node to its steepest
  neighbour, with every location's map pixel walled off: a location levels its pixel, and a
  channel with it. A river starts where a catchment passes 150 km² of ordinary rain, so dry
  climates carry fewer. The largest donor at a confluence carries the trunk.
- **Courses.** A course is a line of stations about 48 m apart, smoothed off the lattice.
  Width follows the root of the catchment, and a head tapers in over its first 3 km, as a
  catchment reaches the source all at once. The surface comes from the valley's own lowest
  nodes, only ever falls, and a tributary ends on the surface of what it joins, so water never
  climbs. Ground that stands in the way is cut through. Rivers are read before the locations'
  pads, which blur valley floors up into dams.
- **Floor.** The bake lowers the lattice toward the water out from each course, as far as
  its width carries, so a large river opens a flat valley. Ground is only ever lowered.
- **Channel.** The height function cuts a channel and its banks at runtime, on top of the
  relief and the filter, so collider, near ground and far ground all carry it. The bed follows
  the water both ways, and a bank's lip stands over it whatever the ground does, so water never
  spills. Gullies fade out toward a channel: cut beside it, they would leave the land below the
  water and the river on a levee. A sampling too coarse for a channel widens it to its own cells,
  and one far too coarse leaves it out.
- **Water.** Each map pixel about the player holds one mesh, a ribbon of sections level
  across and falling with the river, as far as the walked ground holds a channel. Past that the
  channel wears far ground's sea, as a bed does past the sheets. A channel that has come down to
  the ocean clamp is the sea's: its sheets fill it, and the ribbon stops.
- **Swimming.** Each section has a box that says someone may be in the river. It reaches the
  section's higher end and overhangs its banks and neighbours, so its top is no surface: a swimmer
  floats at the river's own surface at their position, however steep the reach, and past the banks
  there is no water.
- **Banks.** A channel's bed and lip paint as shore, and nature keeps off them.

## Tuning by eye

`world.horizon-set LIFT FOG` changes the lift and haze for one run, `world.ground-blend
CONTRAST METRES SHARPNESS` the ground's blend, `world.weather KIND` and `world.ground-weather
WET SNOW` the weather, and `world.hour HOUR` sets the light. Over bare WOODS its stepped heights make ranges into
mesas at any lift; the relief makes them peaks.
