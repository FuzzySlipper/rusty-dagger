/// <reference path="./live-debug-panel.d.ts" />
import { mountLiveDebugPanel, type LiveDebugPanelMount } from '@rusty-engine/live-debug';
import { adopt, heldRevision, image, type ArtRequestAction, type UiArt } from './art.js';
import { mountInventory, type InventoryProjection, type InventoryAction } from './inventory.js';
import { mountCharacter, isCharacterProjection, type CharacterProjection } from './character.js';
import { mountLoot, type LootProjection, type LootAction } from './loot.js';
import { BEGIN_ACTION, TITLE_MODE, screenForMode } from './screens.js';

interface ProjectionEnvelope {
  readonly contract: string;
  readonly value: unknown;
}

interface ControllerUiObservation {
  readonly context: 'interface';
  readonly fact:
    | { readonly kind: 'controller-button'; readonly button: string; readonly edge: 'pressed' | 'released' }
    | { readonly kind: 'controller-axis'; readonly axis: string; readonly value: number }
    | { readonly kind: 'controller-button-value'; readonly button: string; readonly value: number };
}

interface ProductUiContext {
  readonly input?: { subscribe(observer: (input: ControllerUiObservation) => void): () => void };
  readonly ui: {
    setInteractionMode(mode: 'gameplay' | 'interface'): void;
    focusGameplay(): void;
  };
  readonly projection?: { subscribe(listener: (projection: ProjectionEnvelope | null) => void): () => void };
  readonly intents?: { claim(intent: string, value: { kind: 'product-payload'; contract: string; data: { action: string } | InventoryAction | LootAction | ArtRequestAction }): void };
}

interface DaggerHud {
  readonly resources: readonly { readonly id: string; readonly label: string; readonly current: number; readonly maximum: number }[];
  readonly lastOutcome: string;
  readonly mode?: string;
  readonly composition: CompositionIdentity;
  readonly inventory?: InventoryProjection;
  readonly character?: CharacterProjection;
  readonly loot?: LootProjection | null;
  readonly uiArtRevision?: string;
  readonly uiArt?: UiArt | null;
  readonly panelRequest?: PanelRequest | null;
}

/** A panel the player asked for on a device the DOM has no channel of its own for. */
interface PanelRequest {
  readonly panel: string;
  readonly revision: string;
}

interface CompositionIdentity {
  readonly bundle: string;
  readonly ruleset: string;
  readonly contentPacks: readonly string[];
  readonly tuning: string;
}

/** Snapshots between repeated requests for art this DOM has not received. */
const ART_REQUEST_INTERVAL = 120;
const MENU_CONTROLLER = Object.freeze({
  accept: 'button-0', back: 'button-1', character: 'button-3',
  inventory: 'button-8', menu: 'button-9', previous: 'button-12', next: 'button-13',
  navigationAxis: 'axis-1', navigationThreshold: 0.55,
});
const MENU_FOCUSABLE = 'button:not(:disabled),select:not(:disabled),input:not(:disabled),a[href],[tabindex="0"]';

