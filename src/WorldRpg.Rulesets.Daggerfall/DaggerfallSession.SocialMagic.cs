using System.Globalization;
using Rusty.Engine.Mechanics;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Policies;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed partial class DaggerfallSession
{
    private DaggerfallIdentifyRequest? _pendingIdentify;
    private DaggerfallIdentifyRequest? CurrentIdentifyRequest
    {
        get
        {
            if (_pendingIdentify?.SourceItem is ulong source && !IdentifySourceAvailable(source))
                _pendingIdentify = null;
            return _pendingIdentify;
        }
    }

    private bool IdentifySourceAvailable(ulong source) => State.ItemInstances.ContainsUnique(source)
        && State.ItemInstances.RequireUnique(source) is { Owner: var owner } metadata
        && owner == DaggerfallItemOwner.Player
        && (metadata.MaximumCondition == 0 || metadata.CurrentCondition > 0)
        && State.Inventory.Read().UniqueItems.Any(item => State.Inventory.GetDurableItemId(item.Entity).Value == source);

    internal DaggerfallIdentifyView? IdentifyView => CurrentIdentifyRequest is { } request
        ? new(request.Instance,request.Cost,IdentifyCandidates().Select(item=>new DaggerfallIdentifyOption(
            State.Inventory.GetDurableItemId(item.Entity).Value.ToString(CultureInfo.InvariantCulture),
            _inventoryUi.DescribeItem($"unique:{item.Entity.Value}",item.Definition.Value,1,owner:DaggerfallItemOwner.Player).Label)).ToArray()) : null;

    private UniqueInventoryItem[] IdentifyCandidates()=>State.Inventory.Read().UniqueItems
        .Where(item=>State.ItemInstances.RequireUnique(State.Inventory.GetDurableItemId(item.Entity).Value)
            is { Enchantment:not null,Identified:false }).ToArray();

    private void ApplyPacify(DaggerfallActiveEffect effect,int? group)
    {
        long target=checked((long)effect.Context.Target.Value);
        if (!_roster.Definitions.TryGetValue(target,out var definition) || !State.Actors.TryGet(target,out var actor)
            || actor.IsDefeated || (group is null ? definition.Kind != DaggerfallActorKinds.EnemyClass
                : definition.Kind != DaggerfallActorKinds.Monster || DaggerfallFormulaPolicy.EnemyGroupFor(definition) != group switch
                {0=>DaggerfallEnemyGroup.Animals,1=>DaggerfallEnemyGroup.Undead,2=>DaggerfallEnemyGroup.Humanoid,3=>DaggerfallEnemyGroup.Daedra,_=>DaggerfallEnemyGroup.None}))
        {effect.InitialOutcome=DaggerfallEffectAdmissionOutcome.NoMatch;return;}
        _enemyBehavior.Pacify(target);
    }

    private void RequestIdentify(DaggerfallActiveEffect effect,DaggerfallCastEffectState state)
    {
        if (effect.Context.Target.Value!=DaggerfallActorIdentity.PlayerEntityId
            || (effect.Context.Item is { } source && !IdentifySourceAvailable(source.Value)))
        {effect.InitialOutcome=DaggerfallEffectAdmissionOutcome.NoMatch;return;}
        int cost=Math.Max(5,DaggerfallMagicCostPolicy.CalculateEffectCosts(_definitions.Magic,state.Settings,
            new[] {"destruction","restoration","illusion","alteration","thaumaturgy","mysticism"}
                .ToDictionary(key=>key,key=>DaggerfallCasting.Read(State.Actors.Player.Stats,key))).SpellPoints);
        _vitality.RestoreSpellTrack(State.Actors.Player.Actor,TrackId.Parse("magicka"),cost);
        _pendingIdentify=new(effect.Context.Instance.Value,DaggerfallMagicAdmissionPolicy.CalculateEffectChance(state.Settings,state.CasterLevel),cost,effect.Context.Item?.Value);
    }

    internal void ChooseIdentify(string revision,string? key)
    {
        if (CurrentIdentifyRequest is not { } request || request.Instance!=revision)
        {Presentation.SetOutcome("Identify choice is no longer current.");return;}
        if (key is null){_pendingIdentify=null;Presentation.SetOutcome("Identify cancelled.");return;}
        var candidates=IdentifyCandidates();
        var selected=key=="all" ? candidates : candidates.Where(item=>State.Inventory.GetDurableItemId(item.Entity).Value.ToString(CultureInfo.InvariantCulture)==key).ToArray();
        if(selected.Length==0){Presentation.SetOutcome("That unidentified item is no longer available.");return;}
        var magicka=State.Actors.Player.Stats.GetTrack(TrackId.Parse("magicka"));
        if(magicka.Current<request.Cost){Presentation.SetOutcome("Not enough magicka to identify.");return;}
        magicka.SetCurrent(magicka.Current-request.Cost,clamp:true);
        _pendingIdentify=request with {Attempts=checked(request.Attempts+1)};
        int successes=0;
        foreach(var item in selected)
        {
            ulong id=State.Inventory.GetDurableItemId(item.Entity).Value;
            bool success=_random.DrawKeyed(new(0,"daggerfall.identify.v1",$"{request.Instance}:attempt:{request.Attempts}:item:{id}",1,100)).Value<=request.Chance;
            if(success){_itemCondition.Identify(new WorldRpg.Kit.Inventory.UniqueInventoryItem(item.Entity.Value,new WorldRpg.Kit.Inventory.InventoryItemId(item.Definition.Value)));successes++;}
            _facts.Append(new MagicItemIdentifiedFact(id,success));
        }
        Presentation.SetOutcome($"Identified {successes} of {selected.Length} items.");
    }
}
