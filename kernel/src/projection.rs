use crate::model::DaggerProductAuthority;
use dagger_runtime::{
    InventoryGridOccupant, InventoryItemReadout, InventoryStackReadout, PlayerInventoryReadout,
    ProductNoticeKind, ProductNoticeRecord,
};
use rusty_engine::product_kernel::serde_json::{json, Value};

fn ui_item_from_unique(item: &InventoryItemReadout) -> Value {
    json!({
        "entity": item.entity.to_string(),
        "id": item.item,
        "quantity": 1,
        "compatibleSlots": item.compatible_slots,
        "detail": item.equip_slot,
    })
}

fn ui_item_from_stack(stack: &InventoryStackReadout) -> Value {
    json!({
        "entity": Value::Null,
        "id": stack.item,
        "quantity": stack.quantity,
        "compatibleSlots": [],
        "detail": Value::Null,
    })
}

fn ui_item_for_occupant(
    occupant: &Option<InventoryGridOccupant>,
    inventory: &PlayerInventoryReadout,
) -> Option<Value> {
    match occupant.as_ref()? {
        InventoryGridOccupant::Item { entity } => inventory
            .items
            .iter()
            .find(|item| item.entity == *entity)
            .map(ui_item_from_unique),
        InventoryGridOccupant::Stack { item } => inventory
            .stacks
            .iter()
            .find(|stack| stack.item == *item)
            .map(ui_item_from_stack),
    }
}

fn ui_notice_kind(kind: ProductNoticeKind) -> &'static str {
    match kind {
        ProductNoticeKind::CapacityRejected | ProductNoticeKind::InventoryDropRejected => "warning",
        ProductNoticeKind::LevelUp => "success",
        ProductNoticeKind::MaterialIneffective
        | ProductNoticeKind::EmptyContainer
        | ProductNoticeKind::Observation
        | ProductNoticeKind::DebugNavigation => "info",
    }
}

fn ui_notice(notice: &ProductNoticeRecord) -> Value {
    json!({
        "id": notice.sequence.to_string(),
        "message": notice.message,
        "kind": ui_notice_kind(notice.kind),
    })
}

fn emission_rgb(style: &Value) -> Result<Value, crate::adapter::KernelError> {
    let Some(emission) = style.get("emissionColor") else {
        return Ok(json!([0.0, 0.0, 0.0]));
    };
    let values =
        emission
            .as_array()
            .ok_or_else(|| crate::adapter::KernelError::InvalidResource {
                resource: "content/projects/privateers-hold.project.json".to_owned(),
                detail: "material emissionColor must be authored RGBA".to_owned(),
            })?;
    if values.len() != 4 {
        return Err(crate::adapter::KernelError::InvalidResource {
            resource: "content/projects/privateers-hold.project.json".to_owned(),
            detail: "material emissionColor must contain exactly four channels".to_owned(),
        });
    }
    let rgb = values[..3]
        .iter()
        .map(|value| value.as_f64().filter(|value| value.is_finite()))
        .collect::<Option<Vec<_>>>()
        .ok_or_else(|| crate::adapter::KernelError::InvalidResource {
            resource: "content/projects/privateers-hold.project.json".to_owned(),
            detail: "material emissionColor RGB channels must be finite numbers".to_owned(),
        })?;
    Ok(json!(rgb))
}
use rusty_engine::{
    product_kernel::ProductRuntimeResources,
    render_model::{
        Geometry, Material, RenderDiff, RenderFrameDiff, RenderHandle, RenderLayer, RenderMetadata,
        RenderNode, Transform,
    },
    runtime_composition::{ProductRuntimeOutputs, ProductRuntimeUi},
};

// ProductDevRuntime wraps this frame in a bounded output envelope. Keeping
// retained JSON below 192 KiB leaves substantial deterministic headroom under
// its 256 KiB output ceiling without importing the host crate into the kernel.
const MAX_INITIAL_FRAME_BYTES: usize = 192 * 1024;

