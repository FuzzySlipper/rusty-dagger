import { useEffect, useMemo, useRef, useState } from 'preact/hooks';
import type {
  AtlasBaseMode,
  AtlasOverlayMode,
  WorldAtlasMasksResponse,
  WorldAtlasOverviewResponse,
  WorldAtlasPinTuple,
  WorldAtlasRegion,
} from '../types';

export interface AtlasFlyToRequest {
  id: number;
  x?: number;
  y?: number;
  zoom?: number;
  bounds?: { minX: number; minY: number; maxX: number; maxY: number };
}

export interface DecodedAtlasMasks {
  width: number;
  height: number;
  politic: Int16Array;
  climate: Uint8Array;
}

export function decodeAtlasMasks(raw: WorldAtlasMasksResponse): DecodedAtlasMasks {
  const total = raw.width * raw.height;
  const politic = new Int16Array(total);
  politic.fill(-1);
  let pos = 0;
  for (let i = 0; i < raw.politicRuns.length; i += 2) {
    const val = raw.politicRuns[i];
    const count = raw.politicRuns[i + 1];
    const end = Math.min(pos + count, total);
    politic.fill(val, pos, end);
    pos = end;
  }

  const climate = new Uint8Array(total);
  pos = 0;
  for (let i = 0; i < raw.climateRuns.length; i += 2) {
    const val = raw.climateRuns[i];
    const count = raw.climateRuns[i + 1];
    const end = Math.min(pos + count, total);
    climate.fill(val, pos, end);
    pos = end;
  }

  return { width: raw.width, height: raw.height, politic, climate };
}

function hexToRgb(hex: string): [number, number, number] {
  const clean = hex.replace('#', '');
  const n = parseInt(clean, 16);
  return [(n >> 16) & 255, (n >> 8) & 255, n & 255];
}

function hslToRgb(h: number, s: number, l: number): [number, number, number] {
  const c = (1 - Math.abs(2 * l - 1)) * s;
  const x = c * (1 - Math.abs(((h / 60) % 2) - 1));
  const m = l - c / 2;
  let r = 0;
  let g = 0;
  let b = 0;
  if (h < 60) [r, g, b] = [c, x, 0];
  else if (h < 120) [r, g, b] = [x, c, 0];
  else if (h < 180) [r, g, b] = [0, c, x];
  else if (h < 240) [r, g, b] = [0, x, c];
  else if (h < 300) [r, g, b] = [x, 0, c];
  else [r, g, b] = [c, 0, x];
  return [Math.round((r + m) * 255), Math.round((g + m) * 255), Math.round((b + m) * 255)];
}

export function regionPaletteRgb(regionId: number): [number, number, number] {
  const hue = (regionId * 137.508 + 22) % 360;
  const sat = 0.56 + ((regionId * 7) % 3) * 0.08;
  const light = 0.48 + ((regionId * 13) % 3) * 0.06;
  return hslToRgb(hue, sat, light);
}

function packRgba(r: number, g: number, b: number, a: number): number {
  return ((a & 255) << 24) | ((b & 255) << 16) | ((g & 255) << 8) | (r & 255);
}

function lerpColor(t: number, a: [number, number, number], b: [number, number, number]): [number, number, number] {
  const u = Math.max(0, Math.min(1, t));
  return [
    Math.round(a[0] + (b[0] - a[0]) * u),
    Math.round(a[1] + (b[1] - a[1]) * u),
    Math.round(a[2] + (b[2] - a[2]) * u),
  ];
}

