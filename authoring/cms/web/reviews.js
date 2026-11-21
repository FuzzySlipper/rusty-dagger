const ratings = ['good', 'maybe', 'bad'];
const reviewRows = new Map();
const entryKey = (category, id) => JSON.stringify([category, id]);
const ratingName = value => value[0].toUpperCase() + value.slice(1);

export function loadReviews(rows) {
  reviewRows.clear();
  for (const row of rows) reviewRows.set(entryKey(row.category, row.id), row);
}

export function reviewFor(category, id) {
  return reviewRows.get(entryKey(category, id)) ?? { marked: false, rating: null, note: '' };
}

export function markedCount(category) {
  return [...reviewRows.values()].filter(row => row.category === category && row.marked).length;
}

export function reviewBadge(row) {
  return row.rating ? `<span class="chip review-${row.rating}">${ratingName(row.rating)}</span>` :
    row.previousRating ? '<span class="chip needs-check">Needs re-review</span>' : '';
}

function wrapper(category, id, escape) {
  return `data-review-category="${escape(category)}" data-review-id="${escape(id)}"`;
}

function markControl(row) {
  return `<button type="button" data-mark aria-pressed="${row.marked}">${row.marked ? 'Marked' : 'Mark'}</button>`;
}

export function tileReview(category, id, escape) {
  const row = reviewFor(category, id);
  return `<div class="tile-review" ${wrapper(category, id, escape)}>${markControl(row)}
    <span data-rating-badge>${reviewBadge(row)}</span>
    ${row.note || row.related?.some(item => item.note) ? '<span class="muted">Note saved</span>' : ''}
    <output data-review-status role="status"></output></div>`;
}

export function reviewPanel(category, id, escape, heading = 'Review and notes', revision = null) {
  const row = reviewFor(category, id);
  const related = row.related?.map(item => `<p><code>${escape(item.id)}</code>
    ${reviewBadge(item)} ${escape(item.note)}</p>`).join('');
  const version = revision === null ? '' : ` data-review-revision="${escape(revision)}"`;
  return `<section class="panel record-review" ${wrapper(category, id, escape)}${version}>
    <div class="section-heading"><h2>${escape(heading)}</h2>${markControl(row)}</div>
    <div class="rating-bar" role="group" aria-label="Review rating">
      ${ratings.map(rating => `<button type="button" class="review-${rating}" data-rating="${rating}"
        aria-pressed="${row.rating === rating}">${ratingName(rating)}</button>`).join('')}
      <button type="button" data-rating="" ${row.rating || row.previousRating ? '' : 'disabled'}>Clear rating</button>
      <span data-rating-badge>${reviewBadge(row)}</span></div>
    <form data-note-form><label>Note<textarea name="note" rows="3">${escape(row.note)}</textarea></label>
      <button type="submit">Save note</button> <output data-review-status role="status"></output></form>
    ${related ? `<details><summary>Notes and ratings from merged IDs</summary>${related}</details>` : ''}
    </section>`;
}

export function installReviewActions(root, request, changed) {
  function showChanges(row) {
    for (const element of root.querySelectorAll('[data-review-category]')) {
      if (element.dataset.reviewCategory !== row.category || element.dataset.reviewId !== row.id) continue;
      const mark = element.querySelector('[data-mark]');
      mark.textContent = row.marked ? 'Marked' : 'Mark';
      mark.setAttribute('aria-pressed', String(row.marked));
      const badge = element.querySelector('[data-rating-badge]');
      badge.innerHTML = reviewBadge(row);
      for (const button of element.querySelectorAll('[data-rating]')) {
        if (button.dataset.rating) button.setAttribute('aria-pressed', String(row.rating === button.dataset.rating));
        else button.disabled = !(row.rating || row.previousRating);
      }
    }
  }

  async function save(target, change) {
    const region = target.closest('[data-review-category]');
    const { reviewCategory: category, reviewId: id, reviewRevision } = region.dataset;
    if (change.rating && reviewRevision !== undefined) change.expectedRevision = reviewRevision;
    const status = region.querySelector('[data-review-status]');
    const button = target.matches('form') ? target.querySelector('button[type=submit]') : target;
    button.disabled = true;
    try {
      const row = await request(`/api/reviews/${encodeURIComponent(category)}/${encodeURIComponent(id)}`, {
        method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(change),
      });
      reviewRows.set(entryKey(category, id), row);
      showChanges(row);
      if (status) status.textContent = 'Saved.';
      changed(category);
    } catch (error) {
      if (status) status.textContent = error.message;
    } finally {
      button.disabled = button.matches('[data-rating=""]') && !(reviewFor(category, id).rating || reviewFor(category, id).previousRating);
    }
  }

  root.addEventListener('click', async event => {
    const button = event.target.closest('button');
    if (!button) return;
    if (button.hasAttribute('data-mark')) {
      const { reviewCategory, reviewId } = button.closest('[data-review-category]').dataset;
      await save(button, { marked: !reviewFor(reviewCategory, reviewId).marked });
    } else if (button.hasAttribute('data-rating')) {
      await save(button, { rating: button.dataset.rating || null });
    } else if (button.dataset.clearMarks) {
      const category = button.dataset.clearMarks;
      const status = root.querySelector('#mark-status');
      button.disabled = true;
      try {
        await request(`/api/marks/${encodeURIComponent(category)}/clear`, { method: 'POST' });
        for (const row of reviewRows.values()) {
          if (row.category === category && row.marked) {
            row.marked = false;
            showChanges(row);
          }
        }
        changed(category);
        status.textContent = 'Marks cleared.';
      } catch (error) {
        status.textContent = error.message;
      } finally {
        button.disabled = markedCount(category) === 0;
      }
    }
  });

  root.addEventListener('submit', event => {
    if (!event.target.matches('[data-note-form]')) return;
    event.preventDefault();
    save(event.target, { note: new FormData(event.target).get('note') });
  });
}
