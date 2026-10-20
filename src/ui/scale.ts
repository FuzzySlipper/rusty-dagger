import type { RustyApplicationUiContext } from '@rusty-engine/product-ui';

/** The steps and bounds of the interface scale, as the ruleset publishes them with the controls. */
export interface UiScaleRange { readonly minimum: number; readonly maximum: number; readonly step: number }

/**
 * The layout every screen is designed for at UI scale 1: a 1920x1080 window in CSS pixels. At 4K with
 * the operating system at 200% the window is still 1920x1080 CSS pixels, so the scale stays 1 and the
 * kit's 2x art lands one image pixel per device pixel.
 */
export const REFERENCE_WIDTH = 1920;
export const REFERENCE_HEIGHT = 1080;

/**
 * The scale the interface uses: the player's chosen scale when there is one, otherwise the largest
 * step at which the reference layout still fits the window, within the published bounds.
 */
export function chooseScale(width: number, height: number, chosen: number | null, range: UiScaleRange): number {
  const clamp = (value: number): number => Math.min(range.maximum, Math.max(range.minimum, value));
  if (chosen !== null) return clamp(chosen);
  if (!(width > 0) || !(height > 0)) return clamp(1);
  const fit = Math.min(width / REFERENCE_WIDTH, height / REFERENCE_HEIGHT);
  // A tiny epsilon keeps an exact fit (3840/1920 = 2) from rounding down a step.
  return clamp(Math.floor(fit / range.step + 1e-9) * range.step);
}

/**
 * Applies the interface scale through the Engine's one UI scale (`context.ui.setScale`, which sets
 * the root font size, so every rem-sized surface follows). It follows the window while the player
 * has chosen automatic, and the player's choice otherwise; the choice itself is a player preference
 * the ruleset persists, never state kept here.
 */
export function mountScale(context: RustyApplicationUiContext): {
  update(chosen: number | null, range: UiScaleRange): void;
  current(): number;
  dispose(): void;
} {
  let chosen: number | null = null;
  // Nothing is applied until the ruleset has published its bounds with the first controls projection.
  let range: UiScaleRange | null = null;
  const apply = (): void => {
    if (range === null) return;
    const next = chooseScale(window.innerWidth, window.innerHeight, chosen, range);
    if (context.ui.scale() !== next) context.ui.setScale(next);
  };
  window.addEventListener('resize', apply);
  return {
    update(value, published) {
      if (range !== null && value === chosen && published.minimum === range.minimum && published.maximum === range.maximum && published.step === range.step) return;
      chosen = value; range = published;
      apply();
    },
    current: () => context.ui.scale(),
    dispose() { window.removeEventListener('resize', apply); },
  };
}
