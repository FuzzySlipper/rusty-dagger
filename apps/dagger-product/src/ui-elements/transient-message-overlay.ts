import {
  AfterViewInit,
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  Inject,
  Injectable,
  InjectionToken,
  OnDestroy,
  ViewEncapsulation,
  inject,
} from '@angular/core';

/** Visual/semantic severity exposed by the portable message API. */
export type TransientMessageSeverity = 'info' | 'success' | 'warning' | 'error';

/**
 * A host-relative CSS-pixel position.
 *
 * The origin is the top-left of the overlay host's padding box. Callers that
 * start with viewport coordinates must subtract the host's
 * `getBoundingClientRect().left/top` before spawning a message.
 */
export interface TransientMessagePosition {
  readonly x: number;
  readonly y: number;
}

/** Public input for one transient message. */
export interface SpawnTransientMessageOptions {
  readonly text: string;
  readonly severity?: TransientMessageSeverity;
  readonly position: TransientMessagePosition;
  /** Requested visible lifetime in milliseconds; it is bounded by the config. */
  readonly lifetime?: number;
  /** Set false for non-semantic decoration that must not enter the live region. */
  readonly announce?: boolean;
}

/** Resolved limits for one overlay instance. */
export interface TransientMessageOverlayConfig {
  /** Maximum number of live DOM messages. The oldest is evicted at the cap. */
  readonly maxActiveMessages: number;
  readonly defaultLifetimeMs: number;
  readonly minLifetimeMs: number;
  readonly maxLifetimeMs: number;
}

/** Optional config overrides used by an embedding product or a focused test. */
export type TransientMessageOverlayConfigOverrides = Partial<TransientMessageOverlayConfig>;

const DEFAULT_CONFIG: TransientMessageOverlayConfig = {
  maxActiveMessages: 128,
  defaultLifetimeMs: 1_500,
  minLifetimeMs: 100,
  maxLifetimeMs: 10_000,
};

/**
 * Angular configuration seam. The default keeps the element useful without
 * requiring a provider; another product can provide a complete resolved
 * config or use the controller directly with overrides.
 */
export const TRANSIENT_MESSAGE_OVERLAY_CONFIG =
  new InjectionToken<TransientMessageOverlayConfig>('Dagger transient message overlay config', {
    providedIn: 'root',
    factory: () => DEFAULT_CONFIG,
  });

/** Minimal timer surface that makes lifecycle tests deterministic. */
export interface TransientMessageOverlayScheduler {
  setTimeout(callback: () => void, delayMs: number): number;
  clearTimeout(handle: number): void;
}

/** Monotonic time source used for deterministic expiry calculations. */
export type TransientMessageOverlayClock = () => number;

/** Handle returned by {@link TransientMessageOverlayService.spawn}. */
export interface TransientMessageHandle {
  readonly id: string;
  /** Removes this message if it is still active; returns whether it existed. */
  cancel(): boolean;
}

/** Immutable record exposed by the lifecycle/test surface. */
export interface TransientMessageSnapshot {
  readonly id: string;
  readonly text: string;
  readonly severity: TransientMessageSeverity;
  readonly position: TransientMessagePosition;
  readonly createdAt: number;
  readonly expiresAt: number;
  readonly stackIndex: number;
  readonly announce: boolean;
}

/**
 * Small diagnostic surface for focused lifecycle and burst tests.
 * `lastFlushBatchSize` reports how many records the most recent expiry flush
 * removed, which makes batched cleanup observable without inspecting timers.
 */
export interface TransientMessageOverlayDebugSnapshot {
  readonly activeCount: number;
  readonly spawnedCount: number;
  readonly expiredCount: number;
  readonly evictedCount: number;
  readonly cancelledCount: number;
  readonly scheduledExpiryAt: number | undefined;
  readonly lastFlushBatchSize: number;
}

/** Construction seam for tests; production callers normally use the service. */
export interface TransientMessageOverlayControllerOptions {
  readonly config?: TransientMessageOverlayConfigOverrides;
  readonly clock?: TransientMessageOverlayClock;
  readonly scheduler?: TransientMessageOverlayScheduler;
}

