using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;
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

        // The faction faces resolve by the donor's index, which is what a social or escort view asks
        // for - they belong to no race and are not paper-doll heads.
        Assert.Equal(61, presentation.FactionFaces.Count);
        Assert.Equal("character.faction-face.00", presentation.FactionFaces[0].MediaId);
        Assert.Equal("character.faction-face.60", presentation.FactionFaces[60].MediaId);
        Assert.All(presentation.FactionFaces, face => Assert.Equal("FACES.CIF", face.SourceFile));
        Assert.All(presentation.FactionFaces, face => Assert.Equal("ART_PAL.COL", face.Palette));

        // A career's portrait resolves by career identity, and the careers the corpus does not depict
        // say so: the pack publishes three portraits and records the rest.
        Assert.Equal(3, presentation.Careers.Count);
        DaggerfallCareerPortraitDefinition portrait = presentation.RequirePortrait("class00");
        Assert.Equal("character.portrait.mage.0", portrait.MediaId);
        Assert.Equal(("MAGE.CEL", 15, "ART_PAL.COL"), (portrait.SourceFile, portrait.FrameCount, portrait.Palette));

        // The portrait is matched by the career's class name, not by its identity: the catalog
        // identifies a career by its record position.
        Assert.Equal("Mage", definitions.Catalogs.Careers.Single(career => career.Id == "class00").Name);
        Assert.Equal((10, 15), (presentation.RequirePortrait("class08").FrameCount, presentation.RequirePortrait("class16").FrameCount));
        Assert.Equal("ROGUE.CEL", presentation.RequirePortrait("class08").SourceFile);
        Assert.Equal("WARRIOR.CEL", presentation.RequirePortrait("class16").SourceFile);

        // The other sixteen careers are recorded as having no portrait rather than left to be
        // discovered by a missing lookup.
        Assert.Equal(16, presentation.CareersWithoutPortrait.Count);
        Assert.Contains(presentation.CareersWithoutPortrait, entry => entry.Reason.Contains("no class portrait named for", StringComparison.Ordinal));
        Assert.Throws<InvalidOperationException>(() => presentation.RequirePortrait("class03"));

        // The player's own declared race and career resolve through the same records, which is what
        // the sheet publishes: an actor naming no race draws no paper doll rather than a default one.
        DaggerfallActorDefinition player = definitions.Actors[definitions.Actors.Keys.Single(id => id.Value == "player")];
        Assert.Equal("breton", player.Race);
        Assert.Equal("class00", player.Career);
        CharacterIdentityPresentation identity = CharacterIdentityPresentation.From(definitions, player)!;
        Assert.Equal("breton", identity.Race);
        Assert.Equal(1, identity.DonorRaceId);
        Assert.Equal("character.portrait.mage.0", identity.Portrait);
        Assert.Equal(25, identity.Media.Length);
        Assert.Contains(identity.Media, medium => medium.Layer == "background" && medium.MediaId == "character.scbg.scbg00i0.0");
        Assert.Contains(identity.Media, medium => medium.Layer == "head.female.3" && medium.MediaId == "character.head.female.00.3");

        // An actor that declares no race presents none, and one naming a career the corpus does not
        // depict publishes no portrait rather than another class's art.
        Assert.Null(CharacterIdentityPresentation.From(definitions, player with { Race = null }));
        Assert.Equal(string.Empty, CharacterIdentityPresentation.From(definitions, player with { Career = "class03" })!.Portrait);

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
