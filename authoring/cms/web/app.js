const content = document.querySelector('#content');
const queryInput = document.querySelector('#query');
const stateNames = {
  'needs-Patch': 'Needs review', 'needs-check': 'Needs check', 'approved': 'Approved',
  'empty': 'Empty', 'running': 'Running', 'failed': 'Failed', 'rejected': 'Rejected',
  'on-hold': 'On hold', 'skipped': 'Skipped',
};

const escape = value => String(value ?? '').replace(/[&<>"']/g, char => ({
  '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;',
})[char]);
const key = value => encodeURIComponent(value);
const title = (name, intro = '', action = '') => `
  <header class="page-heading"><div><h1>${escape(name)}</h1>${intro ? `<p>${escape(intro)}</p>` : ''}</div>${action}</header>`;
const link = (url, label, css = '') => url ? `<a class="${css}" href="${escape(url)}">${escape(label)}</a>` : '';
const chip = state => `<span class="chip ${escape(state)}">${escape(stateNames[state] ?? state)}</span>`;
const sprite = (url, alt, isPixel = false) => `<div class="preview${isPixel ? ' pixel' : ''}">
  ${url ? `<img src="${escape(url)}" alt="${escape(alt)}" loading="lazy">` : '<span class="muted">No preview</span>'}</div>`;
const empty = message => `<p class="empty">${escape(message)}</p>`;
const subjectUrl = id => `#character/${key(id)}`;
const pipelineUrl = id => `#pipeline/${key(id)}`;
const factList = facts => `<dl class="facts">${Object.entries(facts).map(([name, value]) =>
  `<div><dt>${escape(name)}</dt><dd>${escape(typeof value === 'boolean' ? (value ? 'Yes' : 'No') : value)}</dd></div>`
).join('')}</dl>`;

let catalog;
let currentView = 0;
let actionStatus = '';

async function request(path, options) {
  const response = await fetch(path, options);
  const data = await response.json();
  if (!response.ok) throw new Error(data.error || `Request failed (${response.status})`);
  return data;
}

function sections() {
  return [
    ['npcs', 'NPC records', catalog.npcs.length],
    ['mobiles', 'Classic mobiles', catalog.mobiles.length],
    ['pipelines', 'Character pipelines', catalog.pipelines.length],
    ['flora', 'Flora records', catalog.flora.length],
  ];
}

function overview() {
  const counts = sections().map(([id, label, count]) => `
    <a class="collection" href="#${id}"><strong>${count}</strong><span>${label}</span></a>`).join('');
  const reviews = catalog.review.map(row => `
    <tr><td>${link(pipelineUrl(row.pipeline), row.character)} <code>${escape(row.pipeline)}</code></td>
    <td>${escape(row.name)}</td>
    <td>${chip(row.state)}</td><td>${escape(row.note)}</td>
    <td>${link(row.reviewUrl, 'Workbench review') || link(pipelineUrl(row.pipeline), 'Open stages')}</td></tr>`).join('');
  return title('Authoring overview', '', link('/media/npc/', 'Compare & merge NPCs', 'button primary')) +
    `<div class="collections">${counts}</div><section class="panel"><div class="section-heading"><h2>Needs review</h2>
    <span class="muted">${catalog.review.length} stages</span></div>${reviews ? `
    <div class="table-wrap"><table><thead><tr><th>Character</th><th>Stage</th><th>State</th><th>Note</th><th>Review</th></tr></thead>
    <tbody>${reviews}</tbody></table></div>` : empty('No character stages are waiting for review.')}</section>`;
}

function characterCard(row) {
  const status = row.pipelineIds.length ? `${row.pipelineIds.length} linked pipeline${row.pipelineIds.length === 1 ? '' : 's'}` : row.kind;
  return `<a class="tile" href="${subjectUrl(row.id)}">${sprite(row.classic, '', true)}
    <div class="tile-info"><code>${escape(row.id)}</code><strong>${escape(row.name)}</strong><small>${escape(status)}</small></div></a>`;
}

function pipelineCard(row) {
  const status = row.final ? 'Ready for game testing' : row.stages.find(stage => ['needs-Patch', 'needs-check'].includes(stage.state))?.name;
  return `<a class="tile" href="${pipelineUrl(row.id)}">${sprite(row.thumbnail, '')}
    <div class="tile-info"><code>${escape(row.id)}</code><strong>${escape(row.name)}</strong><small>${escape(status || 'Character stages')}</small></div></a>`;
}

function floraCard(row) {
  return `<a class="tile" href="#flora/${key(row.key)}">${sprite(row.views.front || row.views.sprite, '')}
    <div class="tile-info"><code>${escape(row.key)}</code><strong>${escape(row.form || row.category)}</strong><small>${escape(row.climate)}</small></div></a>`;
}

