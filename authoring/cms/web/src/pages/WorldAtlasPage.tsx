import { useEffect, useMemo, useState } from 'preact/hooks';
import { apiRequest, isAbortError } from '../api';
import { castUrl, locationsUrl, locationUrl, spawnerUrl, worldMapUrl } from '../router';
import type {
  AtlasBaseMode,
  AtlasOverlayMode,
  WorldAtlasMasksResponse,
  WorldAtlasOverviewResponse,
  WorldAtlasPinsResponse,
  WorldAtlasPinTuple,
  WorldAtlasRegion,
} from '../types';
import {
  AtlasCanvasView,
  decodeAtlasMasks,
  regionPaletteRgb,
  type AtlasFlyToRequest,
  type DecodedAtlasMasks,
} from '../atlas/AtlasCanvasView';
import { Chip, Empty, FactList, PageHeading } from '../components/ui';

const LOCATION_TYPE_LABELS: Record<number, string> = {
  0: 'City',
  1: 'Town / Hamlet',
  2: 'Village',
  3: 'Farmstead',
  4: 'Dungeon Labyrinth',
  5: 'Temple',
  6: 'Tavern',
  7: 'Dungeon Keep',
  8: 'Manor / Home',
  9: 'Cult / Lodge',
  10: 'Ruins',
  11: 'Poor Home',
  12: 'Graveyard',
  13: 'Coven',
  14: 'Player Ship',
};

const OVERLAY_OPTIONS: Array<{ id: AtlasOverlayMode; label: string }> = [
  { id: 'geography', label: 'Geography' },
  { id: 'political', label: 'Political' },
  { id: 'diplomacy', label: 'Diplomacy' },
  { id: 'climate', label: 'Climate' },
  { id: 'power', label: 'Power' },
  { id: 'density', label: 'Density' },
];

type AtlasInspectorTab = 'politics' | 'factions' | 'locations';

function rgbCss([r, g, b]: [number, number, number]): string {
  return `rgb(${r}, ${g}, ${b})`;
}

