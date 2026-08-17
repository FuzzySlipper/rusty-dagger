use dagger_rpg::{
    compile_gameplay_package, initial_actor_state, resolve_dagger_action, DaggerActorState,
    DaggerEffect, DaggerEvent, DaggerEvidence, DaggerGameplayError, DaggerGameplayState,
    DaggerIntent, DaggerIntentOrigin, DaggerRejection,
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
fn spawn_state() -> DaggerGameplayState {
    let catalog = compile_gameplay_package(PACKAGE).expect("compile authored Dagger package");
    let mut state = DaggerGameplayState::default();
    state.insert_actor(
        "player",
        initial_actor_state(&catalog, "player", &[]).expect("spawn player"),
    );
    state.insert_actor(
        "rat-2007",
        initial_actor_state(
            &catalog,
            "rat",
            &[DaggerEvidence {
                id: "rat.health".to_string(),
                value: 12,
            }],
        )
        .expect("spawn rat"),
    );
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

fn melee_evidence(d100: i64) -> Vec<DaggerEvidence> {
    vec![
        DaggerEvidence {
            id: "melee-attack.d100".to_string(),
            value: d100,
        },
        DaggerEvidence {
            id: "weapon-damage.iron-longsword".to_string(),
            value: 8,
        },
    ]
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
fn derived_track_maximums_evaluate_at_spawn() {
    let catalog = compile_gameplay_package(PACKAGE).expect("compile authored Dagger package");
    let player = initial_actor_state(&catalog, "player", &[]).expect("spawn player");
    assert_eq!(player.track("health"), Some(85));
    assert_eq!(player.track("stamina"), Some(90));
    assert_eq!(player.track("magicka"), Some(50));

    let rat = initial_actor_state(
        &catalog,
        "rat",
        &[DaggerEvidence {
            id: "rat.health".to_string(),
            value: 16,
        }],
    )
    .expect("spawn rat");
    assert_eq!(rat.track("health"), Some(16));

    let out_of_bounds = initial_actor_state(
        &catalog,
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
    let mut state = spawn_state();
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
        amount: 10,
    }));
    assert_eq!(state.actor("rat-2007").unwrap().track("health"), Some(4));
    assert_eq!(state.actor("player").unwrap().track("stamina"), Some(80));
    assert!(receipt.events().contains(&DaggerEvent::TrackSpent {
        actor: "player".to_string(),
        track: "stamina".to_string(),
        amount: 10,
    }));
    assert_eq!(readout.package_fingerprint, catalog.fingerprint());
    assert!(readout.trace.iter().any(|record| record.detail.is_some()));
}

#[test]
fn player_melee_miss_still_spends_stamina_but_applies_no_damage() {
    let catalog = compile_gameplay_package(PACKAGE).expect("compile authored Dagger package");
    let mut state = spawn_state();
    // Chance vs rat: 60 skill + 30 armor - 50 = 40, so a roll of 90 misses.
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
    assert_eq!(state.actor("rat-2007").unwrap().track("health"), Some(12));
    assert_eq!(state.actor("player").unwrap().track("stamina"), Some(80));
}

#[test]
fn player_and_ai_origins_share_the_same_policy_path() {
    let catalog = compile_gameplay_package(PACKAGE).expect("compile authored Dagger package");
    let mut player_state = spawn_state();
    let mut ai_state = spawn_state();
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
    assert_eq!(player_state, ai_state);
}

#[test]
fn insufficient_stamina_rejects_before_mutation() {
    let catalog = compile_gameplay_package(PACKAGE).expect("compile authored Dagger package");
    let mut state = spawn_state();
    let exhausted = DaggerActorState::new("player")
        .with_track("health", 85)
        .with_track("stamina", 4)
        .with_track("magicka", 50);
    state.insert_actor("player", exhausted);
    let before = state.clone();
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
    assert_eq!(state, before);
}

#[test]
fn roll_evidence_outside_declared_bounds_rejects() {
    let catalog = compile_gameplay_package(PACKAGE).expect("compile authored Dagger package");
    let mut state = spawn_state();
    // A hitting d100 roll reaches the damage operation, where the declared
    // weapon dice bounds (2..16) reject the supplied 99.
    let (receipt, _) = resolve_dagger_action(
        &catalog,
        &mut state,
        identity(1),
        ResolutionMode::Apply,
        player_melee_intent(DaggerIntentOrigin::Player),
        vec![
            DaggerEvidence {
                id: "melee-attack.d100".to_string(),
                value: 25,
            },
            DaggerEvidence {
                id: "weapon-damage.iron-longsword".to_string(),
                value: 99,
            },
        ],
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
    let mut state = spawn_state();
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
        vec![
            DaggerEvidence {
                id: "power-attack.d100".to_string(),
                value: 25,
            },
            DaggerEvidence {
                id: "weapon-damage.iron-longsword".to_string(),
                value: 8,
            },
        ],
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
    assert_eq!(state.actor("rat-2007").unwrap().track("health"), Some(0));
    assert_eq!(state.actor("player").unwrap().track("stamina"), Some(65));
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

    let catalog_for_state = compile_gameplay_package(PACKAGE).expect("compile authored package");
    let mut state = DaggerGameplayState::default();
    let mut player = initial_actor_state(&catalog_for_state, "player", &[]).expect("spawn player");
    player.add_condition("exhausted");
    state.insert_actor("player", player);
    state.insert_actor(
        "rat-2007",
        initial_actor_state(
            &catalog_for_state,
            "rat",
            &[DaggerEvidence {
                id: "rat.health".to_string(),
                value: 12,
            }],
        )
        .expect("spawn rat"),
    );
    let before = state.clone();

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
    assert_eq!(state, before);
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
    mutated["payload"]["actors"][1]["behavior"]["action"] = serde_json::Value::from("rat-explode");
    assert!(matches!(
        compile_gameplay_package(&encode(mutated)),
        Err(DaggerGameplayError::InvalidValue { .. })
    ));

    // Dice with min > max.
    let mut mutated = package.clone();
    mutated["payload"]["actors"][1]["tracks"][0]["max"] = serde_json::json!({
        "kind": "dice", "id": "rat.health", "min": 16, "max": 9
    });
    assert!(matches!(
        compile_gameplay_package(&encode(mutated)),
        Err(DaggerGameplayError::InvalidValue { .. })
    ));

    // weaponDice referencing an item without a weapon block.
    let mut mutated = package.clone();
    mutated["payload"]["items"][0] = serde_json::json!({ "id": "iron-longsword" });
    assert!(matches!(
        compile_gameplay_package(&encode(mutated)),
        Err(DaggerGameplayError::InvalidValue { .. })
    ));

    // Duplicate actor id.
    let mut mutated = package.clone();
    let rat = mutated["payload"]["actors"][1].clone();
    mutated["payload"]["actors"]
        .as_array_mut()
        .unwrap()
        .push(rat);
    assert!(matches!(
        compile_gameplay_package(&encode(mutated)),
        Err(DaggerGameplayError::DuplicateId { .. })
    ));
}
