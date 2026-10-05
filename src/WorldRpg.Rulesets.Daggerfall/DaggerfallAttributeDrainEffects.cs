using System.Text.Json;
using Rusty.Engine.Mechanics;
using Rusty.Engine.Entities;
using WorldRpg.Kit.Effects;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed record DaggerfallAttributeDrainState(DaggerfallCastEffectState Cast, int Magnitude);

/// <summary>One target-owned incumbent per attribute; damage changes live sources, never permanent bases.</summary>
internal static class DaggerfallAttributeDrainEffects
{
    private const string SourceDefinition = "daggerfall.attribute-drain";
    internal static readonly DaggerfallStatId[] Attributes = DaggerfallMechanicsIds.Attributes;

    internal static string Key(int subtype) => $"drain-{Attributes[subtype].Value}";
    internal static JsonElement Encode(DaggerfallAttributeDrainState state) =>
        JsonSerializer.SerializeToElement(state, DaggerfallSaveJsonContext.Default.DaggerfallAttributeDrainState);

    /// <summary>The accepted artifact strike already supplied its save; retain only historical provenance.</summary>
    internal static void DrainArtifactStrength(DaggerfallEffectLifecycle effects, string instance,
        long caster, long target, ulong item, int amount)
    {
        if (amount is < 1 or > 6) throw new ArgumentOutOfRangeException(nameof(amount));
        DaggerfallSpellEffectDefinition settings = new("artifact-strength-drain", 7, 0,
            0, 0, 1, 0, 0, 1, amount, amount, 0, 0, 1);
        effects.Start(new(instance, Key(0), "artifact-molag-bal-drain", null, target, settings.Key, "Magic", null, 1, null,
            Encode(new(new(settings, 1, amount, 100, new(caster, item, DaggerfallCastSource.ItemStrike)), amount))));
    }

    internal static IEnumerable<DaggerfallEffectDefinition> Definitions(Func<DaggerfallCareerDefinition> career, Action<long, long, string> attacked) =>
        DefinitionsFor(7, career, attacked, null);

