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
const ENGINE_REVISION: &str = "880a119466faebbf19ed05e39206ff4ba87237a2";

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
    root: PathBuf,
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
            root,
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

/// Shared admission rule for project-relative resource paths (texture bytes
/// today). A catalog `sourcePath` is accepted only when it is a normalized
/// relative path naming a regular file inside the project root: no absolute
/// paths, no `ParentDir`/`CurDir` components, no non-normalized spellings
/// (`a//b`, `a/./b`, trailing separators), no symlinks anywhere in the
/// chain, and the canonical file must stay inside the canonical root.
///
/// Returns `None` to fail closed — the caller emits no texture descriptor or
/// resource entry for a rejected path, so an escaping or non-regular catalog
/// identity is never admitted or exposed (R6521-1).
fn project_resource_path(root: &Path, source_path: &str) -> Option<PathBuf> {
    if source_path.is_empty() {
        return None;
    }
    let relative = Path::new(source_path);
    if relative.is_absolute() {
        return None;
    }
    if !relative
        .components()
        .all(|component| matches!(component, Component::Normal(_)))
    {
        return None;
    }
    // The catalog spelling must already be the normalized form: recomposing
    // the components must reproduce the input byte-for-byte (Path equality is
    // component-based and would not see `a//b` or `a/./b` as different).
    if relative.components().collect::<PathBuf>().as_os_str() != relative.as_os_str() {
        return None;
    }
    let canonical_root = root.canonicalize().ok()?;
    let mut candidate = canonical_root.clone();
    let mut file_metadata = None;
    for component in relative.components() {
        candidate.push(component);
        let metadata = fs::symlink_metadata(&candidate).ok()?;
        if metadata.file_type().is_symlink() {
            return None;
        }
        file_metadata = Some(metadata);
    }
    if !file_metadata?.file_type().is_file() {
        return None;
    }
    let canonical = candidate.canonicalize().ok()?;
    if !canonical.starts_with(&canonical_root) {
        return None;
    }
    Some(canonical)
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

/// Exact content-addressed texture descriptor (protocol-14) for one project
/// texture asset. Returns None when the asset lacks the exact identity facts
/// (e.g. the untextured fallback chain) — the renderer then keeps the
/// historical color-fallback meaning for that material.
fn texture_descriptor(root: &Path, asset: &Value, texture: &Value) -> Option<Value> {
    let id = asset.get("id").and_then(Value::as_str)?;
    let catalog = asset.get("catalog")?;
    let hash_hex = catalog.get("hash").and_then(Value::as_str)?;
    if hash_hex.len() != 64 || !hash_hex.chars().all(|c| c.is_ascii_hexdigit()) {
        return None;
    }
    let source_path = catalog.get("sourcePath").and_then(Value::as_str)?;
    if source_path.is_empty() {
        return None;
    }
    // Byte length comes from the on-disk resource the host will serve; reading
    // it here keeps the descriptor consistent with the exact bytes whose hash
    // was stamped at generation time. The shared containment rule admits only
    // normalized project-root-relative regular files (R6521-1).
    let bytes = fs::read(project_resource_path(root, source_path)?).ok()?;
    let expected = format!("sha256:{hash_hex}");
    let actual = format!("sha256:{:x}", Sha256::digest(&bytes));
    if actual != expected {
        // Content drifted since generation: fail closed, project nothing
        // rather than admit a mismatched resource identity.
        return None;
    }
    Some(json!({
        "id": id,
        "width": texture.get("width").and_then(Value::as_u64).unwrap_or(0),
        "height": texture.get("height").and_then(Value::as_u64).unwrap_or(0),
        "filter": texture.get("filter").and_then(Value::as_str).unwrap_or("nearest"),
        "wrap": texture.get("wrap").and_then(Value::as_str).unwrap_or("repeat"),
        "contentHash": expected,
        "version": 1,
        "payload": {
            "encoding": "pngRgba8",
            "colorSpace": "srgb",
            "contentHash": expected,
            "byteLength": bytes.len(),
            "source": { "kind": "resource", "resource": format!("texture-resource/{hash_hex}") },
        },
    }))
}

/// Protocol-14 `textureResources` readout: one entry per project texture
/// asset with a resolvable exact resource identity.
fn texture_resources(root: &Path, project: &Map<String, Value>) -> Value {
    let assets = project
        .get("assets")
        .and_then(Value::as_array)
        .cloned()
        .unwrap_or_default();
    let mut resources = Vec::new();
    let mut seen_hashes = std::collections::HashSet::new();
    for asset in &assets {
        let Some(texture) = asset.get("texture") else {
            continue;
        };
        let Some(catalog) = asset.get("catalog") else {
            continue;
        };
        let Some(hash_hex) = catalog.get("hash").and_then(Value::as_str) else {
            continue;
        };
        let Some(source_path) = catalog.get("sourcePath").and_then(Value::as_str) else {
            continue;
        };
        if hash_hex.len() != 64 || source_path.is_empty() {
            continue;
        }
        let Some(path) = project_resource_path(root, source_path) else {
            continue;
        };
        let Ok(bytes) = fs::read(path) else { continue };
        let expected = format!("sha256:{hash_hex}");
        if format!("sha256:{:x}", Sha256::digest(&bytes)) != expected {
            continue;
        }
        let _ = texture;
        // Distinct classic textures may decode to identical bytes (e.g.
        // TEXTURE.120[3] == TEXTURE.168[3]); the manifest is keyed by exact
        // resource identity, so emit one entry per unique resource while
        // defineTexture ops keep their per-asset ids bound to it.
        if seen_hashes.insert(hash_hex.to_owned()) {
            resources.push(json!({
                "resource": format!("texture-resource/{hash_hex}"),
                "contentHash": expected,
                "byteLength": bytes.len(),
                "sourcePath": source_path,
            }));
        }
    }
    Value::Array(resources)
}

fn projection(root: &Path, project: &Map<String, Value>, entities: &[Value]) -> Value {
    let mut ops = Vec::new();
    let assets = project
        .get("assets")
        .and_then(Value::as_array)
        .cloned()
        .unwrap_or_default();
    for asset in &assets {
        let id = asset.get("id").and_then(Value::as_str).unwrap_or("");
        // Exact content-addressed texture resources (protocol-14). The host
        // serves the bytes from catalog.sourcePath after re-hashing them; the
        // renderer preloads them through the studio texture-resource manifest.
        if let Some(texture) = asset.get("texture") {
            if let Some(descriptor) = texture_descriptor(root, asset, texture) {
                ops.push(json!({"op":"defineTexture","texture":descriptor}));
            }
        }
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
            let texture_ref = style
                .get("texture")
                .and_then(|t| t.get("id"))
                .cloned()
                .unwrap_or(Value::Null);
            ops.push(json!({"op":"defineMaterial","material":{"schemaVersion":1,"id":id,"color":color,"texture":texture_ref,"roughness":style.get("roughness").and_then(Value::as_f64).unwrap_or(1.0),"textureTint":style.get("textureTint").cloned().unwrap_or_else(|| json!([1.0,1.0,1.0,1.0])),"emissionColor":emission,"emissionIntensity":style.get("emissive").and_then(Value::as_f64).unwrap_or(0.0),"uvStrategy":style.get("uvStrategy").and_then(Value::as_str).unwrap_or("flat")}}));
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
    for entity in entities
        .iter()
        .filter(|entity| entity.get("sprite").is_some())
    {
        let id = entity.get("id").and_then(Value::as_u64).unwrap_or(0);
        let sprite = entity.get("sprite").unwrap_or(&Value::Null);
        let asset = sprite.get("asset").and_then(Value::as_str).unwrap_or("");
        // SpriteInstanceDescriptor: cylindrical (Y-facing) billboard with the
        // flat's transparent texture, at the flat's world position.
        ops.push(json!({
            "op": "createSprite",
            "handle": id,
            "parent": null,
            "sprite": {
                "asset": asset,
                "frame": 0,
                "pivot": [0.5, 0.0],
                "size": [1.0, 1.0],
                "sizeMode": sprite.get("sizeMode").and_then(Value::as_str).unwrap_or("world"),
                "billboard": sprite.get("billboard").and_then(Value::as_str).unwrap_or("cylindrical"),
                "tint": [1.0, 1.0, 1.0, 1.0],
                "renderOrder": 0,
                "depth": sprite.get("depth").and_then(Value::as_str).unwrap_or("default"),
                "shading": sprite.get("shading").and_then(Value::as_str).unwrap_or("lit"),
                "visible": sprite.get("visible").and_then(Value::as_bool).unwrap_or(true),
                "transform": transform(entity),
                "attachment": {"sourceEntity": id, "sourceSceneNode": id, "attachmentPoint": null},
                "metadata": {"sourceEntity": id, "sourceSceneNode": id, "tags": [], "label": entity.get("name").and_then(Value::as_str)},
            }
        }));
    }
    json!({ "schemaVersion": 1, "ops": ops })
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::sync::atomic::{AtomicUsize, Ordering};

    static NEXT_TEMP_DIR: AtomicUsize = AtomicUsize::new(0);

    struct TempTree {
        path: PathBuf,
    }

    impl TempTree {
        fn new(name: &str) -> Self {
            let id = NEXT_TEMP_DIR.fetch_add(1, Ordering::Relaxed);
            let path = std::env::temp_dir().join(format!(
                "dagger-studio-adapter-{name}-{}-{id}",
                std::process::id()
            ));
            fs::create_dir_all(&path).expect("temp tree");
            Self { path }
        }
    }

    impl Drop for TempTree {
        fn drop(&mut self) {
            let _ = fs::remove_dir_all(&self.path);
        }
    }

    fn sha256_hex(bytes: &[u8]) -> String {
        format!("{:x}", Sha256::digest(bytes))
    }

    fn texture_asset(id: &str, source_path: &str, hash_hex: &str) -> Value {
        json!({
            "id": id,
            "catalog": {
                "version": 1,
                "hash": hash_hex,
                "sourcePath": source_path,
                "label": id,
                "dependencies": [],
            },
            "texture": { "width": 4, "height": 4, "filter": "nearest", "wrap": "repeat" },
        })
    }

    fn define_texture_ids(ops: &[Value]) -> Vec<String> {
        ops.iter()
            .filter(|op| op.get("op").and_then(Value::as_str) == Some("defineTexture"))
            .map(|op| {
                op.pointer("/texture/id")
                    .and_then(Value::as_str)
                    .unwrap_or("")
                    .to_owned()
            })
            .collect()
    }

    /// R6521-1 regression: a catalog entry that names anything other than a
    /// normalized project-root-relative regular file — even with a hash that
    /// matches real bytes on disk — must produce no defineTexture op and no
    /// textureResources entry on either adapter code path.
    #[cfg(unix)]
    #[test]
    fn catalog_texture_paths_must_be_normalized_regular_files_inside_the_project_root() {
        let tree = TempTree::new("containment");
        let root = tree.path.join("root");
        let textures = root.join("content/textures");
        fs::create_dir_all(&textures).unwrap();
        let good_bytes = b"good-texture-bytes";
        let outside_bytes = b"outside-the-project-root";
        fs::write(textures.join("tex.png"), good_bytes).unwrap();
        fs::write(tree.path.join("outside.bin"), outside_bytes).unwrap();
        std::os::unix::fs::symlink("../../outside.bin", root.join("content/link.png")).unwrap();
        std::os::unix::fs::symlink("textures", root.join("content/linked-dir")).unwrap();
        let good_hash = sha256_hex(good_bytes);
        let outside_hash = sha256_hex(outside_bytes);
        let absolute_outside = tree.path.join("outside.bin").to_string_lossy().into_owned();
        let assets = vec![
            texture_asset("texture/good", "content/textures/tex.png", &good_hash),
            texture_asset("texture/parent-escape", "../outside.bin", &outside_hash),
            texture_asset("texture/absolute-escape", &absolute_outside, &outside_hash),
            texture_asset(
                "texture/double-slash",
                "content//textures/tex.png",
                &good_hash,
            ),
            texture_asset("texture/dot-dir", "content/./textures/tex.png", &good_hash),
            texture_asset(
                "texture/parent-in-middle",
                "content/textures/../textures/tex.png",
                &good_hash,
            ),
            texture_asset("texture/symlink-file", "content/link.png", &outside_hash),
            texture_asset(
                "texture/symlink-dir",
                "content/linked-dir/tex.png",
                &good_hash,
            ),
            texture_asset("texture/directory", "content/textures", &good_hash),
        ];
        let project = json!({ "assets": assets });
        let project = project.as_object().unwrap();

        let projected = projection(&root, project, &[]);
        let ops = projected.get("ops").and_then(Value::as_array).unwrap();
        assert_eq!(
            define_texture_ids(ops),
            vec!["texture/good".to_owned()],
            "only the in-root regular-file texture may be projected",
        );
        let resources = texture_resources(&root, project);
        let resources = resources.as_array().unwrap();
        assert_eq!(resources.len(), 1);
        assert_eq!(
            resources[0].get("sourcePath").and_then(Value::as_str),
            Some("content/textures/tex.png"),
        );
    }

    /// R6521-1 companion: hardening must not change canonical admission — the
    /// committed Privateer's Hold project still projects every authored
    /// texture descriptor and every unique content-addressed resource.
    #[test]
    fn canonical_project_texture_projection_is_unchanged() {
        let workspace = Path::new(env!("CARGO_MANIFEST_DIR"))
            .parent()
            .and_then(Path::parent)
            .expect("workspace root")
            .to_path_buf();
        let project_text =
            fs::read_to_string(workspace.join("content/projects/privateers-hold.project.json"))
                .expect("committed project document");
        let project = serde_json::from_str::<Value>(&project_text).unwrap();
        let project = project.as_object().unwrap();
        let projected = projection(&workspace, project, &[]);
        let ops = projected.get("ops").and_then(Value::as_array).unwrap();
        assert_eq!(
            define_texture_ids(ops).len(),
            114,
            "the committed project must keep projecting every authored texture (81 dungeon + 33 billboard; archive-210/16 restored by R6523-1)",
        );
        let resources = texture_resources(&workspace, project);
        let resources = resources.as_array().unwrap();
        assert_eq!(
            resources.len(),
            113,
            "the committed project keeps its unique content-addressed texture resources",
        );
        for entry in resources {
            let source_path = entry.get("sourcePath").and_then(Value::as_str).unwrap();
            assert!(
                source_path.starts_with("content/textures/"),
                "unexpected texture resource path: {source_path}",
            );
        }
    }
}
