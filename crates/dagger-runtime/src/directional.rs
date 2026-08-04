//! Directional sprite evaluation (task 6595): the Daggerfall-side authority
//! mapping a camera pose to each enemy's orientation frame and camera-facing
//! rotation. The semantics live in `arena2::mobile` (DFU port); this module
//! owns the runtime-space conversion and the assignment shape so consumers
//! (the engine-render-check driver today, a live `dagger-world` loop later)
//! apply assignments and never re-implement the math.
//!
//! The evaluation is a naive per-pose poll of every directional sprite —
//! documented stopgap until the engine exposes a renderer-visibility query
//! (rusty-engine task for the capability is linked from the Den thread).

/// A directional sprite assignment for one enemy under one camera pose.
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct DirectionalAssignment {
    /// 8-sector orientation frame (DFU DaggerfallMobileUnit record order).
    pub frame: usize,
    /// Y-rotation quaternion facing the camera cylindrically (glTF space).
    /// Applied to the sprite's parent node because renderer-three does not
    /// implement billboard modes yet (rusty-engine 6630).
    pub rotation: [f32; 4],
}

/// Evaluate one identity-facing enemy (DFU spawns RDB enemies unrotated,
/// facing Unity +z) against a camera position. Positions are glTF world
/// space; converted to DFU/Unity space (z negated) internally.
pub fn evaluate_directional(enemy: [f32; 3], camera: [f32; 3]) -> DirectionalAssignment {
    let frame = arena2::mobile::orientation_index(
        [enemy[0], enemy[1], -enemy[2]],
        [0.0, 0.0, 1.0],
        [camera[0], camera[1], -camera[2]],
    );
    // Face the camera cylindrically: the sprite plane looks down +z, so yaw
    // about Y by atan2(dx, dz) in glTF right-handed space.
    let yaw = (camera[0] - enemy[0]).atan2(camera[2] - enemy[2]);
    let half = yaw / 2.0;
    DirectionalAssignment {
        frame,
        rotation: [0.0, half.sin(), 0.0, half.cos()],
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn front_and_back_poses_map_to_dfu_frames() {
        let enemy = [10.0, 33.0, -7.0];
        // Camera in front (Unity +z side == glTF -z side): front frame 0.
        let front = evaluate_directional(enemy, [10.5, 34.4, -11.0]);
        assert_eq!(front.frame, 0);
        // Camera behind (glTF +z): back frame 4.
        let back = evaluate_directional(enemy, [10.5, 34.4, -3.0]);
        assert_eq!(back.frame, 4);
        // The sprite's +z must point at the camera: camera in front
        // (glTF -z of the enemy) means yaw ~180 degrees.
        let yaw = front.rotation[1].atan2(front.rotation[3]) * 2.0;
        assert!((yaw.to_degrees() - 180.0).abs() < 8.0, "yaw {}", yaw.to_degrees());
        let yaw = back.rotation[1].atan2(back.rotation[3]) * 2.0;
        assert!(yaw.to_degrees().abs() < 8.0, "yaw {}", yaw.to_degrees());
    }
}
