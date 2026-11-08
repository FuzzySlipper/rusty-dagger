// Ground cover shells (DaggerfallGroundCover): a stack of layers over near ground, each cut out where the shell texture's
// blade height (its alpha, times the vertex's grass cover) falls below its layer's cutoff. The wind slides each layer by
// whole texels, more the higher it stands, so the pixel blades lean in steps rather than sliding.
//
// parameters[0]: x the shell texture's texels per metre; y how far the top layer slides at full wind, in metres; z the
// gusts' rate; w this layer's height, from 0 at the lowest to 1 at the top.
#import rusty::types::{Surface, Vertex}
#import rusty::view::frame
#import rusty::material::material
#import rusty::shade::standard_shade

fn displace(vertex: Vertex) -> vec3<f32> {
    let layer = material.parameters[0].w;
    let phase = dot(vertex.world_position.xz, vec2<f32>(0.37, 0.23));
    let gust = 1.0 - frame.wind.w + frame.wind.w * (0.5 + 0.5 * sin(frame.time.x * material.parameters[0].z + phase));
    let lean = material.parameters[0].y * frame.wind.z * gust * layer * layer;
    let texels = round(lean * material.parameters[0].x) / material.parameters[0].x;
    return vertex.world_position + vec3<f32>(frame.wind.x, 0.0, frame.wind.y) * texels;
}

fn shade(surface: Surface) -> vec4<f32> {
    return standard_shade(surface);
}
