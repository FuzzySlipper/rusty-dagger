import { image } from './art.js';
import type { ConfirmQuestion } from './confirm.js';
import { createStepper, type Stepper } from './stepper.js';
import type {
  ItemMakerProjection, ItemMakerSetting, PotionMakerProjection, SpellMakerOption, SpellMakerProjection, SpellMakerSetting,
  SummoningProjection, UiAction,
} from './projection.js';

export function isSpellMakerProjection(value:unknown):value is SpellMakerProjection {
  if(!value || typeof value!=='object') return false;
  const v=value as Partial<SpellMakerProjection>;const draft=v.draft;
  const options=(list:unknown):boolean=>Array.isArray(list) && list.every(o=>o && typeof o==='object' && Number.isInteger(o.value) && typeof o.label==='string');
  const limits=v.limits;
  return typeof v.revision==='string' && typeof v.provider==='string' && Array.isArray(v.effects) && options(v.targets) && options(v.elements)
    && !!limits && ['nameLength','icons','effects','duration','chance','magnitude','perLevels'].every(key=>Number.isSafeInteger(limits[key as keyof typeof limits]) && limits[key as keyof typeof limits]>0)
    && v.effects.every(e=>e && typeof e.key==='string' && typeof e.name==='string' && typeof e.school==='string' && Number.isInteger(e.type)
      && Number.isInteger(e.subType) && typeof e.duration==='boolean' && typeof e.chance==='boolean' && typeof e.magnitude==='boolean'
      && Number.isInteger(e.targets) && Number.isInteger(e.elements))
    && !!draft && typeof draft.name==='string' && Number.isInteger(draft.element) && Number.isInteger(draft.rangeType)
    && Number.isInteger(draft.icon) && Array.isArray(draft.effects)
    && draft.effects.every(e=>e && typeof e.key==='string'
      && ['type','subType','durationBase','durationMod','durationPerLevel','chanceBase','chanceMod','chancePerLevel',
        'magnitudeBaseLow','magnitudeBaseHigh','magnitudeLevelBase','magnitudeLevelHigh','magnitudePerLevel']
        .every(key=>Number.isInteger(e[key as keyof SpellMakerSetting])))
    && (v.quote===null || !!v.quote && typeof v.quote.key==='string' && Number.isSafeInteger(v.quote.gold)
      && v.quote.gold>=0 && Number.isSafeInteger(v.quote.spellPoints) && v.quote.spellPoints>=0
      && typeof v.quote.eligible==='boolean' && (v.quote.reason===null || typeof v.quote.reason==='string'));
}

/**
 * The spellmaker: name, target, element, an icon picker and the effect slots, every field bounded by
 * the limits the ruleset publishes. Update the cost preview to quote the draft; a quoted spell is
 * bought through the drawn confirm.
 */
