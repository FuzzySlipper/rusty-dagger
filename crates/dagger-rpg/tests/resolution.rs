use std::collections::BTreeMap;

use dagger_rpg::{
    bind_actor_loot, compile_gameplay_package, evaluate_expr, generate_loot, loot_roll_evidence,
    resolve_dagger_action, set_actor_track, spawn_actor, spawn_container, track_maximum,
    ActorExprValues, DaggerEffect, DaggerEvent, DaggerEvidence, DaggerExpr, DaggerGameplayError,
    DaggerGameplayState, DaggerIntent, DaggerIntentOrigin, DaggerRejection, DaggerSubject,
    ExprContext,
};
use rusty_engine::gameplay_resolution::{
    AttemptStatus, CommitStatus, CorrelationId, ResolutionId, ResolutionIdentity, ResolutionMode,
};

const PACKAGE: &[u8] = include_bytes!("../../../data/gameplay/dagger-core.package.json");

fn identity(value: u64) -> ResolutionIdentity {
    ResolutionIdentity::root(
        ResolutionId::new(value).unwrap(),
        CorrelationId::new(700).unwrap(),
    )
}

/// Fixed spawn and combat rolls: rat health rolls 12 of 9..16, the player
/// rolls 25 on d100 (a hit against the rat's armor vulnerability), and the
/// longsword rolls 8 of 2..16.
fn spawn_state(catalog: &dagger_rpg::DaggerGameplayCatalog) -> DaggerGameplayState {
    let mut state = DaggerGameplayState::default();
    spawn_actor(&mut state, catalog, "player", "player", &[]).expect("spawn player");
    spawn_actor(
        &mut state,
        catalog,
        "rat",
        "rat-2007",
        &[DaggerEvidence {
            id: "rat.health".to_string(),
            value: 12,
        }],
    )
    .expect("spawn rat");
    state
}

fn player_melee_intent(origin: DaggerIntentOrigin) -> DaggerIntent {
    DaggerIntent {
        action: "melee-attack".to_string(),
        actor: "player".to_string(),
        target: "rat-2007".to_string(),
        origin,
    }
}

/// The player-only career/swing dice, supplied as 0 until careers and swing
/// states are modeled.
fn zeroed_player_dice(action: &str) -> Vec<DaggerEvidence> {
    [
        "swing-to-hit",
        "proficiency-to-hit",
        "racial-to-hit",
        "proficiency-damage",
        "racial-damage",
        "adrenaline-rush",
        "target-adrenaline-rush",
    ]
    .iter()
    .map(|suffix| DaggerEvidence {
        id: format!("{action}.{suffix}"),
        value: 0,
    })
    .collect()
}

fn melee_evidence(d100: i64) -> Vec<DaggerEvidence> {
    let mut evidence = vec![
        DaggerEvidence {
            id: "melee-attack.d100".to_string(),
            value: d100,
        },
        DaggerEvidence {
            id: "melee-attack.equipped-weapon-damage".to_string(),
            value: 8,
        },
        // Struck-part roll 0: head. Every rat armor part starts at the same
        // flat 30, so the part choice does not change the hit math at base.
        DaggerEvidence {
            id: "melee-attack.struck-body-part".to_string(),
            value: 0,
        },
    ];
    evidence.extend(zeroed_player_dice("melee-attack"));
    evidence
}

#[test]
fn typescript_package_compiles_the_real_catalogs() {
    let catalog = compile_gameplay_package(PACKAGE).expect("compile authored Dagger package");
    assert!(catalog.stats().attributes.contains("strength"));
    assert!(catalog.stats().skills.contains("long-blade"));
    assert!(catalog.stats().tracks.contains("health"));
    assert!(catalog.actors().contains_key("player"));
    assert!(catalog.actors().contains_key("rat"));
    assert!(catalog.actors().contains_key("skeletal-warrior"));
    assert!(catalog.actions().contains_key("melee-attack"));
    assert!(catalog.items()["iron-longsword"].weapon.is_some());
    assert!(catalog.encounters().contains_key("rat-introduction"));
    assert_eq!(catalog.actors()["rat"].mobile_id, Some(0));
    assert_eq!(catalog.actors()["skeletal-warrior"].mobile_id, Some(15));
    assert_eq!(
        catalog.actors()["rat"].behavior.as_ref().unwrap().action,
        "rat-bite"
    );
}

#[test]
fn spawn_attaches_mechanics_components_with_derived_track_maxima() {
    let catalog = compile_gameplay_package(PACKAGE).expect("compile authored Dagger package");
    let mut state = DaggerGameplayState::default();
    spawn_actor(&mut state, &catalog, "player", "player", &[]).expect("spawn player");
    assert_eq!(state.track_value("player", "health"), Some(85));
    assert_eq!(state.track_value("player", "stamina"), Some(90));
    assert_eq!(state.track_value("player", "magicka"), Some(50));

    spawn_actor(
        &mut state,
        &catalog,
        "rat",
        "rat",
        &[DaggerEvidence {
            id: "rat.health".to_string(),
            value: 16,
        }],
    )
    .expect("spawn rat");
    assert_eq!(state.track_value("rat", "health"), Some(16));

    let mut state = DaggerGameplayState::default();
    let out_of_bounds = spawn_actor(
        &mut state,
        &catalog,
        "rat",
        "rat",
        &[DaggerEvidence {
            id: "rat.health".to_string(),
            value: 17,
        }],
    );
    assert!(out_of_bounds.is_err());
}

#[test]
fn player_melee_hit_spends_stamina_and_applies_weapon_damage() {
    let catalog = compile_gameplay_package(PACKAGE).expect("compile authored Dagger package");
    let mut state = spawn_state(&catalog);
    let (receipt, readout) = resolve_dagger_action(
        &catalog,
        &mut state,
        identity(1),
        ResolutionMode::Apply,
        player_melee_intent(DaggerIntentOrigin::Player),
        melee_evidence(25),
    );

    assert!(receipt.succeeded());
    assert_eq!(receipt.commit(), &CommitStatus::Applied);
    // Weapon roll 8 + strength modifier floor((50-50)/5) = 8 damage.
    assert!(receipt.effects().contains(&DaggerEffect::Damage {
        target: "rat-2007".to_string(),
        amount: 8,
    }));
    assert!(receipt.effects().contains(&DaggerEffect::SpendTrack {
        actor: "player".to_string(),
        track: "stamina".to_string(),
        amount: 5,
    }));
    assert_eq!(state.track_value("rat-2007", "health"), Some(4));
    assert_eq!(state.track_value("player", "stamina"), Some(85));
    assert!(receipt.events().contains(&DaggerEvent::TrackSpent {
        actor: "player".to_string(),
        track: "stamina".to_string(),
        amount: 5,
    }));
    assert_eq!(readout.package_fingerprint, catalog.fingerprint());
    assert!(readout.trace.iter().any(|record| record.detail.is_some()));
}

#[test]
fn player_melee_miss_still_spends_stamina_but_applies_no_damage() {
    let catalog = compile_gameplay_package(PACKAGE).expect("compile authored Dagger package");
    let mut state = spawn_state(&catalog);
    // Chance vs rat: 60 skill + 30 armor - 50 + luck 0 + agility -3 = 37,
    // so a roll of 90 misses.
    let (receipt, _) = resolve_dagger_action(
        &catalog,
        &mut state,
        identity(1),
        ResolutionMode::Apply,
        player_melee_intent(DaggerIntentOrigin::Player),
        melee_evidence(90),
    );

    assert!(receipt.succeeded());
    assert!(!receipt
        .effects()
        .iter()
        .any(|effect| matches!(effect, DaggerEffect::Damage { .. })));
    assert_eq!(state.track_value("rat-2007", "health"), Some(12));
    assert_eq!(state.track_value("player", "stamina"), Some(85));
}

#[test]
fn player_and_ai_origins_share_the_same_policy_path() {
    let catalog = compile_gameplay_package(PACKAGE).expect("compile authored Dagger package");
    let mut player_state = spawn_state(&catalog);
    let mut ai_state = spawn_state(&catalog);
    resolve_dagger_action(
        &catalog,
        &mut player_state,
        identity(1),
        ResolutionMode::Apply,
        player_melee_intent(DaggerIntentOrigin::Player),
        melee_evidence(25),
    );
    resolve_dagger_action(
        &catalog,
        &mut ai_state,
        identity(2),
        ResolutionMode::Apply,
        player_melee_intent(DaggerIntentOrigin::Ai),
        melee_evidence(25),
    );
    for actor in ["player", "rat-2007"] {
        for track in ["health", "stamina", "magicka"] {
            assert_eq!(
                player_state.track_value(actor, track),
                ai_state.track_value(actor, track),
                "{actor}.{track} diverged between origins"
            );
        }
    }
}

#[test]
fn insufficient_stamina_rejects_before_mutation() {
    let catalog = compile_gameplay_package(PACKAGE).expect("compile authored Dagger package");
    let mut state = spawn_state(&catalog);
    set_actor_track(&mut state, &catalog, "player", "stamina", 4).expect("drain stamina");
    let (receipt, _) = resolve_dagger_action(
        &catalog,
        &mut state,
        identity(1),
        ResolutionMode::Apply,
        player_melee_intent(DaggerIntentOrigin::Player),
        melee_evidence(25),
    );

    assert!(!receipt.succeeded());
    assert!(matches!(
        receipt.attempt().status(),
        AttemptStatus::Rejected(DaggerRejection::InsufficientTrack { .. })
    ));
    assert_eq!(state.track_value("player", "stamina"), Some(4));
    assert_eq!(state.track_value("rat-2007", "health"), Some(12));
}

#[test]
fn roll_evidence_outside_declared_bounds_rejects() {
    let catalog = compile_gameplay_package(PACKAGE).expect("compile authored Dagger package");
    let mut state = spawn_state(&catalog);
    // A hitting d100 roll reaches the damage operation, where the equipped
    // longsword's bounds (2..16) reject the supplied 99.
    let mut evidence = melee_evidence(25);
    evidence
        .iter_mut()
        .find(|entry| entry.id == "melee-attack.equipped-weapon-damage")
        .expect("weapon evidence")
        .value = 99;
    let (receipt, _) = resolve_dagger_action(
        &catalog,
        &mut state,
        identity(1),
        ResolutionMode::Apply,
        player_melee_intent(DaggerIntentOrigin::Player),
        evidence,
    );

    assert!(!receipt.succeeded());
    assert!(matches!(
        receipt.attempt().status(),
        AttemptStatus::Rejected(DaggerRejection::RollOutOfBounds { .. })
    ));
}

