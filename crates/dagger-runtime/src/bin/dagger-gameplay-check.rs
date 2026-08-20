//! Production-path proof for the authored Dagger gameplay package: admits the
//! committed package, spawns actors through the mechanics-backed spawn
//! authority, resolves the same authored melee action for player and AI
//! origins with effects committed through the Engine's track service, and
//! generates classic loot-table contents for the hold's treasure key.

use dagger_rpg::{
    compile_gameplay_package, resolve_dagger_action, spawn_actor, DaggerEvidence,
    DaggerGameplayState, DaggerIntent, DaggerIntentOrigin,
};
use rusty_engine::gameplay_resolution::{
    CorrelationId, ResolutionId, ResolutionIdentity, ResolutionMode,
};

const PACKAGE: &[u8] = include_bytes!("../../../../data/gameplay/dagger-core.package.json");

fn state(catalog: &dagger_rpg::DaggerGameplayCatalog) -> DaggerGameplayState {
    let mut state = DaggerGameplayState::default();
    spawn_actor(&mut state, catalog, "player", "player", &[]).expect("spawn player");
    spawn_actor(
        &mut state,
        catalog,
        "rat",
        "rat-2007",
        &[DaggerEvidence {
            id: "rat.health".to_string(),
            value: 12,
        }],
    )
    .expect("spawn rat");
    state
}

