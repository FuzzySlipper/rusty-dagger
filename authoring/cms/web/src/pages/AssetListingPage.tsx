import { useEffect, useMemo, useState } from 'preact/hooks';
import {
  ASSET_LABELS,
  isCharacterRow,
  type AssetCategory,
  type CatalogAssetRow,
  type CharacterSummary,
  type PipelineSummary,
} from '../types';
import { subjectUrl } from '../router';
import { useApp } from '../state';
import { useSessionState } from '../hooks/useSessionState';
import { PeopleCompareModal, RetireNpcModal, RetiredPeopleModal } from '../components/PeopleCurationModals';
import { TileReview } from '../components/ReviewControls';
import { AssetTile, Chip, Empty, PageHeading, PipelineTile, SpritePreview } from '../components/ui';

type SearchableRow = {
  id: string;
  name: string;
  kind?: string;
  climate?: string;
  brief?: { brief?: string } | null;
  aliases?: string[];
};

type UsageFilter = '' | 'cast' | 'spawner' | 'used' | 'unused';
type ArtNeedFilter = '' | 'needs-concept' | 'has-concept' | 'needs-final' | 'has-final';

export function matchRecords<T extends SearchableRow>(rows: T[], query: string): T[] {
  const trimmed = query.trim();
  if (!trimmed) return rows;
  const terms = trimmed.toLowerCase().split(/\s+/);
  return rows.filter(row => {
    const haystack = [
      row.id,
      row.name,
      row.kind,
      row.climate,
      row.brief?.brief,
      ...(row.aliases ?? []),
    ]
      .filter(Boolean)
      .join(' ')
      .toLowerCase();
    return terms.every(term => haystack.includes(term));
  });
}

function PeopleCuratableTile({
  row,
  selected,
  onToggleSelect,
}: {
  row: CharacterSummary;
  selected: boolean;
  onToggleSelect: (id: string) => void;
}) {
  const curation = row.curation;
  const previewUrl = row.conceptPreview ?? row.classic;
  const status = row.pipelineIds.length
    ? `${row.pipelineIds.length} linked pipeline${row.pipelineIds.length === 1 ? '' : 's'}`
    : row.kind;

  return (
    <article class={`tile${selected ? ' tile-selected' : ''}`} data-npc-id={row.id}>
      <a class="tile-link" href={subjectUrl(row.id)}>
        <SpritePreview url={previewUrl} alt="" pixel={!row.conceptPreview} />
        <div class="tile-info">
          <code>{row.id}</code>
          <strong>{row.name}</strong>
          <small>{status}</small>
          {curation ? (
            <div class="tile-badges">
              {curation.castCount > 0 ? <Chip state="approved" label={`Cast (${curation.castCount})`} /> : null}
              {curation.spawnerCount > 0 ? (
                <Chip state="empty" label={`Spawners (${curation.spawnerCount})`} />
              ) : null}
              {curation.castCount === 0 && curation.spawnerCount === 0 && curation.slotCount === 0 ? (
                <Chip state="needs-check" label="Unused" />
              ) : null}
              {curation.hasApprovedConcept ? <Chip state="approved" label="Concept" /> : null}
              {curation.hasFinalAsset ? <Chip state="approved" label="Final" /> : null}
            </div>
          ) : null}
        </div>
      </a>
      <div class="tile-curation-bar">
        <button
          type="button"
          data-select-npc={row.id}
          aria-pressed={selected ? 'true' : 'false'}
          onClick={() => onToggleSelect(row.id)}
        >
          {selected ? 'Selected' : 'Select'}
        </button>
      </div>
      <TileReview category={row.category} id={row.id} />
    </article>
  );
}

interface ListingFilters {
  term: string;
  facet: string;
  ratingFilter: string;
  markedOnly: boolean;
  usageFilter: UsageFilter;
  artFilter: ArtNeedFilter;
}

const DEFAULT_LISTING_FILTERS: ListingFilters = {
  term: '',
  facet: '',
  ratingFilter: '',
  markedOnly: false,
  usageFilter: '',
  artFilter: '',
};

