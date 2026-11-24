import type { ComponentChildren } from 'preact';
import type { ValidationIssue } from '../types';

export function ValidationSummary({
  error,
  issues = [],
}: {
  error?: string | null;
  issues?: ValidationIssue[];
}) {
  if (!error && !issues.length) return null;
  return (
    <div class="validation-summary" role="alert">
      {error ? <strong>{error}</strong> : null}
      {issues.length ? (
        <ul>
          {issues.map((issue, idx) => (
            <li key={`${issue.field}-${idx}`}>
              {issue.field ? <code>{issue.field}</code> : null} {issue.message}
            </li>
          ))}
        </ul>
      ) : null}
    </div>
  );
}

export function FieldRow({
  label,
  hint,
  error,
  children,
}: {
  label: string;
  hint?: string;
  error?: string | null;
  children: ComponentChildren;
}) {
  return (
    <div class={`field-row${error ? ' has-error' : ''}`}>
      <label class="field-label">
        <span>{label}</span>
        {hint ? <small class="muted">{hint}</small> : null}
        {children}
      </label>
      {error ? (
        <span class="field-error" role="alert">
          {error}
        </span>
      ) : null}
    </div>
  );
}

export function DirtyBar({
  dirty,
  saving = false,
  status = '',
  saveLabel = 'Save changes',
  onSave,
  onRevert,
}: {
  dirty: boolean;
  saving?: boolean;
  status?: string;
  saveLabel?: string;
  onSave: () => void;
  onRevert?: () => void;
}) {
  if (!dirty && !status) return null;
  return (
    <div class={`dirty-bar${dirty ? ' is-dirty' : ''}`} role="status">
      <div class="dirty-bar-copy">
        {dirty ? <span class="chip needs-check">Unsaved changes</span> : null}
        {status ? <output>{status}</output> : null}
      </div>
      {dirty ? (
        <div class="actions">
          {onRevert ? (
            <button type="button" disabled={saving} onClick={onRevert}>
              Revert
            </button>
          ) : null}
          <button type="button" class="primary" disabled={saving} onClick={onSave}>
            {saving ? 'Saving…' : saveLabel}
          </button>
        </div>
      ) : null}
    </div>
  );
}
