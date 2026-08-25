//! Rust-owned user settings for the Dagger product.
//!
//! This module deliberately stops at the product contract.  A host (the
//! browser during development, or a future Tauri adapter) is responsible for
//! persistence; this module owns the schema, defaults, validation, effective
//! values, and optimistic-concurrency rules shared by those hosts.

use std::collections::BTreeMap;

use serde::{Deserialize, Serialize};

/// Version of the serialized Dagger settings contract.
pub const SETTINGS_SCHEMA_VERSION: u32 = 1;

/// Stable identifier for the first product setting.
pub const DEBUG_FAILED_INVENTORY_DROP_MESSAGES_ID: &str = "debug.failedInventoryDropMessages";

/// The supported wire/runtime kinds for registered settings.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub enum SettingKind {
    Boolean,
}

impl SettingKind {
    fn of_value(value: &SettingValue) -> Self {
        match value {
            SettingValue::Boolean(_) => Self::Boolean,
        }
    }
}

/// A typed setting value on the product wire contract.
///
/// The enum is intentionally untagged: a boolean is serialized as `true` or
/// `false`, rather than as an implementation-specific tagged object.  Adding
/// a future kind remains a schema-controlled change without changing the
/// existing setting's ID or boolean representation.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(untagged)]
pub enum SettingValue {
    Boolean(bool),
}

impl SettingValue {
    pub const fn boolean(value: bool) -> Self {
        Self::Boolean(value)
    }

    pub const fn as_boolean(&self) -> Option<bool> {
        match self {
            Self::Boolean(value) => Some(*value),
        }
    }
}

/// Immutable metadata for one registered setting.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct SettingDescriptor {
    /// Stable dotted setting ID.  IDs are part of the persisted contract.
    pub id: String,
    pub kind: SettingKind,
    pub default_value: SettingValue,
}

impl SettingDescriptor {
    /// Construct a boolean descriptor while checking the stable ID and
    /// default's declared type.
    pub fn boolean(id: impl Into<String>, default_value: bool) -> Result<Self, SettingsError> {
        Self::new(
            id,
            SettingKind::Boolean,
            SettingValue::Boolean(default_value),
        )
    }

    pub fn new(
        id: impl Into<String>,
        kind: SettingKind,
        default_value: SettingValue,
    ) -> Result<Self, SettingsError> {
        let descriptor = Self {
            id: id.into(),
            kind,
            default_value,
        };
        validate_setting_id(&descriptor.id)?;
        let actual_kind = SettingKind::of_value(&descriptor.default_value);
        if descriptor.kind != actual_kind {
            return Err(SettingsError::DescriptorTypeMismatch {
                id: descriptor.id,
                expected: descriptor.kind,
                actual: actual_kind,
            });
        }
        Ok(descriptor)
    }
}

/// One setting and its effective value in the read model.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct SettingReadout {
    pub id: String,
    pub kind: SettingKind,
    pub default_value: SettingValue,
    pub value: SettingValue,
}

/// Canonical effective settings read model.  The host may persist this value
/// as-is; its `revision` is the optimistic-concurrency token for a later
/// reconciliation.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct SettingsReadout {
    pub schema_version: u32,
    pub revision: u64,
    pub settings: Vec<SettingReadout>,
}

/// One typed mutation submitted by a host or product route.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct SettingUpdate {
    pub id: String,
    pub value: SettingValue,
}

impl SettingUpdate {
    pub fn boolean(id: impl Into<String>, value: bool) -> Self {
        Self {
            id: id.into(),
            value: SettingValue::Boolean(value),
        }
    }
}

/// A compare-and-swap update request.  All changes are validated before any
/// value is committed, so a rejected request cannot partially update state.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct SettingsUpdateRequest {
    pub schema_version: u32,
    pub expected_revision: u64,
    pub changes: Vec<SettingUpdate>,
}

/// A full host snapshot submitted when browser/Tauri state is flushed back to
/// Rust.  `expected_revision` is intentionally separate from the persisted
/// readout's revision: a host can read a snapshot, edit values, then submit
/// the edited values without pretending to have created a new Rust revision.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct SettingsReconcileRequest {
    pub schema_version: u32,
    pub expected_revision: u64,
    pub settings: Vec<SettingUpdate>,
}

