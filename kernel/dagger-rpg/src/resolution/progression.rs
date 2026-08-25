//! The kill-XP progression authority. Classic skill-use formulas remain in
//! the derived catalog but are not evaluated by this profile.
//!
//! Progression stats (`xp`, `level`) are declared in the stats section's
//! `progression` category and compile to wide-range mechanics stats
//! (0..=1_000_000 — xp accumulates past the classic attribute range). The
//! spawn authority attaches them to player-kind actors only (xp 0, level 1);
//! monsters never carry them — their classic `level` is definition data,
//! unrelated.
//!
//! A kill awards the victim definition's `xpReward` to a player-kind killer:
//! xp accumulates on the killer's `xp` stat base, the derived `xp-level`
//! rule (the pacing curve, catalog-authored) maps the live xp total to
//! thresholds crossed, and each level gained rolls the classic
//! `hit-points-per-level-up` rule (donor
//! `FormulaHelper.CalculateHitPointsPerLevelUp`: a roll in
//! [hitPointsPerLevel/2, hitPointsPerLevel] plus the endurance modifier,
//! minimum 1) and applies it to the `health-max` stat base AND to current
//! health (clamped to the new maximum through the track service — the donor
//! raises only the maximum; adding current health is a current profile
//! choice). Only player kills award; AI kills don't.
//!
//! The derived rules are evaluated against LIVE component stats (policy
//! style), never definition bases: `xp-level` reads live xp and only exists
//! in these live-state contexts. Stat-base mutations go through the Engine's
//! `StatService::set_base` so catalog bounds and track reconciliation stay
//! upstream-owned.

use std::collections::BTreeMap;

use rusty_engine::gameplay_mechanics::{
    MechanicsScalar, OperationId, SourceInstanceId, SourceInstanceIdentity,
    StatBaseMutationRequest, StatId, StatService, StatsComponent,
};

use super::eval::{evaluate_expr, set_actor_track, ActorExprValues, ExprContext};
use super::mechanics::track_max_stat_id;
use super::{
    armor_part_stat_id, DaggerActorDefinition, DaggerActorKind, DaggerEvidence,
    DaggerGameplayCatalog, DaggerGameplayError, DaggerGameplayState, DaggerLevelUpOutcome,
    DaggerProgressionRecord,
};

/// The progression stat ids the spawn authority and this module understand.
/// Declaring any other progression stat fails closed at spawn — there is no
/// honest spawn base for an unknown counter.
pub const XP_STAT_ID: &str = "xp";
pub const LEVEL_STAT_ID: &str = "level";

/// The derived rule mapping live xp to thresholds crossed. The live level is
/// the spawn base (1) plus that count.
pub const XP_LEVEL_RULE_ID: &str = "xp-level";

/// The derived rule for one level-up's health gain (donor
/// `CalculateHitPointsPerLevelUp` shape).
pub const HP_PER_LEVEL_RULE_ID: &str = "hit-points-per-level-up";

/// The evidence id the `hit-points-per-level-up` rule reads.
pub const HP_ROLL_EVIDENCE_ID: &str = "hp-level-up-roll";

/// Player spawn bases for the progression stats.
pub const PLAYER_SPAWN_XP: i64 = 0;
pub const PLAYER_SPAWN_LEVEL: i64 = 1;

/// The spawn base for one declared progression stat. Unknown progression ids
/// have no honest base; the spawn authority fails closed on them.
pub fn progression_spawn_base(id: &str) -> Option<i64> {
    match id {
        XP_STAT_ID => Some(PLAYER_SPAWN_XP),
        LEVEL_STAT_ID => Some(PLAYER_SPAWN_LEVEL),
        _ => None,
    }
}

fn actor_definition<'a>(
    state: &DaggerGameplayState,
    catalog: &'a DaggerGameplayCatalog,
    actor: &str,
) -> Result<&'a DaggerActorDefinition, DaggerGameplayError> {
    let binding = state
        .actor(actor)
        .ok_or_else(|| DaggerGameplayError::InvalidState(format!("unknown actor {actor}")))?;
    catalog.actors().get(binding.definition()).ok_or_else(|| {
        DaggerGameplayError::InvalidState(format!(
            "actor {actor} references unknown definition {}",
            binding.definition()
        ))
    })
}

