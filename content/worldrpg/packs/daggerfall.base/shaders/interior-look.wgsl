// Pulp / retro dark fantasy interior & dungeon full-screen finish pass (#11042).
// Runs over the view's finished picture (`OFFSCREEN_FORMAT = Rgba8UnormSrgb`), so
// `pixel.color.rgb` arrives hardware-decoded to linear 0..1 and the returned linear colour
// is hardware-encoded to sRGB. Tonal curves, warm-protected luminance-preserving split-toning,
// gentle framing vignette and optional print tooth run in perceptual space so ink-black SSAO
// crevices stay crisp, unlit stonework takes rich chromatic hue, and firelit pools stay warm.
//
// effect_parameter(0u): (shadow_r, shadow_g, shadow_b, shadow_strength)
// effect_parameter(1u): (hi_r, hi_g, hi_b, hi_strength)
// effect_parameter(2u): (vignette_strength, vignette_power, grain_strength, grain_pixel_size)
// effect_parameter(3u): (shadow_lift, contrast_punch, depth_tint_strength, unused)

#import rusty::image::{ImagePixel, effect_parameter, picture_size}

const LUMA_WEIGHTS: vec3<f32> = vec3<f32>(0.2126, 0.7152, 0.0722);

fn hash12(p: vec2<f32>) -> f32 {
    var p3 = fract(vec3<f32>(p.x, p.y, p.x) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return fract((p3.x + p3.y) * p3.z);
}

fn image_effect(pixel: ImagePixel) -> vec4<f32> {
    let sh_param = effect_parameter(0u);
    let hi_param = effect_parameter(1u);
    let vg_param = effect_parameter(2u);
    let curve_param = effect_parameter(3u);

    let linear_in = max(pixel.color.rgb, vec3<f32>(0.0));
    var col = pow(linear_in, vec3<f32>(1.0 / 2.2));

    // Gentle shadow-midtone legibility lift that keeps deep SSAO crevices ink-black.
    if curve_param.x != 0.0 {
        let base_luma = dot(col, LUMA_WEIGHTS);
        let lift = smoothstep(0.014, 0.09, base_luma) * (1.0 - col) * (1.0 - col);
        col = clamp(col + vec3<f32>(curve_param.x) * lift, vec3<f32>(0.0), vec3<f32>(1.0));
    }

    // Subtle graphic S-curve contrast punch (kept modest so ACES Filmic does the heavy lifting).
    if curve_param.y > 0.0 {
        let s = col * col * (3.0 - 2.0 * col);
        col = mix(col, s, curve_param.y);
    }

    let luma = dot(col, LUMA_WEIGHTS);
    // Warm firelight protection: surfaces already warmed by torches/braziers/hearths (R > B)
    // keep their molten amber-vermilion glow instead of getting pulled back into cool shadow tint.
    let warmth = smoothstep(0.04, 0.24, col.r - col.b);

    // Luminance-preserving shadow split-toning for deep & mid-dark unlit stonework.
    if sh_param.w > 0.0 {
        let sh_mask = smoothstep(0.016, 0.075, luma) * (1.0 - smoothstep(0.14, 0.36, luma)) * (1.0 - warmth * 0.85);
        let sh_luma = max(dot(sh_param.rgb, LUMA_WEIGHTS), 0.16);
        let sh_hue = sh_param.rgb / sh_luma;
        let sh_target = clamp(sh_hue * luma, vec3<f32>(0.0), vec3<f32>(1.0));
        col = mix(col, sh_target, clamp(sh_mask * sh_param.w, 0.0, 0.65));
    }

    // Highlight split-toning: warms firelit edges, stone rims and lantern pools.
    if hi_param.w > 0.0 {
        let hi_mask = smoothstep(0.30, 0.86, luma);
        let hi_luma = max(dot(hi_param.rgb, LUMA_WEIGHTS), 0.25);
        let hi_hue = hi_param.rgb / hi_luma;
        let hi_target = clamp(col * mix(vec3<f32>(1.0), hi_hue, 0.55) + hi_param.rgb * (0.045 * hi_mask), vec3<f32>(0.0), vec3<f32>(1.0));
        col = mix(col, hi_target, clamp(hi_mask * hi_param.w, 0.0, 0.75));
    }

    // Subtle far-depth chromatic tint into the shadow hue (only over world geometry, warm-protected).
    if curve_param.z > 0.0 && pixel.depth < 0.9999 {
        let far_w = smoothstep(0.95, 0.997, pixel.depth) * (1.0 - warmth * 0.70);
        let far_luma = max(dot(sh_param.rgb, LUMA_WEIGHTS), 0.16);
        let far_hue = sh_param.rgb / far_luma;
        let far_target = clamp(far_hue * max(luma, 0.05), vec3<f32>(0.0), vec3<f32>(1.0));
        col = mix(col, far_target, clamp(far_w * curve_param.z, 0.0, 0.42));
    }

    // Gentle framing vignette tinted into the shadow hue rather than crushing room edges.
    if vg_param.x > 0.0 {
        let centered = (pixel.uv - vec2<f32>(0.5)) * vec2<f32>(1.06, 1.0);
        let dist = clamp(length(centered) * 1.41421356, 0.0, 1.0);
        let vig = pow(dist, max(vg_param.y, 1.0)) * vg_param.x;
        let sh_luma = max(dot(sh_param.rgb, LUMA_WEIGHTS), 0.16);
        let edge_tint = mix(vec3<f32>(0.45), clamp((sh_param.rgb / sh_luma) * 0.48, vec3<f32>(0.0), vec3<f32>(1.0)), step(0.001, sh_param.w));
        col = mix(col, col * edge_tint, clamp(vig, 0.0, 0.55));
    }

    // Optional tactile pulp book-cover print tooth / luminance-weighted static dither.
    if vg_param.z > 0.0 {
        let cell = floor(pixel.uv * picture_size() / max(vg_param.w, 1.0));
        let tri = hash12(cell) - hash12(cell + vec2<f32>(17.3, 43.7));
        let g_luma = dot(col, LUMA_WEIGHTS);
        let g_mask = smoothstep(0.020, 0.09, g_luma) * (1.0 - smoothstep(0.65, 0.96, g_luma));
        col = col + vec3<f32>(tri * vg_param.z * g_mask);
    }

    col = clamp(col, vec3<f32>(0.0), vec3<f32>(1.0));
    return vec4<f32>(pow(col, vec3<f32>(2.2)), pixel.color.a);
}
