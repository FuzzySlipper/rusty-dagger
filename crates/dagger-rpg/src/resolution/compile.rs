use std::collections::{BTreeMap, BTreeSet};

use rusty_engine::{
    gameplay_mechanics::{MechanicsScalar, StatId, TrackId},
    gameplay_resolution::Program,
    gameplay_rules::decode_rule_package,
    gameplay_standard::{
        CapabilityRoleId, ComposedExactComparison, ComposedExactExpr, ComposedExactLeafKindId,
        ComposedExactProductLeaf, ExactInputReference, StandardExactFactReference,
    },
};

use super::{
    AuthoredActorDefinition, AuthoredCmpOp, AuthoredEncounterDefinition, AuthoredExactInput,
    AuthoredExpr, AuthoredGameplayPayload, AuthoredItemDefinition, AuthoredLootTable,
    AuthoredOperation, AuthoredPredicate, AuthoredProgram, AuthoredRuleDefinition,
    AuthoredSelector, DaggerActionDefinition, DaggerActorDefinition, DaggerActorKind,
    DaggerArmorDefinition, DaggerBehaviorDefinition, DaggerDamageRange, DaggerDerivedRule,
    DaggerEncounterDefinition, DaggerEquipmentSection, DaggerEquipmentSlotDefinition,
    DaggerExactLeaf, DaggerExpr, DaggerGameplayCatalog, DaggerGameplayError, DaggerItemDefinition,
    DaggerItemEquipment, DaggerLoadoutEntry, DaggerLootCategories, DaggerLootTable,
    DaggerOperation, DaggerPredicate, DaggerProgram, DaggerRuleDefinition, DaggerSelector,
    DaggerShieldDefinition, DaggerStatsSection, DaggerSubject, DaggerTrackDefinition,
    DaggerWeaponDefinition, DaggerWeaponHands, DAGGER_GAMEPLAY_SCHEMA_VERSION, MAX_BEHAVIOR_VALUE,
    MAX_DAGGER_ACTIONS, MAX_DAGGER_ACTORS, MAX_DAGGER_DECLARED_IDS, MAX_DAGGER_DERIVED,
    MAX_DAGGER_ENCOUNTERS, MAX_DAGGER_ENCOUNTER_MEMBERS, MAX_DAGGER_EXPR_DEPTH,
    MAX_DAGGER_EXPR_NODES, MAX_DAGGER_ID_BYTES, MAX_DAGGER_ITEMS, MAX_DAGGER_LOOT_TABLES,
    MAX_DAGGER_PROGRAM_DEPTH, MAX_DAGGER_PROGRAM_NODES, MAX_DAGGER_RULES, MAX_DAGGER_TEXT_BYTES,
};

const MIN_TUNING_VALUE: f64 = 0.001;

/// Classic supports up to 5 sub-attacks per swing; no classic monster uses
/// more than 3.
const MAX_ATTACK_RANGES: usize = 5;

/// The capacity metric id items weigh against (classic quarter-kg units).
pub const WEIGHT_CAPACITY_METRIC: &str = "weight";

/// The primary and off-hand equipment slot ids (classic paper-doll names).
/// Weapon reads prefer the right hand (the donor's primary hand).
pub const RIGHT_HAND_SLOT: &str = "right-hand";
pub const LEFT_HAND_SLOT: &str = "left-hand";

/// The exclusivity group two-handed weapons and shields share (classic: a
/// two-hander occupies both hands, so it conflicts with a shield).
pub const HANDS_EXCLUSIVE_GROUP: &str = "hands";

/// Classic armor value per material (donor
/// `DaggerfallUnityItem.GetMaterialArmorValue` — adopted; armor value is a
/// property of the material, not the piece). This fixed classic set is also
/// the declared-material vocabulary weapon and armor materials validate
/// against — one local table serves both checks.
const ARMOR_VALUES_BY_MATERIAL: [(&str, i64); 12] = [
    ("leather", 3),
    ("chain", 6),
    ("iron", 7),
    ("steel", 9),
    ("silver", 9),
    ("elven", 11),
    ("dwarven", 13),
    ("mithril", 15),
    ("adamantium", 15),
    ("ebony", 17),
    ("orcish", 19),
    ("daedric", 21),
];

/// Armor pieces and the classification each maps to (`armor-<piece>`).
const ARMOR_PIECES: [&str; 7] = [
    "head",
    "chest",
    "right-arm",
    "left-arm",
    "legs",
    "hands",
    "feet",
];

fn armor_value_for_material(material: &str) -> Option<i64> {
    ARMOR_VALUES_BY_MATERIAL
        .iter()
        .find(|(name, _)| *name == material)
        .map(|(_, value)| *value)
}

fn compile_actor_attacks(
    path: &str,
    attacks: Vec<super::AuthoredDamageRange>,
) -> Result<Vec<DaggerDamageRange>, DaggerGameplayError> {
    if attacks.len() > MAX_ATTACK_RANGES {
        return Err(DaggerGameplayError::Quota {
            field: "actor attack ranges",
            actual: attacks.len(),
            maximum: MAX_ATTACK_RANGES,
        });
    }
    attacks
        .into_iter()
        .enumerate()
        .map(|(index, range)| {
            if range.min.0 < 0 || range.min > range.max {
                return Err(DaggerGameplayError::InvalidValue {
                    path: format!("{path}.attacks[{index}]"),
                    reason: format!(
                        "must satisfy 0 <= min <= max, got {}..{}",
                        range.min.0, range.max.0
                    ),
                });
            }
            Ok(DaggerDamageRange {
                min: range.min.0,
                max: range.max.0,
            })
        })
        .collect()
}

/// The single binary64 -> f32 boundary: every approximate behavior/tuning
/// value crosses here, with Dagger-owned semantic ranges applied. Rejects
/// non-finite and out-of-range values; nothing is silently cast or
/// truncated anywhere else.
fn tuning_to_f32(
    path: &str,
    value: f64,
    minimum: f64,
    maximum: f64,
) -> Result<f32, DaggerGameplayError> {
    if !value.is_finite() || value < minimum || value > maximum {
        return Err(DaggerGameplayError::InvalidValue {
            path: path.to_string(),
            reason: format!("must be finite and between {minimum} and {maximum}, got {value}"),
        });
    }
    Ok(value as f32)
}