interface MutableTransientMessage extends TransientMessageSnapshot {
  readonly lifetimeMs: number;
}

const SEVERITIES: readonly TransientMessageSeverity[] = ['info', 'success', 'warning', 'error'];
const MAX_STACK_DEPTH = 6;

const browserScheduler: TransientMessageOverlayScheduler = {
  setTimeout: (callback, delayMs) => globalThis.setTimeout(callback, delayMs),
  clearTimeout: (handle) => globalThis.clearTimeout(handle),
};

function browserClock(): number {
  return typeof performance === 'undefined' ? Date.now() : performance.now();
}

function resolveConfig(overrides: TransientMessageOverlayConfigOverrides | undefined): TransientMessageOverlayConfig {
  const config: TransientMessageOverlayConfig = {
    ...DEFAULT_CONFIG,
    ...overrides,
  };
  if (!Number.isInteger(config.maxActiveMessages) || config.maxActiveMessages < 1) {
    throw new RangeError('Transient message maxActiveMessages must be a positive integer');
  }
  if (!Number.isFinite(config.minLifetimeMs) || config.minLifetimeMs <= 0) {
    throw new RangeError('Transient message minLifetimeMs must be positive');
  }
  if (!Number.isFinite(config.maxLifetimeMs) || config.maxLifetimeMs < config.minLifetimeMs) {
    throw new RangeError('Transient message maxLifetimeMs must be >= minLifetimeMs');
  }
  if (!Number.isFinite(config.defaultLifetimeMs) || config.defaultLifetimeMs <= 0) {
    throw new RangeError('Transient message defaultLifetimeMs must be positive');
  }
  return config;
}

function requireFiniteTime(clock: TransientMessageOverlayClock): number {
  const now = clock();
  if (!Number.isFinite(now)) throw new Error('Transient message clock returned a non-finite value');
  return now;
}

function requireValidPosition(position: TransientMessagePosition): TransientMessagePosition {
  if (!Number.isFinite(position.x) || !Number.isFinite(position.y)) {
    throw new RangeError('Transient message position coordinates must be finite CSS pixels');
  }
  return { x: position.x, y: position.y };
}

function requireValidSeverity(severity: TransientMessageSeverity): TransientMessageSeverity {
  if (!SEVERITIES.includes(severity)) {
    throw new RangeError(`Unknown transient message severity: ${String(severity)}`);
  }
  return severity;
}

/**
 * DOM-independent lifecycle owner for transient messages.
 *
 * It owns one timer for the earliest expiry rather than one timer per message.
 * Expiry work is flushed as a batch, and CSS owns the compositor animation;
 * neither path asks Angular to run change detection for each frame.
 */
export class TransientMessageOverlayController {
  private readonly config: TransientMessageOverlayConfig;
  private readonly clock: TransientMessageOverlayClock;
  private readonly scheduler: TransientMessageOverlayScheduler;
  private readonly messages = new Map<string, MutableTransientMessage>();
  private readonly elements = new Map<string, HTMLElement>();
  private host: HTMLElement | undefined;
  private expiryTimer: number | undefined;
  private scheduledExpiryAt: number | undefined;
  private nextSequence = 1;
  private spawnedCount = 0;
  private expiredCount = 0;
  private evictedCount = 0;
  private cancelledCount = 0;
  private lastFlushBatchSize = 0;

  constructor(options: TransientMessageOverlayControllerOptions = {}) {
    this.config = resolveConfig(options.config);
    this.clock = options.clock ?? browserClock;
    this.scheduler = options.scheduler ?? browserScheduler;
  }

  /**
   * Attaches the DOM host used for presentation. Existing active records are
   * replayed when a component is mounted after an early spawn.
   */
  attachHost(host: HTMLElement): void {
    if (this.host === host) return;
    this.detachHost();
    // A timer may not have run yet when a detached host is reattached. Do not
    // resurrect records whose deterministic expiry has already elapsed.
    this.flushExpired();
    this.host = host;
    for (const message of this.messages.values()) this.mountMessage(message);
    this.scheduleExpiryWake();
  }

