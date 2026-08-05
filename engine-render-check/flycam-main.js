/**
 * Interactive flycam for Privateer's Hold through the REAL rusty-engine
 * renderer (renderer-three browser surface, texture admission via
 * renderer-host). Pointer-lock mouse look + WASD/QE flight; every frame the
 * camera moves, directional enemy frames come from the Rust runtime authority
 * (dagger-sprite-frames --serve, proxied at /assignments by serve-flycam.mjs)
 * — this page never re-implements the orientation math (6595).
 */
import { loadRendererTextureResourceSource } from '@rusty-engine/renderer-host';
import { mountRendererBrowserSurface } from '@rusty-engine/renderer-three';

const hud = document.getElementById('hud');
const hint = document.getElementById('hint');
const canvas = document.getElementById('renderer');

async function main() {
  const [frame, manifest, enemies] = await Promise.all([
    fetch('/generated/frame.json').then((r) => r.json()),
    fetch('/generated/texture-manifest.json').then((r) => r.json()),
    fetch('/generated/enemies.json').then((r) => r.json()),
  ]);

  const textureResourceSource = await loadRendererTextureResourceSource(
    manifest,
    async (descriptor) => {
      const url = `/${descriptor.sourcePath.replace(/^content\//u, '')}`;
      const response = await fetch(url);
      if (!response.ok) throw new Error(`texture fetch failed ${response.status}: ${url}`);
      return response.arrayBuffer();
    },
  );

  // Spawn at the start marker (same default as the project player entity).
  const spawn = enemies.spawn ?? [25.6, 1.6, -25.6];
  const state = {
    position: [...spawn],
    yawDegrees: 180,
    pitchDegrees: 0,
    keys: new Set(),
    moved: true,
  };
  window.__flycam = state; // debug/test seam

  const surface = mountRendererBrowserSurface(canvas, {
    autoStart: false,
    camera: {
      initialPose: { position: state.position, yawDegrees: state.yawDegrees, pitchDegrees: 0 },
      projection: { fovYDegrees: 70, near: 0.05, far: 2000 },
    },
    clearColor: 0x101418,
    frame,
    pixelRatio: 1,
    textureResourceSource,
  });

  // --- input -------------------------------------------------------------
  // Click anywhere (the hint overlay sits on top of the canvas) to lock.
  const lock = () => canvas.requestPointerLock();
  canvas.addEventListener('click', lock);
  hint.addEventListener('click', lock);
  document.addEventListener('pointerlockchange', () => {
    hint.style.display = document.pointerLockElement === canvas ? 'none' : 'grid';
  });
  document.addEventListener('mousemove', (event) => {
    if (document.pointerLockElement !== canvas) return;
    state.yawDegrees -= event.movementX * 0.12;
    state.pitchDegrees = Math.max(-89, Math.min(89, state.pitchDegrees - event.movementY * 0.12));
    state.moved = true;
  });
  window.addEventListener('keydown', (event) => {
    if (['KeyW', 'KeyA', 'KeyS', 'KeyD', 'KeyQ', 'KeyE', 'ShiftLeft', 'ShiftRight'].includes(event.code)) {
      state.keys.add(event.code);
      event.preventDefault();
    }
  });
  window.addEventListener('keyup', (event) => state.keys.delete(event.code));

  // --- debug gizmos (G): anchor markers + sprite quad bounds ---------------
  let gizmosOn = false;
  let gizmosCreated = false;
  const GIZMO_HANDLE_BASE = 9_000_000;
  const gizmoHandles = [];
  window.addEventListener('keydown', (event) => {
    if (event.code !== 'KeyG') return;
    const ops = [];
    if (!gizmosCreated) {
      gizmosCreated = true;
      let i = 0;
      for (const sprite of enemies.sprites ?? []) {
        const color = sprite.kind === 'enemy' ? [0.2, 1.0, 0.3, 1.0] : [1.0, 0.85, 0.2, 1.0];
        const centerY = sprite.position[1] + sprite.size[1] * (0.5 - sprite.pivot[1]);
        // anchor marker: small solid cube at the sprite's authored position
        ops.push({ op: 'create', handle: GIZMO_HANDLE_BASE + i, parent: null, node: {
          geometry: { kind: 'cube' }, material: { color, wireframe: false },
          transform: { translation: sprite.position, rotation: [0, 0, 0, 1], scale: [0.08, 0.08, 0.08] },
          visible: false, layer: 'scene',
          metadata: { sourceEntity: null, sourceSceneNode: null, tags: [], label: 'sprite-anchor' },
        } });
        gizmoHandles.push(GIZMO_HANDLE_BASE + i);
        i += 1;
        // quad bounds: wireframe box of the sprite's size at its pivot
        ops.push({ op: 'create', handle: GIZMO_HANDLE_BASE + i, parent: null, node: {
          geometry: { kind: 'cube' }, material: { color, wireframe: true },
          transform: {
            translation: [sprite.position[0], centerY, sprite.position[2]],
            rotation: [0, 0, 0, 1], scale: [sprite.size[0], sprite.size[1], 0.02],
          },
          visible: false, layer: 'scene',
          metadata: { sourceEntity: null, sourceSceneNode: null, tags: [], label: 'sprite-bounds' },
        } });
        gizmoHandles.push(GIZMO_HANDLE_BASE + i);
        i += 1;
      }
    }
    gizmosOn = !gizmosOn;
    for (const handle of gizmoHandles) {
      ops.push({ op: 'update', handle, transform: null, material: null, visible: gizmosOn, metadata: null });
    }
    surface.applyFrame({ schemaVersion: 1, ops });
    surface.renderOnce(performance.now());
  });

  // --- nav grid gizmo (N): walkable cells near the camera ------------------
  // The grid comes from the committed dagger-navgrid artifact (Rust trimesh
  // derivation, task 6639) — the page only visualizes it. A fixed handle pool
  // is created once; each rebuild (debounced, on camera move) points the pool
  // at the nearest cells and hides the rest, so op counts stay bounded.
  const navgrid = await fetch('/projects/privateers-hold.navgrid.json').then((r) => r.json());
  const GRID_HANDLE_BASE = 8_000_000;
  const GRID_POOL = 2048;
  const GRID_RADIUS = 10;
  // Only cells near the camera's own level — Privateer's Hold stacks rooms in
  // the same columns, so an unfiltered gizmo reads as a solid slab.
  const GRID_VERTICAL_WINDOW = 6;
  let gridOn = false;
  let gridCreated = false;
  let gridBuiltAt = [NaN, NaN, NaN];
  let gridLastBuild = 0;
  const gridCells = navgrid.cells.map(([x, z, level, y]) => ({
    cx: (x + 0.5) * navgrid.cellSize,
    cz: (z + 0.5) * navgrid.cellSize,
    y,
  }));

  function rebuildGridGizmos(now) {
    const nearest = gridCells
      .map((cell) => ({
        cell,
        d2: (cell.cx - state.position[0]) ** 2 + (cell.cz - state.position[2]) ** 2,
      }))
      .filter((entry) => entry.d2 <= GRID_RADIUS * GRID_RADIUS
        && Math.abs(entry.cell.y - state.position[1]) <= GRID_VERTICAL_WINDOW)
      .sort((a, b) => a.d2 - b.d2)
      .slice(0, GRID_POOL);
    const ops = [];
    for (let i = 0; i < GRID_POOL; i += 1) {
      const handle = GRID_HANDLE_BASE + i;
      if (i < nearest.length) {
        const { cell } = nearest[i];
        ops.push({
          op: 'update', handle, material: null, metadata: null, visible: true,
          transform: {
            translation: [cell.cx, cell.y + 0.03, cell.cz],
            rotation: [0, 0, 0, 1],
            scale: [navgrid.cellSize * 0.7, 0.05, navgrid.cellSize * 0.7],
          },
        });
      } else {
        ops.push({ op: 'update', handle, transform: null, material: null, visible: false, metadata: null });
      }
    }
    surface.applyFrame({ schemaVersion: 1, ops });
    surface.renderOnce(now);
    gridBuiltAt = [...state.position];
    gridLastBuild = now;
  }

  window.addEventListener('keydown', (event) => {
    if (event.code !== 'KeyN') return;
    if (!gridCreated) {
      gridCreated = true;
      const ops = [];
      for (let i = 0; i < GRID_POOL; i += 1) {
        ops.push({ op: 'create', handle: GRID_HANDLE_BASE + i, parent: null, node: {
          geometry: { kind: 'cube' }, material: { color: [0.2, 0.9, 1.0, 0.85], wireframe: false },
          transform: { translation: [0, -100, 0], rotation: [0, 0, 0, 1], scale: [0.01, 0.01, 0.01] },
          visible: false, layer: 'scene',
          metadata: { sourceEntity: null, sourceSceneNode: null, tags: [], label: 'navgrid-cell' },
        } });
      }
      surface.applyFrame({ schemaVersion: 1, ops });
    }
    gridOn = !gridOn;
    if (gridOn) {
      rebuildGridGizmos(performance.now());
    } else {
      const ops = [];
      for (let i = 0; i < GRID_POOL; i += 1) {
        ops.push({ op: 'update', handle: GRID_HANDLE_BASE + i, transform: null, material: null, visible: false, metadata: null });
      }
      surface.applyFrame({ schemaVersion: 1, ops });
      surface.renderOnce(performance.now());
    }
  });

  // --- per-frame loop ------------------------------------------------------
  let last = performance.now();
  let lastRender = 0;
  const MIN_RENDER_MS = 5; // cap ~200Hz — the loop is RAF-driven on high-refresh displays
  let spriteRefresh = 0; // timestamp of last completed assignment fetch
  let fetching = false;
  const poseEquals = (a, b) => a[0] === b[0] && a[1] === b[1] && a[2] === b[2];
  let lastSpriteCamera = [NaN, NaN, NaN];

  function step(now) {
    const dt = Math.min(0.1, (now - last) / 1000);
    last = now;

    const yaw = state.yawDegrees * Math.PI / 180;
    const pitch = state.pitchDegrees * Math.PI / 180;
    // Forward matches the surface convention: yaw 0/pitch 0 looks down -Z.
    const forward = [-Math.cos(pitch) * Math.sin(yaw), Math.sin(pitch), -Math.cos(pitch) * Math.cos(yaw)];
    const right = [Math.cos(yaw), 0, -Math.sin(yaw)];
    const speed = (state.keys.has('ShiftLeft') || state.keys.has('ShiftRight') ? 14 : 5) * dt;
    const before = [...state.position];
    for (const key of state.keys) {
      if (key === 'KeyW') for (let i = 0; i < 3; i += 1) state.position[i] += forward[i] * speed;
      if (key === 'KeyS') for (let i = 0; i < 3; i += 1) state.position[i] -= forward[i] * speed;
      if (key === 'KeyD') for (let i = 0; i < 3; i += 1) state.position[i] += right[i] * speed;
      if (key === 'KeyA') for (let i = 0; i < 3; i += 1) state.position[i] -= right[i] * speed;
      if (key === 'KeyE') state.position[1] += speed;
      if (key === 'KeyQ') state.position[1] -= speed;
    }
    if (!poseEquals(before, state.position)) state.moved = true;

    if (state.moved) {
      surface.setCameraPose({
        position: state.position,
        yawDegrees: state.yawDegrees,
        pitchDegrees: state.pitchDegrees,
      });
      state.moved = false;
    }

    // Directional sprite refresh: at most one in flight, at most ~10 Hz, and
    // only when the camera actually changed.
    if (!fetching && !poseEquals(lastSpriteCamera, state.position) && now - spriteRefresh > 100) {
      fetching = true;
      const cam = state.position.map((v) => v.toFixed(3)).join(',');
      fetch(`/assignments?cam=${cam}`)
        .then((r) => r.json())
        .then(({ assignments }) => {
          surface.applyFrame({
            schemaVersion: 1,
            ops: assignments.map((a) => ({
              op: 'updateSprite',
              handle: enemies.enemies[a.index].handle,
              frame: a.frame,
              tint: null,
              renderOrder: null,
              visible: null,
            })),
          });
          lastSpriteCamera = [...state.position];
          spriteRefresh = performance.now();
        })
        .catch(() => { /* keep flying if the authority hiccups */ })
        .finally(() => {
          fetching = false;
        });
    }

    // Nav grid gizmo rebuild: debounced, only when the camera strayed far
    // enough that the visible cell set should change.
    if (
      gridOn && now - gridLastBuild > 250
      && ((state.position[0] - gridBuiltAt[0]) ** 2 + (state.position[2] - gridBuiltAt[2]) ** 2 > 9)
    ) {
      rebuildGridGizmos(now);
    }

    if (now - lastRender >= MIN_RENDER_MS) {
      surface.renderOnce(now);
      lastRender = now;
    }
    hud.textContent = `pos ${state.position.map((v) => v.toFixed(1)).join(', ')}  yaw ${state.yawDegrees.toFixed(0)}  pitch ${state.pitchDegrees.toFixed(0)}`;
    requestAnimationFrame(step);
  }
  requestAnimationFrame(step);
}

main().catch((error) => {
  hint.textContent = `flycam failed: ${error && error.stack ? error.stack : error}`;
});
