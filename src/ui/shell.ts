/**
 * The page's one shell: the HUD and every surface the product UI draws, in the stacking order the
 * stylesheets assume. Each surface module finds its own elements here by class.
 */
/** `skin.css` comes last: it draws every primitive from the kit, and screens only lay out. */
const STYLESHEETS = ['styles.css', 'inventory.css', 'character.css', 'loot.css', 'skin.css'];

export function mountShell(root: HTMLElement): { shell: HTMLElement; dispose(): void } {
  const links = STYLESHEETS.map(name => {
    const link = document.createElement('link');
    link.rel = 'stylesheet';
    link.href = new URL(`./${name}`, import.meta.url).href;
    return link;
  });
  document.head.append(...links);
  const shell = document.createElement('section');
  shell.className = 'dagger-hud';
  shell.innerHTML = SHELL_MARKUP;
  root.append(shell);
  return { shell, dispose() { for (const link of links) link.remove(); shell.remove(); } };
}

const SHELL_MARKUP = `
    <div class="dagger-title"><span class="dagger-site"></span><strong>Exploring</strong><span class="dagger-calendar" aria-live="off"></span></div>
    <div class="dagger-reticle" aria-hidden="true">+</div>
    <section class="dagger-vitals" aria-live="polite">
    </section>
    <ol class="dagger-outcome" role="status" aria-label="Recent messages"><li>Awaiting projection…</li></ol>
    <label class="dagger-breath" hidden>Breath <meter min="0" max="1" value="1"></meter><span></span></label>
    <span class="dagger-activation-indicator" aria-live="polite"></span>
    <section class="dagger-quests" aria-live="polite"></section>
    <p class="dagger-view" aria-live="polite"></p><section class="dagger-detectors" aria-label="Detected nearby objects"></section><section class="dagger-status"></section><section class="dagger-effects" aria-label="Active effects" hidden><h2>Active effects</h2><ul></ul></section><button class="dagger-cinematic-skip" type="button" hidden>Skip</button>
    <div class="dagger-death" role="alertdialog" aria-labelledby="dagger-death-title" hidden>
      <img class="dagger-death-screen" alt="You have died.">
      <div class="dagger-death-fade" aria-hidden="true"></div>
      <section class="dagger-death-panel dagger-frame">
        <h1 class="dagger-death-message" id="dagger-death-title"></h1>
        <label class="dagger-death-load">Load saved game <select class="dagger-death-load-slot"><option value="">Choose a saved game</option></select></label>
        <div class="dagger-death-actions">
          <button class="dagger-death-new" type="button">New game</button>
          <button class="dagger-death-load-button" type="button">Load game</button>
          <button class="dagger-death-quit" type="button">Quit to title</button>
        </div>
      </section>
    </div>
    <section class="dagger-dungeon-text dagger-frame" data-skin="parchment" role="dialog" aria-label="Dungeon text" hidden>
      <p class="dagger-dungeon-text-body"></p>
      <form class="dagger-dungeon-text-answer" hidden><label>Answer <input maxlength="256" autocomplete="off"></label><button type="submit">Answer</button></form>
      <button class="dagger-dungeon-text-close" type="button">Continue</button>
    </section>
    <div class="dagger-entry" role="dialog" aria-label="Title" hidden><img class="dagger-entry-screen" alt="Rusty Dagger"><button class="dagger-entry-begin" type="button">Begin</button></div>
    <button class="dagger-menu-toggle" type="button" data-action="menu" aria-haspopup="dialog">Menu · Esc</button>
    <dialog class="dagger-menu dagger-frame" aria-labelledby="dagger-menu-title">
      <h1 id="dagger-menu-title" class="dagger-frame-title" tabindex="-1">Game menu</h1>
      <div class="dagger-menu-home">
        <button data-action="resume" autofocus>Return to game</button>
        <button data-action="inventory" data-icon="inventory">Inventory &amp; equipment · I</button>
        <button data-action="character" data-icon="character">Character · C</button>
        <button data-action="spells" data-icon="spells">Spells</button>
        <button data-action="magic-items" data-icon="magic-items">Magic items · U</button>
        <button data-action="map" data-icon="map">Map</button>
        <button data-action="transport" data-icon="travel">Travel &amp; transport</button>
        <button data-action="rest" data-icon="rest">Rest &amp; loiter</button>
        <button data-action="journal" data-icon="journal">Journal &amp; notes</button>
        <button data-action="loot">Activate aimed target · F</button>
        <label>Activation mode <select class="dagger-activation-mode"><option value="grab">Grab</option><option value="info">Information</option><option value="talk">Talk</option><option value="steal">Steal / Lockpick</option><option value="bash">Bash</option></select></label>
        <button data-action="save-game" data-icon="save">Save game</button>
        <button data-action="load-game" data-icon="load">Load game</button>
        <button data-action="quick-save">Quick save</button>
        <button data-action="quick-load">Quick load</button>
        <button data-action="debug">Engine debug console</button>
        <button data-action="diagnostics">Composition diagnostics</button>
        <button data-action="settings" data-icon="settings">Control settings</button>
        <button data-action="action-driven-time" aria-pressed="false">Action-driven time: Off</button>
        <p>Action-driven time pauses while you are idle or looking around. Movement and actions advance the world; swings and jumps finish before it pauses.</p>
        <p>The world pauses while this menu is open.</p>
      </div>
      <div class="dagger-menu-panel" hidden>
      <section class="dagger-composition" aria-label="Resolved composition diagnostics">
      <strong>Resolved composition</strong>
      <dl></dl>
      <strong>Text diagnostics</strong>
      <ul class="dagger-text-diagnostics" aria-label="Text diagnostics"></ul>
    </section>
      <div class="dagger-controls-root" hidden></div>
      <div class="dagger-inventory-root" hidden></div>
      <div class="dagger-character-root" hidden></div>
      <section class="dagger-spells-root" hidden aria-label="Known spells"></section>
      <div class="dagger-map-root" hidden></div>
      <div class="dagger-transport-root" hidden></div>
      <section class="dagger-rest-root" hidden aria-label="Rest and loiter">
        <p class="dagger-rest-status" role="status" aria-live="polite">Choose a rest action.</p>
        <div class="dagger-rest-actions">
          <label>Rest hours <input class="dagger-rest-hours" type="number" min="1" max="99" step="1" value="1"></label>
          <button type="button" data-action="rest" data-rest-mode="timed">Rest for hours</button>
          <button type="button" data-action="rest" data-rest-mode="until-healed">Rest until healed</button>
          <label>Loiter hours <input class="dagger-loiter-hours" type="number" min="1" max="3" step="1" value="1"></label>
          <button type="button" data-action="rest" data-rest-mode="loiter">Loiter</button>
        </div>
      </section>
      <div class="dagger-notebook-root" hidden></div>
      <div class="dagger-loot-root" hidden></div>
      <section class="dagger-save-slots" hidden aria-label="Save slots">
        <p class="dagger-save-slots-diagnostic" role="status"></p>
        <label>Save name <input class="dagger-save-slots-label" maxlength="120" autocomplete="off"></label>
        <label>Selected slot <select class="dagger-save-slots-select"><option value="">New slot</option></select></label>
        <div class="dagger-save-slots-actions">
          <button data-action="save-slot">Save</button>
          <button data-action="load-slot">Load selected</button>
          <button data-action="delete-slot">Delete selected</button>
        </div>
      </section>
      <div class="dagger-debug-root" data-rusty-ui-interactive hidden></div>
      <button data-action="loot-exit" hidden>Exit loot</button>
      <button data-action="back">Back to menu</button>
      </div>
    </dialog>
    <dialog class="dagger-dialogue dagger-frame" data-skin="parchment" aria-labelledby="dagger-dialogue-title">
      <h2 id="dagger-dialogue-title" class="dagger-dialogue-target dagger-frame-title"></h2>
      <p class="dagger-dialogue-greeting" aria-live="polite"></p>
      <label>Tone <select class="dagger-dialogue-tone"><option value="polite">Polite</option><option value="normal">Normal</option><option value="blunt">Blunt</option></select></label>
      <label class="dagger-dialogue-donation" hidden>Donation gold <input class="dagger-dialogue-donation-amount" type="number" min="1" step="1" value="1" inputmode="numeric"></label>
      <p class="dagger-dialogue-question" aria-live="polite"></p>
      <p class="dagger-dialogue-reply" aria-live="polite"></p>
      <div class="dagger-dialogue-topics"></div>
      <section class="dagger-dialogue-training" aria-label="Skill training" hidden>
        <h3>Skill training</h3>
        <p class="dagger-dialogue-training-summary"></p>
        <div class="dagger-dialogue-training-skills"></div>
        <div class="dagger-training-confirm" hidden><p></p><button type="button" data-training-yes>Yes</button><button type="button" data-training-no>No</button></div>
      </section>
      <section class="dagger-dialogue-tavern" aria-label="Tavern food and drink"></section>
      <section class="dagger-dialogue-merchant" aria-label="Merchant services"></section>
      <section class="dagger-dialogue-spells" aria-label="Spells for sale"></section>
      <section class="dagger-dialogue-summoning" aria-label="Daedric summoning"></section>
      <section class="dagger-dialogue-itemmaker" aria-label="Item enchanting"></section>
      <section class="dagger-dialogue-potionmaker" aria-label="Potion making"></section>
      <section class="dagger-dialogue-spellmaker" aria-label="Spell construction"></section>
      <button class="dagger-dialogue-close" type="button">End conversation</button>
    </dialog><dialog class="dagger-daedric-offer dagger-frame" aria-label="Daedric quest offer"></dialog>`;
