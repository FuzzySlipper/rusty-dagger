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
    public void Product_ui_compilation_runs_before_sdk_composition_without_local_runtime_assets()
    {
        string source = File.ReadAllText(Path.GetFullPath("../../../../../src/WorldRpg.SpriteWorkbench/WorldRpg.SpriteWorkbench.csproj", AppContext.BaseDirectory));

        Assert.Contains("<RustyEngineProductUiRoot>", source, StringComparison.Ordinal);
        Assert.Contains("BeforeTargets=\"GenerateRustyEngineProductComposition\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("runtime-adapter", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("browser-bundle", source, StringComparison.OrdinalIgnoreCase);
    }
}