export function renderSpellMaker(root:HTMLElement,view:SpellMakerProjection|null,claim:(action:UiAction)=>void,ask:(question:ConfirmQuestion)=>void):void {
  const stamp=JSON.stringify(view);
  // Admitted modal updates can publish the same projection while the player types.
  if(root.dataset.spellMaker===stamp) return;
  root.dataset.spellMaker=stamp;root.replaceChildren();if(!view) return;
  const limits=view.limits;
  const title=document.createElement('h3');title.textContent=`Make a spell with ${view.provider}`;root.append(title);
  const form=document.createElement('form');form.className='dagger-maker-form';root.append(form);
  const field=(label:string,control:HTMLElement):void=>{const wrap=document.createElement('label');wrap.className='dagger-maker-field';
    const text=document.createElement('span');text.textContent=label;wrap.append(text,control);form.append(wrap);};
  const name=document.createElement('input');name.name='name';name.value=view.draft.name;name.maxLength=limits.nameLength;name.required=true;field('Name',name);
  const select=(label:string,values:readonly SpellMakerOption[],selected:number):HTMLSelectElement=>{
    const control=document.createElement('select');control.setAttribute('aria-label',label);
    values.forEach(value=>control.append(new Option(value.label,String(value.value))));control.value=String(selected);field(label,control);return control;
  };
  const target=select('Target',view.targets,view.draft.rangeType);
  const element=select('Element',view.elements,view.draft.element);
  // The icon picker: one button per published icon, the spell's own picture where the art carries it.
  let icon=Math.min(Math.max(0,view.draft.icon),limits.icons-1);
  const icons=document.createElement('div');icons.className='dagger-spell-icons';icons.setAttribute('role','radiogroup');icons.setAttribute('aria-label','Icon');
  for(let index=0;index<limits.icons;index++){
    const choice=document.createElement('button');choice.type='button';choice.className='dagger-spell-icon dagger-slot';choice.dataset.spellIcon=String(index);
    choice.setAttribute('role','radio');choice.setAttribute('aria-label',`Icon ${index+1}`);choice.setAttribute('aria-checked',String(index===icon));
    const source=image(`spell.icon.${index}`);
    if(source!==null){const picture=document.createElement('img');picture.src=source;picture.alt='';picture.draggable=false;choice.append(picture);}
    else choice.textContent=String(index+1);
    icons.append(choice);
  }
  icons.addEventListener('click',event=>{const choice=(event.target as HTMLElement).closest<HTMLButtonElement>('[data-spell-icon]');if(!choice) return;
    icon=Number(choice.dataset.spellIcon);for(const other of icons.querySelectorAll<HTMLElement>('[data-spell-icon]')) other.setAttribute('aria-checked',String(other===choice));
    form.dispatchEvent(new Event('change',{bubbles:true}));});
  const iconField=document.createElement('div');iconField.className='dagger-maker-field';const iconLabel=document.createElement('span');iconLabel.textContent='Icon';iconField.append(iconLabel,icons);form.append(iconField);
  const slots:{select:HTMLSelectElement;values:Map<keyof SpellMakerSetting,Stepper>}[]=[];
  for(let slot=0;slot<limits.effects;slot++) {
    const section=document.createElement('fieldset');section.className='dagger-maker-effect';const legend=document.createElement('legend');legend.textContent=`Effect ${slot+1}`;section.append(legend);form.append(section);
    const picker=document.createElement('select');picker.setAttribute('aria-label',`Effect ${slot+1}`);
    picker.append(new Option('No effect',''));
    for(const effect of view.effects) picker.append(new Option(`${effect.school}: ${effect.name}`,effect.key));
    const saved=view.draft.effects[slot];picker.value=saved?.key ?? '';section.append(picker);
    const fields=document.createElement('div');fields.className='dagger-maker-settings';section.append(fields);
    const values=new Map<keyof SpellMakerSetting,Stepper>();slots.push({select:picker,values});
    const settings=():void=>{
      fields.replaceChildren();values.clear();const effect=view.effects.find(e=>e.key===picker.value);if(!effect) return;
      const add=(label:string,key:keyof SpellMakerSetting,max:number):void=>{
        const stepper=createStepper(`Effect ${slot+1}: ${label}`);stepper.limit(1,max,saved?.key===effect.key ? Number(saved[key]) : 1);
        const wrap=document.createElement('label');wrap.className='dagger-maker-setting';const text=document.createElement('span');text.textContent=label;
        wrap.append(text,stepper.element);fields.append(wrap);values.set(key,stepper);
      };
      if(effect.duration) {add('Duration base','durationBase',limits.duration);add('Duration increase','durationMod',limits.duration);add('Duration per levels','durationPerLevel',limits.perLevels);}
      if(effect.chance) {add('Chance base','chanceBase',limits.chance);add('Chance increase','chanceMod',limits.chance);add('Chance per levels','chancePerLevel',limits.perLevels);}
      if(effect.magnitude) {add('Magnitude minimum','magnitudeBaseLow',limits.magnitude);add('Magnitude maximum','magnitudeBaseHigh',limits.magnitude);
        add('Magnitude increase minimum','magnitudeLevelBase',limits.magnitude);add('Magnitude increase maximum','magnitudeLevelHigh',limits.magnitude);add('Magnitude per levels','magnitudePerLevel',limits.perLevels);}
    };
    picker.addEventListener('change',settings);settings();
  }
  const preview=document.createElement('button');preview.type='submit';preview.textContent='Update cost preview';form.append(preview);
  form.addEventListener('submit',event=>{
    event.preventDefault();
    const effects=slots.flatMap(slot=>{
      const option=view.effects.find(e=>e.key===slot.select.value);if(!option) return [];
      const values:Record<string,string|number>={key:option.key,type:option.type,subType:option.subType,
        durationBase:1,durationMod:1,durationPerLevel:1,chanceBase:1,chanceMod:1,chancePerLevel:1,
        magnitudeBaseLow:1,magnitudeBaseHigh:1,magnitudeLevelBase:1,magnitudeLevelHigh:1,magnitudePerLevel:1};
      for(const [key,stepper] of slot.values) values[key]=stepper.value();
      return [values];
    });
    claim({action:'spellmaker-draft',revision:view.revision,text:JSON.stringify({name:name.value,element:Number(element.value),
      rangeType:Number(target.value),icon,effects})});
  });
  const status=document.createElement('p');status.setAttribute('role','status');
  status.textContent=view.quote?.eligible ? `${view.quote.gold} gold · ${view.quote.spellPoints} magicka to cast` : view.quote?.reason ?? 'Update the preview with valid settings.';root.append(status);
  const buy=document.createElement('button');buy.type='button';buy.textContent='Buy constructed spell';buy.disabled=!view.quote?.eligible;root.append(buy);
  const stale=():void=>{buy.disabled=true;status.textContent='Update the cost preview after editing.';};
  form.addEventListener('input',stale);form.addEventListener('change',stale);
  buy.addEventListener('click',()=>{const quote=view.quote;if(buy.disabled || !quote?.eligible) return;
    ask({message:`Make this spell for ${quote.gold} gold?`,accept:'Buy',
      onAccept:()=>claim({action:'spellmaker-buy',revision:view.revision,key:quote.key,amount:quote.gold,confirm:true})});});
}


