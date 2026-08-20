use std::{
    io::{Read, Write},
    net::{IpAddr, TcpListener, TcpStream},
    path::{Path, PathBuf},
    sync::{
        atomic::{AtomicBool, Ordering},
        mpsc::{self, Receiver, Sender},
        Arc,
    },
    thread::{self, JoinHandle},
    time::Duration,
};

use anyhow::{Context, Result};

const MAX_REQUEST_BYTES: usize = 64 * 1024;
// Manifest write bodies carry the whole document (enemy-manifest.json is
// ~140KB of frame rects); the tooling API is a LAN operator surface, so allow
// generous bodies.
const MAX_MANIFEST_WRITE_BYTES: usize = 8 * 1024 * 1024;
const REQUEST_READ_TIMEOUT: Duration = Duration::from_secs(2);
// Product bootstrap currently carries the checked scene resources inline and is
// tens of megabytes. Leave enough headroom for a cold serialization and a LAN
// transfer instead of treating normal browser latency as a bridge failure.
const RESPONSE_WRITE_TIMEOUT: Duration = Duration::from_secs(60);
const RUNTIME_REPLY_TIMEOUT: Duration = Duration::from_secs(30);

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
enum ApiSurface {
    Product,
    Tools,
}

fn api_surface(path: &str) -> Option<ApiSurface> {
    if matches!(
        path,
        "/api/dagger-product/bootstrap"
            | "/api/dagger-product/state"
            | "/api/dagger-product/input"
            | "/api/dagger-product/readout"
            | "/api/dagger-product/session/reset"
            | "/api/dagger-product/equipment/equip"
            | "/api/dagger-product/equipment/unequip"
    ) {
        Some(ApiSurface::Product)
    } else if matches!(
        path,
        "/api/dagger-tools/content/jump"
            | "/api/dagger-tools/inventory/grant"
            | "/api/dagger-tools/sprites/index"
    ) || path.starts_with("/api/dagger-tools/sprites/asset/")
        || path.starts_with("/api/dagger-tools/sprites/manifest/")
    {
        Some(ApiSurface::Tools)
    } else {
        None
    }
}

pub(crate) enum ProductCommand {
    ProductBootstrap {
        reply: Sender<ProductReply>,
    },
    ProductState {
        reply: Sender<ProductReply>,
    },
    ProductInput {
        input: ProductInput,
        reply: Sender<ProductReply>,
    },
    Readout {
        reply: Sender<ProductReply>,
    },
    Reset {
        reply: Sender<ProductReply>,
    },
    Jump {
        id: u64,
        reply: Sender<ProductReply>,
    },
    Equip {
        item: u64,
        reply: Sender<ProductReply>,
    },
    Unequip {
        slot: String,
        reply: Sender<ProductReply>,
    },
    Grant {
        item: String,
        quantity: u64,
        reply: Sender<ProductReply>,
    },
}

#[derive(Debug, serde::Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub(crate) struct ProductInput {
    pub(crate) sequence: u64,
    pub(crate) step_seconds: f32,
    pub(crate) pressed_codes: Vec<String>,
    pub(crate) pressed_edges: Vec<String>,
    pub(crate) pointer_delta: [f32; 2],
    pub(crate) buttons: u16,
    pub(crate) button_pressed_edges: u16,
}

pub(crate) struct ProductReply {
    pub(crate) status: u16,
    pub(crate) body: String,
}

pub(crate) struct ProductServer {
    commands: Receiver<ProductCommand>,
    shutdown: Arc<AtomicBool>,
    worker: Option<JoinHandle<()>>,
    port: u16,
}

