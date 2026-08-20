import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectorRef, Component, OnDestroy, OnInit, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { LabToolsApiService } from './lab-tools-api.service';
import {
  SpriteAnimation,
  SpriteEntry,
  SpriteFrameRect,
  displayFrames,
  normalizeSpriteIndex,
} from './sprite-contract';

export interface SpriteGroup {
  readonly manifest: string;
  readonly entries: SpriteEntry[];
}

const STAGE_WIDTH = 320;
const STAGE_HEIGHT = 240;
const THUMB_SIZE = 56;

@Component({
  selector: 'dagger-sprites-panel',
  imports: [CommonModule, FormsModule],
  templateUrl: './sprites-panel.component.html',
})
export class SpritesPanelComponent implements OnInit, OnDestroy {
  private readonly api = inject(LabToolsApiService);
  private readonly changeDetector = inject(ChangeDetectorRef);
  private timer: ReturnType<typeof setInterval> | undefined;
  /** Decoded atlas pixel sizes, keyed by entry. Manifests are not consistent
   * about whether width/height describe the atlas or one frame (animated
   * billboards record the frame), so the blitting math trusts the decoded
   * image and falls back to manifest dims only until it loads. */
  private readonly naturalDims = new Map<string, { width: number; height: number }>();

  entries: SpriteEntry[] = [];
  loadError = '';
  loaded = false;
  filter = '';
  selectedKey: string | undefined;
  animationIndex = 0;
  orientation = 0;
  playing = false;
  step = 0;
  /** 0 = fit the stage box. */
  zoom = 0;
  inspectedFrame: number | undefined;
  /** Raw manifest documents, as served; edits mutate these and save whole. */
  private manifestDocs: Record<string, Record<string, unknown>> = {};
  readonly dirtyManifests = new Set<string>();
  saving = false;
  saveStatus = '';

  ngOnInit(): void {
    void this.load();
  }

  ngOnDestroy(): void {
    this.stopTimer();
  }

  async load(): Promise<void> {
    this.loadError = '';
    try {
      const index = await this.api.spriteIndex();
      this.manifestDocs = index.manifests as Record<string, Record<string, unknown>>;
      this.entries = normalizeSpriteIndex(index);
      this.loaded = true;
      this.selectedKey = this.selectedKey ?? this.entries.at(0)?.key;
      const selected = this.selected();
      if (selected !== undefined) this.preloadDims(selected);
    } catch (error: unknown) {
      this.loadError = errorMessage(error);
    } finally {
      this.changeDetector.markForCheck();
    }
  }

  private groupsCache: { filter: string; entries: readonly SpriteEntry[]; groups: SpriteGroup[] } | undefined;

  groups(): SpriteGroup[] {
    // Memoized: the lab poll ticks change detection every 250 ms, and
    // returning fresh group arrays each time makes the outer ngFor rebuild
    // the nav DOM — resetting scroll and eating clicks.
    const cache = this.groupsCache;
    if (cache !== undefined && cache.filter === this.filter && cache.entries === this.entries) {
      return cache.groups;
    }
    const filter = this.filter.trim().toLowerCase();
    const groups: SpriteGroup[] = [];
    let current: { manifest: string; entries: SpriteEntry[] } | undefined;
    for (const entry of this.entries) {
      if (filter !== '' && !`${entry.label} ${entry.detail}`.toLowerCase().includes(filter)) {
        continue;
      }
      if (current === undefined || current.manifest !== entry.manifest) {
        current = { manifest: entry.manifest, entries: [] };
        groups.push(current);
      }
      current.entries.push(entry);
    }
    this.groupsCache = { filter: this.filter, entries: this.entries, groups };
    return groups;
  }

  trackGroup(_index: number, group: SpriteGroup): string {
    return group.manifest;
  }

  selected(): SpriteEntry | undefined {
    return this.entries.find((entry) => entry.key === this.selectedKey);
  }

  selectEntry(entry: SpriteEntry): void {
    this.selectedKey = entry.key;
    this.animationIndex = 0;
    this.orientation = 0;
    this.step = 0;
    this.inspectedFrame = undefined;
    this.stopTimer();
    this.preloadDims(entry);
  }

