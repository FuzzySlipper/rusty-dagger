import { useEffect, useMemo, useState } from 'preact/hooks';
import { apiRequest, isAbortError } from '../api';
import {
  assetUrl,
  blocksUrl,
  castUrl,
  spawnerUrl,
  subjectUrl,
  worldBlocksUrl,
  worldBlockUrl,
} from '../router';
import { useSessionState } from '../hooks/useSessionState';
import type {
  PeoplePackOverview,
  WorldBlockDetailResponse,
  WorldBlockKind,
  WorldBlockListItem,
  WorldBlockPeopleSlot,
  WorldBlocksQueryResponse,
  WorldBlockVariantModel,
  WorldBlockVariantRecipe,
  WorldVariantsResponse,
} from '../types';
import type { ScenePick, TransformCommitPayload, VariantPreviewOverlay } from '../viewer/BlockScene';
import { BlockViewport, type ViewportEditControls } from '../components/BlockViewport';
import {
  makeSlotDraft,
  ModelPickerModal,
  PeopleSlotEditorForm,
  VariantEditorPanel,
  type PeopleSlotDraft,
  type VariantDraft,
} from '../components/WorldBlockEditors';
import { ArtDataChip } from '../components/ArtDataLink';
import { DataTable, type DataColumn } from '../components/DataTable';
import { Chip, Empty, FactList, PageHeading } from '../components/ui';

const KIND_LABELS: Record<WorldBlockKind, string> = {
  rmbExterior: 'RMB Exterior',
  rmbInterior: 'RMB Interior',
  rdb: 'RDB Dungeon',
};

interface WorldBlockListFilters {
  q: string;
  kind: string;
  hasPeople: boolean;
  page: number;
}

const DEFAULT_FILTERS: WorldBlockListFilters = {
  q: '',
  kind: '',
  hasPeople: false,
  page: 1,
};

function fmtQuat(q: { x: number; y: number; z: number; w: number }): string {
  return `(${q.x.toFixed(3)}, ${q.y.toFixed(3)}, ${q.z.toFixed(3)}, ${q.w.toFixed(3)})`;
}

function fmtVec(v: { x: number; y: number; z: number } | [number, number, number]): string {
  if (Array.isArray(v)) {
    return `(${v[0].toFixed(2)}, ${v[1].toFixed(2)}, ${v[2].toFixed(2)})`;
  }
  return `(${v.x.toFixed(2)}, ${v.y.toFixed(2)}, ${v.z.toFixed(2)})`;
}

export function WorldBlocksListPage({ initialQuery }: { initialQuery: Record<string, string> }) {
  const [filters, setFilters] = useSessionState<WorldBlockListFilters>('cms.worldBlocks.filters', DEFAULT_FILTERS);
  const [result, setResult] = useState<WorldBlocksQueryResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const pageSize = 50;

  useEffect(() => {
    if (initialQuery.kind || initialQuery.q) {
      setFilters(prev => ({
        ...prev,
        kind: initialQuery.kind ?? prev.kind,
        q: initialQuery.q ?? prev.q,
        page: 1,
      }));
    }
  }, [initialQuery.kind, initialQuery.q]);

  const { q, kind, hasPeople, page } = filters;
  const patch = (change: Partial<WorldBlockListFilters>) =>
    setFilters(prev => ({ ...prev, ...change, page: change.page ?? 1 }));
  const hasActiveFilters = Boolean(q || kind || hasPeople);

  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    setError(null);
    const params = new URLSearchParams();
    if (q.trim()) params.set('q', q.trim());
    if (kind) params.set('kind', kind);
    if (hasPeople) params.set('hasPeople', '1');
    params.set('offset', String((page - 1) * pageSize));
    params.set('limit', String(pageSize));

    apiRequest<WorldBlocksQueryResponse>(`/api/world-blocks?${params.toString()}`, { signal: controller.signal })
      .then(data => setResult(data))
      .catch(err => {
        if (!isAbortError(err)) setError(err instanceof Error ? err.message : String(err));
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false);
      });
    return () => controller.abort();
  }, [q, kind, hasPeople, page]);

  const columns: Array<DataColumn<WorldBlockListItem>> = [
    {
      key: 'source',
      header: 'Block',
      render: row => (
        <div>
          <a href={worldBlockUrl(row.key)}>
            <strong>{row.sourceKey}</strong>
            {row.buildingIndex !== null ? ` · Building #${row.buildingIndex}` : ''}
          </a>
          <div>
            <code class="muted">{row.key}</code>
          </div>
        </div>
      ),
    },
    {
      key: 'kind',
      header: 'Kind',
      render: row => <Chip state="empty" label={KIND_LABELS[row.kind]} />,
    },
    {
      key: 'interiors',
      header: 'Interiors',
      render: row =>
        row.kind === 'rmbExterior' ? (
          <span>{row.interiorCount}</span>
        ) : (
          <span class="muted">—</span>
        ),
    },
    {
      key: 'people',
      header: 'People slots',
      render: row =>
        row.totalPeopleSlotCount > 0 ? (
          <span>
            <strong>{row.peopleSlotCount}</strong>
            {row.kind === 'rmbExterior' && row.totalPeopleSlotCount > row.peopleSlotCount ? (
              <small class="muted"> ({row.totalPeopleSlotCount} incl. interiors)</small>
            ) : null}
          </span>
        ) : (
          <span class="muted">0</span>
        ),
    },
    {
      key: 'action',
      header: '',
      render: row => (
        <a class="button" href={worldBlockUrl(row.key)}>
          Open 3D
        </a>
      ),
    },
  ];

  return (
    <>
      <PageHeading
        title="World blocks (3D)"
        intro="Browse and inspect every staged RMB exterior, RMB interior, and RDB dungeon block in 3D with cross-links to models and daggerfall.people slots."
      />

      {result && !result.available ? (
        <Empty>{result.reason ?? 'Staged world-blocks data is not available.'}</Empty>
      ) : (
        <>
          {result ? (
            <div class="stat-strip">
              <button
                type="button"
                class={`stat-pill${kind === '' && !hasPeople ? ' active' : ''}`}
                onClick={() => patch({ kind: '', hasPeople: false })}
              >
                <strong>{result.counts.total.toLocaleString()}</strong>
                <span>All blocks</span>
              </button>
              <button
                type="button"
                class={`stat-pill${kind === 'rmbExterior' ? ' active' : ''}`}
                onClick={() => patch({ kind: kind === 'rmbExterior' ? '' : 'rmbExterior' })}
              >
                <strong>{result.counts.rmbExterior.toLocaleString()}</strong>
                <span>RMB Exteriors</span>
              </button>
              <button
                type="button"
                class={`stat-pill${kind === 'rmbInterior' ? ' active' : ''}`}
                onClick={() => patch({ kind: kind === 'rmbInterior' ? '' : 'rmbInterior' })}
              >
                <strong>{result.counts.rmbInterior.toLocaleString()}</strong>
                <span>RMB Interiors</span>
              </button>
              <button
                type="button"
                class={`stat-pill${kind === 'rdb' ? ' active' : ''}`}
                onClick={() => patch({ kind: kind === 'rdb' ? '' : 'rdb' })}
              >
                <strong>{result.counts.rdb.toLocaleString()}</strong>
                <span>RDB Dungeons</span>
              </button>
            </div>
          ) : null}

          <div class="filters">
            <label>
              Search block
              <input
                type="search"
                value={q}
                placeholder="ALCHAL00.RMB, B0000000.RDB, interior-0"
                onInput={event => patch({ q: (event.currentTarget as HTMLInputElement).value })}
              />
            </label>
            <label>
              Kind
              <select
                value={kind}
                onChange={event => patch({ kind: (event.currentTarget as HTMLSelectElement).value })}
              >
                <option value="">All kinds</option>
                <option value="rmbExterior">RMB Exterior</option>
                <option value="rmbInterior">RMB Interior</option>
                <option value="rdb">RDB Dungeon</option>
              </select>
            </label>
            <label class="mark-filter">
              <input
                type="checkbox"
                checked={hasPeople}
                onChange={event => patch({ hasPeople: (event.currentTarget as HTMLInputElement).checked })}
              />{' '}
              Has daggerfall.people slots
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
                rowKey={row => row.key}
                emptyMessage="No world blocks match the current filters."
                pagination={{
                  page,
                  pageSize,
                  total: result.total,
                  onPageChange: next => setFilters(prev => ({ ...prev, page: next })),
                }}
              />
            </section>
          ) : loading ? (
            <p>Loading world blocks…</p>
          ) : null}
        </>
      )}
    </>
  );
}

