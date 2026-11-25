import { useEffect, useMemo, useRef, useState } from 'preact/hooks';
import type { ArtMode, Bounds3, SeasonName, WorldAppearanceData, WorldBlockDetailResponse } from '../types';
import { fetchWorldAppearance, type RenderAppearanceConfig } from '../viewer/appearance';
import type {
  BlockSceneController,
  CompositeBlockGrid,
  LayerName,
  SceneLoadProgress,
  ScenePick,
  TransformCommitPayload,
  VariantPreviewOverlay,
} from '../viewer/BlockScene';

export interface ViewportEditControls {
  enabled: boolean;
  mode: 'translate' | 'rotate';
  snap: boolean;
  gizmoTargetKey: string | null;
  gizmoTargetLabel?: string | null;
  canRotate: boolean;
  canUndo: boolean;
  canRedo: boolean;
  onModeChange: (mode: 'translate' | 'rotate') => void;
  onSnapChange: (snap: boolean) => void;
  onUndo: () => void;
  onRedo: () => void;
  onTransformCommit: (pickKey: string, payload: TransformCommitPayload) => void;
}

const BASE_LAYERS: Array<{ key: LayerName; label: string }> = [
  { key: 'models', label: 'Models' },
  { key: 'ground', label: 'Ground' },
  { key: 'doors', label: 'Doors' },
  { key: 'people', label: 'People' },
  { key: 'billboards', label: 'Billboards' },
  { key: 'markers', label: 'Markers' },
  { key: 'water', label: 'Water' },
  { key: 'actions', label: 'Actions' },
  { key: 'collision', label: 'Collision' },
];

export interface FocusBoxRequest {
  id: number;
  min: [number, number, number];
  max: [number, number, number];
}

