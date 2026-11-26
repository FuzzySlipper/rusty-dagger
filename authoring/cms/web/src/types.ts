export type AssetCategory =
  | 'people'
  | 'creatures'
  | 'nature'
  | 'props'
  | 'architecture'
  | 'textures'
  | 'animations'
  | 'audio';

export const ASSET_LABELS: Record<AssetCategory, string> = {
  people: 'People',
  creatures: 'Creatures',
  nature: 'Nature',
  props: 'Props',
  architecture: 'Architecture',
  textures: 'Textures',
  animations: 'Animations',
  audio: 'Audio',
};

export const ASSET_PAGES: AssetCategory[] = [
  'people',
  'creatures',
  'nature',
  'props',
  'architecture',
  'textures',
  'animations',
  'audio',
];

export const STATE_NAMES: Record<string, string> = {
  'needs-Patch': 'Needs review',
  'needs-check': 'Needs check',
  approved: 'Approved',
  empty: 'Empty',
  running: 'Running',
  failed: 'Failed',
  rejected: 'Rejected',
  'on-hold': 'On hold',
  skipped: 'Skipped',
};

export type Rating = 'good' | 'maybe' | 'bad';

export interface ReviewRow {
  category: string;
  id: string;
  marked: boolean;
  rating: Rating | null;
  previousRating?: Rating | null;
  note: string;
  revision: string | null;
  updated: string | null;
  related?: ReviewRow[];
}

export interface ReviewPatch {
  marked?: boolean;
  rating?: Rating | null;
  note?: string;
  expectedRevision?: string | null;
}

export interface BriefInfo {
  brief?: string;
  [key: string]: unknown;
}

export interface PeopleCurationSummary {
  castCount: number;
  castIds: string[];
  spawnerCount: number;
  spawnerIds: string[];
  slotCount: number;
  hasApprovedConcept: boolean;
  hasFinalAsset: boolean;
}

export interface CharacterSummary {
  id: string;
  category: AssetCategory;
  name: string;
  kind: string;
  classic: string | null;
  classicUrl: string;
  brief: BriefInfo | null;
  aliases: string[];
  pipelineIds: string[];
  missingPipelines: string[];
  climate?: string;
  curation?: PeopleCurationSummary;
}

export interface GenericAssetRow {
  id: string;
  category: AssetCategory;
  name: string;
  kind: string;
  preview: string | null;
  facts: Record<string, string | number | boolean>;
  views?: Record<string, string>;
  climate?: string;
  brief?: BriefInfo | null;
  aliases?: string[];
  pipelineIds?: undefined;
}

export type CatalogAssetRow = CharacterSummary | GenericAssetRow;

export function isCharacterRow(row: CatalogAssetRow): row is CharacterSummary {
  return Array.isArray((row as CharacterSummary).pipelineIds);
}

export interface StageRow {
  id: string;
  name: string;
  state: string;
  note: string;
  reviewUrl: string | null;
  selection: string[];
  revision?: string;
}

export interface PipelineConceptArt {
  path: string;
  url: string;
  name: string;
  pipeline: string;
  approved: boolean;
  recorded: boolean;
}

export interface PipelineFinalOutput {
  pipeline: string;
  name: string;
  path: string;
  url: string;
  playable: string | null;
  note: string | null;
}

export interface PipelineSummary {
  id: string;
  name: string;
  stages: StageRow[];
  final: PipelineFinalOutput | null;
  thumbnail: string | null;
  subjects: string[];
}

export interface PipelineDetail {
  id: string;
  name: string;
  recipe: string | null;
  stages: StageRow[];
  concepts: PipelineConceptArt[];
  final: PipelineFinalOutput | null;
  stateUrl: string;
  timelineUrl: string | null;
}

export interface PendingStageReview extends StageRow {
  pipeline: string;
  character: string;
}

export interface ConceptStageSummary {
  id: string;
  name: string;
  count: number;
  approved: string | null;
}

export interface PeoplePackCounts {
  available: boolean;
  castCount: number;
  spawnerCount: number;
  slotCount: number;
  castNeedingArt: number;
}

export type WorldBlockKind = 'rmbExterior' | 'rmbInterior' | 'rdb';

export interface WorldBlocksSummaryCounts {
  available: boolean;
  rmbExterior: number;
  rmbInterior: number;
  rdb: number;
  total: number;
  unplacedRmb: number;
}

