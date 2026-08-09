import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectorRef, Component, OnDestroy, OnInit, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { LabApiService } from './lab-api.service';
import {
  CalculationRecord,
  ExperimentDocument,
  ExperimentReadout,
  cloneExperiment,
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
  readout: ExperimentReadout | undefined;
  error = '';
  pending = false;
  dirty = false;

  ngOnInit(): void {
    void this.refresh(true);
    this.pollTimer = setInterval(() => void this.refresh(false), 250);
  }

  ngOnDestroy(): void {
    if (this.pollTimer !== undefined) {
      clearInterval(this.pollTimer);
    }
  }

  markDirty(): void {
    this.dirty = true;
  }

  async apply(): Promise<void> {
    await this.runCommand(() => this.api.apply(this.draft));
    if (this.error === '') {
      this.dirty = false;
      this.changeDetector.markForCheck();
    }
  }

  async reset(): Promise<void> {
    await this.runCommand(() => this.api.reset());
  }

  latestCalculation(): CalculationRecord | undefined {
    return this.readout?.calculations.at(-1);
  }

  format(value: number): string {
    return value.toFixed(2);
  }

  private async refresh(syncDraft: boolean): Promise<void> {
    if (this.loading || this.pending) {
      return;
    }
    this.loading = true;
    try {
      const readout = await this.api.read();
      this.readout = readout;
      this.error = '';
      if (syncDraft && !this.dirty) {
        this.draft = cloneExperiment(readout.document);
      }
    } catch (error: unknown) {
      this.error = errorMessage(error);
    } finally {
      this.loading = false;
      this.changeDetector.markForCheck();
    }
  }

  private async runCommand(command: () => Promise<ExperimentReadout>): Promise<void> {
    this.pending = true;
    try {
      this.readout = await command();
      this.error = '';
    } catch (error: unknown) {
      this.error = errorMessage(error);
    } finally {
      this.pending = false;
      this.changeDetector.markForCheck();
    }
  }
}

function errorMessage(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    const payload: unknown = error.error;
    if (isErrorPayload(payload)) {
      return payload.error;
    }
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
