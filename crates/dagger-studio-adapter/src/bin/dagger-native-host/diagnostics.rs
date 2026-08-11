use std::collections::BTreeMap;

use anyhow::{bail, Context, Result};
use dagger_runtime::PositionUpdate;
use rusty_engine::{
    render_model::{
        Geometry, Material, RenderFrameDiff, RenderHandle, RenderLayer, RenderMetadata, RenderNode,
        Transform,
    },
    render_projection::{RenderHandleNamespace, RetainedNodeProjector},
};
use serde::Deserialize;

use crate::live_presentation::{heading_rotation, LivePresentation};

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

#[derive(Debug, Clone, Copy)]
struct NavOverlayCell {
    key: OverlayKey,
    position: [f32; 3],
}

pub(crate) struct NativeDiagnostics {
    live: LivePresentation,
    nav_cell_size: f32,
    nav_cells: Vec<NavCellDocument>,
    projector: RetainedNodeProjector<OverlayKey>,
    sprite_overlay_enabled: bool,
    nav_overlay_enabled: bool,
    sprite_enabled_seen: bool,
    sprite_disabled_seen: bool,
    nav_enabled_seen: bool,
    nav_disabled_seen: bool,
    visible_nav_cells: Vec<NavOverlayCell>,
    nav_built_at: Option<[f32; 3]>,
    retired_sample_handle: Option<RenderHandle>,
    stale_handle_replaced: bool,
    disposed: bool,
}

impl NativeDiagnostics {
    pub(crate) fn from_documents(project_text: &str, navgrid_text: &str) -> Result<Self> {
        let navgrid: NavGridDocument =
            serde_json::from_str(navgrid_text).context("decode committed navgrid")?;
        if !navgrid.cell_size.is_finite() || navgrid.cell_size <= 0.0 || navgrid.cells.is_empty() {
            bail!("committed navgrid must contain a positive cell size and cells");
        }

        let live = LivePresentation::from_project(project_text)?;

        Ok(Self {
            live,
            nav_cell_size: navgrid.cell_size,
            nav_cells: navgrid.cells,
            projector: RetainedNodeProjector::new(RenderHandleNamespace::DEBUG),
            sprite_overlay_enabled: false,
            nav_overlay_enabled: false,
            sprite_enabled_seen: false,
            sprite_disabled_seen: false,
            nav_enabled_seen: false,
            nav_disabled_seen: false,
            visible_nav_cells: Vec::new(),
            nav_built_at: None,
            retired_sample_handle: None,
            stale_handle_replaced: false,
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

    pub(crate) fn tick(
        &mut self,
        dt: f32,
        camera: [f32; 3],
        encounter_positions: &[(u32, [f32; 3], bool)],
        encounter_updates: &[PositionUpdate],
    ) -> Result<DiagnosticFrame> {
        if self.disposed {
            bail!("native diagnostics were disposed");
        }
        if !dt.is_finite() || !(0.0..=0.25).contains(&dt) {
            bail!("diagnostic tick must be finite and bounded to 0.25 seconds");
        }

        let live = self
            .live
            .tick(dt, camera, encounter_positions, encounter_updates)?;
        let mut ops = live.frame.ops;

        let sample_key = self
            .live
            .sprite_descriptors()
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
                animation_advanced: live.animation_advanced,
                patrol_moved: live.patrol_moved,
                overlays_enabled: self.sprite_enabled_seen && self.nav_enabled_seen,
                overlays_disabled: self.sprite_disabled_seen && self.nav_disabled_seen,
                stale_handle_replaced: self.stale_handle_replaced,
                animation_updates: live.animation_updates,
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
            for sprite in self.live.sprite_descriptors() {
                let Some(live) = self.live.live_sprite(sprite.handle) else {
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
    use dagger_runtime::DaggerRuntime;
    use rusty_engine::render_model::RenderDiff;

    const PROJECT: &str =
        include_str!("../../../../../content/projects/privateers-hold.project.json");
    const NAVGRID: &str =
        include_str!("../../../../../content/projects/privateers-hold.navgrid.json");

    #[test]
    fn real_diagnostics_batch_authorities_and_replace_retired_overlay_handles() {
        let mut diagnostics =
            NativeDiagnostics::from_documents(PROJECT, NAVGRID).expect("real diagnostics");
        let mut runtime = DaggerRuntime::from_project_json(PROJECT).expect("real runtime");
        runtime
            .install_encounter_navigation_json(NAVGRID)
            .expect("install encounter navigation");
        assert!(diagnostics.toggle_sprite_overlay());
        assert!(diagnostics.toggle_nav_overlay());
        let first = diagnostics
            .tick(
                0.0,
                [25.6, 2.35, -25.6],
                &runtime.encounter_positions(),
                &[],
            )
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
            .tick(
                0.1,
                [25.6, 2.35, -25.6],
                &runtime.encounter_positions(),
                &[],
            )
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
            .tick(
                0.1,
                [25.6, 2.35, -25.6],
                &runtime.encounter_positions(),
                &[],
            )
            .expect("replacement frame");
        assert!(replaced.readout.overlays_enabled);
        assert!(replaced.readout.stale_handle_replaced);

        let mut movement = false;
        let mut animation = false;
        for _ in 0..40 {
            let updates = runtime.tick_play_session(0.1).expect("encounter tick");
            let frame = diagnostics
                .tick(
                    0.1,
                    [25.6, 2.35, -25.6],
                    &runtime.encounter_positions(),
                    &updates,
                )
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
        assert!(diagnostics.tick(0.1, [0.0; 3], &[], &[]).is_err());
    }

    #[test]
    fn tick_rejects_unbounded_time_steps_without_mutation() {
        let mut diagnostics =
            NativeDiagnostics::from_documents(PROJECT, NAVGRID).expect("real diagnostics");
        assert!(diagnostics.tick(0.251, [0.0; 3], &[], &[]).is_err());
        let first = diagnostics
            .tick(0.0, [0.0; 3], &[], &[])
            .expect("bounded tick");
        assert!(!first.frame.ops.is_empty());
    }
}
