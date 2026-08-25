use serde_json::{json, Value};

use crate::{
    presentation::{projection, texture_resources},
    protocol::OpenProject,
};

pub(crate) fn make_readout(open: &OpenProject) -> Value {
    let project = open.project.as_object().expect("validated project object");
    let scene_id = project
        .get("entryScene")
        .and_then(Value::as_str)
        .unwrap_or("scene/privateers-hold");
    let scene = project
        .get("scenes")
        .and_then(Value::as_array)
        .and_then(|scenes| {
            scenes
                .iter()
                .find(|scene| scene.get("id").and_then(Value::as_str) == Some(scene_id))
        })
        .or_else(|| {
            project
                .get("scenes")
                .and_then(Value::as_array)
                .and_then(|scenes| scenes.first())
        })
        .expect("runtime admitted a scene");
    let entities = scene
        .get("entities")
        .and_then(Value::as_array)
        .cloned()
        .unwrap_or_default();
    let assets = project
        .get("assets")
        .and_then(Value::as_array)
        .cloned()
        .unwrap_or_default();
    let project_json = open.project_text.clone();
    let asset_catalog_json =
        serde_json::to_string(project.get("assets").unwrap_or(&Value::Array(vec![]))).unwrap();
    let authored_scene_json = serde_json::to_string(scene).unwrap();
    let entity_state_json = serde_json::to_string(&entities).unwrap();
    let content_manifest_json = serde_json::to_string(&json!({
        "projectHash": open.project_hash,
        "meshSources": ["authoring-content/privateers-hold.mesh.json", "authoring-content/privateers-hold.glb"],
    }))
    .unwrap();
    let visible_mesh_count = entities
        .iter()
        .filter(|entity| {
            entity
                .get("renderable")
                .and_then(|renderable| renderable.get("visible"))
                .and_then(Value::as_bool)
                == Some(true)
        })
        .count();
    let light_count = entities
        .iter()
        .filter(|entity| entity.get("light").is_some())
        .count();
    let scene_nodes = entities
        .iter()
        .enumerate()
        .map(|(index, entity)| hierarchy_node(entity, index))
        .collect::<Vec<_>>();
    let scene_name = scene
        .get("name")
        .and_then(Value::as_str)
        .unwrap_or(scene_id);
    let projection = projection(&open.root, project, &entities);
    json!({
        "identity": {
            "projectId": project.get("projectId").and_then(Value::as_str).unwrap_or("privateers-hold"),
            "name": project.get("name").and_then(Value::as_str).unwrap_or("Privateer's Hold"),
            "entryScene": scene_id,
            "sourceSchemaVersion": project.get("schemaVersion").and_then(Value::as_u64).unwrap_or(24),
            "currentSchemaVersion": 24,
            "projectHash": open.project_hash,
            "sceneRevision": 1,
            "relativeProjectFile": open.relative_project_file,
        },
        "canonical": {
            "projectJson": project_json,
            "assetCatalogJson": asset_catalog_json,
            "authoredSceneJson": authored_scene_json,
            "entityStateJson": entity_state_json,
            "contentManifestJson": content_manifest_json,
        },
        "inspections": inspections(&assets, scene_name, &entities),
        "sceneHierarchy": {
            "sceneId": 1,
            "revision": 1,
            "name": scene.get("name").and_then(Value::as_str),
            "rootNodeIds": entities.iter().filter_map(|entity| entity.get("id").and_then(Value::as_u64)).collect::<Vec<_>>(),
            "nodes": scene_nodes,
        },
        "assetBrowser": asset_browser(&assets),
        "voxelAuthoring": { "assets": [], "instances": [], "materials": [] },
        "voxelSurfaceAuthoring": { "textures": [], "atlases": [], "materials": [] },
        "voxelObjectAuthoring": { "assets": [], "instances": [] },
        "animatedMeshResources": [],
        "textureResources": texture_resources(&open.root, project),
        "entityComponents": [],
        "projection": projection,
        "projectionReadout": {
            "frameKind": "complete",
            "sourceRevision": 1,
            "retainedEntities": visible_mesh_count,
            "retainedLights": light_count,
            "retainedVoxelInstances": 0,
            "retainedVoxelChunks": 0,
            "diagnostics": [],
        },
    })
}

pub(crate) fn transform(entity: &Value) -> Value {
    let translation = entity
        .get("translation")
        .cloned()
        .unwrap_or_else(|| json!([0.0, 0.0, 0.0]));
    json!({ "translation": translation, "rotation": [0.0, 0.0, 0.0, 1.0], "scale": [1.0, 1.0, 1.0] })
}

