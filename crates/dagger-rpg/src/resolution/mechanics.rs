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
//! cost in quarter-kg units, equipment policy), the package's equipment
//! section becomes the upstream capacity metrics and equipment slots, and
//! each armor/shield item carries an attributed source that subtracts its
//! classic armor value (x5) from the covered `armor-<part>` stats while
//! equipped.

use std::collections::BTreeMap;

use rusty_engine::gameplay_mechanics::{
    CapacityMetricDefinition, CapacityMetricId, CatalogVersion, EquipmentExclusivityId,
    EquipmentSlotDefinition, EquipmentSlotId, ItemCapacityCost, ItemClassificationId,
    ItemDefinition, ItemDefinitionId, ItemEquipmentPolicy, ItemKind, MechanicsCatalog,
    MechanicsCatalogDefinition, MechanicsScalar, SourceDefinition, SourceDefinitionId,
    StackingGroupId, StackingPolicy, StatContribution, StatContributionDefinition, StatDefinition,
    StatId, TrackDefinition, TrackId, TrackMaximum,
};

use super::compile::WEIGHT_CAPACITY_METRIC;
use super::{
    armor_part_stat_id, DaggerEquipmentSection, DaggerGameplayError, DaggerItemDefinition,
    DaggerStatsSection,
};

pub const MECHANICS_CATALOG_VERSION: &str = "dagger-core-v1";
pub const CLASSIC_STAT_MINIMUM: i64 = 0;
pub const CLASSIC_STAT_MAXIMUM: i64 = 100;
pub const TRACK_MAX_STAT_MAXIMUM: i64 = 1_000_000;

/// Classic armor stats are signed bytes: good gear drives armor negative.
pub const ARMOR_PART_STAT_MINIMUM: i64 = -128;
pub const ARMOR_PART_STAT_MAXIMUM: i64 = 127;

/// One stacking group for every equipment-borne armor contribution (all
/// equipped pieces sum), plus the classic x5 multiplier the donor applies in
/// `UpdateEquippedArmorValues` (armor value x 5 subtracted per covered part).
pub const EQUIPMENT_STACKING_GROUP: &str = "equipment";
pub const ARMOR_CONTRIBUTION_SCALE: i64 = 5;

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

/// The body parts an armor or shield item protects (donor
/// `DaggerfallUnityItem.GetShieldProtectedBodyParts` — adopted). Armor pieces
/// cover their own part; shield coverage is derived from the per-type shield
/// value, which is the shield's classic identity (buckler 1 .. tower 4).
fn equipment_covered_parts(item: &DaggerItemDefinition) -> Option<Vec<&'static str>> {
    if let Some(armor) = &item.armor {
        let part: &'static str = match armor.piece.as_str() {
            "head" => "head",
            "chest" => "chest",
            "right-arm" => "right-arm",
            "left-arm" => "left-arm",
            "legs" => "legs",
            "hands" => "hands",
            "feet" => "feet",
            _ => return None,
        };
        return Some(vec![part]);
    }
    item.shield.as_ref().map(|shield| match shield.value {
        1 => vec!["left-arm", "hands"],
        2 | 3 => vec!["left-arm", "hands", "legs"],
        _ => vec!["head", "left-arm", "hands", "legs"],
    })
}

