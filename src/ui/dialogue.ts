import type { ConfirmQuestion } from './confirm.js';
import { renderItemMaker, renderPotionMaker, renderSpellMaker, renderSummoning } from './crafting.js';
import { button, type Send } from './host.js';
import type { DaggerHud, DialogueProjection, MerchantItemProjection, MerchantProjection, SpellbookProjection } from './projection.js';
import { redraw } from './redraw.js';
import { renderSpellSales, spellAction } from './spells.js';
import { createStepper } from './stepper.js';
import { formatNumber, mountTransfer, type TransferRow } from './transfer.js';

/**
 * A conversation and the services a person offers in it: topics and tone, training, food and
 * drink, a shop, spells for sale, item, potion and spell making, Daedric summoning and the bank.
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
  update(value: DaggerHud, dead: boolean, spellbook: SpellbookProjection | null): void;
}

/** How many exchanges the conversation log keeps. */
const LOG_LENGTH = 8;

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
  const topics = shell.querySelector<HTMLElement>('.dagger-dialogue-topics')!;
  const training = shell.querySelector<HTMLElement>('.dagger-dialogue-training')!;
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

  let dead = false;
  let current: DialogueProjection | null = null;
  // The log is what this conversation has shown, kept only while it lasts; the ruleset publishes the latest exchange.
  let exchanges: { question: string; reply: string }[] = [];
  let conversation: string | null = null;

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
  shell.querySelector<HTMLButtonElement>('.dagger-dialogue-close')!.addEventListener('click', close);
  window_.addEventListener('cancel', event => { event.preventDefault(); close(); });
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
        if (window_.open) window_.close();
        return;
      }
      target.textContent = dialogue.targetLabel + (dialogue.questContacts?.length ? ' — quest contact' : '');
      greeting.textContent = dialogue.greeting + (dialogue.comprehendLanguagesBonus ? ` Language comprehension: +${dialogue.comprehendLanguagesBonus}.` : '');
      tone.value = dialogue.tone;
      donation.hidden = !dialogue.topics.some(topic => topic.id === 'donate');
      recordExchange(dialogue);
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
          list.append(...members.map(topic => {
            const control = button(topic.label);
            control.dataset.topic = topic.id;
            if (topic.key) control.dataset.key = topic.key;
            return control;
          }));
          if (bank) {
            const control = button('Bank services');
            control.dataset.bankOpen = 'true';
            control.addEventListener('click', () => send({ action: 'bank-open', revision: dialogue.revision }));
            list.append(control);
          }
          section.append(heading, list);
          return [section];
        }));
      });
      const offerTraining = dialogue.training ?? null;
      training.hidden = offerTraining === null;
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
      if (!window_.open) window_.showModal();
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
  const claim = (action: string, item: MerchantItemProjection, amount: number): void => {
    if (current === null) return;
    send(action === 'merchant-repair' || action === 'merchant-identify'
      ? { action, revision: current.revision, item: item.key }
      : { action, revision: current.revision, item: item.key, amount });
  };
  const transfer = mountTransfer(root, {
    command(side, key, command, amount) {
      const merchant = current;
      const item = merchant === null ? undefined : (side === 'source' ? merchant.stock : merchant.playerItems).find(row => row.key === key);
      if (merchant === null || item === undefined) return;
      const total = Number(item.unitPrice) * amount;
      const what = amount > 1 ? `${amount} × ${item.label}` : item.label;
      if (command === 'merchant-shoplift') {
        ask({ message: `Steal ${what}? If you are caught, it is a crime.`, accept: 'Steal', onAccept: () => claim(command, item, amount) });
      } else if ((command === 'merchant-buy' || command === 'merchant-sell') && total >= merchant.largeTradeGold) {
        ask({ message: `${command === 'merchant-buy' ? 'Buy' : 'Sell'} ${what} for ${formatNumber(total)} gold?`,
          accept: command === 'merchant-buy' ? 'Buy' : 'Sell', onAccept: () => claim(command, item, amount) });
      } else claim(command, item, amount);
    },
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
    return { key: item.key, label: item.label, icon: null, quantity: item.maximumQuantity > 1 ? String(item.quantity) : null,
      maximumQuantity: item.maximumQuantity, facts, unitPrice: commands.some(command => command.takesAmount) ? unit : undefined, commands };
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
