export interface CharacterAction { readonly action: string; readonly question?: number; readonly answer?: number; readonly name?: string; readonly race?: string; readonly gender?: string; readonly faceIndex?: number; readonly reflexes?: number; readonly career?: string; readonly primarySkills?: string; readonly majorSkills?: string; readonly minorSkills?: string; readonly hitPointsPerLevel?: number; readonly advantages?: string; readonly disadvantages?: string; readonly attribute?: string; readonly backgroundAnswers?: string; readonly attributeAllocations?: string; readonly skillAllocations?: string; }

export interface CharacterStat {
  readonly id: string;
  readonly label: string;
  readonly value: number;
  readonly permanent: number;
}

export interface CharacterResource {
  readonly id: string;
  readonly label: string;
  readonly current: number;
  readonly maximum: number;
}

export interface CharacterProgression {
  readonly level: number;
  readonly experience: number;
  readonly skillProgress?: number | null;
  readonly nextLevelSkillProgress?: number | null;
  readonly pendingLevelUp?: boolean;
}

export interface CharacterAffiliation {
  readonly faction: string;
  readonly guildGroup: string;
  readonly rank: number;
  readonly reputation: number;
  readonly recognition: number;
  readonly currentRequirement?: GuildRankRequirement | null;
  readonly nextRequirement?: GuildRankRequirement | null;
  readonly daysUntilReview?: number | null;
  readonly privileges?: readonly string[];
  /** The rank's title in this guild, from the ruleset; null outside a concrete guild. */
  readonly rankTitle?: string | null;
}

/** The player's legal standing in one region; the label is the ruleset's own wording. */
export interface CharacterLegalStanding {
  readonly region: number;
  readonly regionName: string;
  readonly reputation: number;
  readonly standing: string;
  readonly banished: boolean;
  readonly current: boolean;
  readonly label: string;
}

interface GuildRankRequirement { readonly rank: number; readonly reputation: number; readonly highSkill: number; readonly lowSkill: number; }

export interface CharacterHistory { readonly biography: readonly string[]; }

export interface CharacterEquipment {
  readonly label: string;
  readonly slots: readonly string[];
  readonly details: string;
  readonly condition?: { readonly current: number; readonly maximum: number; readonly percentage: number; readonly broken: boolean; } | null;
  readonly identified?: boolean;
}

export interface CharacterIdentity {
  readonly racialOverride?: { readonly name: string; readonly vampireClan?: string | null; readonly beastForm: boolean; readonly suppressInventory: boolean } | null;
  readonly race: string;
  readonly raceLabel: string;
  readonly donorRaceId: number;
  readonly portrait: string;
  readonly gender: string;
  readonly faceIndex: number;
  readonly career: string;
  readonly media: readonly CharacterMedia[];
  readonly selectedMedia: readonly CharacterMedia[];
}

export interface CharacterMedia { readonly layer: string; readonly mediaId: string; }
export interface CharacterGrantedSkill { readonly id: string; readonly tier: string; readonly label: string; readonly tierLabel: string; }
export interface CharacterChoice { readonly id: string; readonly label: string; readonly available: boolean; readonly restriction: string | null; }
export interface CharacterFace { readonly index: number; readonly mediaId: string; }
export interface CharacterReflex { readonly value: number; readonly label: string; }
export interface CharacterClassQuiz { readonly answered: number; readonly total: number; readonly question: { readonly number: number; readonly text: string; readonly answers: readonly { readonly index: number; readonly text: string }[] }; }
export interface CharacterCreation {
  readonly summary?: readonly string[] | null;
  readonly mode?: string | null;
  readonly classQuestionsAvailable?: boolean;
  readonly classQuiz?: CharacterClassQuiz | null;
  readonly editing: boolean;
  readonly current: { readonly name: string; readonly race: string; readonly gender: string; readonly faceIndex: number; readonly reflexes: number; readonly career: string; };
  readonly races: readonly CharacterChoice[]; readonly careers: readonly CharacterChoice[];
  readonly faces: readonly CharacterFace[]; readonly reflexes: readonly CharacterReflex[];
  readonly custom?: CharacterCustomClass | null;
  readonly background?: CharacterBackground | null;
}
export interface CharacterBackgroundAnswer { readonly letter: string; readonly text: string; }
export interface CharacterBackgroundQuestion { readonly number: number; readonly text: string; readonly selectedLetter: string | null; readonly answers: readonly CharacterBackgroundAnswer[]; }
export interface CharacterBackgroundAttribute { readonly id: string; readonly label: string; readonly rolled: number; readonly allocated: number; readonly value: number; readonly maximum: number; readonly canAllocate: boolean; }
export interface CharacterBackgroundSkill { readonly id: string; readonly label: string; readonly tier: string; readonly rolled: number; readonly allocated: number; readonly biographyBonus: number; readonly value: number; readonly canAllocate: boolean; }
export interface CharacterStartingGrant { readonly itemId: string; readonly label: string; readonly templateIndex: number; readonly quantity: number; readonly sourceEffect: string; }
export interface CharacterBackground { readonly biographyClassIndex: number; readonly biography: readonly string[]; readonly questions: readonly CharacterBackgroundQuestion[]; readonly attributes: readonly CharacterBackgroundAttribute[]; readonly attributeBonusPool: number; readonly remainingAttributePoints: number; readonly skills: readonly CharacterBackgroundSkill[]; readonly primarySkillPoints: number; readonly majorSkillPoints: number; readonly minorSkillPoints: number; readonly startingGrants: readonly CharacterStartingGrant[]; readonly unsupportedEffects: readonly string[]; }
export interface CharacterSkillChoice { readonly id: string; readonly label: string; }
export interface CharacterCustomTrait { readonly id: string; readonly target: string | null; }
export interface CharacterCustomClass {
  readonly name: string; readonly primarySkills: readonly string[]; readonly majorSkills: readonly string[]; readonly minorSkills: readonly string[];
  readonly hitPointsPerLevel: number; readonly advantages: readonly CharacterCustomTrait[]; readonly disadvantages: readonly CharacterCustomTrait[];
  readonly eligibility: readonly string[]; readonly skills: readonly CharacterSkillChoice[]; readonly supportedAdvantages: readonly string[]; readonly supportedDisadvantages: readonly string[];
}
export interface CharacterLevelUpAttribute { readonly id: string; readonly label: string; readonly permanent: number; readonly live: number; readonly pending: number; readonly canAllocate: boolean; }
export interface CharacterLevelUp { readonly title: string; readonly level: number; readonly bonusPool: number; readonly remainingPoints: number; readonly healthGain: number; readonly canCommit: boolean; readonly attributes: readonly CharacterLevelUpAttribute[]; }

