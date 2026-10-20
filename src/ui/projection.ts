import type { UiArt } from './art.js';
import type { CharacterProjection } from './character.js';
import type { ControlsProjection } from './controls.js';
import type { InventoryProjection } from './inventory.js';
import type { QuestJournalProjection } from './journal.js';
import type { SpellIconAtlas } from './spell-icon.js';
import type { LodgingProjection } from './lodging.js';
import type { LootProjection } from './loot.js';
import type { MapProjection } from './maps.js';
import type { NotebookProjection } from './notebook.js';
import type { PropertyProjection } from './property.js';
import type { TravelProjection } from './travel.js';

/** The HUD snapshot the ruleset publishes and the semantic actions the UI sends back. */

export interface UiAction { readonly action: string; readonly [field: string]: string | number | boolean | undefined; }

export interface BundleProjection { readonly current: string; readonly options: readonly { readonly id: string; readonly title: string }[]; }

export interface DaggerHud {
  readonly detectors?: readonly { readonly source: string; readonly kind: string; readonly contacts: readonly {
    readonly kind: string; readonly id: string; readonly label: string; readonly distance: number; readonly bearingRadians: number;
    readonly items: readonly { readonly id: string; readonly definition: string; readonly quantity: number }[];
  }[] }[];
  readonly spells?: SpellbookProjection | null;
  readonly legal?: {readonly revision:string; readonly phase:string; readonly title:string; readonly message:string; readonly charges:readonly string[]; readonly choices:readonly {readonly id:string; readonly label:string}[]} | null;
  readonly createItem?: { readonly revision:string; readonly options:readonly { readonly id:string; readonly label:string }[] } | null;
  readonly identify?: { readonly revision:string; readonly cost:number; readonly options:readonly { readonly id:string; readonly label:string }[] } | null;
  readonly teleport?: { readonly revision:string; readonly options:readonly { readonly id:string; readonly label:string; readonly available:boolean }[] } | null;
  readonly dispel?: { readonly revision: string; readonly options: readonly { readonly id: string; readonly label: string }[] } | null;
  readonly resources: readonly { readonly id: string; readonly label: string; readonly current: number; readonly maximum: number }[];
  readonly lastOutcome: string;
  readonly messages: readonly string[];
  readonly breath?: { readonly current: number; readonly maximum: number; readonly low: boolean } | null;
  readonly mode?: string;
  /** The site the session projects the player at; null before any site owns them. */
  readonly site?: { readonly name: string } | null;
  /** The world calendar's current date and time, worded by the ruleset. */
  readonly calendar?: CalendarProjection | null;
  /** The player's active effects, from the effects owner. */
  readonly effects?: readonly ActiveEffectProjection[];
  /** The cinematic playing now, when one is; its owner supports skipping it. */
  readonly cinematic?: { readonly source: string; readonly skipLabel: string } | null;
  readonly composition: CompositionIdentity;
  readonly inventory?: InventoryProjection;
  readonly character?: CharacterProjection;
  readonly pickScreens?: readonly string[];
  readonly loot?: LootProjection | null;
  readonly uiArtRevision?: string;
  readonly uiArt?: UiArt | null;
  /** The images projections demanded since the session began; travels when it grew or was asked for. */
  readonly uiArtDemandedRevision?: string;
  readonly uiArtDemanded?: UiArt | null;
  readonly panelRequest?: PanelRequest | null;
  readonly saveSlots?: SaveSlotProjection;
  /** The games the entry screen offers while a new game can still choose one; absent or null otherwise. */
  readonly bundles?: BundleProjection | null;
  readonly controls?: ControlsProjection;
  readonly timeControl?: { readonly enabled: boolean; readonly held: boolean };
  readonly activation?: { readonly mode: string; readonly message: string; readonly applied: boolean; readonly dialogue?: DialogueProjection | null };
  readonly transport?: TransportProjection | null;
  readonly property?: PropertyProjection | null;
  readonly travel?: TravelProjection | null;
  readonly map?: MapProjection | null;
  readonly quests?: QuestPresentation;
  readonly notebook?: NotebookProjection;
  readonly view?: { readonly yawRadians: number; readonly pitchRadians: number; readonly interaction: string };
  readonly slots?: readonly { readonly owner: string; readonly id: string; readonly label: string; readonly detail: string; readonly order: number }[];
  readonly dungeonText?: { readonly actionId: string; readonly kind: string; readonly text: string; readonly revision: string; readonly requiresAnswer: boolean } | null;
  readonly death?: DeathProjection | null;
  readonly rest?: RestProjection | null;
  readonly lodging?: LodgingProjection | null;
}