impl SettingsReconcileRequest {
    pub fn from_readout(readout: SettingsReadout) -> Self {
        Self {
            schema_version: readout.schema_version,
            expected_revision: readout.revision,
            settings: readout
                .settings
                .into_iter()
                .map(|setting| SettingUpdate {
                    id: setting.id,
                    value: setting.value,
                })
                .collect(),
        }
    }
}

/// Result of an accepted settings mutation.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct SettingsUpdateReceipt {
    pub previous_revision: u64,
    pub committed_revision: u64,
    pub changed: bool,
    pub settings: SettingsReadout,
}

/// Errors are intentionally structured so routes can distinguish malformed
/// payloads, stale optimistic-concurrency tokens, unknown IDs, and invalid
/// values without inferring from display strings.
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum SettingsError {
    MalformedPayload(String),
    UnsupportedSchemaVersion {
        actual: u32,
        expected: u32,
    },
    StaleRevision {
        expected: u64,
        current: u64,
    },
    UnknownSetting {
        id: String,
    },
    MissingSetting {
        id: String,
    },
    DuplicateSetting {
        id: String,
    },
    InvalidSettingId {
        id: String,
    },
    DuplicateRegistration {
        id: String,
    },
    DescriptorTypeMismatch {
        id: String,
        expected: SettingKind,
        actual: SettingKind,
    },
    InvalidValueType {
        id: String,
        expected: SettingKind,
        actual: SettingKind,
    },
    EmptyUpdate,
}

impl std::fmt::Display for SettingsError {
    fn fmt(&self, formatter: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        match self {
            Self::MalformedPayload(message) => {
                write!(formatter, "malformed settings payload: {message}")
            }
            Self::UnsupportedSchemaVersion { actual, expected } => write!(
                formatter,
                "unsupported settings schema version {actual}; expected {expected}"
            ),
            Self::StaleRevision { expected, current } => write!(
                formatter,
                "stale settings revision: expected {expected}, current {current}"
            ),
            Self::UnknownSetting { id } => write!(formatter, "unknown setting: {id}"),
            Self::MissingSetting { id } => write!(formatter, "missing setting: {id}"),
            Self::DuplicateSetting { id } => write!(formatter, "duplicate setting: {id}"),
            Self::InvalidSettingId { id } => write!(formatter, "invalid setting ID: {id}"),
            Self::DuplicateRegistration { id } => {
                write!(formatter, "setting already registered: {id}")
            }
            Self::DescriptorTypeMismatch {
                id,
                expected,
                actual,
            } => write!(
                formatter,
                "setting descriptor {id} declares {expected:?}, default is {actual:?}"
            ),
            Self::InvalidValueType {
                id,
                expected,
                actual,
            } => write!(
                formatter,
                "setting {id} expects {expected:?}, received {actual:?}"
            ),
            Self::EmptyUpdate => write!(
                formatter,
                "settings update must contain at least one setting"
            ),
        }
    }
}

impl std::error::Error for SettingsError {}

/// The immutable descriptor catalog used to construct runtime settings
/// state.  Registration is available during host/product setup; once a
/// `SettingsState` is created, the catalog is owned by that state and all
/// descriptor access is read-only.
#[derive(Debug, Clone, Default)]
pub struct SettingsRegistry {
    descriptors: BTreeMap<String, SettingDescriptor>,
}

impl SettingsRegistry {
    pub fn new() -> Self {
        Self::default()
    }

    /// Create the product's initial registry.
    pub fn with_builtins() -> Result<Self, SettingsError> {
        let mut registry = Self::new();
        registry.register(SettingDescriptor::boolean(
            DEBUG_FAILED_INVENTORY_DROP_MESSAGES_ID,
            false,
        )?)?;
        Ok(registry)
    }

    /// Register a descriptor before constructing state.  Duplicate IDs are
    /// rejected to keep persisted IDs unambiguous.
    pub fn register(&mut self, descriptor: SettingDescriptor) -> Result<(), SettingsError> {
        validate_setting_id(&descriptor.id)?;
        let actual_kind = SettingKind::of_value(&descriptor.default_value);
        if descriptor.kind != actual_kind {
            return Err(SettingsError::DescriptorTypeMismatch {
                id: descriptor.id,
                expected: descriptor.kind,
                actual: actual_kind,
            });
        }
        if self.descriptors.contains_key(&descriptor.id) {
            return Err(SettingsError::DuplicateRegistration { id: descriptor.id });
        }
        self.descriptors.insert(descriptor.id.clone(), descriptor);
        Ok(())
    }