/** Read-only Daggerfall sheet values supplied by the C# projection. */
export interface CharacterProjection {
  readonly name: string;
  readonly attributes: readonly CharacterStat[];
  readonly skills: readonly CharacterStat[];
  readonly resources: readonly CharacterResource[];
  readonly progression: CharacterProgression;
  readonly equipment: readonly CharacterEquipment[];
  readonly resistances?: readonly CharacterStat[];
  readonly affiliations?: readonly CharacterAffiliation[];
  readonly legalStandings?: readonly CharacterLegalStanding[];
  readonly history?: CharacterHistory | null;
  readonly identity?: CharacterIdentity | null;
  readonly grantedSkills?: readonly CharacterGrantedSkill[];
  readonly creation?: CharacterCreation | null;
  readonly creationAvailable?: boolean;
  readonly levelUp?: CharacterLevelUp | null;
}

import { heldRevision, image } from './art.js';
import { redraw } from './redraw.js';

export interface CharacterView {
  readonly creationElement: HTMLElement;
  update(value: CharacterProjection): void;
  refresh(): void;
  dispose(): void;
}

export function mountCharacter(root: HTMLElement, send?: (action: CharacterAction) => void): CharacterView {
  const shell = document.createElement('section');
  shell.className = 'dagger-character';
  shell.dataset.testid = 'character-sheet';
  const chrome = document.createElement('img');
  chrome.className = 'dagger-character-chrome';
  chrome.alt = '';
  chrome.setAttribute('aria-hidden', 'true');
  // The sheet's chrome is published art, so a session that has it sets it and one that does not
  // leaves the element empty until the snapshot that carries it arrives.
  const chromeArt = image('window.character-sheet.chrome');
  if (chromeArt !== null) chrome.src = chromeArt;
  const heading = document.createElement('header');
  heading.className = 'dagger-character-title dagger-frame-title';
  const eyebrow = document.createElement('p');
  eyebrow.textContent = 'Live player profile';
  const title = document.createElement('h2');
  title.textContent = 'Character sheet';
  heading.append(eyebrow, title);
  const overview = document.createElement('section');
  overview.className = 'dagger-character-overview';
  overview.setAttribute('aria-label', 'Player progression');
  const resources = section('Live condition');
  const attributes = section('Attributes');
  const skills = section('Skills');
  const resistances = section('Resistances');
  const affiliations = section('Affiliations');
  const legal = section('Legal standing');
  const history = section('History');
  const equipment = section('Equipped items');
  const portrait = document.createElement('div'); portrait.className = 'dagger-character-art';
  const career = section('Career training');
  const levelUp = section('Level up');
  const creation = section('Character choices');
  const columns = document.createElement('div');
  columns.className = 'dagger-character-columns';
  columns.append(resources.element, attributes.element, skills.element, resistances.element, affiliations.element, legal.element, history.element, career.element, equipment.element, levelUp.element, creation.element);
  shell.append(chrome, heading, portrait, overview, columns);
  root.append(shell);
  let disposed = false;
  let held: CharacterProjection | null = null;
  let creationStamp: string | null = null;

  const view: CharacterView = {
    creationElement: creation.element,
    update(value): void {
      if (disposed) return;
      held = value;
      const published = image('window.character-sheet.chrome');
      if (published !== null && chrome.src !== published) chrome.src = published;
      portrait.replaceChildren(...(value.identity?.selectedMedia ?? []).filter(media =>
        media.layer.startsWith('head') || (value.identity?.racialOverride?.beastForm || value.identity?.racialOverride?.vampireClan) && media.layer === 'background').flatMap(media => {
          const source = image(media.mediaId); if (source === null) return [];
          const img = document.createElement('img'); img.src = source; img.dataset.mediaId = media.mediaId;
          img.alt = `${value.identity?.racialOverride?.beastForm ? value.identity.racialOverride.name : value.name} ${media.layer.startsWith('head') ? 'portrait' : 'form'}`;
          return [img];
        }));
      overview.replaceChildren(
        overviewRow('Player', value.name),
        overviewRow('Level', format(value.progression.level)),
        overviewRow('Total XP', format(value.progression.experience)),
        ...(value.progression.nextLevelSkillProgress == null || value.progression.skillProgress == null ? [] : [
          overviewRow('Level progress', `${format(value.progression.skillProgress)} / ${format(value.progression.nextLevelSkillProgress)} skill total${value.progression.pendingLevelUp === true ? ' · Level up ready' : ''}`),
        ]),
        ...(value.identity ? [overviewRow('Race', value.identity.raceLabel), overviewRow('Career', value.identity.career),
          ...(value.identity.racialOverride ? [overviewRow('Form', `${value.identity.racialOverride.name} · ${value.identity.racialOverride.vampireClan ?? (value.identity.racialOverride.beastForm ? 'Beast form' : 'Human form')}`)] : []),
          overviewRow('Face', `${value.identity.gender} ${format(value.identity.faceIndex + 1)}`)] : []),
      );
      renderRows(resources.rows, value.resources.map(resource => ({
        label: resource.label,
        value: `${format(resource.current)} / ${format(resource.maximum)}`,
        testid: `character-sheet-${resource.id}`,
      })));
      renderRows(attributes.rows, value.attributes.map(stat => ({
        label: stat.label,
        value: stat.value === stat.permanent ? format(stat.value) : `${format(stat.value)} live / ${format(stat.permanent)} permanent`,
        testid: `character-sheet-attribute-${stat.id}`,
      })));
      renderRows(skills.rows, value.skills.map(stat => ({
        label: stat.label,
        value: stat.value === stat.permanent ? format(stat.value) : `${format(stat.value)} live / ${format(stat.permanent)} permanent`,
        testid: `character-sheet-skill-${stat.id}`,
      })));
      renderRows(resistances.rows, (value.resistances ?? []).map(stat => ({
        label: stat.label,
        value: stat.value === stat.permanent ? format(stat.value) : `${format(stat.value)} live / ${format(stat.permanent)} permanent`,
        testid: `character-sheet-resistance-${stat.id}`,
      })));
      renderRows(affiliations.rows, (value.affiliations ?? []).map(affiliation => ({
        label: affiliation.faction,
        value: `${affiliation.guildGroup || 'Guild'} · ${affiliation.rankTitle ?? `Rank ${format(affiliation.rank)}`} · Reputation ${format(affiliation.reputation)} · Recognition ${format(affiliation.recognition)}`
          + (affiliation.nextRequirement ? ` · Next rank ${format(affiliation.nextRequirement.rank)}: reputation ${format(affiliation.nextRequirement.reputation)}, skills ${format(affiliation.nextRequirement.highSkill)}/${format(affiliation.nextRequirement.lowSkill)}` : '')
          + (affiliation.daysUntilReview != null ? ` · Review in ${format(affiliation.daysUntilReview)} days` : '')
          + (affiliation.privileges?.length ? ` · Privileges: ${affiliation.privileges.join(', ')}` : ''),
        testid: `character-sheet-affiliation-${affiliation.faction}`,
      })));
      renderRows(legal.rows, (value.legalStandings ?? []).map(standing => ({
        label: standing.label,
        value: `Reputation ${format(standing.reputation)}`,
        testid: `character-sheet-legal-${standing.region}`,
      })));
      renderRows(history.rows, (value.history?.biography ?? []).map((line, index) => ({
        label: `History ${format(index + 1)}`,
        value: line,
        testid: `character-sheet-history-${index}`,
      })));
      equipment.rows.replaceChildren(...value.equipment.map(item => equipmentRow(item)));
      renderRows(career.rows, (value.grantedSkills ?? []).map(skill => ({
        label: skill.label,
        value: skill.tierLabel,
        testid: `character-sheet-granted-${skill.id}`,
      })));
      // The art revision is part of the stamp: a face preview drawn before its art arrived is drawn again.
      const nextCreationStamp = JSON.stringify([value.creationAvailable === true, value.creation ?? null, heldRevision()]);
      if (creationStamp !== nextCreationStamp) {
        renderCreation(creation.rows, value.creation ?? null, value.creationAvailable === true, send);
        creationStamp = nextCreationStamp;
      }
      levelUp.heading.textContent = value.levelUp?.title ?? "Level up";
      redraw(levelUp.rows, JSON.stringify(value.levelUp ?? null), () => renderLevelUp(levelUp.rows, value.levelUp ?? null, send));
    },
    // The sheet's chrome is published art that can arrive after the sheet's own state.
    refresh(): void {
      if (disposed || held === null) return;
      view.update(held);
    },
    dispose(): void {
      if (disposed) return;
      disposed = true;
      shell.remove();
    },
  };
  return view;
}

