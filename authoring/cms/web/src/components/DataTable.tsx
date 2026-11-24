import type { ComponentChildren } from 'preact';
import { useMemo, useState } from 'preact/hooks';
import { Empty } from './ui';

export interface DataColumn<T> {
  key: string;
  header: string;
  render: (row: T) => ComponentChildren;
  sortValue?: (row: T) => string | number;
  width?: string;
}

export interface PaginationState {
  page: number;
  pageSize: number;
  total: number;
  onPageChange: (nextPage: number) => void;
}

export interface DataTableProps<T> {
  rows: T[];
  columns: Array<DataColumn<T>>;
  rowKey: (row: T, index: number) => string;
  emptyMessage?: string;
  initialSort?: { key: string; direction: 'asc' | 'desc' };
  /** Supply for server-side pagination; omit for client-side pagination when pageSize is set. */
  pagination?: PaginationState;
  /** Optional client-side page size when `pagination` is not controlled by the server. */
  clientPageSize?: number;
  onRowClick?: (row: T) => void;
}

export function DataTable<T>({
  rows,
  columns,
  rowKey,
  emptyMessage = 'No matching records.',
  initialSort,
  pagination,
  clientPageSize,
  onRowClick,
}: DataTableProps<T>) {
  const [sort, setSort] = useState(initialSort ?? null);
  const [clientPage, setClientPage] = useState(1);

  const sortedRows = useMemo(() => {
    if (!sort) return rows;
    const col = columns.find(item => item.key === sort.key);
    if (!col?.sortValue) return rows;
    const getVal = col.sortValue;
    const dir = sort.direction === 'asc' ? 1 : -1;
    return [...rows].sort((a, b) => {
      const av = getVal(a);
      const bv = getVal(b);
      if (av === bv) return 0;
      if (typeof av === 'number' && typeof bv === 'number') return (av - bv) * dir;
      return String(av).localeCompare(String(bv)) * dir;
    });
  }, [rows, columns, sort]);

  const effectiveTotal = pagination ? pagination.total : sortedRows.length;
  const effectivePageSize = pagination ? pagination.pageSize : clientPageSize ?? sortedRows.length;
  const totalPages = Math.max(1, Math.ceil(effectiveTotal / Math.max(1, effectivePageSize)));
  const currentPage = pagination
    ? Math.min(Math.max(1, pagination.page), totalPages)
    : Math.min(Math.max(1, clientPage), totalPages);

  const visibleRows = useMemo(() => {
    if (pagination || !clientPageSize) return sortedRows;
    const start = (currentPage - 1) * clientPageSize;
    return sortedRows.slice(start, start + clientPageSize);
  }, [pagination, clientPageSize, sortedRows, currentPage]);

  function toggleSort(column: DataColumn<T>) {
    if (!column.sortValue) return;
    setSort(prev => {
      if (!prev || prev.key !== column.key) return { key: column.key, direction: 'asc' };
      return { key: column.key, direction: prev.direction === 'asc' ? 'desc' : 'asc' };
    });
  }

  function changePage(next: number) {
    const clamped = Math.min(Math.max(1, next), totalPages);
    if (pagination) pagination.onPageChange(clamped);
    else setClientPage(clamped);
  }

  if (!rows.length) return <Empty>{emptyMessage}</Empty>;

  const showPager = totalPages > 1;
  const startIndex = (currentPage - 1) * effectivePageSize + 1;
  const endIndex = Math.min(effectiveTotal, startIndex + visibleRows.length - 1);

  return (
    <div class="data-table">
      <div class="table-wrap">
        <table>
          <thead>
            <tr>
              {columns.map(col => {
                const isSorted = sort?.key === col.key;
                return (
                  <th key={col.key} style={col.width ? { width: col.width } : undefined}>
                    {col.sortValue ? (
                      <button
                        type="button"
                        class="sort-header"
                        onClick={() => toggleSort(col)}
                        aria-sort={isSorted ? (sort.direction === 'asc' ? 'ascending' : 'descending') : 'none'}
                      >
                        {col.header}
                        {isSorted ? (sort.direction === 'asc' ? ' ▲' : ' ▼') : ''}
                      </button>
                    ) : (
                      col.header
                    )}
                  </th>
                );
              })}
            </tr>
          </thead>
          <tbody>
            {visibleRows.map((row, idx) => (
              <tr
                key={rowKey(row, idx)}
                class={onRowClick ? 'clickable-row' : undefined}
                onClick={onRowClick ? () => onRowClick(row) : undefined}
              >
                {columns.map(col => (
                  <td key={col.key}>{col.render(row)}</td>
                ))}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {showPager ? (
        <div class="table-pager">
          <span class="muted">
            Showing {startIndex}–{endIndex} of {effectiveTotal}
          </span>
          <div class="actions">
            <button type="button" disabled={currentPage <= 1} onClick={() => changePage(currentPage - 1)}>
              Previous
            </button>
            <output>
              Page {currentPage} of {totalPages}
            </output>
            <button type="button" disabled={currentPage >= totalPages} onClick={() => changePage(currentPage + 1)}>
              Next
            </button>
          </div>
        </div>
      ) : null}
    </div>
  );
}
