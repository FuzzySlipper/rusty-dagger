// Snow on a winter tree's leaves (DaggerfallFloraField): where a leaf card faces up, clumps of it are drawn in the
// snow's colour, as the winter sprites paint snow lying on their crowns. Drifts are smooth noise over the leaf texture's
// texels, shifted on every card, cut at the texture's pixels so snow takes the leaves' pixel shapes; the leaves' dark
// outlines and the gaps between them stay as they are.
//
// parameters[0]: x the share of upward leaf the snow covers; y the leaf texture's texels a side; z a drift's texels across.
// parameters[1]: xyz the snow's linear colour.
#import rusty::types::Surface
#import rusty::material::material
#import rusty::shade::standard_shade

// A leaf texel darker than this (linear) is an outline or a shadow, which snow leaves bare.
const OUTLINE: f32 = 0.012;

fn hash(p: vec3<f32>) -> f32 {
    var q = fract(p * 0.1031);
    q += dot(q, q.yzx + 33.33);
    return fract((q.x + q.y) * q.z);
}

// Smooth noise over a plane, from 0 to 1, varied by a seed.
fn drifts(at: vec2<f32>, seed: vec3<f32>) -> f32 {
    let cell = floor(at);
    let f = smoothstep(vec2<f32>(0.0), vec2<f32>(1.0), at - cell);
    let a = hash(vec3<f32>(cell, 0.0) + seed);
    let b = hash(vec3<f32>(cell + vec2<f32>(1.0, 0.0), 0.0) + seed);
    let c = hash(vec3<f32>(cell + vec2<f32>(0.0, 1.0), 0.0) + seed);
    let d = hash(vec3<f32>(cell + vec2<f32>(1.0, 1.0), 0.0) + seed);
    return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}

fn shade(surface: Surface) -> vec4<f32> {
    var snowed = surface;
    let lightness = dot(surface.base.rgb, vec3<f32>(0.2126, 0.7152, 0.0722));
    // Snow is judged texel by texel, so its edge is the texture's pixels; its drifts are smooth noise a clump's size
    // across, which the card's place in the world shifts from card to card.
    let texel = floor(surface.uv * material.parameters[0].y) + 0.5;
    let seed = floor(surface.world_position * 0.7) * 17.0;
    let facing = smoothstep(0.05, 0.6, surface.normal.y);
    if lightness > OUTLINE && drifts(texel / material.parameters[0].z, seed) < material.parameters[0].x * facing {
        // A little light and shade across the snow, texel by texel, as the sprites paint it.
        let tone = 1.0 + 0.12 * hash(vec3<f32>(texel, 1.0) + seed);
        snowed.base = vec4<f32>(material.parameters[1].xyz * tone, surface.base.a);
    }
    return standard_shade(snowed);
}
