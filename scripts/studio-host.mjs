#!/usr/bin/env node
/**
 * Rusty Dagger's local Studio bridge.
 *
 * The Rust adapter is the authority.  This process only provides the HTTP
 * transport, exact host identity, bounded resource reads, and static serving
 * for the public Rusty Engine Studio application.
 */
import { createHash } from 'node:crypto';
import { createReadStream, existsSync } from 'node:fs';
import {
  access,
  lstat,
  mkdir,
  open,
  readFile,
  readdir,
  realpath,
  rename,
  stat,
} from 'node:fs/promises';
import { createServer } from 'node:http';
import { createInterface } from 'node:readline';
import { execFileSync, spawn } from 'node:child_process';
import { homedir } from 'node:os';
import {
  dirname,
  extname,
  isAbsolute,
  join,
  normalize,
  parse,
  relative,
  resolve,
} from 'node:path';
import { fileURLToPath } from 'node:url';

const repo = resolve(fileURLToPath(new URL('..', import.meta.url)));
const defaultStaticRoot = process.env.RUSTY_ENGINE_STUDIO_STATIC_ROOT
  ?? resolve(repo, '../rusty-engine/studio/dist/apps/studio-app/browser');
const maxBodyBytes = 256 * 1024;
const maxResourceBytes = 64 * 1024 * 1024;
const maxHostFileEntries = 512;
const maxHostPathBytes = 4096;
const maxSettingsBytes = 64 * 1024;
const settingsRoot = resolve(
  process.env.XDG_CONFIG_HOME?.trim() || join(homedir(), '.config'),
  'rusty-engine-studio',
  'projects',
);
function option(name, fallback) {
  const index = process.argv.indexOf(name);
  return index >= 0 && process.argv[index + 1] !== undefined ? process.argv[index + 1] : fallback;
}

const adapterBinary = resolve(option('--adapter-binary', resolve(repo, 'target/debug/dagger-studio-adapter')));
const staticRoot = resolve(option('--static-root', defaultStaticRoot));
const host = option('--host', '127.0.0.1');
const port = Number(option('--port', '4173'));
const consumerCommit = process.env.RUSTY_DAGGER_COMMIT
  ?? execFileSync('git', ['rev-parse', 'HEAD'], { cwd: repo, encoding: 'utf8' }).trim();
// The static root only needs to be an Engine Studio build; the engine moves
// fast and drift is fixed forward, not gated. RUSTY_ENGINE_SOURCE_COMMIT is
// an informational label for /api/studio-status, not an enforced pin.
if (!existsSync(join(staticRoot, 'index.html'))) {
  throw new Error(`Studio static app not found at ${staticRoot} (no index.html)`);
}
const engineRevision = process.env.RUSTY_ENGINE_SOURCE_COMMIT?.trim() ?? 'unknown';

class AdapterProcess {
  constructor(binary) {
    this.child = spawn(binary, [], { cwd: repo, stdio: ['pipe', 'pipe', 'inherit'] });
    this.lines = createInterface({ input: this.child.stdout });
    this.waiting = [];
    this.lines.on('line', (line) => {
      const next = this.waiting.shift();
      if (next === undefined) return;
      try { next.resolve(JSON.parse(line)); } catch (error) { next.reject(error); }
    });
    this.child.on('error', (error) => this.fail(error));
    this.child.on('exit', (code, signal) => this.fail(new Error(`adapter exited (${code ?? signal})`)));
  }

  fail(error) {
    for (const pending of this.waiting.splice(0)) pending.reject(error);
  }

  exchange(value) {
    return new Promise((resolvePromise, reject) => {
      this.waiting.push({ resolve: resolvePromise, reject });
      this.child.stdin.write(`${value}\n`, (error) => { if (error) reject(error); });
    });
  }

  close() {
    this.child.kill('SIGTERM');
    this.lines.close();
  }
}

const adapter = new AdapterProcess(adapterBinary);
const binarySha256 = createHash('sha256').update(await readFile(adapterBinary)).digest('hex');

function json(response, status, value) {
  const text = JSON.stringify(value);
  response.writeHead(status, {
    'cache-control': 'no-store',
    'content-type': 'application/json; charset=utf-8',
    'content-length': Buffer.byteLength(text),
  });
  response.end(text);
}

