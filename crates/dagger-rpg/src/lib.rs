//! Host-neutral gameplay semantics for interactive Dagger experiments.
//!
//! TypeScript and JSON author a small immutable document. This crate is the
//! only authority that admits that document, calculates derived values, and
//! produces the calculation steps shown by designer tools.

#![forbid(unsafe_code)]

use std::collections::BTreeSet;

use serde::{Deserialize, Serialize};

pub const EXPERIMENT_SCHEMA_VERSION: u32 = 1;
pub const MIN_MOVE_SPEED_UNITS_PER_SECOND: f32 = 0.1;
pub const MAX_MOVE_SPEED_UNITS_PER_SECOND: f32 = 50.0;
pub const MAX_STAT_INPUT: f32 = 10_000.0;
pub const MAX_ENEMY_DEFINITIONS: usize = 64;

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct ExperimentDocument {
    pub schema_version: u32,
    pub player: PlayerExperiment,
    pub enemies: Vec<EnemyExperiment>,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct PlayerExperiment {
    pub movement: PlayerMovementExperiment,
    pub stats: ActorStatsExperiment,
    pub combat: PlayerCombatExperiment,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct PlayerMovementExperiment {
    pub speed_units_per_second: f32,
}

/// One gameplay definition keyed to the classic identity owned by `arena2`.
///
/// This crate deliberately stores no mobile name, sprite, or identity table.
/// The Dagger runtime joins this authored gameplay definition to an admitted
/// project through `mobile_id`.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct EnemyExperiment {
    pub mobile_id: u8,
    pub stats: ActorStatsExperiment,
    pub combat: EnemyCombatExperiment,
    pub behavior: EnemyBehaviorExperiment,
}

/// The editable terms for the first player melee experiment. Rust owns the
/// closed hit and damage formula shapes; authoring changes only named inputs.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct PlayerCombatExperiment {
    pub attack_range: f32,
    pub hit_bonus: f32,
    pub base_damage: f32,
    pub damage_per_strength: f32,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct EnemyCombatExperiment {
    pub defense: f32,
    pub armor: f32,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct EnemyBehaviorExperiment {
    pub detection_range: f32,
    pub patrol_speed: f32,
    pub chase_speed: f32,
    pub attack_range: f32,
    pub attack_cooldown_seconds: f32,
    pub attack_damage: f32,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct ActorStatsExperiment {
    pub attributes: ActorAttributes,
    pub resources: ActorResourceTerms,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct ActorAttributes {
    pub strength: f32,
    pub endurance: f32,
    pub intelligence: f32,
}

/// Named inputs for the three fixed resource formulas required by the first
/// player-versus-Rat experiment.
///
/// Rust owns these formula shapes. The document exposes useful knobs without
/// defining an expression language or allowing TypeScript to evaluate them.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct ActorResourceTerms {
    pub base_health: f32,
    pub health_per_endurance: f32,
    pub base_stamina: f32,
    pub stamina_per_attribute: f32,
    pub base_magicka: f32,
    pub magicka_per_intelligence: f32,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct AdmittedExperiment {
    pub document: ExperimentDocument,
    pub player: AdmittedPlayerValues,
    pub enemies: Vec<AdmittedEnemyValues>,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct AdmittedPlayerValues {
    pub move_speed_units_per_second: f32,
    pub stats: AdmittedActorValues,
    pub combat: PlayerCombatExperiment,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct AdmittedEnemyValues {
    pub mobile_id: u8,
    pub stats: AdmittedActorValues,
    pub combat: EnemyCombatExperiment,
    pub behavior: EnemyBehaviorExperiment,
}

#[derive(Debug, Clone, PartialEq)]
pub struct MeleeResolutionInput<'a> {
    pub actor: &'a str,
    pub target: &'a str,
    pub raw_roll: u8,
    pub player: &'a AdmittedPlayerValues,
    pub enemy: &'a AdmittedEnemyValues,
    pub target_health_before: f32,
}

/// Designer-facing semantic record for one legal melee attack. Range and
/// collision admission are runtime concerns and are recorded alongside this
/// result by the Dagger runtime.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct MeleeResolutionRecord {
    pub actor: String,
    pub action: String,
    pub target: String,
    pub raw_roll: u8,
    pub hit_bonus: f32,
    pub attack_total: f32,
    pub target_defense: f32,
    pub hit: bool,
    pub base_damage: f32,
    pub strength: f32,
    pub damage_per_strength: f32,
    pub damage_before_armor: f32,
    pub armor: f32,
    pub final_damage: f32,
    pub health_before: f32,
    pub health_after: f32,
    pub died: bool,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct AdmittedActorValues {
    pub attributes: ActorAttributes,
    pub max_health: f32,
    pub max_stamina: f32,
    pub max_magicka: f32,
    pub calculations: Vec<CalculationRecord>,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct CalculationRecord {
    pub rule: String,
    pub expression: String,
    pub inputs: Vec<CalculationInput>,
    pub steps: Vec<CalculationStep>,
    pub result: f32,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct CalculationInput {
    pub name: String,
    pub value: f32,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct CalculationStep {
    pub operation: String,
    pub left: f32,
    pub right: f32,
    pub result: f32,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum ExperimentError {
    Json(String),
    UnsupportedSchema { actual: u32, expected: u32 },
    InvalidValue { path: String, reason: String },
}

impl std::fmt::Display for ExperimentError {
    fn fmt(&self, formatter: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        match self {
            Self::Json(message) => write!(formatter, "invalid experiment JSON: {message}"),
            Self::UnsupportedSchema { actual, expected } => write!(
                formatter,
                "unsupported experiment schemaVersion {actual}; expected {expected}"
            ),
            Self::InvalidValue { path, reason } => write!(formatter, "{path}: {reason}"),
        }
    }
}

impl std::error::Error for ExperimentError {}

impl ExperimentDocument {
    pub fn from_json(document: &str) -> Result<Self, ExperimentError> {
        serde_json::from_str(document).map_err(|error| ExperimentError::Json(error.to_string()))
    }

    pub fn admit(self) -> Result<AdmittedExperiment, ExperimentError> {
        if self.schema_version != EXPERIMENT_SCHEMA_VERSION {
            return Err(ExperimentError::UnsupportedSchema {
                actual: self.schema_version,
                expected: EXPERIMENT_SCHEMA_VERSION,
            });
        }
        finite_in_range(
            "player.movement.speedUnitsPerSecond",
            self.player.movement.speed_units_per_second,
            MIN_MOVE_SPEED_UNITS_PER_SECOND,
            MAX_MOVE_SPEED_UNITS_PER_SECOND,
        )?;
        for (path, value, minimum, maximum) in [
            (
                "player.combat.attackRange",
                self.player.combat.attack_range,
                0.1,
                10.0,
            ),
            (
                "player.combat.hitBonus",
                self.player.combat.hit_bonus,
                -100.0,
                100.0,
            ),
            (
                "player.combat.baseDamage",
                self.player.combat.base_damage,
                0.0,
                MAX_STAT_INPUT,
            ),
            (
                "player.combat.damagePerStrength",
                self.player.combat.damage_per_strength,
                0.0,
                100.0,
            ),
        ] {
            finite_in_range(path, value, minimum, maximum)?;
        }
        if self.enemies.len() > MAX_ENEMY_DEFINITIONS {
            return Err(ExperimentError::InvalidValue {
                path: "enemies".to_string(),
                reason: format!("must contain no more than {MAX_ENEMY_DEFINITIONS} definitions"),
            });
        }

        let mut mobile_ids = BTreeSet::new();
        for (index, enemy) in self.enemies.iter().enumerate() {
            if !mobile_ids.insert(enemy.mobile_id) {
                return Err(ExperimentError::InvalidValue {
                    path: format!("enemies[{index}].mobileId"),
                    reason: format!(
                        "duplicate gameplay definition for mobile {}",
                        enemy.mobile_id
                    ),
                });
            }
            finite_in_range(
                format!("enemies[{index}].combat.defense"),
                enemy.combat.defense,
                0.0,
                200.0,
            )?;
            for (name, value, minimum, maximum) in [
                ("detectionRange", enemy.behavior.detection_range, 0.1, 100.0),
                ("patrolSpeed", enemy.behavior.patrol_speed, 0.0, 20.0),
                ("chaseSpeed", enemy.behavior.chase_speed, 0.1, 20.0),
                ("attackRange", enemy.behavior.attack_range, 0.1, 10.0),
                (
                    "attackCooldownSeconds",
                    enemy.behavior.attack_cooldown_seconds,
                    0.1,
                    60.0,
                ),
                (
                    "attackDamage",
                    enemy.behavior.attack_damage,
                    0.0,
                    MAX_STAT_INPUT,
                ),
            ] {
                finite_in_range(
                    format!("enemies[{index}].behavior.{name}"),
                    value,
                    minimum,
                    maximum,
                )?;
            }
            finite_in_range(
                format!("enemies[{index}].combat.armor"),
                enemy.combat.armor,
                0.0,
                MAX_STAT_INPUT,
            )?;
        }

        let player_stats = admit_actor_stats("player", "player.stats", &self.player.stats)?;
        let enemies = self
            .enemies
            .iter()
            .enumerate()
            .map(|(index, enemy)| {
                Ok(AdmittedEnemyValues {
                    mobile_id: enemy.mobile_id,
                    combat: enemy.combat.clone(),
                    behavior: enemy.behavior.clone(),
                    stats: admit_actor_stats(
                        &format!("enemy.mobile{}", enemy.mobile_id),
                        &format!("enemies[{index}].stats"),
                        &enemy.stats,
                    )?,
                })
            })
            .collect::<Result<Vec<_>, ExperimentError>>()?;
        Ok(AdmittedExperiment {
            player: AdmittedPlayerValues {
                move_speed_units_per_second: self.player.movement.speed_units_per_second,
                stats: player_stats,
                combat: self.player.combat.clone(),
            },
            enemies,
            document: self,
        })
    }
}

pub fn admit_json(document: &str) -> Result<AdmittedExperiment, ExperimentError> {
    ExperimentDocument::from_json(document)?.admit()
}

/// Resolve one already-admitted melee attempt. The roll is supplied by the
/// runtime so this semantic authority stays host-neutral and easy to exercise
/// with ordinary formula examples.
pub fn resolve_melee_attack(input: MeleeResolutionInput<'_>) -> MeleeResolutionRecord {
    let player = &input.player;
    let enemy = &input.enemy;
    let attack_total = f32::from(input.raw_roll) + player.combat.hit_bonus;
    let hit = attack_total >= enemy.combat.defense;
    let strength_damage = player.stats.attributes.strength * player.combat.damage_per_strength;
    let damage_before_armor = player.combat.base_damage + strength_damage;
    let final_damage = if hit {
        (damage_before_armor - enemy.combat.armor).max(0.0)
    } else {
        0.0
    };
    let health_after = (input.target_health_before - final_damage).max(0.0);
    MeleeResolutionRecord {
        actor: input.actor.to_string(),
        action: "melee attack".to_string(),
        target: input.target.to_string(),
        raw_roll: input.raw_roll,
        hit_bonus: player.combat.hit_bonus,
        attack_total,
        target_defense: enemy.combat.defense,
        hit,
        base_damage: player.combat.base_damage,
        strength: player.stats.attributes.strength,
        damage_per_strength: player.combat.damage_per_strength,
        damage_before_armor,
        armor: enemy.combat.armor,
        final_damage,
        health_before: input.target_health_before,
        health_after,
        died: input.target_health_before > 0.0 && health_after <= 0.0,
    }
}

fn admit_actor_stats(
    rule_prefix: &str,
    document_prefix: &str,
    stats: &ActorStatsExperiment,
) -> Result<AdmittedActorValues, ExperimentError> {
    for (name, value) in [
        ("attributes.strength", stats.attributes.strength),
        ("attributes.endurance", stats.attributes.endurance),
        ("attributes.intelligence", stats.attributes.intelligence),
        ("resources.baseHealth", stats.resources.base_health),
        (
            "resources.healthPerEndurance",
            stats.resources.health_per_endurance,
        ),
        ("resources.baseStamina", stats.resources.base_stamina),
        (
            "resources.staminaPerAttribute",
            stats.resources.stamina_per_attribute,
        ),
        ("resources.baseMagicka", stats.resources.base_magicka),
        (
            "resources.magickaPerIntelligence",
            stats.resources.magicka_per_intelligence,
        ),
    ] {
        finite_in_range(
            format!("{document_prefix}.{name}"),
            value,
            0.0,
            MAX_STAT_INPUT,
        )?;
    }

    let attributes = &stats.attributes;
    let resources = &stats.resources;
    let endurance_health = attributes.endurance * resources.health_per_endurance;
    let max_health = resources.base_health + endurance_health;
    let stamina_attributes = attributes.strength + attributes.endurance;
    let attribute_stamina = stamina_attributes * resources.stamina_per_attribute;
    let max_stamina = resources.base_stamina + attribute_stamina;
    let intelligence_magicka = attributes.intelligence * resources.magicka_per_intelligence;
    let max_magicka = resources.base_magicka + intelligence_magicka;
    for (name, value) in [
        ("maxHealth", max_health),
        ("maxStamina", max_stamina),
        ("maxMagicka", max_magicka),
    ] {
        if !value.is_finite() || value > MAX_STAT_INPUT {
            return Err(ExperimentError::InvalidValue {
                path: document_prefix.to_string(),
                reason: format!(
                    "derived {name} must be finite and no greater than {MAX_STAT_INPUT}"
                ),
            });
        }
    }

    let calculations = vec![
        CalculationRecord {
            rule: format!("{rule_prefix}.maxHealth"),
            expression: "baseHealth + endurance * healthPerEndurance".to_string(),
            inputs: vec![
                input("baseHealth", resources.base_health),
                input("endurance", attributes.endurance),
                input("healthPerEndurance", resources.health_per_endurance),
            ],
            steps: vec![
                step(
                    "multiply",
                    attributes.endurance,
                    resources.health_per_endurance,
                    endurance_health,
                ),
                step("add", resources.base_health, endurance_health, max_health),
            ],
            result: max_health,
        },
        CalculationRecord {
            rule: format!("{rule_prefix}.maxStamina"),
            expression: "baseStamina + (strength + endurance) * staminaPerAttribute".to_string(),
            inputs: vec![
                input("baseStamina", resources.base_stamina),
                input("strength", attributes.strength),
                input("endurance", attributes.endurance),
                input("staminaPerAttribute", resources.stamina_per_attribute),
            ],
            steps: vec![
                step(
                    "add attributes",
                    attributes.strength,
                    attributes.endurance,
                    stamina_attributes,
                ),
                step(
                    "multiply",
                    stamina_attributes,
                    resources.stamina_per_attribute,
                    attribute_stamina,
                ),
                step(
                    "add",
                    resources.base_stamina,
                    attribute_stamina,
                    max_stamina,
                ),
            ],
            result: max_stamina,
        },
        CalculationRecord {
            rule: format!("{rule_prefix}.maxMagicka"),
            expression: "baseMagicka + intelligence * magickaPerIntelligence".to_string(),
            inputs: vec![
                input("baseMagicka", resources.base_magicka),
                input("intelligence", attributes.intelligence),
                input("magickaPerIntelligence", resources.magicka_per_intelligence),
            ],
            steps: vec![
                step(
                    "multiply",
                    attributes.intelligence,
                    resources.magicka_per_intelligence,
                    intelligence_magicka,
                ),
                step(
                    "add",
                    resources.base_magicka,
                    intelligence_magicka,
                    max_magicka,
                ),
            ],
            result: max_magicka,
        },
    ];

    Ok(AdmittedActorValues {
        attributes: attributes.clone(),
        max_health,
        max_stamina,
        max_magicka,
        calculations,
    })
}

fn input(name: &str, value: f32) -> CalculationInput {
    CalculationInput {
        name: name.to_string(),
        value,
    }
}

fn step(operation: &str, left: f32, right: f32, result: f32) -> CalculationStep {
    CalculationStep {
        operation: operation.to_string(),
        left,
        right,
        result,
    }
}

fn finite_in_range(
    path: impl Into<String>,
    value: f32,
    minimum: f32,
    maximum: f32,
) -> Result<(), ExperimentError> {
    if value.is_finite() && (minimum..=maximum).contains(&value) {
        Ok(())
    } else {
        Err(ExperimentError::InvalidValue {
            path: path.into(),
            reason: format!("must be finite and between {minimum} and {maximum}"),
        })
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn stats(strength: f32, endurance: f32, intelligence: f32) -> ActorStatsExperiment {
        ActorStatsExperiment {
            attributes: ActorAttributes {
                strength,
                endurance,
                intelligence,
            },
            resources: ActorResourceTerms {
                base_health: 25.0,
                health_per_endurance: 1.5,
                base_stamina: 0.0,
                stamina_per_attribute: 1.0,
                base_magicka: 0.0,
                magicka_per_intelligence: 1.0,
            },
        }
    }

    fn starter() -> ExperimentDocument {
        ExperimentDocument {
            schema_version: EXPERIMENT_SCHEMA_VERSION,
            player: PlayerExperiment {
                movement: PlayerMovementExperiment {
                    speed_units_per_second: 3.5,
                },
                stats: stats(50.0, 40.0, 50.0),
                combat: PlayerCombatExperiment {
                    attack_range: 2.25,
                    hit_bonus: 35.0,
                    base_damage: 1.0,
                    damage_per_strength: 0.1,
                },
            },
            enemies: vec![EnemyExperiment {
                mobile_id: 0,
                stats: ActorStatsExperiment {
                    attributes: ActorAttributes {
                        strength: 10.0,
                        endurance: 10.0,
                        intelligence: 0.0,
                    },
                    resources: ActorResourceTerms {
                        base_health: 2.0,
                        health_per_endurance: 0.1,
                        base_stamina: 0.0,
                        stamina_per_attribute: 0.5,
                        base_magicka: 0.0,
                        magicka_per_intelligence: 0.0,
                    },
                },
                combat: EnemyCombatExperiment {
                    defense: 50.0,
                    armor: 1.0,
                },
                behavior: EnemyBehaviorExperiment {
                    detection_range: 6.0,
                    patrol_speed: 1.0,
                    chase_speed: 2.0,
                    attack_range: 1.25,
                    attack_cooldown_seconds: 1.5,
                    attack_damage: 4.0,
                },
            }],
        }
    }

    #[test]
    fn admits_player_and_rat_stats_and_explains_rust_owned_formulas() {
        let admitted = starter().admit().expect("admit starter experiment");
        assert_eq!(admitted.player.move_speed_units_per_second, 3.5);
        assert_eq!(admitted.player.stats.max_health, 85.0);
        assert_eq!(admitted.player.stats.max_stamina, 90.0);
        assert_eq!(admitted.player.stats.max_magicka, 50.0);
        assert_eq!(admitted.enemies[0].mobile_id, 0);
        assert_eq!(admitted.enemies[0].stats.max_health, 3.0);
        assert_eq!(admitted.enemies[0].stats.max_stamina, 10.0);
        assert_eq!(admitted.enemies[0].stats.max_magicka, 0.0);
        assert_eq!(admitted.player.stats.calculations.len(), 3);
        assert_eq!(admitted.enemies[0].stats.calculations.len(), 3);
        assert_eq!(
            admitted.player.stats.calculations[0].rule,
            "player.maxHealth"
        );
        assert_eq!(
            admitted.enemies[0].stats.calculations[0].rule,
            "enemy.mobile0.maxHealth"
        );
    }

    #[test]
    fn rejects_unknown_fields_invalid_values_and_duplicate_mobile_ids() {
        let json = r#"{
            "schemaVersion": 1,
            "player": {
                "movement": { "speedUnitsPerSecond": 3.5, "hidden": true },
                "stats": {
                    "attributes": { "strength": 50, "endurance": 40, "intelligence": 50 },
                    "resources": { "baseHealth": 25, "healthPerEndurance": 1.5, "baseStamina": 0, "staminaPerAttribute": 1, "baseMagicka": 0, "magickaPerIntelligence": 1 }
                }
            },
            "enemies": []
        }"#;
        assert!(matches!(admit_json(json), Err(ExperimentError::Json(_))));

        let mut document = starter();
        document.player.movement.speed_units_per_second = 0.0;
        assert!(matches!(
            document.admit(),
            Err(ExperimentError::InvalidValue { path, .. })
                if path == "player.movement.speedUnitsPerSecond"
        ));

        let mut document = starter();
        document.enemies.push(document.enemies[0].clone());
        assert!(matches!(
            document.admit(),
            Err(ExperimentError::InvalidValue { path, .. }) if path == "enemies[1].mobileId"
        ));
    }

    #[test]
    fn rejects_a_derived_value_outside_the_bounded_gameplay_range() {
        let mut document = starter();
        document.player.stats.resources.base_health = MAX_STAT_INPUT;
        document.player.stats.attributes.endurance = 1.0;
        assert!(matches!(
            document.admit(),
            Err(ExperimentError::InvalidValue { path, .. }) if path == "player.stats"
        ));
    }

    #[test]
    fn resolves_hit_miss_armor_health_and_death_as_semantic_records() {
        let admitted = starter().admit().expect("admit combat experiment");
        let miss = resolve_melee_attack(MeleeResolutionInput {
            actor: "Player",
            target: "Rat 2007",
            raw_roll: 10,
            player: &admitted.player,
            enemy: &admitted.enemies[0],
            target_health_before: 3.0,
        });
        assert!(!miss.hit);
        assert_eq!(miss.final_damage, 0.0);
        assert_eq!(miss.health_after, 3.0);

        let hit = resolve_melee_attack(MeleeResolutionInput {
            raw_roll: 20,
            target_health_before: 3.0,
            ..MeleeResolutionInput {
                actor: "Player",
                target: "Rat 2007",
                raw_roll: 10,
                player: &admitted.player,
                enemy: &admitted.enemies[0],
                target_health_before: 3.0,
            }
        });
        assert!(hit.hit);
        assert_eq!(hit.damage_before_armor, 6.0);
        assert_eq!(hit.final_damage, 5.0);
        assert_eq!(hit.health_after, 0.0);
        assert!(hit.died);
    }
}
