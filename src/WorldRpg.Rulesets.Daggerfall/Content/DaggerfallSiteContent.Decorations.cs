using System.Text.Json;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <param name="Source">The texture archive and record of its summer sprite.</param>
/// <param name="WinterSource">The texture archive and record of its winter sprite.</param>
/// <param name="Lamp">A burning lamp, as the importer publishes it beside the light the lamp sheds.</param>
/// <param name="Light">The id among the site's lights of the light it sheds, as the importer publishes it: a lamp's own, or a
/// dungeon's light record that stands for it.</param>
internal sealed record DaggerfallSiteDecoration(string Id, WorldPoint Position, bool BaseAligned,
    NormalizedBillboardSprite Summer, NormalizedBillboardSprite Winter, (int Archive, int Record) Source, (int Archive, int Record) WinterSource,
    bool Lamp, string? Light = null);

internal static partial class DaggerfallSiteContent
{
    internal static IReadOnlyList<DaggerfallSiteDecoration> ReadDecorations(ReadOnlyMemory<byte> bytes,
        IReadOnlyDictionary<(int Archive, int Record), NormalizedBillboardSprite> sprites,
        DaggerfallWorldProfileKind kind, int climate, DaggerfallContentDiagnostics diagnostics)
    {
        using JsonDocument document = JsonDocument.Parse(bytes);
        var world = document.RootElement.GetProperty("world");
        if (!world.TryGetProperty("billboards", out var billboards)) return [];
        List<DaggerfallSiteDecoration> result = [];
        foreach (var billboard in billboards.EnumerateArray())
        {
            string id = DaggerfallBaseContent.Text(billboard, "id", diagnostics);
            string spriteId = DaggerfallBaseContent.Text(billboard, "spriteResourceId", diagnostics);
            string[] parts = spriteId.Split('-');
            if (parts is not ["sprite/texture", var archiveText, var recordText]
                || !int.TryParse(archiveText, out int archive) || !int.TryParse(recordText, out int record))
            { diagnostics.Add($"Decoration '{id}' has invalid sprite '{spriteId}'."); continue; }
            bool nature = kind == DaggerfallWorldProfileKind.Exterior && OptionalBoolean(billboard, "climateNature", false, diagnostics);
            int summer = nature ? DaggerfallClimateArchives.NatureTextureArchive(climate, DaggerfallExteriorSeason.Summer) : archive;
            int winter = nature ? DaggerfallClimateArchives.NatureTextureArchive(climate, DaggerfallExteriorSeason.Winter) : archive;
            if (!sprites.TryGetValue((summer, record), out var warm) || !sprites.TryGetValue((winter, record), out var cold))
            { diagnostics.Add($"Decoration '{id}' lacks shared sprite {summer}/{record} or {winter}/{record}."); continue; }
            var position = ObjectVector3(billboard.GetProperty("position"), id, diagnostics);
            result.Add(new(id, WorldPoint.From(position), OptionalBoolean(billboard, "baseAligned", false, diagnostics), warm, cold, (summer, record), (winter, record),
                OptionalBoolean(billboard, "lamp", false, diagnostics), DaggerfallBaseContent.OptionalText(billboard, "light", diagnostics)));
        }
        return result;
    }
}
