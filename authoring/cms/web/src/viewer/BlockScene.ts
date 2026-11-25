import * as THREE from 'three';
import { OrbitControls } from 'three/examples/jsm/controls/OrbitControls.js';
import type { WorldBlockDetailResponse, WorldBlockGroundTile, WorldBlockModel } from '../types';
import { daggerfallSourceRotation, parseRstatmsh, type ParsedRstatmsh } from './rstatmsh';

export type LayerName = 'models' | 'ground' | 'doors' | 'people' | 'billboards' | 'markers' | 'water';

export type PickKind =
  | 'model'
  | 'door'
  | 'transitionDoor'
  | 'gate'
  | 'peopleSlot'
  | 'staticNpc'
  | 'billboard'
  | 'light'
  | 'questMarker'
  | 'randomEnemy'
  | 'container'
  | 'marker';

export interface ScenePick {
  pickKey: string;
  kind: PickKind;
  id: string;
  label: string;
  position: [number, number, number];
  buildingIndex?: number | null;
  modelId?: string;
  index?: number;
}

export interface SceneLoadProgress {
  loadedMeshes: number;
  totalMeshes: number;
  failedMeshes: number;
  triangles: number;
}

export interface BlockSceneCallbacks {
  onSelect: (pick: ScenePick | null) => void;
  onProgress?: (progress: SceneLoadProgress) => void;
}

const MATERIAL_RE = /^material\/(texture-\d+-\d+)$/;
const SPRITE_RE = /^sprite\/(texture-\d+-\d+)$/;
const GROUND_TILE_METRES = 6.4;

function makeFallbackMaterial(wireframe: boolean, color = 0x7c8a96): THREE.MeshStandardMaterial {
  return new THREE.MeshStandardMaterial({
    color,
    roughness: 0.9,
    metalness: 0.0,
    wireframe,
    side: THREE.FrontSide,
  });
}

export class BlockSceneController {
  private container: HTMLElement;
  private callbacks: BlockSceneCallbacks;
  private renderer: THREE.WebGLRenderer;
  private scene: THREE.Scene;
  private camera: THREE.PerspectiveCamera;
  private controls: OrbitControls;
  private resizeObserver: ResizeObserver;
  private rafId = 0;
  private disposed = false;

  private layers: Record<LayerName, THREE.Group>;
  private wireframe = false;
  private allMeshMaterials = new Set<THREE.MeshStandardMaterial>();
  private pickableObjects: THREE.Object3D[] = [];
  private pickByKey = new Map<string, { object: THREE.Object3D; pick: ScenePick }>();
  private selectionHelper: THREE.Box3Helper | null = null;

  private meshCache = new Map<string, Promise<ParsedRstatmsh>>();
  private materialCache = new Map<string, THREE.MeshStandardMaterial>();
  private spriteMaterialCache = new Map<string, THREE.SpriteMaterial>();
  private textureLoader = new THREE.TextureLoader();

  private defaultTarget = new THREE.Vector3(0, 2, 0);
  private defaultCameraPos = new THREE.Vector3(24, 18, 24);
  private currentKind: string = '';
  private currentBounds: THREE.Box3 | null = null;
  private clipPlane = new THREE.Plane(new THREE.Vector3(0, -1, 0), 1000);
  private keysPressed = new Set<string>();
  private lastFrameTime = performance.now();
  private pointerDownPos: { x: number; y: number } | null = null;
  private loadGeneration = 0;

  constructor(container: HTMLElement, callbacks: BlockSceneCallbacks) {
    this.container = container;
    this.callbacks = callbacks;

    this.scene = new THREE.Scene();
    this.scene.background = new THREE.Color(0x13191d);

    const width = Math.max(container.clientWidth, 320);
    const height = Math.max(container.clientHeight, 320);
    this.camera = new THREE.PerspectiveCamera(55, width / height, 0.1, 1500);
    this.camera.position.copy(this.defaultCameraPos);

    this.renderer = new THREE.WebGLRenderer({ antialias: true, powerPreference: 'high-performance' });
    this.renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
    this.renderer.setSize(width, height);
    this.renderer.outputColorSpace = THREE.SRGBColorSpace;
    container.innerHTML = '';
    container.appendChild(this.renderer.domElement);

    // Balanced lighting so exteriors, interiors, and dark dungeons are all readable
    const hemi = new THREE.HemisphereLight(0xeef5ff, 0x3b444a, 1.35);
    this.scene.add(hemi);
    const keyLight = new THREE.DirectionalLight(0xfff6e5, 0.95);
    keyLight.position.set(60, 110, 45);
    this.scene.add(keyLight);
    const fillLight = new THREE.DirectionalLight(0x9ec5e6, 0.45);
    fillLight.position.set(-50, 40, -60);
    this.scene.add(fillLight);

    this.layers = {
      models: new THREE.Group(),
      ground: new THREE.Group(),
      doors: new THREE.Group(),
      people: new THREE.Group(),
      billboards: new THREE.Group(),
      markers: new THREE.Group(),
      water: new THREE.Group(),
    };
    for (const [name, group] of Object.entries(this.layers)) {
      group.name = `layer:${name}`;
      this.scene.add(group);
    }

    this.controls = new OrbitControls(this.camera, this.renderer.domElement);
    this.controls.enableDamping = true;
    this.controls.dampingFactor = 0.08;
    this.controls.maxDistance = 600;
    this.controls.minDistance = 0.5;
    this.controls.target.copy(this.defaultTarget);

    this.renderer.domElement.addEventListener('pointerdown', this.onPointerDown);
    this.renderer.domElement.addEventListener('pointerup', this.onPointerUp);
    window.addEventListener('keydown', this.onKeyDown);
    window.addEventListener('keyup', this.onKeyUp);

    this.resizeObserver = new ResizeObserver(() => this.handleResize());
    this.resizeObserver.observe(container);

    this.lastFrameTime = performance.now();
    this.animate();
  }