export interface LocationsSummaryCounts {
  available: boolean;
  total: number;
  cities: number;
  townsAndVillages: number;
  multiBlock: number;
  withDungeons: number;
}

export interface CatalogOverview {
  people: CharacterSummary[];
  creatures: CatalogAssetRow[];
  nature: GenericAssetRow[];
  props: GenericAssetRow[];
  architecture: GenericAssetRow[];
  textures: GenericAssetRow[];
  animations: GenericAssetRow[];
  audio: GenericAssetRow[];
  categories: Record<string, AssetCategory>;
  pipelines: PipelineSummary[];
  review: PendingStageReview[];
  reviews: ReviewRow[];
  conceptStages: ConceptStageSummary[];
  peoplePack?: PeoplePackCounts;
  worldBlocks?: WorldBlocksSummaryCounts;
  locations?: LocationsSummaryCounts;
}

export interface LinkChoice {
  pipeline: string;
  path: string;
}

export interface ConceptCandidate {
  id: string;
  subject: string;
  batch: string;
  name: string;
  url: string;
  details: {
    prompt: string;
    seed: string | null;
    model?: string;
    [key: string]: unknown;
  };
  createdAt: string;
  approvedAt?: string;
}

export type ConceptView = 'front' | 'left' | 'right' | 'back';

export interface ConceptStageDetail {
  id: string;
  name: string;
  classic: string | null;
  classicUrl: string;
  candidates: ConceptCandidate[];
  approvals?: Record<ConceptView, ConceptCandidate | null>;
  conflicts?: ConceptView[];
  approved: ConceptCandidate | null;
  conflict: boolean;
}

export interface CharacterGameData {
  facts: Record<string, string | number | boolean>;
  placementCount?: number;
  url?: string;
  record?: Record<string, unknown>;
}

export interface CharacterPeoplePackUsage {
  visualKeys: Array<{ archive: number; record: number; key: string }>;
  cast: Array<{
    id: string;
    name: string;
    race: string | null;
    gender: string;
    factionId: number;
    factionName: string | null;
    homeBlock: string;
    homeSlotId: string;
    interaction: string;
    harm: string;
    visual: { archive: number; record: number };
  }>;
  spawners: Array<{
    spawnerId: string;
    role: string;
    pool: string;
    regionName: string | null;
    weight: number;
    gender: string;
    archive: number;
    record: number;
  }>;
  slotCount: number;
}

export interface CharacterActorRecords {
  spawners: Array<{
    id: string;
    family: string;
    displayName: string;
    template: string | null;
    published: boolean;
  }>;
  templates: Array<{
    id: string;
    branch: string;
    extends: string | null;
  }>;
}

export interface CharacterDetail extends CharacterSummary {
  pipelines: PipelineDetail[];
  automaticPipelines: string[];
  concept: PipelineConceptArt | null;
  conceptChoices: PipelineConceptArt[];
  final: PipelineFinalOutput | null;
  finalChoices: PipelineFinalOutput[];
  explicitConcept: LinkChoice | null;
  explicitFinal: LinkChoice | null;
  conflicts: Array<'concept' | 'final'>;
  game: CharacterGameData;
  turnaround: string | null;
  turnaroundStill: string | null;
  conceptStage?: {
    count: number;
    approved: ConceptCandidate | null;
    conflict: boolean;
  };
  peoplePack?: CharacterPeoplePackUsage;
  actorRecords?: CharacterActorRecords;
}

export interface ActorArtLink {
  id: string;
  category: AssetCategory;
  name: string;
  kind: string;
  classic: string | null;
  conceptUrl: string | null;
  hasApprovedConcept: boolean;
  hasFinalAsset: boolean;
  role?: string;
  mobileId?: number;
  gender?: string;
  visualKey?: string;
}

export interface ActorFieldProvenance {
  field: string;
  value: unknown;
  definedBy: string;
  status: 'local' | 'inherited' | 'overridden' | 'cleared' | 'career-default';
  careerRef?: string | null;
  previousValue?: unknown;
  previousDefinedBy?: string | null;
}

export interface ActorStatProvenance {
  stat: string;
  value: number | null;
  definedBy: string;
  status: 'local' | 'inherited' | 'overridden' | 'cleared' | 'career-default';
  careerRef?: string | null;
  previousValue?: number | null;
  previousDefinedBy?: string | null;
}

