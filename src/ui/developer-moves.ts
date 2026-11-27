import type { LiveDebugTransport } from '@rusty-engine/live-debug';

/**
 * The Moves panel: every seat's moves on the political board as one chronological log (`board.moves`), newest turn
 * first, beside the Board panel's per-seat view. Each turn lists the world's nudges heard that Sunday, the slow moves
 * arriving, each seat's move with how it came out, and the chronicle's arcs as they open, peak or settle. It reads
 * strictly through the debug command and owns no gameplay state: the seat filter only chooses which rows of the last
 * reply to show, and every Refresh asks again.
 */

interface Named {
  readonly id: string;
  readonly name: string;
}

interface MoveFate {
  readonly state: 'Landed' | 'Gathered' | 'UnderWay' | 'Arrived' | 'Failed' | 'Intercepted' | 'Gone';
  readonly turn: number | null;
  readonly due: number | null;
  readonly delays: readonly { readonly turn: number; readonly weeks: number }[];
  readonly deliveredEarly: number | null;
  readonly courier: { readonly status: string; readonly expectedArrivalMinute: number } | null;
}

interface MoveRow {
  readonly id: string;
  readonly seat: string;
  readonly card: string;
  readonly cardName: string;
  readonly targets: readonly Named[];
  readonly visibility: string;
  readonly plan: string | null;
  readonly origin: string | null;
  readonly changes: readonly string[];
  readonly fate: MoveFate;
}

interface MovesTurn {
  readonly turn: number;
  readonly day: number;
  readonly date: string;
  readonly world: { readonly card: string; readonly region: string; readonly actor: string | null; readonly changes: readonly string[] } | null;
  readonly nudges: readonly {
    readonly kind: string; readonly nudge: string; readonly move: string | null; readonly seat: string | null;
    readonly outcome: string; readonly changes: readonly string[];
  }[];
  readonly arrivals: readonly {
    readonly id: string; readonly seat: string; readonly card: string; readonly cardName: string; readonly targets: readonly Named[];
    readonly visibility: string; readonly played: number | null; readonly outcome: string; readonly changes: readonly string[];
  }[];
  readonly moves: readonly MoveRow[];
  readonly arcs: readonly { readonly kind: string; readonly phase: string; readonly key: string; readonly subjects: readonly Named[]; readonly started: number }[];
}

interface MovesReadout {
  readonly turn: number;
  readonly day: number;
  readonly nextTurnDay: number;
  readonly nextTurnDate: string;
  readonly kept: number;
  readonly seats: readonly Named[];
  readonly turns: readonly MovesTurn[];
}

const ALL = '';

/** How a move came out, in a few words: what the outcome badge says. */
export function fateText(fate: MoveFate): string {
  const delays = fate.delays.map(delay => `delayed ${delay.weeks}w at turn ${delay.turn}`);
  const early = fate.deliveredEarly != null ? [`delivered early at turn ${fate.deliveredEarly}`] : [];
  const courier = fate.courier ? [`courier ${fate.courier.status}`] : [];
  const head = (() => {
    switch (fate.state) {
      case 'Landed': return 'landed';
      case 'Gathered': return 'gathered';
      case 'UnderWay': return `under way, due turn ${fate.turn}`;
      case 'Arrived': return `arrived turn ${fate.turn}`;
      case 'Failed': return `failed on arrival turn ${fate.turn}`;
      case 'Intercepted': return `intercepted turn ${fate.turn}`;
      default: return 'gone';
    }
  })();
  return [head, ...early, ...delays, ...courier].join(' · ');
}