export interface CalendarProjection {
  readonly dayName: string; readonly day: number; readonly monthName: string; readonly year: number;
  readonly hour: number; readonly minute: number; readonly date: string; readonly time: string;
}

export interface ActiveEffectProjection {
  readonly id: string; readonly name: string; readonly spell: string; readonly source: string | null;
  readonly remainingSeconds: number | null; readonly remaining: string; readonly detail: string;
}

export interface SpellMakerSetting {
  readonly key:string; readonly type:number; readonly subType:number;
  readonly durationBase:number; readonly durationMod:number; readonly durationPerLevel:number;
  readonly chanceBase:number; readonly chanceMod:number; readonly chancePerLevel:number;
  readonly magnitudeBaseLow:number; readonly magnitudeBaseHigh:number; readonly magnitudeLevelBase:number;
  readonly magnitudeLevelHigh:number; readonly magnitudePerLevel:number;
}
export interface SpellMakerOption { readonly value:number; readonly label:string; }
export interface SpellMakerDraft { readonly name:string;readonly element:number;readonly rangeType:number;readonly icon:number;readonly effects:readonly SpellMakerSetting[]; }
/** The ruleset's spellmaker bounds: the name's length, the icons, the effect slots and each field's maximum. */
export interface SpellMakerLimits {
  readonly nameLength:number;readonly icons:number;readonly effects:number;
  readonly duration:number;readonly chance:number;readonly magnitude:number;readonly perLevels:number;
}
export interface SpellMakerProjection {
  readonly revision:string;readonly provider:string;readonly draft:SpellMakerDraft;readonly limits:SpellMakerLimits;
  /** The atlas the icon picker cuts each icon from. */
  readonly icons?:SpellIconAtlas;
  readonly targets:readonly SpellMakerOption[];readonly elements:readonly SpellMakerOption[];
  readonly effects:readonly {readonly key:string;readonly name:string;readonly type:number;readonly subType:number;readonly school:string;
    readonly duration:boolean;readonly chance:boolean;readonly magnitude:boolean;readonly targets:number;readonly elements:number}[];
  readonly quote:{readonly key:string;readonly gold:number;readonly spellPoints:number;readonly eligible:boolean;readonly reason:string|null}|null;
}

export interface SummoningProjection {
  readonly revision:string;readonly provider:string;readonly offerRevision:string|null;readonly prince:string|null;readonly message:string|null;readonly diagnostics:readonly string[];
  readonly quote:{readonly key:string;readonly name:string;readonly quest:string;readonly gold:number;readonly eligible:boolean;readonly reason:string|null}|null;
}
export interface ItemMakerSetting { readonly key:string;readonly name:string;readonly cost:number;readonly forced:readonly string[]; }
export interface ItemMakerProjection {
  readonly revision:string;readonly provider:string;readonly eligible:boolean;
  readonly items:readonly {readonly key:string;readonly name:string;readonly capacity:number;readonly quantity:number}[];
  readonly settings:readonly ItemMakerSetting[];
  readonly draft:{readonly item:string;readonly name:string;readonly settings:readonly string[]};
  readonly quote:{readonly key:string;readonly capacity:number;readonly power:number;readonly gold:number;readonly eligible:boolean;readonly reason:string|null;readonly payloads:readonly ItemMakerSetting[]}|null;
}
export interface PotionMakerProjection {
  readonly revision:string; readonly provider:string; readonly eligible:boolean;
  /** How many ingredients one mix takes. */
  readonly minimumIngredients:number; readonly maximumIngredients:number;
  readonly ingredients:readonly {readonly template:number;readonly name:string;readonly quantity:number}[];
  readonly recipes:readonly {readonly key:number;readonly name:string;readonly ingredients:readonly number[];readonly available:boolean}[];
}

