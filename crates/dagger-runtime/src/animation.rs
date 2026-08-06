//! Sprite animation service (task 6640): a consolidated per-tick evaluator
//! that advances animation frames for all animated sprites in one pass,
//! producing a batched frame diff for the renderer. Designed so offscreen
//! sprites can be throttled or frozen later without changing the shape.
//!
//! Two sprite kinds are handled separately (different dynamics, same pass):
//!
//! - **Env flats** (torches, flames): cycle through a texture record's frames
//!   at DFU's billboard default 5 fps (`ENV_BILLBOARD_FPS`). The frame index
//!   advances linearly and wraps.
//!
//! - **Enemy directional sprites**: the atlas is laid out as orientation ×
//!   anim_frame (8 × M cells). The visible frame = orientation *
//!   anim_frame_count + current_anim_frame. Orientation comes from the camera
//!   (evaluate_directional); anim_frame advances independently from elapsed
//!   time at DFU speed (6fps ground, 10fps flying). This means a direction
//!   change mid-animation preserves the anim frame position — only the
//!   orientation base shifts. All enemies share the same global clock, so
//!   they're synchronized; per-enemy phase offsets arrive with patrol (6641).
//!
//! The service owns elapsed time and per-sprite state. One `evaluate` call
//! per tick produces the complete diff — callers never poll individual
//! sprites.

use crate::evaluate_directional;
use arena2::mobile::{self, ENV_BILLBOARD_FPS, FLY_ANIM_SPEED, MOVE_ANIM_SPEED};

/// A sprite registered with the animation service.
#[derive(Debug, Clone)]
pub struct SpriteEntry {
    /// Renderer sprite handle (entity ID from the project scene).
    pub handle: u32,
    pub kind: SpriteKind,
    /// Last frame emitted to the renderer; None = not yet sent.
    last_frame: Option<u32>,
}

/// What drives a sprite's frame.
#[derive(Debug, Clone)]
pub enum SpriteKind {
    /// Environment flat: cycles `frame_count` frames at `fps`.
    /// Frame index = `(elapsed * fps) % frame_count`.
    Env { frame_count: u32, fps: u32 },
    /// Enemy directional sprite. The atlas is laid out as
    /// orientation × anim_frame (8 × M cells). The visible frame =
    /// orientation * anim_frame_count + current_anim_frame, where
    /// orientation comes from the camera (evaluate_directional) and
    /// anim_frame advances independently from elapsed time at DFU speed.
    /// This means a direction change mid-animation preserves the anim
    /// frame position — only the orientation base shifts.
    Enemy {
        position: [f32; 3],
        mobile_id: u8,
        /// Frames per orientation in the atlas (M). All 8 orientations
        /// carry the same count (DFU move records are uniform per enemy).
        anim_frame_count: u32,
        /// DFU animation speed: 6fps ground, 10fps flying.
        anim_fps: u32,
    },
}

/// One entry in the consolidated per-tick frame diff.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct FrameUpdate {
    pub handle: u32,
    pub frame: u32,
}

/// The consolidated sprite animation authority. Owns elapsed time and
/// per-sprite state; one `evaluate` call per tick produces the complete diff.
pub struct AnimationService {
    entries: Vec<SpriteEntry>,
    elapsed: f32,
}

impl AnimationService {
    pub fn new() -> Self {
        AnimationService {
            entries: Vec::new(),
            elapsed: 0.0,
        }
    }

    /// Register an env flat sprite for animation. Only call for sprites whose
    /// texture record has `frame_count > 1`; single-frame sprites are static
    /// and don't need registration.
    pub fn add_env(&mut self, handle: u32, frame_count: u32) {
        self.entries.push(SpriteEntry {
            handle,
            kind: SpriteKind::Env {
                frame_count,
                fps: ENV_BILLBOARD_FPS,
            },
            last_frame: None,
        });
    }

    /// Register an enemy directional sprite for animation. The atlas must be
    /// laid out as 8 × `anim_frame_count` cells (orientation × anim_frame).
    pub fn add_enemy(
        &mut self,
        handle: u32,
        position: [f32; 3],
        mobile_id: u8,
        anim_frame_count: u32,
    ) {
        let anim_fps = move_fps(mobile_id);
        self.entries.push(SpriteEntry {
            handle,
            kind: SpriteKind::Enemy {
                position,
                mobile_id,
                anim_frame_count,
                anim_fps,
            },
            last_frame: None,
        });
    }

