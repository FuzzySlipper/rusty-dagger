import { cssUrl, image, type TitleScene } from './art.js';

/**
 * The refreshed title as a living picture behind the title's controls: the town at night seen through a window, built from
 * the layers the art set publishes (art.ts `TitleScene`). The town painting is the title's own image; over it the clouds
 * drift, the sea's strip of frames plays, the town's windows and street lamps glimmer, and the window's flames light
 * the stone. Everything is drawn here from the art; nothing in it is a game state.
 *
 * The picture covers the title as the window art is shaped, cut at its edges rather than letterboxed, and the town fills the
 * window's opening with its foot on the sill.
 */
export interface TitleSceneView {
  readonly element: HTMLElement;
  /// Shows a scene whose layers have all arrived and returns true, or returns false and shows nothing.
  show(scene: TitleScene): boolean;
  hide(): void;
}

/// A lit window's and a lamp's glow, in town pixels at the near edge of the town; farther ones are smaller.
const WINDOW_GLOW = 7, LAMP_GLOW = 16;
/// How many of the lights change in one frame: the rest keep their last level, so a frame restyles a few dozen at most.
const LIGHTS_PER_FRAME = 48;

/// A light's level about 1 at time t: a slow breath, a quicker waver and a little shimmer, unique to its phase.
function waver(t: number, phase: number): number {
  return Math.sin(t * 1.7 + phase) * .5 + Math.sin(t * 4.3 + phase * 1.3) * .3 + Math.sin(t * 9.1 + phase * .7) * .2;
}