#[test]
fn power_attack_hits_harder_and_costs_more_stamina() {
    let catalog = compile_gameplay_package(PACKAGE).expect("compile authored Dagger package");
    let mut state = spawn_state(&catalog);
    let mut evidence = vec![
        DaggerEvidence {
            id: "power-attack.d100".to_string(),
            value: 25,
        },
        DaggerEvidence {
            id: "power-attack.equipped-weapon-damage".to_string(),
            value: 8,
        },
        DaggerEvidence {
            id: "power-attack.struck-body-part".to_string(),
            value: 0,
        },
    ];
    evidence.extend(zeroed_player_dice("power-attack"));
    let (receipt, _) = resolve_dagger_action(
        &catalog,
        &mut state,
        identity(1),
        ResolutionMode::Apply,
        DaggerIntent {
            action: "power-attack".to_string(),
            actor: "player".to_string(),
            target: "rat-2007".to_string(),
            origin: DaggerIntentOrigin::Player,
        },
        evidence,
    );

    assert!(receipt.succeeded());
    // Weapon roll 8 + strength modifier 0 + power bonus 4 = 12 damage.
    assert!(receipt.effects().contains(&DaggerEffect::Damage {
        target: "rat-2007".to_string(),
        amount: 12,
    }));
    assert!(receipt.effects().contains(&DaggerEffect::SpendTrack {
        actor: "player".to_string(),
        track: "stamina".to_string(),
        amount: 25,
    }));
    assert_eq!(state.track_value("rat-2007", "health"), Some(0));
    assert_eq!(state.track_value("player", "stamina"), Some(65));
}

#[test]
fn injected_rule_rejects_a_tagged_action_while_condition_without_mutating() {
    let package: serde_json::Value =
        serde_json::from_slice(PACKAGE).expect("parse committed package");
    let mut mutated = package.clone();
    mutated["payload"]["rules"]
        .as_array_mut()
        .unwrap()
        .push(serde_json::json!({
            "id": "fatigue-lockout",
            "kind": "rejectTagWhileCondition",
            "tag": "melee",
            "condition": "exhausted"
        }));
    let catalog =
        compile_gameplay_package(&serde_json::to_vec(&mutated).expect("encode mutated package"))
            .expect("compile rule-injected package");

    let mut state = spawn_state(&catalog);
    let mut player = state.actors()["player"].clone();
    player.add_condition("exhausted");
    state.insert_actor("player", player);
    let (receipt, _) = resolve_dagger_action(
        &catalog,
        &mut state,
        identity(1),
        ResolutionMode::Apply,
        player_melee_intent(DaggerIntentOrigin::Player),
        melee_evidence(25),
    );

    assert!(!receipt.succeeded());
    assert!(matches!(
        receipt.attempt().status(),
        AttemptStatus::Rejected(DaggerRejection::Rule { .. })
    ));
    assert_eq!(state.track_value("player", "stamina"), Some(90));
    assert_eq!(state.track_value("rat-2007", "health"), Some(12));
}

#[test]
fn monster_attack_profiles_are_structured_catalog_data() {
    let catalog = compile_gameplay_package(PACKAGE).expect("compile authored Dagger package");
    let attacks_of = |id: &str| {
        catalog.actors()[id]
            .attacks
            .iter()
            .map(|range| (range.min, range.max))
            .collect::<Vec<_>>()
    };
    // Donor EnemyBasics: Rat 1-4, Imp 2-15, Ancient Lich 70-100 (single),
    // Spriggan 1-8 / 1-8 / 1-10 (three sub-attacks).
    assert_eq!(attacks_of("rat"), vec![(1, 4)]);
    assert_eq!(attacks_of("imp"), vec![(2, 15)]);
    assert_eq!(attacks_of("ancient-lich"), vec![(70, 100)]);
    assert_eq!(attacks_of("spriggan"), vec![(1, 8), (1, 8), (1, 10)]);
    assert_eq!(attacks_of("daedra-lord"), vec![(15, 50)]);
}

#[test]
fn career_owned_spell_point_multiplier_is_evidence_not_a_fixed_constant() {
    let catalog = compile_gameplay_package(PACKAGE).expect("compile authored Dagger package");
    let evaluate = |multiplier_milli: i64| {
        dagger_rpg::evaluate_derived_rule(
            &catalog,
            "spell-points",
            "player",
            &[DaggerEvidence {
                id: "spell-point-multiplier-milli".to_string(),
                value: multiplier_milli,
            }],
        )
        .expect("evaluate spell-points")
    };
    assert_eq!(evaluate(1500), 75); // 50 intelligence at 1.5x
    assert_eq!(evaluate(2000), 100); // non-default career: 2.0x
    assert_eq!(evaluate(500), 25); // non-default career: 0.5x
}

#[test]
fn binary64_behavior_tuning_crosses_admission_at_one_f32_boundary() {
    let package: serde_json::Value =
        serde_json::from_slice(PACKAGE).expect("parse committed package");
    let compile_with = |patch: &[&str], value: serde_json::Value| {
        let mut mutated = package.clone();
        let mut node = &mut mutated;
        for segment in &patch[..patch.len() - 1] {
            node = match segment.parse::<usize>() {
                Ok(index) => &mut node[index],
                Err(_) => &mut node[*segment],
            };
        }
        let last = patch.last().expect("patch segment");
        node[*last] = value;
        compile_gameplay_package(&serde_json::to_vec(&mutated).expect("encode mutated package"))
    };

    // Common decimal, exact multiplier, and a small range value all cross
    // without extra precision loss beyond the single f64 -> f32 boundary.
    let catalog = compile_with(
        &["payload", "actors", "2", "behavior", "patrolSpeed"],
        serde_json::Value::from(0.1),
    )
    .expect("0.1 patrol speed admits");
    assert_eq!(
        catalog.actors()["rat"]
            .behavior
            .as_ref()
            .unwrap()
            .patrol_speed,
        0.1_f32
    );
    let catalog = compile_with(
        &["payload", "actors", "2", "behavior", "patrolSpeed"],
        serde_json::Value::from(1.5),
    )
    .expect("1.5 patrol speed admits");
    assert_eq!(
        catalog.actors()["rat"]
            .behavior
            .as_ref()
            .unwrap()
            .patrol_speed,
        1.5_f32
    );
    let catalog = compile_with(
        &["payload", "actors", "2", "behavior", "detectionRange"],
        serde_json::Value::from(0.005),
    )
    .expect("small detection range admits");
    assert_eq!(
        catalog.actors()["rat"]
            .behavior
            .as_ref()
            .unwrap()
            .detection_range,
        0.005_f32
    );

    // Negative zero normalizes to zero where the field permits zero.
    let catalog = compile_with(
        &["payload", "actors", "2", "behavior", "patrolSpeed"],
        serde_json::Value::from(-0.0),
    )
    .expect("negative zero patrol speed admits");
    assert_eq!(
        catalog.actors()["rat"]
            .behavior
            .as_ref()
            .unwrap()
            .patrol_speed,
        0.0
    );

    // Dagger semantic ranges reject out-of-range and non-finite values.
    assert!(matches!(
        compile_with(
            &["payload", "actors", "2", "behavior", "detectionRange"],
            serde_json::Value::from(2000.0)
        ),
        Err(DaggerGameplayError::InvalidValue { .. })
    ));
    assert!(matches!(
        compile_with(
            &["payload", "actors", "2", "behavior", "detectionRange"],
            serde_json::Value::from(0.0005)
        ),
        Err(DaggerGameplayError::InvalidValue { .. })
    ));
    assert!(matches!(
        compile_with(
            &["payload", "actors", "2", "behavior", "detectionRange"],
            serde_json::Value::from(f64::NAN)
        ),
        Err(DaggerGameplayError::Payload(_)) | Err(DaggerGameplayError::InvalidValue { .. })
    ));

    // The explicit f64 -> f32 boundary on the player movement speed.
    let catalog = compile_with(
        &["payload", "actors", "0", "moveSpeed"],
        serde_json::Value::from(4.75),
    )
    .expect("fractional move speed admits");
    assert_eq!(catalog.actors()["player"].move_speed, Some(4.75_f32));
}

#[test]
fn admission_rejects_undeclared_vocabulary_and_dangling_references() {
    let package: serde_json::Value =
        serde_json::from_slice(PACKAGE).expect("parse committed package");
    let encode =
        |mutated: serde_json::Value| serde_json::to_vec(&mutated).expect("encode mutated package");

    // Undeclared stat id in an actor definition.
    let mut mutated = package.clone();
    mutated["payload"]["actors"][0]["stats"]["stealthiness"] = serde_json::Value::from(50);
    assert!(matches!(
        compile_gameplay_package(&encode(mutated)),
        Err(DaggerGameplayError::InvalidValue { .. })
    ));

    // Behavior referencing an unknown action.
    let mut mutated = package.clone();
    mutated["payload"]["actors"][2]["behavior"]["action"] = serde_json::Value::from("rat-explode");
    assert!(matches!(
        compile_gameplay_package(&encode(mutated)),
        Err(DaggerGameplayError::InvalidValue { .. })
    ));

    // Dice with min > max.
    let mut mutated = package.clone();
    mutated["payload"]["actors"][2]["tracks"][0]["max"] = serde_json::json!({
        "kind": "dice", "id": "rat.health", "min": 16, "max": 9
    });
    assert!(matches!(
        compile_gameplay_package(&encode(mutated)),
        Err(DaggerGameplayError::InvalidValue { .. })
    ));

    // The retired armor/weaponDice expression kinds are now unknown fields at
    // payload admission.
    let mut mutated = package.clone();
    mutated["payload"]["actors"][2]["tracks"][0]["max"] = serde_json::json!({
        "kind": "armor", "subject": "target"
    });
    assert!(matches!(
        compile_gameplay_package(&encode(mutated)),
        Err(DaggerGameplayError::Payload(_))
    ));
    let mut mutated = package.clone();
    mutated["payload"]["actors"][2]["tracks"][0]["max"] = serde_json::json!({
        "kind": "weaponDice", "item": "iron-longsword"
    });
    assert!(matches!(
        compile_gameplay_package(&encode(mutated)),
        Err(DaggerGameplayError::Payload(_))
    ));

    // Duplicate actor id.
    let mut mutated = package.clone();
    let rat = mutated["payload"]["actors"][2].clone();
    mutated["payload"]["actors"]
        .as_array_mut()
        .unwrap()
        .push(rat);
    assert!(matches!(
        compile_gameplay_package(&encode(mutated)),
        Err(DaggerGameplayError::DuplicateId { .. })
    ));
}