pub fn compile_gameplay_package(
    input: &[u8],
) -> Result<DaggerGameplayCatalog, DaggerGameplayError> {
    let package = decode_rule_package(input)
        .map_err(|error| DaggerGameplayError::Package(error.to_string()))?;
    let domain = package.identity().domain().as_str();
    let package_id = package.identity().package().as_str();
    if domain != "dagger" || package_id != "core" {
        return Err(DaggerGameplayError::WrongPackage {
            domain: domain.to_string(),
            package: package_id.to_string(),
        });
    }
    let payload = serde_json::from_value::<AuthoredGameplayPayload>(package.payload().clone())
        .map_err(|error| DaggerGameplayError::Payload(error.to_string()))?;
    if payload.schema_version.0 != i64::from(DAGGER_GAMEPLAY_SCHEMA_VERSION) {
        return Err(DaggerGameplayError::UnsupportedSchema {
            actual: payload.schema_version.0 as u32,
            expected: DAGGER_GAMEPLAY_SCHEMA_VERSION,
        });
    }
    enforce_quota("actions", payload.actions.len(), MAX_DAGGER_ACTIONS)?;
    enforce_quota("items", payload.items.len(), MAX_DAGGER_ITEMS)?;
    enforce_quota("rules", payload.rules.len(), MAX_DAGGER_RULES)?;
    enforce_quota("actors", payload.actors.len(), MAX_DAGGER_ACTORS)?;
    enforce_quota(
        "encounters",
        payload.encounters.len(),
        MAX_DAGGER_ENCOUNTERS,
    )?;
    enforce_quota("derived", payload.derived.len(), MAX_DAGGER_DERIVED)?;
    enforce_quota(
        "loot tables",
        payload.loot_tables.len(),
        MAX_DAGGER_LOOT_TABLES,
    )?;

    let stats = compile_stats(&payload)?;
    let items = compile_items(payload.items, &stats)?;
    let equipment = compile_equipment(payload.equipment)?;
    validate_item_equipment_references(&items, &equipment)?;
    let actions = compile_actions(payload.actions, &stats)?;
    let actors = compile_actors(payload.actors, &stats, &actions, &items, &equipment)?;
    let rules = compile_rules(payload.rules)?;
    let encounters = compile_encounters(payload.encounters)?;
    let derived = compile_derived(payload.derived, &stats)?;
    let loot_tables = compile_loot_tables(payload.loot_tables)?;
    let mechanics = super::mechanics::compile_mechanics_catalog(&stats, &items, &equipment)?;
    Ok(DaggerGameplayCatalog::new(
        package.fingerprint().as_str().to_string(),
        stats,
        actors,
        actions,
        items,
        rules,
        encounters,
        derived,
        equipment,
        loot_tables,
        mechanics,
    ))
}

/// Reference integrity between items and the equipment section: an
/// equippable item requires declared slots, and any weighed item requires
/// the `weight` capacity metric. The section itself is optional under
/// payload schema 1, but item vocabulary that binds against it is not.
fn validate_item_equipment_references(
    items: &BTreeMap<String, DaggerItemDefinition>,
    equipment: &DaggerEquipmentSection,
) -> Result<(), DaggerGameplayError> {
    for item in items.values() {
        if item.equippable() && equipment.slots.is_empty() {
            return Err(DaggerGameplayError::InvalidValue {
                path: format!("payload.items[{}]", item.id),
                reason: "equippable items require an equipment section with slots".to_string(),
            });
        }
        if item.weight_units > 0
            && !equipment
                .capacity_metrics
                .iter()
                .any(|metric| metric == WEIGHT_CAPACITY_METRIC)
        {
            return Err(DaggerGameplayError::InvalidValue {
                path: format!("payload.items[{}].weightUnits", item.id),
                reason: format!(
                    "weighed items require the {WEIGHT_CAPACITY_METRIC} capacity metric"
                ),
            });
        }
    }
    Ok(())
}

/// Compile the classic loot tables: keys are unique and `-` or a single
/// uppercase letter, gold bounds satisfy `0 <= min <= max`, and every
/// category chance is an integer percentage 0..=100.
fn compile_loot_tables(
    definitions: Vec<AuthoredLootTable>,
) -> Result<BTreeMap<String, DaggerLootTable>, DaggerGameplayError> {
    let mut tables = BTreeMap::new();
    for (index, table) in definitions.into_iter().enumerate() {
        let path = format!("payload.lootTables[{index}]");
        validate_loot_table_key(&format!("{path}.key"), &table.key)?;
        if table.gold.min.0 < 0 || table.gold.min > table.gold.max {
            return Err(DaggerGameplayError::InvalidValue {
                path: format!("{path}.gold"),
                reason: format!(
                    "must satisfy 0 <= min <= max, got {}..{}",
                    table.gold.min.0, table.gold.max.0
                ),
            });
        }
        let authored = table.categories;
        let check = |field: &str, value: i64| -> Result<i64, DaggerGameplayError> {
            if !(0..=100).contains(&value) {
                return Err(DaggerGameplayError::InvalidValue {
                    path: format!("{path}.categories.{field}"),
                    reason: format!("must be an integer percentage 0..=100, got {value}"),
                });
            }
            Ok(value)
        };
        let categories = DaggerLootCategories {
            plant1: check("plant1", authored.plant1.0)?,
            plant2: check("plant2", authored.plant2.0)?,
            creature1: check("creature1", authored.creature1.0)?,
            creature2: check("creature2", authored.creature2.0)?,
            creature3: check("creature3", authored.creature3.0)?,
            misc1: check("misc1", authored.misc1.0)?,
            misc2: check("misc2", authored.misc2.0)?,
            armor: check("armor", authored.armor.0)?,
            weapons: check("weapons", authored.weapons.0)?,
            magic: check("magic", authored.magic.0)?,
            clothing: check("clothing", authored.clothing.0)?,
            books: check("books", authored.books.0)?,
            religious: check("religious", authored.religious.0)?,
        };
        let key = table.key;
        if tables
            .insert(
                key.clone(),
                DaggerLootTable {
                    key: key.clone(),
                    gold_min: table.gold.min.0,
                    gold_max: table.gold.max.0,
                    categories,
                },
            )
            .is_some()
        {
            return Err(DaggerGameplayError::DuplicateId {
                kind: "loot table",
                id: key,
            });
        }
    }
    Ok(tables)
}

/// Loot table keys are classic letters (uppercase) or the `-` default.
fn validate_loot_table_key(path: &str, key: &str) -> Result<(), DaggerGameplayError> {
    let valid = key == "-" || (key.len() == 1 && key.bytes().all(|byte| byte.is_ascii_uppercase()));
    if valid {
        Ok(())
    } else {
        Err(DaggerGameplayError::InvalidValue {
            path: path.to_string(),
            reason: format!("must be \"-\" or a single uppercase letter, got {key:?}"),
        })
    }
}

fn compile_equipment(
    section: Option<super::AuthoredEquipmentSection>,
) -> Result<DaggerEquipmentSection, DaggerGameplayError> {
    let Some(section) = section else {
        return Ok(DaggerEquipmentSection::default());
    };
    enforce_quota(
        "equipment slots",
        section.slots.len(),
        MAX_DAGGER_DECLARED_IDS,
    )?;
    let mut capacity_metrics = Vec::with_capacity(section.capacity_metrics.len());
    for (index, id) in section.capacity_metrics.iter().enumerate() {
        validate_id(&format!("payload.equipment.capacityMetrics[{index}]"), id)?;
        if capacity_metrics.contains(id) {
            return Err(DaggerGameplayError::DuplicateId {
                kind: "capacity metric",
                id: id.clone(),
            });
        }
        capacity_metrics.push(id.clone());
    }
    let mut slots: Vec<DaggerEquipmentSlotDefinition> = Vec::with_capacity(section.slots.len());
    for (index, slot) in section.slots.into_iter().enumerate() {
        let path = format!("payload.equipment.slots[{index}]");
        validate_id(&format!("{path}.id"), &slot.id)?;
        if slots.iter().any(|existing| existing.id == slot.id) {
            return Err(DaggerGameplayError::DuplicateId {
                kind: "equipment slot",
                id: slot.id,
            });
        }
        let mut allowed_classifications = Vec::with_capacity(slot.allowed_classifications.len());
        for (classification_index, classification) in
            slot.allowed_classifications.iter().enumerate()
        {
            validate_id(
                &format!("{path}.allowedClassifications[{classification_index}]"),
                classification,
            )?;
            if allowed_classifications.contains(classification) {
                return Err(DaggerGameplayError::DuplicateId {
                    kind: "equipment slot classification",
                    id: classification.clone(),
                });
            }
            allowed_classifications.push(classification.clone());
        }
        slots.push(DaggerEquipmentSlotDefinition {
            id: slot.id,
            allowed_classifications,
        });
    }
    Ok(DaggerEquipmentSection {
        capacity_metrics,
        slots,
    })
}

