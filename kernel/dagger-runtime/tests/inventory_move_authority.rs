use dagger_runtime::{DaggerRuntime, ProductNoticeKind};
use rusty_engine::product_kernel::serde_json::json;

const PROJECT: &[u8] = include_bytes!("fixtures/privateers-hold.project.json");
const NAVGRID: &[u8] = include_bytes!("fixtures/privateers-hold.navgrid.json");
const ENCOUNTERS: &[u8] = include_bytes!("fixtures/privateers-hold.encounters.json");
const GAMEPLAY: &[u8] = include_bytes!("fixtures/dagger-core.package.json");

fn runtime() -> DaggerRuntime {
    DaggerRuntime::from_product_resources(PROJECT, NAVGRID, ENCOUNTERS, GAMEPLAY)
        .expect("immutable admitted product resources construct the runtime")
}

#[test]
fn rejected_inventory_moves_preserve_grid_and_only_emit_debug_notice_when_enabled() {
    let mut runtime = runtime();
    let initial = runtime.product_readout().expect("initial readout");
    let empty_slot = initial
        .inventory_grid
        .slots
        .iter()
        .find(|slot| slot.occupant.is_none())
        .expect("admitted starter inventory leaves an empty grid slot")
        .index;
    let rejected_move = json!({
        "sourceSlot": empty_slot,
        "targetSlot": 0,
        "expectedRevision": initial.inventory_grid.revision,
    });

    runtime
        .move_inventory_item(&rejected_move)
        .expect("a rejected drop is a valid runtime intent");
    let default_rejection = runtime.product_readout().expect("post-rejection readout");
    assert_eq!(default_rejection.inventory_grid, initial.inventory_grid);
    assert!(default_rejection
        .notices
        .iter()
        .all(|notice| notice.kind != ProductNoticeKind::InventoryDropRejected));

    runtime
        .apply_settings_update(&json!({
            "schemaVersion": 1,
            "expectedRevision": default_rejection.settings.revision,
            "changes": [{
                "id": "debug.failedInventoryDropMessages",
                "value": true,
            }],
        }))
        .expect("enable failed-drop diagnostics through Rust settings authority");
    runtime
        .move_inventory_item(&rejected_move)
        .expect("the same rejected intent remains a valid operation");
    let debug_rejection = runtime.product_readout().expect("debug rejection readout");
    assert_eq!(debug_rejection.inventory_grid, initial.inventory_grid);
    assert!(debug_rejection.notices.iter().any(|notice| {
        notice.kind == ProductNoticeKind::InventoryDropRejected
            && notice.message.contains("source slot")
    }));
}