export interface ActorBoardSeatRef {
  seatId: string;
  seatName: string;
  kind: string | null;
  title: string | null;
  officeFactionId: number | null;
  officeFactionName: string | null;
  home: string | null;
  canonicalHolder: string | null;
  spawnerId?: string;
}

export interface ActorTemplateSummary {
  id: string;
  branch: string;
  extends: string | null;
  chain: string[];
  depth: number;
  directChildrenCount: number;
  directSpawnerCount: number;
  descendantSpawnerCount: number;
  scaling: string | null;
  careerRef: string | null;
  tactic: string | null;
  spellPolicy: string | null;
  level: number | null;
  health: { minimum: number; maximum: number } | null;
  castsMagic: boolean;
}

export interface ActorSpecSummary {
  id: string;
  family: string;
  template: string | null;
  displayName: string;
  role: string | null;
  published: boolean;
  harm: string | null;
  interaction: string | null;
  race: string | null;
  team: string | null;
  factionId: number | null;
  mobileId: number | null;
  primaryArt: ActorArtLink | null;
  artCount: number;
  encounterTableCount: number;
  blockSlotCount: number;
  boardSeatCount: number;
  editorHref: string | null;
}

export interface ActorsOverview {
  available: boolean;
  counts: {
    templates: number;
    specs: number;
    byFamily: Record<string, number>;
    byBranch: Record<string, number>;
  };
  enums: {
    branches: string[];
    families: string[];
    genders: string[];
    regions: Array<{ index: number; name: string }>;
    races: string[];
  };
  templates: ActorTemplateSummary[];
  specs: ActorSpecSummary[];
}

export interface ActorTemplateDetail {
  id: string;
  branch: string;
  extends: string | null;
  chain: Array<{ id: string; branch: string }>;
  depth: number;
  directChildren: Array<{ id: string; branch: string; directSpawnerCount: number }>;
  fields: ActorFieldProvenance[];
  attributeRows: ActorStatProvenance[];
  skillRows: ActorStatProvenance[];
  resolved: Record<string, unknown>;
  reverseRefs: {
    spawners: Array<{
      id: string;
      family: string;
      displayName: string;
      published: boolean;
      blockSlotCount: number;
      encounterTableCount: number;
      primaryArt: ActorArtLink | null;
    }>;
    encounterTables: Array<{ tableIndex: number; slots: number }>;
    blockSlotCount: number;
    boardSeats: ActorBoardSeatRef[];
  };
  artLinks: ActorArtLink[];
}

export interface ActorSpecDetail {
  id: string;
  family: string;
  displayName: string;
  role: string | null;
  template: string | null;
  templateChain: Array<{ id: string; branch: string }>;
  published: boolean;
  genders: Array<{ gender: string; weight: number }>;
  race: string | null;
  naming: string | null;
  harm: string | null;
  interaction: string | null;
  team: string | null;
  factionId: number | null;
  factionName: string | null;
  home: { block?: string; slotId?: string; position?: number[]; site?: { region: number; index: number } } | null;
  familyId: string | null;
  visual: { archive: number; record: number } | null;
  visualMode: string | null;
  appearances: Record<string, unknown> | null;
  sounds: Record<string, unknown> | null;
  classicBinding: Record<string, unknown> | null;
  deriveRegionalFaction: boolean | null;
  defaultPoolCount: number;
  regionalPoolCount: number;
  climatePoolCount: number;
  editorHref: string | null;
  templateFields: ActorFieldProvenance[];
  attributeRows: ActorStatProvenance[];
  skillRows: ActorStatProvenance[];
  reverseRefs: {
    encounterTables: Array<{ tableIndex: number; slots: number; totalSlots: number; share: number }>;
    blockSlots: {
      slotCount: number;
      blockCount: number;
      blocks: string[];
      byKind: Record<string, number>;
      byBuildingType: Array<{ buildingType: number; name: string | null; count: number }>;
    };
    offices: Array<{
      factionId: number;
      factionName: string | null;
      region: number | null;
      parentFactionId: number | null;
      parentFactionName: string | null;
    }>;
    boardSeats: ActorBoardSeatRef[];
  };
  artLinks: ActorArtLink[];
}

