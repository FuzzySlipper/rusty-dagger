// Pass 3 volumetric 3D pixel-shell overlay shader for interiors & dungeons (#11043).
// Reuses the multi-layer shell-stack idea from exterior grass (`shells.wgsl`) on indoor & dungeon
// architectural surfaces:
//   - mode 1: 3D fuzzy moss, hanging vines & golden-olive lichen sprouting out of mossy dungeon stone
//             with organic downward gravity droop on walls & ceilings.
//   - mode 2: plush woven pile on royal palace/manor/temple damask carpets & runners.
//   - mode 3: heavy velvet/wool nap with gold embroidery glints on heraldic tapestries & drapes.
//   - mode 0: fast discard for non-shell slots sharing a multi-material mesh.
//
// parameters[0]: (mode, layer_lift_metres, layer_fraction, cutoff)
// parameters[1]: (droop_metres, fade_start, fade_end, strand_scale)
// parameters[2]: (root_darken, tip_boost, velvet_sheen, chroma_bias)

#import rusty::types::{Surface, Vertex, Caster}
#import rusty::material::{material, albedo}
#import rusty::surface::transform_uv
#import rusty::view::frame
#import rusty::shade::standard_shade

const LUMA_W: vec3<f32> = vec3<f32>(0.2126, 0.7152, 0.0722);

fn hash21(p: vec2<f32>) -> f32 {
    var q = fract(vec3<f32>(p.x, p.y, p.x * 0.73 + p.y * 0.37) * 0.1031);
    q += dot(q, q.yzx + 33.33);
    return fract((q.x + q.y) * q.z);
}

fn smooth_clump(p: vec2<f32>) -> f32 {
    let cell = floor(p);
    let f = smoothstep(vec2<f32>(0.0), vec2<f32>(1.0), p - cell);
    let a = hash21(cell);
    let b = hash21(cell + vec2<f32>(1.0, 0.0));
    let c = hash21(cell + vec2<f32>(0.0, 1.0));
    let d = hash21(cell + vec2<f32>(1.0, 1.0));
    return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}

fn wrap_cell(cell: vec2<i32>, size: vec2<i32>) -> vec2<i32> {
    return ((cell % size) + size) % size;
}

// Perceptual sqrt-RGB moss & lichen detector calibrated against Daggerfall dungeon archives:
// emerald moss (122/123/124/168/322/424/089) + optional golden-olive lichen (023/068/368/468).
fn moss_coverage(linear_rgb: vec3<f32>, olive_bias: f32) -> f32 {
    let s = sqrt(max(linear_rgb, vec3<f32>(0.0)));
    let bright = smoothstep(0.14, 0.26, s.g);
    let green = clamp((s.g - max(s.r * 0.96, s.b * 1.12)) * 7.5, 0.0, 1.0);
    let olive_hue = smoothstep(0.70, 0.86, s.g / max(s.r, 1e-4));
    let olive_sat = smoothstep(1.60, 2.50, s.g / max(s.b, 0.04)) * clamp((s.g - s.b) * 4.5, 0.0, 1.0);
    let olive = olive_hue * olive_sat * olive_bias;
    return max(green, olive) * bright;
}

fn displace(vertex: Vertex) -> vec3<f32> {
    let mode = material.parameters[0].x;
    if mode < 0.5 {
        return vertex.world_position;
    }
    let lift = material.parameters[0].y;
    let h = material.parameters[0].z;
    let droop = material.parameters[1].x;
    let n = normalize(vertex.world_normal);
    // Vertical walls & overhanging ceilings droop gently downward under gravity; floors rise straight up.
    let wall_factor = smoothstep(0.65, 0.15, n.y);
    let sag = vec3<f32>(0.0, -droop * h * h * wall_factor, 0.0);
    return vertex.world_position + n * lift + sag;
}

