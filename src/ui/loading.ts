/**
 * The loading screen: slow work the product has accepted (a new game, a load, a journey, an entrance) shown
 * from the update that accepted it until the world is ready. It is a modal `<dialog>` above every other
 * surface, so the click is acknowledged at once and nothing under it takes a press while the work runs; the
 * product holds the world itself, so this only draws what it publishes.
 *
 * The state arrives as the snapshot's `loading` field. The bar is determinate when the product measures how
 * far the work has come and indeterminate (a moving band, no percentage) when it does not. The frame and bar are
 * the kit's skin primitives.
 */
export interface LoadingProjection {
  readonly title: string;
  readonly label: string;
  readonly step: number;
  readonly steps: number;
  /** The share of the work finished, or null when nothing is measured. */
  readonly fraction: number | null;
}

export interface LoadingScreen {
  readonly dialog: HTMLDialogElement;
  /** Whether work is loading and the screen is up. */
  readonly open: () => boolean;
  /** Where focus rests while the screen is up: the frame itself, since it has no controls. */
  readonly initialFocus: () => HTMLElement;
  /** Draws a published loading value: an object shows the screen, null closes it. */
  update(value: unknown): void;
}

export function isLoadingProjection(value: unknown): value is LoadingProjection {
  if (typeof value !== 'object' || value === null) return false;
  const candidate = value as Record<string, unknown>;
  return typeof candidate.title === 'string' && typeof candidate.label === 'string'
    && typeof candidate.step === 'number' && typeof candidate.steps === 'number'
    && (candidate.fraction === null || typeof candidate.fraction === 'number');
}

export function mountLoading(host: HTMLElement): LoadingScreen {
  const dialog = document.createElement('dialog');
  dialog.className = 'dagger-loading dagger-frame';
  dialog.setAttribute('aria-labelledby', 'dagger-loading-title');
  dialog.setAttribute('aria-busy', 'true');
  dialog.tabIndex = -1;
  const title = document.createElement('h2');
  title.id = 'dagger-loading-title';
  title.className = 'dagger-frame-title';
  const bar = document.createElement('div');
  bar.className = 'dagger-loading-bar dagger-panel';
  bar.setAttribute('role', 'progressbar');
  bar.setAttribute('aria-labelledby', 'dagger-loading-label');
  bar.setAttribute('aria-valuemin', '0');
  bar.setAttribute('aria-valuemax', '100');
  const fill = document.createElement('div');
  fill.className = 'dagger-loading-fill';
  bar.append(fill);
  const label = document.createElement('p');
  label.id = 'dagger-loading-label';
  label.className = 'dagger-loading-label';
  dialog.append(title, bar, label);
  host.append(dialog);
  // Escape cannot dismiss work the product is doing; the screen closes when the product says it is done.
  dialog.addEventListener('cancel', event => event.preventDefault());

  return {
    dialog,
    open: () => dialog.open,
    initialFocus: () => dialog,
    update(value) {
      if (!isLoadingProjection(value)) {
        if (dialog.open) dialog.close();
        return;
      }
      title.textContent = value.title;
      label.textContent = value.label;
      const measured = value.fraction !== null;
      bar.classList.toggle('is-indeterminate', !measured);
      if (measured) {
        const percent = Math.round(Math.min(1, Math.max(0, value.fraction!)) * 100);
        bar.setAttribute('aria-valuenow', String(percent));
        bar.setAttribute('aria-valuetext', `${value.label}, step ${Math.min(value.step + 1, value.steps)} of ${value.steps}`);
        fill.style.width = `${percent}%`;
      } else {
        bar.removeAttribute('aria-valuenow');
        bar.setAttribute('aria-valuetext', value.label);
        fill.style.removeProperty('width');
      }
      if (!dialog.open) dialog.showModal();
      dialog.focus();
    },
  };
}
