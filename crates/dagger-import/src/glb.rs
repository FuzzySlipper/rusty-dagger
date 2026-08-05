//! Minimal GLB (glTF 2.0 binary) writer — single mesh, per-texture primitives.

#[derive(Clone)]
pub struct PrimitiveInput {
    pub name: String,
    pub positions: Vec<[f32; 3]>,
    pub normals: Vec<[f32; 3]>,
    pub uvs: Vec<[f32; 2]>,
    pub indices: Vec<u32>,
    /// Texture index into `textures` (or None for the default untextured material).
    pub texture: Option<usize>,
}

pub struct TextureInput {
    pub name: String,
    pub png: Vec<u8>,
    /// Classic texture identity: (archive, record).
    pub id: (u16, u16),
    /// Average RGB of the decoded texture (0..1), used for untextured material colors.
    pub avg_color: [f32; 3],
}

/// Stable engine-asset slug for a classic texture identity ("texture-450-5").
/// Engine asset ids are lowercase kebab-case; importer prepends "texture/".
pub fn texture_slug(id: (u16, u16)) -> String {
    format!("texture-{}-{}", id.0, id.1)
}

fn pad4(v: &mut Vec<u8>, pad: u8) {
    while v.len() % 4 != 0 {
        v.push(pad);
    }
}

fn json_escape(s: &str) -> String {
    s.replace('\\', "\\\\").replace('"', "\\\"")
}