export function isCharacterProjection(value: unknown): value is CharacterProjection {
  return typeof value === 'object' && value !== null
    && 'name' in value && typeof value.name === 'string'
    && 'attributes' in value && isStats(value.attributes)
    && 'skills' in value && isStats(value.skills)
    && 'resources' in value && Array.isArray(value.resources) && value.resources.every(isResource)
    && 'progression' in value && isProgression(value.progression)
    && 'equipment' in value && Array.isArray(value.equipment) && value.equipment.every(isEquipment)
    && (!('resistances' in value) || Array.isArray(value.resistances) && isStats(value.resistances))
    && (!('affiliations' in value) || Array.isArray(value.affiliations) && value.affiliations.every(isAffiliation))
    && (!('legalStandings' in value) || Array.isArray(value.legalStandings) && value.legalStandings.every(isLegalStanding))
    && (!('history' in value) || value.history === null || isHistory(value.history))
    && (!('identity' in value) || value.identity === null || isIdentity(value.identity))
    && (!('grantedSkills' in value) || Array.isArray(value.grantedSkills) && value.grantedSkills.every(isGrantedSkill))
    && (!('creation' in value) || value.creation === null || isCreation(value.creation))
    && (!('creationAvailable' in value) || typeof value.creationAvailable === 'boolean')
    && (!('levelUp' in value) || value.levelUp === null || isLevelUp(value.levelUp));
}

function section(title: string): { readonly element: HTMLElement; readonly rows: HTMLElement; readonly heading: HTMLElement } {
  const element = document.createElement('section');
  element.className = 'dagger-character-section dagger-panel';
  const heading = document.createElement('h3');
  heading.textContent = title;
  const rows = document.createElement('dl');
  rows.className = 'dagger-character-rows';
  element.append(heading, rows);
  return { element, rows, heading };
}

function overviewRow(label: string, value: string): HTMLElement {
  const row = document.createElement('div');
  const term = document.createElement('span');
  const definition = document.createElement('strong');
  term.textContent = label;
  definition.textContent = value;
  row.append(term, definition);
  return row;
}

function renderRows(root: HTMLElement, values: readonly { readonly label: string; readonly value: string; readonly testid: string }[]): void {
  root.replaceChildren(...values.map(value => {
    const row = document.createElement('div');
    const term = document.createElement('dt');
    const definition = document.createElement('dd');
    term.textContent = value.label;
    definition.textContent = value.value;
    definition.dataset.testid = value.testid;
    row.append(term, definition);
    return row;
  }));
}

function equipmentRow(item: CharacterEquipment): HTMLElement {
  const row = document.createElement('div');
  const label = document.createElement('dt');
  const slots = document.createElement('span');
  const details = document.createElement('dd');
  label.textContent = item.label;
  slots.textContent = item.slots.join(' · ');
  details.textContent = item.details;
  row.append(label, slots, details);
  return row;
}

function isStats(value: unknown): value is readonly CharacterStat[] {
  return Array.isArray(value) && value.every(item => typeof item === 'object' && item !== null
    && 'id' in item && typeof item.id === 'string'
    && 'label' in item && typeof item.label === 'string'
    && 'value' in item && isNumber(item.value)
    && 'permanent' in item && isNumber(item.permanent));
}

function isLevelUp(value: unknown): value is CharacterLevelUp {
  return typeof value === 'object' && value !== null
    && 'title' in value && typeof value.title === 'string'
    && 'level' in value && isNumber(value.level) && 'bonusPool' in value && isNumber(value.bonusPool)
    && 'remainingPoints' in value && isNumber(value.remainingPoints) && 'healthGain' in value && isNumber(value.healthGain)
    && 'canCommit' in value && typeof value.canCommit === 'boolean'
    && 'attributes' in value && Array.isArray(value.attributes) && value.attributes.every(attribute => typeof attribute === 'object' && attribute !== null
      && 'id' in attribute && typeof attribute.id === 'string' && 'label' in attribute && typeof attribute.label === 'string'
      && 'permanent' in attribute && isNumber(attribute.permanent) && 'live' in attribute && isNumber(attribute.live)
      && 'pending' in attribute && isNumber(attribute.pending) && 'canAllocate' in attribute && typeof attribute.canAllocate === 'boolean');
}

function isResource(value: unknown): value is CharacterResource {
  return typeof value === 'object' && value !== null
    && 'id' in value && typeof value.id === 'string'
    && 'label' in value && typeof value.label === 'string'
    && 'current' in value && isNumber(value.current)
    && 'maximum' in value && isNumber(value.maximum);
}

function isProgression(value: unknown): value is CharacterProgression {
  return typeof value === 'object' && value !== null
    && 'level' in value && isNumber(value.level)
    && 'experience' in value && isNumber(value.experience)
    && (!('skillProgress' in value) || value.skillProgress === null || isNumber(value.skillProgress))
    && (!('nextLevelSkillProgress' in value) || value.nextLevelSkillProgress === null || isNumber(value.nextLevelSkillProgress))
    && (!('pendingLevelUp' in value) || typeof value.pendingLevelUp === 'boolean');
}

