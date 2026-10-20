using System.Text;
using Daggerfall.Import.Normalization;
using Daggerfall.Import.Publication;
using Xunit;

namespace Daggerfall.Import.Tests;

public sealed class AuthoredUiAssetSetTests
{
    [Fact]
    public void ReadsHowEachAssetIsDrawnBesideItsProvenance()
    {
        byte[] manifest = Encoding.UTF8.GetBytes("""
            {"assets": [
              {"id": "ui.stone.window.v2", "file": "ui-stone-window-v2.png", "sourceFile": "ui-stone-window-v2.png",
               "generator": "generator", "prompt": "prompt", "display": {"scale": 0.5, "slice": [64, 60, 64, 60]}},
              {"id": "ui.cursor.grab.v2", "file": "ui-cursor-grab-v2.png", "sourceFile": "ui-cursor-grab-v2.png",
               "generator": "generator", "prompt": "prompt", "display": {"scale": 0.5, "hotspot": [32, 32]}},
              {"id": "ui.icon.menu.map.v2", "file": "ui-icon-menu-map-v2.png", "sourceFile": "ui-icon-menu-map-v2.png",
               "generator": "generator", "prompt": "prompt"}
            ]}
            """);
        Arena2ClassicMediaProfile profile = AuthoredUiAssetSet.Read(manifest, _ => [1]);
        Dictionary<string, ClassicAuthoredUiAsset> assets = profile.AuthoredUiAssets!.ToDictionary(asset => asset.Id, StringComparer.Ordinal);

        ClassicAuthoredUiDisplay window = Assert.IsType<ClassicAuthoredUiDisplay>(assets["ui.stone.window.v2"].Display);
        Assert.Equal(0.5, window.Scale);
        Assert.Equal([64, 60, 64, 60], window.Slice);
        Assert.Null(window.Hotspot);
        Assert.Equal([32, 32], assets["ui.cursor.grab.v2"].Display!.Hotspot);
        Assert.Null(assets["ui.icon.menu.map.v2"].Display);
    }

    [Fact]
    public void RefusesADisplayFieldItDoesNotKnow()
    {
        byte[] manifest = Encoding.UTF8.GetBytes("""
            {"assets": [{"id": "ui.stone.window.v2", "file": "a.png", "sourceFile": "a.png", "generator": "g", "prompt": "p",
              "display": {"scale": 0.5, "nineSlice": [1, 1, 1, 1]}}]}
            """);
        Assert.Throws<FormatException>(() => AuthoredUiAssetSet.Read(manifest, _ => [1]));
    }
}