fn hierarchy_node(entity: &Value, index: usize) -> Value {
    let id = entity
        .get("id")
        .and_then(Value::as_u64)
        .unwrap_or(index as u64 + 1);
    let kind = if entity.get("light").is_some() {
        "light"
    } else if entity
        .get("renderable")
        .and_then(|renderable| renderable.get("visible"))
        .and_then(Value::as_bool)
        == Some(true)
    {
        "staticMesh"
    } else if entity.get("playerController").is_some() {
        "bootstrap"
    } else {
        "entityInstance"
    };
    let asset = entity
        .get("renderable")
        .and_then(|renderable| renderable.get("asset"))
        .cloned()
        .unwrap_or(Value::Null);
    let t = transform(entity);
    json!({
        "nodeId": id,
        "parentNodeId": null,
        "childOrder": index,
        "displayOrder": index,
        "depth": 0,
        "nodeKind": kind,
        "label": entity.get("name").and_then(Value::as_str).unwrap_or("entity"),
        "tags": [],
        "asset": asset,
        "entityId": id,
        "localTransform": t,
        "worldTransform": t,
        "renderableTransform": t,
    })
}

fn asset_browser(assets: &[Value]) -> Value {
    let entries = assets
        .iter()
        .map(|asset| {
            let id = asset.get("id").and_then(Value::as_str).unwrap_or("asset");
            let catalog = asset.get("catalog").unwrap_or(&Value::Null);
            let kind = id.split('/').next().unwrap_or("asset");
            let dependencies = catalog
                .get("dependencies")
                .and_then(Value::as_array)
                .map(|items| {
                    items
                        .iter()
                        .filter_map(|item| {
                            item.get("id").and_then(Value::as_str).map(str::to_owned)
                        })
                        .collect::<Vec<_>>()
                })
                .unwrap_or_default();
            json!({
                "assetId": id,
                "kind": kind,
                "version": catalog.get("version").and_then(Value::as_u64).unwrap_or(1),
                "hash": catalog.get("hash").cloned().unwrap_or(Value::Null),
                "sourcePath": catalog.get("sourcePath").cloned().unwrap_or(Value::Null),
                "label": catalog.get("label").cloned().unwrap_or(Value::Null),
                "dependencies": dependencies,
                "dependents": [],
                "material": kind == "material",
                "importedMesh": kind == "mesh",
                "import": null,
            })
        })
        .collect::<Vec<_>>();
    let locks = entries
        .iter()
        .map(|entry| {
            json!({
                "assetId": entry["assetId"], "kind": entry["kind"], "version": entry["version"],
                "hash": entry["hash"], "dependencies": entry["dependencies"],
            })
        })
        .collect::<Vec<_>>();
    json!({ "assets": entries, "lockEntries": locks })
}

fn inspections(assets: &[Value], scene_name: &str, entities: &[Value]) -> Value {
    let entity_count = entities.len();
    let entity_ids = entities
        .iter()
        .filter_map(|entity| entity.get("id").and_then(Value::as_u64))
        .collect::<Vec<_>>();
    let dependency_count = assets
        .iter()
        .map(|asset| {
            asset
                .get("catalog")
                .and_then(|catalog| catalog.get("dependencies"))
                .and_then(Value::as_array)
                .map_or(0, Vec::len)
        })
        .sum::<usize>();
    json!({
        "catalog": { "entryCount": assets.len(), "dependencyCount": dependency_count, "kinds": [{"name":"material","count":assets.iter().filter(|a| a["id"].as_str().unwrap_or("").starts_with("material/")).count()},{"name":"mesh","count":assets.iter().filter(|a| a["id"].as_str().unwrap_or("").starts_with("mesh/")).count()}], "lock": {"entryCount": assets.len(), "findingCount": 0}, "diagnostics": {"diagnostics": []} },
        "scene": { "sceneId": 1, "revision": 1, "schemaVersion": 24, "name": scene_name, "nodeCount": entity_count, "rootCount": entity_count, "dependencyCount": assets.len(), "nodeKinds": [], "diagnostics": {"diagnostics": []} },
        "entityState": { "schemaVersion": 1, "revision": 1, "entityCount": entity_count, "lifecycle": [], "sources": [{"name":"authoredProject","count":entity_count}], "capabilities": [], "relationships": [], "entityIds": entity_ids, "diagnostics": {"diagnostics": []} },
        "persistence": { "schemaVersion": 1, "artifactCount": 1, "requiredArtifactCount": 1, "declaredByteCount": 0, "classes": [{"name":"project","count":1}], "roles": [{"name":"canonical","count":1}], "loadSteps": [{"stage":"project","path":"authoring-content/projects/privateers-hold.project.json"}], "diagnostics": {"diagnostics": []} },
    })
}