function isAffiliation(value: unknown): value is CharacterAffiliation {
  return typeof value === 'object' && value !== null
    && 'faction' in value && typeof value.faction === 'string'
    && 'guildGroup' in value && typeof value.guildGroup === 'string'
    && 'rank' in value && isNumber(value.rank)
    && 'reputation' in value && isNumber(value.reputation)
    && 'recognition' in value && isNumber(value.recognition)
    && (!('currentRequirement' in value) || value.currentRequirement === null || isGuildRankRequirement(value.currentRequirement))
    && (!('nextRequirement' in value) || value.nextRequirement === null || isGuildRankRequirement(value.nextRequirement))
    && (!('daysUntilReview' in value) || value.daysUntilReview === null || isNumber(value.daysUntilReview))
    && (!('privileges' in value) || Array.isArray(value.privileges) && value.privileges.every(item => typeof item === 'string'))
    && (!('rankTitle' in value) || value.rankTitle === null || typeof value.rankTitle === 'string');
}

function isLegalStanding(value: unknown): value is CharacterLegalStanding {
  return typeof value === 'object' && value !== null
    && 'region' in value && isNumber(value.region)
    && 'regionName' in value && typeof value.regionName === 'string'
    && 'reputation' in value && isNumber(value.reputation)
    && 'standing' in value && typeof value.standing === 'string'
    && 'banished' in value && typeof value.banished === 'boolean'
    && 'current' in value && typeof value.current === 'boolean'
    && 'label' in value && typeof value.label === 'string';
}

function isGuildRankRequirement(value: unknown): value is GuildRankRequirement {
  return typeof value === 'object' && value !== null
    && 'rank' in value && isNumber(value.rank)
    && 'reputation' in value && isNumber(value.reputation)
    && 'highSkill' in value && isNumber(value.highSkill)
    && 'lowSkill' in value && isNumber(value.lowSkill);
}

function isHistory(value: unknown): value is CharacterHistory {
  return typeof value === 'object' && value !== null && 'biography' in value && Array.isArray(value.biography)
    && value.biography.every(line => typeof line === 'string');
}

function isEquipment(value: unknown): value is CharacterEquipment {
  return typeof value === 'object' && value !== null
    && 'label' in value && typeof value.label === 'string'
    && 'slots' in value && Array.isArray(value.slots) && value.slots.every(slot => typeof slot === 'string')
    && 'details' in value && typeof value.details === 'string'
    && (!('condition' in value) || value.condition === null || typeof value.condition === 'object' && value.condition !== null
      && 'current' in value.condition && isNumber(value.condition.current) && 'maximum' in value.condition && isNumber(value.condition.maximum)
      && 'percentage' in value.condition && isNumber(value.condition.percentage) && 'broken' in value.condition && typeof value.condition.broken === 'boolean')
    && (!('identified' in value) || typeof value.identified === 'boolean');
}

function isIdentity(value: unknown): value is CharacterIdentity {
  return typeof value === 'object' && value !== null
    && (!('racialOverride' in value) || value.racialOverride == null || isRacialOverride(value.racialOverride))
    && 'race' in value && typeof value.race === 'string'
    && 'raceLabel' in value && typeof value.raceLabel === 'string'
    && 'donorRaceId' in value && isNumber(value.donorRaceId)
    && 'portrait' in value && typeof value.portrait === 'string'
    && 'gender' in value && typeof value.gender === 'string'
    && 'faceIndex' in value && isNumber(value.faceIndex)
    && 'career' in value && typeof value.career === 'string'
    && 'media' in value && Array.isArray(value.media) && value.media.every(isMedia)
    && 'selectedMedia' in value && Array.isArray(value.selectedMedia) && value.selectedMedia.every(isMedia);
}

function isRacialOverride(value: unknown): boolean {
  return typeof value === 'object' && value !== null
    && 'name' in value && typeof value.name === 'string'
    && (!('vampireClan' in value) || value.vampireClan == null || typeof value.vampireClan === 'string')
    && 'beastForm' in value && typeof value.beastForm === 'boolean'
    && 'suppressInventory' in value && typeof value.suppressInventory === 'boolean';
}

function isMedia(value: unknown): value is CharacterMedia {
  return typeof value === 'object' && value !== null
    && 'layer' in value && typeof value.layer === 'string'
    && 'mediaId' in value && typeof value.mediaId === 'string';
}

function isGrantedSkill(value: unknown): value is CharacterGrantedSkill {
  return typeof value === 'object' && value !== null
    && 'id' in value && typeof value.id === 'string'
    && 'tier' in value && typeof value.tier === 'string'
    && 'label' in value && typeof value.label === 'string'
    && 'tierLabel' in value && typeof value.tierLabel === 'string';
}

function isCreation(value: unknown): value is CharacterCreation {
  return typeof value === 'object' && value !== null
    && 'editing' in value && typeof value.editing === 'boolean'
    && 'current' in value && isCreationCurrent(value.current)
    && 'races' in value && isChoices(value.races)
    && 'careers' in value && isChoices(value.careers)
    && 'faces' in value && Array.isArray(value.faces) && value.faces.every(face => typeof face === 'object' && face !== null && 'index' in face && isNumber(face.index) && 'mediaId' in face && typeof face.mediaId === 'string')
    && 'reflexes' in value && Array.isArray(value.reflexes) && value.reflexes.every(reflex => typeof reflex === 'object' && reflex !== null && 'value' in reflex && isNumber(reflex.value) && 'label' in reflex && typeof reflex.label === 'string')
    && (!('mode' in value) || value.mode === null || value.mode === 'character-pick' || value.mode === 'character-generation')
    && (!('classQuestionsAvailable' in value) || typeof value.classQuestionsAvailable === 'boolean')
    && (!('summary' in value) || value.summary === null || Array.isArray(value.summary) && value.summary.every(item => typeof item === 'string'))
    && (!('classQuiz' in value) || value.classQuiz === null || isClassQuiz(value.classQuiz))
    && (!('custom' in value) || value.custom === null || isCustomClass(value.custom))
    && (!('background' in value) || value.background === null || isBackground(value.background));

function isClassQuiz(value: unknown): value is CharacterClassQuiz {
  if (typeof value !== 'object' || value === null || !('answered' in value) || !isNumber(value.answered)
    || !('total' in value) || !isNumber(value.total) || !('question' in value)) return false;
  const q = value.question;
  return typeof q === 'object' && q !== null && 'number' in q && isNumber(q.number) && 'text' in q && typeof q.text === 'string'
    && 'answers' in q && Array.isArray(q.answers) && q.answers.every(a => typeof a === 'object' && a !== null && 'index' in a && isNumber(a.index) && 'text' in a && typeof a.text === 'string');
}

function isBackground(value: unknown): value is CharacterBackground {
  const allocation = (entry: unknown): boolean => typeof entry === 'object' && entry !== null && 'id' in entry && typeof entry.id === 'string' && 'allocated' in entry && isNumber(entry.allocated) && 'value' in entry && isNumber(entry.value);
  return typeof value === 'object' && value !== null
    && 'biographyClassIndex' in value && isNumber(value.biographyClassIndex) && 'biography' in value && Array.isArray(value.biography) && value.biography.every(line => typeof line === 'string')
    && 'attributeBonusPool' in value && isNumber(value.attributeBonusPool) && 'remainingAttributePoints' in value && isNumber(value.remainingAttributePoints)
    && 'primarySkillPoints' in value && isNumber(value.primarySkillPoints) && 'majorSkillPoints' in value && isNumber(value.majorSkillPoints) && 'minorSkillPoints' in value && isNumber(value.minorSkillPoints)
    && 'questions' in value && Array.isArray(value.questions) && value.questions.every(question => typeof question === 'object' && question !== null && 'number' in question && isNumber(question.number) && 'text' in question && typeof question.text === 'string' && 'selectedLetter' in question && (question.selectedLetter === null || typeof question.selectedLetter === 'string') && 'answers' in question && Array.isArray(question.answers))
    && 'attributes' in value && Array.isArray(value.attributes) && value.attributes.every(allocation)
    && 'skills' in value && Array.isArray(value.skills) && value.skills.every(skill => allocation(skill) && typeof skill === 'object' && skill !== null && 'label' in skill && typeof skill.label === 'string')
    && 'startingGrants' in value && Array.isArray(value.startingGrants)
    && 'unsupportedEffects' in value && Array.isArray(value.unsupportedEffects) && value.unsupportedEffects.every(effect => typeof effect === 'string');
}
}

