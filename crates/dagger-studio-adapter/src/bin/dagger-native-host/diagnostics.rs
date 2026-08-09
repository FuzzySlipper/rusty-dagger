use std::collections::{BTreeMap, BTreeSet};

use anyhow::{bail, Context, Result};
use dagger_runtime::{AnimationService, PatrolService};
use rusty_engine::{
    render_model::{
        Geometry, Material, RenderDiff, RenderFrameDiff, RenderHandle, RenderLayer, RenderMetadata,
        RenderNode, Transform,
    },
    render_projection::{RenderHandleNamespace, RetainedNodeProjector},
};
use serde::Deserialize;

const NAV_RADIUS: f32 = 10.0;
const NAV_VERTICAL_WINDOW: f32 = 6.0;
const NAV_CELL_LIMIT: usize = 512;
const NAV_REBUILD_DISTANCE_SQUARED: f32 = 9.0;

#[derive(Debug, Clone, Copy, Default)]
pub(crate) struct DiagnosticFrameReadout {
    pub(crate) animation_advanced: bool,
    pub(crate) patrol_moved: bool,
    pub(crate) overlays_enabled: bool,
    pub(crate) overlays_disabled: bool,
    pub(crate) stale_handle_replaced: bool,
    pub(crate) animation_updates: usize,
    pub(crate) retained_overlays: usize,
}

pub(crate) struct DiagnosticFrame {
    pub(crate) frame: RenderFrameDiff,
    pub(crate) readout: DiagnosticFrameReadout,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, PartialOrd, Ord)]
enum OverlayKey {
    AuthoredSpawn(u32),
    LiveAnchor(u32),
    LiveBounds(u32),
    Heading(u32),
    NavCell(i32, i32, i32),
}

#[derive(Debug, Clone)]
struct SpriteOverlay {
    handle: u32,
    authored: [f32; 3],
    size: [f32; 2],
    pivot: [f32; 2],
}

#[derive(Debug, Clone, Copy)]
struct LiveSprite {
    translation: [f32; 3],
    heading: f32,
}

#[derive(Debug, Clone, Copy)]
struct NavOverlayCell {
    key: OverlayKey,
    position: [f32; 3],
}

pub(crate) struct NativeDiagnostics {
    animation: AnimationService,
    patrol: PatrolService,
    nav_cell_size: f32,
    nav_cells: Vec<NavCellDocument>,
    sprite_overlays: Vec<SpriteOverlay>,
    live_sprites: BTreeMap<u32, LiveSprite>,
    projector: RetainedNodeProjector<OverlayKey>,
    sprite_overlay_enabled: bool,
    nav_overlay_enabled: bool,
    sprite_enabled_seen: bool,
    sprite_disabled_seen: bool,
    nav_enabled_seen: bool,
    nav_disabled_seen: bool,
    visible_nav_cells: Vec<NavOverlayCell>,
    nav_built_at: Option<[f32; 3]>,
    last_frames: BTreeMap<u32, u32>,
    retired_sample_handle: Option<RenderHandle>,
    stale_handle_replaced: bool,
    animation_advanced: bool,
    patrol_moved: bool,
    disposed: bool,
}

impl NativeDiagnostics {
    pub(crate) fn from_documents(project_text: &str, navgrid_text: &str) -> Result<Self> {
        let project: ProjectDocument =
            serde_json::from_str(project_text).context("decode diagnostic project")?;
        let navgrid: NavGridDocument =
            serde_json::from_str(navgrid_text).context("decode committed navgrid")?;
        if !navgrid.cell_size.is_finite() || navgrid.cell_size <= 0.0 || navgrid.cells.is_empty() {
            bail!("committed navgrid must contain a positive cell size and cells");
        }

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
            .context("diagnostic project has no scene")?;

        let mut animation = AnimationService::new();
        let mut enemy_spawns = Vec::new();
        let mut sprite_overlays = Vec::new();
        let mut seen_handles = BTreeSet::new();
        for entity in &scene.entities {
            let Some(sprite) = &entity.sprite else {
                continue;
            };
            if !seen_handles.insert(entity.id) {
                bail!("duplicate diagnostic sprite handle {}", entity.id);
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
                enemy_spawns.push((entity.id, entity.translation));
                sprite_overlays.push(SpriteOverlay {
                    handle: entity.id,
                    authored: entity.translation,
                    size: sprite.size,
                    pivot: sprite.pivot,
                });
            } else if frame_count > 1 {
                animation.add_env(entity.id, frame_count);
            }
        }
        if enemy_spawns.is_empty() || animation.is_empty() {
            bail!("real diagnostic project produced no patrol or animation authorities");
        }

        let patrol_cells = navgrid
            .cells
            .iter()
            .map(|cell| (cell.0, cell.1, cell.2, cell.3))
            .collect::<Vec<_>>();
        let patrol = PatrolService::new(&patrol_cells, &enemy_spawns);
        patrol.validate().map_err(anyhow::Error::msg)?;
        let live_sprites = patrol
            .positions()
            .into_iter()
            .map(|(handle, translation, _)| {
                (
                    handle,
                    LiveSprite {
                        translation,
                        heading: 0.0,
                    },
                )
            })
            .collect();

        Ok(Self {
            animation,
            patrol,
            nav_cell_size: navgrid.cell_size,
            nav_cells: navgrid.cells,
            sprite_overlays,
            live_sprites,
            projector: RetainedNodeProjector::new(RenderHandleNamespace::DEBUG),
            sprite_overlay_enabled: false,
            nav_overlay_enabled: false,
            sprite_enabled_seen: false,
            sprite_disabled_seen: false,
            nav_enabled_seen: false,
            nav_disabled_seen: false,
            visible_nav_cells: Vec::new(),
            nav_built_at: None,
            last_frames: BTreeMap::new(),
            retired_sample_handle: None,
            stale_handle_replaced: false,
            animation_advanced: false,
            patrol_moved: false,
            disposed: false,
        })
    }

