using System.Text.Json;
using WorldRpg.Kit.Effects;
using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.Facts;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Normal/true power is derived from the existing effect graph, never a second stealth state.</summary>
internal static class DaggerfallConcealmentEffects
{
    private const string PresentationOwner = "magic.concealment";
    private const DaggerfallConcealment Normal = DaggerfallConcealment.InvisibleNormal | DaggerfallConcealment.BlendingNormal | DaggerfallConcealment.ShadeNormal;
    internal static string Key(int type, int subtype) => $"{(type == 13 ? "invisibility" : type == 23 ? "chameleon" : type == 24 ? "shadow" : throw new ArgumentOutOfRangeException(nameof(type)))}-{(subtype == 0 ? "normal" : subtype == 1 ? "true" : throw new ArgumentOutOfRangeException(nameof(subtype)))}";
    internal static IEnumerable<DaggerfallEffectDefinition> Definitions()
    {
        foreach (int type in new[] { 13, 23, 24 })
        foreach (int subtype in new[] { 0, 1 })
        {
            int selectedType = type, selectedSubtype = subtype;
            var flag = (type, subtype) switch
            {
                (13, 0) => DaggerfallConcealment.InvisibleNormal, (13, 1) => DaggerfallConcealment.InvisibleTrue,
                (23, 0) => DaggerfallConcealment.BlendingNormal, (23, 1) => DaggerfallConcealment.BlendingTrue,
                (24, 0) => DaggerfallConcealment.ShadeNormal, _ => DaggerfallConcealment.ShadeTrue,
            };
            string key = Key(type, subtype);
            yield return new(key, key, DaggerfallEffectStacking.Stack, ushort.MaxValue, 1,
                Apply: effect => Validate(effect, selectedType, selectedSubtype), Resume: effect => Validate(effect, selectedType, selectedSubtype),
                Perception: new(Invisible: type == 13, Blending: type == 23, Shade: type == 24, Concealment: flag),
                Spell: new(type, subtype, SpellMaker: true, SupportsDuration: true), ExtendIncumbentDuration: true,
                IncumbentSettingsMatch: (_, _) => true);
        }
    }
    internal static int BreakNormal(DaggerfallEffectLifecycle effects, long actorId)
    {
        var instances = effects.Active.Where(effect => effect.Context.Target.Value == checked((ulong)actorId)
            && (effect.Definition.Perception.Concealment & Normal) != 0).Select(effect => effect.Context.Instance).ToArray();
        foreach (var instance in instances) effects.Cancel(instance);
        return instances.Length;
    }
    internal static void AfterPhysicalHit(DaggerfallEffectLifecycle effects, AttackHitFact hit)
    {
        // Classic physical attacks reveal their source when calculated contact damage is positive,
        // even when a shield subsequently absorbs the bounded health loss.
        if (hit.CalculatedDamage > 0) BreakNormal(effects, hit.AttackerId);
    }
    internal static int End(DaggerfallEffectLifecycle effects, long actorId)
    {
        var instances = effects.Active.Where(effect => effect.Context.Target.Value == checked((ulong)actorId)
            && effect.Definition.Perception.Concealment != DaggerfallConcealment.None).Select(effect => effect.Context.Instance).ToArray();
        foreach (var instance in instances) effects.Cancel(instance);
        return instances.Length;
    }
    internal static void Publish(DaggerfallEffectLifecycle effects, long target, PresentationSlots slots)
    {
        slots.RetireOwner(PresentationOwner);
        foreach (var effect in effects.Active.Where(effect => effect.Context.Target.Value == checked((ulong)target)
            && effect.Definition.Perception.Concealment != DaggerfallConcealment.None))
        {
            var state = effect.Definition.Perception;
            string label = state.Invisible ? "Invisibility" : state.Blending ? "Chameleon" : "Shadow";
            bool normal = (state.Concealment & Normal) != 0;
            slots.Publish(new(PresentationOwner, effect.Context.Instance.Value, normal ? label : $"True {label.ToLowerInvariant()}",
                normal ? "Ends when an attack hits." : "Persists through attacks.", 30));
        }
    }
    private static IEnumerable<IActiveEffectContribution> Validate(DaggerfallActiveEffect effect, int type, int subtype)
    {
        var state = effect.State.Deserialize(DaggerfallSaveJsonContext.Default.DaggerfallCastEffectState);
        if (state?.Settings is not { } setting || setting.Type != type || setting.SubType != subtype || state.CasterLevel < 1
            || state.Amount != 0 || state.SavePercent is < 1 or > 100)
            throw new ArgumentException("Concealment state does not match its admitted duration-only variant.");
        return [];
    }
}
