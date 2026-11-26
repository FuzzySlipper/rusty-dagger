import { useEffect, useMemo, useState } from 'preact/hooks';
import { apiRequest, isAbortError } from '../api';
import {
  actorSpecUrl,
  actorTemplateUrl,
  actorsUrl,
  blocksUrl,
  encodeKey,
  subjectUrl,
} from '../router';
import type {
  ActorArtLink,
  ActorFieldProvenance,
  ActorPreviewResult,
  ActorSpecDetail,
  ActorStatProvenance,
  ActorTemplateDetail,
  ActorsOverview,
} from '../types';
import { ArtDataCard, ArtDataChip } from '../components/ArtDataLink';
import { Breadcrumb, Chip, Empty, FactList, PageHeading, SpritePreview } from '../components/ui';

function formatValue(val: unknown): string {
  if (val === null || val === undefined) return '—';
  if (typeof val === 'boolean') return val ? 'true' : 'false';
  if (typeof val === 'number' || typeof val === 'string') return String(val);
  if (Array.isArray(val)) {
    if (val.length === 0) return '[]';
    if (val.every(v => typeof v === 'string' || typeof v === 'number')) {
      return val.join(', ');
    }
    return JSON.stringify(val);
  }
  if (typeof val === 'object') {
    const obj = val as Record<string, unknown>;
    if ('minimum' in obj && 'maximum' in obj && Object.keys(obj).length === 2) {
      return `${obj.minimum}–${obj.maximum}`;
    }
    return JSON.stringify(val);
  }
  return String(val);
}

function ProvenanceBadge({ status, careerRef }: { status: ActorFieldProvenance['status']; careerRef?: string | null }) {
  const label =
    status === 'career-default'
      ? `career${careerRef ? ` (${careerRef})` : ''}`
      : status;
  return <span class={`actor-prov-badge prov-${status}`}>{label}</span>;
}

function InheritanceBreadcrumb({ chain }: { chain: Array<{ id: string; branch: string }> }) {
  if (!chain.length) return null;
  return (
    <div class="actor-chain" aria-label="Template inheritance chain">
      {chain.map((node, idx) => (
        <span key={node.id} class="actor-chain-node">
          {idx > 0 ? <span class="actor-chain-sep" aria-hidden="true">→</span> : null}
          {idx === chain.length - 1 ? (
            <strong class="actor-chain-current">
              {node.id} <small>({node.branch})</small>
            </strong>
          ) : (
            <a href={actorTemplateUrl(node.id)}>
              {node.id} <small>({node.branch})</small>
            </a>
          )}
        </span>
      ))}
    </div>
  );
}

