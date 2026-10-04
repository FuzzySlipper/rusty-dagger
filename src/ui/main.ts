import { mountProperty, type PropertyProjection } from './property.js';
import { mountMap, isMapProjection, type MapProjection } from './maps.js';
import { mountLodging, isLodgingProjection, type LodgingProjection } from './lodging.js';
import { mountControls, type ControlsProjection, type ControlAction } from './controls.js';
import { mountLiveDebugPanel, type LiveDebugPanelMount } from '@rusty-engine/live-debug';
import { adopt, heldRevision, image, type ArtRequestAction, type UiArt } from './art.js';
import { mountInventory, type InventoryProjection, type InventoryAction } from './inventory.js';
import { mountCharacter, isCharacterProjection, type CharacterProjection, type CharacterAction } from './character.js';
import { mountLoot, type LootProjection, type LootAction } from './loot.js';
import { mountNotebook, type NotebookProjection, type NotebookAction } from './notebook.js';
import { BEGIN_ACTION, TITLE_MODE, screenForMode } from './screens.js';
import { mountTravel, isTravelProjection, type TravelProjection } from './travel.js';

import type { RustyApplicationUiContext } from '@rusty-engine/product-ui';

interface UiAction { readonly action: string; readonly [field: string]: string | number | boolean | undefined; }

interface DaggerHud {
  readonly detectors?: readonly { readonly source: string; readonly kind: string; readonly contacts: readonly {
    readonly kind: string; readonly id: string; readonly distance: number; readonly bearingRadians: number;
    readonly items: readonly { readonly id: string; readonly definition: string; readonly quantity: number }[];
  }[] }[];
  readonly spells?: SpellbookProjection | null;
  readonly createItem?: { readonly revision:string; readonly options:readonly { readonly id:string; readonly label:string }[] } | null;
  readonly identify?: { readonly revision:string; readonly cost:number; readonly options:readonly { readonly id:string; readonly label:string }[] } | null;
  readonly teleport?: { readonly revision:string; readonly anchorSet:boolean } | null;
  readonly dispel?: { readonly revision: string; readonly options: readonly { readonly id: string; readonly label: string }[] } | null;
  readonly resources: readonly { readonly id: string; readonly label: string; readonly current: number; readonly maximum: number }[];
  readonly lastOutcome: string;
  readonly mode?: string;
  /** The site the session projects the player at; null before any site owns them. */
  readonly site?: { readonly name: string } | null;
  readonly composition: CompositionIdentity;
  readonly inventory?: InventoryProjection;
  readonly character?: CharacterProjection;
  readonly pickScreens?: readonly string[];
  readonly loot?: LootProjection | null;
  readonly uiArtRevision?: string;
  readonly uiArt?: UiArt | null;
  readonly panelRequest?: PanelRequest | null;
  readonly saveSlots?: SaveSlotProjection;
  readonly controls?: ControlsProjection;
  readonly activation?: { readonly mode: string; readonly message: string; readonly applied: boolean; readonly dialogue?: DialogueProjection | null };
  readonly transport?: TransportProjection | null;
  readonly property?: PropertyProjection | null;
  readonly travel?: TravelProjection | null;
  readonly map?: MapProjection | null;
  readonly quests?: QuestPresentation;
  readonly notebook?: NotebookProjection;
  readonly view?: { readonly yawRadians: number; readonly pitchRadians: number; readonly interaction: string };
  readonly slots?: readonly { readonly owner: string; readonly id: string; readonly label: string; readonly detail: string; readonly order: number }[];
  readonly focus?: { readonly interaction: string; readonly container: string; readonly close: string } | null;
  readonly dungeonText?: { readonly actionId: string; readonly kind: string; readonly text: string; readonly revision: string; readonly requiresAnswer: boolean } | null;
  readonly death?: DeathProjection | null;
  readonly rest?: RestProjection | null;
  readonly lodging?: LodgingProjection | null;
}

interface SpellMakerSetting {
  readonly key:string; readonly type:number; readonly subType:number;
  readonly durationBase:number; readonly durationMod:number; readonly durationPerLevel:number;
  readonly chanceBase:number; readonly chanceMod:number; readonly chancePerLevel:number;
  readonly magnitudeBaseLow:number; readonly magnitudeBaseHigh:number; readonly magnitudeLevelBase:number;
  readonly magnitudeLevelHigh:number; readonly magnitudePerLevel:number;
}
interface SpellMakerDraft { readonly name:string;readonly element:number;readonly rangeType:number;readonly icon:number;readonly effects:readonly SpellMakerSetting[]; }
interface SpellMakerProjection {
  readonly revision:string;readonly provider:string;readonly draft:SpellMakerDraft;
  readonly effects:readonly {readonly key:string;readonly type:number;readonly subType:number;readonly school:string;
    readonly duration:boolean;readonly chance:boolean;readonly magnitude:boolean;readonly targets:number;readonly elements:number}[];
  readonly quote:{readonly key:string;readonly gold:number;readonly spellPoints:number;readonly eligible:boolean;readonly reason:string|null}|null;
}

interface SpellbookProjection {
  readonly available: readonly {readonly key:string;readonly name:string;readonly cost:number;readonly canCast?:boolean}[];
  readonly ready:string|null;readonly result:string;
  readonly sale?: {readonly revision:string;readonly provider:string;readonly offers:readonly {readonly key:string;readonly name:string;readonly castingCost:number;readonly price:number;readonly known:boolean}[]} | null;
  readonly maker?:SpellMakerProjection|null;
  readonly information?: {readonly key:string;readonly name:string;readonly element:number;readonly target:string;readonly details:readonly string[]} | null;
}

interface RestProjection {
  readonly hasResult: boolean;
  readonly revision: string;
  readonly mode: string | null;
  readonly requestedSeconds: number;
  readonly elapsedSeconds: number;
  readonly recoveryHours: number;
  readonly healthRecovered: number;
  readonly fatigueRecovered: number;
  readonly spellPointsRecovered: number;
  readonly interruption: string;
  readonly message: string | null;
}

interface DeathProjection {
  readonly active: boolean;
  readonly screen: string;
  readonly revision: string;
  readonly message: string;
  readonly controlsSuppressed: boolean;
  readonly cameraEffect: string;
  readonly fadeEffect: string;
  readonly audioCue: string;
  readonly choices: readonly { readonly action: string; readonly id: string; readonly label: string; readonly available: boolean }[];
  readonly selected: string | null;
}

interface DialogueProjection {
  readonly questContacts?: readonly { readonly instance: string; readonly symbol: string }[];
  readonly bankAvailable?: boolean;
  readonly merchant?: MerchantProjection | null;
  readonly comprehendLanguagesBonus?: number;
  readonly revision: string;
  readonly targetLabel: string;
  readonly greeting: string;
  readonly tone: string;
  readonly question: string | null;
  readonly reply: string | null;
  readonly topics: readonly { readonly id: string; readonly label: string; readonly key?: string | null }[];
  readonly training?: {
    readonly providerFaction: number;
    readonly membershipFaction: number;
    readonly member: boolean;
    readonly rank: number;
    readonly price: number;
    readonly durationSeconds: number;
    readonly cooldownReadySecond: number;
    readonly skills: readonly { readonly id: string; readonly permanentValue: number; readonly maximumValue: number }[];
  } | null;
  readonly diagnostics: readonly string[];
}

interface MerchantItemProjection {
  readonly key: string;
  readonly definition: string;
  readonly label: string;
  readonly quantity: string | number;
  readonly unitPrice: string | number;
  readonly currentCondition: number;
  readonly maximumCondition: number;
  readonly identified: boolean;
  readonly stolen: boolean;
  readonly canBuy: boolean;
  readonly canSell: boolean;
}

interface MerchantProjection {
  readonly revision: string;
  readonly provider: string;
  readonly quality: number;
  readonly gold: string | number;
  readonly buyAvailable: boolean;
  readonly sellAvailable: boolean;
  readonly repairAvailable: boolean;
  readonly identifyAvailable: boolean;
  readonly result: string;
  readonly stock: readonly MerchantItemProjection[];
  readonly playerItems: readonly MerchantItemProjection[];
  readonly repairs: readonly { readonly requestId: string; readonly durableItemId: string | number; readonly definition: string; readonly dueMinute: number; readonly ready: boolean }[];
}

interface QuestMessageProjection {
  readonly entryId: string | null;
  readonly promptId: string | null;
  readonly options: readonly { readonly id: number; readonly label: string }[];
  readonly instance: string;
  readonly message: number;
  readonly delivery: 'popup' | 'letter' | 'rumor' | 'journal' | 'prompt';
  readonly text: string;
  readonly signoff: string | null;
  readonly diagnostics: readonly string[];
}
interface QuestPresentation {
  readonly escortFaces: readonly { readonly instance: string; readonly symbol: string; readonly name: string; readonly mediaId: string }[];
  readonly deliveries: readonly QuestMessageProjection[];
  readonly journal: readonly QuestMessageProjection[];
  readonly pending: QuestMessageProjection | null;
}

interface SaveSlotProjection {
  readonly entries: readonly { readonly key: string; readonly label: string; readonly savedAtUtc: string; readonly ruleset: string }[];
  readonly diagnostic: string | null;
}

type SaveSlotAction =
  | { readonly action: 'save-slots' }
  | { readonly action: 'save-slot'; readonly key?: string; readonly label: string; readonly confirm?: boolean }
  | { readonly action: 'load-slot'; readonly key: string }
  | { readonly action: 'delete-slot'; readonly key: string; readonly confirm?: boolean };

type TransportMode = 'foot' | 'horse' | 'cart' | 'ship';

interface TransportOptionProjection {
  readonly id: string;
  readonly mode: string;
  readonly available: boolean;
  readonly selected: boolean;
  readonly label: string;
  readonly message: string;
  readonly travelModifier: number;
}

interface TransportItemProjection {
  readonly key: string;
  readonly definition: string;
  readonly quantity: string | number;
}

interface WagonProjection {
  readonly exists: boolean;
  readonly accessible: boolean;
  readonly id: number | null;
  readonly usedClassicUnits: number;
  readonly capacityClassicUnits: number;
  readonly storeRevision: string | number | null;
  readonly message: string;
  readonly items: readonly TransportItemProjection[];
  readonly refusedDefinitions: readonly string[];
}

interface TransportProjection {
  readonly mode: string;
  readonly onShip: boolean;
  readonly canRun: boolean;
  readonly travelModifier: number;
  readonly oceanMinutesPerMapPixel: number;
  readonly options: readonly TransportOptionProjection[];
  readonly wagon: WagonProjection;
}

type TransportAction =
  | { readonly action: 'transport-select'; readonly mode: Exclude<TransportMode, 'ship'> }
  | { readonly action: 'transport-toggle' }
  | { readonly action: 'transport-leave-ship' }
  | { readonly action: 'transport-board-ship' }
  | { readonly action: 'wagon-put'; readonly revision: string; readonly item: string; readonly amount?: number }
  | { readonly action: 'wagon-take'; readonly revision: string; readonly item: string; readonly amount?: number };

/** A panel the player asked for on a device the DOM has no channel of its own for. */
interface PanelRequest {
  readonly panel: string;
  readonly revision: string;
}

interface CompositionIdentity {
  readonly bundle: string;
  readonly ruleset: string;
  readonly contentPacks: readonly string[];
  readonly tuning: string;
}