/// One actor's live stat BASE from the mechanics component. Progression
/// values are base-owned (no attributed sources write them); live evaluation
/// reads go through `live_stats_map`.
pub fn live_stat_base(
    state: &DaggerGameplayState,
    actor: &str,
    stat: &str,
) -> Result<i64, DaggerGameplayError> {
    let binding = state
        .actor(actor)
        .ok_or_else(|| DaggerGameplayError::InvalidState(format!("unknown actor {actor}")))?;
    let component = state
        .entities()
        .component::<StatsComponent>(binding.entity())
        .map_err(|error| DaggerGameplayError::InvalidState(format!("stats component: {error}")))?
        .ok_or_else(|| {
            DaggerGameplayError::InvalidState(format!("missing stats component: {actor}"))
        })?;
    let stat_id =
        StatId::parse(stat.to_string()).map_err(|error| DaggerGameplayError::InvalidId {
            path: "stat".to_string(),
            value: format!("{stat}: {error:?}"),
        })?;
    component
        .base(&stat_id)
        .map(|value| value.get())
        .ok_or_else(|| {
            DaggerGameplayError::InvalidState(format!("missing stat base {stat}@{actor}"))
        })
}

/// Set one actor's stat base through the Engine's stat-base mutation
/// service: catalog bounds are enforced upstream and track bounds are
/// revalidated against the candidate component, so a raised `health-max`
/// stays consistent with the track bound that reads it.
pub fn set_actor_stat_base(
    state: &mut DaggerGameplayState,
    catalog: &DaggerGameplayCatalog,
    actor: &str,
    stat: &str,
    value: i64,
) -> Result<(), DaggerGameplayError> {
    let entity = state
        .actor(actor)
        .ok_or_else(|| DaggerGameplayError::InvalidState(format!("unknown actor {actor}")))?
        .entity();
    let operation = OperationId::parse("dagger-progression-set").expect("fixed operation identity");
    StatService::set_base(
        state.entities_mut(),
        catalog.mechanics(),
        StatBaseMutationRequest {
            operation: operation.clone(),
            source: SourceInstanceIdentity::Request {
                operation,
                instance: SourceInstanceId::parse("dagger-progression")
                    .expect("fixed source identity"),
            },
            entity,
            stat: StatId::parse(stat.to_string()).map_err(|error| {
                DaggerGameplayError::InvalidId {
                    path: "stat".to_string(),
                    value: format!("{stat}: {error:?}"),
                }
            })?,
            base: MechanicsScalar::new(value).map_err(|error| {
                DaggerGameplayError::InvalidValue {
                    path: format!("stats.{stat}"),
                    reason: format!("mechanics scalar rejected: {error:?}"),
                }
            })?,
            expected_revision: None,
        },
    )
    .map_err(|error| {
        DaggerGameplayError::InvalidState(format!("set stat base {stat}@{actor}: {error:?}"))
    })?;
    Ok(())
}

/// Materialize one actor's live stat values policy-style: base values plus
/// any active attributed sources through `StatService::evaluate`, covering
/// the definition's stats and skills, the `{track}-max` bases, the
/// `armor-<part>` stats, and the progression stats (which materialize from
/// the component like armor parts do — declared attributes/skills zero-fill
/// for definition-omitted ids, but progression stats must never silently
/// read as 0, so a missing component stat is an honest error).
fn live_stats_map(
    state: &DaggerGameplayState,
    catalog: &DaggerGameplayCatalog,
    actor: &str,
) -> Result<BTreeMap<String, i64>, DaggerGameplayError> {
    let binding = state
        .actor(actor)
        .ok_or_else(|| DaggerGameplayError::InvalidState(format!("unknown actor {actor}")))?;
    let definition = actor_definition(state, catalog, actor)?;
    let operation =
        OperationId::parse("dagger-progression-eval").expect("fixed operation identity");
    let ids = definition
        .stats
        .keys()
        .chain(definition.skills.keys())
        .cloned()
        .chain(
            definition
                .tracks
                .iter()
                .map(|track| track_max_stat_id(&track.id)),
        )
        .chain(
            catalog
                .stats()
                .armor_parts
                .iter()
                .map(|part| armor_part_stat_id(part)),
        )
        .chain(catalog.stats().progression.iter().cloned());
    let mut values = BTreeMap::new();
    for id in ids {
        let stat = StatId::parse(id.clone()).map_err(|error| DaggerGameplayError::InvalidId {
            path: "stat".to_string(),
            value: format!("{id}: {error:?}"),
        })?;
        let evaluation = StatService::evaluate(
            state.entities(),
            catalog.mechanics(),
            binding.entity(),
            &stat,
            &operation,
            &[],
        )
        .map_err(|error| {
            DaggerGameplayError::InvalidState(format!("stat evaluation {id}@{actor}: {error:?}"))
        })?;
        values.insert(id.clone(), evaluation.value.get());
    }
    for declared in catalog
        .stats()
        .attributes
        .iter()
        .chain(catalog.stats().skills.iter())
    {
        values.entry(declared.clone()).or_insert(0);
    }
    Ok(values)
}

