import type { LiveDebugTransport } from '@rusty-engine/live-debug';

/**
 * The Board panel: a developer view of the political board the session runs (`board.state`, `board.seat`,
 * `board.turns`). It reads strictly through those debug commands and owns no gameplay state: every Refresh asks again.
 * The table grid shows the board's shape at a glance (who is at war, in truce, trading or feuding with whom), and the
 * supply line shows the regions cut off; the sections below hold the detail.
 */

interface BoardSeatRow {
  readonly id: string;
  readonly name: string;
  readonly kind: string;
  readonly holder: string;
  readonly playsAs: string | null;
  readonly worldHolder: string | null;
  readonly prestige: number;
  readonly coin: number;
  readonly favour: number;
  readonly force: number;
  readonly plan: string | null;
  readonly reserves: string | null;
  readonly contestedWeeks: number;
  readonly reservesCut: boolean;
  readonly arbiter: boolean;
  readonly piecesLeft: number;
  readonly grudges: number;
}

interface BoardRegionRow {
  readonly id: string;
  readonly name: string;
  readonly catalogRegion: number;
  readonly capital: boolean;
  readonly allegiance: string | null;
  readonly control: number;
  readonly prosperity: number;
  readonly order: number;
  readonly supply: string;
  readonly cutWeeks: number;
  readonly influence: Readonly<Record<string, number>>;
  readonly offices: Readonly<Record<string, string>>;
  readonly conditions: readonly { readonly kind: string; readonly detail: string | null; readonly magnitude: number; readonly source: string | null; readonly end: number }[];
  readonly town: string | null;
}

interface BoardClock {
  readonly turn: number;
  readonly day: number;
  readonly lastTurnDay: number;
  readonly nextTurnDay: number;
  readonly nextTurnDate: string;
}

interface BoardStateReadout extends BoardClock {
  readonly leader: string | null;
  readonly seats: readonly BoardSeatRow[];
  readonly relations: readonly { readonly a: string; readonly b: string; readonly value: number }[];
  readonly wars: readonly {
    readonly id: string; readonly a: string; readonly b: string; readonly aim: string;
    readonly alliesOfA: readonly string[]; readonly alliesOfB: readonly string[];
    readonly started: number; readonly wearinessOfA: number; readonly wearinessOfB: number; readonly firstLoss: number | null;
  }[];
  readonly truces: readonly { readonly a: string; readonly b: string; readonly until: number }[];
  readonly feuds: readonly { readonly id: string; readonly a: string; readonly b: string; readonly started: number; readonly heat: number }[];
  readonly agreements: readonly { readonly id: string; readonly proposer: string; readonly gives: string; readonly partner: string; readonly asks: string; readonly start: number; readonly end: number }[];
  readonly regions: readonly BoardRegionRow[];
  readonly arbiters: readonly { readonly seat: string; readonly arbitration: number; readonly warsInForce: number }[];
  readonly pending: readonly {
    readonly id: string; readonly seat: string; readonly card: string; readonly targets: readonly string[];
    readonly played: number; readonly arrives: number; readonly courier: BoardCourier | null;
  }[];
  readonly queued: readonly string[];
  readonly arcs: readonly { readonly kind: string; readonly key: string; readonly subjects: readonly string[]; readonly started: number; readonly peaked: boolean; readonly settles: number | null }[];
}

interface BoardCourier {
  readonly status: string;
  readonly expectedArrivalMinute: number;
}

