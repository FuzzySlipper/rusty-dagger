import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectorRef, Component, OnDestroy, OnInit, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { LabApiService } from './lab-api.service';
import {
  SpriteAnimation,
  SpriteEntry,
  SpriteFrameRect,
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
  private readonly api = inject(LabApiService);
  private readonly changeDetector = inject(ChangeDetectorRef);
  private timer: ReturnType<typeof setInterval> | undefined;

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
      this.entries = normalizeSpriteIndex(index);
      this.loaded = true;
      this.selectedKey = this.selectedKey ?? this.entries.at(0)?.key;
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
    return (
      animation.frameStart + this.orientation * animation.framesPerOrientation + this.step
    );
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
    return `/api/dagger-lab/sprites/asset/${entry.imagePath}`;
  }

  entryTestId(entry: SpriteEntry): string {
    return `sprite-entry-${entry.key.replace(/[^a-z0-9]+/gi, '-')}`;
  }

  trackEntry(_index: number, entry: SpriteEntry): string {
    return entry.key;
  }

  format(value: number | undefined): string {
    return value === undefined ? '—' : value.toFixed(2);
  }

  /// CSS sprite blitting: scale the atlas as a background image and shift it
  /// so exactly one frame rect shows. Pixelated scaling keeps the classic art
  /// crisp; no canvas is involved (Engine owns the sole product canvas).
  /// Sprite atlases are stored bottom-up (see SpriteEntry.flipY), so the
  /// pixels layer is flipped for display while the frame box and pivot marker
  /// keep their normal orientation.
  private frameMetrics(
    entry: SpriteEntry,
    rect: SpriteFrameRect,
    boxWidth: number,
    boxHeight: number,
  ): { width: number; height: number; scale: number } {
    const width = Math.max(1, (rect.uvMax[0] - rect.uvMin[0]) * entry.imageWidth);
    const height = Math.max(1, (rect.uvMax[1] - rect.uvMin[1]) * entry.imageHeight);
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
    const style: Record<string, string> = {
      'background-image': `url("${this.assetUrl(entry)}")`,
      'background-size': `${entry.imageWidth * scale}px ${entry.imageHeight * scale}px`,
      'background-position': `${-rect.uvMin[0] * entry.imageWidth * scale}px ${-rect.uvMin[1] * entry.imageHeight * scale}px`,
    };
    if (entry.flipY) style['transform'] = 'scaleY(-1)';
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
      if (this.step >= active.framesPerOrientation) {
        if (active.loop) {
          this.step = 0;
        } else {
          this.step = active.framesPerOrientation - 1;
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
