import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectorRef, Component, OnDestroy, OnInit, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { LabApiService } from './lab-api.service';
import {
  ExperimentProfile,
  ProfileStoreService,
  documentsEqual,
} from './profile-store.service';
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
  private readonly profileStore = inject(ProfileStoreService);
  private readonly changeDetector = inject(ChangeDetectorRef);
  private pollTimer: ReturnType<typeof setInterval> | undefined;
  private loading = false;
  private profilesInitialized = false;

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
  profiles: ExperimentProfile[] = [];
  selectedProfileId: string | undefined;
  activeProfileId: string | undefined;
  profileName = '';
  profileError = '';

  ngOnInit(): void {
    this.profiles = this.profileStore.load();
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
    if (succeeded) {
      this.dirty = false;
      this.activeProfileId = this.profileForDocument(this.readout?.document)?.id;
    }
  }

  async reset(): Promise<void> {
    await this.runCommand(() => this.api.reset());
  }

  async resetAndPlay(): Promise<void> {
    await this.runCommand(() => this.api.play());
  }

  selectedProfile(): ExperimentProfile | undefined {
    return this.profiles.find((profile) => profile.id === this.selectedProfileId);
  }

  activeProfile(): ExperimentProfile | undefined {
    return this.profiles.find((profile) => profile.id === this.activeProfileId);
  }

  selectProfile(profile: ExperimentProfile): void {
    this.selectedProfileId = profile.id;
    this.draft = cloneExperiment(profile.document);
    this.dirty = false;
    this.profileName = profile.name;
    this.profileError = '';
    this.commandError = '';
  }

  saveAsProfile(): void {
    const name = this.validProfileName();
    if (name === undefined) return;
    const profile = this.profileStore.create(name, documentFromDraft(this.draft));
    this.profiles = [...this.profiles, profile];
    this.profileStore.persist(this.profiles);
    this.selectedProfileId = profile.id;
    this.profileName = profile.name;
    this.profileError = '';
  }

  saveSelectedProfile(): void {
    const selected = this.selectedProfile();
    if (selected === undefined) {
      this.profileError = 'Select a profile before saving changes.';
      return;
    }
    const updated: ExperimentProfile = {
      ...selected,
      document: documentFromDraft(this.draft),
    };
    this.profiles = this.profiles.map((profile) =>
      profile.id === selected.id ? updated : profile,
    );
    this.profileStore.persist(this.profiles);
    if (
      this.activeProfileId === selected.id &&
      this.readout &&
      !documentsEqual(updated.document, this.readout.document)
    ) {
      this.activeProfileId = undefined;
    }
    this.profileError = '';
  }

  duplicateSelectedProfile(): void {
    const selected = this.selectedProfile();
    if (selected === undefined) {
      this.profileError = 'Select a profile before duplicating it.';
      return;
    }
    const duplicate = this.profileStore.create(
      this.uniqueProfileName(`${selected.name} copy`),
      selected.document,
    );
    this.profiles = [...this.profiles, duplicate];
    this.profileStore.persist(this.profiles);
    this.selectProfile(duplicate);
  }

  renameSelectedProfile(): void {
    const selected = this.selectedProfile();
    if (selected === undefined) {
      this.profileError = 'Select a profile before renaming it.';
      return;
    }
    const name = this.validProfileName(selected.id);
    if (name === undefined) return;
    this.profiles = this.profiles.map((profile) =>
      profile.id === selected.id ? { ...profile, name } : profile,
    );
    this.profileStore.persist(this.profiles);
    this.profileName = name;
    this.profileError = '';
  }

  async activateSelectedProfile(): Promise<void> {
    const selected = this.selectedProfile();
    if (selected === undefined) {
      this.profileError = 'Select a profile before activating it.';
      return;
    }
    this.profileError = '';
    const succeeded = await this.runCommand(() => this.api.apply(selected.document));
    if (succeeded && this.readout) {
      const admitted: ExperimentProfile = {
        ...selected,
        document: cloneExperiment(this.readout.document),
      };
      this.profiles = this.profiles.map((profile) =>
        profile.id === selected.id ? admitted : profile,
      );
      this.profileStore.persist(this.profiles);
      this.activeProfileId = selected.id;
      this.draft = cloneExperiment(this.readout.document);
      this.dirty = false;
    }
  }

  deleteSelectedProfile(): void {
    const selected = this.selectedProfile();
    if (selected === undefined) {
      this.profileError = 'Select a profile before deleting it.';
      return;
    }
    if (!globalThis.confirm(`Delete profile “${selected.name}”?`)) return;
    this.profiles = this.profiles.filter((profile) => profile.id !== selected.id);
    this.profileStore.persist(this.profiles);
    if (this.activeProfileId === selected.id) this.activeProfileId = undefined;
    const next = this.profiles.at(0);
    if (next) {
      this.selectProfile(next);
    } else {
      this.selectedProfileId = undefined;
      this.profileName = '';
    }
    this.profileError = '';
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
      this.initializeProfiles(readout);
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
    const active = this.activeProfile();
    if (active && !documentsEqual(active.document, readout.document)) {
      this.activeProfileId = undefined;
    }
    const latest = readout.calculations.at(-1);
    if (
      latest &&
      !readout.calculations.some((record) => record.sequence === this.selectedSequence)
    ) {
      this.selectedSequence = latest.sequence;
    }
  }

  private initializeProfiles(readout: ExperimentReadout): void {
    if (this.profilesInitialized) return;
    if (this.profiles.length === 0) {
      const starter = this.profileStore.create("Privateer's Hold starter", readout.document);
      this.profiles = [starter];
      this.profileStore.persist(this.profiles);
    }
    const match = this.profileForDocument(readout.document);
    this.activeProfileId = match?.id;
    this.selectedProfileId = match?.id ?? this.profiles.at(0)?.id;
    this.profileName = this.selectedProfile()?.name ?? '';
    this.profilesInitialized = true;
  }

  private profileForDocument(
    document: ExperimentDocument | undefined,
  ): ExperimentProfile | undefined {
    if (document === undefined) return undefined;
    const selected = this.selectedProfile();
    if (selected && documentsEqual(selected.document, document)) return selected;
    return this.profiles.find((profile) => documentsEqual(profile.document, document));
  }

  private validProfileName(exceptId?: string): string | undefined {
    const name = this.profileName.trim();
    if (name.length === 0 || name.length > 64) {
      this.profileError = 'Profile names must contain 1 to 64 characters.';
      return undefined;
    }
    if (
      this.profiles.some(
        (profile) => profile.id !== exceptId && profile.name.toLowerCase() === name.toLowerCase(),
      )
    ) {
      this.profileError = `A profile named “${name}” already exists.`;
      return undefined;
    }
    return name;
  }

  private uniqueProfileName(base: string): string {
    if (!this.profiles.some((profile) => profile.name.toLowerCase() === base.toLowerCase())) {
      return base;
    }
    let suffix = 2;
    while (
      this.profiles.some(
        (profile) => profile.name.toLowerCase() === `${base} ${suffix}`.toLowerCase(),
      )
    ) {
      suffix += 1;
    }
    return `${base} ${suffix}`;
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