  atlasDims(entry: SpriteEntry): { width: number; height: number } {
    return (
      this.naturalDims.get(entry.key) ?? { width: entry.imageWidth, height: entry.imageHeight }
    );
  }

  private preloadDims(entry: SpriteEntry): void {
    if (entry.imagePath === '' || this.naturalDims.has(entry.key)) return;
    const image = new Image();
    image.onload = () => {
      this.naturalDims.set(entry.key, {
        width: image.naturalWidth,
        height: image.naturalHeight,
      });
      this.changeDetector.markForCheck();
    };
    image.src = this.assetUrl(entry);
  }

  animation(): SpriteAnimation | undefined {
    return this.selected()?.animations[this.animationIndex];
  }

  selectAnimation(index: number): void {
    this.animationIndex = index;
    this.step = 0;
    this.inspectedFrame = undefined;
    if (this.playing) this.startTimer();
  }

  selectOrientation(orientation: number): void {
    this.orientation = orientation;
    this.step = 0;
  }

  orientations(): number[] {
    const count = this.animation()?.orientationCount ?? 1;
    return Array.from({ length: count }, (_value, index) => index);
  }

  orientationLabel(orientation: number): string {
    const count = this.animation()?.orientationCount ?? 1;
    if (count <= 1) return 'single view';
    const degrees = Math.round((orientation * 360) / count);
    if (orientation === 0) return 'front · 0°';
    if (degrees === 180) return 'back · 180°';
    return `${degrees}°`;
  }

  togglePlay(): void {
    if (this.playing) {
      this.stopTimer();
    } else {
      this.step = 0;
      this.startTimer();
    }
  }

  /** Absolute atlas frame index currently shown on the stage. */
  currentFrame(): number | undefined {
    const entry = this.selected();
    if (entry === undefined) return undefined;
    if (this.inspectedFrame !== undefined && !this.playing) return this.inspectedFrame;
    const animation = this.animation();
    if (animation === undefined) return entry.frames[0]?.frame;
    const frames = displayFrames(animation);
    const animIndex = frames[Math.min(this.step, frames.length - 1)] ?? 0;
    return (
      animation.frameStart + this.orientation * animation.framesPerOrientation + animIndex
    );
  }

  /** Visible beats of the selected animation (classic sequence when present). */
  beats(animation: SpriteAnimation): number {
    return displayFrames(animation).length;
  }

  /** Classic sequence as review text; ⚔ marks the melee damage beat. */
  sequenceText(sequence: readonly number[]): string {
    return sequence.map((frame) => (frame === -1 ? '⚔' : String(frame))).join(' ');
  }

  currentRect(): SpriteFrameRect | undefined {
    const entry = this.selected();
    const frame = this.currentFrame();
    if (entry === undefined || frame === undefined) return undefined;
    return rectFor(entry, frame);
  }

  inspectFrame(frame: number): void {
    this.inspectedFrame = frame;
    this.stopTimer();
  }

  assetUrl(entry: SpriteEntry): string {
    return `/api/dagger-tools/sprites/asset/${entry.imagePath}`;
  }

  entryTestId(entry: SpriteEntry): string {
    return `sprite-entry-${entry.key.replace(/[^a-z0-9]+/gi, '-')}`;
  }

  trackEntry(_index: number, entry: SpriteEntry): string {
    return entry.key;
  }

  // --- Manifest editing --------------------------------------------------
  // Edits mutate the raw manifest document and set the entry's "edited"
  // marker; the exporter preserves marked tunable fields across regeneration
  // (DAGGER_CLOBBER_SPRITES=1 ignores markers). Saving posts the whole
  // manifest; the tooling service then restamps the project documents.

  /** The raw manifest entry object behind a normalized sprite entry. */
  rawEntry(entry: SpriteEntry): Record<string, unknown> | undefined {
    const doc = this.manifestDocs[entry.manifest];
    if (doc === undefined) return undefined;
    const id = entry.key.slice(entry.key.indexOf(':') + 1);
    const records = (list: unknown): Record<string, unknown>[] =>
      Array.isArray(list)
        ? list.filter(
            (item): item is Record<string, unknown> =>
              typeof item === 'object' && item !== null,
          )
        : [];
    if (id.startsWith('mobile-')) {
      return records(doc['enemies']).find((e) => String(e['mobileId']) === id.slice(7));
    }
    if (id.startsWith('billboard-')) {
      const [archive, record] = id.slice(10).split('-');
      return records(doc['billboards']).find(
        (e) => String(e['archive']) === archive && String(e['record']) === record,
      );
    }
    const weapon = doc['weapon'];
    if (typeof weapon === 'object' && weapon !== null) {
      const weaponDoc = weapon as Record<string, unknown>;
      if (weaponDoc['id'] === id) return weaponDoc;
    }
    const effect = records(doc['effects']).find((e) => e['id'] === id);
    if (effect !== undefined) return effect;
    return records(doc['textures']).find((e) => e['path'] === id);
  }

