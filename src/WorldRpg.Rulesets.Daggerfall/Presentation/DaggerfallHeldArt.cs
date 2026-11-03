using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rusty.Engine;
using Rusty.Engine.FirstPerson;
using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>
/// The 3D first-person art: the authored held definition in the product's content (<see cref="DocumentPath"/>) drawn
/// with the models in the optional container installed beside the product (<see cref="ContainerName"/>): rigged arms
/// carrying their clips and the held weapons that replace the classic weapon sprites when the viewmodel tuning asks for
/// them. The container is built on the operator's machine by <c>scripts/fp-arms/build.sh</c> from the old game's
/// first-person arms and melee clips, which may not be committed. The arms are clip-led: each weapon family plays its
/// idle loop at rest and a clip per classic strike, fitted to the strike's timing by its marks, and the look rides the
/// hand. Marks and holds are tuned in the developer workbench, which saves back to the definition.
/// </summary>
/// <remarks>
/// The definition names the arms rig and the Engine's held arms, and each weapon family: the
/// classic weapon visuals it draws, its look (its model, if it has one yet, and where and how the hand holds it), its
/// idle loop and its strike clips, keyed by the classic strike action ids. Its models are container paths. A
/// definition that names what cannot be drawn is refused by file and field rather than drawn wrong; a clip the arms
/// lack is refused when they are first drawn. Its records are the Engine's tunable first-person records, so edits to
/// the loaded art show on the next draw.
/// </remarks>
internal sealed class DaggerfallHeldArt
{
    /// <summary>The container's file name, installed beside the product.</summary>
    internal const string ContainerName = "first-person.rpak";

    /// <summary>The held definition: a content-root path, so <c>content/worldrpg/first-person/</c> in the repository.</summary>
    internal const string DocumentPath = "worldrpg/first-person/daggerfall.held.json";

    private DaggerfallHeldArt(ProductContentBundle container, DaggerfallHeldArtDocument document)
    {
        Container = container;
        Document = document;
    }

    internal ProductContentBundle Container { get; }

    internal DaggerfallHeldArtDocument Document { get; }

    /// <summary>The family that draws the classic weapon visual <paramref name="resourceId"/>, or null when none does.</summary>
    internal string? Family(string resourceId) =>
        Document.Families.FirstOrDefault(family => family.Value.Weapons.Contains(resourceId, StringComparer.Ordinal)).Key;

    /// <summary>The art a composition resolved over, or nothing when no first-person container was opened.</summary>
    internal static DaggerfallHeldArt? For(ResolvedGameComposition composition)
    {
        ArgumentNullException.ThrowIfNull(composition);
        return composition.Installed.FirstOrDefault(installed => installed.Name == ContainerName)?.Container is { } container
            ? Read(container, composition.Content.ReadBytes(DocumentPath)) : null;
    }

