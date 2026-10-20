import { isItemMakerProjection, isPotionMakerProjection, isSpellMakerProjection, isSummoningProjection } from './crafting.js';
import type { ConfirmQuestion } from './confirm.js';
import type { Send } from './host.js';
import type { SpellbookProjection } from './projection.js';
import { keyLabel, type ControlsProjection } from './controls.js';
import { redraw } from './redraw.js';
import { isSpellIconAtlas, spellIcon, spellIconsReady } from './spell-icon.js';

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
  if (action === 'spell-cast') {
    // Casting a spell from the book readies it first, as the donor's book readies the spell it closes on.
    if (key && control.dataset.ready === 'false') send({ action: 'spell-ready', key });
    onCast(); send({ action }); return true;
  }
  return false;
}

export function isSpellbookProjection(value:unknown):value is SpellbookProjection {
  if(!value || typeof value!=='object') return false;
  const v=value as Partial<SpellbookProjection>;
  return Array.isArray(v.available) && v.available.every(spell=>spell && typeof spell==='object'
    && typeof spell.key==='string' && typeof spell.name==='string' && Number.isInteger(spell.cost) && spell.cost>=0
    && (spell.canCast===undefined || typeof spell.canCast==='boolean')
    && (spell.icon===undefined || Number.isSafeInteger(spell.icon) && spell.icon>=0) && (spell.school===undefined || typeof spell.school==='string'))
    && (v.icons===undefined || isSpellIconAtlas(v.icons))
    && (v.schools===undefined || Array.isArray(v.schools) && v.schools.every(school=>school && typeof school.id==='string' && typeof school.label==='string'))
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

/** What the player is looking at in the book: the chosen school tab and the chosen spell. Neither is game state. */
interface SpellbookPlace { school: string; selected: string | null; view: SpellbookProjection | null; hints: string; send: Send | null }
const places = new WeakMap<HTMLElement, SpellbookPlace>();
const ALL_SCHOOLS = 'all';

/**
 * The spellbook: school tabs over the known spells, each row its icon, name and cost, and beside it the
 * chosen spell's description with the commands that act on it. Choosing a row describes it; activating
 * it (double-click, or Set active) readies it. The recast and abort keys come from the bound controls.
 */
export function renderSpells(root:HTMLElement,view:SpellbookProjection|null,controls:ControlsProjection|null|undefined,send:Send):void {
  let place=places.get(root);
  if(!place) {
    place={school:ALL_SCHOOLS,selected:null,view:null,hints:'',send:null};places.set(root,place);
    const current=place;
    root.addEventListener('click',event=>{
      const control=event.target instanceof Element ? event.target.closest<HTMLButtonElement>('button') : null;
      if(!control || control.disabled) return;
      if(control.dataset.spellSchool!==undefined) { current.school=control.dataset.spellSchool;current.selected=null;draw(root,current); }
      else if(control.dataset.action==='spell-select' && control.dataset.spell) select(current,control.dataset.spell,root);
    });
    root.addEventListener('dblclick',event=>{
      const control=event.target instanceof Element ? event.target.closest<HTMLButtonElement>('[data-action="spell-select"]') : null;
      const spell=current.view?.available.find(candidate=>candidate.key===control?.dataset.spell);
      if(spell && spell.canCast!==false) current.send?.({action:'spell-ready',key:spell.key});
    });
  }
  place.view=view;place.send=send;
  const binding=(id:string)=>controls?.bindings.find(candidate=>candidate.id===id)?.keys.map(keyLabel).join(' or ') ?? null;
  const recast=binding('recast'),abort=binding('abort-spell');
  place.hints=[recast && `Recast: ${recast}`,abort && `Abort: ${abort}`].filter(Boolean).join(' · ');
  draw(root,place);
}

function select(place:SpellbookPlace,key:string,root:HTMLElement):void {
  place.selected=key;
  if(place.view?.information?.key!==key) place.send?.({action:'spell-info',key});
  draw(root,place);
}

function visible(place:SpellbookPlace):SpellbookProjection['available'] {
  const spells=place.view?.available ?? [];
  return place.school===ALL_SCHOOLS ? spells : spells.filter(spell=>spell.school===place.school);
}

function draw(root:HTMLElement,place:SpellbookPlace):void {
  const view=place.view;
  const shown=visible(place);
  // The chosen spell stays chosen while it is listed; otherwise the readied spell, then the first row.
  if(!shown.some(spell=>spell.key===place.selected)) place.selected=(shown.find(spell=>spell.key===view?.ready) ?? shown[0])?.key ?? null;
  redraw(root, JSON.stringify([view,place.school,place.selected,place.hints,spellIconsReady(view?.icons)]), () => drawSpells(root,place,shown));
}

function drawSpells(root:HTMLElement,place:SpellbookPlace,shown:SpellbookProjection['available']):void {
  // The menu already titles this panel, so the book carries no heading of its own.
  const view=place.view;
  root.replaceChildren();
  const status=document.createElement('p');status.className='dagger-spellbook-status';status.setAttribute('role','status');
  status.textContent=view?.result ?? '';root.append(status);
  if(!view) return;
  const button=(action:string,text:string,label?:string):HTMLButtonElement=>{
    const control=document.createElement('button');control.type='button';control.dataset.action=action;control.textContent=text;
    if(label!==undefined){control.setAttribute('aria-label',label);control.dataset.tooltip=label;}
    return control;
  };
  const toolbar=document.createElement('div');toolbar.className='dagger-spellbook-toolbar';
  toolbar.append(button('spell-sort','Sort by name, then cost'));
  if(place.hints) { const hints=document.createElement('span');hints.className='dagger-spellbook-keys';hints.textContent=place.hints;toolbar.append(hints); }
  // One tab for every school, each counting its spells, after one for the whole book.
  const tabs=document.createElement('div');tabs.className='dagger-spellbook-tabs';tabs.setAttribute('role','tablist');tabs.setAttribute('aria-label','Schools');
  for(const school of [{id:ALL_SCHOOLS,label:'All'},...(view.schools ?? [])]) {
    const count=school.id===ALL_SCHOOLS ? view.available.length : view.available.filter(spell=>spell.school===school.id).length;
    const tab=document.createElement('button');tab.type='button';tab.dataset.spellSchool=school.id;tab.setAttribute('role','tab');
    tab.setAttribute('aria-selected',String(place.school===school.id));tab.textContent=`${school.label} ${count}`;
    tab.disabled=count===0 && place.school!==school.id;tabs.append(tab);
  }
  const list=document.createElement('ol');list.className='dagger-spellbook-list';
  for(const spell of shown) {
    const row=document.createElement('li');row.className='dagger-spellbook-row';
    row.classList.toggle('is-ready',view.ready===spell.key);
    const entry=document.createElement('button');entry.type='button';entry.className='dagger-spellbook-entry';
    entry.dataset.action='spell-select';entry.dataset.spell=spell.key;entry.setAttribute('aria-label',spell.name);
    entry.setAttribute('aria-pressed',String(place.selected===spell.key));
    const name=document.createElement('span');name.className='dagger-spellbook-name';name.textContent=spell.name;
    const cost=document.createElement('span');cost.className='dagger-spellbook-cost';
    cost.textContent=`${spell.canCast===false ? 'Unavailable' : `${spell.cost} magicka`}${view.ready===spell.key ? ' · Ready' : ''}`;
    entry.append(spellIcon(view.icons,spell.icon ?? 0),name,cost);
    row.append(entry);list.append(row);
  }
  const body=document.createElement('div');body.className='dagger-spellbook-body';
  body.append(list);
  if(shown.length===0) { const empty=document.createElement('p');empty.textContent='No available known spells.';body.append(empty); }
  const chosen=view.available.find(spell=>spell.key===place.selected);
  if(chosen) {
    const index=view.available.indexOf(chosen);
    const details=document.createElement('section');details.className='dagger-spellbook-details dagger-panel';
    const heading=document.createElement('h3');heading.append(spellIcon(view.icons,chosen.icon ?? 0),chosen.name);
    const school=view.schools?.find(candidate=>candidate.id===chosen.school)?.label;
    const facts=document.createElement('p');facts.textContent=[chosen.canCast===false ? 'Unavailable' : `${chosen.cost} magicka`,school,view.ready===chosen.key ? 'Ready' : null].filter(Boolean).join(' · ');
    details.append(heading,facts);
    if(view.information?.key===chosen.key) renderSpellInformation(details,view,false);
    const commands=document.createElement('div');commands.className='dagger-spellbook-commands';
    const isReady=view.ready===chosen.key;
    const activate=isReady ? button('spell-unready','Unready') : button('spell-ready','Set active',`Set ${chosen.name} active`);
    activate.dataset.spell=chosen.key;activate.disabled=!isReady && chosen.canCast===false;
    const cast=button('spell-cast','Cast',`Cast ${chosen.name}`);cast.dataset.spell=chosen.key;cast.dataset.ready=String(isReady);cast.disabled=chosen.canCast===false;
    // The book's order is the player's, so moving a spell swaps it with its neighbour in the whole book.
    const moves=([[-1,'Move up'],[1,'Move down']] as const).map(([offset,label])=>{
      const other=view.available[index+offset];
      const move=button('spell-swap',label,`${label} ${chosen.name}`);move.dataset.spell=chosen.key;move.dataset.swapWith=other?.key;
      move.disabled=!other;return move;
    });
    const forget=button('spell-delete','Forget',`Forget ${chosen.name}`);forget.dataset.spell=chosen.key;forget.classList.add('dagger-spellbook-forget');
    commands.append(activate,cast,...moves,forget);details.append(commands);body.append(details);
  }
  root.append(toolbar,tabs,body);
}

export function renderSpellInformation(root:HTMLElement,view:SpellbookProjection,titled=true):void {
  if(!view.information) return;
  const section=document.createElement('section');section.className='dagger-spellbook-information';
  if(titled) { const title=document.createElement('h3');title.textContent=view.information.name;section.append(title); }
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

