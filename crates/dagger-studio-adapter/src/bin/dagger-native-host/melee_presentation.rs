use std::collections::BTreeMap;

use anyhow::{Context, Result};
use dagger_runtime::{
    AudioAsset, CombatAssetCatalog, EffectAsset, MeleePresentationPhase, MeleePresentationReadout,
    WeaponAnimation,
};
use rusty_engine::{
    render_model::{
        BillboardMode, Geometry, Material, RenderDiff, RenderFrameDiff, RenderHandle, RenderLayer,
        RenderMetadata, RenderNode, SpriteAttachment, SpriteDepthPolicy, SpriteInstanceDescriptor,
        SpriteShading, SpriteSizeMode, Transform,
    },
    render_presentation::{
        AudioBus, AudioClipRef, AudioEmitter, AudioProjectionOp, AudioSourceDescriptor,
        PresentationFrameDiff, PresentationOp, PresentationOpMeta,
    },
    render_projection::{RenderHandleNamespace, RetainedNodeProjector},
};

const WEAPON_ID: &str = "weapon.dagger.steel";
const IDLE_ACTION: &str = "idle";
const STRIKE_ACTION: &str = "strikeDown";
const BLOOD_EFFECT_ID: &str = "effect.blood.0";
const SWING_AUDIO_ID: &str = "audio.melee.dagger.swing";
const WEAPON_SPRITE_HANDLE: RenderHandle = RenderHandle::new((7_u64 << 40) | 1);
const IMPACT_SPRITE_HANDLE: RenderHandle = RenderHandle::new((7_u64 << 40) | 2);
// Engine's current public application host uses a 55-degree perspective
// viewmodel camera. At z=-1 this height fills that camera vertically; the
// generated 320x200 reference canvas supplies the matching horizontal span.
const CLASSIC_VIEWMODEL_HEIGHT: f32 = 1.04;
const CLASSIC_VIEWMODEL_DEPTH: f32 = -1.0;

#[derive(Debug, Clone, Copy, PartialEq, Eq, PartialOrd, Ord)]
enum MeleeNode {
    ViewmodelRoot,
}

/// Dagger-owned first-person weapon presentation projected through Engine's
/// bounded camera-relative layer. The selected resource and frame ranges come
/// from the generated semantic catalog; browser code owns neither the weapon
/// choice nor animation timing.
pub(crate) struct MeleePresentation {
    projector: RetainedNodeProjector<MeleeNode>,
    root: BTreeMap<MeleeNode, RenderNode>,
    weapon_asset: String,
    weapon_size: [f32; 2],
    pivot: [f32; 2],
    idle: WeaponAnimation,
    strike: WeaponAnimation,
    blood: EffectAsset,
    swing_audio: AudioAsset,
    hit_audio: Vec<AudioAsset>,
    swing_emitted_for: Option<u64>,
    hit_emitted_for: Option<u64>,
    sprite_created: bool,
    current_frame: u32,
    impact_created: bool,
    impact_frame: u32,
    impact_position: [f32; 3],
}

impl MeleePresentation {
    pub(crate) fn from_catalog(catalog_text: &str) -> Result<Self> {
        let catalog = CombatAssetCatalog::from_json(catalog_text).map_err(anyhow::Error::msg)?;
        let weapon = catalog
            .weapon(WEAPON_ID)
            .with_context(|| format!("combat catalog is missing {WEAPON_ID}"))?;
        let idle = weapon
            .animation(IDLE_ACTION)
            .with_context(|| format!("{WEAPON_ID} is missing {IDLE_ACTION}"))?
            .clone();
        let strike = weapon
            .animation(STRIKE_ACTION)
            .with_context(|| format!("{WEAPON_ID} is missing {STRIKE_ACTION}"))?
            .clone();
        let blood = catalog
            .effect(BLOOD_EFFECT_ID)
            .with_context(|| format!("combat catalog is missing {BLOOD_EFFECT_ID}"))?
            .clone();
        let swing_audio = catalog
            .audio(SWING_AUDIO_ID)
            .with_context(|| format!("combat catalog is missing {SWING_AUDIO_ID}"))?
            .clone();
        let hit_audio = (1..=5)
            .map(|index| {
                let id = format!("audio.melee.hit.{index}");
                catalog
                    .audio(&id)
                    .with_context(|| format!("combat catalog is missing {id}"))
                    .cloned()
            })
            .collect::<Result<Vec<_>>>()?;
        let mut root = BTreeMap::new();
        root.insert(MeleeNode::ViewmodelRoot, viewmodel_root());
        let weapon_size = [
            CLASSIC_VIEWMODEL_HEIGHT * weapon.reference_size[0] as f32
                / weapon.reference_size[1] as f32,
            CLASSIC_VIEWMODEL_HEIGHT,
        ];
        Ok(Self {
            projector: RetainedNodeProjector::new(RenderHandleNamespace::PRESENTATION),
            root,
            weapon_asset: weapon.sprite_asset_id(),
            weapon_size,
            pivot: weapon.pivot,
            current_frame: idle.frame_start,
            idle,
            strike,
            impact_frame: blood.frames[0].frame,
            blood,
            swing_audio,
            hit_audio,
            swing_emitted_for: None,
            hit_emitted_for: None,
            sprite_created: false,
            impact_created: false,
            impact_position: [0.0, 0.0, 0.0],
        })
    }

