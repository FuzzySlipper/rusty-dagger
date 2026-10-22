import type { ConfirmQuestion } from './confirm.js';
import { renderItemMaker, renderPotionMaker, renderSpellMaker, renderSummoning } from './crafting.js';
import { button, type Send } from './host.js';
import { FOCUSABLE } from './navigation.js';
import type { DaggerHud, DialogueProjection, MerchantItemProjection, MerchantProjection, SpellbookProjection } from './projection.js';
import { redraw } from './redraw.js';
import { renderSpellSales, spellAction } from './spells.js';
import { createStepper } from './stepper.js';
import { formatNumber, mountTransfer, type TransferRow, type TransferSideName } from './transfer.js';

/**
 * A person screen: the services a person offers, each on its own page. The ruleset publishes the
 * services in order with their names and the page contact opens on: talk for a person who offers
 * nothing else, otherwise the chooser that lists them. Talk holds the topics and tone, the bank and
 * temple donation; trade, training, food and drink, spells for sale, item, potion and spell making
 * and Daedric summoning each have a page. Back steps from a page to the chooser and from the
 * chooser (or a talk-only person's talk) ends the conversation; the shoulder buttons step between
 * the pages. Which page is shown is all this keeps: what each page offers is the ruleset's.
 * A summoning offer opens as its own dialog above the conversation and needs an answer.
 *
 * The conversation keeps a short log of what was asked and answered while it lasts; the topics
 * are grouped under the headings the ruleset publishes. The shop is the two-pane transfer layout,
 * and every purchase over the ruleset's large-trade amount, every theft, every training and every
 * paid summoning asks through the drawn confirm first.
 */
export interface Dialogue {
  readonly window: HTMLDialogElement;
  readonly summoningOffer: HTMLDialogElement;
  /** End the conversation, as its close button does. */
  close(): void;
  /** Escape or Back: from a service page to the chooser, otherwise end the conversation. */
  back(): void;
  /** The shoulder buttons: the previous or next service page. */
  panel(direction: -1 | 1): void;
  /** The control the shown page opens on. */
  initialFocus(): HTMLElement | null;
  update(value: DaggerHud, dead: boolean, spellbook: SpellbookProjection | null): void;
}

/** How many exchanges the conversation log keeps. */
const LOG_LENGTH = 8;
/** The chooser page's name, as the ruleset publishes it for a person who offers more than talk. */
const CHOOSER = 'services';