function listing(page, items = catalog[page], term = '') {
  const label = { npcs: 'NPC records', mobiles: 'Classic mobiles', pipelines: 'Character stages', flora: 'Flora' }[page];
  const card = { npcs: characterCard, mobiles: characterCard, pipelines: pipelineCard, flora: floraCard }[page];
  const field = page === 'flora' ? 'climate' : ['npcs', 'mobiles'].includes(page) ? 'kind' : null;
  const labels = field ? [...new Set(items.map(row => row[field]))].sort() : [];
  const controls = `<div class="filters"><label>Filter <input type="search" id="filter" value="${escape(term)}" placeholder="ID, name, or description"></label>
    ${labels.length > 1 ? `<label>${page === 'flora' ? 'Climate' : 'Type'} <select id="facet"><option value="">All</option>
    ${labels.map(value => `<option>${escape(value)}</option>`).join('')}</select></label>` : ''}
    <output id="results" aria-live="polite"></output></div>`;
  content.innerHTML = title(label, '', page === 'npcs' ? link('/media/npc/', 'Compare & merge NPCs', 'button primary') : '') +
    controls + '<div id="tiles" class="tiles"></div>';
  const filter = document.querySelector('#filter');
  const facet = document.querySelector('#facet');
  function show() {
    const matching = match(items, filter.value).filter(row => !facet?.value || row[field] === facet.value);
    document.querySelector('#tiles').innerHTML = matching.length ? matching.map(card).join('') : empty('No matching records.');
    document.querySelector('#results').textContent = `${matching.length} records`;
  }
  filter.addEventListener('input', show);
  facet?.addEventListener('change', show);
  show();
}

function match(rows, query) {
  const terms = query.toLowerCase().trim().split(/\s+/);
  return rows.filter(row => {
    const haystack = [row.id, row.key, row.name, row.kind, row.category, row.form, row.climate,
      row.brief?.brief, ...(row.aliases ?? [])].join(' ').toLowerCase();
    return terms.every(term => haystack.includes(term));
  });
}

function searchResults(term) {
  const groups = [
    ['NPCs and mobiles', [...catalog.npcs, ...catalog.mobiles], characterCard],
    ['Character stages', catalog.pipelines, pipelineCard], ['Flora', catalog.flora, floraCard],
  ];
  const results = groups.map(([name, rows, card]) => {
    const found = match(rows, term);
    return found.length ? `<section><h2>${name} <small>${found.length}</small></h2>
      <div class="tiles">${found.map(card).join('')}</div></section>` : '';
  }).join('');
  return title(`Search for “${term}”`) + (results || empty('No matching assets.'));
}

function pick(role, art, label) {
  return `<button type="button" data-pick="${role}" data-pipeline="${escape(art.pipeline)}"
    data-path="${escape(art.path)}">${escape(label)}</button>`;
}

function stageTable(pipeline) {
  return `<div class="table-wrap"><table><thead><tr><th>Stage</th><th>State</th><th>Selection or note</th><th>Review</th></tr></thead>
    <tbody>${pipeline.stages.map(row => `<tr><td>${escape(row.name)}</td><td>${chip(row.state)}</td>
    <td>${escape([row.selection.join(', '), row.note].filter(Boolean).join(' · '))}</td>
    <td>${link(row.reviewUrl, 'Open review')}</td></tr>`).join('')}</tbody></table></div>`;
}

function pipelineDetails(pipeline, subject) {
  const concept = pipeline.concepts.map(art => {
    const selected = subject?.concept?.pipeline === art.pipeline && subject.concept.path === art.path;
    const status = art.approved ? 'Approved in pipeline' : art.recorded ? 'Recorded candidate' : 'Present on disk';
    return `<figure>${link(art.url, spriteLabel(art.name), 'source-link')}${sprite(art.url, art.name)}
      <figcaption>${escape(status)} ${subject ? (selected ? chip('Current concept') : pick('concept', art, 'Set as canonical')) : ''}</figcaption></figure>`;
  }).join('');
  const final = pipeline.final;
  const finalSelected = subject?.final?.pipeline === final?.pipeline && subject?.final;
  const automatic = subject?.automaticPipelines.includes(pipeline.id);
  return `<article class="panel pipeline"><div class="section-heading"><div><h3>${escape(pipeline.name)}</h3>
    <code>${escape(pipeline.id)}</code>${pipeline.recipe ? ` <span class="muted">· ${escape(pipeline.recipe)}</span>` : ''}</div>
    <div class="actions">${link(pipeline.stateUrl, 'Stage data')}${link(pipeline.timelineUrl, 'Original timeline')}
    ${subject && !automatic ? `<button type="button" data-unlink="${escape(pipeline.id)}">Unlink</button>` : ''}</div></div>
    ${concept ? `<div class="concept-options">${concept}</div>` : ''}
    ${final ? `<div class="output"><div><strong>Ready for game testing</strong><p>${escape(final.note || final.name)}</p></div>
      ${link(final.url, final.name, 'button')}${subject && !finalSelected ? pick('final', final, 'Set as final') : ''}</div>` : ''}
    ${stageTable(pipeline)}</article>`;
}

