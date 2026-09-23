export interface BookReaderProjection {
  readonly id: number;
  readonly title: string;
  readonly author: string;
  readonly page: number;
  readonly pageCount: number;
  readonly text: string;
}

export interface NotebookNoteProjection {
  readonly id: string;
  readonly text: string;
}

export interface NotebookProjection {
  readonly revision: string;
  readonly book: BookReaderProjection | null;
  readonly notes: readonly NotebookNoteProjection[];
}

export type NotebookAction =
  | { readonly action: 'notebook-page'; readonly revision: string; readonly page: number }
  | { readonly action: 'notebook-add'; readonly revision: string; readonly text: string }
  | { readonly action: 'notebook-edit'; readonly revision: string; readonly note: string; readonly text: string }
  | { readonly action: 'notebook-remove'; readonly revision: string; readonly note: string }
  | { readonly action: 'notebook-move'; readonly revision: string; readonly note: string; readonly destination: number };

/** Renders product-projected book pages and notes; every edit remains a guarded semantic request. */
export function mountNotebook(root: HTMLElement, claim: (action: NotebookAction) => void): {
  update(value: NotebookProjection): void; dispose(): void;
} {
  const shell = document.createElement('section');
  shell.className = 'dagger-notebook';
  shell.setAttribute('aria-label', 'Book reader and notebook');
  const reader = document.createElement('section');
  reader.className = 'dagger-book-reader';
  const notes = document.createElement('section');
  notes.className = 'dagger-user-notes';
  shell.append(reader, notes);
  root.append(shell);

  let disposed = false;

  const render = (value: NotebookProjection): void => {
    renderReader(reader, value, claim);
    renderNotes(notes, value, claim);
  };

  return {
    update(value: NotebookProjection): void {
      if (!disposed) render(value);
    },
    dispose(): void {
      disposed = true;
      shell.remove();
    },
  };
}

function renderReader(reader: HTMLElement, value: NotebookProjection, claim: (action: NotebookAction) => void): void {
  const book = value.book;
  reader.replaceChildren();
  const heading = document.createElement('h2');
  heading.textContent = 'Book reader';
  reader.append(heading);
  if (book === null) {
    const empty = document.createElement('p');
    empty.textContent = 'Use a readable book in your inventory to open it here.';
    reader.append(empty);
    return;
  }
  const title = document.createElement('h3');
  title.dataset.testid = 'book-reader-title';
  title.textContent = book.title;
  const author = document.createElement('p');
  author.className = 'dagger-book-author';
  author.textContent = book.author ? `By ${book.author}` : 'Author unknown';
  const page = document.createElement('p');
  page.className = 'dagger-book-page';
  page.dataset.testid = 'book-reader-page';
  page.textContent = book.text;
  const controls = document.createElement('div');
  controls.className = 'dagger-book-controls';
  const previous = document.createElement('button');
  previous.type = 'button'; previous.textContent = 'Previous page'; previous.disabled = book.page === 0;
  previous.dataset.testid = 'book-reader-previous';
  previous.addEventListener('click', () => claim({ action: 'notebook-page', revision: value.revision, page: book.page - 1 }));
  const location = document.createElement('span');
  location.textContent = `Page ${book.page + 1} of ${book.pageCount}`;
  const next = document.createElement('button');
  next.type = 'button'; next.textContent = 'Next page'; next.disabled = book.page + 1 >= book.pageCount;
  next.dataset.testid = 'book-reader-next';
  next.addEventListener('click', () => claim({ action: 'notebook-page', revision: value.revision, page: book.page + 1 }));
  controls.append(previous, location, next);
  reader.append(title, author, page, controls);
}

function renderNotes(notes: HTMLElement, value: NotebookProjection, claim: (action: NotebookAction) => void): void {
  // Reconcile the note list by its durable note id. Product projections arrive for unrelated
  // changes too, and replacing the subtree on every one would discard an in-progress edit and
  // the browser's focus/caret state before the player can submit it.
  let heading = notes.querySelector('h2');
  let add = notes.querySelector('form');
  let input = add?.querySelector('textarea');
  let list = notes.querySelector('ol');
  if (!(heading && add && input && list)) {
    notes.replaceChildren();
    heading = document.createElement('h2');
    heading.textContent = 'Notebook';
    add = document.createElement('form');
    add.className = 'dagger-note-add';
    input = document.createElement('textarea');
    input.maxLength = 2048; input.setAttribute('aria-label', 'New notebook note');
    const addButton = document.createElement('button');
    addButton.type = 'submit'; addButton.textContent = 'Add note';
    add.append(input, addButton);
    list = document.createElement('ol');
    list.className = 'dagger-note-list';
    notes.append(heading, add, list);
  }
  add.onsubmit = event => {
    event.preventDefault();
    const text = input!.value.trim();
    if (text) {
      input!.value = '';
      claim({ action: 'notebook-add', revision: value.revision, text });
    }
  };

  const existing = new Map<string, HTMLElement>();
  for (const child of Array.from(list.children)) {
    if (child instanceof HTMLElement && child.dataset.noteId) existing.set(child.dataset.noteId, child);
  }
  const retained = new Set<string>();
  value.notes.forEach((note, index) => {
    let row = existing.get(note.id);
    if (!row) {
      row = document.createElement('li');
      const edit = document.createElement('textarea');
      edit.maxLength = 2048;
      const save = document.createElement('button');
      save.type = 'button'; save.textContent = 'Save';
      const remove = document.createElement('button');
      remove.type = 'button'; remove.textContent = 'Remove';
      const up = document.createElement('button');
      up.type = 'button'; up.textContent = 'Move up';
      const down = document.createElement('button');
      down.type = 'button'; down.textContent = 'Move down';
      row.append(edit, save, remove, up, down);
    }
    retained.add(note.id);
    row.dataset.noteId = note.id;
    row.dataset.testid = `notebook-note-${note.id}`;
    const edit = row.querySelector('textarea')!;
    edit.maxLength = 2048;
    edit.setAttribute('aria-label', `Notebook note ${index + 1}`);
    if (document.activeElement !== edit) edit.value = note.text;
    const buttons = row.querySelectorAll('button');
    const save = buttons[0];
    const remove = buttons[1];
    const up = buttons[2];
    const down = buttons[3];
    save.onclick = () => {
      const text = edit.value.trim();
      if (text) claim({ action: 'notebook-edit', revision: value.revision, note: note.id, text });
    };
    remove.onclick = () => claim({ action: 'notebook-remove', revision: value.revision, note: note.id });
    up.disabled = index === 0;
    up.onclick = () => claim({ action: 'notebook-move', revision: value.revision, note: note.id, destination: index - 1 });
    down.disabled = index + 1 >= value.notes.length;
    down.onclick = () => claim({ action: 'notebook-move', revision: value.revision, note: note.id, destination: index + 1 });
    // Avoid moving an already correctly positioned row: even moving a node to the same parent
    // can blur its textarea in browsers. Reordering only happens when the projected order changed.
    if (list.children[index] !== row) list.insertBefore(row, list.children[index] ?? null);
  });
  for (const [id, row] of existing) if (!retained.has(id)) row.remove();
}
