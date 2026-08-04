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
//!
//! Stored UVs are NOT final: point 0 is absolute, points 1 and 2 are deltas,
//! and coordinates from point 4 up are ignored in the data. Final per-point
//! UVs are derived the DFU way (Arch3dFile.UVunpack + WritePlane delta
//! accumulation + FaceUVTool.ComputeFaceUVCoordinates) in `fix_plane_uvs`.

use crate::bsa::BsaArchive;
use crate::Cursor;
use std::path::Path;

#[derive(Debug, Clone, Copy)]
pub struct MeshPoint {
    pub x: i32,
    pub y: i32,
    pub z: i32,
    /// Final UV in 1/16 texel sub-units (after `fix_plane_uvs`).
    pub u: i32,
    pub v: i32,
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
        Ok(Arch3dFile {
            bsa: BsaArchive::load(path)?,
        })
    }

    pub fn has_model(&self, model_id: &str) -> bool {
        self.bsa.contains(model_id)
    }

    pub fn mesh(&self, model_id: &str) -> Result<Mesh, String> {
        let data = self
            .bsa
            .get(model_id)
            .ok_or_else(|| format!("model {model_id} not in ARCH3D.BSA"))?;
        // DFU gates UVunpack on recordId < 905; non-numeric names never match.
        let record_id = model_id.parse::<u32>().unwrap_or(u32::MAX);
        parse_mesh(data, record_id)
    }
}

/// DFU Arch3dFile.UVunpack: unpack special texture coordinates.
/// A packed coordinate is outside the -14335..14335 range (-7168 is the only
/// known exception).
fn uv_unpack(u: i32) -> i32 {
    if u > -14336 && u < 14336 && u != -7168 {
        return u;
    }
    // Nearest multiple of 8192.
    let next_mult = (((u - 1) >> 13) + 1) << 13;
    let prev_mult = next_mult - 8192;
    let mult = if u - prev_mult < next_mult - u {
        prev_mult
    } else {
        next_mult
    };
    u - mult
}

