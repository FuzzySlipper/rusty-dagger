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
//! - **Enemy directional sprites**: the visible frame is the camera-driven
//!   orientation (0–7) from `evaluate_directional`. For idle (1-frame
//!   records) this is static per camera pose. When move-state arrives with
//!   the patrol task, the atlas frame = orientation × anim_frame_count +
//!   current_anim_frame, where anim_frame cycles at DFU speed (6 fps ground,
//!   10 fps flying).
//!
//! The service owns elapsed time and per-sprite state. One `evaluate` call
//! per tick produces the complete diff — callers never poll individual
//! sprites.

use crate::evaluate_directional;
use arena2::mobile::{self, ENV_BILLBOARD_FPS, FLY_ANIM_SPEED, IDLE_ANIM_SPEED, MOVE_ANIM_SPEED};

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
    /// Enemy directional sprite: frame = orientation (idle) or
    /// orientation * anim_frames + anim_frame (move, when 6641 adds it).
    Enemy { position: [f32; 3], mobile_id: u8 },
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

    /// Register an enemy directional sprite for animation.
    pub fn add_enemy(&mut self, handle: u32, position: [f32; 3], mobile_id: u8) {
        self.entries.push(SpriteEntry {
            handle,
            kind: SpriteKind::Enemy {
                position,
                mobile_id,
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
                    mobile_id,
                    ..
                } => {
                    // Idle: frame = orientation only (idle records are 1-frame
                    // for all our enemies). The directional evaluator maps
                    // camera pose → 8-sector orientation. Move-state animation
                    // (orientation × anim_frames + cycle) arrives with 6641.
                    let _ = mobile_id; // used when move-state is added
                    evaluate_directional(*position, camera) as u32
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

/// DFU animation speed for a mobile type's idle state.
pub fn idle_fps(mobile_id: u8) -> u32 {
    match mobile::mobile_type(mobile_id) {
        Some(m) if m.flying => FLY_ANIM_SPEED,
        Some(m) if m.has_idle => IDLE_ANIM_SPEED,
        _ => MOVE_ANIM_SPEED,
    }
}

/// DFU animation speed for a mobile type's move state.
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

        // t=0: frame 0
        let u = svc.evaluate(0.0, [0.0, 0.0, 0.0]);
        assert_eq!(
            u,
            vec![FrameUpdate {
                handle: 100,
                frame: 0
            }]
        );

        // 0.2s = 1 frame at 5fps
        let u = svc.evaluate(0.2, [0.0, 0.0, 0.0]);
        assert_eq!(
            u,
            vec![FrameUpdate {
                handle: 100,
                frame: 1
            }]
        );

        // 0.4s = 2 frames
        let u = svc.evaluate(0.2, [0.0, 0.0, 0.0]);
        assert_eq!(
            u,
            vec![FrameUpdate {
                handle: 100,
                frame: 2
            }]
        );

        // 0.6s = 3 frames
        let u = svc.evaluate(0.2, [0.0, 0.0, 0.0]);
        assert_eq!(
            u,
            vec![FrameUpdate {
                handle: 100,
                frame: 3
            }]
        );

        // 0.8s = wraps to 0
        let u = svc.evaluate(0.2, [0.0, 0.0, 0.0]);
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

        // t=0 → frame 0
        let _ = svc.evaluate(0.0, [0.0, 0.0, 0.0]);
        // Tiny dt, still frame 0 → no update
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
        // Never changes
        let u = svc.evaluate(1.0, [0.0, 0.0, 0.0]);
        assert!(u.is_empty());
    }

    #[test]
    fn enemy_idle_uses_directional_orientation() {
        let mut svc = AnimationService::new();
        svc.add_enemy(400, [10.0, 33.0, -7.0], 15); // SkeletalWarrior

        // Camera in front: orientation 0
        let u = svc.evaluate(0.0, [10.5, 34.4, -11.0]);
        assert_eq!(
            u,
            vec![FrameUpdate {
                handle: 400,
                frame: 0
            }]
        );

        // Camera behind: orientation 4
        let u = svc.evaluate(0.0, [10.5, 34.4, -3.0]);
        assert_eq!(
            u,
            vec![FrameUpdate {
                handle: 400,
                frame: 4
            }]
        );
    }

    #[test]
    fn consolidated_diff_mixed_kinds() {
        let mut svc = AnimationService::new();
        svc.add_env(1, 4); // torch
        svc.add_enemy(2, [10.0, 33.0, -7.0], 15); // enemy

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

        // 0.2s later: torch advances to frame 1, enemy stays at frame 0
        let u = svc.evaluate(0.2, [10.5, 34.4, -11.0]);
        assert_eq!(
            u,
            vec![FrameUpdate {
                handle: 1,
                frame: 1
            }]
        );
    }

    #[test]
    fn fps_constants_match_dfu() {
        assert_eq!(ENV_BILLBOARD_FPS, 5);
        assert_eq!(MOVE_ANIM_SPEED, 6);
        assert_eq!(FLY_ANIM_SPEED, 10);
        assert_eq!(IDLE_ANIM_SPEED, 4);
    }

    #[test]
    fn idle_fps_by_mobile_type() {
        assert_eq!(idle_fps(0), IDLE_ANIM_SPEED); // Rat: has_idle
        assert_eq!(idle_fps(1), FLY_ANIM_SPEED); // Imp: flying
        assert_eq!(idle_fps(15), IDLE_ANIM_SPEED); // SkeletalWarrior: has_idle
    }

    #[test]
    fn move_fps_by_mobile_type() {
        assert_eq!(move_fps(0), MOVE_ANIM_SPEED); // Rat: ground
        assert_eq!(move_fps(1), FLY_ANIM_SPEED); // Imp: flying
    }
}
