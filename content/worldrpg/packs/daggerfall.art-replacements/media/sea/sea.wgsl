// The sea and the rivers (#10906, #10910): waves the wind raises, its ripples and whitecaps, and the paths the moons and a
// low sun lay on the water. One shader draws the Engine water sheets near the player, the far sea past them (a flat
// glowing colour on the ground's heightfields, keyword FAR) and the rivers (keyword RIVER). All take the same waves,
// ripples and glints from where they stand in the world, so nothing changes where the sheets end.
//
// Waves are eight Gerstner trains spread about the wind, under a wind sea's spectrum (fetch-limited growth, JONSWAP),
// with the open sea's swell on the longest; learned from rusty-craftsurvive's SeaWaves. Close to the eye the sea's sheets
// are fine enough to be moved by them (displace), less over shallows so they die toward the shore; past `reach` they only
// tilt the light, and a train too small for a
// pixel fades to the flat surface while its slopes widen the glint, as waves too far to see still spread the moon's
// path. Rivers, a quad a section, never move: their waves only tilt.
//
// Ripples come from a map (ripples.png, made by authoring/tools/sea/sea.py) laid along the wind and drifting downwind,
// stronger in a wind and in patches. Its alpha holds each texel's squared tilt, so a mip's alpha less its tilt's squared
// length is the tilt the ripples it averages away still have, and that too widens the glint. In a fresh wind whitecaps
// break on the waves' downwind faces in the foam map's patches (foam.png); far off they are a share of the sea.
//
// A moon's or the sun's reflection is a Gaussian lobe about the mirror direction, as wide as the disc and the waves the
// view can't see: its peak is the disc's light shared over the lobe, so a wider path is a dimmer one. Glitter stands in
// for ripples that have shrunk below a pixel: cells of water that flash at their own rates, a pixel or two along the
// view, so the path glints out to the horizon and the far sea shimmers in its own colour.
//
// parameters[0]: x the bearing the wind blows toward (radians from +x toward +z), y the highest significant wave height
//   drawn (metres), z the wind's speed (m/s), w its fetch (metres).
// parameters[1]: x the moons' glint, y the low sun's; z the glint's spread (radians); w how far from the eye the sea's
//   sheets are fine enough to move, metres: they move fully to 60% of it and not at all past it.
// parameters[2]: x, y each moon's lit fraction, 0 new to 1 full (the moons' art draws their phase, so the sky's is
//   whole); z the ripples' repeat (metres); w their tilt.
// parameters[3]: xyz the sea's colour under the sky's light; w how far glitter shimmers it where the ripples have faded.
// product_map_a: the ripple map; product_map_b: the foam map. Both are data, opened linear and repeating.
//
// authoring/tools/sea/sea.py states this file's payload entry (artReplacements.sea.shader): run it after editing.
#import rusty::types::{Surface, Vertex}
#import rusty::material::{material, product_map_a, product_sampler_a, product_map_b, product_sampler_b}
#import rusty::view::{frame, world_equivalent}
#import rusty::shade::standard_shade

const TAU: f32 = 6.2831853;
const GRAVITY: f32 = 9.81;

// The trains: wavelength (metres), bearing off the wind (radians) and phase. Log-spaced from 4 m, four vertices of the
// sheets' mesh a wave, to 43 m.
const TRAINS: i32 = 8;
const WAVELENGTHS: array<f32, 8> = array<f32, 8>(4.0, 5.6, 7.9, 11.0, 15.5, 22.0, 31.0, 43.0);
const BEARINGS: array<f32, 8> = array<f32, 8>(0.35, -0.30, 0.18, -0.42, 0.26, -0.12, 0.08, -0.05);
const OFFSETS: array<f32, 8> = array<f32, 8>(0.0, 1.7, 4.1, 2.7, 5.3, 0.9, 3.6, 2.2);
// The open sea's swell, metres of amplitude on the longest train, whatever the local wind; rivers have none.
const SWELL_TRAIN: i32 = 7;
const SWELL_AMPLITUDE: f32 = 0.15;
// Fetch-limited growth: Hs = HEIGHT_GROWTH·U·√(F/g), gTp/U = PERIOD_GROWTH·(gF/U²)^⅓; a fully developed sea:
// Hs = DEVELOPED_HEIGHT·U²/g, Tp = DEVELOPED_PERIOD·U/g.
const HEIGHT_GROWTH: f32 = 0.0016;
const PERIOD_GROWTH: f32 = 0.286;
const DEVELOPED_HEIGHT: f32 = 0.21;
const DEVELOPED_PERIOD: f32 = 7.16;
const LEAST_FETCH: f32 = 1.0;
// The spectrum about its peak: longer waves fall away within this width (natural-log wavelength); shorter ones keep
// amplitude in proportion to wavelength, every short wave about as steep as the next (the wind sea's tail).
const SPECTRUM_WIDTH: f32 = 0.45;
// No train is steeper than this (wave number times amplitude), so crests never fold.
const STEEPNESS: f32 = 0.12;
// How far a crest moves sideways against its height (Gerstner's Q).
const CHOPPINESS: f32 = 0.9;
// A wave fades between a quarter of its length and three fifths of it to a pixel along the view.
const SEEN_FROM: f32 = 0.25;
const SEEN_TO: f32 = 0.6;
// The sheets move fully to this share of their reach.
const MOVED_FULLY: f32 = 0.6;

