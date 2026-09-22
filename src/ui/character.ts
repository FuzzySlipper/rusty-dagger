export interface CharacterAction { readonly action: string; readonly name?: string; readonly race?: string; readonly gender?: string; readonly faceIndex?: number; readonly reflexes?: number; readonly career?: string; readonly primarySkills?: string; readonly majorSkills?: string; readonly minorSkills?: string; readonly hitPointsPerLevel?: number; readonly advantages?: string; readonly disadvantages?: string; readonly attribute?: string; readonly backgroundAnswers?: string; readonly attributeAllocations?: string; readonly skillAllocations?: string; }

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
}

export interface CharacterEquipment {
  readonly label: string;
  readonly slots: readonly string[];
  readonly details: string;
}

export interface CharacterIdentity {
  readonly race: string;
  readonly donorRaceId: number;
  readonly portrait: string;
  readonly gender: string;
  readonly faceIndex: number;
  readonly career: string;
  readonly media: readonly CharacterMedia[];
  readonly selectedMedia: readonly CharacterMedia[];
}

export interface CharacterMedia { readonly layer: string; readonly mediaId: string; }
export interface CharacterGrantedSkill { readonly id: string; readonly tier: string; }
export interface CharacterChoice { readonly id: string; readonly label: string; readonly available: boolean; readonly restriction: string | null; }
export interface CharacterFace { readonly index: number; readonly mediaId: string; }
export interface CharacterReflex { readonly value: number; readonly label: string; }
export interface CharacterCreation {
  readonly editing: boolean;
  readonly current: { readonly name: string; readonly race: string; readonly gender: string; readonly faceIndex: number; readonly reflexes: number; readonly career: string; };
  readonly races: readonly CharacterChoice[]; readonly careers: readonly CharacterChoice[];
  readonly faces: readonly CharacterFace[]; readonly reflexes: readonly CharacterReflex[];
  readonly custom?: CharacterCustomClass | null;
  readonly background?: CharacterBackground | null;
}
export interface CharacterBackgroundAnswer { readonly letter: string; readonly text: string; }
export interface CharacterBackgroundQuestion { readonly number: number; readonly text: string; readonly selectedLetter: string | null; readonly answers: readonly CharacterBackgroundAnswer[]; }
export interface CharacterBackgroundAttribute { readonly id: string; readonly label: string; readonly rolled: number; readonly allocated: number; readonly value: number; readonly canAllocate: boolean; }
export interface CharacterBackgroundSkill { readonly id: string; readonly tier: string; readonly rolled: number; readonly allocated: number; readonly biographyBonus: number; readonly value: number; readonly canAllocate: boolean; }
export interface CharacterStartingGrant { readonly itemId: string; readonly templateIndex: number; readonly quantity: number; readonly sourceEffect: string; }
export interface CharacterBackground { readonly biographyClassIndex: number; readonly biography: readonly string[]; readonly questions: readonly CharacterBackgroundQuestion[]; readonly attributes: readonly CharacterBackgroundAttribute[]; readonly attributeBonusPool: number; readonly remainingAttributePoints: number; readonly skills: readonly CharacterBackgroundSkill[]; readonly primarySkillPoints: number; readonly majorSkillPoints: number; readonly minorSkillPoints: number; readonly startingGrants: readonly CharacterStartingGrant[]; readonly unsupportedEffects: readonly string[]; }
export interface CharacterCustomTrait { readonly id: string; readonly target: string | null; }
export interface CharacterCustomClass {
  readonly name: string; readonly primarySkills: readonly string[]; readonly majorSkills: readonly string[]; readonly minorSkills: readonly string[];
  readonly hitPointsPerLevel: number; readonly advantages: readonly CharacterCustomTrait[]; readonly disadvantages: readonly CharacterCustomTrait[];
  readonly eligibility: readonly string[]; readonly skills: readonly string[]; readonly supportedAdvantages: readonly string[]; readonly supportedDisadvantages: readonly string[];
}
export interface CharacterLevelUpAttribute { readonly id: string; readonly label: string; readonly permanent: number; readonly live: number; readonly pending: number; readonly canAllocate: boolean; }
export interface CharacterLevelUp { readonly level: number; readonly bonusPool: number; readonly remainingPoints: number; readonly healthGain: number; readonly canCommit: boolean; readonly attributes: readonly CharacterLevelUpAttribute[]; }

