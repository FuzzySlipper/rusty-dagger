import { useEffect, useRef, useState } from 'preact/hooks';
import type { WorldBlockDetailResponse } from '../types';
import type { BlockSceneController, LayerName, SceneLoadProgress, ScenePick } from '../viewer/BlockScene';

const LAYER_LABELS: Array<{ key: LayerName; label: string }> = [
  { key: 'models', label: 'Models' },
  { key: 'ground', label: 'Ground' },
  { key: 'doors', label: 'Doors' },
  { key: 'people', label: 'People' },
  { key: 'billboards', label: 'Billboards' },
  { key: 'markers', label: 'Markers' },
  { key: 'water', label: 'Water' },
];

export function BlockViewport({
  detail,
  selectedPickKey,
  focusRequestId,
  onSelect,
}: {
  detail: WorldBlockDetailResponse;
  selectedPickKey: string | null;
  focusRequestId: number;
  onSelect: (pick: ScenePick | null) => void;
}) {
  const hostRef = useRef<HTMLDivElement>(null);
  const controllerRef = useRef<BlockSceneController | null>(null);
  const onSelectRef = useRef(onSelect);
  onSelectRef.current = onSelect;

  const [initError, setInitError] = useState<string | null>(null);
  const [engineReady, setEngineReady] = useState(false);
  const [progress, setProgress] = useState<SceneLoadProgress>({
    loadedMeshes: 0,
    totalMeshes: 0,
    failedMeshes: 0,
    triangles: 0,
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
  });

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

  // Load block into scene whenever detail changes or controller finishes initializing
  useEffect(() => {
    if (!engineReady || !controllerRef.current) return;
    void controllerRef.current.loadBlock(detail);
  }, [engineReady, detail]);

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

  // Apply optional horizontal cutaway slice
  useEffect(() => {
    const ctrl = controllerRef.current;
    if (!ctrl) return;
    const b = detail.block.bounds;
    if (!sliceEnabled || !b) {
      ctrl.setClipHeight(null);
      return;
    }
    const minY = b.minimum.y;
    const maxY = b.maximum.y;
    const y = minY + ((maxY - minY) * slicePct) / 100;
    ctrl.setClipHeight(y);
  }, [engineReady, detail, sliceEnabled, slicePct]);

  // Sync external selection / focus request
  useEffect(() => {
    if (!engineReady || !controllerRef.current) return;
    controllerRef.current.selectByKey(selectedPickKey, focusRequestId > 0);
  }, [engineReady, selectedPickKey, focusRequestId]);

  function toggleLayer(name: LayerName) {
    setLayers(prev => ({ ...prev, [name]: !prev[name] }));
  }

  const loadingMeshes = progress.totalMeshes > 0 && progress.loadedMeshes + progress.failedMeshes < progress.totalMeshes;

  return (
    <div class="block-viewport-shell">
      <div class="block-viewport-toolbar">
        <div class="layer-toggles" role="group" aria-label="3D viewport layers">
          {LAYER_LABELS.map(item => (
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
          {detail.block.bounds ? (
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
              : `${progress.loadedMeshes} unique meshes · ${Math.round(progress.triangles).toLocaleString()} tris`}
            {progress.failedMeshes > 0 ? ` (${progress.failedMeshes} failed)` : ''}
          </span>
          <span>Orbit: drag · Pan: right-drag · Zoom: wheel · Fly: WASD/QE · Click to inspect</span>
        </div>
      </div>
    </div>
  );
}