export function mountDialogue(shell: HTMLElement, send: Send, ask: (question: ConfirmQuestion) => void): Dialogue {
  const window_ = shell.querySelector<HTMLDialogElement>('.dagger-dialogue')!;
  const target = shell.querySelector<HTMLElement>('.dagger-dialogue-target')!;
  const greeting = shell.querySelector<HTMLElement>('.dagger-dialogue-greeting')!;
  const tone = shell.querySelector<HTMLSelectElement>('.dagger-dialogue-tone')!;
  const donation = shell.querySelector<HTMLElement>('.dagger-dialogue-donation')!;
  const donationAmount = createStepper('Donation in gold');
  donationAmount.limit(1, 2_147_483_647, 100);
  donation.append(donationAmount.element);
  const log = shell.querySelector<HTMLOListElement>('.dagger-dialogue-log')!;
  // Which directory lists the player opened, kept while the conversation lasts.
  const openCategories = new Set<string>();
  let categoriesFor: string | null = null;
  const topics = shell.querySelector<HTMLElement>('.dagger-dialogue-topics')!;
  const trainingSummary = shell.querySelector<HTMLElement>('.dagger-dialogue-training-summary')!;
  const trainingSkills = shell.querySelector<HTMLElement>('.dagger-dialogue-training-skills')!;
  const tavernRoot = shell.querySelector<HTMLElement>('.dagger-dialogue-tavern')!;
  const merchantRoot = shell.querySelector<HTMLElement>('.dagger-dialogue-merchant')!;
  const spellSales = shell.querySelector<HTMLElement>('.dagger-dialogue-spells')!;
  const summoningRoot = shell.querySelector<HTMLElement>('.dagger-dialogue-summoning')!;
  const summoningOffer = shell.querySelector<HTMLDialogElement>('.dagger-daedric-offer')!;
  const itemMaker = shell.querySelector<HTMLElement>('.dagger-dialogue-itemmaker')!;
  const potionMaker = shell.querySelector<HTMLElement>('.dagger-dialogue-potionmaker')!;
  const spellMaker = shell.querySelector<HTMLElement>('.dagger-dialogue-spellmaker')!;
  const tabs = shell.querySelector<HTMLElement>('.dagger-dialogue-tabs')!;
  const serviceList = shell.querySelector<HTMLElement>('.dagger-dialogue-service-list')!;
  const pages = Array.from(shell.querySelectorAll<HTMLElement>('.dagger-dialogue-page'));
  const backButton = shell.querySelector<HTMLButtonElement>('.dagger-dialogue-back')!;
  const closeButton = shell.querySelector<HTMLButtonElement>('.dagger-dialogue-close')!;

  let dead = false;
  let current: DialogueProjection | null = null;
  // The log is what this conversation has shown, kept only while it lasts; the ruleset publishes the latest exchange.
  let exchanges: { question: string; reply: string }[] = [];
  let conversation: string | null = null;
  // The page shown and the services it steps between: presentation only, reset for each conversation.
  let page: string | null = null;
  let services: DialogueProjection['services'] = [];

  // The control a page opens on: its content, never the tone choice that would take the arrows or the
  // buttons that leave it. Talk opens on its first topic.
  const pageFocus = (): HTMLElement | null => {
    const shown = pages.find(element => element.dataset.page === page);
    if (!shown) return null;
    const usable = (element: HTMLElement): boolean => !element.closest('[hidden]') && !element.closest('details:not([open]) > :not(summary)');
    const preferred = page === 'talk' ? Array.from(topics.querySelectorAll<HTMLElement>('button, summary')).find(usable) : undefined;
    return preferred ?? Array.from(shown.querySelectorAll<HTMLElement>(FOCUSABLE)).find(usable) ?? null;
  };
  const showPage = (next: string, focus: boolean, from?: string): void => {
    page = next;
    window_.dataset.page = next;
    for (const element of pages) element.hidden = element.dataset.page !== next;
    const chooser = services.length > 1;
    tabs.hidden = !chooser || next === CHOOSER;
    backButton.hidden = !chooser || next === CHOOSER;
    for (const tab of tabs.querySelectorAll<HTMLElement>('[role="tab"]')) tab.setAttribute('aria-selected', String(tab.dataset.service === next));
    if (!focus) return;
    // Back to the chooser lands on the service just left; a service page lands on its content.
    const target = next === CHOOSER && from ? Array.from(serviceList.querySelectorAll<HTMLElement>('[data-service]')).find(control => control.dataset.service === from) : pageFocus();
    (target ?? (backButton.hidden ? closeButton : backButton)).focus();
  };
  const choose = (event: Event): void => {
    const control = (event.target as HTMLElement).closest<HTMLElement>('[data-service]');
    if (!dead && control?.dataset.service && services.some(service => service.id === control.dataset.service)) showPage(control.dataset.service, true);
  };
  serviceList.addEventListener('click', choose);
  tabs.addEventListener('click', choose);
  backButton.addEventListener('click', () => { if (page !== null && page !== CHOOSER) showPage(CHOOSER, true, page); });

  tavernRoot.addEventListener('click', event => {
    const control = (event.target as HTMLElement).closest<HTMLButtonElement>('button[data-tavern-item]');
    const option = current?.tavern?.menu.find(item => item.id === control?.dataset.tavernItem);
    if (!dead && option && current?.tavern?.hungry) send({ action: 'tavern-buy', revision: current.revision, key: option.id, amount: option.price });
  });
  tone.addEventListener('change', () => {
    if (!dead && current) send({ action: 'dialogue-tone', revision: current.revision, tone: tone.value });
  });
  topics.addEventListener('click', event => {
    const control = (event.target as HTMLElement).closest<HTMLButtonElement>('button[data-topic]');
    if (dead || !control?.dataset.topic || !current) return;
    const data: Record<string, unknown> = { action: 'dialogue-topic', revision: current.revision, topic: control.dataset.topic };
    if (control.dataset.topic === 'donate') data.amount = donationAmount.value();
    if (control.dataset.key) data.key = control.dataset.key;
    send(data);
  });
  trainingSkills.addEventListener('click', event => {
    const control = (event.target as HTMLElement).closest<HTMLButtonElement>('button[data-training-skill]');
    const offer = current?.training;
    const skill = offer?.skills.find(value => value.id === control?.dataset.trainingSkill);
    if (dead || !skill || !offer || !current || !skill.trainable) return;
    const asked = { revision: current.revision, key: skill.id, price: offer.price };
    const hours = offer.durationHours;
    ask({
      message: `Train ${skill.label} for ${offer.price} gold? It takes ${hours} hour${hours === 1 ? '' : 's'}.`,
      accept: 'Train',
      onAccept: () => {
        // The offer may have changed while the question was open; only the price asked about is paid.
        if (!dead && current?.revision === asked.revision && current.training?.price === asked.price)
          send({ action: 'training-commit', revision: asked.revision, key: asked.key, amount: asked.price, confirm: true });
      },
    });
  });
  const close = (): void => { if (!dead && current) send({ action: 'dialogue-close', revision: current.revision }); };
  closeButton.addEventListener('click', close);
  const back = (): void => {
    if (page !== null && page !== CHOOSER && services.length > 1) showPage(CHOOSER, true, page);
    else close();
  };
  window_.addEventListener('cancel', event => { event.preventDefault(); back(); });
  summoningOffer.addEventListener('cancel', event => event.preventDefault());
  spellSales.addEventListener('click', event => {
    const control = (event.target as HTMLElement).closest<HTMLButtonElement>('button');
    if (control && !control.disabled) spellAction(control, send, ask, () => {});
  });

  const shop = mountShop(merchantRoot, send, ask);

  const recordExchange = (dialogue: DialogueProjection): void => {
    const key = `${dialogue.targetLabel}`;
    if (conversation !== key) { conversation = key; exchanges = []; }
    const reply = dialogue.reply ?? '';
    const question = dialogue.question ?? '';
    const last = exchanges.at(-1);
    if (reply && (last?.reply !== reply || last.question !== question)) exchanges = [...exchanges, { question, reply }].slice(-LOG_LENGTH);
    redraw(log, JSON.stringify(exchanges), () => {
      log.replaceChildren(...exchanges.map((exchange, index) => {
        const entry = document.createElement('li');
        entry.classList.toggle('is-latest', index === exchanges.length - 1);
        if (exchange.question) {
          const asked = document.createElement('p');
          asked.className = 'dagger-dialogue-asked';
          asked.textContent = exchange.question;
          entry.append(asked);
        }
        const answer = document.createElement('p');
        answer.className = 'dagger-dialogue-reply';
        answer.textContent = exchange.reply;
        entry.append(answer);
        return entry;
      }));
      log.lastElementChild?.scrollIntoView?.({ block: 'nearest' });
    });
    log.hidden = exchanges.length === 0;
  };

  return {
    window: window_,
    summoningOffer,
    close,
    back,
    panel(direction) {
      if (dead || services.length < 2) return;
      const index = services.findIndex(service => service.id === page);
      const next = index < 0 ? (direction > 0 ? 0 : services.length - 1) : (index + direction + services.length) % services.length;
      showPage(services[next]!.id, true);
    },
    initialFocus: pageFocus,
    update(value, isDead, spellbook) {
      dead = isDead;
      renderSpellSales(spellSales, spellbook);
      const summoning = spellbook?.summoning ?? null;
      const offer = Boolean(summoning?.offerRevision);
      redraw(offer ? summoningOffer : summoningRoot, JSON.stringify(summoning), () => renderSummoning(offer ? summoningOffer : summoningRoot, summoning, send, ask));
      if (offer) { redraw(summoningRoot, '', () => summoningRoot.replaceChildren()); if (!summoningOffer.open) summoningOffer.showModal(); }
      else if (summoningOffer.open) summoningOffer.close();
      renderItemMaker(itemMaker, spellbook?.itemMaker ?? null, send, ask);
      renderPotionMaker(potionMaker, spellbook?.potionMaker ?? null, send);
      renderSpellMaker(spellMaker, spellbook?.maker ?? null, send, ask);
      // Dead mode owns the whole interaction surface. An activation projection can remain in the
      // retained snapshot for one delivery, but it must not reopen a native dialogue over the death
      // choices or leave a close action aimed at a session that has already stopped ordinary input.
      const dialogue = dead ? null : value.activation?.dialogue ?? null;
      current = dialogue;
      const tavern = dialogue?.tavern;
      redraw(tavernRoot, JSON.stringify(tavern ?? null), () => {
        tavernRoot.replaceChildren();
        if (!tavern) return;
        const title = document.createElement('h3'); title.textContent = 'Food and drink'; tavernRoot.append(title);
        if (!tavern.hungry) { const note = document.createElement('p'); note.textContent = 'You are not hungry.'; tavernRoot.append(note); }
        for (const item of tavern.menu) {
          const control = button(`${item.label} — ${item.price} gold`); control.dataset.tavernItem = item.id;
          control.disabled = !tavern.hungry; tavernRoot.append(control);
        }
      });
      shop.update(dialogue?.merchant ?? null);
      if (dialogue === null) {
        conversation = null;
        exchanges = [];
        page = null;
        services = [];
        if (window_.open) window_.close();
        return;
      }
      const fresh = !window_.open || conversation !== dialogue.targetLabel;
      services = dialogue.services;
      redraw(serviceList, JSON.stringify(services), () => {
        serviceList.replaceChildren(...services.map(service => { const control = button(service.label); control.dataset.service = service.id; return control; }));
        tabs.replaceChildren(...services.map(service => {
          const control = button(service.label);
          control.setAttribute('role', 'tab');
          control.dataset.service = service.id;
          return control;
        }));
      });
      target.textContent = dialogue.targetLabel + (dialogue.questContacts?.length ? ' — quest contact' : '');
      greeting.textContent = dialogue.greeting + (dialogue.comprehendLanguagesBonus ? ` Language comprehension: +${dialogue.comprehendLanguagesBonus}.` : '');
      const tones = dialogue.tones ?? [];
      redraw(tone, JSON.stringify(tones), () => tone.replaceChildren(...tones.map(row => new Option(row.label, row.id))));
      tone.value = dialogue.tone;
      donation.hidden = !dialogue.topics.some(topic => topic.id === 'donate');
      recordExchange(dialogue);
      if (dialogue.targetLabel !== categoriesFor) { openCategories.clear(); categoriesFor = dialogue.targetLabel; }
      redraw(topics, JSON.stringify([dialogue.revision, dialogue.topics, dialogue.topicGroups, dialogue.bankAvailable]), () => {
        const groups = dialogue.topicGroups ?? [{ id: 'services', label: 'Topics' }];
        topics.replaceChildren(...groups.flatMap(group => {
          const members = dialogue.topics.filter(topic => (topic.group ?? 'services') === group.id
            || (group === groups.at(-1) && !groups.some(known => known.id === topic.group)));
          const bank = group.id === 'services' && dialogue.bankAvailable;
          if (members.length === 0 && !bank) return [];
          const section = document.createElement('section');
          section.className = 'dagger-dialogue-topic-group';
          section.dataset.topicGroup = group.id;
          const heading = document.createElement('h3');
          heading.textContent = group.label;
          const list = document.createElement('div');
          list.className = 'dagger-dialogue-topic-list';
          const topicButton = (topic: typeof members[number]): HTMLButtonElement => {
            const control = button(topic.label);
            control.dataset.topic = topic.id;
            if (topic.key) control.dataset.key = topic.key;
            return control;
          };
          list.append(...members.filter(topic => !topic.category).map(topicButton));
          // A heading with lists (the directory's kinds of building, its people, the region) shows each
          // as a closed, counted section, so a town's directory does not bury the rest of the conversation.
          const categories = [...new Set(members.flatMap(topic => topic.category ? [topic.category] : []))];
          const lists = categories.map(category => {
            const entries = members.filter(topic => topic.category === category);
            const details = document.createElement('details');
            details.className = 'dagger-dialogue-topic-category';
            details.dataset.topicCategory = category;
            details.open = openCategories.has(`${group.id}/${category}`);
            details.addEventListener('toggle', () => {
              if (details.open) openCategories.add(`${group.id}/${category}`); else openCategories.delete(`${group.id}/${category}`);
            });
            const summary = document.createElement('summary');
            summary.textContent = `${category} · ${entries.length}`;
            const inner = document.createElement('div');
            inner.className = 'dagger-dialogue-topic-list';
            inner.append(...entries.map(topicButton));
            details.append(summary, inner);
            return details;
          });
          if (bank) {
            const control = button('Bank services');
            control.dataset.bankOpen = 'true';
            control.addEventListener('click', () => send({ action: 'bank-open', revision: dialogue.revision }));
            list.append(control);
          }
          section.append(heading, ...(list.childElementCount > 0 ? [list] : []), ...lists);
          return [section];
        }));
      });
      const offerTraining = dialogue.training ?? null;
      if (offerTraining) {
        const hours = offerTraining.durationHours;
        trainingSummary.textContent = `${offerTraining.price} gold for ${hours} hour${hours === 1 ? '' : 's'}${offerTraining.member ? ' (member rate)' : ''}.`;
      }
      redraw(trainingSkills, JSON.stringify(offerTraining?.skills ?? null), () => trainingSkills.replaceChildren(...(offerTraining?.skills ?? []).map(skill => {
        const row = document.createElement('div');
        row.className = 'dagger-training-row';
        const label = document.createElement('span');
        label.textContent = skill.label;
        const level = document.createElement('span');
        level.className = 'dagger-training-level';
        level.textContent = `${skill.permanentValue} / ${skill.maximumValue}`;
        const control = button(skill.trainable ? 'Train' : 'Mastered here');
        control.dataset.trainingSkill = skill.id;
        control.disabled = !skill.trainable;
        control.setAttribute('aria-label', `Train ${skill.label}`);
        row.append(label, level, control);
        return row;
      })));
      // A conversation opens where the ruleset says; a page whose service is no longer offered goes back
      // to where the conversation opened.
      const opening = !window_.open;
      const start = dialogue.opensOn === 'talk' ? 'talk' : CHOOSER;
      const keep = !fresh && page !== null && (page === CHOOSER ? services.length > 1 : services.some(service => service.id === page));
      showPage(keep ? page! : start, false);
      if (opening) window_.showModal();
      // The browser focused the modal's first control; a conversation's first page opens on its content instead.
      if (fresh) showPage(start, true);
    },
  };
}

