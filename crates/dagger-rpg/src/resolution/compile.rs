use std::collections::{BTreeMap, BTreeSet};

use rusty_engine::{gameplay_resolution::Program, gameplay_rules::decode_rule_package};

use super::{
    AuthoredActionDefinition, AuthoredGameplayPayload, AuthoredInterceptor, AuthoredOperation,
    AuthoredPredicate, AuthoredProgram, AuthoredRuleDefinition, AuthoredSelector,
    DaggerActionDefinition, DaggerGameplayCatalog, DaggerGameplayError, DaggerInterceptorKind,
    DaggerItemDefinition, DaggerOperation, DaggerPredicate, DaggerProgram, DaggerRuleDefinition,
    DaggerSelector, DAGGER_GAMEPLAY_SCHEMA_VERSION, MAX_DAGGER_ACTIONS, MAX_DAGGER_ID_BYTES,
    MAX_DAGGER_ITEMS, MAX_DAGGER_PROGRAM_DEPTH, MAX_DAGGER_PROGRAM_NODES, MAX_DAGGER_RULES,
};

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
    if payload.schema_version != DAGGER_GAMEPLAY_SCHEMA_VERSION {
        return Err(DaggerGameplayError::UnsupportedSchema {
            actual: payload.schema_version,
            expected: DAGGER_GAMEPLAY_SCHEMA_VERSION,
        });
    }
    enforce_quota("actions", payload.actions.len(), MAX_DAGGER_ACTIONS)?;
    enforce_quota("items", payload.items.len(), MAX_DAGGER_ITEMS)?;
    enforce_quota("rules", payload.rules.len(), MAX_DAGGER_RULES)?;

    let actions = compile_actions(payload.actions)?;
    let items = compile_items(payload.items)?;
    let rules = compile_rules(payload.rules)?;
    Ok(DaggerGameplayCatalog::new(
        package.fingerprint().as_str().to_string(),
        actions,
        items,
        rules,
    ))
}

fn compile_actions(
    definitions: Vec<AuthoredActionDefinition>,
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
        let program = compile_program(action.program, &mut nodes, 0)?;
        let id = action.id;
        if actions
            .insert(
                id.clone(),
                DaggerActionDefinition {
                    id: id.clone(),
                    tags,
                    program,
                },
            )
            .is_some()
        {
            return Err(DaggerGameplayError::DuplicateId { kind: "action", id });
        }
    }
    Ok(actions)
}

fn compile_items(
    definitions: Vec<super::AuthoredItemDefinition>,
) -> Result<BTreeMap<String, DaggerItemDefinition>, DaggerGameplayError> {
    let mut items = BTreeMap::new();
    for (index, item) in definitions.into_iter().enumerate() {
        validate_id(&format!("payload.items[{index}].id"), &item.id)?;
        let interceptor = match item.interceptor {
            AuthoredInterceptor::ReduceDamage { amount } => {
                require_positive(format!("payload.items[{index}].interceptor.amount"), amount)?;
                DaggerInterceptorKind::ReduceDamage { amount }
            }
        };
        let id = item.id;
        if items
            .insert(
                id.clone(),
                DaggerItemDefinition {
                    id: id.clone(),
                    interceptor,
                },
            )
            .is_some()
        {
            return Err(DaggerGameplayError::DuplicateId { kind: "item", id });
        }
    }
    Ok(items)
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

fn compile_program(
    program: AuthoredProgram,
    nodes: &mut usize,
    depth: u16,
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
                .map(|step| compile_program(step, nodes, next_depth))
                .collect::<Result<_, _>>()?,
        }),
        AuthoredProgram::When {
            predicate,
            then_program,
            otherwise_program,
        } => Ok(Program::When {
            predicate: compile_predicate(predicate)?,
            then_program: Box::new(compile_program(*then_program, nodes, next_depth)?),
            otherwise_program: otherwise_program
                .map(|value| compile_program(*value, nodes, next_depth).map(Box::new))
                .transpose()?,
        }),
        AuthoredProgram::Operation { operation } => {
            Ok(Program::Operation(compile_operation(operation)?))
        }
    }
}

fn compile_predicate(value: AuthoredPredicate) -> Result<DaggerPredicate, DaggerGameplayError> {
    match value {
        AuthoredPredicate::EvidenceAtLeast { evidence, minimum } => {
            validate_id("payload.actions[].program.predicate.evidence", &evidence)?;
            Ok(DaggerPredicate::EvidenceAtLeast { evidence, minimum })
        }
    }
}

fn compile_selector(value: AuthoredSelector) -> DaggerSelector {
    match value {
        AuthoredSelector::IntentTarget => DaggerSelector::IntentTarget,
    }
}

fn compile_operation(value: AuthoredOperation) -> Result<DaggerOperation, DaggerGameplayError> {
    match value {
        AuthoredOperation::SpendMagicka { amount } => {
            require_positive("payload.actions[].program.operation.amount", amount)?;
            Ok(DaggerOperation::SpendMagicka { amount })
        }
        AuthoredOperation::Damage { target, amount } => {
            require_positive("payload.actions[].program.operation.amount", amount)?;
            Ok(DaggerOperation::Damage {
                target: compile_selector(target),
                amount,
            })
        }
    }
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

fn require_positive(path: impl Into<String>, value: i64) -> Result<(), DaggerGameplayError> {
    if value > 0 {
        Ok(())
    } else {
        Err(DaggerGameplayError::InvalidValue {
            path: path.into(),
            reason: "must be positive".to_string(),
        })
    }
}

fn enforce_quota(
    field: &'static str,
    actual: usize,
    maximum: usize,
) -> Result<(), DaggerGameplayError> {
    if actual > maximum {
        return Err(DaggerGameplayError::Quota {
            field,
            actual,
            maximum,
        });
    }
    Ok(())
}
