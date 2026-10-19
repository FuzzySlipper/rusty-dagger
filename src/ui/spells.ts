import { isItemMakerProjection, isPotionMakerProjection, isSpellMakerProjection, isSummoningProjection } from './crafting.js';
import type { ConfirmQuestion } from './confirm.js';
import type { Send } from './host.js';
import type { SpellbookProjection } from './projection.js';
import { redraw } from './redraw.js';

/**
 * Performs the spell action a clicked control names, for the spellbook and a spell seller alike.
 * Buying and forgetting ask first. Returns false when the control is not a spell action.
 */
export function spellAction(control: HTMLButtonElement, send: Send, ask: (question: ConfirmQuestion) => void, onCast: () => void): boolean {
  const action = control.dataset.action;
  const key = control.dataset.spell;
  if ((action === 'spell-buy' || action === 'spell-delete' || action === 'spell-info') && key) {
    if (action === 'spell-info') { send({ action, key }); return true; }
    if (action === 'spell-buy') {
      const data = { action, key, revision: control.dataset.revision, amount: Number(control.dataset.price), confirm: true };
      ask({ message: `Buy this spell for ${control.dataset.price} gold?`, accept: 'Buy', onAccept: () => send(data) });
    } else {
      const data = { action, key, confirm: true };
      ask({ message: `${control.getAttribute('aria-label') ?? 'Forget this spell'}?`, accept: 'Forget', onAccept: () => send(data) });
    }
    return true;
  }
  if (action === 'spell-sort') { send({ action }); return true; }
  if (action === 'spell-swap') { send({ action, key, item: control.dataset.swapWith }); return true; }
  if (action === 'spell-ready' && key) { send({ action, key }); return true; }
  if (action === 'spell-unready') { send({ action }); return true; }
  if (action === 'spell-cast') { onCast(); send({ action }); return true; }
  return false;
}

export function isSpellbookProjection(value:unknown):value is SpellbookProjection {
  if(!value || typeof value!=='object') return false;
  const v=value as Partial<SpellbookProjection>;
  return Array.isArray(v.available) && v.available.every(spell=>spell && typeof spell==='object'
    && typeof spell.key==='string' && typeof spell.name==='string' && Number.isInteger(spell.cost) && spell.cost>=0
    && (spell.canCast===undefined || typeof spell.canCast==='boolean'))
    && (v.ready===null || typeof v.ready==='string' && v.available.some(spell=>spell.key===v.ready)) && typeof v.result==='string'
    && (v.sale==null || typeof v.sale.revision==='string' && typeof v.sale.provider==='string' && Array.isArray(v.sale.offers)
      && v.sale.offers.every(offer=>offer && typeof offer==='object' && typeof offer.key==='string' && typeof offer.name==='string' && Number.isSafeInteger(offer.price)
        && offer.price>=0 && Number.isInteger(offer.castingCost) && offer.castingCost>=0 && typeof offer.known==='boolean'))
    && (v.maker==null || isSpellMakerProjection(v.maker))
    && (v.potionMaker==null || isPotionMakerProjection(v.potionMaker))
    && (v.itemMaker==null || isItemMakerProjection(v.itemMaker))
    && (v.summoning==null || isSummoningProjection(v.summoning))
    && (v.information==null || typeof v.information.key==='string' && typeof v.information.name==='string'
      && typeof v.information.target==='string' && typeof v.information.element==='string'
      && Array.isArray(v.information.details) && v.information.details.every(line=>typeof line==='string'));
}

export function renderSpells(root:HTMLElement,view:SpellbookProjection|null):void {
  redraw(root, JSON.stringify(view), () => drawSpells(root, view));
}

