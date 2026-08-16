use std::collections::{BTreeMap, BTreeSet};

use rusty_engine::gameplay_resolution::{
    AttemptStatus, CommitStatus, Program, ResolutionMode, ResolutionReceipt,
};
use serde::{Deserialize, Serialize};

pub const DAGGER_GAMEPLAY_SCHEMA_VERSION: u32 = 1;
pub const MAX_DAGGER_ACTIONS: usize = 256;
pub const MAX_DAGGER_ITEMS: usize = 256;
pub const MAX_DAGGER_RULES: usize = 256;
pub const MAX_DAGGER_PROGRAM_NODES: usize = 4_096;
pub const MAX_DAGGER_PROGRAM_DEPTH: u16 = 64;
pub const MAX_DAGGER_ID_BYTES: usize = 96;

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct AuthoredGameplayPayload {
    pub schema_version: u32,
    pub actions: Vec<AuthoredActionDefinition>,
    pub items: Vec<AuthoredItemDefinition>,
    pub rules: Vec<AuthoredRuleDefinition>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct AuthoredActionDefinition {
    pub id: String,
    pub tags: Vec<String>,
    pub program: AuthoredProgram,
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

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(tag = "kind", rename_all = "camelCase", deny_unknown_fields)]
pub enum AuthoredPredicate {
    EvidenceAtLeast { evidence: String, minimum: i64 },
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(tag = "kind", rename_all = "camelCase", deny_unknown_fields)]
pub enum AuthoredSelector {
    IntentTarget,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(tag = "kind", rename_all = "camelCase", deny_unknown_fields)]
pub enum AuthoredOperation {
    SpendMagicka {
        amount: i64,
    },
    Damage {
        target: AuthoredSelector,
        amount: i64,
    },
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct AuthoredItemDefinition {
    pub id: String,
    pub interceptor: AuthoredInterceptor,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(tag = "kind", rename_all = "camelCase", deny_unknown_fields)]
pub enum AuthoredInterceptor {
    ReduceDamage { amount: i64 },
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

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct DaggerGameplayCatalog {
    fingerprint: String,
    actions: BTreeMap<String, DaggerActionDefinition>,
    items: BTreeMap<String, DaggerItemDefinition>,
    rules: Vec<DaggerRuleDefinition>,
}

impl DaggerGameplayCatalog {
    pub fn fingerprint(&self) -> &str {
        &self.fingerprint
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

    pub(crate) fn new(
        fingerprint: String,
        actions: BTreeMap<String, DaggerActionDefinition>,
        items: BTreeMap<String, DaggerItemDefinition>,
        rules: Vec<DaggerRuleDefinition>,
    ) -> Self {
        Self {
            fingerprint,
            actions,
            items,
            rules,
        }
    }
}

pub type DaggerProgram = Program<DaggerPredicate, DaggerOperation>;

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct DaggerActionDefinition {
    pub id: String,
    pub tags: BTreeSet<String>,
    pub program: DaggerProgram,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum DaggerPredicate {
    EvidenceAtLeast { evidence: String, minimum: i64 },
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum DaggerSelector {
    IntentTarget,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum DaggerOperation {
    SpendMagicka { amount: i64 },
    Damage { target: DaggerSelector, amount: i64 },
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct DaggerItemDefinition {
    pub id: String,
    pub interceptor: DaggerInterceptorKind,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum DaggerInterceptorKind {
    ReduceDamage { amount: i64 },
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum DaggerRuleDefinition {
    RejectTagWhileCondition {
        id: String,
        tag: String,
        condition: String,
    },
}

#[derive(Debug, Clone, PartialEq, Eq, Default)]
pub struct DaggerGameplayState {
    actors: BTreeMap<String, DaggerActorState>,
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

    pub(crate) fn actor_mut(&mut self, id: &str) -> Option<&mut DaggerActorState> {
        self.actors.get_mut(id)
    }
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct DaggerActorState {
    health: i64,
    magicka: i64,
    conditions: BTreeSet<String>,
    items: BTreeSet<String>,
}

impl DaggerActorState {
    pub fn new(health: i64, magicka: i64) -> Result<Self, DaggerGameplayError> {
        if health < 0 || magicka < 0 {
            return Err(DaggerGameplayError::InvalidState(
                "health and magicka must be non-negative".to_string(),
            ));
        }
        Ok(Self {
            health,
            magicka,
            conditions: BTreeSet::new(),
            items: BTreeSet::new(),
        })
    }

    pub const fn health(&self) -> i64 {
        self.health
    }

    pub const fn magicka(&self) -> i64 {
        self.magicka
    }

    pub fn conditions(&self) -> &BTreeSet<String> {
        &self.conditions
    }

    pub fn items(&self) -> &BTreeSet<String> {
        &self.items
    }

    pub fn add_condition(&mut self, condition: impl Into<String>) {
        self.conditions.insert(condition.into());
    }

    pub fn add_item(&mut self, item: impl Into<String>) {
        self.items.insert(item.into());
    }

    pub(crate) fn spend_magicka(&mut self, amount: i64) -> Result<(), DaggerTransactionError> {
        if amount < 0 || self.magicka < amount {
            return Err(DaggerTransactionError::InsufficientMagicka {
                available: self.magicka,
                required: amount,
            });
        }
        self.magicka -= amount;
        Ok(())
    }

    pub(crate) fn apply_damage(&mut self, amount: i64) -> Result<(), DaggerTransactionError> {
        if amount < 0 {
            return Err(DaggerTransactionError::InvalidEffect(
                "damage must be non-negative".to_string(),
            ));
        }
        self.health = self.health.saturating_sub(amount).max(0);
        Ok(())
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

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct DaggerAdmittedIntent {
    pub action: DaggerActionDefinition,
    pub actor: String,
    pub target: String,
    pub origin: DaggerIntentOrigin,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct DaggerFacts {
    pub actor: DaggerActorState,
    pub target: DaggerActorState,
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
    SpendMagicka { actor: String, amount: i64 },
    Damage { target: String, amount: i64 },
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(tag = "kind", rename_all = "camelCase")]
pub enum DaggerEvent {
    MagickaSpent { actor: String, amount: i64 },
    DamageApplied { target: String, amount: i64 },
    InterceptorApplied { source: String, amount: i64 },
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct DaggerInterceptor {
    pub source: String,
    pub kind: DaggerInterceptorKind,
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
    Rule { rule: String, reason: String },
    MissingEvidence(String),
    InsufficientMagicka { available: i64, required: i64 },
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
    InsufficientMagicka { available: i64, required: i64 },
    InvalidEffect(String),
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
