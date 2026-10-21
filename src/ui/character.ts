export interface CharacterAction { readonly action: string; readonly question?: number; readonly answer?: number; readonly name?: string; readonly race?: string; readonly gender?: string; readonly faceIndex?: number; readonly reflexes?: number; readonly career?: string; readonly primarySkills?: string; readonly majorSkills?: string; readonly minorSkills?: string; readonly hitPointsPerLevel?: number; readonly advantages?: string; readonly disadvantages?: string; readonly attribute?: string; readonly backgroundAnswers?: string; readonly attributeAllocations?: string; readonly skillAllocations?: string; }

export interface CharacterStat {
  readonly id: string;
  readonly label: string;
  readonly value: number;
  readonly permanent: number;
  /** For a skill: its career group, and its uses toward the next rise by the ruleset's own threshold. */
  readonly group?: string;
  readonly uses?: number;
  readonly usesNeeded?: number;
  readonly mastered?: boolean;
  /** What the value means, from the ruleset, for its tooltip. */
  readonly description?: string | null;
}

export interface CharacterResource {
  readonly id: string;
  readonly label: string;
  readonly current: number;
  readonly maximum: number;
  readonly description?: string | null;
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
  readonly description?: string | null;
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
  readonly description?: string | null;
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
  /** The career's name as the player knows it, a custom class by its own name. */
  readonly careerLabel?: string;
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
  /** What stops the draft committing, in the ruleset's words; empty when it can commit. */
  readonly blocked?: readonly string[];
  /** Why Quick start cannot begin from these choices, or null when it can. */
  readonly quickStartBlocked?: string | null;
  /** The genders a character can be, with their names. */
  readonly genders?: readonly { readonly id: string; readonly label: string }[];
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
  readonly eligibility: readonly string[]; readonly skills: readonly CharacterSkillChoice[];
  /** The advantages and disadvantages a custom class may take, each option with its name. */
  readonly advantageOptions: readonly { readonly id: string; readonly label: string }[];
  readonly disadvantageOptions: readonly { readonly id: string; readonly label: string }[];
  readonly hitPointsPerLevelRange: { readonly minimum: number; readonly maximum: number; readonly default: number };
  readonly skillCounts: { readonly primary: number; readonly major: number; readonly minor: number };
  readonly traitSlots: number;
}
export interface CharacterLevelUpAttribute { readonly id: string; readonly label: string; readonly permanent: number; readonly live: number; readonly pending: number; readonly canAllocate: boolean; }
export interface CharacterLevelUp { readonly title: string; readonly level: number; readonly bonusPool: number; readonly remainingPoints: number; readonly healthGain: number; readonly canCommit: boolean; readonly attributes: readonly CharacterLevelUpAttribute[]; }

/** Read-only Daggerfall sheet values supplied by the C# projection. */
export interface CharacterProjection {
  readonly name: string;
  readonly attributes: readonly CharacterStat[];
  readonly skills: readonly CharacterStat[];
  /** The skill groups in the order the sheet shows them. */
  readonly skillGroups?: readonly { readonly id: string; readonly label: string }[];
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
  readonly paperDoll?: PaperDoll | null;
}

/** One image the paper doll draws, in source pixels from the doll's top-left corner. */
export interface PaperDollLayer {
  readonly kind: string;
  readonly mediaId: string;
  readonly x: number;
  readonly y: number;
  readonly width: number;
  readonly height: number;
  /** The first and after-last image rows drawn, when only part of the image is. */
  readonly clipTop: number | null;
  readonly clipBottom: number | null;
  /** Where this image is opaque, the doll clears what it drew beneath this layer. */
  readonly mask: string | null;
  readonly slot: string | null;
}

/** The player's paper doll as the ruleset composes it: the background, then every layer in draw order. */
export interface PaperDoll {
  readonly width: number;
  readonly height: number;
  readonly background: PaperDollLayer;
  readonly layers: readonly PaperDollLayer[];
  readonly unplaced: readonly string[];
}