export function AtlasCanvasView({
  overview,
  masks,
  pins,
  baseMode,
  overlayMode,
  enabledCategories,
  showLabels,
  showDiplomacyArcs,
  selectedRegionId,
  selectedPin,
  flyToRequest,
  onSelectRegion,
  onSelectPin,
}: {
  overview: WorldAtlasOverviewResponse;
  masks: DecodedAtlasMasks | null;
  pins: WorldAtlasPinTuple[];
  baseMode: AtlasBaseMode;
  overlayMode: AtlasOverlayMode;
  enabledCategories: Record<string, boolean>;
  showLabels: boolean;
  showDiplomacyArcs: boolean;
  selectedRegionId: number | null;
  selectedPin: WorldAtlasPinTuple | null;
  flyToRequest: AtlasFlyToRequest | null;
  onSelectRegion: (regionId: number | null) => void;
  onSelectPin: (pin: WorldAtlasPinTuple | null) => void;
}) {
  const wrapRef = useRef<HTMLDivElement>(null);
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const imageCacheRef = useRef<Map<string, HTMLImageElement | 'loading' | 'error'>>(new Map());
  const overlayCanvasRef = useRef<HTMLCanvasElement | null>(null);

  const cameraRef = useRef({
    cx: 500,
    cy: 250,
    zoom: 1,
    minZoom: 0.7,
    maxZoom: 22,
    initialized: false,
  });
  const animRef = useRef<{
    startCx: number;
    startCy: number;
    startZoom: number;
    endCx: number;
    endCy: number;
    endZoom: number;
    startTime: number;
    duration: number;
  } | null>(null);
  const dragRef = useRef<{
    active: boolean;
    moved: boolean;
    startX: number;
    startY: number;
    startCx: number;
    startCy: number;
  }>({ active: false, moved: false, startX: 0, startY: 0, startCx: 500, startCy: 250 });

  const [hoverInfo, setHoverInfo] = useState<{
    x: number;
    y: number;
    regionId: number;
    climateName: string | null;
    pin: WorldAtlasPinTuple | null;
  } | null>(null);
  const [renderTick, setRenderTick] = useState(0);
  const [zoomPct, setZoomPct] = useState(100);

  const regionsById = useMemo(() => {
    const map = new Map<number, WorldAtlasRegion>();
    for (const r of overview.regions) map.set(r.id, r);
    return map;
  }, [overview.regions]);

  const typeToCategory = useMemo(() => {
    const map = new Map<number, { id: string; color: string }>();
    for (const cat of overview.pinCategories) {
      for (const t of cat.types) {
        map.set(t, { id: cat.id, color: cat.color });
      }
    }
    return map;
  }, [overview.pinCategories]);

  const visiblePins = useMemo(() => {
    return pins.filter(p => {
      const cat = typeToCategory.get(p[4]);
      const catId = cat?.id ?? 'other';
      return Boolean(enabledCategories[catId]);
    });
  }, [pins, enabledCategories, typeToCategory]);

  const hoverRegionId = hoverInfo && hoverInfo.regionId >= 0 ? hoverInfo.regionId : null;

  // Rebuild offscreen 1000x500 overlay mask canvas whenever overlay mode / selection / hover changes
  useEffect(() => {
    if (!masks) {
      overlayCanvasRef.current = null;
      return;
    }
    const { width, height, politic, climate } = masks;
    let off = overlayCanvasRef.current;
    if (!off || off.width !== width || off.height !== height) {
      off = document.createElement('canvas');
      off.width = width;
      off.height = height;
      overlayCanvasRef.current = off;
    }
    const ctx = off.getContext('2d');
    if (!ctx) return;
    const imgData = ctx.createImageData(width, height);
    const buf32 = new Uint32Array(imgData.data.buffer);

    const selectedRegion = selectedRegionId !== null ? regionsById.get(selectedRegionId) ?? null : null;
    const allySet = new Set<number>(selectedRegion?.diplomacy.allyRegions ?? []);
    const enemySet = new Set<number>(selectedRegion?.diplomacy.enemyRegions ?? []);

    // Precompute per-region fill color for O(1) pixel lookup
    const regionColor = new Uint32Array(128);
    const regionBorder = new Uint32Array(128);

    const maxPower = Math.max(1, ...overview.regions.map(r => r.province?.power ?? 0));
    const maxDensity = Math.max(
      1,
      ...overview.regions.map(r => (r.pixelCount > 0 ? (r.locations.cities * 6 + r.locations.townsAndVillages * 2 + r.locations.total * 0.25) / Math.sqrt(r.pixelCount) : 0)),
    );

    for (const r of overview.regions) {
      if (r.id < 0 || r.id >= 128) continue;
      if (overlayMode === 'political') {
        const [cr, cg, cb] = regionPaletteRgb(r.id);
        const alpha = r.id === selectedRegionId ? 150 : r.id === hoverRegionId ? 132 : 96;
        regionColor[r.id] = packRgba(cr, cg, cb, alpha);
        regionBorder[r.id] = packRgba(Math.max(0, cr - 45), Math.max(0, cg - 45), Math.max(0, cb - 45), 215);
      } else if (overlayMode === 'diplomacy') {
        if (selectedRegionId !== null) {
          if (r.id === selectedRegionId) {
            regionColor[r.id] = packRgba(255, 209, 102, 150);
            regionBorder[r.id] = packRgba(255, 232, 145, 245);
          } else if (allySet.has(r.id)) {
            regionColor[r.id] = packRgba(77, 224, 150, 145);
            regionBorder[r.id] = packRgba(130, 255, 190, 235);
          } else if (enemySet.has(r.id)) {
            regionColor[r.id] = packRgba(255, 88, 102, 150);
            regionBorder[r.id] = packRgba(255, 145, 155, 235);
          } else {
            regionColor[r.id] = packRgba(24, 30, 36, 95);
            regionBorder[r.id] = packRgba(65, 76, 86, 130);
          }
        } else {
          const hasTies = r.diplomacy.allyRegions.length > 0 || r.diplomacy.enemyRegions.length > 0;
          if (hasTies) {
            const [cr, cg, cb] = regionPaletteRgb(r.id);
            regionColor[r.id] = packRgba(cr, cg, cb, r.id === hoverRegionId ? 135 : 92);
            regionBorder[r.id] = packRgba(220, 228, 235, 180);
          } else {
            regionColor[r.id] = packRgba(24, 30, 36, 75);
            regionBorder[r.id] = packRgba(65, 76, 86, 110);
          }
        }
      } else if (overlayMode === 'power') {
        const p = r.province?.power ?? 0;
        const t = p / maxPower;
        const [cr, cg, cb] = lerpColor(t, [52, 86, 110], [235, 96, 68]);
        regionColor[r.id] = packRgba(cr, cg, cb, p > 0 ? 132 : 55);
        regionBorder[r.id] = packRgba(35, 42, 48, 165);
      } else if (overlayMode === 'density') {
        const d =
          r.pixelCount > 0
            ? (r.locations.cities * 6 + r.locations.townsAndVillages * 2 + r.locations.total * 0.25) / Math.sqrt(r.pixelCount)
            : 0;
        const t = Math.min(1, d / maxDensity);
        const [cr, cg, cb] = lerpColor(t, [44, 72, 68], [244, 200, 74]);
        regionColor[r.id] = packRgba(cr, cg, cb, d > 0 ? 132 : 55);
        regionBorder[r.id] = packRgba(35, 42, 48, 165);
      } else if (overlayMode === 'geography') {
        if (r.id === selectedRegionId) {
          regionColor[r.id] = packRgba(255, 209, 102, 58);
          regionBorder[r.id] = packRgba(255, 220, 125, 235);
        } else if (r.id === hoverRegionId) {
          regionColor[r.id] = packRgba(120, 228, 175, 48);
          regionBorder[r.id] = packRgba(150, 245, 198, 205);
        }
      }
    }

    const climateColors = overview.climates.map(c => {
      if (c.name === 'Ocean') return 0;
      const [cr, cg, cb] = hexToRgb(c.color);
      return packRgba(cr, cg, cb, 128);
    });

    const selectedBorderColor = packRgba(255, 224, 120, 255);
    const hoverBorderColor = packRgba(148, 250, 198, 235);

    for (let y = 0; y < height; y++) {
      const rowOff = y * width;
      for (let x = 0; x < width; x++) {
        const idx = rowOff + x;
        const reg = politic[idx];

        if (overlayMode === 'climate') {
          const cIdx = climate[idx];
          let px = climateColors[cIdx] ?? 0;
          if (reg >= 0 && (reg === selectedRegionId || reg === hoverRegionId)) {
            const isEdge =
              (x > 0 && politic[idx - 1] !== reg) ||
              (x + 1 < width && politic[idx + 1] !== reg) ||
              (y > 0 && politic[idx - width] !== reg) ||
              (y + 1 < height && politic[idx + width] !== reg);
            if (isEdge) px = reg === selectedRegionId ? selectedBorderColor : hoverBorderColor;
          }
          buf32[idx] = px;
          continue;
        }

        if (reg < 0 || reg >= 128) continue;

        const isEdge =
          (x > 0 && politic[idx - 1] !== reg) ||
          (x + 1 < width && politic[idx + 1] !== reg) ||
          (y > 0 && politic[idx - width] !== reg) ||
          (y + 1 < height && politic[idx + width] !== reg);

        if (isEdge) {
          if (reg === selectedRegionId) {
            buf32[idx] = selectedBorderColor;
          } else if (reg === hoverRegionId) {
            buf32[idx] = hoverBorderColor;
          } else {
            buf32[idx] = regionBorder[reg];
          }
        } else {
          buf32[idx] = regionColor[reg];
        }
      }
    }

    ctx.putImageData(imgData, 0, 0);
    setRenderTick(t => t + 1);
  }, [masks, overlayMode, selectedRegionId, hoverRegionId, overview.regions, overview.climates, regionsById]);

  function getImage(url: string): HTMLImageElement | null {
    const cache = imageCacheRef.current;
    const entry = cache.get(url);
    if (entry instanceof HTMLImageElement) return entry;
    if (entry === 'loading' || entry === 'error') return null;
    cache.set(url, 'loading');
    const img = new Image();
    img.decoding = 'async';
    img.onload = () => {
      cache.set(url, img);
      setRenderTick(t => t + 1);
    };
    img.onerror = () => {
      cache.set(url, 'error');
    };
    img.src = url;
    return null;
  }

  function fitCameraToCanvas() {
    const canvas = canvasRef.current;
    if (!canvas) return;
    const w = Math.max(canvas.clientWidth, 320);
    const h = Math.max(canvas.clientHeight, 240);
    const fitZoom = Math.min(w / 1000, h / 500) * 0.96;
    const cam = cameraRef.current;
    cam.minZoom = Math.min(fitZoom * 0.85, 0.7);
    cam.cx = 500;
    cam.cy = 250;
    cam.zoom = fitZoom;
    cam.initialized = true;
    setZoomPct(Math.round((cam.zoom / fitZoom) * 100));
    setRenderTick(t => t + 1);
  }

  // Handle ResizeObserver + initial fit
  useEffect(() => {
    const el = wrapRef.current;
    if (!el) return;
    const ro = new ResizeObserver(() => {
      if (!cameraRef.current.initialized) {
        fitCameraToCanvas();
      } else {
        setRenderTick(t => t + 1);
      }
    });
    ro.observe(el);
    fitCameraToCanvas();
    return () => ro.disconnect();
  }, []);

  // Fly-to animation
  useEffect(() => {
    if (!flyToRequest || !canvasRef.current) return;
    const canvas = canvasRef.current;
    const w = Math.max(canvas.clientWidth, 320);
    const h = Math.max(canvas.clientHeight, 240);
    const cam = cameraRef.current;
    let endCx = cam.cx;
    let endCy = cam.cy;
    let endZoom = cam.zoom;

    if (flyToRequest.bounds) {
      const b = flyToRequest.bounds;
      endCx = (b.minX + b.maxX) * 0.5;
      endCy = (b.minY + b.maxY) * 0.5;
      const bw = Math.max(b.maxX - b.minX, 32);
      const bh = Math.max(b.maxY - b.minY, 24);
      endZoom = Math.max(cam.minZoom, Math.min(12, Math.min((w * 0.72) / bw, (h * 0.72) / bh)));
    } else if (flyToRequest.x !== undefined && flyToRequest.y !== undefined) {
      endCx = flyToRequest.x;
      endCy = flyToRequest.y;
      endZoom = Math.max(cam.minZoom, Math.min(cam.maxZoom, flyToRequest.zoom ?? Math.max(cam.zoom, 6.5)));
    }

    animRef.current = {
      startCx: cam.cx,
      startCy: cam.cy,
      startZoom: cam.zoom,
      endCx,
      endCy,
      endZoom,
      startTime: performance.now(),
      duration: 360,
    };

    let raf = 0;
    const step = (now: number) => {
      const a = animRef.current;
      if (!a) return;
      const t = Math.min(1, (now - a.startTime) / a.duration);
      const ease = 1 - Math.pow(1 - t, 3);
      cam.cx = a.startCx + (a.endCx - a.startCx) * ease;
      cam.cy = a.startCy + (a.endCy - a.startCy) * ease;
      cam.zoom = a.startZoom + (a.endZoom - a.startZoom) * ease;
      setRenderTick(v => v + 1);
      if (t < 1) {
        raf = requestAnimationFrame(step);
      } else {
        const fitZoom = Math.min(w / 1000, h / 500) * 0.96;
        setZoomPct(Math.round((cam.zoom / fitZoom) * 100));
        animRef.current = null;
      }
    };
    raf = requestAnimationFrame(step);
    return () => cancelAnimationFrame(raf);
  }, [flyToRequest?.id]);

  // Main Canvas draw pass
  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas) return;
    const dpr = Math.min(window.devicePixelRatio || 1, 2);
    const width = Math.max(canvas.clientWidth, 320);
    const height = Math.max(canvas.clientHeight, 240);
    if (canvas.width !== Math.round(width * dpr) || canvas.height !== Math.round(height * dpr)) {
      canvas.width = Math.round(width * dpr);
      canvas.height = Math.round(height * dpr);
    }
    const ctx = canvas.getContext('2d');
    if (!ctx) return;

    const cam = cameraRef.current;
    const worldToScreenX = (wx: number) => (wx - cam.cx) * cam.zoom + width * 0.5;
    const worldToScreenY = (wy: number) => (wy - cam.cy) * cam.zoom + height * 0.5;
    const minVisX = Math.max(0, cam.cx - width / (2 * cam.zoom));
    const maxVisX = Math.min(1000, cam.cx + width / (2 * cam.zoom));
    const minVisY = Math.max(0, cam.cy - height / (2 * cam.zoom));
    const maxVisY = Math.min(500, cam.cy + height / (2 * cam.zoom));

    ctx.save();
    ctx.scale(dpr, dpr);

    // Deep sea backdrop outside 1000x500 world bounds
    ctx.fillStyle = '#182b3a';
    ctx.fillRect(0, 0, width, height);

    // 1. Base Map: Painted Relief Atlas pyramid OR Classic TRAV0I00 + FMAP sheets
    if (baseMode === 'classic' && overview.classicMaps.available) {
      ctx.imageSmoothingEnabled = false;
      const worldImg = getImage(overview.classicMaps.world.url);
      if (worldImg) {
        const r = overview.classicMaps.world;
        const sx = worldToScreenX(r.left);
        const sy = worldToScreenY(r.top);
        const sw = (r.right - r.left) * cam.zoom;
        const sh = (r.bottom - r.top) * cam.zoom;
        ctx.drawImage(worldImg, sx, sy, sw, sh);
      }
      if (cam.zoom >= 1.8) {
        for (const sheet of overview.classicMaps.sheets) {
          if (sheet.right < minVisX || sheet.left > maxVisX || sheet.bottom < minVisY || sheet.top > maxVisY) {
            continue;
          }
          const img = getImage(sheet.url);
          if (!img) continue;
          const sx = worldToScreenX(sheet.left);
          const sy = worldToScreenY(sheet.top);
          const sw = (sheet.right - sheet.left) * cam.zoom;
          const sh = (sheet.bottom - sheet.top) * cam.zoom;
          ctx.drawImage(img, sx, sy, sw, sh);
        }
      }
    } else if (overview.atlas.available && overview.atlas.levels.length > 0) {
      ctx.imageSmoothingEnabled = true;
      const levels = overview.atlas.levels;
      const tileSize = overview.atlas.tileSize;
      const effZoom = cam.zoom * dpr;
      let targetIdx = 0;
      for (let i = 0; i < levels.length; i++) {
        const scale = levels[i].width / overview.atlas.worldWidth;
        if (effZoom >= scale * 0.8) targetIdx = i;
      }

      const drawLevel = (levelIdx: number) => {
        const lvl = levels[levelIdx];
        const worldPerTexelX = overview.atlas.worldWidth / lvl.width;
        const worldPerTexelY = overview.atlas.worldHeight / lvl.height;
        const tileWorldW = tileSize * worldPerTexelX;
        const tileWorldH = tileSize * worldPerTexelY;
        const c0 = Math.max(0, Math.floor(minVisX / tileWorldW));
        const c1 = Math.min(lvl.columns - 1, Math.floor(maxVisX / tileWorldW));
        const r0 = Math.max(0, Math.floor(minVisY / tileWorldH));
        const r1 = Math.min(lvl.rows - 1, Math.floor(maxVisY / tileWorldH));
        for (let row = r0; row <= r1; row++) {
          for (let col = c0; col <= c1; col++) {
            const url = `${overview.atlas.tileBaseUrl}${lvl.level}/${col}-${row}.png`;
            const img = getImage(url);
            if (!img) continue;
            const wx0 = col * tileWorldW;
            const wy0 = row * tileWorldH;
            // Overlap by 0.5px to prevent subpixel hairlines between tiles
            const sx = worldToScreenX(wx0);
            const sy = worldToScreenY(wy0);
            const sw = tileWorldW * cam.zoom + 0.5;
            const sh = tileWorldH * cam.zoom + 0.5;
            ctx.drawImage(img, sx, sy, sw, sh);
          }
        }
      };

      // Always draw level 0 as instant fallback beneath finer tiles
      drawLevel(0);
      if (targetIdx > 0) {
        drawLevel(targetIdx);
      }
    }

    // 2. Political / Diplomacy / Climate / Power / Density 1000x500 mask overlay
    if (overlayCanvasRef.current) {
      ctx.imageSmoothingEnabled = cam.zoom < 2.2;
      ctx.drawImage(
        overlayCanvasRef.current,
        worldToScreenX(0),
        worldToScreenY(0),
        1000 * cam.zoom,
        500 * cam.zoom,
      );
    }

    // World bounds frame
    ctx.strokeStyle = 'rgba(200, 215, 224, 0.25)';
    ctx.lineWidth = 1;
    ctx.strokeRect(worldToScreenX(0), worldToScreenY(0), 1000 * cam.zoom, 500 * cam.zoom);

    // 3. Diplomatic relation arcs (in Diplomacy mode or when toggled)
    if (overlayMode === 'diplomacy' && showDiplomacyArcs) {
      const drawnPairs = new Set<string>();
      const drawArc = (a: WorldAtlasRegion, b: WorldAtlasRegion, kind: 'ally' | 'enemy', emphasized: boolean) => {
        const key = `${Math.min(a.id, b.id)}-${Math.max(a.id, b.id)}-${kind}`;
        if (drawnPairs.has(key)) return;
        drawnPairs.add(key);
        const x1 = worldToScreenX(a.centroid.x);
        const y1 = worldToScreenY(a.centroid.y);
        const x2 = worldToScreenX(b.centroid.x);
        const y2 = worldToScreenY(b.centroid.y);
        const mx = (x1 + x2) * 0.5;
        const my = (y1 + y2) * 0.5;
        const dx = x2 - x1;
        const dy = y2 - y1;
        const dist = Math.hypot(dx, dy);
        if (dist < 4) return;
        const lift = Math.min(dist * 0.22, 70);
        const cx = mx - (dy / dist) * lift;
        const cy = my + (dx / dist) * lift;

        ctx.save();
        ctx.beginPath();
        ctx.moveTo(x1, y1);
        ctx.quadraticCurveTo(cx, cy, x2, y2);
        ctx.strokeStyle =
          kind === 'ally'
            ? emphasized
              ? 'rgba(92, 240, 168, 0.92)'
              : 'rgba(92, 240, 168, 0.45)'
            : emphasized
              ? 'rgba(255, 98, 114, 0.92)'
              : 'rgba(255, 98, 114, 0.48)';
        ctx.lineWidth = emphasized ? 2.4 : 1.3;
        if (kind === 'enemy') ctx.setLineDash([6, 4]);
        ctx.stroke();
        ctx.restore();
      };

      const sourceRegions =
        selectedRegionId !== null
          ? [regionsById.get(selectedRegionId)].filter((r): r is WorldAtlasRegion => Boolean(r))
          : overview.regions;
      for (const r of sourceRegions) {
        const emph = selectedRegionId !== null;
        for (const allyId of r.diplomacy.allyRegions) {
          const other = regionsById.get(allyId);
          if (other) drawArc(r, other, 'ally', emph);
        }
        for (const enemyId of r.diplomacy.enemyRegions) {
          const other = regionsById.get(enemyId);
          if (other) drawArc(r, other, 'enemy', emph);
        }
      }
    }

    // 4. Location pins (fast fillRect path when thousands of dense non-city pins are zoomed out)
    const pinScale = Math.max(0.85, Math.min(1.6, cam.zoom * 0.35));
    const fastDotMode = visiblePins.length > 1800 && cam.zoom < 3.0;
    for (let i = 0; i < visiblePins.length; i++) {
      const p = visiblePins[i];
      const wx = p[2] + 0.5;
      const wy = p[3] + 0.5;
      if (wx < minVisX - 4 || wx > maxVisX + 4 || wy < minVisY - 4 || wy > maxVisY + 4) continue;
      const sx = worldToScreenX(wx);
      const sy = worldToScreenY(wy);
      const ltype = p[4];
      const cat = typeToCategory.get(ltype);
      const color = cat?.color ?? '#f4978e';
      const isCity = ltype === 0;
      const isTown = ltype === 1 || ltype === 2;
      const r = (isCity ? 3.8 : isTown ? 2.5 : 2.1) * pinScale;

      ctx.fillStyle = color;
      if (fastDotMode && !isCity) {
        const d = Math.max(2.5, r * 1.6);
        ctx.fillRect(sx - d * 0.5, sy - d * 0.5, d, d);
        continue;
      }
      ctx.strokeStyle = 'rgba(12, 16, 20, 0.85)';
      ctx.lineWidth = 1;
      ctx.beginPath();
      if (isCity) {
        ctx.moveTo(sx, sy - r);
        ctx.lineTo(sx + r, sy);
        ctx.lineTo(sx, sy + r);
        ctx.lineTo(sx - r, sy);
        ctx.closePath();
      } else {
        ctx.arc(sx, sy, r, 0, Math.PI * 2);
      }
      ctx.fill();
      ctx.stroke();
    }

    // Highlight selected or hovered pin with crisp ring
    const drawPinRing = (pin: WorldAtlasPinTuple, color: string, radius: number) => {
      const sx = worldToScreenX(pin[2] + 0.5);
      const sy = worldToScreenY(pin[3] + 0.5);
      ctx.beginPath();
      ctx.arc(sx, sy, radius, 0, Math.PI * 2);
      ctx.strokeStyle = color;
      ctx.lineWidth = 2;
      ctx.stroke();
    };
    if (hoverInfo?.pin) drawPinRing(hoverInfo.pin, '#e8fff3', 8);
    if (selectedPin) drawPinRing(selectedPin, '#ffd166', 10);

    // 5. Cartographic labels (world names, region names, zoomed city/town names)
    if (showLabels) {
      ctx.textAlign = 'center';
      ctx.textBaseline = 'middle';

      // Macro sea & realm labels
      if (cam.zoom < 3.4) {
        ctx.font = 'italic 600 13px Georgia, "Times New Roman", serif';
        for (const wn of overview.worldNames) {
          const sx = worldToScreenX(wn.x);
          const sy = worldToScreenY(wn.y);
          if (sx < -80 || sx > width + 80 || sy < -30 || sy > height + 30) continue;
          ctx.fillStyle = 'rgba(10, 14, 18, 0.72)';
          ctx.fillText(wn.name.toUpperCase(), sx + 1, sy + 1);
          ctx.fillStyle = wn.kind === 'sea' || wn.kind === 'bay' ? 'rgba(170, 214, 232, 0.75)' : 'rgba(242, 232, 206, 0.72)';
          ctx.fillText(wn.name.toUpperCase(), sx, sy);
        }
      }

      // Region names
      const placedBoxes: Array<[number, number, number, number]> = [];
      const sortedRegions = [...overview.regions]
        .filter(r => r.hasTerritory || r.locations.total > 0)
        .sort((a, b) => {
          if (a.id === selectedRegionId) return -1;
          if (b.id === selectedRegionId) return 1;
          if (a.id === hoverRegionId) return -1;
          if (b.id === hoverRegionId) return 1;
          return b.pixelCount - a.pixelCount;
        });

      for (const r of sortedRegions) {
        const sx = worldToScreenX(r.centroid.x);
        const sy = worldToScreenY(r.centroid.y);
        if (sx < -60 || sx > width + 60 || sy < -20 || sy > height + 20) continue;
        const isFocus = r.id === selectedRegionId || r.id === hoverRegionId;
        if (!isFocus && cam.zoom < 1.5 && r.pixelCount < 280) continue;

        const fontSize = isFocus ? 13 : r.pixelCount > 3000 ? 12 : 10.5;
        ctx.font = `${isFocus ? 700 : 600} ${fontSize}px system-ui, -apple-system, sans-serif`;
        const tw = ctx.measureText(r.name).width + 8;
        const th = fontSize + 4;
        const box: [number, number, number, number] = [sx - tw * 0.5, sy - th * 0.5, sx + tw * 0.5, sy + th * 0.5];
        if (
          !isFocus &&
          placedBoxes.some(b => !(box[2] < b[0] || box[0] > b[2] || box[3] < b[1] || box[1] > b[3]))
        ) {
          continue;
        }
        placedBoxes.push(box);

        ctx.lineWidth = 3;
        ctx.strokeStyle = 'rgba(10, 14, 18, 0.85)';
        ctx.strokeText(r.name, sx, sy);
        ctx.fillStyle = r.id === selectedRegionId ? '#ffe599' : r.id === hoverRegionId ? '#b8f7d4' : '#edf2f4';
        ctx.fillText(r.name, sx, sy);
      }

      // City / Town labels when zoomed in
      if (cam.zoom >= 2.2) {
        ctx.font = '600 11px system-ui, -apple-system, sans-serif';
        ctx.textAlign = 'left';
        let labelCount = 0;
        for (let i = 0; i < visiblePins.length && labelCount < 70; i++) {
          const p = visiblePins[i];
          const isCity = p[4] === 0;
          const isTown = p[4] === 1 || p[4] === 2;
          if (!isCity && !(cam.zoom >= 5.2 && isTown) && !(cam.zoom >= 8.5)) continue;
          const sx = worldToScreenX(p[2] + 0.5);
          const sy = worldToScreenY(p[3] + 0.5);
          if (sx < 0 || sx > width || sy < 0 || sy > height) continue;
          const lx = sx + 7;
          const ly = sy;
          const tw = ctx.measureText(p[6]).width + 6;
          const box: [number, number, number, number] = [lx, ly - 7, lx + tw, ly + 7];
          if (placedBoxes.some(b => !(box[2] < b[0] || box[0] > b[2] || box[3] < b[1] || box[1] > b[3]))) {
            continue;
          }
          placedBoxes.push(box);
          labelCount++;
          ctx.lineWidth = 3;
          ctx.strokeStyle = 'rgba(10, 14, 18, 0.88)';
          ctx.strokeText(p[6], lx, ly);
          ctx.fillStyle = isCity ? '#ffe399' : '#d5ece0';
          ctx.fillText(p[6], lx, ly);
        }
      }
    }

    ctx.restore();
  }, [
    renderTick,
    baseMode,
    overlayMode,
    overview,
    visiblePins,
    showLabels,
    showDiplomacyArcs,
    selectedRegionId,
    selectedPin,
    hoverInfo?.pin,
    regionsById,
    typeToCategory,
  ]);

  function screenToWorld(clientX: number, clientY: number): { wx: number; wy: number; sx: number; sy: number } | null {
    const canvas = canvasRef.current;
    if (!canvas) return null;
    const rect = canvas.getBoundingClientRect();
    const sx = clientX - rect.left;
    const sy = clientY - rect.top;
    const cam = cameraRef.current;
    const wx = (sx - rect.width * 0.5) / cam.zoom + cam.cx;
    const wy = (sy - rect.height * 0.5) / cam.zoom + cam.cy;
    return { wx, wy, sx, sy };
  }

  function findNearestPin(sx: number, sy: number): WorldAtlasPinTuple | null {
    const canvas = canvasRef.current;
    if (!canvas) return null;
    const rect = canvas.getBoundingClientRect();
    const cam = cameraRef.current;
    const maxDistPx = 10;
    let best: WorldAtlasPinTuple | null = null;
    let bestDist = maxDistPx * maxDistPx;
    for (let i = 0; i < visiblePins.length; i++) {
      const p = visiblePins[i];
      const px = (p[2] + 0.5 - cam.cx) * cam.zoom + rect.width * 0.5;
      const py = (p[3] + 0.5 - cam.cy) * cam.zoom + rect.height * 0.5;
      const dx = px - sx;
      const dy = py - sy;
      const d2 = dx * dx + dy * dy;
      if (d2 <= bestDist) {
        bestDist = d2;
        best = p;
      }
    }
    return best;
  }

  function onWheel(event: WheelEvent) {
    event.preventDefault();
    const pos = screenToWorld(event.clientX, event.clientY);
    const canvas = canvasRef.current;
    if (!pos || !canvas) return;
    animRef.current = null;
    const cam = cameraRef.current;
    const factor = event.deltaY < 0 ? 1.22 : 1 / 1.22;
    const nextZoom = Math.max(cam.minZoom, Math.min(cam.maxZoom, cam.zoom * factor));
    const rect = canvas.getBoundingClientRect();
    cam.zoom = nextZoom;
    cam.cx = pos.wx - (pos.sx - rect.width * 0.5) / nextZoom;
    cam.cy = pos.wy - (pos.sy - rect.height * 0.5) / nextZoom;
    const fitZoom = Math.min(rect.width / 1000, rect.height / 500) * 0.96;
    setZoomPct(Math.round((cam.zoom / fitZoom) * 100));
    setRenderTick(t => t + 1);
  }

  function onPointerDown(event: PointerEvent) {
    if (event.button !== 0) return;
    animRef.current = null;
    (event.currentTarget as HTMLElement).setPointerCapture(event.pointerId);
    const cam = cameraRef.current;
    dragRef.current = {
      active: true,
      moved: false,
      startX: event.clientX,
      startY: event.clientY,
      startCx: cam.cx,
      startCy: cam.cy,
    };
  }

  function onPointerMove(event: PointerEvent) {
    const drag = dragRef.current;
    const cam = cameraRef.current;
    if (drag.active) {
      const dx = event.clientX - drag.startX;
      const dy = event.clientY - drag.startY;
      if (Math.hypot(dx, dy) > 4) drag.moved = true;
      cam.cx = drag.startCx - dx / cam.zoom;
      cam.cy = drag.startCy - dy / cam.zoom;
      setRenderTick(t => t + 1);
      return;
    }

    const pos = screenToWorld(event.clientX, event.clientY);
    if (!pos) return;
    const ix = Math.floor(pos.wx);
    const iy = Math.floor(pos.wy);
    if (ix < 0 || ix >= 1000 || iy < 0 || iy >= 500) {
      setHoverInfo(null);
      return;
    }
    const idx = iy * 1000 + ix;
    const reg = masks ? masks.politic[idx] : -1;
    const climIdx = masks ? masks.climate[idx] : 0;
    const climateName = overview.climates[climIdx]?.name ?? null;
    const pin = findNearestPin(pos.sx, pos.sy);
    setHoverInfo({ x: ix, y: iy, regionId: reg, climateName, pin });
  }

  function onPointerUp(event: PointerEvent) {
    const drag = dragRef.current;
    if (!drag.active) return;
    drag.active = false;
    try {
      (event.currentTarget as HTMLElement).releasePointerCapture(event.pointerId);
    } catch {
      // Ignore release errors
    }
    if (drag.moved) return;

    const pos = screenToWorld(event.clientX, event.clientY);
    if (!pos) return;
    const pin = findNearestPin(pos.sx, pos.sy);
    if (pin) {
      onSelectPin(pin);
      onSelectRegion(pin[0]);
      return;
    }

    const ix = Math.floor(pos.wx);
    const iy = Math.floor(pos.wy);
    if (masks && ix >= 0 && ix < 1000 && iy >= 0 && iy < 500) {
      const reg = masks.politic[iy * 1000 + ix];
      onSelectPin(null);
      onSelectRegion(reg >= 0 ? reg : null);
    }
  }

  function nudgeZoom(factor: number) {
    const canvas = canvasRef.current;
    if (!canvas) return;
    const cam = cameraRef.current;
    cam.zoom = Math.max(cam.minZoom, Math.min(cam.maxZoom, cam.zoom * factor));
    const fitZoom = Math.min(canvas.clientWidth / 1000, canvas.clientHeight / 500) * 0.96;
    setZoomPct(Math.round((cam.zoom / fitZoom) * 100));
    setRenderTick(t => t + 1);
  }

  const hoverRegionName =
    hoverInfo && hoverInfo.regionId >= 0 ? regionsById.get(hoverInfo.regionId)?.name ?? `Region ${hoverInfo.regionId}` : 'Iliac Bay / Sea';

  return (
    <div ref={wrapRef} class="atlas-canvas-wrap">
      <canvas
        ref={canvasRef}
        class="atlas-canvas"
        onWheel={onWheel}
        onPointerDown={onPointerDown}
        onPointerMove={onPointerMove}
        onPointerUp={onPointerUp}
        onPointerLeave={() => setHoverInfo(null)}
      />

      <div class="atlas-zoom-controls">
        <button type="button" onClick={() => nudgeZoom(1.4)} title="Zoom in">
          +
        </button>
        <button type="button" onClick={() => nudgeZoom(1 / 1.4)} title="Zoom out">
          −
        </button>
        <button type="button" onClick={fitCameraToCanvas} title="Fit whole Iliac Bay">
          Fit
        </button>
      </div>

      <div class="block-canvas-hud atlas-hud">
        <span>
          {hoverInfo ? (
            <>
              <strong>{hoverInfo.pin ? `${hoverInfo.pin[6]} · ` : ''}</strong>
              {hoverRegionName}
              {hoverInfo.climateName ? ` · ${hoverInfo.climateName}` : ''} · ({hoverInfo.x}, {hoverInfo.y})
            </>
          ) : (
            'Drag to pan · Wheel to zoom · Click any region or pin to inspect'
          )}
        </span>
        <span>
          Zoom {zoomPct}% · {visiblePins.length.toLocaleString()} pins shown
        </span>
      </div>
    </div>
  );
}
