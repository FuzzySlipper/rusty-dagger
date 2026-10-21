import { redraw } from './redraw.js';
import { createStepper } from './stepper.js';
import type { UiScaleRange } from './scale.js';

export interface ControlsProjection {
  readonly diagnostic: string;
  readonly lookSensitivity?: number;
  readonly invertVertical?: boolean;
  /** The player's chosen interface scale, or null to follow the window. */
  readonly uiScale?: number | null;
  readonly uiScaleRange?: UiScaleRange;
  readonly bindings: readonly ControlBinding[];
  /** The categories in the order the screen groups the bindings, with their headings. */
  readonly categories?: readonly { readonly id: string; readonly label: string }[];
  /** Each sound bus's level, from 0 (silent) to 1 (full), and the step a level moves in. */
  readonly volumes?: readonly { readonly id: string; readonly label: string; readonly level: number }[];
  readonly volumeStep?: number;
}

export interface ControlBinding {
  readonly id: string;
  readonly category: string;
  /** The action in the player's words. */
  readonly label?: string;
  readonly keys: readonly string[];
  readonly fixed: boolean;
  /** Why the keys cannot change, when they cannot. */
  readonly fixedReason?: string | null;
  /** The controller buttons the action answers to, by their standard-layout numbers. */
  readonly buttons?: readonly number[];
  /** Why the action takes no controller button, when it does not. */
  readonly buttonFixedReason?: string | null;
}
export interface ControlAction { readonly action: string; readonly item?: string; readonly key?: string; readonly amount?: number; readonly confirm?: boolean; readonly sensitivity?: number; readonly invert?: boolean; readonly scale?: number }

/** The standard-layout controller buttons by number, as the player reads them. */
const BUTTON_NAMES = ['A', 'B', 'X', 'Y', 'Left bumper', 'Right bumper', 'Left trigger', 'Right trigger', 'View', 'Menu',
  'Left stick press', 'Right stick press', 'D-pad up', 'D-pad down', 'D-pad left', 'D-pad right'];
/** The button that cancels a controller capture: it always opens the menu, so it is never a binding. */
const CANCEL_BUTTON = 9;

export function buttonLabel(button: number): string {
  return BUTTON_NAMES[button] ?? `Button ${button}`;
}