export function isPotionMakerProjection(value:unknown):value is PotionMakerProjection {
  if(!value || typeof value!=='object') return false;
  const v=value as Partial<PotionMakerProjection>;
  return typeof v.revision==='string' && typeof v.provider==='string' && typeof v.eligible==='boolean'
    && Number.isSafeInteger(v.minimumIngredients) && Number.isSafeInteger(v.maximumIngredients)
    && Array.isArray(v.ingredients) && v.ingredients.every(i=>i && Number.isInteger(i.template) && typeof i.name==='string' && Number.isSafeInteger(i.quantity) && i.quantity>0)
    && Array.isArray(v.recipes) && v.recipes.every(r=>r && Number.isInteger(r.key) && typeof r.name==='string' && typeof r.available==='boolean'
      && Array.isArray(r.ingredients) && r.ingredients.every(Number.isInteger));
}

/** The potion maker: a carried recipe, or an experiment with a stepper per ingredient, within the ruleset's ingredient count. */
export function renderPotionMaker(root:HTMLElement,view:PotionMakerProjection|null,claim:(action:UiAction)=>void):void {
  const stamp=JSON.stringify(view); if(root.dataset.potionMaker===stamp) return;
  root.dataset.potionMaker=stamp;root.replaceChildren();if(!view) return;
  const title=document.createElement('h3');title.textContent=`Make a potion with ${view.provider}`;root.append(title);
  if(!view.eligible) { const p=document.createElement('p');p.textContent='Your current guild standing does not permit potion making.';root.append(p);return; }
  const note=document.createElement('p');
  const explain=():string=>`Choose a recipe you carry, or experiment with ${view.minimumIngredients} to ${view.maximumIngredients} ingredients from your pack and wagon. Mixing uses the ingredients even if it fails.`;
  note.textContent=explain();root.append(note);
  const form=document.createElement('form');form.className='dagger-maker-form';root.append(form);
  const recipe=document.createElement('select');recipe.setAttribute('aria-label','Recipe');
  recipe.append(new Option('Experiment',''));
  for(const r of view.recipes) {const option=new Option(r.name+(r.available?'':' — ingredients missing'),String(r.key));option.disabled=!r.available;recipe.append(option);}form.append(recipe);
  const amounts=new Map<number,Stepper>();
  const count=document.createElement('p');count.className='dagger-potion-count';
  const total=():number=>[...amounts.values()].reduce((sum,stepper)=>sum+stepper.value(),0);
  const showCount=():void=>{count.textContent=`${total()} of ${view.maximumIngredients} ingredients chosen.`;};
  for(const i of view.ingredients) {
    const stepper=createStepper(i.name);stepper.limit(0,Math.min(view.maximumIngredients,i.quantity),0);
    const label=document.createElement('label');label.className='dagger-maker-setting';const text=document.createElement('span');text.textContent=`${i.name} (${i.quantity} carried)`;
    label.append(text,stepper.element);form.append(label);amounts.set(i.template,stepper);
    stepper.input.addEventListener('input',()=>{if(recipe.value!=='') recipe.value='';showCount();});
  }
  recipe.addEventListener('change',()=>{const chosen=view.recipes.find(r=>String(r.key)===recipe.value);
    for(const [template,stepper] of amounts) {stepper.input.value='';stepper.limit(0,Math.min(view.maximumIngredients,view.ingredients.find(i=>i.template===template)!.quantity),chosen?.ingredients.filter(i=>i===template).length ?? 0);}
    // The steppers' own updates clear a chosen recipe, so the choice is set again once they are filled.
    if(chosen) recipe.value=String(chosen.key);showCount();});
  form.append(count);showCount();
  const mix=document.createElement('button');mix.type='submit';mix.textContent='Mix potion';mix.disabled=view.ingredients.length===0;form.append(mix);
  form.addEventListener('submit',event=>{event.preventDefault();const selected:number[]=[];for(const [template,stepper] of amounts) for(let i=0;i<stepper.value();i++) selected.push(template);
    if(selected.length<view.minimumIngredients || selected.length>view.maximumIngredients) {note.textContent=`Choose ${view.minimumIngredients} to ${view.maximumIngredients} ingredients.`;return;}
    claim({action:'potion-mix',revision:view.revision,text:JSON.stringify(selected)});
  });
}

