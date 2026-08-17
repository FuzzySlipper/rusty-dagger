/**
 * Read-only lab contract: mirrors the Rust `LabReadout` — the committed
 * gameplay package's definitions plus live state and resolution explanation.
 * There is no editable document here; `gameplay/src` owns gameplay truth.
 */

export type Json = string | number | boolean | null | Json[] | { [key: string]: Json };

export interface StatsSection {
  readonly attributes: readonly string[];
  readonly skills: readonly string[];
  readonly tracks: readonly string[];
}

export interface TrackDefinition {
  readonly id: string;
  readonly max: Json;
}

export interface BehaviorDefinition {
  readonly detectionRange: number;
  readonly patrolSpeed: number;
  readonly chaseSpeed: number;
  readonly attackRange: number;
  readonly attackCooldownSeconds: number;
  readonly action: string;
}

export interface ActorDefinition {
  readonly id: string;
  readonly kind: 'player' | 'monster';
  readonly mobileId?: number;
  readonly stats: Readonly<Record<string, number>>;
  readonly skills: Readonly<Record<string, number>>;
  readonly armorValue: number;
  readonly tracks: readonly TrackDefinition[];
  readonly moveSpeed?: number;
  readonly behavior?: BehaviorDefinition;
}

export interface ActionDefinition {
  readonly id: string;
  readonly tags: readonly string[];
  readonly program: Json;
  /** Melee reach in world units, when the action has one. */
  readonly reach?: number;
  /** Cooldown between attempts in seconds. */
  readonly cooldownSeconds?: number;
}

export interface WeaponDefinition {
  readonly damage: Readonly<{ min: number; max: number }>;
  readonly material: string;
  readonly skill: string;
}

export interface ItemDefinition {
  readonly id: string;
  readonly weapon?: WeaponDefinition;
  readonly interceptor?: Readonly<{ kind: string; amount: number }>;
}

export interface RuleDefinition {
  readonly id: string;
  readonly kind: string;
  readonly tag: string;
  readonly condition: string;
}

export interface EncounterDefinition {
  readonly id: string;
  readonly name: string;
  readonly objective: string;
  readonly routeCode: string;
  readonly memberEntityIds: readonly number[];
}

export interface GameplayPackageReadout {
  readonly fingerprint: string;
  readonly schemaVersion: number;
  readonly stats: StatsSection;
  readonly actors: readonly ActorDefinition[];
  readonly actions: readonly ActionDefinition[];
  readonly items: readonly ItemDefinition[];
  readonly rules: readonly RuleDefinition[];
  readonly encounters: readonly EncounterDefinition[];
}

export interface ActorGameplayReadout {
  readonly attributes: {
    readonly strength: number;
    readonly endurance: number;
    readonly intelligence: number;
  };
  readonly maxHealth: number;
  readonly maxStamina: number;
  readonly maxMagicka: number;
  readonly currentHealth: number;
  readonly currentStamina: number;
  readonly currentMagicka: number;
}

export interface CombatRecord {
  readonly sequence: number;
  readonly targetId: number;
  readonly range: number;
  readonly attackRange: number;
  readonly lineOfSightClear: boolean;
  readonly action: string;
  readonly status: string;
  readonly roll: number;
  readonly hit: boolean;
  readonly damage: number;
  readonly died: boolean;
  readonly healthBefore: number;
  readonly healthAfter: number;
  readonly targetMaxHealth: number;
  readonly decisions: readonly string[];
  readonly events: readonly string[];
}

export interface CombatAttemptRecord {
  readonly sequence: number;
  readonly targetId: number | null;
  readonly accepted: boolean;
  readonly outcome: string;
  readonly cooldownBefore: number;
  readonly cooldownAfter: number;
  readonly cooldownDuration: number;
  readonly staminaBefore: number;
  readonly staminaCost: number;
  readonly staminaAfter: number;
}

export interface MeleePresentationReadout {
  readonly attemptSequence: number;
  readonly phase: 'anticipation' | 'contact' | 'recovery' | 'rejected';
  readonly phaseProgress: number;
  readonly accepted: boolean;
  readonly outcome: string;
  readonly targetId: number | null;
  readonly staminaBefore: number;
  readonly staminaAfter: number;
  readonly targetHealthBefore: number | null;
  readonly targetHealthAfter: number | null;
  readonly targetMaxHealth: number | null;
  readonly finalDamage: number | null;
  readonly died: boolean;
}

export interface NamedEncounterReadout {
  readonly id: string;
  readonly name: string;
  readonly objective: string;
  readonly routeCode: string;
  readonly memberEntityIds: readonly number[];
  readonly status: 'available' | 'active' | 'victory' | 'defeat';
}

export interface EncounterDecisionRecord {
  readonly sequence: number;
  readonly enemyId: number;
  readonly enemyName: string;
  readonly decision: string;
  readonly from: string | null;
  readonly to: string | null;
  readonly distanceToPlayer: number;
  readonly damage: number | null;
  readonly lineOfSightClear: boolean | null;
  readonly playerHealthBefore: number | null;
  readonly playerHealthAfter: number | null;
  readonly playerDied: boolean;
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
    readonly aiState: string | null;
  };
}

export interface LabReadout {
  readonly gameplayPackage: GameplayPackageReadout;
  readonly moveSpeedUnitsPerSecond: number;
  readonly maxHealth: number;
  readonly currentHealth: number;
  readonly playerStats: ActorGameplayReadout;
  readonly playerPosition: readonly [number, number, number];
  readonly playerYawDegrees: number;
  readonly combat: readonly CombatRecord[];
  readonly combatAttempts: readonly CombatAttemptRecord[];
  readonly playerAttackCooldownRemaining: number;
  readonly meleePresentation: MeleePresentationReadout | null;
  readonly encounterDecisions: readonly EncounterDecisionRecord[];
  readonly content: readonly ContentEntityReadout[];
  readonly focusedContentId: number | null;
  readonly namedEncounters: readonly NamedEncounterReadout[];
  readonly activeEncounter: NamedEncounterReadout | null;
}