function isCustomClass(value: unknown): value is CharacterCustomClass {
  const strings = (items: unknown): items is readonly string[] => Array.isArray(items) && items.every(item => typeof item === 'string');
  const traits = (items: unknown): items is readonly CharacterCustomTrait[] => Array.isArray(items) && items.every(item => typeof item === 'object' && item !== null && 'id' in item && typeof item.id === 'string' && 'target' in item && (item.target === null || typeof item.target === 'string'));
  return typeof value === 'object' && value !== null && 'name' in value && typeof value.name === 'string'
    && 'primarySkills' in value && strings(value.primarySkills) && 'majorSkills' in value && strings(value.majorSkills) && 'minorSkills' in value && strings(value.minorSkills)
    && 'hitPointsPerLevel' in value && isNumber(value.hitPointsPerLevel) && 'advantages' in value && traits(value.advantages) && 'disadvantages' in value && traits(value.disadvantages)
    && 'eligibility' in value && strings(value.eligibility) && 'skills' in value && Array.isArray(value.skills) && value.skills.every(skill => typeof skill === 'object' && skill !== null && 'id' in skill && typeof skill.id === 'string' && 'label' in skill && typeof skill.label === 'string') && 'supportedAdvantages' in value && strings(value.supportedAdvantages) && 'supportedDisadvantages' in value && strings(value.supportedDisadvantages);
}

function isCreationCurrent(value: unknown): value is CharacterCreation['current'] {
  return typeof value === 'object' && value !== null && 'name' in value && typeof value.name === 'string'
    && 'race' in value && typeof value.race === 'string' && 'gender' in value && typeof value.gender === 'string'
    && 'faceIndex' in value && isNumber(value.faceIndex) && 'reflexes' in value && isNumber(value.reflexes)
    && 'career' in value && typeof value.career === 'string';
}

function isChoices(value: unknown): value is readonly CharacterChoice[] {
  return Array.isArray(value) && value.every(choice => typeof choice === 'object' && choice !== null
    && 'id' in choice && typeof choice.id === 'string' && 'label' in choice && typeof choice.label === 'string'
    && 'available' in choice && typeof choice.available === 'boolean' && 'restriction' in choice
    && (choice.restriction === null || typeof choice.restriction === 'string'));
}

