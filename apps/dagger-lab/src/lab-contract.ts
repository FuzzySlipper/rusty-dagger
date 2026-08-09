export interface ExperimentDocument {
  readonly schemaVersion: 1;
  readonly player: {
    readonly movement: {
      readonly speedUnitsPerSecond: number;
    };
    readonly vitality: {
      readonly baseHealth: number;
      readonly endurance: number;
      readonly healthPerEndurance: number;
    };
  };
}

export interface CalculationStep {
  readonly operation: string;
  readonly left: number;
  readonly right: number;
  readonly result: number;
}

export interface CalculationRecord {
  readonly sequence: number;
  readonly rule: string;
  readonly expression: string;
  readonly inputs: readonly { readonly name: string; readonly value: number }[];
  readonly steps: readonly CalculationStep[];
  readonly result: number;
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

export function cloneExperiment(document: ExperimentDocument): ExperimentDocument {
  return {
    schemaVersion: 1,
    player: {
      movement: {
        speedUnitsPerSecond: document.player.movement.speedUnitsPerSecond,
      },
      vitality: {
        baseHealth: document.player.vitality.baseHealth,
        endurance: document.player.vitality.endurance,
        healthPerEndurance: document.player.vitality.healthPerEndurance,
      },
    },
  };
}
