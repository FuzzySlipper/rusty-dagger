import { useEffect, useState } from 'preact/hooks';
import { apiRequest, isAbortError } from '../api';
import type {
  Arch3dModelOption,
  PeoplePackOverview,
  WorldBlockDetailResponse,
  WorldBlockPeopleSlot,
  WorldBlockVariantModel,
  WorldBlockVariantRecipe,
  WorldVariantsResponse,
} from '../types';

export interface VariantDraft {
  packId: string;
  block: string;
  variant: string;
  originalVariant: string | null;
  isNew: boolean;
  remove: string[];
  add: WorldBlockVariantModel[];
}

export interface PeopleSlotDraft {
  pickKey: string;
  kind: WorldBlockPeopleSlot['kind'];
  block: string;
  buildingIndex: number | null;
  slotId: string;
  position: [number, number, number];
  bindingKind: 'spawner' | 'cast';
  spawner: string;
  castId: string;
  interaction: string;
  deriveRegionalFaction: boolean;
  factionId: string;
}

export function makeSlotDraft(slot: WorldBlockPeopleSlot, pickKey: string): PeopleSlotDraft {
  const bindingKind: 'spawner' | 'cast' = slot.castId ? 'cast' : 'spawner';
  return {
    pickKey,
    kind: slot.kind,
    block: slot.block,
    buildingIndex: slot.buildingIndex,
    slotId: slot.slotId,
    position: [slot.position[0], slot.position[1], slot.position[2]],
    bindingKind,
    spawner: slot.spawner ?? '',
    castId: slot.castId ?? '',
    interaction: slot.interaction || 'full-dialogue',
    deriveRegionalFaction: Boolean(slot.deriveRegionalFaction),
    factionId: slot.factionId !== null && slot.factionId !== undefined ? String(slot.factionId) : '',
  };
}

export function ModelPickerModal({
  open,
  onClose,
  onPick,
}: {
  open: boolean;
  onClose: () => void;
  onPick: (modelId: string) => void;
}) {
  const [q, setQ] = useState('');
  const [items, setItems] = useState<Arch3dModelOption[]>([]);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    if (!open) return;
    const controller = new AbortController();
    const timer = window.setTimeout(() => {
      setLoading(true);
      const params = new URLSearchParams();
      if (q.trim()) params.set('q', q.trim());
      params.set('limit', '50');
      apiRequest<{ total: number; items: Arch3dModelOption[] }>(`/api/world-blocks/models?${params.toString()}`, {
        signal: controller.signal,
      })
        .then(res => {
          setItems(res.items);
          setTotal(res.total);
        })
        .catch(err => {
          if (!isAbortError(err)) {
            setItems([]);
            setTotal(0);
          }
        })
        .finally(() => {
          if (!controller.signal.aborted) setLoading(false);
        });
    }, 120);
    return () => {
      window.clearTimeout(timer);
      controller.abort();
    };
  }, [open, q]);

  if (!open) return null;

  return (
    <div class="modal-backdrop" role="dialog" aria-modal="true" aria-label="Add ARCH3D model to variant">
      <div class="modal-card panel">
        <div class="section-heading" style={{ marginBottom: '8px' }}>
          <strong>Add ARCH3D model</strong>
          <button type="button" onClick={onClose}>
            Close
          </button>
        </div>
        <div style={{ marginBottom: '10px' }}>
          <input
            type="search"
            value={q}
            placeholder="Search ARCH3D ID, prop name, or category (e.g. 41214, barrel, stall)…"
            style={{ width: '100%' }}
            autoFocus
            onInput={event => setQ((event.currentTarget as HTMLInputElement).value)}
          />
        </div>
        <div class="muted" style={{ fontSize: '0.8rem', marginBottom: '8px' }}>
          {loading ? 'Searching models…' : `${total.toLocaleString()} matching ARCH3D meshes`}
        </div>
        <ul class="inspector-list model-picker-list">
          {items.map(item => (
            <li key={item.modelId} class="inspector-row">
              <div>
                <strong>ARCH3D {item.modelId}</strong>
                {item.name ? <span> · {item.name}</span> : null}
                <div class="muted" style={{ fontSize: '0.78rem' }}>
                  <code>{item.meshArtifactId}</code>
                  {item.category ? ` · ${item.category}` : ''}
                  {item.hasRemodel ? ' · Refreshed remodel' : ''}
                </div>
              </div>
              <button
                type="button"
                class="primary"
                onClick={() => {
                  onPick(item.modelId);
                  onClose();
                }}
              >
                Add
              </button>
            </li>
          ))}
        </ul>
      </div>
    </div>
  );
}

