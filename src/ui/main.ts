interface ProjectionEnvelope {
  readonly contract: string;
  readonly value: unknown;
}

interface ProductUiContext {
  readonly projection?: { subscribe(listener: (projection: ProjectionEnvelope | null) => void): () => void };
  readonly intents?: { claim(intent: string, value: { kind: 'digital'; active: boolean }): void };
}

interface DaggerHud {
  readonly resources: readonly { readonly id: string; readonly label: string; readonly current: number; readonly maximum: number }[];
  readonly activeEncounter: { readonly name: string; readonly objective: string } | null;
  readonly lastOutcome: string;
}

export function mountProductUi(root: HTMLElement, context: ProductUiContext): { dispose(): void } {
  const stylesheet = document.createElement('link');
  stylesheet.rel = 'stylesheet';
  stylesheet.href = './ui/styles.css';
  document.head.append(stylesheet);

  const shell = document.createElement('section');
  shell.className = 'dagger-hud';
  shell.innerHTML = `
    <div class="dagger-title"><span>Privateer's Hold</span><strong>Exploring</strong></div>
    <div class="dagger-reticle" aria-hidden="true">+</div>
    <section class="dagger-vitals" aria-live="polite">
    </section>
    <p class="dagger-outcome" role="status">Awaiting projection…</p>
    <button type="button">Attack</button>`;
  root.append(shell);

  const title = shell.querySelector<HTMLElement>('.dagger-title strong')!;
  const outcome = shell.querySelector<HTMLParagraphElement>('.dagger-outcome')!;
  const vitals = shell.querySelector<HTMLElement>('.dagger-vitals')!;
  const attack = shell.querySelector<HTMLButtonElement>('button')!;
  const onAttack = (): void => context.intents?.claim('attack', { kind: 'digital', active: true });
  attack.addEventListener('click', onAttack);
  const unsubscribe = context.projection?.subscribe((projection) => {
    if (projection?.contract !== 'dagger.ui.snapshot.v1' || !isHud(projection.value)) return;
    const value = projection.value;
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
    title.textContent = value.activeEncounter ? `${value.activeEncounter.name} — ${value.activeEncounter.objective}` : 'Exploring';
    outcome.textContent = value.lastOutcome;
  }) ?? (() => {});
  return { dispose: () => { unsubscribe(); attack.removeEventListener('click', onAttack); stylesheet.remove(); shell.remove(); } };
}

function isHud(value: unknown): value is DaggerHud {
  return typeof value === 'object' && value !== null
    && 'resources' in value && Array.isArray(value.resources) && value.resources.every(isResourceRow)
    && 'lastOutcome' in value && typeof value.lastOutcome === 'string';
}

function isResourceRow(value: unknown): value is DaggerHud['resources'][number] {
  return typeof value === 'object' && value !== null
    && 'id' in value && typeof value.id === 'string'
    && 'label' in value && typeof value.label === 'string'
    && 'current' in value && typeof value.current === 'number'
    && 'maximum' in value && typeof value.maximum === 'number';
}
