import { useEffect, useMemo, useState } from 'preact/hooks';
import { apiRequest, isAbortError } from '../api';
import { castUrl, encodeKey, spawnerUrl, subjectUrl } from '../router';
import { useApp } from '../state';
import type { CharacterSummary, PeopleCompareResponse, PeopleReferencesResponse } from '../types';
import { Modal } from './Modal';
import { ReviewBadge } from './ReviewControls';
import { Chip, Empty, SpritePreview } from './ui';

export function PeopleCompareModal({
  open,
  ids,
  initialKeeper,
  onClose,
  onMerged,
}: {
  open: boolean;
  ids: string[];
  initialKeeper?: string;
  onClose: () => void;
  onMerged: (keeperId: string) => void;
}) {
  const { refreshCatalog } = useApp();
  const [data, setData] = useState<PeopleCompareResponse | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [keeper, setKeeper] = useState<string>(initialKeeper || ids[0] || '');
  const [merging, setMerging] = useState(false);
  const [mergeError, setMergeError] = useState<string | null>(null);

  const idsKey = ids.join(',');

  useEffect(() => {
    if (!open || ids.length < 2) return;
    const controller = new AbortController();
    setLoading(true);
    setError(null);
    setMergeError(null);
    setKeeper(prev => (initialKeeper && ids.includes(initialKeeper) ? initialKeeper : ids.includes(prev) ? prev : ids[0]));
    apiRequest<PeopleCompareResponse>('/api/people/compare', {
      method: 'POST',
      body: { ids },
      signal: controller.signal,
    })
      .then(res => setData(res))
      .catch(err => {
        if (!isAbortError(err)) setError(err instanceof Error ? err.message : String(err));
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false);
      });
    return () => controller.abort();
  }, [open, idsKey, initialKeeper]);

  async function applyMerge() {
    if (!keeper || ids.length < 2) return;
    const members = ids.filter(id => id !== keeper);
    setMerging(true);
    setMergeError(null);
    try {
      await apiRequest('/api/people/merge', {
        method: 'POST',
        body: { keep: keeper, merge: members },
      });
      await refreshCatalog();
      onMerged(keeper);
    } catch (err) {
      setMergeError(err instanceof Error ? err.message : String(err));
    } finally {
      setMerging(false);
    }
  }

  return (
    <Modal
      open={open}
      fullScreen
      title="Compare & combine People records"
      subtitle={`Choose one keeper among ${ids.length} selected records; aliases, review notes, concepts, and usages fold into the keeper.`}
      onClose={onClose}
      headerActions={
        <button
          type="button"
          class="primary"
          disabled={merging || loading || !data || !keeper}
          onClick={() => void applyMerge()}
        >
          {merging ? 'Rebuilding chart & applying merge…' : `Keep ${keeper} & merge ${Math.max(0, ids.length - 1)}`}
        </button>
      }
    >
      <div class="curation-modal-body">
        {mergeError ? (
          <div class="validation-summary" role="alert">
            <strong>Merge failed:</strong> {mergeError}
          </div>
        ) : null}

        {data?.warnings.length ? (
          <div class="curation-warning" role="status">
            <strong>Heads up before merging:</strong>
            <ul>
              {data.warnings.map((w, idx) => (
                <li key={idx}>{w}</li>
              ))}
            </ul>
          </div>
        ) : null}

        {loading && !data ? <p>Loading comparison details…</p> : null}
        {error ? <Empty>{error}</Empty> : null}

        {data ? (
          <div class="compare-grid">
            {data.items.map(item => {
              const isKeeper = keeper === item.id;
              const conceptImg = item.conceptStage.front?.url ?? item.concept?.url ?? null;
              return (
                <article key={item.id} class={`panel compare-card${isKeeper ? ' is-keeper' : ''}`}>
                  <div class="section-heading">
                    <div>
                      <strong>{item.name}</strong> <code>{item.id}</code>
                      <div>
                        <small class="muted">{item.kind}</small>
                      </div>
                    </div>
                    <button
                      type="button"
                      class={isKeeper ? 'primary' : ''}
                      aria-pressed={isKeeper ? 'true' : 'false'}
                      disabled={merging}
                      onClick={() => setKeeper(item.id)}
                    >
                      {isKeeper ? 'Keeper' : 'Make keeper'}
                    </button>
                  </div>

                  <div class="compare-visuals">
                    <SpritePreview url={item.classic} alt={item.name} pixel className="compare-img" />
                    {conceptImg ? <SpritePreview url={conceptImg} alt={`${item.name} concept`} className="compare-img" /> : null}
                  </div>

                  <div class="cast-badges">
                    <ReviewBadge row={item.review} />
                    {item.conceptStage.front ? <Chip state="approved" label="Front concept" /> : null}
                    {item.conceptStage.back ? <Chip state="approved" label="Back concept" /> : null}
                    {item.final ? <Chip state="approved" label="Final asset" /> : null}
                    {item.pipelineIds.length ? <Chip state="empty" label={`${item.pipelineIds.length} pipelines`} /> : null}
                  </div>

                  {item.brief?.brief ? <p class="muted compare-brief">{item.brief.brief}</p> : null}
                  {item.review.note ? (
                    <p class="compare-note">
                      <strong>Review note:</strong> {item.review.note}
                    </p>
                  ) : null}

                  <div class="compare-section">
                    <h4>daggerfall.people usage</h4>
                    {item.peoplePack.cast.length ? (
                      <p>
                        <strong>Cast:</strong>{' '}
                        {item.peoplePack.cast.map((c, idx) => (
                          <span key={c.id}>
                            {idx > 0 ? ', ' : ''}
                            <a href={castUrl(c.id)} onClick={onClose}>
                              {c.name}
                            </a>
                          </span>
                        ))}
                      </p>
                    ) : null}
                    {item.peoplePack.spawners.length ? (
                      <p>
                        <strong>Spawners:</strong>{' '}
                        {[...new Set(item.peoplePack.spawners.map(s => s.spawnerId))].map((sid, idx) => (
                          <span key={sid}>
                            {idx > 0 ? ', ' : ''}
                            <a href={spawnerUrl(sid)} onClick={onClose}>
                              {sid}
                            </a>
                          </span>
                        ))}
                      </p>
                    ) : null}
                    <p class="muted">
                      {item.peoplePack.slotCount} pack slots · {item.placementCount} source placements
                    </p>
                  </div>

                  <div class="compare-section">
                    <h4>Aliases ({item.aliases.length})</h4>
                    <p class="muted compare-aliases">{item.aliases.join(', ') || 'None'}</p>
                  </div>
                </article>
              );
            })}
          </div>
        ) : null}
      </div>
    </Modal>
  );
}

