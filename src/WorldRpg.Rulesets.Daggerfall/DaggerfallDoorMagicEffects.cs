using System.Text.Json;
using WorldRpg.Kit.Effects;
using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.Policies;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Ready door operations remain in the one effect lifecycle until a door consumes them.</summary>
internal enum DaggerfallDoorMagic { None, Lock, Open }

internal static class DaggerfallDoorMagicEffects
{
    private const string PresentationOwner = "magic.door-ready";
    internal static void Publish(DaggerfallEffectLifecycle effects, long target, PresentationSlots slots)
    {
        slots.RetireOwner(PresentationOwner);
        foreach (var effect in effects.Active.Where(effect => effect.Context.Target.Value == checked((ulong)target)
            && effect.Definition.DoorMagic != DaggerfallDoorMagic.None))
            slots.Publish(new(PresentationOwner, effect.Context.Instance.Value,
                effect.Definition.DoorMagic == DaggerfallDoorMagic.Lock ? "Ready to lock" : "Ready to open",
                "Activate a door to use the spell.", 30));
    }

    internal static IEnumerable<DaggerfallEffectDefinition> Definitions()
    {
        foreach (var (kind, type) in new[] { (DaggerfallDoorMagic.Lock, 16), (DaggerfallDoorMagic.Open, 17) })
        {
            int variant = type;
            string key = kind == DaggerfallDoorMagic.Lock ? "lock" : "open";
            yield return new(key, key, DaggerfallEffectStacking.Reject, 1, 1,
                Apply: effect => Validate(effect, variant), Resume: effect => Validate(effect, variant),
                Spell: new(type, -1, RollChanceOnCast: true, AllowedTargets: DaggerfallMagicAllowedTargets.CasterOnly,
                    UntilTriggered: true, BypassItemChance: kind == DaggerfallDoorMagic.Open),
                ShowSpellIcon: false, DoorMagic: kind);
        }
    }

    private static IEnumerable<IActiveEffectContribution> Validate(DaggerfallActiveEffect effect, int type)
    {
        var state = effect.State.Deserialize(DaggerfallSaveJsonContext.Default.DaggerfallCastEffectState);
        if (state?.Settings is not { } settings || settings.Type != type || settings.SubType != -1
            || state.CasterLevel < 1 || state.Amount != 0 || state.SavePercent != 100)
            throw new ArgumentException("Ready door magic state does not match its compiled operation.");
        return [];
    }
}
