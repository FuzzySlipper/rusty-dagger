export interface ExperimentDocument {
  readonly schemaVersion: 1;
  readonly player: {
    readonly movement: { readonly speedUnitsPerSecond: number };
    readonly stats: ActorStatsInputs;
    readonly combat: PlayerCombatTerms;
  };
  readonly enemies: readonly EnemyExperiment[];
}

export interface ExperimentDraft {
  schemaVersion: 1;
  player: {
    movement: { speedUnitsPerSecond: number };
    stats: ActorStatsDraft;
    combat: PlayerCombatTermsDraft;
  };
  enemies: EnemyExperimentDraft[];
}

export interface EnemyExperiment {
  readonly mobileId: number;
  readonly stats: ActorStatsInputs;
  readonly combat: EnemyCombatTerms;
}

export interface EnemyExperimentDraft {
  mobileId: number;
  stats: ActorStatsDraft;
  combat: EnemyCombatTermsDraft;
}

export interface PlayerCombatTerms {
  readonly attackRange: number;
  readonly hitBonus: number;
  readonly baseDamage: number;
  readonly damagePerStrength: number;
}

export interface PlayerCombatTermsDraft {
  attackRange: number;
  hitBonus: number;
  baseDamage: number;
  damagePerStrength: number;
}

export interface EnemyCombatTerms {
  readonly defense: number;
  readonly armor: number;
}

export interface EnemyCombatTermsDraft {
  defense: number;
  armor: number;
}

export interface ActorStatsInputs {
  readonly attributes: ActorAttributes;
  readonly resources: ActorResourceTerms;
}

export interface ActorStatsDraft {
  attributes: { strength: number; endurance: number; intelligence: number };
  resources: {
    baseHealth: number;
    healthPerEndurance: number;
    baseStamina: number;
    staminaPerAttribute: number;
    baseMagicka: number;
    magickaPerIntelligence: number;
  };
}

export interface ActorAttributes {
  readonly strength: number;
  readonly endurance: number;
  readonly intelligence: number;
}

export interface ActorResourceTerms {
  readonly baseHealth: number;
  readonly healthPerEndurance: number;
  readonly baseStamina: number;
  readonly staminaPerAttribute: number;
  readonly baseMagicka: number;
  readonly magickaPerIntelligence: number;
}

export interface CalculationStep {
  readonly operation: string;
  readonly left: number;
  readonly right: number;
  readonly result: number;
}

export interface CalculationDetails {
  readonly rule: string;
  readonly expression: string;
  readonly inputs: readonly { readonly name: string; readonly value: number }[];
  readonly steps: readonly CalculationStep[];
  readonly result: number;
}

export interface CalculationRecord extends CalculationDetails {
  readonly sequence: number;
}

export interface AdmittedActorStats {
  readonly attributes: ActorAttributes;
  readonly maxHealth: number;
  readonly maxStamina: number;
  readonly maxMagicka: number;
  readonly calculations: readonly CalculationDetails[];
}

export interface ActorGameplayReadout extends AdmittedActorStats {
  readonly currentHealth: number;
  readonly currentStamina: number;
  readonly currentMagicka: number;
}

export interface EnemyStatsReadout {
  readonly mobileId: number;
  readonly stats: AdmittedActorStats;
}

export interface ExperimentEvaluation {
  readonly document: ExperimentDocument;
  readonly moveSpeedUnitsPerSecond: number;
  readonly maxHealth: number;
  readonly calculation: CalculationDetails;
  readonly playerStats: AdmittedActorStats;
  readonly enemyStats: readonly EnemyStatsReadout[];
}

export interface ExperimentReadout {
  readonly document: ExperimentDocument;
  readonly moveSpeedUnitsPerSecond: number;
  readonly maxHealth: number;
  readonly currentHealth: number;
  readonly playerStats: ActorGameplayReadout;
  readonly enemyStats: readonly EnemyStatsReadout[];
  readonly playerPosition: readonly [number, number, number];
  readonly playerYawDegrees: number;
  readonly calculations: readonly CalculationRecord[];
  readonly combat: readonly CombatRecord[];
  readonly content: readonly ContentEntityReadout[];
  readonly focusedContentId: number | null;
}

export interface CombatRecord {
  readonly sequence: number;
  readonly targetId: number;
  readonly range: number;
  readonly attackRange: number;
  readonly lineOfSightClear: boolean;
  readonly actor: string;
  readonly action: string;
  readonly target: string;
  readonly rawRoll: number;
  readonly hitBonus: number;
  readonly attackTotal: number;
  readonly targetDefense: number;
  readonly hit: boolean;
  readonly baseDamage: number;
  readonly strength: number;
  readonly damagePerStrength: number;
  readonly damageBeforeArmor: number;
  readonly armor: number;
  readonly finalDamage: number;
  readonly healthBefore: number;
  readonly healthAfter: number;
  readonly died: boolean;
}

export interface ContentEntityReadout {
  readonly id: number;
  readonly kind: 'enemy';
  readonly name: string;
  readonly reference: {
    readonly mobileId: number;
    readonly mobileName: string;
    readonly textureArchive: number;
    readonly flying: boolean;
    readonly spriteAsset: string;
    readonly authoredPosition: readonly [number, number, number];
  };
  readonly live: {
    readonly position: readonly [number, number, number];
    readonly distanceFromPlayer: number;
    readonly resources: {
      readonly currentHealth: number;
      readonly currentStamina: number;
      readonly currentMagicka: number;
    } | null;
  };
}

export function cloneExperiment(document: ExperimentDocument): ExperimentDraft {
  return {
    schemaVersion: 1,
    player: {
      movement: { speedUnitsPerSecond: document.player.movement.speedUnitsPerSecond },
      stats: cloneActorStats(document.player.stats),
      combat: { ...document.player.combat },
    },
    enemies: document.enemies.map((enemy) => ({
      mobileId: enemy.mobileId,
      stats: cloneActorStats(enemy.stats),
      combat: { ...enemy.combat },
    })),
  };
}

export function cloneActorStats(stats: ActorStatsInputs): ActorStatsDraft {
  return {
    attributes: { ...stats.attributes },
    resources: { ...stats.resources },
  };
}

export function documentFromDraft(draft: ExperimentDraft): ExperimentDocument {
  return {
    schemaVersion: 1,
    player: {
      movement: { speedUnitsPerSecond: draft.player.movement.speedUnitsPerSecond },
      stats: cloneActorStats(draft.player.stats),
      combat: { ...draft.player.combat },
    },
    enemies: draft.enemies.map((enemy) => ({
      mobileId: enemy.mobileId,
      stats: cloneActorStats(enemy.stats),
      combat: { ...enemy.combat },
    })),
  };
}