  private handleResize() {
    if (this.disposed) return;
    const width = Math.max(this.container.clientWidth, 1);
    const height = Math.max(this.container.clientHeight, 1);
    this.camera.aspect = width / height;
    this.camera.updateProjectionMatrix();
    this.renderer.setSize(width, height);
  }

  private onKeyDown = (event: KeyboardEvent) => {
    const tag = (event.target as HTMLElement | null)?.tagName;
    if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT') return;
    const key = event.key.toLowerCase();
    if (['w', 'a', 's', 'd', 'q', 'e', 'shift'].includes(key)) {
      this.keysPressed.add(key);
    }
  };

  private onKeyUp = (event: KeyboardEvent) => {
    this.keysPressed.delete(event.key.toLowerCase());
  };

  private onPointerDown = (event: PointerEvent) => {
    if (event.button !== 0) return;
    this.pointerDownPos = { x: event.clientX, y: event.clientY };
  };

  private onPointerUp = (event: PointerEvent) => {
    if (event.button !== 0 || !this.pointerDownPos) return;
    const dx = event.clientX - this.pointerDownPos.x;
    const dy = event.clientY - this.pointerDownPos.y;
    this.pointerDownPos = null;
    if (Math.hypot(dx, dy) > 5) return;

    const rect = this.renderer.domElement.getBoundingClientRect();
    const ndc = new THREE.Vector2(
      ((event.clientX - rect.left) / rect.width) * 2 - 1,
      -((event.clientY - rect.top) / rect.height) * 2 + 1,
    );
    const raycaster = new THREE.Raycaster();
    raycaster.setFromCamera(ndc, this.camera);

    const visiblePickables = this.pickableObjects.filter(obj => {
      let cur: THREE.Object3D | null = obj;
      while (cur) {
        if (!cur.visible) return false;
        cur = cur.parent;
      }
      return true;
    });

    const hits = raycaster.intersectObjects(visiblePickables, false);
    if (!hits.length) {
      this.highlightObject(null);
      this.callbacks.onSelect(null);
      return;
    }

    // Prefer non-model overlays if close to the front, otherwise closest hit
    const first = hits[0].object;
    const pick = first.userData.pick as ScenePick | undefined;
    if (pick) {
      this.highlightObject(first);
      this.callbacks.onSelect(pick);
    }
  };

  private animate = () => {
    if (this.disposed) return;
    this.rafId = requestAnimationFrame(this.animate);
    const now = performance.now();
    const dt = Math.min((now - this.lastFrameTime) / 1000, 0.1);
    this.lastFrameTime = now;

    if (this.keysPressed.size > 0) {
      const speed = (this.keysPressed.has('shift') ? 36 : 14) * dt;
      const forward = new THREE.Vector3();
      this.camera.getWorldDirection(forward);
      const right = new THREE.Vector3().crossVectors(forward, this.camera.up).normalize();
      const move = new THREE.Vector3();
      if (this.keysPressed.has('w')) move.addScaledVector(forward, speed);
      if (this.keysPressed.has('s')) move.addScaledVector(forward, -speed);
      if (this.keysPressed.has('d')) move.addScaledVector(right, speed);
      if (this.keysPressed.has('a')) move.addScaledVector(right, -speed);
      if (this.keysPressed.has('e')) move.y += speed;
      if (this.keysPressed.has('q')) move.y -= speed;
      if (move.lengthSq() > 0) {
        this.camera.position.add(move);
        this.controls.target.add(move);
      }
    }

    this.controls.update();
    this.renderer.render(this.scene, this.camera);
  };

  setLayerVisible(layer: LayerName, visible: boolean) {
    this.layers[layer].visible = visible;
  }

  setWireframe(wireframe: boolean) {
    this.wireframe = wireframe;
    for (const mat of this.allMeshMaterials) {
      mat.wireframe = wireframe;
    }
  }

