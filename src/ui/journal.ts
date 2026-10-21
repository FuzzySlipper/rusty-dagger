import type { ConfirmQuestion } from './confirm.js';
import { mountNotebook, type NotebookAction, type NotebookProjection } from './notebook.js';
import { DIRECTION_EVENT, type Direction } from './navigation.js';
import { redraw } from './redraw.js';

export interface QuestJournalEntryProjection {
  readonly entryId: string | null;
  readonly instance: string;
  readonly message: number;
  readonly text: string;
  readonly diagnostics: readonly string[];
}

export interface ActiveQuestJournalProjection {
  readonly instance: string;
  readonly title: string;
  readonly deadline: string | null;
  readonly entries: readonly QuestJournalEntryProjection[];
}

export interface FinishedQuestJournalProjection {
  readonly instance: string;
  readonly title: string;
  readonly succeeded: boolean;
  readonly status: string;
  readonly entries: readonly QuestJournalEntryProjection[];
}

export interface QuestJournalProjection {
  readonly active: readonly ActiveQuestJournalProjection[];
  readonly finished: readonly FinishedQuestJournalProjection[];
}

export type JournalTab = 'active' | 'finished' | 'notes' | 'book';
const TABS: readonly JournalTab[] = ['active', 'finished', 'notes', 'book'];
const TAB_NAMES: Readonly<Record<JournalTab, string>> = { active: 'Quests', finished: 'Finished', notes: 'Notes', book: 'Book' };

export interface Journal {
  update(quests: QuestJournalProjection | undefined, notebook: NotebookProjection | undefined): void;
  /** Show a page of the journal, as a request for the notebook or a book does. */
  select(tab: JournalTab): void;
  /** The triggers or Page Up and Down: the next or previous quest, or a book's next or previous page. */
  page(direction: -1 | 1): void;
  /** What turning a page does on the shown tab, or null when nothing. */
  pageHint(): string | null;
  dispose(): void;
}

/**
 * The journal: the donor's active and finished quest pages, the player's notes and the book being
 * read, as tabs. Tabs follow focus, so moving along them with arrows or a pad shows each page; every
 * quest entry takes focus, so a long journal scrolls by keys and controller as well as by the mouse.
 * The tab shown is presentation state only; every title, date, deadline and status is published text.
 */
