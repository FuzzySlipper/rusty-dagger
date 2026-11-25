using System.Text.Json.Serialization;

namespace WorldRpg.Politics;

/// <summary>
/// The board's JSON: board files and trace lines. A missing field or an unknown one is an error rather than a
/// default, so a typo in a board file is reported instead of starting a region at zero.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    RespectRequiredConstructorParameters = true,
    RespectNullableAnnotations = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(BoardSetup))]
[JsonSerializable(typeof(TurnTrace))]
internal sealed partial class PoliticsJson : JsonSerializerContext;
