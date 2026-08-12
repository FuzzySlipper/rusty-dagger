import { InjectionToken } from '@angular/core';
import type {
  RustyApplicationContent,
  RustyApplicationPresentationFrame,
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
  readonly inputSequence: number;
  readonly camera: DaggerProductCamera;
  readonly playerPosition: readonly [number, number, number];
  readonly frame?: Readonly<Record<string, unknown>>;
  readonly presentation?: RustyApplicationPresentationFrame;
  readonly patrolDebugEnabled: boolean;
  readonly navDebugEnabled: boolean;
  readonly meleePresentation: DaggerMeleePresentationWire | null;
  readonly playerStamina: number;
  readonly playerMaxStamina: number;
}

interface DaggerMeleePresentationWire {
  readonly attemptSequence: number;
  readonly phase: 'anticipation' | 'contact' | 'recovery' | 'rejected';
  readonly phaseProgress: number;
  readonly accepted: boolean;
  readonly outcome: string;
  readonly targetId: number | null;
  readonly staminaBefore: number;
  readonly staminaAfter: number;
  readonly targetHealthBefore: number | null;
  readonly targetHealthAfter: number | null;
  readonly targetMaxHealth: number | null;
  readonly finalDamage: number | null;
  readonly died: boolean;
}

interface DaggerPhysicalInputWire {
  readonly sequence: number;
  readonly stepSeconds: number;
  readonly pressedCodes: readonly string[];
  readonly pressedEdges: readonly string[];
  readonly pointerDelta: readonly [number, number];
  readonly buttons: number;
  readonly buttonPressedEdges: number;
}

