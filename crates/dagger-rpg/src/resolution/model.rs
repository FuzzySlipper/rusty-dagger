use std::collections::{BTreeMap, BTreeSet};

use rusty_engine::core_ids::EntityId;
use rusty_engine::entity_state::EntityState;
use rusty_engine::gameplay_mechanics::{
    gameplay_component_registry, MechanicsCatalog, TrackId, TracksComponent,
};
use rusty_engine::gameplay_resolution::{
    AttemptStatus, CommitStatus, Program, ResolutionMode, ResolutionReceipt,
};
use serde::{Deserialize, Serialize};

pub const DAGGER_GAMEPLAY_SCHEMA_VERSION: u32 = 1;
pub const MAX_DAGGER_ACTIONS: usize = 256;
pub const MAX_DAGGER_ITEMS: usize = 256;
pub const MAX_DAGGER_RULES: usize = 256;
pub const MAX_DAGGER_ACTORS: usize = 256;
pub const MAX_DAGGER_ENCOUNTERS: usize = 64;
pub const MAX_DAGGER_DERIVED: usize = 256;
pub const MAX_DAGGER_ENCOUNTER_MEMBERS: usize = 256;
pub const MAX_DAGGER_DECLARED_IDS: usize = 128;
pub const MAX_DAGGER_PROGRAM_NODES: usize = 4_096;
pub const MAX_DAGGER_PROGRAM_DEPTH: u16 = 64;
pub const MAX_DAGGER_EXPR_NODES: usize = 1_024;
pub const MAX_DAGGER_EXPR_DEPTH: u16 = 32;
pub const MAX_DAGGER_ID_BYTES: usize = 96;
pub const MAX_DAGGER_TEXT_BYTES: usize = 512;
pub const MAX_BEHAVIOR_VALUE: f32 = 1_000.0;

/// Integer payload field crossing the schema-2 binary64 wire. The canonical
/// spelling writes every number as binary64 (`5.0`); this newtype accepts
/// integral binary64 values and rejects non-integral ones, so exact
/// formula data stays integer-backed without a second float encoding.
#[derive(Debug, Clone, Copy, PartialEq, Eq, PartialOrd, Ord, Serialize)]
#[serde(transparent)]
pub struct Binary64I64(pub i64);

