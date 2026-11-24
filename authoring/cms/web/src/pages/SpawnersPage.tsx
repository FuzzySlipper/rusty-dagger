import { useEffect, useMemo, useState } from 'preact/hooks';
import { ApiError, apiRequest, isAbortError } from '../api';
import { blocksUrl, encodeKey, spawnerUrl, subjectUrl } from '../router';
import type {
  PeoplePackOverview,
  RegionOption,
  SpawnerDefinition,
  ValidationIssue,
  VisualOption,
} from '../types';
import { ArtDataChip } from '../components/ArtDataLink';
import { DirtyBar, FieldRow, ValidationSummary } from '../components/FormControls';
import { VisualPickerModal } from '../components/VisualPickerModal';
import { Breadcrumb, Chip, Empty, PageHeading, SpritePreview } from '../components/ui';

export function SpawnersPage({ id }: { id?: string }) {
  if (id) return <SpawnerDetailView id={id} />;
  return <SpawnerBrowserView />;
}

function SpawnerBrowserView() {
  const [data, setData] = useState<PeoplePackOverview | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [query, setQuery] = useState('');
  const [harmFilter, setHarmFilter] = useState('');
  const [auditFilter, setAuditFilter] = useState<'' | 'single-visual' | 'has-regional' | 'no-regional'>('');

  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    setError(null);
    apiRequest<PeoplePackOverview>('/api/people-pack/overview', { signal: controller.signal })
      .then(res => setData(res))
      .catch(err => {
        if (!isAbortError(err)) setError(err instanceof Error ? err.message : String(err));
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false);
      });
    return () => controller.abort();
  }, []);

  const filtered = useMemo(() => {
    if (!data) return [];
    const terms = query.trim().toLowerCase().split(/\s+/).filter(Boolean);
    return data.spawners.filter(sp => {
      if (harmFilter && sp.harm !== harmFilter) return false;
      if (auditFilter === 'single-visual' && sp.defaultPool.length > 1) return false;
      if (auditFilter === 'has-regional' && sp.regionalPools.length === 0) return false;
      if (auditFilter === 'no-regional' && sp.regionalPools.length > 0) return false;
      if (!terms.length) return true;
      const hay = [
        sp.id,
        sp.role,
        sp.harm,
        sp.interaction,
        ...sp.defaultPool.map(e => `${e.archive}-${e.record} ${e.art.npcId ?? ''} ${e.art.npcName ?? ''}`),
        ...sp.regionalPools.map(r => `${r.regionKey} ${r.regionName}`),
      ]
        .join(' ')
        .toLowerCase();
      return terms.every(term => hay.includes(term));
    });
  }, [data, query, harmFilter, auditFilter]);

  if (loading && !data) return <p>Loading typed spawners…</p>;
  if (error || !data) {
    return (
      <>
        <PageHeading title="Typed spawners" />
        <Empty>{error || 'Unable to load daggerfall.people spawners.'}</Empty>
      </>
    );
  }

  return (
    <>
      <PageHeading
        title="Typed spawners"
        intro="Central regional visual pools in daggerfall.people/spawners.json with block usage counts and CMS art previews."
      />

      <div class="stat-strip">
        <button type="button" class={`stat-pill${auditFilter === '' ? ' active' : ''}`} onClick={() => setAuditFilter('')}>
          <strong>{data.counts.spawners}</strong>
          <span>Typed spawners</span>
        </button>
        <button
          type="button"
          class={`stat-pill${auditFilter === 'single-visual' ? ' active' : ''}`}
          onClick={() => setAuditFilter(auditFilter === 'single-visual' ? '' : 'single-visual')}
        >
          <strong>{data.counts.spawnersSingleVisual}</strong>
          <span>Single-visual default</span>
        </button>
        <button
          type="button"
          class={`stat-pill${auditFilter === 'has-regional' ? ' active' : ''}`}
          onClick={() => setAuditFilter(auditFilter === 'has-regional' ? '' : 'has-regional')}
        >
          <strong>{data.counts.spawnersWithRegionalPools}</strong>
          <span>With regional overrides</span>
        </button>
      </div>

      <div class="filters">
        <label>
          Search
          <input
            type="search"
            value={query}
            placeholder="Spawner ID, role, sprite, npc-###, region"
            onInput={event => setQuery((event.currentTarget as HTMLInputElement).value)}
          />
        </label>
        <label>
          Harm policy
          <select value={harmFilter} onChange={event => setHarmFilter((event.currentTarget as HTMLSelectElement).value)}>
            <option value="">All policies</option>
            {data.enums.harms.map(h => (
              <option key={h} value={h}>
                {h}
              </option>
            ))}
          </select>
        </label>
        <label>
          Pool audit
          <select
            value={auditFilter}
            onChange={event =>
              setAuditFilter((event.currentTarget as HTMLSelectElement).value as typeof auditFilter)
            }
          >
            <option value="">All spawners</option>
            <option value="single-visual">Only 1 default visual</option>
            <option value="has-regional">Has regional pools</option>
            <option value="no-regional">No regional overrides yet</option>
          </select>
        </label>
        <output>
          {filtered.length} of {data.spawners.length} spawners
        </output>
      </div>

      {filtered.length ? (
        <div class="spawner-list">
          {filtered.map(sp => (
            <article key={sp.id} class="panel spawner-row">
              <div class="section-heading">
                <div>
                  <a href={spawnerUrl(sp.id)}>
                    <strong>{sp.id}</strong>
                  </a>{' '}
                  <span class="muted">· {sp.role}</span>{' '}
                  <Chip state={sp.harm} /> <Chip state={sp.interaction} />
                  {sp.deriveRegionalFaction ? <Chip state="approved" label="Regional faction" /> : null}
                </div>
                <div class="actions">
                  <a class="button" href={blocksUrl({ spawner: sp.id })}>
                    {sp.usage.slotCount.toLocaleString()} slots ({sp.usage.blockCount} blocks)
                  </a>
                  <a class="button primary" href={spawnerUrl(sp.id)}>
                    Inspect &amp; edit
                  </a>
                </div>
              </div>

              <div class="spawner-preview-strip">
                {sp.defaultPool.slice(0, 12).map((entry, idx) => (
                  <a
                    key={`${entry.archive}-${entry.record}-${entry.gender}-${idx}`}
                    href={entry.art.npcId ? subjectUrl(entry.art.npcId) : spawnerUrl(sp.id)}
                    class="pool-mini"
                    title={`${entry.art.npcName ?? entry.art.key} · ${entry.gender} · weight ${entry.weight} (${Math.round(
                      entry.share * 100,
                    )}%)`}
                  >
                    <SpritePreview url={entry.art.classic} alt={entry.art.key} pixel className="pool-mini-img" />
                    <small>
                      {entry.archive}.{entry.record}
                    </small>
                    <small class="muted">
                      {entry.gender[0]} · {Math.round(entry.share * 100)}%
                    </small>
                  </a>
                ))}
                {sp.defaultPool.length > 12 ? (
                  <a class="pool-mini pool-more" href={spawnerUrl(sp.id)}>
                    +{sp.defaultPool.length - 12} more
                  </a>
                ) : null}
              </div>

              <p class="muted spawner-foot">
                {sp.defaultPool.length} default {sp.defaultPool.length === 1 ? 'visual' : 'visuals'} ·{' '}
                {sp.regionalPools.length} regional {sp.regionalPools.length === 1 ? 'override' : 'overrides'}
                {sp.regionalPools.length
                  ? ` (${sp.regionalPools.map(r => r.regionName).join(', ')})`
                  : ''}
              </p>
            </article>
          ))}
        </div>
      ) : (
        <Empty>No spawners match the current filters.</Empty>
      )}
    </>
  );
}

