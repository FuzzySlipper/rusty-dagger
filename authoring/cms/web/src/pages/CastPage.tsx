import { useEffect, useMemo, useState } from 'preact/hooks';
import { ApiError, apiRequest, isAbortError } from '../api';
import { blocksUrl, castUrl, conceptsUrl, encodeKey, subjectUrl } from '../router';
import type {
  CastIndividual,
  PeoplePackOverview,
  ValidationIssue,
  VisualOption,
} from '../types';
import { ArtDataCard, ArtDataChip } from '../components/ArtDataLink';
import { DirtyBar, FieldRow, ValidationSummary } from '../components/FormControls';
import { VisualPickerModal } from '../components/VisualPickerModal';
import { Breadcrumb, Chip, Empty, FactList, PageHeading, SpritePreview } from '../components/ui';

type ArtFilter = '' | 'needs-concept' | 'needs-final' | 'shared-visual' | 'has-concept';

export function CastPage({ id }: { id?: string }) {
  if (id) return <CastDetailView id={id} />;
  return <CastBrowserView />;
}

function CastBrowserView() {
  const [data, setData] = useState<PeoplePackOverview | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [query, setQuery] = useState('');
  const [artFilter, setArtFilter] = useState<ArtFilter>('');
  const [raceFilter, setRaceFilter] = useState('');
  const [harmFilter, setHarmFilter] = useState('');

  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    setError(null);
    apiRequest<PeoplePackOverview>('/api/people-pack/overview', { signal: controller.signal })
      .then(res => setData(res))
      .catch(err => {
        if (!isAbortError(err)) setError(err instanceof Error ? err.message : String(err));
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false);
      });
    return () => controller.abort();
  }, []);

  const races = useMemo(() => {
    if (!data) return [];
    return [...new Set(data.cast.map(c => c.race ?? '__null'))].sort();
  }, [data]);

  const filtered = useMemo(() => {
    if (!data) return [];
    const terms = query.trim().toLowerCase().split(/\s+/).filter(Boolean);
    return data.cast.filter(row => {
      if (artFilter === 'needs-concept' && !row.needsConceptArt) return false;
      if (artFilter === 'needs-final' && !row.needsFinalArt) return false;
      if (artFilter === 'shared-visual' && row.sharedVisualWith.length === 0) return false;
      if (artFilter === 'has-concept' && row.needsConceptArt) return false;
      if (raceFilter) {
        const val = row.race ?? '__null';
        if (val !== raceFilter) return false;
      }
      if (harmFilter && row.harm !== harmFilter) return false;
      if (!terms.length) return true;
      const hay = [
        row.id,
        row.name,
        row.factionName ?? '',
        String(row.factionId),
        row.race ?? 'unspecified',
        row.gender,
        row.homeBlock,
        row.art.npcId ?? '',
        row.art.npcName ?? '',
        `${row.visual.archive}-${row.visual.record}`,
      ]
        .join(' ')
        .toLowerCase();
      return terms.every(term => hay.includes(term));
    });
  }, [data, query, artFilter, raceFilter, harmFilter]);

  if (loading && !data) return <p>Loading cast individuals…</p>;
  if (error || !data) {
    return (
      <>
        <PageHeading title="Cast individuals" />
        <Empty>{error || 'Unable to load daggerfall.people cast.'}</Empty>
      </>
    );
  }

  return (
    <>
      <PageHeading
        title="Cast individuals"
        intro="Unique named characters in daggerfall.people/cast/individuals.json, linked to their CMS art records."
        action={
          <a class="button" href={blocksUrl()}>
            Browse all {data.counts.slots.toLocaleString()} block slots
          </a>
        }
      />

      <div class="stat-strip">
        <button
          type="button"
          class={`stat-pill${artFilter === '' ? ' active' : ''}`}
          onClick={() => setArtFilter('')}
        >
          <strong>{data.counts.cast}</strong>
          <span>Named cast</span>
        </button>
        <button
          type="button"
          class={`stat-pill${artFilter === 'needs-concept' ? ' active' : ''}`}
          onClick={() => setArtFilter(artFilter === 'needs-concept' ? '' : 'needs-concept')}
        >
          <strong>{data.counts.castNeedingConceptArt}</strong>
          <span>Need concept art</span>
        </button>
        <button
          type="button"
          class={`stat-pill${artFilter === 'needs-final' ? ' active' : ''}`}
          onClick={() => setArtFilter(artFilter === 'needs-final' ? '' : 'needs-final')}
        >
          <strong>{data.counts.castNeedingFinalArt}</strong>
          <span>Need final asset</span>
        </button>
        <button
          type="button"
          class={`stat-pill${artFilter === 'shared-visual' ? ' active' : ''}`}
          onClick={() => setArtFilter(artFilter === 'shared-visual' ? '' : 'shared-visual')}
        >
          <strong>{data.counts.castSharingVisual}</strong>
          <span>Sharing sprite</span>
        </button>
      </div>

      <div class="filters">
        <label>
          Search
          <input
            type="search"
            value={query}
            placeholder="Name, ID, faction, block, npc-###"
            onInput={event => setQuery((event.currentTarget as HTMLInputElement).value)}
          />
        </label>
        <label>
          Art status
          <select
            value={artFilter}
            onChange={event => setArtFilter((event.currentTarget as HTMLSelectElement).value as ArtFilter)}
          >
            <option value="">All cast</option>
            <option value="needs-concept">Needs concept art</option>
            <option value="has-concept">Has approved concept</option>
            <option value="needs-final">Needs final game asset</option>
            <option value="shared-visual">Shares sprite with another cast</option>
          </select>
        </label>
        <label>
          Race
          <select value={raceFilter} onChange={event => setRaceFilter((event.currentTarget as HTMLSelectElement).value)}>
            <option value="">All races</option>
            {races.map(value => (
              <option key={value} value={value}>
                {value === '__null' ? '(unspecified / null)' : value}
              </option>
            ))}
          </select>
        </label>
        <label>
          Harm policy
          <select value={harmFilter} onChange={event => setHarmFilter((event.currentTarget as HTMLSelectElement).value)}>
            <option value="">All policies</option>
            {data.enums.harms.map(h => (
              <option key={h} value={h}>
                {h}
              </option>
            ))}
          </select>
        </label>
        <output>
          {filtered.length} of {data.cast.length} individuals
        </output>
      </div>

      {filtered.length ? (
        <div class="cast-grid">
          {filtered.map(row => (
            <article key={row.id} class="panel cast-card">
              <div class="cast-card-head">
                <a href={castUrl(row.id)} class="cast-card-thumbs">
                  <SpritePreview url={row.art.classic} alt={row.name} pixel className="cast-thumb" />
                  {row.art.conceptUrl ? (
                    <SpritePreview url={row.art.conceptUrl} alt={`${row.name} concept`} className="cast-thumb" />
                  ) : null}
                </a>
                <div class="cast-card-meta">
                  <div class="cast-card-title">
                    <a href={castUrl(row.id)}>
                      <strong>{row.name}</strong>
                    </a>
                    <code>{row.id}</code>
                  </div>
                  <p class="muted">
                    {row.race ?? 'race unset'} · {row.gender} · Faction {row.factionId}
                    {row.factionName && row.factionName !== row.name ? ` (${row.factionName})` : ''}
                  </p>
                  <p class="muted">
                    Home: <code>{row.homeBlock}</code> / <code>{row.homeSlotId}</code> · {row.usage.slotCount}{' '}
                    {row.usage.slotCount === 1 ? 'slot' : 'slots'}
                  </p>
                  <div class="cast-badges">
                    {row.needsConceptArt ? (
                      <Chip state="needs-check" label="Needs concept art" />
                    ) : (
                      <Chip state="approved" label="Concept approved" />
                    )}
                    {row.art.hasFinalAsset ? <Chip state="approved" label="Final ready" /> : null}
                    {row.sharedVisualWith.length ? (
                      <Chip state="failed" label={`Shared sprite (${row.sharedVisualWith.join(', ')})`} />
                    ) : null}
                    <Chip state={row.harm} />
                  </div>
                </div>
              </div>
              <div class="cast-card-footer">
                {row.art.npcId ? (
                  <ArtDataChip
                    href={subjectUrl(row.art.npcId)}
                    id={row.art.npcId}
                    label={row.art.npcName ?? row.art.npcId}
                    thumbnail={row.art.classic}
                  />
                ) : (
                  <span class="chip failed">
                    Unmapped sprite {row.visual.archive}.{row.visual.record}
                  </span>
                )}
                <div class="actions">
                  {row.art.npcId ? (
                    <a class="button" href={conceptsUrl(row.art.npcId)}>
                      Concept art
                    </a>
                  ) : null}
                  <a class="button primary" href={castUrl(row.id)}>
                    Inspect &amp; edit
                  </a>
                </div>
              </div>
            </article>
          ))}
        </div>
      ) : (
        <Empty>No cast individuals match the current filters.</Empty>
      )}
    </>
  );
}