export function mountJournal(root: HTMLElement, claim: (action: NotebookAction) => void,
  ask: (question: ConfirmQuestion) => void): Journal {
  const shell = document.createElement('section');
  shell.className = 'dagger-journal';
  shell.setAttribute('aria-label', 'Journal');
  const tabs = document.createElement('div');
  tabs.className = 'dagger-journal-tabs';
  tabs.setAttribute('role', 'tablist');
  tabs.setAttribute('aria-label', 'Journal pages');
  const panels = new Map<JournalTab, HTMLElement>();
  const buttons = new Map<JournalTab, HTMLButtonElement>();
  for (const tab of TABS) {
    const button = document.createElement('button');
    button.type = 'button'; button.id = `dagger-journal-tab-${tab}`; button.dataset.journalPage = tab;
    button.setAttribute('role', 'tab'); button.setAttribute('aria-controls', `dagger-journal-panel-${tab}`);
    button.addEventListener('click', () => select(tab));
    // Tabs follow focus, so arrows and a pad show each page as they reach its tab.
    button.addEventListener('focus', () => { if (shown !== tab) select(tab); });
    tabs.append(button);
    buttons.set(tab, button);
    const panel = document.createElement('div');
    panel.id = `dagger-journal-panel-${tab}`;
    panel.className = `dagger-journal-panel dagger-journal-panel-${tab}`;
    panel.setAttribute('role', 'tabpanel');
    panel.setAttribute('aria-labelledby', button.id);
    panels.set(tab, panel);
  }
  const quests = document.createElement('div');
  quests.className = 'dagger-quest-journal-page dagger-frame'; quests.dataset.skin = 'parchment';
  panels.get('active')!.append(quests);
  const finished = document.createElement('div');
  finished.className = 'dagger-quest-journal-page dagger-frame'; finished.dataset.skin = 'parchment';
  panels.get('finished')!.append(finished);
  // An entry taller than the page is read by scrolling it: up and down scroll until its edge shows,
  // and only then move to the entry before or after it.
  for (const page of [quests, finished]) page.addEventListener(DIRECTION_EVENT, event => {
    const direction = (event as CustomEvent<Direction>).detail;
    const entry = event.target instanceof HTMLElement ? event.target.closest('li') : null;
    if (!entry || (direction !== 'up' && direction !== 'down')) return;
    const box = page.getBoundingClientRect(), row = entry.getBoundingClientRect();
    const hidden = direction === 'down' ? row.bottom - box.bottom : box.top - row.top;
    if (hidden <= 1) return;
    event.preventDefault();
    page.scrollBy?.({ top: (direction === 'down' ? 1 : -1) * Math.min(hidden, box.height * .8) });
  });
  shell.append(tabs, ...panels.values());
  root.append(shell);
  const notebook = mountNotebook(panels.get('book')!, panels.get('notes')!, claim, ask);

  let shown: JournalTab = 'active';
  let latest: QuestJournalProjection = { active: [], finished: [] };
  let latestNotebook: NotebookProjection | undefined;

  const select = (tab: JournalTab): void => {
    shown = tab;
    for (const [id, button] of buttons) {
      button.setAttribute('aria-selected', String(id === tab));
      // One tab stop for the list; the arrows reach the others.
      button.tabIndex = id === tab ? 0 : -1;
    }
    for (const [id, panel] of panels) panel.hidden = id !== tab;
  };

  const entries = (values: readonly QuestJournalEntryProjection[]): HTMLElement => {
    const list = document.createElement('ol');
    list.className = 'dagger-quest-journal-entries';
    for (const entry of values) {
      const item = document.createElement('li');
      if (entry.entryId) item.dataset.entryId = entry.entryId;
      item.tabIndex = 0;
      item.textContent = entry.text;
      list.append(item);
    }
    return list;
  };
  const drawPage = (page: HTMLElement, groups: readonly (ActiveQuestJournalProjection | FinishedQuestJournalProjection)[], empty: string): void => {
    redraw(page, JSON.stringify(groups), () => {
      if (groups.length === 0) {
        const line = document.createElement('p');
        line.className = 'dagger-quest-journal-empty';
        line.textContent = empty;
        page.replaceChildren(line);
        return;
      }
      page.replaceChildren(...groups.map(group => {
        const article = document.createElement('article');
        article.className = 'dagger-quest-journal-group';
        article.dataset.questInstance = group.instance;
        const title = document.createElement('h3');
        title.textContent = group.title;
        article.append(title);
        const detail = 'status' in group ? group.status : group.deadline;
        if (detail) {
          const line = document.createElement('p');
          line.className = 'status' in group ? 'dagger-quest-journal-status' : 'dagger-quest-journal-deadline';
          if ('succeeded' in group) line.dataset.succeeded = String(group.succeeded);
          line.textContent = detail;
          article.append(line);
        }
        article.append(entries(group.entries));
        return article;
      }));
    });
  };

  const render = (): void => {
    const count = (tab: JournalTab, n: number | null): void => {
      buttons.get(tab)!.textContent = n === null ? TAB_NAMES[tab] : `${TAB_NAMES[tab]} (${n})`;
    };
    count('active', latest.active.length);
    count('finished', latest.finished.length);
    count('notes', latestNotebook?.notes.length ?? 0);
    count('book', null);
    drawPage(quests, latest.active, 'No active quests.');
    drawPage(finished, latest.finished, 'No finished quests.');
  };
  select('active');
  render();

  /** The page's quests, and the one holding focus or the first shown. */
  const questPage = (): HTMLElement | null => shown === 'active' ? quests : shown === 'finished' ? finished : null;

  return {
    update(value, notes): void {
      latest = value ?? { active: [], finished: [] };
      const opened = notes?.book && notes.book.id !== latestNotebook?.book?.id;
      latestNotebook = notes;
      if (notes) notebook.update(notes);
      render();
      // A book that has just been opened is the page the player asked for.
      if (opened && shown !== 'book') select('book');
    },
    select(tab): void {
      select(tab);
      const panel = panels.get(tab)!;
      const first = panel.querySelector<HTMLElement>('li[tabindex], button:not(:disabled), textarea');
      (first ?? buttons.get(tab)!).focus();
    },
    page(direction): void {
      if (shown === 'book') { notebook.turn(direction); return; }
      const page = questPage();
      if (!page) return;
      const groups = Array.from(page.querySelectorAll<HTMLElement>('.dagger-quest-journal-group'));
      const at = groups.findIndex(group => group.contains(document.activeElement));
      const next = groups[at < 0 ? (direction > 0 ? 0 : groups.length - 1) : at + direction];
      const entry = next?.querySelector<HTMLElement>('li[tabindex]');
      entry?.focus();
      entry?.scrollIntoView?.({ block: 'nearest' });
    },
    pageHint(): string | null {
      if (shown === 'book') return latestNotebook?.book && latestNotebook.book.pageCount > 1 ? 'Pages' : null;
      const page = questPage();
      return page && page.querySelectorAll('.dagger-quest-journal-group').length > 1 ? 'Quests' : null;
    },
    dispose(): void { notebook.dispose(); shell.remove(); },
  };
}