export function mountTitleScene(host: HTMLElement): TitleSceneView {
  const element = document.createElement('div'); element.className = 'dagger-title-scene'; element.hidden = true;
  element.setAttribute('aria-hidden', 'true');
  const stage = document.createElement('div'); stage.className = 'dagger-title-scene-stage';
  const view = document.createElement('div'); view.className = 'dagger-title-scene-view';
  const town = document.createElement('img'); town.className = 'dagger-title-scene-town'; town.alt = '';
  const cloudsA = document.createElement('img'), cloudsB = document.createElement('img');
  for (const clouds of [cloudsA, cloudsB]) { clouds.className = 'dagger-title-scene-clouds'; clouds.alt = ''; }
  const sea = document.createElement('div'); sea.className = 'dagger-title-scene-sea';
  const lights = document.createElement('div'); lights.className = 'dagger-title-scene-lights';
  const arch = document.createElement('img'); arch.className = 'dagger-title-scene-window'; arch.alt = '';
  const flames = document.createElement('div'); flames.className = 'dagger-title-scene-flames';
  view.append(town, cloudsA, cloudsB, sea, lights);
  stage.append(view, arch, flames);
  element.append(stage);
  host.prepend(element);

  let shown: TitleScene | null = null;
  let glows: { readonly el: HTMLElement; readonly lamp: boolean; readonly phase: number; readonly rate: number; readonly base: number }[] = [];
  let flameGlows: { readonly el: HTMLElement; readonly phase: number }[] = [];
  let frame = 0;
  let next = 0;

  // Cover the title with the window art's shape, centred, as a painting fills a frame.
  const fit = (): void => {
    if (shown === null) return;
    const { width, height } = shown.window;
    const scale = Math.max(host.clientWidth / width, host.clientHeight / height);
    if (!(scale > 0)) return;
    stage.style.transform = `translate(${(host.clientWidth - width * scale) / 2}px, ${(host.clientHeight - height * scale) / 2}px) scale(${scale})`;
  };
  new ResizeObserver(fit).observe(host);

  const build = (scene: TitleScene): void => {
    const { opening } = scene.window;
    stage.style.width = `${scene.window.width}px`; stage.style.height = `${scene.window.height}px`;
    // The town covers the opening, its foot just below the sill's edge so no sky shows under it.
    const fill = Math.max(opening.width / scene.town.width, opening.height / scene.town.height) * 1.04;
    view.style.width = `${scene.town.width}px`; view.style.height = `${scene.town.height}px`;
    view.style.transform = `translate(${opening.x + opening.width / 2 - scene.town.width * fill / 2}px, ${opening.y + opening.height + scene.window.width / 480 - scene.town.height * fill}px) scale(${fill})`;
    const { place } = scene.sea;
    Object.assign(sea.style, { left: `${place.x}px`, top: `${place.y}px`, width: `${place.width}px`, height: `${place.height}px`,
      backgroundSize: `${place.width}px ${place.height * scene.sea.frames}px` });
    glows = scene.lights.map(light => {
      const glow = document.createElement('span');
      const near = Math.max(0, Math.min(1, (light.y - scene.town.height * .49) / (scene.town.height * .49)));
      const size = (light.lamp ? LAMP_GLOW : WINDOW_GLOW) * (.6 + 1.2 * near);
      glow.className = light.lamp ? 'dagger-title-scene-lamp' : 'dagger-title-scene-lit';
      Object.assign(glow.style, { left: `${light.x - size / 2}px`, top: `${light.y - size / 2}px`, width: `${size}px`, height: `${size}px` });
      return { el: glow, lamp: light.lamp, phase: Math.random() * 1000, rate: .6 + Math.random() * 1.4, base: .35 + Math.random() * .35 };
    });
    lights.replaceChildren(...glows.map(glow => glow.el));
    flameGlows = scene.window.flames.map(flame => {
      const glow = document.createElement('span'); glow.className = 'dagger-title-scene-flame';
      Object.assign(glow.style, { left: `${flame.x - flame.radius}px`, top: `${flame.y - flame.radius}px`, width: `${2 * flame.radius}px`, height: `${2 * flame.radius}px` });
      return { el: glow, phase: Math.random() * 100 };
    });
    flames.replaceChildren(...flameGlows.map(glow => glow.el));
  };

  const animate = (now: number): void => {
    frame = 0;
    if (shown === null || element.hidden) return;
    const t = now / 1000;
    const { clouds, sea: water } = shown;
    // Two copies of the clouds drift the same way half a cycle apart, each fading in and out, so the drift has no seam.
    for (const [layer, offset] of [[cloudsA, 0], [cloudsB, .5]] as const) {
      const u = (t / clouds.cycleSeconds + offset) % 1;
      layer.style.transform = `translateX(${-u * clouds.drift}px)`;
      layer.style.opacity = Math.sin(Math.PI * u).toFixed(3);
    }
    sea.style.backgroundPosition = `0 ${-(Math.floor(t * water.framesPerSecond) % water.frames) * water.place.height}px`;
    for (let i = 0; i < Math.min(LIGHTS_PER_FRAME, glows.length); i++, next = (next + 1) % glows.length) {
      const glow = glows[next]!;
      const level = glow.lamp ? .8 + .2 * waver(t * 2 * glow.rate, glow.phase) : glow.base + .3 * waver(t * glow.rate, glow.phase);
      glow.el.style.opacity = Math.max(0, Math.min(1, level)).toFixed(2);
    }
    for (const glow of flameGlows) glow.el.style.opacity = (.75 + .25 * waver(t * 2.2, glow.phase)).toFixed(3);
    frame = requestAnimationFrame(animate);
  };

  return {
    element,
    show(scene) {
      const art = { town: image(scene.town.id), clouds: image(scene.clouds.id), sea: image(scene.sea.id), window: image(scene.window.id) };
      if (Object.values(art).some(source => source === null)) { this.hide(); return false; }
      if (shown !== scene) { shown = scene; build(scene); }
      if (town.src !== art.town) town.src = art.town!;
      for (const clouds of [cloudsA, cloudsB]) if (clouds.src !== art.clouds) clouds.src = art.clouds!;
      if (arch.src !== art.window) arch.src = art.window!;
      const strip = cssUrl(art.sea!);
      if (sea.dataset.strip !== scene.sea.id) { sea.style.backgroundImage = strip; sea.dataset.strip = scene.sea.id; }
      element.hidden = false;
      fit();
      if (frame === 0 && typeof requestAnimationFrame === 'function') frame = requestAnimationFrame(animate);
      return true;
    },
    hide() {
      element.hidden = true;
      if (frame !== 0) { cancelAnimationFrame(frame); frame = 0; }
    },
  };
}
