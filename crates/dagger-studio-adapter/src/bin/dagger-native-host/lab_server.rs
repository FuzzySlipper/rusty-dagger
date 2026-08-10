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

pub(crate) enum LabCommand {
    ProductBootstrap {
        reply: Sender<LabReply>,
    },
    ProductState {
        reply: Sender<LabReply>,
    },
    ProductInput {
        input: ProductInput,
        reply: Sender<LabReply>,
    },
    Read {
        reply: Sender<LabReply>,
    },
    Apply {
        document: String,
        reply: Sender<LabReply>,
    },
    Evaluate {
        document: String,
        reply: Sender<LabReply>,
    },
    Reset {
        reply: Sender<LabReply>,
    },
    Play {
        reply: Sender<LabReply>,
    },
    Jump {
        id: u64,
        reply: Sender<LabReply>,
    },
}

#[derive(Debug, serde::Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub(crate) struct ProductInput {
    pub(crate) pressed_codes: Vec<String>,
    pub(crate) pointer_delta: [f32; 2],
    pub(crate) buttons: u16,
}

pub(crate) struct LabReply {
    pub(crate) status: u16,
    pub(crate) body: String,
}

pub(crate) struct LabServer {
    commands: Receiver<LabCommand>,
    shutdown: Arc<AtomicBool>,
    worker: Option<JoinHandle<()>>,
    port: u16,
}

impl LabServer {
    pub(crate) fn start(host: IpAddr, port: u16, static_root: PathBuf) -> Result<Self> {
        let listener = TcpListener::bind((host, port))
            .with_context(|| format!("bind Dagger Lab bridge on {host}:{port}"))?;
        listener
            .set_nonblocking(true)
            .context("make Dagger Lab bridge nonblocking")?;
        let port = listener.local_addr()?.port();
        let (send_command, commands) = mpsc::channel();
        let shutdown = Arc::new(AtomicBool::new(false));
        let worker_shutdown = Arc::clone(&shutdown);
        let worker = thread::Builder::new()
            .name("dagger-lab-http".to_string())
            .spawn(move || run(listener, send_command, worker_shutdown, static_root))
            .context("start Dagger Lab bridge thread")?;
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

    pub(crate) fn try_recv(&self) -> Result<LabCommand, mpsc::TryRecvError> {
        self.commands.try_recv()
    }
}

impl Drop for LabServer {
    fn drop(&mut self) {
        self.shutdown.store(true, Ordering::Release);
        if let Some(worker) = self.worker.take() {
            let _ = worker.join();
        }
    }
}

fn run(
    listener: TcpListener,
    commands: Sender<LabCommand>,
    shutdown: Arc<AtomicBool>,
    static_root: PathBuf,
) {
    while !shutdown.load(Ordering::Acquire) {
        match listener.accept() {
            Ok((mut stream, _)) => {
                let _ = stream.set_read_timeout(Some(Duration::from_secs(2)));
                let _ = stream.set_write_timeout(Some(Duration::from_secs(2)));
                if let Err(error) = handle_request(&mut stream, &commands, &static_root) {
                    let _ = write_response(
                        &mut stream,
                        500,
                        &format!(r#"{{"error":"Dagger Lab bridge failed: {error}"}}"#),
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
    commands: &Sender<LabCommand>,
    static_root: &Path,
) -> Result<()> {
    let request = read_request(stream)?;
    if request.method == "OPTIONS" {
        return write_response(stream, 204, "");
    }
    if request.method == "GET" && request.path == "/healthz" {
        return write_response(stream, 200, r#"{"status":"ok","project":"rusty-dagger"}"#);
    }
    if request.method == "GET" && !request.path.starts_with("/api/") {
        return serve_static(stream, static_root, &request.path);
    }
    let (send_reply, receive_reply) = mpsc::channel();
    let command =
        match (request.method.as_str(), request.path.as_str()) {
            ("GET", "/api/dagger-product/bootstrap") => {
                LabCommand::ProductBootstrap { reply: send_reply }
            }
            ("GET", "/api/dagger-product/state") => LabCommand::ProductState { reply: send_reply },
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
                LabCommand::ProductInput {
                    input,
                    reply: send_reply,
                }
            }
            ("GET", "/api/dagger-lab") => LabCommand::Read { reply: send_reply },
            ("PUT", "/api/dagger-lab/experiment") => LabCommand::Apply {
                document: request.body,
                reply: send_reply,
            },
            ("POST", "/api/dagger-lab/evaluate") => LabCommand::Evaluate {
                document: request.body,
                reply: send_reply,
            },
            ("POST", "/api/dagger-lab/reset") => LabCommand::Reset { reply: send_reply },
            ("POST", "/api/dagger-lab/play") => LabCommand::Play { reply: send_reply },
            ("POST", "/api/dagger-lab/content/jump") => {
                let body: JumpRequest = match serde_json::from_str(&request.body) {
                    Ok(body) => body,
                    Err(error) => return write_response(
                        stream,
                        400,
                        &serde_json::json!({ "error": format!("invalid content jump: {error}") })
                            .to_string(),
                    ),
                };
                LabCommand::Jump {
                    id: body.id,
                    reply: send_reply,
                }
            }
            _ => {
                return write_response(stream, 404, r#"{"error":"unknown Dagger Lab route"}"#);
            }
        };
    commands
        .send(command)
        .context("send command to Dagger runtime")?;
    let reply = receive_reply
        .recv_timeout(Duration::from_secs(3))
        .context("wait for Dagger runtime reply")?;
    write_response(stream, reply.status, &reply.body)
}

#[derive(serde::Deserialize)]
struct JumpRequest {
    id: u64,
}

fn serve_static(stream: &mut TcpStream, root: &Path, request_path: &str) -> Result<()> {
    let request_path = request_path.split('?').next().unwrap_or(request_path);
    if request_path.contains("..") {
        return write_response(stream, 404, r#"{"error":"unknown Dagger Lab asset"}"#);
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
            return write_response(stream, 404, r#"{"error":"unknown Dagger Lab asset"}"#)
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
    if header_end + content_length > MAX_REQUEST_BYTES {
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
