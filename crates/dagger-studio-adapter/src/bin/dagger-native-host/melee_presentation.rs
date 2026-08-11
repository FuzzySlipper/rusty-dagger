use std::collections::BTreeMap;

use anyhow::Result;
use dagger_runtime::{MeleePresentationPhase, MeleePresentationReadout};
#[cfg(test)]
use rusty_engine::render_model::RenderHandle;
use rusty_engine::{
    render_model::{
        Geometry, Material, RenderDiff, RenderFrameDiff, RenderLayer, RenderMetadata, RenderNode,
        Transform,
    },
    render_projection::{RenderHandleNamespace, RetainedNodeProjector},
};

#[derive(Debug, Clone, Copy, PartialEq, Eq, PartialOrd, Ord)]
enum MeleeNode {
    Blade,
    Guard,
    Grip,
    StaminaBackground,
    StaminaFill,
    StaminaSpent,
    TargetHealthBackground,
    TargetHealthFill,
    ImpactPrimary,
    ImpactSecondary,
}

/// Dagger-owned first-person melee meaning projected through Engine's bounded
/// camera-relative viewmodel layer. The browser applies retained facts only;
/// it never owns action timing or predicts a combat result.
pub(crate) struct MeleePresentation {
    projector: RetainedNodeProjector<MeleeNode>,
    nodes: BTreeMap<MeleeNode, RenderNode>,
}

impl MeleePresentation {
    pub(crate) fn new() -> Self {
        Self {
            projector: RetainedNodeProjector::new(RenderHandleNamespace::PRESENTATION),
            nodes: BTreeMap::new(),
        }
    }

    pub(crate) fn tick(
        &mut self,
        action: Option<&MeleePresentationReadout>,
        stamina: f32,
        max_stamina: f32,
    ) -> Result<RenderFrameDiff> {
        self.nodes = presentation_nodes(action, stamina, max_stamina);
        self.projector
            .project(self.nodes.clone())
            .map_err(|error| anyhow::anyhow!("project retained melee presentation: {error:?}"))
    }

    pub(crate) fn snapshot(&self) -> Result<RenderFrameDiff> {
        let ops = self
            .nodes
            .iter()
            .filter_map(|(key, node)| {
                self.projector
                    .handle_of(key)
                    .map(|handle| RenderDiff::Create {
                        handle,
                        parent: None,
                        node: node.clone(),
                    })
            })
            .collect();
        RenderFrameDiff::try_from_ops(ops)
            .map_err(|error| anyhow::anyhow!("snapshot retained melee presentation: {error:?}"))
    }

    pub(crate) fn retained_len(&self) -> usize {
        self.nodes.len()
    }

    pub(crate) fn dispose(&mut self) -> Result<RenderFrameDiff> {
        self.nodes.clear();
        self.projector
            .project(BTreeMap::new())
            .map_err(|error| anyhow::anyhow!("dispose retained melee presentation: {error:?}"))
    }

    #[cfg(test)]
    fn handle(&self, key: MeleeNode) -> Option<RenderHandle> {
        self.projector.handle_of(&key)
    }
}

fn presentation_nodes(
    action: Option<&MeleePresentationReadout>,
    stamina: f32,
    max_stamina: f32,
) -> BTreeMap<MeleeNode, RenderNode> {
    let mut nodes = BTreeMap::new();
    let pose = weapon_pose(action);
    let blade_color = weapon_color(action);
    nodes.insert(
        MeleeNode::Blade,
        viewmodel_node(
            Geometry::Cube,
            pose.center,
            [0.045, 0.30, 0.025],
            pose.rotation,
            blade_color,
            false,
            "melee-blade",
        ),
    );
    let guard_center = offset_from(pose.center, pose.rotation, [0.0, -0.29, 0.0]);
    nodes.insert(
        MeleeNode::Guard,
        viewmodel_node(
            Geometry::Cube,
            guard_center,
            [0.18, 0.035, 0.045],
            pose.rotation,
            [0.72, 0.49, 0.16, 1.0],
            false,
            "melee-guard",
        ),
    );
    let grip_center = offset_from(pose.center, pose.rotation, [0.0, -0.42, 0.0]);
    nodes.insert(
        MeleeNode::Grip,
        viewmodel_node(
            Geometry::Cube,
            grip_center,
            [0.06, 0.14, 0.055],
            pose.rotation,
            [0.22, 0.10, 0.045, 1.0],
            false,
            "melee-grip",
        ),
    );

    add_stamina_bar(&mut nodes, action, stamina, max_stamina);
    if let Some(action) = action {
        add_target_health_bar(&mut nodes, action);
        add_impact(&mut nodes, action);
    }
    nodes
}

