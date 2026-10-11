export interface ControlsProjection {
  readonly diagnostic: string;
  readonly lookSensitivity?: number;
  readonly invertVertical?: boolean;
  readonly bindings: readonly { readonly id: string; readonly category: string; readonly keys: readonly string[]; readonly fixed: boolean }[];
}
export interface ControlAction { readonly action: string; readonly item?: string; readonly key?: string; readonly confirm?: boolean; readonly sensitivity?: number; readonly invert?: boolean }

/** Only captures the player's proposed key. C# validates conflicts and owns the active map. */
export function mountControls(root: HTMLElement, claim: (action: ControlAction) => void) {
  const status = document.createElement('p'); status.setAttribute('role', 'status');
  const rows = document.createElement('div');
  const swapLabel = document.createElement('label');
  const swap = document.createElement('input'); swap.type = 'checkbox'; swap.setAttribute('aria-label', 'Swap conflicting binding');
  swapLabel.append(swap, ' Swap with the conflicting action');
  const cancel = document.createElement('button'); cancel.type = 'button'; cancel.textContent = 'Cancel key capture'; cancel.hidden = true;
  const reset = document.createElement('button'); reset.type = 'button'; reset.textContent = 'Reset controls';
  let capture: string | null = null;
  let lastBindings = '';
  let diagnostic = '';
  const endCapture = (): void => { capture = null; cancel.hidden = true; status.textContent = diagnostic; };
  cancel.addEventListener('click', endCapture);
  reset.addEventListener('click', () => { endCapture(); claim({ action: 'controls-reset' }); });
  const sensitivity = document.createElement('input'); sensitivity.type = 'number'; sensitivity.min = '0.1'; sensitivity.max = '10'; sensitivity.step = '0.1';
  sensitivity.setAttribute('aria-label', 'Mouse sensitivity multiplier');
  const sensitivityLabel = document.createElement('label'); sensitivityLabel.append('Mouse sensitivity (1 = default) ', sensitivity);
  const invert = document.createElement('input'); invert.type = 'checkbox'; invert.setAttribute('aria-label', 'Invert vertical look');
  const invertLabel = document.createElement('label'); invertLabel.append(invert, ' Invert vertical look');
  const applyLook = document.createElement('button'); applyLook.type = 'button'; applyLook.textContent = 'Apply look settings';
  applyLook.addEventListener('click', () => {
    const value = Number(sensitivity.value);
    if (Number.isFinite(value) && value > 0) claim({ action: 'controls-look', sensitivity: value, invert: invert.checked });
  });
  root.append(status, sensitivityLabel, invertLabel, applyLook, rows, swapLabel, cancel, reset);
  let lastLook = '';
  const submit = (key: string): void => {
    const item = capture;
    if (!item) return;
    endCapture(); claim({ action: 'controls-rebind', item, key, confirm: swap.checked });
  };
  const pointer = (event: MouseEvent): void => {
    if (!capture || event.target !== status) return;
    event.preventDefault(); event.stopPropagation();
    const key = ['Primary', 'Auxiliary', 'Secondary'][event.button];
    if (key) submit(key);
  };
  root.addEventListener('mousedown', pointer);
  return {
    update(value: ControlsProjection): void {
      diagnostic = value.diagnostic;
      const look = JSON.stringify([value.lookSensitivity, value.invertVertical]);
      if (look !== lastLook) { sensitivity.value = String(value.lookSensitivity ?? 1); invert.checked = value.invertVertical ?? true; lastLook = look; }
      if (!capture) status.textContent = diagnostic;
      const identity = JSON.stringify(value.bindings);
      if (identity === lastBindings) return;
      lastBindings = identity; rows.replaceChildren();
      for (const binding of value.bindings) {
        const row = document.createElement('p');
        const label = document.createElement('span'); label.textContent = `${binding.id}: ${binding.keys.join(', ')} `;
        const button = document.createElement('button'); button.type = 'button'; button.textContent = 'Rebind'; button.disabled = binding.fixed;
        button.addEventListener('click', () => {
          capture = binding.id; cancel.hidden = false;
          status.textContent = `Press a key or click here for ${binding.id}. Escape cancels.`;
        });
        row.append(label, button); rows.append(row);
      }
    },
    captureKey(event: KeyboardEvent): boolean {
      if (!capture) return false;
      event.preventDefault(); event.stopImmediatePropagation();
      if (!event.repeat) { if (event.code === 'Escape') endCapture(); else submit(event.code); }
      return true;
    },
    cancel: endCapture,
    dispose(): void { root.removeEventListener('mousedown', pointer); root.replaceChildren(); },
  };
}
