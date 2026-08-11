use std::collections::{BTreeMap, BTreeSet};

use anyhow::{bail, Context, Result};
use dagger_runtime::{AnimationService, PositionUpdate};
use rusty_engine::render_model::{RenderDiff, RenderFrameDiff, RenderHandle, Transform};
use serde::Deserialize;

#[derive(Debug, Clone)]
pub(crate) struct SpriteDescriptor {
    pub(crate) handle: u32,
    pub(crate) authored: [f32; 3],
    pub(crate) size: [f32; 2],
    pub(crate) pivot: [f32; 2],
}

#[derive(Debug, Clone, Copy)]
pub(crate) struct LiveSprite {
    pub(crate) translation: [f32; 3],
    pub(crate) heading: f32,
}

pub(crate) struct LivePresentationFrame {
    pub(crate) frame: RenderFrameDiff,
    pub(crate) animation_advanced: bool,
    pub(crate) patrol_moved: bool,
    pub(crate) animation_updates: usize,
}

/// Rust-owned dynamic presentation for the admitted Dagger project.
///
/// The runtime owns encounter positions and the animation service owns sprite
/// timing/directional selection. Consumers receive only Engine facade frame
/// diffs; browser code never derives transforms or animation frames.
pub(crate) struct LivePresentation {
    animation: AnimationService,
    sprite_descriptors: Vec<SpriteDescriptor>,
    live_sprites: BTreeMap<u32, LiveSprite>,
    current_frames: BTreeMap<u32, u32>,
    animation_advanced: bool,
    patrol_moved: bool,
}

impl LivePresentation {
    pub(crate) fn from_project(project_text: &str) -> Result<Self> {
        let project: ProjectDocument =
            serde_json::from_str(project_text).context("decode live presentation project")?;
        let frame_counts = project
            .assets
            .iter()
            .filter_map(|asset| {
                asset.texture.as_ref().and_then(|texture| {
                    texture
                        .sprite_atlas
                        .as_ref()
                        .map(|atlas| (asset.id.as_str(), atlas.frames.len() as u32))
                })
            })
            .collect::<BTreeMap<_, _>>();
        let scene = project
            .scenes
            .iter()
            .find(|scene| Some(scene.id.as_str()) == project.entry_scene.as_deref())
            .or_else(|| project.scenes.first())
            .context("live presentation project has no scene")?;

        let mut animation = AnimationService::new();
        let mut sprite_descriptors = Vec::new();
        let mut live_sprites = BTreeMap::new();
        let mut seen_handles = BTreeSet::new();
        for entity in &scene.entities {
            let Some(sprite) = &entity.sprite else {
                continue;
            };
            if !seen_handles.insert(entity.id) {
                bail!("duplicate live sprite handle {}", entity.id);
            }
            let frame_count = frame_counts
                .get(sprite.asset.as_str())
                .copied()
                .unwrap_or(1);
            if let Some(mobile_id) = enemy_mobile_id(&sprite.asset) {
                if frame_count < 8 || frame_count % 8 != 0 {
                    bail!(
                        "enemy sprite {} has {frame_count} frames; expected 8 directional rows",
                        sprite.asset
                    );
                }
                animation.add_enemy(entity.id, entity.translation, mobile_id, frame_count / 8);
                sprite_descriptors.push(SpriteDescriptor {
                    handle: entity.id,
                    authored: entity.translation,
                    size: sprite.size,
                    pivot: sprite.pivot,
                });
                live_sprites.insert(
                    entity.id,
                    LiveSprite {
                        translation: entity.translation,
                        heading: 0.0,
                    },
                );
            } else if frame_count > 1 {
                animation.add_env(entity.id, frame_count);
            }
        }
        if sprite_descriptors.is_empty() || animation.is_empty() {
            bail!("real project produced no encounter or animation presentation authorities");
        }
        Ok(Self {
            animation,
            sprite_descriptors,
            live_sprites,
            current_frames: BTreeMap::new(),
            animation_advanced: false,
            patrol_moved: false,
        })
    }