export function RetireNpcModal({
  open,
  npcId,
  onClose,
  onRetired,
}: {
  open: boolean;
  npcId: string | null;
  onClose: () => void;
  onRetired: (retiredId: string) => void;
}) {
  const { refreshCatalog } = useApp();
  const [refs, setRefs] = useState<PeopleReferencesResponse | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [confirmed, setConfirmed] = useState(false);
  const [retiring, setRetiring] = useState(false);
  const [retireError, setRetireError] = useState<string | null>(null);

  useEffect(() => {
    if (!open || !npcId) return;
    const controller = new AbortController();
    setLoading(true);
    setError(null);
    setRetireError(null);
    setConfirmed(false);
    setRefs(null);
    apiRequest<PeopleReferencesResponse>(`/api/people/${encodeKey(npcId)}/references`, { signal: controller.signal })
      .then(res => setRefs(res))
      .catch(err => {
        if (!isAbortError(err)) setError(err instanceof Error ? err.message : String(err));
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false);
      });
    return () => controller.abort();
  }, [open, npcId]);

  async function doRetire() {
    if (!npcId || !refs || !refs.canRetire) return;
    if (refs.requiresConfirmation && !confirmed) return;
    setRetiring(true);
    setRetireError(null);
    try {
      await apiRequest(`/api/people/${encodeKey(npcId)}/retire`, {
        method: 'POST',
        body: { force: refs.requiresConfirmation },
      });
      await refreshCatalog();
      onRetired(npcId);
    } catch (err) {
      setRetireError(err instanceof Error ? err.message : String(err));
    } finally {
      setRetiring(false);
    }
  }

  return (
    <Modal
      open={open}
      title={refs ? `Retire ${refs.name} (${refs.id})` : `Retire ${npcId ?? ''}`}
      subtitle="Retiring removes this record from the active People catalog via NpcReferenceMerges.json (reversible)."
      onClose={onClose}
    >
      <div class="curation-modal-body">
        {loading && !refs ? <p>Checking active references…</p> : null}
        {error ? <Empty>{error}</Empty> : null}
        {retireError ? (
          <div class="validation-summary" role="alert">
            <strong>Could not retire record:</strong> {retireError}
          </div>
        ) : null}

        {refs ? (
          <>
            <div class="retire-head">
              <SpritePreview url={refs.classic} alt={refs.name} pixel className="picker-thumb" />
              <div>
                <strong>{refs.name}</strong> <code>{refs.id}</code>
                <p class="muted">{refs.kind}</p>
              </div>
            </div>

            {!refs.canRetire ? (
              <div class="validation-summary" role="alert">
                <strong>Cannot retire this record:</strong>
                <ul>
                  {refs.blockers.map((b, idx) => (
                    <li key={idx}>{b}</li>
                  ))}
                </ul>
              </div>
            ) : refs.requiresConfirmation ? (
              <div class="curation-warning" role="alert">
                <strong>Active references found:</strong>
                <ul>
                  {refs.warnings.map((w, idx) => (
                    <li key={idx}>{w}</li>
                  ))}
                </ul>
                {refs.peoplePack.cast.length ? (
                  <p>
                    Cast links:{' '}
                    {refs.peoplePack.cast.map((c, idx) => (
                      <span key={c.id}>
                        {idx > 0 ? ', ' : ''}
                        <a href={castUrl(c.id)} onClick={onClose}>
                          {c.name}
                        </a>
                      </span>
                    ))}
                  </p>
                ) : null}
                {refs.peoplePack.spawners.length ? (
                  <p>
                    Spawner links:{' '}
                    {[...new Set(refs.peoplePack.spawners.map(s => s.spawnerId))].map((sid, idx) => (
                      <span key={sid}>
                        {idx > 0 ? ', ' : ''}
                        <a href={spawnerUrl(sid)} onClick={onClose}>
                          {sid}
                        </a>
                      </span>
                    ))}
                  </p>
                ) : null}
                <label class="confirm-checkbox">
                  <input
                    type="checkbox"
                    checked={confirmed}
                    onChange={event => setConfirmed((event.currentTarget as HTMLInputElement).checked)}
                  />{' '}
                  I understand this record still has active references and want to retire it anyway.
                </label>
              </div>
            ) : (
              <div class="curation-safe">
                <strong>Safe to retire:</strong> No cast individuals, spawner pools, block slots, pipelines, or concept
                art reference this record.
              </div>
            )}

            <div class="actions" style={{ marginTop: '14px' }}>
              <button
                type="button"
                class="danger"
                disabled={retiring || !refs.canRetire || (refs.requiresConfirmation && !confirmed)}
                onClick={() => void doRetire()}
              >
                {retiring ? 'Retiring & rebuilding chart…' : refs.requiresConfirmation ? 'Retire anyway' : 'Retire record'}
              </button>
              <button type="button" disabled={retiring} onClick={onClose}>
                Cancel
              </button>
            </div>
          </>
        ) : null}
      </div>
    </Modal>
  );
}

