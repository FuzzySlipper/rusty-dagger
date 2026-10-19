import type { ConfirmQuestion } from './confirm.js';
import { renderItemMaker, renderPotionMaker, renderSpellMaker, renderSummoning } from './crafting.js';
import { button, type Send } from './host.js';
import type { DaggerHud, DialogueProjection, MerchantItemProjection, MerchantProjection, SpellbookProjection } from './projection.js';
import { redraw } from './redraw.js';
import { renderSpellSales, spellAction } from './spells.js';

/**
 * A conversation and the services a person offers in it: topics and tone, training, food and
 * drink, a shop, spells for sale, item, potion and spell making, Daedric summoning and the bank.
 * A summoning offer opens as its own dialog above the conversation and needs an answer.
 */
export interface Dialogue {
  readonly window: HTMLDialogElement;
  readonly summoningOffer: HTMLDialogElement;
  /** End the conversation, as its close button does. */
  close(): void;
  update(value: DaggerHud, dead: boolean, spellbook: SpellbookProjection | null): void;
}

export function mountDialogue(shell: HTMLElement, send: Send, ask: (question: ConfirmQuestion) => void): Dialogue {
  const window_ = shell.querySelector<HTMLDialogElement>('.dagger-dialogue')!;
  const target = shell.querySelector<HTMLElement>('.dagger-dialogue-target')!;
  const greeting = shell.querySelector<HTMLElement>('.dagger-dialogue-greeting')!;
  const tone = shell.querySelector<HTMLSelectElement>('.dagger-dialogue-tone')!;
  const donation = shell.querySelector<HTMLLabelElement>('.dagger-dialogue-donation')!;
  const donationAmount = shell.querySelector<HTMLInputElement>('.dagger-dialogue-donation-amount')!;
  const question = shell.querySelector<HTMLElement>('.dagger-dialogue-question')!;
  const reply = shell.querySelector<HTMLElement>('.dagger-dialogue-reply')!;
  const topics = shell.querySelector<HTMLElement>('.dagger-dialogue-topics')!;
  const training = shell.querySelector<HTMLElement>('.dagger-dialogue-training')!;
  const trainingSummary = shell.querySelector<HTMLElement>('.dagger-dialogue-training-summary')!;
  const trainingSkills = shell.querySelector<HTMLElement>('.dagger-dialogue-training-skills')!;
  const trainingConfirm = shell.querySelector<HTMLElement>('.dagger-training-confirm')!;
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
  let pendingTraining: { revision: string; key: string; price: number; label: string } | null = null;

  const renderTrainingConfirmation = (): void => {
    trainingConfirm.hidden = pendingTraining === null;
    trainingConfirm.querySelector('p')!.textContent = pendingTraining ? `Train ${pendingTraining.label} for ${pendingTraining.price} gold?` : '';
  };
  trainingConfirm.querySelector('[data-training-no]')!.addEventListener('click', () => { pendingTraining = null; renderTrainingConfirmation(); });
  trainingConfirm.querySelector('[data-training-yes]')!.addEventListener('click', () => {
    const selected = pendingTraining;
    pendingTraining = null; renderTrainingConfirmation();
    if (!dead && selected && current?.revision === selected.revision && current.training?.price === selected.price)
      send({ action: 'training-commit', revision: selected.revision, key: selected.key, amount: selected.price, confirm: true });
  });
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
    if (control.dataset.topic === 'donate') {
      const amount = Number(donationAmount.value);
      if (!Number.isSafeInteger(amount) || amount <= 0) { donationAmount.focus(); return; }
      data.amount = amount;
    }
    if (control.dataset.key) data.key = control.dataset.key;
    send(data);
  });
  trainingSkills.addEventListener('click', event => {
    const control = (event.target as HTMLElement).closest<HTMLButtonElement>('button[data-training-skill]');
    const offer = current?.training;
    const skill = offer?.skills.find(value => value.id === control?.dataset.trainingSkill);
    if (!dead && skill && offer && current && skill.permanentValue < skill.maximumValue) {
      pendingTraining = { revision: current.revision, key: skill.id, price: offer.price, label: skill.label };
      renderTrainingConfirmation();
    }
  });
  const close = (): void => { if (!dead && current) send({ action: 'dialogue-close', revision: current.revision }); };
  shell.querySelector<HTMLButtonElement>('.dagger-dialogue-close')!.addEventListener('click', close);
  window_.addEventListener('cancel', event => { event.preventDefault(); close(); });
  summoningOffer.addEventListener('cancel', event => event.preventDefault());
  spellSales.addEventListener('click', event => {
    const control = (event.target as HTMLElement).closest<HTMLButtonElement>('button');
    if (control && !control.disabled) spellAction(control, send, ask, () => {});
  });

  const renderMerchant = (merchant: MerchantProjection | null): void => {
    merchantRoot.hidden = merchant === null;
    redraw(merchantRoot, JSON.stringify(merchant), () => drawMerchant(merchantRoot, merchant, send));
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
      redraw(offer ? summoningOffer : summoningRoot, JSON.stringify(summoning), () => renderSummoning(offer ? summoningOffer : summoningRoot, summoning, send));
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
      if (pendingTraining && (dialogue?.revision !== pendingTraining.revision || dialogue.training?.price !== pendingTraining.price
        || !dialogue.training.skills.some(skill => skill.id === pendingTraining!.key && skill.permanentValue < skill.maximumValue))) pendingTraining = null;
      renderTrainingConfirmation();
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
      renderMerchant(dialogue?.merchant ?? null);
      if (dialogue === null) {
        if (window_.open) window_.close();
        return;
      }
      target.textContent = dialogue.targetLabel + (dialogue.questContacts?.length ? ' — quest contact' : '');
      greeting.textContent = dialogue.greeting + (dialogue.comprehendLanguagesBonus ? ` Language comprehension: +${dialogue.comprehendLanguagesBonus}.` : '');
      tone.value = dialogue.tone;
      donation.hidden = !dialogue.topics.some(topic => topic.id === 'donate');
      question.textContent = dialogue.question ?? '';
      reply.textContent = dialogue.reply ?? '';
      redraw(topics, JSON.stringify([dialogue.revision, dialogue.topics, dialogue.bankAvailable]), () => {
        topics.replaceChildren(...dialogue.topics.map(topic => {
          const control = button(topic.label);
          control.dataset.topic = topic.id;
          if (topic.key) control.dataset.key = topic.key;
          return control;
        }));
        if (dialogue.bankAvailable) {
          const bank = button('Bank services');
          bank.dataset.bankOpen = 'true';
          bank.addEventListener('click', () => send({ action: 'bank-open', revision: dialogue.revision }));
          topics.append(bank);
        }
      });
      const offerTraining = dialogue.training ?? null;
      training.hidden = offerTraining === null;
      if (offerTraining) {
        const hours = Math.max(1, Math.round(offerTraining.durationSeconds / 3600));
        trainingSummary.textContent = `${offerTraining.price} gold for ${hours} hour${hours === 1 ? '' : 's'}${offerTraining.member ? ' (member rate)' : ''}.`;
      }
      redraw(trainingSkills, JSON.stringify(offerTraining?.skills ?? null), () => trainingSkills.replaceChildren(...(offerTraining?.skills ?? []).map(skill => {
        const control = button(`Train ${skill.label} (${skill.permanentValue}/${skill.maximumValue})`);
        control.dataset.trainingSkill = skill.id;
        control.disabled = skill.permanentValue >= skill.maximumValue;
        return control;
      })));
      if (!window_.open) window_.showModal();
    },
  };
}

