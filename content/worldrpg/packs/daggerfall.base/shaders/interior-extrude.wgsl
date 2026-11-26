// Outward 3D architectural silhouette-POM extrusion shader for interiors & dungeons (#11043, #11067):
//   - mode 0: fast discard for non-extruded slots sharing a multi-material static mesh.
//   - mode 1: single outer envelope displaced +H outward along world_normal, running a 2D-DDA voxel
//     raymarch inward toward the base wall and discarding empty mortar/recessed plaster so bricks,
//     carved medallions, framed art, and timber beams physically break silhouettes past corners and
//     doorways with solid continuous 90-degree voxel sidewalls.
//
// parameters[0]: (mode, lift_metres, steps, cutoff)
// parameters[1]: (fade_start, fade_end, base_roughness, rim_roughness)
// parameters[2]: (rim_metalness, cavity_darkness, height_gamma, mortar_threshold)
// parameters[3]: (has_authored_map, special_flags, water_level_y, has_water)

#import rusty::types::{Surface, Vertex}
#import rusty::material::{material, albedo, albedo_sampler, product_map_a}
#import rusty::surface::transform_uv
#import rusty::view::frame
#import rusty::shade::standard_shade

const LUMA_W: vec3<f32> = vec3<f32>(0.2126, 0.7152, 0.0722);

fn wrap_cell(cell: vec2<i32>, size: vec2<i32>) -> vec2<i32> {
    return ((cell % size) + size) % size;
}

fn texel_luma(cell: vec2<i32>, size: vec2<i32>) -> f32 {
    let rgb = textureLoad(albedo, wrap_cell(cell, size), 0).rgb;
    return sqrt(max(dot(rgb, LUMA_W), 0.0));
}

fn texel_height(cell: vec2<i32>, size: vec2<i32>, has_map: bool, gamma: f32, mortar_t: f32) -> f32 {
    if has_map {
        return textureLoad(product_map_a, wrap_cell(cell, size), 0).a;
    }
    let c = texel_luma(cell, size);
    let n = texel_luma(cell + vec2<i32>(0, -1), size);
    let s = texel_luma(cell + vec2<i32>(0, 1), size);
    let e = texel_luma(cell + vec2<i32>(1, 0), size);
    let w = texel_luma(cell + vec2<i32>(-1, 0), size);

    let nbr_lo = min(min(n, s), min(e, w));
    let nbr_avg = 0.25 * (n + s + e + w);
    let c_clean = select(c, mix(c, nbr_lo, 0.72), nbr_lo > c * 1.45);

    let abs_open = smoothstep(0.015, 0.085, c_clean);
    let rel = c_clean / max(nbr_avg, 0.10);
    let groove = smoothstep(mortar_t, mortar_t + 0.34, rel);
    let tier = 0.56 + 0.44 * pow(clamp(c_clean * 1.15, 0.0, 1.0), gamma);
    return clamp(abs_open * groove * tier, 0.0, 1.0);
}

fn block_height(cell: vec2<i32>, size: vec2<i32>, has_map: bool, gamma: f32, mortar_t: f32, cutoff: f32) -> f32 {
    let h = texel_height(cell, size, has_map, gamma, mortar_t);
    return smoothstep(cutoff, 0.92, h);
}

fn surface_extrude_scale(world_normal: vec3<f32>) -> f32 {
    // Full outward projection on vertical walls; shallower on ceilings and floors so feet/items never bury.
    return mix(1.0, select(0.58, 0.42, world_normal.y > 0.0), smoothstep(0.45, 0.75, abs(world_normal.y)));
}

fn displace(vertex: Vertex) -> vec3<f32> {
    if material.parameters[0].x < 0.5 {
        return vertex.world_position;
    }
    let n = normalize(vertex.world_normal);
    let lift = material.parameters[0].y * surface_extrude_scale(n);
    return vertex.world_position + n * lift;
}