export interface ActorPreviewResult {
  kind: 'spec' | 'template';
  id: string;
  templateId: string | null;
  inputs: {
    level: number;
    gender: string;
    allowedGenders: string[];
    region: number | null;
    regionName: string | null;
  };
  effective: {
    level: number;
    scaling: string;
    race: string;
    raceSource: string;
    health: { minimum: number; maximum: number };
    hitPointsPerLevel: number | null;
    fatigue: number | null;
    magicka: number | null;
    armor: number;
    attributes: Record<string, number>;
    skills: Record<string, number>;
    attacks: Array<{ minimumDamage: number; maximumDamage: number }>;
    loadout: Array<{ item?: string; itemId?: string; quantity: number }>;
    tactic: string | null;
    tendencies: Record<string, number> | null;
    spellPolicy: string;
    spells: Array<{ key: string; name: string; identity: number | null; tier?: number }>;
    harm: string | null;
    interaction: string | null;
    team: string | null;
    factionId: number | null;
    factionName: string | null;
    home: { block?: string; slotId?: string; position?: number[]; site?: { region: number; index: number } } | null;
    visual: {
      mode: string;
      source?: string;
      textureArchive?: number | null;
      art?: ActorArtLink | null;
      entries: Array<{
        archive: number;
        record: number;
        gender: string;
        weight: number;
        share: number;
        art: VisualArtInfo;
      }>;
    };
  };
  notes: string[];
}

export interface VisualArtInfo {
  archive: number;
  record: number;
  key: string;
  npcId: string | null;
  npcName: string | null;
  npcKind: string | null;
  classic: string | null;
  conceptUrl: string | null;
  hasApprovedConcept: boolean;
  hasFinalAsset: boolean;
  pipelineCount: number;
}

export interface VisualOption extends VisualArtInfo {
  castCount: number;
  spawnerCount: number;
  slotCount: number;
}

export interface BlockSlotItem {
  kind: 'rmb-interior' | 'rmb-exterior' | 'rdb-dungeon';
  block: string;
  buildingIndex: number | null;
  buildingType: number | null;
  buildingTypeName: string | null;
  buildingFactionId: number | null;
  slotId: string;
  castId: string | null;
  spawner: string | null;
  position: [number, number, number];
  interaction: string;
  effectiveHarm: string | null;
  factionId: number | null;
  factionName: string | null;
  deriveRegionalFaction: boolean;
  visual: { archive: number; record: number; gender: string };
  art?: VisualArtInfo;
}

export interface CastIndividual {
  id: string;
  factionId: number;
  factionName: string | null;
  name: string;
  race: string | null;
  gender: 'Male' | 'Female';
  visual: { archive: number; record: number };
  homeBlock: string;
  homeSlotId: string;
  interaction: string;
  harm: string;
  art: VisualArtInfo;
  usage: { slotCount: number; blocks: string[] };
  sharedVisualWith: string[];
  needsConceptArt: boolean;
  needsFinalArt: boolean;
  slots?: BlockSlotItem[];
}

export interface SpawnerPoolEntry {
  archive: number;
  record: number;
  gender: 'Male' | 'Female';
  weight: number;
  share: number;
  art: VisualArtInfo;
}

export interface RegionalPoolGroup {
  regionKey: string;
  canonicalKey: string;
  regionIndex: number | null;
  regionName: string;
  entries: SpawnerPoolEntry[];
}

export interface ClimatePoolGroup {
  climate: string;
  entries: SpawnerPoolEntry[];
}

export interface SpawnerDefinition {
  id: string;
  role: string;
  archetype?: string | null;
  interaction: string;
  harm: string;
  deriveRegionalFaction: boolean;
  visualMode: 'pool' | 'classic-slot';
  defaultPool: SpawnerPoolEntry[];
  regionalPools: RegionalPoolGroup[];
  climatePools: ClimatePoolGroup[];
  usage: {
    slotCount: number;
    blockCount: number;
    byKind: Record<string, number>;
    byBuildingType: Array<{ buildingType: number; name: string; count: number }>;
  };
}

export interface RegionOption {
  index: number;
  key: string;
  name: string;
}

export interface PeoplePackEnums {
  genders: Array<'Male' | 'Female'>;
  interactions: string[];
  harms: string[];
  visualModes: Array<'pool' | 'classic-slot'>;
  races: string[];
  climates: string[];
  buildingTypes: Array<{ id: number; name: string }>;
  regions: RegionOption[];
}