export function AssetListingPage({ category }: { category: AssetCategory | 'pipelines' }) {
  const { catalog, reviewFor, markedCount, clearMarks } = useApp();
  const [filters, setFilters] = useSessionState<ListingFilters>(
    `cms.listing.filters.${category}`,
    DEFAULT_LISTING_FILTERS,
  );
  const { term, facet, ratingFilter, markedOnly, usageFilter, artFilter } = filters;
  const patchFilters = (change: Partial<ListingFilters>) => setFilters(prev => ({ ...prev, ...change }));
  const hasActiveFilters = Boolean(term || facet || ratingFilter || markedOnly || usageFilter || artFilter);

  const [clearing, setClearing] = useState(false);
  const [markStatus, setMarkStatus] = useState('');

  // People curation state (multi-select merge + batch guarded retire)
  const [selectedIds, setSelectedIds] = useState<string[]>([]);
  const [compareOpen, setCompareOpen] = useState(false);
  const [retireIds, setRetireIds] = useState<string[]>([]);
  const [retiredModalOpen, setRetiredModalOpen] = useState(false);
  const [curationStatus, setCurationStatus] = useState('');

  useEffect(() => {
    setMarkStatus('');
    setSelectedIds([]);
    setCompareOpen(false);
    setRetireIds([]);
    setCurationStatus('');
  }, [category]);

  if (!catalog) return null;

  const isPipelines = category === 'pipelines';
  const isPeople = category === 'people';
  const label = isPipelines ? 'Character stages' : ASSET_LABELS[category];
  const items: Array<CatalogAssetRow | PipelineSummary> = isPipelines ? catalog.pipelines : catalog[category];

  const peopleStats = useMemo(() => {
    if (!isPeople) return null;
    const people = catalog.people;
    return {
      total: people.length,
      cast: people.filter(r => (r.curation?.castCount ?? 0) > 0).length,
      spawner: people.filter(r => (r.curation?.spawnerCount ?? 0) > 0).length,
      unused: people.filter(
        r => (r.curation?.castCount ?? 0) === 0 && (r.curation?.spawnerCount ?? 0) === 0 && (r.curation?.slotCount ?? 0) === 0,
      ).length,
      needsConcept: people.filter(r => !r.curation?.hasApprovedConcept).length,
      hasConcept: people.filter(r => r.curation?.hasApprovedConcept).length,
    };
  }, [isPeople, catalog.people]);

  const kinds = useMemo(() => {
    if (isPipelines) return [];
    const assetItems = items as CatalogAssetRow[];
    return [...new Set(assetItems.map(row => row.kind))].sort();
  }, [isPipelines, items]);

  const matching = useMemo(() => {
    return matchRecords(items as SearchableRow[], term).filter(row => {
      const saved = reviewFor(category, row.id);
      if (facet && (row.kind ?? '') !== facet) return false;
      if (markedOnly && !saved.marked) return false;
      if (ratingFilter) {
        if (ratingFilter === 'unrated' && saved.rating) return false;
        if (ratingFilter !== 'unrated' && saved.rating !== ratingFilter) return false;
      }
      if (isPeople && isCharacterRow(row as CatalogAssetRow)) {
        const curation = (row as CharacterSummary).curation;
        if (curation) {
          const used = curation.castCount > 0 || curation.spawnerCount > 0 || curation.slotCount > 0;
          if (usageFilter === 'cast' && curation.castCount === 0) return false;
          if (usageFilter === 'spawner' && curation.spawnerCount === 0) return false;
          if (usageFilter === 'used' && !used) return false;
          if (usageFilter === 'unused' && used) return false;
          if (artFilter === 'needs-concept' && curation.hasApprovedConcept) return false;
          if (artFilter === 'has-concept' && !curation.hasApprovedConcept) return false;
          if (artFilter === 'needs-final' && curation.hasFinalAsset) return false;
          if (artFilter === 'has-final' && !curation.hasFinalAsset) return false;
        }
      }
      return true;
    }) as Array<CatalogAssetRow | PipelineSummary>;
  }, [items, term, facet, markedOnly, ratingFilter, usageFilter, artFilter, category, isPeople, reviewFor]);

  const marks = markedCount(category);

  function toggleSelect(id: string) {
    setSelectedIds(prev => (prev.includes(id) ? prev.filter(item => item !== id) : [...prev, id]));
  }

  async function handleClearMarks() {
    setClearing(true);
    setMarkStatus('');
    try {
      await clearMarks(category);
      setMarkStatus('Marks cleared.');
    } catch (error) {
      setMarkStatus(error instanceof Error ? error.message : String(error));
    } finally {
      setClearing(false);
    }
  }

  return (
    <>
      <PageHeading
        title={label}
        action={
          isPeople ? (
            <div class="actions">
              <button type="button" onClick={() => setRetiredModalOpen(true)}>
                Retired records
              </button>
              <a class="button" href="/media/npc/">
                Legacy chart
              </a>
            </div>
          ) : undefined
        }
      />

      {isPeople && peopleStats ? (
        <div class="stat-strip">
          <button
            type="button"
            class={`stat-pill${usageFilter === '' && artFilter === '' ? ' active' : ''}`}
            onClick={() => patchFilters({ usageFilter: '', artFilter: '' })}
          >
            <strong>{peopleStats.total}</strong>
            <span>All people</span>
          </button>
          <button
            type="button"
            class={`stat-pill${usageFilter === 'cast' ? ' active' : ''}`}
            onClick={() => patchFilters({ usageFilter: usageFilter === 'cast' ? '' : 'cast' })}
          >
            <strong>{peopleStats.cast}</strong>
            <span>Used by Cast</span>
          </button>
          <button
            type="button"
            class={`stat-pill${usageFilter === 'spawner' ? ' active' : ''}`}
            onClick={() => patchFilters({ usageFilter: usageFilter === 'spawner' ? '' : 'spawner' })}
          >
            <strong>{peopleStats.spawner}</strong>
            <span>Used by Spawners</span>
          </button>
          <button
            type="button"
            class={`stat-pill${usageFilter === 'unused' ? ' active' : ''}`}
            onClick={() => patchFilters({ usageFilter: usageFilter === 'unused' ? '' : 'unused' })}
          >
            <strong>{peopleStats.unused}</strong>
            <span>Unused in pack</span>
          </button>
          <button
            type="button"
            class={`stat-pill${artFilter === 'needs-concept' ? ' active' : ''}`}
            onClick={() => patchFilters({ artFilter: artFilter === 'needs-concept' ? '' : 'needs-concept' })}
          >
            <strong>{peopleStats.needsConcept}</strong>
            <span>Needs concept art</span>
          </button>
          <button
            type="button"
            class={`stat-pill${artFilter === 'has-concept' ? ' active' : ''}`}
            onClick={() => patchFilters({ artFilter: artFilter === 'has-concept' ? '' : 'has-concept' })}
          >
            <strong>{peopleStats.hasConcept}</strong>
            <span>Concept approved</span>
          </button>
        </div>
      ) : null}

      {isPeople && (selectedIds.length > 0 || curationStatus) ? (
        <div class="curation-bar" role="region" aria-label="People curation selection">
          <div class="curation-bar-items">
            {selectedIds.map(id => (
              <button
                key={id}
                type="button"
                class="art-data-chip"
                title={`Remove ${id} from selection`}
                onClick={() => toggleSelect(id)}
              >
                <code>{id}</code>
                <span>×</span>
              </button>
            ))}
            {curationStatus ? <output role="status">{curationStatus}</output> : null}
          </div>
          {selectedIds.length > 0 ? (
            <div class="actions">
              <button
                type="button"
                class="primary"
                disabled={selectedIds.length < 2}
                onClick={() => setCompareOpen(true)}
              >
                Compare &amp; merge ({selectedIds.length})
              </button>
              <button type="button" class="danger" onClick={() => setRetireIds([...selectedIds])}>
                {selectedIds.length === 1 ? `Retire ${selectedIds[0]}…` : `Retire selected (${selectedIds.length})…`}
              </button>
              <button type="button" onClick={() => setSelectedIds([])}>
                Clear selection
              </button>
            </div>
          ) : null}
        </div>
      ) : null}

      <div class="filters">
        <label>
          Filter
          <input
            type="search"
            id="filter"
            value={term}
            placeholder="ID, name, or description"
            onInput={event => patchFilters({ term: (event.currentTarget as HTMLInputElement).value })}
          />
        </label>
        {kinds.length > 1 ? (
          <label>
            Kind
            <select
              id="facet"
              value={facet}
              onChange={event => patchFilters({ facet: (event.currentTarget as HTMLSelectElement).value })}
            >
              <option value="">All</option>
              {kinds.map(value => (
                <option key={value || '__unsorted'} value={value}>
                  {value || 'Unsorted'}
                </option>
              ))}
            </select>
          </label>
        ) : null}
        {isPeople ? (
          <>
            <label>
              Game usage
              <select
                id="usage-filter"
                value={usageFilter}
                onChange={event =>
                  patchFilters({ usageFilter: (event.currentTarget as HTMLSelectElement).value as UsageFilter })
                }
              >
                <option value="">All usage</option>
                <option value="cast">Used by named Cast</option>
                <option value="spawner">Used by Spawners</option>
                <option value="used">Any daggerfall.people usage</option>
                <option value="unused">Unused in daggerfall.people</option>
              </select>
            </label>
            <label>
              Art status
              <select
                id="art-filter"
                value={artFilter}
                onChange={event =>
                  patchFilters({ artFilter: (event.currentTarget as HTMLSelectElement).value as ArtNeedFilter })
                }
              >
                <option value="">All art states</option>
                <option value="needs-concept">Needs concept art</option>
                <option value="has-concept">Has approved concept</option>
                <option value="needs-final">Needs final game asset</option>
                <option value="has-final">Has final game asset</option>
              </select>
            </label>
          </>
        ) : null}
        <label>
          Rating
          <select
            id="rating-filter"
            value={ratingFilter}
            onChange={event => patchFilters({ ratingFilter: (event.currentTarget as HTMLSelectElement).value })}
          >
            <option value="">All ratings</option>
            <option value="good">Good</option>
            <option value="maybe">Maybe</option>
            <option value="bad">Bad</option>
            <option value="unrated">Unrated</option>
          </select>
        </label>
        <label class="mark-filter">
          <input
            type="checkbox"
            id="marked-filter"
            checked={markedOnly}
            onChange={event => patchFilters({ markedOnly: (event.currentTarget as HTMLInputElement).checked })}
          />{' '}
          Marked only
        </label>
        {hasActiveFilters ? (
          <button type="button" onClick={() => setFilters(DEFAULT_LISTING_FILTERS)}>
            Reset filters
          </button>
        ) : null}
        <output id="results" aria-live="polite">
          {matching.length} {matching.length === 1 ? 'record' : 'records'}
        </output>
      </div>

      <div class="mark-bar">
        <span id="marked-count">{marks} marked</span>
        <button type="button" data-clear-marks={category} disabled={clearing || marks === 0} onClick={handleClearMarks}>
          Clear all marks
        </button>
        <output id="mark-status" role="status">
          {markStatus}
        </output>
      </div>

      <div id="tiles" class="tiles">
        {matching.length ? (
          isPipelines ? (
            (matching as PipelineSummary[]).map(row => <PipelineTile key={row.id} row={row} />)
          ) : isPeople ? (
            (matching as CharacterSummary[]).map(row => (
              <PeopleCuratableTile
                key={row.id}
                row={row}
                selected={selectedIds.includes(row.id)}
                onToggleSelect={toggleSelect}
              />
            ))
          ) : (
            (matching as CatalogAssetRow[]).map(row => <AssetTile key={row.id} row={row} />)
          )
        ) : (
          <Empty>No matching records.</Empty>
        )}
      </div>

      {isPeople ? (
        <>
          <PeopleCompareModal
            open={compareOpen}
            ids={selectedIds}
            onClose={() => setCompareOpen(false)}
            onMerged={keeperId => {
              setCompareOpen(false);
              setSelectedIds([]);
              setCurationStatus(`Merged into ${keeperId}.`);
            }}
          />
          <RetireNpcModal
            open={retireIds.length > 0}
            npcIds={retireIds}
            onClose={() => setRetireIds([])}
            onRetired={doneIds => {
              setRetireIds([]);
              const doneSet = new Set(doneIds);
              setSelectedIds(prev => prev.filter(id => !doneSet.has(id)));
              setCurationStatus(
                doneIds.length === 1 ? `Retired ${doneIds[0]}.` : `Retired ${doneIds.length} records (${doneIds.join(', ')}).`,
              );
            }}
          />
          <RetiredPeopleModal open={retiredModalOpen} onClose={() => setRetiredModalOpen(false)} />
        </>
      ) : null}
    </>
  );
}
