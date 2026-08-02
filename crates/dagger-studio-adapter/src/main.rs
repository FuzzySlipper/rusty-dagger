//! Rusty Dagger's small, Rust-owned Studio protocol boundary.
//!
//! This is intentionally a read-only first seam.  Project admission is still
//! performed by `dagger-runtime`; the adapter only translates the admitted
//! authored project into the public Engine render contract.  Mutation
//! requests are rejected until a Dagger-owned authority for that mutation is
//! implemented rather than being silently accepted by a presentation layer.

use dagger_runtime::DaggerRuntime;
use serde_json::{json, Map, Value};
use sha2::{Digest, Sha256};
use std::fs;
use std::io::{self, BufRead, Write};
use std::path::{Component, Path, PathBuf};

const PROTOCOL_VERSION: u64 = 14;
const MAX_REQUEST_BYTES: usize = 256 * 1024;
const MAX_PROJECT_BYTES: usize = 64 * 1024 * 1024;
const ENGINE_REVISION: &str = "d52c9b0f3287f21eea81d465871978a117750d0c";

const OPERATIONS: &[&str] = &[
    "describe",
    "openProject",
    "createProject",
    "saveProjectAs",
    "readProject",
    "createScene",
    "renameScene",
    "deleteScene",
    "setEntryScene",
    "createSceneObject",
    "deleteSceneObject",
    "renameSceneObject",
    "reparentSceneObject",
    "setSceneObjectTransform",
    "setSceneObjectRenderableTransform",
    "setSceneObjectAppearance",
    "setEntityCollision",
    "setEntityKinematic",
    "setEntityTranslation",
    "upsertMaterial",
    "upsertVoxelSurfaceMaterial",
    "removeVoxelSurfaceMaterial",
    "prepareAssetImport",
    "prepareAssetReimport",
    "applyAssetImport",
    "discardAssetImport",
    "initializeVoxelAsset",
    "duplicateVoxelAsset",
    "attachVoxelInstance",
    "setVoxelInstanceTransform",
    "removeVoxelInstance",
    "replaceVoxelPalette",
    "validateVoxelPick",
    "applyVoxelBrush",
    "applyVoxelPrimitive",
    "initializeVoxelTemplate",
    "importVoxelAssetFile",
    "exportVoxelAssetFile",
    "materializeEnvironment",
    "undoVoxelEdit",
    "redoVoxelEdit",
    "revertVoxelHistory",
    "queryVoxelHistory",
    "prepareVoxelHistoryRevert",
    "applyVoxelHistoryRevert",
    "discardVoxelHistoryRevert",
    "createVoxelAnnotationLayer",
    "editVoxelAnnotation",
    "queryVoxelAnnotation",
    "exportVoxelAnnotation",
    "queryVoxelModel",
    "prepareVoxelConversion",
    "applyVoxelConversion",
    "discardVoxelConversion",
    "inspectVoxelObjectSource",
    "prepareVoxelObjectConversion",
    "previewVoxelObjectConversion",
    "applyVoxelObjectConversion",
    "discardVoxelObjectConversion",
    "prepareVoxelObjectPlacement",
    "attachVoxelObjectInstance",
    "attachVoxelObjectInstances",
    "previewVoxelObjectInstance",
    "closeProject",
];

struct OpenProject {
    _root: PathBuf,
    relative_project_file: String,
    project_hash: String,
    project_text: String,
    project: Value,
    _runtime: DaggerRuntime,
}

#[derive(Default)]
struct Adapter {
    open: Option<OpenProject>,
}

fn main() {
    let stdin = io::stdin();
    let mut stdout = io::BufWriter::new(io::stdout().lock());
    let mut adapter = Adapter::default();
    for line in stdin.lock().lines() {
        let line = match line {
            Ok(line) => line,
            Err(error) => {
                write_response(
                    &mut stdout,
                    &rejected(None, "transport_error", error.to_string()),
                );
                break;
            }
        };
        if line.len() > MAX_REQUEST_BYTES {
            write_response(
                &mut stdout,
                &rejected(None, "request_too_large", "request exceeds 256 KiB"),
            );
            continue;
        }
        let request = match serde_json::from_str::<Value>(&line) {
            Ok(Value::Object(request)) => request,
            Ok(_) => {
                write_response(
                    &mut stdout,
                    &rejected(None, "invalid_request", "request must be a JSON object"),
                );
                continue;
            }
            Err(error) => {
                write_response(
                    &mut stdout,
                    &rejected(None, "invalid_json", error.to_string()),
                );
                continue;
            }
        };
        let response = adapter.handle(&request);
        write_response(&mut stdout, &response);
    }
}

fn write_response(stdout: &mut impl Write, response: &Value) {
    if serde_json::to_writer(&mut *stdout, response).is_err() {
        return;
    }
    let _ = stdout.write_all(b"\n");
    let _ = stdout.flush();
}