/** Read-only Daggerfall sheet values supplied by the C# projection. */
export interface CharacterProjection {
  readonly name: string;
  readonly attributes: readonly CharacterStat[];
  readonly skills: readonly CharacterStat[];
  readonly resources: readonly CharacterResource[];
  readonly progression: CharacterProgression;
  readonly equipment: readonly CharacterEquipment[];
  readonly identity?: CharacterIdentity | null;
  readonly grantedSkills?: readonly CharacterGrantedSkill[];
  readonly creation?: CharacterCreation | null;
  readonly creationAvailable?: boolean;
  readonly levelUp?: CharacterLevelUp | null;
}

import { image } from './art.js';

export interface CharacterView {
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
  heading.className = 'dagger-character-title';
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
  const equipment = section('Equipped items');
  const career = section('Career training');
  const levelUp = section('Level up');
  const creation = section('Character choices');
  const columns = document.createElement('div');
  columns.className = 'dagger-character-columns';
  columns.append(resources.element, attributes.element, skills.element, career.element, equipment.element, levelUp.element, creation.element);
  shell.append(chrome, heading, overview, columns);
  root.append(shell);
  let disposed = false;
  let held: CharacterProjection | null = null;

  const view: CharacterView = {
    update(value): void {
      if (disposed) return;
      held = value;
      const published = image('window.character-sheet.chrome');
      if (published !== null && chrome.src !== published) chrome.src = published;
      overview.replaceChildren(
        overviewRow('Player', value.name),
        overviewRow('Level', format(value.progression.level)),
        overviewRow('Total XP', format(value.progression.experience)),
        ...(value.identity ? [overviewRow('Race', value.identity.race), overviewRow('Career', value.identity.career),
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
      equipment.rows.replaceChildren(...value.equipment.map(item => equipmentRow(item)));
      renderRows(career.rows, (value.grantedSkills ?? []).map(skill => ({
        label: skill.id,
        value: skill.tier,
        testid: `character-sheet-granted-${skill.id}`,
      })));
      renderCreation(creation.rows, value.creation ?? null, value.creationAvailable === true, send);
      renderLevelUp(levelUp.rows, value.levelUp ?? null, send);
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
    && (!('identity' in value) || value.identity === null || isIdentity(value.identity))
    && (!('grantedSkills' in value) || Array.isArray(value.grantedSkills) && value.grantedSkills.every(isGrantedSkill))
    && (!('creation' in value) || value.creation === null || isCreation(value.creation))
    && (!('creationAvailable' in value) || typeof value.creationAvailable === 'boolean')
    && (!('levelUp' in value) || value.levelUp === null || isLevelUp(value.levelUp));
}

function section(title: string): { readonly element: HTMLElement; readonly rows: HTMLElement } {
  const element = document.createElement('section');
  element.className = 'dagger-character-section';
  const heading = document.createElement('h3');
  heading.textContent = title;
  const rows = document.createElement('dl');
  rows.className = 'dagger-character-rows';
  element.append(heading, rows);
  return { element, rows };
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
    && 'experience' in value && isNumber(value.experience);
}

function isEquipment(value: unknown): value is CharacterEquipment {
  return typeof value === 'object' && value !== null
    && 'label' in value && typeof value.label === 'string'
    && 'slots' in value && Array.isArray(value.slots) && value.slots.every(slot => typeof slot === 'string')
    && 'details' in value && typeof value.details === 'string';
}

function isIdentity(value: unknown): value is CharacterIdentity {
  return typeof value === 'object' && value !== null
    && 'race' in value && typeof value.race === 'string'
    && 'donorRaceId' in value && isNumber(value.donorRaceId)
    && 'portrait' in value && typeof value.portrait === 'string'
    && 'gender' in value && typeof value.gender === 'string'
    && 'faceIndex' in value && isNumber(value.faceIndex)
    && 'career' in value && typeof value.career === 'string'
    && 'media' in value && Array.isArray(value.media) && value.media.every(isMedia)
    && 'selectedMedia' in value && Array.isArray(value.selectedMedia) && value.selectedMedia.every(isMedia);
}

function isMedia(value: unknown): value is CharacterMedia {
  return typeof value === 'object' && value !== null
    && 'layer' in value && typeof value.layer === 'string'
    && 'mediaId' in value && typeof value.mediaId === 'string';
}

function isGrantedSkill(value: unknown): value is CharacterGrantedSkill {
  return typeof value === 'object' && value !== null
    && 'id' in value && typeof value.id === 'string'
    && 'tier' in value && typeof value.tier === 'string';
}

function isCreation(value: unknown): value is CharacterCreation {
  return typeof value === 'object' && value !== null
    && 'editing' in value && typeof value.editing === 'boolean'
    && 'current' in value && isCreationCurrent(value.current)
    && 'races' in value && isChoices(value.races)
    && 'careers' in value && isChoices(value.careers)
    && 'faces' in value && Array.isArray(value.faces) && value.faces.every(face => typeof face === 'object' && face !== null && 'index' in face && isNumber(face.index) && 'mediaId' in face && typeof face.mediaId === 'string')
    && 'reflexes' in value && Array.isArray(value.reflexes) && value.reflexes.every(reflex => typeof reflex === 'object' && reflex !== null && 'value' in reflex && isNumber(reflex.value) && 'label' in reflex && typeof reflex.label === 'string')
    && (!('custom' in value) || value.custom === null || isCustomClass(value.custom))
    && (!('background' in value) || value.background === null || isBackground(value.background));

function isBackground(value: unknown): value is CharacterBackground {
  const allocation = (entry: unknown): boolean => typeof entry === 'object' && entry !== null && 'id' in entry && typeof entry.id === 'string' && 'allocated' in entry && isNumber(entry.allocated) && 'value' in entry && isNumber(entry.value);
  return typeof value === 'object' && value !== null
    && 'biographyClassIndex' in value && isNumber(value.biographyClassIndex) && 'biography' in value && Array.isArray(value.biography) && value.biography.every(line => typeof line === 'string')
    && 'attributeBonusPool' in value && isNumber(value.attributeBonusPool) && 'remainingAttributePoints' in value && isNumber(value.remainingAttributePoints)
    && 'primarySkillPoints' in value && isNumber(value.primarySkillPoints) && 'majorSkillPoints' in value && isNumber(value.majorSkillPoints) && 'minorSkillPoints' in value && isNumber(value.minorSkillPoints)
    && 'questions' in value && Array.isArray(value.questions) && value.questions.every(question => typeof question === 'object' && question !== null && 'number' in question && isNumber(question.number) && 'text' in question && typeof question.text === 'string' && 'selectedLetter' in question && (question.selectedLetter === null || typeof question.selectedLetter === 'string') && 'answers' in question && Array.isArray(question.answers))
    && 'attributes' in value && Array.isArray(value.attributes) && value.attributes.every(allocation)
    && 'skills' in value && Array.isArray(value.skills) && value.skills.every(allocation)
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
    && 'eligibility' in value && strings(value.eligibility) && 'skills' in value && strings(value.skills) && 'supportedAdvantages' in value && strings(value.supportedAdvantages) && 'supportedDisadvantages' in value && strings(value.supportedDisadvantages);
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
  if (!value.editing) { root.replaceChildren(begin); return; }
  const name = document.createElement('input'); name.value = value.current.name; name.setAttribute('aria-label', 'Character name');
  const race = select(value.races, value.current.race); race.setAttribute('aria-label', 'Race');
  const gender = select([{ id: 'male', label: 'Male', available: true, restriction: null }, { id: 'female', label: 'Female', available: true, restriction: null }], value.current.gender); gender.setAttribute('aria-label', 'Gender');
  const face = select(value.faces.map(item => ({ id: String(item.index), label: `Face ${item.index + 1}`, available: true, restriction: null })), String(value.current.faceIndex)); face.setAttribute('aria-label', 'Face');
  const reflexes = select(value.reflexes.map(item => ({ id: String(item.value), label: item.label, available: true, restriction: null })), String(value.current.reflexes)); reflexes.setAttribute('aria-label', 'Reflexes');
  const career = select(value.careers, value.current.career); career.setAttribute('aria-label', 'Career');
  const custom = value.custom ?? null;
  const customFields = document.createElement('fieldset'); customFields.dataset.testid = 'character-custom-class';
  const customLegend = document.createElement('legend'); customLegend.textContent = 'Custom class'; customFields.append(customLegend);
  const skillValues = [...(custom?.skills ?? [])];
  const primary = customSkills('Primary skills', custom?.primarySkills ?? skillValues.slice(0, 3), skillValues, 3);
  const major = customSkills('Major skills', custom?.majorSkills ?? skillValues.slice(3, 6), skillValues, 3);
  const minor = customSkills('Minor skills', custom?.minorSkills ?? skillValues.slice(6, 12), skillValues, 6);
  const hp = document.createElement('input'); hp.type = 'number'; hp.min = '4'; hp.max = '30'; hp.value = String(custom?.hitPointsPerLevel ?? 8); hp.setAttribute('aria-label', 'Hit points per level');
  const advantages = traitInput('Advantages', custom?.advantages ?? [], custom?.supportedAdvantages ?? []);
  const disadvantages = traitInput('Disadvantages', custom?.disadvantages ?? [], custom?.supportedDisadvantages ?? []);
  const eligibility = document.createElement('ul'); eligibility.dataset.testid = 'character-custom-eligibility'; eligibility.setAttribute('aria-live', 'polite');
  eligibility.replaceChildren(...(custom?.eligibility ?? ['Choose Custom class to edit its skills and traits.']).map(reason => { const item = document.createElement('li'); item.textContent = reason; return item; }));
  customFields.append(primary.element, major.element, minor.element, labeled('HP per level', hp), advantages.element, disadvantages.element, eligibility);
  const updateVisibility = (): void => { customFields.hidden = career.value !== 'custom'; };
  career.addEventListener('change', updateVisibility); updateVisibility();
  const background = backgroundEditor(value.background ?? null);
  const commit = document.createElement('button'); commit.type = 'button'; commit.textContent = 'Commit character'; commit.dataset.testid = 'character-commit';
  const action = (kind: 'character-update' | 'character-commit' | 'character-background-reroll'): CharacterAction => career.value === 'custom'
    ? { action: kind, name: name.value, race: race.value, gender: gender.value, faceIndex: Number(face.value), reflexes: Number(reflexes.value), career: career.value,
      primarySkills: primary.values().join(','), majorSkills: major.values().join(','), minorSkills: minor.values().join(','), hitPointsPerLevel: Number(hp.value), advantages: advantages.value(), disadvantages: disadvantages.value(), ...background.values() }
    : { action: kind, name: name.value, race: race.value, gender: gender.value, faceIndex: Number(face.value), reflexes: Number(reflexes.value), career: career.value, ...background.values() };
  const update = document.createElement('button'); update.type = 'button'; update.textContent = 'Check custom class'; update.dataset.testid = 'character-custom-update'; update.addEventListener('click', () => send?.(action('character-update')));
  commit.addEventListener('click', () => send?.(action('character-commit')));
  const cancel = document.createElement('button'); cancel.type = 'button'; cancel.textContent = 'Cancel'; cancel.dataset.testid = 'character-cancel'; cancel.addEventListener('click', () => send?.({ action: 'character-cancel' }));
  const reroll = document.createElement('button'); reroll.type = 'button'; reroll.textContent = 'Reroll background'; reroll.dataset.testid = 'character-background-reroll'; reroll.disabled = value.background === null;
  reroll.addEventListener('click', () => send?.(action('character-background-reroll')));
  root.replaceChildren(name, race, gender, face, reflexes, career, customFields, background.element, reroll, update, commit, cancel);
}

function backgroundEditor(value: CharacterBackground | null): { readonly element: HTMLElement; readonly values: () => Pick<CharacterAction, 'backgroundAnswers' | 'attributeAllocations' | 'skillAllocations'> } {
  const element = document.createElement('fieldset'); element.dataset.testid = 'character-background';
  const legend = document.createElement('legend'); legend.textContent = 'Background and starting abilities'; element.append(legend);
  if (value === null) { element.hidden = true; return { element, values: () => ({}) }; }
  const biography = document.createElement('p'); biography.dataset.testid = 'character-biography'; biography.textContent = value.biography.join(' '); element.append(biography);
  const answers = value.questions.map(question => {
    const label = document.createElement('label'); label.textContent = `${question.number}. ${question.text}`;
    const input = select(question.answers.map(answer => ({ id: answer.letter, label: `${answer.letter.toUpperCase()}. ${answer.text}`, available: true, restriction: null })), question.selectedLetter ?? question.answers[0]?.letter ?? '');
    input.setAttribute('aria-label', `Background question ${question.number}`); label.append(input); element.append(label); return { question: question.number, input };
  });
  const attributes = allocationEditor('Attributes', value.attributes.map(attribute => ({ id: attribute.id, label: `${attribute.label}: ${attribute.value}`, allocated: attribute.allocated, canAllocate: attribute.canAllocate })));
  const skills = allocationEditor('Skills', value.skills.map(skill => ({ id: skill.id, label: `${skill.id} (${skill.tier}): ${skill.value}`, allocated: skill.allocated, canAllocate: skill.canAllocate })));
  const pools = document.createElement('p'); pools.textContent = `${value.remainingAttributePoints} of ${value.attributeBonusPool} attribute points remain; ${value.primarySkillPoints}/${value.majorSkillPoints}/${value.minorSkillPoints} primary/major/minor skill points remain.`; element.append(pools, attributes.element, skills.element);
  if (value.startingGrants.length !== 0) { const grants = document.createElement('p'); grants.dataset.testid = 'character-starting-grants'; grants.textContent = `Starting grants: ${value.startingGrants.map(grant => grant.itemId).join(', ')}.`; element.append(grants); }
  if (value.unsupportedEffects.length !== 0) { const unsupported = document.createElement('p'); unsupported.dataset.testid = 'character-background-unsupported-effects'; unsupported.textContent = `Recorded effects: ${value.unsupportedEffects.join(' ')}`; element.append(unsupported); }
  return { element, values: () => ({ backgroundAnswers: answers.map(answer => `${answer.question}:${answer.input.value}`).join(','), attributeAllocations: attributes.values(), skillAllocations: skills.values() }) };
}

function allocationEditor(title: string, values: readonly { readonly id: string; readonly label: string; readonly allocated: number; readonly canAllocate: boolean }[]): { readonly element: HTMLElement; readonly values: () => string } {
  const element = document.createElement('fieldset'); const legend = document.createElement('legend'); legend.textContent = title; element.append(legend);
  const inputs = values.map(value => { const label = document.createElement('label'); label.textContent = value.label; const input = document.createElement('input'); input.type = 'number'; input.min = '0'; input.value = String(value.allocated); input.disabled = !value.canAllocate && value.allocated === 0; input.setAttribute('aria-label', `${title} ${value.id}`); label.append(input); element.append(label); return { id: value.id, input }; });
  return { element, values: () => inputs.map(value => `${value.id}:${Math.max(0, Number(value.input.value) || 0)}`).filter(value => !value.endsWith(':0')).join(',') };
}

function renderLevelUp(root: HTMLElement, value: CharacterLevelUp | null, send?: (action: CharacterAction) => void): void {
  if (value === null) { root.replaceChildren(); return; }
  const summary = document.createElement('p');
  summary.dataset.testid = 'character-level-up-summary';
  summary.textContent = `Level ${format(value.level)}: ${format(value.remainingPoints)} of ${format(value.bonusPool)} points remain; health gain ${format(value.healthGain)}.`;
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
  const commit = document.createElement('button'); commit.type = 'button'; commit.textContent = 'Commit level up';
  commit.disabled = !value.canCommit; commit.dataset.testid = 'character-level-up-commit';
  commit.addEventListener('click', () => send?.({ action: 'character-level-commit' }));
  root.replaceChildren(summary, allocations, commit);
}

function labeled(label: string, input: HTMLElement): HTMLElement { const item = document.createElement('label'); item.textContent = label; item.append(input); return item; }
function customSkills(label: string, selected: readonly string[], skills: readonly string[], count: number): { readonly element: HTMLElement; readonly values: () => string[] } {
  const element = document.createElement('fieldset'); const legend = document.createElement('legend'); legend.textContent = label; element.append(legend);
  const selects = Array.from({ length: count }, (_, index) => { const input = select(skills.map(id => ({ id, label: id, available: true, restriction: null })), selected[index] ?? skills[index] ?? ''); input.setAttribute('aria-label', `${label} ${index + 1}`); element.append(input); return input; });
  return { element, values: () => selects.map(item => item.value) };
}
function traitInput(label: string, traits: readonly CharacterCustomTrait[], supported: readonly string[]): { readonly element: HTMLElement; readonly value: () => string } {
  const element = document.createElement('label'); element.textContent = `${label} (one id[:target] per line)`;
  const input = document.createElement('textarea'); input.setAttribute('aria-label', label); input.value = traits.map(trait => trait.target ? `${trait.id}:${trait.target}` : trait.id).join('\n'); input.placeholder = supported.join(', '); element.append(input);
  return { element, value: () => input.value.split(/\r?\n/).map(item => item.trim()).filter(Boolean).join(',') };
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
