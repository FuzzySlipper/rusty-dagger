//! Daggerfall-side directional sprite authority,
//! mapping a camera pose to each enemy's orientation frame. The semantics
//! live in `arena2::mobile` (DFU port); this module owns the runtime-space
//! conversion so product presentation applies frames without re-implementing
//! the math. Camera-facing itself is the renderer's billboard-mode job.

/// Evaluate one enemy against a camera position, returning the 8-sector
/// orientation frame (DFU DaggerfallMobileUnit record order). Positions are
/// glTF world space; converted to DFU/Unity space (z negated) internally.
/// `heading` is the actor yaw in glTF space, where zero faces glTF -Z.
pub fn evaluate_directional(enemy: [f32; 3], heading: f32, camera: [f32; 3]) -> usize {
    arena2::mobile::orientation_index(
        [enemy[0], enemy[1], -enemy[2]],
        [heading.sin(), 0.0, heading.cos()],
        [camera[0], camera[1], -camera[2]],
    )
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn poses_map_to_dfu_frames() {
        let enemy = [10.0, 33.0, -7.0];
        // Camera in front (Unity +z side == glTF -z side): front frame 0.
        assert_eq!(evaluate_directional(enemy, 0.0, [10.5, 34.4, -11.0]), 0);
        // Camera behind (glTF +z): back frame 4.
        assert_eq!(evaluate_directional(enemy, 0.0, [10.5, 34.4, -3.0]), 4);
        // Orbit sequence around the enemy visits the sectors in DFU order
        // (descending indices for positive bearings).
        let frames: Vec<usize> = (0..8)
            .map(|k| {
                let theta = (1.0f32 + 45.0 * k as f32).to_radians();
                // Unity orbit offset -> glTF (z negated).
                evaluate_directional(
                    enemy,
                    0.0,
                    [
                        enemy[0] + 4.0 * theta.sin(),
                        enemy[1] + 1.4,
                        enemy[2] - 4.0 * theta.cos(),
                    ],
                )
            })
            .collect();
        assert_eq!(frames, [0, 7, 6, 5, 4, 3, 2, 1]);
    }

    #[test]
    fn actor_heading_rotates_directional_sectors() {
        let enemy = [0.0, 0.0, 0.0];
        let camera_on_gltf_negative_z = [0.0, 1.0, -4.0];
        assert_eq!(
            evaluate_directional(enemy, 0.0, camera_on_gltf_negative_z),
            0
        );
        assert_eq!(
            evaluate_directional(enemy, std::f32::consts::PI, camera_on_gltf_negative_z,),
            4
        );
    }
}
