import { useEffect, useMemo, useState } from 'preact/hooks';
import { ApiError, apiRequest, isAbortError } from '../api';
import type { BarksDocument, PeoplePackOverview, RegionOption, ValidationIssue } from '../types';
import { DirtyBar, ValidationSummary } from '../components/FormControls';
import { Empty, PageHeading } from '../components/ui';

type MapKind =
  | 'byRole'
  | 'byArchetype'
  | 'byRegion'
  | 'byClimate'
  | 'issueTemplatesByRegion'
  | 'issueTemplatesByClimate'
  | 'responseTemplatesByRole'
  | 'contactNamesByRegion'
  | 'contactNamesByClimate';

function normalizeDocument(doc: BarksDocument): BarksDocument {
  const cleanList = (lines?: string[]) => (lines ?? []).map(line => line.trim()).filter(Boolean);
  const cleanMap = (map?: Record<string, string[]>) => {
    const out: Record<string, string[]> = {};
    for (const [k, v] of Object.entries(map ?? {})) {
      out[k] = cleanList(v);
    }
    return out;
  };
  return {
    defaultBarks: cleanList(doc.defaultBarks),
    byRole: cleanMap(doc.byRole),
    byArchetype: cleanMap(doc.byArchetype),
    byRegion: cleanMap(doc.byRegion),
    byClimate: cleanMap(doc.byClimate),
    referrals: {
      defaultIssueTemplates: cleanList(doc.referrals?.defaultIssueTemplates),
      issueTemplatesByClimate: cleanMap(doc.referrals?.issueTemplatesByClimate),
      issueTemplatesByRegion: cleanMap(doc.referrals?.issueTemplatesByRegion),
      defaultResponseTemplates: cleanList(doc.referrals?.defaultResponseTemplates),
      responseTemplatesByRole: cleanMap(doc.referrals?.responseTemplatesByRole),
      contactNames: cleanList(doc.referrals?.contactNames),
      contactNamesByClimate: cleanMap(doc.referrals?.contactNamesByClimate),
      contactNamesByRegion: cleanMap(doc.referrals?.contactNamesByRegion),
    },
  };
}

function linesToText(lines?: string[]): string {
  return (lines ?? []).join('\n');
}

function textToLines(raw: string): string[] {
  return raw.split('\n');
}

