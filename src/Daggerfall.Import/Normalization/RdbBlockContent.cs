using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalized;

namespace Daggerfall.Import.Normalization;

/// <summary>What one RDB flat is, read from its editor marker or billboard texture.</summary>
internal enum RdbFlatKind
{
    /// <summary>A quest spawn or item marker.</summary>
    QuestMarker,

    /// <summary>A start marker: the location's start in its start block, and the block's water level and castle flag.</summary>
    StartMarker,

    /// <summary>An enter marker, the location's entrance in its start block.</summary>
    EnterMarker,

    /// <summary>A random treasure marker, whose loot the location's dungeon type selects.</summary>
    Treasure,

    /// <summary>A fixed mobile marker naming a classic mobile.</summary>
    FixedMobile,

    /// <summary>Any other editor flat, which places nothing.</summary>
    Editor,

    /// <summary>An ordinary billboard, which may also light its surroundings.</summary>
    Billboard,
}

/// <summary>One RDB flat in its block's frame with its classification.</summary>
internal sealed record RdbBlockFlat(int Index, RdbFlatSource Source, RdbFlatKind Kind, Arena2ImportPoint Point)
{
    /// <summary>The fixed mobile a <see cref="RdbFlatKind.FixedMobile"/> marker names.</summary>
    public Arena2MobileSource? Mobile { get; init; }
}

/// <summary>One RDB light record in its block's frame, with the donor's light range.</summary>
internal sealed record RdbBlockLight(int Index, Arena2ImportPoint Point, float Range);

/// <summary>One RDB model record with the action and door facts its tags and action record carry.</summary>
/// <param name="Index">Its ordinal in the block's model records.</param>
/// <param name="Source">The source record.</param>
/// <param name="OrdinaryActionDoor">Whether its description tags it as an action door.</param>
/// <param name="SpecialDoorAction">Whether it joins the linked open/close-door action path without the tag.</param>
/// <param name="StartingLockValue">The classic lock an ordinary action door starts with, else zero.</param>
internal sealed record RdbBlockModel(int Index, RdbModelSource Source, bool OrdinaryActionDoor, bool SpecialDoorAction, int StartingLockValue)
{
    private readonly RdbModelRotation rotation = RdbModelRotation.ForModel(Source);

    public bool ActionDoor => OrdinaryActionDoor || SpecialDoorAction;

    /// <summary>Whether its action record moves it, so its mesh stays local to an explicit instance transform.</summary>
    public bool ActionModel => Source.Action is not null && Source.ObjectOffset > 0;

    /// <summary>The model's origin in its block's frame.</summary>
    public Arena2ImportPoint Point => Arena2SourceTransform.ToImportPoint(Source.X, Source.Y, Source.Z);

    /// <summary>The model's source Euler degrees, in the donor's <c>T * Rz * Rx * Ry</c> convention.</summary>
    public Arena2EulerDegrees RotationDegrees => Arena2SourceTransform.ToEulerDegrees(Source);

    /// <summary>Turns one model-local importer point by the model's rotation, before any translation.</summary>
    public Arena2ImportPoint Rotate(Arena2ImportPoint local) => rotation.Transform(local);

    /// <summary>
    /// Places one rotated model point at the model's origin and then the block's: the donor's source
    /// arithmetic, in its order, so the block's own frame and a placed site agree to the bit.
    /// </summary>
    public Arena2ImportPoint Place(Arena2ImportPoint rotated, Arena2ImportPoint blockOrigin) => new(
        rotated.XMetres + RdbBlockContent.ToMetres(Source.X) + blockOrigin.XMetres,
        rotated.YMetres - RdbBlockContent.ToMetres(Source.Y) + blockOrigin.YMetres,
        rotated.ZMetres + RdbBlockContent.ToMetres(Source.Z) + blockOrigin.ZMetres);
}

/// <summary>
/// The block-level normalization of one RDB record: its lights, classified flats, models and action graph,
/// each in the block's own frame. A dungeon site places these at each block's grid origin and applies its
/// location's start block, dungeon type and texture table; the world block publication publishes them in
/// the block's frame. Both read the block only through here.
/// </summary>
internal sealed class RdbBlockContent
{
    /// <summary>The donor lights an RDB light record white, at three times its source radius.</summary>
    private const float LightRangeMultiplier = 3F;

