import { ASSET_PAGES, type AssetCategory } from './types';

export type Route =
  | { page: 'overview' }
  | { page: 'listing'; category: AssetCategory | 'pipelines' }
  | { page: 'character'; id: string }
  | { page: 'concepts'; id: string }
  | { page: 'asset'; id: string }
  | { page: 'pipeline'; id: string }
  | { page: 'review'; id: string; stage: string }
  | { page: 'search'; term: string }
  | { page: 'cast'; id?: string }
  | { page: 'spawners'; id?: string }
  | { page: 'blocks'; query: Record<string, string> }
  | { page: 'world-blocks'; query: Record<string, string> }
  | { page: 'world-block'; key: string };

export const encodeKey = (value: string): string => encodeURIComponent(value);

export const subjectUrl = (id: string): string => `#character/${encodeKey(id)}`;
export const conceptsUrl = (id: string): string => `#concepts/${encodeKey(id)}`;
export const assetUrl = (id: string): string => `#asset/${encodeKey(id)}`;
export const pipelineUrl = (id: string): string => `#pipeline/${encodeKey(id)}`;
export const stageUrl = (id: string, stage: string): string => `#review/${encodeKey(id)}/${encodeKey(stage)}`;
export const searchUrl = (term: string): string => `#search/${encodeKey(term)}`;
export const castUrl = (id?: string): string => (id ? `#cast/${encodeKey(id)}` : '#cast');
export const spawnerUrl = (id?: string): string => (id ? `#spawners/${encodeKey(id)}` : '#spawners');
export const blocksUrl = (filters: Record<string, string | number | undefined> = {}): string => {
  const params = new URLSearchParams();
  for (const [k, v] of Object.entries(filters)) {
    if (v !== undefined && v !== '') params.set(k, String(v));
  }
  const qs = params.toString();
  return qs ? `#blocks?${qs}` : '#blocks';
};
export const worldBlocksUrl = (filters: Record<string, string | number | undefined> = {}): string => {
  const params = new URLSearchParams();
  for (const [k, v] of Object.entries(filters)) {
    if (v !== undefined && v !== '') params.set(k, String(v));
  }
  const qs = params.toString();
  return qs ? `#world-blocks?${qs}` : '#world-blocks';
};
export const worldBlockUrl = (key: string): string => `#world-block/${encodeKey(key)}`;

function safeDecode(value: string): string {
  try {
    return decodeURIComponent(value);
  } catch {
    return value;
  }
}

export function parseHash(hash: string): Route {
  const raw = hash.startsWith('#') ? hash.slice(1) : hash;
  const [pathPart, queryPart = ''] = raw.split('?');
  const [page = 'overview', encoded = '', ...rest] = pathPart.split('/');
  const id = encoded ? safeDecode(encoded) : '';
  const stage = rest.length ? safeDecode(rest.join('/')) : '';

  if (page === 'character' && id) return { page: 'character', id };
  if (page === 'concepts' && id) return { page: 'concepts', id };
  if (page === 'asset' && id) return { page: 'asset', id };
  if (page === 'pipeline' && id) return { page: 'pipeline', id };
  if (page === 'review' && id && stage) return { page: 'review', id, stage };
  if (page === 'search' && id) return { page: 'search', term: id };
  if (page === 'cast') return id ? { page: 'cast', id } : { page: 'cast' };
  if (page === 'spawners') return id ? { page: 'spawners', id } : { page: 'spawners' };
  if (page === 'blocks') {
    const query: Record<string, string> = {};
    if (queryPart) {
      for (const [k, v] of new URLSearchParams(queryPart).entries()) {
        query[k] = v;
      }
    }
    return { page: 'blocks', query };
  }
  if (page === 'world-blocks') {
    const query: Record<string, string> = {};
    if (queryPart) {
      for (const [k, v] of new URLSearchParams(queryPart).entries()) {
        query[k] = v;
      }
    }
    return { page: 'world-blocks', query };
  }
  if (page === 'world-block' && encoded) {
    const fullKey = rest.length ? safeDecode([encoded, ...rest].join('/')) : id;
    return { page: 'world-block', key: fullKey };
  }
  if (page === 'pipelines') return { page: 'listing', category: 'pipelines' };
  if ((ASSET_PAGES as string[]).includes(page)) {
    return { page: 'listing', category: page as AssetCategory };
  }
  return { page: 'overview' };
}

export function activeSection(
  route: Route,
  categoryOf: (id: string) => AssetCategory | undefined,
): string {
  switch (route.page) {
    case 'overview':
      return 'overview';
    case 'listing':
      return route.category;
    case 'character':
    case 'concepts':
    case 'asset':
      return categoryOf(route.id) ?? 'overview';
    case 'pipeline':
    case 'review':
      return 'pipelines';
    case 'cast':
      return 'cast';
    case 'spawners':
      return 'spawners';
    case 'blocks':
      return 'blocks';
    case 'world-blocks':
    case 'world-block':
      return 'world-blocks';
    case 'search':
      return '';
  }
}
