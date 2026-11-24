import { ASSET_LABELS, ASSET_PAGES } from '../types';
import { useApp } from '../state';
import { AssetTile, Empty, PageHeading, PipelineTile } from '../components/ui';
import { matchRecords } from './AssetListingPage';

export function SearchPage({ term }: { term: string }) {
  const { catalog } = useApp();
  if (!catalog) return null;

  const assetSections = ASSET_PAGES.map(page => ({
    key: page,
    label: ASSET_LABELS[page],
    rows: matchRecords(catalog[page], term),
  })).filter(group => group.rows.length > 0);

  const pipelineMatches = matchRecords(catalog.pipelines, term);
  const hasAny = assetSections.length > 0 || pipelineMatches.length > 0;

  return (
    <>
      <PageHeading title={`Search for “${term}”`} />
      {!hasAny ? <Empty>No matching assets.</Empty> : null}
      {assetSections.map(group => (
        <section key={group.key}>
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
        <section>
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
