using System.Text.Json.Serialization;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// The affliction states an effect's save entry carries as its own JSON. They are the effects' state, read
/// and written by these owners; the save boundary carries them as the effect save's opaque state.
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(DaggerfallInfectionState))]
[JsonSerializable(typeof(DaggerfallRacialOverrideState))]
internal partial class DaggerfallAfflictionJson : JsonSerializerContext;
