export function showConcepts(root, stage, request, escape, status = '') {
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
        ${item.id === stage.approved?.id ? '<span class="chip approved">Approved</span>' : ''}
      </button>`).join('')}</div></section>`).join('');
  const approval = stage.approved ? `<div class="concept-selection">
    <button type="button" class="selected-photo" data-inspect="${stage.approved.id}" aria-label="Inspect approved concept">${photo(stage.approved)}</button>
    <div><span class="chip approved">Approved</span> ${caption(stage.approved)}
      <p class="muted">${escape(stage.approved.batch)} · Selected for the next stage</p></div></div>` :
    `<p class="empty">${stage.conflict ? 'Choose one of the concepts from the merged records.' : 'Open a candidate to approve it for the next stage.'}</p>`;
  root.innerHTML = `<div id="concept-stage"><a class="breadcrumb" href="#character/${encodeURIComponent(stage.id)}">Character record</a>
    <header class="page-heading"><div><h1>Concept art</h1><p>${escape(stage.name)} · <code>${escape(stage.id)}</code></p></div></header>
    <section class="panel"><h2>Selected concept</h2>${approval}
    ${status ? `<p class="concept-status" role="status">${escape(status)}</p>` : ''}</section>
    ${groups || '<p class="empty">No concept candidates have been submitted yet.</p>'}
    <dialog id="concept-inspector" aria-labelledby="inspection-name"><div id="inspection"></div></dialog></div>`;

  const page = root.querySelector('#concept-stage');
  const dialog = page.querySelector('#concept-inspector');
  const inspection = page.querySelector('#inspection');
  let index = 0;

  function display() {
    const item = stage.candidates[index];
    const approved = item.id === stage.approved?.id;
    const extra = Object.fromEntries(Object.entries(item.details).filter(([key]) => !['prompt', 'seed', 'model'].includes(key)));
    inspection.innerHTML = `
      <header class="inspection-bar"><div><h2 id="inspection-name">${escape(item.name)}</h2><span class="muted">${escape(item.batch)}</span></div>
      <div class="actions"><button type="button" data-step="-1" ${index === 0 ? 'disabled' : ''}>Previous</button>
      <output>${index + 1} of ${stage.candidates.length}</output>
      <button type="button" data-step="1" ${index + 1 === stage.candidates.length ? 'disabled' : ''}>Next</button>
      <button type="button" data-inspect-close autofocus>Close</button></div></header>
      <div class="inspection-body"><div class="inspection-image">${photo(item, false)}</div>
        <aside class="inspection-details"><div class="actions"><button type="button" class="primary" data-approve ${approved ? 'disabled' : ''}>
        ${approved ? 'Approved' : 'Approve this concept'}</button><a href="${escape(item.url)}" target="_blank" rel="noopener">Original image</a></div>
        <p id="inspection-status" role="status"></p>
        <h3>Generation details</h3><p>${seedLabel(item)}${item.details.model ? ` · ${escape(item.details.model)}` : ''}</p>
        <h3>Prompt</h3><pre>${escape(item.details.prompt)}</pre>
        ${Object.keys(extra).length ? `<h3>Additional settings</h3><pre>${escape(JSON.stringify(extra, null, 2))}</pre>` : ''}
        </aside></div>`;
  }

  async function approve(button) {
    const item = stage.candidates[index];
    const message = inspection.querySelector('#inspection-status');
    button.disabled = true;
    message.textContent = 'Saving approval…';
    try {
      const next = await request(`/api/concepts/${encodeURIComponent(stage.id)}/approve`, {
        method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ candidate: item.id }),
      });
      if (page.isConnected) {
        dialog.close();
        showConcepts(root, next, request, escape, `${item.name} approved.`);
        window.scrollTo(0, 0);
      }
    } catch (error) {
      button.disabled = false;
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
