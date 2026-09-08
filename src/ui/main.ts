import { mountInventory, type InventoryProjection, type InventoryAction } from './inventory.js';

interface ProjectionEnvelope {
  readonly contract: string;
  readonly value: unknown;
}

interface ProductUiContext {
  readonly ui: {
    setInteractionMode(mode: 'gameplay' | 'interface'): void;
    focusGameplay(): void;
  };
  readonly projection?: { subscribe(listener: (projection: ProjectionEnvelope | null) => void): () => void };
  readonly intents?: { claim(intent: string, value: { kind: 'product-payload'; contract: string; data: { action: string } | InventoryAction }): void };
}

interface DaggerHud {
  readonly resources: readonly { readonly id: string; readonly label: string; readonly current: number; readonly maximum: number }[];
  readonly lastOutcome: string;
  readonly composition: CompositionIdentity;
  readonly inventory?: InventoryProjection;
}

interface CompositionIdentity {
  readonly bundle: string;
  readonly ruleset: string;
  readonly contentPacks: readonly string[];
  readonly tuning: string;
  readonly fingerprint: string;
  readonly contentFingerprint: string;
  readonly tuningFingerprint: string;
}

export function mountProductUi(root: HTMLElement, context: ProductUiContext): { dispose(): void } {
  const stylesheet = document.createElement('link');
  stylesheet.rel = 'stylesheet';
  stylesheet.href = new URL('./styles.css', import.meta.url).href;
  const inventoryStylesheet = document.createElement('link');
  inventoryStylesheet.rel = 'stylesheet';
  inventoryStylesheet.href = new URL('./inventory.css', import.meta.url).href;
  document.head.append(stylesheet, inventoryStylesheet);

  const shell = document.createElement('section');
  shell.className = 'dagger-hud';
  shell.innerHTML = `
    <div class="dagger-title"><span>Privateer's Hold</span><strong>Exploring</strong></div>
    <div class="dagger-reticle" aria-hidden="true">+</div>
    <section class="dagger-vitals" aria-live="polite">
    </section>
    <p class="dagger-outcome" role="status">Awaiting projection…</p>
    <button class="dagger-menu-toggle" type="button" aria-haspopup="dialog">Menu · Esc</button>
    <dialog class="dagger-menu" aria-labelledby="dagger-menu-title">
      <h1 id="dagger-menu-title">Game menu</h1>
      <div class="dagger-menu-home">
        <button data-action="resume" autofocus>Return to game</button>
        <button data-action="inventory">Inventory &amp; equipment · I</button>
        <button disabled>Character · C — awaiting restoration</button>
        <button disabled>Loot · F — awaiting restoration</button>
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
      <section class="dagger-tools" hidden><p>The sprite workbench runs as a separate authoring tool. Its launcher is documented in the repository README.</p><p>In-game tool launching and visual editing are being restored separately.</p></section>
      <button data-action="back">Back to menu</button>
      </div>
    </dialog>
    <button class="dagger-attack" type="button">Attack</button>`;
  root.append(shell);

  const title = shell.querySelector<HTMLElement>('.dagger-title strong')!;
  const outcome = shell.querySelector<HTMLParagraphElement>('.dagger-outcome')!;
  const vitals = shell.querySelector<HTMLElement>('.dagger-vitals')!;
  const composition = shell.querySelector<HTMLDListElement>('.dagger-composition dl')!;
  const attack = shell.querySelector<HTMLButtonElement>('.dagger-attack')!;
  const claim = (action: string): void => context.intents?.claim('dagger.ui', {
    kind: 'product-payload', contract: 'dagger.ui.action.v1', data: { action },
  });
  const inventoryRoot = shell.querySelector<HTMLElement>('.dagger-inventory-root')!;
  const inventoryView = mountInventory(inventoryRoot, (action) => context.intents?.claim('dagger.ui', {
    kind: 'product-payload', contract: 'dagger.ui.action.v1', data: action,
  }));
  const onAttack = (): void => { if (!menu.open) claim('attack'); };
  const menu = shell.querySelector<HTMLDialogElement>('dialog')!;
  const menuTitle = shell.querySelector<HTMLElement>('#dagger-menu-title')!;
  const home = shell.querySelector<HTMLElement>('.dagger-menu-home')!;
  const panel = shell.querySelector<HTMLElement>('.dagger-menu-panel')!;
  const diagnostics = shell.querySelector<HTMLElement>('.dagger-composition')!;
  const tools = shell.querySelector<HTMLElement>('.dagger-tools')!;
  const menuToggle = shell.querySelector<HTMLButtonElement>('.dagger-menu-toggle')!;
  let activePanel: 'diagnostics' | 'tools' | 'inventory' | null = null;
  const showHome = (): void => {
    const previous = activePanel;
    activePanel = null;
    menu.classList.remove('has-inventory');
    home.hidden = false;
    panel.hidden = true;
    menuTitle.textContent = 'Game menu';
    home.querySelector<HTMLButtonElement>(`[data-action="${previous ?? 'resume'}"]`)!.focus();
  };
  const closeMenu = (): void => {
    menu.close();
    context.ui.setInteractionMode('gameplay');
    context.ui.focusGameplay();
  };
  const openMenu = (): void => {
    context.ui.setInteractionMode('interface');
    menu.showModal();
    showHome();
  };
  const dismiss = (): void => {
    if (activePanel !== null) showHome();
    else if (menu.open) closeMenu();
    else openMenu();
  };
  const showPanel = (action: 'diagnostics' | 'tools' | 'inventory'): void => {
    if (!menu.open) openMenu();
    activePanel = action;
    home.hidden = true;
    panel.hidden = false;
    diagnostics.hidden = action !== 'diagnostics';
    tools.hidden = action !== 'tools';
    inventoryRoot.hidden = action !== 'inventory';
    menu.classList.toggle('has-inventory', action === 'inventory');
    menuTitle.textContent = action === 'diagnostics' ? 'Composition diagnostics'
      : action === 'inventory' ? 'Inventory & equipment' : 'Sprite animation tool';
    panel.querySelector<HTMLButtonElement>(':scope > [data-action="back"]')!.focus();
  };
  const onMenuClick = (event: MouseEvent): void => {
    const action = (event.target as HTMLElement).closest<HTMLButtonElement>('button')?.dataset.action;
    if (action === 'resume') closeMenu();
    else if (action === 'back') showHome();
    else if (action === 'diagnostics' || action === 'tools' || action === 'inventory') showPanel(action);
  };
  // Capture before Engine input sees navigation keys. Escape's native dialog
  // cancellation is suppressed so one physical press performs exactly one step.
  const onKeyDown = (event: KeyboardEvent): void => {
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
        if (action === 'inventory') showPanel('inventory');
        else claim(action);
      }
    }
  };
  const onCancel = (event: Event): void => event.preventDefault();
  menu.addEventListener('cancel', onCancel);
  menu.addEventListener('click', onMenuClick);
  menuToggle.addEventListener('click', openMenu);
  document.addEventListener('keydown', onKeyDown, true);
  attack.addEventListener('click', onAttack);
  const unsubscribe = context.projection?.subscribe((projection) => {
    if (projection?.contract !== 'dagger.ui.snapshot.v1' || !isHud(projection.value)) return;
    const value = projection.value;
    if (value.inventory) inventoryView.update(value.inventory);
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
    title.textContent = 'Exploring';
    outcome.textContent = value.lastOutcome;
    composition.replaceChildren(...diagnosticRows(value.composition));
  }) ?? (() => {});
  return { dispose: () => {
    unsubscribe();
    inventoryView.dispose();
    document.removeEventListener('keydown', onKeyDown, true);
    menu.removeEventListener('cancel', onCancel);
    menu.removeEventListener('click', onMenuClick);
    menuToggle.removeEventListener('click', openMenu);
    if (menu.open) menu.close();
    attack.removeEventListener('click', onAttack);
    stylesheet.remove();
    inventoryStylesheet.remove();
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
    && 'tuning' in value && typeof value.tuning === 'string'
    && 'fingerprint' in value && isFingerprint(value.fingerprint)
    && 'contentFingerprint' in value && isFingerprint(value.contentFingerprint)
    && 'tuningFingerprint' in value && isFingerprint(value.tuningFingerprint);
}

function isFingerprint(value: unknown): value is string {
  return typeof value === 'string' && /^[0-9a-f]{64}$/.test(value);
}

function diagnosticRows(identity: CompositionIdentity): readonly HTMLElement[] {
  return [
    diagnosticRow('Bundle', identity.bundle),
    diagnosticRow('Ruleset', identity.ruleset),
    diagnosticRow('Packs', identity.contentPacks.join(' → ')),
    diagnosticRow('Tuning', identity.tuning),
    diagnosticRow('Composition', identity.fingerprint),
    diagnosticRow('Content', identity.contentFingerprint),
    diagnosticRow('Tuning profile', identity.tuningFingerprint),
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
