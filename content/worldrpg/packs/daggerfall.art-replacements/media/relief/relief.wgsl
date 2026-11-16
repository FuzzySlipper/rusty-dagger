// Relief on a building's walls, doors and roofs (#10859, from #10854's prototype). The surface stays its flat classic
// face, and near the camera the view ray is marched into the record's relief, texel for texel: product_map_a is the
// record's relief from the art set's one home for texel heights (groundCover.relief), whose alpha is each texel's height,
// 1 at the face and 0 at the floor. Each texel stands as a small column under the face, as the ground cover's shells lift
// pixel blades: the ray walks the texel grid until it meets a column's top or enters through its side, and lights that
// texel's colour there with the top's or the side's normal. A hollow darkens a little. With distance the relief shallows
// to nothing, so far walls shade exactly as the standard stage would, and beyond the fade nothing is marched.
//
// parameters[0]: x the floor's depth under the face, metres (the record's depth times the player's strength); y the most
// texels a ray walks; z where the fade starts and w where the relief is gone, metres from the camera.
// parameters[1]: x how dark the deepest hollow goes; y how far the height's slope turns a top's normal.

#import rusty::types::Surface
#import rusty::material::{material, albedo, albedo_sampler, product_map_a}
#import rusty::surface::transform_uv
#import rusty::view::frame
#import rusty::shade::standard_shade

fn texel_height(cell: vec2<i32>, size: vec2<i32>) -> f32 {
    return textureLoad(product_map_a, ((cell % size) + size) % size, 0).a;
}

fn shade(surface: Surface) -> vec4<f32> {
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
    if depth <= 1e-4 || abs(det) <= 1e-14 {
        return standard_shade(surface);
    }

    // The world per unit of texture uv along u and v, and the face's normal toward the camera.
    let along_u = (dp_dx * dt_dy.y - dp_dy * dt_dx.y) / det;
    let along_v = (dp_dy * dt_dx.x - dp_dx * dt_dy.x) / det;
    let n = surface.normal;
    let size = vec2<i32>(textureDimensions(product_map_a, 0));
    let texels = vec2<f32>(size);

    // The view ray in the texture's frame: texels across per metre it goes under the face. A grazing ray is held to a
    // tilt of about 80 degrees, so the walk stays short.
    let into = normalize(surface.world_position - frame.camera.xyz);
    let down = max(-dot(into, n), 0.17);
    let uu = dot(along_u, along_u);
    let cross_uv = dot(along_u, along_v);
    let vv = dot(along_v, along_v);
    let gram = uu * vv - cross_uv * cross_uv;
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
    var side = 0; // 0 the top, 1 an x side, 2 a y side
    var hit = -1.0;
    let steps = max(i32(material.parameters[0].y), 1);
    for (var i = 0; i < steps; i++) {
        let floor_at = (1.0 - texel_height(cell, size)) * depth;
        if floor_at <= entered {
            hit = entered;
            break;
        }
        let leaves = min(next.x, next.y);
        if floor_at <= leaves {
            hit = floor_at;
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
        hit = (1.0 - texel_height(cell, size)) * depth;
        side = 0;
    }

    // The texel's colour, its normal and how deep it lies.
    let tu = normalize(along_u - n * dot(n, along_u));
    let tv = normalize(along_v - n * dot(n, along_v));
    var normal = n;
    if side == 1 {
        normal = -tu * f32(stride.x);
    } else if side == 2 {
        normal = -tv * f32(stride.y);
    } else {
        let metres = vec2<f32>(sqrt(uu), sqrt(vv)) / texels; // a texel's width along u and v
        let slope = vec2<f32>(texel_height(cell + vec2<i32>(1, 0), size) - texel_height(cell - vec2<i32>(1, 0), size),
            texel_height(cell + vec2<i32>(0, 1), size) - texel_height(cell - vec2<i32>(0, 1), size)) * depth / (2.0 * metres);
        normal = normalize(n - (tu * slope.x + tv * slope.y) * material.parameters[1].y);
    }
    let at = (vec2<f32>(cell) + 0.5) / texels;
    let color = textureSampleGrad(albedo, albedo_sampler, at, dt_dx, dt_dy);
    let hollow = clamp(hit / material.parameters[0].x, 0.0, 1.0);
    var lit = surface;
    lit.normal = normal;
    lit.base = vec4<f32>(surface.tint.rgb * color.rgb * (1.0 - 0.35 * material.parameters[1].x * hollow), surface.base.a);
    lit.occlusion *= 1.0 - material.parameters[1].x * hollow;
    return standard_shade(lit);
}
