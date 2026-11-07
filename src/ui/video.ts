import { mountVideoOptions, type VideoOptionsMount } from '@rusty-engine/video-options';

/**
 * Settings' video section: the Engine's video options panel, drawn beside the controls. The Engine owns the
 * catalogue, applies a choice at once and keeps it for the install, so no video setting is game state. The panel
 * mounts the first time Settings opens and reads the catalogue again each time after, since presets and other pages
 * change it.
 */
export interface VideoSettings {
  show(): void;
  dispose(): void;
}

/** The Engine's Advanced switches: renderer diagnostics, not choices a player makes. */
const HIDDEN = ['clusteredLighting', 'gpuCulling', 'occlusionCulling'];

export function mountVideoSettings(root: HTMLElement): VideoSettings {
  let panel: Promise<VideoOptionsMount> | null = null;
  return {
    show() {
      if (panel) void panel.then(mounted => mounted.refresh());
      else panel = mountVideoOptions(root, { hide: HIDDEN });
    },
    dispose() {
      void panel?.then(mounted => mounted.dispose());
    },
  };
}
