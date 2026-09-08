using System.Text;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallUiActionTests
{
    [Theory]
    [InlineData("{\"action\":\"attack\"}", "attack")]
    [InlineData("{\"action\":\"loot\"}", "loot")]
    [InlineData("{\"action\":\"inventory\"}", "inventory")]
    [InlineData("{\"action\":\"character\"}", "character")]
    [InlineData("{\"action\":\"attack\",\"action\":\"loot\"}", null)]
    [InlineData("{\"action\":\"attack\",\"item\":1}", null)]
    [InlineData("{\"action\":\"transfer-all\"}", null)]
    [InlineData("{\"action\":false}", null)]
    [InlineData("[]", null)]
    [InlineData("{", null)]
    public void Player_action_contract_rejects_ambiguous_or_unrecognized_claims(string json, string? expected)
        => Assert.Equal(expected, DaggerfallUiAction.Parse(Encoding.UTF8.GetBytes(json))?.Action);
    [Theory]
    [InlineData("{\"action\":\"inventory-move\",\"revision\":\"1:2\",\"item\":\"unique:1002\",\"targetGrid\":3}", true)]
    [InlineData("{\"action\":\"inventory-move\",\"revision\":\"1:2\",\"item\":\"unique:1002\",\"targetEquipment\":\"head\"}", true)]
    [InlineData("{\"action\":\"inventory-move\",\"revision\":\"1:2\",\"item\":\"unique:1002\",\"targetGrid\":3,\"targetEquipment\":\"head\"}", false)]
    [InlineData("{\"action\":\"inventory-move\",\"item\":\"unique:1002\",\"targetGrid\":3}", false)]
    [InlineData("{\"action\":\"inventory-move\",\"revision\":\"1:2\",\"item\":\"unique:1002\",\"targetGrid\":\"3\"}", false)]
    public void Inventory_drop_requires_a_revision_identity_and_exactly_one_typed_target(string json, bool accepted)
        => Assert.Equal(accepted, DaggerfallUiAction.Parse(Encoding.UTF8.GetBytes(json)) is not null);
}
