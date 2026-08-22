//! Dagger's closed product-leaf codec for Engine's generated composedExact grammar.
//!
//! Generic expression nodes are decoded and quota-checked by gameplay-standard.
//! This module owns only Dagger's three dynamic leaf meanings and the explicitly named
//! input each leaf contributes at evaluation time.

use std::collections::BTreeSet;

use rusty_engine::gameplay_standard::{
    CapabilityRequirementId, CapabilityRoleId, CompiledComposedExactLeaf, ComposedExactLeafCodec,
    ComposedExactLeafKindId, ExactExpr, ExactExprRequirements, ExactInputReference, InputId,
    StandardExtensionSchema,
};
use serde_json::{json, Map, Value};

use super::{DaggerExactLeaf, DaggerGameplayError, DaggerSubject};

pub(super) struct DaggerExactLeafCodec;

impl ComposedExactLeafCodec for DaggerExactLeafCodec {
    type Leaf = DaggerExactLeaf;
    type Error = DaggerGameplayError;

    fn schema() -> StandardExtensionSchema {
        StandardExtensionSchema::new(
            CapabilityRequirementId::parse("dagger.exact").expect("static id"),
            1,
        )
        .expect("static schema")
    }

    fn decode_leaf(
        kind: &ComposedExactLeafKindId,
        payload: &Value,
    ) -> Result<Self::Leaf, Self::Error> {
        let wrapper = object(payload, "payload")?;
        exact_fields(wrapper, &["kind", "value"], "payload")?;
        let payload_kind = required_string(wrapper, "kind", "payload")?;
        if payload_kind != kind.as_str() {
            return Err(reject(
                "payload.kind",
                "must match the enclosing product kind",
            ));
        }
        let value = object(required(wrapper, "value", "payload")?, "payload.value")?;
        match kind.as_str() {
            "equipped-weapon-skill" => {
                exact_fields(value, &["subject"], "payload.value")?;
                Ok(DaggerExactLeaf::EquippedWeaponSkill {
                    subject: subject(required_string(value, "subject", "payload.value")?)?,
                })
            }
            "equipped-weapon-dice" => {
                exact_fields(value, &["id", "subject"], "payload.value")?;
                Ok(DaggerExactLeaf::EquippedWeaponDice {
                    subject: subject(required_string(value, "subject", "payload.value")?)?,
                    id: input_id(
                        required_string(value, "id", "payload.value")?,
                        "payload.value.id",
                    )?,
                })
            }
            "struck-armor" => {
                exact_fields(value, &["id", "subject"], "payload.value")?;
                Ok(DaggerExactLeaf::StruckArmor {
                    subject: subject(required_string(value, "subject", "payload.value")?)?,
                    id: input_id(
                        required_string(value, "id", "payload.value")?,
                        "payload.value.id",
                    )?,
                })
            }
            _ => Err(reject("payload.kind", "unsupported Dagger product leaf")),
        }
    }

    fn encode_leaf(
        kind: &ComposedExactLeafKindId,
        leaf: &Self::Leaf,
    ) -> Result<Value, Self::Error> {
        let value = match (kind.as_str(), leaf) {
            ("equipped-weapon-skill", DaggerExactLeaf::EquippedWeaponSkill { subject }) => {
                json!({"subject": subject_name(*subject)})
            }
            ("equipped-weapon-dice", DaggerExactLeaf::EquippedWeaponDice { subject, id }) => {
                json!({"subject": subject_name(*subject), "id": id})
            }
            ("struck-armor", DaggerExactLeaf::StruckArmor { subject, id }) => {
                json!({"subject": subject_name(*subject), "id": id})
            }
            _ => {
                return Err(reject(
                    "payload.kind",
                    "leaf does not match its product kind",
                ))
            }
        };
        Ok(json!({"kind": kind.as_str(), "value": value}))
    }

    fn compile_leaf(leaf: &Self::Leaf) -> Result<CompiledComposedExactLeaf, Self::Error> {
        let input = leaf_input(leaf)?;
        let expression = ExactExpr::Input(input);
        let requirements = ExactExprRequirements::inspect(&expression)
            .map_err(|error| reject("product", &format!("standard input rejected: {error:?}")))?;
        Ok(CompiledComposedExactLeaf::new(
            expression,
            requirements,
            Vec::new(),
        ))
    }
}