    pub(crate) fn toggle_sprite_overlay(&mut self) -> bool {
        self.sprite_overlay_enabled = !self.sprite_overlay_enabled;
        if self.sprite_overlay_enabled {
            self.sprite_enabled_seen = true;
        } else if self.sprite_enabled_seen {
            self.sprite_disabled_seen = true;
        }
        self.sprite_overlay_enabled
    }

    pub(crate) fn toggle_nav_overlay(&mut self) -> bool {
        self.nav_overlay_enabled = !self.nav_overlay_enabled;
        self.nav_built_at = None;
        if self.nav_overlay_enabled {
            self.nav_enabled_seen = true;
        } else if self.nav_enabled_seen {
            self.nav_disabled_seen = true;
        }
        self.nav_overlay_enabled
    }

    pub(crate) fn tick(&mut self, dt: f32, camera: [f32; 3]) -> Result<DiagnosticFrame> {
        if self.disposed {
            bail!("native diagnostics were disposed");
        }
        if !dt.is_finite() || !(0.0..=0.25).contains(&dt) {
            bail!("diagnostic tick must be finite and bounded to 0.25 seconds");
        }

        let patrol_updates = self.patrol.evaluate(dt);
        for update in &patrol_updates {
            if let Some(live) = self.live_sprites.get_mut(&update.handle) {
                live.translation = update.translation;
                live.heading = update.heading;
            }
        }
        self.animation.update_enemies(&self.patrol.positions());
        let animation_updates = self.animation.evaluate(dt, camera);

        let mut ops = Vec::with_capacity(animation_updates.len() + patrol_updates.len());
        for update in &patrol_updates {
            if let Some(authored) = self
                .sprite_overlays
                .iter()
                .find(|sprite| sprite.handle == update.handle)
                .map(|sprite| sprite.authored)
            {
                let moved = (update.translation[0] - authored[0])
                    .hypot(update.translation[2] - authored[2]);
                self.patrol_moved |= moved > 0.01;
            }
            ops.push(RenderDiff::Update {
                handle: RenderHandle::new(u64::from(update.handle)),
                transform: Some(Transform {
                    translation: update.translation,
                    rotation: heading_rotation(update.heading),
                    scale: [1.0, 1.0, 1.0],
                }),
                material: None,
                visible: None,
                metadata: None,
            });
        }
        for update in &animation_updates {
            if let Some(previous) = self.last_frames.insert(update.handle, update.frame) {
                self.animation_advanced |= previous != update.frame;
            }
            ops.push(RenderDiff::UpdateSprite {
                handle: RenderHandle::new(u64::from(update.handle)),
                frame: Some(update.frame),
                tint: None,
                render_order: None,
                visible: None,
            });
        }

        let sample_key = self
            .sprite_overlays
            .first()
            .map(|sprite| OverlayKey::LiveAnchor(sprite.handle));
        let sample_before = sample_key.and_then(|key| self.projector.handle_of(&key));
        if !self.sprite_overlay_enabled && sample_before.is_some() {
            self.retired_sample_handle = sample_before;
        }
        let overlay_nodes = self.overlay_nodes(camera);
        let overlay_frame = self
            .projector
            .project(overlay_nodes)
            .map_err(|error| anyhow::anyhow!("project retained diagnostics: {error:?}"))?;
        let sample_after = sample_key.and_then(|key| self.projector.handle_of(&key));
        if self.sprite_overlay_enabled {
            if let (Some(retired), Some(replacement)) = (self.retired_sample_handle, sample_after) {
                self.stale_handle_replaced |= retired != replacement;
            }
        }
        ops.extend(overlay_frame.ops);
        let frame = RenderFrameDiff::try_from_ops(ops)
            .map_err(|error| anyhow::anyhow!("build diagnostic retained frame: {error:?}"))?;
        Ok(DiagnosticFrame {
            frame,
            readout: DiagnosticFrameReadout {
                animation_advanced: self.animation_advanced,
                patrol_moved: self.patrol_moved,
                overlays_enabled: self.sprite_enabled_seen && self.nav_enabled_seen,
                overlays_disabled: self.sprite_disabled_seen && self.nav_disabled_seen,
                stale_handle_replaced: self.stale_handle_replaced,
                animation_updates: animation_updates.len(),
                retained_overlays: self.projector.retained_len(),
            },
        })
    }