pub fn dagger_ui_projection(
    a: &DaggerProductAuthority,
    create: bool,
    initial_offset: usize,
) -> Result<(ProductRuntimeOutputs<Value>, usize, bool), String> {
    let readout = a
        .runtime
        .product_readout()
        .map_err(|error| error.to_string())?;
    let loot = readout
        .open_loot_container_id
        .as_ref()
        .and_then(|id| {
            readout
                .loot_containers
                .iter()
                .find(|container| &container.id == id)
        })
        .map(|container| {
            json!({
                "containerId": container.id,
                "revision": container.source_inventory_revision,
                "items": container
                    .contents
                    .items
                    .iter()
                    .take(64)
                    .map(ui_item_from_unique)
                    .chain(container.contents.stacks.iter().take(64).map(ui_item_from_stack))
                    .take(64)
                    .collect::<Vec<_>>(),
                "message": if container.emptied { Some("This container is empty.") } else { None::<&str> },
            })
        })
        .unwrap_or(Value::Null);
    let slots = readout
        .inventory_grid
        .slots
        .iter()
        .take(50)
        .map(|slot| {
            json!({
                "index": slot.index,
                "item": ui_item_for_occupant(&slot.occupant, &readout.player_inventory),
            })
        })
        .collect::<Vec<_>>();
    let equipment = readout
        .player_inventory
        .items
        .iter()
        .filter_map(|item| {
            item.equip_slot
                .as_ref()
                .map(|slot| json!({"id":slot,"item":ui_item_from_unique(item)}))
        })
        .take(9)
        .collect::<Vec<_>>();
    let receipt = readout.equipment_log.last().map(|record| json!({"accepted":record.accepted,"message":record.reason.clone().unwrap_or_else(|| record.operation.clone())}));
    let attributes = readout
        .player_stats
        .evaluated_attributes
        .iter()
        .take(32)
        .map(|(label, value)| json!({"label":label,"value":value}))
        .collect::<Vec<_>>();
    let skills = readout
        .player_stats
        .modeled_skills
        .iter()
        .take(32)
        .map(|(label, value)| json!({"label":label,"value":value}))
        .collect::<Vec<_>>();
    let notices = readout
        .notices
        .iter()
        .skip(readout.notices.len().saturating_sub(7))
        .map(ui_notice)
        .collect::<Vec<_>>();
    let ui = json!({
        "hud":{"health":{"current":readout.current_health,"maximum":readout.max_health},"stamina":{"current":readout.player_stats.current_stamina,"maximum":readout.player_stats.max_stamina},"magicka":{"current":readout.player_stats.current_magicka,"maximum":readout.player_stats.max_magicka},"level":readout.progression.level,"experience":readout.progression.xp,"experienceToNext":readout.progression.xp_to_next_level,"notices":notices},
        "inventory":{"gridRevision":readout.inventory_grid.revision,"capacity":readout.player_inventory.capacity.iter().take(32).map(|capacity| json!({"label":capacity.metric,"used":capacity.used,"maximum":capacity.maximum})).collect::<Vec<_>>(),"slots":slots,"equipmentRevision":readout.player_inventory.equipment_revision,"equipment":equipment,"receipt":receipt},
        "character":{"attributes":attributes,"skills":skills},
        "loot":loot,
        "debug":{"failedInventoryDropMessages":a.runtime.failed_inventory_drop_messages_enabled().map_err(|error| error.to_string())?}
    });
    let handle = RenderHandle::new(900001);
    let transform = Transform {
        translation: readout.player_position,
        rotation: Transform::IDENTITY.rotation,
        scale: [0.35, 0.8, 0.35],
    };
    let operation = if create {
        RenderDiff::Create {
            handle,
            parent: None,
            node: RenderNode {
                geometry: Geometry::Cube,
                material: Material {
                    color: [0.18, 0.55, 0.92, 1.0],
                    wireframe: false,
                },
                transform,
                visible: true,
                layer: RenderLayer::Scene,
                metadata: RenderMetadata {
                    source_entity: None,
                    source_scene_node: None,
                    tags: vec!["dagger-player".to_owned()],
                    label: Some("Dagger player".to_owned()),
                },
            },
        }
    } else {
        RenderDiff::Update {
            handle,
            transform: Some(transform),
            material: None,
            visible: None,
            metadata: None,
        }
    };
    let mut operations = if create {
        a.static_scene_ops.clone()
    } else {
        Vec::new()
    };
    for content in readout.content.iter().take(64) {
        let handle = RenderHandle::new(1_000_000u64.saturating_add(content.id));
        let transform = Transform {
            translation: content.live.position,
            rotation: Transform::IDENTITY.rotation,
            scale: [0.5, 1.0, 0.5],
        };
        operations.push(if create {
            RenderDiff::Create {
                handle,
                parent: None,
                node: RenderNode {
                    geometry: Geometry::Cube,
                    material: Material {
                        color: if content.kind == "enemy" {
                            [0.78, 0.22, 0.18, 1.0]
                        } else {
                            [0.72, 0.56, 0.18, 1.0]
                        },
                        wireframe: false,
                    },
                    transform,
                    visible: true,
                    layer: RenderLayer::Scene,
                    metadata: RenderMetadata {
                        source_entity: Some(content.id),
                        source_scene_node: None,
                        tags: vec![format!("dagger-{}", content.kind)],
                        label: Some(content.name.clone()),
                    },
                },
            }
        } else {
            RenderDiff::Update {
                handle,
                transform: Some(transform),
                material: None,
                visible: None,
                metadata: None,
            }
        });
    }
    operations.push(operation);
    // Static project ops are admitted immutable data; live content is a
    // fixed authored entity set whose handle order is its stable content id.
    // Rebuilding while the prefix drains therefore cannot shift an offset or
    // duplicate a handle; a later create simply uses the newest authoritative
    // transform for an entity the renderer has not seen yet.
    let total = operations.len();
    let (operations, next_offset, complete) = if create {
        let mut prefix = Vec::new();
        let mut next = initial_offset;
        while next < total {
            let mut candidate = prefix.clone();
            candidate.push(operations[next].clone());
            let frame = RenderFrameDiff::try_from_ops(candidate.clone())
                .map_err(|error| format!("frame {error:?}"))?;
            if frame
                .encode_json()
                .map_err(|error| error.to_string())?
                .len()
                > MAX_INITIAL_FRAME_BYTES
            {
                if prefix.is_empty() {
                    return Err("one retained create exceeds bounded frame quota".to_owned());
                }
                break;
            }
            prefix = candidate;
            next += 1;
        }
        (prefix, next, next == total)
    } else {
        (operations, initial_offset, true)
    };
    let frame =
        RenderFrameDiff::try_from_ops(operations).map_err(|error| format!("frame {error:?}"))?;
    let output = ProductRuntimeOutputs::new(
        vec![ProductRuntimeUi::new("dagger.ui", "dagger.ui.v1", ui)],
        Some(frame),
        None,
    )
    .map_err(|error| error.to_string())?;
    Ok((output, next_offset, complete))
}