/// DFU FaceUVTool.ComputeFaceUVCoordinates (Dave Humphrey's DF_3DSTex.CPP).
/// Points 0..2 get their cumulative absolute UVs; points 3+ are computed with
/// the affine UV = A*x + B*y + D solved from the first three points projected
/// into the plane's 2D basis. Returns false on a singular matrix (DFU then
/// keeps the raw stored UVs).
fn compute_face_uv(points: &mut [MeshPoint]) -> bool {
    let coord = |i: usize| {
        [
            points[i].x as f32,
            points[i].y as f32,
            points[i].z as f32,
        ]
    };
    let dot = |a: [f32; 3], b: [f32; 3]| a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
    let sub = |a: [f32; 3], b: [f32; 3]| [a[0] - b[0], a[1] - b[1], a[2] - b[2]];
    let norm = |a: [f32; 3]| {
        let l = dot(a, a).sqrt();
        [a[0] / l, a[1] / l, a[2] / l]
    };

    let (p0, p1, p2) = (coord(0), coord(1), coord(2));
    let v0 = norm(sub(p1, p0));
    let v1_raw = sub(p2, p0);
    // Orthogonalize V1 against V0, then normalize.
    let d = dot(v1_raw, v0) / dot(v0, v0);
    let v1 = norm([
        v1_raw[0] - v0[0] * d,
        v1_raw[1] - v0[1] * d,
        v1_raw[2] - v0[2] * d,
    ]);

    // Project the first three points into the 2D face basis (C# (Int32) cast
    // truncates toward zero, as does Rust's `as i32`).
    let proj = |i: usize| {
        let p = coord(i);
        (dot(p, v0) as i32 as f32, dot(p, v1) as i32 as f32)
    };
    let (x0, y0) = proj(0);
    let (x1, y1) = proj(1);
    let (x2, y2) = proj(2);

    // Cumulative absolute UVs of the first three points (stored UVs are
    // absolute at point 0 and deltas at points 1 and 2).
    let us = [
        points[0].u as f32,
        (points[0].u + points[1].u) as f32,
        (points[0].u + points[1].u + points[2].u) as f32,
    ];
    let vs = [
        points[0].v as f32,
        (points[0].v + points[1].v) as f32,
        (points[0].v + points[1].v + points[2].v) as f32,
    ];

    // l_ComputeDFUVMatrixXY: solve U = UA*x + UB*y + UD (and V likewise).
    let determinant =
        x0 * y1 + y0 * x2 + x1 * y2 - y1 * x2 - y0 * x1 - x0 * y2;
    if determinant == 0.0 {
        return false;
    }
    let xi = [
        (y1 - y2) / determinant,
        (-x1 + x2) / determinant,
        (x1 * y2 - x2 * y1) / determinant,
    ];
    let yi = [
        (-y0 + y2) / determinant,
        (x0 - x2) / determinant,
        (-x0 * y2 + x2 * y0) / determinant,
    ];
    let zi = [
        (y0 - y1) / determinant,
        (-x0 + x1) / determinant,
        (x0 * y1 - x1 * y0) / determinant,
    ];
    let ua = us[0] * xi[0] + us[1] * yi[0] + us[2] * zi[0];
    let ub = us[0] * xi[1] + us[1] * yi[1] + us[2] * zi[1];
    let ud = us[0] * xi[2] + us[1] * yi[2] + us[2] * zi[2];
    let va = vs[0] * xi[0] + vs[1] * yi[0] + vs[2] * zi[0];
    let vb = vs[0] * xi[1] + vs[1] * yi[1] + vs[2] * zi[1];
    let vd = vs[0] * xi[2] + vs[1] * yi[2] + vs[2] * zi[2];

    // Points 0..2: cumulative absolute UVs. Points 3+: matrix-generated.
    let (u0, u1, u2) = (us[0] as i32, us[1] as i32, us[2] as i32);
    let (w0, w1, w2) = (vs[0] as i32, vs[1] as i32, vs[2] as i32);
    points[0].u = u0;
    points[0].v = w0;
    points[1].u = u1;
    points[1].v = w1;
    points[2].u = u2;
    points[2].v = w2;
    for point in points.iter_mut().skip(3) {
        let (px, py) = {
            let p = [point.x as f32, point.y as f32, point.z as f32];
            (dot(p, v0) as i32 as f32, dot(p, v1) as i32 as f32)
        };
        point.u = (px * ua + py * ub + ud) as i32;
        point.v = (px * va + py * vb + vd) as i32;
    }
    true
}

/// Apply DFU's UV corrections to one plane in place.
fn fix_plane_uvs(points: &mut [MeshPoint], record_id: u32) {
    // DFU Arch3dFile: UVunpack applies to the first 3 points of models whose
    // id is below 905.
    if record_id < 905 {
        for p in points.iter_mut().take(3) {
            p.u = uv_unpack(p.u);
            p.v = uv_unpack(p.v);
        }
    }
    if points.len() > 3 {
        // N-point plane: FaceUVTool path. On a singular matrix DFU keeps the
        // raw stored UVs (same failure tolerance as classic).
        compute_face_uv(points);
    } else if points.len() == 3 {
        // Triangle: points 1 and 2 are deltas added to the previous point.
        points[1].u += points[0].u;
        points[1].v += points[0].v;
        points[2].u += points[1].u;
        points[2].v += points[1].v;
    }
}

