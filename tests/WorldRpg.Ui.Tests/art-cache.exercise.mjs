// DOM component regression, not a gameplay playtest. Run with Engine's
// capture-playtest-warning-delta.mjs --exercise <this file> against a staged host.
export async function exercise({ page, url }) {
  await page.goto(url, { waitUntil: 'domcontentloaded' });
  await page.evaluate(async () => {
    const { mountProductUi } = await import('/product-ui/main.js');
    const { adopt } = await import('/product-ui/art.js');
    const image = color => `data:image/svg+xml,${encodeURIComponent(
      `<svg xmlns="http://www.w3.org/2000/svg" width="8" height="8"><rect width="8" height="8" fill="${color}"/></svg>`)}`;
    const oldImage = image('red');
    const newImage = image('blue');
    const art = (revision, value) => ({ revision, images: [
      { id: 'screen.title', image: value },
      { id: 'inventory.skin.panel-slate.v1', image: value },
      { id: 'inventory.skin.titlebar-slate.v1', image: value },
      { id: 'inventory.skin.grid-slot-slate.v1', image: value },
    ] });
    const assert = (condition, message) => { if (!condition) throw new Error(message); };

    // The module survives a UI remount and still holds the previous publication.
    adopt(art('old-publication', oldImage));
    const root = document.createElement('div');
    document.body.append(root);
    let receive;
    const requests = [];
    const mount = mountProductUi(root, {
      ui: { setInteractionMode() {}, focusGameplay() {} },
      projection: { subscribe(callback) { receive = callback; return () => {}; } },
      intents: { claim(_intent, value) { requests.push(value.data); } },
    });
    const snapshot = (revision, block) => ({ contract: 'dagger.ui.snapshot.v1', value: {
      resources: [], lastOutcome: '', mode: 'title',
      composition: { bundle: 'test', ruleset: 'test', contentPacks: [], tuning: 'test' },
      uiArtRevision: revision, ...(block ? { uiArt: block } : {}),
    } });
    try {
      receive(snapshot('old-publication'));
      const screen = root.querySelector('.dagger-entry-screen');
      assert(screen.src === oldImage, 'Cached publication must initially render.');

      // The initial art-bearing snapshot was missed. Its new token must request the block.
      receive(snapshot('new-publication'));
      assert(requests.some(value => value.action === 'art-request' && value.revision === 'new-publication'),
        'A new publication must request art despite the retained module cache.');
      receive(snapshot('new-publication', art('new-publication', newImage)));
      assert(screen.src === newImage, 'Receiving new art must replace the rendered image.');
      await screen.decode();
      assert(screen.naturalWidth === 8, 'The replacement image must decode in the DOM.');
      const requestCount = requests.length;
      receive(snapshot('new-publication'));
      assert(requests.length === requestCount, 'Cached current art must not request every update.');
      assert(screen.src === newImage, 'Subsequent snapshots must retain the new image.');
    } finally {
      mount.dispose();
      root.remove();
    }
  });
}