    pub fn len(&self) -> usize {
        self.entries.len()
    }

    pub fn is_empty(&self) -> bool {
        self.entries.is_empty()
    }

    /// One consolidated evaluation pass: advances elapsed time, computes the
    /// current frame for every sprite, and returns only the entries whose
    /// frame changed since the last pass. `dt` is seconds since the last call;
    /// `camera` is the glTF-space camera position.
    ///
    /// This is the single applyFrame-per-tick entry point. No per-entity
    /// polling: the service walks all entries internally and emits the
    /// consolidated diff.
    pub fn evaluate(&mut self, dt: f32, camera: [f32; 3]) -> Vec<FrameUpdate> {
        self.elapsed += dt;
        let mut updates = Vec::new();
        for entry in &mut self.entries {
            let frame = match &entry.kind {
                SpriteKind::Env { frame_count, fps } => {
                    if *frame_count <= 1 {
                        0
                    } else {
                        ((self.elapsed * *fps as f32) as u32) % frame_count
                    }
                }
                SpriteKind::Enemy {
                    position,
                    anim_frame_count,
                    anim_fps,
                    ..
                } => {
                    // Orientation from camera (0-7 DFU sectors).
                    let orientation = evaluate_directional(*position, camera) as u32;
                    // Anim frame from global elapsed time. Independent of
                    // orientation — a direction change mid-animation preserves
                    // the anim frame position.
                    let anim_frame = if *anim_frame_count <= 1 {
                        0
                    } else {
                        ((self.elapsed * *anim_fps as f32) as u32) % anim_frame_count
                    };
                    // Atlas layout: orientation * M + anim_frame.
                    orientation * anim_frame_count + anim_frame
                }
            };
            if entry.last_frame != Some(frame) {
                entry.last_frame = Some(frame);
                updates.push(FrameUpdate {
                    handle: entry.handle,
                    frame,
                });
            }
        }
        updates
    }
}

impl Default for AnimationService {
    fn default() -> Self {
        Self::new()
    }
}