export interface SpellbookProjection {
  /** Each known spell with its icon in the atlas and the school of its first effect, which the tabs group by. */
  readonly available: readonly {readonly key:string;readonly name:string;readonly cost:number;readonly canCast?:boolean;readonly icon?:number;readonly school?:string}[];
  readonly ready:string|null;readonly result:string;
  readonly icons?:SpellIconAtlas;
  /** The schools in the order the tabs show them. */
  readonly schools?:readonly {readonly id:string;readonly label:string}[];
  readonly sale?: {readonly revision:string;readonly provider:string;readonly offers:readonly {readonly key:string;readonly name:string;readonly castingCost:number;readonly price:number;readonly known:boolean}[]} | null;
  readonly maker?:SpellMakerProjection|null;
  readonly potionMaker?:PotionMakerProjection|null;
  readonly itemMaker?:ItemMakerProjection|null;
  readonly summoning?:SummoningProjection|null;
  readonly information?: {readonly key:string;readonly name:string;readonly element:string;readonly target:string;readonly details:readonly string[]} | null;
}

export interface RestProjection {
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
  /** The longest timed rest and loiter the ruleset accepts, which bound their duration boxes. */
  readonly maximumRestHours: number;
  readonly maximumLoiterHours: number;
  readonly message: string | null;
}