type InspectorTab = 'summary' | 'buildings' | 'people' | 'models' | 'doors' | 'objects' | 'variants';

function draftFromRecipe(recipe: WorldBlockVariantRecipe): VariantDraft {
  return {
    packId: recipe.packId,
    block: recipe.block,
    variant: recipe.variant,
    originalVariant: recipe.variant,
    isNew: false,
    remove: [...recipe.remove],
    add: recipe.add.map(a => ({
      model: a.model,
      position: { ...a.position },
      yawDegrees: a.yawDegrees,
    })),
  };
}

function cloneVariantDraft(d: VariantDraft): VariantDraft {
  return {
    ...d,
    remove: [...d.remove],
    add: d.add.map(a => ({ model: a.model, position: { ...a.position }, yawDegrees: a.yawDegrees })),
  };
}

function serializeVariantDraft(d: VariantDraft | null): string {
  if (!d) return '';
  return JSON.stringify({ packId: d.packId, block: d.block, variant: d.variant, remove: d.remove, add: d.add });
}

function serializeSlotDraft(d: PeopleSlotDraft | null): string {
  if (!d) return '';
  return JSON.stringify({
    position: d.position,
    bindingKind: d.bindingKind,
    spawner: d.bindingKind === 'spawner' ? d.spawner : null,
    castId: d.bindingKind === 'cast' ? d.castId : null,
    interaction: d.interaction,
    deriveRegionalFaction: d.deriveRegionalFaction,
    factionId: d.factionId.trim(),
  });
}

