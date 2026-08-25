//! Typed bridge from Dagger's canonical entity and collision authority to the
//! Engine-owned `engine.runtime.observe-pairs` mechanism.
//!
//! These components are inert runtime facts. They do not schedule, mutate,
//! or interpret combat consequences; the Product Kernel retains that meaning
//! when it stages the plan's one returned mutation batch.

use rusty_engine::core_math::Vec3;
use rusty_engine::entity_state::EntityComponent;
use rusty_engine::runtime_standard_capabilities::{
    ObservePairsObserver, ObservePairsObserverFacts, ObservePairsTarget,
};

pub const DAGGER_PLAYER_OBSERVER_ROLE: &str = "dagger.player-observer";
pub const DAGGER_COMBAT_TARGET_ROLE: &str = "dagger.combat-target";

#[derive(Debug, Clone, Copy, PartialEq)]
pub struct DaggerPlayerObserver {
    facts: ObservePairsObserverFacts,
}

impl DaggerPlayerObserver {
    pub const fn new(facts: ObservePairsObserverFacts) -> Self {
        Self { facts }
    }

    pub const fn default_facts() -> ObservePairsObserverFacts {
        ObservePairsObserverFacts {
            local_origin: Vec3::ZERO,
            local_forward: Vec3::new(0.0, 0.0, -1.0),
            maximum_distance: 12.0,
            minimum_facing_cosine: 0.64,
            evidence: 1.0,
        }
    }
}

impl EntityComponent for DaggerPlayerObserver {}

impl ObservePairsObserver for DaggerPlayerObserver {
    fn facts(&self) -> ObservePairsObserverFacts {
        self.facts
    }
}

#[derive(Debug, Clone, Copy, PartialEq)]
pub struct DaggerCombatTarget {
    local_center: Vec3,
}

impl DaggerCombatTarget {
    pub const fn new(local_center: Vec3) -> Self {
        Self { local_center }
    }
}

impl EntityComponent for DaggerCombatTarget {}

impl ObservePairsTarget for DaggerCombatTarget {
    fn local_center(&self) -> Vec3 {
        self.local_center
    }
}
