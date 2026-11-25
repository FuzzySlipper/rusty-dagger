// Relief on a building's walls, doors and roofs (#10859, upgraded in #11075). The surface stays its flat classic
// face, and near the camera the view ray is marched into the record's relief, texel for texel: product_map_a is the
// record's relief from the art set's one home for texel heights (groundCover.relief), whose alpha is each texel's height,
// 1 at the face and 0 at the floor. Each texel stands as a small column under the face, as the ground cover's shells lift
// pixel blades: the ray walks the texel grid until it meets a column's top or enters through its side, and lights that
// texel's colour there with the top's or the side's normal. Crevice darkening concentrates on real mortar cracks, seams,
// and openings while keeping recessed flat plaster behind proud timber beams or quoins bright and sunlit. With distance
// the relief shallows to nothing, so far walls shade exactly as the standard stage would, and beyond the fade nothing is
// marched.
//
// parameters[0]: x the floor's depth under the face, metres (the record's depth times the player's strength); y the most
// texels a ray walks; z where the fade starts and w where the relief is gone, metres from the camera.
// parameters[1]: x how dark the deepest hollow goes; y how far the height's slope turns a top's normal.
// parameters[2]: x how far lit window glass rises and falls about its level as the fire behind it flickers, a share of it;
// y how fast, in cycles a second (#10906).
//
// Each window flickers on its own phase: its texture's repeat on its face, and the face's place and facing in the world.
// The world is the session's, so a window's phase holds wherever the player stands in one map pixel's frame.

#import rusty::types::Surface
#import rusty::material::{material, albedo, albedo_sampler, product_map_a}
#import rusty::surface::transform_uv
#import rusty::view::frame
#import rusty::shade::standard_shade

fn texel_height(cell: vec2<i32>, size: vec2<i32>) -> f32 {
    return textureLoad(product_map_a, ((cell % size) + size) % size, 0).a;
}

fn window_phase(surface: Surface) -> f32 {
    let tile = vec2<i32>(floor(surface.uv));
    let plane = i32(round(dot(surface.normal, surface.world_position) * 8.0));
    let facing = vec2<i32>(round(surface.normal.xz * 4.0));
    var h = bitcast<u32>(tile.x) * 73856093u ^ bitcast<u32>(tile.y) * 19349663u ^ bitcast<u32>(plane) * 83492791u
        ^ bitcast<u32>(facing.x * 5 + facing.y) * 2654435761u;
    h = (h ^ (h >> 16u)) * 2246822519u;
    h = h ^ (h >> 13u);
    return f32(h & 0xffffu) / 65536.0;
}

// A lit window's level as the fire behind it flickers: the same waveform as a lamp's (DaggerfallSiteLighting).
fn window_flicker(surface: Surface) -> f32 {
    let t = (frame.time.x * material.parameters[2].y + window_phase(surface)) * 6.2831853;
    return 1.0 + material.parameters[2].x * (sin(t) + 0.6 * sin(2.3 * t + 1.7) + 0.3 * sin(5.1 * t + 0.4)) / 1.9;
}