fn compile_derived(
    definitions: Vec<super::AuthoredDerivedRule>,
    stats: &DaggerStatsSection,
) -> Result<BTreeMap<String, DaggerDerivedRule>, DaggerGameplayError> {
    let mut derived = BTreeMap::new();
    for (index, rule) in definitions.into_iter().enumerate() {
        validate_id(&format!("payload.derived[{index}].id"), &rule.id)?;
        let mut nodes = 0_usize;
        let expr = compile_expr(rule.expr, &mut nodes, 0, stats)?;
        if derived
            .insert(
                rule.id.clone(),
                DaggerDerivedRule {
                    id: rule.id.clone(),
                    expr,
                },
            )
            .is_some()
        {
            return Err(DaggerGameplayError::DuplicateId {
                kind: "derived rule",
                id: rule.id,
            });
        }
    }
    Ok(derived)
}

fn compile_stats(
    payload: &AuthoredGameplayPayload,
) -> Result<DaggerStatsSection, DaggerGameplayError> {
    let compile_ids =
        |kind: &'static str, ids: &[String]| -> Result<BTreeSet<String>, DaggerGameplayError> {
            enforce_quota(kind, ids.len(), MAX_DAGGER_DECLARED_IDS)?;
            let mut declared = BTreeSet::new();
            for (index, id) in ids.iter().enumerate() {
                validate_id(&format!("payload.stats.{kind}[{index}]"), id)?;
                if !declared.insert(id.clone()) {
                    return Err(DaggerGameplayError::DuplicateId {
                        kind,
                        id: id.clone(),
                    });
                }
            }
            Ok(declared)
        };
    Ok(DaggerStatsSection {
        attributes: compile_ids("attributes", &payload.stats.attributes)?,
        skills: compile_ids("skills", &payload.stats.skills)?,
        tracks: compile_ids("tracks", &payload.stats.tracks)?,
        armor_parts: compile_ids("armorParts", &payload.stats.armor_parts)?,
        progression: compile_ids("progression", &payload.stats.progression)?,
    })
}

fn compile_items(
    definitions: Vec<AuthoredItemDefinition>,
    stats: &DaggerStatsSection,
) -> Result<BTreeMap<String, DaggerItemDefinition>, DaggerGameplayError> {
    let mut items = BTreeMap::new();
    for (index, item) in definitions.into_iter().enumerate() {
        let path = format!("payload.items[{index}]");
        validate_id(&format!("{path}.id"), &item.id)?;
        let weight_units =
            u64::try_from(item.weight_units.0).map_err(|_| DaggerGameplayError::InvalidValue {
                path: format!("{path}.weightUnits"),
                reason: format!("must be non-negative, got {}", item.weight_units.0),
            })?;
        let value = u64::try_from(item.value.0).map_err(|_| DaggerGameplayError::InvalidValue {
            path: format!("{path}.value"),
            reason: format!("must be non-negative, got {}", item.value.0),
        })?;
        let weapon = item
            .weapon
            .map(
                |weapon| -> Result<DaggerWeaponDefinition, DaggerGameplayError> {
                    if weapon.damage.min.0 < 0 || weapon.damage.min > weapon.damage.max {
                        return Err(DaggerGameplayError::InvalidValue {
                            path: format!("{path}.weapon.damage"),
                            reason: format!(
                                "must satisfy 0 <= min <= max, got {}..{}",
                                weapon.damage.min.0, weapon.damage.max.0
                            ),
                        });
                    }
                    validate_material(&format!("{path}.weapon.material"), &weapon.material)?;
                    validate_declared(
                        &format!("{path}.weapon.skill"),
                        &weapon.skill,
                        &stats.skills,
                    )?;
                    Ok(DaggerWeaponDefinition {
                        damage_min: weapon.damage.min.0,
                        damage_max: weapon.damage.max.0,
                        material: weapon.material,
                        skill: weapon.skill,
                        hands: match weapon.hands {
                            super::AuthoredWeaponHands::Either => DaggerWeaponHands::Either,
                            super::AuthoredWeaponHands::Both => DaggerWeaponHands::Both,
                            super::AuthoredWeaponHands::LeftOnly => DaggerWeaponHands::LeftOnly,
                        },
                    })
                },
            )
            .transpose()?;
        let armor = item
            .armor
            .map(
                |armor| -> Result<DaggerArmorDefinition, DaggerGameplayError> {
                    validate_material(&format!("{path}.armor.material"), &armor.material)?;
                    if !ARMOR_PIECES.contains(&armor.piece.as_str()) {
                        return Err(DaggerGameplayError::InvalidValue {
                            path: format!("{path}.armor.piece"),
                            reason: format!(
                                "unknown armor piece {}; expected one of {ARMOR_PIECES:?}",
                                armor.piece
                            ),
                        });
                    }
                    Ok(DaggerArmorDefinition {
                        value: armor_value_for_material(&armor.material)
                            .expect("validated material has a table entry"),
                        material: armor.material,
                        piece: armor.piece,
                    })
                },
            )
            .transpose()?;
        let shield = item
            .shield
            .map(
                |shield| -> Result<DaggerShieldDefinition, DaggerGameplayError> {
                    if shield.value.0 < 0 {
                        return Err(DaggerGameplayError::InvalidValue {
                            path: format!("{path}.shield.value"),
                            reason: format!("must be non-negative, got {}", shield.value.0),
                        });
                    }
                    Ok(DaggerShieldDefinition {
                        value: shield.value.0,
                    })
                },
            )
            .transpose()?;
        // An item with any equip block is a unique equippable entity; an
        // item with none (gold, arrows) is a fungible stack.
        let fungible = weapon.is_none() && armor.is_none() && shield.is_none();
        let mut classifications = Vec::new();
        let mut exclusive_group = None;
        if let Some(weapon) = &weapon {
            classifications.push(match weapon.hands {
                DaggerWeaponHands::Either | DaggerWeaponHands::LeftOnly => {
                    "weapon-one-hand".to_string()
                }
                DaggerWeaponHands::Both => "weapon-two-hand".to_string(),
            });
            if weapon.hands == DaggerWeaponHands::Both {
                exclusive_group = Some(HANDS_EXCLUSIVE_GROUP.to_string());
            }
        }
        if let Some(armor) = &armor {
            classifications.push(format!("armor-{}", armor.piece));
        }
        if shield.is_some() {
            classifications.push("shield".to_string());
            exclusive_group = Some(HANDS_EXCLUSIVE_GROUP.to_string());
        }
        let equipment = (!fungible).then_some(DaggerItemEquipment {
            required_slots: 1,
            exclusive_group,
        });
        let id = item.id;
        if items
            .insert(
                id.clone(),
                DaggerItemDefinition {
                    id: id.clone(),
                    weapon,
                    armor,
                    shield,
                    weight_units,
                    value,
                    fungible,
                    classifications,
                    equipment,
                },
            )
            .is_some()
        {
            return Err(DaggerGameplayError::DuplicateId { kind: "item", id });
        }
    }
    Ok(items)
}

