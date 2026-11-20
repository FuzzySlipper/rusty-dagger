import type { Send } from './host.js';
import type { DaggerHud } from './projection.js';

/** Whether a binding's press acts in the world (the combat bindings, and recasting), which a surface holding the cursor keeps from play. */
const actsInWorld = (binding: { readonly id: string; readonly category: string }): boolean => binding.category === 'combat' || binding.id === 'recast';
/** The DOM numbers mouse buttons 0 primary, 1 middle and 2 secondary; the Engine names them. */
const POINTER_KEYS = ['Primary', 'Middle', 'Secondary'];

/**
 * The UI's own claim on input, told to the developer's input readout. While a DOM surface holds the cursor the Engine
 * keeps every key and click from play, so a world act's binding pressed then (an attack click on the view behind the
 * developer tools, a kick key over the menu) reaches no rule and nothing says so. While the readout is on, such a
 * press is reported as an `input-claimed` action naming the binding and the surface; the ruleset words and shows it.
 * A press on a control of the surface itself, or typing in a field, is the surface's own and is not reported.
 *
 * @param claim The surface holding the cursor now, in words (`menu`, `developer tools`), or null in play.
 */
export function mountInputClaims(send: Send, claim: () => string | null): { update(value: DaggerHud): void; dispose(): void } {
  let latest: DaggerHud | null = null;
  const report = (key: string, event: Event, own: string): void => {
    const surface = latest?.readout ? claim() : null;
    // The Engine's renderer canvas is focusable, but it is the view behind the surface, not one of its controls.
    const control = event.target instanceof Element ? event.target.closest(own) : null;
    if (surface === null || (control !== null && !control.matches('[data-rusty-application-renderer]'))) return;
    const binding = latest?.controls?.bindings.find(item => actsInWorld(item) && item.keys.includes(key));
    if (binding) send({ action: 'input-claimed', item: binding.id, key: surface });
  };
  const onKey = (event: KeyboardEvent): void => { if (!event.repeat) report(event.code, event, 'input, textarea, select, [contenteditable="true"]'); };
  const onPointer = (event: PointerEvent): void => {
    const key = POINTER_KEYS[event.button];
    if (key) report(key, event, 'button, a, input, select, textarea, label, [role], [tabindex], dialog, .dagger-developer');
  };
  document.addEventListener('keydown', onKey, true);
  document.addEventListener('pointerdown', onPointer, true);
  return {
    update(value) { latest = value; },
    dispose() {
      document.removeEventListener('keydown', onKey, true);
      document.removeEventListener('pointerdown', onPointer, true);
    },
  };
}
