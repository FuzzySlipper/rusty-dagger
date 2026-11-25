import { useEffect, useMemo, useState } from 'preact/hooks';
import { apiRequest, isAbortError } from '../api';
import {
  assetUrl,
  castUrl,
  locationsUrl,
  locationUrl,
  spawnerUrl,
  subjectUrl,
  worldBlockUrl,
} from '../router';
import { useSessionState } from '../hooks/useSessionState';
import type {
  LocationDetailResponse,
  LocationListItem,
  LocationsQueryResponse,
  WorldBlockBatchResponse,
} from '../types';
import type { CompositeBlockGrid, CompositeBlockPlacement, ScenePick } from '../viewer/BlockScene';
import { BlockViewport, type FocusBoxRequest } from '../components/BlockViewport';
import { ArtDataChip } from '../components/ArtDataLink';
import { DataTable, type DataColumn } from '../components/DataTable';
import { Chip, Empty, FactList, PageHeading } from '../components/ui';

const RMB_SIDE = 102.4;
const RDB_SIDE = 51.2;

interface LocationFilters {
  q: string;
  region: string;
  locationType: string;
  hasDungeon: boolean;
  multiBlock: boolean;
  sort: string;
  page: number;
}

const DEFAULT_FILTERS: LocationFilters = {
  q: '',
  region: '',
  locationType: '',
  hasDungeon: false,
  multiBlock: false,
  sort: 'size',
  page: 1,
};

