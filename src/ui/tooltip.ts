/**
 * Drawn tooltips. The desktop window never shows a `title` tooltip, so an element that explains
 * itself carries `data-tooltip` and this draws it on pointer hover and on keyboard or controller
 * focus alike. The text is also the element's accessible name or description; the tooltip only
 * shows it.
 *
 * The tooltip is placed inside the open `<dialog>` that holds its element, because a modal dialog
 * sits in the top layer and anything drawn outside it would show beneath.
 */
export function mountTooltips(root: HTMLElement): { dispose(): void } {
  const tip = document.createElement('div');
  tip.className = 'dagger-tooltip';
  tip.setAttribute('role', 'tooltip');
  tip.hidden = true;
  let shownFor: Element | null = null;

  const show = (target: Element): void => {
    const text = target.getAttribute('data-tooltip');
    if (!text) return;
    shownFor = target;
    tip.textContent = text;
    (target.closest('dialog[open]') ?? root).append(tip);
    tip.hidden = false;
    const rect = target.getBoundingClientRect();
    const below = rect.bottom + 6;
    tip.style.left = `${Math.max(4, rect.left)}px`;
    tip.style.top = `${below}px`;
  };
  const hide = (target: Element | null): void => {
    if (target !== null && target !== shownFor) return;
    shownFor = null;
    tip.hidden = true;
  };
  const owner = (event: Event): Element | null => {
    const target = event.target instanceof Element ? event.target.closest('[data-tooltip]') : null;
    return target !== null && root.contains(target) ? target : null;
  };
  const onOver = (event: Event): void => { const target = owner(event); if (target !== null && target !== shownFor) show(target); };
  const onOut = (event: Event): void => {
    const target = owner(event);
    // Moving onto the element's own child keeps the tooltip; `contains(null)` is false.
    const next = (event as PointerEvent | FocusEvent).relatedTarget as Node | null;
    if (target !== null && !target.contains(next)) hide(target);
  };
  const onLeave = (): void => hide(null);
  root.addEventListener('pointerover', onOver);
  root.addEventListener('pointerout', onOut);
  root.addEventListener('focusin', onOver);
  root.addEventListener('focusout', onOut);
  window.addEventListener('blur', onLeave);
  return {
    dispose() {
      root.removeEventListener('pointerover', onOver);
      root.removeEventListener('pointerout', onOut);
      root.removeEventListener('focusin', onOver);
      root.removeEventListener('focusout', onOut);
      window.removeEventListener('blur', onLeave);
      tip.remove();
    },
  };
}
