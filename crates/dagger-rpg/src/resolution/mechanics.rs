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
//!
//! The item vocabulary binds the same way: compiled items become upstream
//! item definitions (fungible stacks vs unique entities, weight as capacity
//! cost in quarter-kg units, equipment policy), and the package's equipment
//! section becomes the upstream capacity metrics and equipment slots. Damage
//! responses against equipped armor arrive with 7072 via upstream sources.

use std::collections::BTreeMap;

use rusty_engine::gameplay_mechanics::{
    CapacityMetricDefinition, CapacityMetricId, CatalogVersion, EquipmentExclusivityId,
    EquipmentSlotDefinition, EquipmentSlotId, ItemCapacityCost, ItemClassificationId,
    ItemDefinition, ItemDefinitionId, ItemEquipmentPolicy, ItemKind, MechanicsCatalog,
    MechanicsCatalogDefinition, MechanicsScalar, StatDefinition, StatId, TrackDefinition, TrackId,
    TrackMaximum,
};

use super::compile::WEIGHT_CAPACITY_METRIC;
use super::{
    DaggerEquipmentSection, DaggerGameplayError, DaggerItemDefinition, DaggerStatsSection,
};

pub const MECHANICS_CATALOG_VERSION: &str = "dagger-core-v1";
pub const CLASSIC_STAT_MINIMUM: i64 = 0;
pub const CLASSIC_STAT_MAXIMUM: i64 = 100;
pub const TRACK_MAX_STAT_MAXIMUM: i64 = 1_000_000;

/// Fungible stack cap: a generous classic-plausible ceiling (classic stacks
/// arrows and gold without a small fixed cap).
pub const FUNGIBLE_MAXIMUM_QUANTITY: u64 = 1_000_000;

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

fn mechanics_id<T, E, F>(path: &str, id: &str, parse: F) -> Result<T, DaggerGameplayError>
where
    F: FnOnce(String) -> Result<T, E>,
    E: std::fmt::Debug,
{
    parse(id.to_string()).map_err(|error| DaggerGameplayError::InvalidId {
        path: path.to_string(),
        value: format!("{id}: {error:?}"),
    })
}

/// Compile the item vocabulary into upstream item definitions: fungible
/// stacks for block-less items (gold, arrows), unique entities for
/// equippable ones, weight as a `weight` capacity cost in quarter-kg units,
/// and the Dagger-owned equipment policy.
fn compile_upstream_items(
    items: &BTreeMap<String, DaggerItemDefinition>,
) -> Result<Vec<ItemDefinition>, DaggerGameplayError> {
    let mut definitions = Vec::with_capacity(items.len());
    for item in items.values() {
        let path = format!("items[{}]", item.id);
        let capacity_costs = if item.weight_units > 0 {
            vec![ItemCapacityCost {
                metric: mechanics_id(
                    &format!("{path}.capacityCost"),
                    WEIGHT_CAPACITY_METRIC,
                    CapacityMetricId::parse,
                )?,
                units: item.weight_units,
            }]
        } else {
            Vec::new()
        };
        let classifications = item
            .classifications
            .iter()
            .map(|classification| {
                mechanics_id(
                    &format!("{path}.classifications"),
                    classification,
                    ItemClassificationId::parse,
                )
            })
            .collect::<Result<Vec<_>, _>>()?;
        let equipment = item
            .equipment
            .as_ref()
            .map(|policy| {
                Ok(ItemEquipmentPolicy {
                    required_slots: policy.required_slots,
                    exclusive_group: policy
                        .exclusive_group
                        .as_ref()
                        .map(|group| {
                            mechanics_id(
                                &format!("{path}.exclusiveGroup"),
                                group,
                                EquipmentExclusivityId::parse,
                            )
                        })
                        .transpose()?,
                })
            })
            .transpose()?;
        definitions.push(ItemDefinition {
            id: mechanics_id(&format!("{path}.id"), &item.id, ItemDefinitionId::parse)?,
            kind: if item.fungible {
                ItemKind::Fungible
            } else {
                ItemKind::Unique
            },
            maximum_quantity: if item.fungible {
                FUNGIBLE_MAXIMUM_QUANTITY
            } else {
                1
            },
            classifications,
            capacity_costs,
            equipment,
            // Item-borne damage responses (armor absorption) arrive with
            // 7072 through upstream damage responses.
            sources: Vec::new(),
        });
    }
    Ok(definitions)
}

/// Build the mechanics catalog for one admitted package: stats/tracks from
/// the declared vocabulary plus items, equipment slots, and capacity metrics
/// from the item/equipment sections. Called once from package admission; the
/// result travels with the Dagger catalog so every consumer shares one
/// mechanics identity.
pub fn compile_mechanics_catalog(
    section: &DaggerStatsSection,
    items: &BTreeMap<String, DaggerItemDefinition>,
    equipment: &DaggerEquipmentSection,
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
    let capacity_metrics = equipment
        .capacity_metrics
        .iter()
        .map(|id| {
            Ok(CapacityMetricDefinition {
                id: mechanics_id("equipment.capacityMetrics", id, CapacityMetricId::parse)?,
            })
        })
        .collect::<Result<Vec<_>, DaggerGameplayError>>()?;
    let equipment_slots = equipment
        .slots
        .iter()
        .map(|slot| {
            Ok(EquipmentSlotDefinition {
                id: mechanics_id(
                    &format!("equipment.slots[{}]", slot.id),
                    &slot.id,
                    EquipmentSlotId::parse,
                )?,
                allowed_classifications: slot
                    .allowed_classifications
                    .iter()
                    .map(|classification| {
                        mechanics_id(
                            &format!("equipment.slots[{}]", slot.id),
                            classification,
                            ItemClassificationId::parse,
                        )
                    })
                    .collect::<Result<Vec<_>, DaggerGameplayError>>()?,
            })
        })
        .collect::<Result<Vec<_>, DaggerGameplayError>>()?;
    MechanicsCatalog::admit(MechanicsCatalogDefinition {
        version: mechanics_catalog_version(),
        stats,
        tracks,
        // No attributed sources, damage kinds, or effects exist in the
        // current slice. Damage kinds/responses arrive with resistances and
        // 7072's item-borne damage responses; sources and effects with
        // spell effects.
        sources: Vec::new(),
        damage_kinds: Vec::new(),
        effects: Vec::new(),
        capacity_metrics,
        items: compile_upstream_items(items)?,
        equipment_slots,
    })
    .map_err(|error| DaggerGameplayError::InvalidValue {
        path: "mechanicsCatalog".to_string(),
        reason: format!("mechanics catalog admission rejected: {error:?}"),
    })
}
