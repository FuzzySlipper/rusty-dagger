import { useEffect, useRef, useState } from 'preact/hooks';
import { apiRequest } from '../api';
import { arrayBufferToBase64, extractComfyMetadata, type ExtractedComfyMetadata } from '../comfyMetadata';
import { encodeKey } from '../router';
import type { ConceptCandidate, ConceptStageDetail } from '../types';

type ApproveChoice = '' | 'front' | 'back';

interface QueuedConceptFile {
  localId: string;
  file: File;
  previewUrl: string;
  base64: string;
  name: string;
  prompt: string;
  seed: string;
  model: string;
  approveAs: ApproveChoice;
  meta: ExtractedComfyMetadata;
}

const ACCEPTED_EXT = /\.(png|jpe?g|webp)$/i;

function todayBatchName(): string {
  const d = new Date();
  const yyyy = d.getFullYear();
  const mm = String(d.getMonth() + 1).padStart(2, '0');
  const dd = String(d.getDate()).padStart(2, '0');
  return `manual-${yyyy}-${mm}-${dd}`;
}

function stemOf(filename: string): string {
  return filename.replace(/\.[^.]+$/, '').trim() || 'candidate';
}

export function ConceptUploader({
  subjectId,
  existingNames,
  onUploaded,
}: {
  subjectId: string;
  existingNames: Set<string>;
  onUploaded: (nextStage: ConceptStageDetail, summary: string) => void;
}) {
  const inputRef = useRef<HTMLInputElement>(null);
  const [dragging, setDragging] = useState(false);
  const [batch, setBatch] = useState(() => todayBatchName());
  const [queue, setQueue] = useState<QueuedConceptFile[]>([]);
  const [uploading, setUploading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Clean up object URLs on unmount
  useEffect(() => {
    return () => {
      for (const item of queue) {
        URL.revokeObjectURL(item.previewUrl);
      }
    };
  }, []);

  async function ingestFiles(fileList: FileList | File[]) {
    setError(null);
    const files = Array.from(fileList).filter(f => ACCEPTED_EXT.test(f.name) || f.type.startsWith('image/'));
    if (!files.length) {
      setError('Drop PNG, JPEG, or WebP concept images.');
      return;
    }

    const usedNames = new Set<string>([...existingNames, ...queue.map(q => q.name)]);
    const nextItems: QueuedConceptFile[] = [];

    for (const file of files) {
      if (!ACCEPTED_EXT.test(file.name)) {
        setError(`Skipped '${file.name}': must end in .png, .jpg, .jpeg, or .webp.`);
        continue;
      }
      const buffer = await file.arrayBuffer();
      const meta = extractComfyMetadata(buffer);
      const base64 = arrayBufferToBase64(buffer);
      const previewUrl = URL.createObjectURL(file);

      let candidateName = stemOf(file.name);
      let suffix = 2;
      while (usedNames.has(candidateName)) {
        candidateName = `${stemOf(file.name)}-${suffix}`;
        suffix += 1;
      }
      usedNames.add(candidateName);

      nextItems.push({
        localId: `${Date.now()}-${Math.random().toString(36).slice(2)}`,
        file,
        previewUrl,
        base64,
        name: candidateName,
        prompt: meta.prompt || `Manual upload (${file.name})`,
        seed: meta.seed ?? '',
        model: meta.model ?? '',
        approveAs: '',
        meta,
      });
    }

    if (nextItems.length) {
      setQueue(prev => [...prev, ...nextItems]);
    }
  }

  function removeItem(localId: string) {
    setQueue(prev => {
      const target = prev.find(i => i.localId === localId);
      if (target) URL.revokeObjectURL(target.previewUrl);
      return prev.filter(i => i.localId !== localId);
    });
  }

  function clearQueue() {
    for (const item of queue) URL.revokeObjectURL(item.previewUrl);
    setQueue([]);
    setError(null);
  }

  function updateItem(localId: string, patch: Partial<QueuedConceptFile>) {
    setQueue(prev => prev.map(item => (item.localId === localId ? { ...item, ...patch } : item)));
  }

  async function submitAll() {
    const cleanBatch = batch.trim();
    if (!cleanBatch) {
      setError('Enter a batch name before uploading.');
      return;
    }
    if (!queue.length) return;

    setUploading(true);
    setError(null);
    let uploadedCount = 0;
    const approvedNotes: string[] = [];

    try {
      for (const item of queue) {
        const cleanName = item.name.trim();
        const cleanPrompt = item.prompt.trim();
        if (!cleanName) throw new Error(`Candidate name is required for ${item.file.name}.`);
        if (!cleanPrompt) throw new Error(`Prompt is required for ${cleanName}.`);

        const details: Record<string, unknown> = {
          prompt: cleanPrompt,
          seed: item.seed.trim() ? item.seed.trim() : null,
        };
        if (item.model.trim()) details.model = item.model.trim();
        if (item.meta.detected) {
          details.source = 'comfyui';
          if (item.meta.negativePrompt) details.negativePrompt = item.meta.negativePrompt;
          if (item.meta.sampler) details.sampler = item.meta.sampler;
          if (item.meta.scheduler) details.scheduler = item.meta.scheduler;
          if (item.meta.steps !== undefined) details.steps = item.meta.steps;
          if (item.meta.cfg !== undefined) details.cfg = item.meta.cfg;
          if (item.meta.denoise !== undefined) details.denoise = item.meta.denoise;
          if (item.meta.loras?.length) details.loras = item.meta.loras;
        } else {
          details.source = 'manual-upload';
        }

        const created = await apiRequest<ConceptCandidate>(
          `/api/concepts/${encodeKey(subjectId)}/candidates`,
          {
            method: 'POST',
            body: {
              batch: cleanBatch,
              name: cleanName,
              filename: item.file.name,
              image: item.base64,
              details,
            },
          },
        );
        uploadedCount += 1;

        if (item.approveAs === 'front' || item.approveAs === 'back') {
          await apiRequest<ConceptStageDetail>(`/api/concepts/${encodeKey(subjectId)}/approve`, {
            method: 'POST',
            body: { candidate: created.id, view: item.approveAs },
          });
          approvedNotes.push(`${cleanName} approved as ${item.approveAs}`);
        }
      }

      const freshStage = await apiRequest<ConceptStageDetail>(`/api/concepts/${encodeKey(subjectId)}`);
      clearQueue();
      const summary =
        `Uploaded ${uploadedCount} ${uploadedCount === 1 ? 'candidate' : 'candidates'} into batch '${cleanBatch}'` +
        (approvedNotes.length ? ` (${approvedNotes.join('; ')})` : '') +
        '.';
      onUploaded(freshStage, summary);
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    } finally {
      setUploading(false);
    }
  }

  return (
    <section class="panel concept-upload-panel" aria-label="Upload concept art">
      <div
        class={`concept-dropzone${dragging ? ' dragging' : ''}`}
        onDragOver={event => {
          event.preventDefault();
          setDragging(true);
        }}
        onDragLeave={event => {
          if (event.currentTarget === event.target) setDragging(false);
        }}
        onDrop={event => {
          event.preventDefault();
          setDragging(false);
          if (event.dataTransfer?.files?.length) {
            void ingestFiles(event.dataTransfer.files);
          }
        }}
        onClick={() => inputRef.current?.click()}
        role="button"
        tabIndex={0}
        onKeyDown={event => {
          if (event.key === 'Enter' || event.key === ' ') {
            event.preventDefault();
            inputRef.current?.click();
          }
        }}
      >
        <input
          ref={inputRef}
          type="file"
          accept=".png,.jpg,.jpeg,.webp,image/png,image/jpeg,image/webp"
          multiple
          hidden
          onChange={event => {
            const input = event.currentTarget as HTMLInputElement;
            if (input.files?.length) {
              void ingestFiles(input.files);
              input.value = '';
            }
          }}
        />
        <strong>Drop ComfyUI renders here</strong>
        <span class="muted">
          or click to pick PNG / JPEG / WebP files · ComfyUI prompt, seed, model &amp; sampler metadata are read
          automatically
        </span>
      </div>

      {error ? (
        <div class="curation-warning" role="alert" style={{ marginTop: '10px' }}>
          {error}
        </div>
      ) : null}

      {queue.length > 0 ? (
        <div class="concept-upload-queue">
          <div class="concept-upload-bar">
            <label class="appearance-select">
              Batch name
              <input
                type="text"
                aria-label="Concept batch name"
                value={batch}
                onInput={event => setBatch((event.currentTarget as HTMLInputElement).value)}
              />
            </label>
            <div class="actions">
              <button type="button" class="primary" disabled={uploading} onClick={() => void submitAll()}>
                {uploading
                  ? 'Uploading…'
                  : `Upload ${queue.length} ${queue.length === 1 ? 'candidate' : 'candidates'}`}
              </button>
              <button type="button" disabled={uploading} onClick={clearQueue}>
                Clear
              </button>
            </div>
          </div>

          <ul class="concept-queue-list">
            {queue.map(item => (
              <li key={item.localId} class="concept-queue-item">
                <img class="concept-queue-thumb" src={item.previewUrl} alt={item.name} />
                <div class="concept-queue-fields">
                  <div class="concept-queue-row">
                    <label style={{ flex: 2 }}>
                      Candidate name
                      <input
                        type="text"
                        aria-label={`Candidate name for ${item.file.name}`}
                        value={item.name}
                        onInput={event =>
                          updateItem(item.localId, { name: (event.currentTarget as HTMLInputElement).value })
                        }
                      />
                    </label>
                    <label style={{ flex: 1 }}>
                      Seed
                      <input
                        type="text"
                        placeholder="null"
                        aria-label={`Seed for ${item.file.name}`}
                        value={item.seed}
                        onInput={event =>
                          updateItem(item.localId, { seed: (event.currentTarget as HTMLInputElement).value })
                        }
                      />
                    </label>
                    <label style={{ flex: 1.5 }}>
                      Model
                      <input
                        type="text"
                        placeholder="optional"
                        aria-label={`Model for ${item.file.name}`}
                        value={item.model}
                        onInput={event =>
                          updateItem(item.localId, { model: (event.currentTarget as HTMLInputElement).value })
                        }
                      />
                    </label>
                    <label style={{ flex: 1 }}>
                      Auto-approve
                      <select
                        aria-label={`Auto-approve for ${item.file.name}`}
                        value={item.approveAs}
                        onChange={event =>
                          updateItem(item.localId, {
                            approveAs: (event.currentTarget as HTMLSelectElement).value as ApproveChoice,
                          })
                        }
                      >
                        <option value="">Candidate only</option>
                        <option value="front">Approve as front</option>
                        <option value="back">Approve as back</option>
                      </select>
                    </label>
                  </div>

                  <label style={{ display: 'flex', flexDirection: 'column', gap: '3px', fontSize: '0.78rem' }}>
                    <span class="muted">
                      Prompt{' '}
                      {item.meta.detected ? (
                        <span class="chip approved" style={{ marginLeft: '6px' }}>
                          ComfyUI metadata
                          {item.meta.sampler ? ` · ${item.meta.sampler}` : ''}
                          {item.meta.steps ? ` · ${item.meta.steps} steps` : ''}
                        </span>
                      ) : null}
                    </span>
                    <textarea
                      rows={2}
                      aria-label={`Prompt for ${item.file.name}`}
                      value={item.prompt}
                      onInput={event =>
                        updateItem(item.localId, { prompt: (event.currentTarget as HTMLTextAreaElement).value })
                      }
                    />
                  </label>
                </div>
                <button
                  type="button"
                  disabled={uploading}
                  onClick={() => removeItem(item.localId)}
                  title="Remove from upload queue"
                >
                  ×
                </button>
              </li>
            ))}
          </ul>
        </div>
      ) : null}
    </section>
  );
}