const INPUT_SAMPLE_INTERVAL_MS = 40;
const MAX_QUEUED_STEP_SECONDS = 0.25;
const MAX_SAMPLED_STEP_SECONDS = 0.08;
const MIN_INPUT_STEP_SECONDS = 0.001;

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
  const pressedEdges = new Set<string>();
  const pending: DaggerPhysicalInputWire[] = [];
  let sending = false;
  let polling = false;
  let disposed = false;
  let buttons = 0;
  let buttonPressedEdges = 0;
  let pointerDelta: [number, number] = [0, 0];
  let inputSequence = 0;
  let latestAppliedInputSequence = 0;
  let lastInputSampleAtMs = performance.now();
  let inputChanged = false;
  let dynamicFrameSequence = 0;
  const environmentFrames = new Map<number, number>();
  const enemyTransforms = new Map<number, string>();

  const applyState = async (state: DaggerProductStateWire): Promise<void> => {
    const ownsControlState = state.inputSequence >= latestAppliedInputSequence;
    if (ownsControlState) latestAppliedInputSequence = state.inputSequence;
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
    if (state.presentation !== undefined) {
      const ops = state.presentation['ops'];
      const opCount = Array.isArray(ops) ? ops.length : 0;
      if (opCount > 0) {
        const receipt = await renderer.applyPresentation(state.presentation);
        if (receipt.applied !== opCount || receipt.diagnostics.length > 0) {
          throw new Error(
            `Dagger presentation rejected: ${receipt.diagnostics.map((entry) => entry.message).join('; ')}`,
          );
        }
        document.body.dataset['daggerPresentationOpCount'] = String(opCount);
      }
    }
    if (!ownsControlState || state.inputSequence !== latestAppliedInputSequence) return;
    renderer.setCameraPose(state.camera);
    document.body.dataset['daggerInputSequence'] = String(state.inputSequence);
    document.body.dataset['daggerCameraYaw'] = String(state.camera.yawDegrees);
    document.body.dataset['daggerCameraPitch'] = String(state.camera.pitchDegrees);
    document.body.dataset['daggerAuthoritativePosition'] = state.playerPosition.join(',');
    document.body.dataset['daggerPatrolDebug'] = String(state.patrolDebugEnabled);
    document.body.dataset['daggerNavDebug'] = String(state.navDebugEnabled);
    document.body.dataset['daggerPlayerStamina'] = String(state.playerStamina);
    document.body.dataset['daggerPlayerMaxStamina'] = String(state.playerMaxStamina);
    const melee = state.meleePresentation;
    if (melee === null) {
      delete document.body.dataset['daggerMeleeSequence'];
      delete document.body.dataset['daggerMeleePhase'];
      delete document.body.dataset['daggerMeleeOutcome'];
      delete document.body.dataset['daggerMeleeTarget'];
      delete document.body.dataset['daggerMeleeHealth'];
      delete document.body.dataset['daggerMeleeDamage'];
      delete document.body.dataset['daggerMeleeDied'];
    } else {
      document.body.dataset['daggerMeleeSequence'] = String(melee.attemptSequence);
      document.body.dataset['daggerMeleePhase'] = melee.phase;
      document.body.dataset['daggerMeleeOutcome'] = melee.outcome;
      document.body.dataset['daggerMeleeTarget'] = String(melee.targetId ?? 'none');
      document.body.dataset['daggerMeleeHealth'] =
        `${String(melee.targetHealthBefore ?? 'none')}->${String(melee.targetHealthAfter ?? 'none')}`;
      document.body.dataset['daggerMeleeDamage'] = String(melee.finalDamage ?? 'none');
      document.body.dataset['daggerMeleeDied'] = String(melee.died);
    }
  };
  const drain = async (): Promise<void> => {
    if (sending || disposed) return;
    sending = true;
    try {
      while (pending.length > 0 && !disposed) {
        const input = pending.shift();
        if (input === undefined) break;
        const response = await fetch('/api/dagger-product/input', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(input),
        });
        if (!response.ok) {
          throw new Error(`Dagger product input failed with ${String(response.status)}`);
        }
        await applyState(await response.json() as DaggerProductStateWire);
        delete document.body.dataset['daggerProductInputError'];
      }
    } catch (error: unknown) {
      document.body.dataset['daggerProductInputError'] =
        error instanceof Error ? error.message : String(error);
    } finally {
      sending = false;
      if (pending.length > 0 && !disposed) void drain();
    }
  };
  const submit = (
    sampledStepSeconds: number,
    sampledPointerDelta: readonly [number, number],
    sampledPressedEdges: readonly string[],
    sampledButtonPressedEdges: number,
  ): void => {
    inputSequence += 1;
    document.body.dataset['daggerSampledInputSequence'] = String(inputSequence);
    if (sampledPressedEdges.length > 0) {
      document.body.dataset['daggerLastSampledPressedEdges'] = sampledPressedEdges.join(',');
    }
    const input = {
      sequence: inputSequence,
      stepSeconds: sampledStepSeconds,
      pressedCodes: [...pressed].sort(),
      pressedEdges: sampledPressedEdges,
      pointerDelta: sampledPointerDelta,
      buttons,
      buttonPressedEdges: sampledButtonPressedEdges,
    };
    const previous = pending.at(-1);
    const canCoalesce =
      previous !== undefined
      && previous.buttons === input.buttons
      && previous.pressedCodes.length === input.pressedCodes.length
      && previous.pressedCodes.every((code, index) => code === input.pressedCodes[index])
      && previous.stepSeconds + input.stepSeconds <= MAX_QUEUED_STEP_SECONDS;
    if (canCoalesce) {
      pending[pending.length - 1] = {
        ...input,
        stepSeconds: previous.stepSeconds + input.stepSeconds,
        pressedEdges: [...new Set([...previous.pressedEdges, ...input.pressedEdges])].sort(),
        pointerDelta: [
          previous.pointerDelta[0] + input.pointerDelta[0],
          previous.pointerDelta[1] + input.pointerDelta[1],
        ],
        buttonPressedEdges: previous.buttonPressedEdges | input.buttonPressedEdges,
      };
    } else {
      pending.push(input);
    }
    void drain();
  };
  const flushInput = (): void => {
    const sampledAtMs = performance.now();
    const sampledStepSeconds = Math.min(
      MAX_SAMPLED_STEP_SECONDS,
      Math.max(MIN_INPUT_STEP_SECONDS, (sampledAtMs - lastInputSampleAtMs) / 1_000),
    );
    lastInputSampleAtMs = sampledAtMs;
    const activeInput =
      pressed.size > 0
      || buttons !== 0
      || pressedEdges.size > 0
      || buttonPressedEdges !== 0
      || pointerDelta[0] !== 0
      || pointerDelta[1] !== 0;
    if (!activeInput && !inputChanged) return;
    const sampledPointerDelta: readonly [number, number] = pointerDelta;
    const sampledPressedEdges = [...pressedEdges].sort();
    const sampledButtonPressedEdges = buttonPressedEdges;
    pointerDelta = [0, 0];
    pressedEdges.clear();
    buttonPressedEdges = 0;
    inputChanged = false;
    submit(
      sampledStepSeconds,
      sampledPointerDelta,
      sampledPressedEdges,
      sampledButtonPressedEdges,
    );
  };
  const onKeyDown = (event: KeyboardEvent): void => {
    if (event.code === 'Escape') {
      pressed.clear();
      pressedEdges.clear();
      buttons = 0;
      inputChanged = true;
      context.ui.setInteractionMode('interface');
      window.dispatchEvent(new Event('dagger-open-lab'));
      flushInput();
      return;
    }
    if (event.repeat || !context.ui.allowsGameplayInput(event)) return;
    if (event.code === 'Space') resumeAudioFromGesture();
    if (!pressed.has(event.code)) pressedEdges.add(event.code);
    pressed.add(event.code);
    inputChanged = true;
  };
  const onKeyUp = (event: KeyboardEvent): void => {
    pressed.delete(event.code);
    inputChanged = true;
  };
  const onMouseMove = (event: MouseEvent): void => {
    if (document.pointerLockElement === null || !context.ui.allowsGameplayInput(event)) return;
    buttons = event.buttons;
    pointerDelta = [
      pointerDelta[0] + event.movementX,
      pointerDelta[1] + event.movementY,
    ];
    inputChanged = true;
  };
  const onMouseDown = (event: MouseEvent): void => {
    if (!context.ui.allowsGameplayInput(event)) return;
    if (event.button === 0) resumeAudioFromGesture();
    buttons = event.buttons;
    buttonPressedEdges |= 1 << event.button;
    inputChanged = true;
  };
  const onMouseUp = (event: MouseEvent): void => {
    buttons = event.buttons;
    inputChanged = true;
  };
  const onBlur = (): void => {
    pressed.clear();
    pressedEdges.clear();
    buttons = 0;
    buttonPressedEdges = 0;
    inputChanged = true;
    flushInput();
  };
  window.addEventListener('keydown', onKeyDown);
  window.addEventListener('keyup', onKeyUp);
  window.addEventListener('mousemove', onMouseMove);
  window.addEventListener('mousedown', onMouseDown);
  window.addEventListener('mouseup', onMouseUp);
  window.addEventListener('blur', onBlur);
  const inputTick = window.setInterval(flushInput, INPUT_SAMPLE_INTERVAL_MS);
  const pollState = async (): Promise<void> => {
    if (polling || disposed) return;
    polling = true;
    try {
      const response = await fetch('/api/dagger-product/state', { cache: 'no-store' });
        if (!response.ok) {
          throw new Error(`Dagger product state failed with ${String(response.status)}`);
        }
      await applyState(await response.json() as DaggerProductStateWire);
      delete document.body.dataset['daggerProductStateError'];
    } catch (error: unknown) {
      document.body.dataset['daggerProductStateError'] =
        error instanceof Error ? error.message : String(error);
    } finally {
      polling = false;
    }
  };
  const poll = window.setInterval(() => void pollState(), 100);
  return {
    dispose: () => {
      disposed = true;
      pending.length = 0;
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

  function resumeAudioFromGesture(): void {
    void renderer.resumeAudio().then((receipt) => {
      if (receipt.resumed) {
        delete document.body.dataset['daggerAudioResumeError'];
      } else {
        document.body.dataset['daggerAudioResumeError'] = receipt.diagnostics
          .map((entry) => entry.message)
          .join('; ');
      }
    });
  }
}

function decodeBase64(encoded: string): Uint8Array {
  const binary = atob(encoded);
  const bytes = new Uint8Array(binary.length);
  for (let index = 0; index < binary.length; index += 1) bytes[index] = binary.charCodeAt(index);
  return bytes;
}