export function WorldAtlasPage({ initialRegion }: { initialRegion?: number }) {
  const [overview, setOverview] = useState<WorldAtlasOverviewResponse | null>(null);
  const [masks, setMasks] = useState<DecodedAtlasMasks | null>(null);
  const [pins, setPins] = useState<WorldAtlasPinTuple[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const [baseMode, setBaseMode] = useState<AtlasBaseMode>('painted');
  const [overlayMode, setOverlayMode] = useState<AtlasOverlayMode>('political');
  const [enabledCategories, setEnabledCategories] = useState<Record<string, boolean>>({
    city: true,
    town: false,
    temple: false,
    dungeon: false,
    home: false,
    other: false,
  });
  const [showLabels, setShowLabels] = useState(true);
  const [showDiplomacyArcs, setShowDiplomacyArcs] = useState(true);

  const [selectedRegionId, setSelectedRegionId] = useState<number | null>(initialRegion ?? null);
  const [selectedPin, setSelectedPin] = useState<WorldAtlasPinTuple | null>(null);
  const [inspectorTab, setInspectorTab] = useState<AtlasInspectorTab>('politics');
  const [flyToRequest, setFlyToRequest] = useState<AtlasFlyToRequest | null>(null);
  const [mapSearch, setMapSearch] = useState('');
  const [regionLocFilter, setRegionLocFilter] = useState('');

  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    setError(null);
    Promise.all([
      apiRequest<WorldAtlasOverviewResponse>('/api/world-atlas/overview', { signal: controller.signal }),
      apiRequest<WorldAtlasMasksResponse>('/api/world-atlas/masks', { signal: controller.signal }),
      apiRequest<WorldAtlasPinsResponse>('/api/world-atlas/pins', { signal: controller.signal }),
    ])
      .then(([ov, rawMasks, pinResp]) => {
        setOverview(ov);
        if (rawMasks.politicRuns.length > 0) {
          setMasks(decodeAtlasMasks(rawMasks));
        }
        setPins(pinResp.pins);
        if (!ov.atlas.available && ov.classicMaps.available) {
          setBaseMode('classic');
        }
      })
      .catch(err => {
        if (!isAbortError(err)) setError(err instanceof Error ? err.message : String(err));
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false);
      });
    return () => controller.abort();
  }, []);

  const regionsById = useMemo(() => {
    const map = new Map<number, WorldAtlasRegion>();
    for (const r of overview?.regions ?? []) map.set(r.id, r);
    return map;
  }, [overview]);

  // Sync URL region parameter if navigated to `#world-map/<region>`
  useEffect(() => {
    if (initialRegion === undefined || !overview) return;
    const reg = regionsById.get(initialRegion);
    if (!reg) return;
    setSelectedRegionId(reg.id);
    setSelectedPin(null);
    if (reg.bounds) {
      setFlyToRequest({ id: Date.now(), bounds: reg.bounds });
    } else {
      setFlyToRequest({ id: Date.now(), x: reg.centroid.x, y: reg.centroid.y, zoom: 4 });
    }
  }, [initialRegion, overview, regionsById]);

  const selectedRegion = selectedRegionId !== null ? regionsById.get(selectedRegionId) ?? null : null;

  const regionPins = useMemo(() => {
    if (selectedRegionId === null) return [];
    const term = regionLocFilter.trim().toLowerCase();
    const list = pins.filter(p => p[0] === selectedRegionId && (!term || p[6].toLowerCase().includes(term)));
    // Cities first, then towns/villages, then temples, then dungeons, then alphabetically
    list.sort((a, b) => {
      const rank = (t: number) => (t === 0 ? 0 : t === 1 || t === 2 ? 1 : t === 5 ? 2 : t === 4 || t === 7 ? 3 : 4);
      return rank(a[4]) - rank(b[4]) || a[6].localeCompare(b[6]);
    });
    return list;
  }, [pins, selectedRegionId, regionLocFilter]);

  const quickSearchResults = useMemo(() => {
    const term = mapSearch.trim().toLowerCase();
    if (!term || !overview) return { regions: [] as WorldAtlasRegion[], pins: [] as WorldAtlasPinTuple[] };
    const matchedRegions = overview.regions
      .filter(r => r.name.toLowerCase().includes(term) || (r.province?.regentName ?? '').toLowerCase().includes(term))
      .slice(0, 8);
    const matchedPins: WorldAtlasPinTuple[] = [];
    for (let i = 0; i < pins.length && matchedPins.length < 18; i++) {
      if (pins[i][6].toLowerCase().includes(term)) {
        matchedPins.push(pins[i]);
      }
    }
    return { regions: matchedRegions, pins: matchedPins };
  }, [mapSearch, overview, pins]);

  const sortedRealms = useMemo(() => {
    return [...(overview?.regions ?? [])]
      .filter(r => r.hasTerritory || r.locations.total > 0)
      .sort((a, b) => (b.province?.power ?? 0) - (a.province?.power ?? 0) || b.locations.cities - a.locations.cities || a.name.localeCompare(b.name));
  }, [overview]);

  function focusRegion(region: WorldAtlasRegion) {
    setSelectedRegionId(region.id);
    setSelectedPin(null);
    setRegionLocFilter('');
    if (region.bounds) {
      setFlyToRequest({ id: Date.now() + Math.random(), bounds: region.bounds });
    } else {
      setFlyToRequest({ id: Date.now() + Math.random(), x: region.centroid.x, y: region.centroid.y, zoom: 4.5 });
    }
  }

  function focusPin(pin: WorldAtlasPinTuple) {
    setSelectedPin(pin);
    setSelectedRegionId(pin[0]);
    // Ensure the pin's category is enabled so the user sees it immediately
    const cat = overview?.pinCategories.find(c => c.types.includes(pin[4]));
    if (cat && !enabledCategories[cat.id]) {
      setEnabledCategories(prev => ({ ...prev, [cat.id]: true }));
    }
    setFlyToRequest({ id: Date.now() + Math.random(), x: pin[2] + 0.5, y: pin[3] + 0.5, zoom: 7.5 });
  }

  if (loading && !overview) {
    return (
      <>
        <PageHeading title="World & political atlas" />
        <p>Loading Iliac Bay relief tiles, political regions, diplomacy graph, and locations…</p>
      </>
    );
  }

  if (error || !overview) {
    return (
      <>
        <PageHeading title="World & political atlas" />
        <Empty>{error ?? 'Could not load World Atlas data.'}</Empty>
      </>
    );
  }

  if (!overview.available) {
    return (
      <>
        <PageHeading title="World & political atlas" />
        <Empty>{overview.reason ?? 'Staged world atlas data is not available.'}</Empty>
      </>
    );
  }

  const climateNameByVal = new Map(overview.climates.map(c => [c.value, c.name]));

  return (
    <>
      <PageHeading
        title="World & political atlas"
        intro={`${overview.counts.inhabitedRegions} inhabited realms · ${overview.counts.locations.toLocaleString()} mapped locations · ${overview.counts.diplomaticLinks} inter-realm diplomatic ties over eroded Iliac Bay relief`}
        action={
          <div class="actions">
            <a class="button" href={locationsUrl()}>
              Locations table
            </a>
          </div>
        }
      />

      <div class="atlas-toolbar-stack">
        <div class="block-viewport-toolbar">
          <div class="art-mode-toggle" role="group" aria-label="Base map art">
            <button
              type="button"
              class={baseMode === 'painted' ? 'active' : undefined}
              aria-pressed={baseMode === 'painted' ? 'true' : 'false'}
              disabled={!overview.atlas.available}
              onClick={() => setBaseMode('painted')}
            >
              Painted atlas
            </button>
            <button
              type="button"
              class={baseMode === 'classic' ? 'active' : undefined}
              aria-pressed={baseMode === 'classic' ? 'true' : 'false'}
              disabled={!overview.classicMaps.available}
              onClick={() => setBaseMode('classic')}
            >
              Classic map
            </button>
          </div>

          <div class="art-mode-toggle" role="group" aria-label="Atlas overlay mode">
            {OVERLAY_OPTIONS.map(opt => (
              <button
                key={opt.id}
                type="button"
                class={overlayMode === opt.id ? 'active' : undefined}
                aria-pressed={overlayMode === opt.id ? 'true' : 'false'}
                onClick={() => setOverlayMode(opt.id)}
              >
                {opt.label}
              </button>
            ))}
          </div>

          <div class="atlas-search-wrap">
            <input
              type="search"
              aria-label="Find region or location"
              placeholder="Jump to realm, ruler, or location…"
              value={mapSearch}
              onInput={event => setMapSearch((event.currentTarget as HTMLInputElement).value)}
            />
            {mapSearch.trim() && (quickSearchResults.regions.length > 0 || quickSearchResults.pins.length > 0) ? (
              <div class="atlas-search-dropdown panel" role="listbox">
                {quickSearchResults.regions.map(r => (
                  <button
                    key={`reg-${r.id}`}
                    type="button"
                    class="atlas-search-item"
                    onClick={() => {
                      focusRegion(r);
                      setMapSearch('');
                    }}
                  >
                    <strong>{r.name}</strong>
                    <span class="muted">
                      Realm{r.province?.regentName ? ` · ${r.province.regentName}` : ''} · {r.locations.total} places
                    </span>
                  </button>
                ))}
                {quickSearchResults.pins.map(p => (
                  <button
                    key={`pin-${p[0]}-${p[1]}`}
                    type="button"
                    class="atlas-search-item"
                    onClick={() => {
                      focusPin(p);
                      setMapSearch('');
                    }}
                  >
                    <strong>{p[6]}</strong>
                    <span class="muted">
                      {LOCATION_TYPE_LABELS[p[4]] ?? 'Location'} · {regionsById.get(p[0])?.name ?? `Region ${p[0]}`}
                    </span>
                  </button>
                ))}
              </div>
            ) : null}
          </div>
        </div>

        <div class="appearance-bar">
          <div class="layer-toggles" role="group" aria-label="Location pin categories">
            {overview.pinCategories.map(cat => (
              <button
                key={cat.id}
                type="button"
                class={`layer-pill${enabledCategories[cat.id] ? ' active' : ''}`}
                aria-pressed={enabledCategories[cat.id] ? 'true' : 'false'}
                onClick={() => setEnabledCategories(prev => ({ ...prev, [cat.id]: !prev[cat.id] }))}
              >
                <span class="atlas-dot" style={{ background: cat.color }} /> {cat.label}
              </button>
            ))}
          </div>
          <div class="actions" style={{ marginLeft: 'auto' }}>
            <label class="slice-control">
              <input
                type="checkbox"
                checked={showLabels}
                onChange={event => setShowLabels((event.currentTarget as HTMLInputElement).checked)}
              />{' '}
              Labels
            </label>
            {overlayMode === 'diplomacy' ? (
              <label class="slice-control">
                <input
                  type="checkbox"
                  checked={showDiplomacyArcs}
                  onChange={event => setShowDiplomacyArcs((event.currentTarget as HTMLInputElement).checked)}
                />{' '}
                Diplomacy arcs
              </label>
            ) : null}
          </div>
        </div>

        {overlayMode === 'climate' ? (
          <div class="atlas-legend">
            {overview.climates
              .filter(c => c.name !== 'Ocean')
              .map(c => (
                <span key={c.value} class="atlas-legend-item">
                  <span class="atlas-swatch" style={{ background: c.color }} /> {c.name}
                </span>
              ))}
          </div>
        ) : overlayMode === 'diplomacy' ? (
          <div class="atlas-legend">
            <span class="atlas-legend-item">
              <span class="atlas-swatch" style={{ background: '#ffd166' }} /> Selected realm
            </span>
            <span class="atlas-legend-item">
              <span class="atlas-swatch" style={{ background: '#4de096' }} /> Allied realm / tie
            </span>
            <span class="atlas-legend-item">
              <span class="atlas-swatch" style={{ background: '#ff5866' }} /> Hostile realm / rivalry
            </span>
          </div>
        ) : overlayMode === 'power' || overlayMode === 'density' ? (
          <div class="atlas-legend">
            <span class="muted">
              {overlayMode === 'power' ? 'Province faction power:' : 'Settlement density:'}
            </span>
            <span class="atlas-legend-item">
              <span
                class="atlas-gradient-bar"
                style={{
                  background:
                    overlayMode === 'power'
                      ? 'linear-gradient(90deg, rgb(52,86,110), rgb(235,96,68))'
                      : 'linear-gradient(90deg, rgb(44,72,68), rgb(244,200,74))',
                }}
              />
              Low → High
            </span>
          </div>
        ) : null}
      </div>

      <div class="block-viewer-layout atlas-layout">
        <div class="block-viewer-main">
          <AtlasCanvasView
            overview={overview}
            masks={masks}
            pins={pins}
            baseMode={baseMode}
            overlayMode={overlayMode}
            enabledCategories={enabledCategories}
            showLabels={showLabels}
            showDiplomacyArcs={showDiplomacyArcs}
            selectedRegionId={selectedRegionId}
            selectedPin={selectedPin}
            flyToRequest={flyToRequest}
            onSelectRegion={regId => {
              setSelectedRegionId(regId);
              setRegionLocFilter('');
            }}
            onSelectPin={pin => setSelectedPin(pin)}
          />
        </div>

        <aside class="block-inspector panel atlas-inspector">
          {selectedPin ? (
            <div class="selected-pick-card">
              <div class="section-heading" style={{ marginBottom: '4px' }}>
                <strong>{selectedPin[6]}</strong>
                <button type="button" onClick={() => setSelectedPin(null)}>
                  Clear
                </button>
              </div>
              <div class="muted" style={{ fontSize: '0.82rem', marginBottom: '8px' }}>
                {LOCATION_TYPE_LABELS[selectedPin[4]] ?? `Type ${selectedPin[4]}`} ·{' '}
                {regionsById.get(selectedPin[0])?.name ?? `Region ${selectedPin[0]}`} · ({selectedPin[2]},{' '}
                {selectedPin[3]})
                {selectedPin[7] !== null && climateNameByVal.has(selectedPin[7])
                  ? ` · ${climateNameByVal.get(selectedPin[7])}`
                  : ''}
              </div>
              <div class="actions">
                <a class="button primary" href={locationUrl(selectedPin[0], selectedPin[1])}>
                  Open 3D Town →
                </a>
                {selectedPin[5] === 1 ? (
                  <a class="button" href={locationUrl(selectedPin[0], selectedPin[1], 'dungeon')}>
                    3D Dungeon →
                  </a>
                ) : null}
              </div>
            </div>
          ) : null}

          {!selectedRegion ? (
            <>
              <div class="section-heading" style={{ marginBottom: '8px' }}>
                <strong>Iliac Bay realms ({sortedRealms.length})</strong>
              </div>
              <p class="muted" style={{ marginTop: 0, fontSize: '0.82rem' }}>
                Click any region on the map or pick a realm below to inspect its politics, diplomacy, factions, and
                settlements.
              </p>
              <div class="inspector-scroll">
                <ul class="inspector-list">
                  {sortedRealms.map(r => (
                    <li key={r.id} class="inspector-row">
                      <button type="button" class="inspector-focus-btn" onClick={() => focusRegion(r)}>
                        <div style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
                          <span class="atlas-dot" style={{ background: rgbCss(regionPaletteRgb(r.id)) }} />
                          <strong>{r.name}</strong>
                          {r.province ? <small class="muted">· Power {r.province.power}</small> : null}
                        </div>
                        <div class="muted" style={{ fontSize: '0.78rem' }}>
                          {r.locations.cities} cities · {r.locations.total.toLocaleString()} places
                          {r.diplomacy.allyRegions.length + r.diplomacy.enemyRegions.length > 0
                            ? ` · ${r.diplomacy.allyRegions.length}A / ${r.diplomacy.enemyRegions.length}E`
                            : ''}
                        </div>
                      </button>
                    </li>
                  ))}
                </ul>
              </div>
            </>
          ) : (
            <>
              <div class="section-heading" style={{ marginBottom: '6px' }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: '8px', minWidth: 0 }}>
                  <span class="atlas-swatch" style={{ background: rgbCss(regionPaletteRgb(selectedRegion.id)) }} />
                  <strong>
                    {selectedRegion.name} <small class="muted">(#{selectedRegion.id})</small>
                  </strong>
                </div>
                <div class="actions">
                  <button type="button" onClick={() => focusRegion(selectedRegion)}>
                    Zoom
                  </button>
                  <button
                    type="button"
                    onClick={() => {
                      setSelectedRegionId(null);
                      setSelectedPin(null);
                    }}
                  >
                    All realms
                  </button>
                </div>
              </div>

              <div class="inspector-tabs" role="tablist">
                <button
                  type="button"
                  role="tab"
                  aria-selected={inspectorTab === 'politics' ? 'true' : 'false'}
                  class={inspectorTab === 'politics' ? 'active' : undefined}
                  onClick={() => setInspectorTab('politics')}
                >
                  Politics
                </button>
                <button
                  type="button"
                  role="tab"
                  aria-selected={inspectorTab === 'factions' ? 'true' : 'false'}
                  class={inspectorTab === 'factions' ? 'active' : undefined}
                  onClick={() => setInspectorTab('factions')}
                >
                  Factions ({selectedRegion.factions.length})
                </button>
                <button
                  type="button"
                  role="tab"
                  aria-selected={inspectorTab === 'locations' ? 'true' : 'false'}
                  class={inspectorTab === 'locations' ? 'active' : undefined}
                  onClick={() => setInspectorTab('locations')}
                >
                  Locations ({selectedRegion.locations.total})
                </button>
              </div>

              <div class="inspector-scroll">
                {inspectorTab === 'politics' ? (
                  <>
                    <FactList
                      facts={{
                        Province: selectedRegion.province
                          ? `${selectedRegion.province.name} (Power ${selectedRegion.province.power})`
                          : 'Unclaimed / Wild',
                        ...(selectedRegion.province?.rulerTitle
                          ? {
                              Ruler: selectedRegion.province.regentName
                                ? `${selectedRegion.province.rulerTitle} · ${selectedRegion.province.regentName}`
                                : selectedRegion.province.rulerTitle,
                            }
                          : {}),
                        ...(selectedRegion.court ? { Court: selectedRegion.court.name } : {}),
                        ...(selectedRegion.people ? { People: selectedRegion.people.name } : {}),
                        Territory: `${selectedRegion.pixelCount.toLocaleString()} map px (~${Math.round(selectedRegion.pixelCount * 0.67).toLocaleString()} km²)`,
                        'Dominant climate': selectedRegion.dominantClimate ?? '—',
                        Settlements: `${selectedRegion.locations.cities} cities · ${selectedRegion.locations.townsAndVillages} towns/villages · ${selectedRegion.locations.dungeons} dungeons`,
                      }}
                    />

                    {selectedRegion.climates.length > 0 ? (
                      <div class="variant-section">
                        <strong style={{ fontSize: '0.82rem' }}>Climate mix</strong>
                        <div class="atlas-climate-bar" style={{ marginTop: '6px' }}>
                          {selectedRegion.climates.map(c => (
                            <div
                              key={c.value}
                              style={{ width: `${Math.max(2, c.share * 100)}%`, background: c.color }}
                              title={`${c.name}: ${(c.share * 100).toFixed(1)}%`}
                            />
                          ))}
                        </div>
                        <div class="usage-pills" style={{ marginTop: '6px' }}>
                          {selectedRegion.climates.slice(0, 4).map(c => (
                            <span key={c.value} class="chip">
                              <span class="atlas-dot" style={{ background: c.color }} /> {c.name}{' '}
                              {(c.share * 100).toFixed(0)}%
                            </span>
                          ))}
                        </div>
                      </div>
                    ) : null}

                    <div class="variant-section">
                      <div class="section-heading" style={{ marginBottom: '6px' }}>
                        <strong>Diplomacy & faction ties</strong>
                        {overlayMode !== 'diplomacy' ? (
                          <button type="button" onClick={() => setOverlayMode('diplomacy')}>
                            Show arcs
                          </button>
                        ) : null}
                      </div>

                      {selectedRegion.diplomacy.allyRegions.length > 0 ? (
                        <div style={{ marginBottom: '6px' }}>
                          <div class="muted" style={{ fontSize: '0.78rem', marginBottom: '4px' }}>
                            Allied realms
                          </div>
                          <div class="usage-pills">
                            {selectedRegion.diplomacy.allyRegions.map(rid => {
                              const other = regionsById.get(rid);
                              return (
                                <button
                                  key={rid}
                                  type="button"
                                  class="chip chip-ally"
                                  onClick={() => other && focusRegion(other)}
                                >
                                  {other?.name ?? `Region ${rid}`} →
                                </button>
                              );
                            })}
                          </div>
                        </div>
                      ) : null}

                      {selectedRegion.diplomacy.enemyRegions.length > 0 ? (
                        <div style={{ marginBottom: '6px' }}>
                          <div class="muted" style={{ fontSize: '0.78rem', marginBottom: '4px' }}>
                            Hostile realms
                          </div>
                          <div class="usage-pills">
                            {selectedRegion.diplomacy.enemyRegions.map(rid => {
                              const other = regionsById.get(rid);
                              return (
                                <button
                                  key={rid}
                                  type="button"
                                  class="chip chip-enemy"
                                  onClick={() => other && focusRegion(other)}
                                >
                                  {other?.name ?? `Region ${rid}`} →
                                </button>
                              );
                            })}
                          </div>
                        </div>
                      ) : null}

                      {selectedRegion.diplomacy.allies.length === 0 &&
                      selectedRegion.diplomacy.enemies.length === 0 ? (
                        <p class="muted" style={{ fontSize: '0.8rem', margin: '4px 0' }}>
                          No explicit faction alliances or rivalries recorded in daggerfall.base for this region.
                        </p>
                      ) : (
                        <ul class="inspector-list" style={{ marginTop: '6px' }}>
                          {selectedRegion.diplomacy.allies.map((edge, idx) => (
                            <li key={`a-${idx}`} class="inspector-row">
                              <div style={{ fontSize: '0.8rem' }}>
                                <strong style={{ color: '#8ef0bc' }}>Ally:</strong> {edge.sourceFactionName} ↔{' '}
                                {edge.targetFactionName}
                                {edge.targetRegionName ? (
                                  <span class="muted"> ({edge.targetRegionName})</span>
                                ) : null}
                              </div>
                            </li>
                          ))}
                          {selectedRegion.diplomacy.enemies.map((edge, idx) => (
                            <li key={`e-${idx}`} class="inspector-row">
                              <div style={{ fontSize: '0.8rem' }}>
                                <strong style={{ color: '#ff8a95' }}>Enemy:</strong> {edge.sourceFactionName} ⚔{' '}
                                {edge.targetFactionName}
                                {edge.targetRegionName ? (
                                  <span class="muted"> ({edge.targetRegionName})</span>
                                ) : null}
                              </div>
                            </li>
                          ))}
                        </ul>
                      )}
                    </div>

                    <div class="variant-section">
                      <strong style={{ fontSize: '0.82rem' }}>People pack presence</strong>
                      {selectedRegion.peoplePack.cast.length > 0 ? (
                        <div class="usage-pills" style={{ marginTop: '6px' }}>
                          {selectedRegion.peoplePack.cast.map(c => (
                            <a key={c.id} class="art-data-chip" href={castUrl(c.id)}>
                              <span>{c.name}</span>
                              <code>cast</code>
                            </a>
                          ))}
                        </div>
                      ) : null}
                      {selectedRegion.peoplePack.regionalSpawners.length > 0 ? (
                        <div class="usage-pills" style={{ marginTop: '6px' }}>
                          {selectedRegion.peoplePack.regionalSpawners.map(sid => (
                            <a key={sid} class="art-data-chip" href={spawnerUrl(sid)}>
                              <span>{sid}</span>
                              <code>pool</code>
                            </a>
                          ))}
                        </div>
                      ) : null}
                      {selectedRegion.peoplePack.cast.length === 0 &&
                      selectedRegion.peoplePack.regionalSpawners.length === 0 ? (
                        <p class="muted" style={{ fontSize: '0.8rem', margin: '4px 0' }}>
                          No regional spawner overrides or named cast linked to this region.
                        </p>
                      ) : null}
                      <div class="actions" style={{ marginTop: '8px' }}>
                        <a class="button" href={locationsUrl({ region: selectedRegion.id })}>
                          Browse {selectedRegion.name} in Locations
                        </a>
                        <a class="button" href={worldMapUrl(selectedRegion.id)}>
                          Permalink
                        </a>
                      </div>
                    </div>
                  </>
                ) : null}

                {inspectorTab === 'factions' ? (
                  selectedRegion.factions.length === 0 ? (
                    <Empty>No factions linked to this region in daggerfall.base.</Empty>
                  ) : (
                    <ul class="inspector-list">
                      {selectedRegion.factions.map(f => (
                        <li key={f.id} class="inspector-row">
                          <div>
                            <strong>{f.name}</strong>
                            <div class="muted" style={{ fontSize: '0.78rem' }}>
                              #{f.id} · {f.typeName || `Type ${f.type}`} · Power {f.power}
                            </div>
                          </div>
                          {f.castId ? (
                            <a class="button" href={castUrl(f.castId)}>
                              Cast →
                            </a>
                          ) : (
                            <Chip state="empty" label={f.typeName || String(f.type)} />
                          )}
                        </li>
                      ))}
                    </ul>
                  )
                ) : null}

                {inspectorTab === 'locations' ? (
                  <>
                    <div style={{ marginBottom: '8px' }}>
                      <input
                        type="search"
                        value={regionLocFilter}
                        placeholder={`Filter ${selectedRegion.locations.total} locations…`}
                        style={{ width: '100%' }}
                        onInput={event => setRegionLocFilter((event.currentTarget as HTMLInputElement).value)}
                      />
                    </div>
                    <ul class="inspector-list">
                      {regionPins.slice(0, 150).map(p => (
                        <li
                          key={`${p[0]}-${p[1]}`}
                          class={`inspector-row${selectedPin && selectedPin[0] === p[0] && selectedPin[1] === p[1] ? ' active' : ''}`}
                        >
                          <button type="button" class="inspector-focus-btn" onClick={() => focusPin(p)}>
                            <strong>{p[6]}</strong>
                            <div class="muted" style={{ fontSize: '0.78rem' }}>
                              {LOCATION_TYPE_LABELS[p[4]] ?? `Type ${p[4]}`} · ({p[2]}, {p[3]})
                            </div>
                          </button>
                          <a class="button" href={locationUrl(p[0], p[1])}>
                            3D →
                          </a>
                        </li>
                      ))}
                    </ul>
                    {regionPins.length > 150 ? (
                      <p class="muted" style={{ fontSize: '0.78rem' }}>
                        Showing first 150 of {regionPins.length} locations. Use filter above to narrow.
                      </p>
                    ) : null}
                  </>
                ) : null}
              </div>
            </>
          )}
        </aside>
      </div>
    </>
  );
}