fn resolve(origin: DaggerIntentOrigin, resolution: u64) -> (DaggerGameplayState, String) {
    let catalog = compile_gameplay_package(PACKAGE).expect("admit authored gameplay package");
    let mut state = state(&catalog);
    let identity = ResolutionIdentity::root(
        ResolutionId::new(resolution).expect("non-zero resolution id"),
        CorrelationId::new(resolution).expect("non-zero correlation id"),
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
            // Equipped-weapon damage (iron longsword 2..16) and the struck
            // body part (8 selects chest through the classic table).
            DaggerEvidence {
                id: "melee-attack.equipped-weapon-damage".to_string(),
                value: 8,
            },
            DaggerEvidence {
                id: "melee-attack.struck-body-part".to_string(),
                value: 8,
            },
            // Career/swing facts are 0 until careers and swing states exist.
            DaggerEvidence {
                id: "melee-attack.swing-to-hit".to_string(),
                value: 0,
            },
            DaggerEvidence {
                id: "melee-attack.proficiency-to-hit".to_string(),
                value: 0,
            },
            DaggerEvidence {
                id: "melee-attack.racial-to-hit".to_string(),
                value: 0,
            },
            DaggerEvidence {
                id: "melee-attack.proficiency-damage".to_string(),
                value: 0,
            },
            DaggerEvidence {
                id: "melee-attack.racial-damage".to_string(),
                value: 0,
            },
            DaggerEvidence {
                id: "melee-attack.adrenaline-rush".to_string(),
                value: 0,
            },
            DaggerEvidence {
                id: "melee-attack.target-adrenaline-rush".to_string(),
                value: 0,
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
    for actor in ["player", "rat-2007"] {
        for track in ["health", "stamina", "magicka"] {
            assert_eq!(
                player_state.track_value(actor, track),
                ai_state.track_value(actor, track),
                "player and AI intents must share the same policy path"
            );
        }
    }
    println!("{player_readout}");

    // Equipment-driven combat facts: the player's equipped weapon and the
    // struck body part the scripted evidence selected (8 maps to chest).
    let catalog = compile_gameplay_package(PACKAGE).expect("admit authored gameplay package");
    let weapon = dagger_rpg::equipped_weapon(&player_state, &catalog, "player")
        .expect("player equipment read")
        .map_or("unarmed", |item| item.id.as_str());
    let struck_part =
        dagger_rpg::struck_body_part_name(8).expect("struck-part roll 8 maps to a part");
    assert_eq!(weapon, "iron-longsword");
    assert_eq!(struck_part, "chest");
    println!(
        "{}",
        serde_json::to_string_pretty(&serde_json::json!({
            "combat": { "weapon": weapon, "struckPart": struck_part }
        }))
        .expect("serialize combat facts")
    );

    // Derived-rule proof: the classic formula catalog evaluates against the
    // admitted player definition through the same evaluator.
    let catalog = compile_gameplay_package(PACKAGE).expect("admit authored gameplay package");
    let evidence_for = |id: &str| -> Vec<DaggerEvidence> {
        let value = |evidence_id: &str, value: i64| DaggerEvidence {
            id: evidence_id.to_string(),
            value,
        };
        match id {
            "health-recovery-rate" => {
                vec![value("max-health", 85), value("rapid-healing-active", 0)]
            }
            "fatigue-recovery-rate" => vec![value("max-fatigue", 5760)],
            "spell-point-recovery-rate" => {
                vec![value("max-magicka", 50), value("no-regen-spell-points", 0)]
            }
            "backstab-chance" => vec![value("target-facing-away", 1)],
            "player-level" => vec![
                value("current-level-up-skills-sum", 70),
                value("starting-level-up-skills-sum", 32),
            ],
            "hit-points-per-level-up" => vec![value("hp-level-up-roll", 7)],
            // Career-owned multiplier (milli): evaluate the default 1.5x here.
            "spell-points" => vec![value("spell-point-multiplier-milli", 1500)],
            // Donor golden: skill 30, advancement multiplier 2, career
            // multiplier 1.30 (centi), level 1 yields 33.
            "skill-uses-for-advancement" => vec![
                value("skill-value", 30),
                value("skill-advancement-multiplier", 2),
                value("career-advancement-multiplier-centi", 130),
                value("level", 1),
            ],
            _ => Vec::new(),
        }
    };
    let derived = catalog
        .derived()
        .keys()
        // `xp-level` reads the live xp progression stat, so it only
        // evaluates in live-state contexts — proven in the progression
        // section below, not against definition bases here.
        .filter(|id| id.as_str() != "xp-level")
        .map(|id| {
            let value =
                dagger_rpg::evaluate_derived_rule(&catalog, id, "player", &evidence_for(id))
                    .unwrap_or_else(|error| panic!("derived rule {id}: {error}"));
            serde_json::json!({ "id": id, "value": value })
        })
        .collect::<Vec<_>>();
    println!(
        "{}",
        serde_json::to_string_pretty(&serde_json::json!({ "derived": derived }))
            .expect("serialize derived readout")
    );

    // Player inventory proof: the upstream-sourced view of the spawned
    // player's loadout (InventoryService::view + EquipmentComponent).
    let catalog = compile_gameplay_package(PACKAGE).expect("admit authored gameplay package");
    let mut state = DaggerGameplayState::default();
    spawn_actor(&mut state, &catalog, "player", "player", &[]).expect("spawn player");
    let owner = state.actor("player").expect("player binding").entity();
    let view = rusty_engine::gameplay_mechanics::InventoryService::view(
        state.entities(),
        catalog.mechanics(),
        owner,
    )
    .expect("player inventory view");
    let equipment = state
        .entities()
        .component::<rusty_engine::gameplay_mechanics::EquipmentComponent>(owner)
        .expect("player equipment component read")
        .expect("player has an equipment component");
    let slot_of = |entity: rusty_engine::core_ids::EntityId| {
        equipment
            .assignments()
            .iter()
            .find(|assignment| assignment.item == entity)
            .map(|assignment| assignment.slot.as_str())
    };
    let inventory = serde_json::json!({
        "capacity": view.capacity().iter().map(|usage| serde_json::json!({
            "metric": usage.metric.as_str(),
            "used": usage.used,
            "maximum": usage.maximum,
        })).collect::<Vec<_>>(),
        "stacks": view.stacks().iter().map(|stack| serde_json::json!({
            "item": stack.definition.as_str(),
            "quantity": stack.quantity,
        })).collect::<Vec<_>>(),
        "items": view.unique_items().iter().map(|item| serde_json::json!({
            "item": item.definition.as_str(),
            "entity": item.entity.raw(),
            "equipSlot": slot_of(item.entity),
        })).collect::<Vec<_>>(),
    });
    println!(
        "{}",
        serde_json::to_string_pretty(&serde_json::json!({ "playerInventory": inventory }))
            .expect("serialize player inventory readout")
    );

    // Classic loot-table proof: spawn a container per table with fixed
    // evidence (every roll at its minimum bound — success rolls 0 always
    // succeed, gold rolls its minimum) and print the structured generation
    // records. "M" is the hold's dungeon treasure key (Natural Cave per
    // classic MAPS.BSA); "A" exercises the letter tables at level 1.
    let catalog = compile_gameplay_package(PACKAGE).expect("admit authored gameplay package");
    let mut state = DaggerGameplayState::default();
    let mut loot_records = Vec::new();
    for key in ["M", "A"] {
        let evidence = dagger_rpg::loot_roll_evidence(&catalog, key)
            .expect("loot roll contract")
            .into_iter()
            .map(|(id, min, _)| DaggerEvidence { id, value: min })
            .collect::<Vec<_>>();
        let instance = format!("treasure-{key}");
        dagger_rpg::spawn_container(&mut state, &catalog, &instance, key, 1, &evidence)
            .expect("spawn loot container");
        let container = state.container(&instance).expect("container binding");
        loot_records
            .push(serde_json::to_value(container.generation()).expect("serialize loot record"));
    }
    println!(
        "{}",
        serde_json::to_string_pretty(&serde_json::json!({ "loot": loot_records }))
            .expect("serialize loot readout")
    );

    // Kill-XP progression proof: spawn the player and a rat, award the rat's
    // kill through the progression authority with a fixed in-bounds hp-roll
    // evidence stream, and print the structured award record plus the live
    // `xp-level` curve evaluation (the live-only derived rule).
    let catalog = compile_gameplay_package(PACKAGE).expect("admit authored gameplay package");
    let mut progression_state = {
        let mut fresh = DaggerGameplayState::default();
        spawn_actor(&mut fresh, &catalog, "player", "player", &[]).expect("spawn player");
        spawn_actor(
            &mut fresh,
            &catalog,
            "rat",
            "rat-2007",
            &[DaggerEvidence {
                id: "rat.health".to_string(),
                value: 12,
            }],
        )
        .expect("spawn rat");
        fresh
    };
    let award = dagger_rpg::award_kill_progression(
        &mut progression_state,
        &catalog,
        "player",
        "rat-2007",
        &[],
    )
    .expect("rat kill award")
    .expect("rat carries an xpReward");
    let xp_level = dagger_rpg::evaluate_derived_rule_live(
        &progression_state,
        &catalog,
        "xp-level",
        "player",
        &[],
        &[],
    )
    .expect("live xp-level evaluation");
    let progression = serde_json::json!({
        "award": award,
        "live": {
            "xp": dagger_rpg::live_stat_base(&progression_state, "player", dagger_rpg::XP_STAT_ID)
                .expect("live xp base"),
            "level": dagger_rpg::live_stat_base(&progression_state, "player", dagger_rpg::LEVEL_STAT_ID)
                .expect("live level base"),
            "xpLevelThresholds": xp_level,
            "xpPerLevel": dagger_rpg::xp_level_divisor(&catalog).expect("xp-level divisor"),
        }
    });
    println!(
        "{}",
        serde_json::to_string_pretty(&serde_json::json!({ "progression": progression }))
            .expect("serialize progression readout")
    );
}
