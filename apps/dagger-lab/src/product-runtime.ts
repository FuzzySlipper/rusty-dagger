import { InjectionToken } from '@angular/core';
import type {
  RustyApplicationContent,
  RustyApplicationRendererPort,
  RustyApplicationUiContext,
} from '@rusty-engine/application-host';

export const DAGGER_APPLICATION_CONTEXT = new InjectionToken<RustyApplicationUiContext>(
  'Dagger application host context',
);

interface DaggerProductCamera {
  readonly position: readonly [number, number, number];
  readonly pitchDegrees: number;
  readonly yawDegrees: number;
}

interface DaggerProductResourceWire {
  readonly identity: string;
  readonly contentHash: string;
  readonly mediaType: string;
  readonly bytesBase64: string;
}

interface DaggerProductBootstrapWire {
  readonly schemaVersion: 1;
  readonly camera: DaggerProductCamera;
  readonly frame: Readonly<Record<string, unknown>>;
  readonly resources: readonly DaggerProductResourceWire[];
  readonly sourceEntityCount: number;
}

interface DaggerProductStateWire {
  readonly camera: DaggerProductCamera;
  readonly playerPosition: readonly [number, number, number];
  readonly frame?: Readonly<Record<string, unknown>>;
  readonly patrolDebugEnabled: boolean;
  readonly navDebugEnabled: boolean;
}

interface DaggerPhysicalInputWire {
  readonly pressedCodes: readonly string[];
  readonly pointerDelta: readonly [number, number];
  readonly buttons: number;
}

export interface DaggerProductBootstrap {
  readonly camera: DaggerProductCamera;
  readonly content: RustyApplicationContent;
  readonly sourceEntityCount: number;
}

export async function loadDaggerProductBootstrap(): Promise<DaggerProductBootstrap> {
  const response = await fetch('/api/dagger-product/bootstrap', { cache: 'no-store' });
  if (!response.ok) {
    throw new Error(`Dagger product bootstrap failed with ${String(response.status)}`);
  }
  const wire = await response.json() as DaggerProductBootstrapWire;
  if (wire.schemaVersion !== 1 || !Array.isArray(wire.resources)) {
    throw new Error('Dagger product bootstrap is malformed');
  }
  return {
    camera: wire.camera,
    content: {
      frame: wire.frame,
      resources: wire.resources.map((resource) => ({
        identity: resource.identity,
        contentHash: resource.contentHash,
        mediaType: resource.mediaType,
        bytes: decodeBase64(resource.bytesBase64),
      })),
    },
    sourceEntityCount: wire.sourceEntityCount,
  };
}

