use std::collections::BTreeSet;

use rusty_engine::product_kernel::serde::Deserialize;

#[derive(Debug, Clone, Deserialize)]
#[serde(crate = "rusty_engine::product_kernel::serde")]
#[serde(rename_all = "camelCase")]
pub struct CombatAssetCatalog {
    pub schema_version: u32,
    pub clone_baseline: String,
    pub weapon: WeaponAsset,
    pub effects: Vec<EffectAsset>,
    pub audio: Vec<AudioAsset>,
}

#[derive(Debug, Clone, Deserialize)]
#[serde(crate = "rusty_engine::product_kernel::serde")]
#[serde(rename_all = "camelCase")]
pub struct WeaponAsset {
    pub id: String,
    pub texture_asset_id: String,
    pub width: u32,
    pub height: u32,
    pub reference_size: [u32; 2],
    pub pivot: [f32; 2],
    pub frames: Vec<CombatFrame>,
    pub animations: Vec<WeaponAnimation>,
}

#[derive(Debug, Clone, Deserialize)]
#[serde(crate = "rusty_engine::product_kernel::serde")]
#[serde(rename_all = "camelCase")]
pub struct WeaponAnimation {
    pub action: String,
    pub fps: f32,
    pub alignment: String,
    pub screen_offset: f32,
    pub frame_start: u32,
    pub frame_count: u32,
}

#[derive(Debug, Clone, Deserialize)]
#[serde(crate = "rusty_engine::product_kernel::serde")]
#[serde(rename_all = "camelCase")]
pub struct EffectAsset {
    pub id: String,
    pub texture_asset_id: String,
    pub width: u32,
    pub height: u32,
    pub fps: f32,
    #[serde(rename = "loop")]
    pub loops: bool,
    pub pivot: [f32; 2],
    pub frames: Vec<CombatFrame>,
}

#[derive(Debug, Clone, Deserialize)]
#[serde(crate = "rusty_engine::product_kernel::serde")]
#[serde(rename_all = "camelCase")]
pub struct AudioAsset {
    pub id: String,
    pub path: String,
    pub sha256: String,
    pub byte_length: u64,
    pub mime_type: String,
}

#[derive(Debug, Clone, Deserialize)]
#[serde(crate = "rusty_engine::product_kernel::serde")]
#[serde(rename_all = "camelCase")]
pub struct CombatFrame {
    pub frame: u32,
    pub uv_min: [f32; 2],
    pub uv_max: [f32; 2],
    pub source_size: [u32; 2],
    pub source_offset: [i32; 2],
}

impl CombatAssetCatalog {
    pub fn from_json(document: &str) -> Result<Self, String> {
        let catalog: Self = rusty_engine::product_kernel::serde_json::from_str(document)
            .map_err(|error| format!("decode combat asset catalog: {error}"))?;
        catalog.validate()?;
        Ok(catalog)
    }

    pub fn weapon(&self, id: &str) -> Option<&WeaponAsset> {
        (self.weapon.id == id).then_some(&self.weapon)
    }

    pub fn effect(&self, id: &str) -> Option<&EffectAsset> {
        self.effects.iter().find(|effect| effect.id == id)
    }

    pub fn audio(&self, id: &str) -> Option<&AudioAsset> {
        self.audio.iter().find(|audio| audio.id == id)
    }

