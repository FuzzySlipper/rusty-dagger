//! Rusty Dagger's small, Rust-owned Studio protocol boundary.
//!
//! This is intentionally a read-only first seam.  Project admission is still
//! performed by `dagger-runtime`; the adapter only translates the admitted
//! authored project into the public Engine render contract.  Mutation
//! requests are rejected until a Dagger-owned authority for that mutation is
//! implemented rather than being silently accepted by a presentation layer.

use dagger_runtime::DaggerRuntime;
use serde_json::{json, Map, Value};
use std::fs;
use std::io::{self, BufRead, Write};
use std::path::PathBuf;

use crate::{
    project_access::{safe_project_path, sha256},
    readout::make_readout,
};

const PROTOCOL_VERSION: u64 = 14;
const MAX_REQUEST_BYTES: usize = 256 * 1024;
const MAX_PROJECT_BYTES: usize = 64 * 1024 * 1024;

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

pub(crate) struct OpenProject {
    pub(crate) root: PathBuf,
    pub(crate) relative_project_file: String,
    pub(crate) project_hash: String,
    pub(crate) project_text: String,
    pub(crate) project: Value,
    pub(crate) _runtime: DaggerRuntime,
}

#[derive(Default)]
struct Adapter {
    open: Option<OpenProject>,
}

pub fn run_stdio() {
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

#[cfg(test)]
mod tests {
    use super::*;
    use crate::presentation::{build_render_bundle, projection, texture_resources};
    use rusty_engine::render_model::RenderDiff;
    use sha2::{Digest, Sha256};
    use std::path::Path;
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

    /// A catalog entry that names anything other than a normalized
    /// project-root-relative regular file — even with a hash that
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

    /// The committed Privateer's Hold project projects its authored textures
    /// and every texture resource stays under content/textures/. No exact
    /// counts on purpose: content evolves and the live studio gates audit
    /// texture traffic; this guards only the structural invariant.
    #[test]
    fn canonical_project_texture_projection_is_structurally_sound() {
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
        assert!(
            !define_texture_ids(ops).is_empty(),
            "the committed project must project authored textures",
        );
        let resources = texture_resources(&workspace, project);
        let resources = resources.as_array().unwrap();
        assert!(!resources.is_empty(), "texture resources must be admitted");
        for entry in resources {
            let source_path = entry.get("sourcePath").and_then(Value::as_str).unwrap();
            assert!(
                source_path.starts_with("content/textures/"),
                "unexpected texture resource path: {source_path}",
            );
        }
    }

    #[test]
    fn canonical_project_decodes_through_the_public_engine_facade() {
        let workspace = Path::new(env!("CARGO_MANIFEST_DIR"))
            .parent()
            .and_then(Path::parent)
            .expect("workspace root")
            .to_path_buf();
        let project_text =
            fs::read_to_string(workspace.join("content/projects/privateers-hold.project.json"))
                .expect("committed project document");
        let bundle = build_render_bundle(&workspace, &project_text)
            .expect("Dagger projection must decode as an Engine retained frame");
        assert!(!bundle.frame.ops.is_empty());
        assert!(!bundle.resources.is_empty());
        assert!(bundle.source_entity_count > 0);
        let audio = bundle
            .resources
            .iter()
            .filter(|resource| resource.media_type == "audio/wav")
            .collect::<Vec<_>>();
        assert_eq!(audio.len(), 6, "all classic melee sounds must be admitted");
        assert!(audio.iter().all(|resource| {
            resource.identity.starts_with("audio-resource/")
                && resource.content_hash.starts_with("sha256:")
                && resource.source_path.starts_with("content/audio/")
                && !resource.bytes.is_empty()
        }));
        for atlas_id in ["sprite/enemy-0-atlas", "sprite/enemy-1-atlas"] {
            let atlas = bundle
                .frame
                .ops
                .iter()
                .find_map(|op| match op {
                    RenderDiff::DefineSpriteAtlas { atlas } if atlas.id == atlas_id => Some(atlas),
                    _ => None,
                })
                .unwrap_or_else(|| panic!("missing canonical atlas {atlas_id}"));
            // Per-frame world sizes (classic per-record dims) ride the atlas.
            assert!(atlas.frames.iter().all(|frame| {
                frame
                    .size
                    .is_some_and(|size| size[0] > 0.0 && size[1] > 0.0)
            }));
        }
        let rat = bundle
            .frame
            .ops
            .iter()
            .find_map(|op| match op {
                RenderDiff::CreateSprite { handle, sprite, .. } if handle.raw() == 2007 => {
                    Some(sprite)
                }
                _ => None,
            })
            .expect("canonical Rat sprite");
        assert!(rat.size[0] > 0.0 && rat.size[1] > 0.0);
        let rat_atlas = bundle
            .frame
            .ops
            .iter()
            .find_map(|op| match op {
                RenderDiff::DefineSpriteAtlas { atlas } if atlas.id == "sprite/enemy-0-atlas" => {
                    Some(atlas)
                }
                _ => None,
            })
            .expect("Rat atlas");
        assert!(rat_atlas.frames.iter().all(|frame| {
            frame
                .size
                .is_some_and(|size| size[0] > 0.0 && size[1] > 0.0)
        }));
    }
}