    pub fn descriptors(&self) -> impl Iterator<Item = &SettingDescriptor> {
        self.descriptors.values()
    }

    pub fn descriptor(&self, id: &str) -> Option<&SettingDescriptor> {
        self.descriptors.get(id)
    }

    pub fn len(&self) -> usize {
        self.descriptors.len()
    }

    pub fn is_empty(&self) -> bool {
        self.descriptors.is_empty()
    }

    pub fn build_state(self) -> SettingsState {
        SettingsState::new(self)
    }
}

/// Live Rust-owned effective settings.  The registry is never modified after
/// construction, while values may change only through validated CAS updates
/// or complete snapshot reconciliation.
#[derive(Debug, Clone)]
pub struct SettingsState {
    registry: SettingsRegistry,
    values: BTreeMap<String, SettingValue>,
    revision: u64,
}

impl SettingsState {
    pub fn new(registry: SettingsRegistry) -> Self {
        let values = registry
            .descriptors
            .iter()
            .map(|(id, descriptor)| (id.clone(), descriptor.default_value.clone()))
            .collect();
        Self {
            registry,
            values,
            revision: 0,
        }
    }

    pub fn with_builtins() -> Result<Self, SettingsError> {
        Ok(SettingsRegistry::with_builtins()?.build_state())
    }

    pub fn revision(&self) -> u64 {
        self.revision
    }

    pub fn registry(&self) -> &SettingsRegistry {
        &self.registry
    }

    pub fn value(&self, id: &str) -> Result<&SettingValue, SettingsError> {
        self.registry
            .descriptor(id)
            .ok_or_else(|| SettingsError::UnknownSetting { id: id.to_string() })?;
        Ok(self
            .values
            .get(id)
            .expect("registry values are initialized"))
    }

    pub fn boolean(&self, id: &str) -> Result<bool, SettingsError> {
        match self.value(id)? {
            SettingValue::Boolean(value) => Ok(*value),
        }
    }

    pub fn readout(&self) -> SettingsReadout {
        let settings = self
            .registry
            .descriptors
            .iter()
            .map(|(id, descriptor)| SettingReadout {
                id: id.clone(),
                kind: descriptor.kind,
                default_value: descriptor.default_value.clone(),
                value: self
                    .values
                    .get(id)
                    .expect("registry values are initialized")
                    .clone(),
            })
            .collect();
        SettingsReadout {
            schema_version: SETTINGS_SCHEMA_VERSION,
            revision: self.revision,
            settings,
        }
    }

    /// Decode a JSON update at the contract boundary.  Routes can preserve
    /// `MalformedPayload` as a 400-class failure instead of accepting a
    /// partial or defaulted request.
    pub fn parse_update_request(document: &str) -> Result<SettingsUpdateRequest, SettingsError> {
        serde_json::from_str(document)
            .map_err(|error| SettingsError::MalformedPayload(error.to_string()))
    }

    /// Decode a complete host reconciliation at the contract boundary.
    pub fn parse_reconcile_request(
        document: &str,
    ) -> Result<SettingsReconcileRequest, SettingsError> {
        serde_json::from_str(document)
            .map_err(|error| SettingsError::MalformedPayload(error.to_string()))
    }

    /// Apply one or more typed changes atomically at an expected revision.
    pub fn update(
        &mut self,
        request: SettingsUpdateRequest,
    ) -> Result<SettingsUpdateReceipt, SettingsError> {
        self.validate_header(request.schema_version, request.expected_revision)?;
        self.validate_changes(&request.changes, false)?;
        self.commit_changes(request.changes)
    }

    pub fn update_one(
        &mut self,
        expected_revision: u64,
        id: impl Into<String>,
        value: SettingValue,
    ) -> Result<SettingsUpdateReceipt, SettingsError> {
        self.update(SettingsUpdateRequest {
            schema_version: SETTINGS_SCHEMA_VERSION,
            expected_revision,
            changes: vec![SettingUpdate {
                id: id.into(),
                value,
            }],
        })
    }

