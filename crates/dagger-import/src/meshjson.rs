//! rusty-engine authored mesh source (.mesh.json) writer.
//! Engine schema (asset-import/source.rs): schemaVersion 1, positions/normals/indices,
//! materials[{slot,name,color,texture?}], groups[{materialSlot,start,count}], collision.
//! Note: the engine format carries no UVs — materials are flat colors.

use crate::glb::{PrimitiveInput, TextureInput};

fn fmt_f32(v: f32) -> String {
    // Compact but round-trippable float formatting
    if v == v.trunc() && v.abs() < 1e7 {
        format!("{v:.1}")
    } else {
        format!("{v:.6}")
    }
}

pub fn write_mesh_json(
    mesh_name: &str,
    primitives: &[PrimitiveInput],
    textures: &[TextureInput],
) -> String {
    // Merge all primitives into one vertex/index buffer with one group per primitive.
    // Material slot = texture index + 1 (0 = default untextured).
    let mut positions = String::with_capacity(1 << 20);
    let mut normals = String::with_capacity(1 << 20);
    let mut indices = String::with_capacity(1 << 19);
    let mut groups: Vec<String> = Vec::new();
    let mut vert_base = 0u32;
    let mut idx_pos = 0usize;
    let mut n_pos = 0usize;
    let mut n_nrm = 0usize;
    let mut n_idx = 0usize;

    for prim in primitives {
        for p in &prim.positions {
            for k in 0..3 {
                if n_pos > 0 {
                    positions.push(',');
                }
                positions.push_str(&fmt_f32(p[k]));
                n_pos += 1;
            }
        }
        for n in &prim.normals {
            for k in 0..3 {
                if n_nrm > 0 {
                    normals.push(',');
                }
                normals.push_str(&fmt_f32(n[k]));
                n_nrm += 1;
            }
        }
        for i in &prim.indices {
            if n_idx > 0 {
                indices.push(',');
            }
            indices.push_str(&(vert_base + i).to_string());
            n_idx += 1;
        }
        let slot = prim.texture.map(|t| t + 1).unwrap_or(0);
        groups.push(format!(
            "{{\"materialSlot\":{slot},\"start\":{idx_pos},\"count\":{}}}",
            prim.indices.len()
        ));
        idx_pos += prim.indices.len();
        vert_base += prim.positions.len() as u32;
    }

    // Materials: slot 0 default + one per texture with its average color
    let mut materials: Vec<String> = Vec::new();
    materials.push("{\"slot\":0,\"name\":\"default\",\"color\":[0.72,0.70,0.66,1.0]}".to_string());
    for (i, tex) in textures.iter().enumerate() {
        let [r, g, b] = tex.avg_color;
        // Engine asset ids must be lowercase kebab-case; derive from "TEXTURE.nnn[r]"
        let slug = tex
            .name
            .split(' ')
            .next()
            .unwrap_or("texture")
            .to_lowercase()
            .replace(['.', '[', ']'], "-")
            .trim_matches('-')
            .to_string();
        materials.push(format!(
            "{{\"slot\":{},\"name\":\"{}\",\"color\":[{},{},{},1.0]}}",
            i + 1,
            slug,
            fmt_f32(r),
            fmt_f32(g),
            fmt_f32(b)
        ));
    }

    format!(
        "{{\n  \"schemaVersion\": 1,\n  \"name\": \"{mesh_name}\",\n  \"positions\": [{positions}],\n  \"normals\": [{normals}],\n  \"indices\": [{indices}],\n  \"materials\": [{}],\n  \"groups\": [{}],\n  \"collision\": \"visualOnly\"\n}}\n",
        materials.join(","),
        groups.join(",")
    )
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn emits_engine_schema_shape() {
        let prim = PrimitiveInput {
            name: "tri".into(),
            positions: vec![[0.0, 0.0, 0.0], [1.0, 0.0, 0.0], [0.0, 1.0, 0.0]],
            normals: vec![[0.0, 0.0, 1.0]; 3],
            uvs: vec![[0.0, 0.0]; 3],
            indices: vec![0, 1, 2],
            texture: None,
        };
        let json = write_mesh_json("tri", &[prim], &[]);
        assert!(json.contains("\"schemaVersion\": 1"));
        assert!(json.contains("\"collision\": \"visualOnly\""));
        assert!(json.contains("\"materialSlot\":0,\"start\":0,\"count\":3"));
    }
}
