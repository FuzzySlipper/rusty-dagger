import { ASSET_LABELS, isCharacterRow } from '../types';
import { useApp } from '../state';
import { ReviewPanel } from '../components/ReviewControls';
import { Breadcrumb, Empty, FactList, PageHeading, SpritePreview } from '../components/ui';

const VIEW_LABELS: Record<string, string> = {
  classic: 'Classic source',
  sprite: 'Sprite',
  front: 'Mesh front',
  side: 'Mesh side',
  rear: 'Mesh rear',
};

export function AssetDetailPage({ id }: { id: string }) {
  const { catalog, categoryOf } = useApp();
  if (!catalog) return null;

  const category = categoryOf(id);
  const row = category ? catalog[category].find(item => item.id === id) : undefined;
  if (!row || isCharacterRow(row)) {
    return (
      <>
        <PageHeading title="Unable to open this view" />
        <Empty>Asset record not found.</Empty>
      </>
    );
  }

  const views = Object.entries(row.views ?? (row.preview ? { classic: row.preview } : {}));

  return (
    <>
      <Breadcrumb href={`#${row.category}`} label={ASSET_LABELS[row.category]} />
      <PageHeading
        title={row.name}
        intro={`${row.id}${row.kind ? ` · ${row.kind}` : ''}`}
        action={
          row.views ? (
            <a class="button" href="/media/flora/">
              Original flora review
            </a>
          ) : undefined
        }
      />
      <FactList facts={row.facts} />
      {views.length ? (
        <div class="asset-views">
          {views.map(([angle, url]) => (
            <article key={angle} class="panel depiction">
              <h2>{VIEW_LABELS[angle] ?? angle}</h2>
              <SpritePreview url={url} alt={angle} pixel={angle === 'classic'} />
              <a class="source-link" href={url}>
                Open image
              </a>
            </article>
          ))}
        </div>
      ) : null}
      <ReviewPanel category={row.category} id={row.id} />
    </>
  );
}
