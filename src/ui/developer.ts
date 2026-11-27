import type { RustyApplicationUiContext } from '@rusty-engine/product-ui';
import { createLiveDebugHttpTransport, mountDeveloperShell, type DeveloperShellMount, type LiveDebugTransport } from '@rusty-engine/live-debug';
import { mountBoardPanel } from './developer-board.js';

/**
 * Developer tools: the Engine's developer shell docked beside play, with Dagger's own panel and the first-person
 * workbench (the Engine tunables panel over the ruleset's held-art workbench, `held.motion*`). This is diagnostic UI
 * (`src/ui/AGENTS.md`, "Ownership"): it lives outside the game's shell and skin, carries no game state, and opens
 * only when the host runs with live-debug, which it asks the host before opening. The Dagger panel pauses and steps
 * the Engine's held time, flies the Engine's observer camera, shows the input readout, and turns the ruleset's debug overlay sets on and off;
 * every control is one or two debug commands, the same ones the console takes. The Commands panel is the console's help:
 * every command the host's catalogue serves, with its parameters and full description, filtered as you type. The Social
 * and Board panels read the live social layer and the political board (`developer-board.ts`).
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
    // However the shell closes, the motion viewer returns the hands to what they wield.
    onClose: () => {
      void transport.execute('held.motion.off').catch(() => {});
      options.onChange();
    },
    panels: [
      { kind: 'product', title: 'Dagger', mount: element => mountDaggerPanel(element, transport) },
      { kind: 'tunables', title: 'Workbench' },
      { kind: 'console' },
      { kind: 'product', title: 'Commands', mount: element => mountCommandHelp(element, transport) },
      { kind: 'product', title: 'Social', mount: element => mountSocialPanel(element, transport) },
      { kind: 'product', title: 'Board', mount: element => mountBoardPanel(element, transport) },
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

  // Collision: the Engine draws the Spatial sessions' retained collision around the observer (or the player's camera)
  // and lists what it drew; nothing is read while it is off.
  const collision = section('Collision');
  const drawCollision = document.createElement('input');
  drawCollision.type = 'checkbox';
  drawCollision.dataset.collision = '';
  const radius = document.createElement('input');
  radius.type = 'number';
  radius.min = '1';
  radius.max = '64';
  radius.value = '12';
  radius.setAttribute('aria-label', 'Collision radius in metres');
  const show = (): Promise<string | null> => run(drawCollision.checked
    ? `engine.inspect.overlay show collision ${Number(radius.value) || 12}` : 'engine.inspect.overlay hide collision');
  drawCollision.addEventListener('change', () => void show().then(answer => { if (answer === null) drawCollision.checked = !drawCollision.checked; }));
  radius.addEventListener('change', () => { if (drawCollision.checked) void show(); });
  // Collision outlives the panel, as the overlay sets do: it opens showing what the Engine draws now.
  void run('engine.inspect.overlay').then(answer => {
    const drawn = answer === null ? null : (JSON.parse(answer) as { collision?: { radius?: number } | null }).collision;
    if (drawn) { drawCollision.checked = true; radius.value = String(drawn.radius ?? 12); }
  });
  const toggleLabel = document.createElement('label');
  toggleLabel.append(drawCollision, ' draw collision within ', radius, ' m');
  collision.append(toggleLabel);
  control(collision, 'List colliders here', () => void run(`engine.inspect.collision ${Number(radius.value) || 12}`).then(answer => {
    if (answer === null) return;
    const read = JSON.parse(answer) as { lines: number; omitted: number;
      colliders: readonly { kind: string; content?: string; instance?: number | string; lines: number }[] };
    status.textContent = [`${read.colliders.length} colliders, ${read.lines} lines${read.omitted ? `, ${read.omitted} omitted` : ''}:`,
      ...read.colliders.map(collider => `${collider.kind} ${collider.content ?? `instance ${collider.instance ?? '?'}`} · ${collider.lines} lines`)].join('\n');
  }));

  // Input readout: a line at the bottom of the view with the player's attack, guard, cast and arms state and the
  // latest input refused or dropped. The ruleset keeps it with the run, off by default; the panel opens showing it.
  const readout = section('Input readout');
  const showReadout = document.createElement('input');
  showReadout.type = 'checkbox';
  showReadout.dataset.readout = '';
  showReadout.addEventListener('change', () => void run(showReadout.checked ? 'hud.readout-on' : 'hud.readout-off').then(answer => {
    if (answer === null) showReadout.checked = !showReadout.checked;
  }));
  void run('hud.readout').then(answer => { if (answer !== null) showReadout.checked = (JSON.parse(answer) as { on: boolean }).on; });
  const readoutLabel = document.createElement('label');
  readoutLabel.append(showReadout, ' show the player state and refused input');
  readout.append(readoutLabel);

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

/**
 * The Commands panel: every command in the host's live-debug catalogue (the product's `[DebugCommand]`s and the
 * Engine's own), with its parameters and full description, narrowed as you type to those whose name or description
 * holds every word typed. It reads the catalogue once, as the console's completion does, and runs nothing.
 */
