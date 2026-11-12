using Rusty.Engine.Mechanics;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class TextOwnerContextTests
{
    [Theory]
    [InlineData(0, "Morning Star", "The Ritual", "Winter")]
    [InlineData(1, "Sun's Dawn", "The Lover", "Winter")]
    [InlineData(5, "Midyear", "The Steed", "Summer")]
    [InlineData(8, "Hearthfire", "The Lady", "Fall")]
    public void Calendar_macros_use_published_names_and_the_donor_date_format(int month, string name, string sign, string season)
    {
        var definitions = TestPayload.Definitions;
        var context = DaggerfallTextContext.Empty with { Calendar = DaggerfallTextContexts.Calendar(definitions, new(405, month, 20, 13, 4, 0)) };
        var rendered = Render("%dat; %tim; %day; %dayn; %days; %mon; %monn; %year; %min; %hour; %sign; %sea", context);
        Assert.True(rendered.IsComplete, string.Join(",", rendered.Diagnostics));
        Assert.Equal($"Loredas the 21st of {name}; 13:04; 21; Loredas; 21st; {month + 1}; {name}; 405; 4; 13; {sign}; {season}", rendered.Text);
    }

    [Fact]
    public void Current_player_macros_read_live_stats_signed_modifiers_and_names()
    {
        using var f = new SanguineRoseSessionTests.Fixture();
        var stats = f.Session.State.Actors.Player.Stats;
        foreach (var (id, value) in new[] { ("strength", 75), ("intelligence", 60), ("willpower", 49), ("agility", 29),
            ("endurance", 65), ("personality", 40), ("speed", 80), ("luck", 55) })
            stats.GetStat(StatId.Parse(id)).BaseValue = value;
        var context = f.Session.CurrentTextContext();
        var rendered = Render("%ra; %crn; %dam; %thd; %hea; %hmd; %mad; %enc; %str; %int; %wil; %agi; %end; %per; %spd; %luc", context);
        Assert.True(rendered.IsComplete, string.Join(",", rendered.Diagnostics));
        Assert.Equal("Breton; Daggerfall; +5; -3; +1; +1; 4; 112; 75; 60; 49; 29; 65; 40; 80; 55", rendered.Text);
        stats.GetStat(StatId.Parse("strength")).BaseValue = 40;
        Assert.Equal("-2", Render("%dam", f.Session.CurrentTextContext()).Text);
        Assert.Equal("Dark Elf", DaggerfallTextContexts.Race(TestPayload.Definitions, "dark-elf"));
    }

    [Fact]
    public void Attribute_rating_follows_each_preceding_attribute_without_cached_cross_attribute_values()
    {
        var context = DaggerfallTextContext.Empty with { Player = new(Strength: "75", Intelligence: "20")
            { AttributeRatings = new Dictionary<string, string> { ["%str"] = "strong", ["%int"] = "limited" } } };
        Assert.Equal("75 strong / 20 limited / 75 strong", Render("%str %ark / %int %ark / %str %ark", context).Text);
    }

    [Fact]
    public void Item_macros_include_material_condition_value_weight_damage_and_book_context()
    {
        var definitions = TestPayload.Definitions;
        var definition = definitions.RequireItem(new("iron-dagger"));
        var metadata = new DaggerfallItemInstanceMetadata("iron-dagger", "steel", 0, 50, 100, true, false,
            null, null, null, DaggerfallItemOwner.Player, WeightClassicUnits: 500);
        var context = DaggerfallTextContext.Empty with { Item = DaggerfallTextContexts.Item(definitions, definition, metadata, "Dagger", 2, 7) };
        var rendered = Render("%it; %wep; %arm; %a; %wth; %mat; %qua; %kg; %wdm", context);
        Assert.True(rendered.IsComplete, string.Join(",", rendered.Diagnostics));
        Assert.Equal("Dagger; Dagger; Dagger; 2; 14; Steel; Used; 2.50; 1 - 6", rendered.Text);
        var book = definitions.Books.Books[59];
        Assert.Equal(book.Title + " by " + book.Author, Render("%bt by %ba", DaggerfallTextContexts.Book(book)).Text);
    }

    private static DaggerfallTextRenderResult Render(string text, DaggerfallTextContext context) =>
        TestPayload.Definitions.TextPresentation.ResolveRaw(text, new(DaggerfallTextKind.Internal, "test-owner-context"), context);
}