    fn validate(&self) -> Result<(), String> {
        if self.schema_version != 1 {
            return Err(format!(
                "unsupported combat asset catalog schema {}",
                self.schema_version
            ));
        }
        if self.clone_baseline.trim().is_empty() {
            return Err("combat asset catalog clone baseline is empty".to_string());
        }
        validate_asset_identity(&self.weapon.id, &self.weapon.texture_asset_id)?;
        validate_dimensions(self.weapon.width, self.weapon.height, &self.weapon.id)?;
        validate_dimensions(
            self.weapon.reference_size[0],
            self.weapon.reference_size[1],
            &format!("{} reference canvas", self.weapon.id),
        )?;
        validate_pivot(self.weapon.pivot, &self.weapon.id)?;
        validate_frames(&self.weapon.frames, &self.weapon.id)?;
        let mut actions = BTreeSet::new();
        for animation in &self.weapon.animations {
            if !actions.insert(animation.action.as_str()) {
                return Err(format!("duplicate weapon action {}", animation.action));
            }
            if animation.fps <= 0.0 || !animation.fps.is_finite() {
                return Err(format!(
                    "weapon action {} has invalid fps",
                    animation.action
                ));
            }
            if animation.alignment != "left" && animation.alignment != "right" {
                return Err(format!(
                    "weapon action {} has invalid alignment {}",
                    animation.action, animation.alignment
                ));
            }
            if !animation.screen_offset.is_finite() {
                return Err(format!(
                    "weapon action {} has invalid screen offset",
                    animation.action
                ));
            }
            let end = animation
                .frame_start
                .checked_add(animation.frame_count)
                .ok_or_else(|| {
                    format!("weapon action {} frame range overflows", animation.action)
                })?;
            if animation.frame_count == 0 || end as usize > self.weapon.frames.len() {
                return Err(format!(
                    "weapon action {} frame range exceeds atlas",
                    animation.action
                ));
            }
        }
        for required in ["idle", "strikeDown"] {
            if !actions.contains(required) {
                return Err(format!(
                    "combat weapon is missing required action {required}"
                ));
            }
        }

        let mut ids = BTreeSet::from([self.weapon.id.as_str()]);
        for effect in &self.effects {
            validate_asset_identity(&effect.id, &effect.texture_asset_id)?;
            if !ids.insert(effect.id.as_str()) {
                return Err(format!("duplicate combat semantic id {}", effect.id));
            }
            validate_dimensions(effect.width, effect.height, &effect.id)?;
            validate_pivot(effect.pivot, &effect.id)?;
            validate_frames(&effect.frames, &effect.id)?;
            if effect.fps <= 0.0 || !effect.fps.is_finite() || effect.loops {
                return Err(format!("effect {} must be a finite one-shot", effect.id));
            }
        }
        for audio in &self.audio {
            if !ids.insert(audio.id.as_str()) {
                return Err(format!("duplicate combat semantic id {}", audio.id));
            }
            if !audio.id.starts_with("audio.")
                || audio.path.trim().is_empty()
                || !audio.sha256.starts_with("sha256:")
                || audio.byte_length == 0
                || audio.mime_type != "audio/wav"
            {
                return Err(format!("audio {} has invalid publication facts", audio.id));
            }
        }
        Ok(())
    }
}

impl WeaponAsset {
    pub fn animation(&self, action: &str) -> Option<&WeaponAnimation> {
        self.animations
            .iter()
            .find(|animation| animation.action == action)
    }

    pub fn sprite_asset_id(&self) -> String {
        sprite_asset_id(&self.texture_asset_id)
    }
}

impl EffectAsset {
    pub fn sprite_asset_id(&self) -> String {
        sprite_asset_id(&self.texture_asset_id)
    }
}

fn sprite_asset_id(texture_asset_id: &str) -> String {
    texture_asset_id
        .strip_prefix("texture/")
        .map(|suffix| format!("sprite/{suffix}"))
        .unwrap_or_else(|| format!("sprite/{texture_asset_id}"))
}

fn validate_asset_identity(id: &str, texture_asset_id: &str) -> Result<(), String> {
    if id.trim().is_empty() || !texture_asset_id.starts_with("texture/") {
        Err(format!(
            "combat asset {id:?} has invalid semantic or texture identity"
        ))
    } else {
        Ok(())
    }
}

fn validate_dimensions(width: u32, height: u32, id: &str) -> Result<(), String> {
    if width == 0 || height == 0 {
        Err(format!("combat asset {id} has zero dimensions"))
    } else {
        Ok(())
    }
}

fn validate_pivot(pivot: [f32; 2], id: &str) -> Result<(), String> {
    if pivot
        .into_iter()
        .all(|value| value.is_finite() && (0.0..=1.0).contains(&value))
    {
        Ok(())
    } else {
        Err(format!("combat asset {id} has invalid pivot"))
    }
}

fn validate_frames(frames: &[CombatFrame], id: &str) -> Result<(), String> {
    if frames.is_empty() {
        return Err(format!("combat asset {id} has no frames"));
    }
    let mut seen = BTreeSet::new();
    for frame in frames {
        if !seen.insert(frame.frame)
            || frame.source_size.contains(&0)
            || !frame
                .uv_min
                .iter()
                .chain(frame.uv_max.iter())
                .all(|value| value.is_finite() && (0.0..=1.0).contains(value))
            || frame.uv_max[0] <= frame.uv_min[0]
            || frame.uv_max[1] <= frame.uv_min[1]
        {
            return Err(format!("combat asset {id} has an invalid frame"));
        }
        let _ = frame.source_offset;
    }
    Ok(())
}