export interface PeoplePackOverview {
  available: boolean;
  counts: {
    cast: number;
    spawners: number;
    slots: number;
    castNeedingConceptArt: number;
    castNeedingFinalArt: number;
    castSharingVisual: number;
    spawnersSingleVisual: number;
    spawnersWithRegionalPools: number;
    spawnersWithClimatePools: number;
  };
  enums: PeoplePackEnums;
  cast: CastIndividual[];
  spawners: SpawnerDefinition[];
}

export interface ReferralTemplatesDocument {
  defaultIssueTemplates: string[];
  issueTemplatesByClimate?: Record<string, string[]>;
  issueTemplatesByRegion?: Record<string, string[]>;
  defaultResponseTemplates: string[];
  responseTemplatesByRole?: Record<string, string[]>;
  contactNames: string[];
  contactNamesByClimate?: Record<string, string[]>;
  contactNamesByRegion?: Record<string, string[]>;
}

export interface BarksDocument {
  defaultBarks: string[];
  byRole?: Record<string, string[]>;
  byArchetype?: Record<string, string[]>;
  byRegion?: Record<string, string[]>;
  byClimate?: Record<string, string[]>;
  referrals: ReferralTemplatesDocument;
}

export interface BlockSlotsResponse {
  total: number;
  page: number;
  pageSize: number;
  items: BlockSlotItem[];
  summary: {
    blockCount: number;
    byKind: Record<string, number>;
    byBuildingType: Array<{ buildingType: number; name: string; count: number }>;
  };
}

export interface StageAssetItem {
  id: string;
  name: string;
  url: string;
  media: 'image' | 'video' | 'model';
  revision: string;
}

export interface StageReviewDetail {
  pipeline: string;
  character: string;
  stage: StageRow & { revision: string };
  assets: StageAssetItem[];
}

export interface ValidationIssue {
  field: string;
  message: string;
}

export interface PeopleCompareItem {
  id: string;
  name: string;
  kind: string;
  classic: string | null;
  classicUrl: string;
  brief: BriefInfo | null;
  aliases: string[];
  pipelineIds: string[];
  concept: PipelineConceptArt | null;
  final: PipelineFinalOutput | null;
  conceptStage: {
    count: number;
    front: ConceptCandidate | null;
    back: ConceptCandidate | null;
  };
  review: ReviewRow;
  placementCount: number;
  peoplePack: CharacterPeoplePackUsage;
}

export interface PeopleCompareResponse {
  items: PeopleCompareItem[];
  warnings: string[];
}

export interface PeopleReferencesResponse {
  id: string;
  name: string;
  kind: string;
  classic: string | null;
  canRetire: boolean;
  requiresConfirmation: boolean;
  blockers: string[];
  warnings: string[];
  mergedMembers: string[];
  peoplePack: CharacterPeoplePackUsage;
  pipelines: string[];
  conceptCount: number;
  hasApprovedConcept: boolean;
  hasFinalAsset: boolean;
  review: ReviewRow;
}

export interface Vec3 {
  x: number;
  y: number;
  z: number;
}

/** A unit rotation in the published right-handed frame, as THREE.Quaternion holds it. */
export interface Quat {
  x: number;
  y: number;
  z: number;
  w: number;
}

export interface Bounds3 {
  minimum: Vec3;
  maximum: Vec3;
}

export interface WorldBlockListItem {
  key: string;
  kind: WorldBlockKind;
  sourceKey: string;
  buildingIndex: number | null;
  interiorCount: number;
  peopleSlotCount: number;
  totalPeopleSlotCount: number;
  hasSpatial: boolean;
}

export interface WorldBlocksQueryResponse {
  available: boolean;
  reason?: string;
  counts: {
    rmbExterior: number;
    rmbInterior: number;
    rdb: number;
    total: number;
    unplacedRmb: number;
  };
  total: number;
  offset: number;
  limit: number;
  items: WorldBlockListItem[];
}

export interface WorldBlockCollision {
  vertices: Vec3[];
  triangles: Array<{ a: number; b: number; c: number }>;
}

export interface WorldBlockModel {
  id: string;
  modelId: string;
  meshArtifactId: string | null;
  position: Vec3;
  rotation: Quat;
  buildingIndex: number | null;
  unresolvedReason: string | null;
  doorId: string | null;
  action?: { actionId: string; description: string; modelIndex: number; rawIndex: number } | null;
  bounds?: Bounds3 | null;
  localBounds?: Bounds3 | null;
  collision?: WorldBlockCollision | null;
}

