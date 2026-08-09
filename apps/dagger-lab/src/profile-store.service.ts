import { Injectable } from '@angular/core';
import { ExperimentDocument, cloneExperiment, documentFromDraft } from './lab-contract';

const STORAGE_KEY = 'rusty-dagger.experiment-profiles';

export interface ExperimentProfile {
  readonly id: string;
  readonly name: string;
  readonly document: ExperimentDocument;
}

@Injectable({ providedIn: 'root' })
export class ProfileStoreService {
  load(): ExperimentProfile[] {
    const encoded = globalThis.localStorage.getItem(STORAGE_KEY);
    if (encoded === null) return [];
    try {
      const value: unknown = JSON.parse(encoded);
      return Array.isArray(value) ? value.filter(isExperimentProfile).map(cloneProfile) : [];
    } catch {
      return [];
    }
  }

  persist(profiles: readonly ExperimentProfile[]): void {
    globalThis.localStorage.setItem(STORAGE_KEY, JSON.stringify(profiles));
  }

  create(name: string, document: ExperimentDocument): ExperimentProfile {
    return {
      id: globalThis.crypto.randomUUID(),
      name,
      document: documentFromDraft(cloneExperiment(document)),
    };
  }
}

export function documentsEqual(
  left: ExperimentDocument,
  right: ExperimentDocument,
): boolean {
  return (
    left.schemaVersion === right.schemaVersion &&
    left.player.movement.speedUnitsPerSecond === right.player.movement.speedUnitsPerSecond &&
    left.player.vitality.baseHealth === right.player.vitality.baseHealth &&
    left.player.vitality.endurance === right.player.vitality.endurance &&
    left.player.vitality.healthPerEndurance === right.player.vitality.healthPerEndurance
  );
}

function cloneProfile(profile: ExperimentProfile): ExperimentProfile {
  return {
    id: profile.id,
    name: profile.name,
    document: documentFromDraft(cloneExperiment(profile.document)),
  };
}

function isExperimentProfile(value: unknown): value is ExperimentProfile {
  if (typeof value !== 'object' || value === null) return false;
  const profile = value as Partial<ExperimentProfile>;
  return (
    typeof profile.id === 'string' &&
    profile.id.length > 0 &&
    typeof profile.name === 'string' &&
    profile.name.trim().length > 0 &&
    isExperimentDocument(profile.document)
  );
}

function isExperimentDocument(value: unknown): value is ExperimentDocument {
  if (typeof value !== 'object' || value === null) return false;
  const document = value as Record<string, unknown>;
  const player = document['player'];
  if (document['schemaVersion'] !== 1 || typeof player !== 'object' || player === null) {
    return false;
  }
  const playerRecord = player as Record<string, unknown>;
  const movement = playerRecord['movement'];
  const vitality = playerRecord['vitality'];
  if (
    typeof movement !== 'object' ||
    movement === null ||
    typeof vitality !== 'object' ||
    vitality === null
  ) {
    return false;
  }
  const movementRecord = movement as Record<string, unknown>;
  const vitalityRecord = vitality as Record<string, unknown>;
  return (
    typeof movementRecord['speedUnitsPerSecond'] === 'number' &&
    typeof vitalityRecord['baseHealth'] === 'number' &&
    typeof vitalityRecord['endurance'] === 'number' &&
    typeof vitalityRecord['healthPerEndurance'] === 'number'
  );
}