function mountCommandHelp(element: HTMLElement, transport: LiveDebugTransport): { dispose(): void } {
  element.classList.add('dagger-developer-panel', 'dagger-developer-help');
  const filter = document.createElement('input');
  filter.type = 'search';
  filter.placeholder = 'Filter by name or words';
  filter.setAttribute('aria-label', 'Filter commands');
  const count = document.createElement('output');
  count.className = 'dagger-developer-status';
  const list = document.createElement('dl');
  list.className = 'dagger-developer-commands';
  list.tabIndex = 0;
  element.append(filter, count, list);
  const rows: { readonly text: string; readonly term: HTMLElement; readonly description: HTMLElement }[] = [];
  const show = (): void => {
    const words = filter.value.toLowerCase().split(/\s+/).filter(word => word.length > 0);
    let shown = 0;
    for (const row of rows) {
      const match = words.every(word => row.text.includes(word));
      row.term.hidden = !match;
      row.description.hidden = !match;
      if (match) shown++;
    }
    count.textContent = `${shown} of ${rows.length} commands`;
  };
  filter.addEventListener('input', show);
  void transport.catalog().then(catalog => {
    for (const command of [...catalog.commands].sort((a, b) => a.name.localeCompare(b.name))) {
      const term = document.createElement('dt');
      const name = document.createElement('code');
      name.textContent = command.name;
      term.append(name, ...command.parameters.map(parameter => ` ${parameter.name}:${parameter.type}`));
      const description = document.createElement('dd');
      description.textContent = command.description;
      list.append(term, description);
      rows.push({ text: `${command.name} ${command.description}`.toLowerCase(), term, description });
    }
    show();
  }, (error: unknown) => { count.textContent = `The catalogue did not answer: ${error instanceof Error ? error.message : String(error)}`; });
  return { dispose() { element.replaceChildren(); } };
}

interface SocialActorInspect {
  readonly durableId: number;
  readonly kind: string;
  readonly social?: {
    readonly displayName?: string | null;
    readonly familyId?: string | null;
    readonly householdLabel?: string | null;
    readonly scope?: string;
    readonly householdStanding?: number;
    readonly inBloodFeud?: boolean;
    readonly reaction?: {
      readonly factionId: number;
      readonly factionName?: string | null;
      readonly factionReputation: number;
      readonly personalReputation: number;
      readonly actorDisposition: number;
      readonly vouchBonus: number;
      readonly total: number;
      readonly householdStanding: number;
      readonly inBloodFeud: boolean;
    };
    readonly access?: {
      readonly prominence: string;
      readonly allowed: boolean;
      readonly requiredReach: number;
      readonly achievedReach: number;
      readonly skillSkip: number;
      readonly homeBonus: number;
      readonly hasDepositWaiver: boolean;
      readonly denialReason?: string | null;
    };
    readonly vouches?: readonly {
      readonly referrerDurableId: number;
      readonly referrerName?: string | null;
      readonly referrerFamilyId?: string | null;
      readonly kind: string;
      readonly issuedDay: number;
      readonly originSiteName?: string | null;
      readonly trusted?: boolean;
    }[];
    readonly ledger?: {
      readonly untouched: boolean;
      readonly met: boolean;
      readonly lastFavourDay?: number | null;
      readonly familyIdCleared: boolean;
      readonly personalDispositionDelta: number;
      readonly dirtySections: readonly string[];
      readonly witnessedCrimes: readonly string[];
    };
    readonly localKin?: readonly { readonly durableId: number; readonly name?: string | null }[];
  };
}

