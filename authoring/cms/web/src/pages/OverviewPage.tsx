import { ASSET_LABELS, ASSET_PAGES } from '../types';
import { blocksUrl, castUrl, conceptsUrl, pipelineUrl, spawnerUrl, stageUrl } from '../router';
import { useApp } from '../state';
import { ReviewBadge } from '../components/ReviewControls';
import { Chip, Empty, PageHeading } from '../components/ui';

export function OverviewPage() {
  const { catalog, reviewFor } = useApp();
  if (!catalog) return null;

  const collections: Array<[string, string, number]> = [
    ...ASSET_PAGES.map(page => [page, ASSET_LABELS[page], catalog[page].length] as [string, string, number]),
    ['pipelines', 'Character pipelines', catalog.pipelines.length],
  ];

  const concepts = catalog.conceptStages ?? [];
  const awaitingSelection = concepts.filter(row => !row.approved).length;
  const pendingRatings = catalog.review.filter(
    row => !reviewFor('pipeline-stages', `${row.pipeline}/${row.id}`).rating,
  ).length;

  return (
    <>
      <PageHeading
        title="Authoring overview"
        action={
          <a class="button primary" href="/media/npc/">
            Compare &amp; merge NPCs
          </a>
        }
      />

      <div class="collections">
        {collections.map(([id, label, count]) => (
          <a key={id} class="collection" href={`#${id}`}>
            <strong>{count}</strong>
            <span>{label}</span>
          </a>
        ))}
      </div>

      {catalog.peoplePack?.available ? (
        <section class="panel">
          <div class="section-heading">
            <h2>daggerfall.people pack</h2>
            <span class="muted">{catalog.peoplePack.castNeedingArt} named cast individuals awaiting concept art</span>
          </div>
          <div class="collections" style={{ marginBottom: 0 }}>
            <a class="collection" href={castUrl()}>
              <strong>{catalog.peoplePack.castCount}</strong>
              <span>Cast individuals</span>
            </a>
            <a class="collection" href={spawnerUrl()}>
              <strong>{catalog.peoplePack.spawnerCount}</strong>
              <span>Typed spawners</span>
            </a>
            <a class="collection" href={blocksUrl()}>
              <strong>{catalog.peoplePack.slotCount.toLocaleString()}</strong>
              <span>Block slots</span>
            </a>
          </div>
        </section>
      ) : null}

      {concepts.length ? (
        <section class="panel">
          <div class="section-heading">
            <h2>Concept art</h2>
            <span class="muted">{awaitingSelection} awaiting a selection</span>
          </div>
          <div class="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Character</th>
                  <th>Candidates</th>
                  <th>Selected concept</th>
                </tr>
              </thead>
              <tbody>
                {concepts.map(row => (
                  <tr key={row.id}>
                    <td>
                      <a href={conceptsUrl(row.id)}>{row.name}</a> <code>{row.id}</code>
                    </td>
                    <td>{row.count}</td>
                    <td>{row.approved || 'Awaiting selection'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </section>
      ) : null}

      <section class="panel">
        <div class="section-heading">
          <h2>Stage reviews</h2>
          <span class="muted">{pendingRatings} awaiting a CMS rating</span>
        </div>
        {catalog.review.length ? (
          <div class="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Character</th>
                  <th>Stage</th>
                  <th>State</th>
                  <th>Note</th>
                  <th>Review</th>
                </tr>
              </thead>
              <tbody>
                {catalog.review.map(row => (
                  <tr key={`${row.pipeline}/${row.id}`}>
                    <td>
                      <a href={pipelineUrl(row.pipeline)}>{row.character}</a> <code>{row.pipeline}</code>
                    </td>
                    <td>{row.name}</td>
                    <td>
                      <Chip state={row.state} />{' '}
                      <ReviewBadge row={reviewFor('pipeline-stages', `${row.pipeline}/${row.id}`)} />
                    </td>
                    <td>{row.note}</td>
                    <td>
                      <a href={stageUrl(row.pipeline, row.id)}>Review in CMS</a>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : (
          <Empty>No character stages are waiting for review.</Empty>
        )}
      </section>
    </>
  );
}