/// Evaluate one derived rule against an actor's LIVE component stats —
/// policy-style materialization, never definition bases. `overrides`
/// substitute stat values in the materialized map (e.g. the not-yet-committed
/// xp total when computing thresholds before the award lands). This is the
/// only context where live-reading rules like `xp-level` evaluate.
pub fn evaluate_derived_rule_live(
    state: &DaggerGameplayState,
    catalog: &DaggerGameplayCatalog,
    rule_id: &str,
    actor: &str,
    evidence: &[DaggerEvidence],
    overrides: &[(String, i64)],
) -> Result<i64, DaggerGameplayError> {
    let rule = catalog
        .derived()
        .get(rule_id)
        .ok_or_else(|| DaggerGameplayError::InvalidValue {
            path: format!("derived[{rule_id}]"),
            reason: "unknown derived rule".to_string(),
        })?;
    let definition = actor_definition(state, catalog, actor)?;
    let mut stats = live_stats_map(state, catalog, actor)?;
    for (id, value) in overrides {
        stats.insert(id.clone(), *value);
    }
    let context = ExprContext {
        catalog,
        actor: ActorExprValues {
            definition,
            stats: &stats,
            // Live rule evaluation reads stats and evidence only; track
            // currents and equipment are not part of the progression context.
            tracks: None,
            equipment: None,
        },
        target: None,
        evidence,
    };
    evaluate_expr(&rule.expr, &context).map_err(|rejection| DaggerGameplayError::InvalidValue {
        path: format!("derived[{rule_id}]@{actor}"),
        reason: format!("live evaluation rejected: {rejection:?}"),
    })
}

/// The level semantics in one place: how many `xp-level` thresholds a killer
/// WOULD have crossed after one kill's award, as `(level_before,
/// level_after)`. `Ok(None)` when the kill awards nothing (victim has no
/// `xpReward`, or the killer is not player-kind). No mutation — callers use
/// this to size the hp-roll evidence; `award_kill_progression` recomputes it
/// authoritatively while applying.
pub fn kill_level_gains(
    state: &DaggerGameplayState,
    catalog: &DaggerGameplayCatalog,
    killer: &str,
    victim: &str,
) -> Result<Option<(i64, i64)>, DaggerGameplayError> {
    Ok(award_inputs(state, catalog, killer, victim)?
        .map(|inputs| (inputs.level_before, inputs.level_after)))
}

struct AwardInputs {
    victim_definition: String,
    xp_reward: i64,
    xp_before: i64,
    xp_after: i64,
    level_before: i64,
    level_after: i64,
    hit_points_per_level: Option<i64>,
}

fn award_inputs(
    state: &DaggerGameplayState,
    catalog: &DaggerGameplayCatalog,
    killer: &str,
    victim: &str,
) -> Result<Option<AwardInputs>, DaggerGameplayError> {
    let victim_definition = actor_definition(state, catalog, victim)?;
    // A victim without an xpReward awards nothing (e.g. the player).
    let Some(xp_reward) = victim_definition.xp_reward else {
        return Ok(None);
    };
    let killer_definition = actor_definition(state, catalog, killer)?;
    // Only player kills award progression; AI kills don't.
    if killer_definition.kind != DaggerActorKind::Player {
        return Ok(None);
    }
    let xp_before = live_stat_base(state, killer, XP_STAT_ID)?;
    let xp_after =
        xp_before
            .checked_add(xp_reward)
            .ok_or_else(|| DaggerGameplayError::InvalidValue {
                path: format!("actors[{killer}].xp"),
                reason: format!("xp overflow adding {xp_reward} to {xp_before}"),
            })?;
    let level_before = live_stat_base(state, killer, LEVEL_STAT_ID)?;
    // The level curve reads the xp total as it will be AFTER the award
    // lands; the rule is the arithmetic authority, nothing here reimplements
    // it. The live level is the spawn base plus the thresholds crossed, and
    // never decreases.
    let thresholds = evaluate_derived_rule_live(
        state,
        catalog,
        XP_LEVEL_RULE_ID,
        killer,
        &[],
        &[(XP_STAT_ID.to_string(), xp_after)],
    )?;
    let level_after =
        level_before.max(PLAYER_SPAWN_LEVEL.checked_add(thresholds).ok_or_else(|| {
            DaggerGameplayError::InvalidValue {
                path: format!("actors[{killer}].level"),
                reason: format!("level overflow at {thresholds} thresholds"),
            }
        })?);
    Ok(Some(AwardInputs {
        victim_definition: victim_definition.id.clone(),
        xp_reward,
        xp_before,
        xp_after,
        level_before,
        level_after,
        hit_points_per_level: killer_definition.hit_points_per_level,
    }))
}