  setClipHeight(maxY: number | null) {
    if (maxY === null) {
      this.renderer.clippingPlanes = [];
    } else {
      this.clipPlane.constant = maxY;
      this.renderer.clippingPlanes = [this.clipPlane];
    }
  }

  resetCamera() {
    this.camera.position.copy(this.defaultCameraPos);
    this.controls.target.copy(this.defaultTarget);
    this.controls.update();
  }

  selectByKey(pickKey: string | null, focus = false) {
    if (!pickKey) {
      this.highlightObject(null);
      return;
    }
    const entry = this.pickByKey.get(pickKey);
    if (!entry) {
      this.highlightObject(null);
      return;
    }
    this.highlightObject(entry.object);
    if (focus) {
      const box = new THREE.Box3().setFromObject(entry.object);
      const center = box.isEmpty()
        ? new THREE.Vector3(...entry.pick.position)
        : box.getCenter(new THREE.Vector3());
      const size = box.isEmpty() ? 2.5 : Math.max(box.getSize(new THREE.Vector3()).length(), 2.5);
      const offset = this.camera.position.clone().sub(this.controls.target);
      if (offset.lengthSq() < 0.01) offset.set(1, 0.4, 1);
      const desiredDist =
        this.currentKind === 'rmbInterior'
          ? Math.min(Math.max(size * 1.3, 2.4), 4.5)
          : Math.min(Math.max(size * 1.6, 5), 60);
      offset.normalize().multiplyScalar(desiredDist);
      const nextCam = center.clone().add(offset);
      if (this.currentKind === 'rmbInterior' && this.currentBounds) {
        const margin = 0.55;
        const innerMin = this.currentBounds.min.clone().addScalar(margin);
        const innerMax = this.currentBounds.max.clone().subScalar(margin);
        if (innerMin.x < innerMax.x && innerMin.y < innerMax.y && innerMin.z < innerMax.z) {
          nextCam.clamp(innerMin, innerMax);
        }
      }
      this.controls.target.copy(center);
      this.camera.position.copy(nextCam);
      this.controls.update();
    }
  }

  private highlightObject(object: THREE.Object3D | null) {
    if (this.selectionHelper) {
      this.scene.remove(this.selectionHelper);
      this.selectionHelper.dispose();
      this.selectionHelper = null;
    }
    if (!object) return;
    const box = new THREE.Box3().setFromObject(object);
    if (box.isEmpty()) return;
    box.expandByScalar(0.08);
    this.selectionHelper = new THREE.Box3Helper(box, 0x4de096);
    this.scene.add(this.selectionHelper);
  }

  private clearScene() {
    this.highlightObject(null);
    this.pickableObjects = [];
    this.pickByKey.clear();
    for (const group of Object.values(this.layers)) {
      while (group.children.length > 0) {
        const child = group.children[group.children.length - 1];
        group.remove(child);
        // Dispose non-cached geometries owned by overlay/ground meshes
        if ((child as THREE.Mesh).geometry && !child.userData.sharedGeometry) {
          (child as THREE.Mesh).geometry.dispose();
        }
        if (child.userData.ownedMaterial) {
          const mat = (child as THREE.Mesh).material;
          if (Array.isArray(mat)) mat.forEach(m => m.dispose());
          else if (mat) mat.dispose();
        }
      }
    }
  }

  private registerPick(object: THREE.Object3D, pick: ScenePick) {
    object.userData.pick = pick;
    this.pickableObjects.push(object);
    this.pickByKey.set(pick.pickKey, { object, pick });
  }

  private getMaterial(materialName: string, materialsBaseUrl: string): THREE.MeshStandardMaterial {
    const match = MATERIAL_RE.exec(materialName);
    const key = match ? match[1] : '__fallback__';
    const existing = this.materialCache.get(key);
    if (existing) {
      existing.wireframe = this.wireframe;
      return existing;
    }

    if (!match) {
      const mat = makeFallbackMaterial(this.wireframe);
      this.materialCache.set(key, mat);
      this.allMeshMaterials.add(mat);
      return mat;
    }

    const url = `${materialsBaseUrl}${match[1]}.png`;
    const texture = this.textureLoader.load(url);
    texture.wrapS = THREE.RepeatWrapping;
    texture.wrapT = THREE.RepeatWrapping;
    texture.magFilter = THREE.NearestFilter;
    texture.minFilter = THREE.NearestMipmapLinearFilter;
    texture.flipY = false;
    texture.colorSpace = THREE.SRGBColorSpace;

    const mat = new THREE.MeshStandardMaterial({
      map: texture,
      roughness: 0.92,
      metalness: 0.0,
      wireframe: this.wireframe,
      alphaTest: 0.25,
    });
    this.materialCache.set(key, mat);
    this.allMeshMaterials.add(mat);
    return mat;
  }