#[test]
fn pow_milli_is_iterative_fixed_point_with_floor_at_each_step() {
    let catalog = compile_gameplay_package(PACKAGE).expect("compile authored Dagger package");
    let definition = &catalog.actors()["player"];
    let stats = BTreeMap::new();
    let context = ExprContext {
        catalog: &catalog,
        actor: ActorExprValues {
            definition,
            stats: &stats,
            tracks: None,
            equipment: None,
        },
        target: None,
        evidence: &[],
    };
    let pow = |base: i64, exponent: i64| DaggerExpr::PowMilli {
        base: Box::new(DaggerExpr::Const { value: base }),
        exponent: Box::new(DaggerExpr::Const { value: exponent }),
    };
    assert_eq!(evaluate_expr(&pow(1040, 0), &context), Ok(1000));
    assert_eq!(evaluate_expr(&pow(1040, 1), &context), Ok(1040));
    // 1040 * 1040 / 1000 floors to 1081.
    assert_eq!(evaluate_expr(&pow(1040, 2), &context), Ok(1081));
    assert!(matches!(
        evaluate_expr(&pow(1040, 65), &context),
        Err(DaggerRejection::InvalidExpression(_))
    ));
    assert!(matches!(
        evaluate_expr(&pow(-1, 2), &context),
        Err(DaggerRejection::InvalidExpression(_))
    ));
}

#[test]
fn adrenaline_rush_requires_the_career_flag() {
    let catalog = compile_gameplay_package(PACKAGE).expect("compile authored Dagger package");

    // Full health (85 of 85): no adrenaline condition; chance vs rat is 37,
    // so a roll of 38 misses regardless of flags.
    let mut state = spawn_state(&catalog);
    let (receipt, _) = resolve_dagger_action(
        &catalog,
        &mut state,
        identity(1),
        ResolutionMode::Apply,
        player_melee_intent(DaggerIntentOrigin::Player),
        melee_evidence(38),
    );
    assert!(receipt.succeeded());
    assert!(!receipt
        .effects()
        .iter()
        .any(|effect| matches!(effect, DaggerEffect::Damage { .. })));
    assert_eq!(state.track_value("rat-2007", "health"), Some(12));

    // Low health (5 < 85/8 = 10) WITHOUT the career flag: no bonus (the
    // donor gates adrenaline on Career.AdrenalineRush), so the same roll
    // still misses.
    let mut state = spawn_state(&catalog);
    set_actor_track(&mut state, &catalog, "player", "health", 5).expect("lower player health");
    let (receipt, _) = resolve_dagger_action(
        &catalog,
        &mut state,
        identity(1),
        ResolutionMode::Apply,
        player_melee_intent(DaggerIntentOrigin::Player),
        melee_evidence(38),
    );
    assert!(receipt.succeeded());
    assert!(!receipt
        .effects()
        .iter()
        .any(|effect| matches!(effect, DaggerEffect::Damage { .. })));
    assert_eq!(state.track_value("rat-2007", "health"), Some(12));

    // Low health WITH the career flag: +5, so the chance is 42 and the same
    // roll of 38 hits.
    let mut state = spawn_state(&catalog);
    set_actor_track(&mut state, &catalog, "player", "health", 5).expect("lower player health");
    let mut evidence = melee_evidence(38);
    evidence
        .iter_mut()
        .find(|entry| entry.id == "melee-attack.adrenaline-rush")
        .expect("adrenaline evidence")
        .value = 1;
    let (receipt, _) = resolve_dagger_action(
        &catalog,
        &mut state,
        identity(1),
        ResolutionMode::Apply,
        player_melee_intent(DaggerIntentOrigin::Player),
        evidence,
    );
    assert!(receipt.succeeded());
    assert!(receipt.effects().contains(&DaggerEffect::Damage {
        target: "rat-2007".to_string(),
        amount: 8,
    }));
}

#[test]
fn target_adrenaline_rush_penalizes_a_flagged_low_health_target() {
    let catalog = compile_gameplay_package(PACKAGE).expect("compile authored Dagger package");

    // Rat spawned at its top health roll (16): max/8 = 2, so 1 health is
    // below the threshold but still alive.
    let low_health_rat_state = || {
        let mut state = DaggerGameplayState::default();
        spawn_actor(&mut state, &catalog, "player", "player", &[]).expect("spawn player");
        spawn_actor(
            &mut state,
            &catalog,
            "rat",
            "rat-2007",
            &[DaggerEvidence {
                id: "rat.health".to_string(),
                value: 16,
            }],
        )
        .expect("spawn rat");
        set_actor_track(&mut state, &catalog, "rat-2007", "health", 1).expect("lower rat health");
        state
    };

    // Without the target's career flag the chance stays 37: roll 33 hits
    // (damage clamps to the rat's 1 remaining health).
    let mut state = low_health_rat_state();
    let (receipt, _) = resolve_dagger_action(
        &catalog,
        &mut state,
        identity(1),
        ResolutionMode::Apply,
        player_melee_intent(DaggerIntentOrigin::Player),
        melee_evidence(33),
    );
    assert!(receipt.succeeded());
    assert!(receipt.effects().contains(&DaggerEffect::Damage {
        target: "rat-2007".to_string(),
        amount: 1,
    }));

    // With the target's career flag the chance drops to 32: the same roll
    // misses.
    let mut state = low_health_rat_state();
    let mut evidence = melee_evidence(33);
    evidence
        .iter_mut()
        .find(|entry| entry.id == "melee-attack.target-adrenaline-rush")
        .expect("target adrenaline evidence")
        .value = 1;
    let (receipt, _) = resolve_dagger_action(
        &catalog,
        &mut state,
        identity(1),
        ResolutionMode::Apply,
        player_melee_intent(DaggerIntentOrigin::Player),
        evidence,
    );
    assert!(receipt.succeeded());
    assert!(!receipt
        .effects()
        .iter()
        .any(|effect| matches!(effect, DaggerEffect::Damage { .. })));
    assert_eq!(state.track_value("rat-2007", "health"), Some(1));
}

#[test]
fn signed_differentials_truncate_toward_zero() {
    let catalog = compile_gameplay_package(PACKAGE).expect("compile authored Dagger package");
    let player = &catalog.actors()["player"];
    let rat = &catalog.actors()["rat"];
    // The CalculateStatsToHit term shape: (attacker luck - target luck) / 10.
    let differential = || DaggerExpr::DivTrunc {
        left: Box::new(DaggerExpr::Sub {
            left: Box::new(DaggerExpr::Stat {
                subject: DaggerSubject::Actor,
                id: "luck".to_string(),
            }),
            right: Box::new(DaggerExpr::Stat {
                subject: DaggerSubject::Target,
                id: "luck".to_string(),
            }),
        }),
        right: Box::new(DaggerExpr::Const { value: 10 }),
    };
    let evaluate = |actor_luck: i64, target_luck: i64| {
        let actor_stats = BTreeMap::from([("luck".to_string(), actor_luck)]);
        let target_stats = BTreeMap::from([("luck".to_string(), target_luck)]);
        let context = ExprContext {
            catalog: &catalog,
            actor: ActorExprValues {
                definition: player,
                stats: &actor_stats,
                tracks: None,
                equipment: None,
            },
            target: Some(ActorExprValues {
                definition: rat,
                stats: &target_stats,
                tracks: None,
                equipment: None,
            }),
            evidence: &[],
        };
        evaluate_expr(&differential(), &context).expect("differential evaluates")
    };
    assert_eq!(evaluate(51, 50), 0); // +1 truncates to 0
    assert_eq!(evaluate(61, 50), 1); // +11 truncates to 1
    assert_eq!(evaluate(50, 51), 0); // -1 truncates to 0 (floor would be -1)
    assert_eq!(evaluate(50, 61), -1); // -11 truncates to -1 (floor would be -2)

    // Division by zero still rejects.
    let zero = DaggerExpr::DivTrunc {
        left: Box::new(DaggerExpr::Const { value: 1 }),
        right: Box::new(DaggerExpr::Const { value: 0 }),
    };
    let stats = BTreeMap::new();
    let context = ExprContext {
        catalog: &catalog,
        actor: ActorExprValues {
            definition: player,
            stats: &stats,
            tracks: None,
            equipment: None,
        },
        target: None,
        evidence: &[],
    };
    assert!(matches!(
        evaluate_expr(&zero, &context),
        Err(DaggerRejection::InvalidExpression(_))
    ));
}

#[test]
fn track_reads_reject_where_live_values_do_not_exist() {
    let catalog = compile_gameplay_package(PACKAGE).expect("compile authored Dagger package");
    let definition = &catalog.actors()["player"];
    let stats = BTreeMap::new();
    let context = ExprContext {
        catalog: &catalog,
        actor: ActorExprValues {
            definition,
            stats: &stats,
            tracks: None,
            equipment: None,
        },
        target: None,
        evidence: &[],
    };
    let track = DaggerExpr::Track {
        subject: DaggerSubject::Actor,
        id: "health".to_string(),
    };
    assert!(matches!(
        evaluate_expr(&track, &context),
        Err(DaggerRejection::MissingValue(_))
    ));
    // TrackMax reads the `{track}-max` stat; definition-base maps (derived
    // rules, spawn) do not carry it, so the read rejects honestly.
    let track_max = DaggerExpr::TrackMax {
        subject: DaggerSubject::Actor,
        id: "health".to_string(),
    };
    assert!(matches!(
        evaluate_expr(&track_max, &context),
        Err(DaggerRejection::MissingValue(_))
    ));

    // A track-max expression reading a track current compiles (the id is
    // declared) but spawn rejects it: the value it would read is what the
    // spawn is constructing.
    let package: serde_json::Value =
        serde_json::from_slice(PACKAGE).expect("parse committed package");
    let mut mutated = package.clone();
    mutated["payload"]["actors"][0]["tracks"][0]["max"] = serde_json::json!({
        "kind": "track", "subject": "actor", "id": "health"
    });
    let catalog =
        compile_gameplay_package(&serde_json::to_vec(&mutated).expect("encode mutated package"))
            .expect("track-current track max compiles");
    let mut state = DaggerGameplayState::default();
    assert!(spawn_actor(&mut state, &catalog, "player", "player", &[]).is_err());
}