export function MergePartnerPickerModal({
  open,
  baseId,
  onSelectIds,
  onClose,
}: {
  open: boolean;
  baseId: string;
  onSelectIds: (ids: string[]) => void;
  onClose: () => void;
}) {
  const { catalog } = useApp();
  const [query, setQuery] = useState('');
  const [selected, setSelected] = useState<string[]>([]);

  useEffect(() => {
    if (open) {
      setQuery('');
      setSelected([]);
    }
  }, [open, baseId]);

  const candidates = useMemo(() => {
    const people: CharacterSummary[] = catalog?.people ?? [];
    const terms = query.trim().toLowerCase().split(/\s+/).filter(Boolean);
    return people.filter(row => {
      if (row.id === baseId) return false;
      if (!terms.length) return true;
      const hay = [row.id, row.name, row.kind, ...(row.aliases ?? [])].join(' ').toLowerCase();
      return terms.every(t => hay.includes(t));
    });
  }, [catalog?.people, baseId, query]);

  function toggle(id: string) {
    setSelected(prev => (prev.includes(id) ? prev.filter(x => x !== id) : [...prev, id]));
  }

  return (
    <Modal
      open={open}
      title={`Merge ${baseId} with…`}
      subtitle="Select one or more People records to compare side-by-side before choosing a keeper."
      onClose={onClose}
      headerActions={
        <button
          type="button"
          class="primary"
          disabled={selected.length === 0}
          onClick={() => onSelectIds([baseId, ...selected])}
        >
          Compare ({selected.length + 1})
        </button>
      }
    >
      <div class="picker-body">
        <div class="filters">
          <label>
            Find People record
            <input
              type="search"
              value={query}
              placeholder="ID, name, alias"
              onInput={event => setQuery((event.currentTarget as HTMLInputElement).value)}
            />
          </label>
          <output>{selected.length} selected</output>
        </div>
        <div class="picker-grid">
          {candidates.slice(0, 120).map(row => {
            const isPicked = selected.includes(row.id);
            return (
              <button
                key={row.id}
                type="button"
                class={`picker-card${isPicked ? ' selected' : ''}`}
                onClick={() => toggle(row.id)}
              >
                <SpritePreview url={row.classic} alt={row.name} pixel className="picker-thumb" />
                <div class="picker-info">
                  <code>{row.id}</code>
                  <strong>{row.name}</strong>
                  <small class="muted">{row.kind}</small>
                </div>
              </button>
            );
          })}
        </div>
      </div>
    </Modal>
  );
}

