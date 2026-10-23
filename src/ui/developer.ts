import type { RustyApplicationUiContext } from '@rusty-engine/product-ui';
import { createLiveDebugHttpTransport, mountDeveloperShell, type DeveloperShellMount, type LiveDebugTransport } from '@rusty-engine/live-debug';

/**
 * Developer tools: the Engine's developer shell docked beside play, with Dagger's own panel. This is diagnostic UI
 * (`docs/game-ui.md` "Diagnostic UI"): it lives outside the game's shell and skin, carries no game state, and opens
 * only when the host runs with live-debug, which it asks the host before opening. The Dagger panel pauses and steps
 * the Engine's held time, flies the Engine's observer camera, and turns the ruleset's debug overlay sets on and off;
 * every control is one or two debug commands, the same ones the console takes.
 */
export interface DeveloperTools {
  isOpen(): boolean;
  /** Opens the shell when the host serves live-debug, or closes it; resolves once it has decided. */
  toggle(): Promise<void>;
  dispose(): void;
}

/** The key that opens and closes the developer tools in play. */
export const DEVELOPER_KEY = 'Backquote';
/** How far one fly step moves the observer, and how far one turn turns it. */
const FLY_STEP = 0.5;
const TURN_STEP = 10;
/** Overlay sets the Engine itself publishes, beside the ruleset's. */
const ENGINE_SETS = ['probe', 'inspect.targets'] as const;

export function mountDeveloperTools(root: HTMLElement, context: Pick<RustyApplicationUiContext, 'viewport'>,
  options: { readonly transport?: LiveDebugTransport; readonly onChange: () => void }): DeveloperTools {
  const transport = options.transport ?? createLiveDebugHttpTransport();
  const host = document.createElement('div');
  host.className = 'dagger-developer';
  host.dataset.rustyUiInteractive = '';
  root.append(host);
  const note = document.createElement('p');
  note.className = 'dagger-developer-note';
  note.setAttribute('role', 'status');
  note.hidden = true;
  host.append(note);
  let shell: DeveloperShellMount | null = null;

  const mountShell = (): DeveloperShellMount => mountDeveloperShell(host, {
    presentation: 'tabs',
    side: 'right',
    transport,
    onClose: options.onChange,
    panels: [
      { kind: 'product', title: 'Dagger', mount: element => mountDaggerPanel(element, transport) },
      { kind: 'console' },
      // The pane lists the product's inspection targets (player, dialogue, door) in its own subject choice and frames
      // the chosen one with its default framings; it opens on the player.
      { kind: 'view', title: 'View', options: { viewport: context.viewport, transport, subject: 'target player' } },
      { kind: 'scene' },
      { kind: 'metrics' },
    ],
  });

  return {
    isOpen: () => shell?.isOpen === true,
    async toggle() {
      if (shell?.isOpen) { shell.close(); return; }
      // The tools need the host's live-debug: without it there is nothing to talk to, so nothing opens. A host
      // without it answers that its catalog is unavailable; one that cannot be reached at all refuses.
      const refusal = await transport.catalog()
        .then(catalog => catalog.available ? null : 'the host serves no live-debug catalog')
        .catch((error: unknown) => error instanceof Error ? error.message : String(error));
      if (refusal !== null) {
        note.textContent = `Developer tools need the host started with --live-debug (${refusal}).`;
        note.hidden = false;
        return;
      }
      note.hidden = true;
      shell ??= mountShell();
      await shell.open();
      options.onChange();
    },
    dispose() { shell?.dispose(); shell = null; host.remove(); },
  };
}

