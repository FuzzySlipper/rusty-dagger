/**
 * The cycler: a choice drawn as ‹ value › — the previous and next buttons step through the published
 * options and the value between them names the current one. It replaces a native `<select>` where a
 * pad must choose: both buttons take focus, so directions reach them and accept steps the choice,
 * while the mouse clicks them and the keyboard presses them with Enter.
 *
 * Unavailable options are skipped (their restriction is the ruleset's reason, shown when the current
 * one is unavailable). The cycler only steps; the owner sends the change.
 */
export interface CyclerOption { readonly id: string; readonly label: string; readonly available?: boolean; readonly restriction?: string | null }

export interface Cycler {
  readonly element: HTMLElement;
  value(): string;
  /** Step to an option as if chosen (for a face preview kept beside it, say); sends nothing. */
  set(id: string): void;
}

export function createCycler(label: string, options: readonly CyclerOption[], current: string, onChange: (id: string) => void): Cycler {
  const element = document.createElement('div');
  element.className = 'dagger-cycler';
  element.setAttribute('role', 'group');
  element.setAttribute('aria-label', label);
  element.dataset.cycler = label;
  const previous = document.createElement('button');
  previous.type = 'button';
  previous.className = 'dagger-cycler-step';
  previous.textContent = '‹';
  previous.setAttribute('aria-label', `Previous ${label.toLowerCase()}`);
  const next = document.createElement('button');
  next.type = 'button';
  next.className = 'dagger-cycler-step';
  next.textContent = '›';
  next.setAttribute('aria-label', `Next ${label.toLowerCase()}`);
  const shown = document.createElement('output');
  shown.className = 'dagger-cycler-value';
  shown.setAttribute('aria-live', 'polite');
  element.append(previous, shown, next);

  let index = Math.max(0, options.findIndex(option => option.id === current));
  const draw = (): void => {
    const option = options[index];
    shown.textContent = option ? option.label : '';
    shown.title = option?.restriction ?? '';
    element.dataset.value = option?.id ?? '';
    const choices = options.filter(candidate => candidate.available !== false).length;
    previous.disabled = next.disabled = choices <= 1 && option?.available !== false;
  };
  const step = (direction: -1 | 1): void => {
    if (options.length === 0) return;
    let at = index;
    for (let tries = 0; tries < options.length; tries++) {
      at = (at + direction + options.length) % options.length;
      if (options[at]!.available !== false) break;
    }
    if (at === index) return;
    index = at;
    draw();
    onChange(options[index]!.id);
  };
  previous.addEventListener('click', () => step(-1));
  next.addEventListener('click', () => step(1));
  draw();
  return {
    element,
    value: () => options[index]?.id ?? '',
    set(id) {
      const at = options.findIndex(option => option.id === id);
      if (at >= 0) { index = at; draw(); }
    },
  };
}
