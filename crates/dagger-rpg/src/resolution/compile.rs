use std::collections::{BTreeMap, BTreeSet};

use rusty_engine::{gameplay_resolution::Program, gameplay_rules::decode_rule_package};

use super::{
    AuthoredActorDefinition, AuthoredCmpOp, AuthoredEncounterDefinition, AuthoredExpr,
    AuthoredGameplayPayload, AuthoredInterceptor, AuthoredItemDefinition, AuthoredOperation,
    AuthoredPredicate, AuthoredProgram, AuthoredRuleDefinition, AuthoredSelector, AuthoredSubject,
    DaggerActionDefinition, DaggerActorDefinition, DaggerActorKind, DaggerBehaviorDefinition,
    DaggerCmpOp, DaggerEncounterDefinition, DaggerExpr, DaggerGameplayCatalog, DaggerGameplayError,
    DaggerInterceptorKind, DaggerItemDefinition, DaggerOperation, DaggerPredicate, DaggerProgram,
    DaggerRuleDefinition, DaggerSelector, DaggerStatsSection, DaggerSubject, DaggerTrackDefinition,
    DaggerWeaponDefinition, DAGGER_GAMEPLAY_SCHEMA_VERSION, MAX_BEHAVIOR_VALUE, MAX_DAGGER_ACTIONS,
    MAX_DAGGER_ACTORS, MAX_DAGGER_DECLARED_IDS, MAX_DAGGER_ENCOUNTERS,
    MAX_DAGGER_ENCOUNTER_MEMBERS, MAX_DAGGER_EXPR_DEPTH, MAX_DAGGER_EXPR_NODES,
    MAX_DAGGER_ID_BYTES, MAX_DAGGER_ITEMS, MAX_DAGGER_PROGRAM_DEPTH, MAX_DAGGER_PROGRAM_NODES,
    MAX_DAGGER_RULES, MAX_DAGGER_TEXT_BYTES,
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
    enforce_quota("actors", payload.actors.len(), MAX_DAGGER_ACTORS)?;
    enforce_quota(
        "encounters",
        payload.encounters.len(),
        MAX_DAGGER_ENCOUNTERS,
    )?;

    let stats = compile_stats(&payload)?;
    let items = compile_items(payload.items, &stats)?;
    let actions = compile_actions(payload.actions, &stats, &items)?;
    let actors = compile_actors(payload.actors, &stats, &actions, &items)?;
    let rules = compile_rules(payload.rules)?;
    let encounters = compile_encounters(payload.encounters)?;
    Ok(DaggerGameplayCatalog::new(
        package.fingerprint().as_str().to_string(),
        stats,
        actors,
        actions,
        items,
        rules,
        encounters,
    ))
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
    })
}