interface BoardSeatReadout extends BoardClock {
  readonly id: string;
  readonly name: string;
  readonly kind: string;
  readonly holder: string;
  readonly playsAs: string | null;
  readonly world: { readonly displayName: string | null; readonly castId: string | null; readonly vacant: boolean; readonly officeFactionId: number };
  readonly persona: { readonly goals: Readonly<Record<string, number>>; readonly families: Readonly<Record<string, number>>; readonly risk: number };
  readonly agenda: readonly string[];
  readonly plan: { readonly goal: string; readonly since: number; readonly baseline: number } | null;
  readonly prestige: number;
  readonly coin: number;
  readonly favour: number;
  readonly force: number;
  readonly grants: readonly { readonly resource: string; readonly amount: number; readonly everyWeeks: number }[];
  readonly reserves: string | null;
  readonly contestedWeeks: number;
  readonly reservesCut: boolean;
  readonly arbiter: boolean;
  readonly grudges: readonly { readonly against: string; readonly until: number }[];
  readonly others: readonly { readonly seat: string; readonly relation: number; readonly helpedThem: number; readonly helpedUs: number; readonly grudgeHeld: boolean }[];
  readonly deck: { readonly hand: readonly string[]; readonly drawPile: number; readonly discard: number };
  readonly court: readonly { readonly kind: string; readonly magnitude: number; readonly source: string; readonly end: number }[];
  readonly boons: readonly string[];
  readonly piecesPlaced: number;
  readonly piecesLeft: number;
  readonly footholds: number;
  readonly regionsHeld: readonly string[];
  readonly officesHeld: readonly string[];
}

interface BoardTurnsReadout extends BoardClock {
  readonly kept: number;
  readonly turns: readonly {
    readonly turn: number;
    readonly day: number;
    readonly nudges: readonly { readonly nudge: string; readonly outcome: string; readonly changes: readonly string[] }[];
    readonly upkeep: { readonly changes: readonly string[] };
    readonly world: { readonly card: string; readonly region: string; readonly actor: string | null; readonly changes: readonly string[] } | null;
    readonly arrivals: readonly { readonly id: string; readonly seat: string; readonly card: string; readonly targets: readonly string[]; readonly visibility: string; readonly outcome: string; readonly changes: readonly string[] }[];
    readonly moves: readonly {
      readonly id: string; readonly seat: string; readonly plan: string | null; readonly card: string; readonly targets: readonly string[];
      readonly score: number; readonly visibility: string; readonly origin: string | null; readonly arrives: number | null;
      readonly alternatives: readonly string[]; readonly changes: readonly string[]; readonly courier: BoardCourier | null;
    }[];
    readonly wars: { readonly changes: readonly string[] };
    readonly chronicle: readonly { readonly kind: string; readonly phase: string; readonly subjects: readonly string[]; readonly headlineKey: string }[];
  }[];
}

/** How a pair of seats stands, strongest first: what the grid cell shows. */
type PairState = 'war' | 'truce' | 'feud' | 'trade' | 'friendly' | 'hostile' | 'neutral';

const GLYPH: Readonly<Record<PairState, string>> = { war: 'W', truce: 'T', feud: 'F', trade: '$', friendly: '', hostile: '', neutral: '' };

/** The grid cell for each pair: war over truce over feud over trade, else the sign of the relation. */
export function pairStates(state: BoardStateReadout): Map<string, { state: PairState; relation: number }> {
  const key = (a: string, b: string): string => (a < b ? `${a}|${b}` : `${b}|${a}`);
  const pairs = new Map<string, { state: PairState; relation: number }>();
  for (const relation of state.relations) {
    pairs.set(key(relation.a, relation.b), { state: relation.value > 0 ? 'friendly' : 'hostile', relation: relation.value });
  }
  const mark = (a: string, b: string, value: PairState): void => {
    const existing = pairs.get(key(a, b));
    pairs.set(key(a, b), { state: value, relation: existing?.relation ?? 0 });
  };
  for (const deal of state.agreements) mark(deal.proposer, deal.partner, 'trade');
  for (const feud of state.feuds) mark(feud.a, feud.b, 'feud');
  for (const truce of state.truces) mark(truce.a, truce.b, 'truce');
  for (const war of state.wars) {
    for (const a of [war.a, ...war.alliesOfA]) for (const b of [war.b, ...war.alliesOfB]) mark(a, b, 'war');
  }
  return pairs;
}

