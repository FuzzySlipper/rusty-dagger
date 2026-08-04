/**
 * Browser page: mount the REAL rusty-engine renderer (renderer-three browser
 * surface) on the protocol-14 frame dumped from dagger-studio-adapter, with
 * texture resources admitted through renderer-host's
 * loadRendererTextureResourceSource (byte-length + sha256 verified, so the
 * schemaVersion-1 silent average-color fallback cannot pass unnoticed).
 *
 * Texture PNG bytes are served statically from ../content via vite publicDir:
 * manifest sourcePath 'content/textures/x.png' -> fetch('/textures/x.png').
 *
 * Query: ?cam=overview|interior (poses computed in dump-frame.mjs).
 * Exposes window.__proof (submission statistics + snapshot) or __failure.
 */
import { loadRendererTextureResourceSource } from '@rusty-engine/renderer-host';
import { mountRendererBrowserSurface } from '@rusty-engine/renderer-three';

window.__errors = [];
window.addEventListener('error', (event) => window.__errors.push(String(event.message)));
window.addEventListener('unhandledrejection', (event) => window.__errors.push(String(event.reason)));

async function main() {
  const params = new URLSearchParams(location.search);
  const cam = params.get('cam') ?? 'overview';

  const [frame, manifest, input] = await Promise.all([
    fetch('/generated/frame.json').then((r) => r.json()),
    fetch('/generated/texture-manifest.json').then((r) => r.json()),
    fetch('/generated/proof-input.json').then((r) => r.json()),
  ]);
  // enemy-orbit starts from the enemy-front pose and then walks the camera.
  const poseName = cam === 'enemy-orbit' ? 'enemy-front' : cam;
  const pose = input.poses[poseName];
  if (!pose) throw new Error(`unknown camera pose: ${cam}`);

  const textureResourceSource = await loadRendererTextureResourceSource(
    manifest,
    async (descriptor) => {
      const url = `/${descriptor.sourcePath.replace(/^content\//u, '')}`;
      const response = await fetch(url);
      if (!response.ok) throw new Error(`texture fetch failed ${response.status}: ${url}`);
      return response.arrayBuffer();
    },
  );

  const canvas = document.getElementById('renderer');
  const surface = mountRendererBrowserSurface(canvas, {
    autoStart: false,
    camera: {
      initialPose: pose,
      projection: { fovYDegrees: 60, near: 0.05, far: 2000 },
    },
    clearColor: 0x101418,
    frame,
    pixelRatio: 1,
    textureResourceSource,
  });
  let submission = surface.renderOnce(1);

  // Directional enemy sprite driver (6595). Frame selection is
  // projection-driven by engine design; the Daggerfall-side authority
  // (dagger-runtime, arena2::mobile semantics) computes the frames and this
  // page only applies them. enemy-front/enemy-back use the dump-time static
  // assignments; enemy-orbit is a live loop: per step it moves the camera,
  // polls the Rust authority (dagger-sprite-frames --serve, URL in
  // ?spriteserver=), applies the frames, and re-renders.
  let driver = null;
  if (cam === 'enemy-front' || cam === 'enemy-back') {
    const enemyData = await fetch('/generated/enemies.json').then((r) => r.json());
    driver = driveDirectionalSprites(surface, enemyData, enemyData.poseAssignments[cam]);
    submission = surface.renderOnce(1);
  } else if (cam === 'enemy-orbit') {
    const enemyData = await fetch('/generated/enemies.json').then((r) => r.json());
    const server = params.get('spriteserver');
    if (!server) throw new Error('enemy-orbit needs ?spriteserver=<url>');
    driver = await orbitDirectionalSprites(surface, enemyData, server);
    submission = surface.renderOnce(1);
  }

  const lighting = surface.lightingReadout();
  const framePng = await captureFramePng(canvas);
  window.__proof = {
    ready: true,
    cam,
    driver,
    statistics: {
      drawCallCount: submission.drawCallCount,
      triangleCount: submission.triangleCount,
      textureResourceCount: submission.textureResourceCount,
      materialResourceCount: submission.materialResourceCount,
      geometryResourceCount: submission.geometryResourceCount,
      renderHandleCount: submission.renderHandleCount,
    },
    retainedLightCount: lighting.retainedLights.length,
    snapshot: surface.snapshot(),
    expectations: input.expectations,
    framePng,
  };
}

/**
 * Apply one set of runtime-computed assignments (6595): updateSprite frame
 * per enemy. Renderer billboards (rusty-engine 6630) handle camera-facing.
 */
