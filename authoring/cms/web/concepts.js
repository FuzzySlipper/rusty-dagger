const viewLabels = { front: 'Front', back: 'Back' };

export function showConcepts(root, stage, request, escape, status = '') {
  const views = stage.approvals ? Object.keys(viewLabels) : ['front'];
  const approvals = stage.approvals ?? { front: stage.approved };
  const conflicts = stage.conflicts ?? (stage.conflict ? ['front'] : []);
  const batches = new Map();
  for (const item of stage.candidates) {
    if (!batches.has(item.batch)) batches.set(item.batch, []);
    batches.get(item.batch).push(item);
  }
  const photo = (item, lazy = true) =>
    `<img src="${escape(item.url)}" alt="${escape(item.name)}" ${lazy ? 'loading="lazy"' : ''}>`;
  const seedLabel = item => item.details.seed === null ? 'Seed unavailable' : `Seed ${escape(item.details.seed)}`;
  const caption = item => `<span class="candidate-title">${escape(item.name)}</span>
    <span class="candidate-data">${seedLabel(item)}${item.details.model ? ` · ${escape(item.details.model)}` : ''}</span>`;
  const groups = [...batches].map(([batch, items]) => `
    <section class="concept-batch"><div class="section-heading"><h2>${escape(batch)}</h2><small>${items.length}
      ${items.length === 1 ? 'candidate' : 'candidates'}</small></div>
      <div class="concept-grid">${items.map(item => `
      <button class="concept-card" type="button" data-inspect="${item.id}">
        <span class="concept-photo">${photo(item)}</span>
        ${caption(item)}<span class="candidate-prompt">${escape(item.details.prompt)}</span>
        ${views.filter(view => item.id === approvals[view]?.id)
          .map(view => `<span class="chip approved">${viewLabels[view]} approved</span>`).join('')}
      </button>`).join('')}</div></section>`).join('');
  const selections = views.map(view => {
    const item = approvals[view];
    const label = viewLabels[view];
    const message = conflicts.includes(view) ? `Choose the ${view} from the merged records.` : `Choose a ${view} from the candidates.`;
    return `<section class="panel"><h2>Approved ${view}</h2>
      ${item ? `<div class="concept-selection">
        <button type="button" class="selected-photo" data-inspect="${item.id}" aria-label="Inspect approved ${view}">
        ${photo(item)}</button>
        <div>${caption(item)}<p class="muted">${escape(item.batch)} · ${label} reference</p></div></div>` :
      `<p class="empty">${message}</p>`}</section>`;
  }).join('');
  root.innerHTML = `<div id="concept-stage"><a class="breadcrumb" href="#character/${encodeURIComponent(stage.id)}">Character record</a>
    <header class="page-heading"><div><h1>Concept art</h1><p>${escape(stage.name)} · <code>${escape(stage.id)}</code></p></div></header>
    <div class="concept-approvals">${selections}</div>
    ${status ? `<p class="concept-status" role="status">${escape(status)}</p>` : ''}
    ${groups || '<p class="empty">No concept candidates have been submitted yet.</p>'}
    <dialog id="concept-inspector" aria-labelledby="inspection-name"><div id="inspection"></div></dialog></div>`;

  const page = root.querySelector('#concept-stage');
  const dialog = page.querySelector('#concept-inspector');
  const inspection = page.querySelector('#inspection');
  let index = 0;

  function display() {
    const item = stage.candidates[index];
    const extra = Object.fromEntries(Object.entries(item.details).filter(([key]) => !['prompt', 'seed', 'model'].includes(key)));
    inspection.innerHTML = `
      <header class="inspection-bar"><div><h2 id="inspection-name">${escape(item.name)}</h2><span class="muted">${escape(item.batch)}</span></div>
      <div class="actions"><button type="button" data-step="-1" ${index === 0 ? 'disabled' : ''}>Previous</button>
      <output>${index + 1} of ${stage.candidates.length}</output>
      <button type="button" data-step="1" ${index + 1 === stage.candidates.length ? 'disabled' : ''}>Next</button>
      <button type="button" data-inspect-close autofocus>Close</button></div></header>
      <div class="inspection-body"><div class="inspection-image">${photo(item, false)}</div>
        <aside class="inspection-details"><div class="actions">
        ${views.map(view => {
          const approved = item.id === approvals[view]?.id;
          return `<button type="button" class="primary" data-approve="${view}" ${approved ? 'disabled' : ''}>
            ${approved ? `${viewLabels[view]} approved` : `Approve as ${view}`}</button>`;
        }).join('')}
        <a href="${escape(item.url)}" target="_blank" rel="noopener">Original image</a></div>
        <p id="inspection-status" role="status"></p>
        <h3>Generation details</h3><p>${seedLabel(item)}${item.details.model ? ` · ${escape(item.details.model)}` : ''}</p>
        <h3>Prompt</h3><pre>${escape(item.details.prompt)}</pre>
        ${Object.keys(extra).length ? `<h3>Additional settings</h3><pre>${escape(JSON.stringify(extra, null, 2))}</pre>` : ''}
        </aside></div>`;
  }

  async function approve(button) {
    const item = stage.candidates[index];
    const view = button.dataset.approve;
    const message = inspection.querySelector('#inspection-status');
    const actions = [...inspection.querySelectorAll('[data-approve]:enabled, [data-step]:enabled')];
    for (const action of actions) action.disabled = true;
    message.textContent = 'Saving approval…';
    try {
      const next = await request(`/api/concepts/${encodeURIComponent(stage.id)}/approve`, {
        method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ candidate: item.id, view }),
      });
      if (page.isConnected) {
        dialog.close();
        showConcepts(root, next, request, escape, `${item.name} approved as the ${view} concept.`);
        window.scrollTo(0, 0);
      }
    } catch (error) {
      for (const action of actions) action.disabled = false;
      message.textContent = error.message;
    }
  }

  page.addEventListener('click', event => {
    const button = event.target.closest('button');
    if (!button) return;
    if (button.dataset.inspect) {
      index = stage.candidates.findIndex(item => item.id === button.dataset.inspect);
      display();
      dialog.showModal();
    } else if (button.hasAttribute('data-inspect-close')) {
      dialog.close();
    } else if (button.dataset.step) {
      index += Number(button.dataset.step);
      display();
    } else if (button.hasAttribute('data-approve')) {
      approve(button);
    }
  });
}
