using Xunit;

namespace WorldRpg.SpriteWorkbench.Tests;

public sealed class SpriteWorkbenchUiBoundaryTests
{
    [Fact]
    public void Browser_module_uses_the_product_host_contract_without_render_or_clock_authority()
    {
        string source = File.ReadAllText(Path.GetFullPath("../../../../../src/sprite-ui/workbench.ts", AppContext.BaseDirectory));

        Assert.Contains("export function mountProductUi", source, StringComparison.Ordinal);
        Assert.Contains("worldrpg.sprite-workbench.intent.v1", source, StringComparison.Ordinal);
        Assert.DoesNotContain("mountSpriteWorkbench", source, StringComparison.Ordinal);
        Assert.DoesNotContain("requestAnimationFrame", source, StringComparison.Ordinal);
        Assert.DoesNotContain("setInterval", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new Image", source, StringComparison.Ordinal);
        Assert.DoesNotContain("canvas", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Bundle_generation_uses_the_engine_owned_local_runtime_route_asset()
    {
        string source = File.ReadAllText(Path.GetFullPath("../../../../../src/scripts/build-sprite-workbench-ui.mjs", AppContext.BaseDirectory));

        Assert.Contains("PRODUCT_BROWSER_LOCAL_RUNTIME_BASE_PATH", source, StringComparison.Ordinal);
        Assert.Contains("runtime-adapter.js", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"/__rusty/product/runtime/\"", source, StringComparison.Ordinal);
    }
}
