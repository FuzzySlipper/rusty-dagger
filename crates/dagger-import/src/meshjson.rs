//! rusty-engine authored mesh source (.mesh.json) writer.
//! Engine schema (asset-import/source.rs): schemaVersion 1, positions/normals,
//! optional uvs (one f32 pair per vertex), indices,
//! materials[{slot,name,color,texture?}], groups[{materialSlot,start,count}], collision.
//! With `--format mesh-json` the dungeon is textured: every material references
//! the classic texture decoded by the extractor (`--texture-dir` publishes the
//! PNG bytes the studio host serves as content-addressed render resources).
//! `--untextured` keeps the legacy flat average-color fallback for A/B mood
//! comparison.

use crate::glb::{texture_slug, PrimitiveInput, TextureInput};

fn fmt_f32(v: f32) -> String {
    // Compact but round-trippable float formatting
    if v == v.trunc() && v.abs() < 1e7 {
        format!("{v:.1}")
    } else {
        format!("{v:.6}")
    }
}

pub struct MeshJsonOutput {
    pub json: String,
    /// Unique slugs of referenced textures (for --texture-dir summary).
    pub referenced: Vec<String>,
}

pub fn write_mesh_json(
    mesh_name: &str,
    primitives: &[PrimitiveInput],
    textures: &[TextureInput],
    textured: bool,
) -> MeshJsonOutput {
    // Merge all primitives into one vertex/index buffer with one group per primitive.
    // Material slot = texture index + 1 (0 = default untextured).
    let mut positions = String::with_capacity(1 << 20);
    let mut normals = String::with_capacity(1 << 20);
    let mut uvs = String::with_capacity(1 << 19);
    let mut indices = String::with_capacity(1 << 19);
    let mut groups: Vec<String> = Vec::new();
    let mut vert_base = 0u32;
    let mut idx_pos = 0usize;
    let mut n_pos = 0usize;
    let mut n_nrm = 0usize;
    let mut n_uv = 0usize;
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
        for uv in &prim.uvs {
            for k in 0..2 {
                if n_uv > 0 {
                    uvs.push(',');
                }
                uvs.push_str(&fmt_f32(uv[k]));
                n_uv += 1;
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

    // Materials: slot 0 default + one per texture. Textured materials reference
    // the classic texture slug (importer creates texture/<slug> catalog entries);
    // the average color is kept as the material tint/fallback color either way.
    let mut materials: Vec<String> = Vec::new();
    materials.push("{\"slot\":0,\"name\":\"default\",\"color\":[0.72,0.70,0.66,1.0]}".to_string());
    let mut referenced: Vec<String> = Vec::new();
    for (i, tex) in textures.iter().enumerate() {
        let [r, g, b] = tex.avg_color;
        let slug = texture_slug(tex.id);
        let color = fmt_f32(r);
        let g = fmt_f32(g);
        let b = fmt_f32(b);
        if textured {
            referenced.push(slug.clone());
            materials.push(format!(
                "{{\"slot\":{},\"name\":\"{slug}\",\"color\":[{color},{g},{b},1.0],\"texture\":\"{slug}\"}}",
                i + 1
            ));
        } else {
            materials.push(format!(
                "{{\"slot\":{},\"name\":\"{slug}\",\"color\":[{color},{g},{b},1.0]}}",
                i + 1
            ));
        }
    }
    referenced.dedup();

    let json = format!(
        "{{\n  \"schemaVersion\": 1,\n  \"name\": \"{mesh_name}\",\n  \"positions\": [{positions}],\n  \"normals\": [{normals}],\n  \"uvs\": [{uvs}],\n  \"indices\": [{indices}],\n  \"materials\": [{}],\n  \"groups\": [{}],\n  \"collision\": \"trimesh\"\n}}\n",
        materials.join(","),
        groups.join(",")
    );
    MeshJsonOutput { json, referenced }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn sample_prim() -> PrimitiveInput {
        PrimitiveInput {
            name: "tri".into(),
            positions: vec![[0.0, 0.0, 0.0], [1.0, 0.0, 0.0], [0.0, 1.0, 0.0]],
            normals: vec![[0.0, 0.0, 1.0]; 3],
            uvs: vec![[0.0, 0.0], [1.0, 0.0], [0.0, 1.0]],
            indices: vec![0, 1, 2],
            texture: Some(0),
        }
    }

    fn sample_texture() -> TextureInput {
        TextureInput {
            name: "TEXTURE.450[5] (32x128)".into(),
            png: vec![137, 80, 78, 71],
            id: (450, 5),
            avg_color: [0.5, 0.4, 0.3],
        }
    }

    #[test]
    fn emits_engine_schema_shape() {
        let prim = PrimitiveInput {
            texture: None,
            ..sample_prim()
        };
        let out = write_mesh_json("tri", &[prim], &[], false);
        assert!(out.json.contains("\"schemaVersion\": 1"));
        assert!(out.json.contains("\"collision\": \"trimesh\""));
        assert!(out
            .json
            .contains("\"materialSlot\":0,\"start\":0,\"count\":3"));
        assert!(out.referenced.is_empty());
    }

    #[test]
    fn textured_materials_reference_texture_slugs_and_uvs_flow() {
        let out = write_mesh_json("tri", &[sample_prim()], &[sample_texture()], true);
        assert!(out
            .json
            .contains("\"name\":\"texture-450-5\",\"color\":[0.500000,0.400000,0.300000,1.0],\"texture\":\"texture-450-5\""));
        assert!(out.json.contains("\"uvs\": [0.0,0.0,1.0,0.0,0.0,1.0]"));
        assert_eq!(out.referenced, ["texture-450-5"]);
    }

    #[test]
    fn untextured_fallback_drops_texture_reference_but_keeps_uvs() {
        let out = write_mesh_json("tri", &[sample_prim()], &[sample_texture()], false);
        assert!(!out.json.contains("\"texture\":"));
        assert!(out.json.contains("\"uvs\":"));
        assert!(out.referenced.is_empty());
    }
}
