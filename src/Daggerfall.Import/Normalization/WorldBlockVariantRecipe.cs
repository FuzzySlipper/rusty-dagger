using System.Text.Json;
using Daggerfall.Import.Normalized;
using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Normalization;

/// <summary>
/// An authored variant of one RMB exterior, as a quest's world variant states it: an override of the base block, never a
/// copy. It removes block-own models by the placement ids the base block publishes (<c>model/{block}/misc/{n}</c>) and
/// adds models posed in the block's frame. A building's models, doors, people, flats and gates stay the base block's:
/// removing one would leave a door into a building that is not drawn, or a saved change to something no longer placed.
/// </summary>
public sealed record WorldBlockVariantRecipe(string Block, string Variant, IReadOnlyList<string> Remove, IReadOnlyList<WorldBlockVariantModel> Add)
{
    /// <summary>
    /// A content pack payload's block-variant recipes (<c>worldVariants.blocks</c>), each checked, and none stated twice; a
    /// payload with no world variants has none.
    /// </summary>
    public static IReadOnlyList<WorldBlockVariantRecipe> ReadPayload(ReadOnlySpan<byte> payload, string path)
    {
        WorldBlockVariantRecipe[] recipes;
        try
        {
            using JsonDocument document = JsonDocument.Parse(payload.ToArray());
            if (!document.RootElement.TryGetProperty("worldVariants", out JsonElement variants)) return [];
            recipes = variants.GetProperty("blocks").Deserialize<WorldBlockVariantRecipe[]>(PublishedJson.SectionRead)
                ?? throw new InvalidDataException($"'{path}' states no block variants.");
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException)
        {
            throw new InvalidDataException($"The block variants of '{path}' do not read: {exception.Message}", exception);
        }

        foreach (WorldBlockVariantRecipe recipe in recipes) recipe.Validate();
        if (recipes.GroupBy(recipe => (recipe.Block, recipe.Variant)).FirstOrDefault(group => group.Count() > 1) is { } repeated)
            throw new InvalidDataException($"'{path}' states variant '{repeated.Key.Variant}' of '{repeated.Key.Block}' twice.");
        return recipes;
    }

    /// <summary>Refuses a recipe no quest could select or that changes nothing, naming it.</summary>
    public void Validate()
    {
        string owner = $"Block variant '{Variant}' of '{Block}'";
        if (!Block.EndsWith(".RMB", StringComparison.Ordinal)) throw new InvalidDataException($"{owner} names no RMB block; only RMB exteriors take variants.");
        if (!IsName(Variant)) throw new InvalidDataException($"{owner} has a name a quest's world update cannot select.");
        if (Remove.Count == 0 && Add.Count == 0) throw new InvalidDataException($"{owner} changes nothing.");
        if (Remove.Distinct(StringComparer.Ordinal).Count() != Remove.Count) throw new InvalidDataException($"{owner} removes a placement twice.");
        foreach (WorldBlockVariantModel model in Add)
            if (!uint.TryParse(model.Model, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out _)
                || !float.IsFinite(model.Position.X) || !float.IsFinite(model.Position.Y) || !float.IsFinite(model.Position.Z) || !float.IsFinite(model.YawDegrees))
                throw new InvalidDataException($"{owner} adds model '{model.Model}' with no ARCH3D record number or a non-finite pose.");
    }

    /// <summary>A variant name a quest line can spell (<c>[a-zA-Z0-9_.-]+</c>), and not the dash that clears a selection.</summary>
    public static bool IsName(string name) =>
        !string.IsNullOrEmpty(name) && name != "-" && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '-');
}

/// <summary>
/// One model a block variant adds, in the block's right-handed metre frame: its origin, and its turn about +Y in degrees,
/// counterclockwise seen from above. It is placed as one of the block's own objects, so the pose snaps to the source's
/// grid (2.5 cm, 2048 steps a turn).
/// </summary>
public sealed record WorldBlockVariantModel(string Model, NormalizedVector3 Position, float YawDegrees);
