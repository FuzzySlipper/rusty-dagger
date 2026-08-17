//! Production-path proof for the authored Dagger gameplay package: admits the
//! committed package, spawns actors through the derived-track authority, and
//! resolves the same authored melee action for player and AI origins.

use dagger_rpg::{
    compile_gameplay_package, initial_actor_state, resolve_dagger_action, DaggerEvidence,
    DaggerGameplayState, DaggerIntent, DaggerIntentOrigin,
};
use rusty_engine::gameplay_resolution::{
    CorrelationId, ResolutionId, ResolutionIdentity, ResolutionMode,
};

const PACKAGE: &[u8] = include_bytes!("../../../../data/gameplay/dagger-core.package.json");

fn state() -> DaggerGameplayState {
    let catalog = compile_gameplay_package(PACKAGE).expect("admit authored gameplay package");
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

fn resolve(origin: DaggerIntentOrigin, resolution: u64) -> (DaggerGameplayState, String) {
    let catalog = compile_gameplay_package(PACKAGE).expect("admit authored gameplay package");
    let mut state = state();
    let identity = ResolutionIdentity::root(
        ResolutionId::new(resolution).expect("non-zero resolution id"),
        CorrelationId::new(7044).expect("non-zero correlation id"),
    );
    let (receipt, readout) = resolve_dagger_action(
        &catalog,
        &mut state,
        identity,
        ResolutionMode::Apply,
        DaggerIntent {
            action: "melee-attack".to_string(),
            actor: "player".to_string(),
            target: "rat-2007".to_string(),
            origin,
        },
        vec![
            DaggerEvidence {
                id: "melee-attack.d100".to_string(),
                value: 25,
            },
            DaggerEvidence {
                id: "weapon-damage.iron-longsword".to_string(),
                value: 8,
            },
        ],
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