/**
 * The shop on the two-pane transfer layout: the stock with Buy (and Steal, set apart), the player's
 * items with Sell, Repair and Identify, and repairs waiting to be collected beneath.
 */
function mountShop(root: HTMLElement, send: Send, ask: (question: ConfirmQuestion) => void): { update(value: MerchantProjection | null): void } {
  const heading = document.createElement('h3');
  const purse = document.createElement('p');
  purse.className = 'dagger-shop-purse';
  const result = document.createElement('p');
  result.className = 'dagger-shop-result';
  result.setAttribute('role', 'status');
  const repairs = document.createElement('section');
  repairs.className = 'dagger-shop-repairs';
  let current: MerchantProjection | null = null;
  // Buying, selling and stealing move an amount; repair and identify act on the whole item.
  const claim = (action: string, item: MerchantItemProjection, amount: number, confirm = false): void => {
    if (current === null) return;
    send(action === 'merchant-repair' || action === 'merchant-identify'
      ? { action, revision: current.revision, item: item.key }
      : { action, revision: current.revision, item: item.key, amount, ...(confirm ? { confirm: true } : {}) });
  };
  const sideOf = (side: TransferSideName): 'buy' | 'sell' => side === 'source' ? 'buy' : 'sell';
  // The ruleset's price for an amount: one item is the row's own quote; more waits for the ruleset to
  // quote the whole line, which the trade formula rounds once rather than per item.
  const priceOf = (side: TransferSideName, item: MerchantItemProjection, amount: number): number | null => {
    if (amount === 1) return Number(item.unitPrice);
    const quote = current?.quote;
    return quote && quote.item === item.key && quote.side === sideOf(side) && quote.amount === amount ? quote.total : null;
  };
  const quoteFor = (side: TransferSideName, key: string, amount: number): void => {
    if (current !== null && amount > 1) send({ action: 'merchant-quote', revision: current.revision, item: key, key: sideOf(side), amount });
  };
  // A trade chosen before its quote arrived waits for it, so the confirmation always names the real total.
  let waiting: { side: TransferSideName; key: string; command: string; amount: number } | null = null;
  const trade = (side: TransferSideName, key: string, command: string, amount: number): void => {
    const merchant = current;
    const item = merchant === null ? undefined : (side === 'source' ? merchant.stock : merchant.playerItems).find(row => row.key === key);
    if (merchant === null || item === undefined) return;
    const what = amount > 1 ? `${amount} × ${item.label}` : item.label;
    if (command === 'merchant-shoplift') {
      ask({ message: `Steal ${what}? If you are caught, it is a crime.`, accept: 'Steal', onAccept: () => claim(command, item, amount) });
      return;
    }
    if (command !== 'merchant-buy' && command !== 'merchant-sell') { claim(command, item, amount); return; }
    const total = priceOf(side, item, amount);
    if (total === null) { waiting = { side, key, command, amount }; quoteFor(side, key, amount); return; }
    // One item is priced by its row; a quoted line says itself whether it asks first.
    if (amount === 1 ? total >= merchant.largeTradeGold : merchant.quote?.confirm === true) {
      ask({ message: `${command === 'merchant-buy' ? 'Buy' : 'Sell'} ${what} for ${formatNumber(total)} gold?`,
        accept: command === 'merchant-buy' ? 'Buy' : 'Sell', onAccept: () => claim(command, item, amount, true) });
    } else claim(command, item, amount);
  };
  const transfer = mountTransfer(root, {
    command(side, key, command, amount) { waiting = null; trade(side, key, command, amount); },
    amount(side, key, amount) { quoteFor(side, key, amount); },
  });
  root.prepend(heading, purse);
  root.append(result, repairs);
  repairs.addEventListener('click', event => {
    const control = (event.target as HTMLElement).closest<HTMLButtonElement>('button[data-repair]');
    if (control && current) send({ action: 'merchant-collect-repair', revision: current.revision, key: control.dataset.repair! });
  });

  const row = (item: MerchantItemProjection, commands: TransferRow['commands'], seller: boolean): TransferRow => {
    const unit = Number(item.unitPrice);
    const facts = [`${formatNumber(unit)} gold each`,
      seller && item.maximumCondition > 0 && item.currentCondition < item.maximumCondition
        ? `condition ${item.currentCondition}/${item.maximumCondition}` : null,
      item.identified ? null : 'unidentified', item.stolen ? 'stolen' : null].filter((part): part is string => part !== null).join(' · ');
    return { key: item.key, label: item.label, icon: item.icon ?? null, quantity: item.maximumQuantity > 1 ? String(item.quantity) : null,
      maximumQuantity: item.maximumQuantity, facts, commands,
      price: commands.some(command => command.takesAmount && command.id !== 'merchant-shoplift') ? amount => priceOf(seller ? 'pack' : 'source', item, amount) : undefined };
  };

  return {
    update(value) {
      current = value;
      root.hidden = value === null;
      if (value === null) { transfer.update(null); return; }
      heading.textContent = `${value.provider} · shop quality ${value.quality}`;
      purse.textContent = `You carry ${value.gold} gold.`;
      result.textContent = value.result;
      transfer.update({
        source: { title: 'For sale', empty: 'Nothing for sale.', rows: value.buyAvailable ? value.stock.map(item => row(item, [
          ...(item.canBuy ? [{ id: 'merchant-buy', label: 'Buy', takesAmount: true }] : []),
          ...(item.canBuy && value.shopliftAvailable !== false ? [{ id: 'merchant-shoplift', label: 'Steal', danger: true, takesAmount: true }] : []),
        ], false)) : [] },
        pack: { title: 'Your items', empty: 'You carry nothing this shop deals in.',
          rows: value.sellAvailable || value.repairAvailable || value.identifyAvailable ? value.playerItems.map(item => row(item, [
            ...(value.sellAvailable && item.canSell ? [{ id: 'merchant-sell', label: 'Sell', takesAmount: true }] : []),
            ...(item.canRepair === true ? [{ id: 'merchant-repair', label: item.repairCost == null ? 'Repair' : `Repair · ${item.repairCost} gold` }] : []),
            ...(item.canIdentify === true ? [{ id: 'merchant-identify', label: item.identifyCost == null ? 'Identify' : `Identify · ${item.identifyCost} gold` }] : []),
          ], true)) : [] },
        takeAll: null,
      });
      transfer.refreshTotals();
      const pending = waiting;
      if (pending !== null && value.quote && value.quote.item === pending.key && value.quote.amount === pending.amount) {
        waiting = null;
        trade(pending.side, pending.key, pending.command, pending.amount);
      }
      redraw(repairs, JSON.stringify(value.repairAvailable ? value.repairs : []), () => {
        repairs.replaceChildren();
        if (!value.repairAvailable || value.repairs.length === 0) return;
        const title = document.createElement('h4'); title.textContent = 'Repairs';
        const list = document.createElement('ul');
        for (const repair of value.repairs) {
          const entry = document.createElement('li');
          entry.textContent = `${repair.label ?? repair.definition} · ${repair.status ?? (repair.ready ? 'ready' : 'in progress')}`;
          if (repair.ready) {
            const collect = button('Collect');
            collect.dataset.repair = repair.requestId;
            entry.append(' ', collect);
          }
          list.append(entry);
        }
        repairs.append(title, list);
      });
    },
  };
}