/// Weapon and armor materials validate against the fixed classic material
/// set (the armor-value table): one declared vocabulary serves both, rather
/// than threading a second list through the stats section.
fn validate_material(path: &str, material: &str) -> Result<(), DaggerGameplayError> {
    validate_id(path, material)?;
    if armor_value_for_material(material).is_none() {
        return Err(DaggerGameplayError::InvalidValue {
            path: path.to_string(),
            reason: format!(
                "unknown material {material}; expected one of {:?}",
                ARMOR_VALUES_BY_MATERIAL
                    .iter()
                    .map(|(name, _)| name)
                    .collect::<Vec<_>>()
            ),
        });
    }
    Ok(())
}

fn compile_actions(
    definitions: Vec<super::AuthoredActionDefinition>,
    stats: &DaggerStatsSection,
) -> Result<BTreeMap<String, DaggerActionDefinition>, DaggerGameplayError> {
    let mut actions = BTreeMap::new();
    for (index, action) in definitions.into_iter().enumerate() {
        let path = format!("payload.actions[{index}].id");
        validate_id(&path, &action.id)?;
        let mut tags = BTreeSet::new();
        for (tag_index, tag) in action.tags.into_iter().enumerate() {
            validate_id(&format!("payload.actions[{index}].tags[{tag_index}]"), &tag)?;
            if !tags.insert(tag.clone()) {
                return Err(DaggerGameplayError::DuplicateId {
                    kind: "action tag",
                    id: tag,
                });
            }
        }
        let mut nodes = 0_usize;
        let program = compile_program(action.program, &mut nodes, 0, stats)?;
        let reach = action
            .reach
            .map(|value| {
                tuning_to_f32(
                    &format!("payload.actions[{index}].reach"),
                    value,
                    MIN_TUNING_VALUE,
                    f64::from(MAX_BEHAVIOR_VALUE),
                )
            })
            .transpose()?;
        let cooldown_seconds = action
            .cooldown_seconds
            .map(|value| {
                tuning_to_f32(
                    &format!("payload.actions[{index}].cooldownSeconds"),
                    value,
                    MIN_TUNING_VALUE,
                    f64::from(MAX_BEHAVIOR_VALUE),
                )
            })
            .transpose()?;
        let id = action.id;
        if actions
            .insert(
                id.clone(),
                DaggerActionDefinition {
                    id: id.clone(),
                    tags,
                    program,
                    reach,
                    cooldown_seconds,
                },
            )
            .is_some()
        {
            return Err(DaggerGameplayError::DuplicateId { kind: "action", id });
        }
    }
    Ok(actions)
}

fn compile_actors(
    definitions: Vec<AuthoredActorDefinition>,
    stats: &DaggerStatsSection,
    actions: &BTreeMap<String, DaggerActionDefinition>,
    items: &BTreeMap<String, DaggerItemDefinition>,
    equipment: &DaggerEquipmentSection,
) -> Result<BTreeMap<String, DaggerActorDefinition>, DaggerGameplayError> {
    let mut actors = BTreeMap::new();
    let mut mobile_ids = BTreeSet::new();
    for (index, actor) in definitions.into_iter().enumerate() {
        let path = format!("payload.actors[{index}]");
        validate_id(&format!("{path}.id"), &actor.id)?;
        let kind = match actor.kind {
            super::AuthoredActorKind::Player => DaggerActorKind::Player,
            super::AuthoredActorKind::Monster => DaggerActorKind::Monster,
            super::AuthoredActorKind::EnemyClass => DaggerActorKind::EnemyClass,
        };
        let move_speed = actor
            .move_speed
            .map(|value| {
                tuning_to_f32(
                    &format!("{path}.moveSpeed"),
                    value,
                    MIN_TUNING_VALUE,
                    f64::from(MAX_BEHAVIOR_VALUE),
                )
            })
            .transpose()?;
        if kind == DaggerActorKind::Player && move_speed.is_none() {
            return Err(DaggerGameplayError::InvalidValue {
                path: format!("{path}.moveSpeed"),
                reason: "player actors must declare a movement speed".to_string(),
            });
        }
        let mobile_id = actor
            .mobile_id
            .map(|mobile_id| {
                u8::try_from(mobile_id.0).map_err(|_| DaggerGameplayError::InvalidValue {
                    path: format!("{path}.mobileId"),
                    reason: format!("must be between 0 and 255, got {}", mobile_id.0),
                })
            })
            .transpose()?;
        if let Some(mobile_id) = mobile_id {
            if !mobile_ids.insert(mobile_id) {
                return Err(DaggerGameplayError::DuplicateId {
                    kind: "actor mobileId",
                    id: mobile_id.to_string(),
                });
            }
        }
        let mut actor_stats = BTreeMap::new();
        for (id, value) in actor.stats {
            validate_declared(&format!("{path}.stats"), &id, &stats.attributes)?;
            actor_stats.insert(id, value.0);
        }
        let mut actor_skills = BTreeMap::new();
        for (id, value) in actor.skills {
            validate_declared(&format!("{path}.skills"), &id, &stats.skills)?;
            actor_skills.insert(id, value.0);
        }
        let mut track_ids = BTreeSet::new();
        let mut tracks = Vec::with_capacity(actor.tracks.len());
        for (track_index, track) in actor.tracks.into_iter().enumerate() {
            validate_declared(
                &format!("{path}.tracks[{track_index}].id"),
                &track.id,
                &stats.tracks,
            )?;
            if !track_ids.insert(track.id.clone()) {
                return Err(DaggerGameplayError::DuplicateId {
                    kind: "actor track",
                    id: track.id,
                });
            }
            let mut nodes = 0_usize;
            tracks.push(DaggerTrackDefinition {
                id: track.id,
                max: compile_expr(track.max, &mut nodes, 0, stats)?,
            });
        }
        let behavior = actor
            .behavior
            .map(
                |behavior| -> Result<DaggerBehaviorDefinition, DaggerGameplayError> {
                    validate_id(&format!("{path}.behavior.action"), &behavior.action)?;
                    if !actions.contains_key(&behavior.action) {
                        return Err(DaggerGameplayError::InvalidValue {
                            path: format!("{path}.behavior.action"),
                            reason: format!("unknown action {}", behavior.action),
                        });
                    }
                    Ok(DaggerBehaviorDefinition {
                        detection_range: tuning_to_f32(
                            &format!("{path}.behavior.detectionRange"),
                            behavior.detection_range,
                            MIN_TUNING_VALUE,
                            f64::from(MAX_BEHAVIOR_VALUE),
                        )?,
                        patrol_speed: tuning_to_f32(
                            &format!("{path}.behavior.patrolSpeed"),
                            behavior.patrol_speed,
                            0.0,
                            f64::from(MAX_BEHAVIOR_VALUE),
                        )?,
                        chase_speed: tuning_to_f32(
                            &format!("{path}.behavior.chaseSpeed"),
                            behavior.chase_speed,
                            0.0,
                            f64::from(MAX_BEHAVIOR_VALUE),
                        )?,
                        attack_range: tuning_to_f32(
                            &format!("{path}.behavior.attackRange"),
                            behavior.attack_range,
                            MIN_TUNING_VALUE,
                            f64::from(MAX_BEHAVIOR_VALUE),
                        )?,
                        attack_cooldown_seconds: tuning_to_f32(
                            &format!("{path}.behavior.attackCooldownSeconds"),
                            behavior.attack_cooldown_seconds,
                            MIN_TUNING_VALUE,
                            f64::from(MAX_BEHAVIOR_VALUE),
                        )?,
                        action: behavior.action,
                    })
                },
            )
            .transpose()?;
        let id = actor.id;
        let xp_reward = actor
            .xp_reward
            .map(|value| {
                if value.0 < 0 {
                    return Err(DaggerGameplayError::InvalidValue {
                        path: format!("{path}.xpReward"),
                        reason: format!("must be non-negative, got {}", value.0),
                    });
                }
                Ok(value.0)
            })
            .transpose()?;
        let hit_points_per_level = actor
            .hit_points_per_level
            .map(|value| {
                if value.0 < 0 {
                    return Err(DaggerGameplayError::InvalidValue {
                        path: format!("{path}.hitPointsPerLevel"),
                        reason: format!("must be non-negative, got {}", value.0),
                    });
                }
                Ok(value.0)
            })
            .transpose()?;
        if actors
            .insert(
                id.clone(),
                DaggerActorDefinition {
                    id: id.clone(),
                    kind,
                    mobile_id,
                    stats: actor_stats,
                    skills: actor_skills,
                    armor_value: actor.armor_value.0,
                    tracks,
                    move_speed,
                    behavior,
                    level: actor.level.map(|value| value.0),
                    weight: actor.weight.map(|value| value.0),
                    min_metal_to_hit: actor.min_metal_to_hit,
                    team: actor.team,
                    loot_table_key: actor.loot_table_key,
                    xp_reward,
                    hit_points_per_level,
                    attacks: compile_actor_attacks(&path, actor.attacks)?,
                    inventory: compile_loadout(&path, actor.inventory, items, equipment)?,
                },
            )
            .is_some()
        {
            return Err(DaggerGameplayError::DuplicateId { kind: "actor", id });
        }
    }
    Ok(actors)
}