  isEdited(entry: SpriteEntry): boolean {
    return this.rawEntry(entry)?.['edited'] === true;
  }

  dirtyList(): string {
    return [...this.dirtyManifests].join(', ');
  }

  pivotEditable(entry: SpriteEntry): boolean {
    return entry.pivot !== undefined;
  }

  pivotValue(entry: SpriteEntry, axis: 0 | 1): number {
    const pivot = this.rawEntry(entry)?.['pivot'];
    return Array.isArray(pivot) && typeof pivot[axis] === 'number' ? pivot[axis] : 0.5;
  }

  setPivotAxis(entry: SpriteEntry, axis: 0 | 1, value: number): void {
    const raw = this.rawEntry(entry);
    if (raw === undefined || !Number.isFinite(value)) return;
    const pivot: [number, number] = [this.pivotValue(entry, 0), this.pivotValue(entry, 1)];
    pivot[axis] = Math.round(value * 1000) / 1000;
    raw['pivot'] = pivot;
    this.markEdited(entry);
  }

  /** Drag the stage pivot marker; pivot origin is the quad's bottom-left. */
  startPivotDrag(event: PointerEvent, entry: SpriteEntry): void {
    const marker = event.currentTarget as HTMLElement | null;
    const box = marker?.parentElement;
    if (marker === null || box === null || box === undefined) return;
    event.preventDefault();
    marker.setPointerCapture(event.pointerId);
    const rect = box.getBoundingClientRect();
    const move = (e: PointerEvent) => {
      const x = Math.min(Math.max((e.clientX - rect.left) / rect.width, 0), 1);
      const y = 1 - Math.min(Math.max((e.clientY - rect.top) / rect.height, 0), 1);
      this.setPivotAxis(entry, 0, x);
      this.setPivotAxis(entry, 1, y);
    };
    const stop = () => {
      marker.removeEventListener('pointermove', move);
      marker.removeEventListener('pointerup', stop);
      marker.removeEventListener('pointercancel', stop);
    };
    marker.addEventListener('pointermove', move);
    marker.addEventListener('pointerup', stop);
    marker.addEventListener('pointercancel', stop);
  }

  /** The raw object carrying an animation's fps/loop fields. */
  private animationDoc(
    entry: SpriteEntry,
    animation: SpriteAnimation,
  ): Record<string, unknown> | undefined {
    const raw = this.rawEntry(entry);
    if (raw === undefined) return undefined;
    const states = raw['states'];
    if (typeof states === 'object' && states !== null && animation.name in states) {
      return (states as Record<string, unknown>)[animation.name] as Record<string, unknown>;
    }
    const animations = raw['animations'];
    if (Array.isArray(animations)) {
      const match = animations.find(
        (item) =>
          typeof item === 'object' &&
          item !== null &&
          (item as Record<string, unknown>)['action'] === animation.name,
      );
      if (match !== undefined) return match as Record<string, unknown>;
    }
    return raw;
  }

  animationField(entry: SpriteEntry, animation: SpriteAnimation, field: string): unknown {
    return this.animationDoc(entry, animation)?.[field];
  }

  hasAnimationLoop(entry: SpriteEntry, animation: SpriteAnimation): boolean {
    return this.animationDoc(entry, animation)?.['loop'] !== undefined;
  }

  setAnimationField(
    entry: SpriteEntry,
    animation: SpriteAnimation,
    field: 'fps' | 'loop',
    value: number | boolean,
  ): void {
    const doc = this.animationDoc(entry, animation);
    if (doc === undefined) return;
    if (field === 'fps' && (typeof value !== 'number' || !Number.isFinite(value))) return;
    doc[field] = field === 'fps' ? Math.max(0, Math.round(value as number)) : value === true;
    this.markEdited(entry);
  }