#[derive(Debug, Clone, Copy)]
struct WeaponPose {
    center: [f32; 3],
    rotation: f32,
}

fn weapon_pose(action: Option<&MeleePresentationReadout>) -> WeaponPose {
    let rest = WeaponPose {
        center: [0.48, -0.30, -0.82],
        rotation: -0.48,
    };
    let Some(action) = action else {
        return rest;
    };
    let windup = WeaponPose {
        center: [0.62, -0.23, -0.78],
        rotation: -0.92,
    };
    let strike = WeaponPose {
        center: [-0.10, 0.02, -0.62],
        rotation: 0.92,
    };
    match action.phase {
        MeleePresentationPhase::Anticipation => {
            interpolate_pose(rest, windup, smooth(action.phase_progress))
        }
        MeleePresentationPhase::Contact => {
            interpolate_pose(windup, strike, smooth(action.phase_progress))
        }
        MeleePresentationPhase::Recovery => {
            interpolate_pose(strike, rest, smooth(action.phase_progress))
        }
        MeleePresentationPhase::Rejected => {
            let kick = (action.phase_progress * std::f32::consts::TAU * 2.0).sin()
                * (1.0 - action.phase_progress)
                * 0.045;
            WeaponPose {
                center: [
                    rest.center[0] + kick,
                    rest.center[1] + kick.abs() * 0.4,
                    rest.center[2],
                ],
                rotation: rest.rotation + kick * 2.0,
            }
        }
    }
}

fn weapon_color(action: Option<&MeleePresentationReadout>) -> [f32; 4] {
    let Some(action) = action else {
        return [0.72, 0.79, 0.88, 1.0];
    };
    if action.phase == MeleePresentationPhase::Rejected {
        return [0.95, 0.18, 0.52, 1.0];
    }
    if action.phase == MeleePresentationPhase::Contact {
        return match action.outcome.as_str() {
            "miss" => [0.35, 0.82, 1.0, 1.0],
            "killed" => [1.0, 0.88, 0.28, 1.0],
            _ => [1.0, 0.38, 0.16, 1.0],
        };
    }
    [0.78, 0.84, 0.94, 1.0]
}

fn add_stamina_bar(
    nodes: &mut BTreeMap<MeleeNode, RenderNode>,
    action: Option<&MeleePresentationReadout>,
    stamina: f32,
    max_stamina: f32,
) {
    const WIDTH: f32 = 0.46;
    const LEFT: f32 = -0.68;
    const Y: f32 = -0.30;
    nodes.insert(
        MeleeNode::StaminaBackground,
        viewmodel_node(
            Geometry::Cube,
            [LEFT + WIDTH * 0.5, Y, -0.72],
            [WIDTH * 0.5 + 0.018, 0.052, 0.012],
            0.0,
            [0.035, 0.045, 0.055, 0.92],
            false,
            "melee-stamina-background",
        ),
    );
    let fraction = if max_stamina > 0.0 {
        (stamina / max_stamina).clamp(0.0, 1.0)
    } else {
        0.0
    };
    if fraction > 0.0 {
        nodes.insert(
            MeleeNode::StaminaFill,
            bar_node(
                LEFT,
                Y,
                -0.70,
                WIDTH,
                fraction,
                [0.18, 0.86, 0.42, 1.0],
                "melee-stamina-fill",
            ),
        );
    }
    if let Some(action) = action.filter(|action| action.stamina_before > action.stamina_after) {
        let before = (action.stamina_before / max_stamina).clamp(0.0, 1.0);
        let after = (action.stamina_after / max_stamina).clamp(0.0, 1.0);
        let spent = before - after;
        if spent > 0.0 {
            let spent_left = LEFT + WIDTH * after;
            nodes.insert(
                MeleeNode::StaminaSpent,
                bar_node(
                    spent_left,
                    Y,
                    -0.68,
                    WIDTH,
                    spent,
                    [1.0, 0.63, 0.10, 1.0],
                    "melee-stamina-spent",
                ),
            );
        }
    }
}