function renderCreation(root: HTMLElement, value: CharacterCreation | null, available: boolean, send?: (action: CharacterAction) => void): void {
  if (value === null) { root.replaceChildren(); return; }
  if (!available) { root.replaceChildren(); return; }
  const begin = document.createElement('button'); begin.type = 'button'; begin.textContent = 'Edit character';
  begin.disabled = value.editing; begin.dataset.testid = 'character-begin'; begin.addEventListener('click', () => send?.({ action: 'character-begin' }));
  if (!value.editing) {
    if (!value.summary) { root.replaceChildren(begin); return; }
    const summary = document.createElement('section'); summary.dataset.testid = 'new-game-summary';
    const heading = document.createElement('h3'); heading.textContent = 'Final character summary'; summary.append(heading);
    for (const line of value.summary) { const item = document.createElement('p'); item.textContent = line; summary.append(item); }
    begin.textContent = 'Restart character creation';
    const launch = document.createElement('button'); launch.type = 'button'; launch.textContent = 'Begin new game';
    launch.dataset.testid = 'new-game-launch'; launch.addEventListener('click', () => send?.({ action: 'begin' }));
    const abandon = document.createElement('button'); abandon.type = 'button'; abandon.textContent = 'Abandon character';
    abandon.dataset.testid = 'new-game-abandon'; abandon.addEventListener('click', () => send?.({ action: 'character-cancel' }));
    root.replaceChildren(summary, launch, begin, abandon); return;
  }
  if (value.classQuiz) {
    const quiz = value.classQuiz;
    const heading = document.createElement('p'); heading.textContent = `Class question ${quiz.answered + 1} of ${quiz.total}`;
    const prompt = document.createElement('p'); prompt.textContent = quiz.question.text;
    const answers = quiz.question.answers.map(answer => {
      const button = document.createElement('button'); button.type = 'button'; button.textContent = answer.text;
      button.dataset.testid = `class-answer-${answer.index}`;
      button.addEventListener('click', () => send?.({ action: 'character-class-answer', question: quiz.question.number, answer: answer.index }));
      return button;
    });
    const back = document.createElement('button'); back.type = 'button'; back.textContent = 'Choose a class instead';
    back.addEventListener('click', () => send?.({ action: 'character-class-back' }));
    const cancel = document.createElement('button'); cancel.type = 'button'; cancel.textContent = 'Cancel character';
    cancel.addEventListener('click', () => send?.({ action: 'character-cancel' }));
    root.replaceChildren(heading, prompt, ...answers, back, cancel); return;
  }
  const focused = document.activeElement instanceof HTMLElement && root.contains(document.activeElement) ? document.activeElement.getAttribute('aria-label') : null;
  const name = document.createElement('input'); name.value = value.current.name; name.setAttribute('aria-label', 'Character name'); name.maxLength = 32;
  const race = select(value.races, value.current.race); race.setAttribute('aria-label', 'Race');
  const gender = select([{ id: 'male', label: 'Male', available: true, restriction: null }, { id: 'female', label: 'Female', available: true, restriction: null }], value.current.gender); gender.setAttribute('aria-label', 'Gender');
  const face = select(value.faces.map(item => ({ id: String(item.index), label: `Face ${item.index + 1}`, available: true, restriction: null })), String(value.current.faceIndex)); face.setAttribute('aria-label', 'Face');
  const facePreview = document.createElement('img'); facePreview.className = 'dagger-creation-face'; facePreview.dataset.testid = 'character-face-preview';
  const showFace = (): void => {
    const chosen = value.faces.find(item => String(item.index) === face.value);
    const source = chosen ? image(chosen.mediaId) : null;
    facePreview.hidden = source === null; if (source !== null) facePreview.src = source;
    facePreview.alt = chosen ? `Face ${chosen.index + 1}` : '';
  };
  face.addEventListener('change', showFace); showFace();
  const faceStep = (offset: number): HTMLButtonElement => stepButton(offset < 0 ? '‹' : '›', offset < 0 ? 'Previous face' : 'Next face', () => {
    face.selectedIndex = (face.selectedIndex + offset + face.options.length) % face.options.length; showFace();
  });
  const reflexes = select(value.reflexes.map(item => ({ id: String(item.value), label: item.label, available: true, restriction: null })), String(value.current.reflexes)); reflexes.setAttribute('aria-label', 'Reflexes');
  const career = select(value.careers, value.current.career); career.setAttribute('aria-label', 'Career');
  const custom = value.custom ?? null;
  const customFields = document.createElement('fieldset'); customFields.dataset.testid = 'character-custom-class';
  const customLegend = document.createElement('legend'); customLegend.textContent = 'Custom class'; customFields.append(customLegend);
  const skillValues = [...(custom?.skills ?? [])];
  const skillIds = skillValues.map(skill => skill.id);
  const primary = customSkills('Primary skills', custom?.primarySkills ?? skillIds.slice(0, 3), skillValues, 3);
  const major = customSkills('Major skills', custom?.majorSkills ?? skillIds.slice(3, 6), skillValues, 3);
  const minor = customSkills('Minor skills', custom?.minorSkills ?? skillIds.slice(6, 12), skillValues, 6);
  const hp = document.createElement('input'); hp.type = 'number'; hp.min = '4'; hp.max = '30'; hp.value = String(custom?.hitPointsPerLevel ?? 8); hp.setAttribute('aria-label', 'Hit points per level');
  hp.addEventListener('change', () => { hp.value = String(Math.min(30, Math.max(4, Math.round(Number(hp.value) || 8)))); });
  const advantages = traitInput('Advantages', custom?.advantages ?? [], custom?.supportedAdvantages ?? []);
  const disadvantages = traitInput('Disadvantages', custom?.disadvantages ?? [], custom?.supportedDisadvantages ?? []);
  const eligibility = document.createElement('ul'); eligibility.dataset.testid = 'character-custom-eligibility'; eligibility.setAttribute('aria-live', 'polite');
  eligibility.replaceChildren(...(custom?.eligibility ?? []).map(reason => { const item = document.createElement('li'); item.textContent = reason; return item; }));
  customFields.append(primary.element, major.element, minor.element, labeled('HP per level', hp), advantages.element, disadvantages.element, eligibility);
  customFields.hidden = career.value !== 'custom';
  const background = backgroundEditor(value.background ?? null);
  // Class questions and quick start carry no background: the ruleset rolls one, and quick start completes it.
  const action = (kind: 'character-update' | 'character-commit' | 'character-background-reroll' | 'character-class-questions' | 'character-quick-start'): CharacterAction => {
    const withBackground = kind !== 'character-class-questions' && kind !== 'character-quick-start';
    return career.value === 'custom'
      ? { action: kind, name: name.value, race: race.value, gender: gender.value, faceIndex: Number(face.value), reflexes: Number(reflexes.value), career: career.value,
        primarySkills: primary.values().join(','), majorSkills: major.values().join(','), minorSkills: minor.values().join(','), hitPointsPerLevel: Number(hp.value), advantages: advantages.value(), disadvantages: disadvantages.value(), ...(withBackground ? background.values() : {}) }
      : { action: kind, name: name.value, race: race.value, gender: gender.value, faceIndex: Number(face.value), reflexes: Number(reflexes.value), career: career.value, ...(withBackground ? background.values() : {}) };
  };
  // A choice the ruleset derives other choices from (the faces a race and gender have, the background a
  // career or custom tier layout rolls, the biography its answers write) is sent as it changes, so the
  // draft the player sees is always the ruleset's.
  const live = (): void => send?.(action('character-update'));
  for (const input of [race, gender, career, ...customFields.querySelectorAll('select'), hp, ...background.answers]) input.addEventListener('change', live);
  const commit = document.createElement('button'); commit.type = 'button'; commit.textContent = 'Commit character'; commit.dataset.testid = 'character-commit';
  commit.addEventListener('click', () => send?.(action('character-commit')));
  const blocked = document.createElement('p'); blocked.className = 'dagger-creation-blocked'; blocked.dataset.testid = 'character-commit-blocked'; blocked.setAttribute('aria-live', 'polite');
  const refreshCommit = (): void => {
    const reasons = [
      ...(name.value.trim().length === 0 ? ['Enter a name.'] : []),
      ...(value.background === null || value.background === undefined ? ['Roll a background.'] : background.unspent()),
      ...(career.value === 'custom' ? custom?.eligibility ?? [] : []),
    ];
    commit.disabled = reasons.length !== 0;
    blocked.textContent = reasons.join(' ');
  };
  name.addEventListener('input', refreshCommit); background.element.addEventListener('input', refreshCommit); refreshCommit();
  const cancel = document.createElement('button'); cancel.type = 'button'; cancel.textContent = 'Cancel'; cancel.dataset.testid = 'character-cancel'; cancel.addEventListener('click', () => send?.({ action: 'character-cancel' }));
  const reroll = document.createElement('button'); reroll.type = 'button'; reroll.textContent = 'Reroll background'; reroll.dataset.testid = 'character-background-reroll'; reroll.disabled = value.background === null;
  reroll.addEventListener('click', () => send?.(action('character-background-reroll')));
  const questions = document.createElement('button'); questions.type = 'button'; questions.textContent = 'Answer class questions';
  questions.dataset.testid = 'character-class-questions'; questions.disabled = value.classQuestionsAvailable !== true;
  questions.addEventListener('click', () => send?.(action('character-class-questions')));

  const faceRow = document.createElement('div'); faceRow.className = 'dagger-creation-face-row'; faceRow.append(faceStep(-1), face, faceStep(1));
  const identity = panel('Identity', 'identity', labeled('Name', name), labeled('Race', race), labeled('Gender', gender), labeled('Face', faceRow), facePreview, labeled('Reflexes', reflexes));
  const classPanel = panel('Class', 'class', labeled('Career', career), questions, customFields);
  const backgroundPanel = panel('Background', 'background', ...background.story, reroll);
  const attributesPanel = panel('Attributes', 'attributes', ...background.attributes);
  const skillsPanel = panel('Skills', 'skills', ...background.skills);
  attributesPanel.hidden = skillsPanel.hidden = value.background === null || value.background === undefined;
  // A testing convenience: start a new game at once from these choices, with the background completed for you.
  const quickStart = document.createElement('button'); quickStart.type = 'button'; quickStart.textContent = 'Quick start';
  quickStart.dataset.tooltip = 'Begin a new game now with these choices and a background completed automatically';
  quickStart.dataset.testid = 'character-quick-start'; quickStart.disabled = career.value === 'custom' && (custom?.eligibility ?? []).length !== 0;
  quickStart.addEventListener('click', () => send?.(action('character-quick-start')));
  const actions = document.createElement('footer'); actions.className = 'dagger-creation-actions'; actions.append(blocked, cancel, quickStart, commit);
  const layout = document.createElement('section'); layout.className = 'dagger-creation'; layout.dataset.testid = 'character-background';
  layout.append(identity, classPanel, backgroundPanel, attributesPanel, skillsPanel, actions);
  root.replaceChildren(layout);
  // Re-rendering from the ruleset's answer keeps the control the player was using focused.
  if (focused !== null) [...root.querySelectorAll<HTMLElement>('[aria-label]')].find(element => element.getAttribute('aria-label') === focused)?.focus();
}

