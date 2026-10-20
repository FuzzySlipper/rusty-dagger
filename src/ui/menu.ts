import { mountLiveDebugPanel, type LiveDebugPanelMount } from '@rusty-engine/live-debug';
import { mountCharacter, isCharacterProjection } from './character.js';
import type { ConfirmQuestion } from './confirm.js';
import { mountControls } from './controls.js';
import { button, type Send } from './host.js';
import { mountInventory } from './inventory.js';
import { mountQuestJournal } from './journal.js';
import { mountLodging, isLodgingProjection } from './lodging.js';
import { mountLoot, type LootProjection } from './loot.js';
import { mountMap, isMapProjection } from './maps.js';
import { mountNotebook } from './notebook.js';
import { diagnosticRows, formatRestHours, isRestProjection, isSaveSlots, type DaggerHud, type SaveSlotProjection, type SpellbookProjection } from './projection.js';
import { mountProperty } from './property.js';
import { redraw } from './redraw.js';
import type { NavigationSurface } from './navigation.js';
import { renderSpells, spellAction } from './spells.js';
import { isTransportProjection, mountTransport } from './transport.js';
import { mountTravel, isTravelProjection } from './travel.js';

type Panel = 'spells' | 'magic-items' | 'diagnostics' | 'inventory' | 'character' | 'map' | 'transport' | 'rest' | 'journal' | 'loot' | 'debug' | 'save-slots' | 'settings';

/** The panels the shoulder buttons step through, in the home list's order. */
const STEPPED_PANELS = ['inventory', 'character', 'spells', 'magic-items', 'map', 'transport', 'rest', 'journal'] as const;

/**
 * The game menu: a home list of actions and the panels it opens (inventory, character, spells,
 * magic items, map, travel, rest, journal, loot, saves, settings), plus the diagnostic panels kept
 * at its end. An open menu holds the world, so the product is told whenever it opens or closes.
 */
export interface GameMenu {
  readonly dialog: HTMLDialogElement;
  /** Character creation, which the title screen borrows while it is open. */
  readonly creation: HTMLElement;
  open(): void;
  close(): void;
  /** Back: step out of a panel, or close the menu from its home. */
  dismiss(): void;
  /**
   * Escape or Back: a held item is released first. Then Escape closes the menu from any panel and
   * Back steps out one panel.
   */
  back(source: 'escape' | 'controller'): void;
  runAction(action: string | undefined): void;
  /** The controller's X: a context menu on the focused control, where the panel offers one. */
  contextMenu(): void;
  stepPanel(direction: -1 | 1): void;
  /** Key capture for rebinding, which runs before navigation takes Escape and the arrows. */
  captureKey(event: KeyboardEvent): void;
  update(value: DaggerHud, dead: boolean, spellbook: SpellbookProjection | null, textDiagnostics: readonly string[]): void;
  refreshArt(): void;
  dispose(): void;
}

export interface MenuHost {
  readonly send: Send;
  readonly ask: (question: ConfirmQuestion) => void;
  /** The menu opened or closed; `focus` asks for gameplay focus back. */
  readonly sync: (focus?: boolean) => void;
  readonly isDead: () => boolean;
  /** Registers a surface a panel opens over the menu, such as a context menu. */
  readonly register: (surface: NavigationSurface) => () => void;
  /** The interface scale in use, which Settings shows beside the player's choice. */
  readonly uiScale: () => number;
}