  /** Numeric fields of the inspected frame rect: uvMin/uvMax corners. */
  setFrameUv(entry: SpriteEntry, corner: 'uvMin' | 'uvMax', axis: 0 | 1, value: number): void {
    const raw = this.rawEntry(entry);
    const frame = this.currentFrame();
    if (raw === undefined || frame === undefined || !Number.isFinite(value)) return;
    const frames = raw['frames'];
    if (!Array.isArray(frames)) return;
    const target = frames.find(
      (item) =>
        typeof item === 'object' &&
        item !== null &&
        (item as Record<string, unknown>)['frame'] === frame,
    ) as Record<string, unknown> | undefined;
    const rect = target?.[corner];
    if (!Array.isArray(rect) || rect.length < 2) return;
    rect[axis] = Math.round(value * 10000) / 10000;
    this.markEdited(entry);
  }

  frameUvValue(corner: 'uvMin' | 'uvMax', axis: 0 | 1): number {
    const rect = this.currentRect();
    if (rect === undefined) return 0;
    return corner === 'uvMin' ? rect.uvMin[axis] : rect.uvMax[axis];
  }

  setWorldSizeAxis(entry: SpriteEntry, field: 'worldSize' | 'normalizedSize', axis: 0 | 1, value: number): void {
    const raw = this.rawEntry(entry);
    if (raw === undefined || !Number.isFinite(value)) return;
    const size = raw[field];
    if (!Array.isArray(size) || size.length < 2) return;
    size[axis] = Math.round(value * 1000) / 1000;
    this.markEdited(entry);
  }

  worldSizeValue(entry: SpriteEntry, axis: 0 | 1): number {
    return entry.worldSize?.[axis] ?? 0;
  }

  /** Remove the hand-edit marker; classic defaults return on next full regeneration. */
  clearEdits(entry: SpriteEntry): void {
    const raw = this.rawEntry(entry);
    if (raw === undefined) return;
    delete raw['edited'];
    this.dirtyManifests.add(entry.manifest);
    this.saveStatus = 'Edit marker cleared — classic defaults return on next full regeneration (save to persist the cleared marker).';
    this.changeDetector.markForCheck();
  }

  async saveAll(): Promise<void> {
    this.saving = true;
    this.saveStatus = '';
    const problems: string[] = [];
    try {
      for (const name of this.dirtyManifests) {
        const doc = this.manifestDocs[name];
        if (doc === undefined) continue;
        const result = await this.api.saveSpriteManifest(name, doc);
        if (result.projectDocs !== 'regenerated') {
          problems.push(`${name}: project docs ${result.projectDocs}`);
        }
      }
      this.dirtyManifests.clear();
      this.saveStatus =
        problems.length > 0
          ? `Saved, but ${problems.join('; ')}`
          : 'Saved; project docs restamped.';
    } catch (error: unknown) {
      this.saveStatus = error instanceof Error ? error.message : 'save failed';
    } finally {
      this.saving = false;
      this.changeDetector.markForCheck();
    }
  }

  async discardAll(): Promise<void> {
    this.dirtyManifests.clear();
    this.saveStatus = '';
    await this.load();
  }

  private markEdited(entry: SpriteEntry): void {
    const raw = this.rawEntry(entry);
    if (raw === undefined) return;
    raw['edited'] = true;
    this.dirtyManifests.add(entry.manifest);
    // Rebuild the normalized view so the stage reflects the edit live.
    this.entries = normalizeSpriteIndex({ manifests: this.manifestDocs, files: {} });
    this.changeDetector.markForCheck();
  }

  format(value: number | undefined): string {
    return value === undefined ? '—' : value.toFixed(2);
  }

  /// CSS sprite blitting: scale the atlas as a background image and shift it
  /// so exactly one frame rect shows. Pixelated scaling keeps the classic art
  /// crisp; no canvas is involved (Engine owns the sole product canvas).
  /// Atlases are stored upright (Engine's sprite contract samples upright
  /// decoded-image space), matching CSS's top-left origin directly.
  private frameMetrics(
    entry: SpriteEntry,
    rect: SpriteFrameRect,
    boxWidth: number,
    boxHeight: number,
  ): { width: number; height: number; scale: number } {
    const dims = this.atlasDims(entry);
    const width = Math.max(1, (rect.uvMax[0] - rect.uvMin[0]) * dims.width);
    const height = Math.max(1, (rect.uvMax[1] - rect.uvMin[1]) * dims.height);
    const scale =
      this.zoom > 0
        ? this.zoom
        : Math.max(0.05, Math.min(boxWidth / width, boxHeight / height, 8));
    return { width, height, scale };
  }