fn shade(given: Surface) -> vec4<f32> {
    let p0 = material.parameters[0];
    let p1 = material.parameters[1];
    let p2 = material.parameters[2];
    let mode = p0.x;
    if mode < 0.5 {
        discard;
    }

    let far = length(frame.camera.xyz - given.world_position);
    let fade = 1.0 - smoothstep(p1.y, p1.z, far);
    if fade <= 0.02 {
        discard;
    }

    let uv = transform_uv(material.base_uv_u, material.base_uv_v, given.uv);
    let size = vec2<i32>(textureDimensions(albedo, 0));
    let texels = vec2<f32>(size);
    let cell = wrap_cell(vec2<i32>(floor(uv * texels)), size);
    let rgb = textureLoad(albedo, cell, 0).rgb;

    let strand_cell = floor(uv * texels * p1.w);
    let clump = smooth_clump(strand_cell / 3.2);
    let tuft = hash21(strand_cell);
    let h = p0.z;
    let cutoff = p0.w;

    var lit = given;
    let view = normalize(frame.camera.xyz - given.world_position);
    let fresnel = pow(1.0 - max(dot(normalize(given.normal), view), 0.0), 2.8);

    if mode < 1.5 {
        // Mode 1: Volumetric 3D dungeon moss & hanging vines.
        // Base layers gently bleed outward across stone mortar edges via 4-neighbor moss check so cushions
        // read plump and organic rather than razor-cut, then taper into crisp fuzzy retro pixel tufts at tips.
        var m = moss_coverage(rgb, p2.w);
        if m <= 0.40 && h < 0.55 {
            let n_m = max(
                max(moss_coverage(textureLoad(albedo, wrap_cell(cell + vec2<i32>(1, 0), size), 0).rgb, p2.w),
                    moss_coverage(textureLoad(albedo, wrap_cell(cell + vec2<i32>(-1, 0), size), 0).rgb, p2.w)),
                max(moss_coverage(textureLoad(albedo, wrap_cell(cell + vec2<i32>(0, 1), size), 0).rgb, p2.w),
                    moss_coverage(textureLoad(albedo, wrap_cell(cell + vec2<i32>(0, -1), size), 0).rgb, p2.w)));
            m = max(m, n_m * (0.55 * (1.0 - h)));
        }
        if m <= 0.08 {
            discard;
        }
        let strand_h = (0.38 + 0.48 * clump + 0.14 * tuft) * smoothstep(0.08, 0.50, m) * fade;
        if strand_h < cutoff {
            discard;
        }
        let shade_scale = mix(p2.x, p2.y, h);
        // Subtle lush pulp-fantasy emerald/olive chroma lift on fuzzy outer moss tips.
        let luma = dot(rgb, LUMA_W);
        let vivid = mix(vec3<f32>(luma), rgb, 1.0 + 0.38 * h);
        let rim = vivid * (p2.z * fresnel * (0.35 + 0.65 * h));
        // Gentle organic per-clump normal variation breaks up flat wall-parallel highlights beside torches.
        let bump = vec3<f32>((clump - 0.5) * 0.24, -0.14 * h, (tuft - 0.5) * 0.24);
        lit.normal = normalize(given.normal + bump);
        lit.base = vec4<f32>(max(given.tint.rgb * vivid * shade_scale + rim, vec3<f32>(0.0)), 1.0);
        lit.roughness = mix(0.94, 0.82, h);
        lit.metalness = 0.0;
        lit.occlusion *= mix(0.42, 1.0, h);
        return standard_shade(lit);
    }

    // Modes 2 & 3: Plush carpet pile & heavy woven tapestry nap.
    let luma = dot(rgb, LUMA_W);
    if luma < 0.004 {
        discard;
    }
    let strand_h = (0.36 + 0.24 * clump + 0.40 * tuft) * fade;
    if strand_h < cutoff {
        discard;
    }
    let shade_scale = mix(p2.x, p2.y, h);
    let sheen_col = mix(rgb, vec3<f32>(1.0, 0.92, 0.78), 0.22);
    let velvet = sheen_col * (p2.z * fresnel * (0.4 + 0.6 * h));

    // Metallic gold embroidery / border thread glints on royal tapestries & carpets.
    let gold = smoothstep(0.10, 0.24, rgb.r)
        * smoothstep(0.06, 0.16, rgb.g)
        * smoothstep(1.8, 3.0, rgb.g / max(rgb.b, 0.015))
        * smoothstep(2.4, 1.3, rgb.r / max(rgb.g, 0.015));

    lit.base = vec4<f32>(max(given.tint.rgb * rgb * shade_scale + velvet, vec3<f32>(0.0)), 1.0);
    lit.roughness = mix(0.88, 0.28, gold * 0.75);
    lit.metalness = gold * 0.40;
    lit.occlusion *= mix(0.58, 1.0, h);
    return standard_shade(lit);
}

fn cast_shadow(caster: Caster) {
    // Opaque base architecture already casts the exact shadow; skip shell shadow-pass work completely.
    discard;
}
