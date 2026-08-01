//! ARCH3D.BSA mesh parser (DFU Arch3dFile.cs).
//!
//! Record header: 4B version cstring ("v2.5"/"v2.6"/"v2.7"), i32 pointCount,
//! i32 planeCount, u32 radius, u64 null, i32 planeDataOffset, i32 objectDataOffset,
//! i32 objectDataCount, u32 unk, u64 null, i32 pointListOffset (@48),
//! i32 normalListOffset (@52), u32 unk, i32 planeListOffset (@60).
//! Plane: u8 pointCount, u8 unk, u16 textureBitfield, u32 unk; then per point
//! { i32 pointOffset, i16 u, i16 v }. Point coords: 3 x i32 at
//! pointListOffset + pointOffset (v2.6/v2.7) or pointListOffset + pointOffset*3 (v2.5).
//! textureArchive = bitfield >> 7, textureRecord = bitfield & 0x7F.

use crate::bsa::BsaArchive;
use crate::Cursor;
use std::path::Path;

#[derive(Debug, Clone, Copy)]
pub struct MeshPoint {
    pub x: i32,
    pub y: i32,
    pub z: i32,
    pub u: i16,
    pub v: i16,
}

#[derive(Debug, Clone)]
pub struct MeshPlane {
    pub texture_archive: u16,
    pub texture_record: u16,
    pub points: Vec<MeshPoint>,
}

#[derive(Debug, Default)]
pub struct Mesh {
    pub planes: Vec<MeshPlane>,
}

pub struct Arch3dFile {
    bsa: BsaArchive,
}

impl Arch3dFile {
    pub fn load(path: &Path) -> std::io::Result<Self> {
        Ok(Arch3dFile { bsa: BsaArchive::load(path)? })
    }

    pub fn has_model(&self, model_id: &str) -> bool {
        self.bsa.contains(model_id)
    }

    pub fn mesh(&self, model_id: &str) -> Result<Mesh, String> {
        let data = self
            .bsa
            .get(model_id)
            .ok_or_else(|| format!("model {model_id} not in ARCH3D.BSA"))?;
        parse_mesh(data)
    }
}

pub fn parse_mesh(data: &[u8]) -> Result<Mesh, String> {
    if data.len() < 64 {
        return Err(format!("ARCH3D record too small: {} bytes", data.len()));
    }
    let mut c = Cursor::new(data);
    let version = c.cstring(4);
    let _point_count = c.i32();
    let plane_count = c.i32() as usize;
    let _radius = c.u32();
    let _null1 = c.u64();
    let _plane_data_offset = c.i32();
    let _object_data_offset = c.i32();
    let _object_data_count = c.i32();
    let _unk2 = c.u32();
    let _null2 = c.u64();
    let point_list_offset = c.i32() as usize;
    let _normal_list_offset = c.i32() as usize;
    let _unk3 = c.u32();
    let plane_list_offset = c.i32() as usize;

    let is_v25 = version == "v2.5";
    let mut mesh = Mesh::default();
    let mut p = plane_list_offset;
    for _ in 0..plane_count {
        if p + 8 > data.len() {
            return Err(format!("plane header at {p} out of bounds"));
        }
        let mut h = Cursor::at(data, p);
        let point_count = h.u8() as usize;
        let _unk1 = h.u8();
        let texture_bitfield = h.u16();
        let _unk2 = h.u32();
        let mut plane = MeshPlane {
            texture_archive: texture_bitfield >> 7,
            texture_record: texture_bitfield & 0x7F,
            points: Vec::with_capacity(point_count),
        };
        let mut q = p + 8;
        for _ in 0..point_count {
            let mut pc = Cursor::at(data, q);
            let point_offset = pc.i32() as usize;
            let u = pc.i16();
            let v = pc.i16();
            let ppos = point_list_offset + if is_v25 { point_offset * 3 } else { point_offset };
            let mut vc = Cursor::at(data, ppos);
            let x = vc.i32();
            let y = vc.i32();
            let z = vc.i32();
            plane.points.push(MeshPoint { x, y, z, u, v });
            q += 8;
        }
        mesh.planes.push(plane);
        p = q;
    }
    Ok(mesh)
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::path::PathBuf;

    #[test]
    fn mesh_61000_layout() {
        let dir = PathBuf::from(
            std::env::var("ARENA2_DIR").unwrap_or_else(|_| "/home/research/daggerfall-files".into()),
        );
        let arch = Arch3dFile::load(&dir.join("ARCH3D.BSA")).unwrap();
        let mesh = arch.mesh("61000").unwrap();
        assert_eq!(mesh.planes.len(), 22);
        // Max |coord| verified against the binary record
        let max = mesh
            .planes
            .iter()
            .flat_map(|p| p.points.iter())
            .fold(0, |m, p| m.max(p.x.abs()).max(p.y.abs()).max(p.z.abs()));
        assert_eq!(max, 32768);
    }
}