/// Decode only retained, immutable project presentation definitions from the
/// admitted project resource. All texture identities stay content-addressed;
/// the Engine host preloads those declared resource bytes before this frame.
pub fn static_scene_ops(
    resources: ProductRuntimeResources<'_>,
) -> Result<Vec<RenderDiff>, crate::adapter::KernelError> {
    let project = resources
        .resource("content/projects/privateers-hold.project.json")
        .ok_or_else(|| {
            crate::adapter::KernelError::MissingResource(
                "content/projects/privateers-hold.project.json".to_owned(),
            )
        })?;
    let root: Value =
        rusty_engine::product_kernel::serde_json::from_slice(project).map_err(|error| {
            crate::adapter::KernelError::InvalidResource {
                resource: "content/projects/privateers-hold.project.json".to_owned(),
                detail: format!("project JSON: {error}"),
            }
        })?;
    let object = root
        .as_object()
        .ok_or_else(|| crate::adapter::KernelError::InvalidResource {
            resource: "content/projects/privateers-hold.project.json".to_owned(),
            detail: "project root is not an object".to_owned(),
        })?;
    let mut values = Vec::new();
    for asset in object
        .get("assets")
        .and_then(Value::as_array)
        .into_iter()
        .flatten()
    {
        if let Some(texture) = asset.get("texture") {
            let catalog = asset.get("catalog").unwrap_or(&Value::Null);
            let hash = catalog.get("hash").and_then(Value::as_str).ok_or_else(|| {
                crate::adapter::KernelError::InvalidResource {
                    resource: "content/projects/privateers-hold.project.json".to_owned(),
                    detail: "texture hash missing".to_owned(),
                }
            })?;
            let path = catalog
                .get("sourcePath")
                .and_then(Value::as_str)
                .ok_or_else(|| crate::adapter::KernelError::InvalidResource {
                    resource: "content/projects/privateers-hold.project.json".to_owned(),
                    detail: "texture path missing".to_owned(),
                })?;
            let bytes = resources
                .resource(path)
                .ok_or_else(|| crate::adapter::KernelError::MissingResource(path.to_owned()))?;
            values.push(json!({"op":"defineTexture","texture":{"id":asset.get("id").and_then(Value::as_str).unwrap_or("texture/missing"),"width":texture.get("width").and_then(Value::as_u64).unwrap_or(1),"height":texture.get("height").and_then(Value::as_u64).unwrap_or(1),"filter":texture.get("filter").and_then(Value::as_str).unwrap_or("nearest"),"wrap":texture.get("wrap").and_then(Value::as_str).unwrap_or("repeat"),"contentHash":format!("sha256:{hash}"),"version":catalog.get("version").and_then(Value::as_u64).unwrap_or(1),"payload":{"encoding":"pngRgba8","colorSpace":"srgb","contentHash":format!("sha256:{hash}"),"byteLength":bytes.len(),"source":{"kind":"resource","resource":format!("texture-resource/{hash}")}}}}));
        }
        if let Some(material) = asset.get("material") {
            let style = material.get("style").unwrap_or(&Value::Null);
            let emission_color = emission_rgb(style)?;
            values.push(json!({"op":"defineMaterial","material":{
                "schemaVersion":1,
                "id":asset.get("id").and_then(Value::as_str).unwrap_or("material/default"),
                "color":style.get("color").cloned().unwrap_or_else(||json!([0.7,0.7,0.7,1.0])),
                "texture":style.get("texture").and_then(|value| value.get("id")).cloned().unwrap_or(Value::Null),
                "roughness":style.get("roughness").and_then(Value::as_f64).unwrap_or(1.0),
                "textureTint":style.get("textureTint").cloned().unwrap_or_else(||json!([1.0,1.0,1.0,1.0])),
                "emissionColor":emission_color,
                "emissionIntensity":style.get("emissive").and_then(Value::as_f64).unwrap_or(0.0),
                "uvStrategy":style.get("uvStrategy").and_then(Value::as_str).unwrap_or("flat")
            }}));
        }
        if let Some(mesh) = asset.get("staticMesh") {
            values.push(json!({"op":"defineStaticMesh","asset":mesh}));
        }
    }
    let entry = object.get("entryScene").and_then(Value::as_str);
    let scene = object
        .get("scenes")
        .and_then(Value::as_array)
        .and_then(|scenes| {
            entry
                .and_then(|id| {
                    scenes
                        .iter()
                        .find(|scene| scene.get("id").and_then(Value::as_str) == Some(id))
                })
                .or_else(|| scenes.first())
        })
        .ok_or_else(|| crate::adapter::KernelError::InvalidResource {
            resource: "content/projects/privateers-hold.project.json".to_owned(),
            detail: "project has no scene".to_owned(),
        })?;
    for entity in scene
        .get("entities")
        .and_then(Value::as_array)
        .into_iter()
        .flatten()
    {
        let Some(renderable) = entity.get("renderable") else {
            continue;
        };
        if renderable.get("visible").and_then(Value::as_bool) != Some(true) {
            continue;
        }
        let id = entity.get("id").and_then(Value::as_u64).ok_or_else(|| {
            crate::adapter::KernelError::InvalidResource {
                resource: "content/projects/privateers-hold.project.json".to_owned(),
                detail: "render entity has no id".to_owned(),
            }
        })?;
        let asset = renderable
            .get("asset")
            .and_then(Value::as_str)
            .ok_or_else(|| crate::adapter::KernelError::InvalidResource {
                resource: "content/projects/privateers-hold.project.json".to_owned(),
                detail: "render entity has no asset".to_owned(),
            })?;
        values.push(json!({"op":"createStaticMeshInstance","handle":id,"parent":null,"instance":{"asset":asset,"transform":{"translation":entity.get("translation").cloned().unwrap_or_else(||json!([0.0,0.0,0.0])),"rotation":[0.0,0.0,0.0,1.0],"scale":[1.0,1.0,1.0]},"visible":true,"materialOverrides":[],"metadata":{"sourceEntity":id,"sourceSceneNode":id,"tags":["dagger-static"],"label":entity.get("name").cloned().unwrap_or(Value::Null)}}}));
    }
    values
        .into_iter()
        .map(|value| {
            rusty_engine::product_kernel::serde_json::from_value(value).map_err(|error| {
                crate::adapter::KernelError::InvalidResource {
                    resource: "content/projects/privateers-hold.project.json".to_owned(),
                    detail: format!("retained project frame: {error}"),
                }
            })
        })
        .collect()
}