    internal static IEnumerable<DaggerfallEffectDefinition> TransferDefinitions(Func<DaggerfallCareerDefinition> career,
        Action<long, long, string> attacked, Func<long, Actor?> actor, Func<DaggerfallEffectLifecycle> effects) =>
        DefinitionsFor(11, career, attacked, incoming =>
        {
            if (incoming.Cast.Origin?.CasterId is not long casterId) return;
            Actor? caster = actor(casterId);
            if (caster is null || caster.Get<StatsComponent>().GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Health.Value)).Current <= 0) return;
            Heal(effects(), casterId, Attributes[incoming.Cast.Settings.SubType], incoming.Cast.Amount, career);
        });

    private static IEnumerable<DaggerfallEffectDefinition> DefinitionsFor(int type, Func<DaggerfallCareerDefinition> career,
        Action<long, long, string> attacked, Action<DaggerfallAttributeDrainState>? healCaster)
    {
        for (int subtype = 0; subtype < Attributes.Length; subtype++)
        {
            int selected = subtype;
            string key = type == 7 ? Key(selected) : $"transfer-{Attributes[selected].Value}";
            yield return new(key, key, DaggerfallEffectStacking.RefreshDuration, 1, 1,
                Apply: effect =>
                {
                    var incoming = Read(effect.State, selected, type);
                    Update(effect, incoming with { Magnitude = Bounded(effect, selected, incoming.Magnitude) }, selected, career);
                    if (incoming.Cast.Origin?.CasterId is long casterId)
                        attacked(casterId, checked((long)effect.Context.Target.Value), effect.BundleId ?? effect.Context.Instance.Value);
                    healCaster?.Invoke(incoming);
                    return Cleanup(effect, selected, career);
                },
                Resume: effect =>
                {
                    var state = Read(effect.State, selected, type);
                    VerifySources(effect, selected, state.Magnitude);
                    return Cleanup(effect, selected, career);
                },
                RefreshState: (effect, payload) =>
                {
                    var incoming = Read(payload, selected, type);
                    var incumbent = Read(effect.State, selected, effect.Definition.Spell!.Type);
                    Update(effect, incumbent with { Magnitude = Bounded(effect, selected, (long)incumbent.Magnitude + incoming.Magnitude) }, selected, career);
                    if (incoming.Cast.Origin?.CasterId is long casterId)
                        attacked(casterId, checked((long)effect.Context.Target.Value), effect.BundleId ?? effect.Context.Instance.Value);
                    healCaster?.Invoke(incoming);
                },
                Spell: new(type, selected, SpellMaker: true, SupportsMagnitude: true, UntilHealed: true,
                    AllowedElements: type == 11 ? DaggerfallMagicAllowedElements.Magic : DaggerfallMagicAllowedElements.Fire | DaggerfallMagicAllowedElements.Cold
                        | DaggerfallMagicAllowedElements.Poison | DaggerfallMagicAllowedElements.Shock | DaggerfallMagicAllowedElements.Magic,
                    AllowedTargets: DaggerfallMagicAllowedTargets.Other,
                    CreateState: cast => Encode(new(cast, cast.Amount))), ShowSpellIcon: false,
                IncumbentDefinitionMatch: incoming => incoming.Spell is { } binding && binding.SubType == selected
                    && (type == 7 ? binding.Type is 7 or 11 : binding.Type == 11));
        }
    }

    /// <summary>Matching healing consumes only this drain, leaving disease/poison and other attributes untouched.</summary>
    internal static int Heal(DaggerfallEffectLifecycle effects, long targetId, DaggerfallStatId attribute, int amount,
        Func<DaggerfallCareerDefinition> career)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        int subtype = Array.IndexOf(Attributes, attribute);
        if (subtype < 0) throw new ArgumentException("Drain healing requires an attribute.", nameof(attribute));
        int remaining = amount;
        // Match the admitted bundle order: consume each matching source without healing another attribute.
        foreach (var effect in effects.Active.Where(effect => effect.Context.Target.Value == checked((ulong)targetId)
            && IsAttributeDamage(effect.Definition, subtype)).OrderBy(effect => effect.BundleSequence)
            .ThenBy(effect => effect.Context.Instance.Value, StringComparer.Ordinal).ToArray())
        {
            if (remaining == 0) break;
            var state = Read(effect.State, subtype, effect.Definition.Spell!.Type);
            int healed = Math.Min(remaining, state.Magnitude);
            if (state.Magnitude == healed) effects.Cure(effect.Context.Instance);
            else Update(effect, state with { Magnitude = state.Magnitude - healed }, subtype, career);
            remaining -= healed;
        }
        return amount - remaining;
    }

    internal static bool IsAttributeDamage(DaggerfallEffectDefinition definition, int subtype) =>
        definition.Spell is { Type: 7 or 11 } binding && binding.SubType == subtype;

    internal static DaggerfallAttributeDrainState Read(JsonElement payload, int subtype, int type = 7)
    {
        var state = payload.Deserialize(DaggerfallSaveJsonContext.Default.DaggerfallAttributeDrainState);
        if (state?.Cast is not { } cast || cast.Settings is not { } setting || setting.Type != type || type is not (7 or 11) || setting.SubType != subtype
            || cast.CasterLevel < 1 || cast.Amount < 0 || cast.SavePercent is < 1 or > 100 || state.Magnitude < 0
            || cast.Origin is not { } origin || !Enum.IsDefined(origin.Source)
            || origin.CasterId is <= 0 || origin.ItemId == 0
            || origin.Source != DaggerfallCastSource.DungeonAction && origin.CasterId is null
            || origin.Source == DaggerfallCastSource.DungeonAction
                && (origin.CasterId is not null || origin.ItemId is not null || origin.ActionSource is not { IsValid: true })
            || origin.Source == DaggerfallCastSource.Spell && origin.ItemId is not null
            || origin.Source != DaggerfallCastSource.Spell && origin.Source != DaggerfallCastSource.DungeonAction && origin.ItemId is null)
            throw new ArgumentException("Attribute drain state does not match its admitted variant and origin.");
        return state;
    }

    private static int Bounded(DaggerfallActiveEffect effect, int subtype, long magnitude) =>
        checked((int)Math.Min(magnitude, Math.Max(0, StatFor(effect, subtype).BaseValue - 1)));
    private static Stat StatFor(DaggerfallActiveEffect effect, int subtype) => effect.Target.Get<StatsComponent>().GetStat(StatId.Parse(Attributes[subtype].Value));
    private static EffectSourceIdentity Identity(DaggerfallActiveEffect effect) => new(effect.Target.Entity,
        effect.Context.Instance, 1, SourceDefinitionId.Parse(SourceDefinition));

    private static void Update(DaggerfallActiveEffect effect, DaggerfallAttributeDrainState state, int subtype, Func<DaggerfallCareerDefinition> career)
    {
        Stat stat = StatFor(effect, subtype); StatId id = StatId.Parse(Attributes[subtype].Value);
        var identity = Identity(effect);
        List<StatSource> sources = [.. stat.Sources.Where(source => source.Identity != identity)];
        if (state.Magnitude > 0) sources.Add(new(identity, SourceDefinitionId.Parse(SourceDefinition), 0,
            [new(id, StackingGroupId.Parse($"daggerfall.drain.{id.Value}"), MechanicsStackingPolicy.Sum, new StatContribution.Add(-state.Magnitude))]));
        stat.SetSources(id, sources);
        effect.State = Encode(state);
        Refresh(effect, career);
    }

    private static IEnumerable<IActiveEffectContribution> Cleanup(DaggerfallActiveEffect effect, int subtype, Func<DaggerfallCareerDefinition> career) =>
        [new DelegateActiveEffectContribution(() => { StatFor(effect, subtype).RemoveSource(Identity(effect)); Refresh(effect, career); })];
    private static void Refresh(DaggerfallActiveEffect effect, Func<DaggerfallCareerDefinition> career)
    {
        if (effect.Context.Target.Value == checked((ulong)DaggerfallActorIdentity.PlayerEntityId))
            DaggerfallStatModifiers.RefreshPlayerDerivedMaxima(effect.Target.Get<StatsComponent>(), career());
    }

    private static void VerifySources(DaggerfallActiveEffect effect, int subtype, int magnitude)
    {
        for (int index = 0; index < Attributes.Length; index++)
        {
            var matches = StatFor(effect, index).Sources.Where(source => source.Identity == Identity(effect)).ToArray();
            if (index != subtype || magnitude == 0)
            { if (matches.Length == 0) continue; }
            else if (matches.Length == 1 && matches[0].Definition == SourceDefinitionId.Parse(SourceDefinition)
                && matches[0].Contributions.Count == 1 && matches[0].Contributions[0].Stat == StatId.Parse(Attributes[subtype].Value)
                && matches[0].Contributions[0].Contribution is StatContribution.Add add && add.Amount == -magnitude) continue;
            throw new ArgumentException("Saved attribute drain sources disagree with their durable magnitude/attribute.");
        }
    }
}