/// Award one kill's progression to the killer. `Ok(None)` when the kill
/// awards nothing (see `kill_level_gains`). The caller (the runtime) ensures
/// the kill actually happened; this authority owns the award's state changes.
///
/// Per level gained the `hit-points-per-level-up` rule evaluates with the
/// bounded roll crossing as evidence id `<killer>.level-up.<level>.hp-roll`
/// in [hitPointsPerLevel/2, hitPointsPerLevel]; the result applies to the
/// `health-max` stat base AND to current health (clamped to the new maximum
/// through the track service; the donor raises only the maximum — adding
/// current health is an experiment-profile choice). All inputs are validated
/// before any mutation.
pub fn award_kill_progression(
    state: &mut DaggerGameplayState,
    catalog: &DaggerGameplayCatalog,
    killer: &str,
    victim: &str,
    evidence: &[DaggerEvidence],
) -> Result<Option<DaggerProgressionRecord>, DaggerGameplayError> {
    let Some(inputs) = award_inputs(state, catalog, killer, victim)? else {
        return Ok(None);
    };
    // Validate every level-up input before mutating anything.
    let mut rolls = Vec::new();
    for level in (inputs.level_before + 1)..=inputs.level_after {
        let hit_points_per_level = inputs.hit_points_per_level.ok_or_else(|| {
            DaggerGameplayError::InvalidState(format!(
                "actor {killer} levels up without hitPointsPerLevel"
            ))
        })?;
        let minimum = hit_points_per_level / 2;
        let roll_evidence = format!("{killer}.level-up.{level}.hp-roll");
        let roll = evidence
            .iter()
            .find(|entry| entry.id == roll_evidence)
            .map(|entry| entry.value)
            .ok_or_else(|| DaggerGameplayError::InvalidValue {
                path: roll_evidence.clone(),
                reason: "missing level-up hp roll evidence".to_string(),
            })?;
        if !(minimum..=hit_points_per_level).contains(&roll) {
            return Err(DaggerGameplayError::InvalidValue {
                path: roll_evidence,
                reason: format!(
                    "hp roll must be in [{minimum}, {hit_points_per_level}], got {roll}"
                ),
            });
        }
        rolls.push((level, roll_evidence, roll));
    }

    set_actor_stat_base(state, catalog, killer, XP_STAT_ID, inputs.xp_after)?;
    let health_max_stat = track_max_stat_id("health");
    let mut level_ups = Vec::with_capacity(rolls.len());
    for (level, roll_evidence, roll) in rolls {
        let hit_points = evaluate_derived_rule_live(
            state,
            catalog,
            HP_PER_LEVEL_RULE_ID,
            killer,
            &[DaggerEvidence {
                id: HP_ROLL_EVIDENCE_ID.to_string(),
                value: roll,
            }],
            &[],
        )?;
        let health_max_before = live_stat_base(state, killer, &health_max_stat)?;
        let health_max_after = health_max_before.checked_add(hit_points).ok_or_else(|| {
            DaggerGameplayError::InvalidValue {
                path: format!("actors[{killer}].healthMax"),
                reason: format!("health max overflow adding {hit_points}"),
            }
        })?;
        set_actor_stat_base(state, catalog, killer, &health_max_stat, health_max_after)?;
        // Experiment profile: current health gains the roll too, clamped to
        // the new maximum through the track service (the donor raises only
        // the maximum).
        let current = state.track_value(killer, "health").unwrap_or(0);
        set_actor_track(state, catalog, killer, "health", current + hit_points)?;
        level_ups.push(DaggerLevelUpOutcome {
            level,
            roll_evidence,
            roll,
            hit_points,
            health_max_before,
            health_max_after,
        });
    }
    if inputs.level_after > inputs.level_before {
        set_actor_stat_base(state, catalog, killer, LEVEL_STAT_ID, inputs.level_after)?;
    }
    Ok(Some(DaggerProgressionRecord {
        victim: inputs.victim_definition,
        xp_awarded: inputs.xp_reward,
        xp_before: inputs.xp_before,
        xp_after: inputs.xp_after,
        level_before: inputs.level_before,
        level_after: inputs.level_after,
        level_ups,
    }))
}