impl Adapter {
    fn handle(&mut self, request: &Map<String, Value>) -> Value {
        let request_id = request.get("requestId").and_then(Value::as_str);
        if request.get("protocolVersion").and_then(Value::as_u64) != Some(PROTOCOL_VERSION) {
            return rejected(
                request_id,
                "protocol_version_mismatch",
                "protocolVersion must be 14",
            );
        }
        let Some(kind) = request.get("type").and_then(Value::as_str) else {
            return rejected(request_id, "invalid_request", "type is required");
        };
        match kind {
            "describe" => self.describe(request_id),
            "openProject" => self.open(request_id, request),
            "readProject" => self.read(request_id),
            "closeProject" => self.close(request_id),
            _ => rejected(
                request_id,
                "unsupported_operation",
                "this Dagger adapter is read-only; the requested mutation has no Rust authority yet",
            ),
        }
    }

    fn describe(&self, request_id: Option<&str>) -> Value {
        json!({
            "type": "described",
            "protocolVersion": PROTOCOL_VERSION,
            "requestId": request_id.unwrap_or(""),
            "adapter": {
                "adapterId": "rusty-dagger.privateers-hold",
                "adapterVersion": 1,
                "protocolVersion": PROTOCOL_VERSION,
                "projectKind": "rusty-dagger-project",
                "projectSchemaVersion": 24,
                "operations": OPERATIONS,
                "entityInspectorContracts": [],
            },
        })
    }

    fn open(&mut self, request_id: Option<&str>, request: &Map<String, Value>) -> Value {
        let Some(root_text) = request.get("root").and_then(Value::as_str) else {
            return rejected(request_id, "invalid_project_path", "root is required");
        };
        let Some(project_file) = request.get("projectFile").and_then(Value::as_str) else {
            return rejected(
                request_id,
                "invalid_project_path",
                "projectFile is required",
            );
        };
        let root = PathBuf::from(root_text);
        let (project_path, relative) = match safe_project_path(&root, project_file) {
            Ok(value) => value,
            Err(error) => return rejected(request_id, "invalid_project_path", error),
        };
        let project_text = match fs::read_to_string(&project_path) {
            Ok(text) if text.len() <= MAX_PROJECT_BYTES => text,
            Ok(_) => return rejected(request_id, "project_too_large", "project exceeds 64 MiB"),
            Err(error) => return rejected(request_id, "project_read_failed", error.to_string()),
        };
        let runtime = match DaggerRuntime::from_project_json(&project_text) {
            Ok(runtime) => runtime,
            Err(error) => return rejected(request_id, "project_rejected", error.to_string()),
        };
        let project = match serde_json::from_str::<Value>(&project_text) {
            Ok(Value::Object(project)) => Value::Object(project),
            Ok(_) => {
                return rejected(
                    request_id,
                    "project_rejected",
                    "project root must be an object",
                )
            }
            Err(error) => return rejected(request_id, "project_rejected", error.to_string()),
        };
        let project_hash = sha256(&project_text);
        self.open = Some(OpenProject {
            _root: root,
            relative_project_file: relative,
            project_hash,
            project_text,
            project,
            _runtime: runtime,
        });
        self.project_response("projectOpened", request_id)
    }

    fn read(&self, request_id: Option<&str>) -> Value {
        if self.open.is_none() {
            return rejected(
                request_id,
                "no_project_open",
                "openProject must succeed before readProject",
            );
        }
        self.project_response("projectRead", request_id)
    }

    fn close(&mut self, request_id: Option<&str>) -> Value {
        self.open = None;
        json!({
            "type": "projectClosed",
            "protocolVersion": PROTOCOL_VERSION,
            "requestId": request_id.unwrap_or(""),
        })
    }

    fn project_response(&self, kind: &str, request_id: Option<&str>) -> Value {
        let Some(open) = self.open.as_ref() else {
            return rejected(
                request_id,
                "no_project_open",
                "openProject must succeed first",
            );
        };
        json!({
            "type": kind,
            "protocolVersion": PROTOCOL_VERSION,
            "requestId": request_id.unwrap_or(""),
            "project": make_readout(open),
        })
    }
}

fn rejected(request_id: Option<&str>, code: &str, message: impl Into<String>) -> Value {
    let mut response = json!({
        "type": "rejected",
        "protocolVersion": PROTOCOL_VERSION,
        "error": { "code": code, "message": message.into() },
    });
    if let Some(request_id) = request_id {
        response["requestId"] = Value::String(request_id.to_owned());
    }
    response
}