export function BlockViewport({
  detail,
  compositeGrid,
  selectedPickKey,
  focusRequestId,
  focusBoxRequest,
  defaultClimate,
  defaultDungeonRemaps,
  defaultDungeonLabel,
  variantOverlay,
  editControls,
  peopleSlotPositionOverrides,
  onSelect,
}: {
  detail?: WorldBlockDetailResponse | null;
  compositeGrid?: CompositeBlockGrid | null;
  selectedPickKey: string | null;
  focusRequestId: number;
  focusBoxRequest?: FocusBoxRequest | null;
  defaultClimate?: number | null;
  defaultDungeonRemaps?: Record<string, number> | null;
  defaultDungeonLabel?: string | null;
  variantOverlay?: VariantPreviewOverlay | null;
  editControls?: ViewportEditControls | null;
  peopleSlotPositionOverrides?: Record<string, [number, number, number]> | null;
  onSelect: (pick: ScenePick | null) => void;
}) {
  const hostRef = useRef<HTMLDivElement>(null);
  const controllerRef = useRef<BlockSceneController | null>(null);
  const onSelectRef = useRef(onSelect);
  onSelectRef.current = onSelect;
  const editControlsRef = useRef(editControls);
  editControlsRef.current = editControls;
  const lastSceneIdentityRef = useRef<string>('');

  const isComposite = Boolean(compositeGrid);
  const isDungeon = compositeGrid ? compositeGrid.mode === 'dungeon' : detail?.block.kind === 'rdb';
  const isExterior = compositeGrid
    ? compositeGrid.mode === 'exterior'
    : detail?.block.kind === 'rmbExterior';
  const layerDefs = isComposite ? [...BASE_LAYERS, { key: 'grid' as LayerName, label: 'Grid' }] : BASE_LAYERS;

  const [initError, setInitError] = useState<string | null>(null);
  const [engineReady, setEngineReady] = useState(false);
  const [appearanceData, setAppearanceData] = useState<WorldAppearanceData | null>(null);
  const [artMode, setArtMode] = useState<ArtMode>('classic');
  const [climateValue, setClimateValue] = useState<number>(defaultClimate ?? 231);
  const [season, setSeason] = useState<SeasonName>('summer');
  const [dungeonPresetId, setDungeonPresetId] = useState<string>(defaultDungeonRemaps ? '__location__' : '');

  const [progress, setProgress] = useState<SceneLoadProgress>({
    loadedMeshes: 0,
    totalMeshes: 0,
    failedMeshes: 0,
    triangles: 0,
    remodelsUsed: 0,
  });
  const [wireframe, setWireframe] = useState(false);
  const [sliceEnabled, setSliceEnabled] = useState(false);
  const [slicePct, setSlicePct] = useState(65);
  const [layers, setLayers] = useState<Record<LayerName, boolean>>({
    models: true,
    ground: true,
    doors: true,
    people: true,
    billboards: true,
    markers: true,
    water: true,
    grid: true,
    actions: true,
    collision: false,
  });

  // Load cached world appearance tables (climate swaps, dungeon presets, art replacements)
  useEffect(() => {
    let cancelled = false;
    fetchWorldAppearance()
      .then(data => {
        if (!cancelled) setAppearanceData(data);
      })
      .catch(() => {});
    return () => {
      cancelled = true;
    };
  }, []);

  // Sync climate / dungeon table defaults when switching to a new location
  useEffect(() => {
    if (defaultClimate !== undefined && defaultClimate !== null) {
      setClimateValue(defaultClimate);
    }
  }, [defaultClimate]);

  useEffect(() => {
    setDungeonPresetId(defaultDungeonRemaps ? '__location__' : '');
  }, [defaultDungeonRemaps]);

  const activeDungeonRemaps = useMemo<Record<string, number> | null>(() => {
    if (!isDungeon) return null;
    if (dungeonPresetId === '__location__') return defaultDungeonRemaps ?? null;
    if (!dungeonPresetId) return null;
    const preset = appearanceData?.dungeonPresets.find(p => p.id === dungeonPresetId);
    return preset?.remaps ?? null;
  }, [isDungeon, dungeonPresetId, defaultDungeonRemaps, appearanceData]);

  const appearanceConfig = useMemo<RenderAppearanceConfig>(
    () => ({
      data: appearanceData,
      artMode,
      climateValue: isDungeon ? null : climateValue,
      season,
      dungeonRemaps: activeDungeonRemaps,
      isExterior,
    }),
    [appearanceData, artMode, isDungeon, climateValue, season, activeDungeonRemaps, isExterior],
  );

  // Lazily import Three.js chunk only when BlockViewport mounts
  useEffect(() => {
    let cancelled = false;
    setInitError(null);
    import('../viewer/BlockScene')
      .then(mod => {
        if (cancelled || !hostRef.current) return;
        const ctrl = new mod.BlockSceneController(hostRef.current, {
          onSelect: pick => onSelectRef.current(pick),
          onProgress: p => {
            if (!cancelled) setProgress(p);
          },
          onTransformCommit: (pickKey, payload) => {
            editControlsRef.current?.onTransformCommit(pickKey, payload);
          },
        });
        controllerRef.current = ctrl;
        setEngineReady(true);
      })
      .catch(err => {
        if (!cancelled) setInitError(err instanceof Error ? err.message : String(err));
      });
    return () => {
      cancelled = true;
      controllerRef.current?.dispose();
      controllerRef.current = null;
      setEngineReady(false);
    };
  }, []);

  const sceneIdentity = compositeGrid
    ? `composite:${compositeGrid.mode}:${compositeGrid.placements.map(p => `${p.gridX},${p.gridY}:${p.blockKey}`).join('|')}`
    : detail
      ? `single:${detail.block.key}`
      : '';

  // Load single block or composite town/dungeon grid into scene, preserving camera when only appearance changes
  useEffect(() => {
    if (!engineReady || !controllerRef.current || !sceneIdentity) return;
    const preserveCamera = lastSceneIdentityRef.current === sceneIdentity;
    lastSceneIdentityRef.current = sceneIdentity;
    if (compositeGrid) {
      void controllerRef.current.loadCompositeGrid(compositeGrid, appearanceConfig, preserveCamera);
    } else if (detail) {
      void controllerRef.current.loadBlock(detail, appearanceConfig, preserveCamera);
    }
  }, [engineReady, detail, compositeGrid, sceneIdentity, appearanceConfig]);

  // Apply layer toggles
  useEffect(() => {
    const ctrl = controllerRef.current;
    if (!ctrl) return;
    for (const [k, visible] of Object.entries(layers) as Array<[LayerName, boolean]>) {
      ctrl.setLayerVisible(k, visible);
    }
  }, [engineReady, layers]);

  // Apply wireframe mode
  useEffect(() => {
    controllerRef.current?.setWireframe(wireframe);
  }, [engineReady, wireframe]);

  // Compute aggregate Y bounds for horizontal cutaway slider
  const activeBounds: Bounds3 | null = detail?.block.bounds
    ? detail.block.bounds
    : compositeGrid?.placements.length
      ? (() => {
          let minY = Infinity;
          let maxY = -Infinity;
          for (const p of compositeGrid.placements) {
            const b = p.detail.block.bounds;
            if (b) {
              minY = Math.min(minY, p.origin[1] + b.minimum.y);
              maxY = Math.max(maxY, p.origin[1] + b.maximum.y);
            }
          }
          if (!Number.isFinite(minY) || !Number.isFinite(maxY)) return null;
          return {
            minimum: { x: 0, y: minY, z: 0 },
            maximum: { x: 0, y: maxY, z: 0 },
          };
        })()
      : null;

  useEffect(() => {
    const ctrl = controllerRef.current;
    if (!ctrl) return;
    if (!sliceEnabled || !activeBounds) {
      ctrl.setClipHeight(null);
      return;
    }
    const minY = activeBounds.minimum.y;
    const maxY = activeBounds.maximum.y;
    ctrl.setClipHeight(minY + ((maxY - minY) * slicePct) / 100);
  }, [engineReady, activeBounds?.minimum.y, activeBounds?.maximum.y, sliceEnabled, slicePct]);

  // Sync external selection / focus request
  useEffect(() => {
    if (!engineReady || !controllerRef.current) return;
    controllerRef.current.selectByKey(selectedPickKey, focusRequestId > 0);
  }, [engineReady, selectedPickKey, focusRequestId]);

  // Sync cell/box focus request (e.g. clicking a block on the Town 2D grid map)
  useEffect(() => {
    if (!engineReady || !controllerRef.current || !focusBoxRequest) return;
    controllerRef.current.focusBox(focusBoxRequest.min, focusBoxRequest.max);
  }, [engineReady, focusBoxRequest?.id]);

  // Sync live variant overlay (hidden misc models + added models)
  useEffect(() => {
    if (!engineReady || !controllerRef.current) return;
    void controllerRef.current.applyVariantOverlay(variantOverlay ?? null);
  }, [engineReady, variantOverlay]);

  // Sync TransformControls gizmo attachment, mode, and snapping
  useEffect(() => {
    const ctrl = controllerRef.current;
    if (!engineReady || !ctrl) return;
    ctrl.setTransformMode(editControls?.mode ?? 'translate');
    ctrl.setTransformSnap(Boolean(editControls?.snap));
    ctrl.attachGizmo(editControls?.enabled ? editControls.gizmoTargetKey : null);
  }, [
    engineReady,
    editControls?.enabled,
    editControls?.mode,
    editControls?.snap,
    editControls?.gizmoTargetKey,
  ]);

  // Sync live people-slot pin position overrides (from numeric input or undo/redo)
  useEffect(() => {
    const ctrl = controllerRef.current;
    if (!engineReady || !ctrl || !peopleSlotPositionOverrides) return;
    for (const [pickKey, pos] of Object.entries(peopleSlotPositionOverrides)) {
      ctrl.updatePeopleSlotPosition(pickKey, pos);
    }
  }, [engineReady, peopleSlotPositionOverrides]);

  // Keyboard shortcuts while 3D edit mode is active
  useEffect(() => {
    if (!editControls?.enabled) return;
    const onKeyDown = (event: KeyboardEvent) => {
      const tag = (event.target as HTMLElement | null)?.tagName;
      if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT') return;
      const ec = editControlsRef.current;
      if (!ec?.enabled) return;
      const key = event.key.toLowerCase();
      if ((event.ctrlKey || event.metaKey) && key === 'z') {
        event.preventDefault();
        if (event.shiftKey) {
          if (ec.canRedo) ec.onRedo();
        } else if (ec.canUndo) {
          ec.onUndo();
        }
        return;
      }
      if ((event.ctrlKey || event.metaKey) && key === 'y') {
        event.preventDefault();
        if (ec.canRedo) ec.onRedo();
        return;
      }
      if (!event.ctrlKey && !event.metaKey && !event.altKey) {
        if (key === 'g') {
          ec.onModeChange('translate');
        } else if (key === 'r' && ec.canRotate) {
          ec.onModeChange('rotate');
        }
      }
    };
    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  }, [editControls?.enabled]);

  function toggleLayer(name: LayerName) {
    setLayers(prev => ({ ...prev, [name]: !prev[name] }));
  }

  const loadingMeshes =
    progress.totalMeshes > 0 && progress.loadedMeshes + progress.failedMeshes < progress.totalMeshes;
  const refreshedAvailable = Boolean(appearanceData?.artReplacements.available);

  return (
    <div class="block-viewport-shell">
      <div class="block-viewport-toolbar">
        <div class="layer-toggles" role="group" aria-label="3D viewport layers">
          {layerDefs.map(item => (
            <button
              key={item.key}
              type="button"
              class={`layer-pill${layers[item.key] ? ' active' : ''}`}
              aria-pressed={layers[item.key] ? 'true' : 'false'}
              onClick={() => toggleLayer(item.key)}
            >
              {item.label}
            </button>
          ))}
        </div>
        <div class="actions">
          {activeBounds ? (
            <label class="slice-control" title="Horizontal cutaway plane to peel off roofs and upper floors">
              <input
                type="checkbox"
                checked={sliceEnabled}
                onChange={event => setSliceEnabled((event.currentTarget as HTMLInputElement).checked)}
              />{' '}
              Cutaway
              {sliceEnabled ? (
                <input
                  type="range"
                  min={5}
                  max={100}
                  value={slicePct}
                  onInput={event => setSlicePct(Number((event.currentTarget as HTMLInputElement).value))}
                />
              ) : null}
            </label>
          ) : null}
          <button
            type="button"
            class={wireframe ? 'primary' : undefined}
            onClick={() => setWireframe(prev => !prev)}
          >
            {wireframe ? 'Textured' : 'Wireframe'}
          </button>
          <button
            type="button"
            disabled={!selectedPickKey}
            onClick={() => controllerRef.current?.selectByKey(selectedPickKey, true)}
          >
            Focus selected
          </button>
          <button type="button" onClick={() => controllerRef.current?.resetCamera()}>
            Reset camera
          </button>
        </div>
      </div>

      {appearanceData ? (
        <div class="appearance-bar" role="region" aria-label="Appearance and climate controls">
          <div class="art-mode-toggle" role="group" aria-label="Art style">
            <button
              type="button"
              class={artMode === 'classic' ? 'active' : undefined}
              aria-pressed={artMode === 'classic' ? 'true' : 'false'}
              onClick={() => setArtMode('classic')}
            >
              Classic art
            </button>
            <button
              type="button"
              class={artMode === 'refreshed' ? 'active' : undefined}
              aria-pressed={artMode === 'refreshed' ? 'true' : 'false'}
              disabled={!refreshedAvailable}
              onClick={() => setArtMode('refreshed')}
              title={
                refreshedAvailable
                  ? `Overlay ${appearanceData.artReplacements.counts.surfaces} refreshed surfaces, ${appearanceData.artReplacements.counts.scenery} scenery sprites, and ${appearanceData.artReplacements.counts.models} ARCH3D remodels`
                  : 'daggerfall.art-replacements pack not found'
              }
            >
              Refreshed art
            </button>
          </div>

          {!isDungeon ? (
            <>
              <label class="appearance-select">
                Climate
                <select
                  aria-label="Climate"
                  value={String(climateValue)}
                  onChange={event => setClimateValue(Number((event.currentTarget as HTMLSelectElement).value))}
                >
                  {appearanceData.climates.map(c => (
                    <option key={c.value} value={String(c.value)}>
                      {c.name} ({c.climateBase})
                    </option>
                  ))}
                </select>
              </label>
              <div class="art-mode-toggle" role="group" aria-label="Season">
                {(['summer', 'winter', 'rain'] as SeasonName[]).map(s => (
                  <button
                    key={s}
                    type="button"
                    class={season === s ? 'active' : undefined}
                    aria-pressed={season === s ? 'true' : 'false'}
                    onClick={() => setSeason(s)}
                  >
                    {s[0].toUpperCase() + s.slice(1)}
                  </button>
                ))}
              </div>
            </>
          ) : (
            <label class="appearance-select">
              Dungeon texture table
              <select
                aria-label="Dungeon texture table"
                value={dungeonPresetId}
                onChange={event => setDungeonPresetId((event.currentTarget as HTMLSelectElement).value)}
              >
                {defaultDungeonRemaps ? (
                  <option value="__location__">{defaultDungeonLabel ?? 'Location texture table'}</option>
                ) : null}
                <option value="">Raw base archives (no remap)</option>
                {appearanceData.dungeonPresets.map(p => (
                  <option key={p.id} value={p.id}>
                    {p.name}
                  </option>
                ))}
              </select>
            </label>
          )}
        </div>
      ) : null}

      {editControls?.enabled ? (
        <div class="edit-bar" role="region" aria-label="3D transform editing controls">
          <div class="art-mode-toggle" role="group" aria-label="Transform mode">
            <button
              type="button"
              class={editControls.mode === 'translate' ? 'active' : undefined}
              aria-pressed={editControls.mode === 'translate' ? 'true' : 'false'}
              onClick={() => editControls.onModeChange('translate')}
              title="Move along X/Y/Z axes (shortcut: G)"
            >
              Translate (G)
            </button>
            <button
              type="button"
              class={editControls.mode === 'rotate' ? 'active' : undefined}
              aria-pressed={editControls.mode === 'rotate' ? 'true' : 'false'}
              disabled={!editControls.canRotate}
              onClick={() => editControls.onModeChange('rotate')}
              title={
                editControls.canRotate
                  ? 'Rotate yaw around +Y (shortcut: R)'
                  : 'Yaw rotation is available on added variant models'
              }
            >
              Rotate Yaw (R)
            </button>
          </div>
          <label class="slice-control">
            <input
              type="checkbox"
              checked={editControls.snap}
              onChange={event => editControls.onSnapChange((event.currentTarget as HTMLInputElement).checked)}
            />{' '}
            Snap (0.25m / 15°)
          </label>
          <div class="actions">
            <button
              type="button"
              disabled={!editControls.canUndo}
              onClick={() => editControls.onUndo()}
              title="Undo (Ctrl+Z)"
            >
              Undo
            </button>
            <button
              type="button"
              disabled={!editControls.canRedo}
              onClick={() => editControls.onRedo()}
              title="Redo (Ctrl+Shift+Z)"
            >
              Redo
            </button>
          </div>
          <span class="muted" style={{ fontSize: '0.8rem', marginLeft: 'auto' }}>
            {editControls.gizmoTargetKey
              ? `Gizmo: ${editControls.gizmoTargetLabel ?? editControls.gizmoTargetKey}`
              : 'Select a variant model or people slot to drag in 3D'}
          </span>
        </div>
      ) : null}

      <div class="block-canvas-wrap">
        <div ref={hostRef} class="block-canvas-host" />
        {!engineReady && !initError ? <div class="block-canvas-overlay">Initializing 3D viewport…</div> : null}
        {initError ? (
          <div class="block-canvas-overlay error" role="alert">
            WebGL viewport unavailable: {initError}
          </div>
        ) : null}
        <div class="block-canvas-hud">
          <span>
            {loadingMeshes
              ? `Loading meshes ${progress.loadedMeshes}/${progress.totalMeshes}…`
              : `${progress.loadedMeshes} unique meshes${
                  progress.remodelsUsed > 0 ? ` (${progress.remodelsUsed} remodels)` : ''
                } · ${Math.round(progress.triangles).toLocaleString()} tris`}
            {progress.failedMeshes > 0 ? ` (${progress.failedMeshes} failed)` : ''}
          </span>
          <span>Orbit: drag · Pan: right-drag · Zoom: wheel · Fly: WASD/QE (+Shift) · Click to inspect</span>
        </div>
      </div>
    </div>
  );
}