export interface DeathProjection {
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

export interface DialogueProjection {
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
  readonly topics: readonly { readonly id: string; readonly label: string; readonly key?: string | null; readonly group?: string }[];
  /** The headings topics are listed under, in order. */
  readonly topicGroups?: readonly { readonly id: string; readonly label: string }[];
  readonly tavern?: { readonly hungry: boolean; readonly menu: readonly { readonly id: string; readonly label: string; readonly price: number }[] } | null;
  readonly training?: {
    readonly providerFaction: number;
    readonly membershipFaction: number;
    readonly member: boolean;
    readonly rank: number;
    readonly price: number;
    readonly durationSeconds: number;
    /** The training's length in whole hours, as the ruleset rounds it. */
    readonly durationHours: number;
    readonly cooldownReadySecond: number;
    readonly skills: readonly { readonly id: string; readonly label: string; readonly permanentValue: number; readonly maximumValue: number;
      /** Whether this trainer can raise the skill further. */
      readonly trainable: boolean }[];
  } | null;
  readonly diagnostics: readonly string[];
}

export interface MerchantItemProjection {
  readonly key: string;
  readonly definition: string;
  readonly label: string;
  readonly quantity: string | number;
  /** The most of this row one trade moves: a stack's count, or one. */
  readonly maximumQuantity: number;
  readonly unitPrice: string | number;
  readonly currentCondition: number;
  readonly maximumCondition: number;
  readonly identified: boolean;
  readonly stolen: boolean;
  readonly canBuy: boolean;
  readonly canSell: boolean;
  readonly canRepair?: boolean;
  readonly canIdentify?: boolean;
  /** The provider's quoted repair price in gold, where it would repair this item. */
  readonly repairCost?: string | null;
  /** The provider's quoted identification price in gold, where it would identify this item. */
  readonly identifyCost?: string | null;
}

export interface MerchantProjection {
  readonly revision: string;
  readonly provider: string;
  readonly quality: number;
  readonly gold: string | number;
  readonly buyAvailable: boolean;
  readonly shopliftAvailable?: boolean;
  readonly sellAvailable: boolean;
  readonly repairAvailable: boolean;
  readonly identifyAvailable: boolean;
  readonly result: string;
  /** A purchase or sale totalling this many gold or more is confirmed first. */
  readonly largeTradeGold: number;
  readonly stock: readonly MerchantItemProjection[];
  readonly playerItems: readonly MerchantItemProjection[];
  readonly repairs: readonly { readonly requestId: string; readonly durableItemId: string | number; readonly definition: string; readonly dueMinute: number; readonly ready: boolean;
    readonly label?: string; readonly status?: string }[];
}

export interface QuestMessageProjection {
  readonly entryId: string | null;
  readonly promptId: string | null;
  readonly options: readonly { readonly id: number; readonly label: string }[];
  readonly instance: string;
  readonly message: number;
  readonly delivery: 'popup' | 'letter' | 'rumor' | 'journal' | 'prompt';
  readonly heading: string;
  readonly text: string;
  readonly signoff: string | null;
  readonly diagnostics: readonly string[];
}
export interface QuestPresentation {
  readonly offer?: { readonly instance: string; readonly text: string; readonly diagnostics: readonly string[] } | null;
  readonly escortFaces: readonly { readonly instance: string; readonly symbol: string; readonly name: string; readonly mediaId: string }[];
  readonly deliveries: readonly QuestMessageProjection[];
  readonly journal: QuestJournalProjection;
  readonly pending: QuestMessageProjection | null;
}

export interface SaveSlotProjection {
  readonly entries: readonly { readonly key: string; readonly label: string; readonly savedAtUtc: string; readonly ruleset: string }[];
  readonly diagnostic: string | null;
}

export type SaveSlotAction =
  | { readonly action: 'save-slots' }
  | { readonly action: 'save-slot'; readonly key?: string; readonly label: string; readonly confirm?: boolean }
  | { readonly action: 'load-slot'; readonly key: string }
  | { readonly action: 'delete-slot'; readonly key: string; readonly confirm?: boolean };

export type TransportMode = 'foot' | 'horse' | 'cart' | 'ship';

export interface TransportOptionProjection {
  readonly id: string;
  readonly mode: string;
  readonly available: boolean;
  readonly selected: boolean;
  readonly label: string;
  readonly message: string;
  readonly travelModifier: number;
}

export interface TransportItemProjection {
  readonly key: string;
  readonly definition: string;
  readonly label: string;
  readonly quantity: string | number;
}

export interface WagonProjection {
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

export interface TransportProjection {
  readonly mode: string;
  readonly onShip: boolean;
  readonly canRun: boolean;
  readonly summary: string;
  readonly travelModifier: number;
  readonly oceanMinutesPerMapPixel: number;
  readonly options: readonly TransportOptionProjection[];
  readonly wagon: WagonProjection;
}

export type TransportAction =
  | { readonly action: 'transport-select'; readonly mode: Exclude<TransportMode, 'ship'> }
  | { readonly action: 'transport-toggle' }
  | { readonly action: 'transport-leave-ship' }
  | { readonly action: 'transport-board-ship' }
  | { readonly action: 'wagon-put'; readonly revision: string; readonly item: string; readonly amount?: number }
  | { readonly action: 'wagon-take'; readonly revision: string; readonly item: string; readonly amount?: number };

/** A panel the player asked for on a device the DOM has no channel of its own for. */
export interface PanelRequest {
  readonly panel: string;
  readonly revision: string;
}

export interface CompositionIdentity {
  readonly bundle: string;
  readonly ruleset: string;
  readonly contentPacks: readonly string[];
  readonly tuning: string;
}


/** The payload contract every semantic player action travels under. */
export const UI_ACTION_CONTRACT = 'dagger.ui.action.v1';

export function isHud(value: unknown): value is DaggerHud {
  return typeof value === 'object' && value !== null
    && 'resources' in value && Array.isArray(value.resources) && value.resources.every(isResourceRow)
    && 'lastOutcome' in value && typeof value.lastOutcome === 'string'
    && 'messages' in value && Array.isArray(value.messages) && value.messages.every(line => typeof line === 'string')
    && (!('timeControl' in value) || (typeof value.timeControl === 'object' && value.timeControl !== null
      && 'enabled' in value.timeControl && typeof value.timeControl.enabled === 'boolean'
      && 'held' in value.timeControl && typeof value.timeControl.held === 'boolean'))
    && 'composition' in value && isCompositionIdentity(value.composition);
}


export function isRestProjection(value: unknown): value is RestProjection {
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
    && (candidate.message === null || typeof candidate.message === 'string')
    && Number.isSafeInteger(candidate.maximumRestHours) && Number.isSafeInteger(candidate.maximumLoiterHours);
}

export function formatRestHours(seconds: number): string {
  const hours = seconds / 3600;
  return Number.isInteger(hours) ? String(hours) : hours.toFixed(2);
}


export function isBundles(value: unknown): value is BundleProjection {
  return typeof value === 'object' && value !== null && 'current' in value && typeof value.current === 'string'
    && 'options' in value && Array.isArray(value.options) && value.options.every(option => typeof option === 'object' && option !== null
      && 'id' in option && typeof option.id === 'string' && 'title' in option && typeof option.title === 'string');
}

export function isSaveSlots(value: unknown): value is SaveSlotProjection {
  if (typeof value !== 'object' || value === null || !('entries' in value) || !Array.isArray(value.entries)) return false;
  const diagnostic = 'diagnostic' in value ? value.diagnostic : null;
  return (diagnostic === null || typeof diagnostic === 'string') && value.entries.every(entry =>
    typeof entry === 'object' && entry !== null
    && 'key' in entry && typeof entry.key === 'string'
    && 'label' in entry && typeof entry.label === 'string'
    && 'savedAtUtc' in entry && typeof entry.savedAtUtc === 'string'
    && 'ruleset' in entry && typeof entry.ruleset === 'string');
}

export function isDeathProjection(value: unknown): value is DeathProjection {
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

export function viewSummary(view: NonNullable<DaggerHud['view']>): string {
  const direction = ['north', 'north-east', 'east', 'south-east', 'south', 'south-west', 'west', 'north-west']
    [Math.round((((view.yawRadians % (Math.PI * 2)) + Math.PI * 2) % (Math.PI * 2)) / (Math.PI / 4)) % 8]!;
  const pitch = view.pitchRadians > 0.35 ? 'looking up' : view.pitchRadians < -0.35 ? 'looking down' : 'level';
  return `Facing ${direction} · ${pitch}`;
}

export function isResourceRow(value: unknown): value is DaggerHud['resources'][number] {
  return typeof value === 'object' && value !== null
    && 'id' in value && typeof value.id === 'string'
    && 'label' in value && typeof value.label === 'string'
    && 'current' in value && typeof value.current === 'number' && Number.isFinite(value.current)
    && 'maximum' in value && typeof value.maximum === 'number' && Number.isFinite(value.maximum);
}

export function isCompositionIdentity(value: unknown): value is CompositionIdentity {
  return typeof value === 'object' && value !== null
    && 'bundle' in value && typeof value.bundle === 'string'
    && 'ruleset' in value && typeof value.ruleset === 'string'
    && 'contentPacks' in value && Array.isArray(value.contentPacks) && value.contentPacks.every((pack) => typeof pack === 'string')
    && 'tuning' in value && typeof value.tuning === 'string';
}

export function diagnosticRows(identity: CompositionIdentity): readonly HTMLElement[] {
  return [
    diagnosticRow('Bundle', identity.bundle),
    diagnosticRow('Ruleset', identity.ruleset),
    diagnosticRow('Packs', identity.contentPacks.join(' → ')),
    diagnosticRow('Tuning', identity.tuning),
  ];
}

export function diagnosticRow(label: string, value: string): HTMLElement {
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