interface PoolEntryDraft {
  archive: number;
  record: number;
  gender: 'Male' | 'Female';
  weight: number;
  art?: VisualOption | SpawnerDefinition['defaultPool'][number]['art'];
}

interface SpawnerDraft {
  role: string;
  interaction: string;
  harm: string;
  deriveRegionalFaction: boolean;
  visualMode: 'pool' | 'classic-slot';
  defaultPool: PoolEntryDraft[];
  regionalPools: Record<string, PoolEntryDraft[]>;
}

function draftFromSpawner(sp: SpawnerDefinition): SpawnerDraft {
  const regionalPools: Record<string, PoolEntryDraft[]> = {};
  for (const group of sp.regionalPools) {
    regionalPools[group.regionKey] = group.entries.map(e => ({
      archive: e.archive,
      record: e.record,
      gender: e.gender,
      weight: e.weight,
      art: e.art,
    }));
  }
  return {
    role: sp.role,
    interaction: sp.interaction,
    harm: sp.harm,
    deriveRegionalFaction: sp.deriveRegionalFaction,
    visualMode: sp.visualMode,
    defaultPool: sp.defaultPool.map(e => ({
      archive: e.archive,
      record: e.record,
      gender: e.gender,
      weight: e.weight,
      art: e.art,
    })),
    regionalPools,
  };
}