    pub(crate) fn tick(
        &mut self,
        dt: f32,
        camera: [f32; 3],
        encounter_positions: &[(u32, [f32; 3], f32, bool)],
        encounter_updates: &[PositionUpdate],
    ) -> Result<LivePresentationFrame> {
        if !dt.is_finite() || !(0.0..=0.25).contains(&dt) {
            bail!("live presentation tick must be finite and bounded to 0.25 seconds");
        }
        for &(handle, translation, heading, _) in encounter_positions {
            if let Some(live) = self.live_sprites.get_mut(&handle) {
                live.translation = translation;
                live.heading = heading;
            }
        }
        for update in encounter_updates {
            if let Some(live) = self.live_sprites.get_mut(&update.handle) {
                live.translation = update.translation;
                live.heading = update.heading;
            }
            if let Some(authored) = self
                .sprite_descriptors
                .iter()
                .find(|sprite| sprite.handle == update.handle)
                .map(|sprite| sprite.authored)
            {
                self.patrol_moved |= (update.translation[0] - authored[0])
                    .hypot(update.translation[2] - authored[2])
                    > 0.01;
            }
        }
        self.animation.update_enemies(encounter_positions);
        let animation_updates = self.animation.evaluate(dt, camera);
        let mut ops = Vec::with_capacity(animation_updates.len() + encounter_updates.len());
        for update in encounter_updates {
            ops.push(transform_update(
                update.handle,
                update.translation,
                update.heading,
            ));
        }
        for update in &animation_updates {
            if let Some(previous) = self.current_frames.insert(update.handle, update.frame) {
                self.animation_advanced |= previous != update.frame;
            }
            ops.push(sprite_update(update.handle, update.frame));
        }
        Ok(LivePresentationFrame {
            frame: frame_from_ops(ops)?,
            animation_advanced: self.animation_advanced,
            patrol_moved: self.patrol_moved,
            animation_updates: animation_updates.len(),
        })
    }

    /// Complete current dynamic state for reconnecting/polling consumers.
    /// Repeating these absolute retained updates is idempotent.
    pub(crate) fn snapshot(&self) -> Result<RenderFrameDiff> {
        let mut ops = Vec::with_capacity(self.live_sprites.len() + self.current_frames.len());
        for (&handle, live) in &self.live_sprites {
            ops.push(transform_update(handle, live.translation, live.heading));
        }
        for (&handle, &frame) in &self.current_frames {
            ops.push(sprite_update(handle, frame));
        }
        frame_from_ops(ops)
    }

    pub(crate) fn sprite_descriptors(&self) -> &[SpriteDescriptor] {
        &self.sprite_descriptors
    }

    pub(crate) fn live_sprite(&self, handle: u32) -> Option<LiveSprite> {
        self.live_sprites.get(&handle).copied()
    }
}

fn transform_update(handle: u32, translation: [f32; 3], heading: f32) -> RenderDiff {
    RenderDiff::Update {
        handle: RenderHandle::new(u64::from(handle)),
        transform: Some(Transform {
            translation,
            rotation: heading_rotation(heading),
            scale: [1.0, 1.0, 1.0],
        }),
        material: None,
        visible: None,
        metadata: None,
    }
}

fn sprite_update(handle: u32, frame: u32) -> RenderDiff {
    RenderDiff::UpdateSprite {
        handle: RenderHandle::new(u64::from(handle)),
        frame: Some(frame),
        tint: None,
        render_order: None,
        visible: None,
    }
}

fn frame_from_ops(ops: Vec<RenderDiff>) -> Result<RenderFrameDiff> {
    RenderFrameDiff::try_from_ops(ops)
        .map_err(|error| anyhow::anyhow!("build live retained frame: {error:?}"))
}

fn enemy_mobile_id(asset: &str) -> Option<u8> {
    asset
        .strip_prefix("texture/enemy-")?
        .strip_suffix("-atlas")?
        .parse()
        .ok()
}