interface SocialSiteReadout {
  readonly day: number;
  readonly worldSocialSeed: number;
  readonly site?: { readonly region: number; readonly regionName: string; readonly index: number; readonly name: string } | null;
  readonly loadedPeople: readonly {
    readonly durableId: number;
    readonly name: string;
    readonly role: string;
    readonly familyId?: string | null;
    readonly scope: string;
    readonly reactionTotal?: number;
    readonly inBloodFeud?: boolean;
    readonly vouchCount: number;
    readonly ledgerUntouched: boolean;
  }[];
  readonly households: readonly {
    readonly familyId: string;
    readonly label?: string | null;
    readonly scope: string;
    readonly standingDelta: number;
    readonly inBloodFeud: boolean;
    readonly loadedMembers: readonly { readonly durableId: number; readonly name: string; readonly role: string }[];
  }[];
  readonly localOffices: readonly {
    readonly kind: string;
    readonly title: string;
    readonly displayName: string;
    readonly familyId?: string | null;
    readonly isVacant: boolean;
    readonly isOverridden: boolean;
  }[];
}

interface SocialRegionReadout {
  readonly day: number;
  readonly region?: number | null;
  readonly regionName?: string | null;
  readonly ruler?: {
    readonly displayName: string;
    readonly title?: string | null;
    readonly familyId?: string | null;
    readonly hasLivingHolder: boolean;
    readonly previousHolderName?: string | null;
  } | null;
  readonly greatHouses: readonly {
    readonly familyId: string;
    readonly label?: string | null;
    readonly arrivedStandingDelta: number;
    readonly inBloodFeud: boolean;
    readonly heldOffices: readonly {
      readonly siteName?: string;
      readonly title: string;
      readonly displayName: string;
      readonly isVacant: boolean;
      readonly isOverridden: boolean;
    }[];
  }[];
  readonly boardSeats: readonly {
    readonly seatId: string;
    readonly seatName: string;
    readonly displayName: string;
    readonly isVacant: boolean;
    readonly isOverridden: boolean;
  }[];
  readonly officeOverrides: readonly {
    readonly officeKey: string;
    readonly holderDisplayName?: string | null;
    readonly vacant: boolean;
    readonly cause: string;
    readonly changedDay: number;
  }[];
}

