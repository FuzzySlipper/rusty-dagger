using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallStaminaRecoveryModuleTests
{
    private static readonly TrackId Health = TrackId.Parse(DaggerfallMechanicsIds.Health.Value);
    private static readonly TrackId Stamina = TrackId.Parse(DaggerfallMechanicsIds.Stamina.Value);

    [Fact]
    public void Admitted_player_swing_delays_fractional_recovery_without_banking_at_full_or_recovering_when_dead()
    {
        using ActorMechanicsState player = PlayerMechanics();
        DaggerfallStaminaRecoveryModule recovery = new(new DaggerfallStaminaRecoveryTuning(5d, 2d));
        player.ReadTrack(Stamina).Spend(10);
        recovery.React(new PlayerAttackStartedFact(7, 13));

        recovery.Update(player, 1.5d);
        Assert.Equal(80d, player.ReadTrack(Stamina).Current);
        recovery.React(new AttackRejectedFact(AttackRejection.Cooldown));
        recovery.Update(player, .5d);
        Assert.Equal(80d, player.ReadTrack(Stamina).Current);
        recovery.Update(player, .2d);
        Assert.Equal(81d, player.ReadTrack(Stamina).Current);
        recovery.Update(player, .8d);
        Assert.Equal(85d, player.ReadTrack(Stamina).Current);

        player.ReadTrack(Stamina).SetCurrent(90);
        recovery.Update(player, 20d);
        player.ReadTrack(Stamina).Spend(5);
        recovery.Update(player, .1d);
        Assert.Equal(85d, player.ReadTrack(Stamina).Current);

        player.ReadTrack(Stamina).SetCurrent(0);
        recovery.React(new PlayerAttackStartedFact(7, 14));
        recovery.Update(player, 2d);
        Assert.Equal(0d, player.ReadTrack(Stamina).Current);
        recovery.React(new AttackRejectedFact(AttackRejection.Cooldown));
        recovery.Update(player, 1d);
        Assert.Equal(5d, player.ReadTrack(Stamina).Current);

        player.ReadTrack(Health).SetCurrent(0);
        recovery.Update(player, 20d);
        Assert.Equal(5d, player.ReadTrack(Stamina).Current);
    }

    [Fact]
    public void Recovery_checkpoint_restores_partial_delay_and_fractional_carry()
    {
        using ActorMechanicsState player = PlayerMechanics();
        player.ReadTrack(Stamina).SetCurrent(0);
        DaggerfallStaminaRecoveryModule recovery = new(new DaggerfallStaminaRecoveryTuning(5d, 2d));
        recovery.React(new PlayerAttackStartedFact(7, 13));
        recovery.Update(player, 1d);
        var delayed = recovery.Capture();
        recovery.Update(player, .5d);
        recovery.Restore(delayed);
        recovery.Update(player, 1d);
        Assert.Equal(0d, player.ReadTrack(Stamina).Current);

        recovery.Update(player, .1d);
        var fractional = recovery.Capture();
        recovery.React(new PlayerAttackStartedFact(7, 14));
        recovery.Restore(fractional);
        recovery.Update(player, .1d);
        Assert.Equal(1d, player.ReadTrack(Stamina).Current);
    }

    private static ActorMechanicsState PlayerMechanics()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        DaggerfallActorDefinition player = definitions.RequireActor(new DaggerfallActorId("player"));
        return new DaggerfallMechanicsState().CreateActor(player, player.PlayerInitialVitals, 1);
    }

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md"))) return current.FullName;
        throw new InvalidOperationException("Could not locate the Rusty Dagger repository root.");
    }
}