export function LocationsListPage({ initialQuery }: { initialQuery: Record<string, string> }) {
  const [filters, setFilters] = useSessionState<LocationFilters>('cms.locations.filters', DEFAULT_FILTERS);
  const [result, setResult] = useState<LocationsQueryResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const pageSize = 50;

  useEffect(() => {
    if (
      initialQuery.q !== undefined ||
      initialQuery.region !== undefined ||
      initialQuery.locationType !== undefined ||
      initialQuery.hasDungeon !== undefined ||
      initialQuery.multiBlock !== undefined
    ) {
      setFilters(prev => ({
        ...prev,
        q: initialQuery.q ?? prev.q,
        region: initialQuery.region ?? prev.region,
        locationType: initialQuery.locationType ?? prev.locationType,
        hasDungeon: initialQuery.hasDungeon ? initialQuery.hasDungeon === '1' : prev.hasDungeon,
        multiBlock: initialQuery.multiBlock ? initialQuery.multiBlock === '1' : prev.multiBlock,
        page: 1,
      }));
    }
  }, [JSON.stringify(initialQuery)]);

  const { q, region, locationType, hasDungeon, multiBlock, sort, page } = filters;
  const patch = (change: Partial<LocationFilters>) =>
    setFilters(prev => ({ ...prev, ...change, page: change.page ?? 1 }));
  const hasActiveFilters = Boolean(q || region || locationType || hasDungeon || multiBlock || sort !== 'size');

  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    setError(null);
    const params = new URLSearchParams();
    if (q.trim()) params.set('q', q.trim());
    if (region) params.set('region', region);
    if (locationType) params.set('locationType', locationType);
    if (hasDungeon) params.set('hasDungeon', '1');
    if (multiBlock) params.set('multiBlock', '1');
    if (sort) params.set('sort', sort);
    params.set('offset', String((page - 1) * pageSize));
    params.set('limit', String(pageSize));

    apiRequest<LocationsQueryResponse>(`/api/locations?${params.toString()}`, { signal: controller.signal })
      .then(data => setResult(data))
      .catch(err => {
        if (!isAbortError(err)) setError(err instanceof Error ? err.message : String(err));
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false);
      });
    return () => controller.abort();
  }, [q, region, locationType, hasDungeon, multiBlock, sort, page]);

  const columns: Array<DataColumn<LocationListItem>> = [
    {
      key: 'name',
      header: 'Location',
      render: row => (
        <div>
          <a href={locationUrl(row.region, row.index)}>
            <strong>{row.name}</strong>
          </a>
          <div class="muted" style={{ fontSize: '0.82rem' }}>
            {row.regionName} · #{row.index}
          </div>
        </div>
      ),
    },
    {
      key: 'type',
      header: 'Type',
      render: row => <Chip state="empty" label={row.locationTypeName} />,
    },
    {
      key: 'grid',
      header: 'Town / Exterior grid',
      render: row =>
        row.exteriorBlockCount > 0 ? (
          <span>
            <strong>
              {row.width}×{row.height}
            </strong>{' '}
            <small class="muted">
              ({row.exteriorBlockCount} {row.exteriorBlockCount === 1 ? 'block' : 'blocks'})
            </small>
          </span>
        ) : (
          <span class="muted">—</span>
        ),
    },
    {
      key: 'dungeon',
      header: 'Dungeon',
      render: row =>
        row.hasDungeon ? (
          <span>
            <Chip state="needs-check" label={`${row.dungeonBlockCount} RDB`} />
            {row.dungeonTypeName ? <small class="muted"> {row.dungeonTypeName}</small> : null}
          </span>
        ) : (
          <span class="muted">—</span>
        ),
    },
    {
      key: 'actions',
      header: '',
      render: row => (
        <div class="actions">
          <a class="button" href={locationUrl(row.region, row.index)}>
            Town 3D
          </a>
          {row.hasDungeon ? (
            <a class="button" href={locationUrl(row.region, row.index, 'dungeon')}>
              Dungeon 3D
            </a>
          ) : null}
        </div>
      ),
    },
  ];

  return (
    <>
      <PageHeading
        title="Towns & locations (3D)"
        intro="Inspect any of Daggerfall's 15,251 authored town grids and 4,232 multi-block dungeons composed in 3D, then jump straight into individual blocks or building interiors."
      />

      {result && !result.available ? (
        <Empty>{result.reason ?? 'Staged location data is not available.'}</Empty>
      ) : (
        <>
          {result ? (
            <div class="stat-strip">
              <button
                type="button"
                class={`stat-pill${!locationType && !multiBlock && !hasDungeon ? ' active' : ''}`}
                onClick={() => patch({ locationType: '', multiBlock: false, hasDungeon: false })}
              >
                <strong>{result.counts.total.toLocaleString()}</strong>
                <span>All locations</span>
              </button>
              <button
                type="button"
                class={`stat-pill${locationType === '0' ? ' active' : ''}`}
                onClick={() =>
                  patch({
                    locationType: locationType === '0' ? '' : '0',
                    multiBlock: false,
                    hasDungeon: false,
                  })
                }
              >
                <strong>{result.counts.cities.toLocaleString()}</strong>
                <span>Cities</span>
              </button>
              <button
                type="button"
                class={`stat-pill${multiBlock && !locationType ? ' active' : ''}`}
                onClick={() => patch({ multiBlock: !multiBlock, locationType: '' })}
              >
                <strong>{result.counts.multiBlock.toLocaleString()}</strong>
                <span>Multi-block towns</span>
              </button>
              <button
                type="button"
                class={`stat-pill${hasDungeon ? ' active' : ''}`}
                onClick={() => patch({ hasDungeon: !hasDungeon })}
              >
                <strong>{result.counts.withDungeons.toLocaleString()}</strong>
                <span>With dungeons</span>
              </button>
            </div>
          ) : null}

          <div class="filters">
            <label>
              Search location
              <input
                type="search"
                value={q}
                placeholder="Daggerfall, Wayrest, Sentinel, Privateer's Hold"
                onInput={event => patch({ q: (event.currentTarget as HTMLInputElement).value })}
              />
            </label>
            <label>
              Region
              <select
                value={region}
                onChange={event => patch({ region: (event.currentTarget as HTMLSelectElement).value })}
              >
                <option value="">All regions</option>
                {(result?.regions ?? []).map(r => (
                  <option key={r.index} value={String(r.index)}>
                    {r.name} ({r.count})
                  </option>
                ))}
              </select>
            </label>
            <label>
              Type
              <select
                value={locationType}
                onChange={event => patch({ locationType: (event.currentTarget as HTMLSelectElement).value })}
              >
                <option value="">All types</option>
                {(result?.locationTypes ?? []).map(t => (
                  <option key={t.id} value={String(t.id)}>
                    {t.name} ({t.count})
                  </option>
                ))}
              </select>
            </label>
            <label>
              Sort
              <select
                value={sort}
                onChange={event => patch({ sort: (event.currentTarget as HTMLSelectElement).value })}
              >
                <option value="size">Largest towns first</option>
                <option value="dungeon">Largest dungeons first</option>
                <option value="name">Name (A–Z)</option>
                <option value="region">Region</option>
              </select>
            </label>
            <label class="mark-filter">
              <input
                type="checkbox"
                checked={multiBlock}
                onChange={event => patch({ multiBlock: (event.currentTarget as HTMLInputElement).checked })}
              />{' '}
              Multi-block
            </label>
            <label class="mark-filter">
              <input
                type="checkbox"
                checked={hasDungeon}
                onChange={event => patch({ hasDungeon: (event.currentTarget as HTMLInputElement).checked })}
              />{' '}
              Has dungeon
            </label>
            {hasActiveFilters ? (
              <button type="button" onClick={() => setFilters(DEFAULT_FILTERS)}>
                Reset filters
              </button>
            ) : null}
            <output>{result ? `${result.total.toLocaleString()} matching` : ''}</output>
          </div>

          {error ? (
            <Empty>{error}</Empty>
          ) : result ? (
            <section class="panel">
              <DataTable
                rows={result.items}
                columns={columns}
                rowKey={row => `${row.region}:${row.index}`}
                emptyMessage="No locations match the active filters."
                pagination={{
                  page,
                  pageSize,
                  total: result.total,
                  onPageChange: next => setFilters(prev => ({ ...prev, page: next })),
                }}
              />
            </section>
          ) : loading ? (
            <p>Loading locations…</p>
          ) : null}
        </>
      )}
    </>
  );
}