/** Only captures the player's proposed key. C# validates conflicts and owns the active map. */
export function mountControls(root: HTMLElement, claim: (action: ControlAction) => void) {
  const status = document.createElement('p'); status.setAttribute('role', 'status');
  const rows = document.createElement('div');
  const swapLabel = document.createElement('label');
  const swap = document.createElement('input'); swap.type = 'checkbox'; swap.setAttribute('aria-label', 'Swap conflicting binding');
  swapLabel.append(swap, ' Swap with the conflicting action');
  const cancel = document.createElement('button'); cancel.type = 'button'; cancel.textContent = 'Cancel key capture'; cancel.hidden = true;
  const reset = document.createElement('button'); reset.type = 'button'; reset.textContent = 'Reset controls';
  let capture: { readonly action: string; readonly label: string; readonly kind: 'key' | 'button' } | null = null;
  // While a key is being captured, a click on this pad binds that mouse button.
  const mousePad = document.createElement('button'); mousePad.type = 'button'; mousePad.className = 'dagger-controls-mouse-pad';
  mousePad.textContent = 'Or click here with the mouse button to use'; mousePad.hidden = true;
  let lastBindings = '';
  let diagnostic = '';
  const endCapture = (): void => { capture = null; cancel.hidden = true; mousePad.hidden = true; status.textContent = diagnostic; };
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
  // Interface scale: a bounded stepper whose limits and steps the ruleset publishes, plus automatic.
  // The choice is a player preference the ruleset persists; scale.ts applies it.
  const scaleGroup = document.createElement('div'); scaleGroup.className = 'dagger-controls-scale'; scaleGroup.setAttribute('role', 'group');
  scaleGroup.setAttribute('aria-label', 'Interface scale');
  const scaleCaption = document.createElement('span'); scaleCaption.textContent = 'Interface scale ';
  const smaller = document.createElement('button'); smaller.type = 'button'; smaller.textContent = '−'; smaller.setAttribute('aria-label', 'Smaller interface');
  const scaleValue = document.createElement('output'); scaleValue.setAttribute('aria-live', 'polite');
  const larger = document.createElement('button'); larger.type = 'button'; larger.textContent = '+'; larger.setAttribute('aria-label', 'Larger interface');
  const automatic = document.createElement('button'); automatic.type = 'button'; automatic.textContent = 'Automatic';
  scaleGroup.append(scaleCaption, smaller, scaleValue, larger, automatic);
  let scale: { chosen: number | null; range: UiScaleRange; effective: number } | null = null;
  const stepScale = (direction: number): void => {
    if (scale === null) return;
    const { range } = scale;
    const next = Math.round(((scale.chosen ?? scale.effective) + direction * range.step) / range.step) * range.step;
    if (next >= range.minimum && next <= range.maximum) claim({ action: 'controls-ui-scale', scale: next });
  };
  smaller.addEventListener('click', () => stepScale(-1));
  larger.addEventListener('click', () => stepScale(1));
  automatic.addEventListener('click', () => { if (scale?.chosen !== null) claim({ action: 'controls-ui-scale' }); });
  const paintScale = (): void => {
    if (scale === null) { scaleGroup.hidden = true; return; }
    scaleGroup.hidden = false;
    const shown = scale.chosen ?? scale.effective;
    scaleValue.textContent = scale.chosen === null ? `Automatic (${shown}×)` : `${shown}×`;
    smaller.disabled = shown - scale.range.step < scale.range.minimum - 1e-6;
    larger.disabled = shown + scale.range.step > scale.range.maximum + 1e-6;
    automatic.setAttribute('aria-pressed', String(scale.chosen === null));
  };
  rows.className = 'dagger-controls-bindings';
  // Sound: a stepper per bus, counting steps of the ruleset's volume step, with the level as a percentage beside it.
  const sound = document.createElement('section'); sound.className = 'dagger-controls-sound'; sound.setAttribute('aria-label', 'Sound');
  let lastVolumes = '';
  const drawSound = (value: ControlsProjection): void => {
    const step = value.volumeStep ?? 0;
    const volumes = value.volumes ?? [];
    const identity = JSON.stringify([volumes, step]);
    if (identity === lastVolumes) return;
    lastVolumes = identity;
    redraw(sound, identity, () => {
      sound.hidden = volumes.length === 0 || step <= 0;
      if (sound.hidden) { sound.replaceChildren(); return; }
      const heading = document.createElement('h3'); heading.textContent = 'Sound';
      sound.replaceChildren(heading, ...volumes.map(bus => {
        const row = document.createElement('div'); row.className = 'dagger-controls-volume'; row.dataset.volume = bus.id;
        const name = document.createElement('span'); name.textContent = bus.label;
        const steps = Math.round(1 / step);
        const stepper = createStepper(`${bus.label} volume`);
        stepper.limit(0, steps, Math.round(bus.level / step));
        const shown = document.createElement('output'); const paint = (): void => { shown.textContent = `${Math.round(stepper.value() * step * 100)}%`; };
        paint();
        stepper.input.addEventListener('input', () => {
          paint();
          const level = Math.round(stepper.value() * step * 1000) / 1000;
          if (Math.abs(level - bus.level) > 1e-6) claim({ action: 'controls-volume', item: bus.id, scale: level });
        });
        row.append(name, stepper.element, shown);
        return row;
      }));
    });
  };
  root.append(status, cancel, mousePad, scaleGroup, sensitivityLabel, invertLabel, applyLook, sound, swapLabel, rows, reset);
  let lastLook = '';
  const submit = (key: string): void => {
    if (capture?.kind !== 'key') return;
    const item = capture.action;
    endCapture(); claim({ action: 'controls-rebind', item, key, confirm: swap.checked });
  };
  // The DOM numbers mouse buttons 0 primary, 1 middle and 2 secondary; the Engine names them.
  const pointer = (event: MouseEvent): void => {
    if (capture?.kind !== 'key' || event.target !== mousePad) return;
    event.preventDefault(); event.stopPropagation();
    const key = ['Primary', 'Middle', 'Secondary'][event.button];
    if (key) submit(key);
  };
  mousePad.addEventListener('mousedown', pointer);
  mousePad.addEventListener('contextmenu', event => event.preventDefault());
  const begin = (binding: ControlBinding, kind: 'key' | 'button'): void => {
    const label = binding.label ?? binding.id;
    capture = { action: binding.id, label, kind };
    cancel.hidden = false;
    mousePad.hidden = kind !== 'key';
    status.textContent = kind === 'key'
      ? `Press a key for ${label}. Escape cancels.`
      : `Press a controller button for ${label}. Menu cancels.`;
    (kind === 'key' ? mousePad : cancel).focus({ preventScroll: false });
  };
  return {
    update(value: ControlsProjection, effectiveScale = 1): void {
      diagnostic = value.diagnostic;
      scale = value.uiScaleRange ? { chosen: value.uiScale ?? null, range: value.uiScaleRange, effective: effectiveScale } : null;
      paintScale();
      drawSound(value);
      const look = JSON.stringify([value.lookSensitivity, value.invertVertical]);
      if (look !== lastLook) { sensitivity.value = String(value.lookSensitivity ?? 1); invert.checked = value.invertVertical ?? true; lastLook = look; }
      if (!capture) status.textContent = diagnostic;
      const identity = JSON.stringify([value.bindings, value.categories]);
      if (identity === lastBindings) return;
      lastBindings = identity;
      redraw(rows, identity, () => drawBindings(rows, value, begin));
    },
    /** A controller press while a button is being captured binds it; the menu button cancels. */
    captureButton(button: string): boolean {
      if (capture?.kind !== 'button') return false;
      const number = Number(/^button-(\d+)$/.exec(button)?.[1]);
      if (!Number.isInteger(number)) return true;
      const item = capture.action;
      endCapture();
      if (number !== CANCEL_BUTTON) claim({ action: 'controls-rebind-button', item, amount: number, confirm: swap.checked });
      return true;
    },
    captureKey(event: KeyboardEvent): boolean {
      if (!capture) return false;
      event.preventDefault(); event.stopImmediatePropagation();
      if (!event.repeat) {
        if (event.code === 'Escape') endCapture();
        else if (capture.kind === 'key') submit(event.code);
      }
      return true;
    },
    cancel: endCapture,
    dispose(): void { mousePad.removeEventListener('mousedown', pointer); root.replaceChildren(); },
  };
}

