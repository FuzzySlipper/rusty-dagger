const grid = document.querySelector('#chart');
const search = document.querySelector('#search');
const count = document.querySelector('#count');
const empty = document.querySelector('#empty');
const similar = document.querySelector('#similar');
const similarLabel = document.querySelector('#similar-label');
const comparison = document.querySelector('#comparison');
const comparisonList = document.querySelector('#comparison-list');
const comparisonCount = document.querySelector('#comparison-count');
const facets = ['kind', 'costume', 'color', 'prop', 'pose'].map(key => [key, document.getElementById(key)]);
const splitTags = value => value ? value.split('|') : [];
const accessories = new Set(['belt', 'boots', 'jewelry', 'gloves', 'bare feet']);

const cards = [...grid.children].map(element => {
  const { id, name, kind, search, brief, costume, color, prop, pose } = element.dataset;
  const costumeTags = splitTags(costume);
  return {
    element, id, name, search, brief,
    button: element.querySelector('.compare-toggle'),
    garments: costumeTags.filter(tag => !accessories.has(tag)),
    tags: { kind: [kind], costume: costumeTags, color: splitTags(color), prop: splitTags(prop), pose: splitTags(pose) },
  };
});
const ids = new Set(cards.map(card => card.id));
const selected = new Set();
const comparisonLimit = 4;
let similarityScores = null;

function filter() {
  const query = search.value.toLowerCase().trim();
  const number = /^(?:npc-)?([0-9]{3})$/.exec(query);
  const npcId = number ? `npc-${number[1]}` : query;
  const exactId = ids.has(npcId) ? npcId : null;
  const words = npcId.split(/\s+/);
  let shown = 0;
  for (const card of cards) {
    const matchesSearch = exactId ? card.id === exactId : words.every(word => card.search.includes(word));
    const matchesFacets = facets.every(([key, select]) => !select.value || card.tags[key].includes(select.value));
    const matchesSimilarity = !similarityScores || similarityScores.get(card) > 0;
    card.element.hidden = !(matchesSearch && matchesFacets && matchesSimilarity);
    if (!card.element.hidden) shown++;
  }
  count.textContent = `${shown} ${shown === 1 ? 'reference' : 'references'}`;
  empty.hidden = shown > 0;
}

function overlap(left, right) {
  const common = left.filter(tag => right.includes(tag)).length;
  return common ? common / (left.length + right.length - common) : 0;
}

function similarity(source, candidate) {
  if (source === candidate) return Infinity;
  const garments = overlap(source.garments, candidate.garments);
  const prop = overlap(source.tags.prop, candidate.tags.prop);
  if (!garments && !prop) return 0;
  // Common accessories can break ties. Matching boots alone doesn't make two outfits similar.
  // Pose stays independent so kneeling and standing variants can rank together.
  const costume = overlap(source.tags.costume, candidate.tags.costume);
  const color = overlap(source.tags.color, candidate.tags.color);
  return 7 * garments + costume + 2 * color + 2 * prop;
}

function findSimilar(source) {
  let ordered = cards;
  similarityScores = null;
  if (source) {
    search.value = '';
    similarityScores = new Map(cards.map(card => [card, similarity(source, card)]));
    ordered = [...cards].sort((a, b) => similarityScores.get(b) - similarityScores.get(a));
    similarLabel.textContent = `Similar to ${source.id}`;
  }
  for (const card of ordered) grid.append(card.element);
  similar.hidden = !source;
  filter();
}

function node(tag, text, className) {
  const element = document.createElement(tag);
  if (text) element.textContent = text;
  if (className) element.className = className;
  return element;
}

function action(label, run) {
  const button = node('button', label);
  button.type = 'button';
  button.addEventListener('click', run);
  return button;
}

function comparisonItem(card) {
  const item = node('li');
  const link = node('a');
  link.href = card.element.querySelector('.card').getAttribute('href');
  const picture = card.element.querySelector('.picture').cloneNode(true);
  const image = picture.querySelector('img');
  if (image) image.loading = 'eager';
  link.append(picture, `${card.name} · ${card.id}`);
  item.append(link);
  if (card.brief) item.append(node('p', card.brief));
  if (card.tags.pose.length) item.append(node('p', `Pose · ${card.tags.pose.join(', ')}`));
  const actions = node('div', '', 'comparison-actions');
  const similarButton = action('Find similar', () => findSimilar(card));
  similarButton.disabled = !card.garments.length && !card.tags.prop.length;
  const removeButton = action('Remove', () => {
    toggle(card);
    if (selected.size) document.querySelector('#clear-comparison').focus();
    else search.focus();
  });
  removeButton.setAttribute('aria-label', `Remove ${card.id} from comparison`);
  actions.append(similarButton, removeButton);
  item.append(actions);
  return item;
}

function updateComparison() {
  for (const card of cards) {
    const active = selected.has(card);
    card.button.textContent = active ? 'Selected' : 'Compare';
    card.button.setAttribute('aria-pressed', String(active));
    card.button.disabled = !active && selected.size >= comparisonLimit;
  }
  comparisonList.replaceChildren(...Array.from(selected, comparisonItem));
  comparisonCount.textContent = `${selected.size} of ${comparisonLimit} selected`;
  comparison.hidden = !selected.size;
}

function toggle(card) {
  if (selected.has(card)) selected.delete(card);
  else if (selected.size < comparisonLimit) selected.add(card);
  updateComparison();
}

for (const card of cards) card.button.addEventListener('click', () => toggle(card));
document.querySelector('#clear-comparison').addEventListener('click', () => {
  selected.clear();
  updateComparison();
  search.focus();
});
document.querySelector('#clear-similar').addEventListener('click', () => {
  findSimilar(null);
  search.focus();
});
search.addEventListener('input', filter);
for (const [, select] of facets) select.addEventListener('change', filter);
window.addEventListener('pageshow', filter);