    /// Project classic one-shot audio from the same Rust-owned melee action
    /// timeline as the weapon and impact sprites. Signal identity is stable per
    /// accepted attempt so polling and repeated diagnostic ticks cannot replay
    /// a sound.
    pub(crate) fn audio_tick(
        &mut self,
        action: Option<&MeleePresentationReadout>,
    ) -> Result<PresentationFrameDiff> {
        let Some(action) = action.filter(|action| action.accepted) else {
            return Ok(PresentationFrameDiff::new());
        };
        let mut ops = Vec::new();
        if self.swing_emitted_for != Some(action.attempt_sequence) {
            let sequence = u32::try_from(ops.len()).context("too many melee audio operations")?;
            ops.push(audio_emit(
                sequence,
                format!("dagger-swing-{}", action.attempt_sequence),
                &self.swing_audio,
                0.85,
            ));
            self.swing_emitted_for = Some(action.attempt_sequence);
        }
        let hit_active = matches!(action.outcome.as_str(), "hit" | "killed")
            && matches!(
                action.phase,
                MeleePresentationPhase::Contact | MeleePresentationPhase::Recovery
            );
        if hit_active && self.hit_emitted_for != Some(action.attempt_sequence) {
            let index = action.attempt_sequence.saturating_sub(1) as usize % self.hit_audio.len();
            let sequence = u32::try_from(ops.len()).context("too many melee audio operations")?;
            ops.push(audio_emit(
                sequence,
                format!("dagger-hit-{}", action.attempt_sequence),
                &self.hit_audio[index],
                1.0,
            ));
            self.hit_emitted_for = Some(action.attempt_sequence);
        }
        PresentationFrameDiff::try_from_ops(ops)
            .map_err(|error| anyhow::anyhow!("build classic melee audio: {error:?}"))
    }

    pub(crate) fn tick(
        &mut self,
        action: Option<&MeleePresentationReadout>,
        _stamina: f32,
        _max_stamina: f32,
        impact_position: Option<[f32; 3]>,
    ) -> Result<RenderFrameDiff> {
        let mut ops = self
            .projector
            .project(self.root.clone())
            .map_err(|error| anyhow::anyhow!("project melee viewmodel root: {error:?}"))?
            .ops;
        let root = self
            .projector
            .handle_of(&MeleeNode::ViewmodelRoot)
            .context("melee viewmodel root has no retained handle")?;
        let frame = weapon_frame(&self.idle, &self.strike, action);
        if !self.sprite_created {
            ops.push(RenderDiff::CreateSprite {
                handle: WEAPON_SPRITE_HANDLE,
                parent: Some(root),
                sprite: weapon_sprite(&self.weapon_asset, self.weapon_size, self.pivot, frame),
            });
            self.sprite_created = true;
        } else if frame != self.current_frame {
            ops.push(RenderDiff::UpdateSprite {
                handle: WEAPON_SPRITE_HANDLE,
                frame: Some(frame),
                tint: None,
                render_order: None,
                visible: None,
            });
        }
        self.current_frame = frame;
        self.project_impact(&mut ops, action, impact_position);
        RenderFrameDiff::try_from_ops(ops)
            .map_err(|error| anyhow::anyhow!("build classic melee presentation: {error:?}"))
    }