  private getTerrainMaterial(archive: number, record: number, terrainBaseUrl: string): THREE.MeshStandardMaterial {
    const key = `terrain-${archive}-${record}`;
    const existing = this.materialCache.get(key);
    if (existing) {
      existing.wireframe = this.wireframe;
      return existing;
    }
    const url = `${terrainBaseUrl}texture-${archive}-${record}.png`;
    const texture = this.textureLoader.load(url);
    texture.wrapS = THREE.ClampToEdgeWrapping;
    texture.wrapT = THREE.ClampToEdgeWrapping;
    texture.magFilter = THREE.NearestFilter;
    texture.minFilter = THREE.NearestMipmapLinearFilter;
    texture.flipY = false;
    texture.colorSpace = THREE.SRGBColorSpace;

    const mat = new THREE.MeshStandardMaterial({
      map: texture,
      roughness: 0.96,
      metalness: 0.0,
      wireframe: this.wireframe,
    });
    this.materialCache.set(key, mat);
    this.allMeshMaterials.add(mat);
    return mat;
  }

  private getSpriteMaterial(url: string, tint = 0xffffff): THREE.SpriteMaterial {
    const key = `${url}#${tint}`;
    const existing = this.spriteMaterialCache.get(key);
    if (existing) return existing;
    const texture = this.textureLoader.load(url);
    texture.magFilter = THREE.NearestFilter;
    texture.minFilter = THREE.NearestFilter;
    texture.colorSpace = THREE.SRGBColorSpace;
    const mat = new THREE.SpriteMaterial({
      map: texture,
      color: tint,
      transparent: true,
      alphaTest: 0.2,
    });
    this.spriteMaterialCache.set(key, mat);
    return mat;
  }

  private loadMesh(meshArtifactId: string, geometryBaseUrl: string): Promise<ParsedRstatmsh> {
    const fileName = `${meshArtifactId.replace(/^geometry\//, '')}.rstatmsh`;
    const url = `${geometryBaseUrl}${fileName}`;
    const cached = this.meshCache.get(url);
    if (cached) return cached;
    const promise = fetch(url)
      .then(res => {
        if (!res.ok) throw new Error(`HTTP ${res.status} loading ${fileName}`);
        return res.arrayBuffer();
      })
      .then(buf => parseRstatmsh(buf));
    this.meshCache.set(url, promise);
    return promise;
  }

  async loadBlock(detail: WorldBlockDetailResponse) {
    const gen = ++this.loadGeneration;
    this.clearScene();
    const { block, mediaUrls, terrainArchive } = detail;

    // 1. Frame camera from block bounds immediately so viewport is centered even before meshes arrive
    this.currentKind = block.kind;
    this.frameFromBounds(block.bounds, block.kind, block.enterMarker?.position ?? null);

    // 2. Build RMB ground grid if present
    if (block.ground?.length) {
      this.buildGround(block.ground, terrainArchive, mediaUrls.terrain);
    }

    // 3. Build doors, transition doors, gates
    this.buildDoors(detail);

    // 4. Build people (daggerfall.people slots + staticNpcs)
    this.buildPeople(detail);

    // 5. Build billboards
    this.buildBillboards(detail);

    // 6. Build markers (lights, quest markers, random enemies, containers, start/enter)
    this.buildMarkers(detail);

    // 7. Build water plane if present
    if (typeof block.waterLevel === 'number') {
      this.buildWater(block.waterLevel, block.bounds);
    }

    // 8. Stream placed `.rstatmsh` models in deduplicated batches
    const placeableModels = (block.models ?? []).filter(m => Boolean(m.meshArtifactId));
    const uniqueArtifacts = [...new Set(placeableModels.map(m => m.meshArtifactId as string))];
    const totalMeshes = uniqueArtifacts.length;
    let loadedMeshes = 0;
    let failedMeshes = 0;
    let triangles = 0;

    const report = () => {
      if (gen !== this.loadGeneration || this.disposed) return;
      this.callbacks.onProgress?.({ loadedMeshes, totalMeshes, failedMeshes, triangles });
    };
    report();

    const parsedByArtifact = new Map<string, ParsedRstatmsh>();
    const BATCH = 16;
    for (let i = 0; i < uniqueArtifacts.length; i += BATCH) {
      if (gen !== this.loadGeneration || this.disposed) return;
      const slice = uniqueArtifacts.slice(i, i + BATCH);
      await Promise.all(
        slice.map(async artifactId => {
          try {
            const parsed = await this.loadMesh(artifactId, mediaUrls.geometry);
            parsedByArtifact.set(artifactId, parsed);
            loadedMeshes += 1;
          } catch {
            failedMeshes += 1;
          }
        }),
      );
      report();
    }

    if (gen !== this.loadGeneration || this.disposed) return;

    for (let idx = 0; idx < placeableModels.length; idx++) {
      const m = placeableModels[idx];
      const parsed = parsedByArtifact.get(m.meshArtifactId as string);
      if (!parsed) continue;
      triangles += (parsed.geometry.index?.count ?? 0) / 3;
      this.addPlacedModel(m, idx, parsed, mediaUrls.materials);
    }

    // If block lacked explicit bounds, frame from loaded models
    if (!block.bounds && this.layers.models.children.length > 0) {
      const box = new THREE.Box3().setFromObject(this.layers.models);
      if (!box.isEmpty()) {
        this.frameFromBox(box, block.kind);
      }
    }

    report();
  }