pub(crate) fn heading_rotation(heading: f32) -> [f32; 4] {
    [0.0, -(heading * 0.5).sin(), 0.0, (heading * 0.5).cos()]
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct ProjectDocument {
    entry_scene: Option<String>,
    assets: Vec<AssetDocument>,
    scenes: Vec<SceneDocument>,
}

#[derive(Deserialize)]
struct AssetDocument {
    id: String,
    texture: Option<TextureDocument>,
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct TextureDocument {
    sprite_atlas: Option<SpriteAtlasDocument>,
}

#[derive(Deserialize)]
struct SpriteAtlasDocument {
    frames: Vec<serde_json::Value>,
}

#[derive(Deserialize)]
struct SceneDocument {
    id: String,
    entities: Vec<EntityDocument>,
}

#[derive(Deserialize)]
struct EntityDocument {
    id: u32,
    translation: [f32; 3],
    sprite: Option<SpriteDocument>,
}

#[derive(Deserialize)]
struct SpriteDocument {
    asset: String,
    #[serde(default = "default_sprite_size")]
    size: [f32; 2],
    #[serde(default = "default_sprite_pivot")]
    pivot: [f32; 2],
}

fn default_sprite_size() -> [f32; 2] {
    [1.0, 1.0]
}

fn default_sprite_pivot() -> [f32; 2] {
    [0.5, 0.0]
}

#[cfg(test)]
mod tests {
    use super::*;

    const GALLERY: &str =
        include_str!("../../../../../content/projects/encounter-gallery.project.json");

    #[test]
    fn gallery_rat_changes_direction_rows_without_changing_its_instance_size() {
        let mut presentation = LivePresentation::from_project(GALLERY).expect("gallery");
        let rat = presentation
            .sprite_descriptors()
            .iter()
            .find(|sprite| sprite.handle == 2000)
            .expect("gallery Rat")
            .clone();
        assert!(rat.size[0] > 0.0 && rat.size[1] > 0.0);

        let position = rat.authored;
        let frame_for = |frame: &LivePresentationFrame| {
            frame.frame.ops.iter().find_map(|op| match op {
                RenderDiff::UpdateSprite { handle, frame, .. } if handle.raw() == 2000 => *frame,
                _ => None,
            })
        };
        let front = presentation
            .tick(
                0.0,
                [position[0], position[1] + 1.5, position[2] - 4.0],
                &[(2000, position, 0.0, false)],
                &[],
            )
            .expect("front tick");
        assert_eq!(frame_for(&front), Some(0));
        let side = presentation
            .tick(
                0.0,
                [position[0] + 4.0, position[1] + 1.5, position[2]],
                &[(2000, position, 0.0, false)],
                &[],
            )
            .expect("side tick");
        assert_eq!(frame_for(&side), Some(48));
        let back = presentation
            .tick(
                0.0,
                [position[0], position[1] + 1.5, position[2] + 4.0],
                &[(2000, position, 0.0, false)],
                &[],
            )
            .expect("back tick");
        assert_eq!(frame_for(&back), Some(32));
    }

    #[test]
    fn gallery_rat_heading_selects_opposite_side_rows_for_opposite_motion() {
        let mut presentation = LivePresentation::from_project(GALLERY).expect("gallery");
        let position = presentation
            .sprite_descriptors()
            .iter()
            .find(|sprite| sprite.handle == 2000)
            .expect("gallery Rat")
            .authored;
        let camera = [position[0], position[1] + 1.5, position[2] - 4.0];
        let row = |frame: &LivePresentationFrame| {
            frame.frame.ops.iter().find_map(|op| match op {
                RenderDiff::UpdateSprite {
                    handle,
                    frame: Some(frame),
                    ..
                } if handle.raw() == 2000 => Some(frame / 8),
                _ => None,
            })
        };
        let right = presentation
            .tick(
                0.0,
                camera,
                &[(2000, position, std::f32::consts::FRAC_PI_2, false)],
                &[],
            )
            .expect("right-facing tick");
        let left = presentation
            .tick(
                0.0,
                camera,
                &[(2000, position, -std::f32::consts::FRAC_PI_2, false)],
                &[],
            )
            .expect("left-facing tick");
        assert_eq!(row(&right), Some(2));
        assert_eq!(row(&left), Some(6));
    }
}