export function VariantEditorPanel({
  detail,
  variantsData,
  draft,
  previewEnabled,
  showRemovedGhosts,
  dirty,
  saving,
  baking,
  statusMessage,
  errorMessage,
  selectedAddIndex,
  onSelectSavedVariant,
  onCreateNewDraft,
  onTogglePreview,
  onToggleGhosts,
  onRestoreRemoved,
  onOpenModelPicker,
  onSelectAddedModel,
  onUpdateAddedModel,
  onDuplicateAddedModel,
  onDeleteAddedModel,
  onSave,
  onBake,
  onDeleteVariant,
}: {
  detail: WorldBlockDetailResponse;
  variantsData: WorldVariantsResponse | null;
  draft: VariantDraft | null;
  previewEnabled: boolean;
  showRemovedGhosts: boolean;
  dirty: boolean;
  saving: boolean;
  baking: boolean;
  statusMessage: string | null;
  errorMessage: string | null;
  selectedAddIndex: number | null;
  onSelectSavedVariant: (recipe: WorldBlockVariantRecipe | null) => void;
  onCreateNewDraft: (packId: string, variantName: string) => void;
  onTogglePreview: (enabled: boolean) => void;
  onToggleGhosts: (show: boolean) => void;
  onRestoreRemoved: (placementId: string) => void;
  onOpenModelPicker: () => void;
  onSelectAddedModel: (index: number) => void;
  onUpdateAddedModel: (index: number, next: WorldBlockVariantModel) => void;
  onDuplicateAddedModel: (index: number) => void;
  onDeleteAddedModel: (index: number) => void;
  onSave: () => void;
  onBake: () => void;
  onDeleteVariant: () => void;
}) {
  const [creating, setCreating] = useState(false);
  const [newPackId, setNewPackId] = useState(variantsData?.packs[0]?.id ?? 'daggerfall.quests');
  const [newVariantName, setNewVariantName] = useState('');

  useEffect(() => {
    if (variantsData?.packs.length && !variantsData.packs.some(p => p.id === newPackId)) {
      setNewPackId(variantsData.packs[0].id);
    }
  }, [variantsData]);

  const savedVariants = variantsData?.variants ?? [];
  const savedMatch = draft
    ? savedVariants.find(
        v =>
          v.packId === draft.packId &&
          v.variant === (draft.originalVariant ?? draft.variant),
      ) ?? null
    : null;

  const modelById = new Map(detail.block.models.map(m => [m.id, m]));
  const selectedKey = draft ? `${draft.packId}::${draft.originalVariant ?? draft.variant}` : '';

  function submitNew(event: Event) {
    event.preventDefault();
    const cleaned = newVariantName.trim();
    if (!cleaned) return;
    onCreateNewDraft(newPackId, cleaned);
    setNewVariantName('');
    setCreating(false);
  }

  return (
    <div class="variant-editor">
      <div class="variant-header-row">
        <select
          aria-label="Block variant"
          value={selectedKey}
          onChange={event => {
            const val = (event.currentTarget as HTMLSelectElement).value;
            if (!val) {
              onSelectSavedVariant(null);
              return;
            }
            const [pid, vname] = val.split('::');
            const found = savedVariants.find(v => v.packId === pid && v.variant === vname) ?? null;
            onSelectSavedVariant(found);
          }}
        >
          <option value="">Base block (no variant)</option>
          {savedVariants.map(v => (
            <option key={`${v.packId}::${v.variant}`} value={`${v.packId}::${v.variant}`}>
              {v.variant} ({v.packId})
            </option>
          ))}
          {draft?.isNew ? (
            <option value={selectedKey}>
              {draft.variant} ({draft.packId}, unsaved draft)
            </option>
          ) : null}
        </select>
        <button type="button" onClick={() => setCreating(prev => !prev)}>
          {creating ? 'Cancel' : '+ New'}
        </button>
      </div>

      {creating ? (
        <form class="variant-new-form" onSubmit={submitNew}>
          <label>
            Content pack
            <select
              value={newPackId}
              onChange={event => setNewPackId((event.currentTarget as HTMLSelectElement).value)}
            >
              {(variantsData?.packs ?? [{ id: 'daggerfall.quests', variantCount: 0 }]).map(p => (
                <option key={p.id} value={p.id}>
                  {p.id}
                </option>
              ))}
            </select>
          </label>
          <label>
            Variant name
            <input
              type="text"
              value={newVariantName}
              placeholder="e.g. closed, festival, ruined"
              onInput={event => setNewVariantName((event.currentTarget as HTMLInputElement).value)}
            />
          </label>
          <button type="submit" class="primary" disabled={!newVariantName.trim()}>
            Create draft
          </button>
        </form>
      ) : null}

      {errorMessage ? (
        <div class="curation-warning" role="alert" style={{ marginTop: '8px' }}>
          {errorMessage}
        </div>
      ) : null}
      {statusMessage ? (
        <div class="variant-status-ok" role="status" style={{ marginTop: '8px' }}>
          {statusMessage}
        </div>
      ) : null}

      {!draft ? (
        <p class="muted" style={{ fontSize: '0.84rem', marginTop: '10px' }}>
          {savedVariants.length > 0
            ? `Select one of the ${savedVariants.length} saved variant(s) above or create a new variant to hide block-own misc models and place ARCH3D props in 3D.`
            : 'No block variants authored for this RMB exterior yet. Click "+ New" to author a variant recipe.'}
        </p>
      ) : (
        <>
          <div class="variant-meta-strip">
            <span class="chip">{draft.packId}</span>
            {savedMatch?.bakedExists ? (
              <span class="chip" title={savedMatch.bakedPath}>
                Baked{savedMatch.baseDigest ? ` · ${savedMatch.baseDigest.slice(0, 8)}` : ''}
              </span>
            ) : (
              <span class="chip">Not baked</span>
            )}
            {dirty ? <span class="chip chip-dirty">Unsaved</span> : null}
          </div>

          <div class="variant-toggles">
            <label>
              <input
                type="checkbox"
                checked={previewEnabled}
                onChange={event => onTogglePreview((event.currentTarget as HTMLInputElement).checked)}
              />{' '}
              Preview in 3D
            </label>
            <label>
              <input
                type="checkbox"
                checked={showRemovedGhosts}
                onChange={event => onToggleGhosts((event.currentTarget as HTMLInputElement).checked)}
              />{' '}
              Show removed ghosts
            </label>
          </div>

          <div class="actions" style={{ marginTop: '8px', flexWrap: 'wrap' }}>
            <button
              type="button"
              class="primary"
              disabled={saving || (draft.remove.length === 0 && draft.add.length === 0)}
              onClick={onSave}
            >
              {saving ? 'Saving…' : 'Save recipe'}
            </button>
            <button
              type="button"
              disabled={baking || dirty || draft.isNew || !variantsData?.arena2Available}
              title={
                dirty || draft.isNew
                  ? 'Save the recipe before baking'
                  : !variantsData?.arena2Available
                    ? 'local/inputs/arena2 not available'
                    : `Run world-variant-bake on ${draft.packId}`
              }
              onClick={onBake}
            >
              {baking ? 'Baking…' : 'Bake variant'}
            </button>
            {!draft.isNew ? (
              <button type="button" disabled={saving || baking} onClick={onDeleteVariant}>
                Delete
              </button>
            ) : null}
          </div>

          <div class="variant-section">
            <div class="section-heading" style={{ marginBottom: '6px' }}>
              <strong>Removed misc models ({draft.remove.length})</strong>
            </div>
            {draft.remove.length === 0 ? (
              <p class="muted" style={{ fontSize: '0.8rem', margin: '4px 0' }}>
                Click any block-own misc model (no building index) in 3D or the Models tab to hide it in this variant.
              </p>
            ) : (
              <ul class="inspector-list">
                {draft.remove.map(id => {
                  const baseModel = modelById.get(id);
                  return (
                    <li key={id} class="inspector-row">
                      <div style={{ minWidth: 0 }}>
                        <code>{id}</code>
                        {baseModel ? <span class="muted"> · ARCH3D {baseModel.modelId}</span> : null}
                      </div>
                      <button type="button" onClick={() => onRestoreRemoved(id)}>
                        Restore
                      </button>
                    </li>
                  );
                })}
              </ul>
            )}
          </div>

          <div class="variant-section">
            <div class="section-heading" style={{ marginBottom: '6px' }}>
              <strong>Added models ({draft.add.length})</strong>
              <button type="button" class="primary" onClick={onOpenModelPicker}>
                + Add model
              </button>
            </div>
            {draft.add.length === 0 ? (
              <p class="muted" style={{ fontSize: '0.8rem', margin: '4px 0' }}>
                No added models yet. Click "+ Add model" to place an ARCH3D prop and drag/rotate it in 3D.
              </p>
            ) : (
              <ul class="inspector-list">
                {draft.add.map((item, idx) => {
                  const isSel = selectedAddIndex === idx;
                  return (
                    <li key={`${idx}-${item.model}`} class={`inspector-row variant-add-row${isSel ? ' active' : ''}`}>
                      <div style={{ width: '100%' }}>
                        <div class="variant-add-head">
                          <button
                            type="button"
                            class="inspector-focus-btn"
                            onClick={() => onSelectAddedModel(idx)}
                          >
                            <strong>
                              #{idx + 1} · ARCH3D {item.model}
                            </strong>
                            <div class="muted" style={{ fontSize: '0.78rem' }}>
                              ({item.position.x.toFixed(2)}, {item.position.y.toFixed(2)}, {item.position.z.toFixed(2)}) ·{' '}
                              {item.yawDegrees.toFixed(1)}°
                            </div>
                          </button>
                          <div class="actions">
                            <button type="button" onClick={() => onDuplicateAddedModel(idx)} title="Duplicate">
                              Dup
                            </button>
                            <button type="button" onClick={() => onDeleteAddedModel(idx)} title="Remove">
                              ×
                            </button>
                          </div>
                        </div>
                        {isSel ? (
                          <div class="numeric-grid">
                            <label>
                              X
                              <input
                                type="number"
                                step="0.25"
                                value={String(item.position.x)}
                                onChange={event => {
                                  const v = Number((event.currentTarget as HTMLInputElement).value);
                                  if (Number.isFinite(v)) {
                                    onUpdateAddedModel(idx, {
                                      ...item,
                                      position: { ...item.position, x: v },
                                    });
                                  }
                                }}
                              />
                            </label>
                            <label>
                              Y
                              <input
                                type="number"
                                step="0.25"
                                value={String(item.position.y)}
                                onChange={event => {
                                  const v = Number((event.currentTarget as HTMLInputElement).value);
                                  if (Number.isFinite(v)) {
                                    onUpdateAddedModel(idx, {
                                      ...item,
                                      position: { ...item.position, y: v },
                                    });
                                  }
                                }}
                              />
                            </label>
                            <label>
                              Z
                              <input
                                type="number"
                                step="0.25"
                                value={String(item.position.z)}
                                onChange={event => {
                                  const v = Number((event.currentTarget as HTMLInputElement).value);
                                  if (Number.isFinite(v)) {
                                    onUpdateAddedModel(idx, {
                                      ...item,
                                      position: { ...item.position, z: v },
                                    });
                                  }
                                }}
                              />
                            </label>
                            <label>
                              Yaw°
                              <input
                                type="number"
                                step="15"
                                value={String(item.yawDegrees)}
                                onChange={event => {
                                  const v = Number((event.currentTarget as HTMLInputElement).value);
                                  if (Number.isFinite(v)) {
                                    onUpdateAddedModel(idx, { ...item, yawDegrees: v });
                                  }
                                }}
                              />
                            </label>
                          </div>
                        ) : null}
                      </div>
                    </li>
                  );
                })}
              </ul>
            )}
          </div>
        </>
      )}
    </div>
  );
}