#[test]
fn negative_bounded_dice_admits_and_evaluates() {
    let catalog = compile_gameplay_package(PACKAGE).expect("compile authored Dagger package");
    // The authored melee-attack carries dice("melee-attack.swing-to-hit",
    // -10, 10); admission succeeding proves negative bounds compile. A
    // negative in-bounds swing value evaluates (chance 37 - 5 = 32, roll 25
    // still hits).
    let mut state = spawn_state(&catalog);
    let mut evidence = melee_evidence(25);
    evidence
        .iter_mut()
        .find(|entry| entry.id == "melee-attack.swing-to-hit")
        .expect("swing evidence")
        .value = -5;
    let (receipt, _) = resolve_dagger_action(
        &catalog,
        &mut state,
        identity(1),
        ResolutionMode::Apply,
        player_melee_intent(DaggerIntentOrigin::Player),
        evidence,
    );
    assert!(receipt.succeeded());
    assert!(receipt.effects().contains(&DaggerEffect::Damage {
        target: "rat-2007".to_string(),
        amount: 8,
    }));

    // Out of the negative bound still rejects.
    let mut state = spawn_state(&catalog);
    let mut evidence = melee_evidence(25);
    evidence
        .iter_mut()
        .find(|entry| entry.id == "melee-attack.swing-to-hit")
        .expect("swing evidence")
        .value = -11;
    let (receipt, _) = resolve_dagger_action(
        &catalog,
        &mut state,
        identity(1),
        ResolutionMode::Apply,
        player_melee_intent(DaggerIntentOrigin::Player),
        evidence,
    );
    assert!(!receipt.succeeded());
    assert!(matches!(
        receipt.attempt().status(),
        AttemptStatus::Rejected(DaggerRejection::RollOutOfBounds { .. })
    ));
}

#[test]
fn skill_uses_for_advancement_matches_the_donor_golden() {
    let catalog = compile_gameplay_package(PACKAGE).expect("compile authored Dagger package");
    let value = |evidence_id: &str, value: i64| DaggerEvidence {
        id: evidence_id.to_string(),
        value,
    };
    // Donor golden (FormulaHelper.CalculateSkillUsesForAdvancement): skill
    // 30, advancement multiplier 2, career multiplier 1.30 (centi), level 1
    // yields floor(30 * 2 * 1.30 * 1.04 * 2 / 5 + 1) = 33.
    let uses = dagger_rpg::evaluate_derived_rule(
        &catalog,
        "skill-uses-for-advancement",
        "player",
        &[
            value("skill-value", 30),
            value("skill-advancement-multiplier", 2),
            value("career-advancement-multiplier-centi", 130),
            value("level", 1),
        ],
    )
    .expect("evaluate skill-uses-for-advancement");
    assert_eq!(uses, 33);

    // Player reflexes 2 (classic Average) scale skill uses by 1.0.
    let scale = dagger_rpg::evaluate_derived_rule(
        &catalog,
        "reflexes-skill-use-scale-milli",
        "player",
        &[],
    )
    .expect("evaluate reflexes-skill-use-scale-milli");
    assert_eq!(scale, 1000);
}

#[test]
fn career_flag_evidence_gates_recovery_rates() {
    let catalog = compile_gameplay_package(PACKAGE).expect("compile authored Dagger package");
    let value = |evidence_id: &str, value: i64| DaggerEvidence {
        id: evidence_id.to_string(),
        value,
    };
    // Base form unchanged with the flag off; RapidHealing raises +60 to +100.
    let recover = |rapid_healing: i64| {
        dagger_rpg::evaluate_derived_rule(
            &catalog,
            "health-recovery-rate",
            "player",
            &[
                value("max-health", 85),
                value("rapid-healing-active", rapid_healing),
            ],
        )
        .expect("evaluate health-recovery-rate")
    };
    assert_eq!(recover(0), 6);
    assert_eq!(recover(1), 10);

    // NoRegenSpellPoints zeroes spell-point recovery.
    let recover_spell_points = |no_regen: i64| {
        dagger_rpg::evaluate_derived_rule(
            &catalog,
            "spell-point-recovery-rate",
            "player",
            &[
                value("max-magicka", 50),
                value("no-regen-spell-points", no_regen),
            ],
        )
        .expect("evaluate spell-point-recovery-rate")
    };
    assert_eq!(recover_spell_points(0), 6);
    assert_eq!(recover_spell_points(1), 0);
}

#[test]
fn policy_live_stats_include_track_max_bases() {
    let catalog = compile_gameplay_package(PACKAGE).expect("compile authored Dagger package");
    let state = spawn_state(&catalog);
    // The spawn-stored `{track}-max` stat base the TrackMax node reads.
    assert_eq!(
        track_maximum(&state, &catalog, "player", "health"),
        Some(85)
    );
    assert_eq!(
        track_maximum(&state, &catalog, "rat-2007", "health"),
        Some(12)
    );
}

#[test]
fn action_roll_evidence_surfaces_the_player_career_dice() {
    let catalog = compile_gameplay_package(PACKAGE).expect("compile authored Dagger package");
    let rolls = dagger_rpg::action_roll_evidence(&catalog, "melee-attack")
        .expect("melee-attack roll evidence");
    let by_id: BTreeMap<&str, (i64, i64)> = rolls
        .iter()
        .map(|(id, min, max)| (id.as_str(), (*min, *max)))
        .collect();
    assert_eq!(by_id["melee-attack.swing-to-hit"], (-10, 10));
    assert_eq!(by_id["melee-attack.proficiency-to-hit"], (0, 30));
    assert_eq!(by_id["melee-attack.racial-to-hit"], (0, 30));
    assert_eq!(by_id["melee-attack.proficiency-damage"], (0, 30));
    assert_eq!(by_id["melee-attack.racial-damage"], (0, 30));
    assert_eq!(by_id["melee-attack.adrenaline-rush"], (0, 1));
    assert_eq!(by_id["melee-attack.target-adrenaline-rush"], (0, 1));
    // Equipment-driven evidence is not statically bounded; the dynamic
    // collector surfaces it instead.
    assert!(!by_id.contains_key("melee-attack.equipped-weapon-damage"));
    assert!(!by_id.contains_key("melee-attack.struck-body-part"));
    let dynamic = dagger_rpg::action_dynamic_roll_evidence(&catalog, "melee-attack")
        .expect("melee-attack dynamic evidence");
    assert_eq!(
        dynamic,
        vec![
            (
                "melee-attack.struck-body-part".to_string(),
                dagger_rpg::DaggerDynamicRoll::StruckBodyPart
            ),
            (
                "melee-attack.equipped-weapon-damage".to_string(),
                dagger_rpg::DaggerDynamicRoll::EquippedWeaponDamage
            ),
        ]
    );
}

fn mutated_package(patch: impl FnOnce(&mut serde_json::Value)) -> Vec<u8> {
    let mut package: serde_json::Value =
        serde_json::from_slice(PACKAGE).expect("parse committed package");
    patch(&mut package);
    serde_json::to_vec(&package).expect("encode mutated package")
}

#[test]
fn equipment_section_compiles_into_the_mechanics_catalog() {
    use rusty_engine::gameplay_mechanics::{
        CapacityMetricId, EquipmentSlotId, ItemDefinitionId, ItemKind,
    };

    let catalog = compile_gameplay_package(PACKAGE).expect("compile authored Dagger package");
    assert_eq!(catalog.equipment().capacity_metrics, ["weight".to_string()]);
    assert_eq!(catalog.equipment().slots.len(), 25);
    assert!(catalog.equipment().slot("right-hand").is_some());

    let mechanics = catalog.mechanics();
    assert!(mechanics
        .capacity_metric(&CapacityMetricId::parse("weight").unwrap())
        .is_some());
    let right_hand = mechanics
        .equipment_slot(&EquipmentSlotId::parse("right-hand").unwrap())
        .expect("right-hand slot");
    assert_eq!(
        right_hand
            .allowed_classifications
            .iter()
            .map(|id| id.as_str())
            .collect::<Vec<_>>(),
        ["weapon-one-hand", "weapon-two-hand"]
    );

    let longsword = mechanics
        .item(&ItemDefinitionId::parse("iron-longsword").unwrap())
        .expect("iron-longsword");
    assert_eq!(longsword.kind, ItemKind::Unique);
    assert_eq!(longsword.maximum_quantity, 1);
    assert_eq!(
        longsword
            .classifications
            .iter()
            .map(|id| id.as_str())
            .collect::<Vec<_>>(),
        ["weapon-one-hand"]
    );
    let policy = longsword.equipment.as_ref().expect("equippable");
    assert_eq!(policy.required_slots, 1);
    assert!(policy.exclusive_group.is_none());
    assert_eq!(
        longsword
            .capacity_costs
            .iter()
            .map(|cost| (cost.metric.as_str(), cost.units))
            .collect::<Vec<_>>(),
        [("weight", 18)]
    );

    // Two-handed weapons and shields share the hands exclusivity group.
    let staff = mechanics
        .item(&ItemDefinitionId::parse("iron-staff").unwrap())
        .expect("iron-staff");
    assert_eq!(
        staff
            .equipment
            .as_ref()
            .and_then(|policy| policy.exclusive_group.as_ref())
            .map(|group| group.as_str()),
        Some("hands")
    );
    let buckler = mechanics
        .item(&ItemDefinitionId::parse("buckler").unwrap())
        .expect("buckler");
    assert_eq!(
        buckler
            .equipment
            .as_ref()
            .and_then(|policy| policy.exclusive_group.as_ref())
            .map(|group| group.as_str()),
        Some("hands")
    );

    // Gold is a fungible stack with no capacity cost (1/400 kg is below the
    // quarter-kg unit resolution).
    let gold = mechanics
        .item(&ItemDefinitionId::parse("gold-piece").unwrap())
        .expect("gold-piece");
    assert_eq!(gold.kind, ItemKind::Fungible);
    assert!(gold.capacity_costs.is_empty());
    assert!(gold.equipment.is_none());

    // Armor value derives from the per-material table at compile (iron 7).
    assert_eq!(
        catalog.items()["iron-cuirass"]
            .armor
            .as_ref()
            .unwrap()
            .value,
        7
    );
    assert_eq!(
        catalog.items()["tower-shield"]
            .shield
            .as_ref()
            .unwrap()
            .value,
        4
    );
}