import { heldRevision, image } from './art.js';
import { createCycler, type Cycler, type CyclerOption } from './cycler.js';
import { letterPickerButton } from './letter-picker.js';
import { invalidate, redraw } from './redraw.js';
import { createStepper } from './stepper.js';

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
        ...(value.identity ? [overviewRow('Race', value.identity.raceLabel), overviewRow('Career', value.identity.careerLabel || value.identity.career),
          ...(value.identity.racialOverride ? [overviewRow('Form', `${value.identity.racialOverride.name} · ${value.identity.racialOverride.vampireClan ?? (value.identity.racialOverride.beastForm ? 'Beast form' : 'Human form')}`)] : []),
          overviewRow('Face', `${value.identity.gender} ${format(value.identity.faceIndex + 1)}`)] : []),
      );
      renderRows(resources.rows, value.resources.map(resource => ({
        label: resource.label,
        value: `${format(resource.current)} / ${format(resource.maximum)}`,
        testid: `character-sheet-${resource.id}`,
        tooltip: resource.description,
      })));
      renderRows(attributes.rows, value.attributes.map(stat => ({
        label: stat.label,
        value: stat.value === stat.permanent ? format(stat.value) : `${format(stat.value)} live / ${format(stat.permanent)} permanent`,
        testid: `character-sheet-attribute-${stat.id}`,
        tooltip: stat.description,
      })));
      redraw(skills.rows, JSON.stringify([value.skills, value.skillGroups]), () => renderSkills(skills.rows, value.skills, value.skillGroups ?? []));
      renderRows(resistances.rows, (value.resistances ?? []).map(stat => ({
        label: stat.label,
        value: stat.value === stat.permanent ? format(stat.value) : `${format(stat.value)} live / ${format(stat.permanent)} permanent`,
        testid: `character-sheet-resistance-${stat.id}`,
        tooltip: stat.description,
      })));
      // Each affiliation as a row: the faction and its rank, then the standing that decides the next one.
      factRows(affiliations.rows, (value.affiliations ?? []).map(affiliation => ({
        label: affiliation.faction,
        value: `${affiliation.guildGroup || 'Guild'} · ${affiliation.rankTitle ?? `Rank ${format(affiliation.rank)}`}`,
        facts: [`Reputation ${format(affiliation.reputation)}`, `Recognition ${format(affiliation.recognition)}`,
          ...(affiliation.nextRequirement ? [`Next rank ${format(affiliation.nextRequirement.rank)}: reputation ${format(affiliation.nextRequirement.reputation)}, skills ${format(affiliation.nextRequirement.highSkill)}/${format(affiliation.nextRequirement.lowSkill)}`] : []),
          ...(affiliation.daysUntilReview != null ? [`Review in ${format(affiliation.daysUntilReview)} days`] : []),
          ...(affiliation.privileges?.length ? [`Privileges: ${affiliation.privileges.join(', ')}`] : [])],
        testid: `character-sheet-affiliation-${affiliation.faction}`,
        tooltip: affiliation.description,
      })));
      // Each region's standing as a row, the region the player is in marked, a banishment called out.
      factRows(legal.rows, (value.legalStandings ?? []).map(standing => ({
        label: standing.regionName,
        value: standing.label,
        facts: [`Reputation ${format(standing.reputation)}`, ...(standing.current ? ['Current region'] : []), ...(standing.banished ? ['Banished'] : [])],
        testid: `character-sheet-legal-${standing.region}`,
        warning: standing.banished,
        tooltip: standing.description,
      })));
      renderRows(history.rows, (value.history?.biography ?? []).map((line, index) => ({
        label: `History ${format(index + 1)}`,
        value: line,
        testid: `character-sheet-history-${index}`,
      })));
      keepRows(equipment.rows, value.equipment.map((item, index) => ({ ...item, testid: `${index}:${item.label}` })),
        () => value.equipment.map((item, index) => { const row = equipmentRow(item); row.dataset.row = `${index}:${item.label}`; return row; }));
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

/** A row that explains itself: its tooltip on hover, and on focus for keys and the controller. */
function explain(row: HTMLElement, tooltip: string | null | undefined): void {
  if (!tooltip) return;
  row.dataset.tooltip = tooltip;
  row.tabIndex = 0;
  row.classList.add('dagger-character-explained');
}