fn shade(given: Surface) -> vec4<f32> {
    let p0 = material.parameters[0];
    let p1 = material.parameters[1];
    let p2 = material.parameters[2];
    let p3 = material.parameters[3];
    let mode = p0.x;
    let has_map = p3.x > 0.5;
    let gamma = max(p2.z, 0.25);
    let mortar_t = p2.w;
    let cutoff = p0.w;

    // Screen-space derivatives in uniform control flow BEFORE any discard.
    let uv = transform_uv(material.base_uv_u, material.base_uv_v, given.uv);
    let dp_dx = dpdx(given.world_position);
    let dp_dy = dpdy(given.world_position);
    let dt_dx = dpdx(uv);
    let dt_dy = dpdy(uv);
    let det = dt_dx.x * dt_dy.y - dt_dy.x * dt_dx.y;

    let far = length(frame.camera.xyz - given.world_position);
    let fade = 1.0 - smoothstep(p1.x, p1.y, far);

    let good_det = abs(det) > 1e-14;
    let safe_det = select(1.0, det, good_det);
    let along_u = (dp_dx * dt_dy.y - dp_dy * dt_dx.y) / safe_det;
    let along_v = (dp_dy * dt_dx.x - dp_dx * dt_dy.x) / safe_det;
    let n = normalize(given.normal);
    let size = select(vec2<i32>(textureDimensions(albedo, 0)), vec2<i32>(textureDimensions(product_map_a, 0)), has_map);
    let texels = vec2<f32>(size);
    let uu = dot(along_u, along_u);
    let cross_uv = dot(along_u, along_v);
    let vv = dot(along_v, along_v);
    let gram = max(uu * vv - cross_uv * cross_uv, 1e-12);
    let metres = max(vec2<f32>(sqrt(uu), sqrt(vv)) / texels, vec2<f32>(1e-4));
    let tu_raw = along_u - n * dot(n, along_u);
    let tv_raw = along_v - n * dot(n, along_v);
    let tu = select(vec3<f32>(1.0, 0.0, 0.0), normalize(tu_raw), dot(tu_raw, tu_raw) > 1e-12);
    let tv = select(vec3<f32>(0.0, 0.0, 1.0), normalize(tv_raw), dot(tv_raw, tv_raw) > 1e-12);

    var keep = mode >= 0.5 && fade > 0.02;
    var cell = vec2<i32>(floor(uv * texels));
    var normal = n;
    var hit_pos = given.world_position;
    var side = 0;
    var ao_factor = 1.0;
    var rim_mask = 0.0;

    if keep {
        // Outward Silhouette-Envelope 2D-DDA Voxel Raymarch.
        let surf_scale = surface_extrude_scale(n);
        let depth = p0.y * surf_scale;
        let into = normalize(given.world_position - frame.camera.xyz);
        let down = max(-dot(into, n), 0.16);
        let a = dot(into, along_u);
        let b = dot(into, along_v);
        let across = vec2<f32>(vv * a - cross_uv * b, uu * b - cross_uv * a) / gram;
        let k = across / down * texels;

        let start = uv * texels;
        cell = vec2<i32>(floor(start));
        let stride = vec2<i32>(select(vec2<f32>(-1.0), vec2<f32>(1.0), k >= vec2<f32>(0.0)));
        let per = 1.0 / max(abs(k), vec2<f32>(1e-6));
        let first = select(start - floor(start), floor(start) + 1.0 - start, k >= vec2<f32>(0.0));
        var next = first * per;
        var entered = 0.0;
        var found = -1.0;
        let steps = clamp(i32(p0.z), 1, 48);

        for (var i = 0; i < steps; i++) {
            let bh = block_height(cell, size, has_map, gamma, mortar_t, cutoff) * fade;
            let leaves = min(next.x, next.y);
            let step_leaves = min(leaves, depth);
            if bh > 0.04 {
                let floor_at = (1.0 - bh) * depth;
                if floor_at <= entered {
                    found = entered;
                    break;
                }
                if floor_at <= step_leaves {
                    found = floor_at;
                    side = 0;
                    break;
                }
            }
            if leaves >= depth {
                break;
            }
            if next.x < next.y {
                cell.x += stride.x;
                entered = next.x;
                next.x += per.x;
                side = 1;
            } else {
                cell.y += stride.y;
                entered = next.y;
                next.y += per.y;
                side = 2;
            }
        }

        if found < 0.0 {
            keep = false;
        } else {
            hit_pos = given.world_position + into * (found / down);
            let hx_p = block_height(cell + vec2<i32>(1, 0), size, has_map, gamma, mortar_t, cutoff);
            let hx_m = block_height(cell - vec2<i32>(1, 0), size, has_map, gamma, mortar_t, cutoff);
            let hy_p = block_height(cell + vec2<i32>(0, 1), size, has_map, gamma, mortar_t, cutoff);
            let hy_m = block_height(cell - vec2<i32>(0, 1), size, has_map, gamma, mortar_t, cutoff);
            let slope = select(vec2<f32>(0.0), vec2<f32>(hx_p - hx_m, hy_p - hy_m) * depth / (2.0 * metres), good_det);

            if side == 1 {
                normal = -tu * f32(stride.x);
            } else if side == 2 {
                normal = -tv * f32(stride.y);
            } else if good_det {
                normal = normalize(n - (tu * slope.x + tv * slope.y) * 0.85);
            }

            let hollow = clamp(found / max(depth, 1e-4), 0.0, 1.0);
            ao_factor = mix(1.0, 1.0 - 0.50 * p2.y, hollow);
            let edge_mag = clamp(length(slope) * 0.60, 0.0, 1.0);
            rim_mask = max(select(0.0, 0.45, side != 0), edge_mag) * (1.0 - hollow * 0.70) * fade;
        }
    }

    // Geometric specular AA derivatives computed in uniform control flow before discard.
    let dn_dx = dpdx(normal);
    let dn_dy = dpdy(normal);
    let norm_var = clamp(dot(dn_dx, dn_dx) + dot(dn_dy, dn_dy), 0.0, 1.0);
    let kernel_rough = min(0.52 * norm_var, 0.34);
    let footprint = max(length(dt_dx * texels), length(dt_dy * texels));
    let spec_aa = smoothstep(0.55, 2.20, footprint) * 0.34;
    let aa_var = kernel_rough + spec_aa * spec_aa;

    let at = (vec2<f32>(cell) + 0.5) / texels;
    let color = textureSampleGrad(albedo, albedo_sampler, at, dt_dx, dt_dy);

    if !keep {
        discard;
    }

    let side_floor = select(0.0, 0.44, side != 0);
    let rough_raw = mix(p1.z, p1.w, rim_mask);
    var final_rough = max(sqrt(clamp(rough_raw * rough_raw + aa_var, 0.0, 1.0)), side_floor);
    let side_metal_scale = select(1.0, 0.30, side != 0);
    var final_metal = p2.x * rim_mask * side_metal_scale * (1.0 - smoothstep(0.60, 1.8, footprint) * 0.65) * (1.0 - norm_var * 0.60);
    var final_base = given.tint.rgb * color.rgb;

    // Preserve Pass 3 material-aware pulp PBR on extruded stones, gold inlays, forged iron & waterline wetness.
    let special = i32(round(p3.y));
    let water_y = p3.z;
    let has_water = p3.w > 0.5;
    let s_col = sqrt(max(color.rgb, vec3<f32>(0.0)));
    let top_face = select(1.0, 0.25, side != 0);

    if special == 1 || special == 2 {
        let gold_m = smoothstep(0.12, 0.24, color.r)
            * smoothstep(0.07, 0.16, color.g)
            * smoothstep(1.9, 3.0, color.g / max(color.b, 0.015))
            * smoothstep(2.3, 1.35, color.r / max(color.g, 0.015))
            * top_face;
        final_metal = max(final_metal, gold_m * 0.46 * (1.0 - smoothstep(0.60, 1.8, footprint) * 0.55));
        let gold_rough = max(sqrt(clamp(0.24 * 0.24 + aa_var, 0.0, 1.0)), side_floor);
        final_rough = mix(final_rough, gold_rough, gold_m * 0.85);
    }
    if special == 2 {
        let max_c = max(max(s_col.r, s_col.g), s_col.b);
        let min_c = min(min(s_col.r, s_col.g), s_col.b);
        let iron_m = smoothstep(0.10, 0.03, max_c - min_c) * smoothstep(0.12, 0.24, max_c) * smoothstep(0.66, 0.46, max_c) * top_face;
        final_metal = max(final_metal, iron_m * 0.44 * (1.0 - smoothstep(0.60, 1.8, footprint) * 0.55));
        let iron_rough = max(sqrt(clamp(0.28 * 0.28 + aa_var, 0.0, 1.0)), side_floor);
        final_rough = mix(final_rough, iron_rough, iron_m * 0.78);
    }
    if special == 3 {
        let blood_m = smoothstep(0.04, 0.12, color.r)
            * smoothstep(1.9, 3.2, color.r / max(color.g, 0.01))
            * smoothstep(2.2, 3.6, color.r / max(color.b, 0.01))
            * top_face;
        let blood_rough = max(sqrt(clamp(0.18 * 0.18 + aa_var, 0.0, 1.0)), side_floor);
        final_rough = mix(final_rough, blood_rough, blood_m * 0.92);
        final_base = mix(final_base, final_base * vec3<f32>(1.08, 0.82, 0.82), blood_m * 0.45);
    }
    if special == 5 {
        let green_m = clamp((s_col.g - max(s_col.r * 0.96, s_col.b * 1.12)) * 7.5, 0.0, 1.0);
        let olive_m = smoothstep(0.70, 0.86, s_col.g / max(s_col.r, 1e-4))
            * smoothstep(1.60, 2.50, s_col.g / max(s_col.b, 0.04));
        let moss_m = max(green_m, olive_m) * smoothstep(0.14, 0.26, s_col.g);
        final_rough = mix(final_rough, 0.88, moss_m * 0.85);
        final_metal *= 1.0 - moss_m * 0.90;
    }
    if has_water {
        let wet = smoothstep(water_y + 0.85, water_y - 0.15, hit_pos.y);
        if wet > 0.005 {
            final_base *= mix(1.0, 0.64, wet);
            let wet_rough = max(0.22, sqrt(0.22 * 0.22 + aa_var));
            final_rough = mix(final_rough, min(final_rough, wet_rough), wet * 0.88);
        }
    }

    // Subtle tonal crispness on 90-degree extruded voxel side faces so projecting blocks pop cleanly.
    let side_tone = select(1.0, 0.86, side != 0);
    var lit = given;
    lit.world_position = hit_pos;
    lit.normal = normal;
    lit.roughness = clamp(final_rough, 0.08, 1.0);
    lit.metalness = clamp(final_metal, 0.0, 0.55);
    lit.base = vec4<f32>(final_base * ao_factor * side_tone, 1.0);
    lit.occlusion *= ao_factor;
    return standard_shade(lit);
}
