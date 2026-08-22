mod compile;
mod composed;
mod eval;
mod loot;
mod mechanics;
mod model;
mod policy;
mod progression;

pub use compile::compile_gameplay_package;
pub use eval::{
    action_dynamic_roll_evidence, action_roll_evidence, apply_standard_inventory_operation,
    definition_base_stats, equipped_weapon, evaluate_derived_rule, evaluate_expr,
    required_roll_evidence, restore_actor_tracks, set_actor_track, spawn_actor, spend_actor_track,
    track_maximum, unarmed_damage_range, ActorEquipment, ActorExprValues, DaggerDynamicRoll,
    DaggerStandardInventoryError, ExprContext,
};
pub use loot::{bind_actor_loot, generate_loot, loot_roll_evidence, spawn_container};
pub use mechanics::{compile_mechanics_catalog, track_max_stat_id};
pub use model::*;
pub use policy::{resolve_dagger_action, DaggerResolutionPolicy, DaggerTransaction};
pub use progression::{
    award_kill_progression, evaluate_derived_rule_live, kill_level_gains, live_stat_base,
    reset_actor_progression, set_actor_stat_base, xp_level_divisor, HP_PER_LEVEL_RULE_ID,
    HP_ROLL_EVIDENCE_ID, LEVEL_STAT_ID, PLAYER_SPAWN_LEVEL, PLAYER_SPAWN_XP, XP_LEVEL_RULE_ID,
    XP_STAT_ID,
};
