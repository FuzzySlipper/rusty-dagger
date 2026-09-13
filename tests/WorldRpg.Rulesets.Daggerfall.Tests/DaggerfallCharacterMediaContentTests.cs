using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The published character-media references a character sheet or social view resolves: race,
/// gender, body and head layers as data. This is the media side; the sheet's own stat projection is
/// covered separately.
/// </summary>
public sealed class DaggerfallCharacterMediaContentTests
{
    [Fact]
    public void Resolves_each_race_background_bodies_and_heads_from_the_published_pack()
    {
        DaggerfallDefinitions definitions = Read();
        DaggerfallCharacterPresentationSet presentation = definitions.CharacterPresentation;

        // The eight playable races, each with a background, both bodies per gender and ten heads per
        // gender - the shape every paper doll draws from.
        Assert.Equal(8, presentation.Races.Count);
        DaggerfallRaceLayers breton = presentation.RequireRace("breton");
        Assert.Equal(1, breton.DonorRaceId);
        Assert.Equal("character.scbg.scbg00i0.0", breton.Background.MediaId);
        Assert.Equal("SCBG00I0.IMG", breton.Background.SourceFile);
        Assert.Equal("character.body-unclothed.male.00.0", breton.Body(DaggerfallCharacterGender.Male, clothed: false).MediaId);
        Assert.Equal("BODY00I0.IMG", breton.Body(DaggerfallCharacterGender.Male, clothed: false).SourceFile);
        Assert.Equal("character.body-clothed.female.00.0", breton.Body(DaggerfallCharacterGender.Female, clothed: true).MediaId);
        Assert.Equal("ART_PAL.COL", breton.Background.Palette);

        // The fourth female head of Breton, which is what a face choice resolves to.
        Assert.Equal(10, breton.Heads(DaggerfallCharacterGender.Female).Count);
        Assert.Equal("character.head.female.00.3", breton.Heads(DaggerfallCharacterGender.Female)[3].MediaId);
        Assert.Equal("FACE10I0.CIF", breton.Heads(DaggerfallCharacterGender.Female)[3].SourceFile);

        // The upper races' female layers are the ones a race-index guard is easiest to get wrong: the
        // donor numbers a female Khajiit sixteen, not six.
        DaggerfallRaceLayers khajiit = presentation.RequireRace("khajiit");
        Assert.Equal(7, khajiit.DonorRaceId);
        Assert.Equal("character.body-clothed.female.06.0", khajiit.Body(DaggerfallCharacterGender.Female, clothed: true).MediaId);
        Assert.Equal("BODY16I1.IMG", khajiit.Body(DaggerfallCharacterGender.Female, clothed: true).SourceFile);

        foreach (DaggerfallRaceLayers race in presentation.Races.Values)
        {
            Assert.Equal(25, race.Layers.Count);
            Assert.NotEmpty(race.Heads(DaggerfallCharacterGender.Male));
            Assert.NotEmpty(race.Heads(DaggerfallCharacterGender.Female));
        }

        // The publication accounts for every supplied character file, and this pack draws every race.
        Assert.Equal(87, presentation.Files.Count);
        Assert.Contains("CMPA00I0.BSS", presentation.Files);
        Assert.Empty(presentation.RacesWithoutMedia);
        Assert.Throws<InvalidOperationException>(() => presentation.RequireRace("vampire"));
    }

    [Fact]
    public void A_layer_naming_a_file_the_publication_does_not_account_for_fails_with_that_file()
    {
        // The reference resolves to nothing if the file it names was never accounted for, so the pack
        // is refused with the file named rather than loading a reference that points nowhere.
        string pack = File.ReadAllText(PackPath()).Replace("\"sourceFile\": \"BODY00I0.IMG\"", "\"sourceFile\": \"ABSENT01I0.IMG\"", StringComparison.Ordinal);
        DaggerfallContentException error = Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(System.Text.Encoding.UTF8.GetBytes(pack)));
        Assert.Contains("ABSENT01I0.IMG", error.Message, StringComparison.Ordinal);
        Assert.Contains("does not account for", error.Message, StringComparison.Ordinal);

        // A layer name this reader does not know is refused rather than given a role by position.
        string unknown = File.ReadAllText(PackPath()).Replace("\"layer\": \"background\"", "\"layer\": \"backdrop\"", StringComparison.Ordinal);
        DaggerfallContentException named = Assert.Throws<DaggerfallContentException>(() => DaggerfallBaseContent.Read(System.Text.Encoding.UTF8.GetBytes(unknown)));
        Assert.Contains("not a layer name this reader knows", named.Message, StringComparison.Ordinal);
    }

    private static DaggerfallDefinitions Read() => DaggerfallBaseContent.Read(File.ReadAllBytes(PackPath()));

    private static string PackPath() => Path.Combine(RepositoryRoot(), "content/worldrpg/payloads/daggerfall.base.json");

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("repository root not found");
    }
}