    private RdbBlockContent(string sourceKey, int sourceOrdinal, RdbBlockSource block)
    {
        SourceKey = sourceKey;
        SourceOrdinal = sourceOrdinal;
        Block = block;
        Lights = [.. block.Lights.Select((light, index) => new RdbBlockLight(index,
            Arena2SourceTransform.ToImportPoint(light.X, light.Y, light.Z), ToMetres(light.Radius) * LightRangeMultiplier))];
        List<RdbBlockFlat> flats = [];
        for (int index = 0; index < block.Flats.Count; index++)
        {
            RdbFlatSource flat = block.Flats[index];
            Arena2ImportPoint point = Arena2SourceTransform.ToImportPoint(flat.X, flat.Y, flat.Z);
            flats.Add(Classify(index, flat, point));
        }

        Flats = flats;
        Models = [.. block.Models.Select((model, index) => new RdbBlockModel(index, model,
            RdbSourceClassification.HasActionDoorTag(model), RdbSourceClassification.HasSpecialDoorAction(model),
            RdbSourceClassification.HasActionDoorTag(model) ? StartingLockValue(model.TriggerFlagStartingLock, block.Source, index) : 0))];
        // DaggerfallBillboard reads a block's water level and castle flag from its start marker; the first
        // start marker is the one the donor's FindMarkers reads.
        RdbBlockFlat? start = flats.FirstOrDefault(flat => flat.Kind == RdbFlatKind.StartMarker);
        Castle = flats.Any(flat => flat.Kind == RdbFlatKind.StartMarker && flat.Source.Magnitude != 0);
        WaterLevel = start is { Source.SoundIndex: not 0 } ? -8 * start.Source.SoundIndex : null;
        AmbientZone = StringComparer.OrdinalIgnoreCase.Equals(sourceKey, "S0000161.RDB")
            ? NormalizedAmbientZoneKind.SpecialArea
            : Castle ? NormalizedAmbientZoneKind.Castle : null;
    }

    public string SourceKey { get; }

    /// <summary>The record's ordinal in BLOCKS.BSA, its provenance.</summary>
    public int SourceOrdinal { get; }

    public RdbBlockSource Block { get; }

    public IReadOnlyList<RdbBlockLight> Lights { get; }

    public IReadOnlyList<RdbBlockFlat> Flats { get; }

    public IReadOnlyList<RdbBlockModel> Models { get; }

    /// <summary>Whether a start marker flags the block as a castle block.</summary>
    public bool Castle { get; }

    /// <summary>
    /// The block's water level in classic source units (Y down), <c>-8</c> times its start marker's sound
    /// index, or null when the block has none: the donor's 10000 "no water" sentinel stays here.
    /// </summary>
    public int? WaterLevel { get; }

    /// <summary>The ambient area the block selects, if any: a castle block, or the special area block.</summary>
    public NormalizedAmbientZoneKind? AmbientZone { get; }