/// Compile one actor's spawn loadout: reference integrity only (the item
/// exists, quantities are sane for the item kind, and an equip slot exists
/// and accepts an equippable item).
fn compile_loadout(
    path: &str,
    entries: Vec<super::AuthoredLoadoutEntry>,
    items: &BTreeMap<String, DaggerItemDefinition>,
    equipment: &DaggerEquipmentSection,
) -> Result<Vec<DaggerLoadoutEntry>, DaggerGameplayError> {
    let mut loadout = Vec::with_capacity(entries.len());
    for (index, entry) in entries.into_iter().enumerate() {
        let path = format!("{path}.inventory[{index}]");
        validate_id(&format!("{path}.item"), &entry.item)?;
        let item = items
            .get(&entry.item)
            .ok_or_else(|| DaggerGameplayError::InvalidValue {
                path: format!("{path}.item"),
                reason: format!("unknown item {}", entry.item),
            })?;
        let quantity = entry.quantity.map(|value| value.0).unwrap_or(1);
        let quantity = u64::try_from(quantity).map_err(|_| DaggerGameplayError::InvalidValue {
            path: format!("{path}.quantity"),
            reason: format!("must be positive, got {quantity}"),
        })?;
        if quantity == 0 {
            return Err(DaggerGameplayError::InvalidValue {
                path: format!("{path}.quantity"),
                reason: "must be positive, got 0".to_string(),
            });
        }
        if !item.fungible && quantity != 1 {
            return Err(DaggerGameplayError::InvalidValue {
                path: format!("{path}.quantity"),
                reason: format!("unique item {} spawns as exactly one entity", item.id),
            });
        }
        let equip_slot = entry
            .equip_slot
            .map(|slot| -> Result<String, DaggerGameplayError> {
                validate_id(&format!("{path}.equipSlot"), &slot)?;
                if equipment.slot(&slot).is_none() {
                    return Err(DaggerGameplayError::InvalidValue {
                        path: format!("{path}.equipSlot"),
                        reason: format!("unknown equipment slot {slot}"),
                    });
                }
                if !item.equippable() {
                    return Err(DaggerGameplayError::InvalidValue {
                        path: format!("{path}.equipSlot"),
                        reason: format!("item {} is not equippable", item.id),
                    });
                }
                Ok(slot)
            })
            .transpose()?;
        loadout.push(DaggerLoadoutEntry {
            item: entry.item,
            quantity,
            equip_slot,
        });
    }
    Ok(loadout)
}

fn compile_rules(
    definitions: Vec<AuthoredRuleDefinition>,
) -> Result<Vec<DaggerRuleDefinition>, DaggerGameplayError> {
    let mut ids = BTreeSet::new();
    let mut rules = Vec::with_capacity(definitions.len());
    for (index, rule) in definitions.into_iter().enumerate() {
        let AuthoredRuleDefinition::RejectTagWhileCondition { id, tag, condition } = rule;
        validate_id(&format!("payload.rules[{index}].id"), &id)?;
        validate_id(&format!("payload.rules[{index}].tag"), &tag)?;
        validate_id(&format!("payload.rules[{index}].condition"), &condition)?;
        if !ids.insert(id.clone()) {
            return Err(DaggerGameplayError::DuplicateId { kind: "rule", id });
        }
        rules.push(DaggerRuleDefinition::RejectTagWhileCondition { id, tag, condition });
    }
    Ok(rules)
}

fn compile_encounters(
    definitions: Vec<AuthoredEncounterDefinition>,
) -> Result<BTreeMap<String, DaggerEncounterDefinition>, DaggerGameplayError> {
    let mut encounters = BTreeMap::new();
    for (index, encounter) in definitions.into_iter().enumerate() {
        let path = format!("payload.encounters[{index}]");
        validate_id(&format!("{path}.id"), &encounter.id)?;
        for (field, value) in [
            ("name", &encounter.name),
            ("objective", &encounter.objective),
            ("routeCode", &encounter.route_code),
        ] {
            if value.is_empty() || value.len() > MAX_DAGGER_TEXT_BYTES {
                return Err(DaggerGameplayError::InvalidValue {
                    path: format!("{path}.{field}"),
                    reason: format!("must be 1..={MAX_DAGGER_TEXT_BYTES} bytes"),
                });
            }
        }
        enforce_quota(
            "encounter members",
            encounter.member_entity_ids.len(),
            MAX_DAGGER_ENCOUNTER_MEMBERS,
        )?;
        if encounter.member_entity_ids.is_empty() {
            return Err(DaggerGameplayError::InvalidValue {
                path: format!("{path}.memberEntityIds"),
                reason: "must name at least one member entity".to_string(),
            });
        }
        let id = encounter.id;
        if encounters
            .insert(
                id.clone(),
                DaggerEncounterDefinition {
                    id: id.clone(),
                    name: encounter.name,
                    objective: encounter.objective,
                    route_code: encounter.route_code,
                    member_entity_ids: encounter
                        .member_entity_ids
                        .into_iter()
                        .map(|id| {
                            u64::try_from(id.0).map_err(|_| DaggerGameplayError::InvalidValue {
                                path: format!("{path}.memberEntityIds"),
                                reason: format!("entity id {} must be non-negative", id.0),
                            })
                        })
                        .collect::<Result<Vec<_>, _>>()?,
                },
            )
            .is_some()
        {
            return Err(DaggerGameplayError::DuplicateId {
                kind: "encounter",
                id,
            });
        }
    }
    Ok(encounters)
}