  /** Detaches presentation while retaining active lifecycle records. */
  detachHost(host?: HTMLElement): void {
    if (host !== undefined && this.host !== host) return;
    for (const element of this.elements.values()) element.remove();
    this.elements.clear();
    this.host = undefined;
  }

  /**
   * Spawns one message and returns a stable cancellation handle.
   *
   * IDs are instance-local, monotonic, and deterministic (`dagger-transient-`
   * plus a zero-padded sequence). Text is inserted with `textContent`, never
   * interpreted as HTML.
   */
  spawn(options: SpawnTransientMessageOptions): TransientMessageHandle {
    if (typeof options.text !== 'string' || options.text.trim() === '') {
      throw new TypeError('Transient message text must be a non-empty string');
    }
    const position = requireValidPosition(options.position);
    const severity = requireValidSeverity(options.severity ?? 'info');
    const requestedLifetime = options.lifetime ?? this.config.defaultLifetimeMs;
    if (!Number.isFinite(requestedLifetime) || requestedLifetime <= 0) {
      throw new RangeError('Transient message lifetime must be a positive finite number');
    }
    const lifetimeMs = Math.min(
      this.config.maxLifetimeMs,
      Math.max(this.config.minLifetimeMs, requestedLifetime),
    );
    const now = requireFiniteTime(this.clock);
    const id = `dagger-transient-${String(this.nextSequence).padStart(6, '0')}`;
    this.nextSequence += 1;
    if (this.messages.size >= this.config.maxActiveMessages) {
      const oldest = this.messages.keys().next().value;
      if (oldest !== undefined) {
        this.removeMessage(oldest, 'evicted');
        this.evictedCount += 1;
      }
    }
    const message: MutableTransientMessage = {
      id,
      text: options.text,
      severity,
      position,
      createdAt: now,
      expiresAt: now + lifetimeMs,
      stackIndex: this.stackIndex(position),
      announce: options.announce !== false,
      lifetimeMs,
    };
    this.messages.set(id, message);
    this.spawnedCount += 1;
    this.mountMessage(message);
    this.scheduleExpiryWake(now);
    return {
      id,
      cancel: () => this.cancel(id),
    };
  }

  /** Cancels an active message by ID. */
  cancel(id: string): boolean {
    const removed = this.removeMessage(id, 'cancelled');
    if (removed) this.cancelledCount += 1;
    this.scheduleExpiryWake();
    return removed;
  }

  /** Removes all active messages and cancels the one shared expiry timer. */
  clear(): number {
    const count = this.messages.size;
    this.messages.clear();
    for (const element of this.elements.values()) element.remove();
    this.elements.clear();
    this.cancelExpiryTimer();
    this.lastFlushBatchSize = 0;
    return count;
  }

  /**
   * Flushes all records expired at `atMs` in one lifecycle batch.
   * Supplying a timestamp is a test hook; production callers can omit it.
   */
  flushExpired(atMs = requireFiniteTime(this.clock)): number {
    if (!Number.isFinite(atMs)) throw new RangeError('Transient message flush time must be finite');
    let removed = 0;
    for (const message of this.messages.values()) {
      if (message.expiresAt > atMs) continue;
      if (this.removeMessage(message.id, 'expired')) {
        this.expiredCount += 1;
        removed += 1;
      }
    }
    this.lastFlushBatchSize = removed;
    this.scheduleExpiryWake(atMs);
    return removed;
  }

  /** Stable lifecycle records for tests and non-Angular hosts. */
  snapshots(): readonly TransientMessageSnapshot[] {
    return [...this.messages.values()].map(({ lifetimeMs: _lifetimeMs, ...message }) => ({
      ...message,
      position: { ...message.position },
    }));
  }

  /** Test hook for checking the rendered node associated with an ID. */
  elementForTest(id: string): HTMLElement | undefined {
    return this.elements.get(id);
  }

