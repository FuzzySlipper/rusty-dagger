using System.Text.Json;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The session's classic held-item presentation: the base payload names bare hands' art and the viewmodel's render
/// order, and each weapon draws the art its template names. What each item draws is in <see cref="PlayerViewmodelTests"/>.
/// </summary>
public sealed class DaggerfallClassicSelectionTests
{
    [Theory]
    // The dagger's template naming an effect sprite.
    [InlineData(113, "effect.blood.0", "Weapon 'iron-dagger' draws no classic weapon art: its template 113 names 'effect.blood.0', which is not admitted weaponSprite media.")]
    // The club's template naming no first-person art.
    [InlineData(288, "", "Weapon 'iron-club' draws no classic weapon art: its template 288 names none.")]
    public void A_weapon_whose_template_names_no_classic_weapon_art_is_refused_at_load(int template, string weaponMediaId, string diagnostic)
    {
        System.Text.Json.Nodes.JsonObject payload = TestPayload.Sections("itemTemplates");
        payload["itemTemplates"]!["templates"]!.AsArray().Single(row => row!["index"]!.GetValue<int>() == template)!["weaponMediaId"] = weaponMediaId;
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(TestPayload.Splice(payload));

        DaggerfallContentException refused = Assert.Throws<DaggerfallContentException>(() =>
            NormalizedMediaValidationTests.Read(FullContent(TestData.RepositoryRoot), definitions));
        Assert.Contains(diagnostic, refused.Diagnostics);
    }

    [Fact]
    public void Bare_hands_whose_art_is_no_admitted_weapon_sprite_are_refused_at_load()
    {
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(TestPayload.Replaced(
            ("\"unarmedVisual\": \"weapon.unarmed\"", "\"unarmedVisual\": \"weapon.missing\"")));

        DaggerfallContentException refused = Assert.Throws<DaggerfallContentException>(() =>
            NormalizedMediaValidationTests.Read(FullContent(TestData.RepositoryRoot), definitions));
        Assert.Contains("Unarmed presentation must select admitted weaponSprite media.", refused.Diagnostics);
    }

    [Fact]
    public void A_classic_presentation_that_names_art_per_item_is_refused()
    {
        using JsonDocument root = JsonDocument.Parse("""
            { "classicPresentation": {
                "weaponVisuals": [ { "itemId": "iron-dagger", "resource": "weapon.longblade" } ],
                "viewmodel": { "renderOrder": 0 }, "unarmedVisual": "weapon.unarmed" } }
            """);
        DaggerfallContentDiagnostics diagnostics = new();

        DaggerfallClassicSelection.Read(root.RootElement, diagnostics);

        string refused = Assert.Throws<DaggerfallContentException>(diagnostics.ThrowIfAny).Message;
        Assert.Contains("classicPresentation sets 'weaponVisuals', which it does not carry: a weapon item draws the art its template names.", refused, StringComparison.Ordinal);
    }
}
