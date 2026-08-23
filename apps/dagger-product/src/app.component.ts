import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectorRef, Component, OnDestroy, OnInit, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ProductApiService } from './product-api.service';
import { LabToolsApiService } from './lab-tools-api.service';
import {
  ActorDefinition,
  ContentEntityReadout,
  EquipmentLogRecord,
  InventoryStackReadout,
  InventoryItemReadout,
  InventoryGridOccupant,
  InventoryGridSlotReadout,
  ItemDefinition,
  ProductNoticeRecord,
  ProductReadout,
  LootContainerReadout,
} from './product-contract';
import { DAGGER_APPLICATION_CONTEXT, loadDaggerProductBootstrap } from './product-runtime';
import { SpritesPanelComponent } from './sprites-panel.component';

const PRODUCT_NOTICE_RETENTION_LIMIT = 32;
const PRODUCT_NOTICE_VISIBLE_LIMIT = 7;
const PRODUCT_NOTICE_HOLD_MS = 2_000;
const PRODUCT_NOTICE_BACKLOG_HOLD_MS = 1_000;

@Component({
  selector: 'dagger-root',
  imports: [CommonModule, FormsModule, SpritesPanelComponent],
  templateUrl: './app.component.html',
})
export class AppComponent implements OnInit, OnDestroy {
  private readonly application = inject(DAGGER_APPLICATION_CONTEXT);
  private readonly productApi = inject(ProductApiService);
  private readonly labTools = inject(LabToolsApiService);
  private readonly changeDetector = inject(ChangeDetectorRef);
  private pollTimer: ReturnType<typeof setInterval> | undefined;
  private noticeTimer: ReturnType<typeof setTimeout> | undefined;
  private noticesHydrated = false;
  private noticeHighWater = 0;
  private readonly noticeQueue: ProductNoticeRecord[] = [];
  private loading = false;
  private commandGeneration = 0;
  private lootOpenCancelled = false;
  private lootClosing = false;
  private readonly openLabRequest = (): void => this.openLab();
  private readonly openInventoryRequest = (event: Event): void => {
    if (this.labOpen || this.lootOpen || this.lootOpening || this.readout === undefined) return;
    event.preventDefault();
    if (this.inventoryOpen) {
      this.closeInventory();
      return;
    }
    this.openInventory(false);
  };
  private readonly openCharacterSheetRequest = (event: Event): void => {
    if (this.labOpen || this.lootOpen || this.lootOpening || this.readout === undefined) return;
    event.preventDefault();
    if (this.characterSheetOpen) {
      this.closeCharacterSheet();
      return;
    }
    this.openCharacterSheet(false);
  };
  private readonly openLootRequest = (event: Event): void => {
    if (this.labOpen || this.inventoryOpen || this.characterSheetOpen || this.lootOpen || this.lootOpening || this.readout === undefined) return;
    event.preventDefault();
    void this.openLoot();
  };
  private readonly dismissOverlayRequest = (event: Event): void => {
    if (this.inventoryOpen) {
      event.preventDefault();
      this.closeInventory();
      return;
    }
    if (this.characterSheetOpen) {
      event.preventDefault();
      this.closeCharacterSheet();
      return;
    }
    if (this.lootOpen || this.lootOpening) {
      event.preventDefault();
      void this.closeLoot();
    }
  };

  readout: ProductReadout | undefined;
  connectionError = '';
  sceneError = '';
  commandError = '';
  pending = false;
  contentFilter = '';
  selectedContentId: number | undefined;
  labOpen = false;
  inventoryOpen = false;
  characterSheetOpen = false;
  lootOpen = false;
  lootOpening = false;
  lootFeedback = '';
  selectedInventoryKey: string | undefined;
  draggedInventory:
    | { readonly kind: 'grid'; readonly sourceSlot: number; readonly occupant: InventoryGridOccupant; readonly gridRevision: number }
    | { readonly kind: 'equipped'; readonly item: InventoryItemReadout; readonly slot: string; readonly equipmentRevision: number }
    | undefined;
  activeTab: 'explorer' | 'sprites' = 'explorer';
  grantItemId = 'gold-piece';
  grantQuantity = 25;
  visibleNotices: readonly ProductNoticeRecord[] = [];