export function WorldBlockDetailPage({ blockKey }: { blockKey: string }) {
  const [detail, setDetail] = useState<WorldBlockDetailResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [tab, setTab] = useState<InspectorTab>('summary');
  const [selectedPick, setSelectedPick] = useState<ScenePick | null>(null);
  const [focusRequestId, setFocusRequestId] = useState(0);
  const [modelFilter, setModelFilter] = useState('');

  // Tier 3: 3D edit mode state
  const [editMode, setEditMode] = useState(false);
  const [transformMode, setTransformMode] = useState<'translate' | 'rotate'>('translate');
  const [snap, setSnap] = useState(true);

  // Tier 3: Block variant recipe authoring state (RMB exteriors)
  const [variantsData, setVariantsData] = useState<WorldVariantsResponse | null>(null);
  const [variantDraft, setVariantDraft] = useState<VariantDraft | null>(null);
  const [variantBaseline, setVariantBaseline] = useState('');
  const [variantPast, setVariantPast] = useState<VariantDraft[]>([]);
  const [variantFuture, setVariantFuture] = useState<VariantDraft[]>([]);
  const [variantPreviewEnabled, setVariantPreviewEnabled] = useState(true);
  const [showRemovedGhosts, setShowRemovedGhosts] = useState(true);
  const [modelPickerOpen, setModelPickerOpen] = useState(false);
  const [variantSaving, setVariantSaving] = useState(false);
  const [variantBaking, setVariantBaking] = useState(false);
  const [variantStatus, setVariantStatus] = useState<string | null>(null);
  const [variantError, setVariantError] = useState<string | null>(null);

  // Tier 3: People-slot spatial editing state
  const [peopleOverview, setPeopleOverview] = useState<PeoplePackOverview | null>(null);
  const [slotDraft, setSlotDraft] = useState<PeopleSlotDraft | null>(null);
  const [slotBaseline, setSlotBaseline] = useState('');
  const [slotPast, setSlotPast] = useState<PeopleSlotDraft[]>([]);
  const [slotFuture, setSlotFuture] = useState<PeopleSlotDraft[]>([]);
  const [slotSaving, setSlotSaving] = useState(false);
  const [slotStatus, setSlotStatus] = useState<string | null>(null);
  const [slotError, setSlotError] = useState<string | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    setError(null);
    setSelectedPick(null);
    setFocusRequestId(0);
    setModelFilter('');
    setVariantsData(null);
    setVariantDraft(null);
    setVariantBaseline('');
    setVariantPast([]);
    setVariantFuture([]);
    setVariantStatus(null);
    setVariantError(null);
    setSlotDraft(null);
    setSlotBaseline('');
    setSlotPast([]);
    setSlotFuture([]);
    setSlotStatus(null);
    setSlotError(null);

    apiRequest<WorldBlockDetailResponse>(`/api/world-blocks/detail?key=${encodeURIComponent(blockKey)}`, {
      signal: controller.signal,
    })
      .then(data => {
        setDetail(data);
        setTab(data.block.kind === 'rmbExterior' && data.block.buildings?.length ? 'buildings' : 'summary');
        if (data.block.kind === 'rmbExterior') {
          apiRequest<WorldVariantsResponse>(
            `/api/world-variants?block=${encodeURIComponent(data.block.sourceKey)}`,
            { signal: controller.signal },
          )
            .then(vdata => setVariantsData(vdata))
            .catch(err => {
              if (!isAbortError(err)) setVariantsData(null);
            });
        }
      })
      .catch(err => {
        if (!isAbortError(err)) setError(err instanceof Error ? err.message : String(err));
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false);
      });
    return () => controller.abort();
  }, [blockKey]);

  // Lazily load people-pack overview (spawner & cast dropdowns) when Edit 3D is activated
  useEffect(() => {
    if (!editMode || peopleOverview) return;
    const controller = new AbortController();
    apiRequest<PeoplePackOverview>('/api/people-pack/overview', { signal: controller.signal })
      .then(data => setPeopleOverview(data))
      .catch(() => {});
    return () => controller.abort();
  }, [editMode, peopleOverview]);

  // Sync slotDraft whenever a peopleSlot is selected
  useEffect(() => {
    if (selectedPick?.kind !== 'peopleSlot' || selectedPick.index === undefined || !detail) {
      setSlotDraft(null);
      setSlotBaseline('');
      setSlotPast([]);
      setSlotFuture([]);
      setSlotStatus(null);
      setSlotError(null);
      return;
    }
    const slot = detail.peopleSlots[selectedPick.index];
    if (!slot) return;
    const nextDraft = makeSlotDraft(slot, selectedPick.pickKey);
    setSlotDraft(nextDraft);
    setSlotBaseline(serializeSlotDraft(nextDraft));
    setSlotPast([]);
    setSlotFuture([]);
    setSlotStatus(null);
    setSlotError(null);
  }, [selectedPick?.pickKey, detail?.block.key]);

  const variantOverlay = useMemo<VariantPreviewOverlay | null>(() => {
    if (!variantDraft) return null;
    return {
      enabled: variantPreviewEnabled,
      remove: variantDraft.remove,
      add: variantDraft.add,
      showRemovedGhosts,
    };
  }, [variantDraft, variantPreviewEnabled, showRemovedGhosts]);

  const peopleSlotPositionOverrides = useMemo<Record<string, [number, number, number]> | null>(() => {
    if (!slotDraft) return null;
    return { [slotDraft.pickKey]: slotDraft.position };
  }, [slotDraft?.pickKey, slotDraft?.position[0], slotDraft?.position[1], slotDraft?.position[2]]);

  const filteredModels = useMemo(() => {
    const models = detail?.block.models ?? [];
    const term = modelFilter.trim().toLowerCase();
    if (!term) return models;
    return models.filter(
      m =>
        m.modelId.toLowerCase().includes(term) ||
        m.id.toLowerCase().includes(term) ||
        (m.buildingIndex !== null && `building ${m.buildingIndex}`.includes(term)),
    );
  }, [detail, modelFilter]);

  if (loading && !detail) {
    return (
      <>
        <PageHeading title="Loading block…" />
        <p>Fetching block geometry and metadata…</p>
      </>
    );
  }

  if (error || !detail) {
    return (
      <>
        <PageHeading
          title="Block not found"
          action={
            <a class="button" href={worldBlocksUrl()}>
              Back to World blocks
            </a>
          }
        />
        <Empty>{error ?? `Could not open block '${blockKey}'.`}</Empty>
      </>
    );
  }

  const { block, parentExteriorKey, peopleSlots, modelLinks } = detail;
  const doorCount =
    (block.doors?.length ?? 0) + (block.transitionDoors?.length ?? 0) + (block.gates?.length ?? 0);
  const peopleCount = peopleSlots.length + (block.staticNpcs?.length ?? 0);
  const objectCount =
    (block.billboards?.length ?? 0) +
    (block.lights?.length ?? 0) +
    (block.questMarkers?.length ?? 0) +
    (block.randomEnemies?.length ?? 0) +
    (block.propertyContainers?.length ?? 0) +
    (block.actions?.length ?? 0);

  function selectAndFocus(pick: ScenePick) {
    setSelectedPick(pick);
    setFocusRequestId(prev => prev + 1);
  }

  const selectedModel =
    selectedPick?.kind === 'model' ? block.models.find(m => m.id === selectedPick.id) ?? null : null;
  const selectedModelAsset = selectedModel ? modelLinks[selectedModel.modelId] ?? null : null;
  const selectedBuilding =
    selectedPick?.buildingIndex !== null && selectedPick?.buildingIndex !== undefined
      ? block.buildings?.find(b => b.index === selectedPick.buildingIndex) ?? null
      : null;
  const selectedPeopleSlot =
    selectedPick?.kind === 'peopleSlot' && selectedPick.index !== undefined
      ? peopleSlots[selectedPick.index] ?? null
      : null;
  const selectedStaticNpc =
    selectedPick?.kind === 'staticNpc'
      ? block.staticNpcs?.find(n => n.id === selectedPick.id) ?? null
      : null;
  const selectedVariantAddIndex =
    selectedPick?.kind === 'variantAdd' && selectedPick.index !== undefined ? selectedPick.index : null;

  const isExterior = block.kind === 'rmbExterior';
  const variantDirty = Boolean(variantDraft && (variantDraft.isNew || serializeVariantDraft(variantDraft) !== variantBaseline));
  const slotDirty = Boolean(slotDraft && serializeSlotDraft(slotDraft) !== slotBaseline);

  function loadSavedVariant(recipe: WorldBlockVariantRecipe | null) {
    setVariantStatus(null);
    setVariantError(null);
    setVariantPast([]);
    setVariantFuture([]);
    if (!recipe) {
      setVariantDraft(null);
      setVariantBaseline('');
      if (selectedPick?.kind === 'variantAdd') setSelectedPick(null);
      return;
    }
    const next = draftFromRecipe(recipe);
    setVariantDraft(next);
    setVariantBaseline(serializeVariantDraft(next));
    setVariantPreviewEnabled(true);
  }

  function createNewVariantDraft(packId: string, variantName: string) {
    setVariantStatus(null);
    setVariantError(null);
    setVariantPast([]);
    setVariantFuture([]);
    const next: VariantDraft = {
      packId,
      block: block.sourceKey,
      variant: variantName,
      originalVariant: null,
      isNew: true,
      remove: [],
      add: [],
    };
    setVariantDraft(next);
    setVariantBaseline('');
    setVariantPreviewEnabled(true);
    setEditMode(true);
  }

  function commitVariantChange(next: VariantDraft) {
    if (variantDraft) {
      if (serializeVariantDraft(next) === serializeVariantDraft(variantDraft)) return;
      setVariantPast(prev => [...prev.slice(-39), cloneVariantDraft(variantDraft)]);
    }
    setVariantFuture([]);
    setVariantDraft(next);
    setVariantStatus(null);
    setVariantError(null);
  }

  function toggleHideMiscModel(placementId: string) {
    if (!variantDraft) return;
    const exists = variantDraft.remove.includes(placementId);
    const nextRemove = exists
      ? variantDraft.remove.filter(id => id !== placementId)
      : [...variantDraft.remove, placementId];
    commitVariantChange({ ...variantDraft, remove: nextRemove });
    if (!exists && selectedPick?.id === placementId) {
      setSelectedPick(null);
    }
  }

  function addModelToVariant(modelId: string) {
    if (!variantDraft) return;
    const defaultPos = selectedPick
      ? { x: selectedPick.position[0], y: Math.max(0, selectedPick.position[1]), z: selectedPick.position[2] }
      : block.bounds
        ? {
            x: Math.round(((block.bounds.minimum.x + block.bounds.maximum.x) * 0.5) * 4) / 4,
            y: 0.5,
            z: Math.round(((block.bounds.minimum.z + block.bounds.maximum.z) * 0.5) * 4) / 4,
          }
        : { x: 51.2, y: 0.5, z: -51.2 };
    const nextIndex = variantDraft.add.length;
    const nextAdd: WorldBlockVariantModel[] = [
      ...variantDraft.add,
      { model: modelId, position: defaultPos, yawDegrees: 0 },
    ];
    commitVariantChange({ ...variantDraft, add: nextAdd });
    setVariantPreviewEnabled(true);
    setEditMode(true);
    setSelectedPick({
      pickKey: `variantAdd:${nextIndex}`,
      kind: 'variantAdd',
      id: `variantAdd:${nextIndex}`,
      label: `Added ARCH3D ${modelId} (#${nextIndex + 1})`,
      position: [defaultPos.x, defaultPos.y, defaultPos.z],
      modelId,
      index: nextIndex,
    });
    setFocusRequestId(prev => prev + 1);
  }

  function updateVariantAddedModel(index: number, nextItem: WorldBlockVariantModel) {
    if (!variantDraft || index < 0 || index >= variantDraft.add.length) return;
    const nextAdd = variantDraft.add.map((item, i) => (i === index ? nextItem : item));
    commitVariantChange({ ...variantDraft, add: nextAdd });
  }

  function duplicateVariantAddedModel(index: number) {
    if (!variantDraft || index < 0 || index >= variantDraft.add.length) return;
    const src = variantDraft.add[index];
    const nextIndex = variantDraft.add.length;
    const copy: WorldBlockVariantModel = {
      model: src.model,
      position: { x: Math.round((src.position.x + 1.5) * 1000) / 1000, y: src.position.y, z: src.position.z },
      yawDegrees: src.yawDegrees,
    };
    commitVariantChange({ ...variantDraft, add: [...variantDraft.add, copy] });
    setEditMode(true);
    setSelectedPick({
      pickKey: `variantAdd:${nextIndex}`,
      kind: 'variantAdd',
      id: `variantAdd:${nextIndex}`,
      label: `Added ARCH3D ${copy.model} (#${nextIndex + 1})`,
      position: [copy.position.x, copy.position.y, copy.position.z],
      modelId: copy.model,
      index: nextIndex,
    });
  }

  function deleteVariantAddedModel(index: number) {
    if (!variantDraft || index < 0 || index >= variantDraft.add.length) return;
    const nextAdd = variantDraft.add.filter((_, i) => i !== index);
    commitVariantChange({ ...variantDraft, add: nextAdd });
    if (selectedPick?.kind === 'variantAdd') setSelectedPick(null);
  }

  async function saveVariantRecipe() {
    if (!variantDraft) return;
    setVariantSaving(true);
    setVariantError(null);
    setVariantStatus(null);
    try {
      const res = await apiRequest<WorldVariantsResponse & { recipe: WorldBlockVariantRecipe }>(
        '/api/world-variants',
        {
          method: 'PUT',
          body: {
            packId: variantDraft.packId,
            block: variantDraft.block,
            variant: variantDraft.variant,
            originalVariant: variantDraft.originalVariant,
            remove: variantDraft.remove,
            add: variantDraft.add,
          },
        },
      );
      setVariantsData({
        packs: res.packs,
        variants: res.variants,
        arena2Available: res.arena2Available,
      });
      const synced = draftFromRecipe(res.recipe);
      setVariantDraft(synced);
      setVariantBaseline(serializeVariantDraft(synced));
      setVariantStatus(`Saved '${res.recipe.variant}' to ${res.recipe.packId}/payload.json.`);
    } catch (err) {
      setVariantError(err instanceof Error ? err.message : String(err));
    } finally {
      setVariantSaving(false);
    }
  }

  async function bakeVariantPack() {
    if (!variantDraft) return;
    setVariantBaking(true);
    setVariantError(null);
    setVariantStatus(null);
    try {
      const res = await apiRequest<WorldVariantsResponse & { ok: boolean; output: string }>(
        '/api/world-variants/bake',
        {
          method: 'POST',
          body: { packId: variantDraft.packId, block: variantDraft.block },
        },
      );
      setVariantsData({
        packs: res.packs,
        variants: res.variants,
        arena2Available: res.arena2Available,
      });
      setVariantStatus(res.output ? `Baked ${variantDraft.packId}: ${res.output.split('\n')[0]}` : `Baked ${variantDraft.packId}.`);
    } catch (err) {
      setVariantError(err instanceof Error ? err.message : String(err));
    } finally {
      setVariantBaking(false);
    }
  }

  async function deleteVariantRecipe() {
    if (!variantDraft || variantDraft.isNew) return;
    const confirmed = window.confirm(
      `Delete variant '${variantDraft.variant}' on ${variantDraft.block} from ${variantDraft.packId}?`,
    );
    if (!confirmed) return;
    setVariantSaving(true);
    setVariantError(null);
    setVariantStatus(null);
    try {
      const params = new URLSearchParams({
        packId: variantDraft.packId,
        block: variantDraft.block,
        variant: variantDraft.originalVariant ?? variantDraft.variant,
      });
      const res = await apiRequest<WorldVariantsResponse & { deleted: boolean }>(
        `/api/world-variants?${params.toString()}`,
        { method: 'DELETE' },
      );
      setVariantsData({
        packs: res.packs,
        variants: res.variants,
        arena2Available: res.arena2Available,
      });
      setVariantDraft(null);
      setVariantBaseline('');
      setVariantPast([]);
      setVariantFuture([]);
      if (selectedPick?.kind === 'variantAdd') setSelectedPick(null);
      setVariantStatus(`Deleted variant '${variantDraft.variant}'.`);
    } catch (err) {
      setVariantError(err instanceof Error ? err.message : String(err));
    } finally {
      setVariantSaving(false);
    }
  }

  function commitSlotChange(next: PeopleSlotDraft) {
    if (slotDraft) {
      if (serializeSlotDraft(next) === serializeSlotDraft(slotDraft)) return;
      setSlotPast(prev => [...prev.slice(-39), { ...slotDraft, position: [...slotDraft.position] }]);
    }
    setSlotFuture([]);
    setSlotDraft(next);
    setSlotStatus(null);
    setSlotError(null);
  }

  function revertSlotDraft() {
    if (!selectedPeopleSlot || !selectedPick) return;
    const fresh = makeSlotDraft(selectedPeopleSlot, selectedPick.pickKey);
    setSlotDraft(fresh);
    setSlotBaseline(serializeSlotDraft(fresh));
    setSlotPast([]);
    setSlotFuture([]);
    setSlotError(null);
    setSlotStatus(null);
  }

  async function saveSlotEdit() {
    if (!slotDraft || selectedPick?.kind !== 'peopleSlot' || selectedPick.index === undefined) return;
    setSlotSaving(true);
    setSlotError(null);
    setSlotStatus(null);
    try {
      const rawFaction = slotDraft.factionId.trim();
      const factionId = rawFaction === '' ? null : Number(rawFaction);
      const updated = await apiRequest<WorldBlockPeopleSlot>('/api/people-pack/blocks/slot', {
        method: 'PUT',
        body: {
          kind: slotDraft.kind,
          block: slotDraft.block,
          buildingIndex: slotDraft.buildingIndex,
          slotId: slotDraft.slotId,
          position: slotDraft.position,
          spawner: slotDraft.bindingKind === 'spawner' ? slotDraft.spawner : null,
          castId: slotDraft.bindingKind === 'cast' ? slotDraft.castId : null,
          interaction: slotDraft.interaction,
          deriveRegionalFaction: slotDraft.deriveRegionalFaction,
          factionId,
        },
      });
      const slotIdx = selectedPick.index;
      setDetail(prev => {
        if (!prev) return prev;
        const nextSlots = prev.peopleSlots.map((s, i) => (i === slotIdx ? updated : s));
        return { ...prev, peopleSlots: nextSlots };
      });
      const synced = makeSlotDraft(updated, slotDraft.pickKey);
      setSlotDraft(synced);
      setSlotBaseline(serializeSlotDraft(synced));
      setSlotStatus(`Saved slot ${updated.slotId} to daggerfall.people.`);
    } catch (err) {
      setSlotError(err instanceof Error ? err.message : String(err));
    } finally {
      setSlotSaving(false);
    }
  }

  function handleTransformCommit(pickKey: string, payload: TransformCommitPayload) {
    if (pickKey.startsWith('variantAdd:') && variantDraft) {
      const idx = Number(pickKey.slice('variantAdd:'.length));
      if (Number.isInteger(idx) && idx >= 0 && idx < variantDraft.add.length) {
        const cur = variantDraft.add[idx];
        updateVariantAddedModel(idx, {
          ...cur,
          position: { x: payload.position[0], y: payload.position[1], z: payload.position[2] },
          yawDegrees: payload.yawDegrees !== undefined ? payload.yawDegrees : cur.yawDegrees,
        });
      }
      return;
    }
    if (pickKey.startsWith('peopleSlot:') && slotDraft && slotDraft.pickKey === pickKey) {
      commitSlotChange({ ...slotDraft, position: [...payload.position] });
    }
  }

  const editingSlot = Boolean(editMode && selectedPick?.kind === 'peopleSlot' && slotDraft);
  const canUndo = editingSlot ? slotPast.length > 0 : variantPast.length > 0;
  const canRedo = editingSlot ? slotFuture.length > 0 : variantFuture.length > 0;

  function handleUndo() {
    if (editingSlot && slotDraft && slotPast.length > 0) {
      const prev = slotPast[slotPast.length - 1];
      setSlotPast(p => p.slice(0, -1));
      setSlotFuture(f => [{ ...slotDraft, position: [...slotDraft.position] }, ...f]);
      setSlotDraft(prev);
      return;
    }
    if (variantDraft && variantPast.length > 0) {
      const prev = variantPast[variantPast.length - 1];
      setVariantPast(p => p.slice(0, -1));
      setVariantFuture(f => [cloneVariantDraft(variantDraft), ...f]);
      setVariantDraft(prev);
    }
  }

  function handleRedo() {
    if (editingSlot && slotDraft && slotFuture.length > 0) {
      const next = slotFuture[0];
      setSlotFuture(f => f.slice(1));
      setSlotPast(p => [...p, { ...slotDraft, position: [...slotDraft.position] }]);
      setSlotDraft(next);
      return;
    }
    if (variantDraft && variantFuture.length > 0) {
      const next = variantFuture[0];
      setVariantFuture(f => f.slice(1));
      setVariantPast(p => [...p, cloneVariantDraft(variantDraft)]);
      setVariantDraft(next);
    }
  }

  const gizmoTargetKey =
    editMode && selectedPick && (selectedPick.kind === 'variantAdd' || selectedPick.kind === 'peopleSlot')
      ? selectedPick.pickKey
      : null;

  const viewportEditControls: ViewportEditControls = {
    enabled: editMode,
    mode: selectedPick?.kind === 'peopleSlot' ? 'translate' : transformMode,
    snap,
    gizmoTargetKey,
    gizmoTargetLabel: selectedPick?.label ?? null,
    canRotate: selectedPick?.kind === 'variantAdd',
    canUndo,
    canRedo,
    onModeChange: next => setTransformMode(next),
    onSnapChange: next => setSnap(next),
    onUndo: handleUndo,
    onRedo: handleRedo,
    onTransformCommit: handleTransformCommit,
  };

  function openVariantsTab() {
    setTab('variants');
    if (!variantDraft && variantsData?.variants.length) {
      loadSavedVariant(variantsData.variants[0]);
    }
  }

  const title =
    block.buildingIndex !== null
      ? `${block.sourceKey} · Building #${block.buildingIndex}${
          block.interiorBuilding?.buildingTypeName ? ` (${block.interiorBuilding.buildingTypeName})` : ''
        }`
      : block.sourceKey;

  return (
    <>
      <PageHeading
        title={title}
        intro={`${KIND_LABELS[block.kind]} · ${block.key}`}
        action={
          <div class="actions">
            <button
              type="button"
              class={editMode ? 'primary' : undefined}
              aria-pressed={editMode ? 'true' : 'false'}
              onClick={() => setEditMode(prev => !prev)}
            >
              {editMode ? 'Done editing 3D' : 'Edit 3D'}
            </button>
            {parentExteriorKey ? (
              <a class="button primary" href={worldBlockUrl(parentExteriorKey)}>
                ← Exterior ({block.sourceKey})
              </a>
            ) : null}
            <a class="button" href={blocksUrl({ q: block.sourceKey })}>
              People slots table
            </a>
            <a class="button" href={worldBlocksUrl()}>
              All blocks
            </a>
          </div>
        }
      />

      <div class="block-viewer-layout">
        <div class="block-viewer-main">
          <BlockViewport
            detail={detail}
            selectedPickKey={selectedPick?.pickKey ?? null}
            focusRequestId={focusRequestId}
            variantOverlay={variantOverlay}
            editControls={viewportEditControls}
            peopleSlotPositionOverrides={peopleSlotPositionOverrides}
            onSelect={pick => setSelectedPick(pick)}
          />
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
              <div class="muted" style={{ fontSize: '0.85rem', marginBottom: '8px' }}>
                <code>{selectedPick.id}</code> · {fmtVec(selectedPick.position)}
              </div>

              {selectedModel ? (
                <>
                  <div class="usage-pills" style={{ marginBottom: '6px' }}>
                    <span class="chip">ARCH3D {selectedModel.modelId}</span>
                    <span class="chip">rot {fmtQuat(selectedModel.rotation)}</span>
                    {selectedModelAsset ? (
                      <a class="art-data-chip" href={assetUrl(selectedModelAsset.id)}>
                        <span>{selectedModelAsset.name}</span>
                        <code>{selectedModelAsset.category}</code>
                      </a>
                    ) : null}
                  </div>
                  {isExterior && selectedModel.buildingIndex === null && variantDraft ? (
                    <div class="actions" style={{ marginTop: '6px' }}>
                      <button
                        type="button"
                        class={variantDraft.remove.includes(selectedModel.id) ? undefined : 'primary'}
                        onClick={() => toggleHideMiscModel(selectedModel.id)}
                      >
                        {variantDraft.remove.includes(selectedModel.id)
                          ? `Restore in '${variantDraft.variant}'`
                          : `Hide in '${variantDraft.variant}'`}
                      </button>
                    </div>
                  ) : null}
                </>
              ) : null}

              {selectedVariantAddIndex !== null && variantDraft?.add[selectedVariantAddIndex] ? (
                <div class="actions" style={{ marginTop: '6px' }}>
                  <button type="button" onClick={() => setTab('variants')}>
                    Edit in Variants tab
                  </button>
                  <button type="button" onClick={() => duplicateVariantAddedModel(selectedVariantAddIndex)}>
                    Duplicate
                  </button>
                  <button type="button" onClick={() => deleteVariantAddedModel(selectedVariantAddIndex)}>
                    Remove
                  </button>
                </div>
              ) : null}

              {selectedBuilding?.interiorKey ? (
                <div class="actions" style={{ marginTop: '6px' }}>
                  <a class="button primary" href={worldBlockUrl(selectedBuilding.interiorKey)}>
                    Enter Building #{selectedBuilding.index}
                    {selectedBuilding.buildingTypeName ? ` (${selectedBuilding.buildingTypeName})` : ''} →
                  </a>
                </div>
              ) : null}

              {selectedPeopleSlot ? (
                <>
                  <div class="usage-pills" style={{ marginTop: '6px' }}>
                    {selectedPeopleSlot.castId ? (
                      <a class="art-data-chip" href={castUrl(selectedPeopleSlot.castId)}>
                        <span>{selectedPeopleSlot.castName ?? selectedPeopleSlot.castId}</span>
                        <code>cast</code>
                      </a>
                    ) : null}
                    {selectedPeopleSlot.spawner ? (
                      <a class="art-data-chip" href={spawnerUrl(selectedPeopleSlot.spawner)}>
                        <span>{selectedPeopleSlot.spawner}</span>
                        <code>spawner</code>
                      </a>
                    ) : null}
                    {selectedPeopleSlot.art?.npcId ? (
                      <ArtDataChip
                        href={subjectUrl(selectedPeopleSlot.art.npcId)}
                        id={selectedPeopleSlot.art.npcId}
                        label={selectedPeopleSlot.art.npcName ?? selectedPeopleSlot.art.npcId}
                        thumbnail={selectedPeopleSlot.art.classic}
                      />
                    ) : null}
                  </div>
                  {editMode && slotDraft ? (
                    <PeopleSlotEditorForm
                      draft={slotDraft}
                      peopleOverview={peopleOverview}
                      dirty={slotDirty}
                      saving={slotSaving}
                      errorMessage={slotError}
                      statusMessage={slotStatus}
                      onChange={next => commitSlotChange(next)}
                      onSave={() => void saveSlotEdit()}
                      onRevert={revertSlotDraft}
                    />
                  ) : (
                    <div class="actions" style={{ marginTop: '6px' }}>
                      <button type="button" onClick={() => setEditMode(true)}>
                        Edit slot position & binding
                      </button>
                    </div>
                  )}
                </>
              ) : null}

              {selectedStaticNpc ? (
                <div class="usage-pills" style={{ marginTop: '6px' }}>
                  <span class="chip">
                    {selectedStaticNpc.billboardArchive}.{selectedStaticNpc.billboardRecord} ({selectedStaticNpc.gender})
                  </span>
                  {selectedStaticNpc.npcId ? (
                    <ArtDataChip
                      href={subjectUrl(selectedStaticNpc.npcId)}
                      id={selectedStaticNpc.npcId}
                      label={selectedStaticNpc.npcName ?? selectedStaticNpc.npcId}
                      thumbnail={selectedStaticNpc.classic ?? null}
                    />
                  ) : null}
                </div>
              ) : null}
            </div>
          ) : (
            <p class="muted" style={{ marginTop: 0, fontSize: '0.88rem' }}>
              Click any mesh, door, person, or marker in 3D — or pick a row below — to inspect and jump to linked CMS
              records.
            </p>
          )}

          <div class="inspector-tabs" role="tablist">
            <button
              type="button"
              role="tab"
              aria-selected={tab === 'summary' ? 'true' : 'false'}
              class={tab === 'summary' ? 'active' : undefined}
              onClick={() => setTab('summary')}
            >
              Summary
            </button>
            {block.buildings?.length ? (
              <button
                type="button"
                role="tab"
                aria-selected={tab === 'buildings' ? 'true' : 'false'}
                class={tab === 'buildings' ? 'active' : undefined}
                onClick={() => setTab('buildings')}
              >
                Buildings ({block.buildings.length})
              </button>
            ) : null}
            <button
              type="button"
              role="tab"
              aria-selected={tab === 'people' ? 'true' : 'false'}
              class={tab === 'people' ? 'active' : undefined}
              onClick={() => setTab('people')}
            >
              People ({peopleCount})
            </button>
            <button
              type="button"
              role="tab"
              aria-selected={tab === 'models' ? 'true' : 'false'}
              class={tab === 'models' ? 'active' : undefined}
              onClick={() => setTab('models')}
            >
              Models ({block.models.length})
            </button>
            <button
              type="button"
              role="tab"
              aria-selected={tab === 'doors' ? 'true' : 'false'}
              class={tab === 'doors' ? 'active' : undefined}
              onClick={() => setTab('doors')}
            >
              Doors ({doorCount})
            </button>
            <button
              type="button"
              role="tab"
              aria-selected={tab === 'objects' ? 'true' : 'false'}
              class={tab === 'objects' ? 'active' : undefined}
              onClick={() => setTab('objects')}
            >
              Objects ({objectCount})
            </button>
            {isExterior ? (
              <button
                type="button"
                role="tab"
                aria-selected={tab === 'variants' ? 'true' : 'false'}
                class={tab === 'variants' ? 'active' : undefined}
                onClick={openVariantsTab}
              >
                Variants ({variantsData?.variants.length ?? 0})
              </button>
            ) : null}
          </div>

          <div class="inspector-scroll">
            {tab === 'summary' ? (
              <>
                <FactList
                  facts={{
                    Kind: KIND_LABELS[block.kind],
                    'Source key': block.sourceKey,
                    Models: block.models.length,
                    'Doors / transitions': doorCount,
                    'People slots (pack)': peopleSlots.length,
                    'Static NPCs (source)': block.staticNpcs?.length ?? 0,
                    Billboards: block.billboards?.length ?? 0,
                    Lights: block.lights?.length ?? 0,
                    ...(block.ground?.length ? { 'Ground tiles': block.ground.length } : {}),
                    ...(block.interiorBuilding
                      ? {
                          'Building type':
                            block.interiorBuilding.buildingTypeName ?? `Type ${block.interiorBuilding.buildingType}`,
                          Quality: block.interiorBuilding.quality,
                          'Faction ID': block.interiorBuilding.factionId,
                        }
                      : {}),
                    ...(typeof block.waterLevel === 'number' ? { 'Water level': `${block.waterLevel.toFixed(2)} m` } : {}),
                    ...(block.ambientZone ? { 'Ambient zone': block.ambientZone } : {}),
                    ...(block.bounds
                      ? {
                          'Bounds min': fmtVec(block.bounds.minimum),
                          'Bounds max': fmtVec(block.bounds.maximum),
                        }
                      : {}),
                  }}
                />
                {block.sourceWarnings?.length ? (
                  <div class="curation-warning" style={{ marginTop: '12px' }}>
                    <strong>Source warnings ({block.sourceWarnings.length})</strong>
                    <ul class="retire-notes">
                      {block.sourceWarnings.map((w, i) => (
                        <li key={i}>{w}</li>
                      ))}
                    </ul>
                  </div>
                ) : null}
              </>
            ) : null}

            {tab === 'buildings' ? (
              <ul class="inspector-list">
                {(block.buildings ?? []).map(b => (
                  <li key={b.index} class="inspector-row">
                    <div>
                      <strong>
                        #{b.index} · {b.buildingTypeName ?? `Type ${b.buildingType}`}
                      </strong>
                      <div class="muted">
                        Quality {b.quality} · Faction {b.factionId}
                      </div>
                    </div>
                    {b.interiorKey ? (
                      <a class="button" href={worldBlockUrl(b.interiorKey)}>
                        Interior →
                      </a>
                    ) : (
                      <span class="muted">Exterior only</span>
                    )}
                  </li>
                ))}
              </ul>
            ) : null}

            {tab === 'people' ? (
              peopleSlots.length === 0 && !(block.staticNpcs?.length) ? (
                <Empty>No people slots or static NPCs in this block.</Empty>
              ) : (
                <ul class="inspector-list">
                  {peopleSlots.map((slot, idx) => {
                    const pickKey = `peopleSlot:${slot.slotId}:${idx}`;
                    return (
                      <li
                        key={pickKey}
                        class={`inspector-row${selectedPick?.pickKey === pickKey ? ' active' : ''}`}
                      >
                        <button
                          type="button"
                          class="inspector-focus-btn"
                          onClick={() =>
                            selectAndFocus({
                              pickKey,
                              kind: 'peopleSlot',
                              id: slot.slotId,
                              label: slot.castName ?? slot.spawner ?? slot.slotId,
                              position: slot.position,
                              buildingIndex: slot.buildingIndex,
                              index: idx,
                            })
                          }
                        >
                          <strong>{slot.castName ?? slot.spawner ?? slot.slotId}</strong>
                          <div class="muted">
                            <code>{slot.slotId}</code> · {slot.interaction}
                          </div>
                        </button>
                        <div class="actions">
                          {slot.castId ? <a href={castUrl(slot.castId)}>Cast</a> : null}
                          {slot.spawner ? <a href={spawnerUrl(slot.spawner)}>Spawner</a> : null}
                          {slot.art?.npcId ? <a href={subjectUrl(slot.art.npcId)}>{slot.art.npcId}</a> : null}
                        </div>
                      </li>
                    );
                  })}
                  {(block.staticNpcs ?? []).map((npc, idx) => {
                    const pickKey = `staticNpc:${npc.id}`;
                    return (
                      <li
                        key={pickKey}
                        class={`inspector-row${selectedPick?.pickKey === pickKey ? ' active' : ''}`}
                      >
                        <button
                          type="button"
                          class="inspector-focus-btn"
                          onClick={() =>
                            selectAndFocus({
                              pickKey,
                              kind: 'staticNpc',
                              id: npc.id,
                              label: npc.npcName ?? npc.id,
                              position: [npc.position.x, npc.position.y, npc.position.z],
                              index: idx,
                            })
                          }
                        >
                          <strong>{npc.npcName ?? `Static NPC ${npc.id}`}</strong>
                          <div class="muted">
                            {npc.billboardArchive}.{npc.billboardRecord} · {npc.gender} · Faction {npc.factionId}
                          </div>
                        </button>
                        {npc.npcId ? <a href={subjectUrl(npc.npcId)}>{npc.npcId}</a> : null}
                      </li>
                    );
                  })}
                </ul>
              )
            ) : null}

            {tab === 'models' ? (
              <>
                <div style={{ marginBottom: '8px' }}>
                  <input
                    type="search"
                    value={modelFilter}
                    placeholder="Filter by ARCH3D ID or building…"
                    style={{ width: '100%' }}
                    onInput={event => setModelFilter((event.currentTarget as HTMLInputElement).value)}
                  />
                </div>
                <ul class="inspector-list">
                  {filteredModels.slice(0, 200).map((m, idx) => {
                    const pickKey = `model:${m.id}`;
                    const asset = modelLinks[m.modelId];
                    return (
                      <li
                        key={m.id}
                        class={`inspector-row${selectedPick?.pickKey === pickKey ? ' active' : ''}`}
                      >
                        <button
                          type="button"
                          class="inspector-focus-btn"
                          onClick={() =>
                            selectAndFocus({
                              pickKey,
                              kind: 'model',
                              id: m.id,
                              label: `Model ${m.modelId}`,
                              position: [m.position.x, m.position.y, m.position.z],
                              buildingIndex: m.buildingIndex,
                              modelId: m.modelId,
                              index: idx,
                            })
                          }
                        >
                          <strong>ARCH3D {m.modelId}</strong>
                          {m.buildingIndex !== null ? <small class="muted"> · Bldg #{m.buildingIndex}</small> : null}
                          <div class="muted">
                            {fmtVec(m.position)}
                            {m.unresolvedReason ? ` · ${m.unresolvedReason}` : ''}
                          </div>
                        </button>
                        {asset ? <a href={assetUrl(asset.id)}>CMS</a> : null}
                      </li>
                    );
                  })}
                </ul>
                {filteredModels.length > 200 ? (
                  <p class="muted" style={{ fontSize: '0.82rem' }}>
                    Showing first 200 of {filteredModels.length} models. Use filter above to narrow.
                  </p>
                ) : null}
              </>
            ) : null}

            {tab === 'doors' ? (
              doorCount === 0 ? (
                <Empty>No doors, transitions, or gates in this block.</Empty>
              ) : (
                <ul class="inspector-list">
                  {(block.doors ?? []).map((d, idx) => {
                    const pickKey = `door:${d.id}`;
                    const targetInterior =
                      d.buildingIndex !== null && d.buildingIndex !== undefined
                        ? detail.interiorKeys[String(d.buildingIndex)]
                        : null;
                    return (
                      <li
                        key={pickKey}
                        class={`inspector-row${selectedPick?.pickKey === pickKey ? ' active' : ''}`}
                      >
                        <button
                          type="button"
                          class="inspector-focus-btn"
                          onClick={() =>
                            selectAndFocus({
                              pickKey,
                              kind: 'door',
                              id: d.id,
                              label: `Door ${d.id}`,
                              position: [d.position.x, d.position.y, d.position.z],
                              buildingIndex: d.buildingIndex,
                              index: idx,
                            })
                          }
                        >
                          <strong>{d.id}</strong>
                          <div class="muted">
                            {d.kind} · lock {d.startingLockValue}
                            {d.buildingIndex !== null && d.buildingIndex !== undefined
                              ? ` · Bldg #${d.buildingIndex}`
                              : ''}
                          </div>
                        </button>
                        {targetInterior ? <a href={worldBlockUrl(targetInterior)}>Interior →</a> : null}
                      </li>
                    );
                  })}
                  {(block.transitionDoors ?? []).map((td, idx) => {
                    const pickKey = `transitionDoor:${td.id}`;
                    return (
                      <li
                        key={pickKey}
                        class={`inspector-row${selectedPick?.pickKey === pickKey ? ' active' : ''}`}
                      >
                        <button
                          type="button"
                          class="inspector-focus-btn"
                          onClick={() =>
                            selectAndFocus({
                              pickKey,
                              kind: 'transitionDoor',
                              id: td.id,
                              label: `${td.kind} (${td.id})`,
                              position: [td.position.x, td.position.y, td.position.z],
                              index: idx,
                            })
                          }
                        >
                          <strong>{td.kind}</strong>
                          <div class="muted">
                            <code>{td.id}</code> · {fmtVec(td.position)}
                          </div>
                        </button>
                        {td.kind === 'buildingExit' && parentExteriorKey ? (
                          <a href={worldBlockUrl(parentExteriorKey)}>Exterior →</a>
                        ) : null}
                      </li>
                    );
                  })}
                  {(block.gates ?? []).map((g, idx) => {
                    const pickKey = `gate:${g.id}`;
                    return (
                      <li
                        key={pickKey}
                        class={`inspector-row${selectedPick?.pickKey === pickKey ? ' active' : ''}`}
                      >
                        <button
                          type="button"
                          class="inspector-focus-btn"
                          onClick={() =>
                            selectAndFocus({
                              pickKey,
                              kind: 'gate',
                              id: g.id,
                              label: `City gate ${g.id}`,
                              position: [g.position.x, g.position.y, g.position.z],
                              index: idx,
                            })
                          }
                        >
                          <strong>City gate {g.id}</strong>
                          <div class="muted">{fmtVec(g.position)}</div>
                        </button>
                      </li>
                    );
                  })}
                </ul>
              )
            ) : null}

            {tab === 'objects' ? (
              objectCount === 0 ? (
                <Empty>No billboards, lights, containers, or markers in this block.</Empty>
              ) : (
                <ul class="inspector-list">
                  {(block.actions ?? []).map((act, idx) => {
                    const aid = act.id ?? act.actionId ?? `action-${idx}`;
                    const pickKey = `action:${aid}`;
                    const pos: [number, number, number] = act.position
                      ? [act.position.x, act.position.y, act.position.z]
                      : [0, 0, 0];
                    return (
                      <li key={pickKey} class={`inspector-row${selectedPick?.pickKey === pickKey ? ' active' : ''}`}>
                        <button
                          type="button"
                          class="inspector-focus-btn"
                          onClick={() =>
                            selectAndFocus({
                              pickKey,
                              kind: 'action',
                              id: aid,
                              label: `Action ${aid} (flag ${act.actionFlag ?? 0})`,
                              position: pos,
                              index: idx,
                            })
                          }
                        >
                          <strong>Action: {aid}</strong>
                          <div class="muted">
                            flag {act.actionFlag ?? 0} · trigger {act.triggerFlag ?? 0}
                            {act.nextActionId ? ` → ${act.nextActionId}` : ''}
                          </div>
                        </button>
                      </li>
                    );
                  })}
                  {(block.randomEnemies ?? []).map((e, idx) => {
                    const pickKey = `randomEnemy:${e.id}`;
                    return (
                      <li key={pickKey} class={`inspector-row${selectedPick?.pickKey === pickKey ? ' active' : ''}`}>
                        <button
                          type="button"
                          class="inspector-focus-btn"
                          onClick={() =>
                            selectAndFocus({
                              pickKey,
                              kind: 'randomEnemy',
                              id: e.id,
                              label: `Enemy spawn slot ${e.encounterSlot}`,
                              position: [e.position.x, e.position.y + 0.7, e.position.z],
                              index: idx,
                            })
                          }
                        >
                          <strong>Random enemy (slot {e.encounterSlot})</strong>
                          <div class="muted">
                            {e.passive ? 'passive · ' : ''}
                            {e.underwater ? 'underwater · ' : ''}
                            {fmtVec(e.position)}
                          </div>
                        </button>
                      </li>
                    );
                  })}
                  {(block.questMarkers ?? []).map((q, idx) => {
                    const pickKey = `questMarker:${q.id}`;
                    return (
                      <li key={pickKey} class={`inspector-row${selectedPick?.pickKey === pickKey ? ' active' : ''}`}>
                        <button
                          type="button"
                          class="inspector-focus-btn"
                          onClick={() =>
                            selectAndFocus({
                              pickKey,
                              kind: 'questMarker',
                              id: q.id,
                              label: `Quest marker (${q.kind ?? 'quest'})`,
                              position: [q.position.x, q.position.y + 0.5, q.position.z],
                              index: idx,
                            })
                          }
                        >
                          <strong>Quest marker: {q.kind ?? q.id}</strong>
                          <div class="muted">{fmtVec(q.position)}</div>
                        </button>
                      </li>
                    );
                  })}
                  {(block.propertyContainers ?? []).map((c, idx) => {
                    const pickKey = `container:${c.id}`;
                    return (
                      <li key={pickKey} class={`inspector-row${selectedPick?.pickKey === pickKey ? ' active' : ''}`}>
                        <button
                          type="button"
                          class="inspector-focus-btn"
                          onClick={() =>
                            selectAndFocus({
                              pickKey,
                              kind: 'container',
                              id: c.id,
                              label: `Container ${c.id}`,
                              position: [c.position.x, c.position.y, c.position.z],
                              index: idx,
                            })
                          }
                        >
                          <strong>Container {c.id}</strong>
                          <div class="muted">{(c.itemGroups ?? []).join(', ') || fmtVec(c.position)}</div>
                        </button>
                      </li>
                    );
                  })}
                  {(block.lights ?? []).map((l, idx) => {
                    const pickKey = `light:${l.id}`;
                    return (
                      <li key={pickKey} class={`inspector-row${selectedPick?.pickKey === pickKey ? ' active' : ''}`}>
                        <button
                          type="button"
                          class="inspector-focus-btn"
                          onClick={() =>
                            selectAndFocus({
                              pickKey,
                              kind: 'light',
                              id: l.id,
                              label: `Light ${l.id}`,
                              position: [l.position.x, l.position.y, l.position.z],
                              index: idx,
                            })
                          }
                        >
                          <strong>Light {l.id}</strong>
                          <div class="muted">
                            range {l.range}m · intensity {l.intensity}
                          </div>
                        </button>
                      </li>
                    );
                  })}
                  {(block.billboards ?? []).slice(0, 100).map((bb, idx) => {
                    const pickKey = `billboard:${bb.id}`;
                    return (
                      <li key={pickKey} class={`inspector-row${selectedPick?.pickKey === pickKey ? ' active' : ''}`}>
                        <button
                          type="button"
                          class="inspector-focus-btn"
                          onClick={() =>
                            selectAndFocus({
                              pickKey,
                              kind: 'billboard',
                              id: bb.id,
                              label: `Billboard ${bb.spriteResourceId}`,
                              position: [bb.position.x, bb.position.y, bb.position.z],
                              index: idx,
                            })
                          }
                        >
                          <strong>{bb.spriteResourceId}</strong>
                          <div class="muted">
                            {bb.lamp ? 'lamp · ' : ''}
                            {fmtVec(bb.position)}
                          </div>
                        </button>
                      </li>
                    );
                  })}
                </ul>
              )
            ) : null}
            {tab === 'variants' && isExterior ? (
              <VariantEditorPanel
                detail={detail}
                variantsData={variantsData}
                draft={variantDraft}
                previewEnabled={variantPreviewEnabled}
                showRemovedGhosts={showRemovedGhosts}
                dirty={variantDirty}
                saving={variantSaving}
                baking={variantBaking}
                statusMessage={variantStatus}
                errorMessage={variantError}
                selectedAddIndex={selectedVariantAddIndex}
                onSelectSavedVariant={loadSavedVariant}
                onCreateNewDraft={createNewVariantDraft}
                onTogglePreview={setVariantPreviewEnabled}
                onToggleGhosts={setShowRemovedGhosts}
                onRestoreRemoved={id => toggleHideMiscModel(id)}
                onOpenModelPicker={() => setModelPickerOpen(true)}
                onSelectAddedModel={idx => {
                  const item = variantDraft?.add[idx];
                  if (!item) return;
                  setEditMode(true);
                  selectAndFocus({
                    pickKey: `variantAdd:${idx}`,
                    kind: 'variantAdd',
                    id: `variantAdd:${idx}`,
                    label: `Added ARCH3D ${item.model} (#${idx + 1})`,
                    position: [item.position.x, item.position.y, item.position.z],
                    modelId: item.model,
                    index: idx,
                  });
                }}
                onUpdateAddedModel={updateVariantAddedModel}
                onDuplicateAddedModel={duplicateVariantAddedModel}
                onDeleteAddedModel={deleteVariantAddedModel}
                onSave={() => void saveVariantRecipe()}
                onBake={() => void bakeVariantPack()}
                onDeleteVariant={() => void deleteVariantRecipe()}
              />
            ) : null}
          </div>
        </aside>
      </div>

      <ModelPickerModal
        open={modelPickerOpen}
        onClose={() => setModelPickerOpen(false)}
        onPick={addModelToVariant}
      />
    </>
  );
}