function ProvenanceTable({
  fields,
  attributeRows,
  skillRows,
  currentTemplateId,
}: {
  fields: ActorFieldProvenance[];
  attributeRows: ActorStatProvenance[];
  skillRows: ActorStatProvenance[];
  currentTemplateId?: string | null;
}) {
  const [overridesOnly, setOverridesOnly] = useState(false);

  const visibleFields = useMemo(
    () => (overridesOnly ? fields.filter(f => f.status !== 'inherited') : fields),
    [fields, overridesOnly],
  );
  const visibleAttrs = useMemo(
    () => (overridesOnly ? attributeRows.filter(r => r.status !== 'inherited') : attributeRows),
    [attributeRows, overridesOnly],
  );
  const visibleSkills = useMemo(
    () => (overridesOnly ? skillRows.filter(r => r.status !== 'inherited') : skillRows),
    [skillRows, overridesOnly],
  );

  return (
    <section class="panel">
      <div class="section-heading">
        <h2>Inherited vs overridden values</h2>
        <label class="actor-toggle">
          <input
            type="checkbox"
            checked={overridesOnly}
            onChange={e => setOverridesOnly((e.currentTarget as HTMLInputElement).checked)}
          />
          <span>Show local &amp; overridden only</span>
        </label>
      </div>

      <div class="table-wrap">
        <table>
          <thead>
            <tr>
              <th>Field</th>
              <th>Effective value</th>
              <th>Status</th>
              <th>Defined by</th>
            </tr>
          </thead>
          <tbody>
            {visibleFields.map(row => (
              <tr key={row.field} class={row.status !== 'inherited' ? 'actor-row-highlight' : undefined}>
                <td>
                  <code>{row.field}</code>
                </td>
                <td>
                  <code>{formatValue(row.value)}</code>
                  {row.previousValue !== null && row.previousValue !== undefined ? (
                    <span class="muted">
                      {' '}
                      (was <code>{formatValue(row.previousValue)}</code> from {row.previousDefinedBy})
                    </span>
                  ) : null}
                </td>
                <td>
                  <ProvenanceBadge status={row.status} careerRef={row.careerRef} />
                </td>
                <td>
                  {row.definedBy === currentTemplateId ? (
                    <strong>{row.definedBy}</strong>
                  ) : (
                    <a href={actorTemplateUrl(row.definedBy)}>{row.definedBy}</a>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {visibleAttrs.length > 0 || visibleSkills.length > 0 ? (
        <div class="actor-stats-split">
          {visibleAttrs.length > 0 ? (
            <div>
              <h3>Attributes ({visibleAttrs.length})</h3>
              <div class="table-wrap">
                <table>
                  <thead>
                    <tr>
                      <th>Attribute</th>
                      <th>Value</th>
                      <th>Source</th>
                      <th>Defined by</th>
                    </tr>
                  </thead>
                  <tbody>
                    {visibleAttrs.map(row => (
                      <tr key={row.stat}>
                        <td><code>{row.stat}</code></td>
                        <td><strong>{row.value ?? 'cleared'}</strong></td>
                        <td><ProvenanceBadge status={row.status} careerRef={row.careerRef} /></td>
                        <td>
                          <a href={actorTemplateUrl(row.definedBy)}>{row.definedBy}</a>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>
          ) : null}
          {visibleSkills.length > 0 ? (
            <div>
              <h3>Skills ({visibleSkills.length})</h3>
              <div class="table-wrap">
                <table>
                  <thead>
                    <tr>
                      <th>Skill</th>
                      <th>Value</th>
                      <th>Source</th>
                      <th>Defined by</th>
                    </tr>
                  </thead>
                  <tbody>
                    {visibleSkills.map(row => (
                      <tr key={row.stat}>
                        <td><code>{row.stat}</code></td>
                        <td><strong>{row.value ?? 'cleared'}</strong></td>
                        <td><ProvenanceBadge status={row.status} careerRef={row.careerRef} /></td>
                        <td>
                          <a href={actorTemplateUrl(row.definedBy)}>{row.definedBy}</a>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>
          ) : null}
        </div>
      ) : null}
    </section>
  );
}

function EffectivePreviewPanel({
  kind,
  id,
  regions,
  initialGender,
}: {
  kind: 'spec' | 'template';
  id: string;
  regions: Array<{ index: number; name: string }>;
  initialGender?: string;
}) {
  const [level, setLevel] = useState(1);
  const [gender, setGender] = useState(initialGender || 'male');
  const [region, setRegion] = useState<number>(17);
  const [preview, setPreview] = useState<ActorPreviewResult | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    setGender(initialGender || 'male');
  }, [id, kind, initialGender]);

  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    setError(null);
    const params = new URLSearchParams({
      kind,
      id,
      level: String(level),
      gender,
      region: String(region),
    });
    apiRequest<ActorPreviewResult>(`/api/actors/preview?${params.toString()}`, { signal: controller.signal })
      .then(data => setPreview(data))
      .catch(err => {
        if (!isAbortError(err)) setError(err instanceof Error ? err.message : String(err));
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false);
      });
    return () => controller.abort();
  }, [kind, id, level, gender, region]);

  const eff = preview?.effective;

  return (
    <section class="panel" id="actor-effective-preview">
      <div class="section-heading">
        <h2>Resolved effective preview</h2>
        <div class="actor-preview-controls">
          <label>
            Level
            <input
              type="number"
              min={1}
              max={30}
              value={level}
              onInput={e => setLevel(Math.max(1, Math.min(30, Number((e.currentTarget as HTMLInputElement).value) || 1)))}
            />
          </label>
          <label>
            Gender
            <select
              value={gender}
              onChange={e => setGender((e.currentTarget as HTMLSelectElement).value)}
            >
              <option value="male">Male</option>
              <option value="female">Female</option>
              <option value="neutral">Neutral</option>
            </select>
          </label>
          <label>
            Region
            <select
              value={region}
              onChange={e => setRegion(Number((e.currentTarget as HTMLSelectElement).value))}
            >
              {regions.map(r => (
                <option key={r.index} value={r.index}>
                  {r.index}: {r.name}
                </option>
              ))}
            </select>
          </label>
        </div>
      </div>

      {error ? <Empty>{error}</Empty> : null}
      {loading && !eff ? <p class="muted">Computing preview…</p> : null}

      {preview && eff ? (
        <>
          {preview.notes.length ? (
            <ul class="actor-preview-notes">
              {preview.notes.map((n, i) => (
                <li key={i}>{n}</li>
              ))}
            </ul>
          ) : null}

          <div class="actor-preview-grid">
            <div class="actor-preview-card">
              <h3>Identity &amp; vitals</h3>
              <FactList
                facts={{
                  'Effective level': `${eff.level} (${eff.scaling})`,
                  Gender: `${preview.inputs.gender} (allowed: ${preview.inputs.allowedGenders.join(', ')})`,
                  Race: `${eff.race} (${eff.raceSource})`,
                  Health: `${eff.health.minimum}–${eff.health.maximum}${eff.hitPointsPerLevel ? ` (+${eff.hitPointsPerLevel}/lvl)` : ''}`,
                  Armor: eff.armor,
                  ...(eff.fatigue !== null ? { Fatigue: eff.fatigue } : {}),
                  ...(eff.magicka !== null ? { Magicka: eff.magicka } : {}),
                  ...(eff.harm ? { Harm: eff.harm } : {}),
                  ...(eff.interaction ? { Interaction: eff.interaction } : {}),
                  ...(eff.team ? { Team: eff.team } : {}),
                  ...(eff.factionId ? { Faction: `${eff.factionName ?? 'Faction'} (#${eff.factionId})` } : {}),
                  ...(eff.tactic ? { Tactic: eff.tactic } : {}),
                }}
              />
            </div>

            <div class="actor-preview-card">
              <h3>Spells ({eff.spellPolicy})</h3>
              {eff.spells.length ? (
                <div class="usage-pills">
                  {eff.spells.map(sp => (
                    <span key={sp.key} class="art-data-chip">
                      <span>{sp.name}</span>
                      <code>{sp.key}{sp.tier ? ` · T${sp.tier}` : ''}</code>
                    </span>
                  ))}
                </div>
              ) : (
                <p class="muted">No spells ({eff.spellPolicy}).</p>
              )}

              {eff.attacks.length ? (
                <>
                  <h3>Attacks</h3>
                  <div class="usage-pills">
                    {eff.attacks.map((atk, idx) => (
                      <span key={idx} class="art-data-chip">
                        <span>Strike {idx + 1}</span>
                        <code>{atk.minimumDamage}–{atk.maximumDamage} dmg</code>
                      </span>
                    ))}
                  </div>
                </>
              ) : null}

              {eff.loadout.length ? (
                <>
                  <h3>Loadout</h3>
                  <div class="usage-pills">
                    {eff.loadout.map((item, idx) => (
                      <span key={idx} class="art-data-chip">
                        <span>{item.item ?? item.itemId}</span>
                        <code>×{item.quantity}</code>
                      </span>
                    ))}
                  </div>
                </>
              ) : null}
            </div>

            <div class="actor-preview-card">
              <h3>Visual preview ({eff.visual.mode})</h3>
              {eff.visual.source ? <p class="muted">Source: <code>{eff.visual.source}</code></p> : null}
              {eff.visual.mode === 'mobile-texture' ? (
                <div>
                  {eff.visual.textureArchive ? (
                    <p>Texture archive: <code>TEXTURE.{eff.visual.textureArchive}</code></p>
                  ) : null}
                  {eff.visual.art ? (
                    <ArtDataCard
                      href={subjectUrl(eff.visual.art.id)}
                      id={eff.visual.art.id}
                      title={eff.visual.art.name}
                      subtitle={eff.visual.art.kind}
                      thumbnail={eff.visual.art.conceptUrl ?? eff.visual.art.classic}
                    />
                  ) : (
                    <p class="muted">No linked art record.</p>
                  )}
                </div>
              ) : eff.visual.entries.length ? (
                <div class="usage-cards">
                  {eff.visual.entries.slice(0, 6).map((entry, idx) => (
                    <ArtDataCard
                      key={`${entry.archive}-${entry.record}-${idx}`}
                      href={entry.art.npcId ? subjectUrl(entry.art.npcId) : blocksUrl({ q: `${entry.archive}-${entry.record}` })}
                      id={entry.art.npcId ?? `${entry.archive}-${entry.record}`}
                      title={entry.art.npcName ?? `TEXTURE.${entry.archive} rec ${entry.record}`}
                      subtitle={`${entry.gender} · weight ${entry.weight} (${Math.round(entry.share * 100)}%)`}
                      thumbnail={entry.art.conceptUrl ?? entry.art.classic}
                    />
                  ))}
                </div>
              ) : (
                <p class="muted">No visual entries for this selection.</p>
              )}
            </div>
          </div>
        </>
      ) : null}
    </section>
  );
}

function ArtLinksSection({ artLinks }: { artLinks: ActorArtLink[] }) {
  return (
    <section class="panel">
      <h2>Linked art records ({artLinks.length})</h2>
      {artLinks.length ? (
        <div class="usage-cards">
          {artLinks.map((art, idx) => (
            <ArtDataCard
              key={`${art.id}-${art.role ?? idx}`}
              href={subjectUrl(art.id)}
              id={art.id}
              title={art.name}
              subtitle={[art.kind, art.role, art.mobileId !== undefined ? `mobile ${art.mobileId}` : null, art.visualKey].filter(Boolean).join(' · ')}
              thumbnail={art.conceptUrl ?? art.classic}
              badge={{ state: art.hasFinalAsset ? 'approved' : art.hasApprovedConcept ? 'needs-check' : 'empty' }}
            />
          ))}
        </div>
      ) : (
        <Empty>No linked npc-### or creature-### art records.</Empty>
      )}
    </section>
  );
}

function TemplateInspector({
  id,
  regions,
}: {
  id: string;
  regions: Array<{ index: number; name: string }>;
}) {
  const [detail, setDetail] = useState<ActorTemplateDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    setError(null);
    apiRequest<ActorTemplateDetail>(`/api/actors/templates/${encodeKey(id)}`, { signal: controller.signal })
      .then(data => setDetail(data))
      .catch(err => {
        if (!isAbortError(err)) setError(err instanceof Error ? err.message : String(err));
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false);
      });
    return () => controller.abort();
  }, [id]);

  if (loading && !detail) return <p>Loading template…</p>;
  if (error || !detail) {
    return (
      <>
        <Breadcrumb href={actorsUrl()} label="Actors" />
        <PageHeading title="Template not found" />
        <Empty>{error || `Template '${id}' not found.`}</Empty>
      </>
    );
  }

  return (
    <>
      <Breadcrumb href={actorsUrl()} label="Actors" />
      <PageHeading
        title={detail.id}
        intro={`Template · branch: ${detail.branch} · depth ${detail.depth}`}
      />

      <section class="panel">
        <h2>Inheritance</h2>
        <InheritanceBreadcrumb chain={detail.chain} />
        <div class="usage-subsection">
          <h3>Direct children ({detail.directChildren.length})</h3>
          {detail.directChildren.length ? (
            <div class="usage-pills">
              {detail.directChildren.map(child => (
                <ArtDataChip
                  key={child.id}
                  href={actorTemplateUrl(child.id)}
                  id={`${child.branch} · ${child.directSpawnerCount} spawners`}
                  label={child.id}
                />
              ))}
            </div>
          ) : (
            <p class="muted">Leaf template (no direct children).</p>
          )}
        </div>
      </section>

      <EffectivePreviewPanel kind="template" id={detail.id} regions={regions} />

      <ProvenanceTable
        fields={detail.fields}
        attributeRows={detail.attributeRows}
        skillRows={detail.skillRows}
        currentTemplateId={detail.id}
      />

      <section class="panel">
        <h2>Reverse references</h2>
        <div class="usage-subsection">
          <h3>Spawners using this template ({detail.reverseRefs.spawners.length})</h3>
          {detail.reverseRefs.spawners.length ? (
            <div class="usage-cards">
              {detail.reverseRefs.spawners.map(sp => (
                <ArtDataCard
                  key={sp.id}
                  href={actorSpecUrl(sp.id)}
                  id={sp.id}
                  title={sp.displayName}
                  subtitle={`${sp.family} · ${sp.blockSlotCount} block slots · ${sp.encounterTableCount} encounter tables`}
                  thumbnail={sp.primaryArt?.conceptUrl ?? sp.primaryArt?.classic}
                />
              ))}
            </div>
          ) : (
            <p class="muted">No direct spawner specs reference this template.</p>
          )}
        </div>

        {detail.reverseRefs.encounterTables.length ? (
          <div class="usage-subsection">
            <h3>Encounter tables ({detail.reverseRefs.encounterTables.length})</h3>
            <div class="usage-pills">
              {detail.reverseRefs.encounterTables.map(et => (
                <span key={et.tableIndex} class="art-data-chip">
                  <span>Table #{et.tableIndex}</span>
                  <code>{et.slots} slots</code>
                </span>
              ))}
            </div>
          </div>
        ) : null}

        {detail.reverseRefs.boardSeats.length ? (
          <div class="usage-subsection">
            <h3>Political board seats ({detail.reverseRefs.boardSeats.length})</h3>
            <div class="usage-pills">
              {detail.reverseRefs.boardSeats.map(seat => (
                <span key={seat.seatId} class="art-data-chip">
                  <span>{seat.seatName} ({seat.title ?? seat.seatId})</span>
                  <code>{seat.spawnerId ?? seat.canonicalHolder ?? seat.seatId}</code>
                </span>
              ))}
            </div>
          </div>
        ) : null}
      </section>

      <ArtLinksSection artLinks={detail.artLinks} />
    </>
  );
}

function SpecInspector({
  id,
  regions,
}: {
  id: string;
  regions: Array<{ index: number; name: string }>;
}) {
  const [detail, setDetail] = useState<ActorSpecDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    setError(null);
    apiRequest<ActorSpecDetail>(`/api/actors/specs/${encodeKey(id)}`, { signal: controller.signal })
      .then(data => setDetail(data))
      .catch(err => {
        if (!isAbortError(err)) setError(err instanceof Error ? err.message : String(err));
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false);
      });
    return () => controller.abort();
  }, [id]);

  if (loading && !detail) return <p>Loading spawner spec…</p>;
  if (error || !detail) {
    return (
      <>
        <Breadcrumb href={actorsUrl()} label="Actors" />
        <PageHeading title="Spawner spec not found" />
        <Empty>{error || `Spawner spec '${id}' not found.`}</Empty>
      </>
    );
  }

  const defaultGender = detail.genders[0]?.gender ?? 'male';
  const blockFilterHref =
    detail.family === 'cast'
      ? blocksUrl({ castId: detail.id })
      : blocksUrl({ spawner: detail.id });

  return (
    <>
      <Breadcrumb href={actorsUrl()} label="Actors" />
      <PageHeading
        title={detail.displayName}
        intro={`${detail.id} · family: ${detail.family}${detail.template ? ` · template: ${detail.template}` : ''}`}
        action={
          <div class="actions">
            {detail.editorHref ? (
              <a class="button primary" href={detail.editorHref}>
                Edit in {detail.family === 'cast' ? 'Cast' : 'Spawners'} editor
              </a>
            ) : null}
            {detail.reverseRefs.blockSlots.slotCount > 0 ? (
              <a class="button" href={blockFilterHref}>
                {detail.reverseRefs.blockSlots.slotCount.toLocaleString()} block slots
              </a>
            ) : null}
          </div>
        }
      />

      <section class="panel">
        <h2>Spawner spec &amp; template chain</h2>
        <InheritanceBreadcrumb chain={detail.templateChain} />
        <FactList
          facts={{
            ID: detail.id,
            Family: detail.family,
            Published: detail.published ? 'yes' : 'no',
            Genders: detail.genders.map(g => `${g.gender} (w:${g.weight})`).join(', ') || '—',
            ...(detail.race ? { Race: detail.race } : {}),
            ...(detail.role ? { Role: detail.role } : {}),
            ...(detail.naming ? { Naming: detail.naming } : {}),
            ...(detail.harm ? { Harm: detail.harm } : {}),
            ...(detail.interaction ? { Interaction: detail.interaction } : {}),
            ...(detail.team ? { Team: detail.team } : {}),
            ...(detail.factionId ? { Faction: `${detail.factionName ?? 'Faction'} (#${detail.factionId})` } : {}),
            ...(detail.home?.block
              ? {
                  Home: `${detail.home.block}${detail.home.site ? ` @ ${detail.home.site.region}/${detail.home.site.index}` : ''}${detail.home.slotId ? ` · slot ${detail.home.slotId}` : ''}${detail.home.position ? ` · pos [${detail.home.position.join(', ')}]` : ''}`,
                }
              : {}),
            ...(detail.classicBinding && 'mobileId' in detail.classicBinding
              ? { 'Classic mobile ID': String(detail.classicBinding.mobileId) }
              : {}),
            ...(detail.defaultPoolCount > 0
              ? { 'Visual pools': `${detail.defaultPoolCount} default · ${detail.regionalPoolCount} regional · ${detail.climatePoolCount} climate` }
              : {}),
          }}
        />
      </section>

      <EffectivePreviewPanel
        kind="spec"
        id={detail.id}
        regions={regions}
        initialGender={defaultGender}
      />

      <section class="panel">
        <h2>Reverse references</h2>

        <div class="usage-subsection">
          <h3>Board seats ({detail.reverseRefs.boardSeats.length})</h3>
          {detail.reverseRefs.boardSeats.length ? (
            <div class="usage-cards">
              {detail.reverseRefs.boardSeats.map(seat => (
                <article key={seat.seatId} class="panel">
                  <strong>{seat.seatName}</strong>
                  <p class="muted">
                    Seat <code>{seat.seatId}</code> · {seat.title ?? 'Seat'} · {seat.kind ?? 'Power'}
                    {seat.officeFactionId ? ` · Office faction #${seat.officeFactionId} (${seat.officeFactionName ?? ''})` : ''}
                    {seat.home ? ` · Home: ${seat.home}` : ''}
                  </p>
                </article>
              ))}
            </div>
          ) : (
            <p class="muted">Not bound to a political board seat.</p>
          )}
        </div>

        {detail.reverseRefs.offices.length ? (
          <div class="usage-subsection">
            <h3>Faction &amp; office lineage</h3>
            <div class="usage-pills">
              {detail.reverseRefs.offices.map(off => (
                <span key={off.factionId} class="art-data-chip">
                  <span>{off.factionName ?? `Faction ${off.factionId}`}</span>
                  <code>
                    #{off.factionId}
                    {off.parentFactionName ? ` → ${off.parentFactionName} (#${off.parentFactionId})` : ''}
                  </code>
                </span>
              ))}
            </div>
          </div>
        ) : null}

        <div class="usage-subsection">
          <h3>Encounter tables ({detail.reverseRefs.encounterTables.length})</h3>
          {detail.reverseRefs.encounterTables.length ? (
            <div class="usage-pills">
              {detail.reverseRefs.encounterTables.map(et => (
                <span key={et.tableIndex} class="art-data-chip">
                  <span>Table #{et.tableIndex}</span>
                  <code>{et.slots}/{et.totalSlots} ({Math.round(et.share * 100)}%)</code>
                </span>
              ))}
            </div>
          ) : (
            <p class="muted">Not referenced in any random encounter table.</p>
          )}
        </div>

        <div class="usage-subsection">
          <h3>Block slots ({detail.reverseRefs.blockSlots.slotCount.toLocaleString()})</h3>
          {detail.reverseRefs.blockSlots.slotCount > 0 ? (
            <>
              <p>
                Placed across <strong>{detail.reverseRefs.blockSlots.blockCount.toLocaleString()}</strong> blocks.{' '}
                <a href={blockFilterHref}>Inspect slots in Block slots →</a>
              </p>
              {detail.reverseRefs.blockSlots.blocks.length ? (
                <div class="usage-pills">
                  {detail.reverseRefs.blockSlots.blocks.map(b => (
                    <a key={b} class="art-data-chip" href={blocksUrl({ q: b })}>
                      <span>{b}</span>
                    </a>
                  ))}
                </div>
              ) : null}
            </>
          ) : (
            <p class="muted">No static block slot references.</p>
          )}
        </div>
      </section>

      <ArtLinksSection artLinks={detail.artLinks} />

      <ProvenanceTable
        fields={detail.templateFields}
        attributeRows={detail.attributeRows}
        skillRows={detail.skillRows}
        currentTemplateId={detail.template}
      />
    </>
  );
}

