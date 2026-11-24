import { useEffect, useState } from 'preact/hooks';
import { ASSET_LABELS, ASSET_PAGES } from './types';
import { activeSection, parseHash, searchUrl, type Route } from './router';
import { useApp } from './state';
import { Empty, PageHeading } from './components/ui';
import { OverviewPage } from './pages/OverviewPage';
import { AssetListingPage } from './pages/AssetListingPage';
import { CharacterPage } from './pages/CharacterPage';
import { AssetDetailPage } from './pages/AssetDetailPage';
import { ConceptsPage } from './pages/ConceptsPage';
import { PipelinePage } from './pages/PipelinePage';
import { StageReviewPage } from './pages/StageReviewPage';
import { SearchPage } from './pages/SearchPage';

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
  }
}

export function App() {
  const { catalog, loading, error, categoryOf, refreshCatalog } = useApp();
  const [route, setRoute] = useState<Route>(() => parseHash(window.location.hash));
  const [query, setQuery] = useState('');
  const [refreshing, setRefreshing] = useState(false);

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
      await refreshCatalog();
    } finally {
      setRefreshing(false);
    }
  }

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
              Find any asset
            </label>
            <input
              id="query"
              name="query"
              type="search"
              autocomplete="off"
              placeholder="Find any asset by ID or name"
              value={query}
              onInput={event => setQuery((event.currentTarget as HTMLInputElement).value)}
            />
            <button type="submit">Search</button>
          </form>
          <button
            type="button"
            class="topbar-refresh"
            disabled={refreshing || loading}
            onClick={() => void handleRefresh()}
            title="Reload catalog data from disk"
          >
            {refreshing ? 'Refreshing…' : 'Refresh'}
          </button>
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
    </div>
  );
}
