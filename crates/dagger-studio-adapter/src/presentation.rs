use std::{fs, path::Path};

use dagger_runtime::CombatAssetCatalog;
use serde_json::{json, Map, Value};
use sha2::{Digest, Sha256};

use crate::{
    project_access::{admit_runtime, project_resource_path},
    readout::transform,
};

/// Exact content-addressed texture descriptor (protocol-14) for one project
/// texture asset. Returns None when the asset lacks the exact identity facts
/// (e.g. the untextured fallback chain) — the renderer then keeps the
/// historical color-fallback meaning for that material.
pub(crate) fn texture_descriptor(root: &Path, asset: &Value, texture: &Value) -> Option<Value> {
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
    // normalized project-root-relative regular files.
    let bytes = fs::read(project_resource_path(root, source_path)?).ok()?;
    let expected = format!("sha256:{hash_hex}");
    let actual = format!("sha256:{:x}", Sha256::digest(&bytes));
    // Hand-edited content is legitimate: when the bytes drifted from the
    // generation-time hash, say so loudly and serve the actual bytes under
    // their actual identity instead of silently dropping the texture.
    let (content_hash, resource_hash) = if actual != expected {
        eprintln!(
            "TEXTURE_DRIFT {source_path}: manifest hash {expected}, actual {actual} — serving actual bytes"
        );
        (
            actual.clone(),
            actual.trim_start_matches("sha256:").to_string(),
        )
    } else {
        (expected, hash_hex.to_string())
    };
    Some(json!({
        "id": id,
        "width": texture.get("width").and_then(Value::as_u64).unwrap_or(0),
        "height": texture.get("height").and_then(Value::as_u64).unwrap_or(0),
        "filter": texture.get("filter").and_then(Value::as_str).unwrap_or("nearest"),
        "wrap": texture.get("wrap").and_then(Value::as_str).unwrap_or("repeat"),
        "contentHash": content_hash,
        "version": 1,
        "payload": {
            "encoding": "pngRgba8",
            "colorSpace": "srgb",
            "contentHash": content_hash,
            "byteLength": bytes.len(),
            "source": { "kind": "resource", "resource": format!("texture-resource/{resource_hash}") },
        },
    }))
}

/// Protocol-14 `textureResources` readout: one entry per project texture
/// asset with a resolvable exact resource identity.
pub(crate) fn texture_resources(root: &Path, project: &Map<String, Value>) -> Value {
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
        let actual = format!("sha256:{:x}", Sha256::digest(&bytes));
        // Drift means hand-edited content: warn and publish the actual
        // resource identity so the edit shows up instead of vanishing.
        let (content_hash, resource_hash) = if actual != expected {
            eprintln!(
                "TEXTURE_DRIFT {source_path}: manifest hash {expected}, actual {actual} — publishing actual resource identity"
            );
            (
                actual.clone(),
                actual.trim_start_matches("sha256:").to_string(),
            )
        } else {
            (expected, hash_hex.to_string())
        };
        let _ = texture;
        // Distinct classic textures may decode to identical bytes (e.g.
        // TEXTURE.120[3] == TEXTURE.168[3]); the manifest is keyed by exact
        // resource identity, so emit one entry per unique resource while
        // defineTexture ops keep their per-asset ids bound to it.
        if seen_hashes.insert(resource_hash.clone()) {
            resources.push(json!({
                "resource": format!("texture-resource/{resource_hash}"),
                "contentHash": content_hash,
                "byteLength": bytes.len(),
                "sourcePath": source_path,
            }));
        }
    }
    Value::Array(resources)
}

