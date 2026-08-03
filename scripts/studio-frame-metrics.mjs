#!/usr/bin/env node
/**
 * PNG frame metrics for the Studio browser proof — no native dependencies.
 *
 * Decodes 8-bit RGB/RGBA non-interlaced PNGs (the shape Playwright writes),
 * then reports the metrics the textured-render proof asserts on:
 *
 * - occupancy: share of pixels differing from the renderer clear color
 *   (sampled from the frame corners) — the "meaningful project pixels" gate.
 * - uniqueColors: distinct RGB values among geometry pixels. The average-color
 *   fallback renders one flat color per material face, so smooth lighting
 *   keeps this in the low hundreds; the textured render produces thousands.
 * - textureCells: 6x6 grid cells dominated by geometry whose per-pixel
 *   luminance standard deviation is high — high-frequency texel alternation
 *   that flat-shaded polygons cannot produce.
 * - hueHistogram: 12-bin normalized hue histogram over geometry pixels, used
 *   for the documented-tolerance comparison against the committed GLB render
 *   reference (histogramIntersection).
 *
 * CLI: `node scripts/studio-frame-metrics.mjs <image.png> [...]` prints the
 * metric JSON per image (used for calibration and inspection).
 */
import { readFile } from 'node:fs/promises';
import { inflateSync } from 'node:zlib';

const PNG_SIGNATURE = Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);

export function decodePng(buffer) {
  if (!buffer.subarray(0, 8).equals(PNG_SIGNATURE)) throw new Error('not a PNG');
  let pos = 8;
  let width = 0;
  let height = 0;
  let bitDepth = 0;
  let colorType = 0;
  let interlace = 0;
  const idat = [];
  while (pos < buffer.length) {
    const length = buffer.readUInt32BE(pos);
    const type = buffer.subarray(pos + 4, pos + 8).toString('ascii');
    if (type === 'IHDR') {
      width = buffer.readUInt32BE(pos + 8);
      height = buffer.readUInt32BE(pos + 12);
      bitDepth = buffer[pos + 16];
      colorType = buffer[pos + 17];
      interlace = buffer[pos + 20];
    } else if (type === 'IDAT') {
      idat.push(buffer.subarray(pos + 8, pos + 8 + length));
    } else if (type === 'IEND') {
      break;
    }
    pos += 12 + length;
  }
  if (bitDepth !== 8 || (colorType !== 2 && colorType !== 6) || interlace !== 0) {
    throw new Error(`unsupported PNG encoding: bitDepth=${bitDepth} colorType=${colorType} interlace=${interlace}`);
  }
  const channels = colorType === 6 ? 4 : 3;
  const stride = width * channels;
  const raw = inflateSync(Buffer.concat(idat));
  const rgb = new Uint8Array(width * height * 3);
  let previous = new Uint8Array(stride);
  for (let y = 0; y < height; y += 1) {
    const filter = raw[y * (stride + 1)];
    const row = raw.subarray(y * (stride + 1) + 1, (y + 1) * (stride + 1));
    const out = new Uint8Array(stride);
    for (let x = 0; x < stride; x += 1) {
      const left = x >= channels ? out[x - channels] : 0;
      const up = previous[x];
      const upLeft = x >= channels ? previous[x - channels] : 0;
      let value;
      switch (filter) {
        case 0:
          value = row[x];
          break;
        case 1:
          value = row[x] + left;
          break;
        case 2:
          value = row[x] + up;
          break;
        case 3:
          value = row[x] + Math.floor((left + up) / 2);
          break;
        case 4: {
          const estimate = left + up - upLeft;
          const distanceLeft = Math.abs(estimate - left);
          const distanceUp = Math.abs(estimate - up);
          const distanceUpLeft = Math.abs(estimate - upLeft);
          value = row[x]
            + (distanceLeft <= distanceUp && distanceLeft <= distanceUpLeft
              ? left
              : distanceUp <= distanceUpLeft
                ? up
                : upLeft);
          break;
        }
        default:
          throw new Error(`unsupported PNG filter ${filter}`);
      }
      out[x] = value & 0xff;
    }
    for (let x = 0, offset = y * width * 3; x < width; x += 1, offset += 3) {
      rgb[offset] = out[x * channels];
      rgb[offset + 1] = out[x * channels + 1];
      rgb[offset + 2] = out[x * channels + 2];
    }
    previous = out;
  }
  return { width, height, rgb };
}

function backgroundColor(image) {
  const { width, height, rgb } = image;
  const corners = [
    [2, 2],
    [width - 3, 2],
    [2, height - 3],
    [width - 3, height - 3],
  ].map(([x, y]) => {
    const offset = (y * width + x) * 3;
    return [rgb[offset], rgb[offset + 1], rgb[offset + 2]];
  });
  const counts = new Map();
  for (const corner of corners) {
    const key = corner.join(',');
    counts.set(key, (counts.get(key) ?? 0) + 1);
  }
  let best = corners[0];
  let bestCount = 0;
  for (const corner of corners) {
    const count = counts.get(corner.join(','));
    if (count > bestCount) {
      best = corner;
      bestCount = count;
    }
  }
  return best;
}

