using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class NormalLightSessionTests
{
    [Fact]
    public void Cast_extends_one_light_restore_rebuilds_and_expiry_releases_light_and_candle()
    {
        using var f = new Fixture();
        int initial = f.Graphics.LightRequests.Count;
        Cast(f.Session);
        f.Session.Update(new ProductUpdate(OuterUpdate(1), []));
        Assert.Equal(initial + 1, f.Graphics.LightRequests.Count);
        var light = f.Graphics.LightRequests[^1];
        Assert.Equal(15f, light.Descriptor.Range);
        Assert.Equal(1f, light.Descriptor.Intensity);
        var active = Assert.Single(f.Session.State.Effects.Active);
        uint rounds = active.Lifecycle.RemainingRounds!.Value;
        Cast(f.Session);
        Assert.Same(active, Assert.Single(f.Session.State.Effects.Active));
        Assert.True(active.Lifecycle.RemainingRounds > rounds);
        f.Session.Update(new ProductUpdate(OuterUpdate(2), []));
        Assert.Equal(initial + 1, f.Graphics.LightRequests.Count);
        Assert.Contains(f.Graphics.LightUpdates, update => update.Replacement.LogicalId == light.LogicalId);
        using var restored = new Fixture(f.Session.CaptureSave());
        Assert.Single(restored.Session.State.Effects.Active);
        restored.Session.Update(new ProductUpdate(OuterUpdate(1), []));
        Assert.Equal(initial + 1, restored.Graphics.LightRequests.Count);
        int disposed = restored.Graphics.DisposedLights;
        uint remaining = Assert.Single(restored.Session.State.Effects.Active).Lifecycle.RemainingRounds!.Value;
        restored.Session.AdvanceElapsedTime((remaining + 1) * 60);
        restored.Session.Update(new ProductUpdate(OuterUpdate(3), []));
        Assert.Empty(restored.Session.State.Effects.Active);
        Assert.Equal(disposed + 1, restored.Graphics.DisposedLights);
        restored.Session.Update(new ProductUpdate(OuterUpdate(4), []));
        Assert.True(restored.Graphics.DisposedAppearances > 0);
    }

    [Fact]
    public void Invalid_target_refuses_and_cancel_disposes_the_caster_light()
    {
        using var f = new Fixture();
        var invalid = Release(f.Session);
        Assert.Equal(DaggerfallCastOutcome.InvalidTarget, f.Session.Casting.Deliver(invalid, [2000]).Outcome);
        Assert.Empty(f.Session.State.Effects.Active);
        Cast(f.Session);
        f.Session.Update(new ProductUpdate(OuterUpdate(1), []));
        int disposed = f.Graphics.DisposedLights;
        f.Session.State.Effects.Cancel(Assert.Single(f.Session.State.Effects.Active).Context.Instance);
        f.Session.Update(new ProductUpdate(OuterUpdate(2), []));
        Assert.Equal(disposed + 1, f.Graphics.DisposedLights);
    }

    [Fact]
    public void Site_transition_retires_old_light_and_candle_and_recreates_the_active_effect_at_destination()
    {
        using var f = new Fixture(twoSites: true);
        Cast(f.Session); f.Session.Update(new ProductUpdate(OuterUpdate(1), []));
        ulong oldLight = f.Graphics.LightRequests[^1].LogicalId;
        int disposed = f.Graphics.DisposedLights;
        Assert.True(f.Session.TryTransitionTo(f.Destination!.ProfileKey));
        f.Session.Update(new ProductUpdate(OuterUpdate(2), []));
        Assert.Single(f.Session.State.Effects.Active);
        Assert.True(f.Graphics.DisposedLights > disposed);
        Assert.NotEqual(oldLight, f.Graphics.LightRequests[^1].LogicalId);
        Assert.Equal(15f, f.Graphics.LightRequests[^1].Descriptor.Range);
        f.Session.Dispose();
        Assert.Equal(f.Graphics.LightRequests.Count, f.Graphics.DisposedLights);
    }

    private static void Cast(DaggerfallSession s) => s.Casting.Deliver(Release(s), [DaggerfallActorIdentity.PlayerEntityId]);
    private static DaggerfallLiveSpell Release(DaggerfallSession s)
    {
        var spell = TestPayload.Definitions.Magic.Spells.Values.First(x => x.RangeType == 0 && x.Effects.Count == 1 && x.Effects[0].Type == 15);
        s.State.Character.LearnSpell(spell.Key);
        var mana = s.State.Actors.Player.Stats.GetTrack(TrackId.Parse("magicka"));
        mana.Maximum.BaseValue = 10000; mana.SetCurrent(10000);
        Assert.Equal(DaggerfallCastOutcome.Ready, s.ReadyPlayerSpell(spell.Key).Outcome);
        return Assert.IsType<DaggerfallLiveSpell>(s.Casting.Release(1, true).Bundle);
    }
    private sealed class Fixture : IDisposable
    {
        internal DaggerfallSession Session { get; }
        internal AppearanceFake Graphics { get; }
        internal DaggerfallSiteProfile? Destination { get; }
        internal Fixture(RulesetSavePayload? save = null, bool twoSites = false)
        {
            var inputs = ReadInputs(TestData.RepositoryRoot); List<string> releases = [];
            ContentFake content = new(releases); PopulateContent(content, inputs);
            if (twoSites)
            {
                Destination = ReadProfile(TestData.RepositoryRoot, FullContent(TestData.RepositoryRoot), TestPayload.Definitions, "daggerfall.castle-necromoghan.json");
                PopulateContent(content, Destination);
            }
            Graphics = new(releases) { RejectDisposeOfRetainedAppearance = true };
            var engine = EngineContextFake.Create(content, SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases).Service, Graphics);
            var composition = new DaggerfallSessionComposition(TestPayload.Definitions, inputs, DaggerfallTuning.Defaults);
            Session = save is null ? DaggerfallSession.StartNew(engine.Context, composition) : DaggerfallSession.Restore(engine.Context, composition, save);
            if (Destination is not null) Session.AdmitSiteProfiles(new([inputs, Destination]));
        }
        public void Dispose() => Session.Dispose();
    }
}