fn compile_program(
    program: AuthoredProgram,
    nodes: &mut usize,
    depth: u16,
    stats: &DaggerStatsSection,
) -> Result<DaggerProgram, DaggerGameplayError> {
    *nodes = nodes.checked_add(1).ok_or(DaggerGameplayError::Quota {
        field: "program nodes",
        actual: usize::MAX,
        maximum: MAX_DAGGER_PROGRAM_NODES,
    })?;
    enforce_quota("program nodes", *nodes, MAX_DAGGER_PROGRAM_NODES)?;
    if depth > MAX_DAGGER_PROGRAM_DEPTH {
        return Err(DaggerGameplayError::Quota {
            field: "program depth",
            actual: usize::from(depth),
            maximum: usize::from(MAX_DAGGER_PROGRAM_DEPTH),
        });
    }
    let next_depth = depth.checked_add(1).ok_or(DaggerGameplayError::Quota {
        field: "program depth",
        actual: usize::MAX,
        maximum: usize::from(MAX_DAGGER_PROGRAM_DEPTH),
    })?;
    match program {
        AuthoredProgram::Sequence { steps } => Ok(Program::Sequence {
            steps: steps
                .into_iter()
                .map(|step| compile_program(step, nodes, next_depth, stats))
                .collect::<Result<_, _>>()?,
        }),
        AuthoredProgram::When {
            predicate,
            then_program,
            otherwise_program,
        } => Ok(Program::When {
            predicate: compile_predicate(predicate, stats)?,
            then_program: Box::new(compile_program(*then_program, nodes, next_depth, stats)?),
            otherwise_program: otherwise_program
                .map(|value| compile_program(*value, nodes, next_depth, stats).map(Box::new))
                .transpose()?,
        }),
        AuthoredProgram::Operation { operation } => {
            Ok(Program::Operation(compile_operation(operation, stats)?))
        }
    }
}

fn compile_predicate(
    value: AuthoredPredicate,
    stats: &DaggerStatsSection,
) -> Result<DaggerPredicate, DaggerGameplayError> {
    match value {
        AuthoredPredicate::Cmp { op, left, right } => {
            let mut nodes = 0_usize;
            let left = compile_expr(left, &mut nodes, 0, stats)?;
            let right = compile_expr(right, &mut nodes, 0, stats)?;
            Ok(match op {
                AuthoredCmpOp::Lt => ComposedExactComparison::LessThan(left, right),
                AuthoredCmpOp::Lte => ComposedExactComparison::LessOrEqual(left, right),
                AuthoredCmpOp::Eq => ComposedExactComparison::Equal(left, right),
                AuthoredCmpOp::Gte => ComposedExactComparison::GreaterOrEqual(left, right),
                AuthoredCmpOp::Gt => ComposedExactComparison::GreaterThan(left, right),
            })
        }
    }
}

fn compile_selector(value: AuthoredSelector) -> DaggerSelector {
    match value {
        AuthoredSelector::IntentTarget => DaggerSelector::IntentTarget,
    }
}

fn compile_operation(
    value: AuthoredOperation,
    stats: &DaggerStatsSection,
) -> Result<DaggerOperation, DaggerGameplayError> {
    match value {
        AuthoredOperation::SpendTrack { track, amount } => {
            validate_declared(
                "payload.actions[].program.operation.track",
                &track,
                &stats.tracks,
            )?;
            let mut nodes = 0_usize;
            Ok(DaggerOperation::SpendTrack {
                track,
                amount: compile_expr(amount, &mut nodes, 0, stats)?,
            })
        }
        AuthoredOperation::Damage { target, amount } => {
            let mut nodes = 0_usize;
            Ok(DaggerOperation::Damage {
                target: compile_selector(target),
                amount: compile_expr(amount, &mut nodes, 0, stats)?,
            })
        }
    }
}

fn compile_expr(
    expr: AuthoredExpr,
    nodes: &mut usize,
    depth: u16,
    stats: &DaggerStatsSection,
) -> Result<DaggerExpr, DaggerGameplayError> {
    *nodes = nodes.checked_add(1).ok_or(DaggerGameplayError::Quota {
        field: "expression nodes",
        actual: usize::MAX,
        maximum: MAX_DAGGER_EXPR_NODES,
    })?;
    enforce_quota("expression nodes", *nodes, MAX_DAGGER_EXPR_NODES)?;
    if depth > MAX_DAGGER_EXPR_DEPTH {
        return Err(DaggerGameplayError::Quota {
            field: "expression depth",
            actual: usize::from(depth),
            maximum: usize::from(MAX_DAGGER_EXPR_DEPTH),
        });
    }
    let next_depth = depth.checked_add(1).ok_or(DaggerGameplayError::Quota {
        field: "expression depth",
        actual: usize::MAX,
        maximum: usize::from(MAX_DAGGER_EXPR_DEPTH),
    })?;
    match expr {
        AuthoredExpr::Literal { value } => MechanicsScalar::new(value.0)
            .map(ComposedExactExpr::Literal)
            .map_err(|error| DaggerGameplayError::InvalidValue {
                path: "expression.literal".to_string(),
                reason: format!("mechanics scalar rejected: {error:?}"),
            }),
        AuthoredExpr::Input { input } => {
            compile_exact_input(input, stats).map(ComposedExactExpr::Input)
        }
        AuthoredExpr::Add { left, right } => Ok(ComposedExactExpr::Add(
            Box::new(compile_expr(*left, nodes, next_depth, stats)?),
            Box::new(compile_expr(*right, nodes, next_depth, stats)?),
        )),
        AuthoredExpr::Subtract { left, right } => Ok(ComposedExactExpr::Subtract(
            Box::new(compile_expr(*left, nodes, next_depth, stats)?),
            Box::new(compile_expr(*right, nodes, next_depth, stats)?),
        )),
        AuthoredExpr::Multiply { left, right } => Ok(ComposedExactExpr::Multiply(
            Box::new(compile_expr(*left, nodes, next_depth, stats)?),
            Box::new(compile_expr(*right, nodes, next_depth, stats)?),
        )),
        AuthoredExpr::FloorDivide { left, right } => Ok(ComposedExactExpr::FloorDivide(
            Box::new(compile_expr(*left, nodes, next_depth, stats)?),
            Box::new(compile_expr(*right, nodes, next_depth, stats)?),
        )),
        AuthoredExpr::TruncatingDivide { left, right } => Ok(ComposedExactExpr::TruncatingDivide(
            Box::new(compile_expr(*left, nodes, next_depth, stats)?),
            Box::new(compile_expr(*right, nodes, next_depth, stats)?),
        )),
        AuthoredExpr::Min { values } => Ok(ComposedExactExpr::Min(compile_expr_terms(
            values, nodes, next_depth, stats,
        )?)),
        AuthoredExpr::Max { values } => Ok(ComposedExactExpr::Max(compile_expr_terms(
            values, nodes, next_depth, stats,
        )?)),
        AuthoredExpr::Product {
            kind,
            payload,
            subject,
            source,
        } => compile_product_leaf(&kind, payload, &subject, &source, nodes, next_depth, stats),
    }
}

