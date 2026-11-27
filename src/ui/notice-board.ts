import type { Send } from './host.js';
import type { DaggerHud } from './projection.js';
import { redraw } from './redraw.js';

export interface NoticeBoardLeadProjection {
  readonly kind: string;
  readonly actionLabel: string;
  readonly pickedUp: boolean;
  readonly targetSummary: string | null;
  readonly rewardGold: number | null;
}

export interface NoticeBoardEntryProjection {
  readonly id: string;
  readonly kind: string;
  readonly kindLabel: string;
  readonly heading: string;
  readonly body: string;
  readonly lead: NoticeBoardLeadProjection | null;
}

export interface NoticeBoardProjection {
  readonly revision: string;
  readonly title: string;
  readonly townName: string;
  readonly regionName: string;
  readonly authorityName: string | null;
  readonly status: string | null;
  readonly entries: readonly NoticeBoardEntryProjection[];
}

/** Physical in-world Town/Tavern Notice Board modal, rendering entries and actionable posted leads. */
export interface NoticeBoard {
  readonly root: HTMLElement;
  close(): void;
  initialFocus(): HTMLElement | null;
  update(value: DaggerHud): void;
}

export function mountNoticeBoard(shell: HTMLElement, send: Send): NoticeBoard {
  const root = shell.querySelector<HTMLElement>('.dagger-notice-board')!;
  const title = root.querySelector<HTMLElement>('.dagger-notice-board-title')!;
  const meta = root.querySelector<HTMLElement>('.dagger-notice-board-meta')!;
  const status = root.querySelector<HTMLElement>('.dagger-notice-board-status')!;
  const list = root.querySelector<HTMLElement>('.dagger-notice-board-list')!;
  const empty = root.querySelector<HTMLElement>('.dagger-notice-board-empty')!;
  const closeButton = root.querySelector<HTMLButtonElement>('.dagger-notice-board-close')!;

  let current: NoticeBoardProjection | null = null;
  const close = (): void => {
    if (current) send({ action: 'notice-board-close', revision: current.revision });
  };
  closeButton.addEventListener('click', close);

  return {
    root,
    close,
    initialFocus: () => root.querySelector<HTMLButtonElement>('.dagger-notice-board-take:not(:disabled)') ?? closeButton,
    update(value) {
      const board = value.noticeBoard ?? null;
      current = board;
      root.hidden = board === null || value.mode === 'dead';
      if (board === null) return;

      title.textContent = board.title;
      const authority = board.authorityName ? ` · ${board.authorityName}` : '';
      meta.textContent = `${board.townName}, ${board.regionName}${authority}`;
      status.textContent = board.status ?? '';
      status.hidden = !board.status;
      empty.hidden = board.entries.length > 0;

      const signature = JSON.stringify(board.entries);
      redraw(list, signature, () => {
        list.replaceChildren();
        for (const entry of board.entries) {
          const item = document.createElement('li');
          item.className = 'dagger-notice-board-entry';
          item.dataset.noticeKind = entry.kind;

          const header = document.createElement('div');
          header.className = 'dagger-notice-board-entry-head';
          const badge = document.createElement('span');
          badge.className = 'dagger-notice-board-kind';
          badge.textContent = entry.kindLabel;
          const heading = document.createElement('strong');
          heading.className = 'dagger-notice-board-heading';
          heading.textContent = entry.heading;
          header.append(badge, heading);

          const body = document.createElement('p');
          body.className = 'dagger-notice-board-body';
          body.textContent = entry.body;

          item.append(header, body);

          if (entry.lead !== null) {
            const lead = entry.lead;
            const footer = document.createElement('div');
            footer.className = 'dagger-notice-board-lead';
            footer.dataset.leadKind = lead.kind;

            if (lead.targetSummary) {
              const target = document.createElement('span');
              target.className = 'dagger-notice-board-lead-target';
              target.textContent = lead.targetSummary;
              footer.append(target);
            }

            if (lead.rewardGold !== null && lead.rewardGold > 0) {
              const reward = document.createElement('span');
              reward.className = 'dagger-notice-board-lead-reward';
              reward.textContent = `${lead.rewardGold} gp`;
              footer.append(reward);
            }

            const takeButton = document.createElement('button');
            takeButton.type = 'button';
            takeButton.className = 'dagger-notice-board-take';
            takeButton.dataset.redrawKey = `notice-take:${entry.id}`;
            takeButton.dataset.entryId = entry.id;
            takeButton.textContent = lead.actionLabel;
            takeButton.disabled = lead.pickedUp;
            takeButton.addEventListener('click', () => {
              if (!current || lead.pickedUp) return;
              send({ action: 'notice-board-take', revision: current.revision, key: entry.id });
            });
            footer.append(takeButton);
            item.append(footer);
          }

          list.append(item);
        }
      });
    },
  };
}
