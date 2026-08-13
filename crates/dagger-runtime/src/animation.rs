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
//! - **Enemy directional sprites**: each atlas contains state-major Move,
//!   Idle, Attack, and Hurt ranges, with eight orientations per state.
//!   Movement follows the shared clock; authoritative attack/hurt sequence
//!   counters start bounded one-shots whose local clocks cannot be restarted
//!   by repeated readout polling.
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
    /// Whether this sprite's entity is currently moving (patrol state).
    /// When false, the anim_frame freezes at 0 (idle). When true, it cycles.
    is_moving: bool,
    attack_sequence: u64,
    hurt_sequence: u64,
    active_action: Option<ActiveEnemyAction>,
}

#[derive(Debug, Clone, Copy, PartialEq)]
struct ActiveEnemyAction {
    kind: EnemyActionKind,
    elapsed: f32,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
enum EnemyActionKind {
    Attack,
    Hurt,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct EnemyAnimationStateLayout {
    pub frame_start: u32,
    pub frames_per_orientation: u32,
    pub fps: u32,
    pub loops: bool,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct EnemyAnimationLayout {
    pub movement: EnemyAnimationStateLayout,
    pub idle: EnemyAnimationStateLayout,
    pub attack: EnemyAnimationStateLayout,
    pub hurt: EnemyAnimationStateLayout,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct EnemyAnimationUpdate {
    pub handle: u32,
    pub attack_sequence: u64,
    pub hurt_sequence: u64,
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
        heading: f32,
        mobile_id: u8,
        /// Frames per orientation in the atlas (M). All 8 orientations
        /// carry the same count (DFU move records are uniform per enemy).
        layout: EnemyAnimationLayout,
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
            is_moving: false,
            attack_sequence: 0,
            hurt_sequence: 0,
            active_action: None,
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
        let state = EnemyAnimationStateLayout {
            frame_start: 0,
            frames_per_orientation: anim_frame_count,
            fps: anim_fps,
            loops: true,
        };
        self.add_enemy_with_layout(
            handle,
            position,
            mobile_id,
            EnemyAnimationLayout {
                movement: state,
                idle: state,
                attack: state,
                hurt: state,
            },
        );
    }

    pub fn add_enemy_with_layout(
        &mut self,
        handle: u32,
        position: [f32; 3],
        mobile_id: u8,
        layout: EnemyAnimationLayout,
    ) {
        self.entries.push(SpriteEntry {
            handle,
            kind: SpriteKind::Enemy {
                position,
                heading: 0.0,
                mobile_id,
                layout,
            },
            last_frame: None,
            is_moving: false,
            attack_sequence: 0,
            hurt_sequence: 0,
            active_action: None,
        });
    }

    /// Update enemy positions and move/idle state from the patrol service.
    /// Called before evaluate() each tick so the animation tracks patrol movement.
    pub fn update_enemies(&mut self, updates: &[(u32, [f32; 3], f32, bool)]) {
        for &(handle, pos, heading, is_moving) in updates {
            for entry in &mut self.entries {
                if entry.handle == handle {
                    if let SpriteKind::Enemy {
                        position,
                        heading: actor_heading,
                        ..
                    } = &mut entry.kind
                    {
                        *position = pos;
                        *actor_heading = heading;
                    }
                    entry.is_moving = is_moving;
                    break;
                }
            }
        }
    }

    /// Apply authoritative one-shot counters. A new hurt event takes priority
    /// over an attack event in the same frame; repeated state polling is
    /// idempotent and cannot restart an animation.
    pub fn update_enemy_actions(&mut self, updates: &[EnemyAnimationUpdate]) {
        for update in updates {
            let Some(entry) = self
                .entries
                .iter_mut()
                .find(|entry| entry.handle == update.handle)
            else {
                continue;
            };
            if update.attack_sequence < entry.attack_sequence
                || update.hurt_sequence < entry.hurt_sequence
            {
                entry.attack_sequence = update.attack_sequence;
                entry.hurt_sequence = update.hurt_sequence;
                entry.active_action = None;
                continue;
            }
            if update.hurt_sequence > entry.hurt_sequence {
                entry.hurt_sequence = update.hurt_sequence;
                entry.attack_sequence = entry.attack_sequence.max(update.attack_sequence);
                entry.active_action = Some(ActiveEnemyAction {
                    kind: EnemyActionKind::Hurt,
                    elapsed: 0.0,
                });
            } else if update.attack_sequence > entry.attack_sequence {
                entry.attack_sequence = update.attack_sequence;
                entry.active_action = Some(ActiveEnemyAction {
                    kind: EnemyActionKind::Attack,
                    elapsed: 0.0,
                });
            }
        }
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
                    heading,
                    layout,
                    ..
                } => {
                    let orientation = evaluate_directional(*position, *heading, camera) as u32;
                    let state = match entry.active_action {
                        Some(action) if action.kind == EnemyActionKind::Hurt => layout.hurt,
                        Some(_) => layout.attack,
                        None if entry.is_moving => layout.movement,
                        None => layout.idle,
                    };
                    let elapsed = entry
                        .active_action
                        .map_or(self.elapsed, |action| action.elapsed);
                    let raw_frame = (elapsed * state.fps as f32) as u32;
                    let anim_frame = if state.frames_per_orientation <= 1 {
                        0
                    } else if state.loops {
                        raw_frame % state.frames_per_orientation
                    } else {
                        raw_frame.min(state.frames_per_orientation - 1)
                    };
                    state.frame_start + orientation * state.frames_per_orientation + anim_frame
                }
            };
            if entry.last_frame != Some(frame) {
                entry.last_frame = Some(frame);
                updates.push(FrameUpdate {
                    handle: entry.handle,
                    frame,
                });
            }
            if let (Some(action), SpriteKind::Enemy { layout, .. }) =
                (&mut entry.active_action, &entry.kind)
            {
                action.elapsed += dt;
                let state = match action.kind {
                    EnemyActionKind::Attack => layout.attack,
                    EnemyActionKind::Hurt => layout.hurt,
                };
                let duration = state.frames_per_orientation as f32 / state.fps.max(1) as f32;
                if action.elapsed >= duration {
                    entry.active_action = None;
                }
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
        svc.update_enemies(&[(500, [10.0, 33.0, -7.0], 0.0, true)]);

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
    fn enemy_attack_and_hurt_are_idempotent_priority_one_shots() {
        let mut svc = AnimationService::new();
        let layout = EnemyAnimationLayout {
            movement: EnemyAnimationStateLayout {
                frame_start: 0,
                frames_per_orientation: 4,
                fps: 6,
                loops: true,
            },
            idle: EnemyAnimationStateLayout {
                frame_start: 32,
                frames_per_orientation: 1,
                fps: 4,
                loops: true,
            },
            attack: EnemyAnimationStateLayout {
                frame_start: 40,
                frames_per_orientation: 6,
                fps: 10,
                loops: false,
            },
            hurt: EnemyAnimationStateLayout {
                frame_start: 88,
                frames_per_orientation: 1,
                fps: 4,
                loops: false,
            },
        };
        svc.add_enemy_with_layout(600, [0.0, 0.0, 0.0], 15, layout);
        let camera = [0.0, 1.0, -4.0];
        assert_eq!(svc.evaluate(0.0, camera)[0].frame, 32);

        let attack = EnemyAnimationUpdate {
            handle: 600,
            attack_sequence: 1,
            hurt_sequence: 0,
        };
        svc.update_enemy_actions(&[attack]);
        assert_eq!(svc.evaluate(0.0, camera)[0].frame, 40);
        svc.update_enemy_actions(&[attack]);
        assert!(svc.evaluate(0.2, camera).is_empty());
        assert_eq!(svc.evaluate(0.0, camera)[0].frame, 42);

        svc.update_enemy_actions(&[EnemyAnimationUpdate {
            handle: 600,
            attack_sequence: 2,
            hurt_sequence: 1,
        }]);
        assert_eq!(svc.evaluate(0.0, camera)[0].frame, 88);
        assert!(svc.evaluate(0.25, camera).is_empty());
        assert_eq!(svc.evaluate(0.0, camera)[0].frame, 32);

        svc.update_enemy_actions(&[EnemyAnimationUpdate {
            handle: 600,
            attack_sequence: 0,
            hurt_sequence: 0,
        }]);
        svc.update_enemy_actions(&[attack]);
        assert_eq!(
            svc.evaluate(0.0, camera)[0].frame,
            40,
            "the first attack after a runtime counter reset must not be swallowed"
        );
    }

    #[test]
    fn enemy_anim_frame_advances_independently() {
        // SkeletalWarrior: 4 move frames, 6fps. Camera stays in front.
        let mut svc = AnimationService::new();
        svc.add_enemy(600, [10.0, 33.0, -7.0], 15, 4);
        svc.update_enemies(&[(600, [10.0, 33.0, -7.0], 0.0, true)]);

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
        svc.update_enemies(&[(2, [10.0, 33.0, -7.0], 0.0, true)]);

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
    fn enemy_heading_changes_directional_frame() {
        let mut svc = AnimationService::new();
        svc.add_enemy(700, [0.0, 0.0, 0.0], 0, 1);
        svc.update_enemies(&[(700, [0.0, 0.0, 0.0], std::f32::consts::PI, false)]);
        assert_eq!(
            svc.evaluate(0.0, [0.0, 1.0, -4.0]),
            vec![FrameUpdate {
                handle: 700,
                frame: 4,
            }]
        );
    }

    #[test]
    fn move_fps_by_mobile_type() {
        assert_eq!(move_fps(0), MOVE_ANIM_SPEED); // Rat: ground
        assert_eq!(move_fps(1), FLY_ANIM_SPEED); // Imp: flying
    }
}
