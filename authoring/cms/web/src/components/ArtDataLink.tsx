import type { ComponentChildren } from 'preact';
import { useApp } from '../state';
import { ReviewBadge } from './ReviewControls';
import { Chip, SpritePreview } from './ui';

export interface ArtDataLinkProps {
  href: string;
  id: string;
  title: string;
  subtitle?: string;
  thumbnail?: string | null;
  pixel?: boolean;
  reviewCategory?: string;
  reviewId?: string;
  badge?: { state: string; label?: string } | null;
  meta?: ComponentChildren;
  actions?: ComponentChildren;
}

/**
 * Shared Art <-> Data cross-link card used on both art pages (linking to cast/spawners/data)
 * and game-data pages (linking to classic sprites, npc-### records, and concept art).
 */
export function ArtDataCard({
  href,
  id,
  title,
  subtitle,
  thumbnail,
  pixel = true,
  reviewCategory,
  reviewId,
  badge,
  meta,
  actions,
}: ArtDataLinkProps) {
  const { reviewFor } = useApp();
  const review = reviewCategory && reviewId ? reviewFor(reviewCategory, reviewId) : null;

  return (
    <article class="art-data-card">
      <a class="art-data-link" href={href}>
        {thumbnail !== undefined ? (
          <SpritePreview url={thumbnail} alt={title} pixel={pixel} className="art-data-thumb" />
        ) : null}
        <div class="art-data-body">
          <div class="art-data-top">
            <code>{id}</code>
            {badge ? <Chip state={badge.state} label={badge.label} /> : null}
            {review ? <ReviewBadge row={review} /> : null}
          </div>
          <strong>{title}</strong>
          {subtitle ? <small class="muted">{subtitle}</small> : null}
          {meta ? <div class="art-data-meta">{meta}</div> : null}
        </div>
      </a>
      {actions ? <div class="art-data-actions">{actions}</div> : null}
    </article>
  );
}

/** Compact inline chip for cross-referencing an art record or data entity inside tables. */
export function ArtDataChip({
  href,
  id,
  label,
  thumbnail,
  pixel = true,
}: {
  href: string;
  id: string;
  label?: string;
  thumbnail?: string | null;
  pixel?: boolean;
}) {
  return (
    <a class="art-data-chip" href={href}>
      {thumbnail ? (
        <img
          src={thumbnail}
          alt=""
          class={pixel ? 'art-data-chip-img pixel' : 'art-data-chip-img'}
          loading="lazy"
        />
      ) : null}
      <span>{label ?? id}</span>
      <code>{id}</code>
    </a>
  );
}
