using System.Numerics;
using System.Reflection;
using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Crime;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class LawSessionTests
{
    [Fact]
    public void Interior_report_spawns_real_watch_at_entrance_and_guard_death_keeps_the_case()
    {
        using var f = new Fixture(); var s = f.Session;
        f.Spatial.FloorHit = request => request.Direction.Y < 0 ? default(SpatialHit) with
        { Present = true, Point = new(request.Origin.X, 0, request.Origin.Z), Normal = Vector3.UnitY, Converged = true } : default;
        f.Charge(); f.Step();
        var response = Assert.Single(s.State.Crime.LegalResponses);
        Assert.InRange(response.Guards.Length, 2, 5);
        Assert.All(response.Guards, id => Assert.Equal(146, s.DefinitionsByActor[id].MobileId));
        Assert.NotEmpty(f.Spatial.OverlapRequests);
        Assert.All(f.Spatial.FloorProbes.Where(probe => probe.Direction.Y == 0), probe => Assert.InRange(probe.MaxDistance, 0, 3));
        Assert.Equal("arrest", s.LegalView!.Phase);
        f.Choose("resist");
        foreach (long id in response.Guards) s.State.Actors.Get(id).Stats.GetTrack(TrackId.Parse("health")).SetCurrent(0);
        f.Step();
        Assert.Equal(DaggerfallLegalPhase.Escaped, s.State.Crime.LegalResponses.Single().Phase);
        Assert.Single(s.State.Crime.PendingCharges(response.Region));
        using var restored = f.Restore(s.CaptureSave());
        Assert.Equal(response.Guards, restored.State.Crime.LegalResponses.Single().Guards);
    }

    [Fact]
    public void Witnessed_charge_opens_real_guard_arrest_resistance_releases_world_and_site_return_rearrests_same_guards()
    {
        using var f = new Fixture(); var s = f.Session;
        long guard = f.Guard(); f.Charge(); f.Step();
        Assert.Equal("arrest", s.LegalView!.Phase);
        string old = s.LegalView.Revision;
        Assert.Equal(ProductMode.Modal, s.PendingModeRequest);
        long pausedAt = Seconds(s);
        f.Step();
        Assert.Equal(pausedAt, Seconds(s)); // The session holds immediately, before the host acknowledges the modal mode.
        f.Choose("resist");
        Assert.Null(s.LegalView); Assert.Null(s.PendingModeRequest);
        s.ChooseLegal(old, "yield"); Assert.Null(s.LegalView);
        Assert.True(s.TryTransitionTo(f.Other.ProfileKey));
        Assert.Equal(DaggerfallLegalPhase.Escaped, s.State.Crime.LegalResponses.Single().Phase);
        Assert.True(s.TryTransitionTo(f.Inputs.ProfileKey));
        Assert.True(s.State.Actors.TryGet(guard, out _));
        f.Step();
        Assert.Equal("arrest", s.LegalView!.Phase);
        Assert.Contains(guard, s.State.Crime.LegalResponses.Single().Guards);
        s.ChooseLegal(old, "yield");
        Assert.Equal("arrest", s.LegalView!.Phase); // A choice from the first arrest cannot surrender at the later one.
        Assert.Single(s.State.Crime.Incidents);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Immediate_touch_spell_uses_actual_damage_and_crime_identities_and_repeat_delivery_is_deduplicated(bool lethal)
    {
        using var f = new Fixture(); var s = f.Session;
        long target = f.Civilian(); f.Guard();
        var health = s.State.Actors.Get(target).Stats.GetTrack(TrackId.Parse("health"));
        health.Maximum.BaseValue = 1000; health.SetCurrent(lethal ? 1 : 1000);
        var mana = s.State.Actors.Player.Stats.GetTrack(TrackId.Parse("magicka")); mana.Maximum.BaseValue = 10000; mana.SetCurrent(10000);
        s.State.Character.LearnSpell("custom-spell.1");
        Assert.Equal(DaggerfallCastOutcome.Ready, s.ReadyPlayerSpell("custom-spell.1").Outcome);
        f.Spatial.CapsuleCastHit = _ => default(SpatialHit) with { Present = true, Kind = SpatialHitKind.Entity, Entity = s.State.Actors.Get(target).Actor.Entity.Value };
        var result = s.ReleaseReadySpell(1, Vector3.UnitZ);
        Assert.Equal(DaggerfallCastOutcome.DeliveryCompleted, result.Outcome);
        Assert.True(health.Current < (lethal ? 1 : 1000));
        var incident = Assert.Single(s.State.Crime.Incidents);
        Assert.Equal(target, incident.AffectedActorOrOwnerId);
        Assert.Equal(lethal ? DaggerfallCrimeKind.Murder : DaggerfallCrimeKind.Assault, incident.Crime);
        Assert.Equal(DaggerfallCrimeWitnessQuery.CompletedWithWitnesses, incident.Witnesses.Query);
        Assert.Equal(DaggerfallCastOutcome.AlreadyDelivered, s.Casting.Deliver(result.Bundle!, [target]).Outcome);
        Assert.Single(s.State.Crime.Incidents);
        using var restored = f.Restore(s.CaptureSave()); Assert.Single(restored.State.Crime.PendingCharges(incident.Region));
    }

    [Fact]
    public void Court_plea_save_judgment_and_prison_apply_fine_once_and_shared_days_then_release_controls()
    {
        using var f = new Fixture(); var s = f.Session; f.Guard(); f.Charge(); f.Step();
        f.Choose("yield"); Assert.Equal("court", s.LegalView!.Phase);
        var current = s.State.Crime.LegalResponses.Single();
        // Exercise the actual court response and settlement with a fixed already-accused penalty.
        s.State.Crime.SetResponse(current with { Penalty = new(40, 4, false) });
        ulong gold = s.State.Currency.Read().Gold;
        f.Choose("guilty"); Assert.Equal("sentence", s.LegalView!.Phase);
        var save = s.CaptureSave();
        using var restored = f.Restore(save);
        long before = Seconds(restored);
        var sentence = restored.State.Crime.LegalResponses.Single().Sentence!;
        string revision = restored.LegalView!.Revision;
        restored.ChooseLegal(revision, "accept");
        Assert.Equal(gold - (ulong)sentence.Fine, restored.State.Currency.Read().Gold);
        Assert.Equal("prison", restored.LegalView!.Phase);
        restored.ChooseLegal(revision, "accept");
        Assert.Equal(gold - (ulong)sentence.Fine, restored.State.Currency.Read().Gold);
        using var prisoner = f.Restore(restored.CaptureSave());
        prisoner.ChooseLegal(prisoner.LegalView!.Revision, "continue");
        Assert.Null(prisoner.LegalView);
        Assert.Empty(prisoner.State.Crime.PendingCharges(current.Region));
        Assert.Equal(DaggerfallCourtOutcome.Convicted, prisoner.State.Crime.Incidents.Single().Disposition!.Outcome);
        long after = Seconds(prisoner);
        Assert.Equal(sentence.PrisonDays * 86400L + 14400, after - before);
        Assert.Null(prisoner.PendingModeRequest);
    }

    [Theory]
    [InlineData("etiquette")] [InlineData("streetwise")]
    public void Accepted_trial_records_one_skill_use_and_cannot_replay_after_reload(string skill)
    {
        using var f = new Fixture(); var s = f.Session; f.Guard(); f.Charge(); f.Step(); f.Choose("yield");
        int before = s.State.Progression.SkillUses.GetValueOrDefault(skill);
        string revision = s.LegalView!.Revision;
        f.Choose(skill);
        Assert.Equal(before + 1, s.State.Progression.SkillUses[skill]);
        s.ChooseLegal(revision, skill);
        using var restored = f.Restore(s.CaptureSave()); restored.ChooseLegal(revision, skill);
        Assert.Equal(before + 1, restored.State.Progression.SkillUses[skill]);
    }

    [Fact]
    public void Unwitnessed_incident_has_no_response_or_reputation_penalty()
    {
        using var f = new Fixture(); int region = f.Session.Site.Region!.Value;
        int before = f.Session.State.Social.RegionalReputation(region);
        f.Charge(witnessed: false);
        f.Step(); Assert.Null(f.Session.LegalView); Assert.Empty(f.Session.State.Crime.LegalResponses);
        Assert.Equal(before, f.Session.State.Social.RegionalReputation(region));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void One_spell_bundle_deduplicates_assault_but_preserves_a_later_lethal_effect(bool lethal)
    {
        using var f = new Fixture(multipleEffects: true); var s = f.Session;
        long target = f.Civilian(); f.Guard();
        var health = s.State.Actors.Get(target).Stats.GetTrack(TrackId.Parse("health")); health.Maximum.BaseValue = 1000; health.SetCurrent(lethal ? 15 : 1000);
        var mana = s.State.Actors.Player.Stats.GetTrack(TrackId.Parse("magicka")); mana.Maximum.BaseValue = 10000; mana.SetCurrent(10000);
        s.State.Character.LearnSpell("custom-spell.1");
        Assert.Equal(DaggerfallCastOutcome.Ready, s.ReadyPlayerSpell("custom-spell.1").Outcome);
        f.Spatial.CapsuleCastHit = _ => default(SpatialHit) with {Present = true, Kind = SpatialHitKind.Entity, Entity = s.State.Actors.Get(target).Actor.Entity.Value};
        Assert.Equal(DaggerfallCastOutcome.DeliveryCompleted, s.ReleaseReadySpell(1, Vector3.UnitZ).Outcome);
        Assert.True(health.Current <= 980);
        Assert.Equal(lethal ? 2 : 1, s.State.Crime.Incidents.Count);
        Assert.Single(s.State.Crime.Incidents, incident => incident.Crime == DaggerfallCrimeKind.Assault);
        if (lethal) Assert.Single(s.State.Crime.Incidents, incident => incident.Crime == DaggerfallCrimeKind.Murder);
    }

    [Fact]
    public void Wandering_guard_becomes_one_hostile_watch_actor_and_preserves_identity_after_site_return()
    {
        using var f = new Fixture(); var s = f.Session;
        long civilian = f.Civilian("guard");
        f.Charge(); f.Step();
        long watch = Assert.Single(s.State.Crime.LegalResponses.Single().Guards);
        Assert.NotEqual(civilian, watch);
        Assert.False(s.State.Actors.TryGet(civilian, out _));
        Assert.Equal(DaggerfallNpcPresence.Removed, s.State.Npcs.Require(civilian).Presence);
        Assert.Equal(146, s.DefinitionsByActor[watch].MobileId);
        Assert.True(s.State.Actors.Get(watch).Actor.Get<Modules.Behavior.DaggerfallEnemyPerceptionMemory>().ForcedHostile);
        f.Choose("resist");
        Assert.True(s.TryTransitionTo(f.Other.ProfileKey));
        Assert.True(s.TryTransitionTo(f.Inputs.ProfileKey)); f.Step();
        Assert.Equal(watch, Assert.Single(s.State.Crime.LegalResponses.Single().Guards));
        using var restored = f.Restore(s.CaptureSave());
        Assert.False(restored.State.Actors.TryGet(civilian, out _));
        Assert.Equal(146, restored.DefinitionsByActor[watch].MobileId);
    }

    [Fact]
    public void Surrender_from_an_interior_enters_the_admitted_exterior_before_court_and_restores_there()
    {
        using var f = new Fixture(exteriorCourt: true); var s = f.Session;
        f.Guard(); f.Charge(); f.Step(); f.Choose("yield");
        Assert.Equal("court", s.LegalView!.Phase);
        var saved = DaggerfallSavePayload.Read(s.CaptureSave());
        Assert.Equal(f.Outside!.ProfileKey, saved.Site.ActiveProfile!.Require());
        using var restored = f.Restore(s.CaptureSave());
        Assert.Equal("court", restored.LegalView!.Phase);
        Assert.Equal(f.Outside.ProfileKey, DaggerfallSavePayload.Read(restored.CaptureSave()).Site.ActiveProfile!.Require());
    }

    [Fact]
    public void Banishment_preserves_penalized_reputation_and_resolves_the_charge()
    {
        using var f = new Fixture(); var s = f.Session; f.Guard(); f.Charge(); f.Step(); f.Choose("yield");
        var response = s.State.Crime.LegalResponses.Single();
        s.State.Crime.SetResponse(response with {Penalty = new(100, 2, true)});
        int reputation = s.State.Social.RegionalReputation(response.Region);
        f.Choose("guilty"); f.Choose("accept");
        Assert.Contains(response.Region, s.State.Crime.BanishedRegions);
        Assert.Equal(reputation, s.State.Social.RegionalReputation(response.Region));
        Assert.Equal(DaggerfallCourtOutcome.Banished, s.State.Crime.Incidents.Single().Disposition!.Outcome);
    }

    [Fact]
    public void Court_accuses_every_pending_charge_and_rejects_missing_saved_guard()
    {
        using var f = new Fixture(); var s = f.Session; f.Guard(); f.Charge(); f.Charge(operation: "second", crime: DaggerfallCrimeKind.Murder); f.Step();
        var saved = DaggerfallSavePayload.Read(s.CaptureSave());
        var broken = saved with {Crime = saved.Crime with {LegalResponses = [saved.Crime.LegalResponses.Single() with {Guards = [999999]}]}};
        Assert.Throws<ArgumentException>(() => f.Restore(DaggerfallSavePayload.Encode(broken)));
        int region = s.Site.Region!.Value, reputation = s.State.Social.RegionalReputation(region);
        var parts = s.State.Crime.PendingCharges(region).Select(charge => DaggerfallCourtPolicy.Accuse(charge.Crime, reputation, ulong.MaxValue, (_, max) => max)).ToArray();
        var expected = DaggerfallCourtPolicy.FitAvailableGold(new(DaggerfallCourtOutcome.Convicted, parts.Sum(part => part.Fine), parts.Sum(part => part.PrisonDays)), s.State.Currency.Read().Gold);
        f.Choose("yield");
        Assert.Equal(2, s.LegalView!.Charges.Length);
        Assert.Equal(expected.Fine, s.State.Crime.LegalResponses.Single().Penalty!.Fine);
        Assert.Equal(expected.PrisonDays, s.State.Crime.LegalResponses.Single().Penalty!.PrisonDays);
    }

    [Fact]
    public void New_offense_after_accusation_survives_release_for_its_own_sentence()
    {
        using var f = new Fixture(); var s = f.Session; f.Guard(); f.Charge(); f.Step(); f.Choose("yield");
        var response = s.State.Crime.LegalResponses.Single();
        s.State.Crime.SetResponse(response with {Penalty = new(0, 2, false)});
        f.Choose("guilty"); f.Choose("accept");
        f.Charge(operation: "late-offense");
        int reputation = s.State.Social.RegionalReputation(response.Region);
        Assert.Equal(new[] {"charge"}, s.State.Crime.LegalResponses.Single().Charges);
        f.Choose("continue");
        Assert.NotNull(s.State.Crime.Incidents.Single(charge => charge.OperationId == "charge").Disposition);
        Assert.Null(s.State.Crime.Incidents.Single(charge => charge.OperationId == "late-offense").Disposition);
        Assert.Equal(new[] {"late-offense"}, s.State.Crime.LegalResponses.Single().Charges);
        Assert.Equal(reputation + DaggerfallCrimePolicy.RegionalReputationLoss(DaggerfallCrimeKind.Theft) / 2 - 1,
            s.State.Social.RegionalReputation(response.Region));
        using var restored = f.Restore(s.CaptureSave());
        Assert.Equal("late-offense", Assert.Single(restored.State.Crime.PendingCharges(response.Region)).OperationId);
    }

    [Fact]
    public void Insufficient_fine_confiscates_real_stolen_items_and_prison_runs_loan_and_disease_consequences()
    {
        using var f = new Fixture(); var s = f.Session;
        int region = s.Site.Region!.Value;
        long now = Seconds(s);
        var loanStart = World.DaggerfallCalendar.FromAbsoluteSeconds(now - 359L * 86400);
        Assert.True(s.State.Loans.Issue(region, 1, 100, loanStart,
            Banking.DaggerfallLoanSettlementAdapter.ForBank(s.State.Bank, s.State.Currency)).Approved);
        s.State.Progression.AdvanceTo(0, 2);
        Assert.Equal(DaggerfallDiseaseAdmission.Started, s.InflictDisease(new("court-disease", "test-contact", 2000, 1, [DaggerfallClassicDisease.BrainFever])));
        var disease = Assert.Single(s.State.Effects.Active, effect => effect.Context.Instance.Value == "court-disease");
        string diseaseBefore = disease.State.GetRawText();
        var stolen = s.State.Inventory.Read().UniqueItems.First();
        ulong id = s.State.Inventory.GetDurableItemId(stolen.Entity).Value;
        s.State.ItemInstances.ReplaceUnique(id, s.State.ItemInstances.RequireUnique(id) with {Stolen = true});
        ulong gold = s.State.Currency.Read().Gold;
        s.State.Currency.TrySpendGold(gold, []);
        f.Guard(); f.Charge(); f.Step(); f.Choose("yield");
        var response = s.State.Crime.LegalResponses.Single();
        s.State.Crime.SetResponse(response with {Penalty = new(160, 4, false)});
        f.Choose("guilty"); f.Choose("accept");
        Assert.Equal(0UL, s.State.Currency.Read().Gold);
        Assert.False(s.State.ItemInstances.ContainsUnique(id));
        Assert.DoesNotContain(s.State.Inventory.Read().UniqueItems, item => item.Entity == stolen.Entity);
        Assert.DoesNotContain(s.State.Equipment.Read().Assignments, assignment => assignment.Item.EntityId == stolen.Entity.Value);
        Assert.Equal(4 * 86400L + 14400, s.State.Crime.LegalResponses.Single().PrisonSecondsRemaining);
        f.Choose("continue");
        Assert.True(s.State.Loans.Read(region)!.Defaulted);
        Assert.NotEqual(diseaseBefore, disease.State.GetRawText());
        // The release UI also admits the next ordinary world slice.
        Assert.InRange(Seconds(s) - now, 4 * 86400L + 14400, 4 * 86400L + 14401);
    }

    [Fact]
    public void Trial_acquittal_keeps_money_and_items_and_resolves_each_charge_once()
    {
        using var f = new Fixture(acquit: true); var s = f.Session;
        ulong gold = s.State.Currency.Read().Gold;
        var items = s.State.Inventory.Read().UniqueItems.Select(item => item.Entity).ToArray();
        f.Guard(); f.Charge(); f.Step(); f.Choose("yield"); f.Choose("etiquette");
        Assert.Equal(DaggerfallCourtOutcome.Acquitted, s.State.Crime.LegalResponses.Single().Sentence!.Outcome);
        string judgment = s.LegalView!.Revision;
        f.Choose("accept"); s.ChooseLegal(judgment, "accept");
        Assert.Equal(gold, s.State.Currency.Read().Gold);
        Assert.Equal(items, s.State.Inventory.Read().UniqueItems.Select(item => item.Entity));
        Assert.Equal(DaggerfallCourtOutcome.Acquitted, s.State.Crime.Incidents.Single().Disposition!.Outcome);
        Assert.Null(s.LegalView);
    }

    private static long Seconds(DaggerfallSession session)
    {
        var c = DaggerfallSavePayload.Read(session.CaptureSave()).Calendar;
        return new World.DaggerfallCalendar(c.Year, c.Month, c.Day, c.Hour, c.Minute, c.Second).ToAbsoluteSeconds();
    }

    private sealed class Fixture : IDisposable
    {
        internal DaggerfallSession Session { get; }
        internal DaggerfallSiteProfile Inputs { get; }
        internal DaggerfallSiteProfile Other { get; }
        internal DaggerfallSiteProfile? Outside { get; }
        internal SpatialFake Spatial { get; private set; } = null!;
        private readonly DaggerfallSessionComposition composition;
        private ulong step;
        private readonly bool acquit;
        internal Fixture(bool acquit = false, bool multipleEffects = false, bool exteriorCourt = false)
        {
            this.acquit = acquit;
            var content = exteriorCourt ? FullContent(TestData.RepositoryRoot) : null;
            var source = exteriorCourt ? ReadProfile(TestData.RepositoryRoot, content!, TestPayload.Definitions, "daggerfall.charing-interior-1-1-0.json") : ReadInputs(TestData.RepositoryRoot);
            Inputs = exteriorCourt ? source : SameContentAt(source, source.Site!.Value, DaggerfallWorldProfileKind.Interior, "law-room");
            // Other profiles share media, but authored actor identities belong to one profile only.
            var empty = new ProjectFacts(source.Project.PlayerPosition, new Dictionary<long, AuthoredActor>());
            Other = SameContentAt(source, source.Site!.Value, DaggerfallWorldProfileKind.Interior, "law-other", empty);
            Outside = exteriorCourt ? ReadProfile(TestData.RepositoryRoot, content!, TestPayload.Definitions, "daggerfall.charing-exterior.json") : null;
            var settings = new DaggerfallSpellEffectDefinition("touch", 4, 0, 0, 0, 1, 100, 0, 1, 10, 10, 1, 1, 1);
            var definitions = TestPayload.Definitions.ForSession([new DaggerfallSpellDefinition("custom-spell.1", -1, false, "Legal touch", 4, 1, 0, 0, multipleEffects ? [settings, settings with {Key = "second"}] : [settings]) {IsPlayerCreated = true, IsCustom = true, SpellsForSale = false}]);
            var tuning = DaggerfallTuning.Defaults with {Law = new(0, 0, 2, 5, 12.8f, 51.2f, 3.2)};
            composition = new(definitions, Inputs, tuning) {Profiles = new(Outside is null ? [Inputs, Other] : [Inputs, Other, Outside])};
            Session = DaggerfallSession.StartNew(Engine().Context, composition);
            Session.ApplyProductMode(ProductMode.Playing);
        }
        internal void Step() => Session.Update(new ProductUpdate(OuterUpdate(++step), []));
        internal void Choose(string key)
        {
            var payload = JsonSerializer.Serialize(new { action = "legal-choice", revision = Session.LegalView!.Revision, key });
            Session.Update(new ProductUpdate(OuterUpdate(++step), [Ui(payload)]));
        }
        internal long Guard() => Session.SpawnActor(TestPayload.Definitions.Actors.Values.Single(value => value.MobileId == 146).Id.Value,
            new(Session.State.PlayerControl.Position!.Value, 0));
        internal long Civilian(string role = "civilian")
        {
            var site = Session.Site.ActiveSite!;
            long id = Session.State.Npcs.RegisterCivilian(new(site.Id.Region, site.Name, string.Empty), new("breton", "Female", 0, 0, 0, 0), role, ["talk"]);
            return Session.MaterializeNpcActor(id, new(Session.State.PlayerControl.Position!.Value, 0));
        }
        internal void Charge(bool witnessed = true, string operation = "charge", DaggerfallCrimeKind crime = DaggerfallCrimeKind.Theft)
        {
            long guard = Session.State.Actors.All.FirstOrDefault(actor => actor.DurableId != 2000 && actor.DurableId != 2001)?.DurableId ?? 2000;
            Session.State.Crime.RecordIncident(new(operation, crime, DaggerfallCrimeStage.Completed, 1, 2000,
                Session.Site.Region!.Value, 0, DaggerfallCrimeTargetKind.Other,
                witnessed ? new(DaggerfallCrimeWitnessQuery.CompletedWithWitnesses, [guard]) : new(DaggerfallCrimeWitnessQuery.CompletedWithoutWitnesses, []), DaggerfallCrimeGuildCredit.None));
        }
        private EngineContextFake Engine()
        {
            List<string> releases = []; ContentFake content = new(releases); PopulateContent(content, Inputs); PopulateContent(content, Other);
            if (Outside is not null) PopulateContent(content, Outside);
            Spatial = SpatialFake.Create(Inputs.SpatialArtifact.Sha256, releases); Spatial.KeepPosition = true;
            var perception = PerceptionFake.Create();
            perception.Responder = request => Receipt(request.Observers.ToArray().SelectMany(observer => request.Targets.ToArray()
                .Select(target => new PerceptionPair(observer.Entity, target.Entity, 1, 1, PerceptionPairKind.Visible, 1))).ToArray());
            return EngineContextFake.Create(content, Spatial.Service, new AppearanceFake(releases), perception.Service, random: LawRandom.Create(acquit));
        }
        internal DaggerfallSession Restore(RulesetSavePayload save)
        {
            var session = DaggerfallSession.Restore(Engine().Context, composition with {Definitions = TestPayload.Definitions}, save);
            session.ApplyProductMode(ProductMode.Playing); return session;
        }
        public void Dispose() => Session.Dispose();
    }
}

internal class LawRandom : DispatchProxy
{
    private bool acquit;
    internal static IRandomService Create(bool acquit)
    {
        var result = Create<IRandomService, LawRandom>(); ((LawRandom)(object)result).acquit = acquit; return result;
    }
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        var request = (KeyedRngRequest)args![0]!;
        return new KeyedRngReceipt(request.Scope == "daggerfall.weather" || acquit && request.Key.Contains(":trial:", StringComparison.Ordinal) ? request.Minimum : request.Maximum);
    }
}
