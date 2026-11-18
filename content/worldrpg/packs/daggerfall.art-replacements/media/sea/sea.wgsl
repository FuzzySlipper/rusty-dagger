// The sea and the rivers (#10906): their swells, the far sea's ripples, and the paths the moons and a low sun lay on the
// water. One shader draws both the Engine water sheets near the player, which the Engine ripples itself, and the far sea
// past them, a flat glowing colour on the ground's heightfields that the Engine does not ripple. Both take the same
// ripples, swells and glints from where they stand in the world, so nothing changes where the sheets end.
//
// A ripple or swell too small for a pixel to show fades to the flat surface, and its slopes widen the glint instead, as
// waves too far to see still spread the moon's path. Glitter stands in for the ripples that faded: cells of water that
// flash at their own rates, a pixel or two along the view wherever they lie, so the path glints out to the horizon and
// the far sea shimmers in its own colour without flickering pixel by pixel.
//
// A moon's or the sun's reflection is a Gaussian lobe about the mirror direction, as wide as the disc and the waves the
// view can't see: its peak is the disc's light shared over the lobe, so a wider path is a dimmer one.
//
// parameters[0]: x the ripples' wavelength, metres (the water's waveMetres); y their tilt (its ripple); z the swell's
//   wavelength, metres; w its tilt.
// parameters[1]: x the moons' glint, y the low sun's; z the glint's spread, radians; w 1 on the far sea, 0 on Engine water.
// parameters[2]: each moon slot's lit fraction, 0 new to 1 full: the moons' art draws their phase, so the sky's is whole.
// parameters[3]: xyz the sea's colour under the sky's light; w how far glitter shimmers it where the ripples have faded.
//
// Its payload entry, artReplacements.sea, is hashed by hand when it changes: no tool makes it.
#import rusty::types::{Surface, MOON_SLOTS}
#import rusty::material::material
#import rusty::view::{frame, world_equivalent}
#import rusty::shade::standard_shade

const TAU: f32 = 6.2831853;
const GRAVITY: f32 = 9.81;
// The Engine's procedural ripples' slope, root mean square, at a tilt of 1; and the swells'.
const RIPPLE_RMS: f32 = 0.1;
const SWELL_RMS: f32 = 0.5;
// A wave fades between a quarter of its length and three fifths of it to a pixel along the view.
const SEEN_FROM: f32 = 0.25;
const SEEN_TO: f32 = 0.6;
// The sun glints while it stands under this sine of its height, fading out above it.
const LOW_SUN_FROM: f32 = 0.15;
const LOW_SUN_TO: f32 = 0.45;
// The sun's disc, radians, where the sky draws none.
const SUN_RADIUS: f32 = 0.0047;
// The mean of a twinkle's ((1 + sin) / 2)^6, so glitter keeps the path's brightness.
const TWINKLE_MEAN: f32 = 0.2256;

// The Engine's own procedural ripples (its world.wgsl water_surface), read in the world at `at` wavelengths.
fn ripple_slope(at: vec2<f32>, t: f32) -> vec2<f32> {
    let phase = at * TAU;
    return vec2<f32>(
        sin(phase.x + t * 1.3) * 0.5 + sin(phase.x * 0.7 + phase.y * 0.4 + t * 0.9) * 0.3,
        cos(phase.y * 1.1 + t * 1.7) * 0.5 + cos(phase.x * 0.3 - phase.y * 0.8 + t * 0.7) * 0.3,
    ) * 0.25;
}

// Two long swells crossing, each running at deep water's speed for its length.
fn swell_slope(at: vec2<f32>, wavelength: f32, t: f32) -> vec2<f32> {
    let a = vec2<f32>(0.8, 0.6);
    let b = vec2<f32>(-0.35, 0.937);
    let second = wavelength * 0.61;
    let slope = a * cos(TAU * dot(at, a) / wavelength - sqrt(TAU * GRAVITY / wavelength) * t)
        + b * 0.6 * cos(TAU * dot(at, b) / second - sqrt(TAU * GRAVITY / second) * t + 1.3);
    return slope / 1.6;
}

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

fn shade(given: Surface) -> vec4<f32> {
    var surface = given;
    let t = frame.time.x;
    let far = material.parameters[1].w > 0.5;
    // The far sea draws in the backdrop's units; the world's metres are what the waves are measured in.
    let metres = select(1.0, frame.backdrop.w, frame.backdrop.w > 0.0);
    let world = world_equivalent(surface.world_position);
    let to_eye = frame.camera.xyz - surface.world_position;
    let view = normalize(to_eye);
    // How far one pixel reaches over the water along the view, metres.
    let footprint = length(to_eye) * metres * frame.time.y / max(abs(view.y), 0.02);

    let wavelength = material.parameters[0].x;
    let ripple_seen = 1.0 - smoothstep(SEEN_FROM, SEEN_TO, footprint / wavelength);
    var ripple = surface.normal.xz / max(surface.normal.y, 0.1);
    if far {
        ripple = ripple_slope(world.xz / wavelength, t) * material.parameters[0].y;
    }
    let swell_length = material.parameters[0].z;
    let swell_seen = 1.0 - smoothstep(SEEN_FROM, SEEN_TO, footprint / swell_length);
    let tilt = ripple * ripple_seen + swell_slope(world.xz, swell_length, t) * material.parameters[0].w * swell_seen;
    let normal = normalize(vec3<f32>(tilt.x, 1.0, tilt.y));
    surface.normal = normal;

    // The slopes the view can't see widen the glint: a reflection turns by twice its surface's tilt.
    let ripple_rms = RIPPLE_RMS * material.parameters[0].y;
    let swell_rms = SWELL_RMS * material.parameters[0].w;
    let spread = material.parameters[1].z;
    let width = spread * spread + 4.0 * (ripple_rms * ripple_rms * (1.0 - ripple_seen) + swell_rms * swell_rms * (1.0 - swell_seen));
    let reflected = reflect(-view, normal);
    let fresnel = 0.02 + 0.98 * pow(1.0 - max(dot(normal, view), 0.0), 5.0);

    var light = vec3<f32>(0.0);
    for (var slot = 0u; slot < MOON_SLOTS; slot++) {
        let moon = frame.moons[slot];
        let radius = moon.toward.w;
        let lit = material.parameters[2][slot] * moon.color.w;
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

    let glints = glitter(world.xz, footprint, wavelength * 0.5, t);
    let unseen = 1.0 - ripple_seen;
    let added = light * fresnel * mix(1.0, glints, unseen)
        + material.parameters[3].xyz * material.parameters[3].w * (glints - 1.0) * unseen;
    let shaded = standard_shade(surface);
    // Over water that shows what lies under it, what it reflects covers that.
    let alpha = clamp(shaded.a + max(max(added.r, added.g), added.b), shaded.a, 1.0);
    return vec4<f32>(max(shaded.rgb * shaded.a + added, vec3<f32>(0.0)) / max(alpha, 1e-4), alpha);
}
