import { mountProperty } from './property.js';
import { mountMap, isMapProjection } from './maps.js';
import { mountLodging, isLodgingProjection } from './lodging.js';
import { mountControls } from './controls.js';
import { mountLiveDebugPanel, type LiveDebugPanelMount } from '@rusty-engine/live-debug';
import { adopt, heldRevision } from './art.js';
import { mountConfirm, type ConfirmQuestion } from './confirm.js';
import { mountTooltips } from './tooltip.js';
import { mountShell } from './shell.js';
import { sender } from './host.js';
import { mountChoices } from './choices.js';
import { mountDungeonText } from './dungeon-text.js';
import { mountDeath } from './death.js';
import { mountEntry } from './entry.js';
import { mountDialogue } from './dialogue.js';
import { mountInteraction } from './interaction.js';
import { mountNavigation } from './navigation.js';
import { mountInventory } from './inventory.js';
import { mountCharacter, isCharacterProjection } from './character.js';
import { mountLoot, type LootProjection } from './loot.js';
import { mountNotebook } from './notebook.js';
import { mountQuestJournal } from './journal.js';
import { mountTravel, isTravelProjection } from './travel.js';

import type { RustyApplicationUiContext } from '@rusty-engine/product-ui';
import {
  UI_ACTION_CONTRACT, diagnosticRows, formatRestHours, isHud, isRestProjection, isSaveSlots, viewSummary, type SaveSlotProjection
} from './projection.js';
import { isTransportProjection, mountTransport } from './transport.js';
import { isSpellbookProjection, renderSpells, spellAction } from './spells.js';
import { renderQuestMessages } from './quests.js';

export { isHud } from './projection.js';
export { isSummoningProjection, renderSummoning } from './crafting.js';

/** Snapshots between repeated requests for art this DOM has not received. */
const ART_REQUEST_INTERVAL = 120;
/** Controller shortcuts the open menu adds to the shared navigation: Start toggles it, Select and Y open panels. */
const MENU_SHORTCUTS = Object.freeze({ menu: 'button-9', inventory: 'button-8', character: 'button-3' });

