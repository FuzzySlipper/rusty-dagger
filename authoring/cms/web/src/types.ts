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

export interface ConceptStageDetail {
  id: string;
  name: string;
  classic: string | null;
  classicUrl: string;
  candidates: ConceptCandidate[];
  approvals?: Record<'front' | 'back', ConceptCandidate | null>;
  conflicts?: Array<'front' | 'back'>;
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

export interface SpawnerDefinition {
  id: string;
  role: string;
  interaction: string;
  harm: string;
  deriveRegionalFaction: boolean;
  visualMode: 'pool' | 'classic-slot';
  defaultPool: SpawnerPoolEntry[];
  regionalPools: RegionalPoolGroup[];
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
  };
  enums: PeoplePackEnums;
  cast: CastIndividual[];
  spawners: SpawnerDefinition[];
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

export interface WorldBlockModel {
  id: string;
  modelId: string;
  meshArtifactId: string | null;
  position: Vec3;
  rotationDegrees: Vec3;
  buildingIndex: number | null;
  unresolvedReason: string | null;
  doorId: string | null;
  action?: { actionId: string; description: string; modelIndex: number; rawIndex: number } | null;
  bounds?: Bounds3 | null;
  localBounds?: Bounds3 | null;
}

export interface WorldBlockDoor {
  id: string;
  modelId: string;
  doorResourceId: string;
  position: Vec3;
  rotationDegrees: Vec3;
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
  rotationDegrees: Vec3;
  open: { modelId: string; meshArtifactId: string; localBounds: Bounds3 };
  closed: { modelId: string; meshArtifactId: string; localBounds: Bounds3 };
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
