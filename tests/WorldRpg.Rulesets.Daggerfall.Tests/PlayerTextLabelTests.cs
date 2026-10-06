using WorldRpg.Rulesets.Daggerfall.Crime;
using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>Player-facing labels the ruleset publishes in place of codes, enum names and raw values.</summary>
public sealed class PlayerTextLabelTests
{
    [Fact]
    public void Classic_element_zero_is_fire_and_every_range_type_has_a_target_label()
    {
        Assert.Equal("Fire", DaggerfallMagicCostPolicy.ElementLabel(0));
        Assert.Equal(["Fire", "Cold", "Poison", "Shock", "Magic"], DaggerfallMagicCostPolicy.ElementLabels);
        Assert.Equal("Magic", DaggerfallMagicCostPolicy.ElementLabel(4));
        Assert.Throws<ArgumentOutOfRangeException>(() => DaggerfallMagicCostPolicy.ElementLabel(5));
        Assert.Equal("Self", DaggerfallMagicCostPolicy.TargetLabel(0));
        Assert.Equal("Area at range", DaggerfallMagicCostPolicy.TargetLabel(4));
    }

    [Fact]
    public void Spell_service_results_publish_sentences_rather_than_codes()
    {
        foreach (string code in new[] { "SpellUnavailable", "ProtectedSpell", "ConfirmationRequired", "Forgotten", "UnknownSpell",
            "ProviderUnavailable", "InsufficientFunds", "NotMember", "PriceChanged", "SpellbookRequired" })
            Assert.Contains(' ', DaggerfallSession.SpellbookOutcomeText(code));
        foreach (string code in new[] { "InvalidCombination", "InvalidSettings", "DraftChanged", "IdentityUnavailable", "InsufficientRank" })
            Assert.Contains(' ', DaggerfallSession.SpellMakerOutcomeText(code));
        Assert.Equal("Today is not a Daedric summoning day.", DaggerfallSession.SummoningOutcomeText("WrongDay"));
        Assert.Equal("Answer the prince's offer first.", DaggerfallSession.SummoningOutcomeText("AnswerPendingOffer"));
        Assert.Equal("Spell ready.", DaggerfallSession.CastOutcomeText(DaggerfallCastOutcome.Ready));
        // An enchantment construction refusal is already the construction owner's sentence.
        Assert.Equal("The item lacks enchantment capacity.", DaggerfallSession.ItemMakerOutcomeText("The item lacks enchantment capacity."));
        Assert.Equal("That service cannot be completed right now.", DaggerfallServiceOutcomeText.Common("SomeFutureCode"));
    }

    [Fact]
    public void Court_release_and_every_crime_read_as_sentences()
    {
        Assert.Equal("You were acquitted. You are free to leave.", DaggerfallSession.CourtReleaseText(DaggerfallCourtOutcome.Acquitted));
        Assert.DoesNotContain("Convicted", DaggerfallSession.CourtReleaseText(DaggerfallCourtOutcome.Convicted), StringComparison.Ordinal);
        foreach (DaggerfallCrimeKind crime in Enum.GetValues<DaggerfallCrimeKind>())
            Assert.NotEqual("An unnamed crime", DaggerfallSession.CrimeLabel(crime));
        Assert.Equal("Breaking and entering", DaggerfallSession.CrimeLabel(DaggerfallCrimeKind.BreakingAndEntering));
    }

    [Fact]
    public void Travel_and_effect_durations_read_as_game_time()
    {
        Assert.Equal("less than a minute", DaggerfallCalendar.DescribeDuration(59));
        Assert.Equal("1 minute", DaggerfallCalendar.DescribeDuration(60));
        Assert.Equal("2 hours", DaggerfallCalendar.DescribeDuration(7200));
        Assert.Equal("1 day and 1 hour", DaggerfallCalendar.DescribeDuration(DaggerfallCalendar.SecondsPerDay + 3600 + 61));
        Assert.Equal("Your journey was interrupted by an encounter.", DaggerfallSession.TravelInterruptionText(Travel.DaggerfallTravelOutcome.Encounter));
        Assert.Equal("Damage health", DaggerfallEffectCatalog.Label("damage-health"));
    }

    [Fact]
    public void Bank_regions_use_published_region_names()
    {
        var names = TestPayload.Definitions.BuildingNames;
        Assert.NotEmpty(names.RegionNames);
        Assert.Equal(names.RegionNames[17], names.RegionName(17));
        Assert.DoesNotContain("17", names.RegionName(17), StringComparison.Ordinal);
        Assert.Equal("an unnamed region", names.RegionName(names.RegionNames.Count));
    }

    [Fact]
    public void Map_markers_and_city_footprints_publish_semantic_labels()
    {
        Assert.Equal("Exit", DaggerfallMapProjection.MarkerLabel(DaggerfallSiteMarkerKind.Portal));
        Assert.Equal("tavern", DaggerfallMapProjection.FootprintCategory(16));
        Assert.Equal("guild", DaggerfallMapProjection.FootprintCategory(15));
        Assert.Equal("shop", DaggerfallMapProjection.FootprintCategory(3));
        Assert.Equal("common", DaggerfallMapProjection.FootprintCategory(18));
        Assert.Equal("", DaggerfallRestPresentation.InterruptionText(DaggerfallRestInterruption.None));
    }
}