/**
 * The bindings under the ruleset's categories: each action in the player's words with its keys and controller
 * buttons, a Change for each that can change, and the reason beside one that cannot.
 */
function drawBindings(root: HTMLElement, value: ControlsProjection, begin: (binding: ControlBinding, kind: 'key' | 'button') => void): void {
  const categories = value.categories ?? [...new Set(value.bindings.map(binding => binding.category))].map(id => ({ id, label: id }));
  root.replaceChildren(...categories.flatMap(category => {
    const members = value.bindings.filter(binding => binding.category === category.id);
    if (members.length === 0) return [];
    const section = document.createElement('section');
    section.className = 'dagger-controls-category';
    section.dataset.controlsCategory = category.id;
    const heading = document.createElement('h3'); heading.textContent = category.label;
    section.append(heading, ...members.map(binding => {
      const row = document.createElement('div');
      row.className = 'dagger-controls-row';
      row.dataset.binding = binding.id;
      const name = document.createElement('span'); name.className = 'dagger-controls-name'; name.textContent = binding.label ?? binding.id;
      const keys = document.createElement('span'); keys.className = 'dagger-controls-keys';
      keys.append(...binding.keys.map(key => { const chip = document.createElement('kbd'); chip.textContent = keyLabel(key); return chip; }));
      const changeKey = document.createElement('button'); changeKey.type = 'button'; changeKey.textContent = 'Change key';
      changeKey.dataset.rebindKey = binding.id; changeKey.setAttribute('aria-label', `Change the key for ${binding.label ?? binding.id}`);
      changeKey.disabled = binding.fixed;
      changeKey.addEventListener('click', () => begin(binding, 'key'));
      const pad = document.createElement('span'); pad.className = 'dagger-controls-buttons';
      const buttons = binding.buttons ?? [];
      pad.append(...(buttons.length ? buttons : []).map(button => { const chip = document.createElement('kbd'); chip.className = 'is-pad'; chip.textContent = buttonLabel(button); return chip; }));
      const changeButton = document.createElement('button'); changeButton.type = 'button'; changeButton.textContent = 'Change button';
      changeButton.dataset.rebindButton = binding.id; changeButton.setAttribute('aria-label', `Change the controller button for ${binding.label ?? binding.id}`);
      changeButton.disabled = binding.buttonFixedReason != null;
      changeButton.addEventListener('click', () => begin(binding, 'button'));
      row.append(name, keys, changeKey, pad, changeButton);
      const reasons = [binding.fixedReason, binding.buttonFixedReason].filter((reason): reason is string => !!reason);
      if (reasons.length) { const note = document.createElement('small'); note.className = 'dagger-controls-reason'; note.textContent = reasons.join(' '); row.append(note); }
      return row;
    }));
    return [section];
  }));
}

/** A bound key as the player reads it: `KeyQ` is Q, `Digit1` is 1 and `ShiftLeft` is Left Shift. */
export function keyLabel(code: string): string {
  if (/^Key[A-Z]$/.test(code)) return code.slice(3);
  if (/^Digit\d$/.test(code)) return code.slice(5);
  if (code === 'Primary') return 'Left click';
  if (code === 'Secondary') return 'Right click';
  if (code === 'Middle') return 'Middle click';
  const side = /^(Shift|Control|Alt|Meta)(Left|Right)$/.exec(code);
  if (side) return `${side[2]} ${side[1] === 'Control' ? 'Ctrl' : side[1]}`;
  return code;
}
