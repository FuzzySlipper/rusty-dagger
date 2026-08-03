//! Minimal GLB (glTF 2.0 binary) writer — single mesh, per-texture primitives.

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
    /// Average RGB of the decoded texture (0..1), used for untextured material colors.
    pub avg_color: [f32; 3],
}

fn pad4(v: &mut Vec<u8>, pad: u8) {
    while v.len() % 4 != 0 {
        v.push(pad);
    }
}

fn json_escape(s: &str) -> String {
    s.replace('\\', "\\\\").replace('"', "\\\"")
}

/// Write a GLB with one mesh whose primitives are `primitives`.
pub fn write_glb(
    mesh_name: &str,
    primitives: &[PrimitiveInput],
    textures: &[TextureInput],
) -> Vec<u8> {
    let mut bin: Vec<u8> = Vec::new();
    let mut buffer_views: Vec<String> = Vec::new();
    let mut accessors: Vec<String> = Vec::new();

    let mut add_buffer_view = |bin: &mut Vec<u8>, data: &[u8], target: Option<u32>| -> usize {
        pad4(bin, 0);
        let offset = bin.len();
        bin.extend_from_slice(data);
        let target_str = target
            .map(|t| format!(",\"target\":{t}"))
            .unwrap_or_default();
        buffer_views.push(format!(
            "{{\"buffer\":0,\"byteOffset\":{offset},\"byteLength\":{}{target_str}}}",
            data.len()
        ));
        buffer_views.len() - 1
    };

    let mut add_accessor = |accessor_type: &str,
                            component_type: u32,
                            count: usize,
                            view: usize,
                            minmax: Option<(String, String)>|
     -> usize {
        let mm = match minmax {
            Some((mn, mx)) => format!(",\"min\":{mn},\"max\":{mx}"),
            None => String::new(),
        };
        accessors.push(format!(
            "{{\"bufferView\":{view},\"componentType\":{component_type},\"count\":{count},\"type\":\"{accessor_type}\"{mm}}}"
        ));
        accessors.len() - 1
    };

    let mut image_views: Vec<usize> = Vec::new();
    for tex in textures {
        let view = add_buffer_view(&mut bin, &tex.png, None);
        image_views.push(view);
    }

    let mut prim_json: Vec<String> = Vec::new();
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

    for prim in primitives {
        // POSITION
        let pos_bytes: &[u8] = unsafe {
            std::slice::from_raw_parts(
                prim.positions.as_ptr() as *const u8,
                prim.positions.len() * 12,
            )
        };
        let view = add_buffer_view(&mut bin, pos_bytes, Some(34962));
        let (mut mn, mut mx) = ([f32::MAX; 3], [f32::MIN; 3]);
        for p in &prim.positions {
            for k in 0..3 {
                mn[k] = mn[k].min(p[k]);
                mx[k] = mx[k].max(p[k]);
            }
        }
        let min_s = format!("[{},{},{}]", mn[0], mn[1], mn[2]);
        let max_s = format!("[{},{},{}]", mx[0], mx[1], mx[2]);
        let pos_acc = add_accessor(
            "VEC3",
            5126,
            prim.positions.len(),
            view,
            Some((min_s, max_s)),
        );

        // NORMAL
        let nrm_bytes: &[u8] = unsafe {
            std::slice::from_raw_parts(prim.normals.as_ptr() as *const u8, prim.normals.len() * 12)
        };
        let view = add_buffer_view(&mut bin, nrm_bytes, Some(34962));
        let nrm_acc = add_accessor("VEC3", 5126, prim.normals.len(), view, None);

        // TEXCOORD_0
        let uv_bytes: &[u8] = unsafe {
            std::slice::from_raw_parts(prim.uvs.as_ptr() as *const u8, prim.uvs.len() * 8)
        };
        let view = add_buffer_view(&mut bin, uv_bytes, Some(34962));
        let uv_acc = add_accessor("VEC2", 5126, prim.uvs.len(), view, None);

        // INDICES
        let idx_bytes: &[u8] = unsafe {
            std::slice::from_raw_parts(prim.indices.as_ptr() as *const u8, prim.indices.len() * 4)
        };
        let view = add_buffer_view(&mut bin, idx_bytes, Some(34963));
        let idx_acc = add_accessor("SCALAR", 5125, prim.indices.len(), view, None);

        let material = prim.texture.map(|t| t + 1).unwrap_or(0);
        prim_json.push(format!(
            "{{\"attributes\":{{\"POSITION\":{pos_acc},\"NORMAL\":{nrm_acc},\"TEXCOORD_0\":{uv_acc}}},\"indices\":{idx_acc},\"material\":{material},\"mode\":4}}"
        ));
    }

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
\"scene\":0,\"scenes\":[{{\"nodes\":[0]}}],\
\"nodes\":[{{\"mesh\":0,\"name\":\"{}\"}}],\
\"meshes\":[{{\"name\":\"{}\",\"primitives\":[{}]}}],\
\"materials\":[{}],\
\"textures\":[{}],\
\"images\":[{}],\
\"samplers\":[{{\"magFilter\":9728,\"minFilter\":9728,\"wrapS\":10497,\"wrapT\":10497}}],\
\"accessors\":[{}],\
\"bufferViews\":[{}],\
\"buffers\":[{{\"byteLength\":{}}}]}}",
        json_escape(mesh_name),
        json_escape(mesh_name),
        prim_json.join(","),
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
        let glb = write_glb("tri", &[prim], &[]);
        assert_eq!(&glb[0..4], b"glTF");
        let total = u32::from_le_bytes([glb[8], glb[9], glb[10], glb[11]]) as usize;
        assert_eq!(total, glb.len());
    }
}