    /// Apply a complete host snapshot atomically.  Every registered setting
    /// must occur exactly once, which prevents a stale/partial browser copy
    /// from silently resetting a Rust-owned value to an implicit default.
    pub fn reconcile(
        &mut self,
        request: SettingsReconcileRequest,
    ) -> Result<SettingsUpdateReceipt, SettingsError> {
        self.validate_header(request.schema_version, request.expected_revision)?;
        self.validate_changes(&request.settings, true)?;
        self.commit_changes(request.settings)
    }

    pub fn reconcile_readout(
        &mut self,
        readout: SettingsReadout,
    ) -> Result<SettingsUpdateReceipt, SettingsError> {
        let schema_version = readout.schema_version;
        let expected_revision = readout.revision;
        let settings = readout
            .settings
            .into_iter()
            .map(|setting| {
                let descriptor = self.registry.descriptor(&setting.id).ok_or_else(|| {
                    SettingsError::UnknownSetting {
                        id: setting.id.clone(),
                    }
                })?;
                if descriptor.kind != setting.kind
                    || descriptor.default_value != setting.default_value
                {
                    return Err(SettingsError::MalformedPayload(format!(
                        "descriptor metadata for {} does not match the Rust registry",
                        setting.id
                    )));
                }
                Ok(SettingUpdate {
                    id: setting.id,
                    value: setting.value,
                })
            })
            .collect::<Result<Vec<_>, SettingsError>>()?;
        self.reconcile(SettingsReconcileRequest {
            schema_version,
            expected_revision,
            settings,
        })
    }

    fn validate_header(
        &self,
        schema_version: u32,
        expected_revision: u64,
    ) -> Result<(), SettingsError> {
        if schema_version != SETTINGS_SCHEMA_VERSION {
            return Err(SettingsError::UnsupportedSchemaVersion {
                actual: schema_version,
                expected: SETTINGS_SCHEMA_VERSION,
            });
        }
        if expected_revision != self.revision {
            return Err(SettingsError::StaleRevision {
                expected: expected_revision,
                current: self.revision,
            });
        }
        Ok(())
    }

    fn validate_changes(
        &self,
        changes: &[SettingUpdate],
        require_complete: bool,
    ) -> Result<(), SettingsError> {
        if changes.is_empty() {
            return Err(SettingsError::EmptyUpdate);
        }
        let mut seen = BTreeMap::new();
        for change in changes {
            if !self.registry.descriptors.contains_key(&change.id) {
                return Err(SettingsError::UnknownSetting {
                    id: change.id.clone(),
                });
            }
            if seen.insert(change.id.clone(), ()).is_some() {
                return Err(SettingsError::DuplicateSetting {
                    id: change.id.clone(),
                });
            }
            let descriptor = self.registry.descriptor(&change.id).expect("checked above");
            let actual = SettingKind::of_value(&change.value);
            if actual != descriptor.kind {
                return Err(SettingsError::InvalidValueType {
                    id: change.id.clone(),
                    expected: descriptor.kind,
                    actual,
                });
            }
        }
        if require_complete {
            if let Some(id) = self
                .registry
                .descriptors
                .keys()
                .find(|id| !seen.contains_key(*id))
            {
                return Err(SettingsError::MissingSetting { id: id.clone() });
            }
        }
        Ok(())
    }

    fn commit_changes(
        &mut self,
        changes: Vec<SettingUpdate>,
    ) -> Result<SettingsUpdateReceipt, SettingsError> {
        let previous_revision = self.revision;
        let changed = changes.iter().any(|change| {
            self.values
                .get(&change.id)
                .is_some_and(|current| current != &change.value)
        });
        if changed {
            for change in changes {
                self.values.insert(change.id, change.value);
            }
            self.revision = self
                .revision
                .checked_add(1)
                .expect("settings revision overflow");
        }
        Ok(SettingsUpdateReceipt {
            previous_revision,
            committed_revision: self.revision,
            changed,
            settings: self.readout(),
        })
    }
}

impl Default for SettingsState {
    fn default() -> Self {
        Self::with_builtins().expect("built-in settings registry is valid")
    }
}

