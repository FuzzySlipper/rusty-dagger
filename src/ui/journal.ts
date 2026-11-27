import type { ConfirmQuestion } from './confirm.js';
import { mountNotebook, type NotebookAction, type NotebookProjection } from './notebook.js';
import { DIRECTION_EVENT, type Direction } from './navigation.js';
import { redraw } from './redraw.js';
import { reflow } from './reflow.js';

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

export interface SocialJournalHouseholdProjection {
  readonly name: string;
  readonly tier: string;
  readonly standing: string;
  readonly status: string;
  readonly isFeud: boolean;
  readonly settlementHint: string | null;
}

export interface SocialJournalVouchProjection {
  readonly targetName: string;
  readonly voucherName: string;
  readonly kind: string;
  readonly status: string;
}

export interface SocialJournalFactionProjection {
  readonly name: string;
  readonly standing: string;
  readonly detail: string;
}

export interface SocialJournalProjection {
  readonly households: readonly SocialJournalHouseholdProjection[];
  readonly vouches: readonly SocialJournalVouchProjection[];
  readonly factions: readonly SocialJournalFactionProjection[];
}

export type JournalTab = 'active' | 'finished' | 'relations' | 'notes' | 'book';
const TABS: readonly JournalTab[] = ['active', 'finished', 'relations', 'notes', 'book'];
const TAB_NAMES: Readonly<Record<JournalTab, string>> = {
  active: 'Quests',
  finished: 'Finished',
  relations: 'Relations',
  notes: 'Notes',
  book: 'Book',
};

export interface Journal {
  update(
    quests: QuestJournalProjection | undefined,
    notebook: NotebookProjection | undefined,
    social?: SocialJournalProjection | undefined,
  ): void;
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
  const relations = document.createElement('div');
  relations.className = 'dagger-quest-journal-page dagger-social-journal-page dagger-frame'; relations.dataset.skin = 'parchment';
  panels.get('relations')!.append(relations);
  // An entry taller than the page is read by scrolling it: up and down scroll until its edge shows,
  // and only then move to the entry before or after it.
  for (const page of [quests, finished, relations]) page.addEventListener(DIRECTION_EVENT, event => {
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
  let latestSocial: SocialJournalProjection = { households: [], vouches: [], factions: [] };

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
      item.textContent = reflow(entry.text);
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

  const drawRelations = (): void => {
    redraw(relations, JSON.stringify(latestSocial), () => {
      const sections: HTMLElement[] = [];

      if (latestSocial.households.length > 0) {
        const article = document.createElement('article');
        article.className = 'dagger-quest-journal-group dagger-social-journal-section';
        article.dataset.socialSection = 'households';
        const heading = document.createElement('h3');
        heading.textContent = 'Households & Feuds';
        const list = document.createElement('ol');
        list.className = 'dagger-quest-journal-entries dagger-social-journal-list';
        for (const entry of latestSocial.households) {
          const item = document.createElement('li');
          item.className = 'dagger-social-entry';
          item.tabIndex = 0;
          item.dataset.feud = String(entry.isFeud);
          const title = document.createElement('strong');
          title.className = 'dagger-social-entry-title';
          title.textContent = `${entry.name} (${entry.tier}) — ${entry.standing}`;
          const status = document.createElement('p');
          status.className = 'dagger-social-entry-status';
          status.textContent = entry.status;
          item.append(title, status);
          if (entry.settlementHint) {
            const hint = document.createElement('p');
            hint.className = 'dagger-social-entry-hint';
            hint.textContent = entry.settlementHint;
            item.append(hint);
          }
          list.append(item);
        }
        article.append(heading, list);
        sections.push(article);
      }

      if (latestSocial.vouches.length > 0) {
        const article = document.createElement('article');
        article.className = 'dagger-quest-journal-group dagger-social-journal-section';
        article.dataset.socialSection = 'vouches';
        const heading = document.createElement('h3');
        heading.textContent = 'Active Vouches';
        const list = document.createElement('ol');
        list.className = 'dagger-quest-journal-entries dagger-social-journal-list';
        for (const entry of latestSocial.vouches) {
          const item = document.createElement('li');
          item.className = 'dagger-social-entry';
          item.tabIndex = 0;
          const title = document.createElement('strong');
          title.className = 'dagger-social-entry-title';
          title.textContent = `${entry.targetName} — ${entry.kind}`;
          const detail = document.createElement('p');
          detail.className = 'dagger-social-entry-status';
          detail.textContent = `Vouched by ${entry.voucherName} · ${entry.status}`;
          item.append(title, detail);
          list.append(item);
        }
        article.append(heading, list);
        sections.push(article);
      }

      if (latestSocial.factions.length > 0) {
        const article = document.createElement('article');
        article.className = 'dagger-quest-journal-group dagger-social-journal-section';
        article.dataset.socialSection = 'factions';
        const heading = document.createElement('h3');
        heading.textContent = 'Factions & Standings';
        const list = document.createElement('ol');
        list.className = 'dagger-quest-journal-entries dagger-social-journal-list';
        for (const entry of latestSocial.factions) {
          const item = document.createElement('li');
          item.className = 'dagger-social-entry';
          item.tabIndex = 0;
          const title = document.createElement('strong');
          title.className = 'dagger-social-entry-title';
          title.textContent = `${entry.name} — ${entry.standing}`;
          const detail = document.createElement('p');
          detail.className = 'dagger-social-entry-status';
          detail.textContent = entry.detail;
          item.append(title, detail);
          list.append(item);
        }
        article.append(heading, list);
        sections.push(article);
      }

      if (sections.length === 0) {
        const line = document.createElement('p');
        line.className = 'dagger-quest-journal-empty';
        line.textContent = 'No notable household feuds, vouches, or standings yet.';
        relations.replaceChildren(line);
        return;
      }

      relations.replaceChildren(...sections);
    });
  };

  const render = (): void => {
    const count = (tab: JournalTab, n: number | null): void => {
      buttons.get(tab)!.textContent = n === null ? TAB_NAMES[tab] : `${TAB_NAMES[tab]} (${n})`;
    };
    count('active', latest.active.length);
    count('finished', latest.finished.length);
    count('relations', latestSocial.households.length + latestSocial.vouches.length + latestSocial.factions.length);
    count('notes', latestNotebook?.notes.length ?? 0);
    count('book', null);
    drawPage(quests, latest.active, 'No active quests.');
    drawPage(finished, latest.finished, 'No finished quests.');
    drawRelations();
  };
  select('active');
  render();

  /** The active paged list (quests or relations sections) that PageUp/PageDown and triggers step through. */
  const pagedList = (): HTMLElement | null =>
    shown === 'active' ? quests : shown === 'finished' ? finished : shown === 'relations' ? relations : null;

  return {
    update(value, notes, social): void {
      latest = value ?? { active: [], finished: [] };
      const opened = notes?.book && notes.book.id !== latestNotebook?.book?.id;
      latestNotebook = notes;
      latestSocial = social ?? { households: [], vouches: [], factions: [] };
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
      const page = pagedList();
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
      const page = pagedList();
      if (!page || page.querySelectorAll('.dagger-quest-journal-group').length <= 1) return null;
      return shown === 'relations' ? 'Sections' : 'Quests';
    },
    dispose(): void { notebook.dispose(); shell.remove(); },
  };
}
