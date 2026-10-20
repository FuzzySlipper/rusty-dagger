/**
 * The quantity stepper: a bounded amount with − and + beside it. The limits come from the
 * projection that offers the amount; the stepper only keeps the chosen value inside them.
 *
 * The amount is a number field, so the navigation owner already steps it with the controller's
 * left and right; the keyboard's left and right step it here, and the buttons step it by pointer.
 */
export interface Stepper {
  readonly element: HTMLElement;
  readonly input: HTMLInputElement;
  /** The chosen amount, always within the current limits. */
  value(): number;
  /** Set new limits, keeping the chosen amount when it still fits, else `initial` (the minimum by default). */
  limit(minimum: number, maximum: number, initial?: number): void;
}

export function createStepper(label: string): Stepper {
  const element = document.createElement('div');
  element.className = 'dagger-stepper';
  const down = document.createElement('button');
  down.type = 'button';
  down.className = 'dagger-stepper-step';
  down.dataset.stepper = 'down';
  down.textContent = '−';
  down.setAttribute('aria-label', `Fewer: ${label}`);
  const input = document.createElement('input');
  input.type = 'number';
  input.step = '1';
  input.inputMode = 'numeric';
  input.setAttribute('aria-label', label);
  const up = document.createElement('button');
  up.type = 'button';
  up.className = 'dagger-stepper-step';
  up.dataset.stepper = 'up';
  up.textContent = '+';
  up.setAttribute('aria-label', `More: ${label}`);
  element.append(down, input, up);

  let minimum = 1;
  let maximum = 1;
  const clamp = (amount: number): number => Math.min(maximum, Math.max(minimum, Number.isFinite(amount) ? Math.round(amount) : minimum));
  const set = (amount: number): void => {
    const next = String(clamp(amount));
    if (input.value !== next) {
      input.value = next;
      input.dispatchEvent(new Event('input', { bubbles: true }));
    }
    down.disabled = input.disabled || clamp(amount) <= minimum;
    up.disabled = input.disabled || clamp(amount) >= maximum;
  };
  down.addEventListener('click', () => set(Number(input.value) - 1));
  up.addEventListener('click', () => set(Number(input.value) + 1));
  input.addEventListener('change', () => set(Number(input.value)));
  input.addEventListener('keydown', event => {
    if (event.code !== 'ArrowLeft' && event.code !== 'ArrowRight') return;
    event.preventDefault();
    set(Number(input.value) + (event.code === 'ArrowLeft' ? -1 : 1));
  });

  return {
    element,
    input,
    value: () => clamp(Number(input.value)),
    limit(low, high, initial) {
      minimum = Math.min(low, high);
      maximum = Math.max(low, high);
      input.min = String(minimum);
      input.max = String(maximum);
      input.disabled = minimum === maximum;
      const current = Number(input.value);
      set(input.value !== '' && current >= minimum && current <= maximum ? current : initial ?? minimum);
    },
  };
}
