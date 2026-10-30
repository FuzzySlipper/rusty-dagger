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
/// and the held weapons that replace the classic weapon sprites when the viewmodel tuning asks for them. The container
/// is built on the operator's machine by <c>scripts/fp-arms/build.sh</c> from asset-pipeline's generated arms and the
/// procedural dagger. The definition's look and motion were seeded from a capture of the classic weapon animations
/// (<c>scripts/fp-arms/seed.py</c>) and are tuned in the developer workbench, which saves back to it.
/// </summary>
/// <remarks>
/// The definition names the classic weapon visuals its look replaces, the arms rig and the Engine's held arms, the look
/// (its model, the stance that seats it at rest, and where and how the hand holds it), and its held motion, whose
/// tracks are keyed by the classic strike action ids. Its models are container paths. A definition that names what
/// cannot be drawn is refused by file and field rather than drawn wrong. Its records are the Engine's tunable
/// first-person records, so edits to the loaded art show on the next draw.
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

    internal HeldStance Stance => Document.Look.Stance;

    /// <summary>Whether this art draws the classic weapon visual <paramref name="resourceId"/>.</summary>
    internal bool Draws(string resourceId) => Document.Weapons.Contains(resourceId, StringComparer.Ordinal);

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
        Add(document.Look.Stance.Problems(), "look.stance");
        Add(document.Motion.Problems(), "motion");
        if (document.Weapons.Count == 0) problems.Add("weapons: names no classic weapon visual to replace.");
        foreach (string action in document.Motion.Tracks.Keys.Where(action => !NormalizedClassicWeapon.StrikeActions.Contains(action)))
            problems.Add($"motion.tracks.{action}: is not a classic strike action ({string.Join(", ", NormalizedClassicWeapon.StrikeActions)}).");
        foreach (string model in new[] { document.Rig.Model, document.Look.Model }.Where(model => !container.Entries.ToArray().Any(entry => entry.Path == model)))
            problems.Add($"'{model}' is not in {ContainerName}.");
        return problems.Count == 0
            ? new DaggerfallHeldArt(container, document)
            : throw new InvalidDataException($"{DocumentPath}: {string.Join(" ", problems)}");
    }
}

/// <summary>The held definition (<see cref="DaggerfallHeldArt.DocumentPath"/>).</summary>
/// <param name="Weapons">The classic weapon visuals (resource ids) the look replaces.</param>
/// <param name="Rig">The arms model and how to drive it; its model is a container path.</param>
/// <param name="Arms">The shoulder, elbow pole, default hold and hand closures.</param>
/// <param name="Look">The held weapon.</param>
/// <param name="Motion">The weapon's motion, its tracks keyed by classic strike action.</param>
internal sealed record DaggerfallHeldArtDocument(IReadOnlyList<string> Weapons, ArmRig Rig, HeldArms Arms, DaggerfallHeldLook Look, HeldMotion Motion);

/// <summary>
/// A held weapon: its model (a container path, already in look space: origin at the grip, length along -Z,
/// up side +Y), the stance that seats it at rest, the point the hand holds (look space) and how the hand is
/// turned on it (<see cref="HandAim.Hold"/>, degrees). The grip and hold are tuned in place.
/// </summary>
internal sealed record DaggerfallHeldLook(string Model, HeldStance Stance, Vector3 Grip, Vector3 Hold)
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
    Converters = [typeof(DaggerfallVector3Json)], RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true)]
[JsonSerializable(typeof(DaggerfallHeldArtDocument))]
internal sealed partial class DaggerfallHeldArtJson : JsonSerializerContext;
