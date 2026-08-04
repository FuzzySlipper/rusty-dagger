/**
 * Browser page: mount the REAL rusty-engine renderer (renderer-three browser
 * surface) on the protocol-14 frame dumped from dagger-studio-adapter, with
 * texture resources admitted through renderer-host's
 * loadRendererTextureResourceSource (byte-length + sha256 verified, so the
 * schemaVersion-1 silent average-color fallback cannot pass unnoticed).
 *
 * Texture PNG bytes are served statically from ../content via vite publicDir:
 * manifest sourcePath 'content/textures/x.png' -> fetch('/textures/x.png').
 *
 * Query: ?cam=overview|interior (poses computed in dump-frame.mjs).
 * Exposes window.__proof (submission statistics + snapshot) or __failure.
 */
import { loadRendererTextureResourceSource } from '@rusty-engine/renderer-host';
import { mountRendererBrowserSurface } from '@rusty-engine/renderer-three';

window.__errors = [];
window.addEventListener('error', (event) => window.__errors.push(String(event.message)));
window.addEventListener('unhandledrejection', (event) => window.__errors.push(String(event.reason)));

async function main() {
  const params = new URLSearchParams(location.search);
  const cam = params.get('cam') ?? 'overview';

  const [frame, manifest, input] = await Promise.all([
    fetch('/generated/frame.json').then((r) => r.json()),
    fetch('/generated/texture-manifest.json').then((r) => r.json()),
    fetch('/generated/proof-input.json').then((r) => r.json()),
  ]);
  const pose = input.poses[cam];
  if (!pose) throw new Error(`unknown camera pose: ${cam}`);

  const textureResourceSource = await loadRendererTextureResourceSource(
    manifest,
    async (descriptor) => {
      const url = `/${descriptor.sourcePath.replace(/^content\//u, '')}`;
      const response = await fetch(url);
      if (!response.ok) throw new Error(`texture fetch failed ${response.status}: ${url}`);
      return response.arrayBuffer();
    },
  );

  const canvas = document.getElementById('renderer');
  const surface = mountRendererBrowserSurface(canvas, {
    autoStart: false,
    camera: {
      initialPose: pose,
      projection: { fovYDegrees: 60, near: 0.05, far: 2000 },
    },
    clearColor: 0x101418,
    frame,
    pixelRatio: 1,
    textureResourceSource,
  });
  const submission = surface.renderOnce(1);
  const lighting = surface.lightingReadout();
  const framePng = await captureFramePng(canvas);
  window.__proof = {
    ready: true,
    cam,
    statistics: {
      drawCallCount: submission.drawCallCount,
      triangleCount: submission.triangleCount,
      textureResourceCount: submission.textureResourceCount,
      materialResourceCount: submission.materialResourceCount,
      geometryResourceCount: submission.geometryResourceCount,
      renderHandleCount: submission.renderHandleCount,
    },
    retainedLightCount: lighting.retainedLights.length,
    snapshot: surface.snapshot(),
    expectations: input.expectations,
    framePng,
  };
}

/**
 * Export the exact drawing-buffer pixels as a PNG data URL. readPixels runs
 * synchronously right after renderOnce, before the buffer is invalidated;
 * OffscreenCanvas re-encodes at native backing resolution (no CSS upscale
 * interpolation, which would wash out the texel-frequency metrics).
 */
function captureFramePng(canvas) {
  const gl = canvas.getContext('webgl2') ?? canvas.getContext('webgl');
  if (gl === null) throw new Error('WebGL context unavailable for frame capture');
  const width = gl.drawingBufferWidth;
  const height = gl.drawingBufferHeight;
  const pixels = new Uint8Array(width * height * 4);
  gl.readPixels(0, 0, width, height, gl.RGBA, gl.UNSIGNED_BYTE, pixels);
  const flipped = new Uint8ClampedArray(width * height * 4);
  for (let y = 0; y < height; y += 1) {
    flipped.set(
      pixels.subarray((height - 1 - y) * width * 4, (height - y) * width * 4),
      y * width * 4,
    );
  }
  const offscreen = new OffscreenCanvas(width, height);
  const context = offscreen.getContext('2d');
  context.putImageData(new ImageData(flipped, width, height), 0, 0);
  return offscreen.convertToBlob({ type: 'image/png' }).then(async (blob) => {
    const buffer = await blob.arrayBuffer();
    const bytes = new Uint8Array(buffer);
    let binary = '';
    for (let index = 0; index < bytes.length; index += 1) {
      binary += String.fromCharCode(bytes[index]);
    }
    return `data:image/png;base64,${btoa(binary)}`;
  });
}

main().catch((error) => {
  window.__failure = String(error && error.stack ? error.stack : error);
});
