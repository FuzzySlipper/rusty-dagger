use std::collections::BTreeMap;

use dagger_rpg::{
    compile_gameplay_package, evaluate_expr, resolve_dagger_action, set_actor_track, spawn_actor,
    track_maximum, ActorExprValues, DaggerEffect, DaggerEvent, DaggerEvidence, DaggerExpr,
    DaggerGameplayError, DaggerGameplayState, DaggerIntent, DaggerIntentOrigin, DaggerRejection,
    DaggerSubject, ExprContext,
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
            id: "weapon-damage.iron-longsword".to_string(),
            value: 8,
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
    // A hitting d100 roll reaches the damage operation, where the declared
    // weapon dice bounds (2..16) reject the supplied 99.
    let mut evidence = melee_evidence(25);
    evidence
        .iter_mut()
        .find(|entry| entry.id == "weapon-damage.iron-longsword")
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
            id: "weapon-damage.iron-longsword".to_string(),
            value: 8,
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

    // weaponDice referencing an item without a weapon block.
    let mut mutated = package.clone();
    let longsword_index = mutated["payload"]["items"]
        .as_array()
        .unwrap()
        .iter()
        .position(|item| item["id"] == "iron-longsword")
        .expect("iron-longsword in the package");
    mutated["payload"]["items"][longsword_index] =
        serde_json::json!({ "id": "iron-longsword", "weightUnits": 18, "value": 15 });
    assert!(matches!(
        compile_gameplay_package(&encode(mutated)),
        Err(DaggerGameplayError::InvalidValue { .. })
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
fn adrenaline_rush_reads_live_tracks_through_resolution() {
    let catalog = compile_gameplay_package(PACKAGE).expect("compile authored Dagger package");

    // Full health (85 of 85): no adrenaline bonus; chance vs rat is 37, so a
    // roll of 38 misses.
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

    // Health below max/8 (85/8 = 10): adrenaline adds +5, so the chance is
    // 42 and the same roll of 38 hits.
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
    assert!(receipt.effects().contains(&DaggerEffect::Damage {
        target: "rat-2007".to_string(),
        amount: 8,
    }));
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
    assert_eq!(by_id["weapon-damage.iron-longsword"], (2, 16));
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
    // A fungible item cannot equip.
    assert!(matches!(
        compile_gameplay_package(&mutated_package(|package| {
            package["payload"]["actors"][0]["inventory"][1]["equipSlot"] =
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
    assert_eq!(view.unique_items().len(), 1);
    let longsword = &view.unique_items()[0];
    assert_eq!(longsword.definition.as_str(), "iron-longsword");
    let capacity = view
        .capacity()
        .iter()
        .map(|usage| (usage.metric.as_str(), usage.used, usage.maximum))
        .collect::<Vec<_>>();
    assert_eq!(capacity, [("weight", 18, Some(300))]);

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