/** The payload contract every semantic player action travels under. */
const UI_ACTION_CONTRACT = 'dagger.ui.action.v1';
/** Snapshots between repeated requests for art this DOM has not received. */
const ART_REQUEST_INTERVAL = 120;
const MENU_CONTROLLER = Object.freeze({
  accept: 'button-0', back: 'button-1', character: 'button-3',
  inventory: 'button-8', menu: 'button-9', previous: 'button-12', next: 'button-13',
  navigationAxis: 'axis-1', navigationThreshold: 0.55,
});
const MENU_FOCUSABLE = 'button:not(:disabled),select:not(:disabled),input:not(:disabled),a[href],[tabindex="0"]';

export function mountProductUi(root: HTMLElement, context: RustyApplicationUiContext): { dispose(): void } {
  const stylesheet = document.createElement('link');
  stylesheet.rel = 'stylesheet';
  stylesheet.href = new URL('./styles.css', import.meta.url).href;
  const inventoryStylesheet = document.createElement('link');
  inventoryStylesheet.rel = 'stylesheet';
  inventoryStylesheet.href = new URL('./inventory.css', import.meta.url).href;
  const characterStylesheet = document.createElement('link');
  characterStylesheet.rel = 'stylesheet';
  characterStylesheet.href = new URL('./character.css', import.meta.url).href;
  const lootStylesheet = document.createElement('link');
  lootStylesheet.rel = 'stylesheet';
  lootStylesheet.href = new URL('./loot.css', import.meta.url).href;
  document.head.append(stylesheet, inventoryStylesheet, characterStylesheet, lootStylesheet);

  const shell = document.createElement('section');
  shell.className = 'dagger-hud';
  shell.innerHTML = `
    <div class="dagger-title"><span class="dagger-site"></span><strong>Exploring</strong></div>
    <div class="dagger-reticle" aria-hidden="true">+</div>
    <section class="dagger-vitals" aria-live="polite">
    </section>
    <p class="dagger-outcome" role="status">Awaiting projection…</p>
    <section class="dagger-quests" aria-live="polite"></section>
    <p class="dagger-view" aria-live="polite"></p><section class="dagger-detectors" aria-label="Detected nearby objects"></section><section class="dagger-status"></section><button class="dagger-focus-close" hidden></button>
    <div class="dagger-death" role="alertdialog" aria-labelledby="dagger-death-title" aria-describedby="dagger-death-message" hidden>
      <img class="dagger-death-screen" alt="You have died.">
      <div class="dagger-death-fade" aria-hidden="true"></div>
      <section class="dagger-death-panel">
        <h1 id="dagger-death-title">You have died.</h1>
        <p class="dagger-death-message" id="dagger-death-message"></p>
        <label class="dagger-death-load">Load saved game <select class="dagger-death-load-slot"><option value="">Choose a saved game</option></select></label>
        <div class="dagger-death-actions">
          <button class="dagger-death-new" type="button">New game</button>
          <button class="dagger-death-load-button" type="button">Load game</button>
          <button class="dagger-death-quit" type="button">Quit to title</button>
        </div>
      </section>
    </div>
    <section class="dagger-dungeon-text" role="dialog" aria-label="Dungeon text" hidden>
      <p class="dagger-dungeon-text-body"></p>
      <form class="dagger-dungeon-text-answer" hidden><label>Answer <input maxlength="256" autocomplete="off"></label><button type="submit">Answer</button></form>
      <button class="dagger-dungeon-text-close" type="button">Continue</button>
    </section>
    <div class="dagger-entry" role="dialog" aria-label="Title" hidden><img class="dagger-entry-screen" alt="Rusty Dagger"><button class="dagger-entry-begin" type="button">Begin</button></div>
    <button class="dagger-menu-toggle" type="button" data-action="menu" aria-haspopup="dialog">Menu · Esc</button>
    <dialog class="dagger-menu" aria-labelledby="dagger-menu-title">
      <h1 id="dagger-menu-title" tabindex="-1">Game menu</h1>
      <div class="dagger-menu-home">
        <button data-action="resume" autofocus>Return to game</button>
        <button data-action="inventory">Inventory &amp; equipment · I</button>
        <button data-action="character">Character · C</button>
        <button data-action="spells">Spells</button>
        <button data-action="map">Map</button>
        <button data-action="transport">Travel &amp; transport</button>
        <button data-action="rest">Rest &amp; loiter</button>
        <button data-action="journal">Journal &amp; notes</button>
        <button data-action="loot">Activate aimed target · F</button>
        <label>Activation mode <select class="dagger-activation-mode"><option value="grab">Grab</option><option value="info">Information</option><option value="talk">Talk</option><option value="steal">Steal / Lockpick</option><option value="bash">Bash</option></select></label>
        <button data-action="save-game">Save game</button>
        <button data-action="load-game">Load game</button>
        <button data-action="debug">Engine debug console</button>
        <button data-action="diagnostics">Composition diagnostics</button>
        <button data-action="settings">Control settings</button>
        <p>The world continues while this menu is open.</p>
      </div>
      <div class="dagger-menu-panel" hidden>
      <section class="dagger-composition" aria-label="Resolved composition diagnostics">
      <strong>Resolved composition</strong>
      <dl></dl>
    </section>
      <div class="dagger-controls-root" hidden></div>
      <div class="dagger-inventory-root" hidden></div>
      <div class="dagger-character-root" hidden></div>
      <section class="dagger-spells-root" hidden aria-label="Known spells"></section>
      <div class="dagger-map-root" hidden></div>
      <div class="dagger-transport-root" hidden></div>
      <section class="dagger-rest-root" hidden aria-label="Rest and loiter">
        <h2>Rest &amp; loiter</h2>
        <p class="dagger-rest-status" role="status" aria-live="polite">Choose a rest action.</p>
        <label>Hours <input class="dagger-rest-hours" type="number" min="0" max="99" step="1" value="1"></label>
        <div class="dagger-rest-actions">
          <button type="button" data-action="rest" data-rest-mode="timed">Rest for hours</button>
          <button type="button" data-action="rest" data-rest-mode="until-healed">Rest until healed</button>
          <button type="button" data-action="rest" data-rest-mode="loiter">Loiter</button>
        </div>
      </section>
      <div class="dagger-notebook-root" hidden></div>
      <div class="dagger-loot-root" hidden></div>
      <section class="dagger-save-slots" hidden aria-label="Save slots">
        <p class="dagger-save-slots-diagnostic" role="status"></p>
        <label>Save name <input class="dagger-save-slots-label" maxlength="120" autocomplete="off"></label>
        <label>Selected slot <select class="dagger-save-slots-select"><option value="">New slot</option></select></label>
        <div class="dagger-save-slots-actions">
          <button data-action="save-slot">Save</button>
          <button data-action="load-slot">Load selected</button>
          <button data-action="delete-slot">Delete selected</button>
        </div>
      </section>
      <div class="dagger-debug-root" data-rusty-ui-interactive hidden></div>
      <button data-action="loot-exit" hidden>Exit loot</button>
      <button data-action="back">Back to menu</button>
      </div>
    </dialog>
    <dialog class="dagger-dialogue" aria-labelledby="dagger-dialogue-title">
      <h2 id="dagger-dialogue-title" class="dagger-dialogue-target"></h2>
      <p class="dagger-dialogue-greeting" aria-live="polite"></p>
      <label>Tone <select class="dagger-dialogue-tone"><option value="polite">Polite</option><option value="normal">Normal</option><option value="blunt">Blunt</option></select></label>
      <label class="dagger-dialogue-donation" hidden>Donation gold <input class="dagger-dialogue-donation-amount" type="number" min="1" step="1" value="1" inputmode="numeric"></label>
      <p class="dagger-dialogue-question" aria-live="polite"></p>
      <p class="dagger-dialogue-reply" aria-live="polite"></p>
      <div class="dagger-dialogue-topics"></div>
      <section class="dagger-dialogue-training" aria-label="Skill training" hidden>
        <h3>Skill training</h3>
        <p class="dagger-dialogue-training-summary"></p>
        <div class="dagger-dialogue-training-skills"></div>
      </section>
      <section class="dagger-dialogue-merchant" aria-label="Merchant services"></section>
      <section class="dagger-dialogue-spells" aria-label="Spells for sale"></section>
      <section class="dagger-dialogue-spellmaker" aria-label="Spell construction"></section>
      <ul class="dagger-dialogue-diagnostics" aria-label="Text diagnostics"></ul>
      <button class="dagger-dialogue-close" type="button">End conversation</button>
    </dialog>`;
  root.append(shell);

  const activationMode = shell.querySelector<HTMLSelectElement>('.dagger-activation-mode')!;
  activationMode.addEventListener('change', () => context.intents?.claim('dagger.ui', {
    kind: 'product-payload', contract: UI_ACTION_CONTRACT, data: { action: 'activation-mode', mode: activationMode.value },
  }));
  const title = shell.querySelector<HTMLElement>('.dagger-title strong')!;
  const siteName = shell.querySelector<HTMLElement>('.dagger-title .dagger-site')!;
  const outcome = shell.querySelector<HTMLParagraphElement>('.dagger-outcome')!;
  const quests = shell.querySelector<HTMLElement>('.dagger-quests')!;
  const createItem = document.createElement('section');
  createItem.className = 'dagger-create-item'; createItem.hidden = true; quests.before(createItem);
  const identify = document.createElement('section');
  identify.className = 'dagger-identify'; identify.hidden = true; quests.before(identify);
  const teleport = document.createElement('section');
  teleport.className = 'dagger-teleport'; teleport.hidden = true; quests.before(teleport);
  const dispel = document.createElement('section');
  dispel.className = 'dagger-dispel';
  dispel.hidden = true;
  quests.before(dispel);
  const view = shell.querySelector<HTMLParagraphElement>('.dagger-view')!;
  const detectors = shell.querySelector<HTMLElement>('.dagger-detectors')!;
  const status = shell.querySelector<HTMLElement>('.dagger-status')!;
  const focusClose = shell.querySelector<HTMLButtonElement>('.dagger-focus-close')!;
  focusClose.addEventListener('click', () => { if (focusClose.dataset.container && focusClose.dataset.close) context.intents?.claim('dagger.ui', { kind: 'product-payload', contract: UI_ACTION_CONTRACT, data: { action: focusClose.dataset.close, container: focusClose.dataset.container } }); });
  const vitals = shell.querySelector<HTMLElement>('.dagger-vitals')!;
  const composition = shell.querySelector<HTMLDListElement>('.dagger-composition dl')!;
  const claim = (action: string): void => context.intents?.claim('dagger.ui', {
    kind: 'product-payload', contract: UI_ACTION_CONTRACT, data: { action },
  });
  const deathRoot = shell.querySelector<HTMLElement>('.dagger-death')!;
  const deathScreen = shell.querySelector<HTMLImageElement>('.dagger-death-screen')!;
  const deathMessage = shell.querySelector<HTMLElement>('.dagger-death-message')!;
  const deathLoadSlot = shell.querySelector<HTMLSelectElement>('.dagger-death-load-slot')!;
  const deathNew = shell.querySelector<HTMLButtonElement>('.dagger-death-new')!;
  const deathLoad = shell.querySelector<HTMLButtonElement>('.dagger-death-load-button')!;
  const deathQuit = shell.querySelector<HTMLButtonElement>('.dagger-death-quit')!;
  const dungeonTextRoot = shell.querySelector<HTMLElement>('.dagger-dungeon-text')!;
  const dungeonTextBody = shell.querySelector<HTMLElement>('.dagger-dungeon-text-body')!;
  const dungeonTextForm = shell.querySelector<HTMLFormElement>('.dagger-dungeon-text-answer')!;
  const dungeonTextAnswer = dungeonTextForm.querySelector<HTMLInputElement>('input')!;
  const dungeonTextClose = shell.querySelector<HTMLButtonElement>('.dagger-dungeon-text-close')!;
  let currentDungeonText: NonNullable<DaggerHud['dungeonText']> | null = null;
  dungeonTextForm.addEventListener('submit', event => {
    event.preventDefault();
    if (!currentDungeonText?.requiresAnswer) return;
    context.intents?.claim('dagger.ui', { kind: 'product-payload', contract: UI_ACTION_CONTRACT, data: {
      action: 'dungeon-text-answer', revision: currentDungeonText.revision,
      item: currentDungeonText.actionId, text: dungeonTextAnswer.value,
    } });
  });
  dungeonTextClose.addEventListener('click', () => {
    if (!currentDungeonText) return;
    context.intents?.claim('dagger.ui', { kind: 'product-payload', contract: UI_ACTION_CONTRACT, data: {
      action: 'dungeon-text-close', revision: currentDungeonText.revision, item: currentDungeonText.actionId,
    } });
  });
  const entryRoot = shell.querySelector<HTMLElement>('.dagger-entry')!;
  const entryScreen = shell.querySelector<HTMLImageElement>('.dagger-entry-screen')!;
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
  const submitRest = (mode: string): void => {
    if (mode === 'until-healed') {
      context.intents?.claim('dagger.ui', {
        kind: 'product-payload', contract: UI_ACTION_CONTRACT, data: { action: 'rest', mode },
      });
      return;
    }
    const rawHours = restHours.value.trim();
    const hours = Number(rawHours);
    if (rawHours.length === 0 || !Number.isSafeInteger(hours) || hours < 0) {
      restStatus.textContent = 'Enter a whole number of hours.';
      restHours.focus();
      return;
    }
    context.intents?.claim('dagger.ui', {
      kind: 'product-payload', contract: UI_ACTION_CONTRACT, data: { action: 'rest', mode, hours },
    });
  };
  const controlsRoot = shell.querySelector<HTMLElement>('.dagger-controls-root')!;
  const controlsView = mountControls(controlsRoot, action => context.intents?.claim('dagger.ui', {
    kind: 'product-payload', contract: UI_ACTION_CONTRACT, data: action,
  }));
  const characterRoot = shell.querySelector<HTMLElement>('.dagger-character-root')!;
  const characterView = mountCharacter(characterRoot, action => context.intents?.claim('dagger.ui', {
    kind: 'product-payload', contract: UI_ACTION_CONTRACT, data: action,
  }));
  const creationElement = characterView.creationElement;
  const creationHome = creationElement.parentElement!;
  const entryParts = document.createElement('div'); entryParts.className = 'dagger-entry-parts'; entryRoot.append(entryParts);
  const entryCreate = document.createElement('button'); entryCreate.type = 'button'; entryCreate.textContent = 'Create character';
  entryCreate.className = 'dagger-entry-create'; entryCreate.dataset.testid = 'entry-create-character'; entryRoot.append(entryCreate);
  entryCreate.addEventListener('click', () => context.intents?.claim('dagger.ui', {
    kind: 'product-payload', contract: UI_ACTION_CONTRACT, data: { action: 'character-begin' },
  }));
  const notebookRoot = shell.querySelector<HTMLElement>('.dagger-notebook-root')!;
  const notebookView = mountNotebook(notebookRoot, action => context.intents?.claim('dagger.ui', {
    kind: 'product-payload', contract: UI_ACTION_CONTRACT, data: action,
  }));
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
  const claimDeath = (action: string, key?: string): void => context.intents?.claim('dagger.ui', {
    kind: 'product-payload', contract: UI_ACTION_CONTRACT, data: key ? { action, key } : { action },
  });
  deathNew.addEventListener('click', () => claimDeath('death-new-game'));
  deathLoad.addEventListener('click', () => {
    if (deathLoadSlot.value) claimDeath('death-load-game', deathLoadSlot.value);
  });
  deathQuit.addEventListener('click', () => claimDeath('death-quit'));
  deathLoadSlot.addEventListener('change', () => {
    deathLoad.disabled = deathLoadSlot.value.length === 0;
  });
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
  const dialogueWindow = shell.querySelector<HTMLDialogElement>('.dagger-dialogue')!;
  const dialogueTarget = shell.querySelector<HTMLElement>('.dagger-dialogue-target')!;
  const dialogueGreeting = shell.querySelector<HTMLElement>('.dagger-dialogue-greeting')!;
  const dialogueTone = shell.querySelector<HTMLSelectElement>('.dagger-dialogue-tone')!;
  const dialogueDonation = shell.querySelector<HTMLLabelElement>('.dagger-dialogue-donation')!;
  const dialogueDonationAmount = shell.querySelector<HTMLInputElement>('.dagger-dialogue-donation-amount')!;
  const dialogueQuestion = shell.querySelector<HTMLElement>('.dagger-dialogue-question')!;
  const dialogueReply = shell.querySelector<HTMLElement>('.dagger-dialogue-reply')!;
  const dialogueTopics = shell.querySelector<HTMLElement>('.dagger-dialogue-topics')!;
  const dialogueTraining = shell.querySelector<HTMLElement>('.dagger-dialogue-training')!;
  const dialogueTrainingSummary = shell.querySelector<HTMLElement>('.dagger-dialogue-training-summary')!;
  const dialogueTrainingSkills = shell.querySelector<HTMLElement>('.dagger-dialogue-training-skills')!;
  const dialogueMerchant = shell.querySelector<HTMLElement>('.dagger-dialogue-merchant')!;
  const dialogueDiagnostics = shell.querySelector<HTMLElement>('.dagger-dialogue-diagnostics')!;
  let currentDialogue: DialogueProjection | null = null;
  const merchantAmount = (item: MerchantItemProjection): number | undefined => {
    if (!item.key.startsWith('stack:')) return undefined;
    const amount = typeof item.quantity === 'number' ? item.quantity : Number(item.quantity);
    return Number.isSafeInteger(amount) && amount > 0 ? amount : undefined;
  };
  const merchantQuantityInput = (item: MerchantItemProjection): HTMLInputElement | undefined => {
    if (!item.key.startsWith('stack:')) return undefined;
    const available = merchantAmount(item);
    if (available === undefined) return undefined;
    const input = document.createElement('input');
    input.type = 'number';
    input.min = '1';
    input.max = String(available);
    input.value = '1';
    input.step = '1';
    input.inputMode = 'numeric';
    input.className = 'dagger-merchant-quantity';
    input.setAttribute('aria-label', `Quantity of ${item.label}`);
    return input;
  };
  const merchantAction = (action: string, merchant: MerchantProjection, item: MerchantItemProjection, label: string,
    amountInput?: HTMLInputElement): HTMLButtonElement => {
    const button = document.createElement('button');
    button.type = 'button';
    button.textContent = label;
    button.addEventListener('click', () => {
      const data = amountInput === undefined
        ? { action, revision: merchant.revision, item: item.key }
        : (() => {
          const available = merchantAmount(item);
          const amount = Number(amountInput.value);
          if (available === undefined || !Number.isSafeInteger(amount) || amount < 1 || amount > available) {
            amountInput.focus();
            return undefined;
          }
          return { action, revision: merchant.revision, item: item.key, amount };
        })();
      if (data === undefined) return;
      context.intents?.claim('dagger.ui', { kind: 'product-payload', contract: UI_ACTION_CONTRACT, data });
    });
    return button;
  };
  const renderMerchant = (merchant: MerchantProjection | null): void => {
    dialogueMerchant.hidden = merchant === null;
    dialogueMerchant.replaceChildren();
    if (merchant === null) return;
    const heading = document.createElement('h3');
    heading.textContent = `${merchant.provider} · shop quality ${merchant.quality}`;
    const gold = document.createElement('p');
    gold.textContent = `Carried gold: ${merchant.gold}`;
    dialogueMerchant.append(heading, gold);
    const addItems = (title: string, items: readonly MerchantItemProjection[], action: string, predicate: (item: MerchantItemProjection) => boolean): void => {
      const section = document.createElement('section');
      const sectionHeading = document.createElement('h4'); sectionHeading.textContent = title;
      const list = document.createElement('ul');
      for (const item of items) {
        const row = document.createElement('li');
        row.textContent = `${item.label} × ${item.quantity} · ${item.unitPrice} gold${item.stolen ? ' · stolen' : ''}`;
        const amountInput = merchantQuantityInput(item);
        if (amountInput && predicate(item)) row.append(' ', amountInput);
        if (predicate(item)) row.append(' ', merchantAction(action, merchant, item, action === 'merchant-buy' ? 'Buy' : 'Sell', amountInput));
        if (action === 'merchant-buy' && predicate(item)) row.append(' ', merchantAction('merchant-shoplift', merchant, item, 'Steal', amountInput));
        if (action === 'merchant-sell' && merchant.repairAvailable && item.key.startsWith('unique:') && item.maximumCondition > item.currentCondition)
          row.append(' ', merchantAction('merchant-repair', merchant, item, 'Repair'));
        if (action === 'merchant-sell' && merchant.identifyAvailable && item.key.startsWith('unique:') && !item.identified)
          row.append(' ', merchantAction('merchant-identify', merchant, item, 'Identify'));
        list.append(row);
      }
      if (items.length === 0) { const empty = document.createElement('li'); empty.textContent = 'None.'; list.append(empty); }
      section.append(sectionHeading, list); dialogueMerchant.append(section);
    };
    if (merchant.buyAvailable) addItems('Stock', merchant.stock, 'merchant-buy', item => item.canBuy);
    if (merchant.sellAvailable || merchant.repairAvailable || merchant.identifyAvailable)
      addItems('Your items', merchant.playerItems, 'merchant-sell', item => merchant.sellAvailable && item.canSell);
    if (merchant.repairAvailable && merchant.repairs.length > 0) {
      const section = document.createElement('section');
      const sectionHeading = document.createElement('h4'); sectionHeading.textContent = 'Repairs';
      const list = document.createElement('ul');
      for (const repair of merchant.repairs) {
        const row = document.createElement('li'); row.textContent = `${repair.definition} · ${repair.ready ? 'ready' : 'in progress'}`;
        if (repair.ready) {
          const button = document.createElement('button'); button.type = 'button'; button.textContent = 'Collect';
          button.addEventListener('click', () => context.intents?.claim('dagger.ui', { kind: 'product-payload', contract: UI_ACTION_CONTRACT,
            data: { action: 'merchant-collect-repair', revision: merchant.revision, key: repair.requestId } }));
          row.append(' ', button);
        }
        list.append(row);
      }
      section.append(sectionHeading, list); dialogueMerchant.append(section);
    }
  };
  dialogueTone.addEventListener('change', () => {
    if (!deadMode && currentDialogue) context.intents?.claim('dagger.ui', {
      kind: 'product-payload', contract: UI_ACTION_CONTRACT,
      data: { action: 'dialogue-tone', revision: currentDialogue.revision, tone: dialogueTone.value },
    });
  });
  dialogueTopics.addEventListener('click', event => {
    const button = (event.target as HTMLElement).closest<HTMLButtonElement>('button[data-topic]');
    if (deadMode || !button?.dataset.topic || !currentDialogue) return;
    const data: Record<string, unknown> = {
      action: 'dialogue-topic', revision: currentDialogue.revision, topic: button.dataset.topic,
    };
    if (button.dataset.topic === 'donate') {
      const amount = Number(dialogueDonationAmount.value);
      if (!Number.isSafeInteger(amount) || amount <= 0) {
        dialogueDonationAmount.focus();
        return;
      }
      data.amount = amount;
    }
    if (button.dataset.key) data.key = button.dataset.key;
    context.intents?.claim('dagger.ui', {
      kind: 'product-payload', contract: UI_ACTION_CONTRACT, data,
    });
  });
  dialogueTrainingSkills.addEventListener('click', event => {
    const button = (event.target as HTMLElement).closest<HTMLButtonElement>('button[data-training-skill]');
    const training = currentDialogue?.training;
    if (!deadMode && button?.dataset.trainingSkill && training && currentDialogue) context.intents?.claim('dagger.ui', {
      kind: 'product-payload', contract: UI_ACTION_CONTRACT,
      data: { action: 'training-commit', revision: currentDialogue.revision, key: button.dataset.trainingSkill,
        amount: training.price, confirm: true },
    });
  });
  shell.querySelector<HTMLButtonElement>('.dagger-dialogue-close')!.addEventListener('click', () => {
    if (!deadMode && currentDialogue) context.intents?.claim('dagger.ui', {
      kind: 'product-payload', contract: UI_ACTION_CONTRACT,
      data: { action: 'dialogue-close', revision: currentDialogue.revision },
    });
  });
  dialogueWindow.addEventListener('cancel', event => {
    event.preventDefault();
    if (!deadMode && currentDialogue) context.intents?.claim('dagger.ui', {
      kind: 'product-payload', contract: UI_ACTION_CONTRACT,
      data: { action: 'dialogue-close', revision: currentDialogue.revision },
    });
  });
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
  const spellSalesRoot=shell.querySelector<HTMLElement>('.dagger-dialogue-spells')!;
  const spellMakerRoot=shell.querySelector<HTMLElement>('.dagger-dialogue-spellmaker')!;
  let activePanel: 'spells' | 'diagnostics' | 'inventory' | 'character' | 'map' | 'transport' | 'rest' | 'journal' | 'loot' | 'debug' | 'save-slots' | 'settings' | null = null;
  const showHome = (): void => {
    reportMap(false);
    controlsView.cancel();
    const previous = activePanel;
    if (previous === 'debug') closeDebug();
    if (previous === 'loot') closeLoot();
    activePanel = null;
    menu.classList.remove('has-inventory', 'has-character', 'has-transport', 'has-rest', 'has-journal', 'has-loot', 'has-debug');
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
    controlsView.cancel();
    controllerDirection = 0;
    if (activePanel === 'loot') closeLoot();
    closeDebug();
    activePanel = null;
    loadRequested = false;
    menu.close();
    reportMenu(false);
    context.ui.setInteractionMode(titleMode || deadMode ? 'interface' : 'gameplay');
    if (!titleMode && !deadMode) context.ui.focusGameplay();
  };
  const openMenu = (): void => {
    controllerDirection = 0;
    context.ui.setInteractionMode('interface');
    loadRequested = false;
    menu.showModal();
    reportMenu(true);
    showHome();
  };
  const dismiss = (): void => {
    if (deadMode) return;
    if (activePanel !== null) showHome();
    else if (menu.open) closeMenu();
    else openMenu();
  };
  const showPanel = (action: 'spells' | 'diagnostics' | 'inventory' | 'character' | 'map' | 'transport' | 'rest' | 'journal' | 'loot' | 'debug' | 'save-slots' | 'settings'): void => {
    if (!menu.open) openMenu();
    if (activePanel === 'loot' && action !== 'loot') closeLoot();
    if (activePanel === 'debug') closeDebug();
    reportMap(action === 'map');
    activePanel = action;
    home.hidden = true;
    panel.hidden = false;
    diagnostics.hidden = action !== 'diagnostics';
    inventoryRoot.hidden = action !== 'inventory';
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
    menu.classList.toggle('has-transport', action === 'transport');
    menu.classList.toggle('has-rest', action === 'rest');
    menu.classList.toggle('has-journal', action === 'journal');
    menu.classList.toggle('has-loot', action === 'loot');
    menu.classList.toggle('has-debug', action === 'debug');
    menuTitle.textContent = action === 'spells' ? 'Known spells' : action === 'settings' ? 'Control settings' : action === 'diagnostics' ? 'Composition diagnostics'
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
  const runMenuAction = (action: string | undefined): void => {
    if (action === 'resume' || action === 'loot-exit') closeMenu();
    else if (action === 'back') showHome();
    else if (action === 'menu') dismiss();
    else if (action === 'loot') claim('loot');
    else if (action === 'save-game') showSaveSlots('save');
    else if (action === 'load-game') showSaveSlots('load');
    else if (action === 'spells' || action === 'settings' || action === 'diagnostics' || action === 'inventory' || action === 'character' || action === 'map' || action === 'transport' || action === 'rest' || action === 'journal' || action === 'debug') showPanel(action);
  };
  const onMenuClick = (event: MouseEvent): void => {
    const button = (event.target as HTMLElement).closest<HTMLButtonElement>('button');
    if(button?.disabled) return;
    const action = button?.dataset.action;
    if ((action==='spell-buy' || action==='spell-delete' || action==='spell-info') && button?.dataset.spell) {
      if (action !== 'spell-info' && !window.confirm(action==='spell-buy' ? `Buy this spell for ${button.dataset.price} gold?` : 'Forget this spell?')) return;
      const data = action==='spell-buy'
        ? {action,key:button.dataset.spell,revision:button.dataset.revision,amount:Number(button.dataset.price),confirm:true}
        : action==='spell-delete' ? {action,key:button.dataset.spell,confirm:true} : {action,key:button.dataset.spell};
      context.intents?.claim('dagger.ui',{kind:'product-payload',contract:UI_ACTION_CONTRACT,data});return;
    }
    if (action==='spell-ready' && button?.dataset.spell) {
      context.intents?.claim('dagger.ui',{kind:'product-payload',contract:UI_ACTION_CONTRACT,data:{action,key:button.dataset.spell}});
      return;
    }
    if (action==='spell-unready' || action==='spell-cast') {
      if(action==='spell-cast') closeMenu();
      claim(action); return;
    }
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
  spellSalesRoot.addEventListener('click', onMenuClick);
  saveSlotsSelect.addEventListener('change', () => {
    saveConfirm = false;
    deleteConfirm = false;
    const selected = saveSlots.entries.find(entry => entry.key === saveSlotsSelect.value);
    if (selected) saveSlotsLabel.value = selected.label;
    redrawSaveSlots();
  });
  // Capture before Engine input sees navigation keys. Escape's native dialog
  // cancellation is suppressed so one physical press performs exactly one step.
  const onKeyDown = (event: KeyboardEvent): void => {
    if (activePanel === 'settings' && controlsView.captureKey(event)) return;
    shell.classList.remove('controller-navigation');
    if (menu.open && event.code === 'Tab') {
      const buttons = Array.from(menu.querySelectorAll<HTMLElement>('button:not(:disabled),select:not(:disabled),input:not(:disabled),[tabindex="0"]'))
        .filter((button) => button.getClientRects().length > 0);
      const current = buttons.indexOf(document.activeElement as HTMLElement);
      if (current < 0 || (!event.shiftKey && current === buttons.length - 1)
        || (event.shiftKey && current === 0)) {
        event.preventDefault();
        event.stopPropagation();
        buttons[event.shiftKey ? buttons.length - 1 : 0]?.focus();
      }
      return;
    }
    if (event.code === 'Escape') {
      event.preventDefault();
      event.stopPropagation();
      if (!event.repeat && !deadMode) dismiss();
      return;
    }
    if (menu.open || event.ctrlKey || event.altKey || event.metaKey
      || (event.target instanceof Element && event.target.closest('input,textarea,select,[contenteditable="true"]'))) return;

  };
  // The Engine owns controller sampling and interface/gameplay arbitration.
  // This adapter assigns only DOM menu meaning and reuses click/menu handlers.
  let controllerDirection = 0;
  const controllerScope = (): HTMLElement | null => menu.open ? menu : !deathRoot.hidden ? deathRoot : !entryRoot.hidden ? entryRoot : null;
  const focusControllerItem = (direction: number): void => {
    const scope = controllerScope();
    if (scope === null) return;
    const items = Array.from(scope.querySelectorAll<HTMLElement>(MENU_FOCUSABLE))
      .filter(item => item.getClientRects().length > 0);
    if (items.length === 0) return;
    const current = items.indexOf(document.activeElement as HTMLElement);
    const next = current < 0 ? (direction < 0 ? items.length - 1 : 0)
      : (current + direction + items.length) % items.length;
    items[next]?.focus();
  };
  const unsubscribeController = context.input?.subscribe(({ fact }) => {
    shell.classList.add('controller-navigation');
    if (fact.kind === 'controller-axis' && fact.axis === MENU_CONTROLLER.navigationAxis) {
      const direction = Math.abs(fact.value) < MENU_CONTROLLER.navigationThreshold ? 0 : Math.sign(fact.value);
      if (direction !== 0 && direction !== controllerDirection) focusControllerItem(direction);
      controllerDirection = direction;
      return;
    }
    if (fact.kind !== 'controller-button' || fact.edge !== 'pressed') return;
    if (fact.button === MENU_CONTROLLER.previous) focusControllerItem(-1);
    else if (fact.button === MENU_CONTROLLER.next) focusControllerItem(1);
    else if (fact.button === MENU_CONTROLLER.accept) {
      const scope = controllerScope();
      if (scope === null) return;
      let active = document.activeElement as HTMLElement | null;
      if (active === null || !scope.contains(active) || !active.matches(MENU_FOCUSABLE)) {
        focusControllerItem(1);
        active = document.activeElement as HTMLElement | null;
      }
      if (active !== null && scope.contains(active)) active.click();
    } else if (menu.open) {
      if (fact.button === MENU_CONTROLLER.back || fact.button === MENU_CONTROLLER.menu) dismiss();
      else if (fact.button === MENU_CONTROLLER.inventory) runMenuAction('inventory');
      else if (fact.button === MENU_CONTROLLER.character) runMenuAction('character');
    }
  });
  const onCancel = (event: Event): void => event.preventDefault();
  // One named handler, so the listener dispose removes is the listener this mount added.
  const onMenuToggle = (): void => runMenuAction('menu');
  menu.addEventListener('cancel', onCancel);
  menu.addEventListener('click', onMenuClick);
  menuToggle.addEventListener('click', onMenuToggle);
  document.addEventListener('keydown', onKeyDown, true);
  // The published art arrives inside a snapshot: bytes the session read from admitted content, keyed
  // by the pack's media identity. The DOM holds the last block it saw and asks the product for the
  // revision it is missing, which is what a reload needs and what a republished artifact produces.
  let artRevision = heldRevision();
  let artCooldown = 0;
  let deadMode = false;
  let titleMode = false;
  let creationMode: string | null = null;
  let pickScreens: readonly string[] = [];
  let currentDeath: DeathProjection | null = null;
  // The entry screen is the mode's own screen: the mode shows the published artifact, and the one thing
  // the screen does is ask the product to begin. The product decides, so the answer is the mode changing
  // rather than this hiding itself.
  const redrawEntry = (): void => {
    const wasVisible = !entryRoot.hidden;
    entryRoot.hidden = !titleMode;
    if (wasVisible !== titleMode && !menu.open) {
      controllerDirection = 0;
      context.ui.setInteractionMode(titleMode ? 'interface' : 'gameplay');
      if (!titleMode) context.ui.focusGameplay();
    }
    if (!titleMode) {
      if (!deadMode && !menu.open) context.ui.setInteractionMode('gameplay');
      return;
    }
    context.ui.setInteractionMode('interface');
    entryRoot.dataset.mode = creationMode ?? TITLE_MODE;
    const creating = creationMode !== null;
    if (creating && menu.open) menu.close();
    (creating ? entryRoot : creationHome).append(creationElement);
    creationElement.classList.toggle('dagger-entry-creation', creating);
    shell.querySelector<HTMLButtonElement>('.dagger-entry-begin')!.hidden = creating;
    entryCreate.hidden = creating;
    const primary = screenForMode(creationMode ?? TITLE_MODE)!;
    const entry = image(primary);
    if (entry !== null) entryScreen.src = entry;
    entryParts.replaceChildren(...(creationMode === 'character-pick' ? pickScreens.filter(id => id !== primary) : []).flatMap(id => {
      const source = image(id); if (source === null) return [];
      const part = document.createElement('img'); part.src = source; part.alt = ''; part.dataset.mediaId = id; return [part];
    }));
  };
  shell.querySelector<HTMLButtonElement>('.dagger-entry-begin')!.addEventListener('click', () => {
    context.intents?.claim('dagger.ui', {
      kind: 'product-payload', contract: UI_ACTION_CONTRACT, data: { action: BEGIN_ACTION },
    });
  });
  let lastPanelRevision: string | null = null;
  const requestArt = (revision: string): void => context.intents?.claim('dagger.ui', {
    kind: 'product-payload', contract: UI_ACTION_CONTRACT, data: { action: 'art-request', revision },
  });
  const redrawDeath = (death: DeathProjection | null): void => {
    deathRoot.hidden = !deadMode;
    menuToggle.hidden = deadMode;
    if (!deadMode) {
      if (!titleMode && !menu.open) context.ui.setInteractionMode('gameplay');
      return;
    }
    context.ui.setInteractionMode('interface');
    deathMessage.textContent = death?.message ?? 'You have died.';
    deathRoot.dataset.cameraEffect = death?.cameraEffect ?? 'fall';
    deathRoot.dataset.fadeEffect = death?.fadeEffect ?? 'to-black';
    deathRoot.dataset.audioCue = death?.audioCue ?? 'player-death';
    // The ruleset owns which choices death offers; a snapshot without them offers none.
    const choices = death?.choices ?? [];
    const findChoice = (action: string) => choices.find(choice => choice.action === action);
    const selected = death?.selected ?? null;
    const newChoice = findChoice('death-new-game');
    const loadChoice = findChoice('death-load-game');
    const quitChoice = findChoice('death-quit');
    deathNew.textContent = newChoice?.label ?? 'New game';
    deathLoad.textContent = loadChoice?.label ?? 'Load game';
    deathQuit.textContent = quitChoice?.label ?? 'Quit to title';
    deathNew.disabled = selected !== null || newChoice?.available !== true;
    deathQuit.disabled = selected !== null || quitChoice?.available !== true;

    const selectedSlot = deathLoadSlot.value;
    deathLoadSlot.replaceChildren(new Option('Choose a saved game', ''));
    for (const entry of saveSlots.entries) deathLoadSlot.add(new Option(entry.label, entry.key));
    deathLoadSlot.value = saveSlots.entries.some(entry => entry.key === selectedSlot) ? selectedSlot : '';
    const loadAvailable = loadChoice?.available === true;
    deathLoadSlot.disabled = selected !== null || !loadAvailable;
    deathLoad.disabled = selected !== null || !loadAvailable || deathLoadSlot.value.length === 0;
    if (death?.screen) {
      const screen = image(death.screen);
      if (screen !== null) deathScreen.src = screen;
    } else {
      const screen = image(screenForMode('dead')!);
      if (screen !== null) deathScreen.src = screen;
    }
  };
  const redrawArt = (): void => {
    redrawDeath(currentDeath);
    redrawEntry();
    // The panels memo their own state revision, so art that arrived after the state it draws has to
    // ask them to paint again rather than re-send a projection they would ignore.
    inventoryView.refresh();
    characterView.refresh();
    lootView.refresh();
  };
  const unsubscribe = context.projection?.subscribe((projection) => {
    if (projection?.contract !== 'dagger.ui.snapshot.v1' || !isHud(projection.value)) return;
    const value = projection.value;
    createItem.hidden = !value.createItem;
    createItem.replaceChildren();
    if (value.createItem) {
      const choice = value.createItem;
      const title = document.createElement('h2'); title.textContent = 'Create Item'; createItem.append(title);
      for (const option of choice.options) {
        const button = document.createElement('button'); button.textContent = option.label;
        button.addEventListener('click', () => context.intents?.claim('dagger.ui', {kind:'product-payload', contract:UI_ACTION_CONTRACT,
          data:{action:'create-item-select', revision:choice.revision, key:option.id}}));
        createItem.append(button);
      }
    }
    identify.hidden = !value.identify;
    identify.replaceChildren();
    if (value.identify) {
      const choice=value.identify;
      const title=document.createElement('h2');title.textContent=`Identify (${choice.cost} magicka per selection)`;identify.append(title);
      const choose=(key?:string):void=>{context.intents?.claim('dagger.ui',{
        kind:'product-payload',contract:UI_ACTION_CONTRACT,
        data:key ? {action:'identify-select',revision:choice.revision,key} : {action:'identify-cancel',revision:choice.revision},
      });};
      for(const option of [...choice.options,...(choice.options.length ? [{id:'all',label:'All unidentified items'}] : [])]) {
        const button=document.createElement('button');button.textContent=option.label;
        button.addEventListener('click',()=>choose(option.id));identify.append(button);
      }
      const cancel=document.createElement('button');cancel.textContent='Cancel';cancel.addEventListener('click',()=>choose());identify.append(cancel);
    }
    teleport.hidden = !value.teleport;
    teleport.replaceChildren();
    if (value.teleport) {
      const choice=value.teleport;
      const title=document.createElement('h2');title.textContent='Teleport';teleport.append(title);
      for (const [key,label] of [['anchor','Set anchor'],['recall','Recall to anchor'],['cancel','Cancel']] as const) {
        const button=document.createElement('button');button.textContent=label;
        button.disabled=key==='recall' && !choice.anchorSet;
        button.addEventListener('click',()=>context.intents?.claim('dagger.ui', {
          kind:'product-payload',contract:UI_ACTION_CONTRACT,data:{action:'teleport-select',revision:choice.revision,key},
        }));teleport.append(button);
      }
    }
    dispel.hidden = !value.dispel;
    dispel.replaceChildren();
    if (value.dispel) {
      const choice = value.dispel;
      const heading = document.createElement('h2');
      heading.textContent = 'Dispel magic';
      dispel.append(heading);
      const choose = (key?: string): void => { context.intents?.claim('dagger.ui', {
        kind: 'product-payload', contract: UI_ACTION_CONTRACT,
        data: key ? { action: 'dispel-select', revision: choice.revision, key } : { action: 'dispel-cancel', revision: choice.revision },
      }); };
      for (const option of choice.options) {
        const button = document.createElement('button');
        button.textContent = option.label;
        button.addEventListener('click', () => choose(option.id));
        dispel.append(button);
      }
      const cancel = document.createElement('button');
      cancel.textContent = 'Cancel';
      cancel.addEventListener('click', () => choose());
      dispel.append(cancel);
    }
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
    renderSpellSales(spellSalesRoot,spellbook);
    renderSpellMaker(spellMakerRoot,spellbook?.maker ?? null,action =>
      context.intents?.claim('dagger.ui',{kind:'product-payload',contract:UI_ACTION_CONTRACT,data:action}));
    if (value.inventory) inventoryView.update(value.inventory);
    propertyView.update(value.property ?? null, value.inventory);
    transportView.update(isTransportProjection(value.transport) ? value.transport : null, value.inventory);
    mapView.update(isMapProjection(value.map) ? value.map : null);
    travelView.update(isTravelProjection(value.travel) ? value.travel : null);
    if (value.character && isCharacterProjection(value.character)) characterView.update(value.character);
    if (value.notebook) notebookView.update(value.notebook);
    const dungeonText = value.dungeonText ?? null;
    if (dungeonText?.revision !== currentDungeonText?.revision || dungeonText?.actionId !== currentDungeonText?.actionId)
      dungeonTextAnswer.value = '';
    currentDungeonText = dungeonText;
    dungeonTextRoot.hidden = dungeonText === null || value.mode === 'dead';
    if (dungeonText !== null) {
      dungeonTextBody.textContent = dungeonText.text;
      dungeonTextForm.hidden = !dungeonText.requiresAnswer;
      dungeonTextClose.textContent = dungeonText.requiresAnswer ? 'Cancel' : 'Continue';
    }
    lodgingView.update(isLodgingProjection(value.lodging) ? value.lodging : null);
    if (value.rest && isRestProjection(value.rest)) {
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
    currentDeath = isDeathProjection(value.death) ? value.death : null;
    redrawDeath(currentDeath);

    // The entry screen is the mode's screen, the way the death screen is the dead mode's: the mode
    // value decides which one is up, and the artifact the mode names is what it shows.
    titleMode = value.mode === TITLE_MODE;
    creationMode = titleMode && value.character?.creationAvailable && value.character.creation ? value.character.creation.editing
      ? value.character.creation.mode ?? null : value.character.creation.summary ? 'character-pick' : null : null;
    pickScreens = value.pickScreens ?? [];
    redrawEntry();

    title.textContent = value.mode === 'paused' ? 'Paused' : value.mode === 'dead' ? 'Defeated'
      : value.mode === 'title' ? 'Title' : value.mode === 'modal' ? 'Interaction' : 'Exploring';
    siteName.textContent = value.site?.name ?? '';
    outcome.textContent = value.lastOutcome;
    renderQuestMessages(quests, value.quests, (action) => context.intents?.claim('dagger.ui', {
      kind: 'product-payload', contract: UI_ACTION_CONTRACT, data: action,
    }));
    if (value.controls) controlsView.update(value.controls);
    if (value.activation) activationMode.value = value.activation.mode;
    activationMode.disabled = value.mode !== 'playing';
    // Dead mode owns the whole interaction surface. An activation projection can remain in the
    // retained snapshot for one delivery, but it must not reopen a native dialogue over the death
    // choices or leave a close action aimed at a session that has already stopped ordinary input.
    const dialogue = deadMode ? null : value.activation?.dialogue ?? null;
    currentDialogue = dialogue;
    renderMerchant(dialogue?.merchant ?? null);
    if (dialogue === null) {
      if (dialogueWindow.open) dialogueWindow.close();
    } else {
      dialogueTarget.textContent = dialogue.targetLabel + (dialogue.questContacts?.length ? " — quest contact" : "");
      dialogueGreeting.textContent = dialogue.greeting + (dialogue.comprehendLanguagesBonus ? ` Language comprehension: +${dialogue.comprehendLanguagesBonus}.` : '');
      dialogueTone.value = dialogue.tone;
      dialogueDonation.hidden = !dialogue.topics.some(topic => topic.id === 'donate');
      dialogueQuestion.textContent = dialogue.question ?? '';
      dialogueReply.textContent = dialogue.reply ?? '';
      dialogueTopics.replaceChildren(...dialogue.topics.map(topic => {
        const button = document.createElement('button');
        button.type = 'button';
        button.dataset.topic = topic.id;
        if (topic.key) button.dataset.key = topic.key;
        button.textContent = topic.label;
        return button;
      }));
      const training = dialogue.training ?? null;
      dialogueTraining.hidden = training === null;
      if (training) {
        const hours = Math.max(1, Math.round(training.durationSeconds / 3600));
        dialogueTrainingSummary.textContent = `${training.price} gold for ${hours} hour${hours === 1 ? '' : 's'}${training.member ? ' (member rate)' : ''}.`;
        dialogueTrainingSkills.replaceChildren(...training.skills.map(skill => {
          const button = document.createElement('button');
          button.type = 'button';
          button.dataset.trainingSkill = skill.id;
          button.textContent = `Train ${skill.id} (${skill.permanentValue}/${skill.maximumValue})`;
          button.disabled = skill.permanentValue >= skill.maximumValue;
          return button;
        }));
      } else dialogueTrainingSkills.replaceChildren();
      if (dialogue.bankAvailable) {
        const bank = document.createElement('button'); bank.type = 'button'; bank.textContent = 'Bank services';
        bank.dataset.bankOpen = 'true';
        bank.addEventListener('click', () => context.intents?.claim('dagger.ui', {
          kind: 'product-payload', contract: UI_ACTION_CONTRACT, data: { action: 'bank-open', revision: dialogue.revision },
        }));
        dialogueTopics.append(bank);
      }
      dialogueDiagnostics.replaceChildren(...dialogue.diagnostics.map(detail => {
        const item = document.createElement('li');
        item.textContent = detail;
        return item;
      }));
      if (!dialogueWindow.open) dialogueWindow.showModal();
    }
    view.textContent = value.view ? viewSummary(value.view) : '';
    detectors.replaceChildren();
    detectors.hidden = !value.detectors?.length;
    for (const source of value.detectors ?? []) {
      const row = document.createElement('p');
      row.dataset.source = source.source;
      row.textContent = `Detect ${source.kind}: ` + (source.contacts.length ? source.contacts.map(contact => {
        const bearing = Math.round(contact.bearingRadians * 180 / Math.PI);
        return `${contact.kind} ${contact.id}, ${contact.distance.toFixed(1)} m, bearing ${bearing}°`;
      }).join('; ') : 'none nearby');
      detectors.append(row);
    }
    status.replaceChildren(...(value.slots ?? []).map(row => { const item = document.createElement('p'); item.textContent = `${row.label}: ${row.detail}`; return item; }));
    const focus = value.focus ?? null;
    focusClose.hidden = focus === null;
    focusClose.dataset.container = focus?.container ?? '';
    focusClose.dataset.close = focus?.close ?? '';
    focusClose.textContent = focus === null ? '' : `Close ${focus.interaction}`;
    if (isSaveSlots(value.saveSlots)) {
      saveSlots = value.saveSlots;
      redrawSaveSlots();
      if (deadMode) redrawDeath(currentDeath);
    }
    composition.replaceChildren(...diagnosticRows(value.composition));
  }) ?? (() => {});
  return { dispose: () => {
    reportMap(false);
    unsubscribeController?.();
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
    lootView.dispose();
    document.removeEventListener('keydown', onKeyDown, true);
    menu.removeEventListener('cancel', onCancel);
    menu.removeEventListener('click', onMenuClick);
    menuToggle.removeEventListener('click', onMenuToggle);
    saveSlotsSelect.replaceChildren();
    if (menu.open) menu.close();
    stylesheet.remove();
    inventoryStylesheet.remove();
    characterStylesheet.remove();
    lootStylesheet.remove();
    shell.remove();
  } };
}

function mountTransport(root: HTMLElement, claim: (action: TransportAction) => void): {
  update(value: TransportProjection | null, inventory?: InventoryProjection): void;
  dispose(): void;
} {
  const shell = document.createElement('section');
  shell.className = 'dagger-transport';
  shell.setAttribute('aria-label', 'Travel and transport');
  const heading = document.createElement('h2');
  heading.textContent = 'Travel & transport';
  const status = document.createElement('p');
  status.className = 'dagger-transport-status';
  status.setAttribute('role', 'status');
  status.setAttribute('aria-live', 'polite');
  const summary = document.createElement('p');
  summary.className = 'dagger-transport-summary';
  const optionsHeading = document.createElement('h3');
  optionsHeading.textContent = 'Travel mode';
  const options = document.createElement('div');
  options.className = 'dagger-transport-options';
  const toggle = document.createElement('button');
  toggle.type = 'button';
  toggle.dataset.action = 'transport-toggle';
  toggle.textContent = 'Toggle mount';
  const wagon = document.createElement('section');
  wagon.className = 'dagger-transport-wagon';
  wagon.setAttribute('aria-label', 'Wagon storage');
  const wagonHeading = document.createElement('h3');
  wagonHeading.textContent = 'Wagon storage';
  const wagonStatus = document.createElement('p');
  wagonStatus.className = 'dagger-transport-wagon-status';
  wagonStatus.setAttribute('role', 'status');
  const wagonContentsHeading = document.createElement('h4');
  wagonContentsHeading.textContent = 'Stored items';
  const wagonContents = document.createElement('ul');
  wagonContents.className = 'dagger-transport-wagon-items';
  const packHeading = document.createElement('h4');
  packHeading.textContent = 'Store from pack';
  const packContents = document.createElement('ul');
  packContents.className = 'dagger-transport-pack-items';
  wagon.append(wagonHeading, wagonStatus, wagonContentsHeading, wagonContents, packHeading, packContents);
  shell.append(heading, status, summary, optionsHeading, options, toggle, wagon);
  root.append(shell);

  let current: TransportProjection | null = null;
  let currentInventory: InventoryProjection | undefined;
  let disposed = false;

  const itemQuantity = (quantity: string | number): string => String(quantity);
  const safeAmount = (quantity: string | number): number | undefined => {
    const parsed = typeof quantity === 'number' ? quantity : Number(quantity);
    return Number.isSafeInteger(parsed) && parsed > 0 ? parsed : undefined;
  };
  const itemLabel = (definition: string): string => definition.replaceAll('-', ' ');
  const revision = (): string => currentInventory?.revision ?? '';
  const sendItem = (action: 'wagon-put' | 'wagon-take', item: TransportItemProjection): void => {
    const currentRevision = revision();
    if (!currentRevision) return;
    const amount = item.key.startsWith('stack:') ? safeAmount(item.quantity) : undefined;
    claim(amount === undefined
      ? { action, revision: currentRevision, item: item.key }
      : { action, revision: currentRevision, item: item.key, amount });
  };
  const renderItem = (item: TransportItemProjection, action: 'wagon-put' | 'wagon-take', disabled: boolean): HTMLLIElement => {
    const row = document.createElement('li');
    const button = document.createElement('button');
    button.type = 'button';
    button.dataset.action = action;
    button.dataset.item = item.key;
    button.disabled = disabled;
    button.textContent = `${action === 'wagon-put' ? 'Store' : 'Take'} ${itemLabel(item.definition)} × ${itemQuantity(item.quantity)}`;
    button.title = item.key;
    button.addEventListener('click', () => sendItem(action, item));
    row.append(button);
    return row;
  };

  const render = (): void => {
    if (disposed) return;
    options.replaceChildren();
    wagonContents.replaceChildren();
    packContents.replaceChildren();
    if (current === null) {
      status.textContent = 'Transport projection unavailable.';
      summary.textContent = '';
      toggle.disabled = true;
      wagonStatus.textContent = 'Wagon storage is unavailable.';
      return;
    }

    const mode = normalizeTransportMode(current.mode);
    const selected = current.options.find(option => option.selected)?.label ?? mode ?? 'Foot';
    status.textContent = current.onShip ? 'You are aboard a ship.' : `${selected} travel selected.`;
    summary.textContent = `Travel modifier ${current.travelModifier}; ocean travel ${current.oceanMinutesPerMapPixel} minutes per map pixel; running ${current.canRun ? 'allowed' : 'unavailable'}.`;
    toggle.disabled = current.onShip;
    toggle.textContent = current.onShip ? 'Leave ship before choosing a mount' : 'Toggle mount';
    toggle.title = current.onShip ? 'Ship travel is a separate state.' : 'Choose horse, cart, or foot through the ruleset policy.';

    for (const option of current.options) {
      const optionMode = normalizeTransportMode(option.mode);
      if (optionMode === null) continue;
      const button = document.createElement('button');
      button.type = 'button';
      button.dataset.transportMode = optionMode;
      button.textContent = `${option.label}${option.selected ? ' · selected' : ''}`;
      button.title = option.message;
      if (optionMode === 'ship') {
        button.disabled = !current.onShip && !option.available;
        button.textContent = current.onShip ? 'Leave ship' : 'Board ship';
        if (!button.disabled) button.addEventListener('click', () => claim({
          action: current?.onShip ? 'transport-leave-ship' : 'transport-board-ship',
        }));
      } else {
        button.disabled = current.onShip || !option.available;
        if (!button.disabled) button.addEventListener('click', () => claim({ action: 'transport-select', mode: optionMode }));
      }
      options.append(button);
    }
    const wagonView = current.wagon;
    wagonStatus.textContent = `${wagonView.message} ${formatClassicUnits(wagonView.usedClassicUnits)} / ${formatClassicUnits(wagonView.capacityClassicUnits)} classic weight units.`;
    if (!wagonView.accessible) return;
    const currentRevision = revision();
    for (const item of wagonView.items) wagonContents.append(renderItem(item, 'wagon-take', !currentRevision));
    for (const item of currentInventory?.items ?? []) {
      const equipped = item.equippedSlots.length !== 0;
      const invalidQuantity = item.key.startsWith('stack:') && safeAmount(item.quantity) === undefined;
      const refused = wagonView.refusedDefinitions.includes(item.definition);
      packContents.append(renderItem(item, 'wagon-put', !currentRevision || equipped || invalidQuantity || refused));
    }
    if (wagonView.items.length === 0) {
      const empty = document.createElement('li');
      empty.textContent = 'The wagon is empty.';
      wagonContents.append(empty);
    }
    if ((currentInventory?.items.length ?? 0) === 0) {
      const empty = document.createElement('li');
      empty.textContent = 'Your pack has no transferable items.';
      packContents.append(empty);
    }
  };

  toggle.addEventListener('click', () => {
    if (!toggle.disabled) claim({ action: 'transport-toggle' });
  });
  return {
    update(value, inventory): void {
      if (disposed) return;
      current = value;
      currentInventory = inventory;
      render();
    },
    dispose(): void {
      if (disposed) return;
      disposed = true;
      shell.remove();
    },
  };
}

function normalizeTransportMode(value: string): TransportMode | null {
  const mode = value.toLowerCase();
  return mode === 'foot' || mode === 'horse' || mode === 'cart' || mode === 'ship' ? mode : null;
}

function isTransportProjection(value: unknown): value is TransportProjection {
  if (typeof value !== 'object' || value === null || !('options' in value) || !Array.isArray(value.options)
    || !('wagon' in value) || !isWagonProjection(value.wagon)) return false;
  const candidate = value as Partial<TransportProjection>;
  return typeof candidate.mode === 'string'
    && typeof candidate.onShip === 'boolean'
    && typeof candidate.canRun === 'boolean'
    && typeof candidate.travelModifier === 'number' && Number.isFinite(candidate.travelModifier)
    && typeof candidate.oceanMinutesPerMapPixel === 'number' && Number.isFinite(candidate.oceanMinutesPerMapPixel)
    && Array.isArray(candidate.options) && candidate.options.every(isTransportOptionProjection);
}

function isTransportOptionProjection(value: unknown): value is TransportOptionProjection {
  if (typeof value !== 'object' || value === null) return false;
  const candidate = value as Partial<TransportOptionProjection>;
  return typeof candidate.id === 'string'
    && typeof candidate.mode === 'string'
    && typeof candidate.available === 'boolean'
    && typeof candidate.selected === 'boolean'
    && typeof candidate.label === 'string'
    && typeof candidate.message === 'string'
    && typeof candidate.travelModifier === 'number' && Number.isFinite(candidate.travelModifier);
}

function isWagonProjection(value: unknown): value is WagonProjection {
  if (typeof value !== 'object' || value === null || !('items' in value) || !Array.isArray(value.items)) return false;
  const candidate = value as Partial<WagonProjection>;
  return typeof candidate.exists === 'boolean'
    && typeof candidate.accessible === 'boolean'
    && (candidate.id === null || typeof candidate.id === 'number')
    && typeof candidate.usedClassicUnits === 'number' && Number.isFinite(candidate.usedClassicUnits)
    && typeof candidate.capacityClassicUnits === 'number' && Number.isFinite(candidate.capacityClassicUnits)
    && (candidate.storeRevision === null || typeof candidate.storeRevision === 'string' || typeof candidate.storeRevision === 'number')
    && typeof candidate.message === 'string'
    && Array.isArray(candidate.items) && candidate.items.every(isTransportItemProjection)
    && Array.isArray(candidate.refusedDefinitions) && candidate.refusedDefinitions.every(value => typeof value === 'string');
}

function isTransportItemProjection(value: unknown): value is TransportItemProjection {
  if (typeof value !== 'object' || value === null) return false;
  const candidate = value as Partial<TransportItemProjection>;
  return typeof candidate.key === 'string'
    && typeof candidate.definition === 'string'
    && (typeof candidate.quantity === 'string' || typeof candidate.quantity === 'number');
}

function isRestProjection(value: unknown): value is RestProjection {
  if (typeof value !== 'object' || value === null) return false;
  const candidate = value as Partial<RestProjection>;
  return typeof candidate.hasResult === 'boolean'
    && typeof candidate.revision === 'string'
    && (candidate.mode === null || typeof candidate.mode === 'string')
    && typeof candidate.requestedSeconds === 'number' && Number.isFinite(candidate.requestedSeconds)
    && typeof candidate.elapsedSeconds === 'number' && Number.isFinite(candidate.elapsedSeconds)
    && typeof candidate.recoveryHours === 'number' && Number.isFinite(candidate.recoveryHours)
    && typeof candidate.healthRecovered === 'number' && Number.isFinite(candidate.healthRecovered)
    && typeof candidate.fatigueRecovered === 'number' && Number.isFinite(candidate.fatigueRecovered)
    && typeof candidate.spellPointsRecovered === 'number' && Number.isFinite(candidate.spellPointsRecovered)
    && typeof candidate.interruption === 'string'
    && (candidate.message === null || typeof candidate.message === 'string');
}

function formatRestHours(seconds: number): string {
  const hours = seconds / 3600;
  return Number.isInteger(hours) ? String(hours) : hours.toFixed(2);
}

function formatClassicUnits(value: number): string {
  return Number.isFinite(value) ? Math.max(0, value).toLocaleString() : 'unknown';
}

export function isHud(value: unknown): value is DaggerHud {
  return typeof value === 'object' && value !== null
    && 'resources' in value && Array.isArray(value.resources) && value.resources.every(isResourceRow)
    && 'lastOutcome' in value && typeof value.lastOutcome === 'string'
    && 'composition' in value && isCompositionIdentity(value.composition);
}

export function isSpellbookProjection(value:unknown):value is SpellbookProjection {
  if(!value || typeof value!=='object') return false;
  const v=value as Partial<SpellbookProjection>;
  return Array.isArray(v.available) && v.available.every(spell=>spell && typeof spell==='object'
    && typeof spell.key==='string' && typeof spell.name==='string' && Number.isInteger(spell.cost) && spell.cost>=0
    && (spell.canCast===undefined || typeof spell.canCast==='boolean'))
    && (v.ready===null || typeof v.ready==='string' && v.available.some(spell=>spell.key===v.ready)) && typeof v.result==='string'
    && (v.sale==null || typeof v.sale.revision==='string' && typeof v.sale.provider==='string' && Array.isArray(v.sale.offers)
      && v.sale.offers.every(offer=>offer && typeof offer==='object' && typeof offer.key==='string' && typeof offer.name==='string' && Number.isSafeInteger(offer.price)
        && offer.price>=0 && Number.isInteger(offer.castingCost) && offer.castingCost>=0 && typeof offer.known==='boolean'))
    && (v.maker==null || isSpellMakerProjection(v.maker))
    && (v.information==null || typeof v.information.key==='string' && typeof v.information.name==='string'
      && typeof v.information.target==='string' && Number.isInteger(v.information.element)
      && Array.isArray(v.information.details) && v.information.details.every(line=>typeof line==='string'));
}

function renderSpells(root:HTMLElement,view:SpellbookProjection|null):void {
  root.replaceChildren();
  const title=document.createElement('h2');title.textContent='Known spells';root.append(title);
  const status=document.createElement('p');status.setAttribute('role','status');
  status.textContent=view?.result ?? '';root.append(status);
  if(!view) return;
  for(const spell of view.available) {
    const button=document.createElement('button');button.type='button';button.dataset.action='spell-ready';button.dataset.spell=spell.key;
    button.textContent=`${spell.name} · ${spell.canCast===false ? 'Unavailable' : `${spell.cost} magicka`}${view.ready===spell.key ? ' · Ready' : ''}`;
    button.disabled=spell.canCast===false;
    button.setAttribute('aria-pressed',String(view.ready===spell.key));root.append(button);
    for(const [action,label] of [['spell-info','Info'],['spell-delete','Forget']]) {
      const control=document.createElement('button');control.type='button';control.dataset.action=action;control.dataset.spell=spell.key;
      control.textContent=`${label} ${spell.name}`;root.append(control);
    }
  }
  renderSpellInformation(root,view);
  if(view.available.length===0) { const empty=document.createElement('p');empty.textContent='No available known spells.';root.append(empty); }
  for(const [action,label] of [['spell-unready','Unready'],['spell-cast','Cast ready spell']]) {
    const button=document.createElement('button');button.type='button';button.dataset.action=action;button.textContent=label;
    button.disabled=view.ready===null;root.append(button);
  }
}

function renderSpellInformation(root:HTMLElement,view:SpellbookProjection):void {
  if(!view.information) return;
  const section=document.createElement('section');const title=document.createElement('h3');title.textContent=view.information.name;section.append(title);
  const target=document.createElement('p');target.textContent=`Target: ${({CasterOnly:'Self',ByTouch:'Touch',SingleTargetAtRange:'Single target at range',AreaAroundCaster:'Area around self',AreaAtRange:'Area at range'} as Record<string,string>)[view.information.target] ?? view.information.target}; element: ${['none','fire','cold','poison','shock','magic'][view.information.element] ?? 'unknown'}`;section.append(target);
  for(const line of view.information.details) { const p=document.createElement('p');p.textContent=line;section.append(p); } root.append(section);
}

function renderSpellSales(root:HTMLElement,view:SpellbookProjection|null):void {
  root.replaceChildren();if(!view?.sale) return;
  const heading=document.createElement('h3');heading.textContent=`Spells offered by ${view.sale.provider}`;root.append(heading);
  const status=document.createElement('p');status.setAttribute('role','status');status.textContent=view.result;root.append(status);
  for(const offer of view.sale.offers) {
    const buy=document.createElement('button');buy.type='button';buy.dataset.action='spell-buy';buy.dataset.spell=offer.key;
    buy.dataset.revision=view.sale.revision;buy.dataset.price=String(offer.price);buy.disabled=offer.known;
    buy.textContent=`${offer.name} · ${offer.price} gold · ${offer.castingCost} magicka${offer.known ? ' · Known' : ''}`;root.append(buy);
    const info=document.createElement('button');info.type='button';info.dataset.action='spell-info';info.dataset.spell=offer.key;
    info.textContent=`Info ${offer.name}`;root.append(info);
  }
  renderSpellInformation(root,view);
}

export function isSpellMakerProjection(value:unknown):value is SpellMakerProjection {
  if(!value || typeof value!=='object') return false;
  const v=value as Partial<SpellMakerProjection>;const draft=v.draft;
  return typeof v.revision==='string' && typeof v.provider==='string' && Array.isArray(v.effects)
    && v.effects.every(e=>e && typeof e.key==='string' && typeof e.school==='string' && Number.isInteger(e.type)
      && Number.isInteger(e.subType) && typeof e.duration==='boolean' && typeof e.chance==='boolean' && typeof e.magnitude==='boolean'
      && Number.isInteger(e.targets) && Number.isInteger(e.elements))
    && !!draft && typeof draft.name==='string' && Number.isInteger(draft.element) && Number.isInteger(draft.rangeType)
    && Number.isInteger(draft.icon) && Array.isArray(draft.effects)
    && draft.effects.every(e=>e && typeof e.key==='string'
      && ['type','subType','durationBase','durationMod','durationPerLevel','chanceBase','chanceMod','chancePerLevel',
        'magnitudeBaseLow','magnitudeBaseHigh','magnitudeLevelBase','magnitudeLevelHigh','magnitudePerLevel']
        .every(key=>Number.isInteger(e[key as keyof SpellMakerSetting])))
    && (v.quote===null || !!v.quote && typeof v.quote.key==='string' && Number.isSafeInteger(v.quote.gold)
      && v.quote.gold>=0 && Number.isSafeInteger(v.quote.spellPoints) && v.quote.spellPoints>=0
      && typeof v.quote.eligible==='boolean' && (v.quote.reason===null || typeof v.quote.reason==='string'));
}

export function renderSpellMaker(root:HTMLElement,view:SpellMakerProjection|null,claim:(action:UiAction)=>void):void {
  const stamp=JSON.stringify(view);
  // Admitted modal updates can publish the same projection while the player types.
  if(root.dataset.spellMaker===stamp) return;
  root.dataset.spellMaker=stamp;root.replaceChildren();if(!view) return;
  const title=document.createElement('h3');title.textContent=`Make a spell with ${view.provider}`;root.append(title);
  const form=document.createElement('form');root.append(form);
  const input=(parent:HTMLElement,label:string,name:string,value:string|number,max?:number):HTMLInputElement=>{
    const wrap=document.createElement('label');wrap.textContent=`${label} `;
    const field=document.createElement('input');field.name=name;field.value=String(value);
    if(max!==undefined) {field.type='number';field.min='1';field.max=String(max);field.step='1';} else field.maxLength=31;
    wrap.append(field);parent.append(wrap);return field;
  };
  const name=input(form,'Name','name',view.draft.name);name.required=true;
  const select=(label:string,name:string,values:readonly string[],selected:number):HTMLSelectElement=>{
    const wrap=document.createElement('label');wrap.textContent=`${label} `;const field=document.createElement('select');field.name=name;
    values.forEach((value,index)=>{const option=document.createElement('option');option.value=String(index);option.textContent=value;field.append(option);});
    field.value=String(selected);wrap.append(field);form.append(wrap);return field;
  };
  const target=select('Target','target',['Self','Touch','Single target at range','Area around self','Area at range'],view.draft.rangeType);
  const element=select('Element','element',['Fire','Cold','Poison','Shock','Magic'],view.draft.element);
  const icon=select('Icon','icon',Array.from({length:69},(_,i)=>String(i+1)),view.draft.icon);
  const slots:{select:HTMLSelectElement;fields:HTMLElement}[]=[];
  for(let slot=0;slot<3;slot++) {
    const section=document.createElement('fieldset');const legend=document.createElement('legend');legend.textContent=`Effect ${slot+1}`;section.append(legend);form.append(section);
    const picker=document.createElement('select');picker.setAttribute('aria-label',`Effect ${slot+1}`);
    const empty=document.createElement('option');empty.value='';empty.textContent='No effect';picker.append(empty);
    for(const effect of view.effects) {const option=document.createElement('option');option.value=effect.key;option.textContent=`${effect.school}: ${effect.key.replaceAll('-',' ')}`;picker.append(option);}
    const saved=view.draft.effects[slot];picker.value=saved?.key ?? '';section.append(picker);
    const fields=document.createElement('div');section.append(fields);slots.push({select:picker,fields});
    const settings=():void=>{
      fields.replaceChildren();const effect=view.effects.find(e=>e.key===picker.value);if(!effect) return;
      const add=(label:string,key:keyof SpellMakerSetting,max:number):void=>{input(fields,label,key,saved?.key===effect.key ? Number(saved[key]) : 1,max);};
      if(effect.duration) {add('Duration base','durationBase',60);add('Duration increase','durationMod',60);add('Duration per levels','durationPerLevel',20);}
      if(effect.chance) {add('Chance base','chanceBase',100);add('Chance increase','chanceMod',100);add('Chance per levels','chancePerLevel',20);}
      if(effect.magnitude) {add('Magnitude minimum','magnitudeBaseLow',100);add('Magnitude maximum','magnitudeBaseHigh',100);
        add('Magnitude increase minimum','magnitudeLevelBase',100);add('Magnitude increase maximum','magnitudeLevelHigh',100);add('Magnitude per levels','magnitudePerLevel',20);}
    };
    picker.addEventListener('change',settings);settings();
  }
  const preview=document.createElement('button');preview.type='submit';preview.textContent='Update cost preview';form.append(preview);
  form.addEventListener('submit',event=>{
    event.preventDefault();
    const effects=slots.flatMap(slot=>{
      const option=view.effects.find(e=>e.key===slot.select.value);if(!option) return [];
      const values:Record<string,string|number>={key:option.key,type:option.type,subType:option.subType,
        durationBase:1,durationMod:1,durationPerLevel:1,chanceBase:1,chanceMod:1,chancePerLevel:1,
        magnitudeBaseLow:1,magnitudeBaseHigh:1,magnitudeLevelBase:1,magnitudeLevelHigh:1,magnitudePerLevel:1};
      for(const field of slot.fields.querySelectorAll<HTMLInputElement>('input')) values[field.name]=Number(field.value);
      return [values];
    });
    claim({action:'spellmaker-draft',revision:view.revision,text:JSON.stringify({name:name.value,element:Number(element.value),
      rangeType:Number(target.value),icon:Number(icon.value),effects})});
  });
  const status=document.createElement('p');status.setAttribute('role','status');
  status.textContent=view.quote?.eligible ? `${view.quote.gold} gold · ${view.quote.spellPoints} magicka to cast` : 'Update the preview with valid settings.';root.append(status);
  const buy=document.createElement('button');buy.type='button';buy.textContent='Buy constructed spell';buy.disabled=!view.quote?.eligible;root.append(buy);
  const stale=():void=>{buy.disabled=true;status.textContent='Update the cost preview after editing.';};
  form.addEventListener('input',stale);form.addEventListener('change',stale);
  buy.addEventListener('click',()=>{if(!buy.disabled && view.quote?.eligible && window.confirm(`Make this spell for ${view.quote.gold} gold?`))
    claim({action:'spellmaker-buy',revision:view.revision,key:view.quote.key,amount:view.quote.gold,confirm:true});});
}

function renderQuestMessages(root: HTMLElement, value: QuestPresentation | undefined, claim: (action: UiAction) => void): void {
  root.replaceChildren();
  if (!value) return;
  if (value.escortFaces?.length) {
    const escorts = document.createElement('aside'); escorts.className = 'dagger-escort-faces';
    escorts.setAttribute('aria-label', 'Escorted companions');
    for (const face of value.escortFaces) {
      const portrait = document.createElement('img'); portrait.alt = face.name;
      portrait.dataset.questInstance = face.instance; portrait.dataset.questResource = face.symbol;
      const source = image(face.mediaId);
      if (source) portrait.src = source;
      else portrait.dataset.artMissing = face.mediaId;
      escorts.append(portrait);
    }
    root.append(escorts);
  }
  const add = (message: QuestMessageProjection, heading: string): void => {
    const article = document.createElement('article');
    article.className = `dagger-quest-message dagger-quest-${message.delivery}`;
    const title = document.createElement('strong');
    title.textContent = heading;
    const text = document.createElement('p');
    text.textContent = message.text;
    article.append(title, text);
    if (message.entryId && (message.delivery === 'popup' || message.delivery === 'letter')) {
      const dismiss = document.createElement('button');
      dismiss.type = 'button'; dismiss.textContent = 'Dismiss';
      dismiss.addEventListener('click', () => claim({ action: 'quest-dismiss', questInstance: message.instance,
        questDelivery: message.entryId ?? undefined }));
      article.append(dismiss);
    }
    if (message.signoff) {
      const signoff = document.createElement('p');
      signoff.textContent = message.signoff;
      article.append(signoff);
    }
    if (message.diagnostics.length > 0) {
      const diagnostics = document.createElement('p');
      diagnostics.className = 'dagger-quest-diagnostic';
      diagnostics.textContent = message.diagnostics.join(' ');
      article.append(diagnostics);
    }
    root.append(article);
  };
  value.deliveries.filter(message => message.delivery !== 'prompt').forEach(message => add(message, message.delivery));
  value.journal.forEach(message => add(message, 'journal'));
  if (value.pending) {
    const message = value.pending;
    const prompt = document.createElement('article');
    prompt.className = 'dagger-quest-message dagger-quest-prompt';
    const text = document.createElement('p');
    text.textContent = message.text;
    prompt.append(text);
    for (const option of message.options) {
      const button = document.createElement('button');
      button.type = 'button'; button.textContent = option.label;
      button.addEventListener('click', () => claim({ action: 'quest-choice', questInstance: message.instance,
        questMessage: message.message, questPrompt: message.promptId ?? undefined, questChoice: option.id }));
      prompt.append(button);
    }
    root.append(prompt);
  }
}

function isSaveSlots(value: unknown): value is SaveSlotProjection {
  if (typeof value !== 'object' || value === null || !('entries' in value) || !Array.isArray(value.entries)) return false;
  const diagnostic = 'diagnostic' in value ? value.diagnostic : null;
  return (diagnostic === null || typeof diagnostic === 'string') && value.entries.every(entry =>
    typeof entry === 'object' && entry !== null
    && 'key' in entry && typeof entry.key === 'string'
    && 'label' in entry && typeof entry.label === 'string'
    && 'savedAtUtc' in entry && typeof entry.savedAtUtc === 'string'
    && 'ruleset' in entry && typeof entry.ruleset === 'string');
}

function isDeathProjection(value: unknown): value is DeathProjection {
  if (typeof value !== 'object' || value === null || !('choices' in value) || !Array.isArray(value.choices)) return false;
  const candidate = value as Partial<DeathProjection>;
  return typeof candidate.active === 'boolean'
    && typeof candidate.screen === 'string'
    && typeof candidate.revision === 'string'
    && typeof candidate.message === 'string'
    && typeof candidate.controlsSuppressed === 'boolean'
    && typeof candidate.cameraEffect === 'string'
    && typeof candidate.fadeEffect === 'string'
    && typeof candidate.audioCue === 'string'
    && (candidate.selected === null || typeof candidate.selected === 'string')
    && candidate.choices!.every(choice => typeof choice === 'object' && choice !== null
      && 'action' in choice && typeof choice.action === 'string'
      && 'id' in choice && typeof choice.id === 'string'
      && 'label' in choice && typeof choice.label === 'string'
      && 'available' in choice && typeof choice.available === 'boolean');
}

function viewSummary(view: NonNullable<DaggerHud['view']>): string {
  const direction = ['north', 'north-east', 'east', 'south-east', 'south', 'south-west', 'west', 'north-west']
    [Math.round((((view.yawRadians % (Math.PI * 2)) + Math.PI * 2) % (Math.PI * 2)) / (Math.PI / 4)) % 8]!;
  const pitch = view.pitchRadians > 0.35 ? 'looking up' : view.pitchRadians < -0.35 ? 'looking down' : 'level';
  return `Facing ${direction} · ${pitch}`;
}

function isResourceRow(value: unknown): value is DaggerHud['resources'][number] {
  return typeof value === 'object' && value !== null
    && 'id' in value && typeof value.id === 'string'
    && 'label' in value && typeof value.label === 'string'
    && 'current' in value && typeof value.current === 'number' && Number.isFinite(value.current)
    && 'maximum' in value && typeof value.maximum === 'number' && Number.isFinite(value.maximum);
}

function isCompositionIdentity(value: unknown): value is CompositionIdentity {
  return typeof value === 'object' && value !== null
    && 'bundle' in value && typeof value.bundle === 'string'
    && 'ruleset' in value && typeof value.ruleset === 'string'
    && 'contentPacks' in value && Array.isArray(value.contentPacks) && value.contentPacks.every((pack) => typeof pack === 'string')
    && 'tuning' in value && typeof value.tuning === 'string';
}

function diagnosticRows(identity: CompositionIdentity): readonly HTMLElement[] {
  return [
    diagnosticRow('Bundle', identity.bundle),
    diagnosticRow('Ruleset', identity.ruleset),
    diagnosticRow('Packs', identity.contentPacks.join(' → ')),
    diagnosticRow('Tuning', identity.tuning),
  ];
}

function diagnosticRow(label: string, value: string): HTMLElement {
  const fragment = document.createDocumentFragment();
  const term = document.createElement('dt');
  const definition = document.createElement('dd');
  term.textContent = label;
  definition.textContent = value;
  fragment.append(term, definition);
  const row = document.createElement('div');
  row.append(fragment);
  return row;
}
