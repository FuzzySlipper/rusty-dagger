import { heldRevision, image } from './art.js';
import type { QuestMessageProjection, QuestPresentation, UiAction } from './projection.js';

export const questMessageNodes = new WeakMap<HTMLElement, Map<string, { signature: string; node: HTMLElement }>>();

export function renderQuestMessages(root: HTMLElement, value: QuestPresentation | undefined, claim: (action: UiAction) => void): void {
  const cache = questMessageNodes.get(root) ?? new Map<string, { signature: string; node: HTMLElement }>();
  questMessageNodes.set(root, cache);
  if (!value) { root.replaceChildren(); cache.clear(); return; }
  const ordered: HTMLElement[] = [];
  const used = new Set<string>();
  const keep = (key: string, projection: unknown, candidate: HTMLElement): void => {
    const signature = JSON.stringify(projection);
    const prior = cache.get(key);
    const node = prior?.signature === signature ? prior.node : candidate;
    cache.set(key, { signature, node }); used.add(key); ordered.push(node);
  };
  if (value.offer) {
    const offer = value.offer;
    const article = document.createElement('article'); article.className = 'dagger-quest-message dagger-frame'; article.dataset.skin = 'parchment';
    const title = document.createElement('strong'); title.textContent = 'Quest offer';
    const text = document.createElement('p'); text.textContent = offer.text; article.append(title, text);
    // An offer is a question answered as the confirmation answers one: accept, or decline.
    const answers = document.createElement('div'); answers.className = 'dagger-confirm-choices';
    for (const [label, confirm] of [['Accept', true], ['Decline', false]] as const) {
      const button = document.createElement('button'); button.type = 'button'; button.textContent = label;
      button.className = confirm ? 'dagger-confirm-accept' : 'dagger-confirm-cancel';
      button.addEventListener('click', () => claim({ action: 'quest-offer-answer', questInstance: offer.instance, confirm }));
      answers.append(button);
    }
    article.append(answers);
    keep(`offer:${offer.instance}`, offer, article);
  }
  if (value.escortFaces?.length) {
    const escorts = document.createElement('aside'); escorts.className = 'dagger-escort-faces';
    escorts.setAttribute('aria-label', 'Escorted companions');
    for (const face of value.escortFaces) {
      const portrait = document.createElement('img'); portrait.alt = face.name;
      portrait.dataset.questInstance = face.instance; portrait.dataset.questResource = face.symbol;
      const source = image(face.mediaId);
      if (source) portrait.src = source;
      else portrait.dataset.artMissing = face.mediaId;
      escorts.append(portrait);
    }
    keep("escorts", [value.escortFaces, heldRevision()], escorts);
  }
  const add = (message: QuestMessageProjection, heading: string, deliveries: readonly QuestMessageProjection[]): void => {
    const article = document.createElement('article');
    // Every quest message is read on parchment, as the classic popups and letters are.
    article.className = `dagger-quest-message dagger-quest-${message.delivery} dagger-frame`;
    article.dataset.skin = 'parchment';
    const title = document.createElement('strong');
    title.textContent = heading;
    const text = document.createElement('p');
    text.textContent = message.text;
    article.append(title, text);
    if (message.signoff) {
      const signoff = document.createElement('p');
      signoff.textContent = message.signoff;
      article.append(signoff);
    }
    if (message.entryId && (message.delivery === 'popup' || message.delivery === 'letter')) {
      const dismiss = document.createElement('button');
      dismiss.type = 'button'; dismiss.textContent = 'Dismiss';
      dismiss.addEventListener('click', () => {
        for (const delivery of deliveries) claim({ action: 'quest-dismiss', questInstance: delivery.instance,
          questDelivery: delivery.entryId ?? undefined });
      });
      article.append(dismiss);
    }
    keep(`delivery:${deliveries.map(delivery => `${delivery.instance}:${delivery.entryId ?? `${delivery.delivery}:${delivery.message}`}`).join('|')}`, deliveries, article);
  };
  // Several quests can deliver the same courier notification together. Show its text once while
  // dismissing each source delivery through the ordinary action; distinct letters keep their text.
  const groups = new Map<string, QuestMessageProjection[]>();
  for (const message of value.deliveries.filter(message => message.delivery !== 'prompt')) {
    const key = message.entryId && message.delivery === 'letter'
      ? JSON.stringify([message.delivery, message.heading, message.text, message.signoff, message.diagnostics])
      : `${message.instance}:${message.entryId ?? `${message.delivery}:${message.message}`}`;
    const group = groups.get(key) ?? []; group.push(message); groups.set(key, group);
  }
  for (const group of groups.values()) add(group[0]!, group[0]!.heading, group);
  if (value.pending) {
    const message = value.pending;
    const prompt = document.createElement('article');
    prompt.className = 'dagger-quest-message dagger-quest-prompt dagger-frame'; prompt.dataset.skin = 'parchment';
    const text = document.createElement('p');
    text.textContent = message.text;
    prompt.append(text);
    for (const option of message.options) {
      const button = document.createElement('button');
      button.type = 'button'; button.textContent = option.label;
      button.addEventListener('click', () => claim({ action: 'quest-choice', questInstance: message.instance,
        questMessage: message.message, questPrompt: message.promptId ?? undefined, questChoice: option.id }));
      prompt.append(button);
    }
    keep(`prompt:${message.instance}:${message.promptId ?? message.message}`, message, prompt);
  }
  // Do not detach unchanged nodes: a projection can arrive between pointerdown and pointerup.
  const retained = new Set(ordered);
  for (const child of [...root.children]) if (!retained.has(child as HTMLElement)) child.remove();
  for (let index = 0; index < ordered.length; index++) {
    const node = ordered[index]!;
    if (root.children[index] !== node) root.insertBefore(node, root.children[index] ?? null);
  }
  while (root.children.length > ordered.length) root.lastElementChild!.remove();
  for (const key of cache.keys()) if (!used.has(key)) cache.delete(key);

}