pub(crate) fn projection(root: &Path, project: &Map<String, Value>, entities: &[Value]) -> Value {
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
            // Sprite atlas (directional enemy frames, or a full-rect single
            // frame for plain billboards): createSprite asset ids resolve
            // against atlases, so without this sprites render untextured.
            if let Some(atlas) = texture.get("spriteAtlas") {
                let sprite_asset = id
                    .strip_prefix("texture/")
                    .map(|suffix| format!("sprite/{suffix}"))
                    .unwrap_or_else(|| format!("sprite/{id}"));
                let frames = atlas
                    .get("frames")
                    .and_then(Value::as_array)
                    .map(|frames| {
                        frames
                            .iter()
                            .map(|frame| {
                                let mut projected = Map::from_iter([
                                    (
                                        "frame".to_owned(),
                                        json!(frame
                                            .get("frame")
                                            .and_then(Value::as_u64)
                                            .unwrap_or(0)),
                                    ),
                                    (
                                        "uvMin".to_owned(),
                                        frame
                                            .get("uvMin")
                                            .cloned()
                                            .unwrap_or_else(|| json!([0.0, 0.0])),
                                    ),
                                    (
                                        "uvMax".to_owned(),
                                        frame
                                            .get("uvMax")
                                            .cloned()
                                            .unwrap_or_else(|| json!([1.0, 1.0])),
                                    ),
                                ]);
                                if let Some(size) = frame.get("size") {
                                    projected.insert("size".to_owned(), size.clone());
                                }
                                Value::Object(projected)
                            })
                            .collect::<Vec<_>>()
                    })
                    .unwrap_or_default();
                ops.push(json!({
                    "op": "defineSpriteAtlas",
                    "atlas": {
                        "id": sprite_asset,
                        "texture": id,
                        "frames": frames,
                    }
                }));
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
        .filter(|entity| entity.get("primitive").is_some())
    {
        let id = entity.get("id").and_then(Value::as_u64).unwrap_or(0);
        let primitive = entity.get("primitive").unwrap_or(&Value::Null);
        ops.push(json!({
            "op": "create",
            "handle": id,
            "parent": null,
            "node": {
                "geometry": {"kind": primitive.get("geometry").and_then(Value::as_str).unwrap_or("cube")},
                "material": {
                    "color": primitive.get("color").cloned().unwrap_or_else(|| json!([0.35, 0.38, 0.42, 1.0])),
                    "wireframe": primitive.get("wireframe").and_then(Value::as_bool).unwrap_or(false),
                },
                "transform": transform(entity),
                "visible": true,
                "layer": "scene",
                "metadata": {"sourceEntity": id, "sourceSceneNode": id, "tags": [], "label": entity.get("name").and_then(Value::as_str)},
            }
        }));
    }
    for entity in entities
        .iter()
        .filter(|entity| entity.get("sprite").is_some())
    {
        let id = entity.get("id").and_then(Value::as_u64).unwrap_or(0);
        let sprite = entity.get("sprite").unwrap_or(&Value::Null);
        let texture_asset = sprite.get("asset").and_then(Value::as_str).unwrap_or("");
        let asset = texture_asset
            .strip_prefix("texture/")
            .map(|suffix| format!("sprite/{suffix}"))
            .unwrap_or_else(|| format!("sprite/{texture_asset}"));
        let frame = sprite.get("frame").and_then(Value::as_u64).unwrap_or(0);
        let size = sprite
            .get("size")
            .cloned()
            .unwrap_or_else(|| json!([1.0, 1.0]));
        let pivot = sprite
            .get("pivot")
            .cloned()
            .unwrap_or_else(|| json!([0.5, 0.0]));
        // SpriteInstanceDescriptor: cylindrical (Y-facing) billboard with the
        // flat's transparent texture, at the flat's world position. The
        // renderer honors billboard modes; directional frame selection stays
        // consumer-side via updateSprite.
        ops.push(json!({
            "op": "createSprite",
            "handle": id,
            "parent": null,
            "sprite": {
                "asset": asset,
                "frame": frame,
                "pivot": pivot,
                "size": size,
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

/// Exact renderer input derived from an admitted Dagger project. The Dagger
/// package owns what the project means; Engine owns decoding and presenting
/// this typed retained frame and its content-addressed resources.
pub struct DaggerRenderBundle {
    pub frame: rusty_engine::render_model::RenderFrameDiff,
    pub resources: Vec<DaggerRenderResource>,
    pub source_entity_count: usize,
}

pub struct DaggerRenderResource {
    pub identity: String,
    pub content_hash: String,
    pub media_type: String,
    pub source_path: String,
    pub bytes: Vec<u8>,
}

/// Admit and strictly decode the checked Dagger presentation through Engine's
/// public Rust facade. No downstream renderer package or TypeScript contract
/// is exposed to the caller.
pub fn build_render_bundle(root: &Path, project_text: &str) -> Result<DaggerRenderBundle, String> {
    admit_runtime(root, project_text)
        .map_err(|error| format!("project admission failed: {error}"))?;
    let project_value = serde_json::from_str::<Value>(project_text)
        .map_err(|error| format!("project JSON failed: {error}"))?;
    let project = project_value
        .as_object()
        .ok_or_else(|| "project root must be an object".to_owned())?;
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
                .or_else(|| scenes.first())
        })
        .ok_or_else(|| "admitted project has no renderable scene".to_owned())?;
    let entities = scene
        .get("entities")
        .and_then(Value::as_array)
        .cloned()
        .unwrap_or_default();
    let frame: rusty_engine::render_model::RenderFrameDiff =
        serde_json::from_value(projection(root, project, &entities))
            .map_err(|error| format!("Engine retained-frame decode failed: {error}"))?;
    frame
        .validate()
        .map_err(|error| format!("Engine retained-frame validation failed: {error:?}"))?;

    let resource_values = texture_resources(root, project);
    let mut resources = Vec::new();
    for resource in resource_values
        .as_array()
        .ok_or_else(|| "texture resource manifest was not an array".to_owned())?
    {
        let identity = resource
            .get("resource")
            .and_then(Value::as_str)
            .ok_or_else(|| "texture resource identity is missing".to_owned())?;
        let content_hash = resource
            .get("contentHash")
            .and_then(Value::as_str)
            .ok_or_else(|| format!("texture resource {identity} has no content hash"))?;
        let source_path = resource
            .get("sourcePath")
            .and_then(Value::as_str)
            .ok_or_else(|| format!("texture resource {identity} has no source path"))?;
        let path = project_resource_path(root, source_path)
            .ok_or_else(|| format!("texture resource path was rejected: {source_path}"))?;
        let bytes = fs::read(&path)
            .map_err(|error| format!("read texture resource {source_path}: {error}"))?;
        resources.push(DaggerRenderResource {
            identity: identity.to_owned(),
            content_hash: content_hash.to_owned(),
            media_type: "image/png".to_owned(),
            source_path: source_path.to_owned(),
            bytes,
        });
    }
    let combat_manifest_path =
        project_resource_path(root, "authoring-content/textures/combat-manifest.json")
            .ok_or_else(|| "combat asset catalog path was rejected".to_owned())?;
    let combat_manifest = fs::read_to_string(&combat_manifest_path)
        .map_err(|error| format!("read combat asset catalog: {error}"))?;
    let combat_catalog = CombatAssetCatalog::from_json(&combat_manifest)?;
    for audio in &combat_catalog.audio {
        if audio.mime_type != "audio/wav" {
            return Err(format!(
                "combat audio {} has unsupported media type {}",
                audio.id, audio.mime_type
            ));
        }
        let path = project_resource_path(root, &audio.path)
            .ok_or_else(|| format!("combat audio path was rejected: {}", audio.path))?;
        let bytes = fs::read(&path)
            .map_err(|error| format!("read combat audio {}: {error}", audio.path))?;
        let actual = format!("sha256:{:x}", Sha256::digest(&bytes));
        // Hand-edited audio is legitimate content: warn on drift and publish
        // the actual identity rather than failing the whole bundle build.
        if bytes.len() as u64 != audio.byte_length || actual != audio.sha256 {
            eprintln!(
                "AUDIO_DRIFT {}: catalog hash {} length {}, actual {actual} length {} — publishing actual identity",
                audio.id,
                audio.sha256,
                audio.byte_length,
                bytes.len()
            );
        }
        let hash_hex = actual
            .strip_prefix("sha256:")
            .ok_or_else(|| format!("combat audio {} has invalid content hash", audio.id))?;
        resources.push(DaggerRenderResource {
            identity: format!("audio-resource/{hash_hex}"),
            content_hash: actual,
            media_type: audio.mime_type.clone(),
            source_path: audio.path.clone(),
            bytes,
        });
    }
    if resources.is_empty() {
        return Err("admitted project produced no exact texture resources".to_owned());
    }
    Ok(DaggerRenderBundle {
        frame,
        resources,
        source_entity_count: entities.len(),
    })
}
