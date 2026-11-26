import { useEffect, useMemo, useState } from 'preact/hooks';
import { apiRequest, isAbortError } from '../api';
import { encodeKey, subjectUrl } from '../router';
import { useApp } from '../state';
import type { ConceptCandidate, ConceptStageDetail, ConceptView } from '../types';
import { Modal } from '../components/Modal';
import { ConceptUploader } from '../components/ConceptUploader';
import { Breadcrumb, Empty, PageHeading } from '../components/ui';

const ALL_VIEWS: ConceptView[] = ['front', 'left', 'right', 'back'];
const VIEW_LABELS: Record<ConceptView, string> = {
  front: 'Front',
  left: 'Left',
  right: 'Right',
  back: 'Back',
};

const seedLabel = (item: ConceptCandidate): string =>
  item.details.seed === null ? 'Seed unavailable' : `Seed ${item.details.seed}`;

function CandidateCaption({ item }: { item: ConceptCandidate }) {
  return (
    <>
      <span class="candidate-title">{item.name}</span>
      <span class="candidate-data">
        {seedLabel(item)}
        {item.details.model ? ` · ${item.details.model}` : ''}
      </span>
    </>
  );
}

export function ConceptsPage({ id }: { id: string }) {
  const { refreshCatalog } = useApp();
  const [stage, setStage] = useState<ConceptStageDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [status, setStatus] = useState('');
  const [inspectIndex, setInspectIndex] = useState<number | null>(null);
  const [approving, setApproving] = useState(false);
  const [inspectStatus, setInspectStatus] = useState('');

  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    setError(null);
    setStatus('');
    setInspectIndex(null);
    apiRequest<ConceptStageDetail>(`/api/concepts/${encodeKey(id)}`, { signal: controller.signal })
      .then(data => setStage(data))
      .catch(err => {
        if (!isAbortError(err)) setError(err instanceof Error ? err.message : String(err));
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false);
      });
    return () => controller.abort();
  }, [id]);

  const batches = useMemo(() => {
    const map = new Map<string, ConceptCandidate[]>();
    for (const item of stage?.candidates ?? []) {
      const list = map.get(item.batch) ?? [];
      list.push(item);
      map.set(item.batch, list);
    }
    return [...map.entries()];
  }, [stage?.candidates]);

  useEffect(() => {
    if (inspectIndex === null || !stage) return;
    function onKeyDown(event: KeyboardEvent) {
      if (event.key === 'ArrowLeft') {
        setInspectIndex(prev => (prev !== null && prev > 0 ? prev - 1 : prev));
      } else if (event.key === 'ArrowRight') {
        setInspectIndex(prev => (prev !== null && stage && prev + 1 < stage.candidates.length ? prev + 1 : prev));
      }
    }
    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  }, [inspectIndex !== null, stage]);

  if (loading && !stage) return <p>Loading concept art…</p>;
  if (error || !stage) {
    return (
      <>
        <PageHeading title="Unable to open this view" />
        <Empty>{error || 'Character not found.'}</Empty>
      </>
    );
  }

  const views: ConceptView[] = stage.approvals ? ALL_VIEWS : ['front'];
  const approvals: Record<ConceptView, ConceptCandidate | null> = {
    front: stage.approvals?.front ?? stage.approved ?? null,
    left: stage.approvals?.left ?? null,
    right: stage.approvals?.right ?? null,
    back: stage.approvals?.back ?? null,
  };
  const conflicts: ConceptView[] = stage.conflicts ?? (stage.conflict ? ['front'] : []);

  const inspected = inspectIndex !== null ? stage.candidates[inspectIndex] ?? null : null;
  const extraDetails = inspected
    ? Object.fromEntries(
        Object.entries(inspected.details).filter(([key]) => !['prompt', 'seed', 'model'].includes(key)),
      )
    : {};

  function openInspector(candidateId: string) {
    if (!stage) return;
    const idx = stage.candidates.findIndex(item => item.id === candidateId);
    if (idx >= 0) {
      setInspectStatus('');
      setInspectIndex(idx);
    }
  }

  async function approve(view: ConceptView) {
    if (!stage || !inspected) return;
    setApproving(true);
    setInspectStatus('Saving approval…');
    try {
      const next = await apiRequest<ConceptStageDetail>(`/api/concepts/${encodeKey(stage.id)}/approve`, {
        method: 'POST',
        body: { candidate: inspected.id, view },
      });
      setStage(next);
      setInspectIndex(null);
      setInspectStatus('');
      setStatus(`${inspected.name} approved as the ${view} concept.`);
      window.scrollTo(0, 0);
      void refreshCatalog();
    } catch (err) {
      setInspectStatus(err instanceof Error ? err.message : String(err));
    } finally {
      setApproving(false);
    }
  }

  const classicNode = stage.classic ? (
    <div class="concept-selection">
      <a class="preview pixel classic-photo" href={stage.classic} target="_blank" rel="noopener">
        <img src={stage.classic} alt={`Classic depiction of ${stage.name}`} />
      </a>
      <a href={stage.classicUrl} target="_blank" rel="noopener">
        Classic source
      </a>
    </div>
  ) : null;

  return (
    <div id="concept-stage">
      <Breadcrumb href={subjectUrl(stage.id)} label="Character record" />
      <PageHeading
        title="Concept art"
        intro={
          <>
            {stage.name} · <code>{stage.id}</code>
          </>
        }
      />

      <div class="concept-approvals">
        {classicNode ? (
          <section class="panel">
            <h2>Classic depiction</h2>
            {classicNode}
          </section>
        ) : null}
        {views.map(view => {
          const item = approvals[view];
          const label = VIEW_LABELS[view];
          const emptyMsg = conflicts.includes(view)
            ? `Choose the ${view} from the merged records.`
            : `Choose a ${view} from the candidates.`;
          return (
            <section key={view} class="panel">
              <h2>Approved {view}</h2>
              {item ? (
                <div class="concept-selection">
                  <button
                    type="button"
                    class="selected-photo"
                    aria-label={`Inspect approved ${view}`}
                    onClick={() => openInspector(item.id)}
                  >
                    <img src={item.url} alt={item.name} loading="lazy" />
                  </button>
                  <div>
                    <CandidateCaption item={item} />
                    <p class="muted">
                      {item.batch} · {label} reference
                    </p>
                  </div>
                </div>
              ) : (
                <Empty>{emptyMsg}</Empty>
              )}
            </section>
          );
        })}
      </div>

      {status ? (
        <p class="concept-status" role="status">
          {status}
        </p>
      ) : null}

      <ConceptUploader
        subjectId={stage.id}
        existingNames={new Set(stage.candidates.map(c => c.name))}
        onUploaded={(nextStage, summary) => {
          setStage(nextStage);
          setStatus(summary);
          void refreshCatalog();
        }}
      />

      {batches.length ? (
        batches.map(([batch, items]) => (
          <section key={batch} class="concept-batch">
            <div class="section-heading">
              <h2>{batch}</h2>
              <small>
                {items.length} {items.length === 1 ? 'candidate' : 'candidates'}
              </small>
            </div>
            <div class="concept-grid">
              {items.map(item => (
                <button
                  key={item.id}
                  class="concept-card"
                  type="button"
                  data-inspect={item.id}
                  onClick={() => openInspector(item.id)}
                >
                  <span class="concept-photo">
                    <img src={item.url} alt={item.name} loading="lazy" />
                  </span>
                  <CandidateCaption item={item} />
                  <span class="candidate-prompt">{item.details.prompt}</span>
                  {views
                    .filter(view => item.id === approvals[view]?.id)
                    .map(view => (
                      <span key={view} class="chip approved">
                        {VIEW_LABELS[view]} approved
                      </span>
                    ))}
                </button>
              ))}
            </div>
          </section>
        ))
      ) : (
        <Empty>No concept candidates have been submitted yet.</Empty>
      )}

      <Modal
        open={inspected !== null}
        fullScreen
        title={inspected?.name ?? ''}
        subtitle={inspected?.batch}
        onClose={() => setInspectIndex(null)}
        headerActions={
          inspectIndex !== null ? (
            <>
              <button
                type="button"
                disabled={approving || inspectIndex <= 0}
                onClick={() => setInspectIndex(inspectIndex - 1)}
              >
                Previous
              </button>
              <output>
                {inspectIndex + 1} of {stage.candidates.length}
              </output>
              <button
                type="button"
                disabled={approving || inspectIndex + 1 >= stage.candidates.length}
                onClick={() => setInspectIndex(inspectIndex + 1)}
              >
                Next
              </button>
            </>
          ) : null
        }
      >
        {inspected ? (
          <div class="inspection-body">
            <div class="inspection-image">
              <img src={inspected.url} alt={inspected.name} />
            </div>
            <aside class="inspection-details">
              <div class="actions">
                {views.map(view => {
                  const isApproved = inspected.id === approvals[view]?.id;
                  return (
                    <button
                      key={view}
                      type="button"
                      class="primary"
                      disabled={approving || isApproved}
                      onClick={() => void approve(view)}
                    >
                      {isApproved ? `${VIEW_LABELS[view]} approved` : `Approve as ${view}`}
                    </button>
                  );
                })}
                <a href={inspected.url} target="_blank" rel="noopener">
                  Original image
                </a>
              </div>
              <p id="inspection-status" role="status">
                {inspectStatus}
              </p>
              {classicNode ? (
                <>
                  <h3>Classic depiction</h3>
                  {classicNode}
                </>
              ) : null}
              <h3>Generation details</h3>
              <p>
                {seedLabel(inspected)}
                {inspected.details.model ? ` · ${inspected.details.model}` : ''}
              </p>
              <h3>Prompt</h3>
              <pre>{inspected.details.prompt}</pre>
              {Object.keys(extraDetails).length ? (
                <>
                  <h3>Additional settings</h3>
                  <pre>{JSON.stringify(extraDetails, null, 2)}</pre>
                </>
              ) : null}
            </aside>
          </div>
        ) : null}
      </Modal>
    </div>
  );
}