  private frameFromBounds(
    bounds: WorldBlockDetailResponse['block']['bounds'],
    kind: string,
    enterPos: { x: number; y: number; z: number } | null = null,
  ) {
    if (!bounds) {
      this.currentBounds = null;
      this.defaultTarget.set(0, 2, 0);
      this.defaultCameraPos.set(20, 16, 20);
      this.resetCamera();
      return;
    }
    const box = new THREE.Box3(
      new THREE.Vector3(bounds.minimum.x, bounds.minimum.y, bounds.minimum.z),
      new THREE.Vector3(bounds.maximum.x, bounds.maximum.y, bounds.maximum.z),
    );
    this.currentBounds = box;
    this.frameFromBox(box, kind, enterPos);
  }

  private frameFromBox(
    box: THREE.Box3,
    kind: string,
    enterPos: { x: number; y: number; z: number } | null = null,
  ) {
    const center = box.getCenter(new THREE.Vector3());
    const size = box.getSize(new THREE.Vector3());

    if (kind === 'rmbInterior') {
      // Stand inside the room (near enterMarker when available) looking toward the interior center
      const lookY = Math.min(box.min.y + 1.6, center.y);
      this.defaultTarget.set(center.x, lookY, center.z);
      if (enterPos) {
        const cam = new THREE.Vector3(enterPos.x, Math.min(enterPos.y + 1.85, box.max.y - 0.6), enterPos.z);
        // Nudge slightly inward from the doorway toward center so camera never grazes door wall
        cam.lerp(this.defaultTarget, 0.14);
        this.defaultCameraPos.copy(cam);
      } else {
        this.defaultCameraPos.set(
          center.x + Math.min(size.x * 0.32, 3.5),
          Math.min(box.min.y + 2.1, box.max.y - 0.6),
          center.z + Math.min(size.z * 0.32, 3.5),
        );
      }
      this.resetCamera();
      return;
    }

    const radius = Math.max(size.length() * 0.5, 4);
    const dist = radius * 1.15;
    this.defaultTarget.copy(center);
    this.defaultCameraPos.set(center.x + dist * 0.65, center.y + dist * 0.65, center.z + dist * 0.72);
    this.resetCamera();
  }

  private addPlacedModel(
    model: WorldBlockModel,
    index: number,
    parsed: ParsedRstatmsh,
    materialsBaseUrl: string,
  ) {
    const materials = parsed.materialNames.length
      ? parsed.materialNames.map(name => this.getMaterial(name, materialsBaseUrl))
      : [makeFallbackMaterial(this.wireframe)];

    const mesh = new THREE.Mesh(parsed.geometry, materials.length === 1 ? materials[0] : materials);
    mesh.position.set(model.position.x, model.position.y, model.position.z);
    mesh.quaternion.copy(daggerfallSourceRotation(model.rotationDegrees));
    mesh.userData.sharedGeometry = true;

    const label =
      model.buildingIndex !== null && model.buildingIndex !== undefined
        ? `Model ${model.modelId} (Building #${model.buildingIndex})`
        : `Model ${model.modelId}`;

    this.registerPick(mesh, {
      pickKey: `model:${model.id}`,
      kind: 'model',
      id: model.id,
      label,
      position: [model.position.x, model.position.y, model.position.z],
      buildingIndex: model.buildingIndex,
      modelId: model.modelId,
      index,
    });
    this.layers.models.add(mesh);
  }

  private buildGround(tiles: WorldBlockGroundTile[], terrainArchive: number, terrainBaseUrl: string) {
    // Group quads by textureRecord so we create <= 56 draw calls for the 256-tile ground grid
    const byRecord = new Map<number, { positions: number[]; normals: number[]; uvs: number[]; indices: number[] }>();

    for (const tile of tiles) {
      const rec = tile.textureRecord < 56 ? tile.textureRecord : 2;
      let bucket = byRecord.get(rec);
      if (!bucket) {
        bucket = { positions: [], normals: [], uvs: [], indices: [] };
        byRecord.set(rec, bucket);
      }
      const x0 = tile.x * GROUND_TILE_METRES;
      const x1 = (tile.x + 1) * GROUND_TILE_METRES;
      const yRow = 15 - tile.y;
      const z0 = -(yRow * GROUND_TILE_METRES);
      const z1 = -((yRow + 1) * GROUND_TILE_METRES);

      const base = bucket.positions.length / 3;
      // Right-handed CCW upward (+Y) quad matching RmbBlockContent.GroundTile
      bucket.positions.push(
        x0, -0.02, z0,
        x1, -0.02, z0,
        x1, -0.02, z1,
        x0, -0.02, z1,
      );
      bucket.normals.push(0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1, 0);

      let uvs: Array<[number, number]> = tile.rotated
        ? [
            [0, 1],
            [0, 0],
            [1, 0],
            [1, 1],
          ]
        : [
            [0, 0],
            [1, 0],
            [1, 1],
            [0, 1],
          ];
      if (tile.flipped) {
        uvs = [...uvs].reverse();
      }
      for (const [u, v] of uvs) {
        bucket.uvs.push(u, v);
      }
      bucket.indices.push(base, base + 1, base + 2, base, base + 2, base + 3);
    }

    for (const [rec, bucket] of byRecord.entries()) {
      const geo = new THREE.BufferGeometry();
      geo.setAttribute('position', new THREE.Float32BufferAttribute(bucket.positions, 3));
      geo.setAttribute('normal', new THREE.Float32BufferAttribute(bucket.normals, 3));
      geo.setAttribute('uv', new THREE.Float32BufferAttribute(bucket.uvs, 2));
      geo.setIndex(bucket.indices);
      const mat = this.getTerrainMaterial(terrainArchive, rec, terrainBaseUrl);
      const mesh = new THREE.Mesh(geo, mat);
      this.layers.ground.add(mesh);
    }
  }

