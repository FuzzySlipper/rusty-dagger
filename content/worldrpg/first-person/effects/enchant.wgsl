// An enchanted weapon's lightning, as the classic enchanted frames draw it (HeldArcs): hard lines of a fixed width on the
// screen, redrawn whole a few times a second, not a soft glow. The arcs are ribbons down paths on the model: a vertex's
// normal runs along its path, uv.x is its side of the ribbon and uv.y its arc's number plus how far along it is.
// With the RIM keyword it is instead the weapon drawn again a little larger behind itself, the frames' blue outline.
//
// parameters[0]: x the width as a share of the view's height; y how far an arc wanders from its path, in metres; z its
// redraws a second; w the share of arcs shown at once.
// parameters[1]: rgb the line's edge; parameters[2]: rgb its core; parameters[3]: rgb a hot core, w the share of arcs with one.
// For the rim, parameters[0].x is its width, y how far behind the weapon it is pushed, and parameters[1] its colour.
#import rusty::types::{Surface, Vertex}
#import rusty::view::frame
#import rusty::material::material

fn scatter(key: vec2<f32>) -> f32 {
    return fract(sin(dot(key, vec2<f32>(12.9898, 78.233))) * 43758.5453);
}

fn redraw() -> f32 {
    return floor(frame.time.x * material.parameters[0].z);
}

// How many metres a share of the view's height spans at this distance.
fn spans(share: f32, distance: f32) -> f32 {
    return share * 2.0 * distance / abs(frame.view_proj[1][1]);
}

#ifdef RIM
fn displace(vertex: Vertex) -> vec3<f32> {
    let to_eye = frame.camera.xyz - vertex.world_position;
    let distance = length(to_eye);
    let grown = normalize(vertex.world_normal) * spans(material.parameters[0].x, distance);
    return vertex.world_position + grown - to_eye / distance * material.parameters[0].y;
}

fn shade(surface: Surface) -> vec4<f32> {
    return vec4<f32>(material.parameters[1].rgb, 1.0);
}
#else
fn displace(vertex: Vertex) -> vec3<f32> {
    let arc = floor(vertex.uv.y);
    let along = vertex.uv.y - arc;
    let to_eye = frame.camera.xyz - vertex.world_position;
    let distance = length(to_eye);
    let across = normalize(cross(normalize(vertex.world_normal), to_eye / distance));
    let wander = (scatter(vec2<f32>(vertex.uv.y, redraw())) - 0.5) * material.parameters[0].y;
    let width = spans(material.parameters[0].x, distance) * (1.0 - along * 0.6);
    // Lifted toward the eye, so an arc on the near face isn't lost in the model's crunch.
    return vertex.world_position + across * (vertex.uv.x * width + wander) + to_eye / distance * 0.005;
}

fn shade(surface: Surface) -> vec4<f32> {
    let arc = floor(surface.uv.y);
    if scatter(vec2<f32>(arc, redraw() + 17.0)) > material.parameters[0].w {
        discard;
    }
    // A share of the arcs burn hotter, as the frames' yellow runs do.
    let hot = scatter(vec2<f32>(arc, redraw() + 31.0)) < material.parameters[3].w;
    let core = select(material.parameters[2].rgb, material.parameters[3].rgb, hot);
    return vec4<f32>(select(material.parameters[1].rgb, core, abs(surface.uv.x) < 0.4), 1.0);
}
#endif