/** The Dagger panel: time, the observer camera and the overlay sets. Each control sends debug commands. */
function mountDaggerPanel(element: HTMLElement, transport: LiveDebugTransport): { dispose(): void } {
  element.classList.add('dagger-developer-panel');
  const status = document.createElement('output');
  status.className = 'dagger-developer-status';
  const run = async (...commands: string[]): Promise<string | null> => {
    let last: string | null = null;
    for (const command of commands) {
      const result = await transport.execute(command);
      if (!result.succeeded) { status.textContent = `${command}: ${result.message}`; return null; }
      last = result.message;
    }
    return last;
  };
  const section = (title: string): HTMLElement => {
    const block = document.createElement('section');
    const heading = document.createElement('h3');
    heading.textContent = title;
    block.append(heading);
    element.append(block);
    return block;
  };
  const control = (parent: HTMLElement, label: string, onClick: () => void): HTMLButtonElement => {
    const button = document.createElement('button');
    button.type = 'button';
    button.textContent = label;
    button.addEventListener('click', onClick);
    parent.append(button);
    return button;
  };

  // Time: the Engine's held time, so the world stands still and steps by admitted updates; never a second clock.
  const time = section('Time');
  control(time, 'Pause', () => void run('engine.time.mode manual').then(answer => { if (answer !== null) status.textContent = 'Paused.'; }));
  control(time, 'Resume', () => void run('engine.time.mode realtime').then(answer => { if (answer !== null) status.textContent = 'Running.'; }));
  control(time, 'Step 0.1 s', () => void run('engine.time.advance 100').then(answer => { if (answer !== null) status.textContent = answer; }));
  control(time, 'Step 1 s', () => void run('engine.time.advance 1000').then(answer => { if (answer !== null) status.textContent = answer; }));

  // Camera: the Engine's observer, started at the player's eye and flown from here; the player never moves.
  const camera = section('Free camera');
  let pose: { x: number; y: number; z: number; yaw: number; pitch: number } | null = null;
  const place = (): void => { if (pose) void run(`engine.renderer.camera ${pose.x} ${pose.y} ${pose.z} ${pose.yaw} ${pose.pitch}`); };
  const fly = (forward: number, right: number, up: number, turn = 0, look = 0): void => {
    if (!pose) return;
    const yaw = pose.yaw * Math.PI / 180;
    pose = { x: pose.x + Math.sin(yaw) * forward + Math.cos(yaw) * right, y: pose.y + up, z: pose.z - Math.cos(yaw) * forward + Math.sin(yaw) * right,
      yaw: pose.yaw + turn, pitch: Math.max(-89, Math.min(89, pose.pitch + look)) };
    place();
  };
  const freeToggle = control(camera, 'Free camera: off', () => void (async () => {
    if (pose) { pose = null; await run('engine.renderer.camera none'); freeToggle.textContent = 'Free camera: off'; return; }
    const observed = await run('playtest.observe');
    if (observed === null) return;
    const player = JSON.parse(observed).player as { viewpoint: { x: number; y: number; z: number }; yawDegrees: number; pitchDegrees: number };
    pose = { ...player.viewpoint, yaw: player.yawDegrees, pitch: player.pitchDegrees };
    place();
    freeToggle.textContent = 'Free camera: on';
  })());
  const pad = document.createElement('div');
  pad.className = 'dagger-developer-fly';
  for (const [label, move] of [
    ['Forward (W)', () => fly(FLY_STEP, 0, 0)], ['Back (S)', () => fly(-FLY_STEP, 0, 0)],
    ['Left (A)', () => fly(0, -FLY_STEP, 0)], ['Right (D)', () => fly(0, FLY_STEP, 0)],
    ['Up (R)', () => fly(0, 0, FLY_STEP)], ['Down (F)', () => fly(0, 0, -FLY_STEP)],
    ['Turn left (Q)', () => fly(0, 0, 0, -TURN_STEP)], ['Turn right (E)', () => fly(0, 0, 0, TURN_STEP)],
    ['Look up (T)', () => fly(0, 0, 0, 0, TURN_STEP)], ['Look down (G)', () => fly(0, 0, 0, 0, -TURN_STEP)],
  ] as const) control(pad, label, move);
  camera.append(pad);
  const keys: Readonly<Record<string, () => void>> = {
    KeyW: () => fly(FLY_STEP, 0, 0), KeyS: () => fly(-FLY_STEP, 0, 0), KeyA: () => fly(0, -FLY_STEP, 0), KeyD: () => fly(0, FLY_STEP, 0),
    KeyR: () => fly(0, 0, FLY_STEP), KeyF: () => fly(0, 0, -FLY_STEP), KeyQ: () => fly(0, 0, 0, -TURN_STEP), KeyE: () => fly(0, 0, 0, TURN_STEP),
    KeyT: () => fly(0, 0, 0, 0, TURN_STEP), KeyG: () => fly(0, 0, 0, 0, -TURN_STEP),
  };
  const onKey = (event: KeyboardEvent): void => {
    if (!pose || (event.target instanceof Element && event.target.matches('input:not([type="checkbox"]), textarea'))) return;
    const action = keys[event.code];
    if (action) { event.preventDefault(); action(); }
  };
  element.addEventListener('keydown', onKey);

  // Overlays: the ruleset's sets are built only while on; the Engine draws whichever are shown.
  const overlays = section('Overlays');
  const list = document.createElement('div');
  list.className = 'dagger-developer-overlays';
  overlays.append(list);
  const toggle = (name: string, ruleset: boolean, on: boolean): Promise<string | null> => {
    const engine = `engine.inspect.overlay ${on ? 'show' : 'hide'} set ${name}`;
    if (name === 'inspect.targets' && on) return run('inspect.targets', engine);
    return ruleset ? run(on ? `world.overlay-on ${name}` : `world.overlay-off ${name}`, engine) : run(engine);
  };
  const box = (name: string, ruleset: boolean, checked: boolean): void => {
    const label = document.createElement('label');
    const input = document.createElement('input');
    input.type = 'checkbox';
    input.checked = checked;
    input.dataset.overlay = name;
    input.addEventListener('change', () => void toggle(name, ruleset, input.checked).then(answer => {
      if (answer === null) input.checked = !input.checked;
    }));
    label.append(input, ` ${name}`);
    list.append(label);
  };
  void run('world.overlays').then(answer => {
    if (answer === null) return;
    for (const set of (JSON.parse(answer) as { sets: { name: string; on: boolean }[] }).sets) box(set.name, true, set.on);
    for (const name of ENGINE_SETS) box(name, false, false);
  });
  element.append(status);
  return {
    dispose() {
      element.removeEventListener('keydown', onKey);
      // A flown observer is the Engine's state; closing the tools gives the player's camera back.
      if (pose) void transport.execute('engine.renderer.camera none');
      element.replaceChildren();
    },
  };
}
