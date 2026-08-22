//! The classic loot-table generation authority (donor `LootTables.cs`
//! `GenerateRandomLoot` — adopted/adapted; transcription in
//! `gameplay/src/catalogs/loot.ts`). Everything here is bounded named
//! evidence in, deterministic items out: no RNG, no time source. The caller
//! resolves the declared contract (`loot_roll_evidence`), supplies values,
//! and gets a structured receipt (`DaggerLootGeneration`) that makes every
//! skipped or unsupported category visible.
//!
//! Documented deviations from the donor:
//! - The donor's per-category `while (SuccessRoll(chance))` loop is
//!   unbounded; our evidence contract is statically bounded, so each
//!   category yields at most `LOOT_CATEGORY_SLOTS` (3) items. Slot chances
//!   halve geometrically exactly as the donor's `chance *= 0.5` floored to
//!   integer at the roll.
//! - The donor reseeds its RNG per generation (`Random.InitState`); our
//!   determinism comes from the caller-supplied evidence stream instead.
//! - Categories with no catalog item pool yet (the seven ingredient groups,
//!   magic, clothing, books, religious) roll normally and record successes
//!   against `supported: false`, producing no items.
//! - The donor's item builders scale material with player level (iron at
//!   level 1); our catalog is iron-tier only, so picks are uniform over the
//!   catalog pools until richer materials arrive.

use rusty_engine::core_ids::EntityId;
use rusty_engine::entity_state::{EntityAuthoringService, EntityDefinition};
use rusty_engine::gameplay_mechanics::{
    InventoryComponent, ItemDefinitionId, OperationId, SourceInstanceId, SourceInstanceIdentity,
};
use rusty_engine::gameplay_standard::StandardOperation;

use super::eval::{
    apply_standard_mechanics_operation, bind_unique_item, bounded_sample_receipt,
    bounded_sample_value, mechanics_role, reject_unexpected_bounded_evidence,
};
use super::mechanics::mechanics_catalog_version;
use super::{
    DaggerEvidence, DaggerGameplayCatalog, DaggerGameplayError, DaggerGameplayState,
    DaggerLootCategoryOutcome, DaggerLootGeneration, DaggerLootGoldOutcome, DaggerLootRollOutcome,
    DaggerRejection,
};

/// Items one category can yield per generation. See the module docs: the
/// donor's halving loop is unbounded, but a bounded evidence contract needs
/// a fixed slot count. At the highest classic chance (100) slots roll
/// 100/50/25, so the cap only trims a fourth success at 12%-or-lower odds.
const LOOT_CATEGORY_SLOTS: u8 = 3;

/// What one category's success produces.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
enum LootPool {
    /// Uniform pick over the catalog's weapon items, sorted by id.
    Weapons,
    /// Uniform pick over the catalog's armor + shield items, sorted by id.
    ArmorAndShields,
    /// No catalog pool yet: successes are recorded as unsupported coverage.
    Unsupported,
}

struct LootCategorySpec {
    name: &'static str,
    /// Donor quirk (verified against classic): the first four ingredient
    /// categories (C1, C2, P1, P2) roll chance x player level; everything
    /// else rolls the raw table value.
    level_scaled: bool,
    pool: LootPool,
}

/// Donor generation order (`GenerateRandomLoot`): weapons, armor, the seven
/// ingredient groups, then magic, clothing, books, religious.
const LOOT_CATEGORY_SPECS: [LootCategorySpec; 13] = [
    LootCategorySpec {
        name: "weapons",
        level_scaled: false,
        pool: LootPool::Weapons,
    },
    LootCategorySpec {
        name: "armor",
        level_scaled: false,
        pool: LootPool::ArmorAndShields,
    },
    LootCategorySpec {
        name: "creature1",
        level_scaled: true,
        pool: LootPool::Unsupported,
    },
    LootCategorySpec {
        name: "creature2",
        level_scaled: true,
        pool: LootPool::Unsupported,
    },
    LootCategorySpec {
        name: "creature3",
        level_scaled: false,
        pool: LootPool::Unsupported,
    },
    LootCategorySpec {
        name: "plant1",
        level_scaled: true,
        pool: LootPool::Unsupported,
    },
    LootCategorySpec {
        name: "plant2",
        level_scaled: true,
        pool: LootPool::Unsupported,
    },
    LootCategorySpec {
        name: "misc1",
        level_scaled: false,
        pool: LootPool::Unsupported,
    },
    LootCategorySpec {
        name: "misc2",
        level_scaled: false,
        pool: LootPool::Unsupported,
    },
    LootCategorySpec {
        name: "magic",
        level_scaled: false,
        pool: LootPool::Unsupported,
    },
    LootCategorySpec {
        name: "clothing",
        level_scaled: false,
        pool: LootPool::Unsupported,
    },
    LootCategorySpec {
        name: "books",
        level_scaled: false,
        pool: LootPool::Unsupported,
    },
    LootCategorySpec {
        name: "religious",
        level_scaled: false,
        pool: LootPool::Unsupported,
    },
];