#[cfg(test)]
mod tests {
    use super::{dagger_ui_projection, static_scene_ops};
    use crate::model::DaggerProductAuthority;
    use dagger_runtime::{DaggerRuntime, InventoryGridOccupant};
    use rusty_engine::{
        product_kernel::serde_json,
        product_kernel::{ProductRuntimeResource, ProductRuntimeResources},
        render_model::{
            Geometry, Material, RenderDiff, RenderFrameDiff, RenderHandle, RenderLayer,
            RenderMetadata, RenderNode, Transform,
        },
    };

    #[test]
    fn texture_projection_uses_the_admitted_content_address_identity() {
        let project = br#"{"entryScene":"scene/hold","assets":[{"id":"texture/test","catalog":{"version":1,"hash":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","sourcePath":"content/textures/test.png"},"texture":{"width":1,"height":1,"filter":"nearest","wrap":"repeat"}}],"scenes":[{"id":"scene/hold","entities":[]}]}
        "#;
        let resources = [
            ProductRuntimeResource::new("content/projects/privateers-hold.project.json", project),
            ProductRuntimeResource::new("content/textures/test.png", b"png"),
        ];
        let ops =
            static_scene_ops(ProductRuntimeResources::new(b"{}", &resources)).expect("frame ops");
        assert_eq!(ops.len(), 1);
        let value = serde_json::to_value(&ops[0]).expect("serialize op");
        assert_eq!(value["op"], "defineTexture");
        assert_eq!(
            value["texture"]["payload"]["source"]["resource"],
            "texture-resource/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
        );
        assert_eq!(value["texture"]["payload"]["byteLength"], 3);
    }

