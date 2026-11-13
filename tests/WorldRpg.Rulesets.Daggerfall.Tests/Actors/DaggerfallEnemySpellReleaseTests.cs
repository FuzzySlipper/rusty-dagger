using System.Numerics;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Modules.Behavior;
using WorldRpg.Rulesets.Daggerfall.Policies;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>An enemy's spell release built alone over Magic's owners; the swept flight and touch reach are the session's (CastingSessionTests).</summary>
public sealed class DaggerfallEnemySpellReleaseTests
{
    [Fact]
    public void An_enemy_releases_a_spell_it_can_ready_onto_itself_and_refuses_one_it_cannot()
    {
        using MagicCastFixture f = new();
        DaggerfallEnemySpellRelease release = new(f.Casting, f.Targets, _ => throw new InvalidOperationException("No touch spell here."),
            null!, null!, () => default);
        WorldPoint at = new(3f, 0f, 0f);

        DaggerfallCastResult refused = release.Execute(new(MagicCastFixture.Monster, "spell.unknown", DaggerfallSpellTarget.CasterOnly, at, at, Vector3.UnitZ));
        Assert.NotEqual(DaggerfallCastOutcome.Ready, refused.Outcome);
        Assert.Null(refused.Bundle);

        DaggerfallCastResult cast = release.Execute(new(MagicCastFixture.Monster, f.SpellKey, DaggerfallSpellTarget.CasterOnly, at, at, Vector3.UnitZ));
        Assert.Equal(MagicCastFixture.Monster, cast.Bundle!.CasterId);
        Assert.Equal(MagicCastFixture.Monster, Assert.Single(cast.Bundle.Results).TargetId);
    }

    [Fact]
    public void Only_a_ranged_spell_needs_a_clear_path_and_one_aimed_inside_the_arm_never_has_one()
    {
        using MagicCastFixture f = new();
        DaggerfallEnemySpellRelease release = new(f.Casting, f.Targets, _ => default, null!, null!, () => default);
        WorldPoint at = new(3f, 0f, 0f);
        Assert.True(release.RangedPathClear(new(MagicCastFixture.Monster, f.SpellKey, DaggerfallSpellTarget.ByTouch, at, at, Vector3.UnitZ)));
        Assert.False(release.RangedPathClear(new(MagicCastFixture.Monster, f.SpellKey, DaggerfallSpellTarget.SingleTargetAtRange, at, at, Vector3.UnitZ)));
        Assert.False(release.RangedPathClear(new(MagicCastFixture.Monster, f.SpellKey, DaggerfallSpellTarget.SingleTargetAtRange, at,
            new WorldPoint(3f, 0f, DaggerfallDungeonSpellPolicy.MissileArmLengthMetres / 2f), Vector3.UnitZ)));
    }
}
