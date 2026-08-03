import { createHash } from 'node:crypto';
import { lstat, readdir, readFile } from 'node:fs/promises';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const scriptDirectory = dirname(fileURLToPath(import.meta.url));
const manifestPath = join(scriptDirectory, 'studio-static-provenance.json');

export const expectedEngineSourceCommit = '880a119466faebbf19ed05e39206ff4ba87237a2';

export async function loadStudioStaticProvenance(path = manifestPath) {
  const manifest = JSON.parse(await readFile(path, 'utf8'));
  if (manifest.schemaVersion !== 1
    || manifest.engineSourceCommit !== expectedEngineSourceCommit
    || !Array.isArray(manifest.files)
    || manifest.files.length === 0
    || !/^[0-9a-f]{64}$/u.test(manifest.artifactTreeSha256 ?? '')) {
    throw new Error('invalid Studio static provenance manifest');
  }
  const paths = new Set();
  for (const file of manifest.files) {
    if (typeof file.path !== 'string' || file.path.length === 0
      || file.path.includes('\0') || file.path.includes('/')
      || paths.has(file.path) || !Number.isSafeInteger(file.size) || file.size < 0
      || !/^[0-9a-f]{64}$/u.test(file.sha256)) {
      throw new Error('invalid Studio static provenance file entry');
    }
    paths.add(file.path);
  }
  const ordered = [...manifest.files].sort((left, right) => left.path.localeCompare(right.path));
  const canonical = ordered.map((file) => `${file.path}\t${file.sha256}\t${file.size}\n`).join('');
  const treeSha256 = createHash('sha256').update(canonical).digest('hex');
  if (treeSha256 !== manifest.artifactTreeSha256) {
    throw new Error('Studio static provenance manifest tree hash is invalid');
  }
  return Object.freeze({
    ...manifest,
    files: Object.freeze(ordered),
  });
}

export async function verifyStudioStaticRoot(staticRoot, manifest) {
  const root = resolve(staticRoot);
  const entries = await readdir(root, { withFileTypes: true });
  const actualNames = entries.map((entry) => entry.name).sort();
  const expectedNames = manifest.files.map((file) => file.path).sort();
  if (actualNames.length !== expectedNames.length
    || actualNames.some((name, index) => name !== expectedNames[index])) {
    throw new Error(`Studio static root does not match Engine ${manifest.engineSourceCommit} artifact file set`);
  }
  const actual = [];
  for (const expected of manifest.files) {
    const path = join(root, expected.path);
    const metadata = await lstat(path);
    if (!metadata.isFile() || metadata.isSymbolicLink()) {
      throw new Error(`Studio static artifact is not a regular file: ${expected.path}`);
    }
    const bytes = await readFile(path);
    const sha256 = createHash('sha256').update(bytes).digest('hex');
    if (bytes.byteLength !== expected.size || sha256 !== expected.sha256) {
      throw new Error(`Studio static artifact does not match Engine ${manifest.engineSourceCommit}: ${expected.path}`);
    }
    actual.push(`${expected.path}\t${sha256}\t${bytes.byteLength}\n`);
  }
  return Object.freeze({
    engineSourceCommit: manifest.engineSourceCommit,
    artifactTreeSha256: manifest.artifactTreeSha256,
    files: manifest.files.length,
  });
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  const staticRoot = process.argv[2];
  if (!staticRoot) throw new Error('usage: studio-static-provenance.mjs <static-root>');
  const manifest = await loadStudioStaticProvenance();
  const result = await verifyStudioStaticRoot(staticRoot, manifest);
  console.log(`STUDIO STATIC PROVENANCE PASSED Engine ${result.engineSourceCommit} tree ${result.artifactTreeSha256} (${result.files} files)`);
}
