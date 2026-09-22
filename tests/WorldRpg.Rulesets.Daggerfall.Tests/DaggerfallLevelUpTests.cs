using System.Reflection;
using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Progression;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallLevelUpTests
{
    [Fact]
    public void Eligible_rest_level_stages_points_then_commits_permanent_attributes_and_one_resolved_health_gain()
    {
        (DaggerfallLevelUpState levelUps, ProgressionState progression, StatsComponent stats, _) = Eligible();
        double strength = stats.GetStat(StatId.Parse("strength")).BaseValue;
        long healthMaximum = stats.GetTrack(TrackId.Parse("health")).Maximum.ValueInt64;
        long healthCurrent = stats.GetTrack(TrackId.Parse("health")).ValueInt64;

        Assert.True(levelUps.BeginIfEligible());
        DaggerfallLevelUpSave pending = Assert.IsType<DaggerfallLevelUpSave>(levelUps.Pending);
        Assert.Equal(4, pending.BonusPool);
        Assert.True(pending.HealthGain >= 1);
        Assert.False(levelUps.BeginIfEligible());
        for (int point = 0; point < pending.BonusPool; point++) levelUps.Allocate("strength");
        Assert.True(Assert.IsType<DaggerfallLevelUpPresentation>(levelUps.Read()).CanCommit);

        levelUps.Commit();

        Assert.Null(levelUps.Pending);
        Assert.Equal(2, progression.Level);
        Assert.Equal(strength + pending.BonusPool, stats.GetStat(StatId.Parse("strength")).BaseValue);
        Assert.Equal(healthMaximum + pending.HealthGain, stats.GetTrack(TrackId.Parse("health")).Maximum.ValueInt64);
        Assert.Equal(healthCurrent + pending.HealthGain, stats.GetTrack(TrackId.Parse("health")).ValueInt64);
        Assert.Single(stats.GetStat(StatId.Parse("health-maximum")).Sources,
            source => DaggerfallLevelUpHealthSource.IsForLevel(source, new EntityId(DaggerfallActorIdentity.PlayerEntityId), 2));
        Assert.Throws<ArgumentException>(() => levelUps.Commit());
    }

    [Fact]
    public void Pending_allocation_and_resolved_health_survive_restore_without_a_second_random_draw()
    {
        (DaggerfallLevelUpState original, _, _, _) = Eligible();
        Assert.True(original.BeginIfEligible());
        original.Allocate("strength");
        DaggerfallLevelUpSave pending = Assert.IsType<DaggerfallLevelUpSave>(original.Capture());

        (DaggerfallLevelUpState restored, ProgressionState progression, StatsComponent stats, RecordingRandom random) = Eligible();
        restored.Restore(pending);
        Assert.Equal(pending, restored.Pending);
        Assert.Empty(random.Requests);
        while (Assert.IsType<DaggerfallLevelUpSave>(restored.Pending).Allocations.Sum(item => item.Points) < pending.BonusPool)
            restored.Allocate("strength");

        restored.Commit();

        Assert.Empty(random.Requests);
        Assert.Equal(2, progression.Level);
        Assert.Single(stats.GetStat(StatId.Parse("health-maximum")).Sources,
            source => DaggerfallLevelUpHealthSource.IsForLevel(source, new EntityId(DaggerfallActorIdentity.PlayerEntityId), 2));
    }

    [Fact]
    public void Exhausted_attribute_capacity_allows_the_pending_level_to_complete()
    {
        (DaggerfallLevelUpState levelUps, ProgressionState progression, StatsComponent stats, _) = Eligible();
        foreach (string attribute in new[] { "strength", "intelligence", "willpower", "agility", "endurance", "personality", "speed", "luck", "reflexes" })
            stats.GetStat(StatId.Parse(attribute)).BaseValue = 100;

        Assert.True(levelUps.BeginIfEligible());
        DaggerfallLevelUpPresentation view = Assert.IsType<DaggerfallLevelUpPresentation>(levelUps.Read());
        Assert.Equal(view.BonusPool, view.RemainingPoints);
        Assert.True(view.CanCommit);
        Assert.All(view.Attributes, attribute => Assert.False(attribute.CanAllocate));

        levelUps.Commit();

        Assert.Equal(2, progression.Level);
    }

    private static (DaggerfallLevelUpState LevelUps, ProgressionState Progression, StatsComponent Stats, RecordingRandom Random) Eligible()
    {
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(RepositoryRoot(), "content/worldrpg/payloads/daggerfall.base.json")));
        DaggerfallActorDefinition player = definitions.RequireActor(new DaggerfallActorId("player"));
        DaggerfallCareerDefinition career = definitions.Catalogs.RequireCareer("class00");
        ProgressionState progression = new();
        StatsComponent stats = new DaggerfallMechanicsState().CreateStats(player, DaggerfallPlayerVitals.Initial(player.Stats, career));
        foreach (string skill in new[] { "mysticism", "alteration", "thaumaturgy", "illusion", "destruction", "restoration", "medical", "short-blade", "blunt-weapon", "dragonish", "daedric", "dodging" })
            stats.GetStat(StatId.Parse(skill)).BaseValue = 10;
        stats.GetStat(StatId.Parse("reflexes")).BaseValue = 2;
        DaggerfallSkillUseReactions skills = new(progression, stats, definitions, player);
        progression.TallySkillUse("mysticism", DaggerfallSkillUseReactions.MaximumSkillUses, DaggerfallSkillUseReactions.MaximumSkillUses);
        progression.TallySkillUse("destruction", DaggerfallSkillUseReactions.MaximumSkillUses, DaggerfallSkillUseReactions.MaximumSkillUses);
        Assert.True(skills.RaiseSkills(361));
        Assert.True(skills.PendingLevelUp);
        RecordingRandom random = new();
        DaggerfallRewardReactions rewards = new(progression, stats, new EntityId(DaggerfallActorIdentity.PlayerEntityId), () => career, random.Service,
            new Dictionary<long, DaggerfallActorDefinition>());
        return (new DaggerfallLevelUpState(progression, skills, stats, definitions, () => career, random.Service, rewards), progression, stats, random);
    }

    private sealed class RecordingRandom
    {
        internal List<KeyedRngRequest> Requests { get; } = [];
        internal IRandomService Service
        {
            get
            {
                Proxy proxy = (Proxy)(object)DispatchProxy.Create<IRandomService, Proxy>();
                proxy.Initialize(this);
                return (IRandomService)(object)proxy;
            }
        }

        private class Proxy : DispatchProxy
        {
            private RecordingRandom _owner = null!;
            internal void Initialize(RecordingRandom owner) => _owner = owner;
            protected override object? Invoke(MethodInfo? method, object?[]? arguments)
            {
                if (method?.Name != nameof(IRandomService.DrawKeyed)) throw new NotSupportedException(method?.Name);
                KeyedRngRequest request = (KeyedRngRequest)arguments![0]!;
                _owner.Requests.Add(request);
                return new KeyedRngReceipt(request.Minimum);
            }
        }
    }

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md"))) return current.FullName;
        throw new InvalidOperationException("Could not locate the Rusty Dagger repository root.");
    }
}
