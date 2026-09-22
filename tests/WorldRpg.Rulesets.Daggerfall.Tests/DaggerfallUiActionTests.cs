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
    [Theory]
    [InlineData("{\"action\":\"loot-take\",\"container\":\"2000:1\",\"revision\":\"2000:1:8\",\"item\":\"stack:gold-piece\"}", true)]
    [InlineData("{\"action\":\"loot-take\",\"revision\":\"8\",\"item\":\"stack:gold-piece\"}", false)]
    [InlineData("{\"action\":\"loot-take\",\"container\":\"2000:1\",\"revision\":\"8\",\"item\":\"stack:gold-piece\",\"quantity\":5}", false)]
    [InlineData("{\"action\":\"loot-close\",\"container\":\"2000:1\"}", true)]
    [InlineData("{\"action\":\"loot-close\"}", false)]
    public void Loot_actions_require_the_open_container_and_selected_revision(string json, bool accepted)
        => Assert.Equal(accepted, DaggerfallUiAction.Parse(Encoding.UTF8.GetBytes(json)) is not null);
    [Theory]
    [InlineData("{\"action\":\"art-request\",\"revision\":\"2a1b\"}", true)]
    [InlineData("{\"action\":\"art-request\"}", false)]
    [InlineData("{\"action\":\"art-request\",\"revision\":\"  \"}", false)]
    [InlineData("{\"action\":\"art-request\",\"revision\":\"2a1b\",\"item\":\"unique:1002\"}", false)]
    [InlineData("{\"action\":\"art-request\",\"revision\":3}", false)]
    public void Art_requests_name_the_revision_the_dom_is_missing(string json, bool accepted)
        => Assert.Equal(accepted, DaggerfallUiAction.Parse(Encoding.UTF8.GetBytes(json)) is not null);

    [Theory]
    [InlineData("{\"action\":\"activation-mode\",\"mode\":\"grab\"}", "grab")]
    [InlineData("{\"action\":\"activation-mode\",\"mode\":\"info\"}", "info")]
    [InlineData("{\"action\":\"activation-mode\",\"mode\":\"talk\"}", "talk")]
    [InlineData("{\"action\":\"activation-mode\",\"mode\":\"steal\"}", "steal")]
    [InlineData("{\"action\":\"activation-mode\",\"mode\":\"bash\"}", "bash")]
    [InlineData("{\"action\":\"activation-mode\",\"mode\":\"attack\"}", null)]
    [InlineData("{\"action\":\"activation-mode\"}", null)]
    [InlineData("{\"action\":\"activation-mode\",\"mode\":\"grab\",\"item\":\"x\"}", null)]
    public void Activation_mode_actions_are_exact_and_typed(string json, string? expected)
        => Assert.Equal(expected, DaggerfallUiAction.Parse(Encoding.UTF8.GetBytes(json))?.Mode);

    [Theory]
    [InlineData("{\"action\":\"save-slots\"}", true)]
    [InlineData("{\"action\":\"save-slot\",\"label\":\"Before the dungeon\"}", true)]
    [InlineData("{\"action\":\"save-slot\",\"key\":\"slot-1\",\"label\":\"Before the dungeon\",\"confirm\":true}", true)]
    [InlineData("{\"action\":\"save-slot\",\"label\":\"  \"}", false)]
    [InlineData("{\"action\":\"load-slot\",\"key\":\"slot-1\"}", true)]
    [InlineData("{\"action\":\"load-slot\"}", false)]
    [InlineData("{\"action\":\"delete-slot\",\"key\":\"slot-1\",\"confirm\":true}", true)]
    [InlineData("{\"action\":\"delete-slot\",\"key\":\"slot-1\",\"confirm\":\"yes\"}", false)]
    public void Save_slot_actions_are_small_and_exact(string json, bool accepted)
        => Assert.Equal(accepted, DaggerfallUiAction.Parse(Encoding.UTF8.GetBytes(json)) is not null);

    [Theory]
    [InlineData("{\"action\":\"character-level-allocate\",\"attribute\":\"strength\"}", "strength")]
    [InlineData("{\"action\":\"character-level-allocate\"}", null)]
    [InlineData("{\"action\":\"character-level-allocate\",\"attribute\":\"strength\",\"career\":\"class00\"}", null)]
    public void Level_up_allocate_actions_are_exact_and_typed(string json, string? attribute)
    {
        DaggerfallPlayerUiAction? action = DaggerfallUiAction.Parse(Encoding.UTF8.GetBytes(json));
        Assert.Equal(attribute, action?.Attribute);
    }

    [Theory]
    [InlineData("{\"action\":\"character-level-commit\"}", true)]
    [InlineData("{\"action\":\"character-level-commit\",\"attribute\":\"strength\"}", false)]
    public void Level_up_commit_action_has_no_untyped_fields(string json, bool accepted) =>
        Assert.Equal(accepted, DaggerfallUiAction.Parse(Encoding.UTF8.GetBytes(json)) is not null);

    [Theory]
    [InlineData("{\"action\":\"cinematic-skip\"}", true)]
    [InlineData("{\"action\":\"cinematic-skip\",\"item\":\"unexpected\"}", false)]
    public void Cinematic_skip_is_a_small_semantic_action(string json, bool accepted) =>
        Assert.Equal(accepted, DaggerfallUiAction.Parse(Encoding.UTF8.GetBytes(json)) is not null);
}
