import { useEffect, useState } from 'preact/hooks';
import { apiRequest, isAbortError } from '../api';
import { blocksUrl, castUrl, spawnerUrl, subjectUrl, worldBlockUrl } from '../router';
import type { BlockSlotItem, BlockSlotsResponse, PeoplePackOverview } from '../types';
import { ArtDataChip } from '../components/ArtDataLink';
import { DataTable, type DataColumn } from '../components/DataTable';
import { Chip, Empty, PageHeading } from '../components/ui';

function slotToWorldBlockKey(row: BlockSlotItem): string {
  const slug = row.block
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-|-$/g, '');
  if (row.kind === 'rmb-interior' && row.buildingIndex !== null) {
    return `rmb/${slug}/interior-${row.buildingIndex}`;
  }
  if (row.kind === 'rmb-exterior') {
    return `rmb/${slug}/exterior`;
  }
  return `rdb/${slug}`;
}

export function BlockSlotsPage({ initialQuery }: { initialQuery: Record<string, string> }) {
  const [overview, setOverview] = useState<PeoplePackOverview | null>(null);
  const [result, setResult] = useState<BlockSlotsResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const [q, setQ] = useState(initialQuery.q ?? '');
  const [spawner, setSpawner] = useState(initialQuery.spawner ?? '');
  const [castId, setCastId] = useState(initialQuery.castId ?? '');
  const [kind, setKind] = useState(initialQuery.kind ?? '');
  const [buildingType, setBuildingType] = useState(initialQuery.buildingType ?? '');
  const [deriveRegional, setDeriveRegional] = useState(initialQuery.deriveRegional ?? '');
  const [interaction, setInteraction] = useState(initialQuery.interaction ?? '');
  const [harm, setHarm] = useState(initialQuery.harm ?? '');
  const [page, setPage] = useState(Number(initialQuery.page) || 1);
  const pageSize = 50;

  useEffect(() => {
    setQ(initialQuery.q ?? '');
    setSpawner(initialQuery.spawner ?? '');
    setCastId(initialQuery.castId ?? '');
    setKind(initialQuery.kind ?? '');
    setBuildingType(initialQuery.buildingType ?? '');
    setDeriveRegional(initialQuery.deriveRegional ?? '');
    setInteraction(initialQuery.interaction ?? '');
    setHarm(initialQuery.harm ?? '');
    setPage(Number(initialQuery.page) || 1);
  }, [JSON.stringify(initialQuery)]);

  useEffect(() => {
    const controller = new AbortController();
    apiRequest<PeoplePackOverview>('/api/people-pack/overview', { signal: controller.signal })
      .then(data => setOverview(data))
      .catch(() => {});
    return () => controller.abort();
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    setError(null);
    const params = new URLSearchParams();
    if (q.trim()) params.set('q', q.trim());
    if (spawner) params.set('spawner', spawner);
    if (castId) params.set('castId', castId);
    if (kind) params.set('kind', kind);
    if (buildingType) params.set('buildingType', buildingType);
    if (deriveRegional) params.set('deriveRegional', deriveRegional);
    if (interaction) params.set('interaction', interaction);
    if (harm) params.set('harm', harm);
    params.set('page', String(page));
    params.set('pageSize', String(pageSize));

    apiRequest<BlockSlotsResponse>(`/api/people-pack/blocks?${params.toString()}`, { signal: controller.signal })
      .then(data => setResult(data))
      .catch(err => {
        if (!isAbortError(err)) setError(err instanceof Error ? err.message : String(err));
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false);
      });
    return () => controller.abort();
  }, [q, spawner, castId, kind, buildingType, deriveRegional, interaction, harm, page]);

  function resetFilters() {
    window.location.hash = blocksUrl();
  }

  const columns: Array<DataColumn<BlockSlotItem>> = [
    {
      key: 'block',
      header: 'Block / Slot',
      render: row => (
        <div>
          <a href={worldBlockUrl(slotToWorldBlockKey(row))} title="Inspect block in 3D viewer">
            <code>{row.block}</code>
          </a>{' '}
          <a class="chip" href={worldBlockUrl(slotToWorldBlockKey(row))}>
            3D
          </a>
          <div>
            <small class="muted">
              {row.slotId} · {row.kind}
            </small>
          </div>
        </div>
      ),
    },
    {
      key: 'target',
      header: 'Spawner / Cast',
      render: row =>
        row.castId ? (
          <a href={castUrl(row.castId)}>
            <strong>{row.castId}</strong> <span class="chip approved">cast</span>
          </a>
        ) : row.spawner ? (
          <a href={spawnerUrl(row.spawner)}>
            <strong>{row.spawner}</strong>
          </a>
        ) : (
          <span class="muted">—</span>
        ),
    },
    {
      key: 'visual',
      header: 'Classic visual',
      render: row =>
        row.art?.npcId ? (
          <ArtDataChip
            href={subjectUrl(row.art.npcId)}
            id={row.art.npcId}
            label={`${row.visual.archive}.${row.visual.record}`}
            thumbnail={row.art.classic}
          />
        ) : (
          <code>
            {row.visual.archive}.{row.visual.record} ({row.visual.gender})
          </code>
        ),
    },
    {
      key: 'building',
      header: 'Building',
      render: row =>
        row.buildingTypeName ? (
          <span>
            {row.buildingTypeName}
            {row.buildingIndex !== null ? <small class="muted"> #{row.buildingIndex}</small> : null}
          </span>
        ) : (
          <span class="muted">—</span>
        ),
    },
    {
      key: 'faction',
      header: 'Faction',
      render: row => (
        <div>
          {row.factionId ? (
            <span>
              {row.factionName ?? `Faction ${row.factionId}`} <code>{row.factionId}</code>
            </span>
          ) : (
            <span class="muted">none</span>
          )}
          {row.deriveRegionalFaction ? (
            <div>
              <Chip state="approved" label="regional" />
            </div>
          ) : null}
        </div>
      ),
    },
    {
      key: 'policy',
      header: 'Interaction / Harm',
      render: row => (
        <div class="cast-badges">
          <Chip state={row.interaction} />
          {row.effectiveHarm ? <Chip state={row.effectiveHarm} /> : null}
        </div>
      ),
    },
  ];

  const hasFilters = Boolean(q || spawner || castId || kind || buildingType || deriveRegional || interaction || harm);

  return (
    <>
      <PageHeading
        title="Block slots"
        intro="Server-filtered audit of every RMB interior, RMB exterior, and RDB dungeon slot in daggerfall.people/blocks/."
        action={
          hasFilters ? (
            <button type="button" onClick={resetFilters}>
              Clear filters
            </button>
          ) : undefined
        }
      />

      <div class="filters">
        <label>
          Search
          <input
            type="search"
            value={q}
            placeholder="Block, slotId, faction, 183-5"
            onInput={event => {
              setQ((event.currentTarget as HTMLInputElement).value);
              setPage(1);
            }}
          />
        </label>
        <label>
          Spawner
          <select
            value={spawner}
            onChange={event => {
              setSpawner((event.currentTarget as HTMLSelectElement).value);
              setPage(1);
            }}
          >
            <option value="">All spawners</option>
            {(overview?.spawners ?? []).map(s => (
              <option key={s.id} value={s.id}>
                {s.id} ({s.usage.slotCount})
              </option>
            ))}
          </select>
        </label>
        <label>
          Cast individual
          <select
            value={castId}
            onChange={event => {
              setCastId((event.currentTarget as HTMLSelectElement).value);
              setPage(1);
            }}
          >
            <option value="">All cast</option>
            {(overview?.cast ?? []).map(c => (
              <option key={c.id} value={c.id}>
                {c.name} ({c.id})
              </option>
            ))}
          </select>
        </label>
        <label>
          Block kind
          <select
            value={kind}
            onChange={event => {
              setKind((event.currentTarget as HTMLSelectElement).value);
              setPage(1);
            }}
          >
            <option value="">All kinds</option>
            <option value="rmb-interior">rmb-interior</option>
            <option value="rmb-exterior">rmb-exterior</option>
            <option value="rdb-dungeon">rdb-dungeon</option>
          </select>
        </label>
        <label>
          Building type
          <select
            value={buildingType}
            onChange={event => {
              setBuildingType((event.currentTarget as HTMLSelectElement).value);
              setPage(1);
            }}
          >
            <option value="">All buildings</option>
            {(overview?.enums.buildingTypes ?? []).map(bt => (
              <option key={bt.id} value={String(bt.id)}>
                {bt.name} ({bt.id})
              </option>
            ))}
          </select>
        </label>
        <label>
          Regional faction
          <select
            value={deriveRegional}
            onChange={event => {
              setDeriveRegional((event.currentTarget as HTMLSelectElement).value);
              setPage(1);
            }}
          >
            <option value="">Any</option>
            <option value="true">Derives regional</option>
            <option value="false">Fixed / none</option>
          </select>
        </label>
        <label>
          Interaction
          <select
            value={interaction}
            onChange={event => {
              setInteraction((event.currentTarget as HTMLSelectElement).value);
              setPage(1);
            }}
          >
            <option value="">Any</option>
            {(overview?.enums.interactions ?? ['none', 'ambient-bark', 'full-dialogue']).map(v => (
              <option key={v} value={v}>
                {v}
              </option>
            ))}
          </select>
        </label>
        <label>
          Effective harm
          <select
            value={harm}
            onChange={event => {
              setHarm((event.currentTarget as HTMLSelectElement).value);
              setPage(1);
            }}
          >
            <option value="">Any</option>
            {(overview?.enums.harms ?? ['protected', 'law-protected-civilian', 'combatant']).map(v => (
              <option key={v} value={v}>
                {v}
              </option>
            ))}
          </select>
        </label>
      </div>

      {result ? (
        <section class="panel">
          <div class="section-heading">
            <h2>
              {result.total.toLocaleString()} matching {result.total === 1 ? 'slot' : 'slots'} across{' '}
              {result.summary.blockCount.toLocaleString()} blocks
            </h2>
            {loading ? <span class="muted">Updating…</span> : null}
          </div>
          {result.summary.byBuildingType.length ? (
            <div class="usage-pills" style={{ marginBottom: '12px' }}>
              {Object.entries(result.summary.byKind).map(([k, count]) => (
                <span key={k} class="chip">
                  {k}: {count.toLocaleString()}
                </span>
              ))}
              {result.summary.byBuildingType.slice(0, 10).map(bt => (
                <button
                  key={bt.buildingType}
                  type="button"
                  class="chip-btn"
                  onClick={() => {
                    setBuildingType(String(bt.buildingType));
                    setPage(1);
                  }}
                >
                  {bt.name}: {bt.count.toLocaleString()}
                </button>
              ))}
            </div>
          ) : null}

          <DataTable
            rows={result.items}
            columns={columns}
            rowKey={(row, idx) => `${row.block}:${row.slotId}:${idx}`}
            emptyMessage="No block slots match the active filters."
            pagination={{
              page: result.page,
              pageSize: result.pageSize,
              total: result.total,
              onPageChange: next => setPage(next),
            }}
          />
        </section>
      ) : error ? (
        <Empty>{error}</Empty>
      ) : (
        <p>Loading block slots…</p>
      )}
    </>
  );
}
