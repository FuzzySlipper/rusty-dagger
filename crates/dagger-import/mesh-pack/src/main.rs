//! Deterministically materialize the runtime-only mesh resource variant.
//!
//! The Studio project retains its readable inline mesh streams.  This tool
//! copies that project for Product Assembly, replacing only the selected
//! `StaticMeshAsset` payload with Engine-owned packed resource descriptors.

use std::{env, fs, path::PathBuf};

use rusty_engine::render_model::{
    pack_mesh_resources, validate_mesh_resource_header, StaticMeshAsset, MAX_MESH_RESOURCE_BYTES,
};
use serde_json::Value;

struct Args {
    input: PathBuf,
    project_out: PathBuf,
    resource_out: PathBuf,
    check: bool,
}

fn usage() -> &'static str {
    "usage: dagger-pack-mesh --input AUTHORING_PROJECT --project-out RUNTIME_PROJECT --resource-out RUNTIME_RESOURCE [--check]"
}

fn parse_args() -> Result<Args, String> {
    let mut input = None;
    let mut project_out = None;
    let mut resource_out = None;
    let mut check = false;
    let mut args = env::args().skip(1);
    while let Some(argument) = args.next() {
        match argument.as_str() {
            "--input" => input = Some(PathBuf::from(args.next().ok_or("--input needs a value")?)),
            "--project-out" => {
                project_out = Some(PathBuf::from(
                    args.next().ok_or("--project-out needs a value")?,
                ))
            }
            "--resource-out" => {
                resource_out = Some(PathBuf::from(
                    args.next().ok_or("--resource-out needs a value")?,
                ))
            }
            "--check" => check = true,
            "--help" | "-h" => return Err(usage().to_string()),
            other => return Err(format!("unknown argument {other}; {}", usage())),
        }
    }
    Ok(Args {
        input: input.ok_or_else(|| format!("--input is required; {}", usage()))?,
        project_out: project_out
            .ok_or_else(|| format!("--project-out is required; {}", usage()))?,
        resource_out: resource_out
            .ok_or_else(|| format!("--resource-out is required; {}", usage()))?,
        check,
    })
}

fn packed_outputs(input: &PathBuf) -> Result<(Vec<u8>, Vec<u8>), String> {
    let mut project: Value =
        serde_json::from_slice(&fs::read(input).map_err(|error| error.to_string())?)
            .map_err(|error| format!("{} is not JSON: {error}", input.display()))?;
    let assets = project
        .get_mut("assets")
        .and_then(Value::as_array_mut)
        .ok_or("project assets must be an array")?;
    rewrite_runtime_texture_source_paths(assets)?;
    let asset = assets
        .iter_mut()
        .find(|asset| asset.get("id").and_then(Value::as_str) == Some("mesh/privateers-hold"))
        .ok_or("project has no mesh/privateers-hold asset")?;
    let static_mesh = asset
        .get_mut("staticMesh")
        .ok_or("mesh/privateers-hold has no staticMesh payload")?;
    let source: StaticMeshAsset = serde_json::from_value(static_mesh.clone())
        .map_err(|error| format!("static mesh decode failed: {error}"))?;
    source
        .validate()
        .map_err(|error| format!("static mesh is invalid: {error:?}"))?;
    let packed = pack_mesh_resources(&[source.payload.clone()], MAX_MESH_RESOURCE_BYTES)
        .map_err(|error| format!("mesh packing failed: {error:?}"))?;
    if packed.resources.len() != 1 || packed.payloads.len() != 1 {
        return Err("Privateer's Hold must pack to exactly one mesh resource".to_string());
    }
    let resource = &packed.resources[0];
    resource
        .validate()
        .map_err(|error| format!("packed resource is invalid: {error:?}"))?;
    validate_mesh_resource_header(&resource.bytes)
        .map_err(|error| format!("packed mesh header is invalid: {error:?}"))?;
    let mut runtime_mesh = source;
    runtime_mesh.payload = packed
        .payloads
        .into_iter()
        .next()
        .expect("one payload was checked");
    *static_mesh = serde_json::to_value(runtime_mesh).map_err(|error| error.to_string())?;
    let project_bytes = serde_json::to_vec(&project).map_err(|error| error.to_string())?;
    Ok((project_bytes, resource.bytes.clone()))
}

