#!/usr/bin/env node
/** Focused protocol/HTTP check for the local rusty-dagger Studio host. */
import assert from 'node:assert/strict';
import { mkdtemp, rm, writeFile } from 'node:fs/promises';
import { spawnSync } from 'node:child_process';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const base = (process.env.RUSTY_STUDIO_URL ?? 'http://127.0.0.1:4173').replace(/\/$/u, '');
const root = resolve(process.env.RUSTY_DAGGER_ROOT ?? new URL('..', import.meta.url).pathname);
const project = 'content/projects/privateers-hold.project.json';

async function request(path, init) {
  const response = await fetch(`${base}${path}`, init);
  const text = await response.text();
  let value;
  try { value = JSON.parse(text); } catch { value = text; }
  assert.equal(response.ok, true, `${path} returned HTTP ${response.status}: ${text.slice(0, 400)}`);
  return value;
}

const health = await request('/health');
assert.equal(health.status, 'ok');
assert.equal(health.adapter, true);

const status = await request('/api/studio-status');
assert.equal(status.mode, 'managed');
assert.equal(status.engineSourceCommit, '880a119466faebbf19ed05e39206ff4ba87237a2');
assert.equal(status.staticBuild.engineSourceCommit, status.engineSourceCommit);
assert.match(status.staticBuild.artifactTreeSha256, /^[0-9a-f]{64}$/u);
assert.equal(status.staticBuild.files, 5);
assert.equal(status.runningAdapter.protocolVersion, 14);

const described = await request('/api/studio-adapter', {
  method: 'POST',
  headers: { 'content-type': 'application/json' },
  body: JSON.stringify({ type: 'describe', protocolVersion: 14, requestId: 'check-describe' }),
});
assert.equal(described.type, 'described');
assert.equal(described.adapter.adapterId, 'rusty-dagger.privateers-hold');

const opened = await request('/api/studio-adapter', {
  method: 'POST',
  headers: { 'content-type': 'application/json' },
  body: JSON.stringify({ type: 'openProject', protocolVersion: 14, requestId: 'check-open', root, projectFile: project }),
});
assert.equal(opened.type, 'projectOpened');
assert.equal(opened.project.identity.projectId, 'privateers-hold');
assert.equal(opened.project.identity.sourceSchemaVersion, 24);
assert.ok(opened.project.projection.ops.some((operation) => operation.op === 'defineStaticMesh'));
assert.ok(opened.project.projection.ops.some((operation) => operation.op === 'createStaticMeshInstance'));
assert.ok(opened.project.projection.ops.some((operation) => operation.op === 'createLight'));

const read = await request('/api/studio-adapter', {
  method: 'POST',
  headers: { 'content-type': 'application/json' },
  body: JSON.stringify({ type: 'readProject', protocolVersion: 14, requestId: 'check-read' }),
});
assert.equal(read.type, 'projectRead');
assert.equal(read.project.identity.projectHash, opened.project.identity.projectHash);

const projectDirectory = resolve(root, 'content/projects');
const files = await request(`/api/studio-host-files?directory=${encodeURIComponent(projectDirectory)}&extension=.json`);
assert.equal(files.ok, true);
assert.ok(files.entries.some((entry) => entry.name === 'privateers-hold.project.json'));

const settings = await request(`/api/studio-user-settings?projectRoot=${encodeURIComponent(root)}`);
assert.equal(settings.ok, true);
assert.equal(settings.canonicalProjectRoot, root);
assert.equal(typeof settings.projectKey, 'string');

const index = await fetch(`${base}/`);
assert.equal(index.ok, true);
assert.match(index.headers.get('content-type') ?? '', /text\/html/u);

const closed = await request('/api/studio-adapter', {
  method: 'POST',
  headers: { 'content-type': 'application/json' },
  body: JSON.stringify({ type: 'closeProject', protocolVersion: 14, requestId: 'check-close' }),
});
assert.equal(closed.type, 'projectClosed');

const mismatchRoot = await mkdtemp(join(tmpdir(), 'rusty-dagger-studio-mismatch-'));
try {
  await writeFile(join(mismatchRoot, 'index.html'), '<!doctype html><title>unproven</title>');
  const hostScript = fileURLToPath(new URL('./studio-host.mjs', import.meta.url));
  const mismatch = spawnSync(process.execPath, [hostScript, '--static-root', mismatchRoot], {
    cwd: root,
    encoding: 'utf8',
    timeout: 10_000,
  });
  assert.notEqual(mismatch.status, 0, 'unproven static roots must fail startup');
  assert.match(`${mismatch.stdout}\n${mismatch.stderr}`, /static root does not match Engine/u);
} finally {
  await rm(mismatchRoot, { recursive: true, force: true });
}

console.log(`STUDIO HOST CHECK PASSED (${opened.project.projection.ops.length} projection ops, ${files.entries.length} project files)`);
