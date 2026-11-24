import type { ComponentChildren } from 'preact';
import { ASSET_LABELS, STATE_NAMES, isCharacterRow, type CatalogAssetRow, type PipelineSummary } from '../types';
import { assetUrl, pipelineUrl, subjectUrl } from '../router';
import { TileReview } from './ReviewControls';

export function PageHeading({
  title,
  intro,
  action,
}: {
  title: string;
  intro?: ComponentChildren;
  action?: ComponentChildren;
}) {
  return (
    <header class="page-heading">
      <div>
        <h1>{title}</h1>
        {intro ? <p>{intro}</p> : null}
      </div>
      {action ?? null}
    </header>
  );
}

export function Breadcrumb({ href, label }: { href: string; label: string }) {
  return (
    <a class="breadcrumb" href={href}>
      {label}
    </a>
  );
}

export function Chip({ state, label }: { state: string; label?: string }) {
  return <span class={`chip ${state}`}>{label ?? STATE_NAMES[state] ?? state}</span>;
}

export function SpritePreview({
  url,
  alt = '',
  pixel = false,
  className = '',
  lazy = true,
}: {
  url: string | null | undefined;
  alt?: string;
  pixel?: boolean;
  className?: string;
  lazy?: boolean;
}) {
  const classes = ['preview', pixel ? 'pixel' : '', className].filter(Boolean).join(' ');
  return (
    <div class={classes}>
      {url ? (
        <img src={url} alt={alt} loading={lazy ? 'lazy' : undefined} />
      ) : (
        <span class="muted">No preview</span>
      )}
    </div>
  );
}

export function FactList({ facts }: { facts: Record<string, string | number | boolean> }) {
  const entries = Object.entries(facts);
  if (!entries.length) return null;
  return (
    <dl class="facts">
      {entries.map(([name, value]) => (
        <div key={name}>
          <dt>{name}</dt>
          <dd>{typeof value === 'boolean' ? (value ? 'Yes' : 'No') : String(value)}</dd>
        </div>
      ))}
    </dl>
  );
}

export function Empty({ children }: { children: ComponentChildren }) {
  return <p class="empty">{children}</p>;
}

export function Tile({
  category,
  id,
  href,
  preview,
  name,
  status,
  pixel = false,
}: {
  category: string;
  id: string;
  href: string;
  preview: string | null | undefined;
  name: string;
  status: string;
  pixel?: boolean;
}) {
  return (
    <article class="tile">
      <a class="tile-link" href={href}>
        <SpritePreview url={preview} alt="" pixel={pixel} />
        <div class="tile-info">
          <code>{id}</code>
          <strong>{name}</strong>
          <small>{status}</small>
        </div>
      </a>
      <TileReview category={category} id={id} />
    </article>
  );
}

export function AssetTile({ row }: { row: CatalogAssetRow }) {
  if (isCharacterRow(row)) {
    const status = row.pipelineIds.length
      ? `${row.pipelineIds.length} linked pipeline${row.pipelineIds.length === 1 ? '' : 's'}`
      : row.kind;
    return (
      <Tile
        category={row.category}
        id={row.id}
        href={subjectUrl(row.id)}
        preview={row.classic}
        name={row.name}
        status={status}
        pixel
      />
    );
  }
  return (
    <Tile
      category={row.category}
      id={row.id}
      href={assetUrl(row.id)}
      preview={row.preview}
      name={row.name}
      status={row.kind || ASSET_LABELS[row.category]}
      pixel
    />
  );
}

export function PipelineTile({ row }: { row: PipelineSummary }) {
  const status = row.final
    ? 'Ready for game testing'
    : row.stages.find(stage => stage.state === 'needs-Patch' || stage.state === 'needs-check')?.name;
  return (
    <Tile
      category="pipelines"
      id={row.id}
      href={pipelineUrl(row.id)}
      preview={row.thumbnail}
      name={row.name}
      status={status || 'Character stages'}
    />
  );
}
