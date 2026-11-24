import { useEffect, useState } from 'preact/hooks';
import { apiRequest, isAbortError } from '../api';
import { encodeKey, subjectUrl } from '../router';
import { useApp } from '../state';
import type { PipelineDetail } from '../types';
import { ReviewPanel } from '../components/ReviewControls';
import { Breadcrumb, Empty, PageHeading } from '../components/ui';
import { PipelineDetailsCard } from './CharacterPage';

export function PipelinePage({ id }: { id: string }) {
  const { catalog } = useApp();
  const [pipeline, setPipeline] = useState<PipelineDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const summary = catalog?.pipelines.find(row => row.id === id);

  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    setError(null);
    apiRequest<PipelineDetail>(`/api/pipelines/${encodeKey(id)}`, { signal: controller.signal })
      .then(data => setPipeline(data))
      .catch(err => {
        if (!isAbortError(err)) setError(err instanceof Error ? err.message : String(err));
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false);
      });
    return () => controller.abort();
  }, [id]);

  if (loading && !pipeline) return <p>Loading pipeline…</p>;
  if (error || !pipeline) {
    return (
      <>
        <PageHeading title="Unable to open this view" />
        <Empty>{error || 'Character pipeline not found.'}</Empty>
      </>
    );
  }

  return (
    <>
      <Breadcrumb href="#pipelines" label="Character stages" />
      <PageHeading title={summary?.name ?? pipeline.name} intro={pipeline.id} />
      {summary?.subjects.length ? (
        <div class="actions">
          {summary.subjects.map(subject => (
            <a key={subject} class="button" href={subjectUrl(subject)}>
              {subject}
            </a>
          ))}
        </div>
      ) : null}
      <ReviewPanel category="pipelines" id={pipeline.id} />
      <PipelineDetailsCard pipeline={pipeline} subject={null} />
    </>
  );
}
