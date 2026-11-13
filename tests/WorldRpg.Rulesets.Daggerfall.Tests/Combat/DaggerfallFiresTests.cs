using System.Numerics;
using System.Text.Json;
using Rusty.Engine.Mechanics;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>Who a fire burns, and how fast.</summary>
public sealed class DaggerfallFiresTests
{
    private const double Step = 1d / 60d;
    private static readonly DaggerfallHazardTuning Tuning = DaggerfallHazardTuning.Default;

    private static int Stand(DaggerfallFires fires, double seconds, bool inFire = true)
    {
        int lost = 0;
        for (double elapsed = 0d; elapsed < seconds; elapsed += Step) lost += fires.BurnPlayer(inFire, Step);
        return lost;
    }

    [Fact]
    public void Brushing_a_fire_does_little_and_standing_in_it_ramps()
    {
        Assert.InRange(Stand(new(Tuning), .4d), 0, 1);

        DaggerfallFires standing = new(Tuning);
        int first = Stand(standing, 1d);
        int second = Stand(standing, 1d);
        int third = Stand(standing, 1d);

        Assert.True(first < second && second < third, $"{first}, {second}, {third}");
    }

    [Fact]
    public void The_burn_tops_out_and_stepping_out_cools_it()
    {
        DaggerfallFires fires = new(Tuning);
        _ = Stand(fires, 20d);
        Assert.InRange(Stand(fires, 1d), (int)Tuning.PlayerBurnMaximum - 1, (int)Tuning.PlayerBurnMaximum + 1);

        Assert.Equal(0, Stand(fires, 12d, inFire: false));

        Assert.InRange(Stand(fires, .4d), 0, 1); // Fully cooled, it starts low again.
    }

    [Fact]
    public void A_thrown_body_burns_on_after_it_leaves_the_fire_and_then_stops()
    {
        DaggerfallFires fires = new(Tuning);
        fires.Ignite(7);
        int lost = 0;
        for (double elapsed = 0d; elapsed < Tuning.BodyBurnSeconds + 1d; elapsed += Step)
            lost += fires.BurnBodies(Step).Where(burn => burn.Actor == 7).Sum(burn => burn.Damage);

        Assert.InRange(lost, (int)(Tuning.BodyBurnPerSecond * Tuning.BodyBurnSeconds) - 1, (int)(Tuning.BodyBurnPerSecond * Tuning.BodyBurnSeconds) + 1);
        Assert.Empty(fires.BurnBodies(1d));
    }

    [Theory]
    [InlineData(.25d)]
    [InlineData(.5d)]
    [InlineData(.75d)]
    public void Partial_resistance_takes_its_share_of_a_burn_at_the_ordinary_step(double felt)
    {
        DaggerfallFires full = new(Tuning), resisted = new(Tuning);
        full.Ignite(7);
        resisted.Ignite(7);
        int fullLoss = 0, resistedLoss = 0, playerFull = 0, playerResisted = 0;
        DaggerfallFires playerAtFull = new(Tuning), playerResisting = new(Tuning);
        for (int step = 0; step < 300; step++)
        {
            fullLoss += full.BurnBodies(Step).Sum(burn => burn.Damage);
            resistedLoss += resisted.BurnBodies(Step, _ => felt).Sum(burn => burn.Damage);
            playerFull += playerAtFull.BurnPlayer(true, Step);
            playerResisted += playerResisting.BurnPlayer(true, Step, felt);
        }

        Assert.InRange(resistedLoss, fullLoss * felt - 1d, fullLoss * felt + 1d);
        Assert.InRange(playerResisted, playerFull * felt - 1d, playerFull * felt + 1d);
    }

    [Fact]
    public void Full_resistance_stops_a_burn_and_a_weakness_adds_to_it()
    {
        DaggerfallFires body = new(Tuning), player = new(Tuning);
        body.Ignite(7);
        int lost = 0;
        for (int step = 0; step < 300; step++) lost += body.BurnBodies(Step, _ => 0d).Sum(burn => burn.Damage) + player.BurnPlayer(true, Step, 0d);
        Assert.Equal(0, lost);

        StatsComponent weak = new();
        weak.AddStat(StatId.Parse("resistance-fire"), new Stat(-50, -10_000, 10_000));
        Assert.Equal(1.5d, DaggerfallHazards.FireFelt(weak));
        weak.GetStat(StatId.Parse("resistance-fire")).BaseValue = 150;
        Assert.Equal(0d, DaggerfallHazards.FireFelt(weak));
        weak.GetStat(StatId.Parse("resistance-fire")).BaseValue = 25;
        Assert.Equal(.75d, DaggerfallHazards.FireFelt(weak));
    }

    [Theory]
    [InlineData(.5f, 0f, 0f, true)]
    [InlineData(.9f, 0f, 0f, false)] // Beside it.
    [InlineData(0f, 3f, 0f, false)] // On the floor above.
    public void Only_a_body_at_the_fire_is_in_it(float x, float y, float z, bool inFire) =>
        Assert.Equal(inFire, new DaggerfallFires(Tuning).Near(new(x, y, z), [Vector3.Zero]));

    [Theory]
    [InlineData(210, 0, true)] // Brazier.
    [InlineData(210, 1, true)] // Campfire.
    [InlineData(210, 19, true)] // Pedestal brazier.
    [InlineData(210, 6, false)] // Torches are decoration.
    [InlineData(210, 17, false)]
    public void Only_campfires_and_braziers_burn(int archive, int record, bool burns) =>
        Assert.Equal(burns, Tuning.BurningFlats.Contains((archive, record)));

    [Fact]
    public void The_payload_states_the_code_defaults()
    {
        using JsonDocument payload = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(TestData.RepositoryRoot, "content/worldrpg/tuning/daggerfall.defaults/payload.json")));
        DaggerfallHazardTuning loaded = DaggerfallHazardTuning.Read(payload.RootElement.GetProperty("hazards"));

        Assert.True(Tuning.BurningFlats.SetEquals(loaded.BurningFlats));
        Assert.Equal(Tuning with { BurningFlats = loaded.BurningFlats }, loaded);
    }
}