fn gold_roll_id(key: &str) -> String {
    format!("loot.{key}.gold")
}

fn success_roll_id(key: &str, category: &str, slot: u8) -> String {
    format!("loot.{key}.{category}.{slot}")
}

fn pick_roll_id(key: &str, category: &str, slot: u8) -> String {
    format!("{}.pick", success_roll_id(key, category, slot))
}

fn loot_table<'a>(
    catalog: &'a DaggerGameplayCatalog,
    key: &str,
) -> Result<&'a super::DaggerLootTable, DaggerGameplayError> {
    catalog
        .loot_tables()
        .get(key)
        .ok_or_else(|| DaggerGameplayError::InvalidValue {
            path: format!("lootTables[{key}]"),
            reason: "unknown loot table".to_string(),
        })
}

/// The deterministic pick pool for one category: catalog items sorted by id.
fn category_pool(catalog: &DaggerGameplayCatalog, pool: LootPool) -> Vec<String> {
    let filter: fn(&super::DaggerItemDefinition) -> bool = match pool {
        LootPool::Weapons => |item| item.weapon.is_some(),
        LootPool::ArmorAndShields => |item| item.armor.is_some() || item.shield.is_some(),
        LootPool::Unsupported => return Vec::new(),
    };
    catalog
        .items()
        .values()
        .filter(|item| filter(item))
        .map(|item| item.id.clone())
        .collect()
}

/// The full roll contract for one loot table: every bounded named roll
/// `generate_loot` reads, as `(evidence id, min, max)` in deterministic
/// order — gold first (omitted when the table's gold range is 0..0), then
/// per category with a positive chance (donor generation order) three
/// success dice bounded 0..=99, each followed by its pick die for the
/// supported item categories (bounded over the category's pool).
///
/// Ids use the plain `loot.<key>...` namespace. Callers generating loot per
/// entity map these ids into their own per-entity evidence stream (e.g. by
/// prefixing with the entity's stream id) and hand the values back through
/// `evidence`; generation reads by exact id match. Keeping the contract
/// prefix-free leaves stream layout to the caller — the runtime owns its
/// evidence streams, not this module.
pub fn loot_roll_evidence(
    catalog: &DaggerGameplayCatalog,
    key: &str,
) -> Result<Vec<(String, i64, i64)>, DaggerGameplayError> {
    let table = loot_table(catalog, key)?;
    let mut rolls = Vec::new();
    if table.gold_min != 0 || table.gold_max != 0 {
        rolls.push((gold_roll_id(key), table.gold_min, table.gold_max));
    }
    for spec in &LOOT_CATEGORY_SPECS {
        let chance = table
            .categories
            .chance(spec.name)
            .expect("spec names a category field");
        if chance == 0 {
            continue;
        }
        let pool = category_pool(catalog, spec.pool);
        for slot in 0..LOOT_CATEGORY_SLOTS {
            rolls.push((success_roll_id(key, spec.name, slot), 0, 99));
            if !pool.is_empty() {
                rolls.push((
                    pick_roll_id(key, spec.name, slot),
                    0,
                    i64::try_from(pool.len()).expect("pool length fits i64") - 1,
                ));
            }
        }
    }
    Ok(rolls)
}

