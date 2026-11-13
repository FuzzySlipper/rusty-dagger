using System.Text.Json.Serialization;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// The magic states an effect's save entry carries as its own JSON. They are the effects' state, read and
/// written by Magic's effect owners; the save boundary carries them as the effect save's opaque state.
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(DaggerfallCastEffectState))]
[JsonSerializable(typeof(DaggerfallSoulTrapState))]
[JsonSerializable(typeof(DaggerfallShieldState))]
[JsonSerializable(typeof(DaggerfallPeriodicCastState))]
[JsonSerializable(typeof(DaggerfallAttributeDrainState))]
[JsonSerializable(typeof(DaggerfallMolagBalState))]
[JsonSerializable(typeof(DaggerfallSpellPointHealingState))]
internal partial class DaggerfallMagicJson : JsonSerializerContext;
