import type { ComponentChildren } from 'preact';
import { useEffect, useState } from 'preact/hooks';
import { apiRequest, isAbortError } from '../api';
import { actorSpecUrl, actorTemplateUrl, blocksUrl, castUrl, conceptsUrl, encodeKey, spawnerUrl, stageUrl } from '../router';
import { useApp } from '../state';
import { ASSET_LABELS, type CharacterDetail, type LinkChoice, type PipelineDetail } from '../types';
import { ArtDataCard, ArtDataChip } from '../components/ArtDataLink';
import { MergePartnerPickerModal, PeopleCompareModal, RetireNpcModal } from '../components/PeopleCurationModals';
import { ReviewBadge, ReviewPanel } from '../components/ReviewControls';
import { Breadcrumb, Chip, Empty, FactList, PageHeading, SpritePreview } from '../components/ui';

export function StageTable({ pipeline }: { pipeline: PipelineDetail }) {
  const { reviewFor } = useApp();
  return (
    <div class="table-wrap">
      <table>
        <thead>
          <tr>
            <th>Stage</th>
            <th>State</th>
            <th>Selection or note</th>
            <th>Review</th>
          </tr>
        </thead>
        <tbody>
          {pipeline.stages.map(row => (
            <tr key={row.id}>
              <td>{row.name}</td>
              <td>
                <Chip state={row.state} />
              </td>
              <td>{[row.selection.join(', '), row.note].filter(Boolean).join(' · ')}</td>
              <td>
                <a href={stageUrl(pipeline.id, row.id)}>Review in CMS</a>{' '}
                <ReviewBadge row={reviewFor('pipeline-stages', `${pipeline.id}/${row.id}`)} />
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

export function PipelineDetailsCard({
  pipeline,
  subject,
  saving,
  onUnlink,
  onPick,
}: {
  pipeline: PipelineDetail;
  subject: CharacterDetail | null;
  saving?: boolean;
  onUnlink?: (pipelineId: string) => void;
  onPick?: (role: 'concept' | 'final', choice: LinkChoice) => void;
}) {
  const hasConceptApproval = Boolean(subject?.conceptStage?.approved || subject?.conceptStage?.conflict);
  const final = pipeline.final;
  const finalSelected = Boolean(subject?.final && subject.final.pipeline === final?.pipeline);
  const automatic = subject?.automaticPipelines.includes(pipeline.id) ?? false;

  return (
    <article class="panel pipeline">
      <div class="section-heading">
        <div>
          <h3>{pipeline.name}</h3>
          <code>{pipeline.id}</code>
          {pipeline.recipe ? <span class="muted"> · {pipeline.recipe}</span> : null}
        </div>
        <div class="actions">
          <a href={pipeline.stateUrl}>Stage data</a>
          {pipeline.timelineUrl ? <a href={pipeline.timelineUrl}>Original timeline</a> : null}
          {subject && !automatic && onUnlink ? (
            <button type="button" disabled={saving} onClick={() => onUnlink(pipeline.id)}>
              Unlink
            </button>
          ) : null}
        </div>
      </div>

      {pipeline.concepts.length ? (
        <div class="concept-options">
          {pipeline.concepts.map(art => {
            const selected =
              subject?.concept?.pipeline === art.pipeline && subject.concept.path === art.path;
            const status = art.approved
              ? 'Approved in pipeline'
              : art.recorded
                ? 'Recorded candidate'
                : 'Present on disk';
            return (
              <figure key={`${art.pipeline}:${art.path}`}>
                <a class="source-link" href={art.url}>
                  Open {art.name.toLowerCase()}
                </a>
                <SpritePreview url={art.url} alt={art.name} />
                <figcaption>
                  {status}{' '}
                  {subject && !hasConceptApproval ? (
                    selected ? (
                      <Chip state="Current concept" />
                    ) : onPick ? (
                      <button
                        type="button"
                        disabled={saving}
                        onClick={() => onPick('concept', { pipeline: art.pipeline, path: art.path })}
                      >
                        Set as canonical
                      </button>
                    ) : null
                  ) : null}
                </figcaption>
              </figure>
            );
          })}
        </div>
      ) : null}

      {final ? (
        <div class="output">
          <div>
            <strong>Ready for game testing</strong>
            <p>{final.note || final.name}</p>
          </div>
          <a class="button" href={final.url}>
            {final.name}
          </a>
          {subject && !finalSelected && onPick ? (
            <button
              type="button"
              disabled={saving}
              onClick={() => onPick('final', { pipeline: final.pipeline, path: final.path })}
            >
              Set as final
            </button>
          ) : null}
        </div>
      ) : null}

      <StageTable pipeline={pipeline} />
    </article>
  );
}

interface DepictionView {
  url: string;
  link?: string;
  caption: string;
  note?: string;
  action?: ComponentChildren;
}

function DepictionPanel({
  name,
  art,
  fallback,
  pixel = false,
}: {
  name: string;
  art: DepictionView | null;
  fallback: string;
  pixel?: boolean;
}) {
  return (
    <article class="panel depiction">
      <h2>{name}</h2>
      {art ? <SpritePreview url={art.url} alt={name} pixel={pixel} /> : <Empty>{fallback}</Empty>}
      {art ? (
        <div class="output">
          <div>
            <a href={art.link || art.url}>{art.caption}</a>
            {art.note ? <p>{art.note}</p> : null}
          </div>
          {art.action ?? null}
        </div>
      ) : null}
    </article>
  );
}

export function CharacterPage({ id }: { id: string }) {
  const { catalog, refreshCatalog } = useApp();
  const [subject, setSubject] = useState<CharacterDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [actionStatus, setActionStatus] = useState('');
  const [selectedPipeline, setSelectedPipeline] = useState('');
  const [mergePickerOpen, setMergePickerOpen] = useState(false);
  const [compareIds, setCompareIds] = useState<string[]>([]);
  const [retireOpen, setRetireOpen] = useState(false);

  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    setError(null);
    setActionStatus('');
    apiRequest<CharacterDetail>(`/api/characters/${encodeKey(id)}`, { signal: controller.signal })
      .then(data => {
        setSubject(data);
      })
      .catch(err => {
        if (!isAbortError(err)) {
          setError(err instanceof Error ? err.message : String(err));
        }
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false);
      });
    return () => controller.abort();
  }, [id]);

  const linkedIds = new Set(subject?.pipelineIds ?? []);
  const availablePipelines = (catalog?.pipelines ?? []).filter(p => !linkedIds.has(p.id));

  useEffect(() => {
    setSelectedPipeline(availablePipelines[0]?.id ?? '');
  }, [subject?.id, availablePipelines.length]);

  async function saveLink(change: Record<string, unknown>) {
    if (!subject) return;
    setSaving(true);
    setActionStatus('Saving link…');
    try {
      const updated = await apiRequest<CharacterDetail>(`/api/characters/${encodeKey(subject.id)}/links`, {
        method: 'POST',
        body: change,
      });
      setSubject(updated);
      setActionStatus('Saved.');
      void refreshCatalog();
    } catch (err) {
      setActionStatus(err instanceof Error ? err.message : String(err));
    } finally {
      setSaving(false);
    }
  }

  if (loading && !subject) return <p>Loading character…</p>;
  if (error || !subject) {
    return (
      <>
        <PageHeading title="Unable to open this view" />
        <Empty>{error || 'Character not found.'}</Empty>
      </>
    );
  }

  const selectedConcept = subject.conceptStage?.approved;
  const art = subject.conceptStage?.conflict ? null : subject.concept;
  const final = subject.final;
  const candidate =
    !subject.explicitConcept &&
    !subject.conceptStage?.conflict &&
    subject.conceptChoices.find(item => item.name === 'Front concept');

  const classic: DepictionView | null = subject.classic
    ? { url: subject.classic, link: subject.classicUrl, caption: 'Classic source' }
    : null;

  const depiction: DepictionView | null = selectedConcept
    ? {
        url: selectedConcept.url,
        caption: selectedConcept.name,
        note: `${selectedConcept.batch} · Approved in concept stage`,
      }
    : art
      ? {
          url: art.url,
          caption: art.name,
          note: `${art.pipeline} · ${subject.explicitConcept ? 'Selected here' : 'Approved in pipeline'}`,
          action: subject.explicitConcept ? (
            <button type="button" disabled={saving} onClick={() => void saveLink({ concept: null })}>
              Use pipeline default
            </button>
          ) : undefined,
        }
      : candidate
        ? {
            url: candidate.url,
            caption: candidate.name,
            note: `${candidate.pipeline} · ${
              subject.conflicts.includes('concept') ? 'Choose between merged references' : 'Available reference'
            }`,
            action: (
              <button
                type="button"
                disabled={saving}
                onClick={() => void saveLink({ concept: { pipeline: candidate.pipeline, path: candidate.path } })}
              >
                Set as canonical
              </button>
            ),
          }
        : null;

  const conceptFallback = subject.conceptStage?.conflict
    ? 'Choose one concept from the merged records.'
    : subject.explicitConcept
      ? 'The saved concept is unavailable.'
      : 'Choose a concept from the concept stage or a linked pipeline.';

  const gameFacts =
    subject.game.placementCount === undefined
      ? subject.game.facts
      : { ...subject.game.facts, 'Source placements': subject.game.placementCount };

  return (
    <>
      <Breadcrumb href={`#${subject.category}`} label={ASSET_LABELS[subject.category]} />
      <PageHeading
        title={subject.name}
        intro={`${subject.id} · ${subject.kind}`}
        action={
          <div class="actions">
            {subject.category === 'people' ? (
              <>
                <button type="button" onClick={() => setMergePickerOpen(true)}>
                  Merge with…
                </button>
                <button type="button" class="danger" onClick={() => setRetireOpen(true)}>
                  Retire record…
                </button>
              </>
            ) : null}
            <a class="button primary" href={conceptsUrl(subject.id)}>
              Concept art{subject.conceptStage?.count ? ` · ${subject.conceptStage.count}` : ''}
            </a>
          </div>
        }
      />

      {subject.brief?.brief ? <p class="brief">{subject.brief.brief}</p> : null}

      <div class="depictions">
        <DepictionPanel name="Classic depiction" art={classic} fallback="Classic sprite export unavailable." pixel />
        <DepictionPanel name="Canonical depiction" art={depiction} fallback={conceptFallback} />
        <article class="panel depiction final">
          <h2>Final representation</h2>
          {final ? (
            <>
              <strong>{final.name}</strong>
              <p>
                Ready for game testing · {final.pipeline}
              </p>
              <div class="actions">
                <a class="button primary" href={final.url}>
                  Open final asset
                </a>
                {final.playable ? <a href={final.playable}>Playable manifest</a> : null}
                {subject.explicitFinal ? (
                  <button type="button" disabled={saving} onClick={() => void saveLink({ final: null })}>
                    Use pipeline default
                  </button>
                ) : null}
              </div>
            </>
          ) : (
            <Empty>
              {subject.explicitFinal
                ? 'The saved final is unavailable.'
                : subject.conflicts.includes('final')
                  ? 'Choose a game-ready output from the merged pipelines.'
                  : 'A game-ready output has yet to be selected.'}
            </Empty>
          )}
        </article>
      </div>

      <ReviewPanel category={subject.category} id={subject.id} />

      {subject.actorRecords && (subject.actorRecords.spawners.length > 0 || subject.actorRecords.templates.length > 0) ? (
        <section class="panel" id="actor-records-usage">
          <div class="section-heading">
            <h2>Unified actor records</h2>
            <a class="button" href="#actors">
              Open Actors view
            </a>
          </div>
          {subject.actorRecords.spawners.length ? (
            <div class="usage-subsection">
              <h3>Spawner specs ({subject.actorRecords.spawners.length})</h3>
              <div class="usage-pills">
                {subject.actorRecords.spawners.map(sp => (
                  <ArtDataChip
                    key={sp.id}
                    href={actorSpecUrl(sp.id)}
                    id={`${sp.family}${sp.template ? ` · ${sp.template}` : ''}`}
                    label={`${sp.displayName} (${sp.id})`}
                  />
                ))}
              </div>
            </div>
          ) : null}
          {subject.actorRecords.templates.length ? (
            <div class="usage-subsection">
              <h3>Templates ({subject.actorRecords.templates.length})</h3>
              <div class="usage-pills">
                {subject.actorRecords.templates.map(tpl => (
                  <ArtDataChip
                    key={tpl.id}
                    href={actorTemplateUrl(tpl.id)}
                    id={tpl.branch}
                    label={tpl.extends ? `${tpl.id} ← ${tpl.extends}` : tpl.id}
                  />
                ))}
              </div>
            </div>
          ) : null}
        </section>
      ) : null}

      {subject.peoplePack ? (
        <section class="panel" id="people-pack-usage">
          <div class="section-heading">
            <h2>daggerfall.people usage</h2>
            <div class="actions">
              {subject.peoplePack.visualKeys.map(vk => (
                <a key={vk.key} class="art-data-chip" href={blocksUrl({ q: vk.key })}>
                  <span>TEXTURE.{vk.archive}</span>
                  <code>rec {vk.record}</code>
                </a>
              ))}
              {subject.peoplePack.slotCount > 0 && subject.peoplePack.visualKeys[0] ? (
                <a class="button" href={blocksUrl({ q: subject.peoplePack.visualKeys[0].key })}>
                  {subject.peoplePack.slotCount.toLocaleString()} block slots
                </a>
              ) : null}
            </div>
          </div>

          {subject.peoplePack.cast.length ? (
            <div class="usage-subsection">
              <h3>Named cast individuals ({subject.peoplePack.cast.length})</h3>
              <div class="usage-cards">
                {subject.peoplePack.cast.map(c => (
                  <ArtDataCard
                    key={c.id}
                    href={castUrl(c.id)}
                    id={c.id}
                    title={c.name}
                    subtitle={`${c.race ?? 'race unset'} · ${c.gender} · Faction ${c.factionId} · ${c.homeBlock}`}
                    badge={{ state: c.harm }}
                  />
                ))}
              </div>
            </div>
          ) : null}

          {subject.peoplePack.spawners.length ? (
            <div class="usage-subsection">
              <h3>Typed spawner pools ({subject.peoplePack.spawners.length})</h3>
              <div class="usage-pills">
                {subject.peoplePack.spawners.map((ref, idx) => (
                  <ArtDataChip
                    key={`${ref.spawnerId}-${ref.pool}-${ref.archive}-${ref.record}-${idx}`}
                    href={spawnerUrl(ref.spawnerId)}
                    id={ref.pool === 'default' ? `w:${ref.weight}` : `${ref.regionName ?? ref.pool} · w:${ref.weight}`}
                    label={`${ref.spawnerId} (${ref.gender})`}
                  />
                ))}
              </div>
            </div>
          ) : null}

          {!subject.peoplePack.cast.length && !subject.peoplePack.spawners.length && subject.peoplePack.slotCount === 0 ? (
            <Empty>Not currently referenced by any cast individual, spawner pool, or block slot in daggerfall.people.</Empty>
          ) : null}
        </section>
      ) : null}

      <section class="panel">
        <div class="section-heading">
          <h2>Game data</h2>
          {subject.game.url ? <a href={subject.game.url}>Source record</a> : null}
        </div>
        <FactList facts={gameFacts} />
        {subject.game.record ? (
          <details>
            <summary>Mobile definition</summary>
            <pre>{JSON.stringify(subject.game.record, null, 2)}</pre>
          </details>
        ) : null}
      </section>

      <section>
        <div class="section-heading">
          <h2>Character pipelines</h2>
          {availablePipelines.length ? (
            <form
              id="link-pipeline"
              onSubmit={event => {
                event.preventDefault();
                if (selectedPipeline) void saveLink({ addPipeline: selectedPipeline });
              }}
            >
              <label>
                Character pipeline
                <select
                  name="pipeline"
                  value={selectedPipeline}
                  onChange={event => setSelectedPipeline((event.currentTarget as HTMLSelectElement).value)}
                >
                  {availablePipelines.map(row => (
                    <option key={row.id} value={row.id}>
                      {row.id} · {row.name}
                    </option>
                  ))}
                </select>
              </label>
              <button class="primary" type="submit" disabled={saving}>
                Link pipeline
              </button>
            </form>
          ) : null}
        </div>

        <p id="action-status" role="status">
          {actionStatus}
        </p>

        {subject.missingPipelines.length ? (
          <Empty>Unavailable pipelines: {subject.missingPipelines.join(', ')}</Empty>
        ) : null}

        {subject.pipelines.length ? (
          subject.pipelines.map(pipeline => (
            <PipelineDetailsCard
              key={pipeline.id}
              pipeline={pipeline}
              subject={subject}
              saving={saving}
              onUnlink={pipelineId => void saveLink({ removePipeline: pipelineId })}
              onPick={(role, choice) => void saveLink({ [role]: choice })}
            />
          ))
        ) : (
          <Empty>Link an existing character pipeline to connect concept art, animation stages, and game outputs.</Empty>
        )}
      </section>

      {subject.turnaroundStill ? (
        <section class="panel">
          <h2>Directional reference</h2>
          <div class="concept-options">
            <figure>
              <SpritePreview url={subject.turnaroundStill} alt="Curated front view" />
              <figcaption>
                {subject.turnaround ? <a href={subject.turnaround}>Open turnaround</a> : null}
              </figcaption>
            </figure>
          </div>
        </section>
      ) : null}

      {subject.category === 'people' ? (
        <>
          <MergePartnerPickerModal
            open={mergePickerOpen}
            baseId={subject.id}
            onClose={() => setMergePickerOpen(false)}
            onSelectIds={ids => {
              setMergePickerOpen(false);
              setCompareIds(ids);
            }}
          />
          <PeopleCompareModal
            open={compareIds.length >= 2}
            ids={compareIds}
            initialKeeper={subject.id}
            onClose={() => setCompareIds([])}
            onMerged={keeperId => {
              setCompareIds([]);
              window.location.hash = `#character/${encodeKey(keeperId)}`;
            }}
          />
          <RetireNpcModal
            open={retireOpen}
            npcIds={retireOpen ? [subject.id] : []}
            onClose={() => setRetireOpen(false)}
            onRetired={() => {
              setRetireOpen(false);
              window.location.hash = '#people';
            }}
          />
        </>
      ) : null}
    </>
  );
}