    pub(crate) fn dispose(&mut self) -> Result<RenderFrameDiff> {
        if self.disposed {
            return Ok(RenderFrameDiff::new());
        }
        self.sprite_overlay_enabled = false;
        self.nav_overlay_enabled = false;
        self.visible_nav_cells.clear();
        let frame = self
            .projector
            .project(BTreeMap::new())
            .map_err(|error| anyhow::anyhow!("dispose retained diagnostics: {error:?}"))?;
        self.disposed = true;
        Ok(frame)
    }

    fn overlay_nodes(&mut self, camera: [f32; 3]) -> BTreeMap<OverlayKey, RenderNode> {
        if self.nav_overlay_enabled && self.nav_needs_rebuild(camera) {
            self.rebuild_nav_cells(camera);
        } else if !self.nav_overlay_enabled {
            self.visible_nav_cells.clear();
        }

        let mut nodes = BTreeMap::new();
        if self.sprite_overlay_enabled {
            for sprite in &self.sprite_overlays {
                let Some(live) = self.live_sprites.get(&sprite.handle) else {
                    continue;
                };
                nodes.insert(
                    OverlayKey::AuthoredSpawn(sprite.handle),
                    cube_node(
                        sprite.authored,
                        [0.06, 0.06, 0.06],
                        [0.1, 0.5, 0.15, 0.7],
                        false,
                        "authored-spawn",
                    ),
                );
                nodes.insert(
                    OverlayKey::LiveAnchor(sprite.handle),
                    cube_node(
                        live.translation,
                        [0.09, 0.09, 0.09],
                        [0.2, 1.0, 0.3, 1.0],
                        false,
                        "patrol-live-anchor",
                    ),
                );
                let center_y = live.translation[1] + sprite.size[1] * (0.5 - sprite.pivot[1]);
                nodes.insert(
                    OverlayKey::LiveBounds(sprite.handle),
                    cube_node(
                        [live.translation[0], center_y, live.translation[2]],
                        [sprite.size[0], sprite.size[1], 0.02],
                        [0.2, 1.0, 0.3, 1.0],
                        true,
                        "patrol-live-bounds",
                    ),
                );
                let offset = [live.heading.cos() * 0.18, 0.12, live.heading.sin() * 0.18];
                let mut heading = cube_node(
                    [
                        live.translation[0] + offset[0],
                        live.translation[1] + offset[1],
                        live.translation[2] + offset[2],
                    ],
                    [0.35, 0.02, 0.02],
                    [1.0, 0.2, 0.2, 1.0],
                    false,
                    "patrol-heading",
                );
                heading.transform.rotation = heading_rotation(live.heading);
                nodes.insert(OverlayKey::Heading(sprite.handle), heading);
            }
        }
        if self.nav_overlay_enabled {
            for cell in &self.visible_nav_cells {
                nodes.insert(
                    cell.key,
                    cube_node(
                        cell.position,
                        [self.nav_cell_size * 0.7, 0.05, self.nav_cell_size * 0.7],
                        [0.2, 0.9, 1.0, 0.85],
                        false,
                        "navgrid-cell",
                    ),
                );
            }
        }
        nodes
    }

    fn nav_needs_rebuild(&self, camera: [f32; 3]) -> bool {
        self.nav_built_at.is_none_or(|built| {
            (camera[0] - built[0]).powi(2) + (camera[2] - built[2]).powi(2)
                > NAV_REBUILD_DISTANCE_SQUARED
        })
    }

    fn rebuild_nav_cells(&mut self, camera: [f32; 3]) {
        let mut nearest = self
            .nav_cells
            .iter()
            .filter_map(|cell| {
                let x = (cell.0 as f32 + 0.5) * self.nav_cell_size;
                let z = (cell.1 as f32 + 0.5) * self.nav_cell_size;
                let y = cell.3 as f32;
                let distance_squared = (x - camera[0]).powi(2) + (z - camera[2]).powi(2);
                (distance_squared <= NAV_RADIUS * NAV_RADIUS
                    && (y - camera[1]).abs() <= NAV_VERTICAL_WINDOW)
                    .then_some((distance_squared, *cell, [x, y + 0.03, z]))
            })
            .collect::<Vec<_>>();
        nearest.sort_by(|left, right| left.0.total_cmp(&right.0));
        self.visible_nav_cells = nearest
            .into_iter()
            .take(NAV_CELL_LIMIT)
            .map(|(_, cell, position)| NavOverlayCell {
                key: OverlayKey::NavCell(cell.0 as i32, cell.1 as i32, cell.2 as i32),
                position,
            })
            .collect();
        self.nav_built_at = Some(camera);
    }
}

