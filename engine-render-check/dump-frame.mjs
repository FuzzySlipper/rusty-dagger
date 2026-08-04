#!/usr/bin/env node
/**
 * Dump the sanctioned protocol-14 render readout from the real
 * dagger-studio-adapter over stdio (same line-delimited JSON protocol as
 * scripts/check-adapter.py): openProject -> readProject -> closeProject.
 *
 * Writes into engine-render-check/generated/:
 * - frame.json            the `projection` RenderFrameDiff (inline static mesh)
 * - texture-manifest.json {kind: 'rusty_renderer_texture_resources.v1', resources}
 * - proof-input.json      camera poses + expected counts derived from the frame
 *
 * The browser page never parses the project doc; this dump is the only
 * project -> frame path, exactly as the Studio host consumes it.
 */
import { spawn } from 'node:child_process';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = dirname(fileURLToPath(import.meta.url));
const ROOT = resolve(HERE, '..');
const ADAPTER = process.env.RUSTY_STUDIO_ADAPTER
  ?? resolve(ROOT, 'target/debug/dagger-studio-adapter');
const PROJECT = 'content/projects/privateers-hold.project.json';
const PROTOCOL = 14;
const GENERATED = resolve(HERE, 'generated');
const SPRITE_FRAMES = resolve(ROOT, 'target/debug/dagger-sprite-frames');

/** Directional sprite frames from the Rust runtime authority (6595). */
async function runSpriteFrames(cameras) {
  const args = [
    resolve(ROOT, 'content/privateers-hold.scene.json'),
    ...cameras.map((c) => c.join(',')),
  ];
  const proc = spawn(SPRITE_FRAMES, args, { stdio: ['ignore', 'pipe', 'inherit'] });
  let out = '';
  proc.stdout.setEncoding('utf8');
  proc.stdout.on('data', (chunk) => {
    out += chunk;
  });
  const code = await new Promise((resolveExit) => proc.on('close', resolveExit));
  if (code !== 0) throw new Error(`dagger-sprite-frames exited ${code} (build it: cargo build -p dagger-runtime)`);
  return JSON.parse(out);
}

