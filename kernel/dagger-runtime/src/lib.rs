//! Rust-owned runtime seam for the imported Privateer's Hold project.
//!
//! This crate deliberately owns only Daggerfall-specific admission and player
//! control. It consumes generic Rusty Engine spatial/entity services and does
//! not depend on the loading-bay demo or a sibling checkout.

#![forbid(unsafe_code)]

pub mod animation;
pub mod combat_assets;
pub mod directional;
mod mobile;
pub mod navgrid;
pub mod observe_pairs;
pub mod patrol;
mod player;
mod project;
mod runtime;
pub mod settings;

pub use animation::{
    AnimationService, AttackSequence, EnemyAnimationLayout, EnemyAnimationStateLayout,
    EnemyAnimationUpdate, FrameUpdate, SpriteEntry, SpriteKind,
};
pub use combat_assets::{
    AudioAsset, CombatAssetCatalog, CombatFrame, EffectAsset, WeaponAnimation, WeaponAsset,
};
pub use directional::evaluate_directional;
pub use navgrid::{derive_nav_grid, ground_spawn, level_of, NavCell, NavGrid, SpawnGrounding};
pub use observe_pairs::{
    DaggerCombatTarget, DaggerPlayerObserver, DAGGER_COMBAT_TARGET_ROLE,
    DAGGER_PLAYER_OBSERVER_ROLE,
};
pub use patrol::{PatrolGrid, PatrolService, PositionUpdate};
pub use settings::{
    SettingDescriptor, SettingKind, SettingReadout, SettingUpdate, SettingValue, SettingsError,
    SettingsReadout, SettingsReconcileRequest, SettingsRegistry, SettingsState,
    SettingsUpdateReceipt, SettingsUpdateRequest, DEBUG_FAILED_INVENTORY_DROP_MESSAGES_ID,
    SETTINGS_SCHEMA_VERSION,
};

pub use player::{
    PlayerControlFact, PlayerControlReceipt, PlayerControllerConfig, PlayerControllerState,
    PlayerFrameReceipt, PlayerInputBindings, ResolvedPlayerAction, ResolvedPlayerFrame,
    MAX_PLAYER_FRAME_LOOK_UNITS, MAX_PLAYER_FRAME_STEP_SECONDS, MAX_PLAYER_LOOK_DEGREES_PER_UNIT,
    MAX_PLAYER_SPEED_UNITS_PER_SECOND, MAX_PLAYER_STEP_UP_UNITS,
};
pub use project::{AdmittedProject, ProjectAdmissionError};
pub use runtime::{
    ActorAttributeReadout, ActorGameplayReadout, CombatAttemptRecord, CombatRecord,
    ContentEntityReadout, ContentError, ContentLiveReadout, DaggerRuntime,
    EnemyPresentationReadout, EnemyReferenceReadout, GameplayPackageReadout, InventoryGridOccupant,
    InventoryGridReadout, InventoryGridSlotReadout, InventoryItemReadout, InventoryStackReadout,
    LiveActorResources, LootContainerReadout, MeleePresentationPhase, MeleePresentationReadout,
    NamedEncounterReadout, PlayerInventoryReadout, ProductNoticeKind, ProductNoticeRecord,
    ProductReadout, ProgressionReadout, RuntimeError, LOOT_INTERACT_REACH, MELEE_ACTION_ID,
    MELEE_ANTICIPATION_SECONDS, MELEE_CONTACT_SECONDS, MELEE_RECOVERY_SECONDS,
    MELEE_REJECTION_SECONDS, PRODUCT_NOTICE_HISTORY_LIMIT,
};