function panel(title: string, kind: string, ...children: HTMLElement[]): HTMLElement {
  const element = document.createElement('section'); element.className = `dagger-creation-panel dagger-panel dagger-creation-${kind}`;
  const heading = document.createElement('h3'); heading.textContent = title; element.append(heading, ...children); return element;
}

function stepButton(text: string, label: string, click: () => void): HTMLButtonElement {
  const button = document.createElement('button'); button.type = 'button'; button.textContent = text; button.setAttribute('aria-label', label);
  button.addEventListener('click', click); return button;
}

interface BackgroundEditor {
  readonly story: readonly HTMLElement[];
  readonly attributes: readonly HTMLElement[];
  readonly skills: readonly HTMLElement[];
  readonly answers: readonly HTMLSelectElement[];
  /** The element whose `input` events report a changed allocation. */
  readonly element: HTMLElement;
  readonly unspent: () => string[];
  readonly values: () => Pick<CharacterAction, 'backgroundAnswers' | 'attributeAllocations' | 'skillAllocations'>;
}

const SKILL_TIERS = [['primary', 'Primary'], ['major', 'Major'], ['minor', 'Minor']] as const;

function backgroundEditor(value: CharacterBackground | null): BackgroundEditor {
  const element = document.createElement('div');
  if (value === null) return { story: [], attributes: [], skills: [], answers: [], element, unspent: () => [], values: () => ({}) };
  const questions = value.questions.map(question => {
    const input = select(question.answers.map(answer => ({ id: answer.letter, label: `${answer.letter.toUpperCase()}. ${answer.text}`, available: true, restriction: null })), question.selectedLetter ?? question.answers[0]?.letter ?? '');
    input.setAttribute('aria-label', `Background question ${question.number}`);
    return { question: question.number, input, element: labeled(`${question.number}. ${question.text}`, input) };
  });
  const biography = document.createElement('div'); biography.className = 'dagger-creation-biography'; biography.dataset.testid = 'character-biography';
  biography.replaceChildren(...value.biography.map(line => { const paragraph = document.createElement('p'); paragraph.textContent = line; return paragraph; }));
  const questionGrid = document.createElement('div'); questionGrid.className = 'dagger-creation-questions';
  questionGrid.append(...questions.map(question => question.element));
  const story: HTMLElement[] = [questionGrid, biography];
  if (value.startingGrants.length !== 0) { const grants = document.createElement('p'); grants.dataset.testid = 'character-starting-grants'; grants.textContent = `Starting grants: ${value.startingGrants.map(grant => `${grant.quantity} × ${grant.label}`).join(', ')}.`; story.push(grants); }
  if (value.unsupportedEffects.length !== 0) { const unsupported = document.createElement('p'); unsupported.dataset.testid = 'character-background-unsupported-effects'; unsupported.textContent = `Recorded effects: ${value.unsupportedEffects.join(' ')}`; story.push(unsupported); }

  // Each pool is what the ruleset said remains plus what the draft had allocated from it, so the
  // steppers can spend exactly that pool and never more.
  const attributePool = value.remainingAttributePoints + value.attributes.reduce((total, attribute) => total + attribute.allocated, 0);
  const tierPool = (tier: string, remaining: number): number => remaining + value.skills.filter(skill => skill.tier === tier).reduce((total, skill) => total + skill.allocated, 0);
  const tierPools = new Map<string, number>([['primary', tierPool('primary', value.primarySkillPoints)], ['major', tierPool('major', value.majorSkillPoints)], ['minor', tierPool('minor', value.minorSkillPoints)]]);
  const attributes = value.attributes.map(attribute => stepper('Attributes', attribute.id, attribute.label, attribute.allocated, attribute.value - attribute.allocated, attribute.maximum));
  const skills = value.skills.map(skill => ({ tier: skill.tier, row: stepper('Skills', skill.id, skill.label, skill.allocated, skill.value - skill.allocated, Number.MAX_SAFE_INTEGER) }));
  const spent = (rows: readonly Stepper[]): number => rows.reduce((total, row) => total + row.allocated(), 0);
  const attributeLeft = (): number => attributePool - spent(attributes);
  const tierLeft = (tier: string): number => (tierPools.get(tier) ?? 0) - spent(skills.filter(skill => skill.tier === tier).map(skill => skill.row));
  const pools = document.createElement('p'); pools.className = 'dagger-creation-pool'; pools.dataset.testid = 'character-allocation-pools'; pools.setAttribute('aria-live', 'polite');
  const tierHeadings = new Map(SKILL_TIERS.map(([tier, label]) => { const heading = document.createElement('h4'); heading.dataset.tier = tier; heading.dataset.label = label; return [tier, heading]; }));
  const refresh = (): void => {
    for (const row of attributes) row.bound(attributeLeft());
    for (const skill of skills) skill.row.bound(tierLeft(skill.tier));
    pools.textContent = `${attributeLeft()} of ${attributePool} bonus points left`;
    for (const [tier, heading] of tierHeadings) heading.textContent = `${heading.dataset.label} skills · ${tierLeft(tier)} of ${tierPools.get(tier) ?? 0} points left`;
  };
  element.addEventListener('input', refresh);
  for (const row of [...attributes, ...skills.map(skill => skill.row)]) row.onChange(() => element.dispatchEvent(new Event('input')));
  refresh();
  const skillElements = SKILL_TIERS.flatMap(([tier]) => {
    const rows = skills.filter(skill => skill.tier === tier);
    return rows.length === 0 ? [] : [tierHeadings.get(tier)!, ...rows.map(skill => skill.row.element)];
  });
  return {
    story, answers: questions.map(question => question.input), element,
    attributes: [pools, ...attributes.map(row => row.element)],
    skills: skillElements,
    unspent: () => [
      ...(attributeLeft() > 0 ? [`Spend ${attributeLeft()} more bonus ${attributeLeft() === 1 ? 'point' : 'points'}.`] : []),
      ...SKILL_TIERS.flatMap(([tier, label]) => tierLeft(tier) > 0 ? [`Spend ${tierLeft(tier)} more ${label.toLowerCase()} skill ${tierLeft(tier) === 1 ? 'point' : 'points'}.`] : []),
    ],
    values: () => ({
      backgroundAnswers: questions.map(question => `${question.question}:${question.input.value}`).join(','),
      attributeAllocations: allocations(attributes), skillAllocations: allocations(skills.map(skill => skill.row)),
    }),
  };
}