function drawMerchant(root: HTMLElement, merchant: MerchantProjection | null, send: Send): void {
  root.replaceChildren();
  if (merchant === null) return;
  const heading = document.createElement('h3');
  heading.textContent = `${merchant.provider} · shop quality ${merchant.quality}`;
  const gold = document.createElement('p');
  gold.textContent = `Carried gold: ${merchant.gold}`;
  root.append(heading, gold);
  const addItems = (title: string, items: readonly MerchantItemProjection[], action: string, predicate: (item: MerchantItemProjection) => boolean): void => {
    const section = document.createElement('section');
    const sectionHeading = document.createElement('h4'); sectionHeading.textContent = title;
    const list = document.createElement('ul');
    for (const item of items) {
      const row = document.createElement('li');
      const condition = action === 'merchant-sell' && item.maximumCondition > 0 && item.currentCondition < item.maximumCondition
        ? ` · condition ${item.currentCondition}/${item.maximumCondition}` : '';
      row.textContent = `${item.label} × ${item.quantity}${item.stolen ? ' · stolen' : ''}${condition} · `;
      const tradeAvailable = predicate(item);
      const amountInput = tradeAvailable ? merchantQuantityInput(item) : undefined;
      row.append(merchantPriceLabel(item, amountInput));
      if (amountInput) row.append(' ', amountInput);
      if (tradeAvailable) row.append(' ', merchantAction(send, action, merchant, item, action === 'merchant-buy' ? 'Buy' : 'Sell', amountInput));
      if (action === 'merchant-buy' && tradeAvailable && merchant.shopliftAvailable !== false) row.append(' ', merchantAction(send, 'merchant-shoplift', merchant, item, 'Steal', amountInput));
      if (action === 'merchant-sell' && item.canRepair === true)
        row.append(' ', merchantAction(send, 'merchant-repair', merchant, item, item.repairCost == null ? 'Repair' : `Repair (${item.repairCost} gold)`));
      if (action === 'merchant-sell' && item.canIdentify === true)
        row.append(' ', merchantAction(send, 'merchant-identify', merchant, item, item.identifyCost == null ? 'Identify' : `Identify (${item.identifyCost} gold)`));
      list.append(row);
    }
    if (items.length === 0) { const empty = document.createElement('li'); empty.textContent = 'None.'; list.append(empty); }
    section.append(sectionHeading, list); root.append(section);
  };
  if (merchant.buyAvailable) addItems('Stock', merchant.stock, 'merchant-buy', item => item.canBuy);
  if (merchant.sellAvailable || merchant.repairAvailable || merchant.identifyAvailable)
    addItems('Your items', merchant.playerItems, 'merchant-sell', item => merchant.sellAvailable && item.canSell);
  if (merchant.repairAvailable && merchant.repairs.length > 0) {
    const section = document.createElement('section');
    const sectionHeading = document.createElement('h4'); sectionHeading.textContent = 'Repairs';
    const list = document.createElement('ul');
    for (const repair of merchant.repairs) {
      const row = document.createElement('li'); row.textContent = `${repair.label ?? repair.definition} · ${repair.status ?? (repair.ready ? 'ready' : 'in progress')}`;
      if (repair.ready) {
        const collect = button('Collect');
        collect.addEventListener('click', () => send({ action: 'merchant-collect-repair', revision: merchant.revision, key: repair.requestId }));
        row.append(' ', collect);
      }
      list.append(row);
    }
    section.append(sectionHeading, list); root.append(section);
  }
}