#[test]
fn equippable_items_without_an_equipment_section_reject() {
    // Section absent entirely.
    assert!(matches!(
        compile_gameplay_package(&mutated_package(|package| {
            package["payload"]
                .as_object_mut()
                .unwrap()
                .remove("equipment");
        })),
        Err(DaggerGameplayError::InvalidValue { .. })
    ));
    // Section present but slot-less.
    assert!(matches!(
        compile_gameplay_package(&mutated_package(|package| {
            package["payload"]["equipment"]["slots"] = serde_json::json!([]);
        })),
        Err(DaggerGameplayError::InvalidValue { .. })
    ));
    // Weighted items without the weight capacity metric reject too.
    assert!(matches!(
        compile_gameplay_package(&mutated_package(|package| {
            package["payload"]["equipment"]["capacityMetrics"] = serde_json::json!([]);
        })),
        Err(DaggerGameplayError::InvalidValue { .. })
    ));
}

#[test]
fn loadout_unknown_item_and_equip_slot_reject() {
    assert!(matches!(
        compile_gameplay_package(&mutated_package(|package| {
            package["payload"]["actors"][0]["inventory"][0]["item"] =
                serde_json::Value::from("mithril-ladle");
        })),
        Err(DaggerGameplayError::InvalidValue { .. })
    ));
    assert!(matches!(
        compile_gameplay_package(&mutated_package(|package| {
            package["payload"]["actors"][0]["inventory"][0]["equipSlot"] =
                serde_json::Value::from("third-hand");
        })),
        Err(DaggerGameplayError::InvalidValue { .. })
    ));
    // A fungible item cannot equip. The gold stack is the last loadout entry.
    let gold_index = {
        let package: serde_json::Value =
            serde_json::from_slice(PACKAGE).expect("parse committed package");
        package["payload"]["actors"][0]["inventory"]
            .as_array()
            .unwrap()
            .iter()
            .position(|entry| entry["item"] == "gold-piece")
            .expect("gold-piece in the player loadout")
    };
    assert!(matches!(
        compile_gameplay_package(&mutated_package(|package| {
            package["payload"]["actors"][0]["inventory"][gold_index]["equipSlot"] =
                serde_json::Value::from("left-hand");
        })),
        Err(DaggerGameplayError::InvalidValue { .. })
    ));
}

#[test]
fn invalid_hands_material_and_interceptor_fields_reject() {
    // Unknown hands variant fails payload admission (deny_unknown_fields /
    // unknown enum variant).
    assert!(matches!(
        compile_gameplay_package(&mutated_package(|package| {
            package["payload"]["items"][0]["weapon"]["hands"] = serde_json::Value::from("three");
        })),
        Err(DaggerGameplayError::Payload(_))
    ));
    // Unknown weapon material.
    assert!(matches!(
        compile_gameplay_package(&mutated_package(|package| {
            package["payload"]["items"][0]["weapon"]["material"] = serde_json::Value::from("glass");
        })),
        Err(DaggerGameplayError::InvalidValue { .. })
    ));
    // Unknown armor material (no table entry).
    let cuirass = |package: &serde_json::Value| {
        package["payload"]["items"]
            .as_array()
            .unwrap()
            .iter()
            .position(|item| item["id"] == "iron-cuirass")
            .expect("iron-cuirass in the package")
    };
    let index = cuirass(&serde_json::from_slice(PACKAGE).expect("parse committed package"));
    assert!(matches!(
        compile_gameplay_package(&mutated_package(|package| {
            package["payload"]["items"][index]["armor"]["material"] =
                serde_json::Value::from("glass");
        })),
        Err(DaggerGameplayError::InvalidValue { .. })
    ));
    // The retired interceptor block is now an unknown field.
    assert!(matches!(
        compile_gameplay_package(&mutated_package(|package| {
            package["payload"]["items"][index]["interceptor"] =
                serde_json::json!({ "kind": "reduceDamage", "amount": 1 });
        })),
        Err(DaggerGameplayError::Payload(_))
    ));
}

fn inventory_view(
    state: &DaggerGameplayState,
    catalog: &dagger_rpg::DaggerGameplayCatalog,
    actor: &str,
) -> rusty_engine::gameplay_mechanics::InventoryView {
    let owner = state.actor(actor).expect("actor binding").entity();
    rusty_engine::gameplay_mechanics::InventoryService::view(
        state.entities(),
        catalog.mechanics(),
        owner,
    )
    .expect("inventory view")
}

#[test]
fn spawn_binds_loadout_into_upstream_inventory_and_equipment() {
    use rusty_engine::gameplay_mechanics::{
        EquipmentComponent, EquipmentSlotId, InventoryCapacityLimit, InventoryComponent,
    };

    let catalog = compile_gameplay_package(PACKAGE).expect("compile authored Dagger package");
    let mut state = DaggerGameplayState::default();
    spawn_actor(&mut state, &catalog, "player", "player", &[]).expect("spawn player");
    let owner = state.actor("player").expect("player binding").entity();

    // Capacity limit: derived max-encumbrance floor(50 STR x 1.5) = 75 kg,
    // in quarter-kg units = 300.
    let inventory = state
        .entities()
        .component::<InventoryComponent>(owner)
        .expect("inventory component read")
        .expect("player has an inventory component");
    assert_eq!(
        inventory.capacity_limits(),
        &[InventoryCapacityLimit::new(
            rusty_engine::gameplay_mechanics::CapacityMetricId::parse("weight").unwrap(),
            300
        )]
    );

    let view = inventory_view(&state, &catalog, "player");
    assert_eq!(
        view.stacks()
            .iter()
            .map(|stack| (stack.definition.as_str(), stack.quantity))
            .collect::<Vec<_>>(),
        [("gold-piece", 25)]
    );
    // Longsword equipped at spawn plus the carried dagger and cuirass.
    assert_eq!(view.unique_items().len(), 3);
    let longsword = view
        .unique_items()
        .iter()
        .find(|item| item.definition.as_str() == "iron-longsword")
        .expect("longsword in the loadout");
    let capacity = view
        .capacity()
        .iter()
        .map(|usage| (usage.metric.as_str(), usage.used, usage.maximum))
        .collect::<Vec<_>>();
    assert_eq!(capacity, [("weight", 70, Some(300))]);

    let equipment = state
        .entities()
        .component::<EquipmentComponent>(owner)
        .expect("equipment component read")
        .expect("player has an equipment component");
    let assignment = equipment
        .assignment(&EquipmentSlotId::parse("right-hand").unwrap())
        .expect("right-hand assignment");
    assert_eq!(assignment.item, longsword.entity);
}

/// Player loadout carrying an unequipped two-hander, shield, two either-hand
/// weapons, and a cuirass for equip-legality tests.
fn equip_legality_state() -> (dagger_rpg::DaggerGameplayCatalog, DaggerGameplayState) {
    let package = mutated_package(|package| {
        package["payload"]["actors"][0]["inventory"] = serde_json::json!([
            { "item": "iron-staff" },
            { "item": "buckler" },
            { "item": "iron-dagger" },
            { "item": "iron-wakazashi" },
            { "item": "iron-cuirass" }
        ]);
    });
    let catalog = compile_gameplay_package(&package).expect("compile loadout package");
    let mut state = DaggerGameplayState::default();
    spawn_actor(&mut state, &catalog, "player", "player", &[]).expect("spawn player");
    (catalog, state)
}

fn equip(
    state: &mut DaggerGameplayState,
    catalog: &dagger_rpg::DaggerGameplayCatalog,
    item: &str,
    slot: &str,
) -> Result<(), String> {
    use rusty_engine::gameplay_mechanics::{
        EquipmentEquipRequest, EquipmentService, EquipmentSlotId, OperationId, SourceInstanceId,
        SourceInstanceIdentity,
    };

    let owner = state.actor("player").expect("player binding").entity();
    let view = inventory_view(state, catalog, "player");
    let item_entity = view
        .unique_items()
        .iter()
        .find(|entry| entry.definition.as_str() == item)
        .expect("item in inventory")
        .entity;
    let operation = OperationId::parse("test-equip").unwrap();
    let expected_state_revision = state.entities().revision();
    EquipmentService::equip(
        state.entities_mut(),
        catalog.mechanics(),
        EquipmentEquipRequest {
            operation: operation.clone(),
            source: SourceInstanceIdentity::Request {
                operation,
                instance: SourceInstanceId::parse("test").unwrap(),
            },
            owner,
            item: item_entity,
            slots: vec![EquipmentSlotId::parse(slot).unwrap()],
            expected_equipment_revision: None,
            expected_state_revision,
        },
    )
    .map(|_| ())
    .map_err(|error| format!("{error:?}"))
}

#[test]
fn either_hand_dual_wield_is_legal() {
    let (catalog, mut state) = equip_legality_state();
    equip(&mut state, &catalog, "iron-dagger", "right-hand").expect("dagger equips right-hand");
    equip(&mut state, &catalog, "iron-wakazashi", "left-hand").expect("wakazashi equips left-hand");
}

#[test]
fn two_hander_conflicts_with_an_equipped_shield() {
    let (catalog, mut state) = equip_legality_state();
    equip(&mut state, &catalog, "buckler", "left-hand").expect("buckler equips left-hand");
    let conflict = equip(&mut state, &catalog, "iron-staff", "right-hand");
    assert!(
        matches!(conflict, Err(ref error) if error.contains("EquipmentExclusivityConflict")),
        "expected exclusivity conflict, got {conflict:?}"
    );
}

#[test]
fn armor_into_a_wrong_slot_rejects() {
    let (catalog, mut state) = equip_legality_state();
    let mismatch = equip(&mut state, &catalog, "iron-cuirass", "head");
    assert!(
        matches!(mismatch, Err(ref error) if error.contains("EquipmentSlotClassificationMismatch")),
        "expected classification mismatch, got {mismatch:?}"
    );
    equip(&mut state, &catalog, "iron-cuirass", "chest-armor").expect("cuirass equips chest-armor");
}

fn item_entity(
    state: &DaggerGameplayState,
    catalog: &dagger_rpg::DaggerGameplayCatalog,
    item: &str,
) -> rusty_engine::core_ids::EntityId {
    inventory_view(state, catalog, "player")
        .unique_items()
        .iter()
        .find(|entry| entry.definition.as_str() == item)
        .expect("item in inventory")
        .entity
}