  frameBoxStyle(
    entry: SpriteEntry,
    rect: SpriteFrameRect,
    boxWidth: number,
    boxHeight: number,
  ): Record<string, string> {
    const { width, height, scale } = this.frameMetrics(entry, rect, boxWidth, boxHeight);
    return { width: `${width * scale}px`, height: `${height * scale}px` };
  }

  framePixelStyle(
    entry: SpriteEntry,
    rect: SpriteFrameRect,
    boxWidth: number,
    boxHeight: number,
  ): Record<string, string> {
    const { scale } = this.frameMetrics(entry, rect, boxWidth, boxHeight);
    const dims = this.atlasDims(entry);
    const style: Record<string, string> = {
      'background-image': `url("${this.assetUrl(entry)}")`,
      'background-size': `${dims.width * scale}px ${dims.height * scale}px`,
      'background-position': `${-rect.uvMin[0] * dims.width * scale}px ${-rect.uvMin[1] * dims.height * scale}px`,
    };
    return style;
  }

  stageBoxStyle(entry: SpriteEntry, rect: SpriteFrameRect): Record<string, string> {
    return this.frameBoxStyle(entry, rect, STAGE_WIDTH, STAGE_HEIGHT);
  }

  stagePixelStyle(entry: SpriteEntry, rect: SpriteFrameRect): Record<string, string> {
    return this.framePixelStyle(entry, rect, STAGE_WIDTH, STAGE_HEIGHT);
  }

  thumbBoxStyle(entry: SpriteEntry, rect: SpriteFrameRect): Record<string, string> {
    return this.frameBoxStyle(entry, rect, THUMB_SIZE, THUMB_SIZE);
  }

  thumbPixelStyle(entry: SpriteEntry, rect: SpriteFrameRect): Record<string, string> {
    return this.framePixelStyle(entry, rect, THUMB_SIZE, THUMB_SIZE);
  }

  pivotStyle(entry: SpriteEntry, rect: SpriteFrameRect): Record<string, string> | undefined {
    const pivot = entry.pivot;
    if (pivot === undefined || pivot.length < 2) return undefined;
    const { width, height, scale } = this.frameMetrics(entry, rect, STAGE_WIDTH, STAGE_HEIGHT);
    const pivotX = pivot[0] ?? 0;
    const pivotY = pivot[1] ?? 0;
    return {
      // Engine sprite pivots are measured from the bottom-left of the quad.
      left: `${pivotX * width * scale}px`,
      bottom: `${pivotY * height * scale}px`,
    };
  }

  private startTimer(): void {
    this.stopTimer();
    const animation = this.animation();
    if (animation === undefined || animation.framesPerOrientation <= 0) {
      return;
    }
    this.playing = true;
    const fps = animation.fps > 0 ? animation.fps : 4;
    this.timer = setInterval(() => {
      const active = this.animation();
      if (active === undefined) {
        this.stopTimer();
        return;
      }
      this.step += 1;
      const beats = displayFrames(active).length;
      if (this.step >= beats) {
        if (active.loop) {
          this.step = 0;
        } else {
          this.step = beats - 1;
          this.stopTimer();
        }
      }
      this.changeDetector.markForCheck();
    }, 1000 / fps);
  }

  private stopTimer(): void {
    this.playing = false;
    if (this.timer !== undefined) {
      clearInterval(this.timer);
      this.timer = undefined;
    }
  }
}

export function rectFor(entry: SpriteEntry, frame: number): SpriteFrameRect | undefined {
  const direct = entry.frames[frame];
  if (direct !== undefined && direct.frame === frame) return direct;
  return entry.frames.find((rect) => rect.frame === frame);
}

function errorMessage(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    return `sprite index request failed (${error.status})`;
  }
  return error instanceof Error ? error.message : 'sprite index request failed';
}
