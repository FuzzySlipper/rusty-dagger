using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalized;

namespace Daggerfall.Import.Normalization;

/// <summary>
/// DFU's interior-light flat policy, expressed as normalized source facts without Unity prefabs: each lamp's light
/// above its sprite's centre, its reach, strength and colour.
/// </summary>
public static class DaggerfallInteriorLightFacts
{
    public const ushort Archive = 210;

    /// <summary>
    /// The lamps that burn. Records 7 and 10 are chandeliers with their candles out and 12 an extinguished lamp: their
    /// art shows no flame, yet DFU's switch has no case for them, so its prefab's light shone from them anyway.
    /// </summary>
    internal static bool Lit(int record) => record is not (7 or 10 or 12);

    /// <summary>
    /// How near a dungeon's light record stands to a lit lamp flat when both are that lamp's light. The donor lights a
    /// dungeon by its records alone and a building by its flats alone; nearly every record stands this near a lamp, most
    /// within a metre, and most of the rest far from any.
    /// </summary>
    private const float LampTwinMetres = 2F;

    /// <summary>
    /// A dungeon block's lights, one for each lamp: of a light record and the lamp flats beside it, the farther-reaching
    /// stands for them. A torch's own light, in its record's colour at its flame, outreaches the record beside it; a
    /// record among candles outreaches them, and lights them as it did in the donor. A record with no lamp beside it lights
    /// alone. Both lit, a dungeon lit each lamp twice, and a lamp's shadow would be filled by its twin.
    /// </summary>
    internal static IReadOnlyList<NormalizedLightPlacement> OnePerLamp(IReadOnlyList<NormalizedLightPlacement> records, IReadOnlyList<NormalizedLightPlacement> lamps)
    {
        List<NormalizedLightPlacement> kept = [];
        HashSet<string> outreached = new(StringComparer.Ordinal);
        foreach (NormalizedLightPlacement record in records)
        {
            NormalizedLightPlacement[] beside = [.. lamps.Where(lamp => Distance(lamp.Position, record.Position) <= LampTwinMetres)];
            if (beside.Any(lamp => lamp.Range >= record.Range)) continue;
            kept.Add(record);
            outreached.UnionWith(beside.Select(lamp => lamp.Id));
        }
        return [.. kept, .. lamps.Where(lamp => !outreached.Contains(lamp.Id))];
    }

    private static float Distance(NormalizedVector3 a, NormalizedVector3 b) =>
        MathF.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y)) + ((a.Z - b.Z) * (a.Z - b.Z)));

    internal static bool TryProject(string id, RdbFlatSource flat, NormalizedVector3 position, float billboardHeight, out NormalizedLightPlacement light)
        => TryProject(id, flat.TextureArchive, flat.TextureRecord, position, billboardHeight, out light);

    /// <summary>
    /// How far above its sprite's centre a lamp's flame burns, where its light sits. DFU's offsets, and where its switch
    /// says "todo" or sets the light at a standing lantern's top, the flame as the art draws it (#10867 measured each
    /// refreshed sprite's glow against the classic frame, and #10852's lamp-post meshes keep their flames where their
    /// sprites do): the wall torches 16 and 18 hold theirs a fifth of a metre up, the pillar 19 its bowl near its top, the
    /// short-chained lantern 26 hangs its flame half a metre below the centre, where the chain would otherwise take the
    /// light, and the lamp posts 14, 15, 28 and 29 carry theirs in the globe or lantern below their tops.
    /// </summary>
    internal static float FlameAboveCentre(int record, float billboardHeight) => record switch
    {
        0 => -.1F, 2 or 3 => .1F, 5 => .15F, 6 or 20 => .6F, 9 => .4F, 11 => -.4F,
        13 => -.35F, 14 => .7F, 15 => .75F, 16 or 17 or 18 => .2F, 19 => .45F, 21 => billboardHeight / 2.4F,
        22 or 26 => -.5F, 24 => -1.85F, 25 => -1F, 27 => -.02F, 28 => 1.6F, 29 => 1.5F, _ => 0F,
    };

    internal static bool TryProject(string id, int archive, int record, NormalizedVector3 position, float billboardHeight, out NormalizedLightPlacement light)
    {
        light = default!;
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (archive != Archive || !Lit(record)) return false;
        float offset = FlameAboveCentre(record, billboardHeight);
        (float range, float intensity, NormalizedVector3 color) = ((float Range, float Intensity, NormalizedVector3 Color))(record switch
        {
            0 => (20F, 1.1F, new(.95F, .91F, .63F)),
            2 => (10F / 3F, .6F, new(1F, .99F, .82F)),
            3 or 4 => (10F / 3F, 1F, new(1F, 1F, 1F)),
            5 => (7.5F, .33F, new(1F, .89F, .61F)),
            6 => (15F, .75F, new(1F, .93F, .62F)),
            8 => (10F, 1F, new(.68F, 1F, .94F)),
            9 => (15F, .65F, new(1F, .92F, .6F)),
            11 => (5F, .5F, new(1F, 1F, 1F)),
            13 => (12F, 1.1F, new(.93F, .84F, .49F)),
            17 => (10F, .8F, new(1F, .97F, .87F)),
            20 => (12F, .75F, new(1F, .92F, .72F)),
            21 => (10F / 3F, .5F, new(1F, .95F, .67F)),
            22 => (10F, 1.5F, new(1F, .95F, .78F)),
            24 or 25 or 26 or 27 => (10F, 1.4F, new(1F, .98F, .64F)),
            _ => (10F, 1F, new(1F, 1F, 1F)),
        });
        light = new(id, new(position.X, position.Y + offset, position.Z), range, intensity, color);
        return true;
    }
}