  private buildDoors(detail: WorldBlockDetailResponse) {
    const { block } = detail;

    for (let i = 0; i < (block.doors?.length ?? 0); i++) {
      const door = block.doors![i];
      const cb = door.collisionBounds;
      const sx = cb ? Math.max(cb.maximum.x - cb.minimum.x, 0.35) : 1.25;
      const sy = cb ? Math.max(cb.maximum.y - cb.minimum.y, 1.8) : 2.25;
      const sz = cb ? Math.max(cb.maximum.z - cb.minimum.z, 0.35) : 0.4;
      const geo = new THREE.BoxGeometry(sx, sy, sz);
      const mat = new THREE.MeshBasicMaterial({
        color: 0xf5a623,
        transparent: true,
        opacity: 0.42,
        depthWrite: false,
      });
      const mesh = new THREE.Mesh(geo, mat);
      mesh.position.set(door.position.x, door.position.y, door.position.z);
      mesh.quaternion.copy(daggerfallSourceRotation(door.rotationDegrees));
      mesh.userData.ownedMaterial = true;
      this.registerPick(mesh, {
        pickKey: `door:${door.id}`,
        kind: 'door',
        id: door.id,
        label:
          door.buildingIndex !== null && door.buildingIndex !== undefined
            ? `Door ${door.id} (Building #${door.buildingIndex})`
            : `Door ${door.id}`,
        position: [door.position.x, door.position.y, door.position.z],
        buildingIndex: door.buildingIndex,
        index: i,
      });
      this.layers.doors.add(mesh);
    }

    for (let i = 0; i < (block.transitionDoors?.length ?? 0); i++) {
      const td = block.transitionDoors![i];
      const b = td.bounds;
      const sx = Math.max(b.maximum.x - b.minimum.x, 0.35);
      const sy = Math.max(b.maximum.y - b.minimum.y, 1.8);
      const sz = Math.max(b.maximum.z - b.minimum.z, 0.35);
      const geo = new THREE.BoxGeometry(sx, sy, sz);
      const mat = new THREE.MeshBasicMaterial({
        color: 0x38d9f5,
        transparent: true,
        opacity: 0.48,
        depthWrite: false,
      });
      const mesh = new THREE.Mesh(geo, mat);
      mesh.position.set(td.position.x, td.position.y, td.position.z);
      mesh.userData.ownedMaterial = true;
      this.registerPick(mesh, {
        pickKey: `transitionDoor:${td.id}`,
        kind: 'transitionDoor',
        id: td.id,
        label: `${td.kind} (${td.id})`,
        position: [td.position.x, td.position.y, td.position.z],
        index: i,
      });
      this.layers.doors.add(mesh);
    }

    for (let i = 0; i < (block.gates?.length ?? 0); i++) {
      const gate = block.gates![i];
      const b = gate.closed?.localBounds;
      const sx = b ? Math.max(b.maximum.x - b.minimum.x, 2) : 4;
      const sy = b ? Math.max(b.maximum.y - b.minimum.y, 3) : 5;
      const sz = b ? Math.max(b.maximum.z - b.minimum.z, 0.8) : 1;
      const geo = new THREE.BoxGeometry(sx, sy, sz);
      const mat = new THREE.MeshBasicMaterial({
        color: 0xb784ff,
        transparent: true,
        opacity: 0.38,
        depthWrite: false,
      });
      const mesh = new THREE.Mesh(geo, mat);
      mesh.position.set(gate.position.x, gate.position.y + sy * 0.5, gate.position.z);
      mesh.quaternion.copy(daggerfallSourceRotation(gate.rotationDegrees));
      mesh.userData.ownedMaterial = true;
      this.registerPick(mesh, {
        pickKey: `gate:${gate.id}`,
        kind: 'gate',
        id: gate.id,
        label: `City gate ${gate.id}`,
        position: [gate.position.x, gate.position.y, gate.position.z],
        index: i,
      });
      this.layers.doors.add(mesh);
    }
  }