    /// <summary>Reads one RDB record from the block archive, refusing a record that is absent.</summary>
    public static RdbBlockContent Read(BsaArchive blocks, string sourceKey)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        if (!blocks.TryGetByName(sourceKey, out BsaRecord? record) || record is null)
            throw new InvalidOperationException($"BLOCKS.BSA is missing requested block '{sourceKey}'.");
        return new(sourceKey, record.Ordinal, RdbDecoder.Decode(blocks.GetPayload(record).Span, blocks.Source));
    }

    internal static float ToMetres(int sourceUnits) => sourceUnits * Arena2SourceTransform.SourceUnitMetres;

    /// <summary>
    /// The block's action graph under one identity scope: <c>action/{scope}/model-N</c> and
    /// <c>action/{scope}/flat-N</c>, linked by source object offset, with door links as
    /// <c>door/{scope}/N</c>. A placed site and the block's own publication differ only in the scope.
    /// </summary>
    public IReadOnlyList<NormalizedDungeonAction> Actions(string scope, Func<Arena2ImportPoint, NormalizedVector3> place)
    {
        List<NormalizedDungeonAction> actions = [];
        Dictionary<int, string> actionIdsByOffset = new();
        Dictionary<int, string?> doorIdsByOffset = new();
        foreach (RdbBlockModel model in Models)
        {
            if (!model.ActionModel)
                continue;

            string id = $"action/{scope}/model-{model.Index}";
            if (!actionIdsByOffset.TryAdd(model.Source.ObjectOffset, id))
                throw new InvalidOperationException($"RDB block '{scope}' repeats action object offset {model.Source.ObjectOffset}.");

            doorIdsByOffset[model.Source.ObjectOffset] = model.ActionDoor ? $"door/{scope}/{model.Index}" : null;
        }

        foreach (RdbBlockFlat flat in Flats)
        {
            // Offset zero is Arena2's absolute null-link sentinel. Preserve that authored node
            // even when all other action fields are zero; only the negative no-object sentinel
            // can prove that a flat carries no action record at all.
            if (flat.Source.ObjectOffset <= 0 || flat.Source.Action == 0 && flat.Source.Flags == 0 && flat.Source.NextObjectOffset < 0)
                continue;

            string id = $"action/{scope}/flat-{flat.Index}";
            if (!actionIdsByOffset.TryAdd(flat.Source.ObjectOffset, id))
                throw new InvalidOperationException($"RDB block '{scope}' repeats action object offset {flat.Source.ObjectOffset}.");
            doorIdsByOffset[flat.Source.ObjectOffset] = null;
        }

        foreach (RdbBlockModel model in Models)
        {
            if (model.Source.Action is not { } action || model.Source.ObjectOffset <= 0)
                continue;

            string id = actionIdsByOffset[model.Source.ObjectOffset];
            actions.Add(new(
                id,
                model.Source.ObjectOffset,
                model.Source.TriggerFlagStartingLock,
                action.Flags,
                action.Axis,
                action.Duration,
                action.Magnitude,
                action.NextObjectOffset,
                action.NextObjectOffset > 0 && actionIdsByOffset.TryGetValue(action.NextObjectOffset, out string? next) ? next : null,
                doorIdsByOffset[model.Source.ObjectOffset],
                IsFlat: false,
                SoundIndex: model.Source.SoundIndex,
                Position: null,
                RawIndex: model.Source.SoundIndex,
                Poison: action.Flags == 0x1A ? new(SourceKey, "source-unresolved") : null));
        }

        foreach (RdbBlockFlat flat in Flats)
        {
            if (flat.Source.ObjectOffset <= 0 || !actionIdsByOffset.TryGetValue(flat.Source.ObjectOffset, out string? id))
                continue;
            if (flat.Source.Action == 0x1A && (flat.Source.TextureArchive != 199 || flat.Source.TextureRecord != 19))
                throw new InvalidOperationException($"RDB Poison action '{id}' is not the owner-approved unresolved treasure marker.");

            actions.Add(new(
                id,
                flat.Source.ObjectOffset,
                flat.Source.Flags,
                flat.Source.Action,
                flat.Source.Magnitude,
                0,
                flat.Source.Magnitude,
                flat.Source.NextObjectOffset,
                flat.Source.NextObjectOffset > 0 && actionIdsByOffset.TryGetValue(flat.Source.NextObjectOffset, out string? next) ? next : null,
                DoorId: null,
                IsFlat: true,
                SoundIndex: flat.Source.SoundIndex,
                Position: place(flat.Point),
                RawIndex: flat.Source.SoundIndex,
                Poison: flat.Source.Action == 0x1A ? new(SourceKey, "source-unresolved") : null));
        }

        return actions;
    }

    /// <summary>
    /// Classifies one flat. Daggerfall's fixed-mobile meaning is carried only by the editor flat marker
    /// (archive 199, record 16); its classic source data has garbage high bits, mobile zero is valid and 99 is
    /// the one reserved invalid value, so an ordinary billboard is never classified from a faction low byte.
    /// </summary>
    private static RdbBlockFlat Classify(int index, RdbFlatSource flat, Arena2ImportPoint point)
    {
        if (QuestMarkerNormalization.KindOf(flat.TextureArchive, flat.TextureRecord) is not null)
            return new(index, flat, RdbFlatKind.QuestMarker, point);
        if (RdbSourceClassification.IsStartMarker(flat)) return new(index, flat, RdbFlatKind.StartMarker, point);
        if (RdbSourceClassification.IsEnterMarker(flat)) return new(index, flat, RdbFlatKind.EnterMarker, point);
        if (RdbSourceClassification.IsRandomTreasureMarker(flat)) return new(index, flat, RdbFlatKind.Treasure, point);
        byte mobileId = unchecked((byte)flat.FactionOrMobileId);
        if (RdbSourceClassification.IsFixedMobileMarker(flat)
            && mobileId != 99
            && MobileSourceMetadata.TryGet(new Arena2MobileId(mobileId), out Arena2MobileSource? mobile))
            return new(index, flat, RdbFlatKind.FixedMobile, point) { Mobile = mobile };
        return new(index, flat, flat.TextureArchive == RdbSourceClassification.EditorFlatArchive ? RdbFlatKind.Editor : RdbFlatKind.Billboard, point);
    }

    private static int StartingLockValue(uint sourceValue, string source, int modelIndex)
    {
        ReadOnlySpan<int> values = [0, 2, 4, 6, 8, 10, 12, 14, 16, 18, 20, 25, 30, 50, 128, 255];
        uint selector = sourceValue >> 4;
        if (selector >= (uint)values.Length)
            throw new Arena2FormatException(source, modelIndex, $"RDB action-door lock selector {selector} is outside the classic lock table.");
        return values[(int)selector];
    }
}