export function RetiredPeopleModal({
  open,
  onClose,
}: {
  open: boolean;
  onClose: () => void;
}) {
  const { refreshCatalog } = useApp();
  const [retired, setRetired] = useState<string[]>([]);
  const [loading, setLoading] = useState(false);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!open) return;
    const controller = new AbortController();
    setLoading(true);
    setError(null);
    apiRequest<{ retired: string[] }>('/api/people/retired', { signal: controller.signal })
      .then(res => setRetired(res.retired))
      .catch(err => {
        if (!isAbortError(err)) setError(err instanceof Error ? err.message : String(err));
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false);
      });
    return () => controller.abort();
  }, [open]);

  async function restore(id: string) {
    setBusyId(id);
    setError(null);
    try {
      await apiRequest(`/api/people/${encodeKey(id)}/restore`, { method: 'POST' });
      await refreshCatalog();
      setRetired(prev => prev.filter(item => item !== id));
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    } finally {
      setBusyId(null);
    }
  }

  return (
    <Modal
      open={open}
      title="Retired People records"
      subtitle="Records hidden from the active People chart via NpcReferenceMerges.json."
      onClose={onClose}
    >
      <div class="curation-modal-body">
        {loading ? <p>Loading retired records…</p> : null}
        {error ? (
          <div class="validation-summary" role="alert">
            {error}
          </div>
        ) : null}
        {!loading && !retired.length ? <Empty>No People records are currently retired.</Empty> : null}
        {retired.length ? (
          <ul class="retired-list">
            {retired.map(id => (
              <li key={id}>
                <code>{id}</code>
                <div class="actions">
                  <button type="button" disabled={busyId !== null} onClick={() => void restore(id)}>
                    {busyId === id ? 'Restoring…' : 'Restore'}
                  </button>
                </div>
              </li>
            ))}
          </ul>
        ) : null}
        <p class="muted" style={{ marginTop: '12px' }}>
          Restoring rebuilds the local NPC chart and makes the record visible under <a href={subjectUrl('npc-001')} onClick={onClose}>People</a> again.
        </p>
      </div>
    </Modal>
  );
}
