import { apiRequest } from '../api';
import type { ArtMode, ArtReplacementModel, ClimateOption, SeasonName, WorldAppearanceData } from '../types';

export interface RenderAppearanceConfig {
  data: WorldAppearanceData | null;
  artMode: ArtMode;
  climateValue: number | null;
  season: SeasonName;
  dungeonRemaps: Record<string, number> | null;
  isExterior: boolean;
}

let appearancePromise: Promise<WorldAppearanceData> | null = null;

export function fetchWorldAppearance(): Promise<WorldAppearanceData> {
  if (!appearancePromise) {
    appearancePromise = apiRequest<WorldAppearanceData>('/api/world-blocks/appearance').catch(err => {
      appearancePromise = null;
      throw err;
    });
  }
  return appearancePromise;
}

export function findClimateOption(data: WorldAppearanceData | null, climateValue: number | null): ClimateOption | null {
  if (!data?.climates.length) return null;
  if (climateValue !== null) {
    const found = data.climates.find(c => c.value === climateValue);
    if (found) return found;
  }
  return data.climates.find(c => c.value === 231) ?? data.climates[0];
}

export function resolveGroundArchive(config: RenderAppearanceConfig, fallbackArchive: number): number {
  const climate = findClimateOption(config.data, config.climateValue);
  if (!climate) return fallbackArchive;
  return climate.ground[config.season] ?? climate.ground.summer ?? fallbackArchive;
}

export function resolveMaterialArchive(
  archive: number,
  record: number,
  config: RenderAppearanceConfig,
): number {
  // Dungeon texture table remaps take precedence when active (RDB dungeons)
  if (config.dungeonRemaps) {
    const remapped = config.dungeonRemaps[String(archive)];
    return remapped !== undefined ? remapped : archive;
  }

  // RMB exteriors/interiors: match DaggerfallSiteContent.ExteriorTextureArchive + SwapArchive
  const climate = findClimateOption(config.data, config.climateValue);
  if (!climate || !config.data) return archive;

  if (config.isExterior && archive < 500 && archive % 100 === 2) {
    return climate.ground[config.season] ?? archive;
  }

  const rules = config.data.swaps?.[climate.climateBase]?.[config.season]?.[String(archive)];
  if (!rules) return archive;
  for (const [firstRec, lastRec, target] of rules) {
    if (record >= firstRec && record <= lastRec) {
      return target;
    }
  }
  return archive;
}

export function resolveSurfaceUrl(
  archive: number,
  record: number,
  materialsBaseUrl: string,
  config: RenderAppearanceConfig,
): { url: string; refreshed: boolean; cacheKey: string } {
  const drawn = resolveMaterialArchive(archive, record, config);
  if (config.artMode === 'refreshed' && config.data?.artReplacements.available) {
    const refreshedUrl = config.data.artReplacements.surfaces[`${drawn}-${record}`];
    if (refreshedUrl) {
      return { url: refreshedUrl, refreshed: true, cacheKey: `refreshed:surface:${drawn}-${record}` };
    }
  }
  return {
    url: `${materialsBaseUrl}texture-${drawn}-${record}.png`,
    refreshed: false,
    cacheKey: `classic:surface:${drawn}-${record}`,
  };
}

export function resolveGroundUrl(
  record: number,
  fallbackArchive: number,
  terrainBaseUrl: string,
  config: RenderAppearanceConfig,
): { url: string; refreshed: boolean; cacheKey: string } {
  const archive = resolveGroundArchive(config, fallbackArchive);
  if (config.artMode === 'refreshed' && config.data?.artReplacements.available) {
    const refreshedUrl = config.data.artReplacements.surfaces[`${archive}-${record}`];
    if (refreshedUrl) {
      return { url: refreshedUrl, refreshed: true, cacheKey: `refreshed:ground:${archive}-${record}` };
    }
  }
  return {
    url: `${terrainBaseUrl}texture-${archive}-${record}.png`,
    refreshed: false,
    cacheKey: `classic:ground:${archive}-${record}`,
  };
}

export function resolveBillboardUrl(
  archive: number,
  record: number,
  climateNature: boolean,
  billboardsBaseUrl: string,
  config: RenderAppearanceConfig,
): { url: string; refreshed: boolean } {
  let drawn = archive;
  if (climateNature) {
    const climate = findClimateOption(config.data, config.climateValue);
    if (climate) {
      drawn = climate.nature[config.season] ?? archive;
    }
  } else if (config.dungeonRemaps) {
    const remapped = config.dungeonRemaps[String(archive)];
    if (remapped !== undefined) drawn = remapped;
  }

  if (config.artMode === 'refreshed' && config.data?.artReplacements.available) {
    const refreshedUrl = config.data.artReplacements.scenery[`${drawn}-${record}`];
    if (refreshedUrl) {
      return { url: refreshedUrl, refreshed: true };
    }
  }
  return { url: `${billboardsBaseUrl}texture-${drawn}-${record}.png`, refreshed: false };
}

export function resolveModelOverride(
  modelId: string,
  config: RenderAppearanceConfig,
): ArtReplacementModel | null {
  if (config.artMode !== 'refreshed' || !config.data?.artReplacements.available) {
    return null;
  }
  return config.data.artReplacements.models[modelId] ?? null;
}