/// <summary>An RDB model's rotation, the donor's <c>Rz * Rx * Ry</c> of its source Euler degrees.</summary>
internal readonly record struct RdbModelRotation(float M11, float M12, float M13, float M21, float M22, float M23, float M31, float M32, float M33)
{
    public static RdbModelRotation ForModel(RdbModelSource model)
    {
        Arena2EulerDegrees degrees = Arena2SourceTransform.ToEulerDegrees(model);
        return RotationZ(degrees.Z) * RotationX(degrees.X) * RotationY(degrees.Y);
    }

    public Arena2ImportPoint Transform(Arena2ImportPoint value) => new(
        (M11 * value.XMetres) + (M12 * value.YMetres) + (M13 * value.ZMetres),
        (M21 * value.XMetres) + (M22 * value.YMetres) + (M23 * value.ZMetres),
        (M31 * value.XMetres) + (M32 * value.YMetres) + (M33 * value.ZMetres));

    public static RdbModelRotation operator *(RdbModelRotation left, RdbModelRotation right) => new(
        (left.M11 * right.M11) + (left.M12 * right.M21) + (left.M13 * right.M31), (left.M11 * right.M12) + (left.M12 * right.M22) + (left.M13 * right.M32), (left.M11 * right.M13) + (left.M12 * right.M23) + (left.M13 * right.M33),
        (left.M21 * right.M11) + (left.M22 * right.M21) + (left.M23 * right.M31), (left.M21 * right.M12) + (left.M22 * right.M22) + (left.M23 * right.M32), (left.M21 * right.M13) + (left.M22 * right.M23) + (left.M23 * right.M33),
        (left.M31 * right.M11) + (left.M32 * right.M21) + (left.M33 * right.M31), (left.M31 * right.M12) + (left.M32 * right.M22) + (left.M33 * right.M32), (left.M31 * right.M13) + (left.M32 * right.M23) + (left.M33 * right.M33));

    private static RdbModelRotation RotationX(float degrees)
    {
        (float sin, float cos) = MathF.SinCos(DegreesToRadians(degrees));
        return new(1F, 0F, 0F, 0F, cos, -sin, 0F, sin, cos);
    }

    private static RdbModelRotation RotationY(float degrees)
    {
        (float sin, float cos) = MathF.SinCos(DegreesToRadians(degrees));
        return new(cos, 0F, sin, 0F, 1F, 0F, -sin, 0F, cos);
    }

    private static RdbModelRotation RotationZ(float degrees)
    {
        (float sin, float cos) = MathF.SinCos(DegreesToRadians(degrees));
        return new(cos, -sin, 0F, sin, cos, 0F, 0F, 0F, 1F);
    }

    private static float DegreesToRadians(float degrees) => degrees * (MathF.PI / 180F);
}
