//! Production-path proof for the authored Dagger gameplay package.

use dagger_rpg::{
    compile_gameplay_package, resolve_dagger_action, DaggerActorState, DaggerEvidence,
    DaggerGameplayState, DaggerIntent, DaggerIntentOrigin,
};
use rusty_engine::gameplay_resolution::{
    CorrelationId, ResolutionId, ResolutionIdentity, ResolutionMode,
};

const PACKAGE: &[u8] = include_bytes!("../../../../data/gameplay/dagger-core.package.json");

fn state() -> DaggerGameplayState {
    let mut state = DaggerGameplayState::default();
    state.insert_actor(
        "caster",
        DaggerActorState::new(20, 20).expect("valid caster"),
    );
    let mut target = DaggerActorState::new(30, 0).expect("valid target");
    target.add_item("ruby-ward");
    state.insert_actor("target", target);
    state
}

fn resolve(origin: DaggerIntentOrigin, resolution: u64) -> (DaggerGameplayState, String) {
    let catalog = compile_gameplay_package(PACKAGE).expect("admit authored gameplay package");
    let mut state = state();
    let identity = ResolutionIdentity::root(
        ResolutionId::new(resolution).expect("non-zero resolution id"),
        CorrelationId::new(7032).expect("non-zero correlation id"),
    );
    let (receipt, readout) = resolve_dagger_action(
        &catalog,
        &mut state,
        identity,
        ResolutionMode::Apply,
        DaggerIntent {
            action: "ember-lance".to_string(),
            actor: "caster".to_string(),
            target: "target".to_string(),
            origin,
        },
        vec![DaggerEvidence {
            id: "spell-hit".to_string(),
            value: 80,
        }],
    );
    assert!(receipt.succeeded(), "authored action must resolve");
    (
        state,
        serde_json::to_string_pretty(&readout).expect("serialize resolution readout"),
    )
}

fn main() {
    let (player_state, player_readout) = resolve(DaggerIntentOrigin::Player, 1);
    let (ai_state, _) = resolve(DaggerIntentOrigin::Ai, 2);
    assert_eq!(
        player_state, ai_state,
        "player and AI intents must share the same policy path"
    );
    println!("{player_readout}");
}