function spriteLabel(text) {
  return `Open ${text.toLowerCase()}`;
}

function paintPanel(name, art, fallback, pixel = false) {
  return `<article class="panel depiction"><h2>${escape(name)}</h2>
    ${art ? sprite(art.url, name, pixel) : empty(fallback)}
    ${art ? `<div class="output"><div>${link(art.link || art.url, art.caption)}
    ${art.note ? `<p>${escape(art.note)}</p>` : ''}</div>${art.action || ''}</div>` : ''}
    </article>`;
}

function characterDetail(subject) {
  const art = subject.concept;
  const final = subject.final;
  const candidate = !subject.explicitConcept && subject.conceptChoices.find(item => item.name === 'Front concept');
  const classic = subject.classic && { url: subject.classic, link: subject.classicUrl, caption: 'Classic source' };
  const linked = new Set(subject.pipelineIds);
  const options = catalog.pipelines.filter(pipeline => !linked.has(pipeline.id));
  const linkForm = options.length ? `<form id="link-pipeline"><label>Character pipeline
    <select name="pipeline">${options.map(row => `<option value="${escape(row.id)}">${escape(row.id)} · ${escape(row.name)}</option>`).join('')}</select>
    </label><button class="primary" type="submit">Link pipeline</button></form>` : '';
  const depiction = art ? { url: art.url, caption: art.name,
    note: `${art.pipeline} · ${subject.explicitConcept ? 'Selected here' : 'Approved in pipeline'}`,
    action: subject.explicitConcept ? '<button type="button" data-clear="concept">Use pipeline default</button>' : '' }
    : candidate ? { url: candidate.url, caption: candidate.name, note: `${candidate.pipeline} · ${subject.conflicts.includes('concept') ? 'Choose between merged references' : 'Available reference'}`,
      action: pick('concept', candidate, 'Set as canonical') } : null;
  const conceptPanel = paintPanel('Canonical depiction', depiction,
    subject.explicitConcept ? 'The saved concept is unavailable.' : 'Choose a concept from a linked pipeline.');
  const finalPanel = `<article class="panel depiction final"><h2>Final representation</h2>${final ? `
    <strong>${escape(final.name)}</strong><p>Ready for game testing · ${escape(final.pipeline)}</p>
    <div class="actions">${link(final.url, 'Open final asset', 'button primary')}${link(final.playable, 'Playable manifest')}
    ${subject.explicitFinal ? '<button type="button" data-clear="final">Use pipeline default</button>' : ''}</div>` :
    empty(subject.explicitFinal ? 'The saved final is unavailable.' : subject.conflicts.includes('final') ?
      'Choose a game-ready output from the merged pipelines.' : 'A game-ready output has yet to be selected.')}</article>`;
  const game = subject.game;
  content.innerHTML = `<a class="breadcrumb" href="#${subject.id.startsWith('npc-') ? 'npcs' : 'mobiles'}">Character catalog</a>` +
    title(subject.name, `${subject.id} · ${subject.kind}`) +
    (subject.brief?.brief ? `<p class="brief">${escape(subject.brief.brief)}</p>` : '') +
    `<div class="depictions">${paintPanel('Classic depiction', classic, 'Classic sprite export unavailable.', true)}
      ${conceptPanel}${finalPanel}</div>
    <section class="panel"><div class="section-heading"><h2>Game data</h2>${link(game.url, 'Source record')}</div>
    ${factList(game.placementCount === undefined ? game.facts : { ...game.facts, 'Source placements': game.placementCount })}
    ${game.record ? `<details><summary>Mobile definition</summary><pre>${escape(JSON.stringify(game.record, null, 2))}</pre></details>` : ''}</section>
    <section><div class="section-heading"><h2>Character pipelines</h2>${linkForm}</div>
    <p id="action-status" role="status">${escape(actionStatus)}</p>
    ${subject.missingPipelines.length ? empty(`Unavailable pipelines: ${subject.missingPipelines.join(', ')}`) : ''}
    ${subject.pipelines.length ? subject.pipelines.map(pipeline => pipelineDetails(pipeline, subject)).join('') :
      empty('Link an existing character pipeline to connect concept art, animation stages, and game outputs.')}</section>
    ${subject.turnaroundStill ? `<section class="panel"><h2>Directional reference</h2>
      <div class="concept-options"><figure>${sprite(subject.turnaroundStill, 'Curated front view')}<figcaption>
      ${link(subject.turnaround, 'Open turnaround')}</figcaption></figure></div></section>` : ''}`;
  actionStatus = '';
  bindLinks(subject);
}