    pub(crate) fn snapshot(&self) -> Result<RenderFrameDiff> {
        if !self.sprite_created {
            return Ok(RenderFrameDiff::new());
        }
        let root = self
            .projector
            .handle_of(&MeleeNode::ViewmodelRoot)
            .context("melee viewmodel root has no retained handle")?;
        let mut ops = vec![
            RenderDiff::Create {
                handle: root,
                parent: None,
                node: self.root[&MeleeNode::ViewmodelRoot].clone(),
            },
            RenderDiff::CreateSprite {
                handle: WEAPON_SPRITE_HANDLE,
                parent: Some(root),
                sprite: weapon_sprite(
                    &self.weapon_asset,
                    self.weapon_size,
                    self.pivot,
                    self.current_frame,
                ),
            },
        ];
        if self.impact_created {
            ops.push(RenderDiff::CreateSprite {
                handle: IMPACT_SPRITE_HANDLE,
                parent: None,
                sprite: impact_sprite(
                    &self.blood.sprite_asset_id(),
                    self.blood.pivot,
                    self.impact_frame,
                    self.impact_position,
                ),
            });
        }
        RenderFrameDiff::try_from_ops(ops)
            .map_err(|error| anyhow::anyhow!("snapshot classic melee presentation: {error:?}"))
    }

    pub(crate) fn retained_len(&self) -> usize {
        usize::from(self.sprite_created)
            + usize::from(self.impact_created)
            + self.projector.retained_len()
    }

    pub(crate) fn dispose(&mut self) -> Result<RenderFrameDiff> {
        let mut ops = Vec::new();
        if self.sprite_created {
            ops.push(RenderDiff::Destroy {
                handle: WEAPON_SPRITE_HANDLE,
            });
            self.sprite_created = false;
        }
        if self.impact_created {
            ops.push(RenderDiff::Destroy {
                handle: IMPACT_SPRITE_HANDLE,
            });
            self.impact_created = false;
        }
        ops.extend(
            self.projector
                .project(BTreeMap::new())
                .map_err(|error| anyhow::anyhow!("dispose melee viewmodel root: {error:?}"))?
                .ops,
        );
        RenderFrameDiff::try_from_ops(ops)
            .map_err(|error| anyhow::anyhow!("dispose classic melee presentation: {error:?}"))
    }

    fn project_impact(
        &mut self,
        ops: &mut Vec<RenderDiff>,
        action: Option<&MeleePresentationReadout>,
        impact_position: Option<[f32; 3]>,
    ) {
        let active = action.filter(|action| {
            action.accepted
                && matches!(action.outcome.as_str(), "hit" | "killed")
                && matches!(
                    action.phase,
                    MeleePresentationPhase::Contact | MeleePresentationPhase::Recovery
                )
        });
        let Some((action, position)) = active.zip(impact_position) else {
            if self.impact_created {
                ops.push(RenderDiff::Destroy {
                    handle: IMPACT_SPRITE_HANDLE,
                });
                self.impact_created = false;
            }
            return;
        };
        let progress = match action.phase {
            MeleePresentationPhase::Contact => action.phase_progress * 0.45,
            MeleePresentationPhase::Recovery => 0.45 + action.phase_progress * 0.55,
            _ => 0.0,
        }
        .clamp(0.0, 0.999_999);
        let frame_index = (progress * self.blood.frames.len() as f32) as usize;
        let frame = self.blood.frames[frame_index].frame;
        let position = [position[0], position[1] + 0.55, position[2]];
        if !self.impact_created {
            ops.push(RenderDiff::CreateSprite {
                handle: IMPACT_SPRITE_HANDLE,
                parent: None,
                sprite: impact_sprite(
                    &self.blood.sprite_asset_id(),
                    self.blood.pivot,
                    frame,
                    position,
                ),
            });
            self.impact_created = true;
        } else {
            if position != self.impact_position {
                ops.push(RenderDiff::Update {
                    handle: IMPACT_SPRITE_HANDLE,
                    transform: Some(Transform {
                        translation: position,
                        ..Transform::IDENTITY
                    }),
                    material: None,
                    visible: None,
                    metadata: None,
                });
            }
            if frame != self.impact_frame {
                ops.push(RenderDiff::UpdateSprite {
                    handle: IMPACT_SPRITE_HANDLE,
                    frame: Some(frame),
                    tint: None,
                    render_order: None,
                    visible: None,
                });
            }
        }
        self.impact_frame = frame;
        self.impact_position = position;
    }
}

fn audio_emit(sequence: u32, signal_id: String, audio: &AudioAsset, volume: f32) -> PresentationOp {
    let hash_hex = audio
        .sha256
        .strip_prefix("sha256:")
        .unwrap_or(&audio.sha256);
    PresentationOp::Audio {
        meta: PresentationOpMeta::new(sequence),
        op: AudioProjectionOp::Emit {
            signal_id,
            descriptor: AudioSourceDescriptor {
                clip: AudioClipRef {
                    asset: format!("audio-resource/{hash_hex}"),
                    content_hash: audio.sha256.clone(),
                },
                bus: AudioBus::Sfx,
                volume,
                pitch: 1.0,
                looping: false,
                spatial_blend: 0.0,
                attenuation: 1.0,
                pan: 0.0,
                emitter: AudioEmitter::Global2d,
            },
        },
    }
}