pub(super) fn leaf_input(
    leaf: &DaggerExactLeaf,
) -> Result<ExactInputReference, DaggerGameplayError> {
    match leaf {
        DaggerExactLeaf::EquippedWeaponSkill { subject } => Ok(ExactInputReference::Fact {
            role: role(subject_name(*subject))?,
            id: input("equipped-weapon-skill")?,
        }),
        DaggerExactLeaf::EquippedWeaponDice { subject, id }
        | DaggerExactLeaf::StruckArmor { subject, id } => Ok(ExactInputReference::Roll {
            role: role(subject_name(*subject))?,
            id: input(id)?,
        }),
    }
}

fn subject(value: &str) -> Result<DaggerSubject, DaggerGameplayError> {
    match value {
        "actor" => Ok(DaggerSubject::Actor),
        "target" => Ok(DaggerSubject::Target),
        _ => Err(reject("payload.value.subject", "must be actor or target")),
    }
}

fn subject_name(value: DaggerSubject) -> &'static str {
    match value {
        DaggerSubject::Actor => "actor",
        DaggerSubject::Target => "target",
    }
}

fn role(value: &str) -> Result<CapabilityRoleId, DaggerGameplayError> {
    CapabilityRoleId::parse(value)
        .map_err(|error| reject("product.role", &format!("invalid role: {error:?}")))
}

fn input(value: &str) -> Result<InputId, DaggerGameplayError> {
    InputId::parse(value)
        .map_err(|error| reject("product.input", &format!("invalid input id: {error:?}")))
}

fn input_id(value: &str, path: &str) -> Result<String, DaggerGameplayError> {
    input(value)
        .map(|_| value.to_owned())
        .map_err(|_| reject(path, "must use the documented Dagger id grammar"))
}

fn object<'a>(value: &'a Value, path: &str) -> Result<&'a Map<String, Value>, DaggerGameplayError> {
    value
        .as_object()
        .ok_or_else(|| reject(path, "must be an object"))
}

fn required<'a>(
    object: &'a Map<String, Value>,
    field: &str,
    path: &str,
) -> Result<&'a Value, DaggerGameplayError> {
    object
        .get(field)
        .ok_or_else(|| reject(&format!("{path}.{field}"), "is required"))
}

fn required_string<'a>(
    object: &'a Map<String, Value>,
    field: &str,
    path: &str,
) -> Result<&'a str, DaggerGameplayError> {
    required(object, field, path)?
        .as_str()
        .ok_or_else(|| reject(&format!("{path}.{field}"), "must be a string"))
}

fn exact_fields(
    object: &Map<String, Value>,
    fields: &[&str],
    path: &str,
) -> Result<(), DaggerGameplayError> {
    let actual = object.keys().map(String::as_str).collect::<BTreeSet<_>>();
    let expected = fields.iter().copied().collect::<BTreeSet<_>>();
    if actual == expected {
        Ok(())
    } else {
        Err(reject(path, "has unknown or missing fields"))
    }
}

fn reject(path: &str, reason: &str) -> DaggerGameplayError {
    DaggerGameplayError::InvalidValue {
        path: path.to_owned(),
        reason: reason.to_owned(),
    }
}

#[cfg(test)]
mod tests {
    use super::leaf_input;
    use crate::resolution::{DaggerExactLeaf, DaggerSubject};
    use rusty_engine::gameplay_standard::ExactInputReference;

    #[test]
    fn leaf_requirements_name_the_runtime_subject_and_evidence_input() {
        let target_weapon_roll = leaf_input(&DaggerExactLeaf::EquippedWeaponDice {
            subject: DaggerSubject::Target,
            id: "target-weapon".to_string(),
        })
        .expect("target weapon requirement");
        assert!(matches!(
            target_weapon_roll,
            ExactInputReference::Roll { ref role, ref id }
                if role.as_str() == "target" && id.as_str() == "target-weapon"
        ));

        let target_armor_roll = leaf_input(&DaggerExactLeaf::StruckArmor {
            subject: DaggerSubject::Target,
            id: "struck-body-part".to_string(),
        })
        .expect("target armor requirement");
        assert!(matches!(
            target_armor_roll,
            ExactInputReference::Roll { ref role, ref id }
                if role.as_str() == "target" && id.as_str() == "struck-body-part"
        ));
    }
}