/// Product Assembly admits runtime bytes from `content/`, while Studio reads
/// the separate inline authoring project.  Only authored texture paths are
/// allowed to cross this productization boundary, and they must name one flat
/// texture body so a mistaken offline path cannot reach the runtime host.
fn rewrite_runtime_texture_source_paths(assets: &mut [Value]) -> Result<(), String> {
    const AUTHORING_TEXTURES: &str = "authoring-content/textures/";
    for asset in assets {
        let id = asset
            .get("id")
            .and_then(Value::as_str)
            .unwrap_or("<unknown>")
            .to_owned();
        let catalog = asset
            .get_mut("catalog")
            .and_then(Value::as_object_mut)
            .ok_or_else(|| format!("asset {id} has no catalog"))?;
        let source_path = catalog
            .get_mut("sourcePath")
            .ok_or_else(|| format!("asset {id} catalog has no sourcePath"))?;
        if source_path.is_null() {
            continue;
        }
        let source = source_path
            .as_str()
            .ok_or_else(|| format!("asset {id} sourcePath must be a string or null"))?;
        let file = source.strip_prefix(AUTHORING_TEXTURES).ok_or_else(|| {
            format!("asset {id} sourcePath {source:?} is not an admitted authoring texture")
        })?;
        if file.is_empty()
            || file.contains('/')
            || file.contains('\\')
            || file == "."
            || file == ".."
        {
            return Err(format!(
                "asset {id} has unsafe texture sourcePath {source:?}"
            ));
        }
        *source_path = Value::String(format!("content/textures/{file}"));
    }
    Ok(())
}

fn main() {
    let args = match parse_args() {
        Ok(args) => args,
        Err(error) => {
            eprintln!("{error}");
            std::process::exit(2);
        }
    };
    let (project, resource) = match packed_outputs(&args.input) {
        Ok(outputs) => outputs,
        Err(error) => {
            eprintln!("dagger-pack-mesh: {error}");
            std::process::exit(1);
        }
    };
    if args.check {
        let stale = [
            (&args.project_out, &project),
            (&args.resource_out, &resource),
        ]
        .into_iter()
        .filter(|(path, expected)| fs::read(path).map_or(true, |actual| actual != **expected))
        .map(|(path, _)| path.display().to_string())
        .collect::<Vec<_>>();
        if !stale.is_empty() {
            eprintln!(
                "{} stale; run scripts/generate-project.py --write",
                stale.join(", ")
            );
            std::process::exit(1);
        }
        println!(
            "packed runtime mesh is up to date ({} bytes)",
            resource.len()
        );
        return;
    }
    for path in [&args.project_out, &args.resource_out] {
        if let Some(parent) = path.parent() {
            fs::create_dir_all(parent)
                .map_err(|error| format!("{}: {error}", parent.display()))
                .unwrap();
        }
    }
    fs::write(&args.project_out, project).expect("runtime project write must succeed");
    fs::write(&args.resource_out, resource).expect("runtime mesh write must succeed");
    println!("packed runtime mesh: {}", args.resource_out.display());
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn privateers_hold_pack_is_deterministic_and_header_valid() {
        let input = PathBuf::from(env!("CARGO_MANIFEST_DIR"))
            .join("../../../authoring-content/projects/privateers-hold.project.json");
        let first = packed_outputs(&input).expect("authoring project must pack");
        let second = packed_outputs(&input).expect("authoring project must pack consistently");
        assert_eq!(first, second);
        validate_mesh_resource_header(&first.1).expect("packed mesh header must validate");

        let project: Value =
            serde_json::from_slice(&first.0).expect("runtime project must be JSON");
        let source = project["assets"]
            .as_array()
            .and_then(|assets| {
                assets
                    .iter()
                    .find(|asset| asset["id"] == "mesh/privateers-hold")
            })
            .and_then(|asset| asset.pointer("/staticMesh/payload/source"))
            .expect("runtime static mesh must have a source");
        assert_eq!(source["kind"], "resource");

        let manifest_path =
            PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("../../../content/manifest.json");
        let manifest: Value = serde_json::from_slice(
            &fs::read(manifest_path).expect("runtime content manifest must exist"),
        )
        .expect("runtime content manifest must be JSON");
        let declared = manifest["artifacts"]
            .as_array()
            .expect("runtime content manifest artifacts must be an array")
            .iter()
            .filter_map(|artifact| artifact["path"].as_str())
            .collect::<std::collections::BTreeSet<_>>();
        for asset in project["assets"]
            .as_array()
            .expect("runtime project assets must be an array")
        {
            let Some(path) = asset.pointer("/catalog/sourcePath").and_then(Value::as_str) else {
                continue;
            };
            assert!(
                path.starts_with("content/textures/"),
                "unexpected source path {path}"
            );
            assert!(
                declared.contains(path.strip_prefix("content/").expect("prefix checked")),
                "source path {path} is not admitted"
            );
            assert!(!path.starts_with("authoring-content/"));
        }
    }
}