export function mountProductUi(root: HTMLElement, context: ProductUiContext): { dispose(): void } {
  const stylesheet = document.createElement('link');
  stylesheet.rel = 'stylesheet';
  stylesheet.href = new URL('./styles.css', import.meta.url).href;
  const inventoryStylesheet = document.createElement('link');
  inventoryStylesheet.rel = 'stylesheet';
  inventoryStylesheet.href = new URL('./inventory.css', import.meta.url).href;
  const characterStylesheet = document.createElement('link');
  characterStylesheet.rel = 'stylesheet';
  characterStylesheet.href = new URL('./character.css', import.meta.url).href;
  const lootStylesheet = document.createElement('link');
  lootStylesheet.rel = 'stylesheet';
  lootStylesheet.href = new URL('./loot.css', import.meta.url).href;
  document.head.append(stylesheet, inventoryStylesheet, characterStylesheet, lootStylesheet);

  const shell = document.createElement('section');
  shell.className = 'dagger-hud';
  shell.innerHTML = `
    <div class="dagger-title"><span>Privateer's Hold</span><strong>Exploring</strong></div>
    <div class="dagger-reticle" aria-hidden="true">+</div>
    <section class="dagger-vitals" aria-live="polite">
    </section>
    <p class="dagger-outcome" role="status">Awaiting projection…</p>
    <div class="dagger-death" role="alert" hidden><img class="dagger-death-screen" alt="You have died."></div>
    <div class="dagger-entry" role="dialog" aria-label="Title" hidden><img class="dagger-entry-screen" alt="Rusty Dagger"><button class="dagger-entry-begin" type="button">Begin</button></div>
    <button class="dagger-menu-toggle" type="button" data-action="menu" aria-haspopup="dialog">Menu · Esc</button>
    <dialog class="dagger-menu" aria-labelledby="dagger-menu-title">
      <h1 id="dagger-menu-title" tabindex="-1">Game menu</h1>
      <div class="dagger-menu-home">
        <button data-action="resume" autofocus>Return to game</button>
        <button data-action="inventory">Inventory &amp; equipment · I</button>
        <button data-action="character">Character · C</button>
        <button data-action="loot">Search aimed loot · F</button>
        <button data-action="save-game">Save game</button>
        <button data-action="load-game">Load game</button>
        <button data-action="debug">Engine debug console</button>
        <button data-action="diagnostics">Composition diagnostics</button>
        <button data-action="tools">Sprite animation tool</button>
        <button disabled>Settings — not yet available</button>
        <p>The world continues while this menu is open.</p>
      </div>
      <div class="dagger-menu-panel" hidden>
      <section class="dagger-composition" aria-label="Resolved composition diagnostics">
      <strong>Resolved composition</strong>
      <dl></dl>
    </section>
      <div class="dagger-inventory-root" hidden></div>
      <div class="dagger-character-root" hidden></div>
      <div class="dagger-loot-root" hidden></div>
      <div class="dagger-debug-root" data-rusty-ui-interactive hidden></div>
      <button data-action="loot-exit" hidden>Exit loot</button>
      <section class="dagger-tools" hidden><p>Sprite Workbench is a separate authoring application. Start it from the repository terminal:</p><pre>bash src/scripts/run-sprite-workbench.sh</pre><p>Edits save to authoring/sprites/privateers-hold.json.</p><a class="dagger-workbench-link" target="_blank" rel="noopener">Open Sprite Workbench ↗</a></section>
      <button data-action="back">Back to menu</button>
      </div>
    </dialog>`;
  root.append(shell);

  const title = shell.querySelector<HTMLElement>('.dagger-title strong')!;
  const outcome = shell.querySelector<HTMLParagraphElement>('.dagger-outcome')!;
  const vitals = shell.querySelector<HTMLElement>('.dagger-vitals')!;
  const composition = shell.querySelector<HTMLDListElement>('.dagger-composition dl')!;
  const claim = (action: string): void => context.intents?.claim('dagger.ui', {
    kind: 'product-payload', contract: 'dagger.ui.action.v1', data: { action },
  });
  const deathRoot = shell.querySelector<HTMLElement>('.dagger-death')!;
  const deathScreen = shell.querySelector<HTMLImageElement>('.dagger-death-screen')!;
  const entryRoot = shell.querySelector<HTMLElement>('.dagger-entry')!;
  const entryScreen = shell.querySelector<HTMLImageElement>('.dagger-entry-screen')!;
  const inventoryRoot = shell.querySelector<HTMLElement>('.dagger-inventory-root')!;
  const inventoryView = mountInventory(inventoryRoot, (action) => context.intents?.claim('dagger.ui', {
    kind: 'product-payload', contract: 'dagger.ui.action.v1', data: action,
  }));
  const characterRoot = shell.querySelector<HTMLElement>('.dagger-character-root')!;
  const characterView = mountCharacter(characterRoot);
  const lootRoot = shell.querySelector<HTMLElement>('.dagger-loot-root')!;
  const lootView = mountLoot(lootRoot, action => context.intents?.claim('dagger.ui', {
    kind: 'product-payload', contract: 'dagger.ui.action.v1', data: action,
  }));
  let currentLoot: LootProjection | null = null;
  let lastLootContainer: string | null = null;
  const closeLoot = (): void => {
    if (currentLoot) context.intents?.claim('dagger.ui', {
      kind: 'product-payload', contract: 'dagger.ui.action.v1', data: { action: 'loot-close', container: currentLoot.container },
    });
  };
  const menu = shell.querySelector<HTMLDialogElement>('dialog')!;
  const menuTitle = shell.querySelector<HTMLElement>('#dagger-menu-title')!;
  const home = shell.querySelector<HTMLElement>('.dagger-menu-home')!;
  const panel = shell.querySelector<HTMLElement>('.dagger-menu-panel')!;
  const diagnostics = shell.querySelector<HTMLElement>('.dagger-composition')!;
  const tools = shell.querySelector<HTMLElement>('.dagger-tools')!;
  const workbenchUrl = new URL(window.location.href);
  workbenchUrl.port = '4175'; workbenchUrl.pathname = '/'; workbenchUrl.search = ''; workbenchUrl.hash = '';
  shell.querySelector<HTMLAnchorElement>('.dagger-workbench-link')!.href = workbenchUrl.href;
  const menuToggle = shell.querySelector<HTMLButtonElement>('.dagger-menu-toggle')!;
  const debugRoot = shell.querySelector<HTMLElement>('.dagger-debug-root')!;
  let debugPanel: LiveDebugPanelMount | null = null;
  const closeDebug = (): void => { debugPanel?.dispose(); debugPanel = null; debugRoot.replaceChildren(); };
  const openDebug = (): void => {
    closeDebug();
    const host = document.createElement('div');
    debugRoot.append(host);
    void mountLiveDebugPanel(host, { enabled: true, presentation: 'inline' }).then(mount => {
      if (!host.isConnected || activePanel !== 'debug') { mount.dispose(); return; }
      debugPanel = mount;
      host.querySelector<HTMLInputElement>('input')?.focus();
    }).catch(error => {
      if (host.isConnected) host.textContent = `Debug console unavailable: ${error instanceof Error ? error.message : String(error)}`;
    });
  };
  let activePanel: 'diagnostics' | 'tools' | 'inventory' | 'character' | 'loot' | 'debug' | null = null;
  const showHome = (): void => {
    const previous = activePanel;
    if (previous === 'debug') closeDebug();
    if (previous === 'loot') closeLoot();
    activePanel = null;
    menu.classList.remove('has-inventory', 'has-character', 'has-loot', 'has-debug');
    home.hidden = false;
    panel.hidden = true;
    menuTitle.textContent = 'Game menu';
    home.querySelector<HTMLButtonElement>(`[data-action="${previous ?? 'resume'}"]`)!.focus();
  };
  const closeMenu = (): void => {
    controllerDirection = 0;
    if (activePanel === 'loot') closeLoot();
    closeDebug();
    activePanel = null;
    menu.close();
    context.ui.setInteractionMode(titleMode ? 'interface' : 'gameplay');
    if (!titleMode) context.ui.focusGameplay();
  };
  const openMenu = (): void => {
    controllerDirection = 0;
    context.ui.setInteractionMode('interface');
    menu.showModal();
    showHome();
  };
  const dismiss = (): void => {
    if (activePanel !== null) showHome();
    else if (menu.open) closeMenu();
    else openMenu();
  };
  const showPanel = (action: 'diagnostics' | 'tools' | 'inventory' | 'character' | 'loot' | 'debug'): void => {
    if (!menu.open) openMenu();
    if (activePanel === 'loot' && action !== 'loot') closeLoot();
    if (activePanel === 'debug') closeDebug();
    activePanel = action;
    home.hidden = true;
    panel.hidden = false;
    diagnostics.hidden = action !== 'diagnostics';
    tools.hidden = action !== 'tools';
    inventoryRoot.hidden = action !== 'inventory';
    characterRoot.hidden = action !== 'character';
    lootRoot.hidden = action !== 'loot';
    debugRoot.hidden = action !== 'debug';
    if (action === 'debug') openDebug();
    shell.querySelector<HTMLButtonElement>('[data-action="loot-exit"]')!.hidden = action !== 'loot';
    menu.classList.toggle('has-inventory', action === 'inventory');
    menu.classList.toggle('has-character', action === 'character');
    menu.classList.toggle('has-loot', action === 'loot');
    menu.classList.toggle('has-debug', action === 'debug');
    menuTitle.textContent = action === 'diagnostics' ? 'Composition diagnostics'
      : action === 'inventory' ? 'Inventory & equipment' : action === 'character' ? 'Character'
      : action === 'loot' ? 'Loot' : action === 'debug' ? 'Engine debug console' : 'Sprite animation tool';
    if (action === 'character') {
      menuTitle.focus({ preventScroll: true });
      menu.scrollTop = 0;
    } else panel.querySelector<HTMLButtonElement>(':scope > [data-action="back"]')!.focus();
  };
  // One place decides what a menu action means, whether the DOM heard it from a click, a key, or the
  // product answering a button on a pad the DOM cannot see.
  const runMenuAction = (action: string | undefined): void => {
    if (action === 'resume' || action === 'loot-exit') closeMenu();
    else if (action === 'back') showHome();
    else if (action === 'menu') dismiss();
    else if (action === 'loot') claim('loot');
    else if (action === 'save-game' || action === 'load-game') claim(action);
    else if (action === 'diagnostics' || action === 'tools' || action === 'inventory' || action === 'character' || action === 'debug') showPanel(action);
  };
  const onMenuClick = (event: MouseEvent): void =>
    runMenuAction((event.target as HTMLElement).closest<HTMLButtonElement>('button')?.dataset.action);
  // Capture before Engine input sees navigation keys. Escape's native dialog
  // cancellation is suppressed so one physical press performs exactly one step.
  const onKeyDown = (event: KeyboardEvent): void => {
    shell.classList.remove('controller-navigation');
    if (menu.open && event.code === 'Tab') {
      const buttons = Array.from(menu.querySelectorAll<HTMLElement>('button:not(:disabled),select:not(:disabled),input:not(:disabled),[tabindex="0"]'))
        .filter((button) => button.getClientRects().length > 0);
      const current = buttons.indexOf(document.activeElement as HTMLElement);
      if (current < 0 || (!event.shiftKey && current === buttons.length - 1)
        || (event.shiftKey && current === 0)) {
        event.preventDefault();
        event.stopPropagation();
        buttons[event.shiftKey ? buttons.length - 1 : 0]?.focus();
      }
      return;
    }
    if (event.code === 'Escape') {
      event.preventDefault();
      event.stopPropagation();
      if (!event.repeat) dismiss();
      return;
    }
    if (menu.open || event.ctrlKey || event.altKey || event.metaKey
      || (event.target instanceof Element && event.target.closest('input,textarea,select,[contenteditable="true"]'))) return;
    const action = ({ KeyI: 'inventory', KeyC: 'character', KeyF: 'loot' } as Record<string, string>)[event.code];
    if (action) {
      event.preventDefault();
      event.stopPropagation();
      if (!event.repeat) {
        if (action === 'inventory' || action === 'character' || action === 'debug') showPanel(action);
        else claim(action);
      }
    }
  };
  // The Engine owns controller sampling and interface/gameplay arbitration.
  // This adapter assigns only DOM menu meaning and reuses click/menu handlers.
  let controllerDirection = 0;
  const controllerScope = (): HTMLElement | null => menu.open ? menu : !entryRoot.hidden ? entryRoot : null;
  const focusControllerItem = (direction: number): void => {
    const scope = controllerScope();
    if (scope === null) return;
    const items = Array.from(scope.querySelectorAll<HTMLElement>(MENU_FOCUSABLE))
      .filter(item => item.getClientRects().length > 0);
    if (items.length === 0) return;
    const current = items.indexOf(document.activeElement as HTMLElement);
    const next = current < 0 ? (direction < 0 ? items.length - 1 : 0)
      : (current + direction + items.length) % items.length;
    items[next]?.focus();
  };
  const unsubscribeController = context.input?.subscribe(({ fact }) => {
    shell.classList.add('controller-navigation');
    if (fact.kind === 'controller-axis' && fact.axis === MENU_CONTROLLER.navigationAxis) {
      const direction = Math.abs(fact.value) < MENU_CONTROLLER.navigationThreshold ? 0 : Math.sign(fact.value);
      if (direction !== 0 && direction !== controllerDirection) focusControllerItem(direction);
      controllerDirection = direction;
      return;
    }
    if (fact.kind !== 'controller-button' || fact.edge !== 'pressed') return;
    if (fact.button === MENU_CONTROLLER.previous) focusControllerItem(-1);
    else if (fact.button === MENU_CONTROLLER.next) focusControllerItem(1);
    else if (fact.button === MENU_CONTROLLER.accept) {
      const scope = controllerScope();
      if (scope === null) return;
      let active = document.activeElement as HTMLElement | null;
      if (active === null || !scope.contains(active) || !active.matches(MENU_FOCUSABLE)) {
        focusControllerItem(1);
        active = document.activeElement as HTMLElement | null;
      }
      if (active !== null && scope.contains(active)) active.click();
    } else if (menu.open) {
      if (fact.button === MENU_CONTROLLER.back || fact.button === MENU_CONTROLLER.menu) dismiss();
      else if (fact.button === MENU_CONTROLLER.inventory) runMenuAction('inventory');
      else if (fact.button === MENU_CONTROLLER.character) runMenuAction('character');
    }
  });
  const onCancel = (event: Event): void => event.preventDefault();
  // One named handler, so the listener dispose removes is the listener this mount added.
  const onMenuToggle = (): void => runMenuAction('menu');
  menu.addEventListener('cancel', onCancel);
  menu.addEventListener('click', onMenuClick);
  menuToggle.addEventListener('click', onMenuToggle);
  document.addEventListener('keydown', onKeyDown, true);
  // The published art arrives inside a snapshot: bytes the session read from admitted content, keyed
  // by the pack's media identity. The DOM holds the last block it saw and asks the product for the
  // revision it is missing, which is what a reload needs and what a republished artifact produces.
  let artRevision = heldRevision();
  let artCooldown = 0;
  let deadMode = false;
  let titleMode = false;
  // The entry screen is the mode's own screen: the mode shows the published artifact, and the one thing
  // the screen does is ask the product to begin. The product decides, so the answer is the mode changing
  // rather than this hiding itself.
  const redrawEntry = (): void => {
    const wasVisible = !entryRoot.hidden;
    entryRoot.hidden = !titleMode;
    if (wasVisible !== titleMode && !menu.open) {
      controllerDirection = 0;
      context.ui.setInteractionMode(titleMode ? 'interface' : 'gameplay');
      if (!titleMode) context.ui.focusGameplay();
    }
    if (!titleMode) return;
    const entry = image(screenForMode(TITLE_MODE)!);
    if (entry !== null) entryScreen.src = entry;
  };
  shell.querySelector<HTMLButtonElement>('.dagger-entry-begin')!.addEventListener('click', () => {
    context.intents?.claim('dagger.ui', {
      kind: 'product-payload', contract: 'dagger.ui.action.v1', data: { action: BEGIN_ACTION },
    });
  });
  let lastPanelRevision: string | null = null;
  const requestArt = (revision: string): void => context.intents?.claim('dagger.ui', {
    kind: 'product-payload', contract: 'dagger.ui.action.v1', data: { action: 'art-request', revision },
  });
  const redrawArt = (): void => {
    deathRoot.hidden = !deadMode;
    const death = image(screenForMode('dead')!);
    if (deadMode && death !== null) deathScreen.src = death;
    redrawEntry();
    // The panels memo their own state revision, so art that arrived after the state it draws has to
    // ask them to paint again rather than re-send a projection they would ignore.
    inventoryView.refresh();
    characterView.refresh();
    lootView.refresh();
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
        requestArt(value.uiArtRevision);
      } else {
        artCooldown--;
      }
    } else {
      artCooldown = 0;
    }

    if (value.inventory) inventoryView.update(value.inventory);
    if (value.character && isCharacterProjection(value.character)) characterView.update(value.character);
    // A pad button reaches the product, not this DOM, so the panel it asked for arrives here as the
    // menu action that opens it. Each revision is performed once; the request itself stays published.
    const panelRequest = value.panelRequest ?? null;
    if (panelRequest !== null && panelRequest.revision !== lastPanelRevision) {
      lastPanelRevision = panelRequest.revision;
      runMenuAction(panelRequest.panel);
    }
    currentLoot = value.loot ?? null;
    lootView.update(currentLoot);
    if (currentLoot && currentLoot.container !== lastLootContainer) {
      lastLootContainer = currentLoot.container;
      showPanel('loot');
    } else if (!currentLoot && activePanel === 'loot') showHome();
    vitals.replaceChildren(...value.resources.map((resource) => {
      const row = document.createElement('p');
      const label = document.createElement('span');
      const amount = document.createElement('strong');
      label.textContent = resource.label;
      amount.dataset.resource = resource.id;
      amount.textContent = `${resource.current} / ${resource.maximum}`;
      row.append(label, amount);
      return row;
    }));
    // Death outranks every other mode, so the screen the mode owns replaces the HUD rather than
    // joining it; the image is the published artifact this mode exists to show.
    deadMode = value.mode === 'dead';
    deathRoot.hidden = !deadMode;
    if (deadMode) {
      const death = image(screenForMode('dead')!);
      if (death !== null) deathScreen.src = death;
    }

    // The entry screen is the mode's screen, the way the death screen is the dead mode's: the mode
    // value decides which one is up, and the artifact the mode names is what it shows.
    titleMode = value.mode === TITLE_MODE;
    redrawEntry();

    title.textContent = 'Exploring';
    outcome.textContent = value.lastOutcome;
    composition.replaceChildren(...diagnosticRows(value.composition));
  }) ?? (() => {});
  return { dispose: () => {
    unsubscribeController?.();
    unsubscribe();
    closeDebug();
    inventoryView.dispose();
    characterView.dispose();
    lootView.dispose();
    document.removeEventListener('keydown', onKeyDown, true);
    menu.removeEventListener('cancel', onCancel);
    menu.removeEventListener('click', onMenuClick);
    menuToggle.removeEventListener('click', onMenuToggle);
    if (menu.open) menu.close();
    stylesheet.remove();
    inventoryStylesheet.remove();
    characterStylesheet.remove();
    lootStylesheet.remove();
    shell.remove();
  } };
}