/// DFU animation speed for a mobile type's move state (ground=6, flying=10).
pub fn move_fps(mobile_id: u8) -> u32 {
    match mobile::mobile_type(mobile_id) {
        Some(m) if m.flying => FLY_ANIM_SPEED,
        _ => MOVE_ANIM_SPEED,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn env_flats_cycle_at_5fps() {
        let mut svc = AnimationService::new();
        svc.add_env(100, 4); // 4-frame torch at 5fps

        let u = svc.evaluate(0.0, [0.0, 0.0, 0.0]);
        assert_eq!(
            u,
            vec![FrameUpdate {
                handle: 100,
                frame: 0
            }]
        );

        let u = svc.evaluate(0.2, [0.0, 0.0, 0.0]); // 0.2s = 1 frame at 5fps
        assert_eq!(
            u,
            vec![FrameUpdate {
                handle: 100,
                frame: 1
            }]
        );

        let u = svc.evaluate(0.2, [0.0, 0.0, 0.0]);
        assert_eq!(
            u,
            vec![FrameUpdate {
                handle: 100,
                frame: 2
            }]
        );

        let u = svc.evaluate(0.2, [0.0, 0.0, 0.0]);
        assert_eq!(
            u,
            vec![FrameUpdate {
                handle: 100,
                frame: 3
            }]
        );

        let u = svc.evaluate(0.2, [0.0, 0.0, 0.0]); // wraps to 0
        assert_eq!(
            u,
            vec![FrameUpdate {
                handle: 100,
                frame: 0
            }]
        );
    }

    #[test]
    fn no_diff_when_frame_unchanged() {
        let mut svc = AnimationService::new();
        svc.add_env(200, 5);
        let _ = svc.evaluate(0.0, [0.0, 0.0, 0.0]);
        let u = svc.evaluate(0.01, [0.0, 0.0, 0.0]);
        assert!(u.is_empty(), "no diff expected when frame unchanged");
    }

    #[test]
    fn single_frame_env_is_static() {
        let mut svc = AnimationService::new();
        svc.add_env(300, 1);
        let u = svc.evaluate(1.0, [0.0, 0.0, 0.0]);
        assert_eq!(
            u,
            vec![FrameUpdate {
                handle: 300,
                frame: 0
            }]
        );
        let u = svc.evaluate(1.0, [0.0, 0.0, 0.0]);
        assert!(u.is_empty());
    }

    #[test]
    fn enemy_combines_orientation_and_anim_frame() {
        // SkeletalWarrior: 4 move frames per orientation, 6fps ground speed.
        let mut svc = AnimationService::new();
        svc.add_enemy(400, [10.0, 33.0, -7.0], 15, 4);

        // t=0, camera in front: orientation 0, anim_frame 0 → frame 0
        let u = svc.evaluate(0.0, [10.5, 34.4, -11.0]);
        assert_eq!(
            u,
            vec![FrameUpdate {
                handle: 400,
                frame: 0
            }]
        );

        // Camera behind: orientation 4, same anim_frame 0 → frame 4*4+0=16
        let u = svc.evaluate(0.0, [10.5, 34.4, -3.0]);
        assert_eq!(
            u,
            vec![FrameUpdate {
                handle: 400,
                frame: 16
            }]
        );
    }

    #[test]
    fn enemy_direction_change_preserves_anim_frame() {
        // SkeletalWarrior: 4 move frames, 6fps.
        let mut svc = AnimationService::new();
        svc.add_enemy(500, [10.0, 33.0, -7.0], 15, 4);

        // Advance ~0.35s → anim_frame = floor(0.35 * 6) % 4 = 2
        svc.evaluate(0.35, [10.5, 34.4, -11.0]); // front, frame = 0*4+2=2
        let last = svc.entries[0].last_frame;
        assert_eq!(last, Some(2));

        // Change direction: back. Anim frame stays 2 → frame = 4*4+2=18
        let u = svc.evaluate(0.0, [10.5, 34.4, -3.0]);
        assert_eq!(
            u,
            vec![FrameUpdate {
                handle: 500,
                frame: 18
            }]
        );
    }

    #[test]
    fn enemy_anim_frame_advances_independently() {
        // SkeletalWarrior: 4 move frames, 6fps. Camera stays in front.
        let mut svc = AnimationService::new();
        svc.add_enemy(600, [10.0, 33.0, -7.0], 15, 4);

        // t=0: orientation 0, anim_frame 0 → frame 0
        let _ = svc.evaluate(0.0, [10.5, 34.4, -11.0]);
        // ~0.17s later: anim_frame = floor(0.17 * 6) % 4 = 1 → frame 0*4+1=1
        let u = svc.evaluate(0.17, [10.5, 34.4, -11.0]);
        assert_eq!(
            u,
            vec![FrameUpdate {
                handle: 600,
                frame: 1
            }]
        );
        // ~0.17s later: anim_frame = floor(0.34 * 6) % 4 = 2 → frame 2
        let u = svc.evaluate(0.17, [10.5, 34.4, -11.0]);
        assert_eq!(
            u,
            vec![FrameUpdate {
                handle: 600,
                frame: 2
            }]
        );
    }

    #[test]
    fn consolidated_diff_mixed_kinds() {
        let mut svc = AnimationService::new();
        svc.add_env(1, 4); // torch: 4 frames at 5fps
        svc.add_enemy(2, [10.0, 33.0, -7.0], 15, 4); // enemy: 4 frames at 6fps

        // t=0: both emit their initial frame
        let u = svc.evaluate(0.0, [10.5, 34.4, -11.0]);
        assert_eq!(u.len(), 2);
        assert!(u.contains(&FrameUpdate {
            handle: 1,
            frame: 0
        }));
        assert!(u.contains(&FrameUpdate {
            handle: 2,
            frame: 0
        }));

        // 0.2s later: torch advances to frame 1 (5fps), enemy advances to
        // frame 1 (6fps: floor(0.2*6)%4=1 → orientation 0 * 4 + 1 = 1)
        let u = svc.evaluate(0.2, [10.5, 34.4, -11.0]);
        assert!(u.contains(&FrameUpdate {
            handle: 1,
            frame: 1
        }));
        assert!(u.contains(&FrameUpdate {
            handle: 2,
            frame: 1
        }));
    }

    #[test]
    fn move_fps_by_mobile_type() {
        assert_eq!(move_fps(0), MOVE_ANIM_SPEED); // Rat: ground
        assert_eq!(move_fps(1), FLY_ANIM_SPEED); // Imp: flying
    }
}