export function mountProductUi(root: HTMLElement, context: RustyApplicationUiContext): { dispose(): void } {
  const { shell, dispose: disposeShell } = mountShell(root);

  const confirmPrompt = mountConfirm(shell);
  const askConfirm = (question: ConfirmQuestion): void => confirmPrompt.ask(question);
  const tooltips = mountTooltips(shell);

  const activationMode = shell.querySelector<HTMLSelectElement>('.dagger-activation-mode')!;
  activationMode.addEventListener('change', () => context.intents?.claim('dagger.ui', {
    kind: 'product-payload', contract: UI_ACTION_CONTRACT, data: { action: 'activation-mode', mode: activationMode.value },
  }));
  const title = shell.querySelector<HTMLElement>('.dagger-title strong')!;
  const siteName = shell.querySelector<HTMLElement>('.dagger-title .dagger-site')!;
  const outcome = shell.querySelector<HTMLOListElement>('.dagger-outcome')!;
  const breath = shell.querySelector<HTMLLabelElement>('.dagger-breath')!;
  const activationIndicator = shell.querySelector<HTMLElement>('.dagger-activation-indicator')!;
  const quests = shell.querySelector<HTMLElement>('.dagger-quests')!;
  const send = sender(context);
  const choices = mountChoices(shell, quests, send);
  const { legal, createItem } = choices;
  const view = shell.querySelector<HTMLParagraphElement>('.dagger-view')!;
  const detectors = shell.querySelector<HTMLElement>('.dagger-detectors')!;
  const status = shell.querySelector<HTMLElement>('.dagger-status')!;
  const calendar = shell.querySelector<HTMLElement>('.dagger-calendar')!;
  const effects = shell.querySelector<HTMLElement>('.dagger-effects')!;
  const effectList = effects.querySelector<HTMLUListElement>('ul')!;
  const cinematicSkip = shell.querySelector<HTMLButtonElement>('.dagger-cinematic-skip')!;
  const sendCinematicSkip = (): void => { context.intents?.claim('dagger.ui', {
    kind: 'product-payload', contract: UI_ACTION_CONTRACT, data: { action: 'cinematic-skip' },
  }); };
  cinematicSkip.addEventListener('click', sendCinematicSkip);
  // The shell draws a playing video above this UI, so the button cannot be reached while one plays.
  // As in the classic game, any key or button press skips the film instead; the press goes no further.
  let cinematicPlaying = false;
  const skipOnPress = (event: KeyboardEvent | PointerEvent): void => {
    if (!cinematicPlaying || (event instanceof KeyboardEvent && event.repeat)) return;
    event.preventDefault();
    event.stopImmediatePropagation();
    sendCinematicSkip();
  };
  document.addEventListener('keydown', skipOnPress, true);
  document.addEventListener('pointerdown', skipOnPress, true);
  const vitals = shell.querySelector<HTMLElement>('.dagger-vitals')!;
  const composition = shell.querySelector<HTMLDListElement>('.dagger-composition dl')!;
  const claim = (action: string): void => context.intents?.claim('dagger.ui', {
    kind: 'product-payload', contract: UI_ACTION_CONTRACT, data: { action },
  });
  const death = mountDeath(shell, send);
  const dungeonText = mountDungeonText(shell, send);
  const inventoryRoot = shell.querySelector<HTMLElement>('.dagger-inventory-root')!;
  const inventoryView = mountInventory(inventoryRoot, (action) => context.intents?.claim('dagger.ui', {
    kind: 'product-payload', contract: UI_ACTION_CONTRACT, data: action,
  }));
  const mapRoot = shell.querySelector<HTMLElement>('.dagger-map-root')!;
  const mapView = mountMap(mapRoot, action => context.intents?.claim('dagger.ui', {
    kind: 'product-payload', contract: UI_ACTION_CONTRACT, data: action,
  }), () => showPanel('transport'));
  let mapReported = false;
  const reportMap = (open: boolean): void => {
    if (mapReported === open) return;
    mapReported = open;
    context.intents?.claim('dagger.ui', {
      kind: 'product-payload', contract: UI_ACTION_CONTRACT, data: { action: 'map-open', open },
    });
  };
  const transportRoot = shell.querySelector<HTMLElement>('.dagger-transport-root')!;
  const transportView = mountTransport(transportRoot, action => context.intents?.claim('dagger.ui', {
    kind: 'product-payload', contract: UI_ACTION_CONTRACT, data: action,
  }));
  const propertyRoot = document.createElement('section');
  propertyRoot.className = 'dagger-property-root';
  inventoryRoot.append(propertyRoot);
  const propertyView = mountProperty(propertyRoot, action => context.intents?.claim('dagger.ui', {
    kind: 'product-payload', contract: UI_ACTION_CONTRACT, data: action,
  }));
  const travelView = mountTravel(transportRoot, action => context.intents?.claim('dagger.ui', {
    kind: 'product-payload', contract: UI_ACTION_CONTRACT, data: action,
  }));
  const restRoot = shell.querySelector<HTMLElement>('.dagger-rest-root')!;
  const lodgingView = mountLodging(restRoot, action => context.intents?.claim('dagger.ui', {
    kind: 'product-payload', contract: UI_ACTION_CONTRACT, data: action,
  }));
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
    if (mode === 'until-healed') {
      context.intents?.claim('dagger.ui', {
        kind: 'product-payload', contract: UI_ACTION_CONTRACT, data: { action: 'rest', mode },
      });
      return;
    }
    const box = mode === 'loiter' ? loiterHours : restHours;
    const rawHours = box.value.trim();
    const hours = Number(rawHours);
    if (rawHours.length === 0 || !Number.isSafeInteger(hours) || hours < Number(box.min) || hours > Number(box.max)) {
      restStatus.textContent = `Enter a whole number of hours from ${box.min} to ${box.max}.`;
      box.focus();
      return;
    }
    context.intents?.claim('dagger.ui', {
      kind: 'product-payload', contract: UI_ACTION_CONTRACT, data: { action: 'rest', mode, hours },
    });
  };
  const magicItemsRoot = document.createElement('section'); magicItemsRoot.className = 'dagger-magic-items'; magicItemsRoot.hidden = true;
  let magicItemsIdentity = '';
  shell.querySelector('.dagger-controls-root')!.before(magicItemsRoot);
  const controlsRoot = shell.querySelector<HTMLElement>('.dagger-controls-root')!;
  const controlsView = mountControls(controlsRoot, action => context.intents?.claim('dagger.ui', {
    kind: 'product-payload', contract: UI_ACTION_CONTRACT, data: action,
  }));
  const characterRoot = shell.querySelector<HTMLElement>('.dagger-character-root')!;
  const characterView = mountCharacter(characterRoot, action => context.intents?.claim('dagger.ui', {
    kind: 'product-payload', contract: UI_ACTION_CONTRACT, data: action,
  }));
  const entry = mountEntry(shell, send, characterView.creationElement, () => { if (menu.open) menu.close(); });
  const notebookRoot = shell.querySelector<HTMLElement>('.dagger-notebook-root')!;
  const notebookView = mountNotebook(notebookRoot, action => context.intents?.claim('dagger.ui', {
    kind: 'product-payload', contract: UI_ACTION_CONTRACT, data: action,
  }));
  const questJournalView = mountQuestJournal(notebookRoot);
  const lootRoot = shell.querySelector<HTMLElement>('.dagger-loot-root')!;
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
  const lootView = mountLoot(lootRoot, action => context.intents?.claim('dagger.ui', {
    kind: 'product-payload', contract: UI_ACTION_CONTRACT, data: action,
  }));
  let currentLoot: LootProjection | null = null;
  let lastLootContainer: string | null = null;
  const closeLoot = (): void => {
    if (currentLoot) context.intents?.claim('dagger.ui', {
      kind: 'product-payload', contract: UI_ACTION_CONTRACT, data: { action: 'loot-close', container: currentLoot.container },
    });
  };
  const menu = shell.querySelector<HTMLDialogElement>('dialog')!;
  const dialogue = mountDialogue(shell, send, askConfirm);
  // Unresolved text symbols are developer diagnostics: they are listed in the composition
  // diagnostics panel, never in the conversation, quest message or summoning text the player reads.
  const textDiagnostics = shell.querySelector<HTMLElement>('.dagger-text-diagnostics')!;
  const menuTitle = shell.querySelector<HTMLElement>('#dagger-menu-title')!;
  const home = shell.querySelector<HTMLElement>('.dagger-menu-home')!;
  const panel = shell.querySelector<HTMLElement>('.dagger-menu-panel')!;
  const diagnostics = shell.querySelector<HTMLElement>('.dagger-composition')!;
  const menuToggle = shell.querySelector<HTMLButtonElement>('.dagger-menu-toggle')!;
  const debugRoot = shell.querySelector<HTMLElement>('.dagger-debug-root')!;
  let debugPanel: LiveDebugPanelMount | null = null;
  const closeDebug = (): void => { debugPanel?.dispose(); debugPanel = null; debugRoot.replaceChildren(); };
  const openDebug = (): void => {
    closeDebug();
    const host = document.createElement('div');
    debugRoot.append(host);
    void mountLiveDebugPanel(host, { enabled: true, presentation: 'inline' }).then(mount => {
      if (!host.isConnected || activePanel !== 'debug') { mount.dispose(); return; }
      debugPanel = mount;
      host.querySelector<HTMLInputElement>('input')?.focus();
    }).catch(error => {
      if (host.isConnected) host.textContent = `Debug console unavailable: ${error instanceof Error ? error.message : String(error)}`;
    });
  };
  const spellsRoot=shell.querySelector<HTMLElement>('.dagger-spells-root')!;
  let questAnswerRequired = false;
  // A pointer lock the world held, lost in play, is the player asking for the menu: the browser and
  // the desktop shell spend Escape on releasing the lock, so this is what Escape does in play.
  const interaction = mountInteraction(context, { onLockLostInPlay: () => { if (!menu.open && !deadMode && !entry.isTitle()) openMenu(); } });
  interaction.needsCursor(() => menu.open || dialogue.window.open || dialogue.summoningOffer.open);
  // Every cursor surface is a navigation surface; deciding the mode and the focus scope together keeps
  // them from disagreeing about which surface is up.
  const syncInteraction = (focus = false): void => { interaction.sync(focus); navigation.refresh(); };
  let inventorySuppressed = false;
  let activePanel: 'spells' | 'magic-items' | 'diagnostics' | 'inventory' | 'character' | 'map' | 'transport' | 'rest' | 'journal' | 'loot' | 'debug' | 'save-slots' | 'settings' | null = null;
  const showHome = (): void => {
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
  // An open menu holds the world, so the product is told whenever this one opens or closes.
  const reportMenu = (open: boolean): void => context.intents?.claim('dagger.ui', {
    kind: 'product-payload', contract: UI_ACTION_CONTRACT, data: { action: 'menu', open },
  });
  const closeMenu = (): void => {
    reportMap(false);
    travelView.hide();
    controlsView.cancel();
    if (activePanel === 'loot') closeLoot();
    closeDebug();
    activePanel = null;
    loadRequested = false;
    menu.close();
    syncInteraction(true);
    reportMenu(false);
  };
  const openMenu = (): void => {
    loadRequested = false;
    menu.showModal();
    syncInteraction();
    showHome();
    reportMenu(true);
  };
  // Escape pressed under pointer lock never reaches the page: the browser, and the desktop shell
  // likewise, spend it releasing the lock. A lock lost while the world has the pointer (Escape or
  // focus loss) therefore opens the menu, so one press does what Escape does everywhere else. A lock
  // this UI gives up by entering interface mode is already past gameplay when the release arrives.
  const dismiss = (): void => {
    if (deadMode) return;
    if (activePanel !== null) showHome();
    else if (menu.open) closeMenu();
    else openMenu();
  };
  const showPanel = (action: 'spells' | 'magic-items' | 'diagnostics' | 'inventory' | 'character' | 'map' | 'transport' | 'rest' | 'journal' | 'loot' | 'debug' | 'save-slots' | 'settings'): void => {
    if (action === 'inventory' && inventorySuppressed) return;
    if (!menu.open) openMenu();
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
    spellsRoot.hidden=action!=='spells';
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
    menu.classList.toggle('has-inventory', action === 'inventory');
    menu.classList.toggle('has-character', action === 'character');
    menu.classList.toggle('has-spells', action === 'spells');
    menu.classList.toggle('has-transport', action === 'transport');
    menu.classList.toggle('has-rest', action === 'rest');
    menu.classList.toggle('has-journal', action === 'journal');
    menu.classList.toggle('has-loot', action === 'loot');
    menu.classList.toggle('has-debug', action === 'debug');
    menuTitle.textContent = action === 'magic-items' ? 'Use magic item' : action === 'spells' ? 'Known spells' : action === 'settings' ? 'Control settings' : action === 'diagnostics' ? 'Composition diagnostics'
      : action === 'inventory' ? 'Inventory & equipment' : action === 'character' ? 'Character' : action === 'map' ? 'Map' : action === 'transport' ? 'Travel & transport' : action === 'rest' ? 'Rest & loiter' : action === 'journal' ? 'Journal & notes'
      : action === 'loot' ? 'Loot' : action === 'debug' ? 'Engine debug console'
      : saveSlotMode === 'save' ? 'Save game' : 'Load game';
    if (action === 'character') {
      menuTitle.focus({ preventScroll: true });
      menu.scrollTop = 0;
    } else panel.querySelector<HTMLButtonElement>(':scope > [data-action="back"]')!.focus();
  };
  const redrawSaveSlots = (): void => {
    const selected = saveSlotsSelect.value;
    saveSlotsSelect.replaceChildren(new Option('New slot', ''));
    for (const entry of saveSlots.entries) {
      const savedAt = Number.isNaN(Date.parse(entry.savedAtUtc)) ? '' : ` · ${new Date(entry.savedAtUtc).toLocaleString()}`;
      saveSlotsSelect.add(new Option(`${entry.label}${savedAt}`, entry.key));
    }
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
    claim('save-slots');
  };
  // One place decides what a menu action means, whether the DOM heard it from a click, a key, or the
  // product answering a button on a pad the DOM cannot see.
  const setActionDrivenTime = (enabled: boolean): void => context.intents?.claim('dagger.ui', {
    kind: 'product-payload', contract: UI_ACTION_CONTRACT, data: { action: 'action-driven-time', enabled },
  });
  const runMenuAction = (action: string | undefined): void => {
    if (action === 'resume' || action === 'loot-exit') closeMenu();
    else if (action === 'back') showHome();
    else if (action === 'menu') dismiss();
    else if (action === 'loot') claim('loot');
    else if (action === 'action-driven-time') setActionDrivenTime(!actionDrivenTime);
    else if (action === 'save-game') showSaveSlots('save');
    // Quick save and quick load are the product's own actions over the Host's quick-save slot; a
    // successful quick load replaces the session, so the menu that asked closes the way a slot load does.
    else if (action === 'quick-save') claim('save-game');
    else if (action === 'quick-load') { claim('load-game'); loadRequested = true; }
    else if (action === 'load-game') showSaveSlots('load');
    else if (action === 'magic-items' || action === 'spells' || action === 'settings' || action === 'diagnostics' || action === 'inventory' || action === 'character' || action === 'map' || action === 'transport' || action === 'rest' || action === 'journal' || action === 'debug') showPanel(action);
  };
  const onMenuClick = (event: MouseEvent): void => {
    const button = (event.target as HTMLElement).closest<HTMLButtonElement>('button');
    if(button?.disabled) return;
    const action = button?.dataset.action;
    if (button && spellAction(button, send, askConfirm, closeMenu)) return;
    if (action === 'rest' && button?.dataset.restMode) {
      submitRest(button.dataset.restMode);
      return;
    }
    if (action === 'save-slot') {
      const key = saveSlotsSelect.value || undefined;
      const label = saveSlotsLabel.value.trim();
      if (!label) {
        saveSlotsDiagnostic.textContent = 'Name the save slot before saving.';
        saveSlotsLabel.focus();
        return;
      }
      if (key && !saveConfirm) { saveConfirm = true; redrawSaveSlots(); return; }
      context.intents?.claim('dagger.ui', { kind: 'product-payload', contract: UI_ACTION_CONTRACT, data: { action: 'save-slot', key, label, confirm: saveConfirm } });
      saveConfirm = false;
      return;
    }
    if (action === 'load-slot' && saveSlotsSelect.value) {
      context.intents?.claim('dagger.ui', { kind: 'product-payload', contract: UI_ACTION_CONTRACT, data: { action: 'load-slot', key: saveSlotsSelect.value } });
      loadRequested = true;
      return;
    }
    if (action === 'delete-slot' && saveSlotsSelect.value) {
      if (!deleteConfirm) { deleteConfirm = true; redrawSaveSlots(); return; }
      context.intents?.claim('dagger.ui', { kind: 'product-payload', contract: UI_ACTION_CONTRACT, data: { action: 'delete-slot', key: saveSlotsSelect.value, confirm: true } });
      deleteConfirm = false;
      return;
    }
    runMenuAction(action);
  };
  saveSlotsSelect.addEventListener('change', () => {
    saveConfirm = false;
    deleteConfirm = false;
    const selected = saveSlots.entries.find(entry => entry.key === saveSlotsSelect.value);
    if (selected) saveSlotsLabel.value = selected.label;
    redrawSaveSlots();
  });
  // Key capture for rebinding runs before navigation, which would otherwise take Escape and the arrows.
  const onKeyDown = (event: KeyboardEvent): void => { if (activePanel === 'settings') controlsView.captureKey(event); };
  document.addEventListener('keydown', onKeyDown, true);
  // Escape with no surface open is Escape in play: it opens the menu, except where there is none.
  const navigation = mountNavigation(shell, context, {
    onPlayEscape: () => { if (!deadMode && !entry.isTitle()) openMenu(); },
    onButton: button => {
      if (!menu.open || confirmPrompt.open()) return;
      if (button === MENU_SHORTCUTS.menu) dismiss();
      else if (button === MENU_SHORTCUTS.inventory) runMenuAction('inventory');
      else if (button === MENU_SHORTCUTS.character) runMenuAction('character');
    },
  });
  // The surfaces that take the cursor, topmost first. Escape and Back go to the topmost: from any menu
  // panel Escape closes the whole menu while Back steps out one panel. A question the product needs
  // answered (a summoning offer, a court, a quest prompt, the death and title screens) has no back, so
  // neither press acts on it or on what it was asked over; a spell choice with a Cancel takes Cancel.
  const MENU_PANELS = ['inventory', 'character', 'spells', 'magic-items', 'map', 'transport', 'rest', 'journal'] as const;
  const surfaces = [
    navigation.register({ name: 'confirm', root: confirmPrompt.dialog, layer: 100, isOpen: confirmPrompt.open, back: confirmPrompt.cancel }),
    navigation.register({ name: 'menu', root: menu, layer: 90, isOpen: () => menu.open,
      back: source => { if (source === 'escape') closeMenu(); else dismiss(); },
      panel: direction => {
        const index = MENU_PANELS.indexOf(activePanel as typeof MENU_PANELS[number]);
        const from = index < 0 ? (direction > 0 ? -1 : 0) : index;
        const next = MENU_PANELS[(from + direction + MENU_PANELS.length) % MENU_PANELS.length]!;
        runMenuAction(next);
      } }),
    navigation.register({ name: 'summoning-offer', root: dialogue.summoningOffer, layer: 80, isOpen: () => dialogue.summoningOffer.open }),
    navigation.register({ name: 'legal', root: legal, layer: 70, isOpen: () => !legal.hidden }),
    navigation.register({ name: 'quests', root: quests, layer: 60, isOpen: () => questAnswerRequired && !deadMode }),
    navigation.register({ name: 'dialogue', root: dialogue.window, layer: 50, isOpen: () => dialogue.window.open, back: dialogue.close }),
    navigation.register({ name: 'create-item', root: createItem, layer: 45, isOpen: () => !createItem.hidden }),
    ...choices.cancellable.map(service => navigation.register({ name: service.getAttribute('aria-label')!, root: service, layer: 45,
      isOpen: () => !service.hidden, back: () => service.querySelector<HTMLButtonElement>('[data-escape]:not(:disabled)')?.click() })),
    navigation.register({ name: 'dungeon-text', root: dungeonText.root, layer: 40, isOpen: () => !dungeonText.root.hidden, back: dungeonText.close }),
    navigation.register({ name: 'death', root: death.root, layer: 20, isOpen: () => !death.root.hidden }),
    navigation.register({ name: 'title', root: entry.root, layer: 10, isOpen: () => !entry.root.hidden }),
  ];
  const onCancel = (event: Event): void => event.preventDefault();
  // One named handler, so the listener dispose removes is the listener this mount added.
  const onMenuToggle = (): void => runMenuAction('menu');
  menu.addEventListener('cancel', onCancel);
  menu.addEventListener('click', onMenuClick);
  menuToggle.addEventListener('click', onMenuToggle);
  // The published art arrives inside a snapshot: bytes the session read from admitted content, keyed
  // by the pack's media identity. The DOM holds the last block it saw and asks the product for the
  // revision it is missing, which is what a reload needs and what a republished artifact produces.
  let artRevision = heldRevision();
  let artCooldown = 0;
  let deadMode = false;
  let lastPanelRevision: string | null = null;
  const requestArt = (revision: string): void => context.intents?.claim('dagger.ui', {
    kind: 'product-payload', contract: UI_ACTION_CONTRACT, data: { action: 'art-request', revision },
  });
  const redrawArt = (): void => {
    choices.refreshArt();
    death.refreshArt();
    entry.refreshArt();
    // The panels memo their own state revision, so art that arrived after the state it draws has to
    // ask them to paint again rather than re-send a projection they would ignore.
    inventoryView.refresh();
    characterView.refresh();
    lootView.refresh();
  };
  const unsubscribe = context.projection?.subscribe((projection) => {
    if (projection?.contract !== 'dagger.ui.snapshot.v1' || !isHud(projection.value)) return;
    const value = projection.value;
    choices.update(value);
    const adopted = value.uiArt ? adopt(value.uiArt) : '';
    if (adopted.length > 0 && adopted !== artRevision) {
      artRevision = adopted;
      redrawArt();
    }

    if (value.uiArtRevision && value.uiArtRevision !== artRevision) {
      // Ask again on a slow retry rather than once, because the answer travels the same transport
      // this request does: one snapshot that carried art would have cleared the cooldown instead.
      if (artCooldown === 0) {
        artCooldown = ART_REQUEST_INTERVAL;
        requestArt(value.uiArtRevision);
      } else {
        artCooldown--;
      }
    } else {
      artCooldown = 0;
    }

    const spellbook = isSpellbookProjection(value.spells) ? value.spells : null;
    renderSpells(spellsRoot,spellbook);
    inventorySuppressed = value.character?.identity?.racialOverride?.suppressInventory === true;
    for (const button of shell.querySelectorAll<HTMLButtonElement>('[data-action="inventory"]')) button.disabled = inventorySuppressed;
    if (inventorySuppressed && activePanel === 'inventory') showHome();
    if (value.inventory) inventoryView.update(value.inventory);
    propertyView.update(value.property ?? null, value.inventory);
    transportView.update(isTransportProjection(value.transport) ? value.transport : null, value.inventory);
    mapView.update(isMapProjection(value.map) ? value.map : null);
    travelView.update(isTravelProjection(value.travel) ? value.travel : null);
    if (value.character && isCharacterProjection(value.character)) characterView.update(value.character);
    if (value.notebook) notebookView.update(value.notebook);
    questJournalView.update(value.quests?.journal);
    dungeonText.update(value);
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
    if (!deadMode && panelRequest !== null && panelRequest.revision !== lastPanelRevision) {
      lastPanelRevision = panelRequest.revision;
      runMenuAction(panelRequest.panel);
    }
    currentLoot = value.loot ?? null;
    lootView.update(currentLoot);
    if (currentLoot && currentLoot.container !== lastLootContainer) {
      lastLootContainer = currentLoot.container;
      showPanel('loot');
    } else if (!currentLoot && activePanel === 'loot') showHome();
    vitals.replaceChildren(...value.resources.map((resource) => {
      const row = document.createElement('p');
      const label = document.createElement('span');
      const amount = document.createElement('strong');
      label.textContent = resource.label;
      amount.dataset.resource = resource.id;
      amount.textContent = `${resource.current} / ${resource.maximum}`;
      row.append(label, amount);
      return row;
    }));
    // Death outranks every other mode, so the screen the mode owns replaces the HUD rather than
    // joining it; its semantic choices and effects arrive in the same projection as the image.
    deadMode = value.mode === 'dead';
    if (deadMode && menu.open) closeMenu();
    // The open menu holds the world, so the product reports modal until a requested load replaces the
    // session; a failed load leaves the old session, still modal, and the menu shows why.
    if (loadRequested && menu.open && value.mode === 'playing') closeMenu();
    if (isSaveSlots(value.saveSlots)) saveSlots = value.saveSlots;
    death.update(deadMode, value.death, saveSlots);
    menuToggle.hidden = deadMode;
    entry.update(value);

    title.textContent = value.mode === 'paused' ? 'Paused' : value.mode === 'dead' ? 'Defeated'
      : value.mode === 'title' ? 'Title' : value.mode === 'modal' ? 'Interaction' : 'Exploring';
    actionDrivenTime = value.timeControl?.enabled ?? false;
    const timeToggle = menu.querySelector<HTMLButtonElement>('[data-action="action-driven-time"]')!;
    timeToggle.setAttribute('aria-pressed', String(value.timeControl?.enabled ?? false));
    timeToggle.textContent = `Action-driven time: ${value.timeControl?.enabled ? 'On' : 'Off'}`;
    if (value.mode === 'playing' && value.timeControl?.enabled) title.textContent = `Action-driven · ${value.timeControl.held ? 'Paused' : 'Acting'}`;
    siteName.textContent = value.site?.name ?? '';
    outcome.replaceChildren(...value.messages.map(message => {
      const line = document.createElement('li'); line.textContent = message; return line;
    }));
    breath.hidden = !value.breath;
    if (value.breath) {
      const meter = breath.querySelector('meter')!;
      meter.max = value.breath.maximum; meter.value = value.breath.current;
      meter.setAttribute('aria-label', 'Breath');
      breath.querySelector('span')!.textContent = `${value.breath.current} / ${value.breath.maximum}`;
      breath.classList.toggle('low', value.breath.low);
    }
    const modeLabels: Record<string, string> = { grab: 'Grab', info: 'Information', talk: 'Talk', steal: 'Steal / Lockpick', bash: 'Bash' };
    activationIndicator.textContent = value.activation ? modeLabels[value.activation.mode] ?? value.activation.mode : '';
    activationIndicator.hidden = value.mode !== 'playing' || !value.activation;
    renderQuestMessages(quests, value.quests, (action) => context.intents?.claim('dagger.ui', {
      kind: 'product-payload', contract: UI_ACTION_CONTRACT, data: action,
    }));
    const questMessages = value.quests ? [value.quests.offer, ...value.quests.deliveries,
      ...[...value.quests.journal.active, ...value.quests.journal.finished].flatMap(group => group.entries), value.quests.pending] : [];
    textDiagnostics.replaceChildren(...[
      ...(value.activation?.dialogue?.diagnostics ?? []),
      ...questMessages.flatMap(message => message?.diagnostics ?? []),
      ...(value.spells?.summoning?.diagnostics ?? []),
    ].map(detail => { const item = document.createElement('li'); item.textContent = detail; return item; }));
    const magicIdentity = JSON.stringify([value.inventory?.revision, value.inventory?.items.filter(item => item.canUseMagic)]);
    if (magicIdentity !== magicItemsIdentity) {
      magicItemsIdentity = magicIdentity;
      magicItemsRoot.replaceChildren();
      for (const item of value.inventory?.items ?? []) {
        if (!item.canUseMagic) continue;
        const button = document.createElement('button'); button.type = 'button'; button.textContent = item.label;
        button.addEventListener('click', () => {
          context.intents?.claim('dagger.ui', { kind: 'product-payload', contract: UI_ACTION_CONTRACT,
            data: { action: 'inventory-use', revision: value.inventory!.revision, item: item.key } });
          closeMenu();
        });
        magicItemsRoot.append(button);
      }
      if (!magicItemsRoot.childElementCount) magicItemsRoot.textContent = 'No usable magic items or potions.';
    }
    if (value.controls) controlsView.update(value.controls);
    if (value.activation) activationMode.value = value.activation.mode;
    // The selector lives in the menu, which the product reports as modal, so it serves both.
    activationMode.disabled = value.mode !== 'playing' && value.mode !== 'modal';
    dialogue.update(value, deadMode, spellbook);
    view.textContent = value.view ? viewSummary(value.view) : '';
    detectors.replaceChildren();
    detectors.hidden = !value.detectors?.length;
    for (const source of value.detectors ?? []) {
      const row = document.createElement('p');
      row.dataset.source = source.source;
      row.textContent = `Detect ${source.kind}: ` + (source.contacts.length ? source.contacts.map(contact => {
        const bearing = Math.round(contact.bearingRadians * 180 / Math.PI);
        return `${contact.label}, ${contact.distance.toFixed(1)} m, bearing ${bearing}°`;
      }).join('; ') : 'none nearby');
      detectors.append(row);
    }
    const date = value.calendar ?? null;
    calendar.textContent = date === null ? '' : `${date.date} · ${date.time}`;
    calendar.hidden = date === null;
    const activeEffects = value.effects ?? [];
    effects.hidden = activeEffects.length === 0;
    effectList.replaceChildren(...activeEffects.map(effect => {
      const item = document.createElement('li');
      item.dataset.effect = effect.id;
      const name = document.createElement('strong');
      name.textContent = effect.spell === effect.name ? effect.name : `${effect.spell}: ${effect.name}`;
      const detail = document.createElement('span');
      detail.textContent = effect.detail;
      item.append(name, ' ', detail);
      return item;
    }));
    const cinematic = value.cinematic ?? null;
    cinematicPlaying = cinematic !== null;
    cinematicSkip.hidden = cinematic === null;
    cinematicSkip.textContent = cinematic?.skipLabel ?? 'Skip';
    status.replaceChildren(...(value.slots ?? []).map(row => { const item = document.createElement('p'); item.textContent = `${row.label}: ${row.detail}`; return item; }));
    if (isSaveSlots(value.saveSlots)) redrawSaveSlots();
    // The Engine owns pointer lock; the product tells it whether any current surface needs a cursor.
    questAnswerRequired = Boolean(value.quests?.offer || value.quests?.pending
      || value.quests?.deliveries.some(message => message.delivery === 'popup' || message.delivery === 'letter'));
    interaction.setProjectionNeedsCursor(value.mode === 'title' || value.mode === 'dead' || value.mode === 'paused' || value.mode === 'modal'
      || cinematicPlaying || questAnswerRequired
      || Boolean(value.dungeonText || value.legal || value.createItem || value.identify || value.teleport || value.dispel));
    // A question outlives nothing it was asked over: death, or the menu or dialogue closing, answers no.
    if (deadMode) confirmPrompt.cancel(); else confirmPrompt.dropIfOwnerClosed();
    syncInteraction();
    composition.replaceChildren(...diagnosticRows(value.composition));
  }) ?? (() => {});
  return { dispose: () => {
    reportMap(false);
    tooltips.dispose();
    for (const unregister of surfaces) unregister();
    navigation.dispose();
    interaction.dispose();
    unsubscribe();
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
    document.removeEventListener('keydown', onKeyDown, true);
    document.removeEventListener('keydown', skipOnPress, true);
    document.removeEventListener('pointerdown', skipOnPress, true);
    menu.removeEventListener('cancel', onCancel);
    menu.removeEventListener('click', onMenuClick);
    menuToggle.removeEventListener('click', onMenuToggle);
    saveSlotsSelect.replaceChildren();
    if (menu.open) menu.close();
    disposeShell();
  } };
}
