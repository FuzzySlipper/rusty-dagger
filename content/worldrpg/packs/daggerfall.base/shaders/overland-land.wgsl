// The 3D travel map's land (#11111): dual-scale anti-tiled landscape detail over per-vertex climate and curvature colours,
// slope-aware rocky crags and geological stratification on steep mountain faces, and soft drifting cloud shadows scaled by
// active weather. Reuses travel-map/detail.png (product_map_a) and travel-map/detail-normal.png (product_map_b) with zero
// new texture assets and minimal ALU cost so 3 km close-ups stay crisp and 42 km views stay organic and repetition-free.
//
// parameters[0]: x detail repeat (km), y detail normal relief scale, z ocean height (backdrop km), w cloud cover (0..1).
// parameters[1]: xy wind direction (unit XZ), z cloud drift speed (km/s), w daylight share (0 night .. 1 day).
// parameters[2]: xyz rock palette colour (linear RGB), w winter share (0 summer .. 1 winter).
// product_map_a: travel-map detail mottle (linear, repeating).
// product_map_b: travel-map detail normal map (linear, repeating).
#import rusty::types::Surface
#import rusty::material::{material, product_map_a, product_sampler_a, product_map_b, product_sampler_b}
#import rusty::view::frame
#import rusty::shade::standard_shade

const TAU: f32 = 6.2831853;
// Non-harmonic rotation (37 deg) and scale (2.73x) for the macro detail layer so no grid repeats at wide map zooms.
const ROT_C: f32 = 0.7986355;
const ROT_S: f32 = 0.6018150;
const MACRO_SCALE: f32 = 2.73;

fn hash21(p: vec2<f32>) -> f32 {
    let q = fract(p * vec2<f32>(123.34, 456.21));
    let d = dot(q, q + 45.32);
    return fract((q.x + d) * (q.y + d));
}

fn vnoise(p: vec2<f32>) -> f32 {
    let i = floor(p);
    let f = fract(p);
    let u = f * f * (3.0 - 2.0 * f);
    let a = hash21(i);
    let b = hash21(i + vec2<f32>(1.0, 0.0));
    let c = hash21(i + vec2<f32>(0.0, 1.0));
    let d = hash21(i + vec2<f32>(1.0, 1.0));
    return mix(mix(a, b, u.x), mix(c, d, u.x), u.y);
}

fn unpack_tilt(uv: vec2<f32>) -> vec2<f32> {
    let n = textureSample(product_map_b, product_sampler_b, uv).xyz * 2.0 - 1.0;
    // travel_map.py packs (-east, -north, z); convert to world (+x east, +z south) slope tilt.
    return vec2<f32>(n.x, -n.y) / max(n.z, 0.25);
}