export interface WorldBlockDoor {
  id: string;
  modelId: string;
  doorResourceId: string;
  position: Vec3;
  rotation: Quat;
  plane?: number | null;
  texture?: { archive: number; record: number } | null;
  kind: string;
  startingLockValue: number;
  buildingIndex?: number | null;
  collisionBounds?: Bounds3 | null;
}

export interface WorldBlockTransitionDoor {
  id: string;
  modelId: string;
  kind: string;
  position: Vec3;
  normal: Vec3;
  bounds: Bounds3;
  plane: number;
  texture: { archive: number; record: number };
}

export interface WorldBlockGate {
  id: string;
  modelId: string;
  position: Vec3;
  rotation: Quat;
  open: { modelId: string; meshArtifactId: string; localBounds: Bounds3; collision?: WorldBlockCollision | null };
  closed: { modelId: string; meshArtifactId: string; localBounds: Bounds3; collision?: WorldBlockCollision | null };
}

export interface WorldBlockBuilding {
  index: number;
  buildingType: number;
  buildingTypeName?: string | null;
  factionId: number;
  quality: number;
  nameSeed: number;
  hasInterior: boolean;
  interiorKey?: string | null;
}

export interface WorldBlockGroundTile {
  x: number;
  y: number;
  textureRecord: number;
  rotated: boolean;
  flipped: boolean;
}

export interface WorldBlockStaticNpc {
  id: string;
  position: Vec3;
  billboardArchive: number;
  billboardRecord: number;
  factionId: number;
  race: string | null;
  gender: string;
  sourceOffset: number;
  npcId?: string | null;
  npcName?: string | null;
  classic?: string | null;
}

export interface WorldBlockBillboard {
  id: string;
  spriteResourceId: string;
  position: Vec3;
  size: { x: number; y: number };
  baseAligned?: boolean;
  climateNature?: boolean;
  lamp?: boolean;
  light?: string | null;
}

export interface WorldBlockLight {
  id: string;
  position: Vec3;
  range: number;
  intensity: number;
  color?: Vec3 | null;
  cityLight?: boolean;
}

export interface WorldBlockMarker {
  id: string;
  position: Vec3;
  kind?: string;
  sourceOrdinal?: number;
}

export interface WorldBlockRandomEnemy {
  id: string;
  position: Vec3;
  encounterSlot: number;
  spawnDistance: number;
  passive: boolean;
  actionId?: string | null;
  underwater?: boolean;
  aquaticSpawnAllowed?: boolean;
}

export interface WorldBlockPropertyContainer {
  id: string;
  position: Vec3;
  itemGroups?: string[];
}

export interface WorldBlockAction {
  id?: string;
  actionId?: string;
  sourceOffset?: number;
  triggerFlag?: number;
  actionFlag?: number;
  axis?: number;
  duration?: number;
  magnitude?: number;
  nextActionId?: string | null;
  doorId?: string | null;
  isFlat?: boolean;
  position?: Vec3 | null;
  nextObjectPosition?: Vec3 | null;
  [key: string]: unknown;
}

export interface WorldBlockDocument {
  key: string;
  kind: WorldBlockKind;
  sourceKey: string;
  sourceOrdinal: number;
  buildingIndex: number | null;
  bounds: Bounds3 | null;
  sourceWarnings?: string[];
  models: WorldBlockModel[];
  doors?: WorldBlockDoor[];
  transitionDoors?: WorldBlockTransitionDoor[];
  gates?: WorldBlockGate[];
  buildings?: WorldBlockBuilding[];
  interiorBuilding?: WorldBlockBuilding | null;
  ground?: WorldBlockGroundTile[];
  staticNpcs?: WorldBlockStaticNpc[];
  billboards?: WorldBlockBillboard[];
  lights?: WorldBlockLight[];
  questMarkers?: WorldBlockMarker[];
  randomEnemies?: WorldBlockRandomEnemy[];
  treasures?: WorldBlockMarker[];
  propertyContainers?: WorldBlockPropertyContainer[];
  actions?: WorldBlockAction[];
  startMarker?: WorldBlockMarker | null;
  startMarkers?: WorldBlockMarker[];
  enterMarker?: WorldBlockMarker | null;
  waterLevel?: number | null;
  ambientZone?: string | null;
}