fn compile_items(
    definitions: Vec<AuthoredItemDefinition>,
    stats: &DaggerStatsSection,
) -> Result<BTreeMap<String, DaggerItemDefinition>, DaggerGameplayError> {
    let mut items = BTreeMap::new();
    for (index, item) in definitions.into_iter().enumerate() {
        validate_id(&format!("payload.items[{index}].id"), &item.id)?;
        let weapon = item
            .weapon
            .map(
                |weapon| -> Result<DaggerWeaponDefinition, DaggerGameplayError> {
                    if weapon.damage.min < 0 || weapon.damage.min > weapon.damage.max {
                        return Err(DaggerGameplayError::InvalidValue {
                            path: format!("payload.items[{index}].weapon.damage"),
                            reason: format!(
                                "must satisfy 0 <= min <= max, got {}..{}",
                                weapon.damage.min, weapon.damage.max
                            ),
                        });
                    }
                    validate_id(
                        &format!("payload.items[{index}].weapon.material"),
                        &weapon.material,
                    )?;
                    validate_declared(
                        &format!("payload.items[{index}].weapon.skill"),
                        &weapon.skill,
                        &stats.skills,
                    )?;
                    Ok(DaggerWeaponDefinition {
                        damage_min: weapon.damage.min,
                        damage_max: weapon.damage.max,
                        material: weapon.material,
                        skill: weapon.skill,
                    })
                },
            )
            .transpose()?;
        let interceptor = item
            .interceptor
            .map(|interceptor| match interceptor {
                AuthoredInterceptor::ReduceDamage { amount } => {
                    require_positive(format!("payload.items[{index}].interceptor.amount"), amount)?;
                    Ok(DaggerInterceptorKind::ReduceDamage { amount })
                }
            })
            .transpose()?;
        let id = item.id;
        if items
            .insert(
                id.clone(),
                DaggerItemDefinition {
                    id: id.clone(),
                    weapon,
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

fn compile_actions(
    definitions: Vec<super::AuthoredActionDefinition>,
    stats: &DaggerStatsSection,
    items: &BTreeMap<String, DaggerItemDefinition>,
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
        let program = compile_program(action.program, &mut nodes, 0, stats, items)?;
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

fn compile_actors(
    definitions: Vec<AuthoredActorDefinition>,
    stats: &DaggerStatsSection,
    actions: &BTreeMap<String, DaggerActionDefinition>,
    items: &BTreeMap<String, DaggerItemDefinition>,
) -> Result<BTreeMap<String, DaggerActorDefinition>, DaggerGameplayError> {
    let mut actors = BTreeMap::new();
    let mut mobile_ids = BTreeSet::new();
    for (index, actor) in definitions.into_iter().enumerate() {
        let path = format!("payload.actors[{index}]");
        validate_id(&format!("{path}.id"), &actor.id)?;
        let kind = match actor.kind {
            super::AuthoredActorKind::Player => DaggerActorKind::Player,
            super::AuthoredActorKind::Monster => DaggerActorKind::Monster,
        };
        if let Some(mobile_id) = actor.mobile_id {
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
            actor_stats.insert(id, value);
        }
        let mut actor_skills = BTreeMap::new();
        for (id, value) in actor.skills {
            validate_declared(&format!("{path}.skills"), &id, &stats.skills)?;
            actor_skills.insert(id, value);
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
                max: compile_expr(track.max, &mut nodes, 0, stats, items)?,
            });
        }
        let behavior = actor
            .behavior
            .map(
                |behavior| -> Result<DaggerBehaviorDefinition, DaggerGameplayError> {
                    let milli_max = i64::from(MAX_BEHAVIOR_VALUE as u32) * 1000;
                    for (field, value, minimum) in [
                        ("detectionRangeMilli", behavior.detection_range_milli, 1_i64),
                        ("patrolSpeedMilli", behavior.patrol_speed_milli, 0_i64),
                        ("chaseSpeedMilli", behavior.chase_speed_milli, 0_i64),
                        ("attackRangeMilli", behavior.attack_range_milli, 1_i64),
                        (
                            "attackCooldownMillis",
                            behavior.attack_cooldown_millis,
                            1_i64,
                        ),
                    ] {
                        if value < minimum || value > milli_max {
                            return Err(DaggerGameplayError::InvalidValue {
                                path: format!("{path}.behavior.{field}"),
                                reason: format!("must be between {minimum} and {milli_max}"),
                            });
                        }
                    }
                    validate_id(&format!("{path}.behavior.action"), &behavior.action)?;
                    if !actions.contains_key(&behavior.action) {
                        return Err(DaggerGameplayError::InvalidValue {
                            path: format!("{path}.behavior.action"),
                            reason: format!("unknown action {}", behavior.action),
                        });
                    }
                    let from_milli = |value: i64| value as f32 / 1000.0;
                    Ok(DaggerBehaviorDefinition {
                        detection_range: from_milli(behavior.detection_range_milli),
                        patrol_speed: from_milli(behavior.patrol_speed_milli),
                        chase_speed: from_milli(behavior.chase_speed_milli),
                        attack_range: from_milli(behavior.attack_range_milli),
                        attack_cooldown_seconds: from_milli(behavior.attack_cooldown_millis),
                        action: behavior.action,
                    })
                },
            )
            .transpose()?;
        let id = actor.id;
        if actors
            .insert(
                id.clone(),
                DaggerActorDefinition {
                    id: id.clone(),
                    kind,
                    mobile_id: actor.mobile_id,
                    stats: actor_stats,
                    skills: actor_skills,
                    armor_value: actor.armor_value,
                    tracks,
                    behavior,
                },
            )
            .is_some()
        {
            return Err(DaggerGameplayError::DuplicateId { kind: "actor", id });
        }
    }
    Ok(actors)
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
                    member_entity_ids: encounter.member_entity_ids,
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
    items: &BTreeMap<String, DaggerItemDefinition>,
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
                .map(|step| compile_program(step, nodes, next_depth, stats, items))
                .collect::<Result<_, _>>()?,
        }),
        AuthoredProgram::When {
            predicate,
            then_program,
            otherwise_program,
        } => Ok(Program::When {
            predicate: compile_predicate(predicate, stats, items)?,
            then_program: Box::new(compile_program(
                *then_program,
                nodes,
                next_depth,
                stats,
                items,
            )?),
            otherwise_program: otherwise_program
                .map(|value| compile_program(*value, nodes, next_depth, stats, items).map(Box::new))
                .transpose()?,
        }),
        AuthoredProgram::Operation { operation } => Ok(Program::Operation(compile_operation(
            operation, stats, items,
        )?)),
    }
}

fn compile_predicate(
    value: AuthoredPredicate,
    stats: &DaggerStatsSection,
    items: &BTreeMap<String, DaggerItemDefinition>,
) -> Result<DaggerPredicate, DaggerGameplayError> {
    match value {
        AuthoredPredicate::Cmp { op, left, right } => {
            let mut nodes = 0_usize;
            Ok(DaggerPredicate::Cmp {
                op: compile_cmp_op(op),
                left: compile_expr(left, &mut nodes, 0, stats, items)?,
                right: compile_expr(right, &mut nodes, 0, stats, items)?,
            })
        }
    }
}

fn compile_cmp_op(op: AuthoredCmpOp) -> DaggerCmpOp {
    match op {
        AuthoredCmpOp::Lt => DaggerCmpOp::Lt,
        AuthoredCmpOp::Lte => DaggerCmpOp::Lte,
        AuthoredCmpOp::Eq => DaggerCmpOp::Eq,
        AuthoredCmpOp::Gte => DaggerCmpOp::Gte,
        AuthoredCmpOp::Gt => DaggerCmpOp::Gt,
    }
}