/// Generate one table's loot from caller-supplied evidence. `level` is the
/// player level the donor multiplies gold (and the C1/C2/P1/P2 chances) by;
/// it must be >= 1.
pub fn generate_loot(
    catalog: &DaggerGameplayCatalog,
    key: &str,
    level: i64,
    evidence: &[DaggerEvidence],
) -> Result<DaggerLootGeneration, DaggerRejection> {
    let table = catalog
        .loot_tables()
        .get(key)
        .ok_or_else(|| DaggerRejection::MissingValue(format!("loot-table.{key}")))?;
    if level < 1 {
        return Err(DaggerRejection::InvalidExpression(format!(
            "loot level must be >= 1, got {level}"
        )));
    }
    let requirements = loot_roll_evidence(catalog, key).map_err(|error| {
        DaggerRejection::InvalidExpression(format!("loot evidence requirements: {error:?}"))
    })?;
    let receipt = if requirements.is_empty() {
        reject_unexpected_bounded_evidence(evidence)?;
        None
    } else {
        Some(bounded_sample_receipt(
            "dagger.loot",
            &requirements,
            evidence,
            true,
        )?)
    };
    let mut items: Vec<(String, u64)> = Vec::new();
    let gold = if table.gold_min == 0 && table.gold_max == 0 {
        None
    } else {
        let id = gold_roll_id(key);
        let roll = bounded_sample_value(
            receipt
                .as_ref()
                .expect("gold has a bounded evidence requirement"),
            &id,
        )?;
        let amount = roll.checked_mul(level).ok_or_else(|| {
            DaggerRejection::InvalidExpression("gold amount overflow".to_string())
        })?;
        if amount > 0 {
            items.push((
                "gold-piece".to_string(),
                u64::try_from(amount).expect("positive gold amount fits u64"),
            ));
        }
        Some(DaggerLootGoldOutcome {
            roll,
            level,
            amount,
        })
    };
    let mut categories = Vec::new();
    for spec in &LOOT_CATEGORY_SPECS {
        let chance = table
            .categories
            .chance(spec.name)
            .expect("spec names a category field");
        if chance == 0 {
            continue;
        }
        let effective_chance = if spec.level_scaled {
            chance.checked_mul(level).ok_or_else(|| {
                DaggerRejection::InvalidExpression("category chance overflow".to_string())
            })?
        } else {
            chance
        };
        let pool = category_pool(catalog, spec.pool);
        let supported = !pool.is_empty();
        let mut slot_chance = effective_chance;
        let mut rolls = Vec::with_capacity(usize::from(LOOT_CATEGORY_SLOTS));
        for slot in 0..LOOT_CATEGORY_SLOTS {
            let id = success_roll_id(key, spec.name, slot);
            let roll = bounded_sample_value(
                receipt
                    .as_ref()
                    .expect("category success has a bounded evidence requirement"),
                &id,
            )?;
            // Donor `Dice100.SuccessRoll`: a 0..99 roll strictly below the
            // chance succeeds; chance 0 never succeeds.
            let success = roll < slot_chance;
            let mut pick = None;
            let mut item = None;
            if success && supported {
                let pick_id = pick_roll_id(key, spec.name, slot);
                let value = bounded_sample_value(
                    receipt
                        .as_ref()
                        .expect("supported pick has a bounded evidence requirement"),
                    &pick_id,
                )?;
                pick = Some(value);
                let picked = pool[usize::try_from(value).expect("bounded pick")].clone();
                items.push((picked.clone(), 1));
                item = Some(picked);
            }
            rolls.push(DaggerLootRollOutcome {
                slot,
                chance: slot_chance,
                roll,
                success,
                pick,
                item,
            });
            // Donor `while (SuccessRoll(chance))`: the category terminates at
            // the first failed roll — later slots are never evaluated, so
            // the receipt records exactly the rolls that occurred.
            if !success {
                break;
            }
            // Donor geometric halving (`chance *= 0.5`, floored at the
            // roll's integer cast) — integer floor division is identical for
            // integer chances.
            slot_chance /= 2;
        }
        categories.push(DaggerLootCategoryOutcome {
            category: spec.name.to_string(),
            chance,
            effective_chance,
            supported,
            rolls,
        });
    }
    Ok(DaggerLootGeneration {
        key: key.to_string(),
        level,
        gold,
        categories,
        items,
    })
}