export function mountMovesPanel(element: HTMLElement, transport: LiveDebugTransport): { dispose(): void } {
  element.classList.add('dagger-developer-panel', 'dagger-developer-moves');
  let disposed = false;
  let last: MovesReadout | null = null;

  const toolbar = document.createElement('div');
  toolbar.className = 'dagger-developer-board-bar';
  const refreshButton = document.createElement('button');
  refreshButton.type = 'button';
  refreshButton.textContent = 'Refresh';
  const seatSelect = document.createElement('select');
  seatSelect.setAttribute('aria-label', 'Filter by seat');
  toolbar.append(refreshButton, seatSelect);

  const status = document.createElement('output');
  status.className = 'dagger-developer-status';
  const log = document.createElement('div');
  log.className = 'dagger-developer-moves-log';
  element.append(toolbar, status, log);

  const span = (className: string, text: string): HTMLSpanElement => {
    const node = document.createElement('span');
    node.className = className;
    node.textContent = text;
    return node;
  };
  const targets = (named: readonly Named[]): string => (named.length === 0 ? '' : `→ ${named.map(target => target.name).join(', ')}`);

  const setSeatOptions = (seats: readonly Named[]): void => {
    const keep = seatSelect.value;
    seatSelect.replaceChildren();
    const all = document.createElement('option');
    all.value = ALL;
    all.textContent = 'All seats';
    seatSelect.append(all);
    for (const seat of seats) {
      const option = document.createElement('option');
      option.value = seat.id;
      option.textContent = seat.name;
      seatSelect.append(option);
    }
    seatSelect.value = seats.some(seat => seat.id === keep) ? keep : ALL;
  };

  const render = (): void => {
    if (!last) return;
    const readout = last;
    const seat = seatSelect.value;
    const name = (id: string): string => readout.seats.find(row => row.id === id)?.name ?? id;
    const shows = (id: string | null): boolean => seat === ALL || id === seat;
    const blocks: HTMLElement[] = [];
    for (const turn of readout.turns) {
      const rows: HTMLElement[] = [];
      const row = (kind: string, seatId: string | null, ...parts: HTMLElement[]): HTMLElement => {
        const node = document.createElement('div');
        node.className = 'dagger-developer-moves-row';
        node.dataset.row = kind;
        if (seatId) node.dataset.seat = seatId;
        node.append(...parts);
        rows.push(node);
        return node;
      };
      const changes = (node: HTMLElement, list: readonly string[]): void => {
        if (list.length > 0) node.append(span('dagger-developer-moves-changes', list.join('; ')));
      };

      for (const nudge of turn.nudges.filter(nudge => shows(nudge.seat))) {
        const node = row('nudge', nudge.seat, span('dagger-developer-moves-kind', 'world'),
          ...(nudge.seat ? [span('dagger-developer-moves-seat', name(nudge.seat))] : []),
          span('dagger-developer-moves-what', nudge.nudge), span('dagger-developer-moves-fate', nudge.outcome.toLowerCase()));
        node.dataset.outcome = nudge.outcome;
        changes(node, nudge.changes);
      }
      for (const arrival of turn.arrivals.filter(arrival => shows(arrival.seat))) {
        const node = row('arrival', arrival.seat, span('dagger-developer-moves-kind', 'arrives'),
          span('dagger-developer-moves-seat', name(arrival.seat)),
          span('dagger-developer-moves-what', `${arrival.cardName} ${targets(arrival.targets)}`.trim()),
          span('dagger-developer-moves-visibility', arrival.visibility.toLowerCase()),
          span('dagger-developer-moves-fate', `${arrival.outcome === 'Resolved' ? 'resolved' : 'failed'}${arrival.played != null ? `, played turn ${arrival.played}` : ''}`));
        node.dataset.visibility = arrival.visibility;
        node.dataset.outcome = arrival.outcome;
        changes(node, arrival.changes);
      }
      for (const move of turn.moves.filter(move => shows(move.seat))) {
        const node = row('move', move.seat, span('dagger-developer-moves-kind', 'move'),
          span('dagger-developer-moves-seat', name(move.seat)),
          span('dagger-developer-moves-what', `${move.cardName} ${targets(move.targets)}`.trim()),
          span('dagger-developer-moves-visibility', move.visibility.toLowerCase()),
          span('dagger-developer-moves-fate', fateText(move.fate)));
        node.dataset.move = move.id;
        node.dataset.visibility = move.visibility;
        node.dataset.fate = move.fate.state;
        changes(node, move.changes);
      }
      for (const arc of turn.arcs.filter(arc => seat === ALL || arc.subjects.some(subject => subject.id === seat))) {
        const node = row('arc', null, span('dagger-developer-moves-kind', 'chronicle'),
          span('dagger-developer-moves-what', `${arc.kind} ${arc.subjects.map(subject => subject.name).join(' / ')}`),
          span('dagger-developer-moves-fate', `${arc.phase.toLowerCase()}${arc.phase === 'Opened' ? '' : `, opened turn ${arc.started}`}`));
        node.dataset.phase = arc.phase;
      }
      if (rows.length === 0 && seat !== ALL) continue;

      const block = document.createElement('section');
      block.className = 'dagger-developer-moves-turn';
      block.dataset.turn = String(turn.turn);
      const heading = document.createElement('h3');
      heading.textContent = `Turn ${turn.turn} · ${turn.date} (day ${turn.day})`;
      block.append(heading);
      if (turn.world && seat === ALL) {
        block.append(span('dagger-developer-moves-world', `World card: ${turn.world.card} in ${turn.world.region}${turn.world.actor ? `, by ${turn.world.actor}` : ''}`));
      }
      block.append(...rows);
      blocks.push(block);
    }
    if (blocks.length === 0) {
      const empty = document.createElement('p');
      empty.className = 'dagger-developer-moves-empty';
      empty.textContent = readout.turns.length === 0 ? 'No turn played this session yet.' : `No moves by ${name(seat)} in the kept turns.`;
      blocks.push(empty);
    }
    log.replaceChildren(...blocks);
  };

  const refresh = async (): Promise<void> => {
    let readout: MovesReadout;
    try {
      const result = await transport.execute('board.moves');
      if (disposed) return;
      if (!result.succeeded) { status.textContent = result.message; return; }
      readout = JSON.parse(result.message) as MovesReadout;
    } catch (error: unknown) {
      if (!disposed) status.textContent = error instanceof Error ? error.message : String(error);
      return;
    }
    last = readout;
    setSeatOptions(readout.seats);
    status.textContent = `Turn ${readout.turn} · next Sunday ${readout.nextTurnDate} (day ${readout.nextTurnDay}) · ${readout.kept} turns kept`;
    render();
  };

  refreshButton.addEventListener('click', () => void refresh());
  seatSelect.addEventListener('change', render);

  void refresh();
  return {
    dispose() {
      disposed = true;
      element.replaceChildren();
    },
  };
}