fn add_target_health_bar(
    nodes: &mut BTreeMap<MeleeNode, RenderNode>,
    action: &MeleePresentationReadout,
) {
    if !action.accepted {
        return;
    }
    let (Some(before), Some(after), Some(maximum)) = (
        action.target_health_before,
        action.target_health_after,
        action.target_max_health,
    ) else {
        return;
    };
    const WIDTH: f32 = 0.62;
    const LEFT: f32 = -0.31;
    const Y: f32 = 0.30;
    nodes.insert(
        MeleeNode::TargetHealthBackground,
        viewmodel_node(
            Geometry::Cube,
            [0.0, Y, -0.74],
            [WIDTH * 0.5 + 0.018, 0.055, 0.012],
            0.0,
            [0.05, 0.025, 0.025, 0.94],
            false,
            "melee-target-health-background",
        ),
    );
    let contact_reached = action.phase != MeleePresentationPhase::Anticipation;
    let shown = if contact_reached { after } else { before };
    let fraction = if maximum > 0.0 {
        (shown / maximum).clamp(0.0, 1.0)
    } else {
        0.0
    };
    if fraction > 0.0 {
        nodes.insert(
            MeleeNode::TargetHealthFill,
            bar_node(
                LEFT,
                Y,
                -0.72,
                WIDTH,
                fraction,
                [0.92, 0.12, 0.10, 1.0],
                "melee-target-health-fill",
            ),
        );
    }
}

fn add_impact(nodes: &mut BTreeMap<MeleeNode, RenderNode>, action: &MeleePresentationReadout) {
    let visible_contact = matches!(
        action.phase,
        MeleePresentationPhase::Contact | MeleePresentationPhase::Recovery
    );
    if action.phase == MeleePresentationPhase::Rejected {
        let fade = 1.0 - action.phase_progress;
        nodes.insert(
            MeleeNode::ImpactPrimary,
            viewmodel_node(
                Geometry::Cube,
                [0.0, 0.02, -0.55],
                [0.28 * fade.max(0.55), 0.04, 0.018],
                0.72,
                [0.95, 0.08, 0.38, 0.95],
                false,
                "melee-rejected-primary",
            ),
        );
        nodes.insert(
            MeleeNode::ImpactSecondary,
            viewmodel_node(
                Geometry::Cube,
                [0.0, 0.02, -0.55],
                [0.28 * fade.max(0.55), 0.04, 0.018],
                -0.72,
                [0.95, 0.08, 0.38, 0.95],
                false,
                "melee-rejected-secondary",
            ),
        );
        return;
    }
    if !visible_contact {
        return;
    }
    let (color, wireframe, scale) = match action.outcome.as_str() {
        "miss" => ([0.30, 0.80, 1.0, 0.92], true, 0.22),
        "killed" => ([1.0, 0.84, 0.18, 1.0], false, 0.30),
        _ => ([1.0, 0.24, 0.10, 0.96], false, 0.23),
    };
    nodes.insert(
        MeleeNode::ImpactPrimary,
        viewmodel_node(
            Geometry::Sphere,
            [0.0, 0.04, -0.54],
            [scale, scale, 0.035],
            0.0,
            color,
            wireframe,
            match action.outcome.as_str() {
                "miss" => "melee-impact-miss",
                "killed" => "melee-impact-kill",
                _ => "melee-impact-hit",
            },
        ),
    );
    if action.died {
        nodes.insert(
            MeleeNode::ImpactSecondary,
            viewmodel_node(
                Geometry::Cube,
                [0.0, 0.04, -0.52],
                [0.36, 0.045, 0.018],
                0.78,
                [1.0, 0.94, 0.56, 1.0],
                false,
                "melee-impact-kill-cross",
            ),
        );
    }
}

fn bar_node(
    left: f32,
    y: f32,
    z: f32,
    width: f32,
    fraction: f32,
    color: [f32; 4],
    label: &str,
) -> RenderNode {
    let visible_width = width * fraction;
    viewmodel_node(
        Geometry::Cube,
        [left + visible_width * 0.5, y, z],
        [visible_width * 0.5, 0.040, 0.012],
        0.0,
        color,
        false,
        label,
    )
}

