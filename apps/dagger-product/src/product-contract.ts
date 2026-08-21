/**
 * Product-service contract: mirrors the Rust `ProductReadout` — the admitted
 * gameplay package's definitions plus live state and resolution explanation.
 * Browser surfaces do not evaluate gameplay; `gameplay/src` authors inert
 * definitions and Rust owns their meaning.
 */

export type Json = string | number | boolean | null | Json[] | { [key: string]: Json };

export interface StatsSection {
  readonly attributes: readonly string[];
  readonly skills: readonly string[];
  readonly tracks: readonly string[];
  readonly armorParts?: readonly string[];
  readonly progression?: readonly string[];
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

export interface LoadoutEntry {
  readonly item: string;
  readonly quantity?: number;
  readonly equipSlot?: string;
}

export interface ActorDefinition {
  readonly id: string;
  readonly kind: 'player' | 'monster' | 'enemy-class';
  readonly mobileId?: number;
  readonly stats: Readonly<Record<string, number>>;
  readonly skills: Readonly<Record<string, number>>;
  readonly armorValue: number;
  readonly tracks: readonly TrackDefinition[];
  readonly moveSpeed?: number;
  readonly behavior?: BehaviorDefinition;
  readonly level?: number;
  readonly weight?: number;
  readonly minMetalToHit?: string;
  readonly team?: string;
  readonly lootTableKey?: string;
  /** Kill-XP experiment profile: xp the player earns for killing this actor. */
  readonly xpReward?: number;
  /** Career-owned hit-points-per-level bound (player). */
  readonly hitPointsPerLevel?: number;
  readonly attacks?: readonly Readonly<{ min: number; max: number }>[];
  readonly inventory?: readonly LoadoutEntry[];
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
  readonly hands: 'either' | 'both' | 'leftOnly';
}

export interface ArmorDefinition {
  readonly material: string;
  readonly piece: string;
}

export interface ShieldDefinition {
  readonly value: number;
}

export interface ItemDefinition {
  readonly id: string;
  readonly weapon?: WeaponDefinition;
  readonly armor?: ArmorDefinition;
  readonly shield?: ShieldDefinition;
  /** Weight in the classic quarter-kg unit. */
  readonly weightUnits: number;
  /** Value in gold pieces. */
  readonly value: number;
}

export interface EquipmentSlotDefinition {
  readonly id: string;
  readonly allowedClassifications: readonly string[];
}

export interface EquipmentSection {
  readonly capacityMetrics: readonly string[];
  readonly slots: readonly EquipmentSlotDefinition[];
}

export interface DerivedRule {
  readonly id: string;
  readonly expr: Json;
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
  readonly derived: readonly DerivedRule[];
  readonly equipment?: EquipmentSection;
}

export interface ActorGameplayReadout {
  /** Compatibility summary used by existing product surfaces. */
  readonly attributes: {
    readonly strength: number;
    readonly endurance: number;
    readonly intelligence: number;
  };
  /** Live mechanics values for the eight primary attributes. */
  readonly evaluatedAttributes: Readonly<Record<string, number>>;
  /** Live reflexes setting, represented as Average by the sheet for value 2. */
  readonly reflexes: number;
  /** Live values for only the skills actually modeled by this actor. */
  readonly modeledSkills: Readonly<Record<string, number>>;
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
  /** Weapon item id the swing resolved with, or "unarmed". */
  readonly weapon: string;
  /** The body part the struck-part roll selected, when the action reads one. */
  readonly struckPart: string | null;
  /** True when the target's minMetalToHit gated the damage to 0. */
  readonly materialIneffective: boolean;
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
  readonly kind: 'enemy' | 'treasure';
  readonly name: string;
  /** Decoded mobile reference for enemies; null for treasure containers. */
  readonly reference: {
    readonly mobileId: number;
    readonly mobileName: string;
    readonly textureArchive: number;
    readonly flying: boolean;
    readonly spriteAsset: string;
    readonly authoredPosition: readonly [number, number, number];
  } | null;
  /** Classic loot table key for treasure containers; null for enemies. */
  readonly lootKey: string | null;
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

export interface InventoryCapacityReadout {
  readonly metric: string;
  readonly used: number;
  readonly maximum: number | null;
}

export interface InventoryStackReadout {
  readonly item: string;
  readonly quantity: number;
}

export interface InventoryItemReadout {
  readonly item: string;
  readonly entity: number;
  readonly equipSlot: string | null;
}

export interface PlayerInventoryReadout {
  readonly capacity: readonly InventoryCapacityReadout[];
  readonly stacks: readonly InventoryStackReadout[];
  readonly items: readonly InventoryItemReadout[];
}

/** One Rust-owned inventory/equipment action receipt, including loot-window actions. */
export interface EquipmentLogRecord {
  readonly sequence: number;
  /** equip | unequip | swap | grant | loot-open | loot-transfer */
  readonly operation: string;
  /** Item definition id, or the opened container id for loot-open. */
  readonly item: string;
  readonly slots: readonly string[];
  /** For a swap, the item definition id it replaced. */
  readonly replacedItem: string | null;
  /** Stack size for fungible grants or loot transfers. */
  readonly quantity: number | null;
  readonly accepted: boolean;
  /** Upstream rejection reason when the mutation was refused. */
  readonly reason: string | null;
  /** Committed component revision after a mutation; null on rejection or non-mutating open. */
  readonly committedRevision: number | null;
}

/** One success slot's roll record in a loot generation receipt. */
export interface LootRollOutcomeReadout {
  readonly slot: number;
  readonly chance: number;
  readonly roll: number;
  readonly success: boolean;
  readonly pick: number | null;
  readonly item: string | null;
}

/** One rolled loot category's outcome; `supported: false` marks categories with no catalog pool yet. */
export interface LootCategoryOutcomeReadout {
  readonly category: string;
  readonly chance: number;
  readonly effectiveChance: number;
  readonly supported: boolean;
  readonly rolls: readonly LootRollOutcomeReadout[];
}

/** The spawn-time loot generation receipt (DaggerLootGeneration). */
export interface LootGenerationReadout {
  readonly key: string;
  readonly level: number;
  readonly gold: { readonly roll: number; readonly level: number; readonly amount: number } | null;
  readonly categories: readonly LootCategoryOutcomeReadout[];
  readonly items: readonly (readonly [string, number])[];
}

/** One live loot container (treasure pile or loot-bearing enemy corpse). */
export interface LootContainerReadout {
  /** Container instance id (`treasure-<id>` / `enemy-<id>`). */
  readonly id: string;
  readonly kind: 'treasure' | 'corpse';
  /** The scene content entity this container anchors to. */
  readonly contentEntityId: number;
  readonly lootKey: string;
  /** Current Engine inventory revision for stale-transfer protection. */
  readonly sourceInventoryRevision: number;
  /** Current contents from InventoryService::view on the container entity. */
  readonly contents: PlayerInventoryReadout;
  /** Spawn-time generation receipt, including unsupported-category coverage. */
  readonly generation: LootGenerationReadout;
  readonly emptied: boolean;
}

/** One level-up's outcome inside a progression record (DaggerLevelUpOutcome). */
export interface LevelUpOutcomeReadout {
  /** The level gained (2 for the first level-up from the spawn base 1). */
  readonly level: number;
  /** Evidence id the roll crossed as (`<killer>.level-up.<level>.hp-roll`). */
  readonly rollEvidence: string;
  /** The bounded roll value in [hitPointsPerLevel/2, hitPointsPerLevel]. */
  readonly roll: number;
  /** The rule result applied to health-max AND current health. */
  readonly hitPoints: number;
  readonly healthMaxBefore: number;
  readonly healthMaxAfter: number;
}

/** One kill-XP award receipt (DaggerProgressionRecord). */
export interface ProgressionRecordReadout {
  /** Victim's actor definition id. */
  readonly victim: string;
  readonly xpAwarded: number;
  readonly xpBefore: number;
  readonly xpAfter: number;
  readonly levelBefore: number;
  readonly levelAfter: number;
  readonly levelUps: readonly LevelUpOutcomeReadout[];
}

/** Kill-XP progression state: live stats, pacing to next level, health, history. */
export interface ProgressionReadout {
  readonly xp: number;
  readonly level: number;
  /** xp remaining until the next `xp-level` threshold (the next level). */
  readonly xpToNextLevel: number;
  readonly currentHealth: number;
  readonly maxHealth: number;
  readonly history: readonly ProgressionRecordReadout[];
}

/** One Rust-authored, transient semantic notice for passive product display. */
export interface ProductNoticeRecord {
  readonly sequence: number;
  readonly kind: 'material-ineffective' | 'empty-container' | 'capacity-rejected' | 'level-up';
  /** Final Rust-authored wording; the browser must not reinterpret it. */
  readonly message: string;
}

export interface ProductReadout {
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
  readonly openLootContainerId: string | null;
  readonly namedEncounters: readonly NamedEncounterReadout[];
  readonly activeEncounter: NamedEncounterReadout | null;
  readonly playerInventory: PlayerInventoryReadout;
  /** Ordered Rust-owned inventory/equipment action receipts. */
  readonly equipmentLog: readonly EquipmentLogRecord[];
  /** Live loot containers (treasure piles + loot-bearing corpses). */
  readonly lootContainers: readonly LootContainerReadout[];
  /** Kill-XP progression state: xp, level, pacing, health, and the award history. */
  readonly progression: ProgressionReadout;
  /** Bounded semantic feedback tail; emitted only by Rust mutation hooks. */
  readonly notices: readonly ProductNoticeRecord[];
}
