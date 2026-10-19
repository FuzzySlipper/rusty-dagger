import type { ConfirmQuestion } from './confirm.js';
import type {
  ItemMakerProjection, ItemMakerSetting, PotionMakerProjection, SpellMakerOption, SpellMakerProjection, SpellMakerSetting,
  SummoningProjection, UiAction,
} from './projection.js';

export function isSpellMakerProjection(value:unknown):value is SpellMakerProjection {
  if(!value || typeof value!=='object') return false;
  const v=value as Partial<SpellMakerProjection>;const draft=v.draft;
  const options=(list:unknown):boolean=>Array.isArray(list) && list.every(o=>o && typeof o==='object' && Number.isInteger(o.value) && typeof o.label==='string');
  return typeof v.revision==='string' && typeof v.provider==='string' && Array.isArray(v.effects) && options(v.targets) && options(v.elements)
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

export function renderSpellMaker(root:HTMLElement,view:SpellMakerProjection|null,claim:(action:UiAction)=>void,ask:(question:ConfirmQuestion)=>void):void {
  const stamp=JSON.stringify(view);
  // Admitted modal updates can publish the same projection while the player types.
  if(root.dataset.spellMaker===stamp) return;
  root.dataset.spellMaker=stamp;root.replaceChildren();if(!view) return;
  const title=document.createElement('h3');title.textContent=`Make a spell with ${view.provider}`;root.append(title);
  const form=document.createElement('form');root.append(form);
  const input=(parent:HTMLElement,label:string,name:string,value:string|number,max?:number):HTMLInputElement=>{
    const wrap=document.createElement('label');wrap.textContent=`${label} `;
    const field=document.createElement('input');field.name=name;field.value=String(value);
    if(max!==undefined) {field.type='number';field.min='1';field.max=String(max);field.step='1';} else field.maxLength=31;
    wrap.append(field);parent.append(wrap);return field;
  };
  const name=input(form,'Name','name',view.draft.name);name.required=true;
  const select=(label:string,name:string,values:readonly SpellMakerOption[],selected:number):HTMLSelectElement=>{
    const wrap=document.createElement('label');wrap.textContent=`${label} `;const field=document.createElement('select');field.name=name;
    values.forEach(value=>{const option=document.createElement('option');option.value=String(value.value);option.textContent=value.label;field.append(option);});
    field.value=String(selected);wrap.append(field);form.append(wrap);return field;
  };
  const target=select('Target','target',view.targets,view.draft.rangeType);
  const element=select('Element','element',view.elements,view.draft.element);
  const icon=select('Icon','icon',Array.from({length:69},(_,i)=>({value:i,label:String(i+1)})),view.draft.icon);
  const slots:{select:HTMLSelectElement;fields:HTMLElement}[]=[];
  for(let slot=0;slot<3;slot++) {
    const section=document.createElement('fieldset');const legend=document.createElement('legend');legend.textContent=`Effect ${slot+1}`;section.append(legend);form.append(section);
    const picker=document.createElement('select');picker.setAttribute('aria-label',`Effect ${slot+1}`);
    const empty=document.createElement('option');empty.value='';empty.textContent='No effect';picker.append(empty);
    for(const effect of view.effects) {const option=document.createElement('option');option.value=effect.key;option.textContent=`${effect.school}: ${effect.name}`;picker.append(option);}
    const saved=view.draft.effects[slot];picker.value=saved?.key ?? '';section.append(picker);
    const fields=document.createElement('div');section.append(fields);slots.push({select:picker,fields});
    const settings=():void=>{
      fields.replaceChildren();const effect=view.effects.find(e=>e.key===picker.value);if(!effect) return;
      const add=(label:string,key:keyof SpellMakerSetting,max:number):void=>{input(fields,label,key,saved?.key===effect.key ? Number(saved[key]) : 1,max);};
      if(effect.duration) {add('Duration base','durationBase',60);add('Duration increase','durationMod',60);add('Duration per levels','durationPerLevel',20);}
      if(effect.chance) {add('Chance base','chanceBase',100);add('Chance increase','chanceMod',100);add('Chance per levels','chancePerLevel',20);}
      if(effect.magnitude) {add('Magnitude minimum','magnitudeBaseLow',100);add('Magnitude maximum','magnitudeBaseHigh',100);
        add('Magnitude increase minimum','magnitudeLevelBase',100);add('Magnitude increase maximum','magnitudeLevelHigh',100);add('Magnitude per levels','magnitudePerLevel',20);}
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
      for(const field of slot.fields.querySelectorAll<HTMLInputElement>('input')) values[field.name]=Number(field.value);
      return [values];
    });
    claim({action:'spellmaker-draft',revision:view.revision,text:JSON.stringify({name:name.value,element:Number(element.value),
      rangeType:Number(target.value),icon:Number(icon.value),effects})});
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
    && Array.isArray(v.ingredients) && v.ingredients.every(i=>i && Number.isInteger(i.template) && typeof i.name==='string' && Number.isSafeInteger(i.quantity) && i.quantity>0)
    && Array.isArray(v.recipes) && v.recipes.every(r=>r && Number.isInteger(r.key) && typeof r.name==='string' && typeof r.available==='boolean'
      && Array.isArray(r.ingredients) && r.ingredients.every(Number.isInteger));
}

export function renderPotionMaker(root:HTMLElement,view:PotionMakerProjection|null,claim:(action:UiAction)=>void):void {
  const stamp=JSON.stringify(view); if(root.dataset.potionMaker===stamp) return;
  root.dataset.potionMaker=stamp;root.replaceChildren();if(!view) return;
  const title=document.createElement('h3');title.textContent=`Make a potion with ${view.provider}`;root.append(title);
  if(!view.eligible) { const p=document.createElement('p');p.textContent='Your current guild standing does not permit potion making.';root.append(p);return; }
  const note=document.createElement('p');note.textContent='Choose a recipe you carry, or experiment with up to eight ingredients from your pack and wagon. Mixing uses the ingredients even if it fails.';root.append(note);
  const form=document.createElement('form');root.append(form);
  const recipe=document.createElement('select');recipe.setAttribute('aria-label','Recipe');
  const experiment=document.createElement('option');experiment.value='';experiment.textContent='Experiment';recipe.append(experiment);
  for(const r of view.recipes) {const option=document.createElement('option');option.value=String(r.key);option.textContent=r.name+(r.available?'':' — ingredients missing');option.disabled=!r.available;recipe.append(option);}form.append(recipe);
  const inputs=new Map<number,HTMLInputElement>();
  for(const i of view.ingredients) {
    const label=document.createElement('label');label.textContent=`${i.name} (${i.quantity} available) `;
    const input=document.createElement('input');input.type='number';input.min='0';input.max=String(Math.min(8,i.quantity));input.step='1';input.value='0';
    input.setAttribute('aria-label',i.name);input.addEventListener('input',()=>{recipe.value='';});label.append(input);form.append(label);inputs.set(i.template,input);
  }
  recipe.addEventListener('change',()=>{const chosen=view.recipes.find(r=>String(r.key)===recipe.value);for(const [template,input] of inputs) input.value=String(chosen?.ingredients.filter(i=>i===template).length ?? 0);});
  const mix=document.createElement('button');mix.type='submit';mix.textContent='Mix potion';mix.disabled=view.ingredients.length===0;form.append(mix);
  form.addEventListener('submit',event=>{event.preventDefault();const selected:number[]=[];for(const [template,input] of inputs) for(let i=0;i<Number(input.value);i++) selected.push(template);
    if(selected.length<1 || selected.length>8) {note.textContent='Choose one to eight ingredients.';return;}
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
export function renderSummoning(root:HTMLElement,view:SummoningProjection|null,claim:(action:UiAction)=>void):void {
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
    p.textContent=view.quote.eligible ? `Summon ${view.quote.name} for ${view.quote.gold} gold. Payment is lost if the summoning fails.`
      : view.quote.reason;
    const button=document.createElement('button');button.type='button';button.textContent='Pay and summon';button.disabled=!view.quote.eligible;
    button.addEventListener('click',()=>claim({action:'daedra-summon',revision:view.revision,key:view.quote!.key,amount:view.quote!.gold,confirm:true}));root.append(button);
  }
}