function error(response, status, message) {
  // `message` is the field the studio client's studioHostError reads.
  json(response, status, { error: message, message });
}

async function body(request) {
  const chunks = [];
  let size = 0;
  for await (const chunk of request) {
    size += chunk.length;
    if (size > maxBodyBytes) throw new Error('request exceeds 256 KiB');
    chunks.push(chunk);
  }
  return Buffer.concat(chunks).toString('utf8');
}

// Active session tracking for the strict host-status contract (engine
// f8b15a0): both null or both set, project file always project-relative.
let activeProjectRoot = null;
let activeProjectFile = null;

function hostStatus() {
  return {
    schemaVersion: 1,
    project: 'rusty-engine-studio',
    status: 'ok',
    // 'unmanaged': we spawn an explicit adapter binary without managed
    // certification; the decoder forbids managed identity claims here.
    mode: 'unmanaged',
    engineSourceCommit: null,
    configuredConsumer: null,
    activeProjectRoot,
    activeProjectFile,
    runningAdapter: {
      adapterId: 'rusty-dagger.privateers-hold',
      adapterVersion: 1,
      protocolVersion: 14,
      buildCommit: null,
      binarySha256,
    },
  };
}

function checkedStaticPath(pathname) {
  const decoded = decodeURIComponent(pathname);
  if (decoded.includes('\0')) throw new Error('malformed static path');
  const child = resolve(staticRoot, decoded === '/' ? 'index.html' : decoded.replace(/^\/+/, ''));
  const fromRoot = relative(staticRoot, child);
  if (fromRoot.startsWith('..') || isAbsolute(fromRoot)) throw new Error('static path escapes root');
  return child;
}

async function serveStatic(request, response, pathname) {
  if (!['GET', 'HEAD'].includes(request.method)) { response.writeHead(405, { allow: 'GET, HEAD' }); response.end(); return; }
  const file = checkedStaticPath(pathname);
  const metadata = await stat(file);
  if (!metadata.isFile()) throw new Error('static path is not a file');
  const fileExtension = extname(file).toLowerCase();
  response.writeHead(200, {
    'cache-control': 'no-cache',
    'content-type': fileExtension === '.html' ? 'text/html; charset=utf-8'
      : fileExtension === '.js' ? 'text/javascript; charset=utf-8'
        : fileExtension === '.css' ? 'text/css; charset=utf-8'
          : fileExtension === '.json' ? 'application/json; charset=utf-8'
            : fileExtension === '.svg' ? 'image/svg+xml'
              : fileExtension === '.png' ? 'image/png'
                : 'application/octet-stream',
    'content-length': metadata.size,
  });
  if (request.method === 'HEAD') { response.end(); return; }
  createReadStream(file).pipe(response);
}

async function serveResource(response, url) {
  const projectRoot = url.searchParams.get('projectRoot');
  const sourcePath = url.searchParams.get('sourcePath');
  const expected = url.searchParams.get('contentHash');
  if (!projectRoot || !sourcePath || !expected || !isAbsolute(projectRoot) || normalize(projectRoot) !== projectRoot || isAbsolute(sourcePath) || normalize(sourcePath) !== sourcePath || !/^sha256:[0-9a-f]{64}$/.test(expected)) {
    throw new Error('resource requires normalized projectRoot/sourcePath/contentHash');
  }
  if (!['.glb', '.png', '.rmesh'].includes(extname(sourcePath).toLowerCase())) throw new Error('unsupported resource extension');
  const file = resolve(projectRoot, sourcePath);
  const fromRoot = relative(resolve(projectRoot), file);
  if (fromRoot.startsWith('..') || isAbsolute(fromRoot)) throw new Error('resource escapes project root');
  const metadata = await lstat(file);
  if (!metadata.isFile() || metadata.isSymbolicLink() || metadata.size > maxResourceBytes) throw new Error('resource must be a bounded regular file');
  const bytes = await readFile(file);
  const actual = `sha256:${createHash('sha256').update(bytes).digest('hex')}`;
  if (actual !== expected) throw new Error('resource hash does not match the admitted content hash');
  response.writeHead(200, { 'cache-control': 'no-store', 'content-type': 'application/octet-stream', 'content-length': bytes.length });
  response.end(bytes);
}

