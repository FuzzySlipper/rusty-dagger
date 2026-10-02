using System.Text.Json;
using System.Text.Json.Serialization;
using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Policies;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed record DaggerfallPeriodicCastState(
    [property: JsonRequired] DaggerfallCastEffectState Cast,
    [property: JsonRequired] long NextRound);

/// <summary>Saved admitted settings and one round's draw identity shared by periodic magic payloads.</summary>
internal static class DaggerfallPeriodicCast
{
    internal static JsonElement Encode(DaggerfallPeriodicCastState state) =>
        JsonSerializer.SerializeToElement(state, DaggerfallSaveJsonContext.Default.DaggerfallPeriodicCastState);
    internal static DaggerfallPeriodicCastState Read(JsonElement state, int type, int subtype)
    {
        var value = state.Deserialize(DaggerfallSaveJsonContext.Default.DaggerfallPeriodicCastState)
            ?? throw new ArgumentException("Periodic cast state is missing.");
        if (value.NextRound < 0 || value.Cast?.Settings is not { } settings || settings.Type != type || settings.SubType != subtype
            || value.Cast.CasterLevel < 1 || value.Cast.Amount < 0 || value.Cast.SavePercent is < 1 or > 100)
            throw new ArgumentException("Periodic cast state does not match its admitted compiled variant.");
        return value;
    }
    internal static int RollMagnitude(DaggerfallActiveEffect effect, IRandomService random, int type, int subtype, string randomScope, string drawPrefix)
    {
        var state = Read(effect.State, type, subtype);
        int draw = 0;
        int Roll(int low, int high) => checked((int)random.DrawKeyed(new(0, randomScope,
            $"{drawPrefix}{effect.Context.Instance.Value}:round:{state.NextRound}:draw:{++draw}", low, high)).Value);
        int amount = DaggerfallMagicAdmissionPolicy.RollEffectMagnitude(state.Cast.Settings, state.Cast.CasterLevel, Roll);
        effect.State = Encode(state with { NextRound = checked(state.NextRound + 1) });
        return (int)(amount * (state.Cast.SavePercent / 100f));
    }
}