/// The equipment-borne armor source one item carries, when it is armor or a
/// shield: `-(value x 5)` added to each covered `armor-<part>` stat while
/// equipped (donor `UpdateEquippedArmorValues` — adopted).
fn equipment_source(
    item: &DaggerItemDefinition,
) -> Result<Option<(SourceDefinitionId, SourceDefinition)>, DaggerGameplayError> {
    let value = item
        .armor
        .as_ref()
        .map(|armor| armor.value)
        .or_else(|| item.shield.as_ref().map(|shield| shield.value));
    let (Some(value), Some(parts)) = (value, equipment_covered_parts(item)) else {
        return Ok(None);
    };
    if item.shield.is_some() && !(1..=4).contains(&value) {
        return Err(DaggerGameplayError::InvalidValue {
            path: format!("items[{}].shield.value", item.id),
            reason: format!("shield value {value} is outside the classic per-type range 1..=4"),
        });
    }
    let mut contributions = Vec::with_capacity(parts.len());
    for part in parts {
        contributions.push(StatContributionDefinition {
            stat: mechanics_id(
                &format!("items[{}].sources", item.id),
                &armor_part_stat_id(part),
                StatId::parse,
            )?,
            contribution: StatContribution::Add {
                amount: scalar(-value * ARMOR_CONTRIBUTION_SCALE, "sources.contribution")?,
            },
            stacking_group: mechanics_id(
                &format!("items[{}].sources", item.id),
                EQUIPMENT_STACKING_GROUP,
                StackingGroupId::parse,
            )?,
            stacking: StackingPolicy::Sum,
        });
    }
    let id = mechanics_id(
        &format!("items[{}].sources", item.id),
        &format!("source-{}", item.id),
        SourceDefinitionId::parse,
    )?;
    Ok(Some((
        id.clone(),
        SourceDefinition {
            id,
            priority: 0,
            stat_contributions: contributions,
            damage_responses: Vec::new(),
        },
    )))
}

/// Compile the item vocabulary into upstream item definitions: fungible
/// stacks for block-less items (gold, arrows), unique entities for
/// equippable ones, weight as a `weight` capacity cost in quarter-kg units,
/// the Dagger-owned equipment policy, and the equipment-borne armor sources.
fn compile_upstream_items(
    items: &BTreeMap<String, DaggerItemDefinition>,
) -> Result<(Vec<ItemDefinition>, Vec<SourceDefinition>), DaggerGameplayError> {
    let mut definitions = Vec::with_capacity(items.len());
    let mut sources = Vec::new();
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
        let source = equipment_source(item)?;
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
            sources: source
                .as_ref()
                .map(|(id, _)| vec![id.clone()])
                .unwrap_or_default(),
        });
        if let Some((_, definition)) = source {
            sources.push(definition);
        }
    }
    Ok((definitions, sources))
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
    // Progression stats (xp, level) are wide-range counters: xp accumulates
    // past the classic 0..=100 attribute range.
    for id in &section.progression {
        stats.push(StatDefinition {
            id: StatId::parse(id.clone()).map_err(|error| DaggerGameplayError::InvalidId {
                path: "stats".to_string(),
                value: format!("{id}: {error:?}"),
            })?,
            minimum: scalar(CLASSIC_STAT_MINIMUM, "stats.minimum")?,
            maximum: scalar(TRACK_MAX_STAT_MAXIMUM, "stats.maximum")?,
        });
    }
    // Each declared armor part is a signed stat (classic sbyte range); the
    // flat actor armor value is its spawn base and equipment sources
    // subtract from it.
    for part in &section.armor_parts {
        stats.push(StatDefinition {
            id: StatId::parse(armor_part_stat_id(part)).map_err(|error| {
                DaggerGameplayError::InvalidId {
                    path: "stats".to_string(),
                    value: format!("{part}: {error:?}"),
                }
            })?,
            minimum: scalar(ARMOR_PART_STAT_MINIMUM, "stats.minimum")?,
            maximum: scalar(ARMOR_PART_STAT_MAXIMUM, "stats.maximum")?,
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
    let (item_definitions, sources) = compile_upstream_items(items)?;
    MechanicsCatalog::admit(MechanicsCatalogDefinition {
        version: mechanics_catalog_version(),
        stats,
        tracks,
        // Equipment-borne armor/shield contributions are attributed sources
        // on the items. Damage kinds/responses arrive with resistances;
        // effects arrive with spell effects.
        sources,
        damage_kinds: Vec::new(),
        effects: Vec::new(),
        capacity_metrics,
        items: item_definitions,
        equipment_slots,
    })
    .map_err(|error| DaggerGameplayError::InvalidValue {
        path: "mechanicsCatalog".to_string(),
        reason: format!("mechanics catalog admission rejected: {error:?}"),
    })
}
