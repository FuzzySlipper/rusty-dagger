import { useCallback, useEffect, useState } from 'preact/hooks';
import { apiRequest } from './api';
import { ASSET_LABELS, ASSET_PAGES, type ValidationIssue } from './types';
import { activeSection, parseHash, searchUrl, type Route } from './router';
import { useApp } from './state';
import { Modal } from './components/Modal';
import { Empty, PageHeading } from './components/ui';
import { OverviewPage } from './pages/OverviewPage';
import { AssetListingPage } from './pages/AssetListingPage';
import { CharacterPage } from './pages/CharacterPage';
import { AssetDetailPage } from './pages/AssetDetailPage';
import { ConceptsPage } from './pages/ConceptsPage';
import { PipelinePage } from './pages/PipelinePage';
import { StageReviewPage } from './pages/StageReviewPage';
import { SearchPage } from './pages/SearchPage';
import { ActorsPage } from './pages/ActorsPage';
import { CastPage } from './pages/CastPage';
import { SpawnersPage } from './pages/SpawnersPage';
import { BarksPage } from './pages/BarksPage';
import { BlockSlotsPage } from './pages/BlockSlotsPage';
import { WorldBlockDetailPage, WorldBlocksListPage } from './pages/WorldBlocksPage';
import { LocationDetailPage, LocationsListPage } from './pages/LocationsPage';
import { WorldAtlasPage } from './pages/WorldAtlasPage';

interface WorkspaceGitStatus {
  gitAvailable: boolean;
  modified: Array<{ status: string; path: string }>;
}

interface PackValidationReport {
  valid: boolean;
  issues: ValidationIssue[];
  counts: { cast: number; spawners: number; slots: number };
}

function renderRoute(route: Route) {
  switch (route.page) {
    case 'overview':
      return <OverviewPage />;
    case 'listing':
      return <AssetListingPage category={route.category} />;
    case 'character':
      return <CharacterPage id={route.id} />;
    case 'concepts':
      return <ConceptsPage id={route.id} />;
    case 'asset':
      return <AssetDetailPage id={route.id} />;
    case 'pipeline':
      return <PipelinePage id={route.id} />;
    case 'review':
      return <StageReviewPage pipeline={route.id} stage={route.stage} />;
    case 'search':
      return <SearchPage term={route.term} />;
    case 'actors':
      return <ActorsPage kind={route.kind} id={route.id} />;
    case 'cast':
      return <CastPage id={route.id} />;
    case 'spawners':
      return <SpawnersPage id={route.id} />;
    case 'barks':
      return <BarksPage />;
    case 'blocks':
      return <BlockSlotsPage initialQuery={route.query} />;
    case 'world-blocks':
      return <WorldBlocksListPage initialQuery={route.query} />;
    case 'world-block':
      return <WorldBlockDetailPage blockKey={route.key} />;
    case 'locations':
      return <LocationsListPage initialQuery={route.query} />;
    case 'location':
      return <LocationDetailPage region={route.region} index={route.index} mode={route.mode} />;
    case 'world-map':
      return <WorldAtlasPage initialRegion={route.region} />;
  }
}