fn weapon_frame(
    idle: &WeaponAnimation,
    strike: &WeaponAnimation,
    action: Option<&MeleePresentationReadout>,
) -> u32 {
    let Some(action) = action.filter(|action| action.accepted) else {
        return idle.frame_start;
    };
    let overall = match action.phase {
        MeleePresentationPhase::Anticipation => action.phase_progress * 0.2,
        MeleePresentationPhase::Contact => 0.2 + action.phase_progress * 0.55,
        MeleePresentationPhase::Recovery => 0.75 + action.phase_progress * 0.25,
        MeleePresentationPhase::Rejected => return idle.frame_start,
    }
    .clamp(0.0, 0.999_999);
    strike.frame_start + (overall * strike.frame_count as f32) as u32
}

fn viewmodel_root() -> RenderNode {
    RenderNode {
        geometry: Geometry::Group,
        material: Material::DEFAULT,
        transform: Transform::IDENTITY,
        visible: true,
        layer: RenderLayer::Viewmodel,
        metadata: RenderMetadata {
            source_entity: None,
            source_scene_node: None,
            tags: vec!["dagger-melee".to_string()],
            label: Some("classic-weapon-viewmodel".to_string()),
        },
    }
}

fn weapon_sprite(
    asset: &str,
    size: [f32; 2],
    pivot: [f32; 2],
    frame: u32,
) -> SpriteInstanceDescriptor {
    SpriteInstanceDescriptor {
        asset: asset.to_string(),
        frame,
        pivot,
        // The generated fixed cell is a complete 320x200 classic screen
        // canvas. Its transparent placement preserves DFU's per-action
        // left/right alignment while this quad remains stable across frames.
        size,
        size_mode: SpriteSizeMode::World,
        billboard: BillboardMode::None,
        tint: [1.0, 1.0, 1.0, 1.0],
        render_order: 100,
        depth: SpriteDepthPolicy::DepthTestOff,
        shading: SpriteShading::Unlit,
        material: Default::default(),
        visible: true,
        transform: Transform {
            translation: [
                0.0,
                -CLASSIC_VIEWMODEL_HEIGHT / 2.0,
                CLASSIC_VIEWMODEL_DEPTH,
            ],
            rotation: [0.0, 0.0, 0.0, 1.0],
            scale: [1.0, 1.0, 1.0],
        },
        attachment: SpriteAttachment::default(),
        metadata: RenderMetadata {
            source_entity: None,
            source_scene_node: None,
            tags: vec!["classic-combat".to_string(), "dagger-melee".to_string()],
            label: Some("classic-dagger-weapon".to_string()),
        },
    }
}

