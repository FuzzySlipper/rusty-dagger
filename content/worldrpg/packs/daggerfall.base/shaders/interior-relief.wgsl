// Interior & dungeon voxel-style relief + stylized specular rim-catch + local point-light
// micro-self-shadow shader (#11043). Inspired by 2D-DDA texel-column voxel displacement
// (matching exterior `relief.wgsl`) with stylized pulp-fantasy PBR specular highlights on
// raised stone/brick/wood bevels, ink-black mortar hollows, and a fast 6-step heightfield
// micro-shadow march toward the dominant local torch/brazier/lamp.
//
// Works on 100% of interior & dungeon surfaces: uses an authored height map in `product_map_a.a`
// when bound (`parameters[3].x > 0.5`), and otherwise derives a speckle-guarded, wrap-tiling
// mortar-and-tier height map directly from `albedo` texel neighborhoods.
//
// parameters[0]: (depth_metres, max_steps, fade_start, fade_end)
// parameters[1]: (cavity_darkness, top_slope_strength, base_roughness, rim_roughness)
// parameters[2]: (rim_metalness, self_shadow_strength, height_gamma, mortar_threshold)
// parameters[3]: (has_authored_map, special_flags, water_level_y, has_water)
//   special_flags: 0=stone/default, 1=gold/brass inlay, 2=forged iron+brass, 3=wet blood,
//                  4=velvet carpet/tapestry + gold embroidery, 5=mossy stone (matte moss + damp stone)

#import rusty::types::Surface
#import rusty::material::{material, albedo, albedo_sampler, product_map_a}
#import rusty::surface::transform_uv
#import rusty::view::{frame, lights}
#import rusty::shade::standard_shade

const LUMA_W: vec3<f32> = vec3<f32>(0.2126, 0.7152, 0.0722);

fn wrap_cell(cell: vec2<i32>, size: vec2<i32>) -> vec2<i32> {
    return ((cell % size) + size) % size;
}

fn texel_luma(cell: vec2<i32>, size: vec2<i32>) -> f32 {
    let rgb = textureLoad(albedo, wrap_cell(cell, size), 0).rgb;
    // Perceptual lightness so dark wood planks and mid-grey stone share consistent mortar contrast.
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

    // Speckle guard: an isolated 1-texel dark dot surrounded on all 4 sides by brighter stone
    // stays up on the brick face, whereas a continuous mortar seam or plank joint has at least
    // one dark neighbor and recesses cleanly.
    let nbr_lo = min(min(n, s), min(e, w));
    let nbr_avg = 0.25 * (n + s + e + w);
    let c_clean = select(c, mix(c, nbr_lo, 0.72), nbr_lo > c * 1.45);

    // Pitch-black mortar/slits always drop to the floor; continuous darker seams recess
    // relative to their local neighborhood so both dark timber and bright masonry carve cleanly,
    // while coherent dark moss blotches and dark wainscot planks stay solid.
    let abs_open = smoothstep(0.015, 0.085, c_clean);
    let rel = c_clean / max(nbr_avg, 0.10);
    let groove = smoothstep(mortar_t, mortar_t + 0.34, rel);
    let tier = 0.56 + 0.44 * pow(clamp(c_clean * 1.15, 0.0, 1.0), gamma);
    return clamp(abs_open * groove * tier, 0.0, 1.0);
}

// Finds the direction (unit world vector) and relative strength of the dominant local point light
// illuminating this surface, for short voxel-column micro-self-shadowing.
fn dominant_point_light(world_pos: vec3<f32>, face_normal: vec3<f32>) -> vec4<f32> {
    let first = frame.counts.y;
    let count = min(frame.counts.x, 48u);
    var best_dir = vec3<f32>(0.0, 1.0, 0.0);
    var best_score = 0.0;
    for (var i = 0u; i < count; i++) {
        let light = lights[first + i];
        if u32(light.color_kind.w) != 3u {
            continue;
        }
        let to_l = light.position_range.xyz - world_pos;
        let dist_sq = dot(to_l, to_l);
        let range = light.position_range.w;
        if dist_sq >= range * range || dist_sq <= 1e-6 {
            continue;
        }
        let dist = sqrt(dist_sq);
        let dir = to_l / dist;
        let facing = dot(face_normal, dir);
        if facing <= 0.04 {
            continue;
        }
        let ratio = dist / range;
        let win = max(1.0 - ratio * ratio * ratio * ratio, 0.0);
        let power = dot(light.color_kind.rgb, LUMA_W) * win * win * facing / max(dist_sq, 0.25);
        if power > best_score {
            best_score = power;
            best_dir = dir;
        }
    }
    return vec4<f32>(best_dir, best_score);
}

