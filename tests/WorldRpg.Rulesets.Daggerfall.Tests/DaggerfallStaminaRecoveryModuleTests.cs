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
        player.SpendTrack(Stamina, new ExactValue(10));
        recovery.React(new PlayerAttackStartedFact(7, 13));

        recovery.Update(player, 1.5d);
        Assert.Equal(80, player.ReadTrack(Stamina).Current.Raw);
        recovery.React(new AttackRejectedFact(AttackRejection.Cooldown));
        recovery.Update(player, .5d);
        Assert.Equal(80, player.ReadTrack(Stamina).Current.Raw);
        recovery.Update(player, .2d);
        Assert.Equal(81, player.ReadTrack(Stamina).Current.Raw);
        recovery.Update(player, .8d);
        Assert.Equal(85, player.ReadTrack(Stamina).Current.Raw);

        player.SetTrack(Stamina, new ExactValue(90));
        recovery.Update(player, 20d);
        player.SpendTrack(Stamina, new ExactValue(5));
        recovery.Update(player, .1d);
        Assert.Equal(85, player.ReadTrack(Stamina).Current.Raw);

        player.SetTrack(Stamina, ExactValue.Zero);
        recovery.React(new PlayerAttackStartedFact(7, 14));
        recovery.Update(player, 2d);
        Assert.Equal(0, player.ReadTrack(Stamina).Current.Raw);
        recovery.React(new AttackRejectedFact(AttackRejection.Cooldown));
        recovery.Update(player, 1d);
        Assert.Equal(5, player.ReadTrack(Stamina).Current.Raw);

        player.SetTrack(Health, ExactValue.Zero);
        recovery.Update(player, 20d);
        Assert.Equal(5, player.ReadTrack(Stamina).Current.Raw);
    }

    [Fact]
    public void Recovery_checkpoint_restores_partial_delay_and_fractional_carry()
    {
        using ActorMechanicsState player = PlayerMechanics();
        player.SetTrack(Stamina, ExactValue.Zero);
        DaggerfallStaminaRecoveryModule recovery = new(new DaggerfallStaminaRecoveryTuning(5d, 2d));
        recovery.React(new PlayerAttackStartedFact(7, 13));
        recovery.Update(player, 1d);
        var delayed = recovery.Capture();
        recovery.Update(player, .5d);
        recovery.Restore(delayed);
        recovery.Update(player, 1d);
        Assert.Equal(0, player.ReadTrack(Stamina).Current.Raw);

        recovery.Update(player, .1d);
        var fractional = recovery.Capture();
        recovery.React(new PlayerAttackStartedFact(7, 14));
        recovery.Restore(fractional);
        recovery.Update(player, .1d);
        Assert.Equal(1, player.ReadTrack(Stamina).Current.Raw);
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