function merchantAmount(item: MerchantItemProjection): number | undefined {
  if (!item.key.startsWith('stack:')) return undefined;
  const amount = typeof item.quantity === 'number' ? item.quantity : Number(item.quantity);
  return Number.isSafeInteger(amount) && amount > 0 ? amount : undefined;
}

function merchantQuantityInput(item: MerchantItemProjection): HTMLInputElement | undefined {
  if (!item.key.startsWith('stack:')) return undefined;
  const available = merchantAmount(item);
  if (available === undefined) return undefined;
  const input = document.createElement('input');
  input.type = 'number';
  input.min = '1';
  input.max = String(available);
  input.value = '1';
  input.step = '1';
  input.inputMode = 'numeric';
  input.className = 'dagger-merchant-quantity';
  input.setAttribute('aria-label', `Quantity of ${item.label}`);
  return input;
}

function merchantPriceLabel(item: MerchantItemProjection, amountInput?: HTMLInputElement): HTMLSpanElement {
  const label = document.createElement('span');
  const unitPrice = String(item.unitPrice);
  label.textContent = amountInput === undefined ? `Unit price ${unitPrice} gold` : `Unit price ${unitPrice} gold each`;
  return label;
}

function merchantAction(send: Send, action: string, merchant: MerchantProjection, item: MerchantItemProjection, label: string,
  amountInput?: HTMLInputElement): HTMLButtonElement {
  const control = button(label);
  control.addEventListener('click', () => {
    if (amountInput === undefined) { send({ action, revision: merchant.revision, item: item.key }); return; }
    const available = merchantAmount(item);
    const amount = Number(amountInput.value);
    if (available === undefined || !Number.isSafeInteger(amount) || amount < 1 || amount > available) { amountInput.focus(); return; }
    send({ action, revision: merchant.revision, item: item.key, amount });
  });
  return control;
}
