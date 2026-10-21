import type { RustyApplicationUiContext } from '@rusty-engine/product-ui';
import { adopt, adoptDemanded, heldDemandedRevision, heldRevision } from './art.js';
import { mountChoices } from './choices.js';
import { mountConfirm } from './confirm.js';
import { mountDeath } from './death.js';
import { mountDialogue } from './dialogue.js';
import { mountDungeonText } from './dungeon-text.js';
import { mountEntry } from './entry.js';
import { sender } from './host.js';
import { mountHud } from './hud.js';
import { EDIT_TEXT_EVENT, mountLetterPicker } from './letter-picker.js';
import { mountInteraction } from './interaction.js';
import { mountMenu } from './menu.js';
import { mountNavigation } from './navigation.js';
import { mountScale } from './scale.js';
import { applySkin } from './skin.js';
import { isHud, isSaveSlots, type SaveSlotProjection } from './projection.js';
import { renderQuestMessages } from './quests.js';
import { mountShell } from './shell.js';
import { isSpellbookProjection } from './spells.js';
import { mountTooltips } from './tooltip.js';

export { isHud } from './projection.js';
export { isSummoningProjection, renderSummoning } from './crafting.js';

/** Snapshots between repeated requests for art this DOM has not received. */
const ART_REQUEST_INTERVAL = 120;
/** Controller shortcuts the open menu adds to the shared navigation: Start, Select, Y and X. */
const MENU_SHORTCUTS = Object.freeze({ menu: 'button-9', inventory: 'button-8', character: 'button-3', contextMenu: 'button-2' });

/**
 * Mounts the product UI and routes each published snapshot to the surface that draws it. Every
 * surface lives in its own module; this file only composes them with the two shared owners: the
 * interaction lifecycle (interface or gameplay, pointer lock) and keyboard and controller
 * navigation (`docs/game-ui.md`).
 */