// How fast the ripples, gusts and foam ride downwind, metres a second: about a short ripple's own speed, whatever the
// wind. It stays put as the wind's speed steps, since a speed times the renderer's whole elapsed time would move every
// pattern at each step; only their bearing turns, which happens while the sea is out of view.
const RIPPLE_DRIFT: f32 = 0.25;
// A calm's ripples, as a share of a fresh wind's (RIPPLE_WIND, m/s).
const RIPPLE_CALM: f32 = 0.3;
const RIPPLE_WIND: f32 = 8.0;
// The second, finer ripple map: its repeat as a share of the first's, turned off the wind, drifting faster, and weaker.
const FINE_SCALE: f32 = 0.37;
const FINE_TURN: f32 = 0.6;
const FINE_DRIFT: f32 = 1.7;
const FINE_WEIGHT: f32 = 0.5;
// Gusts: the ripples stand stronger and weaker in patches this large (metres), from the foam map.
const PATCH_METRES: f32 = 90.0;
const PATCH_LEAST: f32 = 0.45;
const PATCH_MOST: f32 = 1.3;

// Whitecaps (the look only): they begin in a fresh breeze and cover the steep downwind faces in a gale, in the foam
// map's patches, which drift with the surface. Where the waves are too small to see, they are a share of the sea.
const WHITECAP_WIND_START: f32 = 7.0;
const WHITECAP_WIND_FULL: f32 = 18.0;
const WHITECAP_FACE_START: f32 = 0.1;
const WHITECAP_FACE_FULL: f32 = 0.25;
const WHITECAP_PATCH_START: f32 = 0.72;
const WHITECAP_PATCH_FULL: f32 = 0.9;
const WHITECAP_FAR_SHARE: f32 = 0.08;
// The waves whose faces break, metres: past where a pixel can show them, whitecaps are the share.
const WHITECAP_WAVELENGTH: f32 = 8.0;
const FOAM_METRES: f32 = 9.0;
// The froth inside a patch: the foam map again, finer and turned, so a cap breaks up at its edges rather than lying flat.
const FROTH_METRES: f32 = 2.3;
const FROTH_FROM: f32 = 0.3;
const FROTH_TO: f32 = 0.7;
const FOAM_COLOR: vec3<f32> = vec3<f32>(0.78, 0.84, 0.84);
const FOAM_ROUGHNESS: f32 = 0.9;

// The sun glints while it stands under this sine of its height, fading out above it.
const LOW_SUN_FROM: f32 = 0.15;
const LOW_SUN_TO: f32 = 0.45;
// The sun's disc, radians, where the sky draws none.
const SUN_RADIUS: f32 = 0.0047;
// The mean of a twinkle's ((1 + sin) / 2)^6, so glitter keeps the path's brightness.
const TWINKLE_MEAN: f32 = 0.2256;

fn wind_direction() -> vec2<f32> {
    let bearing = material.parameters[0].x;
    return vec2<f32>(cos(bearing), sin(bearing));
}

