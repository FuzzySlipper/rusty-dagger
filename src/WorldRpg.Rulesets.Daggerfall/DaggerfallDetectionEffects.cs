using System.Text.Json;
using WorldRpg.Kit.Effects;
using WorldRpg.Rulesets.Daggerfall.Policies;

namespace WorldRpg.Rulesets.Daggerfall;

internal enum DaggerfallDetection { None, Magic, Enemy, Treasure }
/// <summary>Resolved current contacts retain product identities, never native handles or DOM guesses.</summary>
internal sealed record DaggerfallDetectedItem(string Id, string Definition, ulong Quantity);
internal sealed record DaggerfallDetectionFact(string Kind, string Id, double Distance, double BearingRadians,
    IReadOnlyList<DaggerfallDetectedItem> Items);
internal sealed record DaggerfallDetectorView(string Source, string Kind, IReadOnlyList<DaggerfallDetectionFact> Contacts);

internal static class DaggerfallDetectionEffects
{
    internal static string Key(int subtype) => subtype switch
    { 0 => "detect-magic", 1 => "detect-enemy", 2 => "detect-treasure", _ => throw new ArgumentOutOfRangeException(nameof(subtype)) };
    internal static IEnumerable<DaggerfallEffectDefinition> Definitions()
    {
        for (int subtype = 0; subtype < 3; subtype++)
        {
            int variant = subtype;
            string key = Key(variant);
            yield return new(key, key, DaggerfallEffectStacking.Stack, ushort.MaxValue, 1,
                Apply: effect => Validate(effect, variant), Resume: effect => Validate(effect, variant),
                Spell: new(39, variant, SpellMaker: true, SupportsDuration: true, AllowedTargets: DaggerfallMagicAllowedTargets.CasterOnly),
                ExtendIncumbentDuration: true, IncumbentSettingsMatch: (_, _) => true,
                Detection: variant == 0 ? DaggerfallDetection.Magic : variant == 1 ? DaggerfallDetection.Enemy : DaggerfallDetection.Treasure);
        }
    }
    private static IEnumerable<IActiveEffectContribution> Validate(DaggerfallActiveEffect effect, int subtype)
    {
        var state = effect.State.Deserialize(DaggerfallSaveJsonContext.Default.DaggerfallCastEffectState);
        if (state?.Settings is not { } settings || settings.Type != 39 || settings.SubType != subtype || state.CasterLevel < 1
            || state.Amount != 0 || state.SavePercent is < 1 or > 100)
            throw new ArgumentException("Detector state does not match its admitted duration-only variant.");
        return [];
    }
}