  trackContent(_index: number, entity: ContentEntityReadout): number {
    return entity.id;
  }

  trackNotice(_index: number, notice: ProductNoticeRecord): number {
    return notice.sequence;
  }

  ngOnInit(): void {
    window.addEventListener('dagger-open-lab', this.openLabRequest);
    window.addEventListener('dagger-open-inventory', this.openInventoryRequest);
    window.addEventListener('dagger-open-character-sheet', this.openCharacterSheetRequest);
    window.addEventListener('dagger-open-loot', this.openLootRequest);
    window.addEventListener('dagger-dismiss-overlay', this.dismissOverlayRequest);
    void this.refresh();
    this.pollTimer = setInterval(() => void this.refresh(), 250);
  }

  ngOnDestroy(): void {
    window.removeEventListener('dagger-open-lab', this.openLabRequest);
    window.removeEventListener('dagger-open-inventory', this.openInventoryRequest);
    window.removeEventListener('dagger-open-character-sheet', this.openCharacterSheetRequest);
    window.removeEventListener('dagger-open-loot', this.openLootRequest);
    window.removeEventListener('dagger-dismiss-overlay', this.dismissOverlayRequest);
    if (this.pollTimer !== undefined) clearInterval(this.pollTimer);
    if (this.noticeTimer !== undefined) clearTimeout(this.noticeTimer);
  }