// Each train's amplitude (metres) for the wind and fetch: Hs is four times the surface's standard deviation, and a train
// of amplitude a has variance a²/2.
fn amplitudes() -> array<f32, 8> {
    var result: array<f32, 8>;
    let wind = material.parameters[0].z;
    let fetch = material.parameters[0].w;
    if wind > 0.0 && fetch >= LEAST_FETCH {
        let height = min(material.parameters[0].y,
            min(HEIGHT_GROWTH * wind * sqrt(fetch / GRAVITY), DEVELOPED_HEIGHT * wind * wind / GRAVITY));
        let period = min(PERIOD_GROWTH * wind / GRAVITY * pow(GRAVITY * fetch / (wind * wind), 1.0 / 3.0),
            DEVELOPED_PERIOD * wind / GRAVITY);
        let peak = GRAVITY * period * period / TAU;
        var lengths = WAVELENGTHS;
        var sum = 0.0;
        for (var i = 0; i < TRAINS; i++) {
            let ratio = lengths[i] / peak;
            let distance = log(ratio) / SPECTRUM_WIDTH;
            result[i] = select(exp(-0.5 * distance * distance), ratio, ratio <= 1.0);
            sum += result[i] * result[i];
        }
        let scale = height / (2.0 * sqrt(2.0) * sqrt(max(sum, 1e-12)));
        for (var i = 0; i < TRAINS; i++) {
            result[i] = min(result[i] * scale, STEEPNESS * lengths[i] / TAU);
        }
    }
#ifndef RIVER
    result[SWELL_TRAIN] += SWELL_AMPLITUDE;
#endif
    return result;
}

fn train_direction(wind: vec2<f32>, i: i32) -> vec2<f32> {
    var bearings = BEARINGS;
    let c = cos(bearings[i]);
    let s = sin(bearings[i]);
    return vec2<f32>(wind.x * c - wind.y * s, wind.x * s + wind.y * c);
}

// A train's phase at a point of the world (metres), now.
fn train_phase(direction: vec2<f32>, i: i32, at: vec2<f32>) -> f32 {
    var lengths = WAVELENGTHS;
    var offsets = OFFSETS;
    let number = TAU / lengths[i];
    return number * dot(direction, at) + offsets[i] - sqrt(GRAVITY * number) * frame.time.x;
}

// How much of the waves the sheets' own vertices carry at a point: all of them near the eye, none past the reach, and
// none on the far sea or the rivers.
fn moved_share(at: vec3<f32>) -> f32 {
#ifdef FAR
    return 0.0;
#else
#ifdef RIVER
    return 0.0;
#else
    let reach = material.parameters[1].w;
    return 1.0 - smoothstep(reach * MOVED_FULLY, reach, length(at.xz - frame.camera.xz));
#endif
#endif
}

#ifndef FAR
#ifndef RIVER
// How much of the waves a vertex of the sheets takes: none past the reach, and less over shallows, whose mesh leans its
// normal along +x by how shallow the water is there (DaggerfallSeaShore), so waves die toward the shore.
fn vertex_share(vertex: Vertex) -> f32 {
    let shallow = clamp(vertex.normal.x / max(vertex.normal.y, 1e-4), 0.0, 1.0);
    return moved_share(vertex.world_position) * (1.0 - shallow);
}

fn displace(vertex: Vertex) -> vec3<f32> {
    let share = vertex_share(vertex);
    if share <= 0.0 {
        return vertex.world_position;
    }
    let heights = amplitudes();
    let wind = wind_direction();
    let at = world_equivalent(vertex.world_position).xz;
    var offset = vec3<f32>(0.0);
    for (var i = 0; i < TRAINS; i++) {
        let direction = train_direction(wind, i);
        let theta = train_phase(direction, i, at);
        let a = heights[i] * share;
        let sideways = CHOPPINESS * a * cos(theta);
        offset += vec3<f32>(direction.x * sideways, a * sin(theta), direction.y * sideways);
    }
    return vertex.world_position + offset;
}

// The closed-form normal of the same sum, so the water is lit smoothly rather than per triangle.
fn displace_normal(vertex: Vertex) -> vec3<f32> {
    let share = vertex_share(vertex);
    var normal = vec3<f32>(0.0, 1.0, 0.0);
    if share <= 0.0 {
        return normal;
    }
    let heights = amplitudes();
    let wind = wind_direction();
    let at = world_equivalent(vertex.world_position).xz;
    var lengths = WAVELENGTHS;
    for (var i = 0; i < TRAINS; i++) {
        let direction = train_direction(wind, i);
        let theta = train_phase(direction, i, at);
        let ka = TAU / lengths[i] * heights[i] * share;
        normal -= vec3<f32>(direction.x * ka * cos(theta), CHOPPINESS * ka * sin(theta), direction.y * ka * cos(theta));
    }
    return normal;
}
#endif
#endif