  private buildPeople(detail: WorldBlockDetailResponse) {
    const { block, peopleSlots, mediaUrls } = detail;
    const seenPositions = new Set<string>();

    const addPersonPin = (
      pick: ScenePick,
      archive: number | null | undefined,
      record: number | null | undefined,
      ringColor: number,
    ) => {
      const [x, y, z] = pick.position;
      if (typeof archive === 'number' && typeof record === 'number') {
        const url = `${mediaUrls.billboards}texture-${archive}-${record}.png`;
        const sprite = new THREE.Sprite(this.getSpriteMaterial(url));
        sprite.scale.set(1.05, 1.85, 1);
        sprite.position.set(x, y + 0.95, z);
        this.registerPick(sprite, pick);
        this.layers.people.add(sprite);
      } else {
        const geo = new THREE.CylinderGeometry(0.32, 0.32, 1.75, 10);
        const mat = new THREE.MeshStandardMaterial({ color: ringColor, roughness: 0.5 });
        const mesh = new THREE.Mesh(geo, mat);
        mesh.position.set(x, y + 0.88, z);
        mesh.userData.ownedMaterial = true;
        this.registerPick(mesh, pick);
        this.layers.people.add(mesh);
      }

      // Ground halo ring so people stand out clearly from architecture
      const ringGeo = new THREE.RingGeometry(0.4, 0.58, 20);
      ringGeo.rotateX(-Math.PI / 2);
      const ringMat = new THREE.MeshBasicMaterial({ color: ringColor, side: THREE.DoubleSide });
      const ring = new THREE.Mesh(ringGeo, ringMat);
      ring.position.set(x, y + 0.04, z);
      ring.userData.ownedMaterial = true;
      this.layers.people.add(ring);
    };

    for (let i = 0; i < peopleSlots.length; i++) {
      const slot = peopleSlots[i];
      const [x, y, z] = slot.position;
      seenPositions.add(`${x.toFixed(2)},${y.toFixed(2)},${z.toFixed(2)}`);
      const title = slot.castName
        ? `${slot.castName} (cast)`
        : slot.spawner
          ? `${slot.spawner} (${slot.slotId})`
          : slot.slotId;
      addPersonPin(
        {
          pickKey: `peopleSlot:${slot.slotId}:${i}`,
          kind: 'peopleSlot',
          id: slot.slotId,
          label: title,
          position: [x, y, z],
          buildingIndex: slot.buildingIndex,
          index: i,
        },
        slot.visual?.archive,
        slot.visual?.record,
        slot.castId ? 0xffd166 : 0x3ddc97,
      );
    }

    for (let i = 0; i < (block.staticNpcs?.length ?? 0); i++) {
      const npc = block.staticNpcs![i];
      const pKey = `${npc.position.x.toFixed(2)},${npc.position.y.toFixed(2)},${npc.position.z.toFixed(2)}`;
      if (seenPositions.has(pKey)) continue;
      addPersonPin(
        {
          pickKey: `staticNpc:${npc.id}`,
          kind: 'staticNpc',
          id: npc.id,
          label: npc.npcName ? `${npc.npcName} (${npc.id})` : `Static NPC ${npc.id}`,
          position: [npc.position.x, npc.position.y, npc.position.z],
          index: i,
        },
        npc.billboardArchive,
        npc.billboardRecord,
        0x61b0ff,
      );
    }
  }

  private buildBillboards(detail: WorldBlockDetailResponse) {
    const { block, mediaUrls } = detail;
    for (let i = 0; i < (block.billboards?.length ?? 0); i++) {
      const bb = block.billboards![i];
      const match = SPRITE_RE.exec(bb.spriteResourceId ?? '');
      if (!match) continue;
      const url = `${mediaUrls.billboards}${match[1]}.png`;
      const sprite = new THREE.Sprite(this.getSpriteMaterial(url));
      const w = Math.max(bb.size?.x || 0.8, 0.25);
      const h = Math.max(bb.size?.y || 1.2, 0.25);
      sprite.scale.set(w, h, 1);
      const yOffset = bb.baseAligned ? h * 0.5 : 0;
      sprite.position.set(bb.position.x, bb.position.y + yOffset, bb.position.z);
      this.registerPick(sprite, {
        pickKey: `billboard:${bb.id}`,
        kind: 'billboard',
        id: bb.id,
        label: `Billboard ${match[1]}`,
        position: [bb.position.x, bb.position.y, bb.position.z],
        index: i,
      });
      this.layers.billboards.add(sprite);
    }
  }