interface Stepper {
  readonly id: string;
  readonly element: HTMLElement;
  allocated(): number;
  /** Enables the steps the pool and the maximum allow, given the points still left in the pool. */
  bound(left: number): void;
  onChange(listener: () => void): void;
}

function stepper(group: string, id: string, label: string, allocated: number, base: number, maximum: number): Stepper {
  let points = allocated;
  const listeners: (() => void)[] = [];
  const element = document.createElement('div'); element.className = 'dagger-creation-stepper'; element.dataset.id = id;
  const name = document.createElement('span'); name.className = 'dagger-creation-stepper-label'; name.textContent = label;
  const count = document.createElement('output'); count.setAttribute('aria-label', `${group} ${id}`);
  const total = document.createElement('strong'); total.className = 'dagger-creation-stepper-total';
  const change = (delta: number): void => { points += delta; for (const listener of listeners) listener(); };
  const less = stepButton('−', `Remove a point from ${label}`, () => change(-1));
  const more = stepButton('+', `Add a point to ${label}`, () => change(1));
  element.append(name, less, count, more, total);
  return {
    id, element,
    allocated: () => points,
    bound(left: number): void {
      count.textContent = points === 0 ? '·' : `+${points}`;
      total.textContent = String(base + points);
      less.disabled = points === 0;
      more.disabled = left <= 0 || base + points >= maximum;
    },
    onChange: listener => { listeners.push(listener); },
  };
}

function allocations(rows: readonly Stepper[]): string {
  return rows.filter(row => row.allocated() !== 0).map(row => `${row.id}:${row.allocated()}`).join(',');
}

function renderLevelUp(root: HTMLElement, value: CharacterLevelUp | null, send?: (action: CharacterAction) => void): void {
  if (value === null) { root.replaceChildren(); return; }
  const summary = document.createElement('p');
  summary.dataset.testid = 'character-level-up-summary';
  summary.textContent = `${value.title} · Level ${format(value.level)}: ${format(value.remainingPoints)} of ${format(value.bonusPool)} points remain; health gain ${format(value.healthGain)}.`;
  const allocations = document.createElement('dl');
  allocations.dataset.testid = 'character-level-up-attributes';
  for (const attribute of value.attributes) {
    const row = document.createElement('div');
    const label = document.createElement('dt'); label.textContent = attribute.label;
    const detail = document.createElement('dd');
    detail.textContent = `${format(attribute.live)} live / ${format(attribute.permanent)} permanent${attribute.pending === 0 ? '' : ` + ${format(attribute.pending)} pending`}`;
    const allocate = document.createElement('button'); allocate.type = 'button'; allocate.textContent = `Increase ${attribute.label}`;
    allocate.disabled = !attribute.canAllocate; allocate.dataset.testid = `character-level-up-${attribute.id}`;
    allocate.addEventListener('click', () => send?.({ action: 'character-level-allocate', attribute: attribute.id }));
    row.append(label, detail, allocate); allocations.append(row);
  }
  const commit = document.createElement('button'); commit.type = 'button'; commit.textContent = 'Apply attribute choices';
  commit.disabled = !value.canCommit; commit.dataset.testid = 'character-level-up-commit';
  commit.addEventListener('click', () => send?.({ action: 'character-level-commit' }));
  root.replaceChildren(summary, allocations, commit);
}

function labeled(label: string, input: HTMLElement): HTMLElement { const item = document.createElement('label'); item.textContent = label; item.append(input); return item; }
function customSkills(label: string, selected: readonly string[], skills: readonly CharacterSkillChoice[], count: number): { readonly element: HTMLElement; readonly values: () => string[] } {
  const element = document.createElement('fieldset'); const legend = document.createElement('legend'); legend.textContent = label; element.append(legend);
  const selects = Array.from({ length: count }, (_, index) => { const input = select(skills.map(skill => ({ id: skill.id, label: skill.label, available: true, restriction: null })), selected[index] ?? skills[index]?.id ?? ''); input.setAttribute('aria-label', `${label} ${index + 1}`); element.append(input); return input; });
  return { element, values: () => selects.map(item => item.value) };
}
function traitInput(label: string, traits: readonly CharacterCustomTrait[], supported: readonly string[]): { readonly element: HTMLElement; readonly value: () => string } {
  const element = document.createElement('fieldset');
  const legend = document.createElement('legend'); legend.textContent = label; element.append(legend);
  const values = traits.map(trait => trait.target ? `${trait.id}:${trait.target}` : trait.id);
  const choices = Array.from({ length: 7 }, (_, index) => {
    const input = document.createElement('select'); input.setAttribute('aria-label', `${label} ${index + 1}`);
    const empty = document.createElement('option'); empty.value = ''; empty.textContent = 'None'; input.append(empty);
    for (const value of supported) {
      const option = document.createElement('option'); option.value = value;
      option.textContent = value.replaceAll('-', ' ').replace(':', ': '); option.selected = values[index] === value; input.append(option);
    }
    element.append(input); return input;
  });
  return { element, value: () => choices.map(item => item.value).filter(Boolean).join(',') };
}

function select(values: readonly CharacterChoice[], current: string): HTMLSelectElement {
  const element = document.createElement('select');
  for (const value of values) { const option = document.createElement('option'); option.value = value.id; option.textContent = value.restriction ? `${value.label} — ${value.restriction}` : value.label; option.disabled = !value.available; option.selected = value.id === current; element.append(option); }
  return element;
}

function isNumber(value: unknown): value is number {
  return typeof value === 'number' && Number.isFinite(value);
}

function format(value: number): string {
  return new Intl.NumberFormat(undefined, { maximumFractionDigits: 0 }).format(value);
}