function checkedHostDirectory(requested) {
  if (typeof requested !== 'string'
    || requested.trim().length === 0
    || Buffer.byteLength(requested, 'utf8') > maxHostPathBytes
    || requested.includes('\0')
    || !isAbsolute(requested)
    || normalize(requested) !== requested) {
    throw new Error('directory must be a bounded, absolute, normalized path');
  }
  return requested;
}

async function requireNoSymlinkChain(path) {
  const root = parse(path).root;
  let current = root;
  for (const part of path.slice(root.length).split('/').filter(Boolean)) {
    current = join(current, part);
    if ((await lstat(current)).isSymbolicLink()) {
      throw new Error('symbolic links are not accepted in host file paths');
    }
  }
}

async function listHostDirectory(url) {
  const directory = checkedHostDirectory(url.searchParams.get('directory'));
  await requireNoSymlinkChain(directory);
  const metadata = await lstat(directory);
  if (!metadata.isDirectory()) throw new Error('directory is not a regular directory');
  const extensions = url.searchParams.getAll('extension').map((value) => {
    const extension = value.trim().toLowerCase();
    if (!/^\.[a-z0-9][a-z0-9._-]*$/.test(extension) || extension.length > 17) {
      throw new Error(`invalid file extension filter: ${value}`);
    }
    return extension;
  });
  if (extensions.length > 16) throw new Error('too many file extension filters');
  const allEntries = (await readdir(directory, { withFileTypes: true }))
    .filter((entry) => !entry.isSymbolicLink())
    .flatMap((entry) => {
      if (entry.isDirectory()) return [{ name: entry.name, path: join(directory, entry.name), kind: 'directory' }];
      if (!entry.isFile()) return [];
      if (extensions.length > 0 && !extensions.some((extension) => entry.name.toLowerCase().endsWith(extension))) return [];
      return [{ name: entry.name, path: join(directory, entry.name), kind: 'file' }];
    })
    .sort((left, right) => left.kind === right.kind
      ? left.name.localeCompare(right.name)
      : left.kind === 'directory' ? -1 : 1);
  return {
    ok: true,
    directory,
    parent: directory === parse(directory).root ? null : dirname(directory),
    entries: allEntries.slice(0, maxHostFileEntries),
    truncated: allEntries.length > maxHostFileEntries,
  };
}

async function canonicalProjectRoot(requested) {
  if (typeof requested !== 'string' || requested.trim().length === 0
    || Buffer.byteLength(requested, 'utf8') > maxHostPathBytes || requested.includes('\0')) {
    throw new Error('projectRoot must be a bounded non-empty path');
  }
  const absolute = resolve(requested);
  try { return await realpath(absolute); } catch { return absolute; }
}

function settingsLocation(projectRoot) {
  const digest = createHash('sha256').update(projectRoot).digest('hex');
  return {
    canonicalProjectRoot: projectRoot,
    projectKey: `rusty-studio-project:${digest}`,
    settingsRoot,
    path: join(settingsRoot, `${digest}.json`),
  };
}

async function readUserSettings(url) {
  const location = settingsLocation(await canonicalProjectRoot(url.searchParams.get('projectRoot')));
  try { await access(location.path); } catch {
    return { ok: true, exists: false, ...location, text: null, sha256: null };
  }
  const metadata = await lstat(location.path);
  if (metadata.isSymbolicLink() || !metadata.isFile() || metadata.size > maxSettingsBytes) {
    throw new Error('host-user settings must be a bounded regular file');
  }
  const bytes = await readFile(location.path);
  const digest = createHash('sha256').update(bytes).digest('hex');
  return { ok: true, exists: true, ...location, text: bytes.toString('utf8'), sha256: digest };
}

