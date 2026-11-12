// Ground cover shells (DaggerfallGroundCover): a stack of layers over near ground, each cut out where a texel's height
// (the texture's alpha) falls below its layer's cutoff. The vertex alpha is the share of the ground the stack's layer
// covers there; where it thins, texels keep their height in clumps that drop out one by one, so one ground gives way to
// the next raggedly rather than shrinking evenly. The wind slides each layer by whole texels, more the higher it stands,
// so pixel blades lean in steps rather than sliding. Snow's top texels glint as the viewer moves.
//
// parameters[0]: x the texture's texels per metre; y how far the top layer slides at full wind, in metres; z the gusts'
// rate; w this layer's height, from 0 at the lowest to 1 at the top.
// parameters[1]: x this layer's cutoff; y how raggedly the ground gives way (0 evenly, 1 in clumps); z the share of
// texels that glint; w the texture's texels a side.
//
// The material's own alpha cutoff is only above zero, so this shader makes the cut.
#import rusty::types::{Surface, Vertex, Caster}
#import rusty::view::frame
#import rusty::material::material
#import rusty::shade::standard_shade

// The clumps a thinning ground drops out by span this many texels.
const CLUMP_TEXELS: f32 = 5.0;

fn hash(p: vec3<f32>) -> f32 {
    var q = fract(p * 0.1031);
    q += dot(q, q.yzx + 33.33);
    return fract((q.x + q.y) * q.z);
}

// Smooth noise over the texture's texels in clumps, from 0 to 1.
fn clumps(texel: vec2<f32>) -> f32 {
    let at = texel / CLUMP_TEXELS;
    let cell = floor(at);
    let f = smoothstep(vec2<f32>(0.0), vec2<f32>(1.0), at - cell);
    let a = hash(vec3<f32>(cell, 7.0));
    let b = hash(vec3<f32>(cell + vec2<f32>(1.0, 0.0), 7.0));
    let c = hash(vec3<f32>(cell + vec2<f32>(0.0, 1.0), 7.0));
    let d = hash(vec3<f32>(cell + vec2<f32>(1.0, 1.0), 7.0));
    return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}

fn texel_of(uv: vec2<f32>) -> vec2<f32> {
    return floor(uv * material.parameters[1].w);
}

fn displace(vertex: Vertex) -> vec3<f32> {
    let layer = material.parameters[0].w;
    let phase = dot(vertex.world_position.xz, vec2<f32>(0.37, 0.23));
    let gust = 1.0 - frame.wind.w + frame.wind.w * (0.5 + 0.5 * sin(frame.time.x * material.parameters[0].z + phase));
    let lean = material.parameters[0].y * frame.wind.z * gust * layer * layer;
    let texels = round(lean * material.parameters[0].x) / material.parameters[0].x;
    return vertex.world_position + vec3<f32>(frame.wind.x, 0.0, frame.wind.y) * texels;
}

fn shade(surface: Surface) -> vec4<f32> {
    let cover = surface.tint.a;
    let height = surface.base.a / max(cover, 1e-4);
    let texel = texel_of(surface.uv);
    // Evenly, a texel stands at its height times the cover; raggedly, at its full height where the cover is above its
    // clump's threshold and not at all where it is below, a clump crossing over a few hundredths of cover.
    let threshold = 0.05 + 0.85 * clumps(texel);
    let kept = smoothstep(threshold - 0.04, threshold + 0.04, cover);
    if height * mix(cover, kept, material.parameters[1].y) < material.parameters[1].x {
        discard;
    }

    var lit = standard_shade(surface);
    let glints = material.parameters[1].z;
    if glints > 0.0 {
        // A texel glints from some directions and not others, so the glints twinkle as the viewer moves.
        let toward = normalize(frame.camera.xyz - surface.world_position);
        let facing = floor(toward.xz * 6.0);
        if hash(vec3<f32>(texel, dot(facing, vec2<f32>(13.0, 7.0)))) < glints {
            lit = vec4<f32>(lit.rgb * 2.4 + vec3<f32>(0.03), lit.a);
        }
    }
    return lit;
}

fn cast_shadow(caster: Caster) {
    // The shadow takes the even cut: its pixels are too coarse for the clumps.
    if caster.alpha < material.parameters[1].x {
        discard;
    }
}
