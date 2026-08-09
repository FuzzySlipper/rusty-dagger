//! Host-neutral gameplay semantics for interactive Dagger experiments.
//!
//! TypeScript and JSON author a small immutable document. This crate is the
//! only authority that admits that document, calculates derived values, and
//! produces the calculation steps shown by designer tools.

#![forbid(unsafe_code)]

use serde::{Deserialize, Serialize};

pub const EXPERIMENT_SCHEMA_VERSION: u32 = 1;
pub const MIN_MOVE_SPEED_UNITS_PER_SECOND: f32 = 0.1;
pub const MAX_MOVE_SPEED_UNITS_PER_SECOND: f32 = 50.0;
pub const MAX_VITALITY_INPUT: f32 = 10_000.0;

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct ExperimentDocument {
    pub schema_version: u32,
    pub player: PlayerExperiment,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct PlayerExperiment {
    pub movement: PlayerMovementExperiment,
    pub vitality: PlayerVitalityExperiment,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct PlayerMovementExperiment {
    pub speed_units_per_second: f32,
}

/// Named inputs for the deliberately small first gameplay formula.
///
/// Rust owns the formula `base_health + endurance * health_per_endurance`.
/// The authoring document exposes its useful design knobs without defining a
/// general expression language or allowing TypeScript to evaluate gameplay.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct PlayerVitalityExperiment {
    pub base_health: f32,
    pub endurance: f32,
    pub health_per_endurance: f32,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct AdmittedExperiment {
    pub document: ExperimentDocument,
    pub player: AdmittedPlayerValues,
    pub calculation: CalculationRecord,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct AdmittedPlayerValues {
    pub move_speed_units_per_second: f32,
    pub max_health: f32,
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
    InvalidValue { path: &'static str, reason: String },
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
        finite_in_range(
            "player.vitality.baseHealth",
            self.player.vitality.base_health,
            0.0,
            MAX_VITALITY_INPUT,
        )?;
        finite_in_range(
            "player.vitality.endurance",
            self.player.vitality.endurance,
            0.0,
            MAX_VITALITY_INPUT,
        )?;
        finite_in_range(
            "player.vitality.healthPerEndurance",
            self.player.vitality.health_per_endurance,
            0.0,
            MAX_VITALITY_INPUT,
        )?;

        let vitality = &self.player.vitality;
        let endurance_health = vitality.endurance * vitality.health_per_endurance;
        let max_health = vitality.base_health + endurance_health;
        if !max_health.is_finite() || max_health > MAX_VITALITY_INPUT {
            return Err(ExperimentError::InvalidValue {
                path: "player.vitality",
                reason: format!(
                    "derived maxHealth must be finite and no greater than {MAX_VITALITY_INPUT}"
                ),
            });
        }
        let calculation = CalculationRecord {
            rule: "player.maxHealth".to_string(),
            expression: "baseHealth + endurance * healthPerEndurance".to_string(),
            inputs: vec![
                CalculationInput {
                    name: "baseHealth".to_string(),
                    value: vitality.base_health,
                },
                CalculationInput {
                    name: "endurance".to_string(),
                    value: vitality.endurance,
                },
                CalculationInput {
                    name: "healthPerEndurance".to_string(),
                    value: vitality.health_per_endurance,
                },
            ],
            steps: vec![
                CalculationStep {
                    operation: "multiply".to_string(),
                    left: vitality.endurance,
                    right: vitality.health_per_endurance,
                    result: endurance_health,
                },
                CalculationStep {
                    operation: "add".to_string(),
                    left: vitality.base_health,
                    right: endurance_health,
                    result: max_health,
                },
            ],
            result: max_health,
        };
        Ok(AdmittedExperiment {
            player: AdmittedPlayerValues {
                move_speed_units_per_second: self.player.movement.speed_units_per_second,
                max_health,
            },
            document: self,
            calculation,
        })
    }
}

pub fn admit_json(document: &str) -> Result<AdmittedExperiment, ExperimentError> {
    ExperimentDocument::from_json(document)?.admit()
}

fn finite_in_range(
    path: &'static str,
    value: f32,
    minimum: f32,
    maximum: f32,
) -> Result<(), ExperimentError> {
    if value.is_finite() && (minimum..=maximum).contains(&value) {
        Ok(())
    } else {
        Err(ExperimentError::InvalidValue {
            path,
            reason: format!("must be finite and between {minimum} and {maximum}"),
        })
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn starter() -> ExperimentDocument {
        ExperimentDocument {
            schema_version: EXPERIMENT_SCHEMA_VERSION,
            player: PlayerExperiment {
                movement: PlayerMovementExperiment {
                    speed_units_per_second: 3.5,
                },
                vitality: PlayerVitalityExperiment {
                    base_health: 25.0,
                    endurance: 40.0,
                    health_per_endurance: 1.5,
                },
            },
        }
    }

    #[test]
    fn admits_named_inputs_and_explains_the_rust_owned_formula() {
        let admitted = starter().admit().expect("admit starter experiment");
        assert_eq!(admitted.player.move_speed_units_per_second, 3.5);
        assert_eq!(admitted.player.max_health, 85.0);
        assert_eq!(admitted.calculation.steps.len(), 2);
        assert_eq!(admitted.calculation.steps[0].operation, "multiply");
        assert_eq!(admitted.calculation.steps[1].result, 85.0);
    }

    #[test]
    fn rejects_unknown_fields_and_invalid_values() {
        let json = r#"{
            "schemaVersion": 1,
            "player": {
                "movement": { "speedUnitsPerSecond": 3.5, "hidden": true },
                "vitality": { "baseHealth": 25, "endurance": 40, "healthPerEndurance": 1.5 }
            }
        }"#;
        assert!(matches!(admit_json(json), Err(ExperimentError::Json(_))));

        let mut document = starter();
        document.player.movement.speed_units_per_second = 0.0;
        assert!(matches!(
            document.admit(),
            Err(ExperimentError::InvalidValue {
                path: "player.movement.speedUnitsPerSecond",
                ..
            })
        ));
    }

    #[test]
    fn rejects_a_derived_value_outside_the_bounded_gameplay_range() {
        let mut document = starter();
        document.player.vitality.base_health = MAX_VITALITY_INPUT;
        document.player.vitality.endurance = 1.0;
        assert!(matches!(
            document.admit(),
            Err(ExperimentError::InvalidValue {
                path: "player.vitality",
                ..
            })
        ));
    }
}