export function App() {
  const { catalog, loading, error, categoryOf, refreshCatalog } = useApp();
  const [route, setRoute] = useState<Route>(() => parseHash(window.location.hash));
  const [query, setQuery] = useState('');
  const [refreshing, setRefreshing] = useState(false);

  const [gitStatus, setGitStatus] = useState<WorkspaceGitStatus | null>(null);
  const [gitModalOpen, setGitModalOpen] = useState(false);
  const [validating, setValidating] = useState(false);
  const [validation, setValidation] = useState<PackValidationReport | null>(null);
  const [validationOpen, setValidationOpen] = useState(false);

  const loadGitStatus = useCallback(async () => {
    try {
      const status = await apiRequest<WorkspaceGitStatus>('/api/workspace/status');
      setGitStatus(status);
    } catch {
      setGitStatus(null);
    }
  }, []);

  useEffect(() => {
    void loadGitStatus();
  }, [loadGitStatus, route.page]);

  useEffect(() => {
    function onHashChange() {
      setRoute(parseHash(window.location.hash));
      window.scrollTo(0, 0);
    }
    window.addEventListener('hashchange', onHashChange);
    return () => window.removeEventListener('hashchange', onHashChange);
  }, []);

  const currentSection = activeSection(route, categoryOf);

  function submitSearch(event: Event) {
    event.preventDefault();
    const term = query.trim();
    if (term) {
      window.location.hash = searchUrl(term);
    }
  }

  async function handleRefresh() {
    setRefreshing(true);
    try {
      await Promise.all([refreshCatalog(), loadGitStatus()]);
    } finally {
      setRefreshing(false);
    }
  }

  async function handleValidatePack() {
    setValidating(true);
    try {
      const report = await apiRequest<PackValidationReport>('/api/people-pack/validate');
      setValidation(report);
      if (!report.valid) setValidationOpen(true);
    } catch (err) {
      setValidation({
        valid: false,
        issues: [{ field: 'pack', message: err instanceof Error ? err.message : String(err) }],
        counts: { cast: 0, spawners: 0, slots: 0 },
      });
      setValidationOpen(true);
    } finally {
      setValidating(false);
    }
  }

  const modifiedCount = gitStatus?.modified.length ?? 0;

  return (
    <div class="shell">
      <aside class="sidebar">
        <a class="brand" href="#overview">
          <span class="monogram" aria-hidden="true">
            RD
          </span>
          <span>
            Rusty Dagger<small>Authoring workspace</small>
          </span>
        </a>
        <nav class="navigation" aria-label="Workspace">
          <a href="#overview" data-page="overview" aria-current={currentSection === 'overview' ? 'page' : undefined}>
            Overview
          </a>
          <div class="nav-group-label">Art &amp; assets</div>
          {ASSET_PAGES.map(page => (
            <a
              key={page}
              href={`#${page}`}
              data-page={page}
              aria-current={currentSection === page ? 'page' : undefined}
            >
              {ASSET_LABELS[page]}
            </a>
          ))}
          <div class="nav-group-label">Game data</div>
          <a href="#actors" data-page="actors" aria-current={currentSection === 'actors' ? 'page' : undefined}>
            Actors (unified)
          </a>
          <a href="#cast" data-page="cast" aria-current={currentSection === 'cast' ? 'page' : undefined}>
            Cast individuals
          </a>
          <a href="#spawners" data-page="spawners" aria-current={currentSection === 'spawners' ? 'page' : undefined}>
            Typed spawners
          </a>
          <a href="#barks" data-page="barks" aria-current={currentSection === 'barks' ? 'page' : undefined}>
            Barks &amp; referrals
          </a>
          <a href="#blocks" data-page="blocks" aria-current={currentSection === 'blocks' ? 'page' : undefined}>
            Block slots
          </a>
          <a
            href="#world-blocks"
            data-page="world-blocks"
            aria-current={currentSection === 'world-blocks' ? 'page' : undefined}
          >
            World blocks (3D)
          </a>
          <a
            href="#locations"
            data-page="locations"
            aria-current={currentSection === 'locations' ? 'page' : undefined}
          >
            Towns &amp; locations (3D)
          </a>
          <a
            href="#world-map"
            data-page="world-map"
            aria-current={currentSection === 'world-map' ? 'page' : undefined}
          >
            World map (Atlas)
          </a>
          <div class="nav-group-label">Pipelines</div>
          <a href="#pipelines" data-page="pipelines" aria-current={currentSection === 'pipelines' ? 'page' : undefined}>
            Character stages
          </a>
        </nav>
        <a class="external" href="/media/npc/">
          NPC compare &amp; merge
        </a>
      </aside>
      <main class="workspace">
        <header class="topbar">
          <form id="asset-search" role="search" onSubmit={submitSearch}>
            <label class="visually-hidden" for="query">
              Find any asset or game data
            </label>
            <input
              id="query"
              name="query"
              type="search"
              autocomplete="off"
              placeholder="Find assets, cast, or spawners by ID or name"
              value={query}
              onInput={event => setQuery((event.currentTarget as HTMLInputElement).value)}
            />
            <button type="submit">Search</button>
          </form>
          <div class="actions topbar-tools">
            {modifiedCount > 0 ? (
              <button
                type="button"
                class="topbar-pill dirty"
                onClick={() => setGitModalOpen(true)}
                title="Uncommitted edits in CMS-managed pack/ledger files"
              >
                {modifiedCount} edited {modifiedCount === 1 ? 'file' : 'files'}
              </button>
            ) : null}
            <button
              type="button"
              class={`topbar-pill${validation ? (validation.valid ? ' valid' : ' invalid') : ''}`}
              disabled={validating}
              onClick={() => (validation && !validation.valid ? setValidationOpen(true) : void handleValidatePack())}
              title="Validate daggerfall.people against schema invariants"
            >
              {validating
                ? 'Validating…'
                : validation
                  ? validation.valid
                    ? 'Pack valid'
                    : `${validation.issues.length} pack issue${validation.issues.length === 1 ? '' : 's'}`
                  : 'Validate pack'}
            </button>
            <button
              type="button"
              class="topbar-refresh"
              disabled={refreshing || loading}
              onClick={() => void handleRefresh()}
              title="Reload catalog data from disk"
            >
              {refreshing ? 'Refreshing…' : 'Refresh'}
            </button>
          </div>
        </header>
        <div id="content" aria-live="polite">
          {loading && !catalog ? (
            <p>Loading catalog…</p>
          ) : error && !catalog ? (
            <>
              <PageHeading title="Unable to open this view" />
              <Empty>{error}</Empty>
            </>
          ) : (
            renderRoute(route)
          )}
        </div>
      </main>

      <Modal
        open={gitModalOpen}
        title="Uncommitted CMS changes"
        subtitle="Git-tracked authoring files modified in this working copy."
        onClose={() => setGitModalOpen(false)}
        headerActions={
          <button type="button" onClick={() => void loadGitStatus()}>
            Re-check
          </button>
        }
      >
        <div class="curation-modal-body">
          {gitStatus?.modified.length ? (
            <ul class="retired-list">
              {gitStatus.modified.map(item => (
                <li key={item.path}>
                  <code>{item.path}</code>
                  <span class="chip needs-check">{item.status}</span>
                </li>
              ))}
            </ul>
          ) : (
            <Empty>All CMS-managed pack and ledger files are clean in git.</Empty>
          )}
        </div>
      </Modal>

      <Modal
        open={validationOpen}
        title="daggerfall.people validation"
        subtitle={
          validation
            ? `${validation.counts.cast} cast · ${validation.counts.spawners} spawners · ${validation.counts.slots.toLocaleString()} slots`
            : undefined
        }
        onClose={() => setValidationOpen(false)}
        headerActions={
          <button type="button" disabled={validating} onClick={() => void handleValidatePack()}>
            {validating ? 'Validating…' : 'Re-validate'}
          </button>
        }
      >
        <div class="curation-modal-body">
          {validation?.valid ? (
            <div class="curation-safe">
              <strong>Pack is valid.</strong> Zero schema or cross-reference issues found.
            </div>
          ) : validation ? (
            <div class="validation-summary" role="alert">
              <strong>Found {validation.issues.length} issue(s):</strong>
              <ul>
                {validation.issues.map((issue, idx) => (
                  <li key={`${issue.field}-${idx}`}>
                    <code>{issue.field}</code>: {issue.message}
                  </li>
                ))}
              </ul>
            </div>
          ) : null}
        </div>
      </Modal>
    </div>
  );
}