pub fn parse_mesh(data: &[u8], record_id: u32) -> Result<Mesh, String> {
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
            let u = pc.i16() as i32;
            let v = pc.i16() as i32;
            let ppos = point_list_offset
                + if is_v25 {
                    point_offset * 3
                } else {
                    point_offset
                };
            let mut vc = Cursor::at(data, ppos);
            let x = vc.i32();
            let y = vc.i32();
            let z = vc.i32();
            plane.points.push(MeshPoint { x, y, z, u, v });
            q += 8;
        }
        fix_plane_uvs(&mut plane.points, record_id);
        mesh.planes.push(plane);
        p = q;
    }
    Ok(mesh)
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::arena2_dir;

    #[test]
    fn mesh_61000_layout() {
        let dir = arena2_dir();
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

    #[test]
    fn uv_unpack_matches_dfu() {
        // In-range coordinates are untouched.
        assert_eq!(uv_unpack(0), 0);
        assert_eq!(uv_unpack(1024), 1024);
        assert_eq!(uv_unpack(14335), 14335);
        assert_eq!(uv_unpack(-14335), -14335);
        // -7168 is the known exception: in range but still a packed
        // coordinate, so it unwraps (to 1024) like any other packed value.
        assert_eq!(uv_unpack(-7168), 1024);
        // Packed coordinates unwrap by the nearest multiple of 8192.
        assert_eq!(uv_unpack(14336), -2048);
        assert_eq!(uv_unpack(-14336), 2048);
        assert_eq!(uv_unpack(8192 * 2 + 100), 100);
    }

    fn point(x: i32, y: i32, z: i32, u: i32, v: i32) -> MeshPoint {
        MeshPoint { x, y, z, u, v }
    }

    #[test]
    fn triangle_uvs_accumulate_deltas() {
        let mut pts = vec![
            point(0, 0, 0, 100, 50),
            point(256, 0, 0, 20, 5),
            point(256, 256, 0, 3, 2),
        ];
        fix_plane_uvs(&mut pts, 61000);
        let uvs: Vec<(i32, i32)> = pts.iter().map(|p| (p.u, p.v)).collect();
        assert_eq!(uvs, [(100, 50), (120, 55), (123, 57)]);
    }

    #[test]
    fn quad_uvs_use_face_matrix_for_fourth_point() {
        // Unit square in the XY plane; stored UVs are absolute at point 0,
        // deltas at points 1 and 2, garbage at point 3 (ignored in the data).
        let mut pts = vec![
            point(0, 0, 0, 0, 0),
            point(256, 0, 0, 1024, 0),
            point(256, 256, 0, 0, 1024),
            point(0, 256, 0, 9999, 9999),
        ];
        fix_plane_uvs(&mut pts, 61000);
        let uvs: Vec<(i32, i32)> = pts.iter().map(|p| (p.u, p.v)).collect();
        assert_eq!(uvs, [(0, 0), (1024, 0), (1024, 1024), (0, 1024)]);
    }

    #[test]
    fn uv_unpack_only_for_records_below_905() {
        let packed = || point(0, 0, 0, 14336, -14336);
        let mut pts = vec![packed(), point(256, 0, 0, 0, 0), point(256, 256, 0, 0, 0)];
        fix_plane_uvs(&mut pts, 61000);
        assert_eq!((pts[0].u, pts[0].v), (14336, -14336), "no unpack at id >= 905");
        let mut pts = vec![packed(), point(256, 0, 0, 0, 0), point(256, 256, 0, 0, 0)];
        fix_plane_uvs(&mut pts, 42);
        assert_eq!((pts[0].u, pts[0].v), (-2048, 2048), "unpacked at id < 905");
    }

    #[test]
    fn singular_face_keeps_raw_uvs() {
        // Collinear points: no valid UV solution; DFU keeps raw stored UVs.
        let mut pts = vec![
            point(0, 0, 0, 1, 2),
            point(256, 0, 0, 3, 4),
            point(512, 0, 0, 5, 6),
            point(768, 0, 0, 7, 8),
        ];
        assert!(!compute_face_uv(&mut pts));
        let uvs: Vec<(i32, i32)> = pts.iter().map(|p| (p.u, p.v)).collect();
        assert_eq!(uvs, [(1, 2), (3, 4), (5, 6), (7, 8)]);
    }
}