fn swap(
    state: &mut DaggerGameplayState,
    catalog: &dagger_rpg::DaggerGameplayCatalog,
    outgoing: &str,
    incoming: &str,
    slot: &str,
) {
    use rusty_engine::gameplay_mechanics::{
        EquipmentService, EquipmentSlotId, EquipmentSwapRequest, OperationId, SourceInstanceId,
        SourceInstanceIdentity,
    };

    let owner = state.actor("player").expect("player binding").entity();
    let operation = OperationId::parse("test-swap").unwrap();
    let expected_state_revision = state.entities().revision();
    let outgoing_item = item_entity(state, catalog, outgoing);
    let incoming_item = item_entity(state, catalog, incoming);
    EquipmentService::swap(
        state.entities_mut(),
        catalog.mechanics(),
        EquipmentSwapRequest {
            operation: operation.clone(),
            source: SourceInstanceIdentity::Request {
                operation,
                instance: SourceInstanceId::parse("test").unwrap(),
            },
            owner,
            outgoing_item,
            incoming_item,
            incoming_slots: vec![EquipmentSlotId::parse(slot).unwrap()],
            expected_equipment_revision: None,
            expected_state_revision,
        },
    )
    .unwrap_or_else(|error| panic!("swap {outgoing} -> {incoming}: {error:?}"));
}

fn unequip(
    state: &mut DaggerGameplayState,
    catalog: &dagger_rpg::DaggerGameplayCatalog,
    item: &str,
) {
    use rusty_engine::gameplay_mechanics::{
        EquipmentService, EquipmentUnequipRequest, OperationId, SourceInstanceId,
        SourceInstanceIdentity,
    };

    let owner = state.actor("player").expect("player binding").entity();
    let operation = OperationId::parse("test-unequip").unwrap();
    let expected_state_revision = state.entities().revision();
    let item = item_entity(state, catalog, item);
    EquipmentService::unequip(
        state.entities_mut(),
        catalog.mechanics(),
        EquipmentUnequipRequest {
            operation: operation.clone(),
            source: SourceInstanceIdentity::Request {
                operation,
                instance: SourceInstanceId::parse("test").unwrap(),
            },
            owner,
            item,
            expected_equipment_revision: None,
            expected_state_revision,
        },
    )
    .unwrap_or_else(|error| panic!("unequip {item}: {error:?}"));
}

fn resolve_melee(
    catalog: &dagger_rpg::DaggerGameplayCatalog,
    state: &mut DaggerGameplayState,
    sequence: u64,
    evidence: Vec<DaggerEvidence>,
) -> dagger_rpg::DaggerResolutionReceipt {
    resolve_dagger_action(
        catalog,
        state,
        identity(sequence),
        ResolutionMode::Apply,
        player_melee_intent(DaggerIntentOrigin::Player),
        evidence,
    )
    .0
}

fn with_weapon_damage(mut evidence: Vec<DaggerEvidence>, value: i64) -> Vec<DaggerEvidence> {
    evidence
        .iter_mut()
        .find(|entry| entry.id == "melee-attack.equipped-weapon-damage")
        .expect("weapon evidence")
        .value = value;
    evidence
}

#[test]
fn equipped_weapon_drives_damage_bounds_and_hit_skill() {
    let catalog = compile_gameplay_package(PACKAGE).expect("compile authored Dagger package");
    let mut state = spawn_state(&catalog);

    // Swap the right-hand longsword for the carried dagger: the damage
    // evidence is now bounded by the dagger's 1..6 range. A d100 of 1 still
    // hits (the short-blade chance clamps to the 3% floor), so evaluation
    // reaches the damage operation and the out-of-range 8 rejects.
    swap(
        &mut state,
        &catalog,
        "iron-longsword",
        "iron-dagger",
        "right-hand",
    );
    let receipt = resolve_melee(
        &catalog,
        &mut state,
        1,
        with_weapon_damage(melee_evidence(1), 8),
    );
    assert!(matches!(
        receipt.attempt().status(),
        AttemptStatus::Rejected(DaggerRejection::RollOutOfBounds { .. })
    ));
    assert_eq!(state.track_value("rat-2007", "health"), Some(12));
    assert_eq!(state.track_value("player", "stamina"), Some(90));

    // The dagger maps to short-blade, which the player has at 0: the hit
    // chance collapses to the 3% floor and the roll of 25 misses.
    let receipt = resolve_melee(
        &catalog,
        &mut state,
        2,
        with_weapon_damage(melee_evidence(25), 3),
    );
    assert!(receipt.succeeded());
    assert!(!receipt
        .effects()
        .iter()
        .any(|effect| matches!(effect, DaggerEffect::Damage { .. })));
    assert_eq!(state.track_value("rat-2007", "health"), Some(12));

    // Swap the longsword back: long-blade 60 hits for the rolled damage.
    swap(
        &mut state,
        &catalog,
        "iron-dagger",
        "iron-longsword",
        "right-hand",
    );
    let receipt = resolve_melee(&catalog, &mut state, 3, melee_evidence(25));
    assert!(receipt.succeeded());
    assert!(receipt.effects().contains(&DaggerEffect::Damage {
        target: "rat-2007".to_string(),
        amount: 8,
    }));
    assert_eq!(state.track_value("rat-2007", "health"), Some(4));
}

#[test]
fn unarmed_falls_back_to_hand_to_hand_skill_and_derived_damage() {
    let catalog = compile_gameplay_package(PACKAGE).expect("compile authored Dagger package");
    let mut state = spawn_state(&catalog);
    unequip(&mut state, &catalog, "iron-longsword");

    // Hand-to-hand 40: chance vs the rat is 40 + 30 - 50 + 0 - 3 = 17, so a
    // roll of 18 misses and 17 hits. Unarmed damage bounds are the derived
    // hand-to-hand range floor(40/10)+1 .. floor(40/5)+1 = 5..9.
    let receipt = resolve_melee(
        &catalog,
        &mut state,
        1,
        with_weapon_damage(melee_evidence(18), 5),
    );
    assert!(receipt.succeeded());
    assert_eq!(state.track_value("rat-2007", "health"), Some(12));
    let receipt = resolve_melee(
        &catalog,
        &mut state,
        2,
        with_weapon_damage(melee_evidence(17), 5),
    );
    assert!(receipt.succeeded());
    assert!(receipt.effects().contains(&DaggerEffect::Damage {
        target: "rat-2007".to_string(),
        amount: 5,
    }));
    assert_eq!(state.track_value("rat-2007", "health"), Some(7));

    // Above the derived unarmed range rejects out of bounds.
    let receipt = resolve_melee(
        &catalog,
        &mut state,
        3,
        with_weapon_damage(melee_evidence(17), 10),
    );
    assert!(matches!(
        receipt.attempt().status(),
        AttemptStatus::Rejected(DaggerRejection::RollOutOfBounds { .. })
    ));
    assert_eq!(state.track_value("rat-2007", "health"), Some(7));
}

#[test]
fn equipped_cuirass_lowers_the_chest_armor_stat_and_hit_chance() {
    use rusty_engine::gameplay_mechanics::{OperationId, StatId, StatService};

    let catalog = compile_gameplay_package(PACKAGE).expect("compile authored Dagger package");
    let armor_chest = |state: &DaggerGameplayState| {
        let owner = state.actor("player").expect("player binding").entity();
        StatService::evaluate(
            state.entities(),
            catalog.mechanics(),
            owner,
            &StatId::parse("armor-chest").unwrap(),
            &OperationId::parse("test-eval").unwrap(),
            &[],
        )
        .expect("armor-chest evaluates")
        .value
        .get()
    };

    // Base: the player's flat authored armor value 0 replicates per part;
    // the rat's 30 does the same.
    let mut state = spawn_state(&catalog);
    assert_eq!(armor_chest(&state), 0);

    // Equipping the iron cuirass (value 7, donor x5) drops chest armor to -35.
    equip(&mut state, &catalog, "iron-cuirass", "chest-armor").expect("cuirass equips chest-armor");
    assert_eq!(armor_chest(&state), -35);

    // Resolution level: a skeletal warrior (long-blade 75) striking the
    // player. Chance without the cuirass is 75 + 0 - 50 + 0 + 3 = 28; with
    // the cuirass on a chest strike it drops to -7, clamped to 3.
    let strike = |state: &mut DaggerGameplayState, sequence: u64| {
        resolve_dagger_action(
            &catalog,
            state,
            identity(sequence),
            ResolutionMode::Apply,
            DaggerIntent {
                action: "skeleton-strike".to_string(),
                actor: "skeleton-1".to_string(),
                target: "player".to_string(),
                origin: DaggerIntentOrigin::Ai,
            },
            vec![
                DaggerEvidence {
                    id: "skeleton-strike.d100".to_string(),
                    value: 10,
                },
                // 8 maps to chest through the classic struck-part table.
                DaggerEvidence {
                    id: "skeleton-strike.struck-body-part".to_string(),
                    value: 8,
                },
                DaggerEvidence {
                    id: "skeleton-strike.damage".to_string(),
                    value: 7,
                },
            ],
        )
        .0
    };
    let spawn_skeleton = |state: &mut DaggerGameplayState| {
        spawn_actor(
            state,
            &catalog,
            "skeletal-warrior",
            "skeleton-1",
            &[DaggerEvidence {
                id: "skeletal-warrior.health".to_string(),
                value: 20,
            }],
        )
        .expect("spawn skeletal warrior");
    };

    let mut state = spawn_state(&catalog);
    spawn_skeleton(&mut state);
    let receipt = strike(&mut state, 1);
    assert!(receipt.succeeded());
    assert!(receipt.effects().contains(&DaggerEffect::Damage {
        target: "player".to_string(),
        amount: 7,
    }));
    assert_eq!(state.track_value("player", "health"), Some(78));

    let mut state = spawn_state(&catalog);
    spawn_skeleton(&mut state);
    equip(&mut state, &catalog, "iron-cuirass", "chest-armor").expect("cuirass equips chest-armor");
    let receipt = strike(&mut state, 1);
    assert!(receipt.succeeded());
    assert!(!receipt
        .effects()
        .iter()
        .any(|effect| matches!(effect, DaggerEffect::Damage { .. })));
    assert_eq!(state.track_value("player", "health"), Some(85));
}

