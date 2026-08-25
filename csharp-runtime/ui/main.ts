interface ProjectionEnvelope {
  readonly contract: string;
  readonly value: unknown;
}

interface ProductUiContext {
  readonly projection?: { subscribe(listener: (projection: ProjectionEnvelope | null) => void): () => void };
  readonly intents?: { claim(intent: string, value: { kind: 'digital'; active: boolean }): void };
}

interface DaggerHud {
  readonly player: { readonly health: number; readonly maximumHealth: number; readonly stamina: number; readonly maximumStamina: number; readonly magicka: number; readonly maximumMagicka: number };
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
      <p><span>Health</span><strong data-vital="health">—</strong></p>
      <p><span>Stamina</span><strong data-vital="stamina">—</strong></p>
      <p><span>Magicka</span><strong data-vital="magicka">—</strong></p>
    </section>
    <p class="dagger-outcome" role="status">Awaiting projection…</p>
    <button type="button">Attack</button>`;
  root.append(shell);

  const title = shell.querySelector<HTMLElement>('.dagger-title strong')!;
  const outcome = shell.querySelector<HTMLParagraphElement>('.dagger-outcome')!;
  const vital = (name: string): HTMLElement => shell.querySelector<HTMLElement>(`[data-vital="${name}"]`)!;
  const attack = shell.querySelector<HTMLButtonElement>('button')!;
  const onAttack = (): void => context.intents?.claim('attack', { kind: 'digital', active: true });
  attack.addEventListener('click', onAttack);
  const unsubscribe = context.projection?.subscribe((projection) => {
    if (projection?.contract !== 'dagger.ui.snapshot.v1' || !isHud(projection.value)) return;
    const value = projection.value;
    vital('health').textContent = `${value.player.health} / ${value.player.maximumHealth}`;
    vital('stamina').textContent = `${value.player.stamina} / ${value.player.maximumStamina}`;
    vital('magicka').textContent = `${value.player.magicka} / ${value.player.maximumMagicka}`;
    title.textContent = value.activeEncounter ? `${value.activeEncounter.name} — ${value.activeEncounter.objective}` : 'Exploring';
    outcome.textContent = value.lastOutcome;
  }) ?? (() => {});
  return { dispose: () => { unsubscribe(); attack.removeEventListener('click', onAttack); stylesheet.remove(); shell.remove(); } };
}

function isHud(value: unknown): value is DaggerHud {
  return typeof value === 'object' && value !== null && 'player' in value && 'lastOutcome' in value;
}
