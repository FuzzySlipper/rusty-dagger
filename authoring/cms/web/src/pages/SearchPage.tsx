import { useEffect, useMemo, useState } from 'preact/hooks';
import { apiRequest, isAbortError } from '../api';
import { castUrl, spawnerUrl } from '../router';
import { ASSET_LABELS, ASSET_PAGES, type PeoplePackOverview } from '../types';
import { useApp } from '../state';
import { ArtDataCard } from '../components/ArtDataLink';
import { AssetTile, Empty, PageHeading, PipelineTile } from '../components/ui';
import { matchRecords } from './AssetListingPage';

export function SearchPage({ term }: { term: string }) {
  const { catalog } = useApp();
  const [pack, setPack] = useState<PeoplePackOverview | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    apiRequest<PeoplePackOverview>('/api/people-pack/overview', { signal: controller.signal })
      .then(res => setPack(res))
      .catch(err => {
        if (!isAbortError(err)) setPack(null);
      });
    return () => controller.abort();
  }, []);

  const terms = useMemo(() => term.trim().toLowerCase().split(/\s+/).filter(Boolean), [term]);

  const castMatches = useMemo(() => {
    if (!pack || !terms.length) return [];
    return pack.cast.filter(row => {
      const hay = [
        row.id,
        row.name,
        row.factionName ?? '',
        String(row.factionId),
        row.race ?? '',
        row.homeBlock,
        row.art.npcId ?? '',
        row.art.npcName ?? '',
        `${row.visual.archive}-${row.visual.record}`,
      ]
        .join(' ')
        .toLowerCase();
      return terms.every(t => hay.includes(t));
    });
  }, [pack, terms]);

  const spawnerMatches = useMemo(() => {
    if (!pack || !terms.length) return [];
    return pack.spawners.filter(sp => {
      const hay = [
        sp.id,
        sp.role,
        sp.harm,
        sp.interaction,
        ...sp.regionalPools.map(r => `${r.regionKey} ${r.regionName}`),
      ]
        .join(' ')
        .toLowerCase();
      return terms.every(t => hay.includes(t));
    });
  }, [pack, terms]);

  if (!catalog) return null;

  const assetSections = ASSET_PAGES.map(page => ({
    key: page,
    label: ASSET_LABELS[page],
    rows: matchRecords(catalog[page], term),
  })).filter(group => group.rows.length > 0);

  const pipelineMatches = matchRecords(catalog.pipelines, term);
  const hasAny =
    castMatches.length > 0 ||
    spawnerMatches.length > 0 ||
    assetSections.length > 0 ||
    pipelineMatches.length > 0;

  return (
    <>
      <PageHeading title={`Search for “${term}”`} />
      {!hasAny ? <Empty>No matching assets or game data.</Empty> : null}

      {castMatches.length ? (
        <section class="search-section">
          <h2>
            Cast individuals <small>{castMatches.length}</small>
          </h2>
          <div class="usage-cards">
            {castMatches.map(c => (
              <ArtDataCard
                key={c.id}
                href={castUrl(c.id)}
                id={c.id}
                title={c.name}
                subtitle={`${c.race ?? 'race unset'} · ${c.gender} · ${c.homeBlock}`}
                thumbnail={c.art.classic}
                badge={
                  c.needsConceptArt
                    ? { state: 'needs-check', label: 'Needs concept' }
                    : { state: 'approved', label: 'Concept approved' }
                }
              />
            ))}
          </div>
        </section>
      ) : null}

      {spawnerMatches.length ? (
        <section class="search-section">
          <h2>
            Typed spawners <small>{spawnerMatches.length}</small>
          </h2>
          <div class="usage-cards">
            {spawnerMatches.map(sp => (
              <ArtDataCard
                key={sp.id}
                href={spawnerUrl(sp.id)}
                id={sp.id}
                title={sp.id}
                subtitle={`${sp.role} · ${sp.usage.slotCount.toLocaleString()} slots · ${sp.defaultPool.length} default visuals`}
                thumbnail={sp.defaultPool[0]?.art.classic ?? null}
                badge={{ state: sp.harm }}
              />
            ))}
          </div>
        </section>
      ) : null}

      {assetSections.map(group => (
        <section key={group.key} class="search-section">
          <h2>
            {group.label} <small>{group.rows.length}</small>
          </h2>
          <div class="tiles">
            {group.rows.map(row => (
              <AssetTile key={row.id} row={row} />
            ))}
          </div>
        </section>
      ))}

      {pipelineMatches.length ? (
        <section class="search-section">
          <h2>
            Character stages <small>{pipelineMatches.length}</small>
          </h2>
          <div class="tiles">
            {pipelineMatches.map(row => (
              <PipelineTile key={row.id} row={row} />
            ))}
          </div>
        </section>
      ) : null}
    </>
  );
}