export function PeopleSlotEditorForm({
  draft,
  peopleOverview,
  dirty,
  saving,
  errorMessage,
  statusMessage,
  onChange,
  onSave,
  onRevert,
}: {
  draft: PeopleSlotDraft;
  peopleOverview: PeoplePackOverview | null;
  dirty: boolean;
  saving: boolean;
  errorMessage: string | null;
  statusMessage: string | null;
  onChange: (next: PeopleSlotDraft) => void;
  onSave: () => void;
  onRevert: () => void;
}) {
  const spawners = peopleOverview?.spawners ?? [];
  const cast = peopleOverview?.cast ?? [];
  const interactions = peopleOverview?.enums.interactions ?? ['none', 'ambient-bark', 'full-dialogue'];

  return (
    <div class="slot-editor-form">
      <div class="section-heading" style={{ marginBottom: '6px' }}>
        <strong>Edit people slot ({draft.slotId})</strong>
        {dirty ? <span class="chip chip-dirty">Unsaved</span> : null}
      </div>

      <div class="numeric-grid numeric-grid-3">
        {(['X', 'Y', 'Z'] as const).map((axis, idx) => (
          <label key={axis}>
            {axis}
            <input
              type="number"
              step="0.25"
              aria-label={`Slot position ${axis}`}
              value={String(draft.position[idx])}
              onChange={event => {
                const v = Number((event.currentTarget as HTMLInputElement).value);
                if (!Number.isFinite(v)) return;
                const pos: [number, number, number] = [...draft.position];
                pos[idx] = v;
                onChange({ ...draft, position: pos });
              }}
            />
          </label>
        ))}
      </div>

      <div class="slot-editor-fields">
        <div class="art-mode-toggle" role="group" aria-label="Slot binding kind">
          <button
            type="button"
            class={draft.bindingKind === 'spawner' ? 'active' : undefined}
            onClick={() =>
              onChange({
                ...draft,
                bindingKind: 'spawner',
                spawner: draft.spawner || spawners[0]?.id || '',
              })
            }
          >
            Spawner
          </button>
          <button
            type="button"
            class={draft.bindingKind === 'cast' ? 'active' : undefined}
            onClick={() =>
              onChange({
                ...draft,
                bindingKind: 'cast',
                castId: draft.castId || cast[0]?.id || '',
              })
            }
          >
            Cast individual
          </button>
        </div>

        {draft.bindingKind === 'spawner' ? (
          <label>
            Spawner
            <select
              aria-label="Slot spawner"
              value={draft.spawner}
              onChange={event => onChange({ ...draft, spawner: (event.currentTarget as HTMLSelectElement).value })}
            >
              {spawners.map(sp => (
                <option key={sp.id} value={sp.id}>
                  {sp.id} ({sp.role})
                </option>
              ))}
              {draft.spawner && !spawners.some(sp => sp.id === draft.spawner) ? (
                <option value={draft.spawner}>{draft.spawner}</option>
              ) : null}
            </select>
          </label>
        ) : (
          <label>
            Cast individual
            <select
              aria-label="Slot cast individual"
              value={draft.castId}
              onChange={event => onChange({ ...draft, castId: (event.currentTarget as HTMLSelectElement).value })}
            >
              {cast.map(c => (
                <option key={c.id} value={c.id}>
                  {c.name} ({c.id})
                </option>
              ))}
              {draft.castId && !cast.some(c => c.id === draft.castId) ? (
                <option value={draft.castId}>{draft.castId}</option>
              ) : null}
            </select>
          </label>
        )}

        <label>
          Interaction
          <select
            aria-label="Slot interaction"
            value={draft.interaction}
            onChange={event => onChange({ ...draft, interaction: (event.currentTarget as HTMLSelectElement).value })}
          >
            {interactions.map(opt => (
              <option key={opt} value={opt}>
                {opt}
              </option>
            ))}
          </select>
        </label>

        <div class="slot-editor-inline">
          <label style={{ flex: 1 }}>
            Faction ID (optional)
            <input
              type="number"
              placeholder="none"
              aria-label="Slot faction ID"
              value={draft.factionId}
              onInput={event => onChange({ ...draft, factionId: (event.currentTarget as HTMLInputElement).value })}
            />
          </label>
          <label class="mark-filter" style={{ marginTop: '14px' }}>
            <input
              type="checkbox"
              checked={draft.deriveRegionalFaction}
              onChange={event =>
                onChange({ ...draft, deriveRegionalFaction: (event.currentTarget as HTMLInputElement).checked })
              }
            />{' '}
            Derive regional
          </label>
        </div>
      </div>

      {errorMessage ? (
        <div class="curation-warning" role="alert" style={{ marginTop: '6px' }}>
          {errorMessage}
        </div>
      ) : null}
      {statusMessage ? (
        <div class="variant-status-ok" role="status" style={{ marginTop: '6px' }}>
          {statusMessage}
        </div>
      ) : null}

      <div class="actions" style={{ marginTop: '8px' }}>
        <button type="button" class="primary" disabled={saving || !dirty} onClick={onSave}>
          {saving ? 'Saving…' : 'Save slot'}
        </button>
        <button type="button" disabled={saving || !dirty} onClick={onRevert}>
          Revert
        </button>
      </div>
    </div>
  );
}