export function isHud(value: unknown): value is DaggerHud {
  return typeof value === 'object' && value !== null
    && 'resources' in value && Array.isArray(value.resources) && value.resources.every(isResourceRow)
    && 'lastOutcome' in value && typeof value.lastOutcome === 'string'
    && 'composition' in value && isCompositionIdentity(value.composition);
}

function isResourceRow(value: unknown): value is DaggerHud['resources'][number] {
  return typeof value === 'object' && value !== null
    && 'id' in value && typeof value.id === 'string'
    && 'label' in value && typeof value.label === 'string'
    && 'current' in value && typeof value.current === 'number' && Number.isFinite(value.current)
    && 'maximum' in value && typeof value.maximum === 'number' && Number.isFinite(value.maximum);
}

function isCompositionIdentity(value: unknown): value is CompositionIdentity {
  return typeof value === 'object' && value !== null
    && 'bundle' in value && typeof value.bundle === 'string'
    && 'ruleset' in value && typeof value.ruleset === 'string'
    && 'contentPacks' in value && Array.isArray(value.contentPacks) && value.contentPacks.every((pack) => typeof pack === 'string')
    && 'tuning' in value && typeof value.tuning === 'string';
}

function diagnosticRows(identity: CompositionIdentity): readonly HTMLElement[] {
  return [
    diagnosticRow('Bundle', identity.bundle),
    diagnosticRow('Ruleset', identity.ruleset),
    diagnosticRow('Packs', identity.contentPacks.join(' → ')),
    diagnosticRow('Tuning', identity.tuning),
  ];
}

function diagnosticRow(label: string, value: string): HTMLElement {
  const fragment = document.createDocumentFragment();
  const term = document.createElement('dt');
  const definition = document.createElement('dd');
  term.textContent = label;
  definition.textContent = value;
  fragment.append(term, definition);
  const row = document.createElement('div');
  row.append(fragment);
  return row;
}