  openLab(): void {
    if (this.lootOpen || this.lootOpening) return;
    this.inventoryOpen = false;
    this.characterSheetOpen = false;
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

  openInventory(releaseGameplayInput = true): void {
    if (this.labOpen || this.lootOpen || this.lootOpening || this.readout === undefined) return;
    if (releaseGameplayInput) window.dispatchEvent(new Event('dagger-release-gameplay-input'));
    this.characterSheetOpen = false;
    this.inventoryOpen = true;
    this.application.ui.setInteractionMode('interface');
    this.changeDetector.detectChanges();
    requestAnimationFrame(() => {
      if (!this.inventoryOpen) return;
      document.querySelector<HTMLButtonElement>('[data-testid="inventory-exit"]')?.focus();
    });
  }

  closeInventory(): void {
    if (!this.inventoryOpen) return;
    this.inventoryOpen = false;
    this.application.ui.setInteractionMode('gameplay');
    this.application.ui.focusGameplay();
  }

  openCharacterSheet(releaseGameplayInput = true): void {
    if (this.labOpen || this.lootOpen || this.lootOpening || this.readout === undefined) return;
    if (releaseGameplayInput) window.dispatchEvent(new Event('dagger-release-gameplay-input'));
    this.inventoryOpen = false;
    this.characterSheetOpen = true;
    this.application.ui.setInteractionMode('interface');
    this.changeDetector.detectChanges();
    requestAnimationFrame(() => {
      if (!this.characterSheetOpen) return;
      document.querySelector<HTMLButtonElement>('[data-testid="character-sheet-exit"]')?.focus();
    });
  }

  closeCharacterSheet(): void {
    if (!this.characterSheetOpen) return;
    this.characterSheetOpen = false;
    this.application.ui.setInteractionMode('gameplay');
    this.application.ui.focusGameplay();
  }

  modeledSkillEntries(readout: ProductReadout): readonly { readonly id: string; readonly label: string; readonly value: number }[] {
    return Object.entries(readout.playerStats.modeledSkills).map(([id, value]) => ({
      id,
      label: this.displayStatLabel(id),
      value,
    }));
  }

  displayStatLabel(id: string): string {
    return id.split('-').map((part) => part.charAt(0).toUpperCase() + part.slice(1)).join(' ');
  }

  reflexesLabel(value: number): string {
    return value === 2 ? 'Average' : String(value);
  }

  openedLootContainer(readout: ProductReadout): LootContainerReadout | undefined {
    return readout.lootContainers.find((container) => container.id === readout.openLootContainerId);
  }

  async openLoot(): Promise<void> {
    if (this.labOpen || this.inventoryOpen || this.characterSheetOpen || this.lootOpen || this.lootOpening || this.readout === undefined) return;
    window.dispatchEvent(new Event('dagger-release-gameplay-input'));
    this.lootOpenCancelled = false;
    this.lootOpening = true;
    this.lootFeedback = '';
    this.application.ui.setInteractionMode('interface');
    const succeeded = await this.runCommand(() => this.productApi.openAimedLoot());
    if (this.lootOpenCancelled) {
      const closed = await this.runCommand(() => this.productApi.closeLoot());
      this.lootOpening = false;
      if (!closed && this.readout !== undefined && this.openedLootContainer(this.readout) !== undefined) {
        this.lootOpen = true;
        this.lootFeedback = this.commandError || 'Could not close the loot window.';
        this.changeDetector.detectChanges();
        requestAnimationFrame(() => document.querySelector<HTMLButtonElement>('[data-testid="loot-exit"]')?.focus());
        return;
      }
      this.application.ui.setInteractionMode('gameplay');
      this.application.ui.focusGameplay();
      return;
    }
    this.lootOpening = false;
    const open = succeeded && this.readout !== undefined && this.openedLootContainer(this.readout) !== undefined;
    if (!open) {
      this.lootFeedback = this.latestLootFeedback(this.readout) || this.commandError || 'No eligible loot container is in reach.';
      this.application.ui.setInteractionMode('gameplay');
      this.application.ui.focusGameplay();
      return;
    }
    this.lootOpen = true;
    this.changeDetector.detectChanges();
    requestAnimationFrame(() => document.querySelector<HTMLButtonElement>('[data-testid="loot-exit"]')?.focus());
  }

  async closeLoot(): Promise<void> {
    if (!this.lootOpen && !this.lootOpening) return;
    if (this.lootOpening) {
      this.lootOpenCancelled = true;
      return;
    }
    if (this.lootClosing) return;
    this.lootClosing = true;
    const succeeded = await this.runCommand(() => this.productApi.closeLoot());
    this.lootClosing = false;
    if (!succeeded) {
      this.lootFeedback = this.commandError || 'Could not close the loot window.';
      this.changeDetector.detectChanges();
      requestAnimationFrame(() => document.querySelector<HTMLButtonElement>('[data-testid="loot-exit"]')?.focus());
      return;
    }
    this.lootOpen = false;
    this.lootFeedback = '';
    this.application.ui.setInteractionMode('gameplay');
    this.application.ui.focusGameplay();
  }

  async takeLootStack(container: LootContainerReadout, stack: InventoryStackReadout): Promise<void> {
    if (!this.lootOpen) return;
    const succeeded = await this.runCommand(() =>
      this.productApi.transferLootStack(container.id, container.sourceInventoryRevision, stack.item),
    );
    if (succeeded) this.lootFeedback = this.latestLootFeedback(this.readout) ?? '';
    this.focusLootAction(stack.item);
  }

  async takeLootItem(container: LootContainerReadout, item: InventoryItemReadout): Promise<void> {
    if (!this.lootOpen) return;
    const succeeded = await this.runCommand(() =>
      this.productApi.transferLootItem(container.id, container.sourceInventoryRevision, item.entity),
    );
    if (succeeded) this.lootFeedback = this.latestLootFeedback(this.readout) ?? '';
    this.focusLootAction(String(item.entity));
  }

  private latestLootFeedback(readout: ProductReadout | undefined): string | undefined {
    const receipt = readout?.equipmentLog.at(-1);
    if (receipt === undefined || !receipt.operation.startsWith('loot')) return undefined;
    return receipt.reason ?? (receipt.accepted ? `${receipt.operation} accepted` : 'Loot transfer rejected');
  }

  private focusLootAction(key: string): void {
    requestAnimationFrame(() => {
      if (!this.lootOpen) return;
      document.querySelector<HTMLButtonElement>(`[data-testid="loot-take-${key}"]`)?.focus()
        ?? document.querySelector<HTMLButtonElement>('[data-testid="loot-exit"]')?.focus();
    });
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
    await this.runCommand(() => this.productApi.reset());
  }

  async resetAndPlay(): Promise<void> {
    if (await this.runCommand(() => this.productApi.reset())) this.returnToPlay();
  }

  format(value: number): string {
    return value.toFixed(2);
  }

  /** Clamp untrusted display values into a safe, normalized native HUD fill. */
  hudMeter(current: number, maximum: number): { readonly current: number; readonly maximum: number; readonly percent: number } {
    const safeMaximum = Number.isFinite(maximum) && maximum > 0 ? maximum : 0;
    const safeCurrent = Number.isFinite(current) && safeMaximum > 0
      ? Math.min(safeMaximum, Math.max(0, current))
      : 0;
    return {
      current: safeCurrent,
      maximum: safeMaximum,
      percent: safeMaximum === 0 ? 0 : (safeCurrent / safeMaximum) * 100,
    };
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
    const mobileId = this.selectedContent()?.reference?.mobileId;
    if (mobileId === undefined) return undefined;
    return this.readout?.gameplayPackage.actors.find((actor) => actor.mobileId === mobileId);
  }

  playerDefinition(): ActorDefinition | undefined {
    return this.readout?.gameplayPackage.actors.find((actor) => actor.kind === 'player');
  }

  equippedItems(readout: ProductReadout): readonly InventoryItemReadout[] {
    return readout.playerInventory.items.filter((item) => item.equipSlot !== null);
  }

  /** Enemy content count for the badge; treasure containers are separate content. */
  enemyCount(readout: ProductReadout): number {
    return readout.content.filter((entity) => entity.kind === 'enemy').length;
  }

  carriedItems(readout: ProductReadout): readonly InventoryItemReadout[] {
    return readout.playerInventory.items.filter((item) => item.equipSlot === null);
  }

  equipmentSlots(readout: ProductReadout): readonly { readonly id: string; readonly item: InventoryItemReadout | undefined }[] {
    const ids = ['head', 'right-arm', 'chest-armor', 'left-arm', 'right-hand', 'gloves', 'left-hand', 'legs-armor', 'feet'];
    return ids.map((id) => ({ id, item: readout.playerInventory.items.find((item) => item.equipSlot === id) }));
  }

  trackEquipmentSlot(_index: number, slot: { readonly id: string }): string {
    return slot.id;
  }

  trackLootStack(_index: number, stack: InventoryStackReadout): string {
    return stack.item;
  }

  trackLootItem(_index: number, item: InventoryItemReadout): number {
    return item.entity;
  }

  trackInventoryStack(_index: number, stack: InventoryStackReadout): string {
    return stack.item;
  }

  trackInventoryItem(_index: number, item: InventoryItemReadout): number {
    return item.entity;
  }

  selectInventoryItem(item: InventoryItemReadout): void {
    this.selectedInventoryKey = `item:${item.entity}`;
  }

  selectInventoryStack(stack: InventoryStackReadout): void {
    this.selectedInventoryKey = `stack:${stack.item}`;
  }

  selectedInventoryItem(readout: ProductReadout): InventoryItemReadout | undefined {
    const entity = this.selectedInventoryKey?.startsWith('item:')
      ? Number(this.selectedInventoryKey.slice('item:'.length))
      : undefined;
    return readout.playerInventory.items.find((item) => item.entity === entity);
  }

  selectedInventoryStack(readout: ProductReadout): InventoryStackReadout | undefined {
    const itemId = this.selectedInventoryKey?.startsWith('stack:')
      ? this.selectedInventoryKey.slice('stack:'.length)
      : undefined;
    return readout.playerInventory.stacks.find((stack) => stack.item === itemId);
  }

  selectedInventoryDefinition(readout: ProductReadout): ItemDefinition | undefined {
    const itemId = this.selectedInventoryItem(readout)?.item ?? this.selectedInventoryStack(readout)?.item;
    return readout.gameplayPackage.items.find((item) => item.id === itemId);
  }

  selectedInventoryQuantity(readout: ProductReadout): number {
    return this.selectedInventoryStack(readout)?.quantity ?? 1;
  }

  isInventoryEquippable(readout: ProductReadout, item: InventoryItemReadout): boolean {
    return item.compatibleSlots.length > 0;
  }

  selectedCarriedItem(readout: ProductReadout): InventoryItemReadout | undefined {
    const selected = this.selectedInventoryItem(readout);
    return selected?.equipSlot === null ? selected : undefined;
  }

  canEquipInSlot(readout: ProductReadout, item: InventoryItemReadout | undefined, slot: string): boolean {
    return item !== undefined && item.equipSlot === null && item.compatibleSlots.includes(slot);
  }

  gridItem(readout: ProductReadout, slot: InventoryGridSlotReadout): InventoryItemReadout | undefined {
    const occupant = slot.occupant;
    return occupant?.kind === 'item'
      ? readout.playerInventory.items.find((item) => item.entity === occupant.entity)
      : undefined;
  }

  gridStack(readout: ProductReadout, slot: InventoryGridSlotReadout): InventoryStackReadout | undefined {
    const occupant = slot.occupant;
    return occupant?.kind === 'stack'
      ? readout.playerInventory.stacks.find((stack) => stack.item === occupant.item)
      : undefined;
  }

  inventoryIcon(itemId: string): string {
    return `/api/dagger-product/ui/assets/inventory.icon.${itemId}`;
  }

  startGridDrag(event: DragEvent, slot: InventoryGridSlotReadout, readout: ProductReadout): void {
    if (slot.occupant === null) return;
    this.draggedInventory = {
      kind: 'grid', sourceSlot: slot.index, occupant: slot.occupant, gridRevision: readout.inventoryGrid.revision,
    };
    event.dataTransfer?.setData('text/plain', `inventory-grid:${slot.index}`);
    if (event.dataTransfer) event.dataTransfer.effectAllowed = 'move';
  }

  startEquippedDrag(event: DragEvent, item: InventoryItemReadout, readout: ProductReadout): void {
    if (item.equipSlot === null) return;
    this.draggedInventory = { kind: 'equipped', item, slot: item.equipSlot, equipmentRevision: readout.playerInventory.equipmentRevision };
    event.dataTransfer?.setData('text/plain', `equipped:${item.entity}`);
    if (event.dataTransfer) event.dataTransfer.effectAllowed = 'move';
  }

  endInventoryDrag(): void {
    this.draggedInventory = undefined;
  }

  allowEquipDrop(event: DragEvent, slot: string): void {
    const item = this.draggedInventory?.kind === 'grid' && this.readout !== undefined
      ? this.gridItem(this.readout, { index: this.draggedInventory.sourceSlot, occupant: this.draggedInventory.occupant })
      : undefined;
    if (item?.compatibleSlots.includes(slot) && !this.pending) {
      event.preventDefault();
      if (event.dataTransfer) event.dataTransfer.dropEffect = 'move';
    }
  }

  dropOnEquipmentSlot(event: DragEvent, slot: string): void {
    event.preventDefault();
    const dragged = this.draggedInventory;
    this.endInventoryDrag();
    if (dragged?.kind === 'grid' && this.readout !== undefined) {
      const item = this.gridItem(this.readout, { index: dragged.sourceSlot, occupant: dragged.occupant });
      if (item !== undefined) void this.equipIntoSlot(item, slot, this.readout.playerInventory.equipmentRevision);
    }
  }

  allowGridDrop(event: DragEvent): void {
    if (this.draggedInventory?.kind === 'grid' && !this.pending) {
      event.preventDefault();
      if (event.dataTransfer) event.dataTransfer.dropEffect = 'move';
    }
  }

  dropOnGrid(event: DragEvent): void {
    event.preventDefault();
    const dragged = this.draggedInventory;
    this.endInventoryDrag();
    if (dragged?.kind !== 'grid') return;
    const grid = event.currentTarget as HTMLElement;
    const slots = Array.from(grid.querySelectorAll<HTMLElement>('.inventory-grid-slot'));
    const directTarget = (event.target as HTMLElement | null)?.closest<HTMLElement>('.inventory-grid-slot');
    const target = directTarget !== undefined && directTarget !== null && grid.contains(directTarget) ? directTarget : slots.reduce<HTMLElement | undefined>((closest, candidate) => {
      const rect = candidate.getBoundingClientRect();
      const distance = (rect.left + rect.width / 2 - event.clientX) ** 2
        + (rect.top + rect.height / 2 - event.clientY) ** 2;
      if (closest === undefined) return candidate;
      const closestRect = closest.getBoundingClientRect();
      const closestDistance = (closestRect.left + closestRect.width / 2 - event.clientX) ** 2
        + (closestRect.top + closestRect.height / 2 - event.clientY) ** 2;
      return distance < closestDistance ? candidate : closest;
    }, undefined);
    const targetSlot = Number(target?.dataset['slotIndex']);
    if (Number.isInteger(targetSlot) && targetSlot !== dragged.sourceSlot) {
      void this.moveInventoryGrid(dragged.sourceSlot, targetSlot, dragged.gridRevision);
    }
  }

  dropOnGridSlot(event: DragEvent, targetSlot: number): void {
    event.preventDefault();
    event.stopPropagation();
    const dragged = this.draggedInventory;
    this.endInventoryDrag();
    if (dragged?.kind === 'grid' && targetSlot !== dragged.sourceSlot) {
      void this.moveInventoryGrid(dragged.sourceSlot, targetSlot, dragged.gridRevision);
    }
  }

  allowUnequipDrop(event: DragEvent): void {
    if (this.draggedInventory?.kind === 'equipped' && !this.pending) {
      event.preventDefault();
      if (event.dataTransfer) event.dataTransfer.dropEffect = 'move';
    }
  }

  dropIntoCarried(event: DragEvent): void {
    event.preventDefault();
    const dragged = this.draggedInventory;
    this.endInventoryDrag();
    if (dragged?.kind === 'equipped') void this.unequipFromSlot(dragged.item, dragged.slot, dragged.equipmentRevision);
  }

  latestEquipmentReceipt(readout: ProductReadout): EquipmentLogRecord | undefined {
    return [...readout.equipmentLog].reverse().find((receipt) =>
      receipt.operation === 'equip' || receipt.operation === 'swap' || receipt.operation === 'unequip',
    );
  }

  filteredContent(): readonly ContentEntityReadout[] {
    const filter = this.contentFilter.trim().toLowerCase();
    // The content browser lists enemies; treasure containers live in the
    // loot panel below.
    const content = (this.readout?.content ?? []).filter((entity) => entity.kind === 'enemy');
    if (filter === '') return content;
    return content.filter((entity) =>
      [entity.name, entity.reference?.mobileName ?? '', String(entity.reference?.mobileId ?? '')]
        .some((value) => value.toLowerCase().includes(filter)),
    );
  }

  /** Unsupported loot categories that still rolled a success (skipped coverage). */
  unsupportedLootNotes(container: LootContainerReadout): readonly string[] {
    return container.generation.categories
      .filter((category) => !category.supported && category.rolls.some((roll) => roll.success))
      .map((category) => category.category);
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
    const succeeded = await this.runCommand(() => this.labTools.jumpToContent(selected.id));
    if (succeeded) {
      this.selectedContentId = selected.id;
      this.returnToPlay();
    }
  }

  async equipItem(item: InventoryItemReadout): Promise<void> {
    const slot = item.compatibleSlots.at(0);
    if (slot !== undefined && this.readout !== undefined) await this.equipIntoSlot(item, slot, this.readout.playerInventory.equipmentRevision);
  }

  async unequipItem(item: InventoryItemReadout): Promise<void> {
    if (item.equipSlot !== null && this.readout !== undefined) await this.unequipFromSlot(item, item.equipSlot, this.readout.playerInventory.equipmentRevision);
  }

  async equipIntoSlot(item: InventoryItemReadout, slot: string, expectedEquipmentRevision: number): Promise<void> {
    if (this.pending || !item.compatibleSlots.includes(slot)) return;
    await this.runCommand(() => this.productApi.equipItem(item.entity, slot, expectedEquipmentRevision));
  }

  async unequipFromSlot(item: InventoryItemReadout, slot: string, expectedEquipmentRevision: number): Promise<void> {
    if (this.pending) return;
    await this.runCommand(() => this.productApi.unequipSlot(slot, item.entity, expectedEquipmentRevision));
  }

  async moveInventoryGrid(sourceSlot: number, targetSlot: number, expectedRevision: number): Promise<void> {
    if (this.pending || sourceSlot === targetSlot) return;
    await this.runCommand(() => this.productApi.moveInventoryGrid(sourceSlot, targetSlot, expectedRevision));
  }

  async grantItem(): Promise<void> {
    await this.runCommand(() =>
      this.labTools.grantItem(this.grantItemId, Math.trunc(this.grantQuantity)),
    );
  }

  /** Fungible (stackable) item definitions from the committed package. */
  fungibleItems(): readonly { readonly id: string }[] {
    return (this.readout?.gameplayPackage.items ?? []).filter(
      (item) => !item.weapon && !item.armor && !item.shield,
    );
  }

  private async refresh(): Promise<void> {
    if (this.loading || this.pending) return;
    this.loading = true;
    const commandGeneration = this.commandGeneration;
    try {
      const readout = await this.productApi.read();
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

  private async runCommand(command: () => Promise<ProductReadout>): Promise<boolean> {
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

  private acceptReadout(readout: ProductReadout): void {
    this.readout = readout;
    this.acceptNotices(readout.notices);
    if (this.lootOpen && !this.lootClosing && readout.openLootContainerId === null) {
      this.lootOpen = false;
      this.lootOpening = false;
      this.lootFeedback = this.latestLootFeedback(readout) ?? 'Loot window closed because its source changed.';
      this.application.ui.setInteractionMode('gameplay');
      this.application.ui.focusGameplay();
    }
    if (this.selectedContentId === undefined) {
      this.selectedContentId = readout.focusedContentId ?? readout.content.at(0)?.id;
    }
    const selectionStillExists = this.selectedInventoryKey?.startsWith('item:')
      ? this.selectedInventoryItem(readout) !== undefined
      : this.selectedInventoryKey?.startsWith('stack:')
        ? this.selectedInventoryStack(readout) !== undefined
        : false;
    if (!selectionStillExists) {
      const item = this.carriedItems(readout).at(0) ?? this.equippedItems(readout).at(0);
      const stack = readout.playerInventory.stacks.at(0);
      this.selectedInventoryKey = item === undefined ? stack && `stack:${stack.item}` : `item:${item.entity}`;
    }
  }

  private acceptNotices(notices: readonly ProductNoticeRecord[]): void {
    if (!this.noticesHydrated) {
      this.noticesHydrated = true;
      this.noticeHighWater = notices.reduce((highWater, notice) => Math.max(highWater, notice.sequence), 0);
      return;
    }
    if (notices.length === 0) {
      this.clearNoticePresentation();
      return;
    }
    for (const notice of notices) {
      if (notice.sequence <= this.noticeHighWater) continue;
      this.noticeHighWater = notice.sequence;
      this.noticeQueue.push(notice);
    }
    while (this.noticeQueue.length + this.visibleNotices.length > PRODUCT_NOTICE_RETENTION_LIMIT) {
      this.noticeQueue.shift();
    }
    this.showQueuedNotices();
  }

  private showQueuedNotices(): void {
    const visible = [...this.visibleNotices];
    while (visible.length < PRODUCT_NOTICE_VISIBLE_LIMIT && this.noticeQueue.length > 0) {
      const notice = this.noticeQueue.shift();
      if (notice !== undefined) visible.push(notice);
    }
    this.visibleNotices = visible;
    if (this.noticeTimer === undefined && this.visibleNotices.length > 0) {
      this.noticeTimer = setTimeout(() => {
        this.noticeTimer = undefined;
        this.visibleNotices = this.visibleNotices.slice(1);
        this.showQueuedNotices();
        this.changeDetector.markForCheck();
      }, this.noticeQueue.length > 0 ? PRODUCT_NOTICE_BACKLOG_HOLD_MS : PRODUCT_NOTICE_HOLD_MS);
    }
  }

  private clearNoticePresentation(): void {
    this.noticeQueue.length = 0;
    this.visibleNotices = [];
    if (this.noticeTimer !== undefined) {
      clearTimeout(this.noticeTimer);
      this.noticeTimer = undefined;
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
