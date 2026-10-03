using System.Numerics;
using System.Reflection;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Combat;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Crime;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class CrimeCallerSessionTests
{
    [Fact]
    public void Ordinary_civilian_pickpocket_transfers_stolen_gold_once_and_restores_disposition()
    {
        using Fixture f = new(); var s = f.Session; f.Civilian(s);
        ulong gold = s.State.Currency.Read().Gold;
        f.Steal(s, 2);
        var attempt = Assert.Single(s.State.Crime.Attempts);
        Assert.Equal(DaggerfallCrimeAttemptOutcome.PropertyTransferred, attempt.Outcome);
        Assert.Equal(2000, attempt.AffectedActorOrOwnerId);
        Assert.Equal(s.Site.Region, attempt.Region);
        Assert.Equal(gold + 6, s.State.Currency.Read().Gold);
        var stack = Assert.Single(s.State.Inventory.Read().Stacks, value => value.Id.Value.StartsWith("daggerfall.pickpocket."));
        Assert.True(s.State.ItemInstances.RequireStack(DaggerfallItemOwner.Player, stack.Id).Stolen);
        Assert.Equal(1, s.State.Progression.SkillUses["pickpocket"]);
        f.Steal(s, 3);
        Assert.Single(s.State.Crime.Attempts); Assert.Equal(gold + 6, s.State.Currency.Read().Gold);
        using var restored = f.Restore(s.CaptureSave()); f.Steal(restored, 4);
        Assert.Single(restored.State.Crime.Attempts); Assert.Equal(gold + 6, restored.State.Currency.Read().Gold);
        Assert.Equal(1, restored.State.Progression.SkillUses["pickpocket"]);
        Assert.Empty(restored.State.Crime.Incidents);
    }

    [Fact]
    public void Failed_civilian_pickpocket_reports_even_without_visible_witness_and_repeated_activation_is_refused()
    {
        using Fixture f = new(fail: true); var s = f.Session; f.Civilian(s);
        int notifications = 0; s.CrimeReported += _ => notifications++;
        ulong gold = s.State.Currency.Read().Gold;
        f.Steal(s, 2); f.Steal(s, 3);
        var incident = Assert.Single(s.State.Crime.Incidents);
        Assert.Equal(DaggerfallCrimeKind.Pickpocketing, incident.Crime);
        Assert.Equal(DaggerfallCrimeStage.Attempted, incident.Stage);
        Assert.Equal(DaggerfallCrimeWitnessQuery.CompletedWithoutWitnesses, incident.Witnesses.Query);
        Assert.Equal(1, notifications); Assert.Equal(gold, s.State.Currency.Read().Gold);
        Assert.Equal(1, s.State.Progression.SkillUses["pickpocket"]);
        using var restored = f.Restore(s.CaptureSave());
        Assert.Single(restored.State.Crime.Incidents);
    }

    [Fact]
    public void Classic_enemy_mobiles_allow_repeated_attempts_without_legal_incidents()
    {
        using Fixture f = new(fail: true); var s = f.Session;
        f.Steal(s, 2); f.Steal(s, 3);
        Assert.Equal(2, s.State.Crime.Attempts.Count);
        Assert.Empty(s.State.Crime.Incidents);
        Assert.Equal(2, s.State.Progression.SkillUses["pickpocket"]);
        using var restored = f.Restore(s.CaptureSave()); f.Steal(restored, 4);
        Assert.Equal(3, restored.State.Crime.Attempts.Count);
    }

    [Fact]
    public void Successful_nothing_found_does_not_grant_money_or_thieving_credit()
    {
        using Fixture f = new(nothing: true); var s = f.Session; f.Civilian(s);
        ulong gold = s.State.Currency.Read().Gold; f.Steal(s, 2);
        Assert.Equal(gold, s.State.Currency.Read().Gold);
        Assert.Equal(DaggerfallCrimeAttemptOutcome.SucceededWithoutTakingProperty, Assert.Single(s.State.Crime.Attempts).Outcome);
        Assert.Empty(s.State.Crime.Incidents);
        Assert.Contains("nothing valuable", s.Presentation.LastOutcome);
    }

    [Fact]
    public void Rejected_reach_has_no_attempt_or_skill_use_and_talk_does_not_target_enemies()
    {
        using Fixture f = new(); var s = f.Session;
        s.State.PlayerControl.MoveTo(s.State.Actors.Get(2000).Position.ToVector() + 20 * Vector3.UnitZ);
        s.Update(new ProductUpdate(OuterUpdate(1), [Ui("{\"action\":\"activation-mode\",\"mode\":\"steal\"}")]));
        s.Update(new ProductUpdate(OuterUpdate(2), [Input(InputEventKind.DirectDigital, x: 1, phase: InputPhase.DirectUi, intent: "interact")]));
        Assert.False(s.ActivationView.Applied);
        Assert.Empty(s.State.Crime.Attempts);
        Assert.Equal(0, s.State.Progression.SkillUses.GetValueOrDefault("pickpocket"));
        s.Update(new ProductUpdate(OuterUpdate(3), [Ui("{\"action\":\"activation-mode\",\"mode\":\"talk\"}")]));
        AimActivationAt(s, 2000);
        s.Update(new ProductUpdate(OuterUpdate(4), [Input(InputEventKind.DirectDigital, x: 1, phase: InputPhase.DirectUi, intent: "interact")]));
        Assert.False(s.ActivationView.Applied); Assert.Empty(s.State.Crime.Attempts);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Actual_accepted_physical_zero_hit_is_assault_and_lethal_civilian_hit_is_murder(bool lethal)
    {
        using Fixture f = new(); var s = f.Session; f.Civilian(s);
        s.State.Actors.Player.Actor.Get<CombatContributions>().Rules.Add(new ForceDamage(lethal ? 10000 : 0));
        s.ResolveExplicitMelee(new(1, 2000, 8, 10, .125));
        var crime = Assert.Single(s.State.Crime.Incidents);
        Assert.Equal(lethal ? DaggerfallCrimeKind.Murder : DaggerfallCrimeKind.Assault, crime.Crime);
        Assert.Equal(lethal ? DaggerfallCrimeGuildCredit.CivilianMurder : DaggerfallCrimeGuildCredit.None, crime.GuildCredit);
        // A refused repeat of the same admitted attack does not publish another legal consequence.
        s.ResolveExplicitMelee(new(1, 2000, 8, 10, .125));
        Assert.Single(s.State.Crime.Incidents);
        using var restored = f.Restore(s.CaptureSave()); Assert.Single(restored.State.Crime.Incidents);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Civilian_guard_death_is_assault_but_actual_city_watch_death_is_murder(bool cityWatch)
    {
        using Fixture f = new(); var s = f.Session;
        long target = 2000;
        if (cityWatch) target = s.SpawnActor(TestPayload.Definitions.Actors.Values.First(actor => actor.MobileId == 146).Id.Value, new(new(10, 0, 10), 0));
        else f.Civilian(s, "guard");
        s.State.Actors.Player.Actor.Get<CombatContributions>().Rules.Add(new ForceDamage(10000));
        s.ResolveExplicitMelee(new(1, target, 8, 10, .125));
        var incident = Assert.Single(s.State.Crime.Incidents);
        Assert.Equal(DaggerfallCrimeTargetKind.Guard, incident.TargetKind);
        Assert.Equal(cityWatch ? DaggerfallCrimeKind.Murder : DaggerfallCrimeKind.Assault, incident.Crime);
        Assert.Equal(cityWatch ? DaggerfallCrimeGuildCredit.GuardMurder : DaggerfallCrimeGuildCredit.None, incident.GuildCredit);
    }

    [Fact]
    public void Ordinary_pickpocket_reach_uses_the_authored_classic_128_unit_range()
    {
        using Fixture f = new(); var s = f.Session; f.Civilian(s);
        s.Update(new ProductUpdate(OuterUpdate(1), [Ui("{\"action\":\"activation-mode\",\"mode\":\"steal\"}")]));
        s.State.PlayerControl.MoveTo(s.State.Actors.Get(2000).Position.ToVector() + 3.1f * Vector3.UnitZ);
        s.State.PlayerControl.YawRadians = 0; s.State.PlayerControl.PitchRadians = 0;
        s.Update(new ProductUpdate(OuterUpdate(2), [Input(InputEventKind.DirectDigital, x: 1, phase: InputPhase.DirectUi, intent: "interact")]));
        Assert.Single(s.State.Crime.Attempts);
        Assert.Equal(3.2d, s.LastActivationTargeting!.Request.Observers.Span[0].MaximumDistance, precision: 6);
    }

    [Fact]
    public void Missed_attack_does_not_report_assault()
    {
        using Fixture f = new(); var s = f.Session; f.Civilian(s);
        s.State.Actors.Player.Actor.Get<CombatContributions>().Rules.Add(new ForceMiss());
        s.ResolveExplicitMelee(new(1, 2000, 8, 10, .125));
        Assert.Empty(s.State.Crime.Incidents);
    }

    private sealed class ForceDamage(int damage) : ICombatContribution
    {
        public void Hit(TryHitEvent value) => value.Hit = true;
        public void Damage(DamageEvent value) => value.Damage = damage;
    }
    private sealed class ForceMiss : ICombatContribution { public void Hit(TryHitEvent value) => value.Hit = false; }

    private sealed class Fixture : IDisposable
    {
        private readonly DaggerfallSessionComposition composition;
        private readonly IRandomService random;
        internal DaggerfallSession Session { get; }
        internal Fixture(bool fail = false, bool nothing = false)
        {
            random = CrimeRandom.Create(fail, nothing);
            composition = new(TestPayload.Definitions, ReadInputs(TestData.RepositoryRoot), DaggerfallTuning.Defaults);
            Session = DaggerfallSession.StartNew(Engine().Context, composition);
            Session.ApplyProductMode(ProductMode.Playing);
        }
        internal void Civilian(DaggerfallSession s, string role = "civilian")
        {
            var site = s.Site.ActiveSite!;
            s.State.Npcs.Restore([new(2000, DaggerfallNpcKind.Civilian, string.Empty,
                new(site.Id.Region, site.Name, string.Empty), new("Breton", "Female", 0, 0, 0, 0), role,
                ["talk"], DaggerfallNpcPresence.Active, null, null, null)]);
        }
        private EngineContextFake Engine()
        {
            List<string> releases = []; ContentFake content = new(releases); PopulateContent(content, composition.StartSite);
            var spatial = SpatialFake.Create(composition.StartSite.SpatialArtifact.Sha256, releases); spatial.KeepPosition = true;
            var perception = PerceptionFake.Create();
            perception.Responder = request => request.Observers.ToArray().Any(observer => observer.Entity == 1)
                ? Receipt(new PerceptionPair(1, 2000, 1, 1, PerceptionPairKind.Visible, 1)) : Receipt();
            return EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), perception.Service, random: random);
        }
        internal void Steal(DaggerfallSession s, ulong step)
        {
            if (s.ActivationMode != DaggerfallActivationMode.Steal)
                s.Update(new ProductUpdate(OuterUpdate(step - 1), [Ui("{\"action\":\"activation-mode\",\"mode\":\"steal\"}")]));
            AimActivationAt(s, 2000);
            s.Update(new ProductUpdate(OuterUpdate(step), [Input(InputEventKind.DirectDigital, x: 1, phase: InputPhase.DirectUi, intent: "interact")]));
        }
        internal DaggerfallSession Restore(WorldRpg.Kit.RulesetSavePayload saved) => DaggerfallSession.Restore(Engine().Context, composition, saved);
        public void Dispose() => Session.Dispose();
    }
}

internal class CrimeRandom : DispatchProxy
{
    private bool fail, nothing;
    internal static IRandomService Create(bool fail, bool nothing)
    {
        var service = DispatchProxy.Create<IRandomService, CrimeRandom>();
        var fake = (CrimeRandom)(object)service; fake.fail = fail; fake.nothing = nothing; return service;
    }
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        var request = (KeyedRngRequest)args![0]!;
        long value = request.Key.EndsWith(":success") ? (fail ? 100 : 1)
            : request.Key.EndsWith(":valuable") ? (nothing ? 1 : 100) : request.Maximum;
        return new KeyedRngReceipt(value);
    }
}