impl<'de> Deserialize<'de> for Binary64I64 {
    fn deserialize<D>(deserializer: D) -> Result<Self, D::Error>
    where
        D: serde::Deserializer<'de>,
    {
        let number = serde_json::Number::deserialize(deserializer)?;
        if let Some(value) = number.as_i64() {
            return Ok(Self(value));
        }
        let float = number
            .as_f64()
            .ok_or_else(|| serde::de::Error::custom("expected a number"))?;
        if float.fract() == 0.0 && float >= i64::MIN as f64 && float <= i64::MAX as f64 {
            Ok(Self(float as i64))
        } else {
            Err(serde::de::Error::custom(format!(
                "expected an integral number, got {float}"
            )))
        }
    }
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct AuthoredGameplayPayload {
    pub schema_version: Binary64I64,
    pub stats: AuthoredStatsSection,
    pub actors: Vec<AuthoredActorDefinition>,
    pub actions: Vec<AuthoredActionDefinition>,
    pub items: Vec<AuthoredItemDefinition>,
    pub rules: Vec<AuthoredRuleDefinition>,
    pub encounters: Vec<AuthoredEncounterDefinition>,
    #[serde(default)]
    pub derived: Vec<AuthoredDerivedRule>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub equipment: Option<AuthoredEquipmentSection>,
}

/// Capacity metrics and equipment slots the package's items bind against.
/// Optional and additive under payload schema 1; required by admission once
/// any item is equippable or weighs anything.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct AuthoredEquipmentSection {
    pub capacity_metrics: Vec<String>,
    pub slots: Vec<AuthoredEquipmentSlot>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct AuthoredEquipmentSlot {
    pub id: String,
    pub allowed_classifications: Vec<String>,
}

/// One inventory entry in an actor's spawn loadout.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct AuthoredLoadoutEntry {
    pub item: String,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub quantity: Option<Binary64I64>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub equip_slot: Option<String>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct AuthoredDerivedRule {
    pub id: String,
    pub expr: AuthoredExpr,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct AuthoredStatsSection {
    pub attributes: Vec<String>,
    pub skills: Vec<String>,
    pub tracks: Vec<String>,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct AuthoredActorDefinition {
    pub id: String,
    pub kind: AuthoredActorKind,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub mobile_id: Option<Binary64I64>,
    pub stats: BTreeMap<String, Binary64I64>,
    pub skills: BTreeMap<String, Binary64I64>,
    pub armor_value: Binary64I64,
    pub tracks: Vec<AuthoredTrackDefinition>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub move_speed: Option<f64>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub behavior: Option<AuthoredBehaviorDefinition>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub level: Option<Binary64I64>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub weight: Option<Binary64I64>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub min_metal_to_hit: Option<String>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub team: Option<String>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub loot_table_key: Option<String>,
    #[serde(default, skip_serializing_if = "Vec::is_empty")]
    pub attacks: Vec<AuthoredDamageRange>,
    #[serde(default, skip_serializing_if = "Vec::is_empty")]
    pub inventory: Vec<AuthoredLoadoutEntry>,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum AuthoredActorKind {
    #[serde(rename = "player")]
    Player,
    #[serde(rename = "monster")]
    Monster,
    #[serde(rename = "enemy-class")]
    EnemyClass,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct AuthoredTrackDefinition {
    pub id: String,
    pub max: AuthoredExpr,
}

/// Behavior tuning values are schema-2 binary64 numbers; the compiled
/// catalog carries f32 (converted at one named boundary in the compiler).
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct AuthoredBehaviorDefinition {
    pub detection_range: f64,
    pub patrol_speed: f64,
    pub chase_speed: f64,
    pub attack_range: f64,
    pub attack_cooldown_seconds: f64,
    pub action: String,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(tag = "kind", rename_all = "camelCase", deny_unknown_fields)]
pub enum AuthoredExpr {
    Const {
        value: Binary64I64,
    },
    Stat {
        subject: AuthoredSubject,
        id: String,
    },
    Skill {
        subject: AuthoredSubject,
        id: String,
    },
    Armor {
        subject: AuthoredSubject,
    },
    Evidence {
        id: String,
    },
    Dice {
        id: String,
        min: Binary64I64,
        max: Binary64I64,
    },
    WeaponDice {
        item: String,
    },
    Track {
        subject: AuthoredSubject,
        id: String,
    },
    TrackMax {
        subject: AuthoredSubject,
        id: String,
    },
    PowMilli {
        base: Box<Self>,
        exponent: Box<Self>,
    },
    Add {
        terms: Vec<Self>,
    },
    Sub {
        left: Box<Self>,
        right: Box<Self>,
    },
    Mul {
        terms: Vec<Self>,
    },
    DivFloor {
        left: Box<Self>,
        right: Box<Self>,
    },
    DivTrunc {
        left: Box<Self>,
        right: Box<Self>,
    },
    Min {
        terms: Vec<Self>,
    },
    Max {
        terms: Vec<Self>,
    },
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub enum AuthoredSubject {
    Actor,
    Target,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(tag = "kind", rename_all = "camelCase", deny_unknown_fields)]
pub enum AuthoredPredicate {
    Cmp {
        op: AuthoredCmpOp,
        left: AuthoredExpr,
        right: AuthoredExpr,
    },
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum AuthoredCmpOp {
    #[serde(rename = "lt")]
    Lt,
    #[serde(rename = "lte")]
    Lte,
    #[serde(rename = "eq")]
    Eq,
    #[serde(rename = "gte")]
    Gte,
    #[serde(rename = "gt")]
    Gt,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(tag = "kind", rename_all = "camelCase", deny_unknown_fields)]
pub enum AuthoredSelector {
    IntentTarget,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(tag = "kind", rename_all = "camelCase", deny_unknown_fields)]
pub enum AuthoredOperation {
    SpendTrack {
        track: String,
        amount: AuthoredExpr,
    },
    Damage {
        target: AuthoredSelector,
        amount: AuthoredExpr,
    },
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(tag = "kind", rename_all = "camelCase", deny_unknown_fields)]
pub enum AuthoredProgram {
    Sequence {
        steps: Vec<Self>,
    },
    When {
        predicate: AuthoredPredicate,
        #[serde(rename = "thenProgram")]
        then_program: Box<Self>,
        #[serde(default, rename = "otherwiseProgram")]
        otherwise_program: Option<Box<Self>>,
    },
    Operation {
        operation: AuthoredOperation,
    },
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct AuthoredActionDefinition {
    pub id: String,
    pub tags: Vec<String>,
    pub program: AuthoredProgram,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub reach: Option<f64>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub cooldown_seconds: Option<f64>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct AuthoredItemDefinition {
    pub id: String,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub weapon: Option<AuthoredWeaponDefinition>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub armor: Option<AuthoredArmorDefinition>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub shield: Option<AuthoredShieldDefinition>,
    /// Weight in the classic quarter-kg unit.
    pub weight_units: Binary64I64,
    /// Value in gold pieces.
    pub value: Binary64I64,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct AuthoredWeaponDefinition {
    pub damage: AuthoredDamageRange,
    pub material: String,
    pub skill: String,
    pub hands: AuthoredWeaponHands,
}

/// Weapon handedness (donor `ItemEquipTable.GetItemHands`).
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum AuthoredWeaponHands {
    #[serde(rename = "either")]
    Either,
    #[serde(rename = "both")]
    Both,
    #[serde(rename = "leftOnly")]
    LeftOnly,
}

/// Armor is valued per material, not per piece; the piece selects the
/// `armor-<piece>` classification and therefore its legal slots.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct AuthoredArmorDefinition {
    pub material: String,
    pub piece: String,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct AuthoredShieldDefinition {
    pub value: Binary64I64,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct AuthoredDamageRange {
    pub min: Binary64I64,
    pub max: Binary64I64,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(tag = "kind", rename_all = "camelCase", deny_unknown_fields)]
pub enum AuthoredRuleDefinition {
    RejectTagWhileCondition {
        id: String,
        tag: String,
        condition: String,
    },
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct AuthoredEncounterDefinition {
    pub id: String,
    pub name: String,
    pub objective: String,
    pub route_code: String,
    pub member_entity_ids: Vec<Binary64I64>,
}

#[derive(Debug, Clone)]
pub struct DaggerGameplayCatalog {
    fingerprint: String,
    stats: DaggerStatsSection,
    actors: BTreeMap<String, DaggerActorDefinition>,
    actions: BTreeMap<String, DaggerActionDefinition>,
    items: BTreeMap<String, DaggerItemDefinition>,
    rules: Vec<DaggerRuleDefinition>,
    encounters: BTreeMap<String, DaggerEncounterDefinition>,
    derived: BTreeMap<String, DaggerDerivedRule>,
    equipment: DaggerEquipmentSection,
    mechanics: MechanicsCatalog,
}

impl DaggerGameplayCatalog {
    pub fn fingerprint(&self) -> &str {
        &self.fingerprint
    }

    pub fn stats(&self) -> &DaggerStatsSection {
        &self.stats
    }

    pub fn actors(&self) -> &BTreeMap<String, DaggerActorDefinition> {
        &self.actors
    }

    pub fn actions(&self) -> &BTreeMap<String, DaggerActionDefinition> {
        &self.actions
    }

    pub fn items(&self) -> &BTreeMap<String, DaggerItemDefinition> {
        &self.items
    }

    pub fn rules(&self) -> &[DaggerRuleDefinition] {
        &self.rules
    }

    pub fn encounters(&self) -> &BTreeMap<String, DaggerEncounterDefinition> {
        &self.encounters
    }

    pub fn derived(&self) -> &BTreeMap<String, DaggerDerivedRule> {
        &self.derived
    }

    /// The package's equipment vocabulary (capacity metrics, slot legality).
    pub fn equipment(&self) -> &DaggerEquipmentSection {
        &self.equipment
    }

    /// The Engine mechanics catalog admitted from this package's declared
    /// stats and tracks. All durable stat/track state resolves through it.
    pub fn mechanics(&self) -> &MechanicsCatalog {
        &self.mechanics
    }

    #[allow(clippy::too_many_arguments)]
    pub(crate) fn new(
        fingerprint: String,
        stats: DaggerStatsSection,
        actors: BTreeMap<String, DaggerActorDefinition>,
        actions: BTreeMap<String, DaggerActionDefinition>,
        items: BTreeMap<String, DaggerItemDefinition>,
        rules: Vec<DaggerRuleDefinition>,
        encounters: BTreeMap<String, DaggerEncounterDefinition>,
        derived: BTreeMap<String, DaggerDerivedRule>,
        equipment: DaggerEquipmentSection,
        mechanics: MechanicsCatalog,
    ) -> Self {
        Self {
            fingerprint,
            stats,
            actors,
            actions,
            items,
            rules,
            encounters,
            derived,
            equipment,
            mechanics,
        }
    }
}

#[derive(Debug, Clone, PartialEq, Eq, Default)]
pub struct DaggerStatsSection {
    pub attributes: BTreeSet<String>,
    pub skills: BTreeSet<String>,
    pub tracks: BTreeSet<String>,
}

pub type DaggerProgram = Program<DaggerPredicate, DaggerOperation>;

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum DaggerExpr {
    Const {
        value: i64,
    },
    Stat {
        subject: DaggerSubject,
        id: String,
    },
    Skill {
        subject: DaggerSubject,
        id: String,
    },
    Armor {
        subject: DaggerSubject,
    },
    Evidence {
        id: String,
    },
    Dice {
        id: String,
        min: i64,
        max: i64,
    },
    WeaponDice {
        item: String,
    },
    Track {
        subject: DaggerSubject,
        id: String,
    },
    TrackMax {
        subject: DaggerSubject,
        id: String,
    },
    PowMilli {
        base: Box<Self>,
        exponent: Box<Self>,
    },
    Add {
        terms: Vec<Self>,
    },
    Sub {
        left: Box<Self>,
        right: Box<Self>,
    },
    Mul {
        terms: Vec<Self>,
    },
    DivFloor {
        left: Box<Self>,
        right: Box<Self>,
    },
    DivTrunc {
        left: Box<Self>,
        right: Box<Self>,
    },
    Min {
        terms: Vec<Self>,
    },
    Max {
        terms: Vec<Self>,
    },
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum DaggerSubject {
    Actor,
    Target,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum DaggerCmpOp {
    Lt,
    Lte,
    Eq,
    Gte,
    Gt,
}

impl DaggerCmpOp {
    pub fn compare(self, left: i64, right: i64) -> bool {
        match self {
            Self::Lt => left < right,
            Self::Lte => left <= right,
            Self::Eq => left == right,
            Self::Gte => left >= right,
            Self::Gt => left > right,
        }
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum DaggerActorKind {
    Player,
    Monster,
    EnemyClass,
}

#[derive(Debug, Clone, PartialEq)]
pub struct DaggerActorDefinition {
    pub id: String,
    pub kind: DaggerActorKind,
    pub mobile_id: Option<u8>,
    pub stats: BTreeMap<String, i64>,
    pub skills: BTreeMap<String, i64>,
    pub armor_value: i64,
    pub tracks: Vec<DaggerTrackDefinition>,
    pub move_speed: Option<f32>,
    pub behavior: Option<DaggerBehaviorDefinition>,
    pub level: Option<i64>,
    pub weight: Option<i64>,
    pub min_metal_to_hit: Option<String>,
    pub team: Option<String>,
    pub loot_table_key: Option<String>,
    /// Classic melee attack damage ranges (1-3 sub-attacks per swing).
    pub attacks: Vec<DaggerDamageRange>,
    /// Spawn loadout bound into upstream inventory/equipment components.
    pub inventory: Vec<DaggerLoadoutEntry>,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct DaggerDamageRange {
    pub min: i64,
    pub max: i64,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct DaggerDerivedRule {
    pub id: String,
    pub expr: DaggerExpr,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct DaggerTrackDefinition {
    pub id: String,
    pub max: DaggerExpr,
}

#[derive(Debug, Clone, PartialEq)]
pub struct DaggerBehaviorDefinition {
    pub detection_range: f32,
    pub patrol_speed: f32,
    pub chase_speed: f32,
    pub attack_range: f32,
    pub attack_cooldown_seconds: f32,
    pub action: String,
}

#[derive(Debug, Clone, PartialEq)]
pub struct DaggerActionDefinition {
    pub id: String,
    pub tags: BTreeSet<String>,
    pub program: DaggerProgram,
    pub reach: Option<f32>,
    pub cooldown_seconds: Option<f32>,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum DaggerPredicate {
    Cmp {
        op: DaggerCmpOp,
        left: DaggerExpr,
        right: DaggerExpr,
    },
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum DaggerSelector {
    IntentTarget,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum DaggerOperation {
    SpendTrack {
        track: String,
        amount: DaggerExpr,
    },
    Damage {
        target: DaggerSelector,
        amount: DaggerExpr,
    },
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct DaggerItemDefinition {
    pub id: String,
    pub weapon: Option<DaggerWeaponDefinition>,
    /// Armor value is compiled here from the per-material classic table.
    pub armor: Option<DaggerArmorDefinition>,
    pub shield: Option<DaggerShieldDefinition>,
    /// Weight in the classic quarter-kg unit.
    pub weight_units: u64,
    /// Value in gold pieces.
    pub value: u64,
    /// Fungible items (gold, arrows) are stacks; equippable items are unique
    /// entities. Derived from the absence of weapon/armor/shield blocks.
    pub fungible: bool,
    /// Upstream mapping input: equipment classifications for slot legality.
    pub classifications: Vec<String>,
    /// Upstream mapping input: equipment policy for equippable items.
    pub equipment: Option<DaggerItemEquipment>,
}

impl DaggerItemDefinition {
    pub fn equippable(&self) -> bool {
        self.equipment.is_some()
    }
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct DaggerWeaponDefinition {
    pub damage_min: i64,
    pub damage_max: i64,
    pub material: String,
    pub skill: String,
    pub hands: DaggerWeaponHands,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum DaggerWeaponHands {
    Either,
    Both,
    LeftOnly,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct DaggerArmorDefinition {
    pub material: String,
    pub piece: String,
    /// Per-material armor value (classic inverted convention: higher is
    /// easier to hit), compiled from the donor table.
    pub value: i64,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct DaggerShieldDefinition {
    pub value: i64,
}

/// Equipment policy an equippable item carries into the upstream catalog.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct DaggerItemEquipment {
    pub required_slots: u16,
    pub exclusive_group: Option<String>,
}

/// The package's equipment vocabulary: capacity metrics and slot legality.
#[derive(Debug, Clone, PartialEq, Eq, Default)]
pub struct DaggerEquipmentSection {
    pub capacity_metrics: Vec<String>,
    pub slots: Vec<DaggerEquipmentSlotDefinition>,
}

impl DaggerEquipmentSection {
    pub fn slot(&self, id: &str) -> Option<&DaggerEquipmentSlotDefinition> {
        self.slots.iter().find(|slot| slot.id == id)
    }
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct DaggerEquipmentSlotDefinition {
    pub id: String,
    pub allowed_classifications: Vec<String>,
}

/// One compiled spawn-loadout entry.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct DaggerLoadoutEntry {
    pub item: String,
    pub quantity: u64,
    pub equip_slot: Option<String>,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum DaggerRuleDefinition {
    RejectTagWhileCondition {
        id: String,
        tag: String,
        condition: String,
    },
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct DaggerEncounterDefinition {
    pub id: String,
    pub name: String,
    pub objective: String,
    pub route_code: String,
    pub member_entity_ids: Vec<u64>,
}

/// Mechanics-backed live gameplay state: an entity-state store holding the
/// Engine's stat/track and inventory/equipment components, plus Dagger-owned
/// actor bindings (which catalog definition each actor embodies, conditions).
#[derive(Debug, Clone)]
pub struct DaggerGameplayState {
    entities: EntityState,
    actors: BTreeMap<String, DaggerActorState>,
    next_entity: u64,
}

impl Default for DaggerGameplayState {
    fn default() -> Self {
        Self {
            entities: EntityState::from_definitions_with_registry(
                gameplay_component_registry().expect("gameplay component registry is valid"),
                [],
            )
            .expect("gameplay component registry admits an empty state"),
            actors: BTreeMap::new(),
            next_entity: 1,
        }
    }
}

impl DaggerGameplayState {
    pub fn insert_actor(&mut self, id: impl Into<String>, actor: DaggerActorState) {
        self.actors.insert(id.into(), actor);
    }

    pub fn actor(&self, id: &str) -> Option<&DaggerActorState> {
        self.actors.get(id)
    }

    pub fn actors(&self) -> &BTreeMap<String, DaggerActorState> {
        &self.actors
    }

    pub fn entities(&self) -> &EntityState {
        &self.entities
    }

    /// Mutable access to the entity store for driving upstream mechanics
    /// services (equipment, inventory) against spawned actors.
    pub fn entities_mut(&mut self) -> &mut EntityState {
        &mut self.entities
    }

    pub(crate) fn allocate_entity(&mut self) -> EntityId {
        let raw = self.next_entity;
        self.next_entity = self.next_entity.saturating_add(1);
        EntityId::new(raw)
    }

    /// Current value of one actor's track from the mechanics component.
    pub fn track_value(&self, id: &str, track: &str) -> Option<i64> {
        let binding = self.actors.get(id)?;
        let component = self
            .entities
            .component::<TracksComponent>(binding.entity())
            .ok()??;
        let track_id = TrackId::parse(track).ok()?;
        component.current(&track_id).map(|value| value.get())
    }
}

/// Live binding of one actor instance: its entity (which carries the
/// mechanics stat/track components), which catalog definition it embodies,
/// and Dagger-owned condition sets. Conditions stay Dagger-owned until spell
/// effects introduce the Engine's active-effects model; inventory and
/// equipment live on the entity as upstream Inventory/Equipment components
/// attached at spawn.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct DaggerActorState {
    entity: EntityId,
    definition: String,
    conditions: BTreeSet<String>,
}

impl DaggerActorState {
    pub fn new(entity: EntityId, definition: impl Into<String>) -> Self {
        Self {
            entity,
            definition: definition.into(),
            conditions: BTreeSet::new(),
        }
    }

    pub const fn entity(&self) -> EntityId {
        self.entity
    }

    pub fn definition(&self) -> &str {
        &self.definition
    }

    pub fn conditions(&self) -> &BTreeSet<String> {
        &self.conditions
    }

    pub fn add_condition(&mut self, condition: impl Into<String>) {
        self.conditions.insert(condition.into());
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub enum DaggerIntentOrigin {
    Player,
    Ai,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct DaggerIntent {
    pub action: String,
    pub actor: String,
    pub target: String,
    pub origin: DaggerIntentOrigin,
}

#[derive(Debug, Clone, PartialEq)]
pub struct DaggerAdmittedIntent {
    pub action: DaggerActionDefinition,
    pub actor: String,
    pub target: String,
    pub origin: DaggerIntentOrigin,
}

/// Materialized per-actor facts gathered for one resolution: current track
/// values read from the mechanics components plus the Dagger-owned sets.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct DaggerActorFacts {
    pub definition: String,
    pub tracks: BTreeMap<String, i64>,
    pub conditions: BTreeSet<String>,
}

impl DaggerActorFacts {
    pub fn track(&self, id: &str) -> Option<i64> {
        self.tracks.get(id).copied()
    }
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct DaggerFacts {
    pub actor: DaggerActorFacts,
    pub target: DaggerActorFacts,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct DaggerEvidence {
    pub id: String,
    pub value: i64,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(tag = "kind", rename_all = "camelCase")]
pub enum DaggerEffect {
    SpendTrack {
        actor: String,
        track: String,
        amount: i64,
    },
    Damage {
        target: String,
        amount: i64,
    },
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(tag = "kind", rename_all = "camelCase")]
pub enum DaggerEvent {
    TrackSpent {
        actor: String,
        track: String,
        amount: i64,
    },
    DamageApplied {
        target: String,
        amount: i64,
    },
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(tag = "kind", rename_all = "camelCase")]
pub enum DaggerTraceDetail {
    Definition { id: String },
    Facts { actor: String, target: String },
    Decision { reason: String },
    Source { id: String },
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum DaggerRejection {
    UnknownAction(String),
    UnknownActor(String),
    UnknownTarget(String),
    Rule {
        rule: String,
        reason: String,
    },
    MissingEvidence(String),
    RollOutOfBounds {
        id: String,
        value: i64,
        min: i64,
        max: i64,
    },
    MissingValue(String),
    InvalidExpression(String),
    InsufficientTrack {
        track: String,
        available: i64,
        required: i64,
    },
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum DaggerFault {
    InvalidProgram(String),
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct DaggerSuspension {
    pub token: Vec<u8>,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum DaggerTransactionError {
    UnknownActor(String),
    Mechanics(String),
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum DaggerGameplayError {
    Package(String),
    WrongPackage {
        domain: String,
        package: String,
    },
    Payload(String),
    UnsupportedSchema {
        actual: u32,
        expected: u32,
    },
    Quota {
        field: &'static str,
        actual: usize,
        maximum: usize,
    },
    DuplicateId {
        kind: &'static str,
        id: String,
    },
    InvalidId {
        path: String,
        value: String,
    },
    InvalidValue {
        path: String,
        reason: String,
    },
    InvalidState(String),
}

impl std::fmt::Display for DaggerGameplayError {
    fn fmt(&self, formatter: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        write!(formatter, "Dagger gameplay rejected: {self:?}")
    }
}

impl std::error::Error for DaggerGameplayError {}

pub type DaggerResolutionReceipt = ResolutionReceipt<
    DaggerIntent,
    DaggerAdmittedIntent,
    DaggerFacts,
    DaggerEvidence,
    DaggerEffect,
    DaggerEvent,
    DaggerRejection,
    DaggerFault,
    DaggerSuspension,
    DaggerTraceDetail,
    DaggerTransactionError,
>;

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct DaggerResolutionReadout {
    pub package_fingerprint: String,
    pub resolution_id: u64,
    pub correlation_id: u64,
    pub mode: String,
    pub status: String,
    pub commit: String,
    pub effects: Vec<DaggerEffect>,
    pub events: Vec<DaggerEvent>,
    pub trace: Vec<DaggerTraceReadout>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct DaggerTraceReadout {
    pub resolution_id: u64,
    pub phase: String,
    pub kind: String,
    pub detail: Option<DaggerTraceDetail>,
}

impl DaggerResolutionReadout {
    pub fn from_receipt(fingerprint: &str, receipt: &DaggerResolutionReceipt) -> Self {
        Self {
            package_fingerprint: fingerprint.to_string(),
            resolution_id: receipt.attempt().identity().resolution().get(),
            correlation_id: receipt.attempt().identity().correlation().get(),
            mode: match receipt.mode() {
                ResolutionMode::Preview => "preview",
                ResolutionMode::Apply => "apply",
            }
            .to_string(),
            status: format_attempt_status(receipt.attempt().status()),
            commit: format_commit_status(receipt.commit()),
            effects: receipt.effects().to_vec(),
            events: receipt.events().to_vec(),
            trace: receipt
                .attempt()
                .trace()
                .iter()
                .map(|record| DaggerTraceReadout {
                    resolution_id: record.identity().resolution().get(),
                    phase: format!("{:?}", record.phase()),
                    kind: format!("{:?}", record.kind()),
                    detail: record.detail().cloned(),
                })
                .collect(),
        }
    }
}

fn format_attempt_status(
    status: &AttemptStatus<DaggerRejection, DaggerFault, DaggerSuspension>,
) -> String {
    match status {
        AttemptStatus::Planned => "planned".to_string(),
        AttemptStatus::Rejected(reason) => format!("rejected: {reason:?}"),
        AttemptStatus::Suspended(suspension) => format!("suspended: {suspension:?}"),
        AttemptStatus::Faulted(fault) => format!("faulted: {fault:?}"),
        AttemptStatus::LimitExceeded(error) => format!("limit: {error}"),
        AttemptStatus::ChildFailed => "child failed".to_string(),
    }
}

fn format_commit_status(status: &CommitStatus<DaggerTransactionError>) -> String {
    match status {
        CommitStatus::NotAttempted => "not attempted".to_string(),
        CommitStatus::Previewed => "previewed".to_string(),
        CommitStatus::Applied => "applied".to_string(),
        CommitStatus::Failed(error) => format!("failed: {error:?}"),
    }
}