/// Write a GLB: one named node for the combined dungeon mesh (per-texture
/// primitives) plus one named glTF node per door primitive, each carrying its
/// own single-primitive mesh so every carved door is addressable by name
/// (door-N-<model_id>) in the engine-consumable artifact.
pub fn write_glb(
    mesh_name: &str,
    primitives: &[PrimitiveInput],
    door_primitives: &[PrimitiveInput],
    textures: &[TextureInput],
) -> Vec<u8> {
    let mut bin: Vec<u8> = Vec::new();
    let mut buffer_views: Vec<String> = Vec::new();
    let mut accessors: Vec<String> = Vec::new();

    let mut image_views: Vec<usize> = Vec::new();
    for tex in textures {
        pad4(&mut bin, 0);
        let offset = bin.len();
        bin.extend_from_slice(&tex.png);
        buffer_views.push(format!(
            "{{\"buffer\":0,\"byteOffset\":{offset},\"byteLength\":{}}}",
            tex.png.len()
        ));
        image_views.push(buffer_views.len() - 1);
    }

    let mut material_json: Vec<String> = Vec::new();

    // Default untextured material (index 0) always present
    material_json.push(
        "{\"name\":\"default\",\"pbrMetallicRoughness\":{\"baseColorFactor\":[0.72,0.70,0.66,1.0],\"metallicFactor\":0.0,\"roughnessFactor\":1.0}}".to_string(),
    );
    for (i, tex) in textures.iter().enumerate() {
        material_json.push(format!(
            "{{\"name\":\"{}\",\"pbrMetallicRoughness\":{{\"baseColorTexture\":{{\"index\":{}}},\"metallicFactor\":0.0,\"roughnessFactor\":1.0}}}}",
            json_escape(&tex.name),
            i
        ));
    }

    // Build accessor sets + primitive JSON for a slice of primitives; returns
    // the primitive JSON entries (shared by the dungeon mesh and per-door meshes).
    let build_prims = |prims: &[PrimitiveInput],
                       bin: &mut Vec<u8>,
                       buffer_views: &mut Vec<String>,
                       accessors: &mut Vec<String>|
     -> Vec<String> {
        let mut out = Vec::new();
        for prim in prims {
            // POSITION
            let pos_bytes: &[u8] = unsafe {
                std::slice::from_raw_parts(
                    prim.positions.as_ptr() as *const u8,
                    prim.positions.len() * 12,
                )
            };
            pad4(bin, 0);
            let poff = bin.len();
            bin.extend_from_slice(pos_bytes);
            buffer_views.push(format!(
                "{{\"buffer\":0,\"byteOffset\":{poff},\"byteLength\":{},\"target\":34962}}",
                pos_bytes.len()
            ));
            let pview = buffer_views.len() - 1;
            let (mut mn, mut mx) = ([f32::MAX; 3], [f32::MIN; 3]);
            for p in &prim.positions {
                for k in 0..3 {
                    mn[k] = mn[k].min(p[k]);
                    mx[k] = mx[k].max(p[k]);
                }
            }
            accessors.push(format!(
                "{{\"bufferView\":{pview},\"componentType\":5126,\"count\":{},\"type\":\"VEC3\",\"min\":[{},{},{}],\"max\":[{},{},{}]}}",
                prim.positions.len(), mn[0], mn[1], mn[2], mx[0], mx[1], mx[2]
            ));
            let pos_acc = accessors.len() - 1;

            // NORMAL
            let nrm_bytes: &[u8] = unsafe {
                std::slice::from_raw_parts(
                    prim.normals.as_ptr() as *const u8,
                    prim.normals.len() * 12,
                )
            };
            pad4(bin, 0);
            let noff = bin.len();
            bin.extend_from_slice(nrm_bytes);
            buffer_views.push(format!(
                "{{\"buffer\":0,\"byteOffset\":{noff},\"byteLength\":{},\"target\":34962}}",
                nrm_bytes.len()
            ));
            let nview = buffer_views.len() - 1;
            accessors.push(format!(
                "{{\"bufferView\":{nview},\"componentType\":5126,\"count\":{},\"type\":\"VEC3\"}}",
                prim.normals.len()
            ));
            let nrm_acc = accessors.len() - 1;

            // TEXCOORD_0
            let uv_bytes: &[u8] = unsafe {
                std::slice::from_raw_parts(prim.uvs.as_ptr() as *const u8, prim.uvs.len() * 8)
            };
            pad4(bin, 0);
            let uoff = bin.len();
            bin.extend_from_slice(uv_bytes);
            buffer_views.push(format!(
                "{{\"buffer\":0,\"byteOffset\":{uoff},\"byteLength\":{},\"target\":34962}}",
                uv_bytes.len()
            ));
            let uview = buffer_views.len() - 1;
            accessors.push(format!(
                "{{\"bufferView\":{uview},\"componentType\":5126,\"count\":{},\"type\":\"VEC2\"}}",
                prim.uvs.len()
            ));
            let uv_acc = accessors.len() - 1;

            // INDICES
            let idx_bytes: &[u8] = unsafe {
                std::slice::from_raw_parts(
                    prim.indices.as_ptr() as *const u8,
                    prim.indices.len() * 4,
                )
            };
            pad4(bin, 0);
            let ioff = bin.len();
            bin.extend_from_slice(idx_bytes);
            buffer_views.push(format!(
                "{{\"buffer\":0,\"byteOffset\":{ioff},\"byteLength\":{},\"target\":34963}}",
                idx_bytes.len()
            ));
            let iview = buffer_views.len() - 1;
            accessors.push(format!(
                "{{\"bufferView\":{iview},\"componentType\":5125,\"count\":{},\"type\":\"SCALAR\"}}",
                prim.indices.len()
            ));
            let idx_acc = accessors.len() - 1;

            let material = prim.texture.map(|t| t + 1).unwrap_or(0);
            out.push(format!(
                "{{\"attributes\":{{\"POSITION\":{pos_acc},\"NORMAL\":{nrm_acc},\"TEXCOORD_0\":{uv_acc}}},\"indices\":{idx_acc},\"material\":{material},\"mode\":4}}"
            ));
        }
        out
    };

    // Dungeon mesh (per-texture primitives) + one named mesh per door.
    let dungeon_prims_json = build_prims(primitives, &mut bin, &mut buffer_views, &mut accessors);
    let mut meshes_json: Vec<String> = Vec::new();
    meshes_json.push(format!(
        "{{\"name\":\"{}\",\"primitives\":[{}]}}",
        json_escape(mesh_name),
        dungeon_prims_json.join(",")
    ));
    let mut nodes_json: Vec<String> = Vec::new();
    nodes_json.push(format!(
        "{{\"mesh\":0,\"name\":\"{}\"}}",
        json_escape(mesh_name)
    ));

    for (door_i, door) in door_primitives.iter().enumerate() {
        let door_json = build_prims(
            std::slice::from_ref(door),
            &mut bin,
            &mut buffer_views,
            &mut accessors,
        );
        meshes_json.push(format!(
            "{{\"name\":\"{}\",\"primitives\":[{}]}}",
            json_escape(&door.name),
            door_json.join(",")
        ));
        nodes_json.push(format!(
            "{{\"mesh\":{},\"name\":\"{}\"}}",
            door_i + 1,
            json_escape(&door.name)
        ));
    }
    let node_indices: Vec<String> = (0..nodes_json.len()).map(|i| i.to_string()).collect();

    let textures_json: Vec<String> = textures
        .iter()
        .enumerate()
        .map(|(i, _)| format!("{{\"sampler\":0,\"source\":{i}}}"))
        .collect();
    let images_json: Vec<String> = textures
        .iter()
        .enumerate()
        .map(|(i, t)| {
            format!(
                "{{\"bufferView\":{},\"mimeType\":\"image/png\",\"name\":\"{}\"}}",
                image_views[i],
                json_escape(&t.name)
            )
        })
        .collect();

    let json = format!(
        "{{\"asset\":{{\"version\":\"2.0\",\"generator\":\"dagger-import\"}},\
\"scene\":0,\"scenes\":[{{\"nodes\":[{}]}}],\
\"nodes\":[{}],\
\"meshes\":[{}],\
\"materials\":[{}],\
\"textures\":[{}],\
\"images\":[{}],\
\"samplers\":[{{\"magFilter\":9728,\"minFilter\":9728,\"wrapS\":10497,\"wrapT\":10497}}],\
\"accessors\":[{}],\
\"bufferViews\":[{}],\
\"buffers\":[{{\"byteLength\":{}}}]}}",
        node_indices.join(","),
        nodes_json.join(","),
        meshes_json.join(","),
        material_json.join(","),
        textures_json.join(","),
        images_json.join(","),
        accessors.join(","),
        buffer_views.join(","),
        bin.len()
    );

    let mut json_bytes = json.into_bytes();
    pad4(&mut json_bytes, b' ');
    pad4(&mut bin, 0);

    let total = 12 + 8 + json_bytes.len() + 8 + bin.len();
    let mut glb = Vec::with_capacity(total);
    glb.extend_from_slice(&0x4654_6C67u32.to_le_bytes()); // "glTF"
    glb.extend_from_slice(&2u32.to_le_bytes());
    glb.extend_from_slice(&(total as u32).to_le_bytes());
    glb.extend_from_slice(&(json_bytes.len() as u32).to_le_bytes());
    glb.extend_from_slice(&0x4E4F_534Au32.to_le_bytes()); // "JSON"
    glb.extend_from_slice(&json_bytes);
    glb.extend_from_slice(&(bin.len() as u32).to_le_bytes());
    glb.extend_from_slice(&0x004E_4942u32.to_le_bytes()); // "BIN\0"
    glb.extend_from_slice(&bin);
    glb
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn writes_valid_glb_container() {
        let prim = PrimitiveInput {
            name: "tri".into(),
            positions: vec![[0.0, 0.0, 0.0], [1.0, 0.0, 0.0], [0.0, 1.0, 0.0]],
            normals: vec![[0.0, 0.0, 1.0]; 3],
            uvs: vec![[0.0, 0.0], [1.0, 0.0], [0.0, 1.0]],
            indices: vec![0, 1, 2],
            texture: None,
        };
        let glb = write_glb("tri", &[prim], &[], &[]);
        assert_eq!(&glb[0..4], b"glTF");
        let total = u32::from_le_bytes([glb[8], glb[9], glb[10], glb[11]]) as usize;
        assert_eq!(total, glb.len());
    }
}