fn shade(given: Surface) -> vec4<f32> {
    var surface = given;
    let t = frame.time.x;
    let km = surface.world_position.xz;
    let to_eye = frame.camera.xyz - surface.world_position;
    let dist_km = length(to_eye);
    let view = to_eye / max(dist_km, 1e-5);
    let footprint_km = dist_km * frame.time.y / max(abs(view.y), 0.10);

    let detail_km = max(material.parameters[0].x, 0.1);
    let relief_scale = material.parameters[0].y;
    let cloud_cover = clamp(material.parameters[0].w, 0.0, 1.0);
    let wind = material.parameters[1].xy;
    let cloud_speed = material.parameters[1].z;
    let daylight = clamp(material.parameters[1].w, 0.0, 1.0);
    let rock_col = material.parameters[2].xyz;
    let winter = clamp(material.parameters[2].w, 0.0, 1.0);

    // 1. Dual-scale anti-tiled detail mottle and normal relief: crisp fine layer close in, rotated macro layer farther out.
    let uv_a = km / detail_km;
    let rot_km = vec2<f32>(km.x * ROT_C - km.y * ROT_S, km.x * ROT_S + km.y * ROT_C);
    let uv_b = rot_km / (detail_km * MACRO_SCALE) + vec2<f32>(0.37, 0.61);

    let mottle_a = textureSample(product_map_a, product_sampler_a, uv_a).r;
    let mottle_b = textureSample(product_map_a, product_sampler_a, uv_b).r;
    let macro_mix = smoothstep(0.012, 0.085, footprint_km);
    let mottle = mix(mix(mottle_a, mottle_b, 0.28), mix(mottle_a, mottle_b, 0.72), macro_mix);

    let tilt_a = unpack_tilt(uv_a);
    let tilt_b_rot = unpack_tilt(uv_b);
    let tilt_b = vec2<f32>(tilt_b_rot.x * ROT_C + tilt_b_rot.y * ROT_S, tilt_b_rot.y * ROT_C - tilt_b_rot.x * ROT_S);
    let detail_fade = 1.0 - smoothstep(0.06, 0.28, footprint_km);
    let detail_tilt = mix(tilt_a * 0.72 + tilt_b * 0.35, tilt_b * 0.60, macro_mix) * relief_scale * (0.25 + 0.75 * detail_fade);

    // 2. Slope-aware rocky crags: folded geological strata crossed by vertical rock fissures on steep highland/mountain faces.
    let ocean_y = material.parameters[0].z;
    let n0 = normalize(given.normal);
    let steep = smoothstep(0.14, 0.42, 1.0 - n0.y);
    let highland = smoothstep(ocean_y + 0.14, ocean_y + 0.42, surface.world_position.y);
    let horiz = n0.xz;
    let horiz_len = length(horiz);
    let slope_dir = select(vec2<f32>(1.0, 0.0), horiz / horiz_len, horiz_len > 1e-4);
    let slope_perp = vec2<f32>(-slope_dir.y, slope_dir.x);

    let dip = dot(km, vec2<f32>(0.32, -0.24)) * 2.2;
    let strata_coord = surface.world_position.y * 10.5 + dip + (mottle_b - 0.5) * 5.2 + (mottle_a - 0.5) * 3.0;
    let strata_broad = sin(strata_coord * TAU);
    let strata_fine = sin(strata_coord * TAU * 2.3 + mottle_a * 5.5);
    let fissure = sin(dot(km, slope_perp) * 34.0 + mottle_b * 6.0 + mottle_a * 3.5);
    let outcrop_mask = smoothstep(0.28, 0.78, mottle_b);
    let crag_band = (0.48 * strata_broad + 0.24 * strata_fine) * (0.45 + 0.55 * outcrop_mask) + 0.28 * fissure;
    // Detect snow-covered slopes so winter crags keep crisp frosty rock contrast without turning muddy.
    let lum = dot(given.base.rgb, vec3<f32>(0.299, 0.587, 0.114));
    let snow_like = smoothstep(0.55, 0.80, lum) * (0.4 + 0.6 * winter);
    let crag_weight = steep * (0.25 + 0.75 * highland);
    let crag_shade = 1.0 + crag_weight * crag_band * mix(0.16, 0.09, snow_like);
    let rock_tint = mix(given.base.rgb, mix(given.base.rgb, rock_col, 0.36), steep * highland * (1.0 - snow_like * 0.65));

    // Perturb geometric normal with detail relief + fractured rock ledges and vertical gullies on steep slopes.
    let ledge_grad = (cos(strata_coord * TAU) * 0.24 + cos(strata_coord * TAU * 2.3 + mottle_a * 5.5) * 0.12) * (0.45 + 0.55 * outcrop_mask);
    let fissure_grad = cos(dot(km, slope_perp) * 34.0 + mottle_b * 6.0 + mottle_a * 3.5) * 0.18;
    let crag_tilt = (slope_dir * ledge_grad + slope_perp * fissure_grad) * crag_weight * (0.35 + 0.65 * detail_fade);
    let total_tilt = detail_tilt + crag_tilt;
    surface.normal = normalize(n0 + vec3<f32>(total_tilt.x, 0.0, total_tilt.y));

    // 3. Soft procedural drifting cloud shadows scaled by weather and daylight (cool sky-tinted shade, never flat grey).
    let cloud_p = km * 0.14 - wind * (cloud_speed * t);
    let n1 = vnoise(cloud_p);
    let n2 = vnoise(cloud_p * 2.15 + vec2<f32>(n1 * 0.65, -n1 * 0.55) + vec2<f32>(11.3, 4.7));
    let cloud_field = n1 * 0.62 + n2 * 0.38;
    let thresh = mix(0.66, 0.32, cloud_cover);
    let cloud_mask = smoothstep(thresh - 0.14, thresh + 0.16, cloud_field);
    let cloud_depth = mix(0.16, 0.30, cloud_cover) * mix(0.22, 1.0, daylight);
    let cloud_tint = mix(vec3<f32>(1.0), vec3<f32>(0.76, 0.81, 0.90), cloud_mask * cloud_depth * 1.55);

    surface.base = vec4<f32>(rock_tint * mottle * crag_shade * cloud_tint, given.base.a);
    // Keep meadows/forests matte (0.95) while weathered rock ledges catch subtle grazing light (0.82).
    surface.roughness = mix(0.95, 0.82, steep * (0.5 + 0.5 * strata_broad));
    return standard_shade(surface);
}
