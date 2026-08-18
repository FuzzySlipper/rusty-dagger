mod compile;
mod eval;
mod loot;
mod mechanics;
mod model;
mod policy;

pub use compile::compile_gameplay_package;
pub use eval::{
    action_dynamic_roll_evidence, action_roll_evidence, definition_base_stats, equipped_weapon,
    evaluate_derived_rule, evaluate_expr, required_roll_evidence, restore_actor_tracks,
    set_actor_track, spawn_actor, spend_actor_track, track_maximum, unarmed_damage_range,
    ActorEquipment, ActorExprValues, DaggerDynamicRoll, ExprContext,
};
pub use loot::{bind_actor_loot, generate_loot, loot_roll_evidence, spawn_container};
pub use mechanics::{compile_mechanics_catalog, track_max_stat_id};
pub use model::*;
pub use policy::{resolve_dagger_action, DaggerResolutionPolicy, DaggerTransaction};