impl ProductServer {
    pub(crate) fn start(
        host: IpAddr,
        port: u16,
        static_root: PathBuf,
        content_root: PathBuf,
    ) -> Result<Self> {
        let listener = TcpListener::bind((host, port))
            .with_context(|| format!("bind Dagger product service on {host}:{port}"))?;
        listener
            .set_nonblocking(true)
            .context("make Dagger product service nonblocking")?;
        let port = listener.local_addr()?.port();
        let (send_command, commands) = mpsc::channel();
        let shutdown = Arc::new(AtomicBool::new(false));
        let worker_shutdown = Arc::clone(&shutdown);
        let worker = thread::Builder::new()
            .name("dagger-product-http".to_string())
            .spawn(move || {
                run(
                    listener,
                    send_command,
                    worker_shutdown,
                    static_root,
                    content_root,
                )
            })
            .context("start Dagger product service thread")?;
        Ok(Self {
            commands,
            shutdown,
            worker: Some(worker),
            port,
        })
    }

    pub(crate) fn port(&self) -> u16 {
        self.port
    }

    pub(crate) fn try_recv(&self) -> Result<ProductCommand, mpsc::TryRecvError> {
        self.commands.try_recv()
    }
}

impl Drop for ProductServer {
    fn drop(&mut self) {
        self.shutdown.store(true, Ordering::Release);
        if let Some(worker) = self.worker.take() {
            let _ = worker.join();
        }
    }
}

