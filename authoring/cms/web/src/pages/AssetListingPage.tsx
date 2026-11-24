import { useEffect, useMemo, useState } from 'preact/hooks';
import { ASSET_LABELS, type AssetCategory, type CatalogAssetRow, type PipelineSummary } from '../types';
import { useApp } from '../state';
import { AssetTile, Empty, PageHeading, PipelineTile } from '../components/ui';

type SearchableRow = {
  id: string;
  name: string;
  kind?: string;
  climate?: string;
  brief?: { brief?: string } | null;
  aliases?: string[];
};

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

export function AssetListingPage({ category }: { category: AssetCategory | 'pipelines' }) {
  const { catalog, reviewFor, markedCount, clearMarks } = useApp();
  const [term, setTerm] = useState('');
  const [facet, setFacet] = useState('');
  const [ratingFilter, setRatingFilter] = useState('');
  const [markedOnly, setMarkedOnly] = useState(false);
  const [clearing, setClearing] = useState(false);
  const [markStatus, setMarkStatus] = useState('');

  useEffect(() => {
    setTerm('');
    setFacet('');
    setRatingFilter('');
    setMarkedOnly(false);
    setMarkStatus('');
  }, [category]);

  if (!catalog) return null;

  const isPipelines = category === 'pipelines';
  const label = isPipelines ? 'Character stages' : ASSET_LABELS[category];
  const items: Array<CatalogAssetRow | PipelineSummary> = isPipelines ? catalog.pipelines : catalog[category];

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
      return true;
    }) as Array<CatalogAssetRow | PipelineSummary>;
  }, [items, term, facet, markedOnly, ratingFilter, category, reviewFor]);

  const marks = markedCount(category);

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
          category === 'people' ? (
            <a class="button primary" href="/media/npc/">
              Compare &amp; merge NPCs
            </a>
          ) : undefined
        }
      />

      <div class="filters">
        <label>
          Filter
          <input
            type="search"
            id="filter"
            value={term}
            placeholder="ID, name, or description"
            onInput={event => setTerm((event.currentTarget as HTMLInputElement).value)}
          />
        </label>
        {kinds.length > 1 ? (
          <label>
            Kind
            <select
              id="facet"
              value={facet}
              onChange={event => setFacet((event.currentTarget as HTMLSelectElement).value)}
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
        <label>
          Rating
          <select
            id="rating-filter"
            value={ratingFilter}
            onChange={event => setRatingFilter((event.currentTarget as HTMLSelectElement).value)}
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
            onChange={event => setMarkedOnly((event.currentTarget as HTMLInputElement).checked)}
          />{' '}
          Marked only
        </label>
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
          ) : (
            (matching as CatalogAssetRow[]).map(row => <AssetTile key={row.id} row={row} />)
          )
        ) : (
          <Empty>No matching records.</Empty>
        )}
      </div>
    </>
  );
}