    /// <summary>Reads and checks the held definition against the container's models.</summary>
    /// <exception cref="InvalidDataException">The definition is unreadable, or names what cannot be drawn.</exception>
    internal static DaggerfallHeldArt Read(ProductContentBundle container, ReadOnlyMemory<byte> definition)
    {
        ArgumentNullException.ThrowIfNull(container);
        DaggerfallHeldArtDocument document;
        try
        {
            document = JsonSerializer.Deserialize(definition.Span, DaggerfallHeldArtJson.Default.DaggerfallHeldArtDocument)
                ?? throw new InvalidDataException($"{DocumentPath} is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"{DocumentPath} cannot be read: {exception.Message}", exception);
        }
        List<string> problems = [];
        void Add(IEnumerable<HeldProblem> found, string under) => problems.AddRange(found.Select(problem => $"{problem.Under(under).Field}: {problem.Message}"));
        Add(document.Rig.Problems(), "rig");
        if (document.Families.Count == 0) problems.Add("families: names no weapon family.");
        foreach (var (name, family) in document.Families)
        {
            string at = $"families.{name}";
            if (family.Weapons.Count == 0) problems.Add($"{at}.weapons: names no classic weapon visual to replace.");
            foreach (string weapon in family.Weapons.Where(weapon => document.Families.Any(other => other.Key != name && other.Value.Weapons.Contains(weapon))))
                problems.Add($"{at}.weapons: '{weapon}' is drawn by another family too.");
            if (family.Idle.Clip.Length == 0) problems.Add($"{at}.idle.clip: must name a clip.");
            if (!(family.Idle.Seconds > 0)) problems.Add($"{at}.idle.seconds: must be positive.");
            if (family.Strikes.Count == 0) problems.Add($"{at}.strikes: names no strike clip.");
            foreach (var (action, clip) in family.Strikes)
            {
                if (!NormalizedClassicWeapon.StrikeActions.Contains(action))
                    problems.Add($"{at}.strikes.{action}: is not a classic strike action ({string.Join(", ", NormalizedClassicWeapon.StrikeActions)}).");
                Add(clip.Problems(), $"{at}.strikes.{action}");
            }
            foreach (string action in (family.LeftHand ?? []).Where(action => !family.Strikes.ContainsKey(action)))
                problems.Add($"{at}.leftHand: '{action}' has no strike clip here.");
        }
        IEnumerable<string> models = [document.Rig.Model, document.RetroModel, .. document.Families.Values.Select(family => family.Look.Model).OfType<string>()];
        foreach (string model in models.Where(model => !container.Entries.ToArray().Any(entry => entry.Path == model)))
            problems.Add($"'{model}' is not in {ContainerName}.");
        return problems.Count == 0
            ? new DaggerfallHeldArt(container, document)
            : throw new InvalidDataException($"{DocumentPath}: {string.Join(" ", problems)}");
    }
}

/// <summary>The held definition (<see cref="DaggerfallHeldArt.DocumentPath"/>).</summary>
/// <param name="Rig">The arms model, which carries every clip the families play, and how to drive it; its model is a container path.</param>
/// <param name="RetroModel">The same arms with the retro look (their colour map alone, small and drawn nearest), which the heldRetro style draws; a container path.</param>
/// <param name="Arms">The shoulder (where the clips' right upper arm sits from the eye), elbow pole, default hold and hand closures.</param>
/// <param name="Families">The weapon families by name, each playing its own clips.</param>
internal sealed record DaggerfallHeldArtDocument(ArmRig Rig, string RetroModel, HeldArms Arms, Dictionary<string, DaggerfallHeldFamily> Families);

/// <summary>
/// A weapon family: the classic weapon visuals (resource ids) it draws, its look, the clip the arms loop at rest and
/// the clip each classic strike plays (Engine <see cref="ArmClip"/>, its phases marked in the clip). The off hand rests
/// out of view, unless the look is held in both hands (<see cref="TwoHanded"/>: it plays every clip) or the strike is
/// one of <see cref="LeftHand"/>.
/// </summary>
internal sealed record DaggerfallHeldFamily(IReadOnlyList<string> Weapons, DaggerfallHeldLook Look, DaggerfallIdleClip Idle, Dictionary<string, ArmClip> Strikes)
{
    public bool TwoHanded { get; init; }
    public string[]? LeftHand { get; init; }

    /// <summary>Whether the off hand plays the clip of <paramref name="action"/> (null at rest).</summary>
    internal bool MovesLeft(string? action) => TwoHanded || action is not null && (LeftHand?.Contains(action) ?? false);
}

/// <summary>A clip the arms loop at rest, once every <see cref="Seconds"/> of world time.</summary>
internal sealed record DaggerfallIdleClip(string Clip, float Seconds)
{
    public float Seconds { get; set; } = Seconds;
}

/// <summary>
/// A held weapon: its model (a container path, in look space: origin at the grip, length along -Z, up side +Y; null
/// while the family has none, when the hand plays its clips holding nothing), the point the hand holds (look space) and
/// how the hand is turned on it (<see cref="HandAim.Hold"/>, degrees). The build measures both where the clips' weapon
/// bone sits in the hand, so a look rides the clip's swing as the pack's own weapon did; they are tuned in place.
/// </summary>
internal sealed record DaggerfallHeldLook(string? Model, Vector3 Grip, Vector3 Hold)
{
    public Vector3 Grip { get; set; } = Grip;
    public Vector3 Hold { get; set; } = Hold;
}

/// <summary>A <see cref="Vector3"/> as three numbers, <c>[x, y, z]</c>.</summary>
internal sealed class DaggerfallVector3Json : JsonConverter<Vector3>
{
    public override Vector3 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("expected [x, y, z].");
        Span<float> values = stackalloc float[3];
        for (int i = 0; i < 3; i++)
        {
            if (!reader.Read() || reader.TokenType != JsonTokenType.Number) throw new JsonException("expected [x, y, z].");
            values[i] = reader.GetSingle();
        }
        if (!reader.Read() || reader.TokenType != JsonTokenType.EndArray) throw new JsonException("expected [x, y, z].");
        return new Vector3(values[0], values[1], values[2]);
    }

    public override void Write(Utf8JsonWriter writer, Vector3 value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        writer.WriteNumberValue(value.X);
        writer.WriteNumberValue(value.Y);
        writer.WriteNumberValue(value.Z);
        writer.WriteEndArray();
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true,
    Converters = [typeof(DaggerfallVector3Json)], RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(DaggerfallHeldArtDocument))]
internal sealed partial class DaggerfallHeldArtJson : JsonSerializerContext;