fn shade(given: Surface) -> vec4<f32> {
    var surface = given;
    let p0 = material.parameters[0];
    let p1 = material.parameters[1];
    let p2 = material.parameters[2];
    let has_map = material.parameters[3].x > 0.5;

    // Screen derivatives in uniform control flow first.
    let uv = transform_uv(material.base_uv_u, material.base_uv_v, surface.uv);
    let dp_dx = dpdx(surface.world_position);
    let dp_dy = dpdy(surface.world_position);
    let dt_dx = dpdx(uv);
    let dt_dy = dpdy(uv);
    let det = dt_dx.x * dt_dy.y - dt_dy.x * dt_dx.y;

    let far = length(frame.camera.xyz - surface.world_position);
    let fade = 1.0 - smoothstep(p0.z, p0.w, far);
    // Keep downward-facing ceiling slabs slightly shallower so timber beams pop without pitting ceiling mottling.
    let ceiling_scale = select(1.0, 0.65, surface.normal.y < -0.5);
    let gamma = max(p2.z, 0.25);
    let mortar_t = p2.w;

    // Keep uniform control flow (no early return) so screen-space normal derivatives remain valid for specular AA.
    let good_det = abs(det) > 1e-14;
    let safe_det = select(1.0, det, good_det);
    let along_u = (dp_dx * dt_dy.y - dp_dy * dt_dx.y) / safe_det;
    let along_v = (dp_dy * dt_dx.x - dp_dx * dt_dy.x) / safe_det;
    let n = surface.normal;
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

    // Cap physical extrusion relative to physical texel width so narrow UV-squished strips (door jambs,
    // thin trim boards) never over-extrude across dozens of tiny wood-grain texels; deep-pom (>10cm) relaxes cap.
    let cap_mult = select(1.35, 3.20, p0.x > 0.10);
    let texel_cap = min(metres.x, metres.y) * cap_mult;
    let depth = select(0.0, min(p0.x, texel_cap) * fade * ceiling_scale, good_det);

    var cell = vec2<i32>(floor(uv * texels));
    var side = 0; // 0 = top face, 1 = x voxel wall, 2 = y voxel wall
    var stride = vec2<i32>(1, 1);
    var hit = 0.0;
    var hit_h = 1.0;

    if depth > 1e-4 {
        // 2D-DDA texel-column voxel raymarch (grazing rays clamped to ~80 deg).
        let into = normalize(surface.world_position - frame.camera.xyz);
        let down = max(-dot(into, n), 0.17);
        let a = dot(into, along_u);
        let b = dot(into, along_v);
        let across = vec2<f32>(vv * a - cross_uv * b, uu * b - cross_uv * a) / gram;
        let k = across / down * texels;

        let start = uv * texels;
        cell = vec2<i32>(floor(start));
        stride = vec2<i32>(select(vec2<f32>(-1.0), vec2<f32>(1.0), k >= vec2<f32>(0.0)));
        let per = 1.0 / max(abs(k), vec2<f32>(1e-6));
        let first = select(start - floor(start), floor(start) + 1.0 - start, k >= vec2<f32>(0.0));
        var next = first * per;
        var entered = 0.0;
        var found = -1.0;
        let steps = clamp(i32(p0.y), 1, 64);

        for (var i = 0; i < steps; i++) {
            let h = texel_height(cell, size, has_map, gamma, mortar_t);
            let floor_at = (1.0 - h) * depth;
            if floor_at <= entered {
                found = entered;
                hit_h = h;
                break;
            }
            let leaves = min(next.x, next.y);
            if floor_at <= leaves {
                found = floor_at;
                hit_h = h;
                side = 0;
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
            hit_h = texel_height(cell, size, has_map, gamma, mortar_t);
            found = (1.0 - hit_h) * depth;
            side = 0;
        }
        hit = found;
    } else {
        hit_h = texel_height(cell, size, has_map, gamma, mortar_t);
        hit = (1.0 - hit_h) * 0.04 * fade;
    }

    // Surface normal: crisp 90-degree extruded voxel side walls, or beveled top slope.
    let hx_p = texel_height(cell + vec2<i32>(1, 0), size, has_map, gamma, mortar_t);
    let hx_m = texel_height(cell - vec2<i32>(1, 0), size, has_map, gamma, mortar_t);
    let hy_p = texel_height(cell + vec2<i32>(0, 1), size, has_map, gamma, mortar_t);
    let hy_m = texel_height(cell - vec2<i32>(0, 1), size, has_map, gamma, mortar_t);
    let eff_depth = select(min(0.035, texel_cap * 0.65) * fade, depth, depth > 1e-4);
    let slope = select(vec2<f32>(0.0), vec2<f32>(hx_p - hx_m, hy_p - hy_m) * eff_depth / (2.0 * metres), good_det);

    var normal = n;
    if side == 1 {
        normal = -tu * f32(stride.x);
    } else if side == 2 {
        normal = -tv * f32(stride.y);
    } else if good_det {
        normal = normalize(n - (tu * slope.x + tv * slope.y) * p1.y);
    }

    // Geometric specular anti-aliasing (Valve / Kaplanyan normal-variance roughness filtering):
    // widens GGX lobe wherever screen-space normal jumps sharply (thin 90-degree voxel edges near
    // intense torches or grazing walls) while keeping broad cobble & brick top faces gleaming.
    let dn_dx = dpdx(normal);
    let dn_dy = dpdy(normal);
    let norm_var = clamp(dot(dn_dx, dn_dx) + dot(dn_dy, dn_dy), 0.0, 1.0);
    let kernel_rough = min(0.52 * norm_var, 0.34);

    let at = (vec2<f32>(cell) + 0.5) / texels;
    let color = select(surface.base, textureSampleGrad(albedo, albedo_sampler, at, dt_dx, dt_dy), depth > 1e-4);
    let hollow = select(1.0 - hit_h, clamp(hit / max(p0.x, 1e-4), 0.0, 1.0), depth > 1e-4);

    // Stylized PBR specular rim-catch: broad raised brick/cobble top bevels catch gleaming warm
    // firelight & cool blue fills; razor-thin 90-degree voxel side walls stay satin to prevent
    // single-pixel HDR bloom sparks beside close torches; deep mortar hollows go dead-matte.
    let edge_mag = clamp(length(slope) * 0.65, 0.0, 1.0);
    let rim_mask = max(select(0.0, 0.45, side != 0), edge_mag) * (1.0 - hollow * 0.85) * fade;
    let hollow_matte = smoothstep(0.50, 0.90, hollow);
    let footprint = max(length(dt_dx * texels), length(dt_dy * texels));
    let spec_aa = smoothstep(0.55, 2.20, footprint) * 0.34;
    let side_floor = select(0.0, 0.44, side != 0);
    let rough_raw = mix(mix(p1.z, p1.w, rim_mask), 0.96, hollow_matte);
    let rough_aa = sqrt(clamp(rough_raw * rough_raw + kernel_rough + spec_aa * spec_aa, 0.0, 1.0));
    let rough = max(rough_aa, side_floor);
    let side_metal_scale = select(1.0, 0.30, side != 0);
    let metal = p2.x * rim_mask * side_metal_scale * (1.0 - hollow_matte) * (1.0 - smoothstep(0.60, 1.8, footprint) * 0.65) * (1.0 - norm_var * 0.60);

    // Local 6-step voxel relief self-shadow toward the dominant nearby point light.
    var self_shadow = 1.0;
    if p2.y > 0.0 && depth > 1e-4 && fade > 0.05 {
        let dom = dominant_point_light(surface.world_position, n);
        if dom.w > 0.005 {
            let l_dir = dom.xyz;
            let l_up = max(dot(l_dir, n), 0.12);
            let la = dot(l_dir, along_u);
            let lb = dot(l_dir, along_v);
            let l_across = vec2<f32>(vv * la - cross_uv * lb, uu * lb - cross_uv * la) / gram;
            let l_texels = l_across * texels;
            let l_len = length(l_texels);
            if l_len > 1e-4 {
                let step_uv = l_texels / l_len;
                let dh_per_texel = (l_up / l_len) / max(depth, 1e-4);
                let start_h = 1.0 - hollow;
                var occ = 0.0;
                for (var s = 1; s <= 6; s++) {
                    let dist_t = f32(s) * 0.95;
                    let scell = vec2<i32>(floor(vec2<f32>(cell) + vec2<f32>(0.5) + step_uv * dist_t));
                    let sh = texel_height(scell, size, has_map, gamma, mortar_t);
                    let ray_h = start_h + dh_per_texel * dist_t;
                    if sh > ray_h + 0.02 {
                        occ = max(occ, clamp((sh - ray_h) * 3.5, 0.0, 1.0));
                    }
                }
                self_shadow = 1.0 - occ * p2.y * fade;
            }
        }
    }

    var lit = surface;
    lit.normal = normal;
    let special = i32(round(material.parameters[3].y));
    let water_y = material.parameters[3].z;
    let has_water = material.parameters[3].w > 0.5;

    var final_base = surface.tint.rgb * color.rgb;
    var final_rough = clamp(rough, 0.08, 1.0);
    var final_metal = clamp(metal, 0.0, 0.5);

    // Pass 3 material-specific pulp-fantasy surface responses (#11043):
    let s_col = sqrt(max(color.rgb, vec3<f32>(0.0)));
    let top_face = select(1.0, 0.25, side != 0);
    let aa_var = kernel_rough + spec_aa * spec_aa;
    if special == 1 || special == 2 || special == 4 {
        // Warm gold / brass inlays, studs, medallions, organ pipes, heraldic embroidery.
        let gold_m = smoothstep(0.12, 0.24, color.r)
            * smoothstep(0.07, 0.16, color.g)
            * smoothstep(1.9, 3.0, color.g / max(color.b, 0.015))
            * smoothstep(2.3, 1.35, color.r / max(color.g, 0.015))
            * (1.0 - hollow_matte) * top_face;
        final_metal = max(final_metal, gold_m * 0.46 * (1.0 - smoothstep(0.60, 1.8, footprint) * 0.55));
        let gold_rough = max(sqrt(clamp(0.24 * 0.24 + aa_var, 0.0, 1.0)), side_floor);
        final_rough = mix(final_rough, gold_rough, gold_m * 0.85);
    }
    if special == 2 {
        // Forged dark iron bars, door bands, hinges & rivets (neutral low-chroma dark-to-mid metal).
        let max_c = max(max(s_col.r, s_col.g), s_col.b);
        let min_c = min(min(s_col.r, s_col.g), s_col.b);
        let chroma = max_c - min_c;
        let iron_m = smoothstep(0.10, 0.03, chroma) * smoothstep(0.12, 0.24, max_c) * smoothstep(0.66, 0.46, max_c) * (1.0 - hollow_matte) * top_face;
        final_metal = max(final_metal, iron_m * 0.44 * (1.0 - smoothstep(0.60, 1.8, footprint) * 0.55));
        let iron_rough = max(sqrt(clamp(0.28 * 0.28 + aa_var, 0.0, 1.0)), side_floor);
        final_rough = mix(final_rough, iron_rough, iron_m * 0.78);
    }
    if special == 3 {
        // Wet crimson dungeon blood splatters catch slick dark glossy specular glints.
        let blood_m = smoothstep(0.04, 0.12, color.r)
            * smoothstep(1.9, 3.2, color.r / max(color.g, 0.01))
            * smoothstep(2.2, 3.6, color.r / max(color.b, 0.01))
            * top_face;
        let blood_rough = max(sqrt(clamp(0.18 * 0.18 + aa_var, 0.0, 1.0)), side_floor);
        final_rough = mix(final_rough, blood_rough, blood_m * 0.92);
        final_base = mix(final_base, final_base * vec3<f32>(1.08, 0.82, 0.82), blood_m * 0.45);
    }
    if special == 4 {
        // Soft grazing velvet / heavy woven fabric Fresnel sheen on carpets, banners & drapes.
        let view = normalize(frame.camera.xyz - surface.world_position);
        let velvet_f = pow(1.0 - max(dot(normal, view), 0.0), 3.0);
        final_base += final_base * (velvet_f * 0.22);
    }
    if special == 5 {
        // Keep organic moss & lichen patches soft matte while surrounding damp dungeon stone gleams.
        let green_m = clamp((s_col.g - max(s_col.r * 0.96, s_col.b * 1.12)) * 7.5, 0.0, 1.0);
        let olive_m = smoothstep(0.70, 0.86, s_col.g / max(s_col.r, 1e-4))
            * smoothstep(1.60, 2.50, s_col.g / max(s_col.b, 0.04));
        let moss_m = max(green_m, olive_m) * smoothstep(0.14, 0.26, s_col.g);
        final_rough = mix(final_rough, 0.88, moss_m * 0.85);
        final_metal *= 1.0 - moss_m * 0.90;
    }

    // Flooded dungeon capillary waterline wetness: stone within 0.85m above water darkens and glistens wet.
    if has_water {
        let wet = smoothstep(water_y + 0.85, water_y - 0.15, surface.world_position.y);
        if wet > 0.005 {
            final_base *= mix(1.0, 0.64, wet);
            let wet_rough = max(0.22, sqrt(0.22 * 0.22 + kernel_rough + spec_aa * spec_aa));
            final_rough = mix(final_rough, min(final_rough, wet_rough), wet * 0.88);
        }
    }

    lit.roughness = clamp(final_rough, 0.08, 1.0);
    lit.metalness = clamp(final_metal, 0.0, 0.55);
    let cavity_base = 1.0 - 0.52 * p1.x * hollow;
    lit.base = vec4<f32>(final_base * cavity_base * self_shadow, surface.base.a);
    lit.occlusion *= (1.0 - p1.x * hollow) * mix(1.0, self_shadow, 0.45);
    return standard_shade(lit);
}
