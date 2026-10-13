using System.Text.Json;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Content;

internal sealed record DaggerfallSiteDecoration(string Id, WorldPoint Position, bool BaseAligned,
    NormalizedBillboardSprite Summer, NormalizedBillboardSprite Winter);

internal static partial class DaggerfallSiteContent
{
    internal static IReadOnlyList<DaggerfallSiteDecoration> ReadDecorations(ReadOnlyMemory<byte> bytes,
        IReadOnlyDictionary<(int Archive, int Record), NormalizedBillboardSprite> sprites,
        DaggerfallWorldProfileKind kind, int climate, DaggerfallContentDiagnostics diagnostics)
    {
        using JsonDocument document = JsonDocument.Parse(bytes);
        var world = document.RootElement.GetProperty("world");
        if (!world.TryGetProperty("billboards", out var flats)) return [];
        List<DaggerfallSiteDecoration> result = [];
        foreach (var flat in flats.EnumerateArray())
        {
            string id = DaggerfallBaseContent.Text(flat, "id", diagnostics);
            string spriteId = DaggerfallBaseContent.Text(flat, "spriteResourceId", diagnostics);
            string[] parts = spriteId.Split('-');
            if (parts is not ["sprite/texture", var archiveText, var recordText]
                || !int.TryParse(archiveText, out int archive) || !int.TryParse(recordText, out int record))
            { diagnostics.Add($"Decoration '{id}' has invalid sprite '{spriteId}'."); continue; }
            bool nature = kind == DaggerfallWorldProfileKind.Exterior && OptionalBoolean(flat, "climateNature", false, diagnostics);
            int summer = nature ? DaggerfallExteriorEnvironment.NatureTextureArchive(climate, DaggerfallExteriorSeason.Summer) : archive;
            int winter = nature ? DaggerfallExteriorEnvironment.NatureTextureArchive(climate, DaggerfallExteriorSeason.Winter) : archive;
            if (!sprites.TryGetValue((summer, record), out var warm) || !sprites.TryGetValue((winter, record), out var cold))
            { diagnostics.Add($"Decoration '{id}' lacks shared sprite {summer}/{record} or {winter}/{record}."); continue; }
            var position = ObjectVector3(flat.GetProperty("position"), id, diagnostics);
            result.Add(new(id, WorldPoint.From(position), OptionalBoolean(flat, "baseAligned", false, diagnostics), warm, cold));
        }
        return result;
    }
}