/// The pacing divisor of the authored `xp-level` curve, read from the
/// compiled rule itself (shape `divFloor(stat("actor", "xp"), constant(d))`)
/// so readouts derive xp-to-next from the same rule without duplicating its
/// arithmetic. Re-authoring the curve in a different shape fails closed here
/// rather than silently pacing readouts against a stale constant.
pub fn xp_level_divisor(catalog: &DaggerGameplayCatalog) -> Result<i64, DaggerGameplayError> {
    let rule = catalog.derived().get(XP_LEVEL_RULE_ID).ok_or_else(|| {
        DaggerGameplayError::InvalidValue {
            path: format!("derived[{XP_LEVEL_RULE_ID}]"),
            reason: "unknown derived rule".to_string(),
        }
    })?;
    let unsupported = || DaggerGameplayError::InvalidValue {
        path: format!("derived[{XP_LEVEL_RULE_ID}]"),
        reason: "xp-level must be divFloor(stat(\"actor\", \"xp\"), constant(d)) with d > 0"
            .to_string(),
    };
    let rusty_engine::gameplay_standard::ComposedExactExpr::FloorDivide(left, right) = &rule.expr
    else {
        return Err(unsupported());
    };
    match (left.as_ref(), right.as_ref()) {
        (
            rusty_engine::gameplay_standard::ComposedExactExpr::Input(
                rusty_engine::gameplay_standard::ExactInputReference::StandardFact(
                    rusty_engine::gameplay_standard::StandardExactFactReference::Stat {
                        role,
                        stat,
                    },
                ),
            ),
            rusty_engine::gameplay_standard::ComposedExactExpr::Literal(value),
        ) if role.as_str() == "actor" && stat.as_str() == XP_STAT_ID && value.get() > 0 => {
            Ok(value.get())
        }
        _ => Err(unsupported()),
    }
}

/// Reset one actor's progression to spawn state: the progression stat bases
/// back to their spawn bases and the `health-max` base back to the
/// definition-evaluated spawn maximum. Player-kind actors only; monsters
/// carry no progression state, so this is a no-op for them. Track CURRENTS
/// are restored by `restore_actor_tracks`, which reads the bases this resets.
pub fn reset_actor_progression(
    state: &mut DaggerGameplayState,
    catalog: &DaggerGameplayCatalog,
    actor: &str,
) -> Result<(), DaggerGameplayError> {
    let definition = actor_definition(state, catalog, actor)?;
    if definition.kind != DaggerActorKind::Player {
        return Ok(());
    }
    for id in &catalog.stats().progression {
        let base = progression_spawn_base(id).ok_or_else(|| DaggerGameplayError::InvalidValue {
            path: "stats.progression".to_string(),
            reason: format!("no spawn base for progression stat {id}"),
        })?;
        set_actor_stat_base(state, catalog, actor, id, base)?;
    }
    // Restore the spawn-evaluated health maximum: the definition's derived
    // track max evaluated against definition bases, exactly as spawn_actor
    // computes it. The player's health max has no dice; a definition whose
    // health max rolls rejects honestly here.
    if let Some(track) = definition.tracks.iter().find(|track| track.id == "health") {
        let base_stats = super::eval::definition_base_stats(definition);
        let context = ExprContext {
            catalog,
            actor: ActorExprValues {
                definition,
                stats: &base_stats,
                tracks: None,
                equipment: None,
            },
            target: None,
            evidence: &[],
        };
        let maximum = evaluate_expr(&track.max, &context).map_err(|rejection| {
            DaggerGameplayError::InvalidValue {
                path: format!("actors[{}].tracks[health]", definition.id),
                reason: format!("spawn health maximum rejected: {rejection:?}"),
            }
        })?;
        set_actor_stat_base(state, catalog, actor, &track_max_stat_id("health"), maximum)?;
    }
    Ok(())
}
