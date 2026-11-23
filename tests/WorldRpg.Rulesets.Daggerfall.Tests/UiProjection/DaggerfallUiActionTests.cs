using System.Text;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallUiActionTests
{
    [Theory]
    [InlineData("{\"action\":\"teleport-select\",\"revision\":\"cast.1\",\"key\":\"anchor\"}", true)]
    [InlineData("{\"action\":\"teleport-select\",\"revision\":\"cast.1\",\"key\":\"recall\"}", true)]
    [InlineData("{\"action\":\"teleport-select\",\"revision\":\"cast.1\",\"key\":\"cancel\"}", true)]
    [InlineData("{\"action\":\"teleport-select\",\"key\":\"anchor\"}", false)]
    [InlineData("{\"action\":\"teleport-select\",\"revision\":\"cast.1\",\"key\":\"other\"}", false)]
    [InlineData("{\"action\":\"teleport-select\",\"revision\":\"cast.1\",\"key\":\"anchor\",\"profile\":\"fake\"}", false)]
    public void Teleport_choice_uses_only_the_current_paid_request_and_named_choice(string json, bool accepted)
        => Assert.Equal(accepted, DaggerfallUiAction.Parse(Encoding.UTF8.GetBytes(json)) is not null);

    [Theory]
    [InlineData("{\"action\":\"attack\"}", "attack")]
    [InlineData("{\"action\":\"loot\"}", "loot")]
    // The DOM owns its panels, so opening one is not a product action.
    [InlineData("{\"action\":\"inventory\"}", null)]
    [InlineData("{\"action\":\"character\"}", null)]
    [InlineData("{\"action\":\"save-game\"}", "save-game")]
    [InlineData("{\"action\":\"load-game\"}", "load-game")]
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
    [InlineData("{\"action\":\"inventory-use\",\"revision\":\"1:2\",\"item\":\"unique:1002\"}", true)]
    [InlineData("{\"action\":\"inventory-use\",\"revision\":\"1:2\",\"item\":\"unique:1002\",\"amount\":1}", false)]
    public void Inventory_drop_requires_a_revision_identity_and_exactly_one_typed_target(string json, bool accepted)
        => Assert.Equal(accepted, DaggerfallUiAction.Parse(Encoding.UTF8.GetBytes(json)) is not null);

    [Theory]
    [InlineData("{\"action\":\"notebook-page\",\"revision\":\"3\",\"page\":1}", true)]
    [InlineData("{\"action\":\"notebook-page\",\"revision\":\"3\",\"page\":-1}", false)]
    [InlineData("{\"action\":\"notebook-add\",\"revision\":\"3\",\"text\":\"A durable note.\"}", true)]
    [InlineData("{\"action\":\"notebook-add\",\"revision\":\"3\",\"text\":\" \"}", false)]
    [InlineData("{\"action\":\"notebook-edit\",\"revision\":\"3\",\"note\":\"note:1\",\"text\":\"Edited.\"}", true)]
    [InlineData("{\"action\":\"notebook-remove\",\"revision\":\"3\",\"note\":\"note:1\"}", true)]
    [InlineData("{\"action\":\"notebook-move\",\"revision\":\"3\",\"note\":\"note:1\",\"destination\":1}", true)]
    [InlineData("{\"action\":\"notebook-move\",\"revision\":\"3\",\"note\":\"note:1\",\"destination\":-1}", false)]
    [InlineData("{\"action\":\"notebook-remove\",\"revision\":\"3\",\"note\":\"note:1\",\"item\":\"stack:book\"}", false)]
    public void Notebook_actions_are_exact_guarded_semantic_claims(string json, bool accepted)
        => Assert.Equal(accepted, DaggerfallUiAction.Parse(Encoding.UTF8.GetBytes(json)) is not null);
    [Theory]
    [InlineData("{\"action\":\"loot-take\",\"container\":\"2000:1\",\"revision\":\"2000:1:8\",\"item\":\"stack:gold-piece\"}", true)]
    [InlineData("{\"action\":\"loot-take\",\"container\":\"2000:1\",\"revision\":\"2000:1:8\",\"item\":\"stack:gold-piece\",\"amount\":5}", true)]
    [InlineData("{\"action\":\"loot-take\",\"container\":\"2000:1\",\"revision\":\"2000:1:8\",\"item\":\"stack:gold-piece\",\"amount\":0}", false)]
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
    [InlineData("{\"action\":\"activation-mode\",\"mode\":\"lockpick\"}", "lockpick")]
    [InlineData("{\"action\":\"activation-mode\",\"mode\":\"bash\"}", "bash")]
    [InlineData("{\"action\":\"activation-mode\",\"mode\":\"attack\"}", null)]
    [InlineData("{\"action\":\"activation-mode\"}", null)]
    [InlineData("{\"action\":\"activation-mode\",\"mode\":\"grab\",\"item\":\"x\"}", null)]
    public void Activation_mode_actions_are_exact_and_typed(string json, string? expected)
        => Assert.Equal(expected, DaggerfallUiAction.Parse(Encoding.UTF8.GetBytes(json))?.Mode);

    [Theory]
    [InlineData("{\"action\":\"character-quick-start\",\"name\":\"\",\"race\":\"breton\",\"gender\":\"male\",\"faceIndex\":0,\"reflexes\":2,\"career\":\"class00\"}", true)]
    [InlineData("{\"action\":\"character-quick-start\",\"name\":\"Aubk-i\",\"race\":\"breton\",\"gender\":\"male\",\"faceIndex\":0,\"reflexes\":2,\"career\":\"class00\"}", true)]
    [InlineData("{\"action\":\"character-quick-start\",\"name\":\"\",\"race\":\"breton\",\"gender\":\"male\",\"faceIndex\":0,\"reflexes\":2,\"career\":\"class00\",\"backgroundAnswers\":\"\",\"attributeAllocations\":\"\",\"skillAllocations\":\"\"}", false)]
    [InlineData("{\"action\":\"character-quick-start\",\"name\":\"\",\"gender\":\"male\",\"faceIndex\":0,\"reflexes\":2,\"career\":\"class00\"}", false)]
    public void Quick_start_carries_the_draft_identity_and_career_and_no_background(string json, bool accepted)
        => Assert.Equal(accepted, DaggerfallUiAction.Parse(Encoding.UTF8.GetBytes(json)) is not null);

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
    [InlineData("{\"action\":\"death-new-game\"}", true)]
    [InlineData("{\"action\":\"death-quit\"}", true)]
    [InlineData("{\"action\":\"death-load-game\",\"key\":\"slot-1\"}", true)]
    [InlineData("{\"action\":\"death-load-game\"}", false)]
    [InlineData("{\"action\":\"death-load-game\",\"key\":\"  \"}", false)]
    [InlineData("{\"action\":\"death-new-game\",\"key\":\"slot-1\"}", false)]
    [InlineData("{\"action\":\"quit-game\"}", true)]
    [InlineData("{\"action\":\"quit-game\",\"confirm\":true}", false)]
    public void Death_outcome_actions_require_the_exact_choice_shape(string json, bool accepted)
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
    [InlineData("{\"action\":\"currency-deposit-gold\",\"amount\":25}", true)]
    [InlineData("{\"action\":\"currency-withdraw-letter\",\"amount\":100}", true)]
    [InlineData("{\"action\":\"currency-withdraw-gold\",\"amount\":0}", false)]
    [InlineData("{\"action\":\"currency-deposit-gold\"}", false)]
    [InlineData("{\"action\":\"currency-deposit-letters\"}", true)]
    [InlineData("{\"action\":\"currency-deposit-letters\",\"amount\":1}", false)]
    [InlineData("{\"action\":\"bank-transfer\",\"amount\":250,\"destination\":4}", true)]
    [InlineData("{\"action\":\"bank-transfer\",\"amount\":0,\"destination\":4}", false)]
    [InlineData("{\"action\":\"bank-transfer\",\"amount\":250}", false)]
    [InlineData("{\"action\":\"bank-transfer\",\"amount\":250,\"destination\":-1}", false)]
    public void Currency_actions_admit_only_explicit_nonzero_amounts(string json, bool accepted) =>
        Assert.Equal(accepted, DaggerfallUiAction.Parse(Encoding.UTF8.GetBytes(json)) is not null);

    [Theory]
    [InlineData("{\"action\":\"cinematic-skip\"}", true)]
    [InlineData("{\"action\":\"map-note-add\",\"text\":\"Lever room\"}", true)]
    [InlineData("{\"action\":\"map-note-add\",\"text\":\" \"}", false)]
    [InlineData("{\"action\":\"map-note-edit\",\"note\":\"note-1\",\"text\":\"Pulled\"}", true)]
    [InlineData("{\"action\":\"map-note-remove\",\"note\":\"note-1\"}", true)]
    [InlineData("{\"action\":\"map-note-remove\",\"note\":\"note-1\",\"text\":\"x\"}", false)]
    [InlineData("{\"action\":\"cinematic-skip\",\"item\":\"unexpected\"}", false)]
    public void Cinematic_skip_is_a_small_semantic_action(string json, bool accepted) =>
        Assert.Equal(accepted, DaggerfallUiAction.Parse(Encoding.UTF8.GetBytes(json)) is not null);

    [Theory]
    [InlineData("{\"action\":\"quest-choice\",\"questInstance\":\"quest:1\",\"questMessage\":1010,\"questChoice\":3,\"questPrompt\":\"prompt:1\"}", true)]
    [InlineData("{\"action\":\"quest-choice\",\"questInstance\":\"quest:1\",\"questMessage\":0,\"questChoice\":3,\"questPrompt\":\"prompt:1\"}", false)]
    [InlineData("{\"action\":\"quest-choice\",\"questInstance\":\"quest:1\",\"questMessage\":1010}", false)]
    [InlineData("{\"action\":\"quest-choice\",\"questInstance\":\"quest:1\",\"questMessage\":1010,\"questChoice\":3,\"questPrompt\":\"prompt:1\",\"item\":\"x\"}", false)]
    public void Quest_choice_actions_name_the_pending_prompt_once(string json, bool accepted) =>
        Assert.Equal(accepted, DaggerfallUiAction.Parse(Encoding.UTF8.GetBytes(json)) is not null);

    [Theory]
    [InlineData("{\"action\":\"quest-dismiss\",\"questInstance\":\"quest:1\",\"questDelivery\":\"quest-message:2\"}", true)]
    [InlineData("{\"action\":\"quest-dismiss\",\"questInstance\":\"quest:1\"}", false)]
    [InlineData("{\"action\":\"quest-dismiss\",\"questInstance\":\"quest:1\",\"questDelivery\":\" \"}", false)]
    [InlineData("{\"action\":\"quest-dismiss\",\"questInstance\":\"quest:1\",\"questDelivery\":\"quest-message:2\",\"text\":\"Same text\"}", false)]
    public void Quest_dismissal_names_a_delivery_instead_of_its_rendered_text(string json, bool accepted) =>
        Assert.Equal(accepted, DaggerfallUiAction.Parse(Encoding.UTF8.GetBytes(json)) is not null);

    [Theory]
    [InlineData("{\"action\":\"dialogue-tone\",\"revision\":\"3\",\"tone\":\"polite\"}", true)]
    [InlineData("{\"action\":\"dialogue-tone\",\"revision\":\"3\",\"tone\":\"shout\"}", false)]
    [InlineData("{\"action\":\"dialogue-tone\",\"tone\":\"polite\"}", false)]
    [InlineData("{\"action\":\"dialogue-tone\",\"revision\":\"3\",\"tone\":\"normal\",\"topic\":\"news\"}", false)]
    [InlineData("{\"action\":\"dialogue-topic\",\"revision\":\"3\",\"topic\":\"directions\"}", true)]
    [InlineData("{\"action\":\"dialogue-topic\",\"revision\":\"3\",\"topic\":\"direction:site:1:2\"}", true)]
    [InlineData("{\"action\":\"dialogue-topic\",\"revision\":\"3\",\"topic\":\"direction:x\"}", true)]
    [InlineData("{\"action\":\"dialogue-topic\",\"revision\":\"3\",\"topic\":\"direction:\"}", false)]
    [InlineData("{\"action\":\"dialogue-topic\",\"revision\":\"3\",\"topic\":\"armor\"}", true)]
    [InlineData("{\"action\":\"dialogue-topic\",\"revision\":\"3\",\"topic\":\"armor\",\"key\":\"102\"}", true)]
    [InlineData("{\"action\":\"dialogue-topic\",\"revision\":\"3\",\"topic\":\"armor\",\"key\":\" \"}", false)]
    [InlineData("{\"action\":\"dialogue-topic\",\"revision\":\"3\",\"topic\":\"rank-review\"}", true)]
    [InlineData("{\"action\":\"dialogue-topic\",\"revision\":\"3\",\"topic\":\"quest\"}", false)]
    [InlineData("{\"action\":\"training-commit\",\"revision\":\"3\",\"key\":\"long-blade\",\"amount\":250,\"confirm\":true}", true)]
    [InlineData("{\"action\":\"training-commit\",\"revision\":\"3\",\"key\":\"long-blade\",\"amount\":0,\"confirm\":true}", false)]
    [InlineData("{\"action\":\"training-commit\",\"revision\":\"3\",\"key\":\"long-blade\",\"amount\":250}", false)]
    [InlineData("{\"action\":\"dialogue-close\",\"revision\":\"3\"}", true)]
    [InlineData("{\"action\":\"dialogue-close\"}", false)]
    public void Dialogue_actions_require_a_live_revision_and_exact_choices(string json, bool accepted) =>
        Assert.Equal(accepted, DaggerfallUiAction.Parse(Encoding.UTF8.GetBytes(json)) is not null);

    [Theory]
    [InlineData("{\"action\":\"merchant-buy\",\"revision\":\"dialogue.1|stock.2\",\"item\":\"stack:gold\",\"amount\":3}", true)]
    [InlineData("{\"action\":\"merchant-buy\",\"revision\":\"dialogue.1|stock.2\",\"item\":\"unique:1002\"}", true)]
    [InlineData("{\"action\":\"merchant-sell\",\"revision\":\"dialogue.1|stock.2\",\"item\":\"stack:book\"}", true)]
    [InlineData("{\"action\":\"merchant-shoplift\",\"revision\":\"dialogue.1|stock.2\",\"item\":\"stack:gem\",\"amount\":1}", true)]
    [InlineData("{\"action\":\"merchant-repair\",\"revision\":\"dialogue.1|stock.2\",\"item\":\"unique:1002\"}", true)]
    [InlineData("{\"action\":\"merchant-identify\",\"revision\":\"dialogue.1|stock.2\",\"item\":\"unique:1002\"}", true)]
    [InlineData("{\"action\":\"merchant-collect-repair\",\"revision\":\"dialogue.1|stock.2\",\"key\":\"merchant:repair:1\"}", true)]
    [InlineData("{\"action\":\"merchant-buy\",\"revision\":\"dialogue.1|stock.2\",\"item\":\"unique:1002\",\"amount\":2}", false)]
    [InlineData("{\"action\":\"merchant-buy\",\"revision\":\"dialogue.1|stock.2\",\"item\":\"stack:gold\",\"amount\":90,\"confirm\":true}", true)]
    [InlineData("{\"action\":\"merchant-shoplift\",\"revision\":\"dialogue.1|stock.2\",\"item\":\"stack:gem\",\"amount\":1,\"confirm\":true}", false)]
    [InlineData("{\"action\":\"merchant-quote\",\"revision\":\"dialogue.1|stock.2\",\"item\":\"stack:gold\",\"key\":\"buy\",\"amount\":90}", true)]
    [InlineData("{\"action\":\"merchant-quote\",\"revision\":\"dialogue.1|stock.2\",\"item\":\"stack:gold\",\"key\":\"steal\",\"amount\":90}", false)]
    [InlineData("{\"action\":\"merchant-repair\",\"revision\":\"dialogue.1|stock.2\"}", false)]
    [InlineData("{\"action\":\"merchant-collect-repair\",\"revision\":\"dialogue.1|stock.2\"}", false)]
    public void Merchant_actions_admit_only_typed_provider_payloads(string json, bool accepted) =>
        Assert.Equal(accepted, DaggerfallUiAction.Parse(Encoding.UTF8.GetBytes(json)) is not null);

    [Theory]
    [InlineData("{\"action\":\"transport-select\",\"mode\":\"horse\"}", true)]
    [InlineData("{\"action\":\"transport-select\",\"mode\":\"ship\"}", false)]
    [InlineData("{\"action\":\"transport-select\"}", false)]
    [InlineData("{\"action\":\"transport-toggle\"}", false)]
    [InlineData("{\"action\":\"wagon-put\",\"revision\":\"1:2\",\"item\":\"stack:gold-piece\",\"amount\":5}", true)]
    [InlineData("{\"action\":\"wagon-take\",\"revision\":\"1:2\",\"item\":\"unique:1002\"}", true)]
    [InlineData("{\"action\":\"wagon-put\",\"item\":\"stack:gold-piece\"}", false)]
    [InlineData("{\"action\":\"wagon-put\",\"revision\":\"1:2\",\"item\":\"stack:gold-piece\",\"amount\":0}", false)]
    public void Transport_and_wagon_actions_require_exact_modes_and_guarded_item_selections(string json, bool accepted) =>
        Assert.Equal(accepted, DaggerfallUiAction.Parse(Encoding.UTF8.GetBytes(json)) is not null);

    [Theory]
    [InlineData("{\"action\":\"rest\",\"mode\":\"timed\",\"hours\":2}", true)]
    [InlineData("{\"action\":\"rest\",\"mode\":\"timed\",\"hours\":0}", true)]
    [InlineData("{\"action\":\"rest\",\"mode\":\"until-healed\"}", true)]
    [InlineData("{\"action\":\"rest\",\"mode\":\"loiter\",\"hours\":3}", true)]
    [InlineData("{\"action\":\"rest\",\"mode\":\"timed\"}", false)]
    [InlineData("{\"action\":\"rest\",\"mode\":\"until-healed\",\"hours\":1}", false)]
    [InlineData("{\"action\":\"rest\",\"mode\":\"loiter\",\"hours\":-1}", false)]
    [InlineData("{\"action\":\"rest\",\"mode\":\"invalid\",\"hours\":1}", false)]
    [InlineData("{\"action\":\"rest\",\"mode\":\"timed\",\"hours\":2,\"item\":\"unexpected\"}", false)]
    public void Rest_actions_require_an_explicit_mode_and_duration_shape(string json, bool accepted) =>
        Assert.Equal(accepted, DaggerfallUiAction.Parse(Encoding.UTF8.GetBytes(json)) is not null);

    [Theory]
    [InlineData("{\"action\":\"expedition-open\"}", true)]
    [InlineData("{\"action\":\"expedition-open\",\"open\":true}", false)]
    [InlineData("{\"action\":\"expedition-go\",\"x\":120,\"y\":240}", true)]
    [InlineData("{\"action\":\"expedition-go\",\"x\":120,\"y\":240,\"region\":17,\"index\":3}", true)]
    [InlineData("{\"action\":\"expedition-go\",\"x\":120,\"y\":240,\"region\":17}", false)]
    [InlineData("{\"action\":\"expedition-go\",\"x\":1000,\"y\":240}", false)]
    [InlineData("{\"action\":\"expedition-go\",\"x\":-1,\"y\":240}", false)]
    [InlineData("{\"action\":\"expedition-pin\"}", true)]
    [InlineData("{\"action\":\"expedition-view\",\"view\":[-15,5,0.5,-0.5,-2]}", true)]
    [InlineData("{\"action\":\"expedition-view\",\"view\":[0,0,0,0]}", false)]
    [InlineData("{\"action\":\"expedition-view\",\"view\":[0,0,9,0,0]}", false)]
    [InlineData("{\"action\":\"expedition-follow\"}", true)]
    [InlineData("{\"action\":\"expedition-look\",\"x\":120,\"y\":240}", true)]
    [InlineData("{\"action\":\"expedition-go-at\",\"point\":[0.25,1]}", true)]
    [InlineData("{\"action\":\"expedition-go-at\",\"point\":[1.5,0.5]}", false)]
    [InlineData("{\"action\":\"expedition-pin-at\",\"point\":[0.5]}", false)]
    [InlineData("{\"action\":\"world-map\",\"open\":false}", true)]
    [InlineData("{\"action\":\"world-map-pin\"}", true)]
    [InlineData("{\"action\":\"world-map-travel\"}", false)]
    [InlineData("{\"action\":\"world-map-travel\",\"x\":120,\"y\":240,\"region\":17,\"index\":3}", true)]
    [InlineData("{\"action\":\"expedition-speed\",\"speed\":2}", true)]
    [InlineData("{\"action\":\"expedition-speed\"}", false)]
    [InlineData("{\"action\":\"expedition-mode\",\"mode\":\"camp\"}", true)]
    [InlineData("{\"action\":\"expedition-mode\",\"mode\":\"gallop\"}", false)]
    [InlineData("{\"action\":\"atlas-tiles\",\"tiles\":[[0,1,1],[2,15,7]]}", true)]
    [InlineData("{\"action\":\"atlas-tiles\",\"tiles\":[]}", false)]
    [InlineData("{\"action\":\"atlas-tiles\",\"tiles\":[[0,1]]}", false)]
    [InlineData("{\"action\":\"atlas-tiles\",\"tiles\":[[0,1,\"x\"]]}", false)]
    [InlineData("{\"action\":\"atlas-tiles\",\"tiles\":[[0,1,1]],\"open\":true}", false)]
    [InlineData("{\"action\":\"content-import\"}", true)]
    [InlineData("{\"action\":\"content-import-cancel\"}", true)]
    [InlineData("{\"action\":\"content-import\",\"key\":\"x\"}", false)]
    public void The_expedition_map_opens_takes_points_speeds_and_modes_and_asks_for_the_atlas_tiles_it_shows(string json, bool accepted) =>
        Assert.Equal(accepted, DaggerfallUiAction.Parse(Encoding.UTF8.GetBytes(json)) is not null);

    [Fact]
    public void Every_action_kind_declares_one_wire_name_and_its_admitted_phases()
    {
        Assert.Equal(Enum.GetValues<DaggerfallUiActionKind>().Order(), DaggerfallUiAction.Rules.Select(rule => rule.Kind).Order());
        Assert.Equal(DaggerfallUiAction.Rules.Count, DaggerfallUiAction.Rules.Select(rule => rule.Wire).Distinct(StringComparer.Ordinal).Count());
        foreach (DaggerfallUiActionRule rule in DaggerfallUiAction.Rules)
        {
            Assert.Equal(rule.Kind, DaggerfallUiAction.KindOf(rule.Wire));
            Assert.NotEqual(DaggerfallUiPhases.None, rule.Phases);
        }
    }

    private const string Draft = "\"name\":\"Aubk-i\",\"race\":\"breton\",\"gender\":\"male\",\"faceIndex\":0,\"reflexes\":2,\"career\":\"class00\"";

    /// <summary>A valid payload's fields besides its action, for each kind that carries any; every other kind carries only its action.</summary>
    private static readonly Dictionary<DaggerfallUiActionKind, string> FieldsOf = new()
    {
        [DaggerfallUiActionKind.ArtRequest] = "\"revision\":\"2a1b\"",
        [DaggerfallUiActionKind.ControlsRebind] = "\"item\":\"jump\",\"key\":\"Space\"",
        [DaggerfallUiActionKind.ControlsRebindButton] = "\"item\":\"jump\",\"amount\":1",
        [DaggerfallUiActionKind.ControlsVolume] = "\"item\":\"music\",\"scale\":0.5",
        [DaggerfallUiActionKind.ControlsLook] = "\"sensitivity\":1.5,\"invert\":false",
        [DaggerfallUiActionKind.ControlsUiScale] = "\"scale\":1.25",
        [DaggerfallUiActionKind.ControlsGroundCover] = "\"enabled\":true",
        [DaggerfallUiActionKind.ControlsClutterDistance] = "\"scale\":2",
        [DaggerfallUiActionKind.ControlsLampShadows] = "\"item\":\"big\",\"scale\":0.5,\"enabled\":true",
        [DaggerfallUiActionKind.ControlsSurfaceRelief] = "\"scale\":0.6",
        [DaggerfallUiActionKind.ControlsReliefDistance] = "\"scale\":1.5",
        [DaggerfallUiActionKind.ActionDrivenTime] = "\"enabled\":false",
        [DaggerfallUiActionKind.CharacterClassQuestions] = Draft,
        [DaggerfallUiActionKind.CharacterClassAnswer] = "\"question\":1,\"answer\":2",
        [DaggerfallUiActionKind.CharacterUpdate] = Draft,
        [DaggerfallUiActionKind.CharacterBackgroundReroll] = Draft,
        [DaggerfallUiActionKind.CharacterCommit] = Draft,
        [DaggerfallUiActionKind.CharacterQuickStart] = Draft,
        [DaggerfallUiActionKind.CharacterLevelAllocate] = "\"attribute\":\"strength\"",
        [DaggerfallUiActionKind.ActivationMode] = "\"mode\":\"grab\"",
        [DaggerfallUiActionKind.SpellReady] = "\"key\":\"spell:1\"",
        [DaggerfallUiActionKind.DaedraSummon] = "\"revision\":\"1\",\"key\":\"daedra:1\",\"amount\":200,\"confirm\":true",
        [DaggerfallUiActionKind.DaedraAnswer] = "\"revision\":\"1\",\"confirm\":true",
        [DaggerfallUiActionKind.ItemMakerDraft] = "\"revision\":\"1\",\"text\":\"{}\"",
        [DaggerfallUiActionKind.ItemMakerBuy] = "\"key\":\"draft:1\",\"revision\":\"1\",\"amount\":90,\"confirm\":false",
        [DaggerfallUiActionKind.PotionMix] = "\"revision\":\"1\",\"text\":\"{}\"",
        [DaggerfallUiActionKind.SpellMakerDraft] = "\"revision\":\"1\",\"text\":\"{}\"",
        [DaggerfallUiActionKind.SpellMakerBuy] = "\"key\":\"draft:1\",\"revision\":\"1\",\"amount\":90,\"confirm\":false",
        [DaggerfallUiActionKind.SpellBuy] = "\"key\":\"spell:1\",\"revision\":\"1\",\"amount\":90,\"confirm\":false",
        [DaggerfallUiActionKind.SpellDelete] = "\"key\":\"spell:1\",\"confirm\":true",
        [DaggerfallUiActionKind.SpellSwap] = "\"key\":\"spell:1\",\"item\":\"spell:2\"",
        [DaggerfallUiActionKind.SpellInfo] = "\"key\":\"spell:1\"",
        [DaggerfallUiActionKind.TutorialContinue] = "\"page\":1",
        [DaggerfallUiActionKind.Menu] = "\"open\":true",
        [DaggerfallUiActionKind.IdentifySelect] = "\"revision\":\"1\",\"key\":\"unique:1002\"",
        [DaggerfallUiActionKind.IdentifyCancel] = "\"revision\":\"1\"",
        [DaggerfallUiActionKind.LegalChoice] = "\"revision\":\"1\",\"key\":\"yield\"",
        [DaggerfallUiActionKind.CreateItemSelect] = "\"revision\":\"1\",\"key\":\"item:1\"",
        [DaggerfallUiActionKind.CreateItemCancel] = "\"revision\":\"1\"",
        [DaggerfallUiActionKind.DispelSelect] = "\"revision\":\"1\",\"key\":\"effect:1\"",
        [DaggerfallUiActionKind.DispelCancel] = "\"revision\":\"1\"",
        [DaggerfallUiActionKind.TeleportSelect] = "\"revision\":\"1\",\"key\":\"anchor\"",
        [DaggerfallUiActionKind.DialogueTone] = "\"revision\":\"1\",\"tone\":\"polite\"",
        [DaggerfallUiActionKind.DialogueTopic] = "\"revision\":\"1\",\"topic\":\"news\"",
        [DaggerfallUiActionKind.DialogueClose] = "\"revision\":\"1\"",
        [DaggerfallUiActionKind.TavernBuy] = "\"revision\":\"1\",\"key\":\"ale\",\"amount\":1",
        [DaggerfallUiActionKind.TrainingCommit] = "\"revision\":\"1\",\"key\":\"long-blade\",\"amount\":250,\"confirm\":true",
        [DaggerfallUiActionKind.MerchantBuy] = "\"revision\":\"1\",\"item\":\"stack:gold\"",
        [DaggerfallUiActionKind.MerchantSell] = "\"revision\":\"1\",\"item\":\"stack:book\"",
        [DaggerfallUiActionKind.MerchantRepair] = "\"revision\":\"1\",\"item\":\"unique:1002\"",
        [DaggerfallUiActionKind.MerchantCollectRepair] = "\"revision\":\"1\",\"key\":\"merchant:repair:1\"",
        [DaggerfallUiActionKind.MerchantIdentify] = "\"revision\":\"1\",\"item\":\"unique:1002\"",
        [DaggerfallUiActionKind.MerchantShoplift] = "\"revision\":\"1\",\"item\":\"stack:gem\"",
        [DaggerfallUiActionKind.MerchantQuote] = "\"revision\":\"1\",\"item\":\"stack:gold\",\"key\":\"buy\",\"amount\":90",
        [DaggerfallUiActionKind.TransportSelect] = "\"mode\":\"foot\"",
        [DaggerfallUiActionKind.PropertyBuy] = "\"key\":\"house:1\"",
        [DaggerfallUiActionKind.PropertySell] = "\"key\":\"house:1\"",
        [DaggerfallUiActionKind.PropertyEnter] = "\"key\":\"house:1\"",
        [DaggerfallUiActionKind.PropertyPut] = "\"key\":\"house:1\",\"item\":\"unique:1002\",\"revision\":\"1\"",
        [DaggerfallUiActionKind.PropertyTake] = "\"key\":\"house:1\",\"item\":\"unique:1002\",\"revision\":\"1\"",
        [DaggerfallUiActionKind.MapOpen] = "\"open\":true",
        [DaggerfallUiActionKind.MapBuilding] = "\"region\":17,\"destination\":3,\"item\":\"building:1\"",
        [DaggerfallUiActionKind.MapNoteAdd] = "\"text\":\"Lever room\"",
        [DaggerfallUiActionKind.MapNoteEdit] = "\"note\":\"note-1\",\"text\":\"Pulled\"",
        [DaggerfallUiActionKind.MapNoteRemove] = "\"note\":\"note-1\"",
        [DaggerfallUiActionKind.ExpeditionFind] = "\"text\":\"Daggerfall\"",
        [DaggerfallUiActionKind.ExpeditionSpeed] = "\"speed\":1",
        [DaggerfallUiActionKind.ExpeditionMode] = "\"mode\":\"reckless\"",
        [DaggerfallUiActionKind.ExpeditionGo] = "\"x\":120,\"y\":240,\"region\":17,\"index\":3",
        [DaggerfallUiActionKind.ExpeditionPin] = "\"x\":120,\"y\":240",
        [DaggerfallUiActionKind.ExpeditionView] = "\"view\":[15,0,0.25,0,1]",
        [DaggerfallUiActionKind.ExpeditionLook] = "\"x\":120,\"y\":240",
        [DaggerfallUiActionKind.ExpeditionGoAt] = "\"point\":[0.5,0.25]",
        [DaggerfallUiActionKind.ExpeditionPinAt] = "\"point\":[0.5,0.25]",
        [DaggerfallUiActionKind.WorldMap] = "\"open\":true",
        [DaggerfallUiActionKind.WorldMapFind] = "\"text\":\"Daggerfall\"",
        [DaggerfallUiActionKind.WorldMapPin] = "\"x\":120,\"y\":240,\"region\":17,\"index\":3",
        [DaggerfallUiActionKind.WorldMapTravel] = "\"x\":120,\"y\":240",
        [DaggerfallUiActionKind.AtlasTiles] = "\"tiles\":[[0,1,1]]",
        [DaggerfallUiActionKind.LodgingQuote] = "\"key\":\"room:1\",\"days\":2",
        [DaggerfallUiActionKind.LodgingBook] = "\"key\":\"room:1\",\"days\":2,\"amount\":0",
        [DaggerfallUiActionKind.Rest] = "\"mode\":\"until-healed\"",
        [DaggerfallUiActionKind.WagonPut] = "\"revision\":\"1\",\"item\":\"stack:gold-piece\"",
        [DaggerfallUiActionKind.WagonTake] = "\"revision\":\"1\",\"item\":\"unique:1002\"",
        [DaggerfallUiActionKind.QuestChoice] = "\"questInstance\":\"quest:1\",\"questMessage\":1010,\"questChoice\":0,\"questPrompt\":\"prompt:1\"",
        [DaggerfallUiActionKind.QuestOfferAnswer] = "\"questInstance\":\"quest:1\",\"confirm\":true",
        [DaggerfallUiActionKind.QuestDismiss] = "\"questInstance\":\"quest:1\",\"questDelivery\":\"quest-message:2\"",
        [DaggerfallUiActionKind.DungeonTextAnswer] = "\"revision\":\"1\",\"item\":\"riddle:1\",\"text\":\"a shadow\"",
        [DaggerfallUiActionKind.DungeonTextClose] = "\"revision\":\"1\",\"item\":\"riddle:1\"",
        [DaggerfallUiActionKind.InventoryMove] = "\"revision\":\"1:2\",\"item\":\"unique:1002\",\"targetGrid\":3",
        [DaggerfallUiActionKind.InventoryInspect] = "\"revision\":\"1:2\",\"item\":\"unique:1002\"",
        [DaggerfallUiActionKind.InventoryUse] = "\"revision\":\"1:2\",\"item\":\"unique:1002\"",
        [DaggerfallUiActionKind.InventoryDrop] = "\"revision\":\"1:2\",\"item\":\"stack:gold-piece\",\"amount\":5",
        [DaggerfallUiActionKind.NotebookPage] = "\"revision\":\"3\",\"page\":1",
        [DaggerfallUiActionKind.NotebookAdd] = "\"revision\":\"3\",\"text\":\"A durable note.\"",
        [DaggerfallUiActionKind.NotebookEdit] = "\"revision\":\"3\",\"note\":\"note:1\",\"text\":\"Edited.\"",
        [DaggerfallUiActionKind.NotebookRemove] = "\"revision\":\"3\",\"note\":\"note:1\"",
        [DaggerfallUiActionKind.NotebookMove] = "\"revision\":\"3\",\"note\":\"note:1\",\"destination\":1",
        [DaggerfallUiActionKind.CurrencyDepositGold] = "\"amount\":25",
        [DaggerfallUiActionKind.CurrencyWithdrawGold] = "\"amount\":25",
        [DaggerfallUiActionKind.CurrencyWithdrawLetter] = "\"amount\":100",
        [DaggerfallUiActionKind.BankTransfer] = "\"amount\":250,\"destination\":4",
        [DaggerfallUiActionKind.BankOpen] = "\"revision\":\"1\"",
        [DaggerfallUiActionKind.BankLoanIssue] = "\"amount\":100",
        [DaggerfallUiActionKind.BankLoanRepayAccount] = "\"amount\":100",
        [DaggerfallUiActionKind.BankLoanRepayCarried] = "\"amount\":100",
        [DaggerfallUiActionKind.LootClose] = "\"container\":\"2000:1\"",
        [DaggerfallUiActionKind.LootTake] = "\"container\":\"2000:1\",\"revision\":\"2000:1:8\",\"item\":\"stack:gold-piece\"",
        [DaggerfallUiActionKind.LootTakeAll] = "\"container\":\"2000:1\",\"revision\":\"2000:1:8\"",
        [DaggerfallUiActionKind.SaveSlot] = "\"label\":\"Before the dungeon\"",
        [DaggerfallUiActionKind.LoadSlot] = "\"key\":\"slot-1\"",
        [DaggerfallUiActionKind.BundleSelect] = "\"key\":\"bundle:1\"",
        [DaggerfallUiActionKind.DeleteSlot] = "\"key\":\"slot-1\"",
        [DaggerfallUiActionKind.DeathLoadGame] = "\"key\":\"slot-1\"",
        [DaggerfallUiActionKind.InputClaimed] = "\"item\":\"attack\",\"key\":\"developer tools\"",
    };

    /// <summary>
    /// The wire is named only by its rule, so every rule's action must reach the session: sent with a
    /// payload of its kind's shape, each parses as its own kind.
    /// </summary>
    [Fact]
    public void Every_rule_s_wire_parses_as_its_kind_with_a_payload_of_its_shape()
    {
        List<string> unreachable = [];
        foreach (DaggerfallUiActionRule rule in DaggerfallUiAction.Rules)
        {
            string json = FieldsOf.TryGetValue(rule.Kind, out string? fields)
                ? $"{{\"action\":\"{rule.Wire}\",{fields}}}"
                : $"{{\"action\":\"{rule.Wire}\"}}";
            if (DaggerfallUiAction.Parse(Encoding.UTF8.GetBytes(json))?.Kind != rule.Kind) unreachable.Add($"{rule.Kind}: {json}");
        }
        Assert.Empty(unreachable);
    }

    /// <summary>A wire no rule names is not an action: it is refused, never thrown out of the parse.</summary>
    [Theory]
    [InlineData("{\"action\":\"spellinfo\",\"key\":\"spell:1\"}")]
    [InlineData("{\"action\":\"Spell-Info\",\"key\":\"spell:1\"}")]
    [InlineData("{\"action\":\"spell-info \",\"key\":\"spell:1\"}")]
    [InlineData("{\"key\":\"spell:1\"}")]
    public void A_wire_no_rule_names_is_refused_without_an_exception(string json) =>
        Assert.Null(DaggerfallUiAction.Parse(Encoding.UTF8.GetBytes(json)));
}
