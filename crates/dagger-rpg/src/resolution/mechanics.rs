//! Binding from the admitted Dagger gameplay package to the Engine's
//! gameplay-mechanics catalog and components.
//!
//! Dagger owns every identifier and meaning; the Engine supplies the neutral
//! stat/track storage, evaluation, and mutation services. Each declared
//! attribute and skill becomes a mechanics stat (classic bounds 0..=100).
//! Each declared track gets a synthetic `{track}-max` stat so the track's
//! maximum is stat-derived: the Dagger expression evaluator computes the
//! maximum at spawn (derived rules are Dagger-owned, arbitrary expressions
//! the neutral catalog does not model), stores it as the entity's stat base,
//! and the mechanics track bound references it. Level-up style maximum
//! changes later flow through track reconciliation against a new stat base.

use rusty_engine::gameplay_mechanics::{
    CatalogVersion, MechanicsCatalog, MechanicsCatalogDefinition, MechanicsScalar, StatDefinition,
    StatId, TrackDefinition, TrackId, TrackMaximum,
};

use super::{DaggerGameplayError, DaggerStatsSection};

pub const MECHANICS_CATALOG_VERSION: &str = "dagger-core-v1";
pub const CLASSIC_STAT_MINIMUM: i64 = 0;
pub const CLASSIC_STAT_MAXIMUM: i64 = 100;
pub const TRACK_MAX_STAT_MAXIMUM: i64 = 1_000_000;

pub fn mechanics_catalog_version() -> CatalogVersion {
    CatalogVersion::parse(MECHANICS_CATALOG_VERSION).expect("fixed mechanics identity")
}

/// Synthetic stat holding one entity's evaluated maximum for a track.
pub fn track_max_stat_id(track: &str) -> String {
    format!("{track}-max")
}

fn scalar(value: i64, path: &str) -> Result<MechanicsScalar, DaggerGameplayError> {
    MechanicsScalar::new(value).map_err(|error| DaggerGameplayError::InvalidValue {
        path: path.to_string(),
        reason: format!("mechanics scalar rejected: {error:?}"),
    })
}

/// Build the mechanics catalog for one admitted stats section. Called once
/// from package admission; the result travels with the Dagger catalog so
/// every consumer shares one mechanics identity.
pub fn compile_mechanics_catalog(
    section: &DaggerStatsSection,
) -> Result<MechanicsCatalog, DaggerGameplayError> {
    let mut stats = Vec::new();
    for id in section.attributes.iter().chain(section.skills.iter()) {
        stats.push(StatDefinition {
            id: StatId::parse(id.clone()).map_err(|error| DaggerGameplayError::InvalidId {
                path: "stats".to_string(),
                value: format!("{id}: {error:?}"),
            })?,
            minimum: scalar(CLASSIC_STAT_MINIMUM, "stats.minimum")?,
            maximum: scalar(CLASSIC_STAT_MAXIMUM, "stats.maximum")?,
        });
    }
    let mut tracks = Vec::new();
    for id in &section.tracks {
        let max_stat = track_max_stat_id(id);
        stats.push(StatDefinition {
            id: StatId::parse(max_stat.clone()).map_err(|error| {
                DaggerGameplayError::InvalidId {
                    path: "stats".to_string(),
                    value: format!("{max_stat}: {error:?}"),
                }
            })?,
            minimum: scalar(CLASSIC_STAT_MINIMUM, "stats.minimum")?,
            maximum: scalar(TRACK_MAX_STAT_MAXIMUM, "stats.maximum")?,
        });
        tracks.push(TrackDefinition {
            id: TrackId::parse(id.clone()).map_err(|error| DaggerGameplayError::InvalidId {
                path: "tracks".to_string(),
                value: format!("{id}: {error:?}"),
            })?,
            minimum: scalar(CLASSIC_STAT_MINIMUM, "tracks.minimum")?,
            maximum: TrackMaximum::Stat {
                stat: StatId::parse(max_stat).expect("validated above"),
            },
        });
    }
    MechanicsCatalog::admit(MechanicsCatalogDefinition {
        version: mechanics_catalog_version(),
        stats,
        tracks,
        // No attributed sources, damage kinds, effects, items, capacity
        // metrics, or equipment slots exist in the current slice. Damage
        // kinds/responses arrive with resistances; sources and effects with
        // spell effects; item mechanics with the loot campaign (6721).
        sources: Vec::new(),
        damage_kinds: Vec::new(),
        effects: Vec::new(),
        capacity_metrics: Vec::new(),
        items: Vec::new(),
        equipment_slots: Vec::new(),
    })
    .map_err(|error| DaggerGameplayError::InvalidValue {
        path: "mechanicsCatalog".to_string(),
        reason: format!("mechanics catalog admission rejected: {error:?}"),
    })
}
