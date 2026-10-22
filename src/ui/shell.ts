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
    <div class="dagger-compass" aria-hidden="true"><div class="dagger-compass-strip"></div><div class="dagger-compass-contacts"></div></div>
    <p class="dagger-view" aria-live="off"></p>
    <div class="dagger-reticle" aria-hidden="true"></div>
    <p class="dagger-prompt" aria-live="polite" hidden><kbd></kbd><span></span></p>
    <section class="dagger-vitals" aria-label="Vitals"></section>
    <ol class="dagger-outcome" role="status" aria-label="Recent messages"><li>Awaiting projection…</li></ol>
    <div class="dagger-breath" hidden><span class="dagger-breath-label">Breath</span><span class="dagger-bar"><span></span></span><strong></strong></div>
    <section class="dagger-loadout" aria-label="Ready">
      <p class="dagger-ready-spell" hidden><span class="dagger-ready-spell-icon"></span><span class="dagger-ready-spell-name"></span></p>
      <p class="dagger-weapon" hidden><span class="dagger-weapon-state"></span><strong class="dagger-weapon-name"></strong></p>
      <span class="dagger-activation-indicator" aria-live="polite"></span>
    </section>
    <section class="dagger-quests" aria-live="polite"></section>
    <section class="dagger-detectors" aria-label="Detected nearby"></section><section class="dagger-status"></section><section class="dagger-effects" aria-label="Active effects" hidden><h2>Active effects</h2><ul></ul></section><button class="dagger-cinematic-skip" type="button" hidden>Skip</button>
    <div class="dagger-death" role="alertdialog" aria-labelledby="dagger-death-title" hidden>
      <img class="dagger-death-screen" alt="You have died.">
      <div class="dagger-death-fade" aria-hidden="true"></div>
      <section class="dagger-death-panel dagger-frame">
        <h1 class="dagger-death-message" id="dagger-death-title"></h1>
        <section class="dagger-death-load" aria-label="Load saved game"><h2>Load saved game</h2><ol class="dagger-death-load-slots"></ol></section>
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
        <section class="dagger-menu-group" aria-label="Character and world">
          <button data-action="inventory" data-icon="inventory">Inventory &amp; equipment · I</button>
          <button data-action="character" data-icon="character">Character · C</button>
          <button data-action="spells" data-icon="spells">Spells</button>
          <button data-action="magic-items" data-icon="magic-items">Magic items · U</button>
          <button data-action="map" data-icon="map">Map</button>
          <button data-action="transport" data-icon="travel">Travel &amp; transport</button>
          <button data-action="rest" data-icon="rest">Rest &amp; loiter</button>
          <button data-action="journal" data-icon="journal">Journal &amp; notes</button>
          <button data-action="loot" data-icon="grab">Activate aimed target · F</button>
          <label class="dagger-menu-field">Activation mode <select class="dagger-activation-mode"></select></label>
        </section>
        <section class="dagger-menu-group" aria-label="Saved games">
          <button data-action="save-game" data-icon="save">Save game</button>
          <button data-action="load-game" data-icon="load">Load game</button>
          <button data-action="quick-save" data-icon="save">Quick save</button>
          <button data-action="quick-load" data-icon="load">Quick load</button>
        </section>
        <section class="dagger-menu-group" aria-label="Settings">
          <button data-action="settings" data-icon="settings">Settings &amp; controls</button>
          <button data-action="action-driven-time" aria-pressed="false">Action-driven time: Off</button>
          <p>Action-driven time pauses while you are idle or looking around. Movement and actions advance the world; swings and jumps finish before it pauses.</p>
          <p>The world pauses while this menu is open.</p>
        </section>
        <details class="dagger-menu-developer">
          <summary>Developer tools</summary>
          <p>Diagnostics for developers, not part of the game.</p>
          <button data-action="debug">Developer tools</button>
          <button data-action="diagnostics">Composition diagnostics</button>
        </details>
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
          <div class="dagger-rest-amount" data-rest-amount="rest"><span>Rest hours</span></div>
          <button type="button" data-action="rest" data-rest-mode="timed">Rest for hours</button>
          <button type="button" data-action="rest" data-rest-mode="until-healed">Rest until healed</button>
          <div class="dagger-rest-amount" data-rest-amount="loiter"><span>Loiter hours</span></div>
          <button type="button" data-action="rest" data-rest-mode="loiter">Loiter</button>
        </div>
      </section>
      <div class="dagger-notebook-root" hidden></div>
      <div class="dagger-loot-root" hidden></div>
      <section class="dagger-save-slots" hidden aria-label="Save slots">
        <p class="dagger-save-slots-diagnostic" role="status"></p>
        <label>Save name <input class="dagger-save-slots-label" maxlength="120" autocomplete="off"></label>
        <ol class="dagger-save-slots-list" aria-label="Saved games"></ol>
        <div class="dagger-save-slots-actions">
          <button data-action="save-slot">Save</button>
          <button data-action="load-slot">Load selected</button>
          <button data-action="delete-slot">Delete selected</button>
        </div>
      </section>
      <button data-action="loot-exit" hidden>Exit loot</button>
      <button data-action="back">Back to menu</button>
      </div>
    </dialog>
    <dialog class="dagger-dialogue dagger-frame" data-skin="parchment" aria-labelledby="dagger-dialogue-title">
      <h2 id="dagger-dialogue-title" class="dagger-dialogue-target dagger-frame-title"></h2>
      <div class="dagger-dialogue-tabs" role="tablist" aria-label="Services" hidden></div>
      <div class="dagger-dialogue-body">
        <div class="dagger-dialogue-page dagger-dialogue-chooser" data-page="services" role="tabpanel" aria-label="Services" hidden>
          <div class="dagger-dialogue-service-list"></div>
        </div>
        <div class="dagger-dialogue-page" data-page="talk" role="tabpanel" aria-label="Talk" hidden>
          <div class="dagger-dialogue-head">
            <p class="dagger-dialogue-greeting" aria-live="polite"></p>
            <label>Tone <select class="dagger-dialogue-tone"></select></label>
          </div>
          <ol class="dagger-dialogue-log" aria-label="Conversation" aria-live="polite"></ol>
          <div class="dagger-dialogue-topics"></div>
          <div class="dagger-dialogue-donation" hidden><span>Donation</span></div>
        </div>
        <div class="dagger-dialogue-page" data-page="trade" role="tabpanel" aria-label="Trade" hidden>
          <section class="dagger-dialogue-merchant" aria-label="Merchant services"></section>
        </div>
        <div class="dagger-dialogue-page" data-page="training" role="tabpanel" aria-label="Training" hidden>
          <section class="dagger-dialogue-training" aria-label="Skill training">
            <h3>Training</h3>
            <p class="dagger-dialogue-training-summary"></p>
            <div class="dagger-dialogue-training-skills"></div>
          </section>
        </div>
        <div class="dagger-dialogue-page" data-page="tavern" role="tabpanel" aria-label="Food and drink" hidden>
          <section class="dagger-dialogue-tavern" aria-label="Tavern food and drink"></section>
        </div>
        <div class="dagger-dialogue-page" data-page="spells" role="tabpanel" aria-label="Spells for sale" hidden>
          <section class="dagger-dialogue-spells" aria-label="Spells for sale"></section>
        </div>
        <div class="dagger-dialogue-page" data-page="item-maker" role="tabpanel" aria-label="Item enchanting" hidden>
          <section class="dagger-dialogue-itemmaker" aria-label="Item enchanting"></section>
        </div>
        <div class="dagger-dialogue-page" data-page="potion-maker" role="tabpanel" aria-label="Potion making" hidden>
          <section class="dagger-dialogue-potionmaker" aria-label="Potion making"></section>
        </div>
        <div class="dagger-dialogue-page" data-page="spell-maker" role="tabpanel" aria-label="Spell construction" hidden>
          <section class="dagger-dialogue-spellmaker" aria-label="Spell construction"></section>
        </div>
        <div class="dagger-dialogue-page" data-page="summoning" role="tabpanel" aria-label="Daedric summoning" hidden>
          <section class="dagger-dialogue-summoning" aria-label="Daedric summoning"></section>
        </div>
      </div>
      <div class="dagger-dialogue-footer">
        <button class="dagger-dialogue-back" type="button" hidden>Back to services</button>
        <button class="dagger-dialogue-close" type="button">End conversation</button>
      </div>
    </dialog><dialog class="dagger-daedric-offer dagger-frame" aria-label="Daedric quest offer"></dialog>`;
