//! Mobile enemy reference data and directional-sprite semantics.
//!
//! Ports (DFU, MIT): EnemyBasics.cs MobileEnemy entries (subset, the enemies
//! present in classic dungeons we load), EnemyBasics MobileAnimation tables
//! (Move/Idle record layout + left-right mirroring), and
//! DaggerfallMobileUnit.UpdateOrientation's 8-sector facing math.
//!
//! Record layout in the texture archive (DFU EnemyBasics): Move state uses
//! records 0..4, Idle uses 15..19; orientations 5..7 reuse the mirrored side
//! records with FlipLeftRight. Sprites are view-only (frame 0); animation
//! playback and gameplay states are out of scope.

use crate::GLOBAL_SCALE;

/// DFU BlocksFile.ScaleDivisor — texture record scale factors divide by 256.
pub const SCALE_DIVISOR: f32 = 256.0;

/// A classic enemy type (DFU EnemyBasics.Enemies subset). Monsters are ids
/// 0-42 (MONSTER.BSA), humanoid mobile types 128-146.
#[derive(Debug, Clone, Copy)]
pub struct MobileType {
    pub id: u8,
    pub name: &'static str,
    /// Male texture archive. DFU randomizes gender for humans
    /// (GetTextureArchive); we use the male texture deterministically.
    pub texture_archive: u16,
    pub has_idle: bool,
    pub flying: bool,
}

/// The enemies present in Privateer's Hold (extend as new dungeons need).
/// DFU EnemyBasics entries: Rat(255), Imp(256), GiantBat(258),
/// GrizzlyBear(259), Orc(262), SkeletalWarrior(270), Thief(484M/483F),
/// Archer(482M/481F).
pub const MOBILE_TYPES: &[MobileType] = &[
    MobileType {
        id: 0,
        name: "Rat",
        texture_archive: 255,
        has_idle: true,
        flying: false,
    },
    MobileType {
        id: 1,
        name: "Imp",
        texture_archive: 256,
        has_idle: false,
        flying: true,
    },
    MobileType {
        id: 3,
        name: "GiantBat",
        texture_archive: 258,
        has_idle: false,
        flying: true,
    },
    MobileType {
        id: 4,
        name: "GrizzlyBear",
        texture_archive: 259,
        has_idle: true,
        flying: false,
    },
    MobileType {
        id: 7,
        name: "Orc",
        texture_archive: 262,
        has_idle: true,
        flying: false,
    },
    MobileType {
        id: 15,
        name: "SkeletalWarrior",
        texture_archive: 270,
        has_idle: true,
        flying: false,
    },
    MobileType {
        id: 138,
        name: "Thief",
        texture_archive: 484,
        has_idle: true,
        flying: false,
    },
    MobileType {
        id: 141,
        name: "Archer",
        texture_archive: 482,
        has_idle: true,
        flying: false,
    },
];

pub fn mobile_type(id: u8) -> Option<&'static MobileType> {
    MOBILE_TYPES.iter().find(|t| t.id == id)
}

/// One orientation's animation record + mirroring (DFU MobileAnimation).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct OrientationAnim {
    pub record: u16,
    pub flip: bool,
}

/// DFU EnemyBasics.MoveAnims (records 0-4, mirrored sides at 5-7).
pub const MOVE_ANIMS: [OrientationAnim; 8] = anims([0, 1, 2, 3, 4], false);
/// DFU EnemyBasics.IdleAnims (records 15-19, mirrored sides at 5-7).
pub const IDLE_ANIMS: [OrientationAnim; 8] = anims([15, 16, 17, 18, 19], false);
/// DFU EnemyBasics.RatIdleAnims — same records, opposite mirroring.
pub const RAT_IDLE_ANIMS: [OrientationAnim; 8] = anims([15, 16, 17, 18, 19], true);

const fn anims(base: [u16; 5], flip_sides: bool) -> [OrientationAnim; 8] {
    [
        OrientationAnim {
            record: base[0],
            flip: false,
        },
        OrientationAnim {
            record: base[1],
            flip: flip_sides,
        },
        OrientationAnim {
            record: base[2],
            flip: flip_sides,
        },
        OrientationAnim {
            record: base[3],
            flip: flip_sides,
        },
        OrientationAnim {
            record: base[4],
            flip: false,
        },
        OrientationAnim {
            record: base[3],
            flip: !flip_sides,
        },
        OrientationAnim {
            record: base[2],
            flip: !flip_sides,
        },
        OrientationAnim {
            record: base[1],
            flip: !flip_sides,
        },
    ]
}

/// The anim set an enemy uses when standing (DFU: Idle where the enemy has
/// one, Move doubles as idle for flying/swimming/no-idle enemies).
pub fn standing_anims(mobile: &MobileType) -> &'static [OrientationAnim; 8] {
    if mobile.id == 0 {
        &RAT_IDLE_ANIMS
    } else if mobile.has_idle {
        &IDLE_ANIMS
    } else {
        &MOVE_ANIMS
    }
}

/// DFU billboard record size: (size + size * scale / 256) * GlobalScale.
pub fn record_world_size(width: i16, height: i16, scale_x: i16, scale_y: i16) -> [f32; 2] {
    let w = width as f32 * (1.0 + scale_x as f32 / SCALE_DIVISOR);
    let h = height as f32 * (1.0 + scale_y as f32 / SCALE_DIVISOR);
    [w * GLOBAL_SCALE, h * GLOBAL_SCALE]
}