fn compile_selector(value: AuthoredSelector) -> DaggerSelector {
    match value {
        AuthoredSelector::IntentTarget => DaggerSelector::IntentTarget,
    }
}

fn compile_subject(value: AuthoredSubject) -> DaggerSubject {
    match value {
        AuthoredSubject::Actor => DaggerSubject::Actor,
        AuthoredSubject::Target => DaggerSubject::Target,
    }
}

fn compile_operation(
    value: AuthoredOperation,
    stats: &DaggerStatsSection,
    items: &BTreeMap<String, DaggerItemDefinition>,
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
                amount: compile_expr(amount, &mut nodes, 0, stats, items)?,
            })
        }
        AuthoredOperation::Damage { target, amount } => {
            let mut nodes = 0_usize;
            Ok(DaggerOperation::Damage {
                target: compile_selector(target),
                amount: compile_expr(amount, &mut nodes, 0, stats, items)?,
            })
        }
    }
}

fn compile_expr(
    expr: AuthoredExpr,
    nodes: &mut usize,
    depth: u16,
    stats: &DaggerStatsSection,
    items: &BTreeMap<String, DaggerItemDefinition>,
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
        AuthoredExpr::Const { value } => Ok(DaggerExpr::Const { value }),
        AuthoredExpr::Stat { subject, id } => {
            validate_declared("expression.stat", &id, &stats.attributes)?;
            Ok(DaggerExpr::Stat {
                subject: compile_subject(subject),
                id,
            })
        }
        AuthoredExpr::Skill { subject, id } => {
            validate_declared("expression.skill", &id, &stats.skills)?;
            Ok(DaggerExpr::Skill {
                subject: compile_subject(subject),
                id,
            })
        }
        AuthoredExpr::Armor { subject } => Ok(DaggerExpr::Armor {
            subject: compile_subject(subject),
        }),
        AuthoredExpr::Evidence { id } => {
            validate_id("expression.evidence", &id)?;
            Ok(DaggerExpr::Evidence { id })
        }
        AuthoredExpr::Dice { id, min, max } => {
            validate_id("expression.dice", &id)?;
            if min < 0 || min > max {
                return Err(DaggerGameplayError::InvalidValue {
                    path: format!("expression.dice.{id}"),
                    reason: format!("must satisfy 0 <= min <= max, got {min}..{max}"),
                });
            }
            Ok(DaggerExpr::Dice { id, min, max })
        }
        AuthoredExpr::WeaponDice { item } => {
            validate_id("expression.weaponDice", &item)?;
            match items.get(&item) {
                Some(definition) if definition.weapon.is_some() => {
                    Ok(DaggerExpr::WeaponDice { item })
                }
                _ => Err(DaggerGameplayError::InvalidValue {
                    path: "expression.weaponDice".to_string(),
                    reason: format!("{item} is not a declared weapon item"),
                }),
            }
        }
        AuthoredExpr::Add { terms } => Ok(DaggerExpr::Add {
            terms: compile_expr_terms(terms, nodes, next_depth, stats, items)?,
        }),
        AuthoredExpr::Sub { left, right } => Ok(DaggerExpr::Sub {
            left: Box::new(compile_expr(*left, nodes, next_depth, stats, items)?),
            right: Box::new(compile_expr(*right, nodes, next_depth, stats, items)?),
        }),
        AuthoredExpr::Mul { terms } => Ok(DaggerExpr::Mul {
            terms: compile_expr_terms(terms, nodes, next_depth, stats, items)?,
        }),
        AuthoredExpr::DivFloor { left, right } => Ok(DaggerExpr::DivFloor {
            left: Box::new(compile_expr(*left, nodes, next_depth, stats, items)?),
            right: Box::new(compile_expr(*right, nodes, next_depth, stats, items)?),
        }),
        AuthoredExpr::Min { terms } => Ok(DaggerExpr::Min {
            terms: compile_expr_terms(terms, nodes, next_depth, stats, items)?,
        }),
        AuthoredExpr::Max { terms } => Ok(DaggerExpr::Max {
            terms: compile_expr_terms(terms, nodes, next_depth, stats, items)?,
        }),
    }
}

fn compile_expr_terms(
    terms: Vec<AuthoredExpr>,
    nodes: &mut usize,
    depth: u16,
    stats: &DaggerStatsSection,
    items: &BTreeMap<String, DaggerItemDefinition>,
) -> Result<Vec<DaggerExpr>, DaggerGameplayError> {
    if terms.is_empty() {
        return Err(DaggerGameplayError::InvalidValue {
            path: "expression.terms".to_string(),
            reason: "must contain at least one term".to_string(),
        });
    }
    terms
        .into_iter()
        .map(|term| compile_expr(term, nodes, depth, stats, items))
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
        Err(DaggerGameplayError::Quota {
            field,
            actual,
            maximum,
        })
    } else {
        Ok(())
    }
}
