// The matched Engine runtime supplies this module through its browser import map.
declare module '@rusty-engine/live-debug' {
  export interface LiveDebugPanelMount { dispose(): void; }
  export function mountLiveDebugPanel(host: HTMLElement, options: {
    readonly enabled: boolean;
    readonly presentation?: 'inline' | 'dock' | 'overlay';
  }): Promise<LiveDebugPanelMount>;
}