  /** Test hook for verifying bounded/batched behavior without DOM inspection. */
  debugSnapshot(): TransientMessageOverlayDebugSnapshot {
    return {
      activeCount: this.messages.size,
      spawnedCount: this.spawnedCount,
      expiredCount: this.expiredCount,
      evictedCount: this.evictedCount,
      cancelledCount: this.cancelledCount,
      scheduledExpiryAt: this.scheduledExpiryAt,
      lastFlushBatchSize: this.lastFlushBatchSize,
    };
  }

  /** Releases DOM/timer resources. Useful for a non-Angular host or a test. */
  destroy(): void {
    this.clear();
    this.detachHost();
  }

  private stackIndex(position: TransientMessagePosition): number {
    let matches = 0;
    for (const message of this.messages.values()) {
      if (message.position.x === position.x && message.position.y === position.y) matches += 1;
    }
    return matches % MAX_STACK_DEPTH;
  }

  private mountMessage(message: MutableTransientMessage): void {
    const document = this.host?.ownerDocument;
    if (document === undefined || this.elements.has(message.id)) return;
    const element = document.createElement('div');
    element.className = 'dagger-transient-message';
    element.id = message.id;
    element.dataset['transientMessageId'] = message.id;
    element.dataset['testid'] = `transient-message-${message.id}`;
    element.dataset['severity'] = message.severity;
    element.dataset['announce'] = String(message.announce);
    element.dataset['expiresAt'] = String(message.expiresAt);
    element.dataset['positionX'] = String(message.position.x);
    element.dataset['positionY'] = String(message.position.y);
    element.setAttribute('role', message.announce ? 'status' : 'presentation');
    if (message.announce) {
      element.setAttribute('aria-live', 'polite');
      element.setAttribute('aria-atomic', 'true');
    } else {
      element.setAttribute('aria-hidden', 'true');
    }
    element.style.left = `${message.position.x}px`;
    element.style.top = `${message.position.y}px`;
    const remainingLifetimeMs = Math.max(1, message.expiresAt - requireFiniteTime(this.clock));
    element.style.setProperty('--dagger-transient-message-lifetime', `${remainingLifetimeMs}ms`);
    element.style.setProperty('--dagger-transient-message-stack-offset', `${message.stackIndex * -24}px`);
    element.textContent = message.text;
    this.host?.append(element);
    this.elements.set(message.id, element);
  }

  private removeMessage(id: string, _reason: 'expired' | 'evicted' | 'cancelled'): boolean {
    const removed = this.messages.delete(id);
    if (!removed) return false;
    this.elements.get(id)?.remove();
    this.elements.delete(id);
    return true;
  }

  private scheduleExpiryWake(now = requireFiniteTime(this.clock)): void {
    if (this.messages.size === 0) {
      this.cancelExpiryTimer();
      return;
    }
    let earliest = Number.POSITIVE_INFINITY;
    for (const message of this.messages.values()) earliest = Math.min(earliest, message.expiresAt);
    if (
      this.expiryTimer !== undefined
      && this.scheduledExpiryAt !== undefined
      && this.scheduledExpiryAt <= earliest
    ) return;
    this.cancelExpiryTimer();
    const delay = Math.max(1, Math.ceil(earliest - now));
    this.scheduledExpiryAt = earliest;
    this.expiryTimer = this.scheduler.setTimeout(() => {
      this.expiryTimer = undefined;
      this.scheduledExpiryAt = undefined;
      this.flushExpired();
    }, delay);
  }

  private cancelExpiryTimer(): void {
    if (this.expiryTimer !== undefined) this.scheduler.clearTimeout(this.expiryTimer);
    this.expiryTimer = undefined;
    this.scheduledExpiryAt = undefined;
  }
}

/**
 * Application-facing Angular adapter. It deliberately delegates to the
 * controller and never mirrors active messages into Angular component state.
 */
@Injectable({ providedIn: 'root' })
export class TransientMessageOverlayService {
  private readonly controller: TransientMessageOverlayController;

  constructor(
    @Inject(TRANSIENT_MESSAGE_OVERLAY_CONFIG)
    config: TransientMessageOverlayConfig,
  ) {
    this.controller = new TransientMessageOverlayController({ config });
  }

  attachHost(host: HTMLElement): void {
    this.controller.attachHost(host);
  }