function drawSpells(root:HTMLElement,view:SpellbookProjection|null):void {
  // The menu already titles this panel, so the book carries no heading of its own.
  root.replaceChildren();
  const status=document.createElement('p');status.className='dagger-spellbook-status';status.setAttribute('role','status');
  status.textContent=view?.result ?? '';root.append(status);
  if(!view) return;
  const button=(action:string,text:string,label?:string):HTMLButtonElement=>{
    const control=document.createElement('button');control.type='button';control.dataset.action=action;control.textContent=text;
    if(label!==undefined){control.setAttribute('aria-label',label);control.dataset.tooltip=label;}
    return control;
  };
  // The spellbook reads as the donor's: the known spells in book order, each readied by choosing it, with
  // its own order and removal controls, beside the chosen spell's description and the cast controls above.
  const toolbar=document.createElement('div');toolbar.className='dagger-spellbook-toolbar';
  const cast=button('spell-cast','Cast ready spell');const unready=button('spell-unready','Unready');
  cast.disabled=unready.disabled=view.ready===null;
  toolbar.append(cast,unready,button('spell-sort','Sort by name, then cost'));
  const list=document.createElement('ol');list.className='dagger-spellbook-list';
  for(const [index, spell] of view.available.entries()) {
    const row=document.createElement('li');row.className='dagger-spellbook-row';
    row.classList.toggle('is-ready',view.ready===spell.key);
    const ready=button('spell-ready',`${spell.name} · ${spell.canCast===false ? 'Unavailable' : `${spell.cost} magicka`}${view.ready===spell.key ? ' · Ready' : ''}`);
    ready.className='dagger-spellbook-ready';ready.dataset.spell=spell.key;
    ready.disabled=spell.canCast===false;ready.setAttribute('aria-pressed',String(view.ready===spell.key));
    const info=button('spell-info','Info',`Describe ${spell.name}`);info.dataset.spell=spell.key;
    const moves=([[-1,'▲','Move up'],[1,'▼','Move down']] as const).map(([offset,glyph,label])=>{
      const other=view.available[index+offset];
      const move=button('spell-swap',glyph,`${label} ${spell.name}`);move.dataset.spell=spell.key;move.dataset.swapWith=other?.key;
      move.disabled=!other;return move;
    });
    const forget=button('spell-delete','Forget',`Forget ${spell.name}`);forget.dataset.spell=spell.key;forget.classList.add('dagger-spellbook-forget');
    row.append(ready,info,...moves,forget);list.append(row);
  }
  const body=document.createElement('div');body.className='dagger-spellbook-body';
  body.append(list);
  if(view.available.length===0) { const empty=document.createElement('p');empty.textContent='No available known spells.';body.append(empty); }
  renderSpellInformation(body,view);
  root.append(toolbar,body);
}

export function renderSpellInformation(root:HTMLElement,view:SpellbookProjection):void {
  if(!view.information) return;
  const section=document.createElement('section');section.className='dagger-spellbook-information';const title=document.createElement('h3');title.textContent=view.information.name;section.append(title);
  const target=document.createElement('p');target.textContent=`Target: ${view.information.target}; element: ${view.information.element}`;section.append(target);
  for(const line of view.information.details) { const p=document.createElement('p');p.textContent=line;section.append(p); } root.append(section);
}

export function renderSpellSales(root:HTMLElement,view:SpellbookProjection|null):void {
  redraw(root, JSON.stringify(view && [view.sale, view.result, view.information]), () => drawSpellSales(root, view));
}

function drawSpellSales(root:HTMLElement,view:SpellbookProjection|null):void {
  root.replaceChildren();if(!view?.sale) return;
  const heading=document.createElement('h3');heading.textContent=`Spells offered by ${view.sale.provider}`;root.append(heading);
  const status=document.createElement('p');status.setAttribute('role','status');status.textContent=view.result;root.append(status);
  for(const offer of view.sale.offers) {
    const buy=document.createElement('button');buy.type='button';buy.dataset.action='spell-buy';buy.dataset.spell=offer.key;
    buy.dataset.revision=view.sale.revision;buy.dataset.price=String(offer.price);buy.disabled=offer.known;
    buy.textContent=`${offer.name} · ${offer.price} gold · ${offer.castingCost} magicka${offer.known ? ' · Known' : ''}`;root.append(buy);
    const info=document.createElement('button');info.type='button';info.dataset.action='spell-info';info.dataset.spell=offer.key;
    info.textContent=`Info ${offer.name}`;root.append(info);
  }
  renderSpellInformation(root,view);
}