export interface WorldBlockPeopleSlot extends BlockSlotItem {
  castName?: string | null;
}

export interface WorldBlockDetailResponse {
  block: WorldBlockDocument;
  parentExteriorKey: string | null;
  interiorKeys: Record<string, string>;
  peopleSlots: WorldBlockPeopleSlot[];
  modelLinks: Record<string, { id: string; name: string; category: AssetCategory }>;
  terrainArchive: number;
  mediaUrls: {
    geometry: string;
    materials: string;
    billboards: string;
    terrain: string;
  };
}

export type ArtMode = 'classic' | 'refreshed';
export type SeasonName = 'summer' | 'winter' | 'rain';

export interface ClimateOption {
  value: number;
  name: string;
  climateBase: string;
  ground: Record<SeasonName, number>;
  nature: Record<SeasonName, number>;
}

export interface DungeonPreset {
  id: string;
  dungeonType: number;
  name: string;
  remaps: Record<string, number>;
}

export interface ArtReplacementModelSlot {
  material?: string;
  textureUrl?: string;
}

export interface ArtReplacementModel {
  meshUrl: string;
  slots: Record<string, ArtReplacementModelSlot>;
}

export interface WorldAppearanceData {
  climates: ClimateOption[];
  seasons: SeasonName[];
  swaps: Record<string, Record<string, Record<string, Array<[number, number, number]>>>>;
  dungeonPresets: DungeonPreset[];
  artReplacements: {
    available: boolean;
    surfaces: Record<string, string>;
    scenery: Record<string, string>;
    models: Record<string, ArtReplacementModel>;
    counts: { surfaces: number; scenery: number; models: number };
  };
}

export interface WorldBlockBatchResponse {
  available: boolean;
  blocks: Record<string, WorldBlockDetailResponse>;
  missing: string[];
  terrainArchive: number;
  mediaUrls: {
    geometry: string;
    materials: string;
    billboards: string;
    terrain: string;
  };
}

export interface LocationListItem {
  region: number;
  regionName: string;
  index: number;
  name: string;
  locationType: number | null;
  locationTypeName: string;
  climate: number | null;
  mapPixelX: number | null;
  mapPixelY: number | null;
  width: number;
  height: number;
  exteriorBlockCount: number;
  uniqueExteriorBlocks: number;
  hasDungeon: boolean;
  dungeonBlockCount: number;
  dungeonType: number | null;
  dungeonTypeName: string | null;
  buildingTypes: number[];
  discovered: boolean;
}

export interface LocationsQueryResponse {
  available: boolean;
  reason?: string;
  counts: {
    total: number;
    cities: number;
    townsAndVillages: number;
    multiBlock: number;
    withDungeons: number;
  };
  regions: Array<{ index: number; name: string; count: number }>;
  locationTypes: Array<{ id: number; name: string; count: number }>;
  total: number;
  offset: number;
  limit: number;
  items: LocationListItem[];
}

export interface LocationExteriorBlockPlacement {
  x: number;
  y: number;
  sourceName: string;
  blockKey: string | null;
  interiorCount: number;
  peopleSlotCount: number;
}

export interface LocationDungeonBlockPlacement {
  x: number;
  z: number;
  sourceKey: string;
  blockKey: string | null;
  start: boolean;
}

export interface LocationDetailResponse {
  location: LocationListItem & {
    mapId?: number | null;
    longitude?: number | null;
    latitude?: number | null;
    buildingTypeLabels: Array<{ id: number; name: string | null }>;
  };
  exterior: {
    locationId?: number | null;
    width: number;
    height: number;
    blocks: LocationExteriorBlockPlacement[];
    uniqueBlockKeys: string[];
  };
  dungeon: {
    dungeonLocationId?: number | null;
    dungeonType: number | null;
    dungeonTypeName: string | null;
    blockCount: number;
    uniqueBlocks: number;
    placements: LocationDungeonBlockPlacement[];
    textureTable?: { archives: number[]; remaps: Array<{ archive: number; targetArchive: number }> } | null;
  } | null;
  worldBlocksAvailable: boolean;
  terrainArchive: number;
  mediaUrls: {
    geometry: string;
    materials: string;
    billboards: string;
    terrain: string;
  };
}

