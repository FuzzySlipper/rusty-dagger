import { useEffect, useState } from 'preact/hooks';
import { apiRequest, isAbortError } from '../api';
import { encodeKey, pipelineUrl } from '../router';
import type { StageReviewDetail } from '../types';
import { ReviewPanel } from '../components/ReviewControls';
import { Breadcrumb, Chip, Empty, PageHeading, SpritePreview } from '../components/ui';

export function StageReviewPage({ pipeline, stage }: { pipeline: string; stage: string }) {
  const [detail, setDetail] = useState<StageReviewDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    setError(null);
    apiRequest<StageReviewDetail>(`/api/stage-review/${encodeKey(pipeline)}/${encodeKey(stage)}`, {
      signal: controller.signal,
    })
      .then(data => setDetail(data))
      .catch(err => {
        if (!isAbortError(err)) setError(err instanceof Error ? err.message : String(err));
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false);
      });
    return () => controller.abort();
  }, [pipeline, stage]);

  if (loading && !detail) return <p>Loading stage review…</p>;
  if (error || !detail) {
    return (
      <>
        <PageHeading title="Unable to open this view" />
        <Empty>{error || 'Stage not found.'}</Empty>
      </>
    );
  }

  return (
    <>
      <Breadcrumb href={pipelineUrl(detail.pipeline)} label="Character stages" />
      <PageHeading title={`${detail.stage.name} review`} intro={`${detail.character} · ${detail.pipeline}`} />
      <div class="review-stage-bar">
        <Chip state={detail.stage.state} />
        {detail.stage.note ? <span>{detail.stage.note}</span> : null}
        {detail.stage.reviewUrl ? <a href={detail.stage.reviewUrl}>Earlier Workbench batch</a> : null}
      </div>
      <ReviewPanel
        category="pipeline-stages"
        id={`${detail.pipeline}/${detail.stage.id}`}
        heading="Stage decision"
        revision={detail.stage.revision}
      />
      <h2>Stage assets</h2>
      {detail.assets.length ? (
        <div class="asset-reviews">
          {detail.assets.map(asset => (
            <article key={asset.id} class="asset-review">
              {asset.media === 'image' ? (
                <SpritePreview url={asset.url} alt={asset.name} />
              ) : asset.media === 'video' ? (
                <video controls preload="metadata" src={asset.url} />
              ) : null}
              <div class="asset-name">
                <a href={asset.url}>{asset.name}</a>
              </div>
              <ReviewPanel
                category="pipeline-assets"
                id={asset.id}
                heading="Asset review"
                revision={asset.revision}
              />
            </article>
          ))}
        </div>
      ) : (
        <Empty>No local media is filed for this stage.</Empty>
      )}
    </>
  );
}
