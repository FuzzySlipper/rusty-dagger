use std::collections::{BTreeMap, BTreeSet};

use rusty_engine::{gameplay_resolution::Program, gameplay_rules::decode_rule_package};

use super::{
    AuthoredActorDefinition, AuthoredCmpOp, AuthoredEncounterDefinition, AuthoredExpr,
    AuthoredGameplayPayload, AuthoredInterceptor, AuthoredItemDefinition, AuthoredOperation,
    AuthoredPredicate, AuthoredProgram, AuthoredRuleDefinition, AuthoredSelector, AuthoredSubject,
    DaggerActionDefinition, DaggerActorDefinition, DaggerActorKind, DaggerBehaviorDefinition,
    DaggerCmpOp, DaggerDamageRange, DaggerDerivedRule, DaggerEncounterDefinition, DaggerExpr,
    DaggerGameplayCatalog, DaggerGameplayError, DaggerInterceptorKind, DaggerItemDefinition,
    DaggerOperation, DaggerPredicate, DaggerProgram, DaggerRuleDefinition, DaggerSelector,
    DaggerStatsSection, DaggerSubject, DaggerTrackDefinition, DaggerWeaponDefinition,
    DAGGER_GAMEPLAY_SCHEMA_VERSION, MAX_BEHAVIOR_VALUE, MAX_DAGGER_ACTIONS, MAX_DAGGER_ACTORS,
    MAX_DAGGER_DECLARED_IDS, MAX_DAGGER_DERIVED, MAX_DAGGER_ENCOUNTERS,
    MAX_DAGGER_ENCOUNTER_MEMBERS, MAX_DAGGER_EXPR_DEPTH, MAX_DAGGER_EXPR_NODES,
    MAX_DAGGER_ID_BYTES, MAX_DAGGER_ITEMS, MAX_DAGGER_PROGRAM_DEPTH, MAX_DAGGER_PROGRAM_NODES,
    MAX_DAGGER_RULES, MAX_DAGGER_TEXT_BYTES,
};

const MIN_TUNING_VALUE: f64 = 0.001;

/// Classic supports up to 5 sub-attacks per swing; no classic monster uses
/// more than 3.
const MAX_ATTACK_RANGES: usize = 5;

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

    let stats = compile_stats(&payload)?;
    let mechanics = super::mechanics::compile_mechanics_catalog(&stats)?;
    let items = compile_items(payload.items, &stats)?;
    let actions = compile_actions(payload.actions, &stats, &items)?;
    let actors = compile_actors(payload.actors, &stats, &actions, &items)?;
    let rules = compile_rules(payload.rules)?;
    let encounters = compile_encounters(payload.encounters)?;
    let derived = compile_derived(payload.derived, &stats, &items)?;
    Ok(DaggerGameplayCatalog::new(
        package.fingerprint().as_str().to_string(),
        stats,
        actors,
        actions,
        items,
        rules,
        encounters,
        derived,
        mechanics,
    ))
}

fn compile_derived(
    definitions: Vec<super::AuthoredDerivedRule>,
    stats: &DaggerStatsSection,
    items: &BTreeMap<String, DaggerItemDefinition>,
) -> Result<BTreeMap<String, DaggerDerivedRule>, DaggerGameplayError> {
    let mut derived = BTreeMap::new();
    for (index, rule) in definitions.into_iter().enumerate() {
        validate_id(&format!("payload.derived[{index}].id"), &rule.id)?;
        let mut nodes = 0_usize;
        let expr = compile_expr(rule.expr, &mut nodes, 0, stats, items)?;
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
                    if weapon.damage.min.0 < 0 || weapon.damage.min > weapon.damage.max {
                        return Err(DaggerGameplayError::InvalidValue {
                            path: format!("payload.items[{index}].weapon.damage"),
                            reason: format!(
                                "must satisfy 0 <= min <= max, got {}..{}",
                                weapon.damage.min.0, weapon.damage.max.0
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
                        damage_min: weapon.damage.min.0,
                        damage_max: weapon.damage.max.0,
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
                    require_positive(
                        format!("payload.items[{index}].interceptor.amount"),
                        amount.0,
                    )?;
                    Ok(DaggerInterceptorKind::ReduceDamage { amount: amount.0 })
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
                max: compile_expr(track.max, &mut nodes, 0, stats, items)?,
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
                    attacks: compile_actor_attacks(&path, actor.attacks)?,
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
        AuthoredExpr::Const { value } => Ok(DaggerExpr::Const { value: value.0 }),
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
            let (min, max) = (min.0, max.0);
            // Negative bounded evidence is legitimate (swing modifiers span
            // -10..+10); only ordering is enforced.
            if min > max {
                return Err(DaggerGameplayError::InvalidValue {
                    path: format!("expression.dice.{id}"),
                    reason: format!("must satisfy min <= max, got {min}..{max}"),
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
        AuthoredExpr::Track { subject, id } => {
            validate_declared("expression.track", &id, &stats.tracks)?;
            Ok(DaggerExpr::Track {
                subject: compile_subject(subject),
                id,
            })
        }
        AuthoredExpr::TrackMax { subject, id } => {
            validate_declared("expression.trackMax", &id, &stats.tracks)?;
            Ok(DaggerExpr::TrackMax {
                subject: compile_subject(subject),
                id,
            })
        }
        AuthoredExpr::PowMilli { base, exponent } => Ok(DaggerExpr::PowMilli {
            base: Box::new(compile_expr(*base, nodes, next_depth, stats, items)?),
            exponent: Box::new(compile_expr(*exponent, nodes, next_depth, stats, items)?),
        }),
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