function serializeSpawnerDraft(draft: SpawnerDraft) {
  const strip = (entries: PoolEntryDraft[]) =>
    entries.map(e => ({ archive: e.archive, record: e.record, gender: e.gender, weight: e.weight }));
  const regional: Record<string, ReturnType<typeof strip>> = {};
  for (const [k, v] of Object.entries(draft.regionalPools)) {
    regional[k] = strip(v);
  }
  return {
    role: draft.role,
    interaction: draft.interaction,
    harm: draft.harm,
    deriveRegionalFaction: draft.deriveRegionalFaction,
    visualMode: draft.visualMode,
    defaultPool: strip(draft.defaultPool),
    regionalPools: regional,
  };
}

type PickerTarget =
  | { scope: 'default'; index: number | null }
  | { scope: 'regional'; regionKey: string; index: number | null };

function SpawnerDetailView({ id }: { id: string }) {
  const [spawner, setSpawner] = useState<SpawnerDefinition | null>(null);
  const [overview, setOverview] = useState<PeoplePackOverview | null>(null);
  const [draft, setDraft] = useState<SpawnerDraft | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [saveStatus, setSaveStatus] = useState('');
  const [saveError, setSaveError] = useState<string | null>(null);
  const [issues, setIssues] = useState<ValidationIssue[]>([]);
  const [pickerTarget, setPickerTarget] = useState<PickerTarget | null>(null);
  const [addRegionKey, setAddRegionKey] = useState('17');

  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    setError(null);
    setSaveStatus('');
    setSaveError(null);
    setIssues([]);
    Promise.all([
      apiRequest<SpawnerDefinition>(`/api/people-pack/spawners/${encodeKey(id)}`, { signal: controller.signal }),
      apiRequest<PeoplePackOverview>('/api/people-pack/overview', { signal: controller.signal }),
    ])
      .then(([sp, ov]) => {
        setSpawner(sp);
        setOverview(ov);
        setDraft(draftFromSpawner(sp));
      })
      .catch(err => {
        if (!isAbortError(err)) setError(err instanceof Error ? err.message : String(err));
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false);
      });
    return () => controller.abort();
  }, [id]);

  const regionsByKey = useMemo(() => {
    const map = new Map<string, RegionOption>();
    for (const r of overview?.enums.regions ?? []) {
      map.set(r.key, r);
      map.set(r.name.toLowerCase(), r);
    }
    return map;
  }, [overview?.enums.regions]);

  const dirty = useMemo(() => {
    if (!spawner || !draft) return false;
    return JSON.stringify(serializeSpawnerDraft(draftFromSpawner(spawner))) !== JSON.stringify(serializeSpawnerDraft(draft));
  }, [spawner, draft]);

  async function save() {
    if (!spawner || !draft) return;
    setSaving(true);
    setSaveError(null);
    setIssues([]);
    setSaveStatus('');
    try {
      const updated = await apiRequest<SpawnerDefinition>(`/api/people-pack/spawners/${encodeKey(spawner.id)}`, {
        method: 'PUT',
        body: serializeSpawnerDraft(draft),
      });
      setSpawner(updated);
      setDraft(draftFromSpawner(updated));
      setSaveStatus('Saved to spawners.json.');
    } catch (err) {
      if (err instanceof ApiError) {
        setSaveError(err.message);
        setIssues(err.issues);
      } else {
        setSaveError(err instanceof Error ? err.message : String(err));
      }
    } finally {
      setSaving(false);
    }
  }

  function updatePoolEntries(target: 'default' | string, updater: (prev: PoolEntryDraft[]) => PoolEntryDraft[]) {
    if (!draft) return;
    if (target === 'default') {
      setDraft({ ...draft, defaultPool: updater(draft.defaultPool) });
    } else {
      setDraft({
        ...draft,
        regionalPools: { ...draft.regionalPools, [target]: updater(draft.regionalPools[target] ?? []) },
      });
    }
  }

  function removeRegion(regionKey: string) {
    if (!draft) return;
    const next = { ...draft.regionalPools };
    delete next[regionKey];
    setDraft({ ...draft, regionalPools: next });
  }

  function handleVisualPicked(visual: VisualOption) {
    if (!pickerTarget || !draft) return;
    const poolKey = pickerTarget.scope === 'default' ? 'default' : pickerTarget.regionKey;
    updatePoolEntries(poolKey, prev => {
      if (pickerTarget.index === null) {
        return [...prev, { archive: visual.archive, record: visual.record, gender: 'Male', weight: 10, art: visual }];
      }
      return prev.map((entry, idx) =>
        idx === pickerTarget.index
          ? { ...entry, archive: visual.archive, record: visual.record, art: visual }
          : entry,
      );
    });
  }

  function addRegionalPool() {
    if (!draft || !addRegionKey || draft.regionalPools[addRegionKey]) return;
    const seedEntry = draft.defaultPool[0]
      ? { ...draft.defaultPool[0], weight: 10 }
      : { archive: 182, record: 0, gender: 'Male' as const, weight: 10 };
    setDraft({
      ...draft,
      regionalPools: { ...draft.regionalPools, [addRegionKey]: [seedEntry] },
    });
  }

  if (loading && !spawner) return <p>Loading spawner…</p>;
  if (error || !spawner || !draft) {
    return (
      <>
        <Breadcrumb href={spawnerUrl()} label="Typed spawners" />
        <PageHeading title="Unable to open spawner" />
        <Empty>{error || 'Spawner not found.'}</Empty>
      </>
    );
  }

  const enums = overview?.enums;
  const availableRegions = (enums?.regions ?? []).filter(r => !(r.key in draft.regionalPools));

  function renderPoolGrid(entries: PoolEntryDraft[], poolTarget: 'default' | string) {
    const totalWeight = entries.reduce((sum, e) => sum + (e.weight > 0 ? e.weight : 0), 0) || 1;
    return (
      <div class="pool-editor-grid">
        {entries.map((entry, idx) => (
          <article key={`${poolTarget}-${idx}`} class="pool-entry-card">
            <div class="pool-entry-top">
              <SpritePreview url={entry.art?.classic} alt={`${entry.archive}.${entry.record}`} pixel className="pool-entry-thumb" />
              <div class="pool-entry-head">
                {entry.art?.npcId ? (
                  <ArtDataChip
                    href={subjectUrl(entry.art.npcId)}
                    id={entry.art.npcId}
                    label={entry.art.npcName ?? entry.art.npcId}
                  />
                ) : (
                  <code>
                    {entry.archive}.{entry.record}
                  </code>
                )}
                <small class="muted">
                  TEXTURE.{entry.archive} rec {entry.record} · {Math.round(((entry.weight > 0 ? entry.weight : 0) / totalWeight) * 100)}%
                </small>
                <div class="actions">
                  <button
                    type="button"
                    onClick={() =>
                      setPickerTarget(
                        poolTarget === 'default'
                          ? { scope: 'default', index: idx }
                          : { scope: 'regional', regionKey: poolTarget, index: idx },
                      )
                    }
                  >
                    Change sprite
                  </button>
                  <button
                    type="button"
                    onClick={() => updatePoolEntries(poolTarget, prev => prev.filter((_, i) => i !== idx))}
                  >
                    Remove
                  </button>
                </div>
              </div>
            </div>
            <div class="pool-entry-fields">
              <label>
                Gender
                <select
                  value={entry.gender}
                  onChange={event => {
                    const gender = (event.currentTarget as HTMLSelectElement).value as 'Male' | 'Female';
                    updatePoolEntries(poolTarget, prev => prev.map((it, i) => (i === idx ? { ...it, gender } : it)));
                  }}
                >
                  <option value="Male">Male</option>
                  <option value="Female">Female</option>
                </select>
              </label>
              <label>
                Weight
                <input
                  type="number"
                  min={1}
                  value={entry.weight}
                  onInput={event => {
                    const weight = Number((event.currentTarget as HTMLInputElement).value);
                    updatePoolEntries(poolTarget, prev => prev.map((it, i) => (i === idx ? { ...it, weight } : it)));
                  }}
                />
              </label>
            </div>
          </article>
        ))}
      </div>
    );
  }

  return (
    <>
      <Breadcrumb href={spawnerUrl()} label="Typed spawners" />
      <PageHeading
        title={spawner.id}
        intro={`${spawner.role} · ${spawner.usage.slotCount.toLocaleString()} block slots across ${spawner.usage.blockCount} blocks`}
        action={
          <a class="button primary" href={blocksUrl({ spawner: spawner.id })}>
            Audit {spawner.usage.slotCount.toLocaleString()} block slots
          </a>
        }
      />

      <ValidationSummary error={saveError} issues={issues} />
      <DirtyBar
        dirty={dirty}
        saving={saving}
        status={saveStatus}
        onSave={() => void save()}
        onRevert={() => {
          setDraft(draftFromSpawner(spawner));
          setSaveError(null);
          setIssues([]);
        }}
      />

      <section class="panel">
        <h2>Spawner policy</h2>
        <div class="form-grid">
          <FieldRow label="Role">
            <input
              type="text"
              value={draft.role}
              onInput={event => setDraft({ ...draft, role: (event.currentTarget as HTMLInputElement).value })}
            />
          </FieldRow>
          <FieldRow label="Interaction">
            <select
              value={draft.interaction}
              onChange={event => setDraft({ ...draft, interaction: (event.currentTarget as HTMLSelectElement).value })}
            >
              {(enums?.interactions ?? ['none', 'ambient-bark', 'full-dialogue']).map(v => (
                <option key={v} value={v}>
                  {v}
                </option>
              ))}
            </select>
          </FieldRow>
          <FieldRow label="Harm policy">
            <select
              value={draft.harm}
              onChange={event => setDraft({ ...draft, harm: (event.currentTarget as HTMLSelectElement).value })}
            >
              {(enums?.harms ?? ['protected', 'law-protected-civilian', 'combatant']).map(v => (
                <option key={v} value={v}>
                  {v}
                </option>
              ))}
            </select>
          </FieldRow>
          <FieldRow label="Visual mode">
            <select
              value={draft.visualMode}
              onChange={event =>
                setDraft({
                  ...draft,
                  visualMode: (event.currentTarget as HTMLSelectElement).value as 'pool' | 'classic-slot',
                })
              }
            >
              <option value="pool">pool</option>
              <option value="classic-slot">classic-slot</option>
            </select>
          </FieldRow>
          <FieldRow label="Regional faction">
            <label class="mark-filter" style={{ paddingTop: '6px' }}>
              <input
                type="checkbox"
                checked={draft.deriveRegionalFaction}
                onChange={event =>
                  setDraft({ ...draft, deriveRegionalFaction: (event.currentTarget as HTMLInputElement).checked })
                }
              />{' '}
              Derive regional people faction
            </label>
          </FieldRow>
        </div>
      </section>

      <section class="panel">
        <div class="section-heading">
          <h2>Default visual pool ({draft.defaultPool.length})</h2>
          <button type="button" class="primary" onClick={() => setPickerTarget({ scope: 'default', index: null })}>
            Add visual
          </button>
        </div>
        {draft.defaultPool.length ? (
          renderPoolGrid(draft.defaultPool, 'default')
        ) : (
          <Empty>Default pool is empty (pool mode requires at least 1 entry).</Empty>
        )}
      </section>

      <section class="panel">
        <div class="section-heading">
          <h2>Regional pool overrides ({Object.keys(draft.regionalPools).length})</h2>
          {availableRegions.length ? (
            <div class="actions">
              <select
                aria-label="Choose region to override"
                value={addRegionKey}
                onChange={event => setAddRegionKey((event.currentTarget as HTMLSelectElement).value)}
              >
                {availableRegions.map(r => (
                  <option key={r.key} value={r.key}>
                    {r.name} ({r.index})
                  </option>
                ))}
              </select>
              <button type="button" onClick={addRegionalPool}>
                Add region override
              </button>
            </div>
          ) : null}
        </div>
        <p class="muted" style={{ marginBottom: '12px' }}>
          Stored under canonical numeric region index keys (e.g. <code>17</code> for Daggerfall, <code>20</code> for
          Sentinel) matching <code>DaggerfallPeopleSlots.PickVisual</code>.
        </p>

        {Object.keys(draft.regionalPools).length ? (
          Object.entries(draft.regionalPools).map(([regionKey, entries]) => {
            const info = regionsByKey.get(regionKey) ?? regionsByKey.get(regionKey.toLowerCase());
            return (
              <div key={regionKey} class="regional-block">
                <div class="section-heading">
                  <h3>
                    {info ? `${info.name} (region ${info.index})` : regionKey} <code>{regionKey}</code>
                  </h3>
                  <div class="actions">
                    <button
                      type="button"
                      onClick={() => setPickerTarget({ scope: 'regional', regionKey, index: null })}
                    >
                      Add visual
                    </button>
                    <button type="button" onClick={() => removeRegion(regionKey)}>
                      Remove region
                    </button>
                  </div>
                </div>
                {renderPoolGrid(entries, regionKey)}
              </div>
            );
          })
        ) : (
          <Empty>No regional overrides yet — all regions draw from the default pool.</Empty>
        )}
      </section>

      <section class="panel">
        <div class="section-heading">
          <h2>Block &amp; building usage</h2>
          <a href={blocksUrl({ spawner: spawner.id })}>Filter slots for {spawner.id}</a>
        </div>
        <div class="usage-pills">
          {Object.entries(spawner.usage.byKind).map(([kind, count]) => (
            <a key={kind} class="art-data-chip" href={blocksUrl({ spawner: spawner.id, kind })}>
              <span>{kind}</span>
              <code>{count}</code>
            </a>
          ))}
          {spawner.usage.byBuildingType.map(bt => (
            <a
              key={bt.buildingType}
              class="art-data-chip"
              href={blocksUrl({ spawner: spawner.id, buildingType: bt.buildingType })}
            >
              <span>{bt.name}</span>
              <code>{bt.count}</code>
            </a>
          ))}
        </div>
      </section>

      <VisualPickerModal
        open={pickerTarget !== null}
        title="Pick sprite for spawner pool"
        onSelect={handleVisualPicked}
        onClose={() => setPickerTarget(null)}
      />
    </>
  );
}