    #[test]
    fn authored_rgba_emission_projects_to_a_valid_material_and_static_mesh_frame() {
        let project = br#"{
          "entryScene":"scene/hold",
          "assets":[
            {"id":"material/plain","material":{"style":{"color":[0.7,0.6,0.5,1.0],"texture":null,"textureTint":[1.0,1.0,1.0,1.0],"emissionColor":[0.1,0.2,0.3,0.9],"roughness":1.0,"emissive":0.0,"uvStrategy":"flat"}}},
            {"id":"mesh/test","staticMesh":{"asset":"mesh/test","payload":{"layout":{"vertexCount":3,"indexCount":3,"indexWidth":"u32","attributes":[{"name":"position","components":3,"kind":"f32"},{"name":"normal","components":3,"kind":"f32"}]},"groups":[{"materialSlot":0,"start":0,"count":3}],"bounds":{"min":[0.0,0.0,0.0],"max":[1.0,1.0,0.0]},"source":{"kind":"inline","positions":[0.0,0.0,0.0,1.0,0.0,0.0,0.0,1.0,0.0],"normals":[0.0,0.0,1.0,0.0,0.0,1.0,0.0,0.0,1.0],"uvs":null,"colors":null,"indices":[0,1,2]},"provenance":"staticAsset"},"materialSlots":[{"slot":0,"material":"material/plain"}],"collision":{"kind":"aabbFallback"}}}
          ],
          "scenes":[{"id":"scene/hold","entities":[{"id":7,"name":"test mesh","translation":[0.0,0.0,0.0],"renderable":{"visible":true,"asset":"mesh/test"}}]}]
        }"#;
        let resources = [ProductRuntimeResource::new(
            "content/projects/privateers-hold.project.json",
            project,
        )];
        let ops =
            static_scene_ops(ProductRuntimeResources::new(b"{}", &resources)).expect("frame ops");
        let frame = RenderFrameDiff::try_from_ops(ops.clone()).expect("current render schema");
        frame.validate().expect("validated retained frame");
        let material = serde_json::to_value(&ops[0]).expect("material op");
        let emission = material["material"]["emissionColor"]
            .as_array()
            .expect("RGB emission array");
        assert_eq!(emission.len(), 3);
        assert!((emission[0].as_f64().expect("red") - 0.1).abs() < 0.000_001);
        assert!((emission[1].as_f64().expect("green") - 0.2).abs() < 0.000_001);
        assert!((emission[2].as_f64().expect("blue") - 0.3).abs() < 0.000_001);
        assert_eq!(ops.len(), 3, "material, mesh definition, instance");
    }

    #[test]
    fn dagger_ui_v1_emits_authoritative_meter_and_inventory_shapes() {
        let runtime = DaggerRuntime::from_product_resources(
            include_bytes!("../dagger-runtime/tests/fixtures/privateers-hold.project.json"),
            include_bytes!("../dagger-runtime/tests/fixtures/privateers-hold.navgrid.json"),
            include_bytes!("../dagger-runtime/tests/fixtures/privateers-hold.encounters.json"),
            include_bytes!("../dagger-runtime/tests/fixtures/dagger-core.package.json"),
        )
        .expect("admitted runtime fixture");
        let (output, _, _) = dagger_ui_projection(
            &DaggerProductAuthority {
                runtime,
                revision: 0,
                static_scene_ops: Vec::new(),
            },
            false,
            0,
        )
        .expect("projection");
        let ui = output.ui().first().expect("dagger.ui").ui();
        for meter in ["health", "stamina", "magicka"] {
            assert!(ui["hud"][meter]["current"].is_number(), "{meter} current");
            assert!(ui["hud"][meter]["maximum"].is_number(), "{meter} maximum");
            assert!(ui["hud"][meter]["maximum"].as_f64().expect("maximum") > 0.0);
        }
        assert!(ui["hud"]["health"]["current"].as_f64().expect("health") > 0.0);
        assert!(ui["inventory"]["slots"].is_array());
        assert!(ui["inventory"]["capacity"].is_array());
        assert!(ui["inventory"]["equipment"].is_array());
    }

    #[test]
    fn dagger_ui_inventory_slots_project_full_authoritative_item_facts() {
        let authority = DaggerProductAuthority {
            runtime: DaggerRuntime::from_product_resources(
                include_bytes!("../dagger-runtime/tests/fixtures/privateers-hold.project.json"),
                include_bytes!("../dagger-runtime/tests/fixtures/privateers-hold.navgrid.json"),
                include_bytes!("../dagger-runtime/tests/fixtures/privateers-hold.encounters.json"),
                include_bytes!("../dagger-runtime/tests/fixtures/dagger-core.package.json"),
            )
            .expect("admitted runtime fixture"),
            revision: 0,
            static_scene_ops: Vec::new(),
        };
        let readout = authority
            .runtime
            .product_readout()
            .expect("authoritative readout");
        let (output, _, _) = dagger_ui_projection(&authority, false, 0).expect("projection");
        let slots = output.ui().first().expect("dagger.ui").ui()["inventory"]["slots"]
            .as_array()
            .expect("inventory slots");

        let mut occupied = 0;
        for slot in &readout.inventory_grid.slots {
            let projected = slots
                .iter()
                .find(|candidate| candidate["index"] == slot.index)
                .expect("every authoritative slot is projected");
            let item = &projected["item"];
            match slot.occupant.as_ref() {
                Some(InventoryGridOccupant::Item { entity }) => {
                    let expected = readout
                        .player_inventory
                        .items
                        .iter()
                        .find(|candidate| candidate.entity == *entity)
                        .expect("grid item resolves to an authoritative inventory item");
                    occupied += 1;
                    assert_eq!(item["entity"].as_str(), Some(entity.to_string().as_str()));
                    assert_eq!(item["id"].as_str(), Some(expected.item.as_str()));
                    assert_eq!(item["quantity"].as_u64(), Some(1));
                    assert_eq!(
                        item["compatibleSlots"],
                        serde_json::to_value(&expected.compatible_slots).expect("slot facts")
                    );
                    assert!(item["detail"].is_null());
                    assert!(
                        item.get("kind").is_none(),
                        "UI receives no raw occupant tag"
                    );
                }
                Some(InventoryGridOccupant::Stack { item: item_id }) => {
                    let expected = readout
                        .player_inventory
                        .stacks
                        .iter()
                        .find(|candidate| candidate.item == *item_id)
                        .expect("grid stack resolves to an authoritative inventory stack");
                    occupied += 1;
                    assert!(item["entity"].is_null());
                    assert_eq!(item["id"].as_str(), Some(expected.item.as_str()));
                    assert_eq!(item["quantity"].as_u64(), Some(expected.quantity));
                    assert_eq!(item["compatibleSlots"], serde_json::json!([]));
                    assert!(item["detail"].is_null());
                    assert!(
                        item.get("kind").is_none(),
                        "UI receives no raw occupant tag"
                    );
                }
                None => assert!(item.is_null(), "empty slots remain empty"),
            }
        }
        assert!(
            occupied > 0,
            "fixture contains a carried item for the UI contract"
        );
    }

    #[test]
    fn dagger_ui_projects_one_structured_failed_drop_notice_only_when_enabled() {
        let mut authority = DaggerProductAuthority {
            runtime: DaggerRuntime::from_product_resources(
                include_bytes!("../dagger-runtime/tests/fixtures/privateers-hold.project.json"),
                include_bytes!("../dagger-runtime/tests/fixtures/privateers-hold.navgrid.json"),
                include_bytes!("../dagger-runtime/tests/fixtures/privateers-hold.encounters.json"),
                include_bytes!("../dagger-runtime/tests/fixtures/dagger-core.package.json"),
            )
            .expect("admitted runtime fixture"),
            revision: 0,
            static_scene_ops: Vec::new(),
        };
        let initial = authority
            .runtime
            .product_readout()
            .expect("initial readout");
        let empty_slot = initial
            .inventory_grid
            .slots
            .iter()
            .find(|slot| slot.occupant.is_none())
            .expect("fixture leaves an empty inventory slot")
            .index;
        let rejected_move = serde_json::json!({
            "sourceSlot": empty_slot,
            "targetSlot": 0,
            "expectedRevision": initial.inventory_grid.revision,
        });

        authority
            .runtime
            .move_inventory_item(&rejected_move)
            .expect("disabled failed-drop diagnostic remains a valid rejection");
        let (output, _, _) = dagger_ui_projection(&authority, false, 0).expect("projection");
        assert_eq!(
            output.ui().first().expect("dagger.ui").ui()["hud"]["notices"]
                .as_array()
                .expect("notice list")
                .len(),
            0,
            "disabled failed-drop diagnostics do not enter the UI aggregate"
        );

        let after_rejection = authority
            .runtime
            .product_readout()
            .expect("rejection readout");
        authority
            .runtime
            .apply_settings_update(&serde_json::json!({
                "schemaVersion": 1,
                "expectedRevision": after_rejection.settings.revision,
                "changes": [{
                    "id": "debug.failedInventoryDropMessages",
                    "value": true,
                }],
            }))
            .expect("enable failed-drop diagnostics through Rust settings");
        authority
            .runtime
            .move_inventory_item(&rejected_move)
            .expect("enabled failed-drop diagnostic remains a valid rejection");

        let readout = authority
            .runtime
            .product_readout()
            .expect("enabled readout");
        let expected = readout.notices.last().expect("failed-drop notice");
        let expected_id = expected.sequence.to_string();
        let (output, _, _) = dagger_ui_projection(&authority, false, 0).expect("projection");
        let notices = output.ui().first().expect("dagger.ui").ui()["hud"]["notices"]
            .as_array()
            .expect("notice list");
        assert_eq!(
            notices.len(),
            1,
            "one enabled failed-drop notice is projected"
        );
        assert_eq!(notices[0]["id"].as_str(), Some(expected_id.as_str()));
        assert_eq!(
            notices[0]["message"].as_str(),
            Some(expected.message.as_str())
        );
        assert_eq!(notices[0]["kind"].as_str(), Some("warning"));

        let (repeat, _, _) = dagger_ui_projection(&authority, false, 0).expect("repeat projection");
        let repeated_notices = repeat.ui().first().expect("dagger.ui").ui()["hud"]["notices"]
            .as_array()
            .expect("repeat notice list");
        assert_eq!(
            repeated_notices, notices,
            "projection does not duplicate notices"
        );
    }

    #[test]
    fn initial_retained_frame_drains_in_bounded_deterministic_prefixes() {
        let runtime = DaggerRuntime::from_product_resources(
            include_bytes!("../dagger-runtime/tests/fixtures/privateers-hold.project.json"),
            include_bytes!("../dagger-runtime/tests/fixtures/privateers-hold.navgrid.json"),
            include_bytes!("../dagger-runtime/tests/fixtures/privateers-hold.encounters.json"),
            include_bytes!("../dagger-runtime/tests/fixtures/dagger-core.package.json"),
        )
        .expect("admitted runtime fixture");
        let static_scene_ops = (0..1200)
            .map(|id| RenderDiff::Create {
                handle: RenderHandle::new(2_000_000 + id),
                parent: None,
                node: RenderNode {
                    geometry: Geometry::Cube,
                    material: Material::DEFAULT,
                    transform: Transform::IDENTITY,
                    visible: true,
                    layer: RenderLayer::Scene,
                    metadata: RenderMetadata {
                        source_entity: None,
                        source_scene_node: None,
                        tags: vec!["bounded".to_owned()],
                        label: None,
                    },
                },
            })
            .collect();
        let authority = DaggerProductAuthority {
            runtime,
            revision: 0,
            static_scene_ops,
        };
        let mut offset = 0;
        let mut seen = std::collections::BTreeSet::new();
        let mut complete = false;
        while !complete {
            let (output, next, done) =
                dagger_ui_projection(&authority, true, offset).expect("projection prefix");
            assert!(
                !output.ui().is_empty(),
                "UI publishes while retained frame drains"
            );
            let frame = output.render().expect("retained prefix");
            assert!(frame.encode_json().expect("encoded frame").len() < 192 * 1024);
            for operation in &frame.ops {
                if let RenderDiff::Create { handle, .. } = operation {
                    assert!(seen.insert(handle.raw()), "no duplicate create handles");
                }
            }
            assert!(next > offset, "each initial projection drains work");
            offset = next;
            complete = done;
        }
    }
}
