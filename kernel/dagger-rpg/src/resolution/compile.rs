use std::collections::{BTreeMap, BTreeSet};

use rusty_engine::{
    gameplay_resolution::Program,
    gameplay_rules::{
        decode_rule_package, select_rule_payload_subtree, AdmittedRulePackage, RulePayloadPath,
        RulePayloadPathSegment,
    },
    gameplay_standard::{compile_composed_exact_embedded, ComposedExactComparison},
};

use super::{
    composed::DaggerExactLeafCodec, AuthoredActorDefinition, AuthoredCmpOp,
    AuthoredEncounterDefinition, AuthoredGameplayPayload, AuthoredItemDefinition,
    AuthoredLootTable, AuthoredOperation, AuthoredPredicate, AuthoredProgram,
    AuthoredRuleDefinition, AuthoredSelector, DaggerActionDefinition, DaggerActorDefinition,
    DaggerActorKind, DaggerArmorDefinition, DaggerBehaviorDefinition, DaggerDamageRange,
    DaggerDerivedRule, DaggerEmbeddedExpressionEvidence, DaggerEncounterDefinition,
    DaggerEquipmentSection, DaggerEquipmentSlotDefinition, DaggerExpr, DaggerGameplayCatalog,
    DaggerGameplayError, DaggerItemDefinition, DaggerItemEquipment, DaggerLoadoutEntry,
    DaggerLootCategories, DaggerLootTable, DaggerOperation, DaggerPredicate, DaggerProgram,
    DaggerRuleDefinition, DaggerSelector, DaggerShieldDefinition, DaggerStatsSection,
    DaggerTrackDefinition, DaggerWeaponDefinition, DaggerWeaponHands,
    DAGGER_GAMEPLAY_SCHEMA_VERSION, MAX_BEHAVIOR_VALUE, MAX_DAGGER_ACTIONS, MAX_DAGGER_ACTORS,
    MAX_DAGGER_DECLARED_IDS, MAX_DAGGER_DERIVED, MAX_DAGGER_ENCOUNTERS,
    MAX_DAGGER_ENCOUNTER_MEMBERS, MAX_DAGGER_ID_BYTES, MAX_DAGGER_ITEMS, MAX_DAGGER_LOOT_TABLES,
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
    let payload = rusty_engine::product_kernel::serde_json::from_value::<AuthoredGameplayPayload>(
        package.payload().clone(),
    )
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
    let mut embedded_expression_evidence = Vec::new();
    let actions = compile_actions(
        &package,
        payload.actions,
        &stats,
        &mut embedded_expression_evidence,
    )?;
    let actors = compile_actors(
        &package,
        payload.actors,
        &stats,
        &actions,
        &items,
        &equipment,
        &mut embedded_expression_evidence,
    )?;
    let rules = compile_rules(payload.rules)?;
    let encounters = compile_encounters(payload.encounters)?;
    let derived = compile_derived(
        &package,
        payload.derived,
        &stats,
        &mut embedded_expression_evidence,
    )?;
    let loot_tables = compile_loot_tables(payload.loot_tables)?;
    let mechanics = super::mechanics::compile_mechanics_catalog(&stats, &items, &equipment)?;
    Ok(DaggerGameplayCatalog::new(
        package.fingerprint().as_str().to_string(),
        embedded_expression_evidence,
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
    package: &AdmittedRulePackage,
    definitions: Vec<super::AuthoredDerivedRule>,
    stats: &DaggerStatsSection,
    evidence: &mut Vec<DaggerEmbeddedExpressionEvidence>,
) -> Result<BTreeMap<String, DaggerDerivedRule>, DaggerGameplayError> {
    let mut derived = BTreeMap::new();
    for (index, rule) in definitions.into_iter().enumerate() {
        validate_id(&format!("payload.derived[{index}].id"), &rule.id)?;
        let expr = compile_expr(
            package,
            payload_path(&[field("derived")?, at_index(index)?, field("expr")?])?,
            stats,
            evidence,
        )?;
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
    package: &AdmittedRulePackage,
    definitions: Vec<super::AuthoredActionDefinition>,
    stats: &DaggerStatsSection,
    evidence: &mut Vec<DaggerEmbeddedExpressionEvidence>,
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
        let program = compile_program(
            package,
            action.program,
            payload_path(&[field("actions")?, at_index(index)?, field("program")?])?,
            &mut nodes,
            0,
            stats,
            evidence,
        )?;
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
    package: &AdmittedRulePackage,
    definitions: Vec<AuthoredActorDefinition>,
    stats: &DaggerStatsSection,
    actions: &BTreeMap<String, DaggerActionDefinition>,
    items: &BTreeMap<String, DaggerItemDefinition>,
    equipment: &DaggerEquipmentSection,
    evidence: &mut Vec<DaggerEmbeddedExpressionEvidence>,
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
            tracks.push(DaggerTrackDefinition {
                id: track.id,
                max: compile_expr(
                    package,
                    payload_path(&[
                        field("actors")?,
                        at_index(index)?,
                        field("tracks")?,
                        at_index(track_index)?,
                        field("max")?,
                    ])?,
                    stats,
                    evidence,
                )?,
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
    package: &AdmittedRulePackage,
    program: AuthoredProgram,
    location: RulePayloadPath,
    nodes: &mut usize,
    depth: u16,
    stats: &DaggerStatsSection,
    evidence: &mut Vec<DaggerEmbeddedExpressionEvidence>,
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
                .enumerate()
                .map(|(index, step)| {
                    compile_program(
                        package,
                        step,
                        child(&child(&location, field("steps")?)?, at_index(index)?)?,
                        nodes,
                        next_depth,
                        stats,
                        evidence,
                    )
                })
                .collect::<Result<_, _>>()?,
        }),
        AuthoredProgram::When {
            predicate,
            then_program,
            otherwise_program,
        } => Ok(Program::When {
            predicate: compile_predicate(
                package,
                predicate,
                child(&location, field("predicate")?)?,
                stats,
                evidence,
            )?,
            then_program: Box::new(compile_program(
                package,
                *then_program,
                child(&location, field("thenProgram")?)?,
                nodes,
                next_depth,
                stats,
                evidence,
            )?),
            otherwise_program: otherwise_program
                .map(|value| {
                    compile_program(
                        package,
                        *value,
                        child(&location, field("otherwiseProgram")?)?,
                        nodes,
                        next_depth,
                        stats,
                        evidence,
                    )
                    .map(Box::new)
                })
                .transpose()?,
        }),
        AuthoredProgram::Operation { operation } => Ok(Program::Operation(compile_operation(
            package,
            operation,
            child(&location, field("operation")?)?,
            stats,
            evidence,
        )?)),
    }
}

fn compile_predicate(
    package: &AdmittedRulePackage,
    value: AuthoredPredicate,
    location: RulePayloadPath,
    stats: &DaggerStatsSection,
    evidence: &mut Vec<DaggerEmbeddedExpressionEvidence>,
) -> Result<DaggerPredicate, DaggerGameplayError> {
    match value {
        AuthoredPredicate::Cmp { op, .. } => {
            let left = compile_expr(package, child(&location, field("left")?)?, stats, evidence)?;
            let right = compile_expr(package, child(&location, field("right")?)?, stats, evidence)?;
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
    package: &AdmittedRulePackage,
    value: AuthoredOperation,
    location: RulePayloadPath,
    stats: &DaggerStatsSection,
    evidence: &mut Vec<DaggerEmbeddedExpressionEvidence>,
) -> Result<DaggerOperation, DaggerGameplayError> {
    match value {
        AuthoredOperation::SpendTrack { track, .. } => {
            validate_declared(
                "payload.actions[].program.operation.track",
                &track,
                &stats.tracks,
            )?;
            Ok(DaggerOperation::SpendTrack {
                track,
                amount: compile_expr(
                    package,
                    child(&location, field("amount")?)?,
                    stats,
                    evidence,
                )?,
            })
        }
        AuthoredOperation::Damage { target, .. } => Ok(DaggerOperation::Damage {
            target: compile_selector(target),
            amount: compile_expr(
                package,
                child(&location, field("amount")?)?,
                stats,
                evidence,
            )?,
        }),
    }
}

fn compile_expr(
    package: &AdmittedRulePackage,
    location: RulePayloadPath,
    stats: &DaggerStatsSection,
    evidence: &mut Vec<DaggerEmbeddedExpressionEvidence>,
) -> Result<DaggerExpr, DaggerGameplayError> {
    let selected = select_rule_payload_subtree(package, package.fingerprint(), location.clone())
        .map_err(|error| DaggerGameplayError::EmbeddedExpression {
            path: location.display().to_owned(),
            reason: error.to_string(),
        })?;
    let admitted =
        compile_composed_exact_embedded::<DaggerExactLeafCodec>(&selected).map_err(|error| {
            DaggerGameplayError::EmbeddedExpression {
                path: error.context().path().to_owned(),
                reason: error.to_string(),
            }
        })?;
    let expression = admitted.definition().expression().clone();
    validate_expr_inputs(&expression, stats, location.display())?;
    evidence.push(DaggerEmbeddedExpressionEvidence {
        parent_identity: selected.parent_identity().to_string(),
        parent_fingerprint: selected.parent_fingerprint().as_str().to_string(),
        path: selected.path().display().to_string(),
        canonical_bytes: selected.canonical_bytes().to_vec(),
    });
    Ok(expression)
}

fn validate_expr_inputs(
    expression: &DaggerExpr,
    stats: &DaggerStatsSection,
    path: &str,
) -> Result<(), DaggerGameplayError> {
    use rusty_engine::gameplay_standard::{
        ComposedExactExpr, ExactInputReference, StandardExactFactReference,
    };
    match expression {
        ComposedExactExpr::Literal(_) => Ok(()),
        ComposedExactExpr::Input(ExactInputReference::Roll { role, id }) => {
            validate_dagger_role(role.as_str(), path)?;
            validate_id(path, id.as_str())
        }
        ComposedExactExpr::Input(ExactInputReference::BoundedRoll { descriptor }) => {
            validate_dagger_role(descriptor.role().as_str(), path)?;
            validate_id(path, descriptor.id().as_str())
        }
        ComposedExactExpr::Input(ExactInputReference::StandardFact(
            StandardExactFactReference::Stat { role, stat },
        )) => {
            validate_dagger_role(role.as_str(), path)?;
            if stats.attributes.contains(stat.as_str())
                || stats.skills.contains(stat.as_str())
                || stats.progression.contains(stat.as_str())
            {
                Ok(())
            } else {
                Err(DaggerGameplayError::InvalidValue {
                    path: path.to_owned(),
                    reason: format!("standard stat {} is not declared", stat.as_str()),
                })
            }
        }
        ComposedExactExpr::Input(ExactInputReference::StandardFact(
            StandardExactFactReference::TrackCurrent { role, track },
        ))
        | ComposedExactExpr::Input(ExactInputReference::StandardFact(
            StandardExactFactReference::TrackMaximum { role, track },
        )) => {
            validate_dagger_role(role.as_str(), path)?;
            validate_declared(path, track.as_str(), &stats.tracks)
        }
        ComposedExactExpr::Input(_) => Err(DaggerGameplayError::InvalidValue {
            path: path.to_owned(),
            reason: "Dagger has no declared adapter for free-form standard inputs".to_string(),
        }),
        ComposedExactExpr::Add(left, right)
        | ComposedExactExpr::Subtract(left, right)
        | ComposedExactExpr::Multiply(left, right)
        | ComposedExactExpr::FloorDivide(left, right)
        | ComposedExactExpr::TruncatingDivide(left, right) => {
            validate_expr_inputs(left, stats, path)?;
            validate_expr_inputs(right, stats, path)
        }
        ComposedExactExpr::FixedPower { base, exponent, .. } => {
            validate_expr_inputs(base, stats, path)?;
            validate_expr_inputs(exponent, stats, path)
        }
        ComposedExactExpr::Min(values) | ComposedExactExpr::Max(values) => {
            for value in values {
                validate_expr_inputs(value, stats, path)?;
            }
            Ok(())
        }
        ComposedExactExpr::Product(leaf) => {
            if leaf.subject().as_str() != "dagger" || leaf.source().as_str() != "dagger" {
                return Err(DaggerGameplayError::InvalidValue {
                    path: path.to_owned(),
                    reason: "Dagger product leaves require documented dagger provenance"
                        .to_string(),
                });
            }
            if matches!(leaf.value(), super::DaggerExactLeaf::StruckArmor { .. })
                && stats.armor_parts.is_empty()
            {
                return Err(DaggerGameplayError::InvalidValue {
                    path: path.to_owned(),
                    reason: "struck armor requires declared stats.armorParts".to_string(),
                });
            }
            Ok(())
        }
    }
}

fn validate_dagger_role(role: &str, path: &str) -> Result<(), DaggerGameplayError> {
    if role == "actor" || role == "target" {
        Ok(())
    } else {
        Err(DaggerGameplayError::InvalidValue {
            path: path.to_owned(),
            reason: "Dagger exposes only actor and target roles".to_string(),
        })
    }
}

fn field(value: &str) -> Result<RulePayloadPathSegment, DaggerGameplayError> {
    RulePayloadPathSegment::field(value).map_err(|error| DaggerGameplayError::EmbeddedExpression {
        path: value.to_owned(),
        reason: error.to_string(),
    })
}

fn at_index(value: usize) -> Result<RulePayloadPathSegment, DaggerGameplayError> {
    RulePayloadPathSegment::index(value).map_err(|error| DaggerGameplayError::EmbeddedExpression {
        path: value.to_string(),
        reason: error.to_string(),
    })
}

fn payload_path(
    segments: &[RulePayloadPathSegment],
) -> Result<RulePayloadPath, DaggerGameplayError> {
    RulePayloadPath::new(segments.to_vec()).map_err(|error| {
        DaggerGameplayError::EmbeddedExpression {
            path: "payload".to_string(),
            reason: error.to_string(),
        }
    })
}

fn child(
    parent: &RulePayloadPath,
    segment: RulePayloadPathSegment,
) -> Result<RulePayloadPath, DaggerGameplayError> {
    let mut segments = parent.segments().to_vec();
    segments.push(segment);
    payload_path(&segments)
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