export function BarksPage() {
  const [savedDoc, setSavedDoc] = useState<BarksDocument | null>(null);
  const [draft, setDraft] = useState<BarksDocument | null>(null);
  const [overview, setOverview] = useState<PeoplePackOverview | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [saveStatus, setSaveStatus] = useState('');
  const [saveError, setSaveError] = useState<string | null>(null);
  const [issues, setIssues] = useState<ValidationIssue[]>([]);
  const [newKeys, setNewKeys] = useState<Record<string, string>>({});

  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    setError(null);
    Promise.all([
      apiRequest<BarksDocument>('/api/people-pack/barks', { signal: controller.signal }),
      apiRequest<PeoplePackOverview>('/api/people-pack/overview', { signal: controller.signal }),
    ])
      .then(([barks, ov]) => {
        const normalized = normalizeDocument(barks);
        setSavedDoc(normalized);
        setDraft(normalized);
        setOverview(ov);
      })
      .catch(err => {
        if (!isAbortError(err)) setError(err instanceof Error ? err.message : String(err));
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false);
      });
    return () => controller.abort();
  }, []);

  const regionsByKey = useMemo(() => {
    const map = new Map<string, RegionOption>();
    for (const r of overview?.enums.regions ?? []) {
      map.set(r.key, r);
      map.set(r.name.toLowerCase(), r);
    }
    return map;
  }, [overview?.enums.regions]);

  const dirty = useMemo(() => {
    if (!savedDoc || !draft) return false;
    return JSON.stringify(normalizeDocument(savedDoc)) !== JSON.stringify(normalizeDocument(draft));
  }, [savedDoc, draft]);

  async function save() {
    if (!draft) return;
    setSaving(true);
    setSaveError(null);
    setIssues([]);
    setSaveStatus('');
    try {
      const updated = await apiRequest<BarksDocument>('/api/people-pack/barks', {
        method: 'PUT',
        body: normalizeDocument(draft),
      });
      const normalized = normalizeDocument(updated);
      setSavedDoc(normalized);
      setDraft(normalized);
      setSaveStatus('Saved to barks.json.');
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

  function getMap(kind: MapKind): Record<string, string[]> {
    if (!draft) return {};
    switch (kind) {
      case 'byRole':
        return draft.byRole ?? {};
      case 'byArchetype':
        return draft.byArchetype ?? {};
      case 'byRegion':
        return draft.byRegion ?? {};
      case 'byClimate':
        return draft.byClimate ?? {};
      case 'issueTemplatesByRegion':
        return draft.referrals.issueTemplatesByRegion ?? {};
      case 'issueTemplatesByClimate':
        return draft.referrals.issueTemplatesByClimate ?? {};
      case 'responseTemplatesByRole':
        return draft.referrals.responseTemplatesByRole ?? {};
      case 'contactNamesByRegion':
        return draft.referrals.contactNamesByRegion ?? {};
      case 'contactNamesByClimate':
        return draft.referrals.contactNamesByClimate ?? {};
    }
  }

  function setMap(kind: MapKind, next: Record<string, string[]>) {
    if (!draft) return;
    switch (kind) {
      case 'byRole':
        setDraft({ ...draft, byRole: next });
        return;
      case 'byArchetype':
        setDraft({ ...draft, byArchetype: next });
        return;
      case 'byRegion':
        setDraft({ ...draft, byRegion: next });
        return;
      case 'byClimate':
        setDraft({ ...draft, byClimate: next });
        return;
      case 'issueTemplatesByRegion':
        setDraft({ ...draft, referrals: { ...draft.referrals, issueTemplatesByRegion: next } });
        return;
      case 'issueTemplatesByClimate':
        setDraft({ ...draft, referrals: { ...draft.referrals, issueTemplatesByClimate: next } });
        return;
      case 'responseTemplatesByRole':
        setDraft({ ...draft, referrals: { ...draft.referrals, responseTemplatesByRole: next } });
        return;
      case 'contactNamesByRegion':
        setDraft({ ...draft, referrals: { ...draft.referrals, contactNamesByRegion: next } });
        return;
      case 'contactNamesByClimate':
        setDraft({ ...draft, referrals: { ...draft.referrals, contactNamesByClimate: next } });
        return;
    }
  }

  function addMapBucket(kind: MapKind, rawKey: string, seedLine: string) {
    const key = rawKey.trim();
    if (!key) return;
    const current = getMap(kind);
    if (key in current) return;
    setMap(kind, { ...current, [key]: [seedLine] });
    setNewKeys(prev => ({ ...prev, [kind]: '' }));
  }

  function removeMapBucket(kind: MapKind, key: string) {
    const next = { ...getMap(kind) };
    delete next[key];
    setMap(kind, next);
  }

  function renderMapEditor(options: {
    kind: MapKind;
    title: string;
    hint: string;
    seedLine: string;
    keyMode: 'text' | 'region' | 'climate';
    placeholder?: string;
  }) {
    const map = getMap(options.kind);
    const entries = Object.entries(map);
    const climates = overview?.enums.climates ?? [];
    const regions = overview?.enums.regions ?? [];
    const availableClimates = climates.filter(c => !(c in map));
    const availableRegions = regions.filter(r => !(r.key in map));
    const selectedValue =
      newKeys[options.kind] ??
      (options.keyMode === 'climate'
        ? availableClimates[0] ?? ''
        : options.keyMode === 'region'
          ? availableRegions[0]?.key ?? ''
          : '');

    return (
      <section class="panel">
        <div class="section-heading">
          <h2>
            {options.title} ({entries.length})
          </h2>
          <div class="actions">
            {options.keyMode === 'region' ? (
              availableRegions.length ? (
                <>
                  <select
                    aria-label={`Add region to ${options.title}`}
                    value={selectedValue}
                    onChange={event =>
                      setNewKeys(prev => ({ ...prev, [options.kind]: (event.currentTarget as HTMLSelectElement).value }))
                    }
                  >
                    {availableRegions.map(r => (
                      <option key={r.key} value={r.key}>
                        {r.name} ({r.index})
                      </option>
                    ))}
                  </select>
                  <button
                    type="button"
                    onClick={() => addMapBucket(options.kind, selectedValue || availableRegions[0]?.key || '', options.seedLine)}
                  >
                    Add region
                  </button>
                </>
              ) : null
            ) : options.keyMode === 'climate' ? (
              availableClimates.length ? (
                <>
                  <select
                    aria-label={`Add climate to ${options.title}`}
                    value={selectedValue}
                    onChange={event =>
                      setNewKeys(prev => ({ ...prev, [options.kind]: (event.currentTarget as HTMLSelectElement).value }))
                    }
                  >
                    {availableClimates.map(c => (
                      <option key={c} value={c}>
                        {c}
                      </option>
                    ))}
                  </select>
                  <button
                    type="button"
                    onClick={() => addMapBucket(options.kind, selectedValue || availableClimates[0] || '', options.seedLine)}
                  >
                    Add climate
                  </button>
                </>
              ) : null
            ) : (
              <>
                <input
                  type="text"
                  value={selectedValue}
                  placeholder={options.placeholder ?? 'Key'}
                  onInput={event =>
                    setNewKeys(prev => ({ ...prev, [options.kind]: (event.currentTarget as HTMLInputElement).value }))
                  }
                />
                <button type="button" onClick={() => addMapBucket(options.kind, selectedValue, options.seedLine)}>
                  Add key
                </button>
              </>
            )}
          </div>
        </div>
        <p class="muted" style={{ marginBottom: '12px' }}>
          {options.hint}
        </p>
        {entries.length ? (
          <div class="form-grid">
            {entries.map(([key, lines]) => {
              const regionInfo =
                options.keyMode === 'region' ? regionsByKey.get(key) ?? regionsByKey.get(key.toLowerCase()) : undefined;
              return (
                <div key={key} class="regional-block">
                  <div class="section-heading">
                    <h3>
                      {regionInfo ? `${regionInfo.name} (region ${regionInfo.index})` : key} <code>{key}</code>
                    </h3>
                    <button type="button" onClick={() => removeMapBucket(options.kind, key)}>
                      Remove
                    </button>
                  </div>
                  <textarea
                    rows={Math.max(2, Math.min(8, lines.length + 1))}
                    style={{ width: '100%' }}
                    value={linesToText(lines)}
                    onInput={event => {
                      const nextLines = textToLines((event.currentTarget as HTMLTextAreaElement).value);
                      setMap(options.kind, { ...map, [key]: nextLines });
                    }}
                  />
                </div>
              );
            })}
          </div>
        ) : (
          <Empty>No overrides authored yet.</Empty>
        )}
      </section>
    );
  }

  if (loading && !draft) return <p>Loading barks and referral templates…</p>;
  if (error || !draft) {
    return (
      <>
        <PageHeading title="Barks & referrals" />
        <Empty>{error || 'Unable to load daggerfall.people/barks.json.'}</Empty>
      </>
    );
  }

  return (
    <>
      <PageHeading
        title="Barks & referrals"
        intro="Data-driven ambient bark pools and pre-visit referral templates in daggerfall.people/barks.json. Resolution order: role → archetype → region → climate → defaultBarks."
      />

      <ValidationSummary error={saveError} issues={issues} />
      <DirtyBar
        dirty={dirty}
        saving={saving}
        status={saveStatus}
        onSave={() => void save()}
        onRevert={() => {
          if (savedDoc) setDraft(savedDoc);
          setSaveError(null);
          setIssues([]);
        }}
      />

      <section class="panel">
        <h2>Default ambient barks ({draft.defaultBarks.length})</h2>
        <p class="muted" style={{ marginBottom: '8px' }}>
          One bark per line. Mandatory fallback when no role, archetype, region, or climate pool matches.
        </p>
        <textarea
          aria-label="Default ambient barks"
          rows={Math.max(4, draft.defaultBarks.length + 1)}
          style={{ width: '100%' }}
          value={linesToText(draft.defaultBarks)}
          onInput={event =>
            setDraft({ ...draft, defaultBarks: textToLines((event.currentTarget as HTMLTextAreaElement).value) })
          }
        />
      </section>

      {renderMapEditor({
        kind: 'byRole',
        title: 'Ambient barks by role',
        hint: 'One bark per line. Specialized trades (merchant, guard, priest, tavern keeper) override regional street flavor.',
        seedLine: 'New role bark line.',
        keyMode: 'text',
        placeholder: 'e.g. merchant, guard',
      })}

      {renderMapEditor({
        kind: 'byArchetype',
        title: 'Ambient barks by archetype',
        hint: 'One bark per line. Keys must match archetype IDs defined in archetypes.json.',
        seedLine: 'New archetype bark line.',
        keyMode: 'text',
        placeholder: 'e.g. noble, mage, child',
      })}

      {renderMapEditor({
        kind: 'byRegion',
        title: 'Ambient barks by region',
        hint: 'One bark per line. Stored under canonical numeric region index keys (e.g. 17 for Daggerfall, 20 for Sentinel).',
        seedLine: 'New regional bark line.',
        keyMode: 'region',
      })}

      {renderMapEditor({
        kind: 'byClimate',
        title: 'Ambient barks by climate',
        hint: 'One bark per line. Fallback for regions without an explicit region bucket.',
        seedLine: 'New climate bark line.',
        keyMode: 'climate',
      })}

      <section class="panel">
        <h2>Referral issue &amp; response defaults</h2>
        <p class="muted" style={{ marginBottom: '8px' }}>
          Issue templates must include <code>{'{target}'}</code>, <code>{'{contact}'}</code>, and{' '}
          <code>{'{speaker}'}</code>. Response templates must include <code>{'{speaker}'}</code> and{' '}
          <code>{'{tag}'}</code>.
        </p>
        <div class="form-grid">
          <div class="regional-block">
            <h3>Default issue templates</h3>
            <textarea
              aria-label="Default referral issue templates"
              rows={Math.max(3, draft.referrals.defaultIssueTemplates.length + 1)}
              style={{ width: '100%' }}
              value={linesToText(draft.referrals.defaultIssueTemplates)}
              onInput={event =>
                setDraft({
                  ...draft,
                  referrals: {
                    ...draft.referrals,
                    defaultIssueTemplates: textToLines((event.currentTarget as HTMLTextAreaElement).value),
                  },
                })
              }
            />
          </div>
          <div class="regional-block">
            <h3>Default response templates</h3>
            <textarea
              aria-label="Default referral response templates"
              rows={Math.max(3, draft.referrals.defaultResponseTemplates.length + 1)}
              style={{ width: '100%' }}
              value={linesToText(draft.referrals.defaultResponseTemplates)}
              onInput={event =>
                setDraft({
                  ...draft,
                  referrals: {
                    ...draft.referrals,
                    defaultResponseTemplates: textToLines((event.currentTarget as HTMLTextAreaElement).value),
                  },
                })
              }
            />
          </div>
          <div class="regional-block">
            <h3>Default contact names</h3>
            <textarea
              aria-label="Default referral contact names"
              rows={Math.max(3, draft.referrals.contactNames.length + 1)}
              style={{ width: '100%' }}
              value={linesToText(draft.referrals.contactNames)}
              onInput={event =>
                setDraft({
                  ...draft,
                  referrals: {
                    ...draft.referrals,
                    contactNames: textToLines((event.currentTarget as HTMLTextAreaElement).value),
                  },
                })
              }
            />
          </div>
        </div>
      </section>

      {renderMapEditor({
        kind: 'issueTemplatesByRegion',
        title: 'Referral issue templates by region',
        hint: 'Each line must contain {target}, {contact}, and {speaker}.',
        seedLine: 'If you travel to {target}, ask {contact} outside the tavern — tell them {speaker} sent you.',
        keyMode: 'region',
      })}

      {renderMapEditor({
        kind: 'issueTemplatesByClimate',
        title: 'Referral issue templates by climate',
        hint: 'Each line must contain {target}, {contact}, and {speaker}.',
        seedLine: 'Should your road reach {target}, seek {contact} outside the tavern and say {speaker} sent you.',
        keyMode: 'climate',
      })}

      {renderMapEditor({
        kind: 'responseTemplatesByRole',
        title: 'Referral response templates by role',
        hint: 'Each line must contain {speaker} and {tag}.',
        seedLine: "You're looking for {speaker}? Word reached me about '{tag}'.",
        keyMode: 'text',
        placeholder: 'e.g. bystander, merchant',
      })}

      {renderMapEditor({
        kind: 'contactNamesByRegion',
        title: 'Referral contact names by region',
        hint: 'One contact name per line for regional referral targets.',
        seedLine: 'Mara',
        keyMode: 'region',
      })}

      {renderMapEditor({
        kind: 'contactNamesByClimate',
        title: 'Referral contact names by climate',
        hint: 'One contact name per line for climate fallback referral targets.',
        seedLine: 'Mara',
        keyMode: 'climate',
      })}
    </>
  );
}