export function ActorsPage({ kind, id }: { kind?: 'template' | 'spec'; id?: string }) {
  const [overview, setOverview] = useState<ActorsOverview | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [tab, setTab] = useState<'specs' | 'templates'>('specs');
  const [familyFilter, setFamilyFilter] = useState<string>('all');
  const [branchFilter, setBranchFilter] = useState<string>('all');
  const [search, setSearch] = useState('');

  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    setError(null);
    apiRequest<ActorsOverview>('/api/actors/overview', { signal: controller.signal })
      .then(data => setOverview(data))
      .catch(err => {
        if (!isAbortError(err)) setError(err instanceof Error ? err.message : String(err));
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false);
      });
    return () => controller.abort();
  }, []);

  const regions = overview?.enums.regions ?? [];

  if (kind === 'template' && id) {
    return <TemplateInspector id={id} regions={regions} />;
  }
  if (kind === 'spec' && id) {
    return <SpecInspector id={id} regions={regions} />;
  }

  if (loading && !overview) return <p>Loading unified actors catalog…</p>;
  if (error || !overview || !overview.available) {
    return (
      <>
        <PageHeading title="Actors" />
        <Empty>{error || 'Unified actor templates/specs catalog is unavailable.'}</Empty>
      </>
    );
  }

  const term = search.trim().toLowerCase();
  const filteredSpecs = overview.specs.filter(sp => {
    if (familyFilter !== 'all' && sp.family !== familyFilter) return false;
    if (!term) return true;
    return `${sp.id} ${sp.displayName} ${sp.template ?? ''} ${sp.family} ${sp.role ?? ''} ${sp.race ?? ''}`
      .toLowerCase()
      .includes(term);
  });
  const filteredTemplates = overview.templates.filter(tpl => {
    if (branchFilter !== 'all' && tpl.branch !== branchFilter) return false;
    if (!term) return true;
    return `${tpl.id} ${tpl.branch} ${tpl.extends ?? ''} ${tpl.careerRef ?? ''} ${tpl.tactic ?? ''}`
      .toLowerCase()
      .includes(term);
  });

  return (
    <>
      <PageHeading
        title="Actors"
        intro={`${overview.counts.templates} templates · ${overview.counts.specs} spawner specs across creatures, enemies, townsfolk, and cast`}
        action={
          <div class="actions">
            <a class="button" href="#cast">Cast editor</a>
            <a class="button" href="#spawners">Spawners editor</a>
          </div>
        }
      />

      <div class="panel actor-toolbar">
        <div class="actor-tabs" role="tablist">
          <button
            type="button"
            class={tab === 'specs' ? 'primary' : undefined}
            onClick={() => setTab('specs')}
          >
            Spawners &amp; Cast ({overview.counts.specs})
          </button>
          <button
            type="button"
            class={tab === 'templates' ? 'primary' : undefined}
            onClick={() => setTab('templates')}
          >
            Templates ({overview.counts.templates})
          </button>
        </div>

        <input
          type="search"
          placeholder={tab === 'specs' ? 'Filter specs by id, name, template, family…' : 'Filter templates by id, branch, parent…'}
          value={search}
          onInput={e => setSearch((e.currentTarget as HTMLInputElement).value)}
        />

        {tab === 'specs' ? (
          <div class="actor-filter-pills">
            {(['all', ...overview.enums.families] as string[]).map(fam => (
              <button
                key={fam}
                type="button"
                class={familyFilter === fam ? 'primary' : undefined}
                onClick={() => setFamilyFilter(fam)}
              >
                {fam === 'all' ? 'All' : `${fam} (${overview.counts.byFamily[fam] ?? 0})`}
              </button>
            ))}
          </div>
        ) : (
          <div class="actor-filter-pills">
            {(['all', ...overview.enums.branches] as string[]).map(br => (
              <button
                key={br}
                type="button"
                class={branchFilter === br ? 'primary' : undefined}
                onClick={() => setBranchFilter(br)}
              >
                {br === 'all' ? 'All' : `${br} (${overview.counts.byBranch[br] ?? 0})`}
              </button>
            ))}
          </div>
        )}
      </div>

      {tab === 'specs' ? (
        <section class="panel">
          <div class="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Spec</th>
                  <th>Family</th>
                  <th>Template</th>
                  <th>Art</th>
                  <th>Refs</th>
                  <th>Actions</th>
                </tr>
              </thead>
              <tbody>
                {filteredSpecs.map(sp => (
                  <tr key={sp.id}>
                    <td>
                      <a href={actorSpecUrl(sp.id)}>
                        <strong>{sp.displayName}</strong>
                      </a>
                      <div><code>{sp.id}</code></div>
                    </td>
                    <td>
                      <Chip state={sp.family} />
                    </td>
                    <td>
                      {sp.template ? <a href={actorTemplateUrl(sp.template)}>{sp.template}</a> : '—'}
                    </td>
                    <td>
                      {sp.primaryArt ? (
                        <a href={subjectUrl(sp.primaryArt.id)} class="actor-art-inline">
                          {sp.primaryArt.classic ? (
                            <SpritePreview url={sp.primaryArt.conceptUrl ?? sp.primaryArt.classic} alt={sp.primaryArt.name} pixel />
                          ) : null}
                          <span>{sp.primaryArt.id}</span>
                        </a>
                      ) : (
                        <span class="muted">—</span>
                      )}
                    </td>
                    <td class="muted">
                      {[
                        sp.encounterTableCount ? `${sp.encounterTableCount} enc` : null,
                        sp.blockSlotCount ? `${sp.blockSlotCount.toLocaleString()} slots` : null,
                        sp.boardSeatCount ? `${sp.boardSeatCount} seat` : null,
                      ]
                        .filter(Boolean)
                        .join(' · ') || '—'}
                    </td>
                    <td>
                      <a href={actorSpecUrl(sp.id)}>Inspect</a>
                      {sp.editorHref ? (
                        <>
                          {' · '}
                          <a href={sp.editorHref}>Edit</a>
                        </>
                      ) : null}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          {filteredSpecs.length === 0 ? <Empty>No matching spawner specs.</Empty> : null}
        </section>
      ) : (
        <section class="panel">
          <div class="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Template</th>
                  <th>Branch</th>
                  <th>Inherits from</th>
                  <th>Children</th>
                  <th>Spawners</th>
                  <th>Scaling / Spells</th>
                </tr>
              </thead>
              <tbody>
                {filteredTemplates.map(tpl => (
                  <tr key={tpl.id}>
                    <td>
                      <a href={actorTemplateUrl(tpl.id)}>
                        <strong>{tpl.id}</strong>
                      </a>
                    </td>
                    <td>
                      <Chip state={tpl.branch} />
                    </td>
                    <td>
                      {tpl.extends ? <a href={actorTemplateUrl(tpl.extends)}>{tpl.extends}</a> : <span class="muted">root</span>}
                    </td>
                    <td>{tpl.directChildrenCount}</td>
                    <td>
                      {tpl.directSpawnerCount}
                      {tpl.descendantSpawnerCount > 0 ? <span class="muted"> (+{tpl.descendantSpawnerCount} via children)</span> : null}
                    </td>
                    <td class="muted">
                      {[tpl.scaling, tpl.spellPolicy && tpl.spellPolicy !== 'none' ? `spells:${tpl.spellPolicy}` : null, tpl.careerRef]
                        .filter(Boolean)
                        .join(' · ') || '—'}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          {filteredTemplates.length === 0 ? <Empty>No matching templates.</Empty> : null}
        </section>
      )}
    </>
  );
}
