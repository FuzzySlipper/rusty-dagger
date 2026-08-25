use std::{
    fs,
    path::{Component, Path, PathBuf},
};

use sha2::{Digest, Sha256};

use dagger_runtime::DaggerRuntime;

const MAX_GAMEPLAY_PACKAGE_BYTES: usize = 2 * 1024 * 1024;
const GAMEPLAY_PACKAGE_PATH: &str = "content/runtime/dagger-core.package.json";

/// Admit a Studio project against the immutable gameplay package declared by
/// the Product Layout. The adapter never reconstructs gameplay meaning from
/// a project path or its own embedded bytes.
pub(crate) fn admit_runtime(root: &Path, project_text: &str) -> Result<DaggerRuntime, String> {
    let package_path = project_resource_path(root, GAMEPLAY_PACKAGE_PATH)
        .ok_or_else(|| "canonical gameplay package is unavailable".to_owned())?;
    let gameplay_package = fs::read(package_path)
        .map_err(|error| format!("canonical gameplay package is unreadable: {error}"))?;
    if gameplay_package.len() > MAX_GAMEPLAY_PACKAGE_BYTES {
        return Err("canonical gameplay package exceeds 2 MiB".to_owned());
    }
    DaggerRuntime::from_project_json_with_gameplay_package(project_text, &gameplay_package)
        .map_err(|error| error.to_string())
}

pub(crate) fn safe_project_path(
    root: &Path,
    project_file: &str,
) -> Result<(PathBuf, String), String> {
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

pub(crate) fn sha256(text: &str) -> String {
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
/// identity is never admitted or exposed.
pub(crate) fn project_resource_path(root: &Path, source_path: &str) -> Option<PathBuf> {
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
