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

type JournalPage = 'active' | 'finished';

/**
 * Renders the ruleset's grouped quest journal as the donor's active-quest and finished-quest pages.
 * The selected page is presentation state only; every title, deadline and status is published text.
 */
export function mountQuestJournal(root: HTMLElement): {
  update(value: QuestJournalProjection | undefined): void; dispose(): void;
} {
  const shell = document.createElement('section');
  shell.className = 'dagger-quest-journal';
  shell.setAttribute('aria-label', 'Quest journal');
  const tabs = document.createElement('div');
  tabs.className = 'dagger-quest-journal-tabs';
  tabs.setAttribute('role', 'tablist');
  const body = document.createElement('div');
  body.className = 'dagger-quest-journal-page';
  shell.append(tabs, body);
  root.prepend(shell);

  let page: JournalPage = 'active';
  let latest: QuestJournalProjection = { active: [], finished: [] };
  const buttons = (['active', 'finished'] as const).map(id => {
    const button = document.createElement('button');
    button.type = 'button'; button.dataset.journalPage = id; button.setAttribute('role', 'tab');
    button.textContent = id === 'active' ? 'Active quests' : 'Finished quests';
    button.addEventListener('click', () => { page = id; render(); });
    tabs.append(button);
    return button;
  });

  const entries = (values: readonly QuestJournalEntryProjection[]): HTMLElement => {
    const list = document.createElement('ol');
    list.className = 'dagger-quest-journal-entries';
    for (const entry of values) {
      const item = document.createElement('li');
      if (entry.entryId) item.dataset.entryId = entry.entryId;
      item.textContent = entry.text;
      list.append(item);
    }
    return list;
  };

  const render = (): void => {
    for (const button of buttons) button.setAttribute('aria-selected', String(button.dataset.journalPage === page));
    const groups = page === 'active' ? latest.active : latest.finished;
    if (groups.length === 0) {
      const empty = document.createElement('p');
      empty.className = 'dagger-quest-journal-empty';
      empty.textContent = page === 'active' ? 'No active quests.' : 'No finished quests.';
      body.replaceChildren(empty);
      return;
    }
    body.replaceChildren(...groups.map(group => {
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
  };
  render();

  return {
    update(value: QuestJournalProjection | undefined): void {
      latest = value ?? { active: [], finished: [] };
      render();
    },
    dispose(): void { shell.remove(); },
  };
}
