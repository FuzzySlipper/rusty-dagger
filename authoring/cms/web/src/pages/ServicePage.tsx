import { useCallback, useEffect, useState } from 'preact/hooks';
import { apiRequest } from '../api';
import { Chip, Empty, PageHeading } from '../components/ui';

export interface WorkspaceServiceStatus {
  service: string;
  pid: number;
  startedAt: string;
  uptimeSeconds: number;
  launchCommit: string | null;
  launchSubject: string | null;
  currentCommit: string | null;
  currentSubject: string | null;
  stale: boolean;
  restartStrategy: 'den-serve' | 'exec' | 'custom';
  denServeProject: string | null;
  repoRoot: string;
  cmsRoot: string;
  git: {
    gitAvailable: boolean;
    modified: Array<{ status: string; path: string }>;
  };
}

function formatUptime(seconds: number): string {
  const s = Math.max(0, Math.floor(seconds));
  const h = Math.floor(s / 3600);
  const m = Math.floor((s % 3600) / 60);
  const rem = s % 60;
  if (h > 0) return `${h}h ${m}m ${rem}s`;
  if (m > 0) return `${m}m ${rem}s`;
  return `${rem}s`;
}

export function ServicePage({ onServiceUpdated }: { onServiceUpdated?: () => void }) {
  const [status, setStatus] = useState<WorkspaceServiceStatus | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [restarting, setRestarting] = useState(false);
  const [restartNote, setRestartNote] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const data = await apiRequest<WorkspaceServiceStatus>('/api/workspace/service');
      setStatus(data);
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  async function handleRestart() {
    if (restarting) return;
    const previousPid = status?.pid ?? null;
    setRestarting(true);
    setError(null);
    setRestartNote('Requesting CMS service restart…');
    try {
      const resp = await apiRequest<{ message?: string }>('/api/workspace/restart', {
        method: 'POST',
        body: {},
      });
      setRestartNote(resp.message ? `${resp.message} Waiting for the server to come back…` : 'Waiting for the server to come back…');
    } catch (err) {
      setRestarting(false);
      setRestartNote(null);
      setError(err instanceof Error ? err.message : String(err));
      return;
    }

    const deadline = Date.now() + 30_000;
    while (Date.now() < deadline) {
      await new Promise(resolve => window.setTimeout(resolve, 750));
      try {
        const res = await fetch('/api/workspace/service', { cache: 'no-store' });
        if (!res.ok) continue;
        const fresh = (await res.json()) as WorkspaceServiceStatus;
        if (previousPid === null || fresh.pid !== previousPid) {
          setStatus(fresh);
          setRestartNote(`CMS restarted cleanly (PID ${fresh.pid}, HEAD ${fresh.launchCommit ?? 'unknown'}). Reloading workspace…`);
          onServiceUpdated?.();
          window.setTimeout(() => window.location.reload(), 400);
          return;
        }
      } catch {
        // Expected briefly while the server is restarting.
      }
    }

    setRestarting(false);
    setRestartNote(null);
    setError('Restart was triggered, but the server did not report a new PID within 30 seconds. Try refreshing manually.');
  }

  return (
    <section class="page">
      <PageHeading
        title="Service & status"
        intro="Runtime process info, git staleness check, and CMS service restart."
        action={
          <div style="display:flex;gap:8px;flex-wrap:wrap">
            <button type="button" disabled={loading || restarting} onClick={() => void load()}>
              {loading ? 'Checking…' : 'Refresh status'}
            </button>
            <button
              type="button"
              class="primary"
              disabled={restarting}
              onClick={() => void handleRestart()}
            >
              {restarting ? 'Restarting…' : 'Restart CMS service'}
            </button>
          </div>
        }
      />

      {error ? (
        <div class="validation-summary" role="alert">
          <strong>Service error:</strong> {error}
        </div>
      ) : null}

      {restartNote ? (
        <div class="curation-warning" role="status">
          {restartNote}
        </div>
      ) : null}

      {!status && loading ? <Empty>Loading service status…</Empty> : null}

      {status ? (
        <div class="panel-grid" style={{ display: 'grid', gap: '16px', gridTemplateColumns: 'repeat(auto-fit, minmax(320px, 1fr))' }}>
          <article class="panel">
            <div class="section-heading">
              <h3>CMS process</h3>
              {status.stale ? (
                <Chip state="needs-check" label="Stale — restart recommended" />
              ) : (
                <Chip state="approved" label="Up to date with HEAD" />
              )}
            </div>
            <dl class="facts">
              <div>
                <dt>Process ID</dt>
                <dd>
                  <code>{status.pid}</code>
                </dd>
              </div>
              <div>
                <dt>Uptime</dt>
                <dd>{formatUptime(status.uptimeSeconds)}</dd>
              </div>
              <div>
                <dt>Started (UTC)</dt>
                <dd>
                  <code>{status.startedAt}</code>
                </dd>
              </div>
              <div>
                <dt>Restart method</dt>
                <dd>
                  <code>{status.restartStrategy}</code>
                  {status.denServeProject ? ` (${status.denServeProject})` : ''}
                </dd>
              </div>
              <div>
                <dt>Launched at commit</dt>
                <dd>
                  <code>{status.launchCommit ?? 'n/a'}</code>
                  {status.launchSubject ? ` — ${status.launchSubject}` : ''}
                </dd>
              </div>
              <div>
                <dt>Current repo HEAD</dt>
                <dd>
                  <code>{status.currentCommit ?? 'n/a'}</code>
                  {status.currentSubject ? ` — ${status.currentSubject}` : ''}
                </dd>
              </div>
              <div>
                <dt>CMS directory</dt>
                <dd>
                  <code>{status.cmsRoot}</code>
                </dd>
              </div>
            </dl>
          </article>

          <article class="panel">
            <div class="section-heading">
              <h3>Workspace edits</h3>
              {status.git.modified.length === 0 ? (
                <Chip state="approved" label="Clean" />
              ) : (
                <Chip state="needs-check" label={`${status.git.modified.length} modified`} />
              )}
            </div>
            {!status.git.gitAvailable ? (
              <p class="muted">Git status unavailable in this workspace.</p>
            ) : status.git.modified.length === 0 ? (
              <p class="muted">No uncommitted edits in CMS-managed pack or ledger files.</p>
            ) : (
              <ul class="git-file-list">
                {status.git.modified.map(file => (
                  <li key={file.path}>
                    <code class="git-code">{file.status}</code>
                    <code>{file.path}</code>
                  </li>
                ))}
              </ul>
            )}
          </article>
        </div>
      ) : null}
    </section>
  );
}