function bindLinks(subject) {
  async function save(button, change) {
    const notice = document.querySelector('#action-status');
    button.disabled = true;
    notice.textContent = 'Saving link…';
    try {
      const result = await request(`/api/characters/${key(subject.id)}/links`, {
        method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(change),
      });
      catalog = await request('/api/catalog');
      if (window.location.hash === subjectUrl(subject.id)) {
        actionStatus = 'Saved.';
        characterDetail(result);
      }
    } catch (error) {
      notice.textContent = error.message;
      button.disabled = false;
    }
  }
  const form = document.querySelector('#link-pipeline');
  form?.addEventListener('submit', event => {
    event.preventDefault();
    save(form.querySelector('button'), { addPipeline: new FormData(form).get('pipeline') });
  });
  for (const button of content.querySelectorAll('[data-unlink]'))
    button.addEventListener('click', () => save(button, { removePipeline: button.dataset.unlink }));
  for (const button of content.querySelectorAll('[data-pick]'))
    button.addEventListener('click', () => save(button, {
      [button.dataset.pick]: { pipeline: button.dataset.pipeline, path: button.dataset.path },
    }));
  for (const button of content.querySelectorAll('[data-clear]'))
    button.addEventListener('click', () => save(button, { [button.dataset.clear]: null }));
}

function floraDetail(row) {
  const views = Object.entries(row.views).map(([angle, url]) => `<article class="panel depiction"><h2>${escape(
    { sprite: 'Sprite', front: 'Mesh front', side: 'Mesh side', rear: 'Mesh rear' }[angle]
  )}</h2>${sprite(url, angle)}${link(url, 'Open render', 'source-link')}</article>`).join('');
  return '<a class="breadcrumb" href="#flora">Flora catalog</a>' +
    title(row.form || row.category, `${row.key} · ${row.climate}`, link('/media/flora/', 'Original flora review', 'button')) +
    factList({ Category: row.category, Height: `${Number(row.height).toFixed(2)} m`, Triangles: row.triangles }) +
    `<div class="flora-views">${views || empty('Render assets are unavailable.')}</div>`;
}

async function render() {
  const view = ++currentView;
  const [page = 'overview', encoded = ''] = location.hash.slice(1).split('/');
  let id;
  try {
    id = decodeURIComponent(encoded);
    const section = page === 'character' ? (id.startsWith('npc-') ? 'npcs' : 'mobiles') :
      page === 'pipeline' ? 'pipelines' : page;
    for (const element of document.querySelectorAll('[data-page]'))
      element.toggleAttribute('aria-current', element.dataset.page === section);
    window.scrollTo(0, 0);
    if (!catalog) catalog = await request('/api/catalog');
    if (view !== currentView) return;
    if (page === 'character' && id) {
      const subject = await request(`/api/characters/${key(id)}`);
      if (view === currentView) characterDetail(subject);
    } else if (page === 'pipeline' && id) {
      const row = catalog.pipelines.find(row => row.id === id);
      if (!row) throw new Error('Character pipeline not found.');
      const pipeline = await request(`/api/pipelines/${key(row.id)}`);
      if (view !== currentView) return;
      content.innerHTML = title(row.name, row.id) +
        `<div class="actions">${row.subjects.map(subject => link(subjectUrl(subject), subject, 'button')).join('')}</div>` +
        pipelineDetails(pipeline, null);
    } else if (page === 'flora' && id) {
      const row = catalog.flora.find(row => row.key === id);
      if (!row) throw new Error('Flora record not found.');
      content.innerHTML = floraDetail(row);
    } else if (page === 'search' && id) {
      content.innerHTML = searchResults(id);
    } else if (['npcs', 'mobiles', 'pipelines', 'flora'].includes(page)) {
      listing(page);
    } else {
      content.innerHTML = overview();
    }
  } catch (error) {
    if (view === currentView) content.innerHTML = title('Unable to open this view') + empty(error.message);
  }
}

document.querySelector('#asset-search').addEventListener('submit', event => {
  event.preventDefault();
  const term = queryInput.value.trim();
  if (term) location.hash = `search/${key(term)}`;
});
window.addEventListener('hashchange', render);
render();