async function writeUserSettings(request) {
  const input = JSON.parse(await body(request));
  const location = settingsLocation(await canonicalProjectRoot(input.projectRoot));
  const text = typeof input.text === 'string' ? input.text : '';
  const bytes = Buffer.from(text, 'utf8');
  if (bytes.byteLength > maxSettingsBytes) throw new Error('host-user settings exceed 64 KiB');
  let parsed;
  try { parsed = JSON.parse(text); } catch { throw new Error('host-user settings must be valid JSON'); }
  if (parsed === null || typeof parsed !== 'object' || Array.isArray(parsed)
    || parsed.projectKey !== location.projectKey) {
    throw new Error('host-user settings projectKey does not match the canonical project root');
  }
  const current = await readUserSettings(new URL(
    `http://host/api/studio-user-settings?projectRoot=${encodeURIComponent(location.canonicalProjectRoot)}`,
  ));
  if ((input.expectedHash ?? null) !== current.sha256) throw new Error('host-user settings changed; reload before saving');
  await mkdir(location.settingsRoot, { recursive: true, mode: 0o700 });
  const temporary = join(location.settingsRoot, `.${process.pid}-${Date.now()}.tmp`);
  const file = await open(temporary, 'wx', 0o600);
  try {
    await file.writeFile(bytes);
    await file.sync();
  } finally {
    await file.close();
  }
  await rename(temporary, location.path);
  return { ok: true, path: location.path, sha256: createHash('sha256').update(bytes).digest('hex') };
}

const server = createServer(async (request, response) => {
  try {
    const url = new URL(request.url ?? '/', `http://${host}:${port}`);
    if (url.pathname === '/health') return json(response, 200, { status: 'ok', adapter: true, engineRevision, consumerCommit });
    if (url.pathname === '/api/studio-status') return json(response, 200, hostStatus());
    if (url.pathname === '/api/studio-session/open') {
      // Session transaction (engine d488a56): describe + openProject in one
      // envelope; the strict decoder requires exactly these keys.
      if (request.method !== 'POST') { response.writeHead(405, { allow: 'POST' }); response.end(); return; }
      const value = JSON.parse(await body(request));
      const sessionRoot = resolve(String(value.root ?? ''));
      const projectFile = String(value.projectFile ?? '');
      const described = await adapter.exchange(JSON.stringify({
        type: 'describe', protocolVersion: 14, requestId: 'session-describe',
      }));
      const opened = await adapter.exchange(JSON.stringify({
        type: 'openProject', protocolVersion: 14, requestId: 'session-open',
        root: sessionRoot, projectFile,
      }));
      if (opened.type !== 'projectOpened') {
        return error(response, 502, `session openProject failed: ${JSON.stringify(opened).slice(0, 300)}`);
      }
      activeProjectRoot = sessionRoot;
      activeProjectFile = projectFile;
      return json(response, 200, {
        schemaVersion: 1,
        type: 'studioSessionOpened',
        adapter: described.adapter,
        project: opened.project,
        hostStatus: hostStatus(),
      });
    }
    if (url.pathname === '/api/studio-adapter') {
      if (request.method !== 'POST') { response.writeHead(405, { allow: 'POST' }); response.end(); return; }
      const value = JSON.parse(await body(request));
      const reply = await adapter.exchange(JSON.stringify(value));
      // Track the active project across the plain adapter path too (the
      // studio shell's close flow uses it).
      if (value.type === 'openProject' && reply.type === 'projectOpened') {
        activeProjectRoot = resolve(String(value.root ?? ''));
        activeProjectFile = String(value.projectFile ?? '');
      } else if (value.type === 'closeProject' && reply.type === 'projectClosed') {
        activeProjectRoot = null;
        activeProjectFile = null;
      }
      return json(response, 200, reply);
    }
    if (url.pathname === '/api/studio-render-resource') {
      if (request.method !== 'GET') { response.writeHead(405, { allow: 'GET' }); response.end(); return; }
      return await serveResource(response, url);
    }
    if (url.pathname === '/api/studio-host-files') {
      if (request.method !== 'GET') { response.writeHead(405, { allow: 'GET' }); response.end(); return; }
      return json(response, 200, await listHostDirectory(url));
    }
    if (url.pathname === '/api/studio-user-settings') {
      if (request.method === 'GET') return json(response, 200, await readUserSettings(url));
      if (request.method === 'PUT') return json(response, 200, await writeUserSettings(request));
      response.writeHead(405, { allow: 'GET, PUT' }); response.end(); return;
    }
    return await serveStatic(request, response, url.pathname);
  } catch (caught) {
    error(response, 400, caught instanceof Error ? caught.message : String(caught));
  }
});

server.listen(port, host, () => {
  console.log(`rusty-dagger Studio host listening on http://${host}:${port}`);
  console.log(`static root: ${staticRoot}`);
  console.log(`Engine revision: ${engineRevision}`);
});

function shutdown() { server.close(); adapter.close(); }
process.on('SIGINT', shutdown);
process.on('SIGTERM', shutdown);
