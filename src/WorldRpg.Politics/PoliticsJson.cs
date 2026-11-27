using System.Text.Json.Serialization;

namespace WorldRpg.Politics;

/// <summary>
/// The board's JSON: board and card files, trace and output lines, saved boards and the world's nudges. A missing field
/// or an unknown one is an error rather than a default, so a typo in a board file is reported instead of starting a
/// region at zero.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    RespectRequiredConstructorParameters = true,
    RespectNullableAnnotations = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(BoardSetup))]
[JsonSerializable(typeof(CardSet))]
[JsonSerializable(typeof(TurnTrace))]
[JsonSerializable(typeof(ScheduledNudge))]
internal sealed partial class PoliticsJson : JsonSerializerContext;

/// <summary>
/// The JSON of records the world reads back: saved boards, each week's outputs and the turn traces a game keeps. Every field is written, nulls
/// included, so the outputs' schema never shifts with the week, and every field is required back, so a save that lost
/// anything is refused rather than resumed with a default.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    RespectRequiredConstructorParameters = true,
    RespectNullableAnnotations = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(BoardSave))]
[JsonSerializable(typeof(BoardOutputs))]
[JsonSerializable(typeof(TurnTrace))]
internal sealed partial class PoliticsRecordJson : JsonSerializerContext;