#[test]
fn struck_armor_maps_the_donor_body_part_table() {
    let catalog = compile_gameplay_package(PACKAGE).expect("compile authored Dagger package");
    let definition = &catalog.actors()["player"];
    // Spot-check the donor distribution: head 0-1, right-arm 2-4, left-arm
    // 5-7, chest 8-11, hands 12-15, legs 16-18, feet 19.
    let expected = [
        "head",
        "head",
        "right-arm",
        "right-arm",
        "right-arm",
        "left-arm",
        "left-arm",
        "left-arm",
        "chest",
        "chest",
        "chest",
        "chest",
        "hands",
        "hands",
        "hands",
        "hands",
        "legs",
        "legs",
        "legs",
        "feet",
    ];
    for (roll, part) in expected.iter().enumerate() {
        assert_eq!(dagger_rpg::struck_body_part_name(roll as i64), Some(*part));
    }

    // The node reads the subject's armor-<part> stat for the rolled part.
    let stats = BTreeMap::from([
        ("armor-head".to_string(), 11),
        ("armor-feet".to_string(), 66),
    ]);
    let struck = || DaggerExpr::StruckArmor {
        subject: DaggerSubject::Target,
        id: "test.struck-body-part".to_string(),
    };
    let evaluate = |roll: i64| {
        let target = &catalog.actors()["rat"];
        let context = ExprContext {
            catalog: &catalog,
            actor: ActorExprValues {
                definition,
                stats: &stats,
                tracks: None,
                equipment: None,
            },
            target: Some(ActorExprValues {
                definition: target,
                stats: &stats,
                tracks: None,
                equipment: None,
            }),
            evidence: &[DaggerEvidence {
                id: "test.struck-body-part".to_string(),
                value: roll,
            }],
        };
        evaluate_expr(&struck(), &context)
    };
    assert_eq!(evaluate(0), Ok(11));
    assert_eq!(evaluate(1), Ok(11));
    assert_eq!(evaluate(19), Ok(66));
    assert!(matches!(
        evaluate(20),
        Err(DaggerRejection::RollOutOfBounds { .. })
    ));
}

#[test]
fn min_metal_to_hit_gates_weapon_damage() {
    let catalog = compile_gameplay_package(PACKAGE).expect("compile authored Dagger package");
    let mut state = spawn_state(&catalog);
    spawn_actor(
        &mut state,
        &catalog,
        "imp",
        "imp-1",
        &[DaggerEvidence {
            id: "imp.health".to_string(),
            value: 15,
        }],
    )
    .expect("spawn imp");

    // Iron longsword vs the imp (requires steel): chance is
    // 60 + 15 - 50 + 0 - 3 = 22, so the roll of 10 hits but the damage plan
    // clamps to 0 with a MaterialIneffective trace marker.
    let (receipt, readout) = resolve_dagger_action(
        &catalog,
        &mut state,
        identity(1),
        ResolutionMode::Apply,
        DaggerIntent {
            action: "melee-attack".to_string(),
            actor: "player".to_string(),
            target: "imp-1".to_string(),
            origin: DaggerIntentOrigin::Player,
        },
        melee_evidence(10),
    );
    assert!(receipt.succeeded());
    assert!(receipt.effects().contains(&DaggerEffect::Damage {
        target: "imp-1".to_string(),
        amount: 0,
    }));
    assert!(readout.trace.iter().any(|record| matches!(
        record.detail,
        Some(dagger_rpg::DaggerTraceDetail::MaterialIneffective { .. })
    )));
    assert_eq!(state.track_value("imp-1", "health"), Some(15));

    // The rat has no material requirement: the same swing lands normally.
    let (receipt, readout) = resolve_dagger_action(
        &catalog,
        &mut state,
        identity(2),
        ResolutionMode::Apply,
        player_melee_intent(DaggerIntentOrigin::Player),
        melee_evidence(10),
    );
    assert!(receipt.succeeded());
    assert!(receipt.effects().contains(&DaggerEffect::Damage {
        target: "rat-2007".to_string(),
        amount: 8,
    }));
    assert!(!readout.trace.iter().any(|record| matches!(
        record.detail,
        Some(dagger_rpg::DaggerTraceDetail::MaterialIneffective { .. })
    )));
}

#[test]
fn spawn_loadout_over_the_capacity_limit_rejects() {
    // Six tower shields (50 units each) plus the longsword, dagger, and
    // cuirass weigh 370 quarter-kg against the player's 300-unit limit.
    let package = mutated_package(|package| {
        package["payload"]["actors"][0]["inventory"] = serde_json::json!([
            { "item": "iron-longsword", "equipSlot": "right-hand" },
            { "item": "iron-dagger" },
            { "item": "iron-cuirass" },
            { "item": "tower-shield" },
            { "item": "tower-shield" },
            { "item": "tower-shield" },
            { "item": "tower-shield" },
            { "item": "tower-shield" },
            { "item": "tower-shield" }
        ]);
    });
    let catalog = compile_gameplay_package(&package).expect("compile loadout package");
    let mut state = DaggerGameplayState::default();
    assert!(matches!(
        spawn_actor(&mut state, &catalog, "player", "player", &[]),
        Err(DaggerGameplayError::InvalidValue { .. })
    ));
}

// --- Classic loot tables (donor LootTables.cs DefaultLootTables) ---

/// Build loot evidence from the declared contract: every roll at its minimum
/// bound (success rolls 0 always succeed, picks select the pool's first
/// item), with per-id overrides applied.
fn loot_evidence(
    catalog: &dagger_rpg::DaggerGameplayCatalog,
    key: &str,
    overrides: &[(&str, i64)],
) -> Vec<DaggerEvidence> {
    loot_roll_evidence(catalog, key)
        .expect("loot roll contract")
        .into_iter()
        .map(|(id, min, _)| {
            let value = overrides
                .iter()
                .find(|(override_id, _)| *override_id == id)
                .map_or(min, |(_, value)| *value);
            DaggerEvidence { id, value }
        })
        .collect()
}

#[test]
fn loot_tables_compile_all_22_classic_keys_with_exact_donor_values() {
    let catalog = compile_gameplay_package(PACKAGE).expect("compile authored Dagger package");
    assert_eq!(catalog.loot_tables().len(), 22);
    // Exact transcription of `DefaultLootTables` (LootTables.cs:77-110).
    // Chances in authored field order: plant1, plant2, creature1, creature2,
    // creature3, misc1, misc2, armor, weapons, magic, clothing, books,
    // religious.
    let expected: [(&str, i64, i64, [i64; 13]); 22] = [
        ("-", 0, 0, [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]),
        ("A", 1, 10, [0, 0, 0, 0, 0, 0, 2, 5, 5, 2, 4, 0, 0]),
        ("B", 0, 0, [10, 10, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]),
        ("C", 2, 20, [10, 10, 5, 5, 5, 5, 2, 5, 25, 3, 0, 2, 2]),
        ("D", 1, 4, [6, 6, 6, 6, 6, 6, 0, 0, 0, 0, 0, 0, 4]),
        ("E", 20, 80, [0, 0, 0, 0, 0, 0, 1, 10, 10, 3, 4, 2, 15]),
        ("F", 4, 30, [2, 2, 5, 5, 5, 2, 3, 50, 50, 1, 0, 0, 0]),
        ("G", 3, 15, [0, 0, 0, 0, 0, 0, 3, 50, 50, 1, 5, 0, 0]),
        ("H", 2, 10, [0, 0, 0, 0, 0, 0, 0, 0, 100, 1, 2, 0, 0]),
        ("I", 0, 0, [0, 0, 0, 0, 0, 0, 0, 0, 0, 2, 0, 0, 5]),
        ("J", 50, 150, [0, 0, 0, 0, 0, 0, 0, 5, 5, 3, 0, 0, 0]),
        ("K", 1, 10, [3, 3, 3, 3, 3, 3, 2, 5, 5, 3, 0, 5, 100]),
        ("L", 1, 20, [0, 0, 3, 3, 3, 3, 5, 50, 50, 1, 75, 0, 3]),
        ("M", 1, 15, [1, 1, 1, 1, 1, 2, 3, 10, 10, 1, 15, 2, 1]),
        ("N", 1, 80, [5, 5, 5, 5, 5, 5, 2, 5, 5, 1, 20, 5, 5]),
        ("O", 5, 20, [1, 1, 1, 1, 1, 1, 0, 10, 15, 2, 0, 0, 0]),
        ("P", 5, 20, [5, 5, 5, 5, 5, 5, 5, 5, 10, 2, 0, 10, 0]),
        ("Q", 20, 80, [2, 2, 8, 8, 8, 2, 3, 10, 25, 3, 35, 5, 0]),
        ("R", 5, 20, [0, 0, 3, 3, 3, 5, 0, 5, 15, 2, 0, 0, 0]),
        ("S", 50, 125, [5, 5, 5, 5, 5, 15, 5, 10, 10, 3, 0, 5, 0]),
        ("T", 20, 80, [0, 0, 0, 0, 0, 0, 0, 100, 100, 1, 0, 0, 0]),
        ("U", 7, 30, [5, 5, 5, 5, 5, 10, 2, 10, 10, 2, 0, 2, 10]),
    ];
    for (key, gold_min, gold_max, chances) in expected {
        let table = catalog
            .loot_tables()
            .get(key)
            .unwrap_or_else(|| panic!("loot table {key}"));
        assert_eq!(
            (table.gold_min, table.gold_max),
            (gold_min, gold_max),
            "{key}"
        );
        let categories = &table.categories;
        let actual = [
            categories.plant1,
            categories.plant2,
            categories.creature1,
            categories.creature2,
            categories.creature3,
            categories.misc1,
            categories.misc2,
            categories.armor,
            categories.weapons,
            categories.magic,
            categories.clothing,
            categories.books,
            categories.religious,
        ];
        assert_eq!(actual, chances, "{key}");
    }
}

#[test]
fn loot_table_admission_rejects_bad_values() {
    // Category percentage above 100.
    assert!(matches!(
        compile_gameplay_package(&mutated_package(|package| {
            package["payload"]["lootTables"][1]["categories"]["armor"] = serde_json::json!(101);
        })),
        Err(DaggerGameplayError::InvalidValue { .. })
    ));
    // Inverted gold range (table A is 1..10).
    assert!(matches!(
        compile_gameplay_package(&mutated_package(|package| {
            package["payload"]["lootTables"][1]["gold"]["min"] = serde_json::json!(11);
        })),
        Err(DaggerGameplayError::InvalidValue { .. })
    ));
    // Duplicate key.
    assert!(matches!(
        compile_gameplay_package(&mutated_package(|package| {
            package["payload"]["lootTables"][1]["key"] = serde_json::Value::from("-");
        })),
        Err(DaggerGameplayError::DuplicateId { .. })
    ));
    // Unknown field in the section entries rejects at payload parse
    // (deny_unknown_fields).
    assert!(matches!(
        compile_gameplay_package(&mutated_package(|package| {
            package["payload"]["lootTables"][0]["surprise"] = serde_json::json!(1);
        })),
        Err(DaggerGameplayError::Payload(_))
    ));
    // Keys are "-" or a single uppercase letter.
    for bad_key in ["a", "AB", "1"] {
        assert!(matches!(
            compile_gameplay_package(&mutated_package(|package| {
                package["payload"]["lootTables"][1]["key"] = serde_json::Value::from(bad_key);
            })),
            Err(DaggerGameplayError::InvalidValue { .. })
        ));
    }
}

