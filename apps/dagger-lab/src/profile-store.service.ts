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
      id: createProfileId(),
      name,
      document: documentFromDraft(cloneExperiment(document)),
    };
  }
}

function createProfileId(): string {
  const bytes = new Uint8Array(16);
  if (typeof globalThis.crypto?.getRandomValues === 'function') {
    globalThis.crypto.getRandomValues(bytes);
  } else {
    // Profile IDs are local trusted-environment keys, not security tokens.
    for (let index = 0; index < bytes.length; index += 1) {
      bytes[index] = Math.floor(Math.random() * 256);
    }
  }
  bytes[6] = (bytes[6]! & 0x0f) | 0x40;
  bytes[8] = (bytes[8]! & 0x3f) | 0x80;
  const encoded = Array.from(bytes, (value) => value.toString(16).padStart(2, '0')).join('');
  return `${encoded.slice(0, 8)}-${encoded.slice(8, 12)}-${encoded.slice(12, 16)}-${encoded.slice(16, 20)}-${encoded.slice(20)}`;
}

export function documentsEqual(
  left: ExperimentDocument,
  right: ExperimentDocument,
): boolean {
  return JSON.stringify(left) === JSON.stringify(right);
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
  const stats = playerRecord['stats'];
  const combat = playerRecord['combat'];
  const enemies = document['enemies'];
  if (
    typeof movement !== 'object' ||
    movement === null ||
    !isActorStats(stats) ||
    !hasNumberFields(combat, [
      'attackRange',
      'attackCooldownSeconds',
      'staminaCost',
      'hitBonus',
      'baseDamage',
      'damagePerStrength',
    ]) ||
    !Array.isArray(enemies)
  ) {
    return false;
  }
  const movementRecord = movement as Record<string, unknown>;
  return (
    typeof movementRecord['speedUnitsPerSecond'] === 'number' &&
    enemies.every((enemy) => {
      if (typeof enemy !== 'object' || enemy === null) return false;
      const enemyRecord = enemy as Record<string, unknown>;
      return (
        typeof enemyRecord['mobileId'] === 'number' &&
        isActorStats(enemyRecord['stats']) &&
        hasNumberFields(enemyRecord['combat'], ['defense', 'armor']) &&
        hasNumberFields(enemyRecord['behavior'], [
          'detectionRange',
          'patrolSpeed',
          'chaseSpeed',
          'attackRange',
          'attackCooldownSeconds',
          'attackDamage',
        ])
      );
    })
  );
}

function hasNumberFields(value: unknown, fields: readonly string[]): boolean {
  if (typeof value !== 'object' || value === null) return false;
  const record = value as Record<string, unknown>;
  return fields.every((field) => typeof record[field] === 'number');
}

function isActorStats(value: unknown): boolean {
  if (typeof value !== 'object' || value === null) return false;
  const stats = value as Record<string, unknown>;
  const attributes = stats['attributes'];
  const resources = stats['resources'];
  if (
    typeof attributes !== 'object' ||
    attributes === null ||
    typeof resources !== 'object' ||
    resources === null
  ) {
    return false;
  }
  const attributeRecord = attributes as Record<string, unknown>;
  const resourceRecord = resources as Record<string, unknown>;
  return (
    ['strength', 'endurance', 'intelligence'].every(
      (key) => typeof attributeRecord[key] === 'number',
    ) &&
    [
      'baseHealth',
      'healthPerEndurance',
      'baseStamina',
      'staminaPerAttribute',
      'baseMagicka',
      'magickaPerIntelligence',
    ].every((key) => typeof resourceRecord[key] === 'number')
  );
}