type TownTab = 'grid' | 'summary' | 'blocks';

export function LocationDetailPage({
  region,
  index,
  mode = 'exterior',
}: {
  region: number;
  index: number;
  mode?: 'exterior' | 'dungeon';
}) {
  const [detail, setDetail] = useState<LocationDetailResponse | null>(null);
  const [batch, setBatch] = useState<WorldBlockBatchResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [loadingBlocks, setLoadingBlocks] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const [tab, setTab] = useState<TownTab>('grid');
  const [selectedPick, setSelectedPick] = useState<ScenePick | null>(null);
  const [selectedCell, setSelectedCell] = useState<{ x: number; y: number } | null>(null);
  const [focusBoxRequest, setFocusBoxRequest] = useState<FocusBoxRequest | null>(null);
  const [blockFilter, setBlockFilter] = useState('');

  const activeMode: 'exterior' | 'dungeon' = mode === 'dungeon' && detail?.dungeon ? 'dungeon' : 'exterior';

  // Fetch location metadata
  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    setError(null);
    setDetail(null);
    setBatch(null);
    setSelectedPick(null);
    setSelectedCell(null);
    setFocusBoxRequest(null);
    apiRequest<LocationDetailResponse>(`/api/locations/detail?region=${region}&index=${index}`, {
      signal: controller.signal,
    })
      .then(data => setDetail(data))
      .catch(err => {
        if (!isAbortError(err)) setError(err instanceof Error ? err.message : String(err));
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false);
      });
    return () => controller.abort();
  }, [region, index]);

  // Batch-load all unique block documents needed for the active view (exterior or dungeon)
  useEffect(() => {
    if (!detail || detail.location.region !== region || detail.location.index !== index) return;
    const keys =
      activeMode === 'dungeon' && detail.dungeon
        ? [...new Set(detail.dungeon.placements.map(p => p.blockKey).filter((k): k is string => Boolean(k)))]
        : detail.exterior.uniqueBlockKeys;

    if (!keys.length) {
      setBatch(null);
      return;
    }

    const controller = new AbortController();
    setLoadingBlocks(true);
    setSelectedPick(null);
    setSelectedCell(null);
    setFocusBoxRequest(null);
    apiRequest<WorldBlockBatchResponse>('/api/world-blocks/batch', {
      method: 'POST',
      body: { keys },
      signal: controller.signal,
    })
      .then(data => setBatch(data))
      .catch(err => {
        if (!isAbortError(err)) setError(err instanceof Error ? err.message : String(err));
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoadingBlocks(false);
      });
    return () => controller.abort();
  }, [detail, activeMode]);

  const compositeGrid: CompositeBlockGrid | null = useMemo(() => {
    if (!detail || !batch || !batch.available) return null;
    const placements: CompositeBlockPlacement[] = [];
    if (activeMode === 'dungeon' && detail.dungeon) {
      for (const p of detail.dungeon.placements) {
        if (!p.blockKey) continue;
        const bDetail = batch.blocks[p.blockKey];
        if (!bDetail) continue;
        placements.push({
          blockKey: p.blockKey,
          sourceName: p.sourceKey,
          gridX: p.x,
          gridY: p.z,
          origin: [p.x * RDB_SIDE, 0, -(p.z * RDB_SIDE)],
          side: RDB_SIDE,
          start: p.start,
          detail: bDetail,
        });
      }
    } else {
      for (const b of detail.exterior.blocks) {
        if (!b.blockKey) continue;
        const bDetail = batch.blocks[b.blockKey];
        if (!bDetail) continue;
        placements.push({
          blockKey: b.blockKey,
          sourceName: b.sourceName,
          gridX: b.x,
          gridY: b.y,
          origin: [b.x * RMB_SIDE, 0, -(b.y * RMB_SIDE)],
          side: RMB_SIDE,
          detail: bDetail,
        });
      }
    }
    return {
      mode: activeMode,
      terrainArchive: batch.terrainArchive,
      mediaUrls: batch.mediaUrls,
      placements,
    };
  }, [detail, batch, activeMode]);

  // Compute grid bounds for the 2D interactive block map
  const gridMeta = useMemo(() => {
    if (!detail) return null;
    if (activeMode === 'dungeon' && detail.dungeon) {
      const ps = detail.dungeon.placements;
      if (!ps.length) return null;
      const minX = Math.min(...ps.map(p => p.x));
      const maxX = Math.max(...ps.map(p => p.x));
      const minY = Math.min(...ps.map(p => p.z));
      const maxY = Math.max(...ps.map(p => p.z));
      const cellMap = new Map<string, { x: number; y: number; name: string; blockKey: string | null; start?: boolean; interiors?: number }>();
      for (const p of ps) {
        cellMap.set(`${p.x},${p.z}`, {
          x: p.x,
          y: p.z,
          name: p.sourceKey,
          blockKey: p.blockKey,
          start: p.start,
        });
      }
      return { minX, maxX, minY, maxY, cellMap, side: RDB_SIDE };
    }
    const bs = detail.exterior.blocks;
    if (!bs.length) return null;
    const minX = Math.min(...bs.map(b => b.x));
    const maxX = Math.max(...bs.map(b => b.x));
    const minY = Math.min(...bs.map(b => b.y));
    const maxY = Math.max(...bs.map(b => b.y));
    const cellMap = new Map<string, { x: number; y: number; name: string; blockKey: string | null; start?: boolean; interiors?: number }>();
    for (const b of bs) {
      cellMap.set(`${b.x},${b.y}`, {
        x: b.x,
        y: b.y,
        name: b.sourceName,
        blockKey: b.blockKey,
        interiors: b.interiorCount,
      });
    }
    return { minX, maxX, minY, maxY, cellMap, side: RMB_SIDE };
  }, [detail, activeMode]);

  const dungeonRemapTable = useMemo<Record<string, number> | null>(() => {
    const remaps = detail?.dungeon?.textureTable?.remaps;
    if (!remaps?.length) return null;
    const out: Record<string, number> = {};
    for (const r of remaps) {
      out[String(r.archive)] = r.targetArchive;
    }
    return out;
  }, [detail?.dungeon]);

  if (loading && !detail) {
    return (
      <>
        <PageHeading title="Loading location…" />
        <p>Fetching location layout…</p>
      </>
    );
  }

  if (error || !detail) {
    return (
      <>
        <PageHeading
          title="Location not found"
          action={
            <a class="button" href={locationsUrl()}>
              Back to Towns &amp; locations
            </a>
          }
        />
        <Empty>{error ?? `Could not open location ${region}:${index}.`}</Empty>
      </>
    );
  }

  const { location, exterior, dungeon } = detail;

  function focusGridCell(x: number, y: number) {
    setSelectedCell({ x, y });
    const side = activeMode === 'dungeon' ? RDB_SIDE : RMB_SIDE;
    const ox = x * side;
    const oz = -(y * side);
    // Use actual block Y bounds when available
    const placement = compositeGrid?.placements.find(p => p.gridX === x && p.gridY === y);
    const b = placement?.detail.block.bounds;
    const minY = b ? b.minimum.y : 0;
    const maxY = b ? b.maximum.y : 16;
    setFocusBoxRequest({
      id: Date.now() + Math.random(),
      min: [ox, minY, oz - side],
      max: [ox + side, maxY, oz],
    });
  }

  // Resolve clicked pick back into its block detail so we can offer interior & CMS jumps!
  const pickedBlockDetail = selectedPick?.blockKey && batch ? batch.blocks[selectedPick.blockKey] ?? null : null;
  const pickedModel =
    pickedBlockDetail && selectedPick?.kind === 'model'
      ? pickedBlockDetail.block.models.find(m => m.id === selectedPick.id) ?? null
      : null;
  const pickedModelAsset =
    pickedBlockDetail && pickedModel ? pickedBlockDetail.modelLinks[pickedModel.modelId] ?? null : null;
  const pickedBuilding =
    pickedBlockDetail && selectedPick?.buildingIndex !== null && selectedPick?.buildingIndex !== undefined
      ? pickedBlockDetail.block.buildings?.find(b => b.index === selectedPick.buildingIndex) ?? null
      : null;
  const pickedPeopleSlot =
    pickedBlockDetail && selectedPick?.kind === 'peopleSlot' && selectedPick.index !== undefined
      ? pickedBlockDetail.peopleSlots[selectedPick.index] ?? null
      : null;

  const activeCellInfo = selectedCell && gridMeta ? gridMeta.cellMap.get(`${selectedCell.x},${selectedCell.y}`) ?? null : null;
  const activeCellDetail = activeCellInfo?.blockKey && batch ? batch.blocks[activeCellInfo.blockKey] ?? null : null;

  const blockListRows = (compositeGrid?.placements ?? []).filter(p => {
    const term = blockFilter.trim().toLowerCase();
    if (!term) return true;
    return p.sourceName.toLowerCase().includes(term) || `${p.gridX},${p.gridY}`.includes(term);
  });

  return (
    <>
      <PageHeading
        title={`${location.name} (${location.regionName})`}
        intro={
          activeMode === 'dungeon' && dungeon
            ? `${location.locationTypeName} · Dungeon (${dungeon.dungeonTypeName ?? 'Dungeon'}) · ${dungeon.blockCount} RDB blocks (${dungeon.uniqueBlocks} unique)`
            : `${location.locationTypeName} · ${exterior.width}×${exterior.height} town grid · ${exterior.blocks.length} RMB blocks (${exterior.uniqueBlockKeys.length} unique)`
        }
        action={
          <div class="actions">
            {dungeon ? (
              <>
                <a
                  class={`button${activeMode === 'exterior' ? ' primary' : ''}`}
                  href={locationUrl(region, index, 'exterior')}
                >
                  Exterior ({exterior.width}×{exterior.height})
                </a>
                <a
                  class={`button${activeMode === 'dungeon' ? ' primary' : ''}`}
                  href={locationUrl(region, index, 'dungeon')}
                >
                  Dungeon ({dungeon.blockCount} RDB)
                </a>
              </>
            ) : null}
            <a class="button" href={locationsUrl()}>
              All locations
            </a>
          </div>
        }
      />

      {!detail.worldBlocksAvailable ? (
        <Empty>
          Staged `world-blocks` geometry is not available on disk. Run `scripts/data-sync.sh` to enable 3D rendering.
        </Empty>
      ) : (
        <div class="block-viewer-layout">
          <div class="block-viewer-main">
            {loadingBlocks && !compositeGrid ? (
              <div class="block-canvas-wrap">
                <div class="block-canvas-overlay">
                  Loading {activeMode === 'dungeon' ? dungeon?.uniqueBlocks : exterior.uniqueBlockKeys.length} unique
                  block documents…
                </div>
              </div>
            ) : compositeGrid ? (
              <BlockViewport
                compositeGrid={compositeGrid}
                selectedPickKey={selectedPick?.pickKey ?? null}
                focusRequestId={0}
                focusBoxRequest={focusBoxRequest}
                defaultClimate={location.climate}
                defaultDungeonRemaps={activeMode === 'dungeon' ? dungeonRemapTable : null}
                defaultDungeonLabel={
                  dungeon?.dungeonTypeName
                    ? `${location.name} (${dungeon.dungeonTypeName})`
                    : `${location.name} texture table`
                }
                onSelect={pick => {
                  setSelectedPick(pick);
                  if (pick && pick.gridX !== undefined && pick.gridY !== undefined) {
                    setSelectedCell({ x: pick.gridX, y: pick.gridY });
                  }
                }}
              />
            ) : (
              <Empty>No renderable block geometry found for this location.</Empty>
            )}
          </div>

          <aside class="block-inspector panel">
            {selectedPick ? (
              <div class="selected-pick-card">
                <div class="section-heading" style={{ marginBottom: '6px' }}>
                  <strong>{selectedPick.label}</strong>
                  <button type="button" onClick={() => setSelectedPick(null)}>
                    Clear
                  </button>
                </div>
                {selectedPick.sourceName ? (
                  <div class="muted" style={{ fontSize: '0.84rem', marginBottom: '6px' }}>
                    Block ({selectedPick.gridX}, {selectedPick.gridY}): <code>{selectedPick.sourceName}</code>
                  </div>
                ) : null}

                <div class="actions" style={{ marginBottom: '6px' }}>
                  {selectedPick.blockKey ? (
                    <a class="button" href={worldBlockUrl(selectedPick.blockKey)}>
                      Open block solo →
                    </a>
                  ) : null}
                  {pickedBuilding?.interiorKey ? (
                    <a class="button primary" href={worldBlockUrl(pickedBuilding.interiorKey)}>
                      Enter #{pickedBuilding.index}
                      {pickedBuilding.buildingTypeName ? ` (${pickedBuilding.buildingTypeName})` : ''} →
                    </a>
                  ) : null}
                </div>

                {pickedModelAsset ? (
                  <div class="usage-pills" style={{ marginTop: '4px' }}>
                    <a class="art-data-chip" href={assetUrl(pickedModelAsset.id)}>
                      <span>{pickedModelAsset.name}</span>
                      <code>{pickedModelAsset.category}</code>
                    </a>
                  </div>
                ) : null}

                {pickedPeopleSlot ? (
                  <div class="usage-pills" style={{ marginTop: '4px' }}>
                    {pickedPeopleSlot.castId ? (
                      <a class="art-data-chip" href={castUrl(pickedPeopleSlot.castId)}>
                        <span>{pickedPeopleSlot.castName ?? pickedPeopleSlot.castId}</span>
                        <code>cast</code>
                      </a>
                    ) : null}
                    {pickedPeopleSlot.spawner ? (
                      <a class="art-data-chip" href={spawnerUrl(pickedPeopleSlot.spawner)}>
                        <span>{pickedPeopleSlot.spawner}</span>
                        <code>spawner</code>
                      </a>
                    ) : null}
                    {pickedPeopleSlot.art?.npcId ? (
                      <ArtDataChip
                        href={subjectUrl(pickedPeopleSlot.art.npcId)}
                        id={pickedPeopleSlot.art.npcId}
                        label={pickedPeopleSlot.art.npcName ?? pickedPeopleSlot.art.npcId}
                        thumbnail={pickedPeopleSlot.art.classic}
                      />
                    ) : null}
                  </div>
                ) : null}
              </div>
            ) : (
              <p class="muted" style={{ marginTop: 0, fontSize: '0.86rem' }}>
                Click any building, model, or door in the 3D town — or pick a cell on the block map below — to jump to
                that block or step straight inside an interior.
              </p>
            )}

            <div class="inspector-tabs" role="tablist">
              <button
                type="button"
                role="tab"
                aria-selected={tab === 'grid' ? 'true' : 'false'}
                class={tab === 'grid' ? 'active' : undefined}
                onClick={() => setTab('grid')}
              >
                Block map
              </button>
              <button
                type="button"
                role="tab"
                aria-selected={tab === 'blocks' ? 'true' : 'false'}
                class={tab === 'blocks' ? 'active' : undefined}
                onClick={() => setTab('blocks')}
              >
                Blocks ({compositeGrid?.placements.length ?? 0})
              </button>
              <button
                type="button"
                role="tab"
                aria-selected={tab === 'summary' ? 'true' : 'false'}
                class={tab === 'summary' ? 'active' : undefined}
                onClick={() => setTab('summary')}
              >
                Summary
              </button>
            </div>

            <div class="inspector-scroll">
              {tab === 'grid' && gridMeta ? (
                <>
                  <div
                    class="town-grid-map"
                    style={{
                      gridTemplateColumns: `repeat(${gridMeta.maxX - gridMeta.minX + 1}, minmax(0, 1fr))`,
                    }}
                  >
                    {Array.from({ length: gridMeta.maxY - gridMeta.minY + 1 }, (_, rowIdx) => {
                      // North (+Y / -Z) at top of 2D map
                      const y = gridMeta.maxY - rowIdx;
                      return Array.from({ length: gridMeta.maxX - gridMeta.minX + 1 }, (_, colIdx) => {
                        const x = gridMeta.minX + colIdx;
                        const cell = gridMeta.cellMap.get(`${x},${y}`);
                        const isSelected = selectedCell?.x === x && selectedCell?.y === y;
                        if (!cell) {
                          return <div key={`${x},${y}`} class="town-grid-empty" />;
                        }
                        const shortName = cell.name.replace(/\.(RMB|RDB)$/i, '');
                        return (
                          <button
                            key={`${x},${y}`}
                            type="button"
                            class={`town-grid-cell${isSelected ? ' active' : ''}${cell.start ? ' start' : ''}`}
                            title={`${cell.name} at (${x}, ${y})${cell.interiors ? ` · ${cell.interiors} interiors` : ''}`}
                            onClick={() => focusGridCell(x, y)}
                          >
                            <span>{shortName}</span>
                          </button>
                        );
                      });
                    })}
                  </div>

                  {activeCellInfo ? (
                    <div class="panel" style={{ marginTop: '12px', padding: '10px' }}>
                      <div class="section-heading" style={{ marginBottom: '6px' }}>
                        <strong>
                          ({activeCellInfo.x}, {activeCellInfo.y}) · {activeCellInfo.name}
                        </strong>
                        {activeCellInfo.blockKey ? (
                          <a class="button" href={worldBlockUrl(activeCellInfo.blockKey)}>
                            Solo 3D →
                          </a>
                        ) : null}
                      </div>
                      {activeCellDetail?.block.buildings?.length ? (
                        <ul class="inspector-list">
                          {activeCellDetail.block.buildings.map(b => (
                            <li key={b.index} class="inspector-row">
                              <span>
                                <strong>#{b.index}</strong> {b.buildingTypeName ?? `Type ${b.buildingType}`}
                              </span>
                              {b.interiorKey ? (
                                <a href={worldBlockUrl(b.interiorKey)}>Interior →</a>
                              ) : (
                                <span class="muted">Ext only</span>
                              )}
                            </li>
                          ))}
                        </ul>
                      ) : (
                        <p class="muted" style={{ margin: 0, fontSize: '0.84rem' }}>
                          {activeMode === 'dungeon'
                            ? activeCellInfo.start
                              ? 'Dungeon entry block (start: true).'
                              : 'Dungeon block.'
                            : 'No interior buildings in this block (wall / gate / open plaza).'}
                        </p>
                      )}
                    </div>
                  ) : null}
                </>
              ) : null}

              {tab === 'blocks' ? (
                <>
                  <div style={{ marginBottom: '8px' }}>
                    <input
                      type="search"
                      value={blockFilter}
                      placeholder="Filter placed blocks by name or (x,y)…"
                      style={{ width: '100%' }}
                      onInput={event => setBlockFilter((event.currentTarget as HTMLInputElement).value)}
                    />
                  </div>
                  <ul class="inspector-list">
                    {blockListRows.map(p => (
                      <li
                        key={`${p.gridX},${p.gridY}:${p.blockKey}`}
                        class={`inspector-row${
                          selectedCell?.x === p.gridX && selectedCell?.y === p.gridY ? ' active' : ''
                        }`}
                      >
                        <button
                          type="button"
                          class="inspector-focus-btn"
                          onClick={() => focusGridCell(p.gridX, p.gridY)}
                        >
                          <strong>{p.sourceName}</strong>
                          <div class="muted">
                            ({p.gridX}, {p.gridY}) · {p.detail.block.models.length} models
                            {p.start ? ' · START' : ''}
                          </div>
                        </button>
                        <a href={worldBlockUrl(p.blockKey)}>Solo →</a>
                      </li>
                    ))}
                  </ul>
                </>
              ) : null}

              {tab === 'summary' ? (
                <>
                  <FactList
                    facts={{
                      Name: location.name,
                      Region: `${location.regionName} (#${location.region})`,
                      Type: location.locationTypeName,
                      Climate: location.climate ?? '—',
                      'Map pixel':
                        location.mapPixelX !== null && location.mapPixelY !== null
                          ? `(${location.mapPixelX}, ${location.mapPixelY})`
                          : '—',
                      'Town grid': `${exterior.width}×${exterior.height} (${exterior.blocks.length} blocks)`,
                      'Unique exterior blocks': exterior.uniqueBlockKeys.length,
                      ...(dungeon
                        ? {
                            Dungeon: `${dungeon.dungeonTypeName ?? 'Dungeon'} (${dungeon.blockCount} RDB blocks)`,
                          }
                        : {}),
                    }}
                  />
                  {location.buildingTypeLabels.length ? (
                    <div style={{ marginTop: '12px' }}>
                      <strong style={{ display: 'block', marginBottom: '6px', fontSize: '0.86rem' }}>
                        Building types present
                      </strong>
                      <div class="usage-pills">
                        {location.buildingTypeLabels.map(bt => (
                          <span key={bt.id} class="chip">
                            {bt.name ?? `Type ${bt.id}`}
                          </span>
                        ))}
                      </div>
                    </div>
                  ) : null}
                </>
              ) : null}
            </div>
          </aside>
        </div>
      )}
    </>
  );
}