  private buildMarkers(detail: WorldBlockDetailResponse) {
    const { block } = detail;

    const addOrb = (pick: ScenePick, color: number, radius: number, shape: 'sphere' | 'octa' | 'box' = 'sphere') => {
      const geo =
        shape === 'octa'
          ? new THREE.OctahedronGeometry(radius)
          : shape === 'box'
            ? new THREE.BoxGeometry(radius * 1.5, radius * 1.5, radius * 1.5)
            : new THREE.SphereGeometry(radius, 12, 10);
      const mat = new THREE.MeshBasicMaterial({ color });
      const mesh = new THREE.Mesh(geo, mat);
      mesh.position.set(...pick.position);
      mesh.userData.ownedMaterial = true;
      this.registerPick(mesh, pick);
      this.layers.markers.add(mesh);
    };

    for (let i = 0; i < (block.lights?.length ?? 0); i++) {
      const l = block.lights![i];
      addOrb(
        {
          pickKey: `light:${l.id}`,
          kind: 'light',
          id: l.id,
          label: `Light ${l.id} (range ${l.range}m)`,
          position: [l.position.x, l.position.y, l.position.z],
          index: i,
        },
        0xffe37d,
        0.26,
        'sphere',
      );
    }

    for (let i = 0; i < (block.questMarkers?.length ?? 0); i++) {
      const q = block.questMarkers![i];
      addOrb(
        {
          pickKey: `questMarker:${q.id}`,
          kind: 'questMarker',
          id: q.id,
          label: `Quest marker (${q.kind ?? 'quest'})`,
          position: [q.position.x, q.position.y + 0.5, q.position.z],
          index: i,
        },
        0xffb703,
        0.42,
        'octa',
      );
    }

    for (let i = 0; i < (block.randomEnemies?.length ?? 0); i++) {
      const e = block.randomEnemies![i];
      addOrb(
        {
          pickKey: `randomEnemy:${e.id}`,
          kind: 'randomEnemy',
          id: e.id,
          label: `Enemy spawn slot ${e.encounterSlot}`,
          position: [e.position.x, e.position.y + 0.7, e.position.z],
          index: i,
        },
        0xef476f,
        0.48,
        'octa',
      );
    }

    for (let i = 0; i < (block.propertyContainers?.length ?? 0); i++) {
      const c = block.propertyContainers![i];
      addOrb(
        {
          pickKey: `container:${c.id}`,
          kind: 'container',
          id: c.id,
          label: `Container ${c.id}`,
          position: [c.position.x, c.position.y, c.position.z],
          index: i,
        },
        0x2ec4b6,
        0.34,
        'box',
      );
    }

    const specialMarkers = [
      ...(block.startMarker ? [{ ...block.startMarker, tag: 'Start marker' }] : []),
      ...(block.enterMarker ? [{ ...block.enterMarker, tag: 'Enter marker' }] : []),
    ];
    for (let i = 0; i < specialMarkers.length; i++) {
      const m = specialMarkers[i];
      addOrb(
        {
          pickKey: `marker:${m.id}`,
          kind: 'marker',
          id: m.id,
          label: `${m.tag} (${m.id})`,
          position: [m.position.x, m.position.y + 0.8, m.position.z],
          index: i,
        },
        0x80ed99,
        0.45,
        'octa',
      );
    }
  }

  private buildWater(waterLevel: number, bounds: WorldBlockDetailResponse['block']['bounds']) {
    const minX = bounds ? bounds.minimum.x : 0;
    const maxX = bounds ? bounds.maximum.x : 51.2;
    const minZ = bounds ? bounds.minimum.z : -51.2;
    const maxZ = bounds ? bounds.maximum.z : 0;
    const w = Math.max(maxX - minX, 4);
    const d = Math.max(maxZ - minZ, 4);
    const geo = new THREE.PlaneGeometry(w, d);
    geo.rotateX(-Math.PI / 2);
    const mat = new THREE.MeshBasicMaterial({
      color: 0x1e88e5,
      transparent: true,
      opacity: 0.38,
      side: THREE.DoubleSide,
      depthWrite: false,
    });
    const plane = new THREE.Mesh(geo, mat);
    plane.position.set((minX + maxX) * 0.5, waterLevel, (minZ + maxZ) * 0.5);
    plane.userData.ownedMaterial = true;
    this.layers.water.add(plane);
  }

  dispose() {
    this.disposed = true;
    this.loadGeneration++;
    cancelAnimationFrame(this.rafId);
    this.resizeObserver.disconnect();
    this.renderer.domElement.removeEventListener('pointerdown', this.onPointerDown);
    this.renderer.domElement.removeEventListener('pointerup', this.onPointerUp);
    window.removeEventListener('keydown', this.onKeyDown);
    window.removeEventListener('keyup', this.onKeyUp);
    this.clearScene();
    for (const mat of this.materialCache.values()) {
      mat.map?.dispose();
      mat.dispose();
    }
    this.materialCache.clear();
    for (const mat of this.spriteMaterialCache.values()) {
      mat.map?.dispose();
      mat.dispose();
    }
    this.spriteMaterialCache.clear();
    this.allMeshMaterials.clear();
    this.meshCache.clear();
    this.controls.dispose();
    this.renderer.dispose();
  }
}