fn run(
    listener: TcpListener,
    commands: Sender<ProductCommand>,
    shutdown: Arc<AtomicBool>,
    static_root: PathBuf,
    content_root: PathBuf,
) {
    while !shutdown.load(Ordering::Acquire) {
        match listener.accept() {
            Ok((mut stream, _)) => {
                let _ = stream.set_read_timeout(Some(REQUEST_READ_TIMEOUT));
                let _ = stream.set_write_timeout(Some(RESPONSE_WRITE_TIMEOUT));
                if let Err(error) =
                    handle_request(&mut stream, &commands, &static_root, &content_root)
                {
                    eprintln!("DAGGER_PRODUCT_REQUEST_ERROR {error:#}");
                    let _ = write_response(
                        &mut stream,
                        500,
                        &format!(r#"{{"error":"Dagger product service failed: {error}"}}"#),
                    );
                }
            }
            Err(error) if error.kind() == std::io::ErrorKind::WouldBlock => {
                thread::sleep(Duration::from_millis(10));
            }
            Err(_) => thread::sleep(Duration::from_millis(10)),
        }
    }
}

fn handle_request(
    stream: &mut TcpStream,
    commands: &Sender<ProductCommand>,
    static_root: &Path,
    content_root: &Path,
) -> Result<()> {
    let request = read_request(stream)?;
    if request.method == "OPTIONS" {
        return write_response(stream, 204, "");
    }
    if request.method == "GET" && request.path == "/healthz" {
        return write_response(stream, 200, r#"{"status":"ok","project":"rusty-dagger"}"#);
    }
    // Sprite review reads derived content straight from disk: no runtime
    // authority is involved, so these never enter the command channel.
    if request.method == "GET" && request.path == "/api/dagger-tools/sprites/index" {
        return serve_sprite_index(stream, content_root);
    }
    if request.method == "GET" {
        if let Some(name) = request
            .path
            .strip_prefix("/api/dagger-tools/sprites/asset/")
        {
            return serve_sprite_asset(stream, content_root, name);
        }
    }
    if request.method == "POST" {
        if let Some(name) = request
            .path
            .strip_prefix("/api/dagger-tools/sprites/manifest/")
        {
            return write_sprite_manifest(stream, content_root, name, &request.body);
        }
    }
    if request.method == "GET" && !request.path.starts_with("/api/") {
        return serve_static(stream, static_root, &request.path);
    }
    if request.path.starts_with("/api/") && api_surface(&request.path).is_none() {
        return write_response(stream, 404, r#"{"error":"unknown Dagger product route"}"#);
    }
    let (send_reply, receive_reply) = mpsc::channel();
    let command =
        match (request.method.as_str(), request.path.as_str()) {
            ("GET", "/api/dagger-product/bootstrap") => {
                ProductCommand::ProductBootstrap { reply: send_reply }
            }
            ("GET", "/api/dagger-product/state") => {
                ProductCommand::ProductState { reply: send_reply }
            }
            ("POST", "/api/dagger-product/input") => {
                let input = match serde_json::from_str(&request.body) {
                    Ok(input) => input,
                    Err(error) => return write_response(
                        stream,
                        400,
                        &serde_json::json!({ "error": format!("invalid product input: {error}") })
                            .to_string(),
                    ),
                };
                ProductCommand::ProductInput {
                    input,
                    reply: send_reply,
                }
            }
            ("GET", "/api/dagger-product/readout") => ProductCommand::Readout { reply: send_reply },
            ("POST", "/api/dagger-product/session/reset") => {
                ProductCommand::Reset { reply: send_reply }
            }
            ("POST", "/api/dagger-tools/content/jump") => {
                let body: JumpRequest = match serde_json::from_str(&request.body) {
                    Ok(body) => body,
                    Err(error) => return write_response(
                        stream,
                        400,
                        &serde_json::json!({ "error": format!("invalid content jump: {error}") })
                            .to_string(),
                    ),
                };
                ProductCommand::Jump {
                    id: body.id,
                    reply: send_reply,
                }
            }
            ("POST", "/api/dagger-product/equipment/equip") => {
                let body: EquipRequest = match serde_json::from_str(&request.body) {
                    Ok(body) => body,
                    Err(error) => return write_response(
                        stream,
                        400,
                        &serde_json::json!({ "error": format!("invalid equip request: {error}") })
                            .to_string(),
                    ),
                };
                ProductCommand::Equip {
                    item: body.item,
                    reply: send_reply,
                }
            }
            ("POST", "/api/dagger-product/equipment/unequip") => {
                let body: UnequipRequest = match serde_json::from_str(&request.body) {
                    Ok(body) => body,
                    Err(error) => return write_response(
                        stream,
                        400,
                        &serde_json::json!({ "error": format!("invalid unequip request: {error}") })
                            .to_string(),
                    ),
                };
                ProductCommand::Unequip {
                    slot: body.slot,
                    reply: send_reply,
                }
            }
            ("POST", "/api/dagger-tools/inventory/grant") => {
                let body: GrantRequest = match serde_json::from_str(&request.body) {
                    Ok(body) => body,
                    Err(error) => return write_response(
                        stream,
                        400,
                        &serde_json::json!({ "error": format!("invalid grant request: {error}") })
                            .to_string(),
                    ),
                };
                ProductCommand::Grant {
                    item: body.item,
                    quantity: body.quantity,
                    reply: send_reply,
                }
            }
            _ => {
                return write_response(stream, 404, r#"{"error":"unknown Dagger product route"}"#);
            }
        };
    commands
        .send(command)
        .context("send command to Dagger runtime")?;
    let reply = receive_reply
        .recv_timeout(RUNTIME_REPLY_TIMEOUT)
        .context("wait for Dagger runtime reply")?;
    write_response(stream, reply.status, &reply.body)
}

#[derive(serde::Deserialize)]
struct JumpRequest {
    id: u64,
}

#[derive(serde::Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
struct EquipRequest {
    item: u64,
}

#[derive(serde::Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
struct UnequipRequest {
    slot: String,
}

#[derive(serde::Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
struct GrantRequest {
    item: String,
    quantity: u64,
}

fn serve_static(stream: &mut TcpStream, root: &Path, request_path: &str) -> Result<()> {
    let request_path = request_path.split('?').next().unwrap_or(request_path);
    if request_path.contains("..") {
        return write_response(stream, 404, r#"{"error":"unknown Dagger product asset"}"#);
    }
    let relative = if request_path == "/" {
        "index.html"
    } else {
        request_path.trim_start_matches('/')
    };
    let path = root.join(relative);
    let bytes = match std::fs::read(&path) {
        Ok(bytes) => bytes,
        Err(error) if error.kind() == std::io::ErrorKind::NotFound => {
            return write_response(stream, 404, r#"{"error":"unknown Dagger product asset"}"#)
        }
        Err(error) => return Err(error).with_context(|| format!("read {}", path.display())),
    };
    let content_type = match path.extension().and_then(|extension| extension.to_str()) {
        Some("html") => "text/html; charset=utf-8",
        Some("js") => "text/javascript; charset=utf-8",
        Some("css") => "text/css; charset=utf-8",
        Some("json") => "application/json; charset=utf-8",
        _ => "application/octet-stream",
    };
    write_bytes_response(stream, 200, content_type, &bytes)
}

/// Consolidated sprite-review index: every `*.json` manifest under
/// `content/textures/` parsed into a name-keyed map, plus the file listing of
/// each content subdirectory so the lab UI can cross-reference anything a
/// manifest does not mention. Directory-driven on purpose: new manifests or
/// assets appear without code changes. Unreadable or malformed manifests are
/// skipped with a log line rather than failing the whole index.
fn serve_sprite_index(stream: &mut TcpStream, content_root: &Path) -> Result<()> {
    let mut manifests = serde_json::Map::new();
    let mut files = serde_json::Map::new();
    for subdir in ["textures", "audio"] {
        let dir = content_root.join(subdir);
        let mut names = Vec::new();
        match std::fs::read_dir(&dir) {
            Ok(entries) => {
                for entry in entries.flatten() {
                    if !entry
                        .file_type()
                        .map(|kind| kind.is_file())
                        .unwrap_or(false)
                    {
                        continue;
                    }
                    names.push(entry.file_name().to_string_lossy().into_owned());
                }
            }
            Err(error) if error.kind() == std::io::ErrorKind::NotFound => {}
            Err(error) => return Err(error).with_context(|| format!("list {}", dir.display())),
        }
        names.sort();
        if subdir == "textures" {
            for name in &names {
                if !name.ends_with(".json") {
                    continue;
                }
                let parsed = std::fs::read_to_string(dir.join(name))
                    .ok()
                    .and_then(|text| serde_json::from_str::<serde_json::Value>(&text).ok());
                match parsed {
                    Some(value) => {
                        manifests.insert(name.clone(), value);
                    }
                    None => eprintln!("DAGGER_LAB_SPRITE_MANIFEST_SKIP {name}"),
                }
            }
        }
        files.insert(subdir.to_string(), serde_json::Value::from(names));
    }
    write_response(
        stream,
        200,
        &serde_json::json!({ "manifests": manifests, "files": files }).to_string(),
    )
}

/// Serve one derived content file (sprite atlas PNG, audio clip, …) by path
/// relative to the content root. Same traversal posture as `serve_static`.
fn serve_sprite_asset(stream: &mut TcpStream, content_root: &Path, name: &str) -> Result<()> {
    let name = name.split('?').next().unwrap_or(name);
    if name.contains("..") || name.starts_with('/') || name.contains('\\') {
        return write_response(stream, 404, r#"{"error":"unknown sprite asset"}"#);
    }
    let path = content_root.join(name);
    let bytes = match std::fs::read(&path) {
        Ok(bytes) => bytes,
        Err(error) if error.kind() == std::io::ErrorKind::NotFound => {
            return write_response(stream, 404, r#"{"error":"unknown sprite asset"}"#)
        }
        Err(error) => return Err(error).with_context(|| format!("read {}", path.display())),
    };
    let content_type = match path.extension().and_then(|extension| extension.to_str()) {
        Some("png") => "image/png",
        Some("jpg") | Some("jpeg") => "image/jpeg",
        Some("gif") => "image/gif",
        Some("webp") => "image/webp",
        Some("wav") => "audio/wav",
        Some("ogg") => "audio/ogg",
        Some("mp3") => "audio/mpeg",
        Some("json") => "application/json; charset=utf-8",
        _ => "application/octet-stream",
    };
    write_bytes_response(stream, 200, content_type, &bytes)
}

/// The manifest documents the lab may overwrite, with the top-level
/// collections each must carry. This table is the single place a manifest
/// opts into lab edits — anything not listed is not a write target.
const KNOWN_SPRITE_MANIFESTS: &[(&str, &[&str])] = &[
    ("billboard-manifest.json", &["billboards"]),
    ("combat-manifest.json", &["effects", "audio"]),
    ("enemy-manifest.json", &["enemies"]),
    ("manifest.json", &["textures"]),
];

/// Light structural validation for a manifest document: current schema
/// version, and each required collection present as an array of objects.
/// Deliberately shallow — field-level meaning belongs to the import pipeline
/// and the lab, and a malformed document is caught here before it can replace
/// a good one on disk.
fn validate_sprite_manifest(collections: &[&str], value: &serde_json::Value) -> Result<(), String> {
    let object = value
        .as_object()
        .ok_or_else(|| "manifest must be a JSON object".to_string())?;
    match object.get("schemaVersion") {
        Some(serde_json::Value::Number(version)) if version.as_u64() == Some(1) => {}
        _ => return Err("schemaVersion must be 1".to_string()),
    }
    for key in collections {
        match object.get(*key) {
            Some(serde_json::Value::Array(items)) if items.iter().all(|item| item.is_object()) => {}
            _ => return Err(format!("\"{key}\" must be an array of objects")),
        }
    }
    Ok(())
}

/// Persist one edited sprite manifest (the lab's sprite review tab writes
/// derived metadata: pivots, fps, frame rects). Only known manifest documents
/// that pass `validate_sprite_manifest` are written — unknown names and
/// malformed documents are rejected before anything touches disk. Valid
/// documents are written pretty-printed and atomically, then the project
/// documents are re-stamped so the manifest and docs never drift. A
/// regeneration failure is reported in the response, not hidden and not
/// fatal to the write — the operator decides what to do with a stale doc.
fn write_sprite_manifest(
    stream: &mut TcpStream,
    content_root: &Path,
    name: &str,
    body: &str,
) -> Result<()> {
    let name = name.split('?').next().unwrap_or(name);
    let Some((_, collections)) = KNOWN_SPRITE_MANIFESTS
        .iter()
        .find(|(known, _)| *known == name)
    else {
        return write_response(stream, 404, r#"{"error":"unknown sprite manifest"}"#);
    };
    let value: serde_json::Value = match serde_json::from_str::<serde_json::Value>(body) {
        Ok(value) => value,
        Err(error) => {
            return write_response(
                stream,
                400,
                &serde_json::json!({ "error": format!("invalid manifest JSON: {error}") })
                    .to_string(),
            )
        }
    };
    if let Err(error) = validate_sprite_manifest(collections, &value) {
        return write_response(
            stream,
            400,
            &serde_json::json!({ "error": error }).to_string(),
        );
    }
    let path = content_root.join("textures").join(name);
    let mut text = serde_json::to_string_pretty(&value).context("encode manifest JSON")?;
    text.push('\n');
    let tmp = path.with_extension("json.tmp");
    std::fs::write(&tmp, &text).with_context(|| format!("write {}", tmp.display()))?;
    std::fs::rename(&tmp, &path).with_context(|| format!("install {}", path.display()))?;

    // Re-stamp project docs from the updated manifests (content config, not
    // runtime authority). Best-effort: report the outcome in the response.
    let repo_root = content_root.parent().unwrap_or(content_root);
    let regenerate = match std::process::Command::new("python3")
        .arg("scripts/generate-project.py")
        .arg("--write")
        .current_dir(repo_root)
        .output()
    {
        Ok(output) if output.status.success() => "regenerated".to_string(),
        Ok(output) => format!(
            "failed: {}",
            String::from_utf8_lossy(&output.stderr)
                .lines()
                .next()
                .unwrap_or("unknown error")
        ),
        Err(error) => format!("failed: {error}"),
    };
    write_response(
        stream,
        200,
        &serde_json::json!({ "status": "ok", "manifest": name, "projectDocs": regenerate })
            .to_string(),
    )
}

struct HttpRequest {
    method: String,
    path: String,
    body: String,
}

fn read_request(stream: &mut TcpStream) -> Result<HttpRequest> {
    let mut bytes = Vec::new();
    let mut buffer = [0_u8; 4096];
    let header_end = loop {
        let count = stream.read(&mut buffer).context("read HTTP request")?;
        if count == 0 {
            anyhow::bail!("request ended before headers");
        }
        bytes.extend_from_slice(&buffer[..count]);
        if bytes.len() > MAX_REQUEST_BYTES {
            anyhow::bail!("request exceeds {MAX_REQUEST_BYTES} bytes");
        }
        if let Some(index) = bytes.windows(4).position(|window| window == b"\r\n\r\n") {
            break index + 4;
        }
    };
    let headers = std::str::from_utf8(&bytes[..header_end]).context("decode HTTP headers")?;
    let mut lines = headers.split("\r\n");
    let request_line = lines.next().context("missing HTTP request line")?;
    let mut request_parts = request_line.split_whitespace();
    let method = request_parts
        .next()
        .context("missing HTTP method")?
        .to_string();
    let path = request_parts
        .next()
        .context("missing HTTP path")?
        .to_string();
    let content_length = lines
        .find_map(|line| {
            let (name, value) = line.split_once(':')?;
            name.eq_ignore_ascii_case("content-length")
                .then(|| value.trim().parse::<usize>())
        })
        .transpose()
        .context("parse Content-Length")?
        .unwrap_or(0);
    let body_cap = if path.starts_with("/api/dagger-tools/sprites/manifest/") {
        MAX_MANIFEST_WRITE_BYTES
    } else {
        MAX_REQUEST_BYTES
    };
    if header_end + content_length > body_cap {
        anyhow::bail!("request body exceeds bridge limit");
    }
    while bytes.len() < header_end + content_length {
        let count = stream.read(&mut buffer).context("read HTTP request body")?;
        if count == 0 {
            anyhow::bail!("request ended before declared body length");
        }
        bytes.extend_from_slice(&buffer[..count]);
    }
    let body = String::from_utf8(bytes[header_end..header_end + content_length].to_vec())
        .context("decode HTTP body")?;
    Ok(HttpRequest { method, path, body })
}

fn write_response(stream: &mut TcpStream, status: u16, body: &str) -> Result<()> {
    write_bytes_response(
        stream,
        status,
        "application/json; charset=utf-8",
        body.as_bytes(),
    )
}

fn write_bytes_response(
    stream: &mut TcpStream,
    status: u16,
    content_type: &str,
    body: &[u8],
) -> Result<()> {
    let status_text = match status {
        200 => "OK",
        204 => "No Content",
        400 => "Bad Request",
        404 => "Not Found",
        _ => "Internal Server Error",
    };
    write!(
        stream,
        "HTTP/1.1 {status} {status_text}\r\nContent-Type: {content_type}\r\nContent-Length: {}\r\nAccess-Control-Allow-Origin: *\r\nAccess-Control-Allow-Methods: GET, PUT, POST, OPTIONS\r\nAccess-Control-Allow-Headers: Content-Type\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n",
        body.len()
    )
    .context("write HTTP response")?;
    stream.write_all(body).context("write HTTP response body")?;
    stream.flush().context("flush HTTP response")
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn product_and_tool_routes_have_distinct_neutral_surfaces() {
        for path in [
            "/api/dagger-product/bootstrap",
            "/api/dagger-product/readout",
            "/api/dagger-product/session/reset",
            "/api/dagger-product/equipment/equip",
        ] {
            assert_eq!(api_surface(path), Some(ApiSurface::Product), "{path}");
        }
        for path in [
            "/api/dagger-tools/content/jump",
            "/api/dagger-tools/inventory/grant",
            "/api/dagger-tools/sprites/index",
            "/api/dagger-tools/sprites/asset/enemies/rat.png",
            "/api/dagger-tools/sprites/manifest/enemy-manifest.json",
        ] {
            assert_eq!(api_surface(path), Some(ApiSurface::Tools), "{path}");
        }
        for retired in [
            "/api/dagger-lab",
            "/api/dagger-lab/reset",
            "/api/dagger-lab/equipment/equip",
            "/api/dagger-lab/sprites/index",
        ] {
            assert_eq!(api_surface(retired), None, "{retired}");
        }
    }
}
