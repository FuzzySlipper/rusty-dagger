import { mountControls, type ControlAction, type ControlsProjection } from './controls.js';
import { mountVideoSettings } from './video.js';

export type SettingsTab = 'game' | 'input' | 'video';

const TABS: readonly SettingsTab[] = ['game', 'input', 'video'];
const TAB_NAMES: Readonly<Record<SettingsTab, string>> = { game: 'Game', input: 'Input', video: 'Video' };

export interface SettingsView {
  /** Called when the Settings panel opens in the game menu. */
  open(): void;
  /** Called when leaving Settings or closing the game menu. */
  close(): void;
  /** Shows one tab of Settings (`game`, `input`, or `video`). */
  select(tab: SettingsTab): void;
  /** The triggers or Page Up and Down: step to the previous or next settings tab. */
  page(direction: -1 | 1): void;
  /** What turning a page does in Settings. */
  pageHint(): string;
  captureKey(event: KeyboardEvent): boolean;
  captureButton(button: string): boolean;
  update(controls: ControlsProjection, effectiveScale: number): void;
  updateTimeControl(enabled: boolean): void;
  dispose(): void;
}

/**
 * The tabbed Settings screen: Game (gameplay, interface scale, sound), Input (look sensitivity,
 * keyboard and controller bindings), and Video (Engine video options plus ground cover, lamp
 * shadows, building relief and interior relief). Tabs follow focus so arrow keys and a controller
 * switch pages naturally, and Page Up/Down or controller triggers step across tabs from anywhere
 * in the panel.
 */
export function mountSettings(root: HTMLElement, send: (action: ControlAction) => void, onTabChange: () => void): SettingsView {
  const shell = document.createElement('section');
  shell.className = 'dagger-settings';
  shell.setAttribute('aria-label', 'Settings');

  const tabs = document.createElement('div');
  tabs.className = 'dagger-settings-tabs';
  tabs.setAttribute('role', 'tablist');
  tabs.setAttribute('aria-label', 'Settings sections');

  const buttons = new Map<SettingsTab, HTMLButtonElement>();
  const panels = new Map<SettingsTab, HTMLElement>();
  for (const tab of TABS) {
    const button = document.createElement('button');
    button.type = 'button';
    button.id = `dagger-settings-tab-${tab}`;
    button.dataset.settingsTab = tab;
    button.textContent = TAB_NAMES[tab];
    button.setAttribute('role', 'tab');
    button.setAttribute('aria-controls', `dagger-settings-panel-${tab}`);
    button.addEventListener('click', () => select(tab));
    // Tabs follow focus, so arrow keys and a controller switch pages as focus moves along the bar.
    button.addEventListener('focus', () => { if (shown !== tab) select(tab); });
    tabs.append(button);
    buttons.set(tab, button);

    const panel = document.createElement('div');
    panel.id = `dagger-settings-panel-${tab}`;
    panel.className = tab === 'input' ? 'dagger-settings-panel dagger-controls-root' : 'dagger-settings-panel';
    panel.dataset.settingsPanel = tab;
    panel.setAttribute('role', 'tabpanel');
    panel.setAttribute('aria-labelledby', button.id);
    panels.set(tab, panel);
  }

  tabs.addEventListener('keydown', event => {
    if (event.code !== 'Home' && event.code !== 'End') return;
    event.preventDefault();
    const target = event.code === 'Home' ? TABS[0]! : TABS[TABS.length - 1]!;
    select(target);
    buttons.get(target)?.focus();
  });

  const gamePanel = panels.get('game')!;
  const inputPanel = panels.get('input')!;
  const videoPanel = panels.get('video')!;
  const videoRoot = document.createElement('div');
  videoRoot.className = 'dagger-video-root';
  videoRoot.hidden = true;
  videoPanel.append(videoRoot);

  const controlsView = mountControls({ game: gamePanel, input: inputPanel }, send);
  const videoView = mountVideoSettings(videoRoot, send);

  shell.append(tabs, gamePanel, inputPanel, videoPanel);
  root.append(shell);

  let shown: SettingsTab = 'game';
  let opened = false;

  const syncVisibility = (): void => {
    for (const [id, button] of buttons) {
      button.setAttribute('aria-selected', String(id === shown));
      // One Tab stop for the tab bar; arrows and controller D-pad reach every tab.
      button.tabIndex = id === shown ? 0 : -1;
    }
    for (const [id, panel] of panels) panel.hidden = id !== shown;
    videoRoot.hidden = !(opened && shown === 'video');
  };

  const select = (tab: SettingsTab): void => {
    const changed = shown !== tab;
    if (changed && shown === 'input') controlsView.cancel();
    shown = tab;
    syncVisibility();
    if (opened && shown === 'video') videoView.show();
    if (changed) onTabChange();
  };

  syncVisibility();

  return {
    open() {
      opened = true;
      syncVisibility();
      if (shown === 'video') videoView.show();
    },
    close() {
      opened = false;
      controlsView.cancel();
      syncVisibility();
    },
    select,
    page(direction) {
      const index = TABS.indexOf(shown);
      const next = TABS[(index + direction + TABS.length) % TABS.length]!;
      select(next);
      buttons.get(next)?.focus();
    },
    pageHint() { return 'Tab'; },
    captureKey(event) { return shown === 'input' && controlsView.captureKey(event); },
    captureButton(button) { return shown === 'input' && controlsView.captureButton(button); },
    update(controls, effectiveScale) {
      controlsView.update(controls, effectiveScale);
      videoView.update(controls.groundCover, controls.lampShadows, controls.surfaceRelief, controls.interiorExtrude);
    },
    updateTimeControl(enabled) {
      controlsView.updateTimeControl(enabled);
    },
    dispose() {
      opened = false;
      controlsView.dispose();
      videoView.dispose();
      shell.remove();
    },
  };
}