export function isItemMakerProjection(value:unknown):value is ItemMakerProjection {
  if(!value || typeof value!=='object') return false;
  const v=value as Partial<ItemMakerProjection>;
  const setting=(s:ItemMakerSetting):boolean=>!!s && typeof s.key==='string' && typeof s.name==='string' && Number.isInteger(s.cost)
    && Array.isArray(s.forced) && s.forced.every(x=>typeof x==='string');
  return typeof v.revision==='string' && typeof v.provider==='string' && typeof v.eligible==='boolean'
    && Array.isArray(v.items) && v.items.every(i=>i && typeof i.key==='string' && typeof i.name==='string' && Number.isInteger(i.capacity) && Number.isSafeInteger(i.quantity) && i.quantity>0)
    && Array.isArray(v.settings) && v.settings.every(setting) && !!v.draft && typeof v.draft.item==='string' && typeof v.draft.name==='string'
    && Array.isArray(v.draft.settings) && v.draft.settings.every(x=>typeof x==='string')
    && (v.quote===null || !!v.quote && typeof v.quote.key==='string' && Number.isInteger(v.quote.capacity) && Number.isInteger(v.quote.power)
      && Number.isSafeInteger(v.quote.gold) && v.quote.gold>=0 && typeof v.quote.eligible==='boolean' && (v.quote.reason===null || typeof v.quote.reason==='string')
      && Array.isArray(v.quote.payloads) && v.quote.payloads.every(setting));
}