/// Bind one generation's items into an owner's upstream inventory: fungible
/// entries (gold) grant stacks through the inventory service; unique entries
/// retain Dagger allocation/naming while the Engine atomically admits,
/// attaches, and contains them — the same binding pattern spawn loadouts use.
fn bind_loot_items(
    state: &mut DaggerGameplayState,
    catalog: &DaggerGameplayCatalog,
    owner: EntityId,
    instance: &str,
    generation: &DaggerLootGeneration,
) -> Result<(), DaggerGameplayError> {
    let operation = OperationId::parse("dagger-loot-generation").expect("fixed operation identity");
    let source = SourceInstanceIdentity::Request {
        operation: operation.clone(),
        instance: SourceInstanceId::parse("dagger-loot").expect("fixed source identity"),
    };
    for (index, (item, quantity)) in generation.items.iter().enumerate() {
        let path = format!("loot[{}].items[{index}].{item}", generation.key);
        let definition =
            catalog
                .items()
                .get(item)
                .ok_or_else(|| DaggerGameplayError::InvalidValue {
                    path: path.clone(),
                    reason: "unknown item".to_string(),
                })?;
        let item_id = ItemDefinitionId::parse(item.clone()).map_err(|error| {
            DaggerGameplayError::InvalidId {
                path: path.clone(),
                value: format!("{item}: {error:?}"),
            }
        })?;
        if definition.fungible {
            apply_standard_mechanics_operation(
                state,
                catalog,
                StandardOperation::GrantStack {
                    role: mechanics_role("loot-owner"),
                    item: item_id,
                    quantity: *quantity,
                },
                vec![(mechanics_role("loot-owner"), owner)],
                operation.clone(),
                source.clone(),
            )
            .map_err(|error| DaggerGameplayError::InvalidValue {
                path: path.clone(),
                reason: format!("standard loot grant rejected: {error:?}"),
            })?;
            continue;
        }
        for unit in 0..*quantity {
            bind_unique_item(
                state,
                catalog,
                owner,
                format!("{instance}:{item}:{index}.{unit}"),
                item_id.clone(),
                &path,
            )?;
        }
    }
    Ok(())
}

/// Spawn a standalone loot container instance: allocate an entity, attach an
/// upstream InventoryComponent with no capacity limit (a pile, not an
/// encumbered actor), generate the table's loot, and bind the contents. The
/// container is tracked in the state (entity + table key + generation
/// receipt) so consumers can enumerate it.
pub fn spawn_container(
    state: &mut DaggerGameplayState,
    catalog: &DaggerGameplayCatalog,
    instance: &str,
    key: &str,
    level: i64,
    evidence: &[DaggerEvidence],
) -> Result<(), DaggerGameplayError> {
    let generation = generate_loot(catalog, key, level, evidence).map_err(|rejection| {
        DaggerGameplayError::InvalidValue {
            path: format!("lootTables[{key}]"),
            reason: format!("loot generation rejected: {rejection:?}"),
        }
    })?;
    let entity = state.allocate_entity();
    let state_revision = state.entities().revision();
    EntityAuthoringService
        .admit(
            state.entities_mut(),
            state_revision,
            [EntityDefinition::new(entity, instance)],
        )
        .map_err(|error| {
            DaggerGameplayError::InvalidState(format!("container admission: {error}"))
        })?;
    let inventory = InventoryComponent::with_capacity_limits(
        mechanics_catalog_version(),
        Vec::new(),
        Vec::new(),
    )
    .map_err(|error| {
        DaggerGameplayError::InvalidState(format!("container inventory component: {error:?}"))
    })?;
    let inventory_revision = state
        .entities()
        .component_revision::<InventoryComponent>(entity)
        .map_err(|error| {
            DaggerGameplayError::InvalidState(format!("container inventory revision: {error}"))
        })?;
    EntityAuthoringService
        .attach_component(state.entities_mut(), inventory_revision, entity, inventory)
        .map_err(|error| {
            DaggerGameplayError::InvalidState(format!("attach container inventory: {error}"))
        })?;
    bind_loot_items(state, catalog, entity, instance, &generation)?;
    state.insert_container(
        instance,
        super::DaggerContainerState::new(entity, key, generation),
    );
    Ok(())
}

/// Generate one table's loot and bind it into an already-spawned actor's
/// inventory — the corpse-loot model: the loot lives in the enemy's
/// inventory from spawn and is looted out of it at death. `spawn_actor`
/// stays unchanged; the runtime calls this per enemy with that enemy's own
/// evidence stream. Returns the generation receipt.
pub fn bind_actor_loot(
    state: &mut DaggerGameplayState,
    catalog: &DaggerGameplayCatalog,
    instance: &str,
    key: &str,
    level: i64,
    evidence: &[DaggerEvidence],
) -> Result<DaggerLootGeneration, DaggerGameplayError> {
    let entity = state
        .actor(instance)
        .ok_or_else(|| DaggerGameplayError::InvalidState(format!("unknown actor {instance}")))?
        .entity();
    let generation = generate_loot(catalog, key, level, evidence).map_err(|rejection| {
        DaggerGameplayError::InvalidValue {
            path: format!("lootTables[{key}]"),
            reason: format!("loot generation rejected: {rejection:?}"),
        }
    })?;
    bind_loot_items(state, catalog, entity, instance, &generation)?;
    Ok(generation)
}