fn viewmodel_node(
    geometry: Geometry,
    translation: [f32; 3],
    scale: [f32; 3],
    rotation_z: f32,
    color: [f32; 4],
    wireframe: bool,
    label: &str,
) -> RenderNode {
    let mut tags = vec!["dagger-melee".to_string(), label.to_string()];
    tags.sort();
    RenderNode {
        geometry,
        material: Material { color, wireframe },
        transform: Transform {
            translation,
            rotation: z_rotation(rotation_z),
            scale,
        },
        visible: true,
        layer: RenderLayer::Viewmodel,
        metadata: RenderMetadata {
            source_entity: None,
            source_scene_node: None,
            tags,
            label: Some(label.to_string()),
        },
    }
}

fn z_rotation(angle: f32) -> [f32; 4] {
    [0.0, 0.0, (angle * 0.5).sin(), (angle * 0.5).cos()]
}

fn offset_from(center: [f32; 3], rotation: f32, offset: [f32; 3]) -> [f32; 3] {
    let (sin, cos) = rotation.sin_cos();
    [
        center[0] + offset[0] * cos - offset[1] * sin,
        center[1] + offset[0] * sin + offset[1] * cos,
        center[2] + offset[2],
    ]
}

fn interpolate_pose(from: WeaponPose, to: WeaponPose, amount: f32) -> WeaponPose {
    WeaponPose {
        center: [
            lerp(from.center[0], to.center[0], amount),
            lerp(from.center[1], to.center[1], amount),
            lerp(from.center[2], to.center[2], amount),
        ],
        rotation: lerp(from.rotation, to.rotation, amount),
    }
}

fn lerp(from: f32, to: f32, amount: f32) -> f32 {
    from + (to - from) * amount
}

fn smooth(value: f32) -> f32 {
    let value = value.clamp(0.0, 1.0);
    value * value * (3.0 - 2.0 * value)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn action(phase: MeleePresentationPhase, outcome: &str) -> MeleePresentationReadout {
        MeleePresentationReadout {
            attempt_sequence: 1,
            phase,
            phase_progress: 0.5,
            accepted: phase != MeleePresentationPhase::Rejected,
            outcome: outcome.to_string(),
            target_id: Some(2007),
            stamina_before: 90.0,
            stamina_after: 80.0,
            target_health_before: Some(3.0),
            target_health_after: Some(if outcome == "killed" { 0.0 } else { 2.0 }),
            target_max_health: Some(3.0),
            final_damage: Some(if outcome == "miss" { 0.0 } else { 1.0 }),
            died: outcome == "killed",
        }
    }

    #[test]
    fn viewmodel_distinguishes_contact_results_and_rejection() {
        let mut presentation = MeleePresentation::new();
        let rest = presentation.tick(None, 90.0, 90.0).expect("rest frame");
        assert!(rest.ops.iter().any(|op| matches!(op, RenderDiff::Create { node, .. } if node.layer == RenderLayer::Viewmodel && node.metadata.label.as_deref() == Some("melee-blade"))));

        let miss = presentation
            .tick(
                Some(&action(MeleePresentationPhase::Contact, "miss")),
                80.0,
                90.0,
            )
            .expect("miss frame");
        assert!(miss.ops.iter().any(|op| matches!(op, RenderDiff::Create { node, .. } if node.metadata.label.as_deref() == Some("melee-impact-miss"))));

        let killed = presentation
            .tick(
                Some(&action(MeleePresentationPhase::Contact, "killed")),
                80.0,
                90.0,
            )
            .expect("kill frame");
        assert!(killed.ops.iter().any(|op| matches!(op, RenderDiff::Create { node, .. } if node.metadata.label.as_deref() == Some("melee-impact-kill-cross"))));

        let rejected = presentation
            .tick(
                Some(&action(MeleePresentationPhase::Rejected, "cooldown")),
                80.0,
                90.0,
            )
            .expect("rejected frame");
        assert!(rejected.ops.iter().any(|op| match op {
            RenderDiff::Create { node, .. } => {
                node.metadata.label.as_deref() == Some("melee-rejected-primary")
            }
            RenderDiff::Update {
                metadata: Some(metadata),
                ..
            } => metadata.label.as_deref() == Some("melee-rejected-primary"),
            _ => false,
        }));
        assert!(presentation.handle(MeleeNode::Blade).is_some());
    }
}
