import { createContext, type ComponentChildren } from 'preact';
import { useCallback, useContext, useEffect, useMemo, useState } from 'preact/hooks';
import { apiRequest } from './api';
import { encodeKey } from './router';
import type { AssetCategory, CatalogOverview, ReviewPatch, ReviewRow } from './types';

const entryKey = (category: string, id: string): string => JSON.stringify([category, id]);

const BLANK_REVIEW: ReviewRow = {
  category: '',
  id: '',
  marked: false,
  rating: null,
  note: '',
  revision: null,
  updated: null,
};

export interface AppState {
  catalog: CatalogOverview | null;
  loading: boolean;
  error: string | null;
  refreshCatalog: () => Promise<CatalogOverview>;
  categoryOf: (id: string) => AssetCategory | undefined;
  reviewFor: (category: string, id: string) => ReviewRow;
  markedCount: (category: string) => number;
  patchReview: (category: string, id: string, change: ReviewPatch) => Promise<ReviewRow>;
  clearMarks: (category: string) => Promise<number>;
}

const AppContext = createContext<AppState | null>(null);

export function AppProvider({ children }: { children: ComponentChildren }) {
  const [catalog, setCatalog] = useState<CatalogOverview | null>(null);
  const [reviews, setReviews] = useState<Map<string, ReviewRow>>(() => new Map());
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const refreshCatalog = useCallback(async () => {
    const data = await apiRequest<CatalogOverview>('/api/catalog');
    const next = new Map<string, ReviewRow>();
    for (const row of data.reviews) {
      next.set(entryKey(row.category, row.id), row);
    }
    setCatalog(data);
    setReviews(next);
    setError(null);
    return data;
  }, []);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    refreshCatalog()
      .catch(err => {
        if (!cancelled) setError(err instanceof Error ? err.message : String(err));
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [refreshCatalog]);

  const categoryOf = useCallback(
    (id: string): AssetCategory | undefined => {
      if (!catalog) return undefined;
      return catalog.categories[id.split('-')[0]];
    },
    [catalog],
  );

  const reviewFor = useCallback(
    (category: string, id: string): ReviewRow =>
      reviews.get(entryKey(category, id)) ?? { ...BLANK_REVIEW, category, id },
    [reviews],
  );

  const markedCount = useCallback(
    (category: string): number => {
      let count = 0;
      for (const row of reviews.values()) {
        if (row.category === category && row.marked) count += 1;
      }
      return count;
    },
    [reviews],
  );

  const patchReview = useCallback(async (category: string, id: string, change: ReviewPatch) => {
    const row = await apiRequest<ReviewRow>(
      `/api/reviews/${encodeKey(category)}/${encodeKey(id)}`,
      { method: 'POST', body: change },
    );
    setReviews(prev => {
      const next = new Map(prev);
      next.set(entryKey(category, id), row);
      return next;
    });
    return row;
  }, []);

  const clearMarks = useCallback(async (category: string) => {
    const result = await apiRequest<{ cleared: number }>(
      `/api/marks/${encodeKey(category)}/clear`,
      { method: 'POST' },
    );
    setReviews(prev => {
      const next = new Map(prev);
      for (const [k, row] of next.entries()) {
        if (row.category === category && row.marked) {
          next.set(k, { ...row, marked: false });
        }
      }
      return next;
    });
    return result.cleared;
  }, []);

  const value = useMemo<AppState>(
    () => ({
      catalog,
      loading,
      error,
      refreshCatalog,
      categoryOf,
      reviewFor,
      markedCount,
      patchReview,
      clearMarks,
    }),
    [catalog, loading, error, refreshCatalog, categoryOf, reviewFor, markedCount, patchReview, clearMarks],
  );

  return <AppContext.Provider value={value}>{children}</AppContext.Provider>;
}

export function useApp(): AppState {
  const ctx = useContext(AppContext);
  if (!ctx) throw new Error('useApp must be used inside AppProvider.');
  return ctx;
}
