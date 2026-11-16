using Rusty.Engine;
using WorldRpg.Host;
using WorldRpg.Kit;
using WorldRpg.Kit.Combat;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// An attack cooldown counts the steps the world takes. The Engine goes on admitting steps while the product
/// is paused or a screen holds the world, and none of those age a charged cooldown.
/// </summary>
public sealed class HeldCooldownSessionTests
{
    private const ulong Charged = 30;
    private const ulong Held = 100;

    [Fact]
    public void A_charged_cooldown_holds_across_a_pause_and_behind_the_game_menu()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        EngineContextFake engine = EngineContextFake.Create(content,
            SpatialFake.Create(inputs.Geometry.Spatial[0].Sha256, releases).Service, new AppearanceFake(releases));
        ProductInputConfiguration input = new(default, default, ReadOnlyMemory<ProductInputDescriptor>.Empty, ReadOnlyMemory<ProductInputMapping>.Empty);
        using WorldRpgProduct product = new(new ProductCreateContext(engine.Context, FullContent(root), input),
            new TestDaggerfallRuleset(videosEnabled: false), new GameBundleId("daggerfall.classic"));
        product.Start();
        NewGameSessionTests.Commit(RunningSession(product));
        product.Begin();
        DaggerfallSession session = RunningSession(product);
        ulong step = 1;
        void Update(params ProductInputEvent[] events) => product.Update(new ProductUpdate(OuterUpdate(step++), events));
        ulong Remaining(ulong at) => Assert.Single(session.State.Kit.AttackExecution.CaptureCooldowns(1, at),
            cooldown => cooldown.AttackerId == DaggerfallActorIdentity.PlayerEntityId).RemainingSteps;

        // Charged on the step the world takes next: thirty steps to run from there.
        session.State.Kit.AttackExecution.RestoreCooldowns([new AttackCooldown(DaggerfallActorIdentity.PlayerEntityId, Charged)]);
        Update();
        Assert.Equal(Charged, Remaining(step - 1));

        // A pause admits nothing to the product, though the Engine admits a hundred steps meanwhile.
        product.Pause();
        for (ulong i = 0; i < Held; i++) Update();
        product.Resume();
        Update();
        Assert.Equal(Charged - 1, Remaining(step - 1));

        // The game menu holds the world as a modal from the update that opens it to the one that closes it, while
        // those updates still reach the session; the world's one step after it is the only one this cooldown runs.
        Update(Ui("""{"action":"menu","open":true}"""));
        Assert.Equal(ProductMode.Modal, product.Mode);
        for (ulong i = 0; i < Held; i++) Update();
        Update(Ui("""{"action":"menu","open":false}"""));
        Assert.Equal(ProductMode.Playing, product.Mode);
        Update();
        Assert.Equal(Charged - 2, Remaining(step - 1));
    }
}