export async function dumpFrame() {
  const proc = spawn(ADAPTER, [], { stdio: ['pipe', 'pipe', 'inherit'] });
  let buffer = '';
  const pending = [];
  proc.stdout.setEncoding('utf8');
  proc.stdout.on('data', (chunk) => {
    buffer += chunk;
    for (;;) {
      const newline = buffer.indexOf('\n');
      if (newline < 0) break;
      const line = buffer.slice(0, newline);
      buffer = buffer.slice(newline + 1);
      if (line.trim() === '') continue;
      const waiter = pending.shift();
      if (waiter === undefined) throw new Error(`unexpected adapter output: ${line.slice(0, 200)}`);
      waiter(JSON.parse(line));
    }
  });
  const exchange = (request) => new Promise((resolveResponse, reject) => {
    pending.push(resolveResponse);
    proc.stdin.write(`${JSON.stringify(request)}\n`, (error) => {
      if (error) reject(error);
    });
  });

  try {
    const opened = await exchange({
      type: 'openProject', protocolVersion: PROTOCOL, requestId: 'open-1',
      root: ROOT, projectFile: PROJECT,
    });
    if (opened.type !== 'projectOpened') {
      throw new Error(`openProject failed: ${JSON.stringify(opened).slice(0, 600)}`);
    }
    const read = await exchange({ type: 'readProject', protocolVersion: PROTOCOL, requestId: 'read-1' });
    if (read.type !== 'projectRead') {
      throw new Error(`readProject failed: ${JSON.stringify(read).slice(0, 600)}`);
    }
    const closed = await exchange({ type: 'closeProject', protocolVersion: PROTOCOL, requestId: 'close-1' });
    if (closed.type !== 'projectClosed') {
      throw new Error(`closeProject failed: ${JSON.stringify(closed).slice(0, 600)}`);
    }

    const frame = read.project?.projection;
    const textureResources = read.project?.textureResources;
    if (!frame || !Array.isArray(frame.ops)) throw new Error('projectRead has no projection frame');
    if (!Array.isArray(textureResources)) throw new Error('projectRead has no textureResources');

    // Derived expectations straight from the frame (no hardcoded dungeon facts).
    const meshOp = frame.ops.find(
      (op) => op.op === 'defineStaticMesh' && op.asset?.asset === 'mesh/privateers-hold',
    );
    if (!meshOp) throw new Error('frame has no defineStaticMesh for mesh/privateers-hold');
    const payload = meshOp.asset.payload;
    const bounds = payload.bounds;
    const triangles = payload.source.indices.length / 3;
    const materialGroups = payload.groups.length;

    const center = [
      (bounds.min[0] + bounds.max[0]) / 2,
      (bounds.min[1] + bounds.max[1]) / 2,
      (bounds.min[2] + bounds.max[2]) / 2,
    ];
    const radius = Math.max(
      bounds.max[0] - bounds.min[0],
      bounds.max[1] - bounds.min[1],
      bounds.max[2] - bounds.min[2],
    );

    // Convert a lookAt target into the surface pose convention (rotation YXZ,
    // yaw 0 / pitch 0 looks down -Z): forward = (-cosP*sinY, sinP, -cosP*cosY).
    const lookAtPose = (position, target) => {
      const d = [target[0] - position[0], target[1] - position[1], target[2] - position[2]];
      const length = Math.hypot(d[0], d[1], d[2]);
      const f = [d[0] / length, d[1] / length, d[2] / length];
      return {
        position,
        pitchDegrees: Math.asin(f[1]) * 180 / Math.PI,
        yawDegrees: Math.atan2(-f[0], -f[2]) * 180 / Math.PI,
      };
    };

    const poses = {
      // Same camera math as the retired render-check/viewer.html.
      overview: lookAtPose(
        [center[0] + radius * 0.9, center[1] + radius * 0.65, center[2] + radius * 0.9],
        center,
      ),
      interior: lookAtPose([25.6, 1.6, -25.6], [25.6, 1.4, -76.8]),
    };

    // Directional enemy sprites (6595): scene.json enemy flats zipped with the
    // frame's enemy createSprite ops (same order), plus the two proof poses
    // around the enemy nearest the start marker. "front" puts the camera on
    // the enemy's facing side (Unity +z == glTF -z) so the classic front
    // sprite (orientation 0) shows; "back" expects orientation 4.
    const sceneMeta = JSON.parse(
      await readFile(resolve(ROOT, 'content/privateers-hold.scene.json'), 'utf8'),
    );
    const sceneEnemies = sceneMeta.enemies ?? [];
    const enemySprites = frame.ops.filter(
      (op) => op.op === 'createSprite' && op.sprite?.asset?.startsWith('texture/enemy-'),
    );
    if (enemySprites.length !== sceneEnemies.length) {
      throw new Error(
        `enemy count mismatch: ${enemySprites.length} sprite ops vs ${sceneEnemies.length} scene enemies`,
      );
    }
    const enemies = sceneEnemies.map((enemy, index) => ({
      handle: enemySprites[index].handle,
      mobileId: enemy.mobileId,
      name: enemy.name,
      position: enemy.position.map(Number),
    }));
    let targetIndex = 0;
    if (enemies.length > 0 && Array.isArray(sceneMeta.startMarker)) {
      let best = Infinity;
      enemies.forEach((enemy, index) => {
        const d = Math.hypot(
          enemy.position[0] - sceneMeta.startMarker[0],
          enemy.position[1] - sceneMeta.startMarker[1],
          enemy.position[2] - sceneMeta.startMarker[2],
        );
        if (d < best) {
          best = d;
          targetIndex = index;
        }
      });
    }
    const target = enemies[targetIndex] ?? null;
    const poseAssignments = {};
    let orbit = null;
    if (target !== null) {
      const feet = target.position;
      const aim = [feet[0], feet[1] + 1.2, feet[2]];
      // The 0.5m x offset keeps the camera off the degenerate exact-0/180
      // bearing (cross product sign is zero there) while staying well inside
      // the front/back 45-degree sectors.
      poses['enemy-front'] = lookAtPose([feet[0] + 0.5, feet[1] + 1.4, feet[2] - 4], aim);
      poses['enemy-back'] = lookAtPose([feet[0] + 0.5, feet[1] + 1.4, feet[2] + 4], aim);

      // Runtime-authoritative directional frames (6595 R6595-2): the Rust
      // runtime computes orientation frames via arena2::mobile; this harness
      // never re-implements the math. Static poses use the one-shot CLI; the
      // live orbit polls the same binary in --serve mode (spawned by
      // check.mjs, URL via RUSTY_SPRITE_SERVER).
      const spriteFrames = await runSpriteFrames(
        ['enemy-front', 'enemy-back'].map((name) => poses[name].position),
      );
      for (const [index, name] of ['enemy-front', 'enemy-back'].entries()) {
        poseAssignments[name] = spriteFrames.poses[index].assignments;
      }
      orbit = { aim, radius: 4, height: 1.4 };
    }

    await mkdir(GENERATED, { recursive: true });
    await writeFile(resolve(GENERATED, 'frame.json'), JSON.stringify(frame));
    await writeFile(resolve(GENERATED, 'texture-manifest.json'), JSON.stringify({
      kind: 'rusty_renderer_texture_resources.v1',
      resources: textureResources,
    }));
    await writeFile(resolve(GENERATED, 'enemies.json'), `${JSON.stringify({
      target: target === null ? null : { ...target, index: targetIndex },
      enemies,
      poseAssignments,
      orbit,
    }, null, 1)}\n`);
    await writeFile(resolve(GENERATED, 'proof-input.json'), `${JSON.stringify({
      poses,
      expectations: {
        triangles,
        materialGroups,
        textureResources: textureResources.length,
      },
    }, null, 1)}\n`);
    return { poses, expectations: { triangles, materialGroups, textureResources: textureResources.length } };
  } finally {
    proc.kill();
  }
}

if (process.argv[1] && import.meta.url === new URL(`file://${process.argv[1]}`).href) {
  const { expectations } = await dumpFrame();
  console.log(
    `frame dumped: triangles=${expectations.triangles} materialGroups=${expectations.materialGroups} `
    + `textureResources=${expectations.textureResources}`,
  );
}
