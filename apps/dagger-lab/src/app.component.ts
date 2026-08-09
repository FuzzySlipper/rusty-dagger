import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectorRef, Component, OnDestroy, OnInit, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { LabApiService } from './lab-api.service';
import {
  CalculationRecord,
  ExperimentDocument,
  ExperimentEvaluation,
  ExperimentReadout,
  VitalityDraft,
  cloneExperiment,
  cloneVitality,
  documentFromDraft,
} from './lab-contract';

const EMPTY_DOCUMENT: ExperimentDocument = {
  schemaVersion: 1,
  player: {
    movement: { speedUnitsPerSecond: 3.5 },
    vitality: { baseHealth: 25, endurance: 40, healthPerEndurance: 1.5 },
  },
};

@Component({
  selector: 'dagger-root',
  imports: [CommonModule, FormsModule],
  templateUrl: './app.component.html',
})
export class AppComponent implements OnInit, OnDestroy {
  private readonly api = inject(LabApiService);
  private readonly changeDetector = inject(ChangeDetectorRef);
  private pollTimer: ReturnType<typeof setInterval> | undefined;
  private loading = false;

  draft = cloneExperiment(EMPTY_DOCUMENT);
  worksheet: VitalityDraft = cloneVitality(EMPTY_DOCUMENT.player.vitality);
  evaluation: ExperimentEvaluation | undefined;
  readout: ExperimentReadout | undefined;
  connectionError = '';
  commandError = '';
  worksheetError = '';
  pending = false;
  evaluating = false;
  dirty = false;
  historyFilter = '';
  selectedSequence: number | undefined;

  ngOnInit(): void {
    void this.refresh(true);
    this.pollTimer = setInterval(() => void this.refresh(false), 250);
  }

  ngOnDestroy(): void {
    if (this.pollTimer !== undefined) clearInterval(this.pollTimer);
  }

  markDirty(): void {
    this.dirty = true;
  }

  async evaluateWorksheet(): Promise<void> {
    this.evaluating = true;
    this.worksheetError = '';
    try {
      const document = documentFromDraft(this.draft);
      const candidate: ExperimentDocument = {
        ...document,
        player: { ...document.player, vitality: cloneVitality(this.worksheet) },
      };
      this.evaluation = await this.api.evaluate(candidate);
    } catch (error: unknown) {
      this.evaluation = undefined;
      this.worksheetError = errorMessage(error);
    } finally {
      this.evaluating = false;
      this.changeDetector.markForCheck();
    }
  }

  async apply(): Promise<void> {
    const succeeded = await this.runCommand(() => this.api.apply(documentFromDraft(this.draft)));
    if (succeeded) this.dirty = false;
  }

  async reset(): Promise<void> {
    await this.runCommand(() => this.api.reset());
  }

  async resetAndPlay(): Promise<void> {
    await this.runCommand(() => this.api.play());
  }

  filteredCalculations(): readonly CalculationRecord[] {
    const filter = this.historyFilter.trim().toLowerCase();
    const calculations = this.readout?.calculations ?? [];
    if (filter === '') return calculations;
    return calculations.filter(
      (record) =>
        record.rule.toLowerCase().includes(filter) || `#${record.sequence}`.includes(filter),
    );
  }

  selectedCalculation(): CalculationRecord | undefined {
    const calculations = this.readout?.calculations ?? [];
    return (
      calculations.find((record) => record.sequence === this.selectedSequence) ??
      calculations.at(-1)
    );
  }

  selectCalculation(record: CalculationRecord): void {
    this.selectedSequence = record.sequence;
  }

  format(value: number): string {
    return value.toFixed(2);
  }

  private async refresh(syncDraft: boolean): Promise<void> {
    if (this.loading || this.pending) return;
    this.loading = true;
    try {
      const readout = await this.api.read();
      this.acceptReadout(readout);
      this.connectionError = '';
      if (syncDraft && !this.dirty) {
        this.draft = cloneExperiment(readout.document);
        this.worksheet = cloneVitality(readout.document.player.vitality);
      }
    } catch (error: unknown) {
      this.connectionError = errorMessage(error);
    } finally {
      this.loading = false;
      this.changeDetector.markForCheck();
    }
  }

  private async runCommand(command: () => Promise<ExperimentReadout>): Promise<boolean> {
    this.pending = true;
    this.commandError = '';
    try {
      this.acceptReadout(await command());
      return true;
    } catch (error: unknown) {
      this.commandError = errorMessage(error);
      return false;
    } finally {
      this.pending = false;
      this.changeDetector.markForCheck();
    }
  }

  private acceptReadout(readout: ExperimentReadout): void {
    this.readout = readout;
    const latest = readout.calculations.at(-1);
    if (
      latest &&
      !readout.calculations.some((record) => record.sequence === this.selectedSequence)
    ) {
      this.selectedSequence = latest.sequence;
    }
  }
}

function errorMessage(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    const payload: unknown = error.error;
    if (isErrorPayload(payload)) return payload.error;
    return `Dagger runtime request failed (${error.status})`;
  }
  return error instanceof Error ? error.message : 'Dagger runtime request failed';
}

function isErrorPayload(value: unknown): value is { readonly error: string } {
  return (
    typeof value === 'object' &&
    value !== null &&
    'error' in value &&
    typeof value.error === 'string'
  );
}
