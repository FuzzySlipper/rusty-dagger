using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalized;
using Xunit;

namespace Daggerfall.Import.Tests;

public sealed class DaggerfallQuestResourceTests
{
    [Fact]
    public void Normalizes_resource_variants_without_requiring_runtime_source_parsing()
    {
        string[] lines = [
            "Foe _band_ is 4 orc", "Item _gold_ gold range 5 to 25",
            "Item _artifact_ artifact Ring_of_Khajiit anyInfo QuestorOffer",
            "Item _weapon_ item class 17 subclass 13", "Item _mod_ item class 0 template 538",
            "Item _letter_ letter used 1017", "Person _anonymous_",
            "Person _questor_ named Morgiah faction Court factionType Noble group Mage female remote atHome face 3",
            "Place _sites_ randompermanent CastleA,CastleB",
        ];
        DaggerfallQuestPack pack = new(new DaggerfallTextSource(DaggerfallTextKind.Resource, "CNT-017-QBN", "fixture/resources", "en", 1, 0, 1),
            [new DaggerfallQuestRecord("fixture", "", "resources.txt", DaggerfallQuestDisposition.Compiled, [],
                [new DaggerfallQuestBlock(QuestBlockKind.Person, 10, lines, null)], [])]);
        var resources = DaggerfallQuestResourceBuilder.Build(pack).Declarations.ToDictionary(value => value.Symbol.CanonicalId);
        Assert.Equal(4, resources["band"].Foe!.Count);
        Assert.Equal((5, 25), (resources["gold"].Item!.RangeLow, resources["gold"].Item!.RangeHigh));
        Assert.True(resources["artifact"].Item!.Artifact);
        Assert.Equal("QuestorOffer", resources["artifact"].Item!.AnyInfoMessage);
        Assert.Equal((17, 13), (resources["weapon"].Item!.Class, resources["weapon"].Item!.Subclass));
        Assert.Equal(538, resources["mod"].Item!.Template);
        Assert.Equal("1017", resources["letter"].Item!.UsedMessage);
        Assert.NotNull(resources["anonymous"].Person);
        var person = resources["questor"].Person!;
        Assert.Equal(("Morgiah", "Court", "Noble", "Mage", "female", "remote", true, 3),
            (person.Named, person.Faction, person.FactionType, person.Group, person.Gender, person.Scope, person.AtHome, person.Face));
        Assert.Equal(["castlea", "castleb"], resources["sites"].Place!.Sites.Select(site => site.CanonicalId));
        Assert.Equal(18, resources["sites"].SourceLine);
        Assert.Equal(lines[8], resources["sites"].SourceText);
    }

    [Fact]
    public void PreservesSymbolSpellingAndFailsAnExplicitDanglingReference()
    {
        DaggerfallQuestPack pack = new(
            new DaggerfallTextSource(DaggerfallTextKind.Resource, "CNT-017-QBN", "fixture/quests", "en", 1, 0, 1),
            [new DaggerfallQuestRecord("fixture", "", "fixture.txt", DaggerfallQuestDisposition.Compiled, [],
                [new DaggerfallQuestBlock(QuestBlockKind.Item, 7, ["Item _Letter_ letter", "place item _Letter_ at _missing_"], null)], [])]);

        DaggerfallQuestResources resources = DaggerfallQuestResourceBuilder.Build(pack);

        DaggerfallQuestResourceDeclaration item = Assert.Single(resources.Declarations);
        Assert.Equal("_Letter_", item.Symbol.SourceSpelling);
        Assert.Equal("letter", item.Symbol.CanonicalId);
        DaggerfallQuestUnresolvedReference missing = Assert.Single(resources.Unresolved);
        Assert.Equal("_missing_", missing.SourceSpelling);
        Assert.Equal("missing", missing.CanonicalId);
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(resources.RequireAllReferencesResolved);
        Assert.Contains("fixture.txt", error.Message, StringComparison.Ordinal);
        Assert.Contains("_missing_", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RetainsEachPlaceSelectionKind()
    {
        DaggerfallQuestPack pack = new(
            new DaggerfallTextSource(DaggerfallTextKind.Resource, "CNT-017-QBN", "fixture/places", "en", 1, 0, 1),
            [new DaggerfallQuestRecord("fixture", "", "places.txt", DaggerfallQuestDisposition.Compiled, [],
                [new DaggerfallQuestBlock(QuestBlockKind.Place, 1, ["Place _a_ local tavern", "Place _b_ remote dungeon", "Place _c_ permanent DaggerfallCastle", "Place _d_ randompermanent A,B"], null)], [])]);

        DaggerfallQuestResources resources = DaggerfallQuestResourceBuilder.Build(pack);
        Assert.Equal([DaggerfallQuestPlaceKind.Local, DaggerfallQuestPlaceKind.Remote, DaggerfallQuestPlaceKind.Permanent, DaggerfallQuestPlaceKind.RandomPermanent], resources.Declarations.Select(value => value.PlaceKind));
    }
}
