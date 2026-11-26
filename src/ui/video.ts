import { mountVideoOptions, type ProductVideoOption, type VideoOptionsMount } from '@rusty-engine/video-options';
import type { ControlAction, GroundCoverChoice, InteriorExtrudeChoice, LampShadowChoice, SurfaceReliefChoice } from './controls.js';

/**
 * Settings' video section: the Engine's video options panel, drawn beside the controls. The Engine owns its
 * catalogue, applies a choice at once and keeps it for the install, so no Engine video setting is game state. The game
 * adds its own rows beside them, ground cover and its distance, beside the Engine's shadow budget which lamps cast
 * shadows, in what sectors and whether outdoors, how deep buildings' relief draws and how far, and outward interior
 * relief with its depth, cutoff and reach, which it keeps with the player's other preferences. The
 * panel mounts the first time Settings opens and reads the catalogue again each time after, since presets and other
 * pages change it.
 */
export interface VideoSettings {
  show(): void;
  update(groundCover: GroundCoverChoice | null | undefined, lampShadows: LampShadowChoice | null | undefined,
    surfaceRelief: SurfaceReliefChoice | null | undefined,
    interiorExtrude?: InteriorExtrudeChoice | null | undefined): void;
  dispose(): void;
}

/** The Engine's Advanced switches: renderer diagnostics, not choices a player makes. */
const HIDDEN = ['clusteredLighting', 'gpuCulling', 'occlusionCulling'];

export function mountVideoSettings(root: HTMLElement, send: (action: ControlAction) => void): VideoSettings {
  let panel: Promise<VideoOptionsMount> | null = null;
  let ground: GroundCoverChoice | null = null;
  let lamps: LampShadowChoice | null = null;
  const shadows = (change: Partial<Pick<LampShadowChoice, 'lamps' | 'sectors' | 'outdoors'>>) => {
    if (!lamps) return;
    const next = { ...lamps, ...change };
    send({ action: 'controls-lamp-shadows', item: next.lamps, scale: next.sectors, enabled: next.outdoors });
  };
  const lampOptions = (): ProductVideoOption[] => lamps ? [
    {
      id: 'lampShadows', label: 'Lamp shadows', group: 'Quality', kind: 'choice', value: lamps.lamps,
      choices: lamps.lampChoices.map(choice => ({ value: choice.id, label: choice.label })),
      description: 'Which lamps near you cast shadows, by how far they light: only the farthest-reaching, every lamp that lights a room, or every lamp, candles too. The shadow budget keeps the nearest.',
      onChange: value => shadows({ lamps: String(value) }),
    },
    {
      id: 'shadowSectors', label: 'Shadow sectors', group: 'Quality', kind: 'choice', value: String(lamps.sectors),
      choices: lamps.sectorShares.map(share => ({ value: String(share), label: share === 0 ? 'None' : share === 1 ? 'Standard' : `${share}× standard` })),
      description: 'Lamps cast only in your part of a place and the parts around it. Larger parts let more far lamps cast; None lets every lamp of the place.',
      onChange: value => shadows({ sectors: Number(value) }),
    },
    {
      id: 'outdoorLampShadows', label: 'Outdoor lamp shadows', group: 'Quality', kind: 'toggle', value: lamps.outdoors,
      description: 'Street lamps and lamps outdoors cast shadows too, near you, beside the sun\'s.',
      onChange: value => shadows({ outdoors: value === true }),
    },
  ] : [];
  const groundOptions = (): ProductVideoOption[] => ground ? [
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
  let relief: SurfaceReliefChoice | null = null;
  const reliefOptions = (): ProductVideoOption[] => relief ? [
    {
      id: 'exteriorExtrude', label: 'Building outward relief', group: 'Buildings', kind: 'toggle', value: relief.extrude,
      description: 'Project stonework, timber, reliefs and trim outward from building walls.',
      onChange: value => send({ action: 'controls-exterior-extrude', enabled: value === true }),
    },
    {
      id: 'surfaceRelief', label: 'Surface relief', group: 'Buildings', kind: 'range',
      min: 0, max: relief.maximum, step: relief.step, unit: '×', value: relief.strength,
      description: 'How deep walls, doors and roofs look up close. 0 draws them flat.',
      onChange: value => send({ action: 'controls-surface-relief', scale: Number(value) }),
    },
    {
      id: 'reliefDistance', label: 'Relief distance', group: 'Buildings', kind: 'range',
      min: relief.distanceMinimum, max: relief.distanceMaximum, step: relief.distanceStep, unit: '×', value: relief.distance,
      description: 'How far from you walls, doors and roofs keep their relief before it fades flat. Farther costs more to draw.',
      onChange: value => send({ action: 'controls-relief-distance', scale: Number(value) }),
    },
  ] : [];
  let extrude: InteriorExtrudeChoice | null = null;
  const extrudeOptions = (): ProductVideoOption[] => extrude ? [
    {
      id: 'interiorExtrude', label: 'Interior outward relief', group: 'Interiors', kind: 'toggle', value: extrude.enabled,
      description: 'Project carved stonework, bricks and reliefs outward from interior and dungeon walls.',
      onChange: value => send({ action: 'controls-interior-extrude', enabled: value === true }),
    },
    {
      id: 'interiorExtrudeDepth', label: 'Outward relief depth', group: 'Interiors', kind: 'range',
      min: extrude.depthMinimum, max: extrude.depthMaximum, step: extrude.depthStep, unit: '×', value: extrude.depth,
      description: 'How far interior stonework and carvings project outward from walls.',
      onChange: value => send({ action: 'controls-interior-extrude-depth', scale: Number(value) }),
    },
    {
      id: 'interiorExtrudeCutoff', label: 'Outward relief cutoff', group: 'Interiors', kind: 'range',
      min: extrude.cutoffMinimum, max: extrude.cutoffMaximum, step: extrude.cutoffStep, value: extrude.cutoff,
      description: 'How much of each wall rises outward rather than staying recessed.',
      onChange: value => send({ action: 'controls-interior-extrude-cutoff', scale: Number(value) }),
    },
    {
      id: 'interiorExtrudeDistance', label: 'Outward relief distance', group: 'Interiors', kind: 'range',
      min: extrude.distanceMinimum, max: extrude.distanceMaximum, step: extrude.distanceStep, unit: '×', value: extrude.distance,
      description: 'How far from you outward interior relief reaches before fading back to the wall.',
      onChange: value => send({ action: 'controls-interior-extrude-distance', scale: Number(value) }),
    },
  ] : [];
  const options = (): ProductVideoOption[] => [...lampOptions(), ...groundOptions(), ...reliefOptions(), ...extrudeOptions()];
  return {
    show() {
      if (panel) void panel.then(mounted => mounted.refresh());
      else panel = mountVideoOptions(root, { hide: HIDDEN, productOptions: options() });
    },
    update(next, nextLamps, nextRelief, nextExtrude) {
      const changed = JSON.stringify([next ?? null, nextLamps ?? null, nextRelief ?? null, nextExtrude ?? null])
        !== JSON.stringify([ground, lamps, relief, extrude]);
      ground = next ?? null;
      lamps = nextLamps ?? null;
      relief = nextRelief ?? null;
      extrude = nextExtrude ?? null;
      if (changed) void panel?.then(mounted => mounted.setProductOptions(options()));
    },
    dispose() {
      void panel?.then(mounted => mounted.dispose());
    },
  };
}