  detachHost(host?: HTMLElement): void {
    this.controller.detachHost(host);
  }

  spawn(options: SpawnTransientMessageOptions): TransientMessageHandle {
    return this.controller.spawn(options);
  }

  cancel(id: string): boolean {
    return this.controller.cancel(id);
  }

  clear(): number {
    return this.controller.clear();
  }

  /** Exposes the controller's deterministic expiry hook for focused tests. */
  flushExpired(atMs?: number): number {
    return atMs === undefined ? this.controller.flushExpired() : this.controller.flushExpired(atMs);
  }

  snapshots(): readonly TransientMessageSnapshot[] {
    return this.controller.snapshots();
  }

  debugSnapshot(): TransientMessageOverlayDebugSnapshot {
    return this.controller.debugSnapshot();
  }

  elementForTest(id: string): HTMLElement | undefined {
    return this.controller.elementForTest(id);
  }
}

/**
 * Empty Angular view whose host is the clipping coordinate space for the
 * controller. The dynamic message nodes are intentionally outside Angular's
 * template/change-detection tree.
 */
@Component({
  selector: 'dagger-transient-message-overlay',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  encapsulation: ViewEncapsulation.None,
  template: '',
  host: {
    class: 'dagger-transient-message-overlay',
    'data-testid': 'transient-message-overlay',
    'data-transient-message-host': 'true',
    role: 'presentation',
  },
  styles: [`
    dagger-transient-message-overlay {
      position: absolute;
      inset: 0;
      display: block;
      overflow: hidden;
      pointer-events: none;
      contain: layout style paint;
      isolation: isolate;
      z-index: 4;
    }

    dagger-transient-message-overlay .dagger-transient-message {
      position: absolute;
      left: 0;
      top: 0;
      box-sizing: border-box;
      max-width: min(32rem, calc(100% - 1rem));
      padding: .34rem .72rem;
      border: 1px solid rgba(240, 216, 121, .86);
      border-radius: .28rem;
      background: rgba(8, 12, 13, .9);
      box-shadow: 0 .25rem 1rem rgba(0, 0, 0, .42);
      color: #fff1b8;
      font: 700 clamp(.78rem, 1.8vw, .96rem)/1.3 Georgia, serif;
      overflow-wrap: anywhere;
      pointer-events: none;
      text-align: center;
      text-shadow: 0 1px 2px #000;
      user-select: none;
      will-change: transform, opacity;
      transform: translate3d(-50%, var(--dagger-transient-message-stack-offset), 0);
      animation: dagger-transient-message-float var(--dagger-transient-message-lifetime) ease-out both;
    }

    dagger-transient-message-overlay .dagger-transient-message[data-severity='success'] {
      border-color: rgba(146, 204, 143, .9);
      color: #d7f0bf;
    }

    dagger-transient-message-overlay .dagger-transient-message[data-severity='warning'] {
      border-color: rgba(240, 178, 98, .92);
      color: #ffe0a5;
    }

    dagger-transient-message-overlay .dagger-transient-message[data-severity='error'] {
      border-color: rgba(213, 123, 104, .94);
      color: #ffd0c9;
    }

    @keyframes dagger-transient-message-float {
      from {
        opacity: 1;
        transform: translate3d(-50%, var(--dagger-transient-message-stack-offset), 0);
      }
      62% {
        opacity: 1;
      }
      to {
        opacity: 0;
        transform: translate3d(-50%, calc(var(--dagger-transient-message-stack-offset) - 24px), 0);
      }
    }

    @media (prefers-reduced-motion: reduce) {
      dagger-transient-message-overlay .dagger-transient-message {
        animation: none;
        opacity: 1;
      }
    }
  `],
})
export class TransientMessageOverlayComponent implements AfterViewInit, OnDestroy {
  private readonly elementRef = inject(ElementRef<HTMLElement>);
  private readonly overlay = inject(TransientMessageOverlayService);

  ngAfterViewInit(): void {
    this.overlay.attachHost(this.elementRef.nativeElement);
  }

  ngOnDestroy(): void {
    this.overlay.detachHost(this.elementRef.nativeElement);
  }
}