fn safe_project_path(root: &Path, project_file: &str) -> Result<(PathBuf, String), String> {
    let relative = Path::new(project_file);
    if relative.is_absolute()
        || relative
            .components()
            .any(|component| matches!(component, Component::ParentDir))
    {
        return Err("projectFile must be a relative path inside root".to_owned());
    }
    let root = root
        .canonicalize()
        .map_err(|error| format!("root is not readable: {error}"))?;
    let candidate = root.join(relative);
    let canonical = candidate
        .canonicalize()
        .map_err(|error| format!("projectFile is not readable: {error}"))?;
    if !canonical.starts_with(&root) {
        return Err("projectFile escapes root".to_owned());
    }
    Ok((canonical, relative.to_string_lossy().replace('\\', "/")))
}

fn sha256(text: &str) -> String {
    let digest = Sha256::digest(text.as_bytes());
    format!("sha256:{digest:x}")
}

fn make_readout(open: &OpenProject) -> Value {
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
        "engineRevision": ENGINE_REVISION,
        "projectHash": open.project_hash,
        "meshSources": ["content/privateers-hold.mesh.json", "content/privateers-hold.glb"],
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
    let projection = projection(project, &entities);
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

fn transform(entity: &Value) -> Value {
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
        "persistence": { "schemaVersion": 1, "artifactCount": 1, "requiredArtifactCount": 1, "declaredByteCount": 0, "classes": [{"name":"project","count":1}], "roles": [{"name":"canonical","count":1}], "loadSteps": [{"stage":"project","path":"content/projects/privateers-hold.project.json"}], "diagnostics": {"diagnostics": []} },
    })
}

fn projection(project: &Map<String, Value>, entities: &[Value]) -> Value {
    let mut ops = Vec::new();
    let assets = project
        .get("assets")
        .and_then(Value::as_array)
        .cloned()
        .unwrap_or_default();
    for asset in &assets {
        let id = asset.get("id").and_then(Value::as_str).unwrap_or("");
        if let Some(material) = asset.get("material") {
            let style = material.get("style").unwrap_or(&Value::Null);
            let color = style
                .get("color")
                .cloned()
                .unwrap_or_else(|| json!([0.7, 0.7, 0.7, 1.0]));
            let emission = style
                .get("emissionColor")
                .and_then(Value::as_array)
                .map(|v| {
                    json!([
                        v.first().and_then(Value::as_f64).unwrap_or(0.0),
                        v.get(1).and_then(Value::as_f64).unwrap_or(0.0),
                        v.get(2).and_then(Value::as_f64).unwrap_or(0.0)
                    ])
                })
                .unwrap_or_else(|| json!([0.0, 0.0, 0.0]));
            ops.push(json!({"op":"defineMaterial","material":{"schemaVersion":1,"id":id,"color":color,"texture":null,"roughness":style.get("roughness").and_then(Value::as_f64).unwrap_or(1.0),"textureTint":style.get("textureTint").cloned().unwrap_or_else(|| json!([1.0,1.0,1.0,1.0])),"emissionColor":emission,"emissionIntensity":style.get("emissive").and_then(Value::as_f64).unwrap_or(0.0),"uvStrategy":style.get("uvStrategy").and_then(Value::as_str).unwrap_or("flat")}}));
        }
        if let Some(static_mesh) = asset.get("staticMesh") {
            ops.push(json!({"op":"defineStaticMesh","asset":static_mesh}));
        }
    }
    let visible = entities.iter().filter(|entity| {
        entity
            .get("renderable")
            .and_then(|renderable| renderable.get("visible"))
            .and_then(Value::as_bool)
            == Some(true)
    });
    for entity in visible {
        let id = entity.get("id").and_then(Value::as_u64).unwrap_or(0);
        let renderable = entity.get("renderable").unwrap_or(&Value::Null);
        let asset = renderable
            .get("asset")
            .and_then(Value::as_str)
            .unwrap_or("mesh/privateers-hold");
        ops.push(json!({"op":"createStaticMeshInstance","handle":id,"parent":null,"instance":{"asset":asset,"transform":transform(entity),"visible":true,"materialOverrides":[],"metadata":{"sourceEntity":id,"sourceSceneNode":id,"tags":[],"label":entity.get("name").and_then(Value::as_str)}}}));
    }
    for entity in entities
        .iter()
        .filter(|entity| entity.get("light").is_some())
    {
        let id = entity.get("id").and_then(Value::as_u64).unwrap_or(0);
        let light = entity.get("light").unwrap_or(&Value::Null);
        ops.push(json!({"op":"createLight","handle":id,"parent":null,"light":{"kind":"point","color":light.get("color").cloned().unwrap_or_else(|| json!([1.0,1.0,1.0])),"intensity":light.get("intensity").and_then(Value::as_f64).unwrap_or(0.8),"enabled":light.get("enabled").and_then(Value::as_bool).unwrap_or(true),"position":entity.get("translation").cloned().unwrap_or_else(|| json!([0.0,0.0,0.0])),"range":light.get("range").cloned().unwrap_or(Value::Null),"decay":light.get("decay").and_then(Value::as_f64).unwrap_or(2.0),"shadowIntent":"disabled"}}));
    }
    json!({ "schemaVersion": 1, "ops": ops })
}
