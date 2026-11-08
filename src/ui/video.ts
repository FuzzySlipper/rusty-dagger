import { mountVideoOptions, type ProductVideoOption, type VideoOptionsMount } from '@rusty-engine/video-options';
import type { ControlAction, GroundCoverChoice } from './controls.js';

/**
 * Settings' video section: the Engine's video options panel, drawn beside the controls. The Engine owns its
 * catalogue, applies a choice at once and keeps it for the install, so no Engine video setting is game state. The game
 * adds its own rows beside them, ground cover and its distance, which it keeps with the player's other preferences. The
 * panel mounts the first time Settings opens and reads the catalogue again each time after, since presets and other
 * pages change it.
 */
export interface VideoSettings {
  show(): void;
  update(groundCover: GroundCoverChoice | null | undefined): void;
  dispose(): void;
}

/** The Engine's Advanced switches: renderer diagnostics, not choices a player makes. */
const HIDDEN = ['clusteredLighting', 'gpuCulling', 'occlusionCulling'];

export function mountVideoSettings(root: HTMLElement, send: (action: ControlAction) => void): VideoSettings {
  let panel: Promise<VideoOptionsMount> | null = null;
  let ground: GroundCoverChoice | null = null;
  const options = (): ProductVideoOption[] => ground ? [
    {
      id: 'groundCover', label: 'Ground cover', group: 'Ground', kind: 'toggle', value: ground.grows,
      description: 'Grass, tufts and relief that lift the ground near you.',
      onChange: value => send({ action: 'controls-ground-cover', enabled: value === true }),
    },
    {
      id: 'clutterDistance', label: 'Clutter distance', group: 'Ground', kind: 'range',
      min: ground.minimum, max: ground.maximum, step: ground.step, unit: '×', value: ground.distance,
      description: 'How far ground cover reaches. Farther costs more to draw.',
      onChange: value => send({ action: 'controls-clutter-distance', scale: Number(value) }),
    },
  ] : [];
  return {
    show() {
      if (panel) void panel.then(mounted => mounted.refresh());
      else panel = mountVideoOptions(root, { hide: HIDDEN, productOptions: options() });
    },
    update(next) {
      const changed = JSON.stringify(next ?? null) !== JSON.stringify(ground);
      ground = next ?? null;
      if (changed) void panel?.then(mounted => mounted.setProductOptions(options()));
    },
    dispose() {
      void panel?.then(mounted => mounted.dispose());
    },
  };
}
