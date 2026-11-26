// Outward 3D architectural silhouette-POM extrusion for exterior buildings (#11075).
// Displaces a single outer envelope +H outward along world_normal, walks a 2D-DDA voxel raymarch
// inward against the record's authored heightmap (`product_map_a.a`, 1 texel tap per step), and
// discards empty mortar, recessed plaster panels (<= cutoff), and window glass openings (0.0) so:
//   - Half-timber beams, corner quoins, shutters, mullions, castle stones, snowdrifts, and pitched
//     roof shingle courses physically project out from building walls and break corner silhouettes;
//   - Flat recessed plaster stays clean and flush on the base wall;
//   - Glowing flickering night windows shine cleanly through recessed 3D window frames & mullions.
//
// parameters[0]: (mode, lift_metres, max_steps, cutoff)
//   mode = 0: fast discard for non-extruded slots sharing a multi-material static mesh.
//   mode = 1: outward silhouette-envelope 2D-DDA voxel raymarch.
// parameters[1]: (fade_start, fade_end, cavity_darkness, slope_strength)
// parameters[2]: (base_roughness, rim_roughness, 0, 0)

#import rusty::types::{Surface, Vertex}
#import rusty::material::{material, albedo, albedo_sampler, product_map_a}
#import rusty::surface::transform_uv
#import rusty::view::frame
#import rusty::shade::standard_shade

fn wrap_cell(cell: vec2<i32>, size: vec2<i32>) -> vec2<i32> {
    return ((cell % size) + size) % size;
}

fn block_height(cell: vec2<i32>, size: vec2<i32>, cutoff: f32) -> f32 {
    let h = textureLoad(product_map_a, wrap_cell(cell, size), 0).a;
    return smoothstep(cutoff, 0.92, h);
}

fn surface_extrude_scale(world_normal: vec3<f32>) -> f32 {
    // Full outward projection on vertical walls and pitched roofs (up to ~60 deg slope) so shingle
    // courses, stonework and snowdrifts step out cleanly; fade cleanly to zero on flat horizontal
    // upward floors/thresholds/pads (ny > 0.96) so buried gate/building slabs never poke up through
    // terrain roads or snow and feet never clip balconies; gentler on flat downward soffits so
    // archway ceilings and eaves stay tight.
    let flat_up = smoothstep(0.86, 0.96, world_normal.y);
    let flat_down = smoothstep(0.82, 0.96, -world_normal.y);
    return (1.0 - flat_up) * mix(1.0, 0.55, flat_down);
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
    let mode = p0.x;
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
    let size = vec2<i32>(textureDimensions(product_map_a, 0));
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

    let surf_scale = surface_extrude_scale(n);
    let into = normalize(given.world_position - frame.camera.xyz);
    let ndotv = -dot(into, n);
    // Taper smoothly to zero on razor-edge-on faces (< ~11 deg grazing, e.g. edge-on open gate
    // leaves or horizon soffit edges) so displaced outer envelopes never float across sky gaps.
    let grazing = smoothstep(0.08, 0.20, ndotv);
    let eff_fade = fade * grazing;

    var keep = mode >= 0.5 && eff_fade > 0.02 && surf_scale > 0.02;
    var cell = vec2<i32>(floor(uv * texels));
    var normal = n;
    var hit_pos = given.world_position;
    var side = 0;
    var ao_factor = 1.0;
    var rim_mask = 0.0;

    if keep {
        let depth = p0.y * surf_scale;
        let down = max(ndotv, 0.12);
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
            let bh = block_height(cell, size, cutoff) * eff_fade;
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
            let hx_p = block_height(cell + vec2<i32>(1, 0), size, cutoff);
            let hx_m = block_height(cell - vec2<i32>(1, 0), size, cutoff);
            let hy_p = block_height(cell + vec2<i32>(0, 1), size, cutoff);
            let hy_m = block_height(cell - vec2<i32>(0, 1), size, cutoff);
            let slope = select(vec2<f32>(0.0), vec2<f32>(hx_p - hx_m, hy_p - hy_m) * depth / (2.0 * metres), good_det);

            if side == 1 {
                normal = -tu * f32(stride.x);
            } else if side == 2 {
                normal = -tv * f32(stride.y);
            } else if good_det {
                normal = normalize(n - (tu * slope.x + tv * slope.y) * p1.w);
            }

            let hollow = clamp(found / max(depth, 1e-4), 0.0, 1.0);
            ao_factor = mix(1.0, 1.0 - 0.42 * p1.z, hollow);
            let edge_mag = clamp(length(slope) * 0.55, 0.0, 1.0);
            rim_mask = max(select(0.0, 0.35, side != 0), edge_mag) * (1.0 - hollow * 0.70) * fade;
        }
    }

    // Geometric specular AA derivatives computed in uniform control flow BEFORE discard.
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

    let side_floor = select(0.0, 0.74, side != 0);
    let rough_raw = mix(p2.x, p2.y, rim_mask);
    let final_rough = max(sqrt(clamp(rough_raw * rough_raw + aa_var, 0.0, 1.0)), side_floor);
    let side_tone = select(1.0, 0.88, side != 0);

    var lit = given;
    lit.world_position = hit_pos;
    lit.normal = normal;
    lit.roughness = clamp(final_rough, 0.25, 1.0);
    lit.metalness = 0.0;
    lit.base = vec4<f32>(given.tint.rgb * color.rgb * ao_factor * side_tone, 1.0);
    lit.occlusion *= ao_factor;
    return standard_shade(lit);
}