fn enemy_mobile_id(asset: &str) -> Option<u8> {
    asset
        .strip_prefix("texture/enemy-")?
        .strip_suffix("-atlas")?
        .parse()
        .ok()
}

fn heading_rotation(heading: f32) -> [f32; 4] {
    [0.0, -(heading * 0.5).sin(), 0.0, (heading * 0.5).cos()]
}

fn cube_node(
    translation: [f32; 3],
    scale: [f32; 3],
    color: [f32; 4],
    wireframe: bool,
    label: &str,
) -> RenderNode {
    let mut tags = vec!["dagger-diagnostic".to_owned(), label.to_owned()];
    tags.sort();
    RenderNode {
        geometry: Geometry::Cube,
        material: Material { color, wireframe },
        transform: Transform {
            translation,
            rotation: Transform::IDENTITY.rotation,
            scale,
        },
        visible: true,
        layer: RenderLayer::Debug,
        metadata: RenderMetadata {
            source_entity: None,
            source_scene_node: None,
            tags,
            label: Some(label.to_owned()),
        },
    }
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
#[serde(rename_all = "camelCase")]
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

#[derive(Debug, Clone, Copy, Deserialize)]
struct NavCellDocument(f64, f64, f64, f64);

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct NavGridDocument {
    cell_size: f32,
    cells: Vec<NavCellDocument>,
}

#[cfg(test)]
mod tests {
    use super::*;

    const PROJECT: &str =
        include_str!("../../../../../content/projects/privateers-hold.project.json");
    const NAVGRID: &str =
        include_str!("../../../../../content/projects/privateers-hold.navgrid.json");

    #[test]
    fn real_diagnostics_batch_authorities_and_replace_retired_overlay_handles() {
        let mut diagnostics =
            NativeDiagnostics::from_documents(PROJECT, NAVGRID).expect("real diagnostics");
        assert!(diagnostics.toggle_sprite_overlay());
        assert!(diagnostics.toggle_nav_overlay());
        let first = diagnostics
            .tick(0.0, [25.6, 2.35, -25.6])
            .expect("first diagnostic frame");
        assert!(first.readout.retained_overlays > 40);
        assert!(first.readout.animation_updates > 40);
        assert!(first
            .frame
            .ops
            .iter()
            .any(|op| matches!(op, RenderDiff::Create { .. })));

        assert!(!diagnostics.toggle_sprite_overlay());
        assert!(!diagnostics.toggle_nav_overlay());
        let disabled = diagnostics
            .tick(0.1, [25.6, 2.35, -25.6])
            .expect("disabled frame");
        assert!(disabled.readout.overlays_disabled);
        assert!(disabled
            .frame
            .ops
            .iter()
            .any(|op| matches!(op, RenderDiff::Destroy { .. })));

        assert!(diagnostics.toggle_sprite_overlay());
        assert!(diagnostics.toggle_nav_overlay());
        let replaced = diagnostics
            .tick(0.1, [25.6, 2.35, -25.6])
            .expect("replacement frame");
        assert!(replaced.readout.overlays_enabled);
        assert!(replaced.readout.stale_handle_replaced);

        let mut movement = false;
        let mut animation = false;
        for _ in 0..40 {
            let frame = diagnostics
                .tick(0.1, [25.6, 2.35, -25.6])
                .expect("live diagnostic frame");
            movement |= frame.readout.patrol_moved;
            animation |= frame.readout.animation_advanced;
            if movement && animation {
                break;
            }
        }
        assert!(movement, "real patrol authority never moved");
        assert!(animation, "real animation authority never advanced");

        let disposed = diagnostics.dispose().expect("dispose diagnostics");
        assert!(!disposed.ops.is_empty());
        assert!(disposed
            .ops
            .iter()
            .all(|op| matches!(op, RenderDiff::Destroy { .. })));
        assert!(diagnostics.tick(0.1, [0.0; 3]).is_err());
    }

    #[test]
    fn tick_rejects_unbounded_time_steps_without_mutation() {
        let mut diagnostics =
            NativeDiagnostics::from_documents(PROJECT, NAVGRID).expect("real diagnostics");
        assert!(diagnostics.tick(0.251, [0.0; 3]).is_err());
        let first = diagnostics.tick(0.0, [0.0; 3]).expect("bounded tick");
        assert!(!first.frame.ops.is_empty());
    }
}