function luminance(r, g, b) {
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}

function hueBin(r, g, b) {
  const max = Math.max(r, g, b);
  const min = Math.min(r, g, b);
  const delta = max - min;
  if (delta === 0 || max === 0) return null;
  const saturation = delta / max;
  if (saturation < 0.10 || max < 20) return null;
  let hue;
  if (max === r) hue = ((g - b) / delta) % 6;
  else if (max === g) hue = (b - r) / delta + 2;
  else hue = (r - g) / delta + 4;
  hue *= 60;
  if (hue < 0) hue += 360;
  return Math.min(11, Math.floor(hue / 30));
}

export function frameMetrics(image, { backgroundDelta = 24, grid = 6, textureStddev = 6 } = {}) {
  const { width, height, rgb } = image;
  const background = backgroundColor(image);
  const total = width * height;
  const geometry = new Uint8Array(total);
  let geometryCount = 0;
  const unique = new Set();
  const hue = new Array(12).fill(0);
  let hueCount = 0;
  for (let index = 0; index < total; index += 1) {
    const offset = index * 3;
    const r = rgb[offset];
    const g = rgb[offset + 1];
    const b = rgb[offset + 2];
    const delta = Math.abs(r - background[0]) + Math.abs(g - background[1]) + Math.abs(b - background[2]);
    if (delta <= backgroundDelta) continue;
    geometry[index] = 1;
    geometryCount += 1;
    unique.add((r << 16) | (g << 8) | b);
    const bin = hueBin(r, g, b);
    if (bin !== null) {
      hue[bin] += 1;
      hueCount += 1;
    }
  }
  const cellWidth = Math.floor(width / grid);
  const cellHeight = Math.floor(height / grid);
  let geometryCells = 0;
  let textureCells = 0;
  let maxCellStddev = 0;
  for (let cy = 0; cy < grid; cy += 1) {
    for (let cx = 0; cx < grid; cx += 1) {
      let cellPixels = 0;
      let cellGeometry = 0;
      let sum = 0;
      let sumSquares = 0;
      for (let y = cy * cellHeight; y < (cy + 1) * cellHeight; y += 1) {
        for (let x = cx * cellWidth; x < (cx + 1) * cellWidth; x += 1) {
          const index = y * width + x;
          cellPixels += 1;
          if (!geometry[index]) continue;
          cellGeometry += 1;
          const offset = index * 3;
          const lum = luminance(rgb[offset], rgb[offset + 1], rgb[offset + 2]);
          sum += lum;
          sumSquares += lum * lum;
        }
      }
      if (cellGeometry < cellPixels * 0.4 || cellGeometry < 64) continue;
      geometryCells += 1;
      const mean = sum / cellGeometry;
      const variance = Math.max(0, sumSquares / cellGeometry - mean * mean);
      const stddev = Math.sqrt(variance);
      if (stddev > maxCellStddev) maxCellStddev = stddev;
      if (stddev >= textureStddev) textureCells += 1;
    }
  }
  return {
    width,
    height,
    background,
    occupancy: total === 0 ? 0 : geometryCount / total,
    geometryPixels: geometryCount,
    uniqueColors: unique.size,
    geometryCells,
    textureCells,
    maxCellStddev: Math.round(maxCellStddev * 100) / 100,
    hueHistogram: hue.map((count) => (hueCount === 0 ? 0 : count / hueCount)),
    huePixels: hueCount,
  };
}

export function differenceRatio(before, after) {
  if (before.width !== after.width || before.height !== after.height) {
    throw new Error('frame sizes differ');
  }
  const total = before.width * before.height;
  let changed = 0;
  for (let index = 0; index < total; index += 1) {
    const offset = index * 3;
    const delta = Math.max(
      Math.abs(before.rgb[offset] - after.rgb[offset]),
      Math.abs(before.rgb[offset + 1] - after.rgb[offset + 1]),
      Math.abs(before.rgb[offset + 2] - after.rgb[offset + 2]),
    );
    if (delta > 12) changed += 1;
  }
  return total === 0 ? 0 : changed / total;
}

export function histogramIntersection(left, right) {
  let intersection = 0;
  for (let index = 0; index < left.length; index += 1) {
    intersection += Math.min(left[index], right[index] ?? 0);
  }
  return intersection;
}

if (process.argv[1] && import.meta.url === new URL(`file://${process.argv[1]}`).href) {
  for (const path of process.argv.slice(2)) {
    const image = decodePng(await readFile(path));
    console.log(path);
    console.log(JSON.stringify(frameMetrics(image), null, 1));
  }
}