export function renderItemMaker(root:HTMLElement,view:ItemMakerProjection|null,claim:(action:UiAction)=>void,ask:(question:ConfirmQuestion)=>void):void {
  const stamp=JSON.stringify(view);if(root.dataset.itemMaker===stamp) return;
  root.dataset.itemMaker=stamp;root.replaceChildren();if(!view)return;
  const title=document.createElement('h3');title.textContent=`Enchant an item with ${view.provider}`;root.append(title);
  if(!view.eligible){const p=document.createElement('p');p.textContent='Your current guild standing does not permit item enchanting.';root.append(p);return;}
  const form=document.createElement('form');root.append(form);
  const items=document.createElement('select');items.setAttribute('aria-label','Item to enchant');
  const empty=document.createElement('option');empty.value='';empty.textContent='Choose an item';items.append(empty);
  for(const i of view.items){const o=document.createElement('option');o.value=i.key;o.textContent=`${i.name} · ${i.capacity} power${i.quantity>1?' · one of '+i.quantity:''}`;items.append(o);}
  items.value=view.draft.item;form.append(items);
  const label=document.createElement('label');label.textContent='Enchanted name ';
  const name=document.createElement('input');name.maxLength=64;name.required=true;name.value=view.draft.name;name.name='enchanted-name';label.append(name);form.append(label);
  const selected=[...view.draft.settings];
  const submit=(settings=selected):void=>claim({action:'itemmaker-draft',revision:view.revision,text:JSON.stringify({item:items.value,name:name.value,settings})});
  items.addEventListener('change',()=>{name.value=view.items.find(i=>i.key===items.value)?.name??'';submit([]);});
  const powers=document.createElement('select');powers.setAttribute('aria-label','Enchantment');
  for(const positive of [true,false]){const group=document.createElement('optgroup');group.label=positive?'Powers':'Side effects';
    for(const setting of view.settings.filter(s=>(s.cost>0)===positive)){const o=document.createElement('option');o.value=setting.key;o.textContent=`${setting.name} (${setting.cost} power)${setting.forced.length?' — also '+setting.forced.join(', '):''}`;group.append(o);}powers.append(group);}
  form.append(powers);const add=document.createElement('button');add.type='button';add.textContent='Add enchantment';add.disabled=!view.draft.item || !view.settings.length;
  add.addEventListener('click',()=>submit([...selected,powers.value]));form.append(add);
  const list=document.createElement('ul');form.append(list);
  selected.forEach((key,index)=>{const row=document.createElement('li');row.textContent=view.settings.find(s=>s.key===key)?.name??key;
    const remove=document.createElement('button');remove.type='button';remove.textContent='Remove';remove.addEventListener('click',()=>submit(selected.filter((_,i)=>i!==index)));row.append(' ',remove);list.append(row);});
  const preview=document.createElement('button');preview.type='submit';preview.textContent='Update preview';form.append(preview);
  form.addEventListener('submit',event=>{event.preventDefault();submit();});
  if(view.quote){const quote=view.quote;const summary=document.createElement('p');summary.textContent=quote.eligible?`${quote.power}/${quote.capacity} power · ${quote.gold} gold`:quote.reason??'Choose valid enchantments.';root.append(summary);
    const payloads=document.createElement('ul');for(const payload of quote.payloads){const row=document.createElement('li');row.textContent=payload.name;payloads.append(row);}root.append(payloads);
    const buy=document.createElement('button');buy.type='button';buy.textContent='Enchant item';buy.disabled=!quote.eligible;
    form.addEventListener('input',()=>{buy.disabled=true;});form.addEventListener('change',()=>{buy.disabled=true;});
    buy.addEventListener('click',()=>ask({message:`Enchant ${view.draft.name} for ${quote.gold} gold?`,accept:'Enchant',
      onAccept:()=>claim({action:'itemmaker-buy',revision:view.revision,key:quote.key,amount:quote.gold,confirm:true})}));root.append(buy);
  }
}

export function isSummoningProjection(value:unknown):value is SummoningProjection {
  if(!value || typeof value!=='object')return false;
  const v=value as Partial<SummoningProjection>;
  return typeof v.revision==='string' && typeof v.provider==='string' && (v.offerRevision===null || typeof v.offerRevision==='string')
    && (v.prince===null || typeof v.prince==='string') && (v.message===null || typeof v.message==='string')
    && Array.isArray(v.diagnostics) && v.diagnostics.every(x=>typeof x==='string')
    && (v.quote===null || !!v.quote && typeof v.quote.key==='string' && typeof v.quote.name==='string' && typeof v.quote.quest==='string'
      && Number.isSafeInteger(v.quote.gold) && v.quote.gold>=0 && typeof v.quote.eligible==='boolean' && (v.quote.reason===null || typeof v.quote.reason==='string'));
}
/** Summoning: a quoted summoning is paid for through the drawn confirm, and an offer is answered in its own dialog. */
export function renderSummoning(root:HTMLElement,view:SummoningProjection|null,claim:(action:UiAction)=>void,ask:(question:ConfirmQuestion)=>void):void {
  root.replaceChildren();if(!view)return;
  const title=document.createElement('h3');title.textContent=view.offerRevision ? view.prince : 'Daedric summoning';root.append(title);
  const p=document.createElement('p');root.append(p);
  if(view.offerRevision){
    p.textContent=view.message;
    for(const [label,accept] of [['Accept the quest',true],['Refuse the prince',false]] as const){
      const button=document.createElement('button');button.type='button';button.textContent=label;
      button.addEventListener('click',()=>claim({action:'daedra-answer',revision:view.offerRevision!,confirm:accept}));root.append(button);
    }
  } else if(view.quote){
    const quote=view.quote;
    p.textContent=quote.eligible ? `Summon ${quote.name} for ${quote.gold} gold. The payment is lost if the summoning fails.` : quote.reason;
    const button=document.createElement('button');button.type='button';button.textContent='Pay and summon';button.disabled=!quote.eligible;
    button.addEventListener('click',()=>ask({message:`Pay ${quote.gold} gold to summon ${quote.name}? The payment is lost if the summoning fails.`,accept:'Pay and summon',
      onAccept:()=>claim({action:'daedra-summon',revision:view.revision,key:quote.key,amount:quote.gold,confirm:true})}));root.append(button);
  }
}