export function mountDaggerProductRuntime(
  renderer: RustyApplicationRendererPort,
  context: RustyApplicationUiContext,
): { readonly dispose: () => void } {
  const pressed = new Set<string>();
  let pending: DaggerPhysicalInputWire | null = null;
  let sending = false;
  let disposed = false;
  let buttons = 0;
  let dynamicFrameSequence = 0;
  const environmentFrames = new Map<number, number>();
  const enemyTransforms = new Map<number, string>();

  const applyState = (state: DaggerProductStateWire): void => {
    if (state.frame !== undefined) {
      const ops = state.frame['ops'];
      const opCount = Array.isArray(ops) ? ops.length : 0;
      const receipt = renderer.applyFrame(state.frame);
      if (!receipt.applied) {
        throw new Error(
          `Dagger dynamic frame rejected: ${receipt.diagnostics.map((entry) => entry.message).join('; ')}`,
        );
      }
      if (opCount > 0) {
        for (const candidate of ops as unknown[]) {
          if (typeof candidate !== 'object' || candidate === null) continue;
          const op = candidate as Record<string, unknown>;
          const handle = op['handle'];
          if (typeof handle !== 'number') continue;
          if (op['op'] === 'updateSprite' && handle < 2000 && typeof op['frame'] === 'number') {
            const previous = environmentFrames.get(handle);
            environmentFrames.set(handle, op['frame']);
            if (previous !== undefined && previous !== op['frame']) {
              document.body.dataset['daggerAnimatedEnvironmentHandle'] = String(handle);
            }
          }
          if (op['op'] === 'update' && handle >= 2000 && op['transform'] !== undefined) {
            const transform = JSON.stringify(op['transform']);
            const previous = enemyTransforms.get(handle);
            enemyTransforms.set(handle, transform);
            if (previous !== undefined && previous !== transform) {
              document.body.dataset['daggerMovedEnemyHandle'] = String(handle);
            }
          }
        }
        renderer.renderOnce();
        dynamicFrameSequence += 1;
        document.body.dataset['daggerDynamicFrameSequence'] = String(dynamicFrameSequence);
        document.body.dataset['daggerDynamicOpCount'] = String(opCount);
      }
    }
    renderer.setCameraPose(state.camera);
    document.body.dataset['daggerAuthoritativePosition'] = state.playerPosition.join(',');
    document.body.dataset['daggerPatrolDebug'] = String(state.patrolDebugEnabled);
    document.body.dataset['daggerNavDebug'] = String(state.navDebugEnabled);
  };
  const drain = async (): Promise<void> => {
    if (sending || disposed) return;
    sending = true;
    try {
      while (pending !== null && !disposed) {
        const input = pending;
        pending = null;
        const response = await fetch('/api/dagger-product/input', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(input),
        });
        if (!response.ok) {
          throw new Error(`Dagger product input failed with ${String(response.status)}`);
        }
        applyState(await response.json() as DaggerProductStateWire);
        delete document.body.dataset['daggerProductInputError'];
      }
    } catch (error: unknown) {
      document.body.dataset['daggerProductInputError'] =
        error instanceof Error ? error.message : String(error);
    } finally {
      sending = false;
      if (pending !== null && !disposed) void drain();
    }
  };
  const submit = (pointerDelta: readonly [number, number], buttons: number): void => {
    pending = {
      pressedCodes: [...pressed].sort(),
      pointerDelta,
      buttons,
    };
    void drain();
  };
  const onKeyDown = (event: KeyboardEvent): void => {
    if (event.code === 'Escape') {
      pressed.clear();
      buttons = 0;
      context.ui.setInteractionMode('interface');
      window.dispatchEvent(new Event('dagger-open-lab'));
      submit([0, 0], 0);
      return;
    }
    if (event.repeat || !context.ui.allowsGameplayInput(event)) return;
    pressed.add(event.code);
    submit([0, 0], buttons);
  };
  const onKeyUp = (event: KeyboardEvent): void => {
    pressed.delete(event.code);
    submit([0, 0], buttons);
  };
  const onMouseMove = (event: MouseEvent): void => {
    if (document.pointerLockElement === null || !context.ui.allowsGameplayInput(event)) return;
    buttons = event.buttons;
    submit([event.movementX, event.movementY], buttons);
  };
  const onMouseDown = (event: MouseEvent): void => {
    if (!context.ui.allowsGameplayInput(event)) return;
    buttons = event.buttons;
    submit([0, 0], buttons);
  };
  const onMouseUp = (event: MouseEvent): void => {
    buttons = event.buttons;
    submit([0, 0], buttons);
  };
  const onBlur = (): void => {
    pressed.clear();
    buttons = 0;
    submit([0, 0], 0);
  };
  window.addEventListener('keydown', onKeyDown);
  window.addEventListener('keyup', onKeyUp);
  window.addEventListener('mousemove', onMouseMove);
  window.addEventListener('mousedown', onMouseDown);
  window.addEventListener('mouseup', onMouseUp);
  window.addEventListener('blur', onBlur);
  const inputTick = window.setInterval(() => {
    if (pressed.size > 0 || buttons !== 0) submit([0, 0], buttons);
  }, 40);
  const poll = window.setInterval(() => {
    void fetch('/api/dagger-product/state', { cache: 'no-store' })
      .then((response) => {
        if (!response.ok) {
          throw new Error(`Dagger product state failed with ${String(response.status)}`);
        }
        return response.json() as Promise<DaggerProductStateWire>;
      })
      .then(applyState)
      .catch(() => undefined);
  }, 100);
  return {
    dispose: () => {
      disposed = true;
      pending = null;
      window.clearInterval(inputTick);
      window.clearInterval(poll);
      window.removeEventListener('keydown', onKeyDown);
      window.removeEventListener('keyup', onKeyUp);
      window.removeEventListener('mousemove', onMouseMove);
      window.removeEventListener('mousedown', onMouseDown);
      window.removeEventListener('mouseup', onMouseUp);
      window.removeEventListener('blur', onBlur);
    },
  };
}

function decodeBase64(encoded: string): Uint8Array {
  const binary = atob(encoded);
  const bytes = new Uint8Array(binary.length);
  for (let index = 0; index < binary.length; index += 1) bytes[index] = binary.charCodeAt(index);
  return bytes;
}