/// DFU DaggerfallMobileUnit.UpdateOrientation: 8-sector orientation index
/// from the signed angle between the camera→enemy bearing and the enemy's
/// facing. All inputs are in DFU/Unity space (left-handed, Y-up; convert
/// from our glTF space with (x, y, -z)). `facing` is the enemy's forward
/// (identity-facing dungeon enemies face Unity +z = [0,0,1]).
pub fn orientation_index(enemy: [f32; 3], facing: [f32; 3], camera: [f32; 3]) -> usize {
    let dir = [camera[0] - enemy[0], camera[2] - enemy[2]];
    let len = (dir[0] * dir[0] + dir[1] * dir[1]).sqrt();
    if len == 0.0 {
        return 0;
    }
    let dir = [dir[0] / len, dir[1] / len];
    let fwd = [facing[0], facing[2]];
    // Vector3.Angle: unsigned 0..180 degrees.
    let cos = (dir[0] * fwd[0] + dir[1] * fwd[1]).clamp(-1.0, 1.0);
    let angle = cos.acos().to_degrees();
    // Signed via cross(dir, fwd).y = dir.z*fwd.x - dir.x*fwd.z
    let cross_y = dir[1] * fwd[0] - dir[0] * fwd[1];
    let signed = angle * -cross_y.signum();
    (-(signed / 45.0).round() as i32).rem_euclid(8) as usize
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn known_enemies_resolve() {
        assert_eq!(mobile_type(0).unwrap().texture_archive, 255);
        assert_eq!(mobile_type(15).unwrap().texture_archive, 270);
        assert_eq!(mobile_type(138).unwrap().name, "Thief");
        assert_eq!(mobile_type(141).unwrap().texture_archive, 482);
        assert!(mobile_type(42).is_none(), "not needed for Privateer's Hold");
    }

    #[test]
    fn anim_tables_match_dfu_layout() {
        assert_eq!(MOVE_ANIMS[0].record, 0);
        assert_eq!(MOVE_ANIMS[4].record, 4);
        assert_eq!(
            MOVE_ANIMS[5],
            OrientationAnim {
                record: 3,
                flip: true
            }
        );
        assert_eq!(
            MOVE_ANIMS[7],
            OrientationAnim {
                record: 1,
                flip: true
            }
        );
        assert_eq!(IDLE_ANIMS[0].record, 15);
        assert_eq!(
            IDLE_ANIMS[6],
            OrientationAnim {
                record: 17,
                flip: true
            }
        );
        // Rat idles mirror the opposite side.
        assert_eq!(
            RAT_IDLE_ANIMS[1],
            OrientationAnim {
                record: 16,
                flip: true
            }
        );
        assert_eq!(
            RAT_IDLE_ANIMS[6],
            OrientationAnim {
                record: 17,
                flip: false
            }
        );
        assert_eq!(standing_anims(&MOBILE_TYPES[0])[1].record, 16);
        assert_eq!(standing_anims(&MOBILE_TYPES[1])[1].record, 1); // Imp: no idle -> Move
    }

    #[test]
    fn record_world_size_applies_dfu_scale() {
        // 64x64 texels, no scale: 64 * 0.025 = 1.6m.
        assert_eq!(record_world_size(64, 64, 0, 0), [1.6, 1.6]);
        // scale 256 doubles the axis.
        assert_eq!(record_world_size(64, 64, 256, 0), [3.2, 1.6]);
    }

    #[test]
    fn orientation_sectors_match_dfu() {
        let enemy = [0.0, 0.0, 0.0];
        let facing = [0.0, 0.0, 1.0]; // Unity +z
                                      // Camera dead ahead (Unity +z of enemy): front, orientation 0.
        assert_eq!(orientation_index(enemy, facing, [0.0, 0.0, 10.0]), 0);
        // Camera behind (Unity -z): back, orientation 4.
        assert_eq!(orientation_index(enemy, facing, [0.0, 0.0, -10.0]), 4);
        // 45-degree sectors with rounding: just inside the next sector.
        let r = 10.0f32;
        let to = |deg: f32| {
            let a = deg.to_radians();
            [r * a.sin(), 0.0, r * a.cos()]
        };
        assert_eq!(orientation_index(enemy, facing, to(22.0)), 0);
        // DFU maps positive camera bearings to descending indices (the
        // -RoundToInt in UpdateOrientation): +23° -> 7, +90° -> 6, -90° -> 2.
        assert_eq!(orientation_index(enemy, facing, to(23.0)), 7);
        assert_eq!(orientation_index(enemy, facing, to(90.0)), 6);
        assert_eq!(orientation_index(enemy, facing, to(-90.0)), 2);
        assert_eq!(orientation_index(enemy, facing, to(135.0)), 5);
        assert_eq!(orientation_index(enemy, facing, to(-135.0)), 3);
        // Wraparound at 180/-180 stays the back sector.
        assert_eq!(orientation_index(enemy, facing, to(179.0)), 4);
        assert_eq!(orientation_index(enemy, facing, to(-179.0)), 4);
    }
}
