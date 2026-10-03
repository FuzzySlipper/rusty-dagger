using Rusty.Engine.Mechanics;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class NewGameSessionTests
{
    public static IEnumerable<object[]> Careers() => Enumerable.Range(0, 18)
        .SelectMany(index => new[] { false, true }.Select(female => new object[] { $"class{index:00}", female }));

    [Theory]
    [MemberData(nameof(Careers))]
    public void Committed_class_and_gender_construct_classic_loadout_full_vitals_and_known_spells(string career, bool female)
    {
        using Fixture f = new();
        Commit(f.Title, career, female);
        var choice = f.Title.State.Character.Capture();
        using var game = Assert.IsType<DaggerfallSession>(f.Title.CreateNewGame());
        Assert.False(game.RequiresCharacterInitialization);
        Assert.Equal(f.Title.State.Character.Identity, game.State.Character.Identity);
        Assert.Equal(f.Inputs.Site, game.Site.Active);
        var configured = f.Definitions.NewGame.Careers.Single(row => row.Career == career);
        Assert.Equal(configured.Spells.Order(), game.State.Character.KnownSpells.Order());
        foreach (var (_, track) in game.State.Actors.Player.Stats.Tracks)
            Assert.Equal(track.Maximum.Value, track.Current);
        var inventory = game.State.Inventory.Read();
        Assert.Contains(inventory.UniqueItems, item => item.Definition.Value == "template-132");
        Assert.Contains(inventory.UniqueItems, item => item.Definition.Value == $"template-{(female ? 206 : 165)}");
        Assert.Contains(inventory.UniqueItems, item => item.Definition.Value == $"template-{(female ? 190 : 151)}");
        Assert.Contains(game.State.Equipment.Read().Assignments, assignment => assignment.Slot.Value == "chest-clothes");
        Assert.Contains(game.State.Equipment.Read().Assignments, assignment => assignment.Slot.Value == "legs-clothes");
        foreach (var item in configured.Items)
        {
            string key = item.Material is null ? $"template-{item.Template}" : $"template-{item.Template}-{item.Material}";
            if (item.Template == 131)
                Assert.Contains(inventory.Stacks, stack => stack.Definition.Value == key && stack.Quantity == item.Quantity);
            else Assert.Contains(inventory.UniqueItems, unique => unique.Definition.Value == key);
        }
        ulong biographyGold = choice.Background!.StartingGrants.Where(item => item.ItemId == "template-276")
            .Aggregate(0UL, (total, item) => total + item.Quantity);
        Assert.Equal((ulong)f.Definitions.NewGame.Gold + biographyGold, Gold(game));
        Assert.Equal(Gold(game), Assert.Single(inventory.Stacks, stack => stack.Definition.Value == "template-276").Quantity);
        Assert.DoesNotContain(inventory.UniqueItems, item => item.Definition.Value == "iron-longsword");
        Assert.NotNull(f.Title.State.Character.ReadCreation().Summary);
    }

    [Fact]
    public void A_new_game_keeps_rendering_through_its_own_camera_after_the_title_session_is_released()
    {
        using Fixture f = new(); Commit(f.Title, "class00");
        ulong? titleCamera = f.TitleEngine.ActiveCamera;
        Assert.NotNull(titleCamera);
        using var game = Assert.IsType<DaggerfallSession>(f.Title.CreateNewGame());
        ulong? gameCamera = f.TitleEngine.ActiveCamera;
        Assert.NotNull(gameCamera);
        Assert.NotEqual(titleCamera, gameCamera);
        f.Title.Dispose();
        Assert.Equal(gameCamera, f.TitleEngine.ActiveCamera);
    }

    [Fact]
    public void New_games_share_no_state_and_encoded_save_restore_never_regrants()
    {
        using Fixture f = new(); Commit(f.Title, "class00");
        using var first = Assert.IsType<DaggerfallSession>(f.Title.CreateNewGame());
        using var second = Assert.IsType<DaggerfallSession>(f.Title.CreateNewGame());
        var health = first.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health"));
        health.SetCurrent(1); first.State.Character.LearnSpell("spell.042");
        Assert.NotEqual(1, second.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).Current);
        Assert.DoesNotContain("spell.042", second.State.Character.KnownSpells);
        var inventory = first.State.Inventory.Read();
        using var restored = DaggerfallSession.Restore(f.Engine().Context, f.Composition, first.CaptureSave());
        Assert.Equal(1, restored.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).Current);
        Assert.Equal(Gold(first), Gold(restored));
        Assert.Equal(Gold(first), Assert.Single(restored.State.Inventory.Read().Stacks, stack => stack.Definition.Value == "template-276").Quantity);
        Assert.Equal(inventory.UniqueItems.Count, restored.State.Inventory.Read().UniqueItems.Count);
        Assert.Equal(first.State.Character.KnownSpells.Order(), restored.State.Character.KnownSpells.Order());
        Assert.Throws<ArgumentException>(() => restored.CreateNewGame());
    }

    [Fact]
    public void Incomplete_or_invalid_character_cannot_launch_and_abandon_restart_does_not_grant()
    {
        using Fixture f = new(); var title = f.Title;
        var inventory = title.State.Inventory.Read();
        Assert.Throws<ArgumentException>(() => title.CreateNewGame());
        title.State.Character.BeginFreshChoices(RandomMinimum.Create());
        title.State.Character.ReplacePending(title.State.Character.Pending! with { CareerId = "missing-career" });
        Assert.Throws<ArgumentException>(() => title.State.Character.CommitChoices());
        Assert.Equal("Nameless", title.State.Character.Identity.Name);
        Assert.Equal(inventory.StoreRevision, title.State.Inventory.Read().StoreRevision);
        title.State.Character.AbandonCreation();
        Assert.Null(title.State.Character.Pending); Assert.Null(title.State.Character.Background);
        Commit(title, "class13"); title.State.Character.BeginFreshChoices(RandomMinimum.Create());
        Assert.Null(title.State.Character.Background); Assert.NotNull(title.State.Character.Pending);
        Assert.Throws<ArgumentException>(() => title.CreateNewGame());
        Assert.Equal(inventory.StoreRevision, title.State.Inventory.Read().StoreRevision);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Custom_class_starting_spells_require_primary_or_major_magic_skills(bool magic)
    {
        using Fixture f = new(); var title = f.Title;
        var baseline = f.Definitions.Catalogs.RequireCareer("class00");
        var custom = DaggerfallCustomCareerChoices.Default(f.Definitions, baseline);
        if (magic)
        {
            custom = custom with { PrimarySkills = baseline.PrimarySkills.ToArray(), MajorSkills = baseline.MajorSkills.ToArray(),
                MinorSkills = baseline.MinorSkills.ToArray() };
        }
        else
        {
            var physical = f.Definitions.Catalogs.Skills.Select(skill => skill.Id).Where(skill => skill is not
                ("destruction" or "restoration" or "illusion" or "alteration" or "thaumaturgy" or "mysticism")).ToArray();
            custom = custom with { PrimarySkills = physical.Take(3).ToArray(), MajorSkills = physical.Skip(3).Take(3).ToArray(),
                MinorSkills = new[] { "mysticism" }.Concat(physical.Skip(6).Take(5)).ToArray() };
        }
        var career = DaggerfallCustomCareerPolicy.Compile(f.Definitions, custom, baseline).Career;
        title.State.Character.BeginFreshChoices(RandomMinimum.Create());
        var choices = title.State.Character.Pending! with { CareerId = "custom", CustomCareer = custom };
        var rolled = DaggerfallCharacterBackgroundPolicy.Roll(f.Definitions, career, choices.ToIdentity(), RandomMinimum.Create(), 1);
        var complete = DaggerfallCharacterBackgroundPolicy.Update(f.Definitions, career, choices.ToIdentity(), rolled, rolled.Answers,
            [new(career.Attributes[0], rolled.AttributeBonusPool)],
            [new(career.PrimarySkills[0], 6), new(career.MajorSkills[0], 6), new(career.MinorSkills[0], 6)]);
        title.State.Character.ReplacePending(choices with { Background = complete }); title.State.Character.CommitChoices();
        using var game = Assert.IsType<DaggerfallSession>(title.CreateNewGame());
        Assert.Equal(magic ? new[] { "spell.008", "spell.042", "spell.036" }.Order() : Enumerable.Empty<string>(), game.State.Character.KnownSpells.Order());
        Assert.Contains(game.State.Inventory.Read().UniqueItems, item => item.Definition.Value == "template-120-iron");
    }

    [Fact]
    public void Live_cancel_cannot_erase_the_initialized_character()
    {
        using Fixture f = new(); Commit(f.Title);
        using var game = Assert.IsType<DaggerfallSession>(f.Title.CreateNewGame());
        game.ApplyProductMode(WorldRpg.Kit.ProductMode.Playing);
        var identity = game.State.Character.Identity;
        var spells = game.State.Character.KnownSpells.Order().ToArray();
        game.Update(new Rusty.Engine.ProductUpdate(OuterUpdate(1), [Input(Rusty.Engine.InputEventKind.DirectDigital) with
        { ValueKind = Rusty.Engine.InputValueKind.ProductPayload, PayloadContract = "dagger.ui.action.v1"u8.ToArray(),
            PayloadData = "{\"action\":\"character-cancel\"}"u8.ToArray() }]));
        Assert.Equal(identity, game.State.Character.Identity); Assert.NotNull(game.State.Character.Background);
        Assert.Equal(spells, game.State.Character.KnownSpells.Order());
    }

    [Fact]
    public void Starting_shirt_keeps_variant_zero_and_uses_the_canonical_random_clothing_dye()
    {
        using Fixture f = new(RandomMaximum.Create()); Commit(f.Title);
        using var game = Assert.IsType<DaggerfallSession>(f.Title.CreateNewGame());
        var shirt = game.State.Inventory.Read().UniqueItems.Single(item => item.Definition.Value == "template-165");
        var metadata = game.State.ItemInstances.RequireUnique(game.State.Inventory.GetDurableItemId(shirt.Entity).Value);
        Assert.Equal(0, metadata.Variant); Assert.Equal("green", metadata.Dye);
    }

    [Theory]
    [InlineData("shirt-group")]
    [InlineData("shirt-slot")]
    [InlineData("weapon-group")]
    [InlineData("material")]
    [InlineData("unique-quantity")]
    [InlineData("missing-material")]
    [InlineData("missing-spell")]
    public void Invalid_starting_configuration_is_rejected_while_reading_content(string mutation)
    {
        var definitions = TestPayload.Definitions;
        var root = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(TestData.RepositoryRoot,
            "content/worldrpg/payloads/daggerfall.base.json")))!.AsObject();
        var config = root["newGame"]!;
        var item = config["careers"]![0]!["items"]![0]!;
        switch (mutation)
        {
            case "shirt-group": config["maleShirtTemplate"] = 206; break;
            case "shirt-slot": config["maleShirtTemplate"] = 151; break;
            case "weapon-group": item["template"] = 165; break;
            case "material": item["material"] = "invalid-metal"; break;
            case "unique-quantity": item["quantity"] = 2; break;
            case "missing-material": item.AsObject().Remove("material"); break;
            case "missing-spell": config["careers"]![0]!["spells"]![0] = "missing-spell"; break;
        }
        using var document = System.Text.Json.JsonDocument.Parse(root.ToJsonString());
        var diagnostics = new DaggerfallContentDiagnostics();
        DaggerfallBaseContent.ReadNewGame(document.RootElement, definitions.Catalogs, definitions.ItemTemplateCatalog,
            definitions.TemplateItems, definitions.EquipmentSlots, definitions.Magic, diagnostics);
        Assert.Throws<DaggerfallContentException>(() => diagnostics.ThrowIfAny());
    }

    internal static void Commit(DaggerfallSession title, string careerId = "class00", bool female = false)
    {
        var definitions = TestPayload.Definitions;
        var career = definitions.Catalogs.RequireCareer(careerId);
        title.State.Character.BeginFreshChoices(RandomMinimum.Create());
        var choices = title.State.Character.Pending! with { Name = "New adventurer", CareerId = careerId,
            Gender = female ? DaggerfallCharacterGender.Female : DaggerfallCharacterGender.Male };
        var rolled = DaggerfallCharacterBackgroundPolicy.Roll(definitions, career, choices.ToIdentity(), RandomMinimum.Create(), 1);
        var complete = DaggerfallCharacterBackgroundPolicy.Update(definitions, career, choices.ToIdentity(), rolled, rolled.Answers,
            [new(career.Attributes[0], rolled.AttributeBonusPool)],
            [new(career.PrimarySkills[0], 6), new(career.MajorSkills[0], 6), new(career.MinorSkills[0], 6)]);
        title.State.Character.ReplacePending(choices with { Background = complete });
        title.State.Character.CommitChoices(replaceCommitted: true);
    }

    internal static string CommitPayload()
    {
        var definitions = TestPayload.Definitions; var career = definitions.Catalogs.RequireCareer("class00");
        var identity = new DaggerfallCharacterIdentity("New adventurer", "breton", DaggerfallCharacterGender.Male, 0, DaggerfallCharacterReflexes.Average, "class00");
        var rolled = DaggerfallCharacterBackgroundPolicy.Roll(definitions, career, identity, RandomMinimum.Create(), 1);
        return System.Text.Json.JsonSerializer.Serialize(new { action = "character-commit", name = identity.Name, race = identity.RaceId,
            gender = "male", faceIndex = 0, reflexes = 2, career = "class00",
            backgroundAnswers = string.Join(',', rolled.Answers.Select(answer => $"{answer.Question}:{answer.Letter}")),
            attributeAllocations = $"{career.Attributes[0]}:{rolled.AttributeBonusPool}",
            skillAllocations = $"{career.PrimarySkills[0]}:6,{career.MajorSkills[0]}:6,{career.MinorSkills[0]}:6" });
    }

    private static ulong Gold(DaggerfallSession game) => game.State.Inventory.Read().Stacks
        .Where(stack => stack.Definition.Value == "template-276").Aggregate(0UL, (total, stack) => total + stack.Quantity);

    private sealed class Fixture : IDisposable
    {
        internal DaggerfallDefinitions Definitions { get; } = TestPayload.Definitions;
        internal DaggerfallSiteProfile Inputs { get; } = ReadInputs(TestData.RepositoryRoot);
        internal DaggerfallSessionComposition Composition { get; }
        internal DaggerfallSession Title { get; }
        internal EngineContextFake TitleEngine { get; }
        private readonly Rusty.Engine.IRandomService _random;
        internal Fixture(Rusty.Engine.IRandomService? random = null)
        {
            _random = random ?? RandomMinimum.Create();
            Composition = new(Definitions, Inputs, DaggerfallTuning.Defaults);
            TitleEngine = Engine();
            Title = DaggerfallSession.StartNew(TitleEngine.Context, Composition);
        }
        internal EngineContextFake Engine()
        {
            List<string> releases = []; ContentFake content = new(releases); PopulateContent(content, Inputs);
            var spatial = SpatialFake.Create(Inputs.SpatialArtifact.Sha256, releases); spatial.KeepPosition = true;
            return EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), random: _random);
        }
        public void Dispose() => Title.Dispose();
    }
}