fn impact_sprite(
    asset: &str,
    pivot: [f32; 2],
    frame: u32,
    position: [f32; 3],
) -> SpriteInstanceDescriptor {
    SpriteInstanceDescriptor {
        asset: asset.to_string(),
        frame,
        pivot,
        size: [0.75, 0.34],
        size_mode: SpriteSizeMode::World,
        billboard: BillboardMode::Spherical,
        tint: [1.0, 1.0, 1.0, 1.0],
        render_order: 0,
        depth: SpriteDepthPolicy::Default,
        shading: SpriteShading::Unlit,
        material: Default::default(),
        visible: true,
        transform: Transform {
            translation: position,
            ..Transform::IDENTITY
        },
        attachment: SpriteAttachment::default(),
        metadata: RenderMetadata {
            source_entity: None,
            source_scene_node: None,
            tags: vec!["classic-combat".to_string(), "world-impact".to_string()],
            label: Some("classic-blood-impact".to_string()),
        },
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    const CATALOG: &str = include_str!("../../../../../content/textures/combat-manifest.json");

    fn action(phase: MeleePresentationPhase, progress: f32) -> MeleePresentationReadout {
        MeleePresentationReadout {
            attempt_sequence: 1,
            phase,
            phase_progress: progress,
            accepted: true,
            outcome: "miss".to_string(),
            target_id: None,
            stamina_before: 10.0,
            stamina_after: 9.0,
            target_health_before: None,
            target_health_after: None,
            target_max_health: None,
            final_damage: None,
            died: false,
        }
    }

    #[test]
    fn semantic_classic_weapon_is_a_camera_relative_sprite_with_stable_identity() {
        let mut presentation = MeleePresentation::from_catalog(CATALOG).expect("catalog");
        let first = presentation
            .tick(None, 10.0, 10.0, None)
            .expect("idle frame");
        assert!(first.ops.iter().any(|op| matches!(
            op,
            RenderDiff::Create { node, .. }
                if node.layer == RenderLayer::Viewmodel
                    && node.metadata.label.as_deref() == Some("classic-weapon-viewmodel")
        )));
        assert!(first.ops.iter().any(|op| matches!(
            op,
            RenderDiff::CreateSprite { sprite, .. }
                if sprite.asset == "sprite/weapon-dagger-steel-atlas"
                    && sprite.frame == 0
                    && sprite.pivot == [0.5, 0.0]
                    && sprite.size == [
                        CLASSIC_VIEWMODEL_HEIGHT * 320.0 / 200.0,
                        CLASSIC_VIEWMODEL_HEIGHT,
                    ]
                    && sprite.transform.translation == [0.0, -0.52, -1.0]
        )));

        let contact = presentation
            .tick(
                Some(&action(MeleePresentationPhase::Contact, 0.5)),
                9.0,
                10.0,
                None,
            )
            .expect("strike frame");
        assert!(contact.ops.iter().any(|op| matches!(
            op,
            RenderDiff::UpdateSprite { handle, frame: Some(frame), .. }
                if *handle == WEAPON_SPRITE_HANDLE && *frame >= 1 && *frame <= 5
        )));
    }

    #[test]
    fn semantic_blood_is_a_bounded_world_sprite_only_for_a_hit() {
        let mut presentation = MeleePresentation::from_catalog(CATALOG).expect("catalog");
        presentation.tick(None, 10.0, 10.0, None).unwrap();
        let mut hit = action(MeleePresentationPhase::Contact, 0.5);
        hit.outcome = "hit".to_string();
        hit.target_id = Some(2007);
        let frame = presentation
            .tick(Some(&hit), 9.0, 10.0, Some([2.0, 3.0, 4.0]))
            .expect("world impact frame");
        assert!(frame.ops.iter().any(|op| matches!(
            op,
            RenderDiff::CreateSprite { parent: None, sprite, .. }
                if sprite.asset == "sprite/effect-blood-0-atlas"
                    && sprite.size == [0.75, 0.34]
                    && sprite.depth == SpriteDepthPolicy::Default
                    && sprite.transform.translation == [2.0, 3.55, 4.0]
        )));
        hit.outcome = "miss".to_string();
        let miss = presentation
            .tick(Some(&hit), 9.0, 10.0, Some([2.0, 3.0, 4.0]))
            .expect("miss frame");
        assert!(miss.ops.iter().any(|op| matches!(
            op,
            RenderDiff::Destroy { handle } if *handle == IMPACT_SPRITE_HANDLE
        )));
    }

    #[test]
    fn classic_audio_emits_swing_once_and_silences_a_miss() {
        let mut presentation = MeleePresentation::from_catalog(CATALOG).expect("catalog");
        let anticipation = action(MeleePresentationPhase::Anticipation, 0.0);
        let first = presentation
            .audio_tick(Some(&anticipation))
            .expect("swing audio");
        assert_eq!(first.ops.len(), 1);
        assert!(matches!(
            &first.ops[0],
            PresentationOp::Audio {
                op: AudioProjectionOp::Emit { signal_id, descriptor }, ..
            } if signal_id == "dagger-swing-1"
                && descriptor.clip.asset.starts_with("audio-resource/")
                && descriptor.clip.content_hash == presentation.swing_audio.sha256
        ));
        assert!(presentation
            .audio_tick(Some(&anticipation))
            .expect("held anticipation")
            .is_empty());
        assert!(presentation
            .audio_tick(Some(&action(MeleePresentationPhase::Contact, 0.5)))
            .expect("contact miss")
            .is_empty());
    }

    #[test]
    fn classic_audio_emits_one_deterministic_hit_at_contact() {
        let mut presentation = MeleePresentation::from_catalog(CATALOG).expect("catalog");
        let mut hit = action(MeleePresentationPhase::Contact, 0.0);
        hit.attempt_sequence = 2;
        hit.outcome = "hit".to_string();
        let frame = presentation.audio_tick(Some(&hit)).expect("hit audio");
        assert_eq!(
            frame.ops.len(),
            2,
            "late observation includes swing and hit"
        );
        assert!(matches!(
            &frame.ops[1],
            PresentationOp::Audio {
                op: AudioProjectionOp::Emit { signal_id, descriptor }, ..
            } if signal_id == "dagger-hit-2"
                && descriptor.clip.content_hash == presentation.hit_audio[1].sha256
        ));
        assert!(presentation
            .audio_tick(Some(&hit))
            .expect("held contact")
            .is_empty());
    }
}
