use dagger_rpg::{
    compile_gameplay_package, resolve_dagger_action, DaggerActorState, DaggerEffect, DaggerEvent,
    DaggerEvidence, DaggerGameplayError, DaggerGameplayState, DaggerIntent, DaggerIntentOrigin,
    DaggerRejection,
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

fn state(with_ward: bool, silenced: bool) -> DaggerGameplayState {
    let mut state = DaggerGameplayState::default();
    let mut caster = DaggerActorState::new(20, 20).unwrap();
    if silenced {
        caster.add_condition("silenced");
    }
    let mut target = DaggerActorState::new(30, 0).unwrap();
    if with_ward {
        target.add_item("ruby-ward");
    }
    state.insert_actor("caster", caster);
    state.insert_actor("target", target);
    state
}

fn intent(origin: DaggerIntentOrigin) -> DaggerIntent {
    DaggerIntent {
        action: "ember-lance".to_string(),
        actor: "caster".to_string(),
        target: "target".to_string(),
        origin,
    }
}

fn hit_evidence() -> Vec<DaggerEvidence> {
    vec![DaggerEvidence {
        id: "spell-hit".to_string(),
        value: 80,
    }]
}

#[test]
fn typescript_package_compiles_and_ember_lance_is_intercepted_by_ruby_ward() {
    let catalog = compile_gameplay_package(PACKAGE).expect("compile authored Dagger package");
    assert_eq!(catalog.actions().len(), 1);
    assert_eq!(catalog.items().len(), 1);
    assert_eq!(catalog.rules().len(), 1);

    let mut state = state(true, false);
    let (receipt, readout) = resolve_dagger_action(
        &catalog,
        &mut state,
        identity(1),
        ResolutionMode::Apply,
        intent(DaggerIntentOrigin::Player),
        hit_evidence(),
    );

    assert!(receipt.succeeded());
    assert_eq!(receipt.commit(), &CommitStatus::Applied);
    assert_eq!(state.actor("caster").unwrap().magicka(), 15);
    assert_eq!(state.actor("target").unwrap().health(), 21);
    assert!(receipt.effects().contains(&DaggerEffect::Damage {
        target: "target".to_string(),
        amount: 9,
    }));
    assert!(receipt.events().contains(&DaggerEvent::InterceptorApplied {
        source: "ruby-ward".to_string(),
        amount: 3,
    }));
    assert_eq!(readout.package_fingerprint, catalog.fingerprint());
    assert!(readout.trace.iter().any(|record| {
        record
            .detail
            .as_ref()
            .is_some_and(|detail| format!("{detail:?}").contains("ruby-ward"))
    }));
    let json = serde_json::to_string_pretty(&readout).expect("serialize explanation readout");
    assert!(json.contains("packageFingerprint"));
    assert!(json.contains("interceptorApplied"));
}

#[test]
fn player_and_ai_origins_converge_on_the_same_resolution_path() {
    let catalog = compile_gameplay_package(PACKAGE).unwrap();
    let mut player_state = state(false, false);
    let mut ai_state = player_state.clone();
    let (player, _) = resolve_dagger_action(
        &catalog,
        &mut player_state,
        identity(2),
        ResolutionMode::Apply,
        intent(DaggerIntentOrigin::Player),
        hit_evidence(),
    );
    let (ai, _) = resolve_dagger_action(
        &catalog,
        &mut ai_state,
        identity(3),
        ResolutionMode::Apply,
        intent(DaggerIntentOrigin::Ai),
        hit_evidence(),
    );

    assert_eq!(player.effects(), ai.effects());
    assert_eq!(player.events(), ai.events());
    assert_eq!(player_state, ai_state);
}

#[test]
fn silence_rejects_the_spell_with_trace_and_no_mutation() {
    let catalog = compile_gameplay_package(PACKAGE).unwrap();
    let mut state = state(true, true);
    let before = state.clone();
    let (receipt, readout) = resolve_dagger_action(
        &catalog,
        &mut state,
        identity(4),
        ResolutionMode::Apply,
        intent(DaggerIntentOrigin::Player),
        hit_evidence(),
    );

    assert!(matches!(
        receipt.attempt().status(),
        AttemptStatus::Rejected(DaggerRejection::Rule { rule, .. }) if rule == "silence"
    ));
    assert_eq!(receipt.commit(), &CommitStatus::NotAttempted);
    assert_eq!(state, before);
    assert!(readout.status.contains("silence"));
    assert!(readout.trace.iter().any(|record| record
        .detail
        .as_ref()
        .is_some_and(|detail| format!("{detail:?}").contains("rejected tag spell"))));
}

#[test]
fn preview_uses_the_same_effects_without_mutating_state() {
    let catalog = compile_gameplay_package(PACKAGE).unwrap();
    let mut state = state(true, false);
    let before = state.clone();
    let (preview, _) = resolve_dagger_action(
        &catalog,
        &mut state,
        identity(5),
        ResolutionMode::Preview,
        intent(DaggerIntentOrigin::Player),
        hit_evidence(),
    );
    assert_eq!(preview.commit(), &CommitStatus::Previewed);
    assert_eq!(state, before);
    assert!(preview.effects().contains(&DaggerEffect::Damage {
        target: "target".to_string(),
        amount: 9,
    }));
}

#[test]
fn rust_semantic_compiler_rejects_invalid_authored_operations() {
    let mut package = serde_json::from_slice::<serde_json::Value>(PACKAGE).unwrap();
    package["payload"]["actions"][0]["program"]["thenProgram"]["steps"][0]["operation"]["amount"] =
        serde_json::json!(0);
    let bytes = serde_json::to_vec(&package).unwrap();
    assert!(matches!(
        compile_gameplay_package(&bytes),
        Err(DaggerGameplayError::InvalidValue { .. })
    ));
}