fn dagger_role(value: &str) -> Result<CapabilityRoleId, DaggerGameplayError> {
    if value != "actor" && value != "target" {
        return Err(DaggerGameplayError::InvalidValue {
            path: "expression.input.role".to_string(),
            reason: "Dagger exposes only the documented actor and target roles".to_string(),
        });
    }
    CapabilityRoleId::parse(value).map_err(|error| DaggerGameplayError::InvalidValue {
        path: "expression.input.role".to_string(),
        reason: format!("standard role rejected: {error:?}"),
    })
}

fn input_id(
    value: String,
    path: &str,
) -> Result<rusty_engine::gameplay_standard::InputId, DaggerGameplayError> {
    validate_id(path, &value)?;
    rusty_engine::gameplay_standard::InputId::parse(value).map_err(|error| {
        DaggerGameplayError::InvalidValue {
            path: path.to_string(),
            reason: format!("standard input rejected: {error:?}"),
        }
    })
}

fn compile_exact_input(
    input: AuthoredExactInput,
    stats: &DaggerStatsSection,
) -> Result<ExactInputReference, DaggerGameplayError> {
    match input {
        AuthoredExactInput::Roll { role, id } => Ok(ExactInputReference::Roll {
            role: dagger_role(&role)?,
            id: input_id(id, "expression.roll")?,
        }),
        AuthoredExactInput::StandardStat { role, stat } => {
            validate_id("expression.standardStat", &stat)?;
            if !stats.attributes.contains(&stat)
                && !stats.skills.contains(&stat)
                && !stats.progression.contains(&stat)
            {
                return Err(DaggerGameplayError::InvalidValue {
                    path: format!("expression.standardStat.{stat}"),
                    reason: "not declared in the stats section".to_string(),
                });
            }
            Ok(ExactInputReference::StandardFact(
                StandardExactFactReference::Stat {
                    role: dagger_role(&role)?,
                    stat: StatId::parse(stat).map_err(|error| {
                        DaggerGameplayError::InvalidValue {
                            path: "expression.standardStat".to_string(),
                            reason: format!("stat rejected: {error:?}"),
                        }
                    })?,
                },
            ))
        }
        AuthoredExactInput::StandardTrackCurrent { role, track } => {
            validate_declared("expression.standardTrackCurrent", &track, &stats.tracks)?;
            Ok(ExactInputReference::StandardFact(
                StandardExactFactReference::TrackCurrent {
                    role: dagger_role(&role)?,
                    track: TrackId::parse(track.clone()).map_err(|error| {
                        DaggerGameplayError::InvalidValue {
                            path: format!("expression.standardTrackCurrent.{track}"),
                            reason: format!("track rejected: {error:?}"),
                        }
                    })?,
                },
            ))
        }
        AuthoredExactInput::StandardTrackMaximum { role, track } => {
            validate_declared("expression.standardTrackMaximum", &track, &stats.tracks)?;
            Ok(ExactInputReference::StandardFact(
                StandardExactFactReference::TrackMaximum {
                    role: dagger_role(&role)?,
                    track: TrackId::parse(track.clone()).map_err(|error| {
                        DaggerGameplayError::InvalidValue {
                            path: format!("expression.standardTrackMaximum.{track}"),
                            reason: format!("track rejected: {error:?}"),
                        }
                    })?,
                },
            ))
        }
        AuthoredExactInput::Parameter { .. }
        | AuthoredExactInput::Fact { .. }
        | AuthoredExactInput::Choice { .. } => Err(DaggerGameplayError::InvalidValue {
            path: "expression.input".to_string(),
            reason: "Dagger has no declared adapter for free-form standard inputs".to_string(),
        }),
    }
}