export function mountMenu(shell: HTMLElement, host: MenuHost): GameMenu {
  const { send, ask } = host;
  const menu = shell.querySelector<HTMLDialogElement>('dialog.dagger-menu')!;
  const menuTitle = shell.querySelector<HTMLElement>('#dagger-menu-title')!;
  const home = shell.querySelector<HTMLElement>('.dagger-menu-home')!;
  const panel = shell.querySelector<HTMLElement>('.dagger-menu-panel')!;
  const diagnostics = shell.querySelector<HTMLElement>('.dagger-composition')!;
  const composition = shell.querySelector<HTMLDListElement>('.dagger-composition dl')!;
  // Unresolved text symbols are developer diagnostics: they are listed in the composition
  // diagnostics panel, never in the conversation, quest message or summoning text the player reads.
  const textDiagnostics = shell.querySelector<HTMLElement>('.dagger-text-diagnostics')!;
  const menuToggle = shell.querySelector<HTMLButtonElement>('.dagger-menu-toggle')!;
  const activationMode = shell.querySelector<HTMLSelectElement>('.dagger-activation-mode')!;
  activationMode.addEventListener('change', () => send({ action: 'activation-mode', mode: activationMode.value }));

  const inventoryRoot = shell.querySelector<HTMLElement>('.dagger-inventory-root')!;
  const inventoryView = mountInventory(inventoryRoot, send, host.register);
  const mapRoot = shell.querySelector<HTMLElement>('.dagger-map-root')!;
  const mapView = mountMap(mapRoot, send, () => showPanel('transport'));
  let mapReported = false;
  const reportMap = (open: boolean): void => {
    if (mapReported === open) return;
    mapReported = open;
    send({ action: 'map-open', open });
  };
  const transportRoot = shell.querySelector<HTMLElement>('.dagger-transport-root')!;
  const transportView = mountTransport(transportRoot, send);
  const propertyRoot = document.createElement('section');
  propertyRoot.className = 'dagger-property-root';
  inventoryRoot.append(propertyRoot);
  const propertyView = mountProperty(propertyRoot, send);
  const travelView = mountTravel(transportRoot, send);
  const restRoot = shell.querySelector<HTMLElement>('.dagger-rest-root')!;
  const lodgingView = mountLodging(restRoot, send);
  const restStatus = shell.querySelector<HTMLElement>('.dagger-rest-status')!;
  const restHours = shell.querySelector<HTMLInputElement>('.dagger-rest-hours')!;
  const loiterHours = shell.querySelector<HTMLInputElement>('.dagger-loiter-hours')!;
  // Each duration box holds only what its mode accepts: a typed value outside the ruleset's
  // range is brought back into it when the box is left.
  for (const box of [restHours, loiterHours]) box.addEventListener('change', () => {
    const typed = Number(box.value);
    if (box.value.trim().length !== 0 && Number.isFinite(typed)) box.value = String(Math.min(Number(box.max), Math.max(Number(box.min), Math.round(typed))));
  });
  const submitRest = (mode: string): void => {
    if (mode === 'until-healed') { send({ action: 'rest', mode }); return; }
    const box = mode === 'loiter' ? loiterHours : restHours;
    const rawHours = box.value.trim();
    const hours = Number(rawHours);
    if (rawHours.length === 0 || !Number.isSafeInteger(hours) || hours < Number(box.min) || hours > Number(box.max)) {
      restStatus.textContent = `Enter a whole number of hours from ${box.min} to ${box.max}.`;
      box.focus();
      return;
    }
    send({ action: 'rest', mode, hours });
  };
  const magicItemsRoot = document.createElement('section'); magicItemsRoot.className = 'dagger-magic-items'; magicItemsRoot.hidden = true;
  shell.querySelector('.dagger-controls-root')!.before(magicItemsRoot);
  const controlsRoot = shell.querySelector<HTMLElement>('.dagger-controls-root')!;
  const controlsView = mountControls(controlsRoot, send);
  const characterRoot = shell.querySelector<HTMLElement>('.dagger-character-root')!;
  const characterView = mountCharacter(characterRoot, send);
  const notebookRoot = shell.querySelector<HTMLElement>('.dagger-notebook-root')!;
  const notebookView = mountNotebook(notebookRoot, send);
  const questJournalView = mountQuestJournal(notebookRoot);
  const spellsRoot = shell.querySelector<HTMLElement>('.dagger-spells-root')!;
  const lootRoot = shell.querySelector<HTMLElement>('.dagger-loot-root')!;
  const lootView = mountLoot(lootRoot, send);
  let currentLoot: LootProjection | null = null;
  let lastLootContainer: string | null = null;
  const closeLoot = (): void => { if (currentLoot) send({ action: 'loot-close', container: currentLoot.container }); };
  const saveSlotsRoot = shell.querySelector<HTMLElement>('.dagger-save-slots')!;
  const saveSlotsDiagnostic = shell.querySelector<HTMLElement>('.dagger-save-slots-diagnostic')!;
  const saveSlotsLabel = shell.querySelector<HTMLInputElement>('.dagger-save-slots-label')!;
  const saveSlotsSelect = shell.querySelector<HTMLSelectElement>('.dagger-save-slots-select')!;
  const saveSlotSave = shell.querySelector<HTMLButtonElement>('[data-action="save-slot"]')!;
  const saveSlotLoad = shell.querySelector<HTMLButtonElement>('[data-action="load-slot"]')!;
  const saveSlotDelete = shell.querySelector<HTMLButtonElement>('[data-action="delete-slot"]')!;
  let saveSlots: SaveSlotProjection = { entries: [], diagnostic: null };
  let saveSlotMode: 'save' | 'load' = 'save';
  let saveConfirm = false;
  let deleteConfirm = false;
  // A load asked for from the menu: a successful one replaces the session, which starts in play, so
  // the menu that asked closes rather than stay open over a world that is running again.
  let loadRequested = false;
  let actionDrivenTime = false;
  let inventorySuppressed = false;
  let lastPanelRevision: string | null = null;
  let activePanel: Panel | null = null;

  const debugRoot = shell.querySelector<HTMLElement>('.dagger-debug-root')!;
  let debugPanel: LiveDebugPanelMount | null = null;
  const closeDebug = (): void => { debugPanel?.dispose(); debugPanel = null; debugRoot.replaceChildren(); };
  const openDebug = (): void => {
    closeDebug();
    const container = document.createElement('div');
    debugRoot.append(container);
    void mountLiveDebugPanel(container, { enabled: true, presentation: 'inline' }).then(mount => {
      if (!container.isConnected || activePanel !== 'debug') { mount.dispose(); return; }
      debugPanel = mount;
      container.querySelector<HTMLInputElement>('input')?.focus();
    }).catch(error => {
      if (container.isConnected) container.textContent = `Debug console unavailable: ${error instanceof Error ? error.message : String(error)}`;
    });
  };

  // Each panel remembers the control that last held focus, so reopening it (from home or by the
  // shoulder buttons) puts the player back where they were rather than on its Back button.
  const panelFocus = new Map<Panel, HTMLElement>();
  panel.addEventListener('focusin', event => {
    // The panel's own Back and Exit buttons leave the panel; they are never where its content focus was.
    const leaving = event.target instanceof HTMLElement && event.target.parentElement === panel
      && (event.target.dataset.action === 'back' || event.target.dataset.action === 'loot-exit');
    if (activePanel !== null && event.target instanceof HTMLElement && !leaving) panelFocus.set(activePanel, event.target);
  });
  const restorePanelFocus = (action: Panel): boolean => {
    const remembered = panelFocus.get(action);
    if (!remembered?.isConnected || !panel.contains(remembered) || remembered.closest('[hidden]')
      || (remembered as HTMLButtonElement).disabled) return false;
    remembered.focus();
    return document.activeElement === remembered;
  };
  const showHome = (): void => {
    inventoryView.closeActions();
    reportMap(false);
    controlsView.cancel();
    const previous = activePanel;
    if (previous === 'transport') travelView.hide();
    if (previous === 'debug') closeDebug();
    if (previous === 'loot') closeLoot();
    activePanel = null;
    menu.classList.remove('has-inventory', 'has-character', 'has-spells', 'has-transport', 'has-rest', 'has-journal', 'has-loot', 'has-debug');
    home.hidden = false;
    panel.hidden = true;
    menuTitle.textContent = 'Game menu';
    const returnAction = previous === 'save-slots' ? (saveSlotMode === 'save' ? 'save-game' : 'load-game') : previous ?? 'resume';
    home.querySelector<HTMLButtonElement>(`[data-action="${returnAction}"]`)?.focus();
  };
  const reportMenu = (open: boolean): void => send({ action: 'menu', open });
  const closeMenu = (): void => {
    inventoryView.closeActions();
    reportMap(false);
    travelView.hide();
    controlsView.cancel();
    if (activePanel === 'loot') closeLoot();
    closeDebug();
    activePanel = null;
    loadRequested = false;
    menu.close();
    host.sync(true);
    reportMenu(false);
  };
  const openMenu = (): void => {
    loadRequested = false;
    menu.showModal();
    host.sync();
    showHome();
    reportMenu(true);
  };
  const dismiss = (): void => {
    if (host.isDead()) return;
    if (activePanel !== null) showHome();
    else if (menu.open) closeMenu();
    else openMenu();
  };
  const showPanel = (action: Panel): void => {
    if (action === 'inventory' && inventorySuppressed) return;
    if (!menu.open) openMenu();
    inventoryView.closeActions();
    if (activePanel === 'loot' && action !== 'loot') closeLoot();
    if (activePanel === 'debug') closeDebug();
    reportMap(action === 'map');
    if (action !== 'transport') travelView.hide();
    activePanel = action;
    home.hidden = true;
    panel.hidden = false;
    diagnostics.hidden = action !== 'diagnostics';
    inventoryRoot.hidden = action !== 'inventory';
    magicItemsRoot.hidden = action !== 'magic-items';
    characterRoot.hidden = action !== 'character';
    spellsRoot.hidden = action !== 'spells';
    mapRoot.hidden = action !== 'map';
    transportRoot.hidden = action !== 'transport';
    restRoot.hidden = action !== 'rest';
    notebookRoot.hidden = action !== 'journal';
    lootRoot.hidden = action !== 'loot';
    saveSlotsRoot.hidden = action !== 'save-slots';
    controlsRoot.hidden = action !== 'settings';
    debugRoot.hidden = action !== 'debug';
    if (action === 'debug') openDebug();
    shell.querySelector<HTMLButtonElement>('[data-action="loot-exit"]')!.hidden = action !== 'loot';
    for (const name of ['inventory', 'character', 'spells', 'transport', 'rest', 'journal', 'loot', 'debug'] as const)
      menu.classList.toggle(`has-${name}`, action === name);
    menuTitle.textContent = PANEL_TITLES[action] ?? (saveSlotMode === 'save' ? 'Save game' : 'Load game');
    if (restorePanelFocus(action)) return;
    if (action === 'character') {
      menuTitle.focus({ preventScroll: true });
      menu.scrollTop = 0;
    } else panel.querySelector<HTMLButtonElement>(':scope > [data-action="back"]')!.focus();
  };
  const redrawSaveSlots = (): void => {
    const selected = saveSlotsSelect.value;
    redraw(saveSlotsSelect, JSON.stringify(saveSlots.entries), () => {
      saveSlotsSelect.replaceChildren(new Option('New slot', ''));
      for (const entry of saveSlots.entries) {
        const savedAt = Number.isNaN(Date.parse(entry.savedAtUtc)) ? '' : ` · ${new Date(entry.savedAtUtc).toLocaleString()}`;
        saveSlotsSelect.add(new Option(`${entry.label}${savedAt}`, entry.key));
      }
    });
    saveSlotsSelect.value = saveSlots.entries.some(entry => entry.key === selected) ? selected : '';
    const selectedEntry = saveSlots.entries.find(entry => entry.key === saveSlotsSelect.value);
    if (selectedEntry && !saveSlotsLabel.value) saveSlotsLabel.value = selectedEntry.label;
    saveSlotsDiagnostic.textContent = saveSlots.diagnostic ?? (saveSlots.entries.length === 0 ? 'No saved games yet.' : 'Choose a saved game or name a new one.');
    saveSlotSave.hidden = saveSlotMode !== 'save';
    saveSlotLoad.hidden = saveSlotMode !== 'load';
    saveSlotDelete.hidden = saveSlotMode !== 'load';
    saveSlotsLabel.closest('label')!.hidden = saveSlotMode !== 'save';
    saveSlotSave.textContent = saveConfirm ? 'Confirm overwrite' : 'Save';
    saveSlotDelete.textContent = deleteConfirm ? 'Confirm delete' : 'Delete selected';
    const hasSelection = saveSlotsSelect.value.length > 0;
    saveSlotLoad.disabled = !hasSelection;
    saveSlotDelete.disabled = !hasSelection;
  };
  const showSaveSlots = (mode: 'save' | 'load'): void => {
    saveSlotMode = mode;
    saveConfirm = false;
    deleteConfirm = false;
    redrawSaveSlots();
    showPanel('save-slots');
    send({ action: 'save-slots' });
  };
  // One place decides what a menu action means, whether the DOM heard it from a click, a key, or the
  // product answering a button on a pad the DOM cannot see.
  const runAction = (action: string | undefined): void => {
    if (action === 'resume' || action === 'loot-exit') closeMenu();
    else if (action === 'back') showHome();
    else if (action === 'menu') dismiss();
    else if (action === 'loot') send({ action: 'loot' });
    else if (action === 'action-driven-time') send({ action: 'action-driven-time', enabled: !actionDrivenTime });
    else if (action === 'save-game') showSaveSlots('save');
    // Quick save and quick load are the product's own actions over the Host's quick-save slot; a
    // successful quick load replaces the session, so the menu that asked closes the way a slot load does.
    else if (action === 'quick-save') send({ action: 'save-game' });
    else if (action === 'quick-load') { send({ action: 'load-game' }); loadRequested = true; }
    else if (action === 'load-game') showSaveSlots('load');
    else if (action === 'magic-items' || action === 'spells' || action === 'settings' || action === 'diagnostics' || action === 'inventory' || action === 'character' || action === 'map' || action === 'transport' || action === 'rest' || action === 'journal' || action === 'debug') showPanel(action);
  };
  const onClick = (event: MouseEvent): void => {
    const control = (event.target as HTMLElement).closest<HTMLButtonElement>('button');
    if (control?.disabled) return;
    const action = control?.dataset.action;
    if (control && spellAction(control, send, ask, closeMenu)) return;
    if (action === 'rest' && control?.dataset.restMode) { submitRest(control.dataset.restMode); return; }
    if (action === 'save-slot') {
      const key = saveSlotsSelect.value || undefined;
      const label = saveSlotsLabel.value.trim();
      if (!label) {
        saveSlotsDiagnostic.textContent = 'Name the save slot before saving.';
        saveSlotsLabel.focus();
        return;
      }
      if (key && !saveConfirm) { saveConfirm = true; redrawSaveSlots(); return; }
      send({ action: 'save-slot', key, label, confirm: saveConfirm });
      saveConfirm = false;
      return;
    }
    if (action === 'load-slot' && saveSlotsSelect.value) {
      send({ action: 'load-slot', key: saveSlotsSelect.value });
      loadRequested = true;
      return;
    }
    if (action === 'delete-slot' && saveSlotsSelect.value) {
      if (!deleteConfirm) { deleteConfirm = true; redrawSaveSlots(); return; }
      send({ action: 'delete-slot', key: saveSlotsSelect.value, confirm: true });
      deleteConfirm = false;
      return;
    }
    runAction(action);
  };
  saveSlotsSelect.addEventListener('change', () => {
    saveConfirm = false;
    deleteConfirm = false;
    const selected = saveSlots.entries.find(entry => entry.key === saveSlotsSelect.value);
    if (selected) saveSlotsLabel.value = selected.label;
    redrawSaveSlots();
  });
  const onCancel = (event: Event): void => event.preventDefault();
  const onToggle = (): void => runAction('menu');
  menu.addEventListener('cancel', onCancel);
  menu.addEventListener('click', onClick);
  menuToggle.addEventListener('click', onToggle);

  return {
    dialog: menu,
    creation: characterView.creationElement,
    open: openMenu,
    close: closeMenu,
    dismiss,
    back(source) {
      if (activePanel === 'inventory' && inventoryView.release()) return;
      if (source === 'escape') closeMenu(); else dismiss();
    },
    runAction,
    contextMenu() { if (activePanel === 'inventory') inventoryView.openActions(); },
    stepPanel(direction) {
      const index = STEPPED_PANELS.indexOf(activePanel as typeof STEPPED_PANELS[number]);
      const from = index < 0 ? (direction > 0 ? -1 : 0) : index;
      runAction(STEPPED_PANELS[(from + direction + STEPPED_PANELS.length) % STEPPED_PANELS.length]!);
    },
    captureKey(event) { if (activePanel === 'settings') controlsView.captureKey(event); },
    update(value, dead, spellbook, diagnosticText) {
      renderSpells(spellsRoot, spellbook);
      inventorySuppressed = value.character?.identity?.racialOverride?.suppressInventory === true;
      for (const control of shell.querySelectorAll<HTMLButtonElement>('[data-action="inventory"]')) control.disabled = inventorySuppressed;
      if (inventorySuppressed && activePanel === 'inventory') showHome();
      if (value.inventory) inventoryView.update(value.inventory);
      inventoryView.updateIdentity(value.character && isCharacterProjection(value.character) ? value.character.identity : null);
      propertyView.update(value.property ?? null, value.inventory);
      transportView.update(isTransportProjection(value.transport) ? value.transport : null, value.inventory);
      mapView.update(isMapProjection(value.map) ? value.map : null);
      travelView.update(isTravelProjection(value.travel) ? value.travel : null);
      if (value.character && isCharacterProjection(value.character)) characterView.update(value.character);
      if (value.notebook) notebookView.update(value.notebook);
      questJournalView.update(value.quests?.journal);
      lodgingView.update(isLodgingProjection(value.lodging) ? value.lodging : null);
      if (value.rest && isRestProjection(value.rest)) {
        restHours.max = String(value.rest.maximumRestHours);
        loiterHours.max = String(value.rest.maximumLoiterHours);
        restStatus.textContent = value.rest.message
          ?? (value.rest.hasResult ? `Rested for ${formatRestHours(value.rest.elapsedSeconds)}.` : 'Choose a rest action.');
      }
      // A pad button reaches the product, not this DOM, so the panel it asked for arrives here as the
      // menu action that opens it. Each revision is performed once; the request itself stays published.
      const panelRequest = value.panelRequest ?? null;
      if (!dead && panelRequest !== null && panelRequest.revision !== lastPanelRevision) {
        lastPanelRevision = panelRequest.revision;
        runAction(panelRequest.panel);
      }
      currentLoot = value.loot ?? null;
      lootView.update(currentLoot);
      if (currentLoot && currentLoot.container !== lastLootContainer) {
        lastLootContainer = currentLoot.container;
        showPanel('loot');
      } else if (!currentLoot && activePanel === 'loot') showHome();
      // Death outranks every other mode: the menu closes under the death screen.
      if (dead && menu.open) closeMenu();
      // The open menu holds the world, so the product reports modal until a requested load replaces the
      // session; a failed load leaves the old session, still modal, and the menu shows why.
      if (loadRequested && menu.open && value.mode === 'playing') closeMenu();
      menuToggle.hidden = dead;
      actionDrivenTime = value.timeControl?.enabled ?? false;
      const timeToggle = menu.querySelector<HTMLButtonElement>('[data-action="action-driven-time"]')!;
      timeToggle.setAttribute('aria-pressed', String(actionDrivenTime));
      timeToggle.textContent = `Action-driven time: ${actionDrivenTime ? 'On' : 'Off'}`;
      redraw(textDiagnostics, JSON.stringify(diagnosticText), () => textDiagnostics.replaceChildren(...diagnosticText.map(detail => {
        const item = document.createElement('li'); item.textContent = detail; return item;
      })));
      const magic = value.inventory?.items.filter(item => item.canUseMagic) ?? [];
      const inventoryRevision = value.inventory?.revision;
      redraw(magicItemsRoot, JSON.stringify([inventoryRevision, magic]), () => {
        magicItemsRoot.replaceChildren();
        for (const item of magic) {
          const control = button(item.label);
          control.addEventListener('click', () => {
            send({ action: 'inventory-use', revision: inventoryRevision, item: item.key });
            closeMenu();
          });
          magicItemsRoot.append(control);
        }
        if (!magicItemsRoot.childElementCount) magicItemsRoot.textContent = 'No usable magic items or potions.';
      });
      if (value.controls) controlsView.update(value.controls, host.uiScale());
      if (value.activation) activationMode.value = value.activation.mode;
      // The selector lives in the menu, which the product reports as modal, so it serves both.
      activationMode.disabled = value.mode !== 'playing' && value.mode !== 'modal';
      if (isSaveSlots(value.saveSlots)) { saveSlots = value.saveSlots; redrawSaveSlots(); }
      redraw(composition, JSON.stringify(value.composition), () => composition.replaceChildren(...diagnosticRows(value.composition)));
    },
    refreshArt() {
      // The panels memo their own state revision, so art that arrived after the state it draws has to
      // ask them to paint again rather than re-send a projection they would ignore.
      inventoryView.refresh();
      characterView.refresh();
      lootView.refresh();
    },
    dispose() {
      reportMap(false);
      closeDebug();
      controlsView.dispose();
      inventoryView.dispose();
      transportView.dispose();
      mapView.dispose();
      travelView.dispose();
      lodgingView.dispose();
      characterView.dispose();
      notebookView.dispose();
      questJournalView.dispose();
      lootView.dispose();
      menu.removeEventListener('cancel', onCancel);
      menu.removeEventListener('click', onClick);
      menuToggle.removeEventListener('click', onToggle);
      saveSlotsSelect.replaceChildren();
      if (menu.open) menu.close();
    },
  };
}

const PANEL_TITLES: Partial<Record<Panel, string>> = {
  'magic-items': 'Use magic item', spells: 'Known spells', settings: 'Control settings', diagnostics: 'Composition diagnostics',
  inventory: 'Inventory & equipment', character: 'Character', map: 'Map', transport: 'Travel & transport', rest: 'Rest & loiter',
  journal: 'Journal & notes', loot: 'Loot', debug: 'Engine debug console',
};