function applySpriteFrames(surface, enemies, assignments) {
  const ops = assignments.map((assignment) => ({
    op: 'updateSprite',
    handle: enemies[assignment.index].handle,
    frame: assignment.frame,
    tint: null,
    renderOrder: null,
    visible: null,
  }));
  surface.applyFrame({ schemaVersion: 1, ops });
  return Object.fromEntries(assignments.map((a) => [enemies[a.index].handle, a.frame]));
}

/** Static enemy pose: apply the dump-time assignments, report target state. */
function driveDirectionalSprites(surface, enemyData, assignments) {
  if (!Array.isArray(assignments)) throw new Error('no runtime sprite assignments for pose');
  const applied = applySpriteFrames(surface, enemyData.enemies, assignments);
  const target = enemyData.target;
  return {
    enemyCount: enemyData.enemies.length,
    appliedCount: Object.keys(applied).length,
    targetHandle: target?.handle ?? null,
    targetFrame: target === null ? null : applied[target.handle] ?? null,
    targetFrameReadback: target === null
      ? null
      : surface.renderer.objectFor(target.handle)?.userData?.frame ?? null,
  };
}

/**
 * Live camera-driven loop (6595 R6595-2): 8 orbit steps around the target
 * enemy; per step the camera moves, the Rust authority (dagger-sprite-frames
 * --serve) computes frames for the new pose, and the renderer-held frame is
 * read back. DFU maps positive camera bearings to descending orientation
 * indices, so a counterclockwise orbit from the front yields 0,7,6,...,1.
 */
async function orbitDirectionalSprites(surface, enemyData, server) {
  const { target, enemies, orbit } = enemyData;
  const sequence = [];
  let appliedCount = 0;
  for (let step = 0; step < 8; step += 1) {
    const theta = (1 + 45 * step) * Math.PI / 180;
    const position = [
      target.position[0] + orbit.radius * Math.sin(theta),
      target.position[1] + orbit.height,
      target.position[2] - orbit.radius * Math.cos(theta),
    ];
    const d = [orbit.aim[0] - position[0], orbit.aim[1] - position[1], orbit.aim[2] - position[2]];
    const length = Math.hypot(d[0], d[1], d[2]);
    const f = [d[0] / length, d[1] / length, d[2] / length];
    surface.setCameraPose({
      position,
      pitchDegrees: Math.asin(f[1]) * 180 / Math.PI,
      yawDegrees: Math.atan2(-f[0], -f[2]) * 180 / Math.PI,
    });
    const response = await fetch(`${server}/assignments?cam=${position.join(',')}`);
    if (!response.ok) throw new Error(`sprite server ${response.status}`);
    const { assignments } = await response.json();
    const applied = applySpriteFrames(surface, enemies, assignments);
    appliedCount = Object.keys(applied).length;
    surface.renderOnce(1);
    sequence.push(surface.renderer.objectFor(target.handle)?.userData?.frame ?? null);
  }
  return {
    mode: 'orbit',
    enemyCount: enemies.length,
    appliedCount,
    targetHandle: target.handle,
    orbitSequence: sequence,
  };
}

/**
 * Export the exact drawing-buffer pixels as a PNG data URL. readPixels runs
 * synchronously right after renderOnce, before the buffer is invalidated;
 * OffscreenCanvas re-encodes at native backing resolution (no CSS upscale
 * interpolation, which would wash out the texel-frequency metrics).
 */
function captureFramePng(canvas) {
  const gl = canvas.getContext('webgl2') ?? canvas.getContext('webgl');
  if (gl === null) throw new Error('WebGL context unavailable for frame capture');
  const width = gl.drawingBufferWidth;
  const height = gl.drawingBufferHeight;
  const pixels = new Uint8Array(width * height * 4);
  gl.readPixels(0, 0, width, height, gl.RGBA, gl.UNSIGNED_BYTE, pixels);
  const flipped = new Uint8ClampedArray(width * height * 4);
  for (let y = 0; y < height; y += 1) {
    flipped.set(
      pixels.subarray((height - 1 - y) * width * 4, (height - y) * width * 4),
      y * width * 4,
    );
  }
  const offscreen = new OffscreenCanvas(width, height);
  const context = offscreen.getContext('2d');
  context.putImageData(new ImageData(flipped, width, height), 0, 0);
  return offscreen.convertToBlob({ type: 'image/png' }).then(async (blob) => {
    const buffer = await blob.arrayBuffer();
    const bytes = new Uint8Array(buffer);
    let binary = '';
    for (let index = 0; index < bytes.length; index += 1) {
      binary += String.fromCharCode(bytes[index]);
    }
    return `data:image/png;base64,${btoa(binary)}`;
  });
}

main().catch((error) => {
  window.__failure = String(error && error.stack ? error.stack : error);
});