fn shade(given: Surface) -> vec4<f32> {
    var surface = given;
    if material.parameters[2].x > 0.0 && any(surface.emission > vec3<f32>(0.0)) {
        surface.emission *= window_flicker(surface);
    }
    // Screen derivatives first, in uniform control flow: the world and the texture's uv.
    let uv = transform_uv(material.base_uv_u, material.base_uv_v, surface.uv);
    let dp_dx = dpdx(surface.world_position);
    let dp_dy = dpdy(surface.world_position);
    let dt_dx = dpdx(uv);
    let dt_dy = dpdy(uv);
    let det = dt_dx.x * dt_dy.y - dt_dy.x * dt_dx.y;
    let far = length(frame.camera.xyz - surface.world_position);
    let fade = 1.0 - smoothstep(material.parameters[0].z, material.parameters[0].w, far);
    let depth = material.parameters[0].x * fade;
    let has_relief = depth > 1e-4 && abs(det) > 1e-14;

    var normal = surface.normal;
    var at = uv;
    var hollow = 0.0;
    var edge_mag = 0.0;
    var side = 0; // 0 the top, 1 an x side, 2 a y side
    var texels = vec2<f32>(64.0, 64.0);

    if has_relief {
        // The world per unit of texture uv along u and v, and the face's normal toward the camera.
        let along_u = (dp_dx * dt_dy.y - dp_dy * dt_dx.y) / det;
        let along_v = (dp_dy * dt_dx.x - dp_dx * dt_dy.x) / det;
        let n = surface.normal;
        let size = vec2<i32>(textureDimensions(product_map_a, 0));
        texels = vec2<f32>(size);

        // The view ray in the texture's frame: texels across per metre it goes under the face. A grazing ray is held to a
        // tilt of about 80 degrees, so the walk stays short.
        let into = normalize(surface.world_position - frame.camera.xyz);
        let down = max(-dot(into, n), 0.17);
        let uu = dot(along_u, along_u);
        let cross_uv = dot(along_u, along_v);
        let vv = dot(along_v, along_v);
        let gram = max(uu * vv - cross_uv * cross_uv, 1e-12);
        let a = dot(into, along_u);
        let b = dot(into, along_v);
        let across = vec2<f32>(vv * a - cross_uv * b, uu * b - cross_uv * a) / gram;
        let k = across / down * texels; // texels per metre under the face

        // Walk the texel grid. `next` is how deep the ray is when it leaves the current texel by its x or its y side.
        let start = uv * texels;
        var cell = vec2<i32>(floor(start));
        let stride = vec2<i32>(select(vec2<f32>(-1.0), vec2<f32>(1.0), k >= vec2<f32>(0.0)));
        let per = 1.0 / max(abs(k), vec2<f32>(1e-6));
        let first = select(start - floor(start), floor(start) + 1.0 - start, k >= vec2<f32>(0.0));
        var next = first * per;
        var entered = 0.0;
        var hit = -1.0;
        var hit_h = 1.0;
        let steps = max(i32(material.parameters[0].y), 1);
        for (var i = 0; i < steps; i++) {
            let h = texel_height(cell, size);
            let floor_at = (1.0 - h) * depth;
            if floor_at <= entered {
                hit = entered;
                hit_h = h;
                break;
            }
            let leaves = min(next.x, next.y);
            if floor_at <= leaves {
                hit = floor_at;
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
        if hit < 0.0 {
            hit_h = texel_height(cell, size);
            hit = (1.0 - hit_h) * depth;
            side = 0;
        }

        // The texel's colour, its normal and how deep it lies.
        let tu = normalize(along_u - n * dot(n, along_u));
        let tv = normalize(along_v - n * dot(n, along_v));
        let metres = max(vec2<f32>(sqrt(uu), sqrt(vv)) / texels, vec2<f32>(1e-4)); // a texel's width along u and v
        let slope = vec2<f32>(
            texel_height(cell + vec2<i32>(1, 0), size) - texel_height(cell - vec2<i32>(1, 0), size),
            texel_height(cell + vec2<i32>(0, 1), size) - texel_height(cell - vec2<i32>(0, 1), size)) * depth / (2.0 * metres);
        if side == 1 {
            normal = -tu * f32(stride.x);
        } else if side == 2 {
            normal = -tv * f32(stride.y);
        } else {
            normal = normalize(n - (tu * slope.x + tv * slope.y) * material.parameters[1].y);
        }
        at = (vec2<f32>(cell) + 0.5) / texels;
        edge_mag = clamp(length(slope) * 0.55, 0.0, 1.0);
        // Concentrate cavity darkening on true mortar cracks, seams, and recessed window glass (< 0.22)
        // so flat recessed plaster behind proud timber beams or stone quoins (~0.26..0.30) stays sunlit.
        let hollow_raw = clamp(hit / max(material.parameters[0].x, 1e-4), 0.0, 1.0);
        hollow = clamp((1.0 - smoothstep(0.02, 0.22, hit_h)) * 0.85 + hollow_raw * 0.22, 0.0, 1.0);
    }

    // Geometric specular AA derivatives computed in uniform control flow.
    let dn_dx = dpdx(normal);
    let dn_dy = dpdy(normal);
    let norm_var = clamp(dot(dn_dx, dn_dx) + dot(dn_dy, dn_dy), 0.0, 1.0);
    let kernel_rough = min(0.52 * norm_var, 0.34);
    let footprint = max(length(dt_dx * texels), length(dt_dy * texels));
    let spec_aa = smoothstep(0.55, 2.20, footprint) * 0.34;
    let color = textureSampleGrad(albedo, albedo_sampler, at, dt_dx, dt_dy);

    if !has_relief {
        return standard_shade(surface);
    }

    let rim_mask = max(select(0.0, 0.35, side != 0), edge_mag) * (1.0 - hollow * 0.85) * fade;
    let rough_raw = mix(mix(0.90, 0.70, rim_mask), 0.98, smoothstep(0.45, 0.88, hollow));
    let rough_aa = sqrt(clamp(rough_raw * rough_raw + kernel_rough + spec_aa * spec_aa, 0.0, 1.0));

    var lit = surface;
    lit.normal = normal;
    lit.roughness = max(rough_aa, select(0.0, 0.72, side != 0));
    lit.base = vec4<f32>(surface.tint.rgb * color.rgb * (1.0 - 0.35 * material.parameters[1].x * hollow), surface.base.a);
    lit.occlusion *= 1.0 - material.parameters[1].x * hollow;
    return standard_shade(lit);
}