fn validate_setting_id(id: &str) -> Result<(), SettingsError> {
    let valid = !id.is_empty()
        && id.chars().all(|character| {
            character.is_ascii_alphanumeric() || matches!(character, '.' | '-' | '_')
        })
        && id.split('.').all(|segment| {
            !segment.is_empty()
                && segment
                    .chars()
                    .next()
                    .is_some_and(|c| c.is_ascii_alphabetic())
        });
    if valid {
        Ok(())
    } else {
        Err(SettingsError::InvalidSettingId { id: id.to_string() })
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn state() -> SettingsState {
        SettingsState::default()
    }

    fn setting<'a>(readout: &'a SettingsReadout, id: &str) -> &'a SettingReadout {
        readout
            .settings
            .iter()
            .find(|setting| setting.id == id)
            .expect("setting in readout")
    }

    #[test]
    fn builtin_readout_has_stable_id_default_and_camel_case_wire_shape() {
        let state = state();
        let readout = state.readout();
        let debug = setting(&readout, DEBUG_FAILED_INVENTORY_DROP_MESSAGES_ID);
        assert_eq!(debug.kind, SettingKind::Boolean);
        assert_eq!(debug.default_value, SettingValue::Boolean(false));
        assert_eq!(debug.value, SettingValue::Boolean(false));
        let json = serde_json::to_value(&readout).expect("serialize readout");
        assert_eq!(json["schemaVersion"], SETTINGS_SCHEMA_VERSION);
        assert_eq!(json["revision"], 0);
        assert_eq!(json["settings"][0]["defaultValue"], false);
        assert_eq!(json["settings"][0]["value"], false);
        assert!(json["settings"][0].get("default_value").is_none());
    }

    #[test]
    fn typed_update_is_atomic_and_advances_revision_only_when_value_changes() {
        let mut state = state();
        let first = state
            .update_one(
                0,
                DEBUG_FAILED_INVENTORY_DROP_MESSAGES_ID,
                SettingValue::Boolean(true),
            )
            .expect("typed update");
        assert_eq!(first.previous_revision, 0);
        assert_eq!(first.committed_revision, 1);
        assert!(first.changed);
        assert!(state
            .boolean(DEBUG_FAILED_INVENTORY_DROP_MESSAGES_ID)
            .unwrap());

        let no_op = state
            .update_one(
                1,
                DEBUG_FAILED_INVENTORY_DROP_MESSAGES_ID,
                SettingValue::Boolean(true),
            )
            .expect("idempotent update");
        assert_eq!(no_op.committed_revision, 1);
        assert!(!no_op.changed);
    }

    #[test]
    fn stale_unknown_and_wrong_type_updates_are_explicit_and_do_not_mutate() {
        let mut state = state();
        state
            .update_one(
                0,
                DEBUG_FAILED_INVENTORY_DROP_MESSAGES_ID,
                SettingValue::Boolean(true),
            )
            .expect("seed state");
        let before = state.readout();

        let stale = state
            .update_one(
                0,
                DEBUG_FAILED_INVENTORY_DROP_MESSAGES_ID,
                SettingValue::Boolean(false),
            )
            .expect_err("stale revision");
        assert_eq!(
            stale,
            SettingsError::StaleRevision {
                expected: 0,
                current: 1
            }
        );
        assert_eq!(state.readout(), before);

        let unknown = state
            .update_one(1, "debug.unknown", SettingValue::Boolean(true))
            .expect_err("unknown ID");
        assert_eq!(
            unknown,
            SettingsError::UnknownSetting {
                id: "debug.unknown".to_string()
            }
        );
        assert_eq!(state.readout(), before);

        // Deserialize a number into a boolean setting's typed value is not
        // possible, so construct the future-facing variant shape through a
        // malformed request and assert it remains a boundary error.
        let malformed = SettingsState::parse_update_request(
            r#"{"schemaVersion":1,"expectedRevision":1,"changes":[{"id":"debug.failedInventoryDropMessages","value":1}]}"#,
        )
        .expect_err("wrong wire type");
        assert!(matches!(malformed, SettingsError::MalformedPayload(_)));
        assert_eq!(state.readout(), before);
    }

    #[test]
    fn multi_update_validates_every_change_before_commit() {
        let mut state = state();
        let error = state
            .update(SettingsUpdateRequest {
                schema_version: SETTINGS_SCHEMA_VERSION,
                expected_revision: 0,
                changes: vec![
                    SettingUpdate::boolean(DEBUG_FAILED_INVENTORY_DROP_MESSAGES_ID, true),
                    SettingUpdate::boolean("debug.unknown", true),
                ],
            })
            .expect_err("unknown second change rejects whole request");
        assert_eq!(
            error,
            SettingsError::UnknownSetting {
                id: "debug.unknown".to_string()
            }
        );
        assert_eq!(state.revision(), 0);
        assert!(!state
            .boolean(DEBUG_FAILED_INVENTORY_DROP_MESSAGES_ID)
            .unwrap());
    }

    #[test]
    fn registry_rejects_invalid_and_duplicate_ids_and_supports_future_descriptors() {
        let mut registry = SettingsRegistry::new();
        registry
            .register(SettingDescriptor::boolean("ui.showHints", true).unwrap())
            .expect("register future setting");
        let duplicate = registry
            .register(SettingDescriptor::boolean("ui.showHints", false).unwrap())
            .expect_err("duplicate ID");
        assert_eq!(
            duplicate,
            SettingsError::DuplicateRegistration {
                id: "ui.showHints".to_string()
            }
        );
        let invalid = SettingDescriptor::boolean("ui showHints", true).expect_err("invalid ID");
        assert_eq!(
            invalid,
            SettingsError::InvalidSettingId {
                id: "ui showHints".to_string()
            }
        );
        assert_eq!(registry.len(), 1);
        assert!(registry.descriptor("ui.showHints").is_some());
    }

    #[test]
    fn reconcile_requires_a_complete_current_snapshot_and_is_atomic() {
        let mut registry = SettingsRegistry::with_builtins().expect("builtins");
        registry
            .register(SettingDescriptor::boolean("ui.showHints", true).unwrap())
            .expect("future setting");
        let mut state = registry.build_state();
        let initial = state.readout();

        let incomplete = state
            .reconcile(SettingsReconcileRequest {
                schema_version: SETTINGS_SCHEMA_VERSION,
                expected_revision: 0,
                settings: vec![SettingUpdate::boolean(
                    DEBUG_FAILED_INVENTORY_DROP_MESSAGES_ID,
                    true,
                )],
            })
            .expect_err("partial host snapshot");
        assert_eq!(
            incomplete,
            SettingsError::MissingSetting {
                id: "ui.showHints".to_string()
            }
        );
        assert_eq!(state.readout(), initial);

        let accepted = state
            .reconcile(SettingsReconcileRequest {
                schema_version: SETTINGS_SCHEMA_VERSION,
                expected_revision: 0,
                settings: vec![
                    SettingUpdate::boolean(DEBUG_FAILED_INVENTORY_DROP_MESSAGES_ID, true),
                    SettingUpdate::boolean("ui.showHints", false),
                ],
            })
            .expect("complete host snapshot");
        assert_eq!(accepted.committed_revision, 1);
        assert!(state
            .boolean(DEBUG_FAILED_INVENTORY_DROP_MESSAGES_ID)
            .unwrap());
        assert!(!state.boolean("ui.showHints").unwrap());

        let stale = state
            .reconcile_readout(initial)
            .expect_err("old host snapshot");
        assert_eq!(
            stale,
            SettingsError::StaleRevision {
                expected: 0,
                current: 1
            }
        );
    }

    #[test]
    fn malformed_schema_and_duplicate_snapshot_entries_are_rejected() {
        let mut state = state();
        let unsupported = state
            .update(SettingsUpdateRequest {
                schema_version: SETTINGS_SCHEMA_VERSION + 1,
                expected_revision: 0,
                changes: vec![SettingUpdate::boolean(
                    DEBUG_FAILED_INVENTORY_DROP_MESSAGES_ID,
                    true,
                )],
            })
            .expect_err("unsupported schema");
        assert_eq!(
            unsupported,
            SettingsError::UnsupportedSchemaVersion {
                actual: SETTINGS_SCHEMA_VERSION + 1,
                expected: SETTINGS_SCHEMA_VERSION
            }
        );
        let duplicate = state
            .reconcile(SettingsReconcileRequest {
                schema_version: SETTINGS_SCHEMA_VERSION,
                expected_revision: 0,
                settings: vec![
                    SettingUpdate::boolean(DEBUG_FAILED_INVENTORY_DROP_MESSAGES_ID, false),
                    SettingUpdate::boolean(DEBUG_FAILED_INVENTORY_DROP_MESSAGES_ID, true),
                ],
            })
            .expect_err("duplicate snapshot entry");
        assert_eq!(
            duplicate,
            SettingsError::DuplicateSetting {
                id: DEBUG_FAILED_INVENTORY_DROP_MESSAGES_ID.to_string()
            }
        );
        assert_eq!(state.revision(), 0);
    }
}
