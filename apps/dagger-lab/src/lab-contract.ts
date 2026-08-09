export interface ExperimentDocument {
  readonly schemaVersion: 1;
  readonly player: {
    readonly movement: { readonly speedUnitsPerSecond: number };
    readonly vitality: VitalityInputs;
  };
}

export interface ExperimentDraft {
  schemaVersion: 1;
  player: {
    movement: { speedUnitsPerSecond: number };
    vitality: VitalityDraft;
  };
}

export interface VitalityInputs {
  readonly baseHealth: number;
  readonly endurance: number;
  readonly healthPerEndurance: number;
}

export interface VitalityDraft {
  baseHealth: number;
  endurance: number;
  healthPerEndurance: number;
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

export interface ExperimentEvaluation {
  readonly document: ExperimentDocument;
  readonly moveSpeedUnitsPerSecond: number;
  readonly maxHealth: number;
  readonly calculation: CalculationDetails;
}

export interface ExperimentReadout {
  readonly document: ExperimentDocument;
  readonly moveSpeedUnitsPerSecond: number;
  readonly maxHealth: number;
  readonly currentHealth: number;
  readonly playerPosition: readonly [number, number, number];
  readonly playerYawDegrees: number;
  readonly calculations: readonly CalculationRecord[];
}

export function cloneExperiment(document: ExperimentDocument): ExperimentDraft {
  return {
    schemaVersion: 1,
    player: {
      movement: { speedUnitsPerSecond: document.player.movement.speedUnitsPerSecond },
      vitality: cloneVitality(document.player.vitality),
    },
  };
}

export function cloneVitality(vitality: VitalityInputs): VitalityDraft {
  return {
    baseHealth: vitality.baseHealth,
    endurance: vitality.endurance,
    healthPerEndurance: vitality.healthPerEndurance,
  };
}

export function documentFromDraft(draft: ExperimentDraft): ExperimentDocument {
  return {
    schemaVersion: 1,
    player: {
      movement: { speedUnitsPerSecond: draft.player.movement.speedUnitsPerSecond },
      vitality: cloneVitality(draft.player.vitality),
    },
  };
}
