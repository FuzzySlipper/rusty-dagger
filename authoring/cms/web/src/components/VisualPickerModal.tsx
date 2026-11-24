import { useEffect, useMemo, useState } from 'preact/hooks';
import { apiRequest, isAbortError } from '../api';
import type { VisualOption } from '../types';
import { Modal } from './Modal';
import { Chip, Empty, SpritePreview } from './ui';

export function VisualPickerModal({
  open,
  title = 'Choose person sprite',
  current,
  onSelect,
  onClose,
}: {
  open: boolean;
  title?: string;
  current?: { archive: number; record: number } | null;
  onSelect: (visual: VisualOption) => void;
  onClose: () => void;
}) {
  const [options, setOptions] = useState<VisualOption[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [query, setQuery] = useState('');
  const [onlyConcept, setOnlyConcept] = useState(false);

  useEffect(() => {
    if (!open || options.length > 0) return;
    const controller = new AbortController();
    setLoading(true);
    setError(null);
    apiRequest<VisualOption[]>('/api/people-pack/visuals', { signal: controller.signal })
      .then(data => setOptions(data))
      .catch(err => {
        if (!isAbortError(err)) setError(err instanceof Error ? err.message : String(err));
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false);
      });
    return () => controller.abort();
  }, [open, options.length]);

  const filtered = useMemo(() => {
    const terms = query.trim().toLowerCase().split(/\s+/).filter(Boolean);
    return options.filter(item => {
      if (onlyConcept && !item.hasApprovedConcept) return false;
      if (!terms.length) return true;
      const haystack = `${item.archive}-${item.record} ${item.archive}.${item.record} ${item.npcId ?? ''} ${
        item.npcName ?? ''
      } ${item.npcKind ?? ''}`.toLowerCase();
      return terms.every(term => haystack.includes(term));
    });
  }, [options, query, onlyConcept]);

  return (
    <Modal
      open={open}
      title={title}
      subtitle="Pick a concrete (archive, record) sprite mapped to its NPC art record"
      onClose={onClose}
    >
      <div class="picker-body">
        <div class="filters">
          <label>
            Search sprite or NPC
            <input
              type="search"
              value={query}
              placeholder="e.g. 183-5, banker, npc-010, guard"
              onInput={event => setQuery((event.currentTarget as HTMLInputElement).value)}
            />
          </label>
          <label class="mark-filter">
            <input
              type="checkbox"
              checked={onlyConcept}
              onChange={event => setOnlyConcept((event.currentTarget as HTMLInputElement).checked)}
            />{' '}
            Has approved concept
          </label>
          <output>{filtered.length} visuals</output>
        </div>

        {loading ? <p>Loading visual sprites…</p> : null}
        {error ? <Empty>{error}</Empty> : null}

        {!loading && !error ? (
          filtered.length ? (
            <div class="picker-grid">
              {filtered.map(item => {
                const isSelected = current?.archive === item.archive && current?.record === item.record;
                return (
                  <button
                    key={item.key}
                    type="button"
                    class={`picker-card${isSelected ? ' selected' : ''}`}
                    onClick={() => {
                      onSelect(item);
                      onClose();
                    }}
                  >
                    <SpritePreview url={item.classic} alt={item.npcName ?? item.key} pixel className="picker-thumb" />
                    <div class="picker-info">
                      <code>
                        {item.archive}.{item.record}
                      </code>
                      <strong>{item.npcName ?? 'Unlinked sprite'}</strong>
                      <small class="muted">
                        {item.npcId ?? 'no npc id'} · {item.spawnerCount} spawners · {item.slotCount} slots
                      </small>
                      {item.hasApprovedConcept ? <Chip state="approved" label="Concept" /> : null}
                    </div>
                  </button>
                );
              })}
            </div>
          ) : (
            <Empty>No matching sprites.</Empty>
          )
        ) : null}
      </div>
    </Modal>
  );
}
