import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectorRef, Component, OnDestroy, OnInit, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { LabApiService } from './lab-api.service';
import {
  ActorDefinition,
  ContentEntityReadout,
  InventoryItemReadout,
  LabReadout,
} from './lab-contract';
import { DAGGER_APPLICATION_CONTEXT, loadDaggerProductBootstrap } from './product-runtime';
import { SpritesPanelComponent } from './sprites-panel.component';

@Component({
  selector: 'dagger-root',
  imports: [CommonModule, FormsModule, SpritesPanelComponent],
  templateUrl: './app.component.html',
})
export class AppComponent implements OnInit, OnDestroy {
  private readonly application = inject(DAGGER_APPLICATION_CONTEXT);
  private readonly api = inject(LabApiService);
  private readonly changeDetector = inject(ChangeDetectorRef);
  private pollTimer: ReturnType<typeof setInterval> | undefined;
  private loading = false;
  private commandGeneration = 0;
  private readonly openLabRequest = (): void => this.openLab();

  readout: LabReadout | undefined;
  connectionError = '';
  sceneError = '';
  commandError = '';
  pending = false;
  contentFilter = '';
  selectedContentId: number | undefined;
  labOpen = false;
  activeTab: 'explorer' | 'sprites' = 'explorer';

  trackContent(_index: number, entity: ContentEntityReadout): number {
    return entity.id;
  }

  ngOnInit(): void {
    window.addEventListener('dagger-open-lab', this.openLabRequest);
    void this.refresh();
    this.pollTimer = setInterval(() => void this.refresh(), 250);
  }

  ngOnDestroy(): void {
    window.removeEventListener('dagger-open-lab', this.openLabRequest);
    if (this.pollTimer !== undefined) clearInterval(this.pollTimer);
  }

  openLab(): void {
    this.labOpen = true;
    this.application.ui.setInteractionMode('interface');
    requestAnimationFrame(() => {
      const scroller = document.querySelector<HTMLElement>('[data-testid="lab-scroll"]');
      if (scroller !== null) scroller.scrollTop = 0;
    });
  }

  returnToPlay(): void {
    this.labOpen = false;
    this.application.ui.setInteractionMode('gameplay');
    this.application.ui.focusGameplay();
  }

  async refreshScene(): Promise<void> {
    this.sceneError = '';
    try {
      const bootstrap = await loadDaggerProductBootstrap();
      const receipt = await this.application.renderer.replaceContent(bootstrap.content);
      if (!receipt.applied) {
        throw new Error(receipt.diagnostics.map((diagnostic) => diagnostic.message).join('; '));
      }
      this.application.renderer.setCameraPose(bootstrap.camera);
      this.application.renderer.renderOnce();
    } catch (error: unknown) {
      this.sceneError = errorMessage(error);
    } finally {
      this.changeDetector.markForCheck();
    }
  }

  async reset(): Promise<void> {
    await this.runCommand(() => this.api.reset());
  }

  async resetAndPlay(): Promise<void> {
    if (await this.runCommand(() => this.api.play())) this.returnToPlay();
  }

  format(value: number): string {
    return value.toFixed(2);
  }

  optional(value: number | undefined): string {
    return value === undefined ? '—' : this.format(value);
  }

  statEntries(definition: ActorDefinition): readonly { key: string; value: number }[] {
    return Object.entries(definition.stats).map(([key, value]) => ({ key, value }));
  }

  skillEntries(definition: ActorDefinition): readonly { key: string; value: number }[] {
    return Object.entries(definition.skills).map(([key, value]) => ({ key, value }));
  }

  actorForSelectedContent(): ActorDefinition | undefined {
    const mobileId = this.selectedContent()?.reference.mobileId;
    if (mobileId === undefined) return undefined;
    return this.readout?.gameplayPackage.actors.find((actor) => actor.mobileId === mobileId);
  }

  playerDefinition(): ActorDefinition | undefined {
    return this.readout?.gameplayPackage.actors.find((actor) => actor.kind === 'player');
  }

  equippedItems(readout: LabReadout): readonly InventoryItemReadout[] {
    return readout.playerInventory.items.filter((item) => item.equipSlot !== null);
  }

  carriedItems(readout: LabReadout): readonly InventoryItemReadout[] {
    return readout.playerInventory.items.filter((item) => item.equipSlot === null);
  }

  filteredContent(): readonly ContentEntityReadout[] {
    const filter = this.contentFilter.trim().toLowerCase();
    const content = this.readout?.content ?? [];
    if (filter === '') return content;
    return content.filter((entity) =>
      [entity.name, entity.reference.mobileName, String(entity.reference.mobileId)]
        .some((value) => value.toLowerCase().includes(filter)),
    );
  }

  selectedContent(): ContentEntityReadout | undefined {
    const content = this.readout?.content ?? [];
    return (
      content.find((entity) => entity.id === this.selectedContentId) ??
      content.find((entity) => entity.id === this.readout?.focusedContentId) ??
      content.at(0)
    );
  }

  selectContent(entity: ContentEntityReadout): void {
    this.selectedContentId = entity.id;
    this.commandError = '';
  }

  async jumpToSelectedContent(): Promise<void> {
    const selected = this.selectedContent();
    if (selected === undefined) return;
    const succeeded = await this.runCommand(() => this.api.jumpToContent(selected.id));
    if (succeeded) {
      this.selectedContentId = selected.id;
      this.returnToPlay();
    }
  }

  private async refresh(): Promise<void> {
    if (this.loading || this.pending) return;
    this.loading = true;
    const commandGeneration = this.commandGeneration;
    try {
      const readout = await this.api.read();
      if (commandGeneration !== this.commandGeneration) return;
      this.acceptReadout(readout);
      this.connectionError = '';
    } catch (error: unknown) {
      if (commandGeneration !== this.commandGeneration) return;
      this.connectionError = errorMessage(error);
    } finally {
      this.loading = false;
      this.changeDetector.markForCheck();
    }
  }

  private async runCommand(command: () => Promise<LabReadout>): Promise<boolean> {
    this.commandGeneration += 1;
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

  private acceptReadout(readout: LabReadout): void {
    this.readout = readout;
    if (this.selectedContentId === undefined) {
      this.selectedContentId = readout.focusedContentId ?? readout.content.at(0)?.id;
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