fn compile_product_leaf(
    kind: &str,
    payload: serde_json::Value,
    subject: &str,
    source: &str,
    nodes: &mut usize,
    depth: u16,
    stats: &DaggerStatsSection,
) -> Result<DaggerExpr, DaggerGameplayError> {
    if subject != "dagger" || source != "dagger" {
        return Err(DaggerGameplayError::InvalidValue {
            path: "expression.product".to_string(),
            reason: "Dagger product leaves require the documented dagger subject and source"
                .to_string(),
        });
    }
    let wrapper = payload
        .as_object()
        .ok_or_else(|| DaggerGameplayError::InvalidValue {
            path: format!("expression.product.{kind}"),
            reason: "payload must be an object".to_string(),
        })?;
    let wrapper_fields = wrapper.keys().map(String::as_str).collect::<BTreeSet<_>>();
    if wrapper_fields != BTreeSet::from(["kind", "value"]) {
        return Err(DaggerGameplayError::InvalidValue {
            path: format!("expression.product.{kind}"),
            reason: "payload must contain exactly kind and value".to_string(),
        });
    }
    if wrapper.get("kind").and_then(serde_json::Value::as_str) != Some(kind) {
        return Err(DaggerGameplayError::InvalidValue {
            path: format!("expression.product.{kind}.kind"),
            reason: "must match the enclosing product kind".to_string(),
        });
    }
    let object = wrapper
        .get("value")
        .and_then(serde_json::Value::as_object)
        .ok_or_else(|| DaggerGameplayError::InvalidValue {
            path: format!("expression.product.{kind}.value"),
            reason: "must be an object".to_string(),
        })?;
    let decode_subject = |field: &str| -> Result<DaggerSubject, DaggerGameplayError> {
        match object.get(field).and_then(serde_json::Value::as_str) {
            Some("actor") => Ok(DaggerSubject::Actor),
            Some("target") => Ok(DaggerSubject::Target),
            _ => Err(DaggerGameplayError::InvalidValue {
                path: format!("expression.product.{kind}.{field}"),
                reason: "must be actor or target".to_string(),
            }),
        }
    };
    let require_fields = |fields: &[&str]| -> Result<(), DaggerGameplayError> {
        let actual = object.keys().map(String::as_str).collect::<BTreeSet<_>>();
        let expected = fields.iter().copied().collect::<BTreeSet<_>>();
        if actual == expected {
            Ok(())
        } else {
            Err(DaggerGameplayError::InvalidValue {
                path: format!("expression.product.{kind}"),
                reason: format!(
                    "must contain exactly [{}], got [{}]",
                    fields.join(", "),
                    actual.into_iter().collect::<Vec<_>>().join(", ")
                ),
            })
        }
    };
    let leaf = match kind {
        "equipped-weapon-skill" => {
            require_fields(&["subject"])?;
            DaggerExactLeaf::EquippedWeaponSkill {
                subject: decode_subject("subject")?,
            }
        }
        "dice" => {
            require_fields(&["id", "min", "max"])?;
            let id = object
                .get("id")
                .and_then(serde_json::Value::as_str)
                .ok_or_else(|| DaggerGameplayError::InvalidValue {
                    path: "expression.product.dice.id".to_string(),
                    reason: "must be a string".to_string(),
                })?
                .to_string();
            validate_id("expression.product.dice", &id)?;
            let min = json_i64(object.get("min"), "expression.product.dice.min")?;
            let max = json_i64(object.get("max"), "expression.product.dice.max")?;
            if min > max {
                return Err(DaggerGameplayError::InvalidValue {
                    path: format!("expression.product.dice.{id}"),
                    reason: format!("must satisfy min <= max, got {min}..{max}"),
                });
            }
            DaggerExactLeaf::Dice { id, min, max }
        }
        "equipped-weapon-dice" => {
            require_fields(&["subject", "id"])?;
            let id = object
                .get("id")
                .and_then(serde_json::Value::as_str)
                .ok_or_else(|| DaggerGameplayError::InvalidValue {
                    path: "expression.product.equippedWeaponDice.id".to_string(),
                    reason: "must be a string".to_string(),
                })?
                .to_string();
            validate_id("expression.product.equippedWeaponDice", &id)?;
            DaggerExactLeaf::EquippedWeaponDice {
                subject: decode_subject("subject")?,
                id,
            }
        }
        "struck-armor" => {
            require_fields(&["subject", "id"])?;
            if stats.armor_parts.is_empty() {
                return Err(DaggerGameplayError::InvalidValue {
                    path: "expression.product.struckArmor".to_string(),
                    reason: "requires declared stats.armorParts".to_string(),
                });
            }
            let id = object
                .get("id")
                .and_then(serde_json::Value::as_str)
                .ok_or_else(|| DaggerGameplayError::InvalidValue {
                    path: "expression.product.struckArmor.id".to_string(),
                    reason: "must be a string".to_string(),
                })?
                .to_string();
            validate_id("expression.product.struckArmor", &id)?;
            DaggerExactLeaf::StruckArmor {
                subject: decode_subject("subject")?,
                id,
            }
        }
        "pow-milli" => {
            require_fields(&["base", "exponent"])?;
            let base = serde_json::from_value(object.get("base").cloned().ok_or_else(|| {
                DaggerGameplayError::InvalidValue {
                    path: "expression.product.powMilli.base".to_string(),
                    reason: "missing".to_string(),
                }
            })?)
            .map_err(|error| DaggerGameplayError::InvalidValue {
                path: "expression.product.powMilli.base".to_string(),
                reason: error.to_string(),
            })?;
            let exponent =
                serde_json::from_value(object.get("exponent").cloned().ok_or_else(|| {
                    DaggerGameplayError::InvalidValue {
                        path: "expression.product.powMilli.exponent".to_string(),
                        reason: "missing".to_string(),
                    }
                })?)
                .map_err(|error| DaggerGameplayError::InvalidValue {
                    path: "expression.product.powMilli.exponent".to_string(),
                    reason: error.to_string(),
                })?;
            DaggerExactLeaf::PowMilli {
                base: Box::new(compile_expr(base, nodes, depth, stats)?),
                exponent: Box::new(compile_expr(exponent, nodes, depth, stats)?),
            }
        }
        _ => {
            return Err(DaggerGameplayError::InvalidValue {
                path: "expression.product.kind".to_string(),
                reason: format!("unsupported Dagger product leaf {kind}"),
            })
        }
    };
    let kind = ComposedExactLeafKindId::parse(kind).map_err(|error| {
        DaggerGameplayError::InvalidValue {
            path: "expression.product.kind".to_string(),
            reason: format!("kind rejected: {error:?}"),
        }
    })?;
    let subject = rusty_engine::gameplay_rules::RuleSubjectId::parse(subject).map_err(|error| {
        DaggerGameplayError::InvalidValue {
            path: "expression.product.subject".to_string(),
            reason: format!("subject rejected: {error:?}"),
        }
    })?;
    let source = rusty_engine::gameplay_rules::RuleSourceId::parse(source).map_err(|error| {
        DaggerGameplayError::InvalidValue {
            path: "expression.product.source".to_string(),
            reason: format!("source rejected: {error:?}"),
        }
    })?;
    Ok(ComposedExactExpr::Product(ComposedExactProductLeaf::new(
        kind, subject, source, leaf,
    )))
}

fn json_i64(value: Option<&serde_json::Value>, path: &str) -> Result<i64, DaggerGameplayError> {
    let value = value.ok_or_else(|| DaggerGameplayError::InvalidValue {
        path: path.to_string(),
        reason: "missing integer".to_string(),
    })?;
    if let Some(integer) = value.as_i64() {
        return Ok(integer);
    }
    let number = value
        .as_f64()
        .ok_or_else(|| DaggerGameplayError::InvalidValue {
            path: path.to_string(),
            reason: "must be an integer".to_string(),
        })?;
    // `i64::MAX as f64` rounds up to 2^63, which a Rust float-to-int cast
    // would saturate. The half-open upper bound admits only binary64 values
    // with one exact i64 representation.
    if number.is_finite()
        && number.fract() == 0.0
        && number >= i64::MIN as f64
        && number < (i64::MAX as f64)
    {
        Ok(number as i64)
    } else {
        Err(DaggerGameplayError::InvalidValue {
            path: path.to_string(),
            reason: "must be an integral binary64".to_string(),
        })
    }
}

fn compile_expr_terms(
    terms: Vec<AuthoredExpr>,
    nodes: &mut usize,
    depth: u16,
    stats: &DaggerStatsSection,
) -> Result<Vec<DaggerExpr>, DaggerGameplayError> {
    if terms.is_empty() {
        return Err(DaggerGameplayError::InvalidValue {
            path: "expression.terms".to_string(),
            reason: "must contain at least one term".to_string(),
        });
    }
    terms
        .into_iter()
        .map(|term| compile_expr(term, nodes, depth, stats))
        .collect()
}

fn validate_id(path: &str, value: &str) -> Result<(), DaggerGameplayError> {
    let valid = !value.is_empty()
        && value.len() <= MAX_DAGGER_ID_BYTES
        && value.bytes().all(|byte| {
            byte.is_ascii_lowercase() || byte.is_ascii_digit() || b"-_.".contains(&byte)
        });
    if valid {
        Ok(())
    } else {
        Err(DaggerGameplayError::InvalidId {
            path: path.to_string(),
            value: value.to_string(),
        })
    }
}

fn validate_declared(
    path: &str,
    value: &str,
    declared: &BTreeSet<String>,
) -> Result<(), DaggerGameplayError> {
    validate_id(path, value)?;
    if declared.contains(value) {
        Ok(())
    } else {
        Err(DaggerGameplayError::InvalidValue {
            path: format!("{path}.{value}"),
            reason: "not declared in the stats section".to_string(),
        })
    }
}

fn enforce_quota(
    field: &'static str,
    actual: usize,
    maximum: usize,
) -> Result<(), DaggerGameplayError> {
    if actual > maximum {
        Err(DaggerGameplayError::Quota {
            field,
            actual,
            maximum,
        })
    } else {
        Ok(())
    }
}
