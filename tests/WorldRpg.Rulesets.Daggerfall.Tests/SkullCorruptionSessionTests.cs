using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class SkullCorruptionSessionTests
{
    private static string Skull => TestPayload.Definitions.Magic.MagicItems.Values.Single(item =>
        item.Enchantments.Any(effect => effect.Type == 26 && effect.Param == 8)).Key;

    [Fact]
    public void Ordinary_item_use_creates_fresh_allied_target_type_with_durable_source_meaning_and_encoded_restore()
    {
        using var f = new SanguineRoseSessionTests.Fixture(magicItemKey: Skull);
        int condition = f.Condition;
        var original = f.Session.State.Actors.Get(f.Enemy);
        var originalEntity = original.Actor.Entity;
        f.Use();
        long copy = Assert.Single(f.Allies());
        Assert.NotEqual(f.Enemy, copy);
        Assert.Equal(originalEntity, f.Session.State.Actors.Get(f.Enemy).Actor.Entity);
        Assert.Equal(f.Session.DefinitionsByActor[f.Enemy].MobileId, f.Session.DefinitionsByActor[copy].MobileId);
        Assert.Equal(condition - 100, f.Condition);
        var origin = f.Session.State.Actors.Get(copy).Actor.Get<DaggerfallCorruptionOrigin>();
        Assert.Equal(new(f.Enemy, f.Source), origin);
        using var restored = f.Restore();
        Assert.Equal(origin, restored.State.Actors.Get(copy).Actor.Get<DaggerfallCorruptionOrigin>());
        Assert.Equal("player-ally", restored.DefinitionsByActor[copy].Team);
        Assert.Equal(f.Session.State.Actors.Get(copy).Pose, restored.State.Actors.Get(copy).Pose);
        Assert.Equal(f.Condition, restored.State.ItemInstances.RequireUnique(f.Source).CurrentCondition);
    }

    [Fact]
    public void Nearest_enemy_is_selected_and_copy_survives_target_retirement()
    {
        using var f = new SanguineRoseSessionTests.Fixture(magicItemKey: Skull);
        var definition = TestPayload.Definitions.Actors.Values.Single(actor => actor.MobileId == 0);
        long target = f.Session.SpawnActor(definition.Id.Value, new(new WorldPoint(0, 0, -2), 0));
        f.Update();
        f.Use();
        Assert.True(f.Allies().Any(), f.Message);
        long copy = Assert.Single(f.Allies());
        Assert.Equal(0, f.Session.DefinitionsByActor[copy].MobileId);
        Assert.Equal(target, f.Session.State.Actors.Get(copy).Actor.Get<DaggerfallCorruptionOrigin>().ActorId);
        f.Session.RetireActor(target);
        using var restored = f.Restore();
        Assert.False(restored.State.Actors.TryGet(target, out _));
        Assert.True(restored.State.Actors.TryGet(copy, out _));
        Assert.Equal(target, restored.State.Actors.Get(copy).Actor.Get<DaggerfallCorruptionOrigin>().ActorId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Missing_target_or_blocked_ground_refuses_without_charging_or_spawning(bool blocked)
    {
        using var f = new SanguineRoseSessionTests.Fixture(magicItemKey: Skull);
        if (blocked) f.Spatial.FloorHit = _ => default;
        else f.Session.State.Actors.Get(f.Enemy).ApplyPose(new(new WorldPoint(0, 0, -13), 0));
        int condition = f.Condition;
        int actors = f.Session.State.Actors.All.Count();
        f.Use();
        Assert.Empty(f.Allies());
        Assert.Equal(actors, f.Session.State.Actors.All.Count());
        Assert.Equal(condition, f.Condition);
        Assert.Contains(blocked ? "no clear ground" : "No monsters nearby", f.Message);
    }

    [Fact]
    public void Exactly_twelve_metres_and_existing_allies_are_not_eligible_sources()
    {
        using var f = new SanguineRoseSessionTests.Fixture(magicItemKey: Skull);
        f.Use(); long copy = Assert.Single(f.Allies());
        f.Session.State.Actors.Get(f.Enemy).ApplyPose(new(new WorldPoint(0, 2, -12), 0));
        f.Session.State.Actors.Get(copy).ApplyPose(new(new WorldPoint(0, 2, -2), 0));
        int condition = f.Condition;
        f.Use();
        Assert.Single(f.Allies()); Assert.Equal(condition, f.Condition);
        Assert.Equal("No monsters nearby.", f.Message);
    }

    [Fact]
    public void Copy_origin_and_identity_resume_once_after_site_unload_and_encoded_restore()
    {
        using var f = new SanguineRoseSessionTests.Fixture(magicItemKey: Skull);
        f.Use(); long copy = Assert.Single(f.Allies());
        var origin = f.Session.State.Actors.Get(copy).Actor.Get<DaggerfallCorruptionOrigin>();
        var profiles = new DaggerfallSiteProfiles([f.Inputs, f.Castle]);
        f.Session.AdmitSiteProfiles(profiles);
        Assert.True(f.Session.TryTransitionTo(f.Castle.ProfileKey));
        Assert.False(f.Session.State.Actors.TryGet(copy, out _));
        using var restored = f.Restore(profiles);
        Assert.True(restored.TryTransitionTo(f.Inputs.ProfileKey));
        Assert.Equal(origin, restored.State.Actors.Get(copy).Actor.Get<DaggerfallCorruptionOrigin>());
        Assert.Equal("player-ally", restored.DefinitionsByActor[copy].Team);
        Assert.Single(restored.DynamicActors.Where(actor => actor.Key == copy));
    }

    [Fact]
    public void Protected_nearest_quest_target_refuses_without_duplication_and_keeps_quest_binding()
    {
        using var f = new SanguineRoseSessionTests.Fixture(magicItemKey: Skull);
        var definitions = TestPayload.Definitions;
        var declaration = definitions.QuestSources.Resources.First(resource => resource.Kind == "foe"
            && definitions.QuestSources.Quests[resource.SourceFile].Disposition == DaggerfallQuestDisposition.Compiled
            && !definitions.QuestSources.Resources.Any(other => other.SourceFile == resource.SourceFile && other.Kind is "place" or "person")
            && !definitions.QuestSources.UnresolvedReferences.Any(link => link.SourceFile == resource.SourceFile)
            && !DaggerfallQuestClockCompiler.Compile(definitions.QuestSources.Resolve(resource.SourceFile)).Any(DaggerfallQuestClockCompiler.UsesTravelDuration));
        var quest = definitions.QuestSources.Quests[declaration.SourceFile];
        f.Session.State.Quests.Start(new("skull-protected", declaration.SourceFile, quest.Name, DaggerfallQuestLifecycle.Active, null,
            [new(declaration.CanonicalId, DaggerfallQuestResourceBinding.Actors(f.Enemy))], []));
        int condition = f.Condition;
        using var protectedSave = f.Restore();
        Assert.True(protectedSave.State.Quests.ProtectsActor(f.Enemy));
        f.Use();
        Assert.Empty(f.Allies()); Assert.Equal(condition, f.Condition);
        Assert.Contains("protected quest target", f.Message);

    }

    [Fact]
    public void Source_break_and_removal_leave_copy_alive_until_its_normal_actor_retirement()
    {
        using var f = new SanguineRoseSessionTests.Fixture(magicItemKey: Skull);
        f.Session.State.ItemInstances.ReplaceUnique(f.Source,
            f.Session.State.ItemInstances.RequireUnique(f.Source) with { CurrentCondition = 100 });
        f.Update();
        f.Use();
        Assert.True(f.Allies().Any(), f.Message); long copy = Assert.Single(f.Allies());
        Assert.Equal(0, f.Condition); Assert.Contains("broke", f.Message);
        f.Use(); Assert.Single(f.Allies()); Assert.Contains("broken", f.Message);
        f.Session.State.Inventory.Destroy(f.Item);
        f.Session.State.Actors.Entities.Destroy(f.Session.State.Actors.Entities.IdentityOf(new(f.Item.EntityId)));
        f.Session.State.ItemInstances.RemoveUnique(f.Source);
        f.Session.RemoveUniqueItemIdentity(f.Source);
        f.Use(); Assert.Single(f.Allies());
        using var restored = f.Restore();
        Assert.Equal(new(f.Enemy, f.Source), restored.State.Actors.Get(copy).Actor.Get<DaggerfallCorruptionOrigin>());
        f.Session.RetireActor(copy);
        Assert.False(f.Session.State.Actors.TryGet(copy, out _));
        Assert.DoesNotContain(copy, f.Session.DefinitionsByActor.Keys);
        using var retired = f.Restore();
        Assert.False(retired.State.Actors.TryGet(copy, out _));
    }
}