export function mountBoardPanel(element: HTMLElement, transport: LiveDebugTransport): { dispose(): void } {
  element.classList.add('dagger-developer-panel', 'dagger-developer-board');
  let disposed = false;

  const toolbar = document.createElement('div');
  toolbar.className = 'dagger-developer-board-bar';
  const refreshButton = document.createElement('button');
  refreshButton.type = 'button';
  refreshButton.textContent = 'Refresh';
  const seatSelect = document.createElement('select');
  seatSelect.setAttribute('aria-label', 'Inspect seat');
  toolbar.append(refreshButton, seatSelect);

  const status = document.createElement('output');
  status.className = 'dagger-developer-status';

  const makeSection = (title: string, open: boolean, key: string): { details: HTMLDetailsElement; body: HTMLElement } => {
    const details = document.createElement('details');
    details.open = open;
    details.dataset.boardSection = key;
    const summary = document.createElement('summary');
    summary.textContent = title;
    const body = document.createElement('div');
    details.append(summary, body);
    return { details, body };
  };
  const pre = (text: string): HTMLPreElement => {
    const block = document.createElement('pre');
    block.className = 'dagger-developer-board-pre';
    block.textContent = text;
    return block;
  };

  const shapeSection = makeSection('Table at a Glance', true, 'shape');
  const seatsSection = makeSection('Seats (board.state)', true, 'seats');
  const seatSection = makeSection('Seat (board.seat)', true, 'seat');
  const diplomacySection = makeSection('Wars, Truces, Feuds & Trade', false, 'diplomacy');
  const regionsSection = makeSection('Regions & Supply', false, 'regions');
  const turnsSection = makeSection('Recent Turns (board.turns)', false, 'turns');
  const pendingSection = makeSection('Pending Moves, Queued Nudges & Arcs', false, 'pending');

  const bodyStack = document.createElement('div');
  bodyStack.className = 'dagger-developer-board-body';
  bodyStack.append(...[shapeSection, seatsSection, seatSection, diplomacySection, regionsSection, turnsSection, pendingSection].map(section => section.details));
  element.append(toolbar, status, bodyStack);

  const execJson = async <T>(command: string): Promise<{ ok: true; data: T } | { ok: false; error: string }> => {
    try {
      const result = await transport.execute(command);
      if (!result.succeeded) return { ok: false, error: result.message };
      return { ok: true, data: JSON.parse(result.message) as T };
    } catch (error: unknown) {
      return { ok: false, error: error instanceof Error ? error.message : String(error) };
    }
  };
  const signed = (value: number): string => (value > 0 ? `+${value}` : String(value));
  const list = (values: readonly string[]): string => (values.length === 0 ? 'none' : values.join(', '));

  const setSeatOptions = (seats: readonly BoardSeatRow[], preserve: string): void => {
    seatSelect.replaceChildren();
    for (const seat of seats) {
      const option = document.createElement('option');
      option.value = seat.id;
      option.textContent = `${seat.name} (${seat.id})`;
      seatSelect.append(option);
    }
    seatSelect.value = seats.some(seat => seat.id === preserve) ? preserve : (seats[0]?.id ?? '');
  };

  const renderShape = (state: BoardStateReadout): void => {
    const pairs = pairStates(state);
    const table = document.createElement('table');
    table.className = 'dagger-developer-board-grid';
    const head = document.createElement('tr');
    head.append(document.createElement('th'));
    for (const seat of state.seats) {
      const th = document.createElement('th');
      th.textContent = seat.id.slice(0, 3);
      th.dataset.seat = seat.id;
      head.append(th);
    }
    table.append(head);
    for (const row of state.seats) {
      const tr = document.createElement('tr');
      const th = document.createElement('th');
      th.textContent = row.name;
      th.dataset.seat = row.id;
      if (row.id === state.leader) th.dataset.leader = '';
      tr.append(th);
      for (const column of state.seats) {
        const td = document.createElement('td');
        if (row.id === column.id) { td.dataset.state = 'self'; tr.append(td); continue; }
        const pair = pairs.get(row.id < column.id ? `${row.id}|${column.id}` : `${column.id}|${row.id}`) ?? { state: 'neutral' as PairState, relation: 0 };
        td.dataset.state = pair.state;
        td.dataset.pair = `${row.id}|${column.id}`;
        td.textContent = GLYPH[pair.state] || (pair.relation !== 0 ? signed(pair.relation) : '');
        tr.append(td);
      }
      table.append(tr);
    }
    const legend = document.createElement('p');
    legend.className = 'dagger-developer-board-legend';
    legend.textContent = 'W war · T truce · F feud · $ trade · +/- relation · leader starred';

    const cut = state.regions.filter(region => region.supply === 'Cut');
    const relieved = state.regions.filter(region => region.supply === 'Relieved');
    const supply = pre([
      `Cut off (${cut.length}): ${list(cut.map(region => `${region.name} ${region.cutWeeks}w`))}`,
      `Relieved by trade (${relieved.length}): ${list(relieved.map(region => region.name))}`,
      `Reserves cut: ${list(state.seats.filter(seat => seat.reservesCut).map(seat => seat.name))}`,
    ].join('\n'));
    supply.dataset.boardSupply = '';
    shapeSection.body.replaceChildren(table, legend, supply);
  };

  const renderState = (state: BoardStateReadout): void => {
    renderShape(state);
    const name = (id: string): string => state.seats.find(seat => seat.id === id)?.name ?? id;

    const seatLines = [...state.seats]
      .sort((a, b) => b.prestige - a.prestige)
      .map(seat => {
        const holder = seat.playsAs ? `${seat.holder} (regency, plays as ${seat.playsAs})` : seat.holder;
        const world = seat.worldHolder ? ` · world: ${seat.worldHolder}` : '';
        const reserves = seat.reserves ? ` · reserves ${seat.reserves}${seat.reservesCut ? ' CUT' : seat.contestedWeeks > 0 ? ` contested ${seat.contestedWeeks}w` : ''}` : '';
        return `${seat.id === state.leader ? '*' : ' '} ${seat.name}: prestige ${seat.prestige} · coin ${seat.coin} favour ${seat.favour} force ${seat.force}`
          + ` · pieces left ${seat.piecesLeft}${seat.grudges > 0 ? ` · grudges ${seat.grudges}` : ''}${seat.arbiter ? ' · ARBITER' : ''}\n`
          + `    holder ${holder}${world} · plan ${seat.plan ?? 'none'}${reserves}`;
      });
    seatsSection.body.replaceChildren(pre(seatLines.join('\n')));

    const diplomacy: string[] = [];
    diplomacy.push(`Wars (${state.wars.length}):`);
    for (const war of state.wars) {
      const allies = (seats: readonly string[]): string => (seats.length > 0 ? ` + ${seats.map(name).join(', ')}` : '');
      diplomacy.push(`  ${name(war.a)}${allies(war.alliesOfA)} vs ${name(war.b)}${allies(war.alliesOfB)} for ${war.aim.toLowerCase()} since turn ${war.started}`
        + ` · weariness ${war.wearinessOfA}/${war.wearinessOfB}${war.firstLoss != null ? ` · first loss turn ${war.firstLoss}` : ''}`);
    }
    diplomacy.push(`Truces (${state.truces.length}):`);
    for (const truce of state.truces) diplomacy.push(`  ${name(truce.a)} and ${name(truce.b)} until turn ${truce.until}`);
    diplomacy.push(`Feuds (${state.feuds.length}):`);
    for (const feud of state.feuds) diplomacy.push(`  ${name(feud.a)} and ${name(feud.b)} since turn ${feud.started} · heat ${feud.heat}`);
    diplomacy.push(`Trade (${state.agreements.length}):`);
    for (const deal of state.agreements) diplomacy.push(`  ${name(deal.proposer)} gives ${deal.gives} to ${name(deal.partner)} for ${deal.asks} · turns ${deal.start}-${deal.end}`);
    diplomacy.push(`Arbiter balance:`);
    for (const arbiter of state.arbiters) diplomacy.push(`  ${name(arbiter.seat)}: arbitration ${signed(arbiter.arbitration)} with ${arbiter.warsInForce} wars in force`);
    diplomacySection.body.replaceChildren(pre(diplomacy.join('\n')));

    const regionLines = state.regions.map(region => {
      const influence = Object.entries(region.influence).map(([seat, pieces]) => `${seat} ${pieces}`).join(', ');
      const offices = Object.entries(region.offices).map(([office, seat]) => `${office.toLowerCase()} ${seat}`).join(', ');
      const conditions = region.conditions.map(condition => `${condition.kind}${condition.detail ? `(${condition.detail})` : ''} ${condition.magnitude}`).join(', ');
      return `${region.capital ? '#' : ' '} ${region.name} [r${region.catalogRegion}${region.town ? ` ${region.town}` : ''}]: ${region.allegiance ?? 'independent'}`
        + ` · control ${region.control} prosperity ${region.prosperity} order ${region.order} · ${region.supply}${region.cutWeeks > 0 ? ` ${region.cutWeeks}w` : ''}`
        + `${influence ? `\n    influence ${influence}` : ''}${offices ? `\n    offices ${offices}` : ''}${conditions ? `\n    conditions ${conditions}` : ''}`;
    });
    regionsSection.body.replaceChildren(pre(regionLines.join('\n')));

    const pending: string[] = [`Pending moves (${state.pending.length}):`];
    for (const move of state.pending) {
      pending.push(`  ${move.id}: ${name(move.seat)} ${move.card} -> ${list(move.targets)} · arrives turn ${move.arrives}`
        + `${move.courier ? ` · courier ${move.courier.status}` : ''}`);
    }
    pending.push(`Queued nudges (${state.queued.length}):`);
    for (const nudge of state.queued) pending.push(`  ${nudge}`);
    pending.push(`Open arcs (${state.arcs.length}):`);
    for (const arc of state.arcs) {
      pending.push(`  ${arc.kind} ${arc.subjects.map(name).join(' / ')} since turn ${arc.started}${arc.peaked ? ' · peaked' : ''}${arc.settles != null ? ` · settles turn ${arc.settles}` : ''}`);
    }
    pendingSection.body.replaceChildren(pre(pending.join('\n')));
  };

  const renderSeat = async (): Promise<void> => {
    const id = seatSelect.value;
    if (!id) { seatSection.body.replaceChildren(pre('No seat.')); return; }
    const res = await execJson<BoardSeatReadout>(`board.seat ${id}`);
    if (disposed) return;
    if (!res.ok) { seatSection.body.replaceChildren(pre(res.error)); return; }
    const seat = res.data;
    const weights = (values: Readonly<Record<string, number>>): string => list(Object.entries(values).map(([key, value]) => `${key} ${value}`));
    const lines: string[] = [];
    lines.push(`${seat.name} (${seat.kind}) · prestige ${seat.prestige}${seat.arbiter ? ' · ARBITER' : ''}`);
    lines.push(`Holder: ${seat.holder}${seat.playsAs ? ` · regency, plays as ${seat.playsAs}` : ''}`);
    lines.push(`World holder: ${seat.world.vacant ? 'VACANT' : (seat.world.displayName ?? 'unnamed')}${seat.world.castId ? ` [${seat.world.castId}]` : ''} · office faction ${seat.world.officeFactionId}`);
    lines.push(`Persona: goals ${weights(seat.persona.goals)} · families ${weights(seat.persona.families)} · risk ${seat.persona.risk}`);
    lines.push(`Agenda: ${list(seat.agenda)}`);
    lines.push(`Plan: ${seat.plan ? `${seat.plan.goal} since turn ${seat.plan.since} (baseline ${seat.plan.baseline})` : 'none'}`);
    lines.push(`Resources: coin ${seat.coin} · favour ${seat.favour} · force ${seat.force} · grants ${list(seat.grants.map(grant => `${grant.amount} ${grant.resource} every ${grant.everyWeeks}w`))}`);
    lines.push(`Reserves: ${seat.reserves ?? 'none'}${seat.reservesCut ? ' CUT' : seat.contestedWeeks > 0 ? ` · contested ${seat.contestedWeeks}w` : ''}`);
    lines.push(`Pieces: ${seat.piecesPlaced} placed · ${seat.piecesLeft} left · ${seat.footholds} footholds`);
    lines.push(`Holds: ${list(seat.regionsHeld)} · offices ${list(seat.officesHeld)}`);
    lines.push(`Hand: ${list(seat.deck.hand)} · draw ${seat.deck.drawPile} · discard ${seat.deck.discard}`);
    lines.push(`Court: ${list(seat.court.map(condition => `${condition.kind} ${condition.magnitude} by ${condition.source} to turn ${condition.end}`))}`);
    lines.push(`Boons: ${list(seat.boons)}`);
    lines.push(`Grudges: ${list(seat.grudges.map(grudge => `${grudge.against} to turn ${grudge.until}`))}`);
    lines.push('Others:');
    for (const other of seat.others) {
      const helps = other.helpedThem || other.helpedUs ? ` · helped them ${other.helpedThem}, helped us ${other.helpedUs}` : '';
      lines.push(`  ${other.seat}: relation ${signed(other.relation)}${helps}${other.grudgeHeld ? ' · holds a grudge against us' : ''}`);
    }
    seatSection.body.replaceChildren(pre(lines.join('\n')));
  };

  const renderTurns = (readout: BoardTurnsReadout): void => {
    if (readout.turns.length === 0) { turnsSection.body.replaceChildren(pre('No turn played this session yet.')); return; }
    const lines: string[] = [];
    for (const turn of readout.turns) {
      lines.push(`Turn ${turn.turn} (day ${turn.day})`);
      for (const nudge of turn.nudges) lines.push(`  nudge ${nudge.nudge} [${nudge.outcome}]${nudge.changes.length > 0 ? `: ${nudge.changes.join('; ')}` : ''}`);
      if (turn.world) lines.push(`  world ${turn.world.card} in ${turn.world.region}${turn.world.actor ? ` by ${turn.world.actor}` : ''}${turn.world.changes.length > 0 ? `: ${turn.world.changes.join('; ')}` : ''}`);
      for (const arrival of turn.arrivals) lines.push(`  arrival ${arrival.id} ${arrival.card} -> ${list(arrival.targets)} [${arrival.outcome}, ${arrival.visibility}]${arrival.changes.length > 0 ? `: ${arrival.changes.join('; ')}` : ''}`);
      for (const move of turn.moves) {
        const slow = move.arrives != null ? ` · arrives turn ${move.arrives}` : '';
        const courier = move.courier ? ` · courier ${move.courier.status}` : '';
        lines.push(`  ${move.seat}: ${move.card} -> ${list(move.targets)} (${move.score}, ${move.visibility.toLowerCase()}${move.plan ? `, for ${move.plan}` : ''})${slow}${courier}`);
        if (move.changes.length > 0) lines.push(`      ${move.changes.join('; ')}`);
        if (move.alternatives.length > 0) lines.push(`      passed over: ${move.alternatives.join(' | ')}`);
      }
      if (turn.wars.changes.length > 0) lines.push(`  wars: ${turn.wars.changes.join('; ')}`);
      for (const arc of turn.chronicle) lines.push(`  chronicle: ${arc.kind} ${arc.phase} ${arc.subjects.join(' / ')}`);
      if (turn.upkeep.changes.length > 0) lines.push(`  upkeep: ${turn.upkeep.changes.length} changes`);
    }
    turnsSection.body.replaceChildren(pre(lines.join('\n')));
  };

  const refreshAll = async (): Promise<void> => {
    const [stateRes, turnsRes] = await Promise.all([
      execJson<BoardStateReadout>('board.state'),
      execJson<BoardTurnsReadout>('board.turns 4'),
    ]);
    if (disposed) return;
    if (!stateRes.ok) {
      status.textContent = stateRes.error;
      return;
    }
    const state = stateRes.data;
    setSeatOptions(state.seats, seatSelect.value);
    const leader = state.leader ? state.seats.find(seat => seat.id === state.leader)?.name ?? state.leader : 'none';
    status.textContent = `Turn ${state.turn} · next Sunday ${state.nextTurnDate} (day ${state.nextTurnDay}) · today day ${state.day} · leader ${leader}`
      + ` · ${state.wars.length} wars · ${state.agreements.length} trade`;
    renderState(state);
    if (turnsRes.ok) renderTurns(turnsRes.data);
    else turnsSection.body.replaceChildren(pre(turnsRes.error));
    await renderSeat();
  };

  refreshButton.addEventListener('click', () => void refreshAll());
  seatSelect.addEventListener('change', () => void renderSeat());

  void refreshAll();
  return {
    dispose() {
      disposed = true;
      element.replaceChildren();
    },
  };
}