export interface WorldBlockVariantModel {
  model: string;
  position: Vec3;
  yawDegrees: number;
}

export interface WorldBlockVariantRecipe {
  packId: string;
  block: string;
  variant: string;
  remove: string[];
  add: WorldBlockVariantModel[];
  bakedExists: boolean;
  bakedPath: string;
  baseDigest: string | null;
  bakedModelCount: number | null;
}

export interface WorldVariantsResponse {
  packs: Array<{ id: string; variantCount: number }>;
  variants: WorldBlockVariantRecipe[];
  arena2Available: boolean;
}

export interface Arch3dModelOption {
  modelId: string;
  meshArtifactId: string;
  name: string | null;
  category: AssetCategory | null;
  cmsId: string | null;
  hasRemodel: boolean;
}

export type AtlasBaseMode = 'painted' | 'classic';
export type AtlasOverlayMode = 'geography' | 'political' | 'diplomacy' | 'climate' | 'power' | 'density';

export interface AtlasLevelInfo {
  level: number;
  width: number;
  height: number;
  columns: number;
  rows: number;
}

export interface ClassicMapRect {
  region?: number;
  url: string;
  width: number;
  height: number;
  left: number;
  top: number;
  right: number;
  bottom: number;
}

export interface AtlasClimateInfo {
  index: number;
  value: number;
  name: string;
  climateBase: string;
  people: string;
  color: string;
}

export interface AtlasPinCategory {
  id: string;
  label: string;
  types: number[];
  color: string;
}

export interface AtlasDiplomaticEdge {
  sourceFactionId: number;
  sourceFactionName: string;
  targetFactionId: number;
  targetFactionName: string;
  targetRegion: number | null;
  targetRegionName: string | null;
}

export interface WorldAtlasRegion {
  id: number;
  name: string;
  hasTerritory: boolean;
  pixelCount: number;
  bounds: { minX: number; minY: number; maxX: number; maxY: number } | null;
  centroid: { x: number; y: number };
  province: {
    id: number;
    name: string;
    power: number;
    rulerCode: number;
    rulerTitle: string | null;
    regentName: string | null;
  } | null;
  court: { id: number; name: string; power: number } | null;
  people: { id: number; name: string; power: number } | null;
  factions: Array<{
    id: number;
    name: string;
    type: number;
    typeName: string;
    power: number;
    parent: number;
    castId: string | null;
  }>;
  diplomacy: {
    allies: AtlasDiplomaticEdge[];
    enemies: AtlasDiplomaticEdge[];
    allyRegions: number[];
    enemyRegions: number[];
  };
  climates: Array<{
    index: number;
    value: number;
    name: string;
    color: string;
    pixels: number;
    share: number;
  }>;
  dominantClimate: string | null;
  locations: {
    total: number;
    cities: number;
    townsAndVillages: number;
    temples: number;
    dungeons: number;
    withDungeons: number;
    other: number;
  };
  peoplePack: {
    cast: Array<{ id: string; name: string }>;
    regionalSpawners: string[];
  };
}

export interface WorldAtlasOverviewResponse {
  available: boolean;
  reason?: string;
  atlas: {
    available: boolean;
    tileBaseUrl: string;
    tileSize: number;
    worldWidth: number;
    worldHeight: number;
    levels: AtlasLevelInfo[];
  };
  classicMaps: {
    available: boolean;
    world: ClassicMapRect;
    sheets: ClassicMapRect[];
  };
  worldNames: Array<{ id: string; name: string; kind: string; x: number; y: number }>;
  climates: AtlasClimateInfo[];
  pinCategories: AtlasPinCategory[];
  regions: WorldAtlasRegion[];
  counts: {
    regions: number;
    inhabitedRegions: number;
    locations: number;
    diplomaticLinks: number;
  };
}

export interface WorldAtlasMasksResponse {
  width: number;
  height: number;
  politicRuns: number[];
  climateRuns: number[];
  climates: AtlasClimateInfo[];
}

/** [region, index, mapPixelX, mapPixelY, locationType, hasDungeonInt, name, climateValue] */
export type WorldAtlasPinTuple = [number, number, number, number, number, number, string, number | null];

export interface WorldAtlasPinsResponse {
  categories: AtlasPinCategory[];
  total: number;
  pins: WorldAtlasPinTuple[];
}
