using WorldRpg.Rulesets.Daggerfall.Content;
using System.Text.Json.Serialization;
using WorldRpg.Rulesets.Daggerfall.Policies;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed record DaggerfallIdentifyRequest([property: JsonRequired] string Instance,
    [property: JsonRequired] int Chance, [property: JsonRequired] int Cost, [property: JsonRequired] ulong? SourceItem = null, [property: JsonRequired] long Attempts = 0)
{
    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Instance);
        ArgumentOutOfRangeException.ThrowIfNegative(Chance);
        ArgumentOutOfRangeException.ThrowIfNegative(Cost);
        ArgumentOutOfRangeException.ThrowIfNegative(Attempts);
        if (SourceItem == 0) throw new ArgumentException("Identify source item must have a durable identity.");
    }
}
internal sealed record DaggerfallIdentifyOption(string Id, string Label);
internal sealed record DaggerfallIdentifyView(string Revision, int Cost, IReadOnlyList<DaggerfallIdentifyOption> Options);

/// <summary>Immediate disposition and selection requests use the existing senses and item owners.</summary>
internal static class DaggerfallSocialMagicEffects
{
    /// <summary>
    /// Calm Humanoid (no group) pacifies a live enemy class; Calm Creature pacifies a live monster of its group. Anyone
    /// else is no match.
    /// </summary>
    /// <param name="liveDefinition">A live, undefeated actor's definition; null for anyone else.</param>
    internal static void Pacify(DaggerfallActiveEffect effect, int? group, Func<long, DaggerfallActorDefinition?> liveDefinition, Action<long> pacify)
    {
        long target=checked((long)effect.Context.Target.Value);
        if (liveDefinition(target) is not { } definition || (group is null ? definition.Kind != DaggerfallActorKinds.EnemyClass
                : definition.Kind != DaggerfallActorKinds.Monster || DaggerfallFormulaPolicy.EnemyGroupFor(definition) != group switch
                {0=>DaggerfallEnemyGroup.Animals,1=>DaggerfallEnemyGroup.Undead,2=>DaggerfallEnemyGroup.Humanoid,3=>DaggerfallEnemyGroup.Daedra,_=>DaggerfallEnemyGroup.None}))
        {effect.InitialOutcome=DaggerfallEffectAdmissionOutcome.NoMatch;return;}
        pacify(target);
    }

    internal static IEnumerable<DaggerfallEffectDefinition> Definitions(Action<DaggerfallActiveEffect,int?> pacify,
        Action<DaggerfallActiveEffect,DaggerfallCastEffectState> identify)
    {
        yield return Definition("charm",34,-1,null);
        for (int group=0;group<4;group++) yield return Definition($"pacify-{group}",33,group,group);
        yield return new("identify","identify",DaggerfallEffectStacking.Stack,ushort.MaxValue,1,
            Apply: effect=>{identify(effect,DaggerfallMysticismEffects.Read(effect,40,-1));return [];},
            MagicRound: effect=>effect.ExpireAfterCurrentRound=true,
            Resume: _=>throw new ArgumentException("Identify is an immediate selection request."),
            Spell:new(40,-1, SpellMaker: true,AllowedTargets:DaggerfallMagicAllowedTargets.CasterOnly),ShowSpellIcon:false);

        DaggerfallEffectDefinition Definition(string key,int type,int subtype,int? group)=>new(key,key,
            DaggerfallEffectStacking.Stack,ushort.MaxValue,1,
            Apply: effect=>{DaggerfallMysticismEffects.Read(effect,type,subtype);pacify(effect,group);return [];},
            MagicRound: effect=>effect.ExpireAfterCurrentRound=true,
            Resume: _=>throw new ArgumentException("Disposition changes persist in canonical senses, not an active spell."),
            Spell:new(type,subtype, SpellMaker: true,RollChanceOnCast:true,AllowedTargets:DaggerfallMagicAllowedTargets.Other,
                AllowedElements:group is null ? (DaggerfallMagicAllowedElements.Fire | DaggerfallMagicAllowedElements.Cold | DaggerfallMagicAllowedElements.Poison | DaggerfallMagicAllowedElements.Shock | DaggerfallMagicAllowedElements.Magic) : DaggerfallMagicAllowedElements.Magic),
            ShowSpellIcon:false);
    }
}
