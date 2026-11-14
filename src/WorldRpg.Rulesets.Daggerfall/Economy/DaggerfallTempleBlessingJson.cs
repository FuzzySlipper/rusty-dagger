using System.Text.Json.Serialization;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// The temple blessing state an effect's save entry carries as its own JSON, read and written by the blessing
/// effects; the save boundary carries it as the effect save's opaque state.
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(DaggerfallTempleBlessingState))]
internal partial class DaggerfallTempleBlessingJson : JsonSerializerContext;