interface CastDraft {
  name: string;
  race: string;
  gender: 'Male' | 'Female';
  interaction: string;
  harm: string;
  archive: number;
  record: number;
}

function draftFromCast(cast: CastIndividual): CastDraft {
  return {
    name: cast.name,
    race: cast.race ?? '',
    gender: cast.gender,
    interaction: cast.interaction,
    harm: cast.harm,
    archive: cast.visual.archive,
    record: cast.visual.record,
  };
}

function CastDetailView({ id }: { id: string }) {
  const [cast, setCast] = useState<CastIndividual | null>(null);
  const [overview, setOverview] = useState<PeoplePackOverview | null>(null);
  const [draft, setDraft] = useState<CastDraft | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [saveStatus, setSaveStatus] = useState('');
  const [saveError, setSaveError] = useState<string | null>(null);
  const [issues, setIssues] = useState<ValidationIssue[]>([]);
  const [pickerOpen, setPickerOpen] = useState(false);

  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    setError(null);
    setSaveStatus('');
    setSaveError(null);
    setIssues([]);
    Promise.all([
      apiRequest<CastIndividual>(`/api/people-pack/cast/${encodeKey(id)}`, { signal: controller.signal }),
      apiRequest<PeoplePackOverview>('/api/people-pack/overview', { signal: controller.signal }),
    ])
      .then(([castData, overviewData]) => {
        setCast(castData);
        setOverview(overviewData);
        setDraft(draftFromCast(castData));
      })
      .catch(err => {
        if (!isAbortError(err)) setError(err instanceof Error ? err.message : String(err));
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false);
      });
    return () => controller.abort();
  }, [id]);

  const dirty = useMemo(() => {
    if (!cast || !draft) return false;
    const base = draftFromCast(cast);
    return (
      base.name !== draft.name ||
      base.race !== draft.race ||
      base.gender !== draft.gender ||
      base.interaction !== draft.interaction ||
      base.harm !== draft.harm ||
      base.archive !== draft.archive ||
      base.record !== draft.record
    );
  }, [cast, draft]);

  async function save() {
    if (!cast || !draft) return;
    setSaving(true);
    setSaveError(null);
    setIssues([]);
    setSaveStatus('');
    try {
      const updated = await apiRequest<CastIndividual>(`/api/people-pack/cast/${encodeKey(cast.id)}`, {
        method: 'PUT',
        body: {
          name: draft.name,
          race: draft.race.trim() ? draft.race.trim() : null,
          gender: draft.gender,
          interaction: draft.interaction,
          harm: draft.harm,
          visual: { archive: draft.archive, record: draft.record },
        },
      });
      setCast(updated);
      setDraft(draftFromCast(updated));
      setSaveStatus('Saved to cast/individuals.json.');
    } catch (err) {
      if (err instanceof ApiError) {
        setSaveError(err.message);
        setIssues(err.issues);
      } else {
        setSaveError(err instanceof Error ? err.message : String(err));
      }
    } finally {
      setSaving(false);
    }
  }

  if (loading && !cast) return <p>Loading cast individual…</p>;
  if (error || !cast || !draft) {
    return (
      <>
        <Breadcrumb href={castUrl()} label="Cast individuals" />
        <PageHeading title="Unable to open cast individual" />
        <Empty>{error || 'Cast individual not found.'}</Empty>
      </>
    );
  }

  const enums = overview?.enums;

  return (
    <>
      <Breadcrumb href={castUrl()} label="Cast individuals" />
      <PageHeading
        title={cast.name}
        intro={
          <>
            <code>{cast.id}</code> · Faction {cast.factionId} ({cast.factionName ?? 'unknown'}) · Home{' '}
            <code>{cast.homeBlock}</code> / <code>{cast.homeSlotId}</code>
          </>
        }
        action={
          <div class="actions">
            {cast.art.npcId ? (
              <>
                <a class="button" href={subjectUrl(cast.art.npcId)}>
                  Art record ({cast.art.npcId})
                </a>
                <a class="button primary" href={conceptsUrl(cast.art.npcId)}>
                  Concept art
                </a>
              </>
            ) : null}
          </div>
        }
      />

      <div class="depictions">
        <article class="panel depiction">
          <h2>Classic sprite</h2>
          <SpritePreview url={cast.art.classic} alt={cast.name} pixel />
          <div class="output">
            <div>
              <code>
                TEXTURE.{cast.visual.archive} record {cast.visual.record}
              </code>
              <p>{cast.art.npcId ? `Linked to ${cast.art.npcId} (${cast.art.npcName})` : 'Not linked in NPC chart'}</p>
            </div>
            <button type="button" onClick={() => setPickerOpen(true)}>
              Change sprite
            </button>
          </div>
        </article>

        <article class="panel depiction">
          <h2>Canonical concept art</h2>
          {cast.art.conceptUrl ? (
            <SpritePreview url={cast.art.conceptUrl} alt={`${cast.name} concept`} />
          ) : (
            <Empty>No approved concept art for this character yet.</Empty>
          )}
          <div class="output">
            <div>
              {cast.needsConceptArt ? (
                <Chip state="needs-check" label="Needs concept art" />
              ) : (
                <Chip state="approved" label="Approved concept" />
              )}
            </div>
            {cast.art.npcId ? (
              <a class="button" href={conceptsUrl(cast.art.npcId)}>
                Open concept stage
              </a>
            ) : null}
          </div>
        </article>

        <article class="panel depiction">
          <h2>Linked CMS art record</h2>
          {cast.art.npcId ? (
            <ArtDataCard
              href={subjectUrl(cast.art.npcId)}
              id={cast.art.npcId}
              title={cast.art.npcName ?? cast.art.npcId}
              subtitle={`${cast.art.npcKind ?? ''} · ${cast.art.pipelineCount} pipelines`}
              thumbnail={cast.art.classic}
              reviewCategory="people"
              reviewId={cast.art.npcId}
              badge={
                cast.art.hasFinalAsset
                  ? { state: 'approved', label: 'Final ready' }
                  : { state: 'needs-check', label: 'Needs final' }
              }
            />
          ) : (
            <Empty>No matching npc-### record found for this archive/record.</Empty>
          )}
          {cast.sharedVisualWith.length ? (
            <p class="muted">
              Shares this sprite with:{' '}
              {cast.sharedVisualWith.map((other, idx) => (
                <span key={other}>
                  {idx > 0 ? ', ' : ''}
                  <a href={castUrl(other)}>{other}</a>
                </span>
              ))}
            </p>
          ) : null}
        </article>
      </div>

      <section class="panel">
        <div class="section-heading">
          <h2>Cast metadata</h2>
          <span class="muted">Edits write only to cast/individuals.json</span>
        </div>

        <ValidationSummary error={saveError} issues={issues} />
        <DirtyBar
          dirty={dirty}
          saving={saving}
          status={saveStatus}
          onSave={() => void save()}
          onRevert={() => {
            setDraft(draftFromCast(cast));
            setSaveError(null);
            setIssues([]);
          }}
        />

        <div class="form-grid">
          <FieldRow label="Display name">
            <input
              type="text"
              value={draft.name}
              onInput={event => setDraft({ ...draft, name: (event.currentTarget as HTMLInputElement).value })}
            />
          </FieldRow>

          <FieldRow label="Race" hint="Blank saves as null">
            <select
              value={draft.race}
              onChange={event => setDraft({ ...draft, race: (event.currentTarget as HTMLSelectElement).value })}
            >
              <option value="">(none / null)</option>
              {(enums?.races ?? []).map(r => (
                <option key={r} value={r}>
                  {r}
                </option>
              ))}
              {draft.race && !(enums?.races ?? []).includes(draft.race) ? (
                <option value={draft.race}>{draft.race}</option>
              ) : null}
            </select>
          </FieldRow>

          <FieldRow label="Gender">
            <select
              value={draft.gender}
              onChange={event =>
                setDraft({ ...draft, gender: (event.currentTarget as HTMLSelectElement).value as 'Male' | 'Female' })
              }
            >
              {(enums?.genders ?? ['Male', 'Female']).map(g => (
                <option key={g} value={g}>
                  {g}
                </option>
              ))}
            </select>
          </FieldRow>

          <FieldRow label="Interaction policy">
            <select
              value={draft.interaction}
              onChange={event => setDraft({ ...draft, interaction: (event.currentTarget as HTMLSelectElement).value })}
            >
              {(enums?.interactions ?? ['none', 'ambient-bark', 'full-dialogue']).map(v => (
                <option key={v} value={v}>
                  {v}
                </option>
              ))}
            </select>
          </FieldRow>

          <FieldRow label="Harm policy">
            <select
              value={draft.harm}
              onChange={event => setDraft({ ...draft, harm: (event.currentTarget as HTMLSelectElement).value })}
            >
              {(enums?.harms ?? ['protected', 'law-protected-civilian', 'combatant']).map(v => (
                <option key={v} value={v}>
                  {v}
                </option>
              ))}
            </select>
          </FieldRow>

          <FieldRow label="Classic sprite (archive / record)">
            <div class="actions">
              <input
                type="number"
                min={0}
                value={draft.archive}
                style={{ width: '92px' }}
                onInput={event =>
                  setDraft({ ...draft, archive: Number((event.currentTarget as HTMLInputElement).value) })
                }
              />
              <input
                type="number"
                min={0}
                value={draft.record}
                style={{ width: '92px' }}
                onInput={event =>
                  setDraft({ ...draft, record: Number((event.currentTarget as HTMLInputElement).value) })
                }
              />
              <button type="button" onClick={() => setPickerOpen(true)}>
                Pick sprite…
              </button>
            </div>
          </FieldRow>
        </div>

        <FactList
          facts={{
            'Faction ID': `${cast.factionId} (${cast.factionName ?? 'unknown'})`,
            'Home block': cast.homeBlock,
            'Home slot': cast.homeSlotId,
          }}
        />
      </section>

      <section class="panel">
        <div class="section-heading">
          <h2>Block placements</h2>
          <a href={blocksUrl({ castId: cast.id })}>Open in Block slots</a>
        </div>
        {cast.slots?.length ? (
          <div class="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Block</th>
                  <th>Kind</th>
                  <th>Slot ID</th>
                  <th>Building</th>
                  <th>Position</th>
                </tr>
              </thead>
              <tbody>
                {cast.slots.map(slot => (
                  <tr key={`${slot.block}:${slot.slotId}`}>
                    <td>
                      <code>{slot.block}</code>
                    </td>
                    <td>{slot.kind}</td>
                    <td>
                      <code>{slot.slotId}</code>
                    </td>
                    <td>{slot.buildingTypeName ?? '—'}</td>
                    <td>
                      <code>{slot.position.join(', ')}</code>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : (
          <Empty>No block slots reference this cast individual.</Empty>
        )}
      </section>

      <VisualPickerModal
        open={pickerOpen}
        title={`Choose sprite for ${cast.name}`}
        current={{ archive: draft.archive, record: draft.record }}
        onSelect={(visual: VisualOption) => setDraft({ ...draft, archive: visual.archive, record: visual.record })}
        onClose={() => setPickerOpen(false)}
      />
    </>
  );
}
