using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class StoryQuestSessionTests
{
    [Theory]
    [InlineData("Gothryd", "gothryd", "completesurrender", 0)]
    [InlineData("Akorithi", "akorithi", "s.30", 100000)]
    [InlineData("Brisienna", "brisienna", "s.32", 0)]
    [InlineData("KingOfWorms", "kingoworms", "s.33", 0)]
    [InlineData("Gortwog", "gortwog", "s.34", 0)]
    [InlineData("Underking", "underking", "s.35", 0)]
    [InlineData("Eadwyre", "eadwyre", "s.36", 0)]
    public void Totem_recipient_source_branch_consumes_item_and_grants_rewards_once(string recipient, string npc, string branch, int gold)
    {
        using var fixture = SourceBackedGuildBankSessionFixture.Create(); fixture.Random = SummonRandom.Create();
        using var game = fixture.Start(fixture.ExteriorProfile);
        var started = game.State.Quests.Start(new("totem", "S0000008.txt", "S0000008", DaggerfallQuestLifecycle.Active, null, [], []));
        var program = DaggerfallQuestTaskCompiler.Compile(TestPayload.Definitions.QuestSources.Resolve("S0000008.txt"));
        Assert.Equal(DaggerfallQuestItemResult.Changed, ((IDaggerfallQuestTaskLifecycle)game.State.Quests).ItemAction(new(started, program),
            new(DaggerfallQuestTaskOperationKind.GetItem, 1, "get item totem", ["totem"], [], null)));
        var saved = game.State.Quests.Capture();
        game.State.Quests.Restore(saved with { Instances = [saved.Instances.Single() with
        {
            Tasks = saved.Instances.Single().Tasks.Select(task => task with
            {
                IsDropped = task.Symbol != branch && !(recipient == "Gothryd" && task.Symbol is "holdtotem" or "surrenderchoice" or "giventodaggerfall" or "s.31"),
                IsSet = recipient == "Gothryd" && task.Symbol is "holdtotem" or "surrenderchoice",
            }).ToArray(),
            Resources = saved.Instances.Single().Resources.Select(resource => resource.Symbol == npc ? resource with { HasPlayerClicked = true } : resource).ToArray(),
        }] });
        using var before = fixture.Restore(game.CaptureSave());
        ulong startingGold = Gold(before);
        QuestDiseaseTests.Advance(before);
        Assert.True(before.State.Variables.ReadGlobal(recipient + "GotTotem"));
        Assert.DoesNotContain(before.State.ItemInstances.UniqueItems, item => item.Value.Owner == DaggerfallItemOwner.Player && item.Value.QuestItemSymbol == "totem");
        Assert.Equal(startingGold + (ulong)gold, Gold(before));
        var inventory = before.State.Inventory.Read();
        using var after = fixture.Restore(before.CaptureSave());
        QuestDiseaseTests.Advance(after);
        Assert.Equal(Gold(before), Gold(after));
        Assert.Equal(inventory.UniqueItems.Count, after.State.Inventory.Read().UniqueItems.Count);
        Assert.True(after.State.Variables.ReadGlobal(recipient + "GotTotem"));
        if (recipient == "KingOfWorms") Assert.Contains(after.State.Quests.All, quest => quest.SourceFile == "S0000106.txt");
    }

    private static ulong Gold(DaggerfallSession session) => session.State.Currency.Read().Gold;

    [Theory]
    [InlineData("Gothryd", 14, false)]
    [InlineData("Brisienna", 10, true)]
    [InlineData("KingOfWorms", 6, false)]
    [InlineData("Gortwog", 8, true)]
    [InlineData("Akorithi", 9, false)]
    [InlineData("Underking", 15, true)]
    [InlineData("Eadwyre", 7, false)]
    public void Each_source_ending_waits_for_finish_or_skip_and_survives_restore(string recipient, int video, bool skip)
    {
        var media = new DaggerfallCinematicPresentationTests.Harness();
        using var fixture = SourceBackedGuildBankSessionFixture.Create(configure: composition => composition with { CinematicContent = media.Content });
        fixture.Random = SummonRandom.Create();
        var recorder = VideoRecorder.Create(); fixture.Video = recorder.Service;
        using var game = fixture.Start(fixture.ExteriorProfile);
        game.State.Quests.Start(new("ending", "S0000016.txt", "S0000016", DaggerfallQuestLifecycle.Active, null, [], []));
        game.State.Variables.WriteGlobal("FinishedMantellanCrux", true);
        game.State.Variables.WriteGlobal(recipient + "GotTotem", true);
        // Saved boundary after returning from the Crux and the one-minute source delay.
        var saved = game.State.Quests.Capture();
        game.State.Quests.Restore(saved with { Instances = [saved.Instances.Single() with
        {
            Tasks = saved.Instances.Single().Tasks.Select(task => task.Kind == DaggerfallQuestTaskKind.Headless || task.Symbol == "s.01"
                ? task with { IsDropped = true } : task.Symbol == "delay" ? task with { IsSet = true } : task).ToArray(),
        }] });
        using var before = fixture.Restore(game.CaptureSave());
        QuestDiseaseTests.Advance(before);
        Assert.Equal("ANIM0003.VID", before.Cinematics!.ActiveSource);
        before.Cinematics.Skip();
        for (int pass = 0; pass < 3 && before.Cinematics.ActiveSource is null; pass++) QuestDiseaseTests.Advance(before);
        Assert.Equal($"ANIM{video:0000}.VID", before.Cinematics.ActiveSource);
        Assert.Equal(DaggerfallQuestLifecycle.Active, before.State.Quests.All.Single().Lifecycle);
        using var during = fixture.Restore(before.CaptureSave());
        QuestDiseaseTests.Advance(during);
        Assert.Equal($"ANIM{video:0000}.VID", during.Cinematics!.ActiveSource);
        if (skip) during.Cinematics.Skip();
        else { recorder.Complete(recorder.Played.Last()); during.Cinematics.Poll(); }
        for (int pass = 0; pass < 6; pass++) QuestDiseaseTests.Advance(during);
        Assert.True(during.State.Variables.ReadGlobal(recipient + "Ending"));
        Assert.NotEqual(DaggerfallQuestLifecycle.Active, during.State.Quests.All.Single().Lifecycle);
        int plays = recorder.Played.Count;
        using var after = fixture.Restore(during.CaptureSave());
        QuestDiseaseTests.Advance(after);
        Assert.True(after.State.Variables.ReadGlobal(recipient + "Ending"));
        Assert.Equal(plays, recorder.Played.Count);
    }

    [Theory]
    [InlineData("$CUREVAM", "s.00", "Vampire")]
    [InlineData("$CUREWER", "s.14", "Werewolf")]
    public void Published_cure_outcome_clears_saved_transformation_once(string source, string outcome, string racialKind)
    {
        using var fixture = SourceBackedGuildBankSessionFixture.Create();
        fixture.Random = SummonRandom.Create();
        using var game = fixture.Start(fixture.StartProfile);
        var kind = Enum.Parse<DaggerfallRacialKind>(racialKind);
        game.State.RacialOverrides.Select(kind, "source-cure", 0, vampireClan: kind == DaggerfallRacialKind.Vampire ? 153 : null);
        game.State.Quests.Start(new("cure", source + ".txt", source, DaggerfallQuestLifecycle.Active, null, [], []));
        if (source == "$CUREVAM")
        {
            var father = game.State.Quests.All.Single().Resources.Single(resource => resource.Symbol == "bloodfather");
            var binding = ((IDaggerfallQuestWorldAdmission)game).Place("cure", father, fixture.StartProfile,
                fixture.StartProfile.QuestMarkers.First(marker => marker.Kind == DaggerfallSiteMarkerKind.QuestSpawn));
            game.State.Quests.SetResource("cure", father with { Binding = binding! });
            ((IDaggerfallQuestTaskLifecycle)game.State.Quests).FoeCommand(
                new(game.State.Quests.All.Single(), DaggerfallQuestTaskCompiler.Compile(TestPayload.Definitions.QuestSources.Resolve(source + ".txt"))),
                new(DaggerfallQuestTaskOperationKind.KillFoe, 1, "kill foe bloodfather", ["bloodfather"], [], null));
            game.State.Quests.ReconcileFoeCommands();
            game.ApplyProductMode(WorldRpg.Kit.ProductMode.Title);
            game.Update(new Rusty.Engine.ProductUpdate(TestSessions.OuterUpdate(1), []));
            Assert.Single(game.State.Quests.All.Single().Resources.Single(resource => resource.Symbol == "bloodfather").DefeatedFoeIds);
        }
        // Reconstruct the source's reached outcome boundary: prior branches have settled and
        // the kill/prompt condition has selected this task. Its actual reward/cure actions run below.
        var quests = game.State.Quests.Capture();
        game.State.Quests.Restore(quests with { Instances = [quests.Instances.Single() with
        {
            Tasks = quests.Instances.Single().Tasks.Select(task => task.Symbol == outcome
                ? task with { IsSet = true } : task with { IsDropped = true }).ToArray(),
        }] });
        using var restored = fixture.Restore(game.CaptureSave());
        Assert.NotNull(restored.State.RacialOverrides.Current);
        QuestDiseaseTests.Advance(restored);
        Assert.Null(restored.State.RacialOverrides.Current);
        using var cured = fixture.Restore(restored.CaptureSave());
        QuestDiseaseTests.Advance(cured);
        Assert.Null(cured.State.RacialOverrides.Current);
        var task = cured.State.Quests.All.Single().Tasks.Single(task => task.Symbol == outcome);
        var program = DaggerfallQuestTaskCompiler.Compile(TestPayload.Definitions.QuestSources.Resolve(source + ".txt"));
        int operation = program.Tasks.Single(task => task.Symbol == outcome).Operations.ToList()
            .FindIndex(operation => operation.Kind is DaggerfallQuestTaskOperationKind.CureVampirism or DaggerfallQuestTaskOperationKind.CureLycanthropy);
        Assert.True(task.OperationCompleted[operation]);
    }

    [Fact]
    public void Committed_new_character_starts_source_tutorial_and_introduction_once_and_load_restores_them()
    {
        using var fixture = SourceBackedGuildBankSessionFixture.Create();
        fixture.Random = SummonRandom.Create();
        using var title = fixture.Start(fixture.StartProfile, composition => composition with
        {
            NewGameQuests = ["_TUTOR__.txt", "_BRISIEN.txt"],
            QuestAdmission = new(DaggerfallNamedQuestCorpusContent.Read(NamedQuestCorpusTests.Payload("story-late"), TestPayload.Definitions)),
        });
        Assert.Empty(title.State.Quests.All);
        NewGameSessionTests.Commit(title);
        using var game = Assert.IsType<DaggerfallSession>(title.CreateNewGame());
        Assert.Equal(2, game.State.Quests.All.Count);
        Assert.Equal(title.State.Character.Identity, game.State.Character.Identity);
        var introduction = Assert.Single(game.State.Quests.All, quest => quest.SourceFile == "_BRISIEN.txt");
        Assert.Contains(introduction.Resources, resource => resource.Symbol == "dirtypit" && resource.Binding.Places.Length != 0);
        QuestDiseaseTests.Advance(game);
        Assert.True(game.TryTransitionTo(fixture.ExteriorProfile.ProfileKey));
        QuestDiseaseTests.Advance(game);
        var invitation = game.State.Quests.All.Single(quest => quest.SourceFile == "_BRISIEN.txt").Clocks.Single(clock => clock.Symbol == "invitepc");
        Assert.True(invitation.Enabled);
        Assert.InRange(invitation.StartingSeconds, 7 * DaggerfallCalendar.SecondsPerDay, 14 * DaggerfallCalendar.SecondsPerDay);
        using var restored = fixture.Restore(game.CaptureSave());
        Assert.False(restored.RequiresCharacterInitialization);
        Assert.Throws<ArgumentException>(() => restored.CreateNewGame());
        Assert.Equal(game.State.Quests.All.Select(quest => quest.InstanceId).Order(), restored.State.Quests.All.Select(quest => quest.InstanceId).Order());
        restored.AdvanceElapsedTime(invitation.RemainingSeconds - 1);
        Assert.DoesNotContain(restored.State.ItemInstances.UniqueItems, item => item.Value.QuestItemSymbol == "letter1");
        restored.AdvanceElapsedTime(1);
        Assert.Single(restored.State.ItemInstances.UniqueItems, item => item.Value.QuestItemSymbol == "letter1");
        using var delivered = fixture.Restore(restored.CaptureSave());
        QuestDiseaseTests.Advance(delivered);
        Assert.Single(delivered.State.ItemInstances.UniqueItems, item => item.Value.QuestItemSymbol == "letter1");
        Assert.Single(delivered.State.Quests.All, quest => quest.SourceFile == "S0000999.txt");
    }

    [Fact]
    public void Lycanthropy_calendar_opportunity_starts_real_cure_and_does_not_duplicate_active_quest_after_restore()
    {
        using var fixture = SourceBackedGuildBankSessionFixture.Create();
        fixture.Random = CureQuestRandom.Create();
        using var game = fixture.Start(fixture.StartProfile);
        game.State.RacialOverrides.Select(DaggerfallRacialKind.Werewolf, "cure-opportunity", 0);
        var date = DaggerfallSavePayload.Read(game.CaptureSave()).Calendar;
        long minute = new DaggerfallCalendar(date.Year, date.Month, date.Day, date.Hour, date.Minute, date.Second).ToAbsoluteSeconds() / 60;
        long boundary = ((minute + 84L * 1440 - 1) / (84L * 1440)) * (84L * 1440);
        game.AdvanceElapsedTime((boundary - minute + 1) * 60);
        var quest = Assert.Single(game.State.Quests.All, value => value.SourceFile == "$CUREWER.txt");
        Assert.NotEmpty(quest.Resources);
        using var restored = fixture.Restore(game.CaptureSave());
        Assert.False(restored.StartLycanthropyCureQuestOpportunity(boundary + 84L * 1440));
        Assert.Single(restored.State.Quests.All, value => value.SourceFile == "$CUREWER.txt");
    }

    [Theory]
    [InlineData("story-early")]
    [InlineData("story-late")]
    [InlineData("cures")]
    public void Retained_named_corpus_has_no_unsupported_program_lines(string id)
    {
        var receipts = DaggerfallNamedQuestCorpusContent.Read(NamedQuestCorpusTests.Payload(id), TestPayload.Definitions);
        Assert.True(receipts.All(receipt => receipt.Runnable), string.Join('\n', receipts.Where(receipt => !receipt.Runnable)
            .SelectMany(receipt => receipt.Diagnostics.Select(diagnostic => $"{receipt.SourceFile}:{diagnostic.Line}: {diagnostic.Text}: {diagnostic.Reason}"))));
    }
}

internal class CureQuestRandom : SummonRandom
{
    internal new static Rusty.Engine.IRandomService Create() => Create<Rusty.Engine.IRandomService, CureQuestRandom>();
    protected override object? Invoke(System.Reflection.MethodInfo? method, object?[]? args)
    {
        if (method?.Name == nameof(Rusty.Engine.IRandomService.DrawKeyed) && args![0] is Rusty.Engine.KeyedRngRequest request
            && request.ToString().Contains("daggerfall.lycanthropy") && request.ToString().Contains(":quest"))
            return new Rusty.Engine.KeyedRngReceipt(request.Minimum);
        return base.Invoke(method, args);
    }
}