interface SocialPlayerReadout {
  readonly day: number;
  readonly actorLedgerCount: number;
  readonly activeRegion?: number | null;
  readonly activeRegionName?: string | null;
  readonly activeRegionReputation: number;
  readonly wantedInActiveRegion: boolean;
  readonly banishedInActiveRegion: boolean;
  readonly regionalReputations: readonly {
    readonly region: number;
    readonly regionName: string;
    readonly reputation: number;
    readonly wanted: boolean;
    readonly banished: boolean;
    readonly pendingChargeCount?: number;
    readonly hasActiveBounty?: boolean;
  }[];
  readonly factionStandings: readonly { readonly factionId: number; readonly name: string; readonly reputation: number }[];
  readonly affiliations: readonly { readonly faction: string; readonly rank: number; readonly reputation: number }[];
  readonly law: {
    readonly banishedRegions: readonly number[];
    readonly pendingChargeCount?: number;
    readonly pendingCharges?: readonly {
      readonly operationId: string;
      readonly crime: string;
      readonly region: number;
      readonly regionName?: string;
      readonly reported: boolean;
      readonly gameMinute: number;
    }[];
    readonly legalResponses?: readonly {
      readonly id: string;
      readonly region: number;
      readonly regionName?: string;
      readonly phase: string;
      readonly chargeCount: number;
      readonly fine?: number;
      readonly prisonDays?: number;
      readonly sentenceOutcome?: string;
    }[];
    readonly incidentCount: number;
    readonly reportedIncidentCount: number;
    readonly recentIncidents: readonly {
      readonly crime: string;
      readonly region: number;
      readonly reported: boolean;
      readonly disposition?: { readonly outcome: string; readonly resolvedMinute: number } | null;
      readonly gameMinute: number;
    }[];
  };
  readonly vouches: readonly {
    readonly targetDurableId: number;
    readonly targetName?: string | null;
    readonly referrerName?: string | null;
    readonly referrerFamilyId?: string | null;
    readonly kind: string;
    readonly issuedDay: number;
  }[];
}

interface WordSpreadReadout {
  readonly minute: number;
  readonly wavefronts: readonly {
    readonly eventId: string;
    readonly kind: string;
    readonly delta: number;
    readonly factionId?: number;
    readonly familyId?: string;
    readonly headline?: string;
    readonly intercepted: boolean;
    readonly statusAtActiveSite?: string;
    readonly remainingMinutes?: number;
  }[];
}

interface ConditionsReadout {
  readonly gateScrutiny?: string;
  readonly watchChallenge?: boolean;
  readonly conditions: readonly {
    readonly tokenId: string;
    readonly kind: string;
    readonly severity: number;
    readonly subjectTag?: string;
    readonly expiresDay?: number;
  }[];
}

interface DispatchesReadout {
  readonly dispatches: readonly {
    readonly dispatchId: string;
    readonly seatId: string;
    readonly actionKind: string;
    readonly status: string;
    readonly destinationRegion: number;
  }[];
}

/**
 * The Social panel: runtime view of the live social and political layers (actor reaction/access/vouches, active town
 * households and local offices, regional Great Houses and seat personas, word-spread/conditions/dispatches, and player
 * standing/law). Reads strictly through debug commands and owns no gameplay state.
 */
