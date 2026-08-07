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

  // --- debug gizmos (G): anchor markers + sprite quad bounds + live heading (6671) ---
  // G toggles authored spawn markers plus LIVE patrol markers that track
  // dagger-runtime::patrol heading. The live markers prove: where NPCs actually
  // are (not where they spawned), whether heading is applied, and whether the
  // renderer respects rotation (cylindrical billboards ignore it — the arrow shows it).
  let gizmosOn = false;
  let gizmosCreated = false;
  const GIZMO_HANDLE_BASE = 9_000_000;
  const gizmoHandles = [];
  const liveGizmoMap = new Map(); // sprite handle -> { anchor, bounds, arrow, basePos, baseSize, basePivot, kind }
  const lastPatrolByHandle = new Map(); // handle -> { translation, rotation, heading }
  // expose for headless proof
  window.__liveGizmoMap = liveGizmoMap;
  window.__lastPatrolByHandle = lastPatrolByHandle;
  window.__gizmosOn = () => gizmosOn;
  window.addEventListener('keydown', (event) => {
    if (event.code !== 'KeyG') return;
    const ops = [];
    if (!gizmosCreated) {
      gizmosCreated = true;
      let i = 0;
      for (const sprite of enemies.sprites ?? []) {
        const color = sprite.kind === 'enemy' ? [0.2, 1.0, 0.3, 1.0] : [1.0, 0.85, 0.2, 1.0];
        const centerY = sprite.position[1] + sprite.size[1] * (0.5 - sprite.pivot[1]);
        // anchor: authored spawn (static reference, dimmer)
        const anchorHandle = GIZMO_HANDLE_BASE + i;
        ops.push({ op: 'create', handle: anchorHandle, parent: null, node: {
          geometry: { kind: 'cube' }, material: { color: [color[0]*0.5, color[1]*0.5, color[2]*0.5, 0.6], wireframe: false },
          transform: { translation: sprite.position, rotation: [0, 0, 0, 1], scale: [0.06, 0.06, 0.06] },
          visible: false, layer: 'scene',
          metadata: { sourceEntity: null, sourceSceneNode: null, tags: [], label: 'sprite-anchor-authored' },
        } });
        gizmoHandles.push(anchorHandle);
        i += 1;
        // bounds: authored quad (wireframe)
        const boundsHandle = GIZMO_HANDLE_BASE + i;
        ops.push({ op: 'create', handle: boundsHandle, parent: null, node: {
          geometry: { kind: 'cube' }, material: { color, wireframe: true },
          transform: {
            translation: [sprite.position[0], centerY, sprite.position[2]],
            rotation: [0, 0, 0, 1], scale: [sprite.size[0], sprite.size[1], 0.02],
          },
          visible: false, layer: 'scene',
          metadata: { sourceEntity: null, sourceSceneNode: null, tags: [], label: 'sprite-bounds-authored' },
        } });
        gizmoHandles.push(boundsHandle);
        i += 1;
        // live anchor: tracks patrol position (solid, larger)
        const liveAnchorHandle = GIZMO_HANDLE_BASE + i;
        ops.push({ op: 'create', handle: liveAnchorHandle, parent: null, node: {
          geometry: { kind: 'cube' }, material: { color, wireframe: false },
          transform: { translation: sprite.position, rotation: [0, 0, 0, 1], scale: [0.09, 0.09, 0.09] },
          visible: false, layer: 'scene',
          metadata: { sourceEntity: null, sourceSceneNode: null, tags: [], label: 'sprite-anchor-live' },
        } });
        gizmoHandles.push(liveAnchorHandle);
        i += 1;
        // heading arrow: thin box pointing along +X rotated by heading (Y yaw). Only for enemies.
        const arrowHandle = GIZMO_HANDLE_BASE + i;
        const arrowVisible = sprite.kind === 'enemy';
        ops.push({ op: 'create', handle: arrowHandle, parent: null, node: {
          geometry: { kind: 'cube' }, material: { color: [1.0, 0.2, 0.2, 1.0], wireframe: false },
          transform: { translation: sprite.position, rotation: [0, 0, 0, 1], scale: [0.35, 0.02, 0.02] },
          visible: false, layer: 'scene',
          metadata: { sourceEntity: null, sourceSceneNode: null, tags: [], label: 'sprite-heading' },
        } });
        gizmoHandles.push(arrowHandle);
        // Only enemies get live tracking; billboards keep static arrow hidden
        if (sprite.kind === 'enemy') {
          // Find corresponding sprite handle for patrol mapping (enemies.json `enemies` array aligns with sprites of kind enemy)
          // Map sprite handle from the `enemies` list (which holds patrol handle == sprite handle)
          const patHandle = enemies.enemies?.find((e) => {
            const dx = e.position[0] - sprite.position[0];
            const dz = e.position[2] - sprite.position[2];
            return Math.abs(dx) < 0.01 && Math.abs(dz) < 0.01;
          })?.handle ?? null;
          // Fallback: use sprite index mapping if position match fails
          const fallbackHandle = enemies.enemies?.[Array.from(liveGizmoMap.values()).filter((v)=>v.kind==='enemy').length]?.handle ?? null;
          const mapHandle = patHandle ?? fallbackHandle ?? sprite.handle ?? 0;
          liveGizmoMap.set(mapHandle, { anchor: liveAnchorHandle, bounds: boundsHandle, arrow: arrowHandle, baseSize: sprite.size, basePivot: sprite.pivot, kind: sprite.kind });
        }
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
  let spriteRefresh = 0; // timestamp of last completed sprite authority fetch
  let fetching = false;
  const poseEquals = (a, b) => a[0] === b[0] && a[1] === b[1] && a[2] === b[2];

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

    // Consolidated sprite frame refresh (6640): the Rust AnimationService
    // (dagger-sprite-frames --serve) owns BOTH directional enemy orientation
    // AND env flat animation (torch flames). This page polls it at ~10Hz and
    // applies the result in a single applyFrame. No per-sprite polling, no
    // JS frame math — the Rust authority is the single source of truth.
    if (!fetching && now - spriteRefresh > 100) {
      fetching = true;
      const cam = state.position.map((v) => v.toFixed(3)).join(',');
      fetch(`/assignments?cam=${cam}`)
        .then((r) => r.json())
        .then(({ updates, transforms }) => {
          const ops = [];
          if (updates) {
            ops.push(...updates.map((u) => ({
              op: 'updateSprite', handle: u.handle, frame: u.frame,
              tint: null, renderOrder: null, visible: null,
            })));
          }
          if (transforms) {
            for (const t of transforms) {
              lastPatrolByHandle.set(t.handle, t);
            }
            ops.push(...transforms.map((t) => ({
              op: 'update', handle: t.handle, material: null, metadata: null,
              visible: null,
              transform: {
                translation: t.translation, rotation: t.rotation ?? [0, 0, 0, 1], scale: [1, 1, 1],
              },
            })));
            // Live gizmo tracking (6671): move the G gizmo cubes/arrows to patrol positions + heading
            if (gizmosOn && liveGizmoMap.size > 0) {
              for (const t of transforms) {
                const entry = liveGizmoMap.get(t.handle);
                if (!entry) continue;
                const rot = t.rotation ?? [0, 0, 0, 1];
                // Live anchor tracks translation exactly
                ops.push({ op: 'update', handle: entry.anchor, transform: { translation: t.translation, rotation: [0, 0, 0, 1], scale: [0.09, 0.09, 0.09] }, material: null, visible: true, metadata: null });
                // Bounds follows live anchor but with authored size at its pivot height
                const liveCenterY = t.translation[1] + entry.baseSize[1] * (0.5 - entry.basePivot[1]);
                ops.push({ op: 'update', handle: entry.bounds, transform: { translation: [t.translation[0], liveCenterY, t.translation[2]], rotation: [0, 0, 0, 1], scale: [entry.baseSize[0], entry.baseSize[1], 0.02] }, material: null, visible: true, metadata: null });
                // Heading arrow: positioned 0.18m out along heading, rotated by heading
                const heading = t.heading ?? 0;
                const offX = Math.cos(heading) * 0.18;
                const offZ = Math.sin(heading) * 0.18;
                const arrowY = t.translation[1] + 0.12;
                ops.push({ op: 'update', handle: entry.arrow, transform: { translation: [t.translation[0] + offX, arrowY, t.translation[2] + offZ], rotation: rot, scale: [0.35, 0.02, 0.02] }, material: null, visible: true, metadata: null });
              }
            }
          }
          if (ops.length > 0) {
            surface.applyFrame({ schemaVersion: 1, ops });
          }
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