#[test]
fn loot_roll_evidence_declares_exactly_the_required_rolls() {
    let catalog = compile_gameplay_package(PACKAGE).expect("compile authored Dagger package");
    let weapon_max = catalog
        .items()
        .values()
        .filter(|item| item.weapon.is_some())
        .count() as i64
        - 1;
    let armor_max = catalog
        .items()
        .values()
        .filter(|item| item.armor.is_some() || item.shield.is_some())
        .count() as i64
        - 1;
    let contract = loot_roll_evidence(&catalog, "A").expect("loot roll contract");
    let mut expected = vec![("loot.A.gold".to_string(), 1, 10)];
    for slot in 0..3 {
        expected.push((format!("loot.A.weapons.{slot}"), 0, 99));
        expected.push((format!("loot.A.weapons.{slot}.pick"), 0, weapon_max));
    }
    for slot in 0..3 {
        expected.push((format!("loot.A.armor.{slot}"), 0, 99));
        expected.push((format!("loot.A.armor.{slot}.pick"), 0, armor_max));
    }
    // Unsupported categories (no catalog pool) roll success dice only.
    for category in ["misc2", "magic", "clothing"] {
        for slot in 0..3 {
            expected.push((format!("loot.A.{category}.{slot}"), 0, 99));
        }
    }
    assert_eq!(contract, expected);

    // The default table rolls nothing at all.
    assert_eq!(
        loot_roll_evidence(&catalog, "-").expect("default contract"),
        Vec::new()
    );
    // Unknown keys reject.
    assert!(matches!(
        loot_roll_evidence(&catalog, "Z"),
        Err(DaggerGameplayError::InvalidValue { .. })
    ));
}

#[test]
fn loot_generation_is_deterministic_from_evidence() {
    let catalog = compile_gameplay_package(PACKAGE).expect("compile authored Dagger package");
    let evidence = loot_evidence(&catalog, "A", &[]);
    let first = generate_loot(&catalog, "A", 1, &evidence).expect("generation");
    let second = generate_loot(&catalog, "A", 1, &evidence).expect("generation");
    assert_eq!(first, second);

    // A different pick roll yields a different weapon.
    let different = generate_loot(
        &catalog,
        "A",
        1,
        &loot_evidence(&catalog, "A", &[("loot.A.weapons.0.pick", 1)]),
    )
    .expect("generation");
    assert_ne!(first.items, different.items);
    assert_eq!(
        different.items[1],
        ("iron-broadsword".to_string(), 1),
        "pick 1 selects the second weapon by sorted id"
    );
}

#[test]
fn loot_success_rolls_halve_geometrically() {
    let catalog = compile_gameplay_package(PACKAGE).expect("compile authored Dagger package");
    // Table H has weapons chance 100: slots roll against 100, 50, 25. The
    // donor's SuccessRoll is a strict `<`, so 49 succeeds at chance 50 and
    // 99 fails at chance 25.
    let generation = generate_loot(
        &catalog,
        "H",
        1,
        &loot_evidence(
            &catalog,
            "H",
            &[("loot.H.weapons.1", 49), ("loot.H.weapons.2", 99)],
        ),
    )
    .expect("generation");
    let weapons = generation
        .categories
        .iter()
        .find(|category| category.category == "weapons")
        .expect("weapons category");
    assert_eq!(
        weapons
            .rolls
            .iter()
            .map(|roll| (roll.chance, roll.roll, roll.success))
            .collect::<Vec<_>>(),
        [(100, 0, true), (50, 49, true), (25, 99, false)]
    );
    assert_eq!(
        generation
            .items
            .iter()
            .filter(|(item, _)| item != "gold-piece")
            .count(),
        2
    );
}

#[test]
fn loot_gold_and_ingredient_chances_scale_with_level() {
    let catalog = compile_gameplay_package(PACKAGE).expect("compile authored Dagger package");
    // Table C: gold 2..20, creature1 chance 5 (level-scaled). Roll 14 fails
    // at level 1 (chance 5) and succeeds at level 3 (chance 15).
    let evidence = loot_evidence(
        &catalog,
        "C",
        &[("loot.C.gold", 5), ("loot.C.creature1.0", 14)],
    );
    let level1 = generate_loot(&catalog, "C", 1, &evidence).expect("level 1 generation");
    let level3 = generate_loot(&catalog, "C", 3, &evidence).expect("level 3 generation");
    assert_eq!(level1.gold.as_ref().expect("gold").amount, 5);
    assert_eq!(level3.gold.as_ref().expect("gold").amount, 15);
    let creature1 = |generation: &dagger_rpg::DaggerLootGeneration| {
        generation
            .categories
            .iter()
            .find(|category| category.category == "creature1")
            .expect("creature1 category")
            .clone()
    };
    assert!(!creature1(&level1).rolls[0].success);
    let scaled = creature1(&level3);
    assert_eq!(scaled.chance, 5);
    assert_eq!(scaled.effective_chance, 15);
    assert!(scaled.rolls[0].success);
    // Level-scaled categories have no catalog pool: success, no item.
    assert!(!scaled.supported);
    assert!(scaled.rolls[0].item.is_none());

    // Level 0 rejects; missing evidence rejects honestly.
    assert!(matches!(
        generate_loot(&catalog, "A", 0, &evidence),
        Err(DaggerRejection::InvalidExpression(_))
    ));
    assert!(matches!(
        generate_loot(&catalog, "A", 1, &[]),
        Err(DaggerRejection::MissingEvidence(_))
    ));
}

#[test]
fn loot_unsupported_category_successes_are_recorded_without_items() {
    let catalog = compile_gameplay_package(PACKAGE).expect("compile authored Dagger package");
    // Table A, all-minimum evidence: every positive-chance category's slot 0
    // succeeds. Magic (2) has no catalog pool, so its successes are visible
    // in the record but produce no items.
    let generation =
        generate_loot(&catalog, "A", 1, &loot_evidence(&catalog, "A", &[])).expect("generation");
    let magic = generation
        .categories
        .iter()
        .find(|category| category.category == "magic")
        .expect("magic category");
    assert!(!magic.supported);
    assert!(magic.rolls[0].success);
    assert!(magic.rolls[0].item.is_none());
    assert!(magic.rolls[0].pick.is_none());
    // Items are gold plus the supported picks only: 1 gold stack, 3 weapons
    // (chance 5/2/1 all succeed on roll 0), 3 armor pieces.
    assert_eq!(generation.items.len(), 7);
    assert_eq!(generation.gold.expect("gold").amount, 1);
}

#[test]
fn loot_rejects_out_of_bounds_evidence() {
    let catalog = compile_gameplay_package(PACKAGE).expect("compile authored Dagger package");
    for (id, value) in [
        ("loot.A.gold", 11),           // gold bounds are 1..=10
        ("loot.A.gold", 0),            // below the minimum
        ("loot.A.weapons.0", 100),     // success dice are 0..=99
        ("loot.A.weapons.0", -1),      // and non-negative
        ("loot.A.weapons.0.pick", 99), // picks bound over the weapon pool
    ] {
        assert!(
            matches!(
                generate_loot(
                    &catalog,
                    "A",
                    1,
                    &loot_evidence(&catalog, "A", &[(id, value)])
                ),
                Err(DaggerRejection::RollOutOfBounds { .. })
            ),
            "{id} = {value}"
        );
    }
}

#[test]
fn spawn_container_binds_generated_contents() {
    use rusty_engine::gameplay_mechanics::InventoryService;

    let catalog = compile_gameplay_package(PACKAGE).expect("compile authored Dagger package");
    let mut state = DaggerGameplayState::default();
    let evidence = loot_evidence(&catalog, "A", &[]);
    spawn_container(&mut state, &catalog, "treasure-1", "A", 1, &evidence)
        .expect("spawn container");
    let container = state.container("treasure-1").expect("container tracked");
    assert_eq!(container.key(), "A");
    assert_eq!(container.generation().items.len(), 7);

    let view = InventoryService::view(state.entities(), catalog.mechanics(), container.entity())
        .expect("container inventory view");
    assert_eq!(
        view.stacks()
            .iter()
            .map(|stack| (stack.definition.as_str(), stack.quantity))
            .collect::<Vec<_>>(),
        [("gold-piece", 1)]
    );
    // Unique picks are contained item entities: three weapons (pool[0] =
    // iron-battle-axe) and three armor pieces (pool[0] = buckler).
    let uniques = view
        .unique_items()
        .iter()
        .map(|item| item.definition.as_str())
        .collect::<Vec<_>>();
    assert_eq!(uniques.len(), 6);
    assert_eq!(
        uniques
            .iter()
            .filter(|id| **id == "iron-battle-axe")
            .count(),
        3
    );
    assert_eq!(uniques.iter().filter(|id| **id == "buckler").count(), 3);
}

#[test]
fn bind_actor_loot_binds_into_a_spawned_actors_inventory() {
    use rusty_engine::gameplay_mechanics::InventoryService;

    let catalog = compile_gameplay_package(PACKAGE).expect("compile authored Dagger package");
    let mut state = spawn_state(&catalog);
    let evidence = loot_evidence(&catalog, "A", &[]);
    let generation = bind_actor_loot(&mut state, &catalog, "rat-2007", "A", 1, &evidence)
        .expect("bind actor loot");
    // Binding shares the plain generation authority.
    assert_eq!(
        generation,
        generate_loot(&catalog, "A", 1, &evidence).expect("generation")
    );

    let rat = state.actor("rat-2007").expect("rat binding").entity();
    let view = InventoryService::view(state.entities(), catalog.mechanics(), rat)
        .expect("rat inventory view");
    assert_eq!(
        view.stacks()
            .iter()
            .map(|stack| (stack.definition.as_str(), stack.quantity))
            .collect::<Vec<_>>(),
        [("gold-piece", 1)]
    );
    assert_eq!(view.unique_items().len(), 6);

    // Binding requires a spawned actor.
    assert!(matches!(
        bind_actor_loot(&mut state, &catalog, "nobody", "A", 1, &evidence),
        Err(DaggerGameplayError::InvalidState(_))
    ));
}