/**
 * Rebuilds a section's rows only when what they show changed, and then puts focus back on the row with the
 * same key, so a snapshot that arrives while the player reads a tooltip neither moves nor drops their place.
 */
function keepRows(root: HTMLElement, rows: readonly { readonly testid: string }[], build: () => HTMLElement[]): void {
  const signature = JSON.stringify(rows);
  if (root.dataset.rows === signature) return;
  root.dataset.rows = signature;
  const focused = document.activeElement instanceof HTMLElement && root.contains(document.activeElement)
    ? document.activeElement.closest<HTMLElement>('[data-row]')?.dataset.row ?? null : null;
  root.replaceChildren(...build());
  if (focused !== null) [...root.querySelectorAll<HTMLElement>('[data-row]')].find(row => row.dataset.row === focused)?.focus({ preventScroll: true });
}

function renderRows(root: HTMLElement, values: readonly { readonly label: string; readonly value: string; readonly testid: string; readonly tooltip?: string | null }[]): void {
  keepRows(root, values, () => values.map(value => {
    const row = document.createElement('div');
    row.dataset.row = value.testid;
    explain(row, value.tooltip);
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
  row.className = 'dagger-character-equipment';
  explain(row, item.details);
  const label = document.createElement('dt');
  const slots = document.createElement('span');
  const details = document.createElement('dd');
  label.textContent = item.label;
  if (item.identified === false) label.append(badge('Unidentified'));
  slots.textContent = item.slots.join(' · ');
  details.textContent = item.details;
  row.append(label, slots);
  // Wear as a bar, so a nearly broken piece reads at a glance; the ruleset's wording stays below it.
  if (item.condition && item.condition.maximum > 1) {
    const wear = meter(item.condition.broken ? 0 : item.condition.percentage / 100, item.condition.broken ? 'Broken' : `Condition ${format(item.condition.percentage)}%`);
    wear.classList.toggle('is-low', item.condition.broken || item.condition.percentage < 25);
    row.append(wear);
  }
  row.append(details);
  return row;
}

function badge(text: string): HTMLElement {
  const element = document.createElement('span');
  element.className = 'dagger-character-badge';
  element.textContent = text;
  return element;
}

/** A labelled bar: `fraction` of it filled, the label beside it for the reader and for assistive tools. */
function meter(fraction: number, label: string): HTMLElement {
  const element = document.createElement('span');
  element.className = 'dagger-character-meter';
  element.setAttribute('role', 'meter');
  element.setAttribute('aria-valuemin', '0');
  element.setAttribute('aria-valuemax', '100');
  element.setAttribute('aria-valuenow', String(Math.round(Math.min(1, Math.max(0, fraction)) * 100)));
  element.setAttribute('aria-label', label);
  const fill = document.createElement('span');
  fill.style.width = `${Math.min(1, Math.max(0, fraction)) * 100}%`;
  const text = document.createElement('span');
  text.className = 'dagger-character-meter-label';
  text.textContent = label;
  element.append(fill, text);
  return element;
}

/**
 * The skills under their career groups, each with its value and a bar of its uses toward the next
 * rise; the tooltip says how many. Skills the ruleset gives no group are listed after the groups.
 */
function renderSkills(root: HTMLElement, skills: readonly CharacterStat[], groups: readonly { readonly id: string; readonly label: string }[]): void {
  const sections: HTMLElement[] = [];
  const listed = new Set<string>();
  const add = (title: string | null, members: readonly CharacterStat[]): void => {
    if (members.length === 0) return;
    if (title !== null) { const heading = document.createElement('dt'); heading.className = 'dagger-character-group'; heading.textContent = title; sections.push(heading); }
    for (const stat of members) {
      listed.add(stat.id);
      const row = document.createElement('div');
      row.className = 'dagger-character-skill';
      const term = document.createElement('dt');
      const definition = document.createElement('dd');
      term.textContent = stat.label;
      definition.textContent = stat.value === stat.permanent ? format(stat.value) : `${format(stat.value)} live / ${format(stat.permanent)} permanent`;
      definition.dataset.testid = `character-sheet-skill-${stat.id}`;
      row.append(term, definition);
      if (stat.usesNeeded !== undefined && stat.uses !== undefined) {
        const tip = stat.mastered ? `${stat.label} can rise no further by use.`
          : `${format(stat.uses)} of ${format(stat.usesNeeded)} uses toward the next rise.`;
        explain(row, tip);
        row.append(meter(stat.mastered ? 1 : stat.usesNeeded > 0 ? stat.uses / stat.usesNeeded : 0, stat.mastered ? 'Mastered' : `${format(stat.uses)} / ${format(stat.usesNeeded)}`));
      }
      sections.push(row);
    }
  };
  for (const group of groups) add(group.label, skills.filter(skill => skill.group === group.id));
  add(groups.length > 0 && listed.size > 0 ? 'Other skills' : null, skills.filter(skill => !listed.has(skill.id)));
  root.replaceChildren(...sections);
}

/** Rows whose value has a line of supporting facts under it. */
function factRows(root: HTMLElement, values: readonly { readonly label: string; readonly value: string; readonly facts: readonly string[]; readonly testid: string; readonly warning?: boolean; readonly tooltip?: string | null }[]): void {
  keepRows(root, values, () => values.map(value => {
    const row = document.createElement('div');
    row.dataset.row = value.testid;
    explain(row, value.tooltip);
    row.className = 'dagger-character-fact-row';
    row.classList.toggle('is-warning', value.warning === true);
    const term = document.createElement('dt');
    const definition = document.createElement('dd');
    term.textContent = value.label;
    definition.dataset.testid = value.testid;
    const main = document.createElement('strong');
    main.textContent = value.value;
    const facts = document.createElement('span');
    facts.className = 'dagger-character-facts';
    facts.textContent = value.facts.join(' · ');
    definition.append(main, facts);
    row.append(term, definition);
    return row;
  }));
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
  const options = (items: unknown): boolean => Array.isArray(items) && items.every(item => typeof item === 'object' && item !== null && 'id' in item && typeof item.id === 'string' && 'label' in item && typeof item.label === 'string');
  const traits = (items: unknown): items is readonly CharacterCustomTrait[] => Array.isArray(items) && items.every(item => typeof item === 'object' && item !== null && 'id' in item && typeof item.id === 'string' && 'target' in item && (item.target === null || typeof item.target === 'string'));
  return typeof value === 'object' && value !== null && 'name' in value && typeof value.name === 'string'
    && 'primarySkills' in value && strings(value.primarySkills) && 'majorSkills' in value && strings(value.majorSkills) && 'minorSkills' in value && strings(value.minorSkills)
    && 'hitPointsPerLevel' in value && isNumber(value.hitPointsPerLevel) && 'advantages' in value && traits(value.advantages) && 'disadvantages' in value && traits(value.disadvantages)
    && 'eligibility' in value && strings(value.eligibility) && 'skills' in value && Array.isArray(value.skills) && value.skills.every(skill => typeof skill === 'object' && skill !== null && 'id' in skill && typeof skill.id === 'string' && 'label' in skill && typeof skill.label === 'string') 
    && 'advantageOptions' in value && options(value.advantageOptions) && 'disadvantageOptions' in value && options(value.disadvantageOptions)
    && 'hitPointsPerLevelRange' in value && typeof value.hitPointsPerLevelRange === 'object' && value.hitPointsPerLevelRange !== null
    && 'skillCounts' in value && typeof value.skillCounts === 'object' && value.skillCounts !== null && 'traitSlots' in value && isNumber(value.traitSlots);
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
  // Only the editor below redraws by signature; any other content clears it, so returning to the
  // editor always builds it afresh.
  if (!value?.editing || value.classQuiz) invalidate(root);
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
  // The draft is the ruleset's: every change is sent as it is made and the screen redraws from the
  // answer. It rebuilds only when what it shows changes, keeping the player's control focused; the
  // name, the commit-blocking reasons and Quick start's rule update in place, so typing is never cut off.
  const shape = JSON.stringify({ ...value, current: { ...value.current, name: null }, blocked: null, quickStartBlocked: null });
  redraw(root, shape, () => buildCreation(root, value, send));
  const name = root.querySelector<HTMLInputElement>('[data-testid="character-name"]');
  if (name && document.activeElement !== name && !name.dataset.pending) name.value = value.current.name;
  const blocked = value.blocked ?? [];
  const commit = root.querySelector<HTMLButtonElement>('[data-testid="character-commit"]');
  if (commit) commit.disabled = blocked.length !== 0;
  const reasons = root.querySelector<HTMLElement>('[data-testid="character-commit-blocked"]');
  if (reasons) reasons.textContent = blocked.join(' ');
  const quickStart = root.querySelector<HTMLButtonElement>('[data-testid="character-quick-start"]');
  if (quickStart) {
    quickStart.disabled = (value.quickStartBlocked ?? null) !== null;
    quickStart.dataset.tooltip = value.quickStartBlocked ?? 'Begin a new game now with these choices and a background completed automatically';
  }
}

function buildCreation(root: HTMLElement, value: CharacterCreation, send?: (action: CharacterAction) => void): void {
  const name = document.createElement('input'); name.value = value.current.name; name.setAttribute('aria-label', 'Character name'); name.maxLength = 32;
  name.dataset.testid = 'character-name';
  const choice = (values: readonly CharacterChoice[]): CyclerOption[] => values.map(item => ({ id: item.id, label: item.label, available: item.available, restriction: item.restriction }));
  // Each choice is sent the moment it changes; the ruleset answers with the draft it now holds.
  const live = (): void => send?.(action('character-update'));
  const race = createCycler('Race', choice(value.races), value.current.race, live);
  const genders = value.genders?.length ? value.genders : [{ id: value.current.gender, label: value.current.gender }];
  const gender = createCycler('Gender', genders.map(item => ({ id: item.id, label: item.label })), value.current.gender, live);
  const facePreview = document.createElement('img'); facePreview.className = 'dagger-creation-face'; facePreview.dataset.testid = 'character-face-preview';
  const showFace = (index: string): void => {
    const chosen = value.faces.find(item => String(item.index) === index);
    const source = chosen ? image(chosen.mediaId) : null;
    facePreview.hidden = source === null; if (source !== null) facePreview.src = source;
    facePreview.alt = chosen ? `Face ${chosen.index + 1}` : '';
  };
  const face = createCycler('Face', value.faces.map(item => ({ id: String(item.index), label: `Face ${item.index + 1}` })), String(value.current.faceIndex),
    index => { showFace(index); live(); });
  showFace(face.value());
  const reflexes = createCycler('Reflexes', value.reflexes.map(item => ({ id: String(item.value), label: item.label })), String(value.current.reflexes), live);
  const career = createCycler('Career', choice(value.careers), value.current.career, live);
  const custom = value.custom ?? null;
  const customFields = document.createElement('fieldset'); customFields.dataset.testid = 'character-custom-class';
  const customLegend = document.createElement('legend'); customLegend.textContent = 'Custom class'; customFields.append(customLegend);
  const skillOptions = (custom?.skills ?? []).map(skill => ({ id: skill.id, label: skill.label }));
  const counts = custom?.skillCounts ?? { primary: 0, major: 0, minor: 0 };
  const primary = customSkills('Primary skills', custom?.primarySkills ?? [], skillOptions, counts.primary, live);
  const major = customSkills('Major skills', custom?.majorSkills ?? [], skillOptions, counts.major, live);
  const minor = customSkills('Minor skills', custom?.minorSkills ?? [], skillOptions, counts.minor, live);
  const range = custom?.hitPointsPerLevelRange ?? { minimum: 1, maximum: 1, default: 1 };
  const hp = createStepper('Hit points per level');
  hp.limit(range.minimum, range.maximum, custom?.hitPointsPerLevel ?? range.default);
  // The stepper reports every step (buttons, arrows, typing) as an input event.
  hp.input.addEventListener('input', live);
  const advantages = traitInput('Advantages', custom?.advantages ?? [], custom?.advantageOptions ?? [], custom?.traitSlots ?? 0, live);
  const disadvantages = traitInput('Disadvantages', custom?.disadvantages ?? [], custom?.disadvantageOptions ?? [], custom?.traitSlots ?? 0, live);
  const eligibility = document.createElement('ul'); eligibility.dataset.testid = 'character-custom-eligibility'; eligibility.setAttribute('aria-live', 'polite');
  eligibility.replaceChildren(...(custom?.eligibility ?? []).map(reason => { const item = document.createElement('li'); item.textContent = reason; return item; }));
  customFields.append(primary.element, major.element, minor.element, labeled('HP per level', hp.element), advantages.element, disadvantages.element, eligibility);
  customFields.hidden = career.value() !== 'custom';
  const background = backgroundEditor(value.background ?? null, live);
  // Class questions and quick start carry no background: the ruleset rolls one, and quick start completes it.
  const action = (kind: 'character-update' | 'character-commit' | 'character-background-reroll' | 'character-class-questions' | 'character-quick-start'): CharacterAction => {
    const withBackground = kind !== 'character-class-questions' && kind !== 'character-quick-start';
    const base = { action: kind, name: name.value, race: race.value(), gender: gender.value(), faceIndex: Number(face.value()), reflexes: Number(reflexes.value()), career: career.value() };
    return career.value() === 'custom'
      ? { ...base, primarySkills: primary.values().join(','), majorSkills: major.values().join(','), minorSkills: minor.values().join(','),
        hitPointsPerLevel: hp.value(), advantages: advantages.value(), disadvantages: disadvantages.value(), ...(withBackground ? background.values() : {}) }
      : { ...base, ...(withBackground ? background.values() : {}) };
  };
  // The name is sent as it is typed (after a short pause), so the ruleset's reasons follow it.
  let typing = 0;
  name.addEventListener('input', () => {
    name.dataset.pending = 'true';
    clearTimeout(typing);
    typing = window.setTimeout(() => { delete name.dataset.pending; live(); }, 300);
  });
  name.addEventListener('change', () => { clearTimeout(typing); delete name.dataset.pending; live(); });
  const commit = document.createElement('button'); commit.type = 'button'; commit.textContent = 'Commit character'; commit.dataset.testid = 'character-commit';
  commit.addEventListener('click', () => send?.(action('character-commit')));
  const blocked = document.createElement('p'); blocked.className = 'dagger-creation-blocked'; blocked.dataset.testid = 'character-commit-blocked'; blocked.setAttribute('aria-live', 'polite');
  const cancel = document.createElement('button'); cancel.type = 'button'; cancel.textContent = 'Cancel'; cancel.dataset.testid = 'character-cancel'; cancel.addEventListener('click', () => send?.({ action: 'character-cancel' }));
  const reroll = document.createElement('button'); reroll.type = 'button'; reroll.textContent = 'Reroll background'; reroll.dataset.testid = 'character-background-reroll'; reroll.disabled = value.background === null;
  reroll.addEventListener('click', () => send?.(action('character-background-reroll')));
  const questions = document.createElement('button'); questions.type = 'button'; questions.textContent = 'Answer class questions';
  questions.dataset.testid = 'character-class-questions'; questions.disabled = value.classQuestionsAvailable !== true;
  questions.addEventListener('click', () => send?.(action('character-class-questions')));

  const nameRow = document.createElement('div'); nameRow.className = 'dagger-creation-name-row'; nameRow.append(name, letterPickerButton(name, 'Enter the name with the letter picker'));
  const identity = panel('Identity', 'identity', labeled('Name', nameRow), labeled('Race', race.element), labeled('Gender', gender.element), labeled('Face', face.element), facePreview, labeled('Reflexes', reflexes.element));
  const classPanel = panel('Class', 'class', labeled('Career', career.element), questions, customFields);
  const backgroundPanel = panel('Background', 'background', ...background.story, reroll);
  const attributesPanel = panel('Attributes', 'attributes', ...background.attributes);
  const skillsPanel = panel('Skills', 'skills', ...background.skills);
  attributesPanel.hidden = skillsPanel.hidden = value.background === null || value.background === undefined;
  // A testing convenience: start a new game at once from these choices, with the background completed for you.
  const quickStart = document.createElement('button'); quickStart.type = 'button'; quickStart.textContent = 'Quick start';
  quickStart.dataset.testid = 'character-quick-start';
  quickStart.addEventListener('click', () => send?.(action('character-quick-start')));
  const actions = document.createElement('footer'); actions.className = 'dagger-creation-actions'; actions.append(blocked, cancel, quickStart, commit);
  const layout = document.createElement('section'); layout.className = 'dagger-creation'; layout.dataset.testid = 'character-background';
  layout.append(identity, classPanel, backgroundPanel, attributesPanel, skillsPanel, actions);
  root.replaceChildren(layout);
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
  readonly values: () => Pick<CharacterAction, 'backgroundAnswers' | 'attributeAllocations' | 'skillAllocations'>;
}

const SKILL_TIERS = [['primary', 'Primary'], ['major', 'Major'], ['minor', 'Minor']] as const;

/**
 * The background: its questions, the biography they write, and the bonus points to spend. What is
 * left in each pool and whether a point can go to a row are the ruleset's; a step sends the new
 * allocation at once, and the redraw shows what the ruleset made of it.
 */
function backgroundEditor(value: CharacterBackground | null, live: () => void): BackgroundEditor {
  if (value === null) return { story: [], attributes: [], skills: [], values: () => ({}) };
  const questions = value.questions.map(question => {
    const input = createCycler(`Background question ${question.number}`, question.answers.map(answer => ({ id: answer.letter, label: `${answer.letter.toUpperCase()}. ${answer.text}` })),
      question.selectedLetter ?? question.answers[0]?.letter ?? '', live);
    const element = document.createElement('div'); element.className = 'dagger-creation-question';
    const text = document.createElement('p'); text.textContent = `${question.number}. ${question.text}`;
    element.append(text, input.element);
    return { question: question.number, input, element };
  });
  const biography = document.createElement('div'); biography.className = 'dagger-creation-biography'; biography.dataset.testid = 'character-biography';
  biography.replaceChildren(...value.biography.map(line => { const paragraph = document.createElement('p'); paragraph.textContent = line; return paragraph; }));
  const questionGrid = document.createElement('div'); questionGrid.className = 'dagger-creation-questions';
  questionGrid.append(...questions.map(question => question.element));
  const story: HTMLElement[] = [questionGrid, biography];
  if (value.startingGrants.length !== 0) { const grants = document.createElement('p'); grants.dataset.testid = 'character-starting-grants'; grants.textContent = `Starting grants: ${value.startingGrants.map(grant => `${grant.quantity} × ${grant.label}`).join(', ')}.`; story.push(grants); }
  if (value.unsupportedEffects.length !== 0) { const unsupported = document.createElement('p'); unsupported.dataset.testid = 'character-background-unsupported-effects'; unsupported.textContent = `Recorded effects: ${value.unsupportedEffects.join(' ')}`; story.push(unsupported); }

  const attributeAllocations = new Map(value.attributes.map(attribute => [attribute.id, attribute.allocated]));
  const skillAllocations = new Map(value.skills.map(skill => [skill.id, skill.allocated]));
  const step = (allocations: Map<string, number>, id: string, delta: number): void => {
    allocations.set(id, Math.max(0, (allocations.get(id) ?? 0) + delta));
    live();
  };
  const pools = document.createElement('p'); pools.className = 'dagger-creation-pool'; pools.dataset.testid = 'character-allocation-pools'; pools.setAttribute('aria-live', 'polite');
  pools.textContent = `${value.remainingAttributePoints} bonus ${value.remainingAttributePoints === 1 ? 'point' : 'points'} left`;
  const attributes = value.attributes.map(attribute => stepper('Attributes', attribute.id, attribute.label, attribute.allocated, attribute.value, attribute.canAllocate,
    delta => step(attributeAllocations, attribute.id, delta)));
  const remaining: Readonly<Record<string, number>> = { primary: value.primarySkillPoints, major: value.majorSkillPoints, minor: value.minorSkillPoints };
  const skillElements = SKILL_TIERS.flatMap(([tier, label]) => {
    const rows = value.skills.filter(skill => skill.tier === tier);
    if (rows.length === 0) return [];
    const heading = document.createElement('h4'); heading.dataset.tier = tier;
    const left = remaining[tier] ?? 0;
    heading.textContent = `${label} skills · ${left} ${left === 1 ? 'point' : 'points'} left`;
    return [heading, ...rows.map(skill => stepper('Skills', skill.id, skill.label, skill.allocated, skill.value, skill.canAllocate, delta => step(skillAllocations, skill.id, delta)))];
  });
  const encode = (allocations: Map<string, number>): string => [...allocations].filter(([, points]) => points !== 0).map(([id, points]) => `${id}:${points}`).join(',');
  return {
    story,
    attributes: [pools, ...attributes],
    skills: skillElements,
    values: () => ({
      backgroundAnswers: questions.map(question => `${question.question}:${question.input.value()}`).join(','),
      attributeAllocations: encode(attributeAllocations), skillAllocations: encode(skillAllocations),
    }),
  };
}

/** One allocation row: the points added, − and +, and the total, enabled as the ruleset allows. */
function stepper(group: string, id: string, label: string, allocated: number, total: number, canAllocate: boolean, change: (delta: number) => void): HTMLElement {
  const element = document.createElement('div'); element.className = 'dagger-creation-stepper'; element.dataset.id = id;
  const name = document.createElement('span'); name.className = 'dagger-creation-stepper-label'; name.textContent = label;
  const count = document.createElement('output'); count.setAttribute('aria-label', `${group} ${id}`);
  count.textContent = allocated === 0 ? '·' : `+${allocated}`;
  const sum = document.createElement('strong'); sum.className = 'dagger-creation-stepper-total'; sum.textContent = String(total);
  const less = stepButton('−', `Remove a point from ${label}`, () => change(-1));
  const more = stepButton('+', `Add a point to ${label}`, () => change(1));
  less.disabled = allocated === 0;
  more.disabled = !canAllocate;
  element.append(name, less, count, more, sum);
  return element;
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
function customSkills(label: string, selected: readonly string[], skills: readonly CyclerOption[], count: number, live: () => void): { readonly element: HTMLElement; readonly values: () => string[] } {
  const element = document.createElement('fieldset'); const legend = document.createElement('legend'); legend.textContent = label; element.append(legend);
  const cyclers: Cycler[] = Array.from({ length: count }, (_, index) => {
    const cycler = createCycler(`${label} ${index + 1}`, skills, selected[index] ?? skills[index]?.id ?? '', live);
    element.append(cycler.element);
    return cycler;
  });
  return { element, values: () => cyclers.map(item => item.value()) };
}
function traitInput(label: string, traits: readonly CharacterCustomTrait[], options: readonly CyclerOption[], slots: number, live: () => void): { readonly element: HTMLElement; readonly value: () => string } {
  const element = document.createElement('fieldset');
  const legend = document.createElement('legend'); legend.textContent = label; element.append(legend);
  const values = traits.map(trait => trait.target ? `${trait.id}:${trait.target}` : trait.id);
  const choices = [{ id: '', label: 'None' }, ...options];
  const cyclers = Array.from({ length: slots }, (_, index) => {
    const cycler = createCycler(`${label} ${index + 1}`, choices, values[index] ?? '', live);
    element.append(cycler.element);
    return cycler;
  });
  return { element, value: () => cyclers.map(item => item.value()).filter(Boolean).join(',') };
}

function isNumber(value: unknown): value is number {
  return typeof value === 'number' && Number.isFinite(value);
}

function format(value: number): string {
  return new Intl.NumberFormat(undefined, { maximumFractionDigits: 0 }).format(value);
}
