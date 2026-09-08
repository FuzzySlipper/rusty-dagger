export interface CharacterStat {
  readonly id: string;
  readonly label: string;
  readonly value: number;
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

/** Read-only Daggerfall sheet values supplied by the C# projection. */
export interface CharacterProjection {
  readonly name: string;
  readonly attributes: readonly CharacterStat[];
  readonly skills: readonly CharacterStat[];
  readonly resources: readonly CharacterResource[];
  readonly progression: CharacterProgression;
  readonly equipment: readonly CharacterEquipment[];
}

export interface CharacterView {
  update(value: CharacterProjection): void;
  dispose(): void;
}

export function mountCharacter(root: HTMLElement): CharacterView {
  const shell = document.createElement('section');
  shell.className = 'dagger-character';
  shell.dataset.testid = 'character-sheet';
  const chrome = document.createElement('img');
  chrome.className = 'dagger-character-chrome';
  chrome.alt = '';
  chrome.setAttribute('aria-hidden', 'true');
  chrome.src = new URL('./character-art/window-character-sheet-chrome.png', import.meta.url).href;
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
  const columns = document.createElement('div');
  columns.className = 'dagger-character-columns';
  columns.append(resources.element, attributes.element, skills.element, equipment.element);
  shell.append(chrome, heading, overview, columns);
  root.append(shell);
  let disposed = false;

  return {
    update(value): void {
      if (disposed) return;
      overview.replaceChildren(
        overviewRow('Player', value.name),
        overviewRow('Level', format(value.progression.level)),
        overviewRow('Total XP', format(value.progression.experience)),
      );
      renderRows(resources.rows, value.resources.map(resource => ({
        label: resource.label,
        value: `${format(resource.current)} / ${format(resource.maximum)}`,
        testid: `character-sheet-${resource.id}`,
      })));
      renderRows(attributes.rows, value.attributes.map(stat => ({
        label: stat.label,
        value: format(stat.value),
        testid: `character-sheet-attribute-${stat.id}`,
      })));
      renderRows(skills.rows, value.skills.map(stat => ({
        label: stat.label,
        value: format(stat.value),
        testid: `character-sheet-skill-${stat.id}`,
      })));
      equipment.rows.replaceChildren(...value.equipment.map(item => equipmentRow(item)));
    },
    dispose(): void {
      if (disposed) return;
      disposed = true;
      shell.remove();
    },
  };
}

export function isCharacterProjection(value: unknown): value is CharacterProjection {
  return typeof value === 'object' && value !== null
    && 'name' in value && typeof value.name === 'string'
    && 'attributes' in value && isStats(value.attributes)
    && 'skills' in value && isStats(value.skills)
    && 'resources' in value && Array.isArray(value.resources) && value.resources.every(isResource)
    && 'progression' in value && isProgression(value.progression)
    && 'equipment' in value && Array.isArray(value.equipment) && value.equipment.every(isEquipment);
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
    && 'value' in item && isNumber(item.value));
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

function isNumber(value: unknown): value is number {
  return typeof value === 'number' && Number.isFinite(value);
}

function format(value: number): string {
  return new Intl.NumberFormat(undefined, { maximumFractionDigits: 0 }).format(value);
}