fn hash(cell: vec2<i32>, level: i32) -> vec2<f32> {
    var v = vec2<u32>(bitcast<u32>(cell.x), bitcast<u32>(cell.y ^ (level * 7919)));
    v = v * 1664525u + 1013904223u;
    v.x += v.y * 1664525u;
    v.y += v.x * 1664525u;
    v = v ^ (v >> vec2<u32>(16u));
    v.x += v.y * 1664525u;
    v.y += v.x * 1664525u;
    v = v ^ (v >> vec2<u32>(16u));
    return vec2<f32>(v) / 4294967296.0;
}

// One cell of water's flash, a mean of 1: each cell of `size` flashes at its own rate and phase.
fn twinkle(at: vec2<f32>, size: f32, level: i32, t: f32) -> f32 {
    let h = hash(vec2<i32>(floor(at / size)), level);
    let wave = 0.5 + 0.5 * sin(t * (1.5 + 2.5 * h.y) + TAU * h.x);
    let cubed = wave * wave * wave;
    return cubed * cubed / TWINKLE_MEAN;
}

// Glitter in cells that double in size with distance, from `smallest` up, so each spans about two pixels along the view.
fn glitter(at: vec2<f32>, footprint: f32, smallest: f32, t: f32) -> f32 {
    let level = log2(max(footprint * 2.0 / smallest, 1.0));
    let low = floor(level);
    let size = smallest * exp2(low);
    return mix(twinkle(at, size, i32(low), t), twinkle(at, size * 2.0, i32(low) + 1, t), level - low);
}

// A disc's light seen in the water: its share of the lobe about the mirror direction, `width` the lobe's variance.
fn lobe(reflected: vec3<f32>, toward: vec3<f32>, width: f32) -> f32 {
    return exp((dot(reflected, toward) - 1.0) / width) * smoothstep(-0.02, 0.04, toward.y);
}

// The ripple map at `at` (map repeats): xy the tilt along the map's axes, z the tilt it averages away there (variance).
fn ripple(at: vec2<f32>) -> vec3<f32> {
    let texel = textureSample(product_map_a, product_sampler_a, at);
    let tilt = texel.xy * 2.0 - 1.0;
    return vec3<f32>(tilt, max(texel.a - dot(tilt, tilt), 0.0));
}