function mountSocialPanel(element: HTMLElement, transport: LiveDebugTransport): { dispose(): void } {
  element.classList.add('dagger-developer-panel', 'dagger-developer-social');
  let disposed = false;

  const toolbar = document.createElement('div');
  toolbar.className = 'dagger-developer-social-bar';
  const refreshButton = document.createElement('button');
  refreshButton.type = 'button';
  refreshButton.textContent = 'Refresh';
  const actorSelect = document.createElement('select');
  actorSelect.setAttribute('aria-label', 'Inspect actor');
  const inspectButton = document.createElement('button');
  inspectButton.type = 'button';
  inspectButton.textContent = 'Inspect';
  toolbar.append(refreshButton, actorSelect, inspectButton);

  const status = document.createElement('output');
  status.className = 'dagger-developer-status';

  const makeSection = (title: string, open: boolean, key: string): { details: HTMLDetailsElement; body: HTMLPreElement } => {
    const details = document.createElement('details');
    details.open = open;
    details.dataset.socialSection = key;
    const summary = document.createElement('summary');
    summary.textContent = title;
    const body = document.createElement('pre');
    body.className = 'dagger-developer-social-pre';
    details.append(summary, body);
    return { details, body };
  };

  const actorSection = makeSection('Actor (actor.inspect)', true, 'actor');
  const siteSection = makeSection('Town Households & Offices (social.site)', true, 'site');
  const regionSection = makeSection('Region: Great Houses & Seats (social.region)', false, 'region');
  const worldSection = makeSection('WordSpread, Conditions & Dispatches', false, 'world');
  const playerSection = makeSection('Player Standing & Law (social.player)', false, 'player');

  const bodyStack = document.createElement('div');
  bodyStack.className = 'dagger-developer-social-body';
  bodyStack.append(
    actorSection.details,
    siteSection.details,
    regionSection.details,
    worldSection.details,
    playerSection.details,
  );
  element.append(toolbar, status, bodyStack);

  const setActorOptions = (people: SocialSiteReadout['loadedPeople'], preserve: string): void => {
    actorSelect.replaceChildren();
    for (const [value, label] of [['target', 'Facing (target)'], ['nearest', 'Nearest (nearest)']] as const) {
      const opt = document.createElement('option');
      opt.value = value;
      opt.textContent = label;
      actorSelect.append(opt);
    }
    for (const person of people) {
      const opt = document.createElement('option');
      opt.value = String(person.durableId);
      const flags = [person.inBloodFeud ? 'FEUD' : '', person.familyId ?? ''].filter(Boolean).join(' · ');
      opt.textContent = flags ? `${person.name} (#${person.durableId}) [${flags}]` : `${person.name} (#${person.durableId})`;
      actorSelect.append(opt);
    }
    const valid = [...actorSelect.options].some(opt => opt.value === preserve);
    actorSelect.value = valid ? preserve : 'target';
  };
  setActorOptions([], 'target');

  const execJson = async <T>(command: string): Promise<{ ok: true; data: T } | { ok: false; error: string }> => {
    try {
      const result = await transport.execute(command);
      if (!result.succeeded) return { ok: false, error: result.message };
      return { ok: true, data: JSON.parse(result.message) as T };
    } catch (error: unknown) {
      return { ok: false, error: error instanceof Error ? error.message : String(error) };
    }
  };

  const fmtSigned = (value: number): string => (value > 0 ? `+${value}` : String(value));

  const renderActor = async (): Promise<void> => {
    const query = actorSelect.value || 'target';
    const res = await execJson<SocialActorInspect>(`actor.inspect ${query}`);
    if (disposed) return;
    if (!res.ok) { actorSection.body.textContent = res.error; return; }
    const data = res.data;
    const social = data.social;
    if (!social) { actorSection.body.textContent = `Actor #${data.durableId} (${data.kind}) — no social component.`; return; }
    const lines: string[] = [];
    lines.push(`${social.displayName ?? `Actor #${data.durableId}`} (#${data.durableId}, ${data.kind})`);
    lines.push(`Family: ${social.familyId ?? 'none'}${social.householdLabel ? ` — ${social.householdLabel}` : ''} [${social.scope ?? 'none'}]`);
    if (social.reaction) {
      const r = social.reaction;
      lines.push(
        `Reaction: ${fmtSigned(r.total)} (faction ${r.factionId}${r.factionName ? ` ${r.factionName}` : ''}: ${fmtSigned(r.factionReputation)}, personal: ${fmtSigned(r.personalReputation)}, disp: ${fmtSigned(r.actorDisposition)}, vouch: ${fmtSigned(r.vouchBonus)})`,
      );
      lines.push(`Household standing: ${fmtSigned(r.householdStanding)}${r.inBloodFeud ? ' [BLOOD FEUD]' : ''}`);
    } else if (social.inBloodFeud || (social.householdStanding ?? 0) !== 0) {
      lines.push(`Household standing: ${fmtSigned(social.householdStanding ?? 0)}${social.inBloodFeud ? ' [BLOOD FEUD]' : ''}`);
    }
    if (social.access) {
      const a = social.access;
      lines.push(
        `Access (${a.prominence}): ${a.allowed ? 'ALLOWED' : 'DENIED'} (reach ${a.achievedReach}/${a.requiredReach}, skillSkip ${a.skillSkip}, home ${a.homeBonus}, depositWaiver: ${a.hasDepositWaiver ? 'yes' : 'no'})`,
      );
      if (a.denialReason) lines.push(`  Reason: ${a.denialReason}`);
    }
    if (social.vouches && social.vouches.length > 0) {
      lines.push(`Vouches (${social.vouches.length}):`);
      for (const v of social.vouches) {
        lines.push(`  - ${v.kind} from ${v.referrerName ?? `#${v.referrerDurableId}`}${v.referrerFamilyId ? ` [${v.referrerFamilyId}]` : ''}${v.originSiteName ? ` (${v.originSiteName})` : ''} day ${v.issuedDay}${v.trusted === false ? ' [UNTRUSTED]' : ''}`);
      }
    } else {
      lines.push('Vouches: none');
    }
    if (social.ledger) {
      const l = social.ledger;
      lines.push(
        `Ledger: ${l.untouched ? 'untouched (0 bytes)' : `dirty [${l.dirtySections.join(', ')}]`} · met=${l.met} · disp=${fmtSigned(l.personalDispositionDelta)}${l.lastFavourDay != null ? ` · lastFavourDay=${l.lastFavourDay}` : ''}${l.familyIdCleared ? ' · familyIdCleared' : ''}`,
      );
      if (l.witnessedCrimes.length > 0) lines.push(`  Witnessed crimes: ${l.witnessedCrimes.join(', ')}`);
    }
    if (social.localKin && social.localKin.length > 0) {
      lines.push(`Loaded kin (${social.localKin.length}): ${social.localKin.map(k => `${k.name ?? `#${k.durableId}`} (#${k.durableId})`).join(', ')}`);
    }
    actorSection.body.textContent = lines.join('\n');
  };

  const refreshAll = async (): Promise<void> => {
    const selected = actorSelect.value || 'target';
    const [siteRes, regionRes, playerRes, condRes, spreadRes, dispRes] = await Promise.all([
      execJson<SocialSiteReadout>('social.site'),
      execJson<SocialRegionReadout>('social.region'),
      execJson<SocialPlayerReadout>('social.player'),
      execJson<ConditionsReadout>('world.conditions'),
      execJson<WordSpreadReadout>('world.word-spread'),
      execJson<DispatchesReadout>('world.dispatches'),
    ]);
    if (disposed) return;

    const summaryParts: string[] = [];
    if (siteRes.ok) {
      const s = siteRes.data;
      setActorOptions(s.loadedPeople, selected);
      summaryParts.push(s.site ? `${s.site.name} (${s.site.regionName})` : 'No active site');
      summaryParts.push(`Day ${s.day}`);
      summaryParts.push(`${s.loadedPeople.length} loaded`);

      const lines: string[] = [];
      lines.push(`Households (${s.households.length}):`);
      if (s.households.length === 0) lines.push('  (none)');
      for (const h of s.households) {
        const members = h.loadedMembers.length > 0
          ? h.loadedMembers.map(m => `${m.name} (#${m.durableId})`).join(', ')
          : '(unloaded / sparse standing)';
        lines.push(`  ${h.familyId}${h.label ? ` — ${h.label}` : ''} [${h.scope}] standing=${fmtSigned(h.standingDelta)}${h.inBloodFeud ? ' [BLOOD FEUD]' : ''}`);
        lines.push(`    Members: ${members}`);
      }
      lines.push(`Local Offices (${s.localOffices.length}):`);
      if (s.localOffices.length === 0) lines.push('  (none)');
      for (const o of s.localOffices) {
        lines.push(`  [${o.kind}] ${o.title}: ${o.isVacant ? 'VACANT' : o.displayName}${o.familyId ? ` [${o.familyId}]` : ''}${o.isOverridden ? ' *override*' : ''}`);
      }
      siteSection.body.textContent = lines.join('\n');
    } else {
      siteSection.body.textContent = siteRes.error;
    }

    if (regionRes.ok) {
      const r = regionRes.data;
      const lines: string[] = [];
      if (r.ruler) {
        lines.push(`Ruler: ${r.ruler.hasLivingHolder ? r.ruler.displayName : 'VACANT'}${r.ruler.title ? ` (${r.ruler.title})` : ''}${r.ruler.familyId ? ` [${r.ruler.familyId}]` : ''}`);
      } else {
        lines.push('Ruler: (none)');
      }
      lines.push(`Great Houses (${r.greatHouses.length}):`);
      for (const h of r.greatHouses) {
        lines.push(`  ${h.familyId}${h.label ? ` — ${h.label}` : ''} standing=${fmtSigned(h.arrivedStandingDelta)}${h.inBloodFeud ? ' [BLOOD FEUD]' : ''} · ${h.heldOffices.length} court offices`);
        for (const o of h.heldOffices) {
          lines.push(`    - ${o.siteName ?? o.title}: ${o.isVacant ? 'VACANT' : o.displayName}${o.isOverridden ? ' *override*' : ''}`);
        }
      }
      lines.push(`Office Overrides (${r.officeOverrides.length}):`);
      if (r.officeOverrides.length === 0) lines.push('  (none — all baseline)');
      for (const ov of r.officeOverrides) {
        lines.push(`  ${ov.officeKey}: ${ov.vacant ? 'VACANT' : (ov.holderDisplayName ?? '?')} (${ov.cause}, day ${ov.changedDay})`);
      }
      lines.push(`Board Seats (${r.boardSeats.length}):`);
      for (const seat of r.boardSeats) {
        lines.push(`  ${seat.seatId}: ${seat.isVacant ? 'VACANT' : seat.displayName}${seat.isOverridden ? ' *override*' : ''}`);
      }
      regionSection.body.textContent = lines.join('\n');
    } else {
      regionSection.body.textContent = regionRes.error;
    }

    const worldLines: string[] = [];
    if (condRes.ok) {
      const c = condRes.data;
      worldLines.push(`Gate scrutiny: ${c.gateScrutiny ?? 'n/a'}${c.watchChallenge ? ' [WATCH CHALLENGE]' : ''}`);
      worldLines.push(`Active Conditions (${c.conditions.length}):`);
      if (c.conditions.length === 0) worldLines.push('  (none)');
      for (const tok of c.conditions) {
        worldLines.push(`  ${tok.kind} sev=${tok.severity}${tok.subjectTag ? ` tag=${tok.subjectTag}` : ''}${tok.expiresDay != null ? ` exp=day ${tok.expiresDay}` : ''}`);
      }
    } else {
      worldLines.push(`Conditions error: ${condRes.error}`);
    }
    if (spreadRes.ok) {
      const sp = spreadRes.data;
      worldLines.push(`WordSpread Wavefronts (${sp.wavefronts.length}):`);
      if (sp.wavefronts.length === 0) worldLines.push('  (none)');
      for (const wf of sp.wavefronts) {
        const subject = wf.familyId ?? (wf.factionId != null ? `faction:${wf.factionId}` : wf.eventId);
        const eta = wf.remainingMinutes != null && wf.remainingMinutes > 0 ? ` (${wf.remainingMinutes}m left)` : '';
        worldLines.push(`  [${wf.kind}] ${subject} ${fmtSigned(wf.delta)} · ${wf.statusAtActiveSite ?? 'n/a'}${eta}${wf.intercepted ? ' [INTERCEPTED]' : ''}`);
      }
    } else {
      worldLines.push(`WordSpread error: ${spreadRes.error}`);
    }
    if (dispRes.ok) {
      worldLines.push(`Courier Dispatches (${dispRes.data.dispatches.length}):`);
      if (dispRes.data.dispatches.length === 0) worldLines.push('  (none)');
      for (const d of dispRes.data.dispatches) {
        worldLines.push(`  ${d.dispatchId}: ${d.seatId} ${d.actionKind} -> r${d.destinationRegion} [${d.status}]`);
      }
    } else {
      worldLines.push(`Dispatches error: ${dispRes.error}`);
    }
    worldSection.body.textContent = worldLines.join('\n');

    if (playerRes.ok) {
      const p = playerRes.data;
      summaryParts.push(`Ledger ${p.actorLedgerCount}`);
      const lines: string[] = [];
      lines.push(
        `Active Region: ${p.activeRegionName ?? 'none'} rep=${fmtSigned(p.activeRegionReputation)}${p.wantedInActiveRegion ? ' [WANTED]' : ''}${p.banishedInActiveRegion ? ' [BANISHED]' : ''}`,
      );
      lines.push(`ActorLedger entries: ${p.actorLedgerCount}`);
      lines.push(`Affiliations (${p.affiliations.length}):`);
      if (p.affiliations.length === 0) lines.push('  (none)');
      for (const a of p.affiliations) lines.push(`  ${a.faction}: rank ${a.rank}, rep ${fmtSigned(a.reputation)}`);
      lines.push(`Non-zero Factions (${p.factionStandings.length}):`);
      if (p.factionStandings.length === 0) lines.push('  (none)');
      for (const f of p.factionStandings) lines.push(`  ${f.name} (#${f.factionId}): ${fmtSigned(f.reputation)}`);
      lines.push(`Law: ${p.law.pendingChargeCount ?? 0} pending charges · ${p.law.reportedIncidentCount}/${p.law.incidentCount} reported incidents · banished regions=[${p.law.banishedRegions.join(', ')}]`);
      if (p.law.pendingCharges && p.law.pendingCharges.length > 0) {
        lines.push(`Pending Charges (${p.law.pendingCharges.length}):`);
        for (const ch of p.law.pendingCharges) {
          lines.push(`  - ${ch.crime} in ${ch.regionName ?? `r${ch.region}`} (${ch.reported ? 'reported' : 'witnessed'}) @m${ch.gameMinute}`);
        }
      }
      if (p.law.legalResponses && p.law.legalResponses.length > 0) {
        lines.push(`Active Legal Responses (${p.law.legalResponses.length}):`);
        for (const resp of p.law.legalResponses) {
          const pen = resp.fine != null || resp.prisonDays != null ? ` fine=${resp.fine ?? 0} prison=${resp.prisonDays ?? 0}d` : '';
          lines.push(`  - ${resp.regionName ?? `r${resp.region}`}: ${resp.phase} (${resp.chargeCount} charges${pen}${resp.sentenceOutcome ? ` ${resp.sentenceOutcome}` : ''})`);
        }
      }
      for (const inc of p.law.recentIncidents) {
        const statusText = inc.disposition ? `resolved:${inc.disposition.outcome}` : 'open';
        lines.push(`  - ${inc.crime} r${inc.region} ${inc.reported ? 'reported' : 'unreported'} [${statusText}] @m${inc.gameMinute}`);
      }
      lines.push(`Active Vouches (${p.vouches.length}):`);
      if (p.vouches.length === 0) lines.push('  (none)');
      for (const v of p.vouches) {
        lines.push(`  -> ${v.targetName ?? `#${v.targetDurableId}`}: ${v.kind} by ${v.referrerName ?? '?'}${v.referrerFamilyId ? ` [${v.referrerFamilyId}]` : ''} (day ${v.issuedDay})`);
      }
      playerSection.body.textContent = lines.join('\n');
    } else {
      playerSection.body.textContent = playerRes.error;
    }

    status.textContent = summaryParts.join(' · ') || 'Refreshed.';
    await renderActor();
  };

  refreshButton.addEventListener('click', () => void refreshAll());
  inspectButton.addEventListener('click', () => void renderActor());
  actorSelect.addEventListener('change', () => void renderActor());

  void refreshAll();
  return {
    dispose() {
      disposed = true;
      element.replaceChildren();
    },
  };
}