export function mountProductUi(root: HTMLElement, context: RustyApplicationUiContext): { dispose(): void } {
  const { shell, dispose: disposeShell } = mountShell(root);
  const send = sender(context);
  const confirmPrompt = mountConfirm(shell);
  const ask = confirmPrompt.ask;
  const tooltips = mountTooltips(shell);

  let dead = false;
  let questAnswerRequired = false;
  let saveSlots: SaveSlotProjection = { entries: [], diagnostic: null };
  // Deciding the mode and the focus scope together keeps them from disagreeing about which surface is up.
  const sync = (focus = false): void => { interaction.sync(focus); navigation.refresh(); };

  // A playing cinematic takes any key or press first, so the HUD listens before navigation does.
  const hud = mountHud(shell, send);
  // Key capture for rebinding runs before navigation, which would otherwise take Escape and the arrows.
  const onKeyDown = (event: KeyboardEvent): void => menu.captureKey(event);
  document.addEventListener('keydown', onKeyDown, true);
  // Escape with no surface open is Escape in play: it opens the menu, except where there is none.
  const navigation = mountNavigation(shell, context, {
    onPlayEscape: () => { if (!dead && !entry.isTitle()) menu.open(); },
    // While a cinematic plays the shell draws it above the page; any pad press skips it, as any key does.
    captureButton: button => {
      if (hud.cinematicPlaying()) { hud.skipCinematic(); return true; }
      return menu.captureButton(button);
    },
    editText: field => picker.edit(field),
    onButton: button => {
      if (!menu.dialog.open || confirmPrompt.open()) return;
      if (button === MENU_SHORTCUTS.menu) menu.dismiss();
      else if (button === MENU_SHORTCUTS.inventory) menu.togglePanel('inventory');
      else if (button === MENU_SHORTCUTS.character) menu.togglePanel('character');
      else if (button === MENU_SHORTCUTS.contextMenu) menu.contextMenu();
    },
  });
  // How a pad types: the letter picker edits any text field, asked by navigation or by a field's ✎ button.
  const picker = mountLetterPicker(shell, surface => navigation.register(surface));
  const onEditText = (event: Event): void => {
    const detail = (event as CustomEvent<{ field: HTMLInputElement | HTMLTextAreaElement; label: string }>).detail;
    picker.edit(detail.field, detail.label);
  };
  shell.addEventListener(EDIT_TEXT_EVENT, onEditText);
  const quests = shell.querySelector<HTMLElement>('.dagger-quests')!;
  const choices = mountChoices(shell, quests, send);
  const death = mountDeath(shell, send);
  const dungeonText = mountDungeonText(shell, send);
  const scale = mountScale(context);
  const menu = mountMenu(shell, { send, ask, sync, isDead: () => dead, register: surface => navigation.register(surface), uiScale: scale.current });
  const entry = mountEntry(shell, send, menu.creation, () => { if (menu.dialog.open) menu.dialog.close(); });
  const dialogue = mountDialogue(shell, send, ask);

  // A pointer lock the world held, lost in play, is the player asking for the menu: the browser and
  // the desktop shell spend Escape on releasing the lock, so this is what Escape does in play.
  const interaction = mountInteraction(context, { onLockLostInPlay: () => { if (!menu.dialog.open && !dead && !entry.isTitle()) menu.open(); } });
  interaction.needsCursor(() => menu.dialog.open || dialogue.window.open || dialogue.summoningOffer.open);
  // The surfaces that take the cursor, topmost first. Escape and Back go to the topmost: from any menu
  // panel Escape closes the whole menu while Back steps out one panel. A question the product needs
  // answered (a summoning offer, a court, a quest prompt, the death and title screens) has no back, so
  // neither press acts on it or on what it was asked over; a spell choice takes its Cancel.
  const surfaces = [
    navigation.register({ name: 'confirm', root: confirmPrompt.dialog, layer: 100, isOpen: confirmPrompt.open, back: confirmPrompt.cancel }),
    navigation.register({ name: 'menu', root: menu.dialog, layer: 90, isOpen: () => menu.dialog.open,
      back: menu.back, panel: menu.stepPanel, page: menu.page, pageHint: menu.pageHint }),
    navigation.register({ name: 'summoning-offer', root: dialogue.summoningOffer, layer: 80, isOpen: () => dialogue.summoningOffer.open }),
    navigation.register({ name: 'legal', root: choices.legal, layer: 70, isOpen: () => !choices.legal.hidden }),
    navigation.register({ name: 'quests', root: quests, layer: 60, isOpen: () => questAnswerRequired && !dead }),
    navigation.register({ name: 'dialogue', root: dialogue.window, layer: 50, isOpen: () => dialogue.window.open, back: dialogue.close }),
    ...choices.cancellable.map(service => navigation.register({ name: service.getAttribute('aria-label')!, root: service, layer: 45,
      isOpen: () => !service.hidden, back: () => service.querySelector<HTMLButtonElement>('[data-escape]:not(:disabled)')?.click() })),
    navigation.register({ name: 'dungeon-text', root: dungeonText.root, layer: 40, isOpen: () => !dungeonText.root.hidden, back: dungeonText.close,
      initialFocus: dungeonText.initialFocus }),
    navigation.register({ name: 'death', root: death.root, layer: 20, isOpen: () => !death.root.hidden,
      initialFocus: () => death.root.querySelector<HTMLElement>('.dagger-death-actions button:not(:disabled)') }),
    navigation.register({ name: 'title', root: entry.root, layer: 10, isOpen: () => !entry.root.hidden, panel: entry.stepPanel }),
  ];

  // The published art arrives inside a snapshot: bytes the session read from admitted content, keyed
  // by the pack's media identity. The DOM holds the last block it saw and asks the product for the
  // revision it is missing, which is what a reload needs and what a republished artifact produces.
  let artRevision = heldRevision();
  let artCooldown = 0;
  let demandedCooldown = 0;
  const redrawArt = (): void => {
    // The kit becomes the skin's custom properties once per art block; every primitive draws from them.
    applySkin();
    choices.refreshArt();
    death.refreshArt();
    entry.refreshArt();
    menu.refreshArt();
  };

  const unsubscribe = context.projection?.subscribe((projection) => {
    if (projection?.contract !== 'dagger.ui.snapshot.v1' || !isHud(projection.value)) return;
    const value = projection.value;
    const adopted = value.uiArt ? adopt(value.uiArt) : '';
    if (adopted.length > 0 && adopted !== artRevision) {
      artRevision = adopted;
      redrawArt();
    }
    if (value.uiArtRevision && value.uiArtRevision !== artRevision) {
      // Ask again on a slow retry rather than once, because the answer travels the same transport
      // this request does: one snapshot that carried art would have cleared the cooldown instead.
      if (artCooldown === 0) {
        artCooldown = ART_REQUEST_INTERVAL;
        send({ action: 'art-request', revision: value.uiArtRevision });
      } else {
        artCooldown--;
      }
    } else {
      artCooldown = 0;
    }

    // The demanded block is adopted on top of the held art, so it only counts once that art is current.
    if (value.uiArtDemanded && artRevision === value.uiArtRevision) {
      const before = heldDemandedRevision();
      if (adoptDemanded(value.uiArtDemanded) !== before) menu.refreshArt();
    }
    if (value.uiArtDemandedRevision && artRevision === value.uiArtRevision && value.uiArtDemandedRevision !== heldDemandedRevision()) {
      if (demandedCooldown === 0) {
        demandedCooldown = ART_REQUEST_INTERVAL;
        send({ action: 'art-request', revision: value.uiArtDemandedRevision });
      } else {
        demandedCooldown--;
      }
    } else {
      demandedCooldown = 0;
    }

    // Death outranks every other mode, so the screen the mode owns replaces the HUD rather than
    // joining it; its semantic choices and effects arrive in the same projection as the image.
    dead = value.mode === 'dead';
    if (isSaveSlots(value.saveSlots)) saveSlots = value.saveSlots;
    // The interface scale follows the player's persisted choice, or the window while it is automatic.
    if (value.controls?.uiScaleRange) scale.update(value.controls.uiScale ?? null, value.controls.uiScaleRange);
    const spellbook = isSpellbookProjection(value.spells) ? value.spells : null;
    choices.update(value);
    dungeonText.update(value);
    const questMessages = value.quests ? [value.quests.offer, ...value.quests.deliveries,
      ...[...value.quests.journal.active, ...value.quests.journal.finished].flatMap(group => group.entries), value.quests.pending] : [];
    menu.update(value, dead, spellbook, [
      ...(value.activation?.dialogue?.diagnostics ?? []),
      ...questMessages.flatMap(message => message?.diagnostics ?? []),
      ...(value.spells?.summoning?.diagnostics ?? []),
    ]);
    death.update(dead, value.death, saveSlots);
    entry.update(value);
    hud.update(value);
    renderQuestMessages(quests, value.quests, send);
    dialogue.update(value, dead, spellbook);

    // The Engine owns pointer lock; the product tells it whether any current surface needs a cursor.
    questAnswerRequired = Boolean(value.quests?.offer || value.quests?.pending
      || value.quests?.deliveries.some(message => message.delivery === 'popup' || message.delivery === 'letter'));
    interaction.setProjectionNeedsCursor(value.mode === 'title' || value.mode === 'dead' || value.mode === 'paused' || value.mode === 'modal'
      || hud.cinematicPlaying() || questAnswerRequired
      || Boolean(value.dungeonText || value.legal || value.createItem || value.identify || value.teleport || value.dispel));
    // A question outlives nothing it was asked over: death, or the menu or dialogue closing, answers no.
    if (dead) confirmPrompt.cancel(); else confirmPrompt.dropIfOwnerClosed();
    sync();
  }) ?? (() => {});

  return { dispose: () => {
    tooltips.dispose();
    scale.dispose();
    for (const unregister of surfaces) unregister();
    navigation.dispose();
    interaction.dispose();
    unsubscribe();
    hud.dispose();
    menu.dispose();
    document.removeEventListener('keydown', onKeyDown, true);
    disposeShell();
  } };
}