fn shade(given: Surface) -> vec4<f32> {
    var surface = given;
    let t = frame.time.x;
    // The far sea draws in the backdrop's units; the world's metres are what the waves are measured in.
    let metres = select(1.0, frame.backdrop.w, frame.backdrop.w > 0.0);
    let world = world_equivalent(surface.world_position);
    let to_eye = frame.camera.xyz - surface.world_position;
    let view = normalize(to_eye);
    // How far one pixel reaches over the water along the view, metres.
    let footprint = length(to_eye) * metres * frame.time.y / max(abs(view.y), 0.02);
    let wind = wind_direction();
    let across = vec2<f32>(-wind.y, wind.x);
    let speed = material.parameters[0].z;

    // The waves the sheets' vertices don't carry, as slopes, where a pixel can show them; the rest widen the glint.
    let heights = amplitudes();
    let moved = moved_share(surface.world_position);
    var lengths = WAVELENGTHS;
    var tilt = surface.normal.xz / max(surface.normal.y, 0.1);
    var unseen = 0.0;
    for (var i = 0; i < TRAINS; i++) {
        let direction = train_direction(wind, i);
        let ka = TAU / lengths[i] * heights[i];
        let seen = 1.0 - smoothstep(SEEN_FROM, SEEN_TO, footprint / lengths[i]);
        tilt -= direction * ka * cos(train_phase(direction, i, world.xz)) * seen * (1.0 - moved);
        unseen += 0.5 * ka * ka * (1.0 - seen);
    }

    // Ripples along the wind, drifting downwind with the surface, stronger in a wind and in gusts.
    let repeat = material.parameters[2].z;
    let drift = RIPPLE_DRIFT * t;
    let along = vec2<f32>(dot(world.xz, wind), dot(world.xz, across)) - vec2<f32>(drift, 0.0);
    let gust = textureSample(product_map_b, product_sampler_b, (world.xz - wind * drift) / PATCH_METRES).r;
    let strength = material.parameters[2].w * mix(RIPPLE_CALM, 1.0, smoothstep(0.0, RIPPLE_WIND, speed))
        * mix(PATCH_LEAST, PATCH_MOST, gust);
    let broad = ripple(along / repeat);
    let turn = vec2<f32>(cos(FINE_TURN), sin(FINE_TURN));
    let turned = vec2<f32>(along.x * turn.x - along.y * turn.y, along.x * turn.y + along.y * turn.x)
        - vec2<f32>(drift * (FINE_DRIFT - 1.0), 0.0);
    let fine = ripple(turned / (repeat * FINE_SCALE));
    let fine_tilt = vec2<f32>(fine.x * turn.x + fine.y * turn.y, fine.y * turn.x - fine.x * turn.y);
    let rippled = (broad.xy + fine_tilt * FINE_WEIGHT) * strength;
    tilt += wind * rippled.x + across * rippled.y;
    unseen += (broad.z + fine.z * FINE_WEIGHT * FINE_WEIGHT) * strength * strength;
    let normal = normalize(vec3<f32>(tilt.x, 1.0, tilt.y));
    surface.normal = normal;

    // Whitecaps on the faces leaning downwind, and a share of the sea where the waves are too small to see.
    let caps = smoothstep(WHITECAP_WIND_START, WHITECAP_WIND_FULL, speed);
    let pattern = textureSample(product_map_b, product_sampler_b, (world.xz - wind * drift) / FOAM_METRES).r;
    let froth = textureSample(product_map_b, product_sampler_b, (along.yx + vec2<f32>(0.0, drift)) / FROTH_METRES).r;
    let breaking = smoothstep(WHITECAP_FACE_START, WHITECAP_FACE_FULL, dot(normal.xz, wind))
        * smoothstep(WHITECAP_PATCH_START, WHITECAP_PATCH_FULL, pattern) * smoothstep(FROTH_FROM, FROTH_TO, froth);
    let faces_seen = 1.0 - smoothstep(SEEN_FROM, SEEN_TO, footprint / WHITECAP_WAVELENGTH);
    let foam = caps * mix(WHITECAP_FAR_SHARE, breaking, faces_seen);
    surface.base = vec4<f32>(mix(surface.base.rgb, FOAM_COLOR, foam), max(surface.base.a, foam));
    surface.emission *= 1.0 - foam;
    surface.roughness = mix(surface.roughness, FOAM_ROUGHNESS, foam);

    // The slopes the view can't see widen the glint: a reflection turns by twice its surface's tilt.
    let spread = material.parameters[1].z;
    let width = spread * spread + 4.0 * unseen;
    let reflected = reflect(-view, normal);
    let fresnel = 0.02 + 0.98 * pow(1.0 - max(dot(normal, view), 0.0), 5.0);

    var light = vec3<f32>(0.0);
    // Daggerfall's two moons, Masser and Secunda, in the sky's first two slots.
    for (var slot = 0u; slot < 2u; slot++) {
        let moon = frame.moons[slot];
        let radius = moon.toward.w;
        let lit = select(material.parameters[2].y, material.parameters[2].x, slot == 0u) * moon.color.w;
        if radius <= 0.0 || lit <= 0.0 {
            continue;
        }
        let disc = width + 0.5 * radius * radius;
        light += moon.color.rgb * lit * material.parameters[1].x * (0.5 * radius * radius / disc)
            * lobe(reflected, normalize(moon.toward.xyz), disc);
    }
    if frame.sun.w > 0.0 {
        // The sun is a light, not a disc of radiance: its light spreads over the lobe.
        let toward = normalize(frame.sun.xyz);
        let disc = width + 0.5 * SUN_RADIUS * SUN_RADIUS;
        let low = 1.0 - smoothstep(LOW_SUN_FROM, LOW_SUN_TO, toward.y);
        light += frame.sun_color.rgb * frame.sun_color.w * material.parameters[1].y * low * lobe(reflected, toward, disc) / (TAU * disc);
    }

    let ripples_seen = 1.0 - smoothstep(SEEN_FROM, SEEN_TO, footprint / repeat);
    let glints = glitter(world.xz, footprint, repeat * 0.5, t);
    let faded = 1.0 - ripples_seen;
    let added = light * fresnel * (1.0 - foam) * mix(1.0, glints, faded)
        + material.parameters[3].xyz * material.parameters[3].w * (glints - 1.0) * faded;
    let shaded = standard_shade(surface);
    // Over water that shows what lies under it, what it reflects covers that.
    let alpha = clamp(shaded.a + max(max(added.r, added.g), added.b), shaded.a, 1.0);
    return vec4<f32>(max(shaded.rgb * shaded.a + added, vec3<f32>(0.0)) / max(alpha, 1e-4), alpha);
}
