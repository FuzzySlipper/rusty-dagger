using System.Text.Json;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Rulesets.Daggerfall.Content;
using Rusty.Engine;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Kit.Combat;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The poison owner: afflictions carried as active effects, ticking through the owners that hold health and
/// attributes, and the donor's own line through a poison's end - what a drug gives back and what a poison
/// leaves behind.
/// </summary>
public sealed class DaggerfallPoisonRuntimeTests
{
    /// <summary>
    /// One poison owner over a lifecycle that compiles only the twelve poisons, over the fixture's actors.
    /// The fixture's random service answers the minimum of every keyed window, so an arm's magnitude is its
    /// archetype minimum; the onset and duration rolls are the caller's, and the top of the window keeps
    /// each course long enough for its whole window to be spent.
    /// </summary>
    private static (DaggerfallPoisonRuntime Poison, DaggerfallEffectLifecycle Effects) Runtime(DaggerCombatFixture fixture)
    {
        DaggerfallCareerDefinition career = fixture.Definitions.Catalogs.RequireCareer("class00");
        DaggerfallEffectLifecycle effects = new(fixture.Actors, new DaggerfallEffectCatalog(
            DaggerfallPoisonEffects.Definitions(
                fixture.Random,
                new DaggerfallVitalityConsequences(new CombatResolution()),
                () => career)));
        return (new DaggerfallPoisonRuntime(effects, (_, maximum) => maximum, () => career), effects);
    }

    /// <summary>Passes minutes the way the session does: one ordinary round per minute.</summary>
    private static void Tick(DaggerfallEffectLifecycle effects, int minutes)
    {
        for (int minute = 0; minute < minutes; minute++) effects.AdvanceOrdinaryRound();
    }

    private static double Track(Actor actor, DaggerfallTrackId id) =>
        actor.Get<StatsComponent>().GetTrack(TrackId.Parse(id.Value)).Current;

    private static double Stat(Actor actor, DaggerfallStatId id) =>
        actor.Get<StatsComponent>().GetStat(StatId.Parse(id.Value)).Value;

    /// <summary>
    /// A second actor that carries the attributes, which a poison's attribute arm needs: the shared combat
    /// fixture's player has the two vital tracks and no attributes, because combat never reads them there.
    /// </summary>
    private static Actor AttributedActor(DaggerCombatFixture fixture)
    {
        DaggerfallActorDefinition player = fixture.Definitions.RequireActor(new DaggerfallActorId("player"));
        DaggerfallMechanicsState mechanics = new();
        StatsComponent stats = mechanics.CreateStats(
            player,
            DaggerfallPlayerVitals.Initial(player.Stats, fixture.Definitions.Catalogs.RequireCareer("class00")));
        return fixture.Actors.CreateActor(3, new EntityTypeId("nymph"), stats, new ActorPose(new WorldPoint(0f, 0f, 0f), 0f), "health").Actor;
    }

    [Fact]
    public void A_poison_waits_its_onset_and_then_takes_health_every_minute()
    {
        using DaggerCombatFixture fixture = new("nymph", playerHealth: 200d);
        Actor player = fixture.Actors.Player.Actor;
        (DaggerfallPoisonRuntime poison, DaggerfallEffectLifecycle effects) = Runtime(fixture);
        double before = Track(player, DaggerfallMechanicsIds.Health);

        Assert.True(poison.Afflict(player, 128));
        // Nux Vomica's onset is four minutes and its arm takes 2..11; the fourth minute acts, and the
        // assignment round is not one of the four.
        Tick(effects, 3);
        Assert.Equal(before, Track(player, DaggerfallMechanicsIds.Health));

        Tick(effects, 1);
        Assert.Equal(before - 2, Track(player, DaggerfallMechanicsIds.Health));

        // Its window is up to ten minutes, so a whole course takes twenty points at the arm's minimum.
        Tick(effects, 9);
        Assert.Equal(before - 20, Track(player, DaggerfallMechanicsIds.Health));
        Assert.False(poison.IsAfflicted(player));
    }

    [Fact]
    public void A_session_advancing_time_ticks_the_poison_its_player_carries()
    {
        // The owner is not a mechanism waiting for a caller: the session's effect lifecycle advances it.
        // Moonseed acts at once and lasts up to four minutes, so six elapsed minutes take health whatever
        // the draws inside its windows were.
        using var fixture = new NormalizedRuntimeSeamTests.ConditionSessionFixture();
        DaggerfallSession session = fixture.Session;
        Actor player = session.State.Actors.Player.Actor;
        double before = Track(player, DaggerfallMechanicsIds.Health);

        Assert.True(session.State.Poisons.Afflict(player, 130));
        Assert.True(session.State.Poisons.IsAfflicted(player));

        _ = session.AdvanceElapsedTime(361);

        Assert.True(Track(player, DaggerfallMechanicsIds.Health) < before);
        Assert.False(session.State.Poisons.IsAfflicted(player));
    }

    [Fact]
    public void Attribute_damage_a_poison_did_stays_after_it_runs_its_course()
    {
        // The donor says it plainly: attribute damage persists until the victim heals or is cured. So a
        // completed poison is still active, holding the sources a cure has to take off.
        using DaggerCombatFixture fixture = new("nymph", playerHealth: 200d);
        Actor victim = AttributedActor(fixture);
        (DaggerfallPoisonRuntime poison, DaggerfallEffectLifecycle effects) = Runtime(fixture);
        double strength = Stat(victim, DaggerfallMechanicsIds.Strength);

        Assert.True(poison.Afflict(victim, 131));
        // Drothweed waits up to ten minutes and then drains three attributes a minute for up to thirty.
        Tick(effects, 40);

        // Every tick of the window drains strength by the arm's magnitude, and the whole window is spent:
        // thirty ticks of nine, which is the minimum the fixture's random service answers. The poison's own
        // total keeps the whole drain while the stat itself saturates at its floor.
        DaggerfallActiveEffect livePoison = Assert.Single(effects.Active);
        Assert.Equal(-270, DaggerfallPoisonArms.State(livePoison).Totals["strength"]);
        Assert.True(Stat(victim, DaggerfallMechanicsIds.Strength) < strength);
        Assert.Equal(DaggerfallPoisonPhase.Complete, poison.Affliction(victim)!.Phase);
        Assert.True(poison.HasPersistingDamage(victim));

        Assert.True(poison.Cure(victim));
        Assert.Equal(strength, Stat(victim, DaggerfallMechanicsIds.Strength));
        Assert.Equal(0, poison.Count);
    }

    [Fact]
    public void A_drug_gives_back_what_it_gave_when_its_course_ends()
    {
        // The other half of the donor's line: a drug's helping arms are removed when the poison completes,
        // while the harm it did stands.
        using DaggerCombatFixture fixture = new("nymph", playerHealth: 200d);
        Actor victim = AttributedActor(fixture);
        (DaggerfallPoisonRuntime poison, DaggerfallEffectLifecycle effects) = Runtime(fixture);
        double luck = Stat(victim, DaggerfallMechanicsIds.Luck);
        double stamina = Track(victim, DaggerfallMechanicsIds.Stamina);

        Assert.True(poison.Afflict(victim, 136));
        // Indulcet waits up to twelve minutes and acts for up to six, two arms to the minute. The helping
        // arm has to be seen helping before its withdrawal can mean anything.
        Tick(effects, 13);
        Assert.True(Stat(victim, DaggerfallMechanicsIds.Luck) > luck);

        Tick(effects, 7);
        Assert.Equal(luck, Stat(victim, DaggerfallMechanicsIds.Luck));
        Assert.True(Track(victim, DaggerfallMechanicsIds.Stamina) < stamina);
        Assert.False(poison.IsAfflicted(victim));
    }

    [Fact]
    public void A_second_poison_does_not_heal_the_damage_the_first_one_left()
    {
        // The damage a completed poison did persists, so taking a second poison must not quietly give it
        // back. The older poison stops running and keeps its own arms, which a cure then takes off with
        // everything else.
        using DaggerCombatFixture fixture = new("nymph", playerHealth: 200d);
        Actor victim = AttributedActor(fixture);
        (DaggerfallPoisonRuntime poison, DaggerfallEffectLifecycle effects) = Runtime(fixture);
        double strength = Stat(victim, DaggerfallMechanicsIds.Strength);

        Assert.True(poison.Afflict(victim, 131));
        Tick(effects, 40);
        double drained = Stat(victim, DaggerfallMechanicsIds.Strength);
        Assert.True(drained < strength);
        Assert.Equal(DaggerfallPoisonPhase.Complete, poison.Affliction(victim)!.Phase);

        // Arsenic outlasts what is left of Drothweed, so it takes over; the drain stays.
        Assert.True(poison.Afflict(victim, 129));
        Assert.Equal(129, poison.Affliction(victim)!.Archetype.Variant);
        Assert.Equal(drained, Stat(victim, DaggerfallMechanicsIds.Strength));
        Assert.Equal(2, poison.Count);

        Assert.True(poison.Cure(victim));
        Assert.Equal(strength, Stat(victim, DaggerfallMechanicsIds.Strength));
        Assert.False(poison.IsAfflicted(victim));
    }

    [Fact]
    public void A_session_with_an_active_poison_saves_and_comes_back_with_it()
    {
        // The case the parallel owner could not satisfy: a poison's arms are stat sources, and the stats
        // save refuses an effect source with no active effect to clean it up. A poison is an active effect
        // now, so the same save that used to fail succeeds and carries the course with it.
        using var fixture = new NormalizedRuntimeSeamTests.ConditionSessionFixture();
        DaggerfallSession session = fixture.Session;
        Actor player = session.State.Actors.Player.Actor;
        // Drothweed drains attributes rather than only health, so the save carries effect-backed stat
        // sources: the strict check on restore meets the stats codec's own round trip.
        Assert.True(session.State.Poisons.Afflict(player, 131));
        for (int minute = 0; minute < 30 && !session.State.Poisons.HasPersistingDamage(player); minute++)
            _ = session.AdvanceElapsedTime(60);
        Assert.True(session.State.Poisons.HasPersistingDamage(player));

        RulesetSavePayload saved = session.CaptureSave();
        using DaggerfallSession restored = fixture.Restore(saved);

        Actor restoredPlayer = restored.State.Actors.Player.Actor;
        Assert.True(restored.State.Poisons.IsAfflicted(restoredPlayer));
        Assert.Equal(131, restored.State.Poisons.Affliction(restoredPlayer)!.Archetype.Variant);
        Assert.True(restored.State.Poisons.HasPersistingDamage(restoredPlayer));

        // And it is still running rather than merely present: the drain deepens from where the reload left it.
        double drained = Stat(restoredPlayer, DaggerfallMechanicsIds.Strength);
        _ = restored.AdvanceElapsedTime(361);
        Assert.True(Stat(restoredPlayer, DaggerfallMechanicsIds.Strength) < drained);
    }

    [Fact]
    public void A_drug_takes_back_only_what_it_helped_with()
    {
        // Sursum is the archetype that separates the two halves on the same attribute kind: it helps
        // strength and harms intelligence, so a completion that withdrew both would be visible here.
        using DaggerCombatFixture fixture = new("nymph", playerHealth: 200d);
        Actor victim = AttributedActor(fixture);
        (DaggerfallPoisonRuntime poison, DaggerfallEffectLifecycle effects) = Runtime(fixture);
        double strength = Stat(victim, DaggerfallMechanicsIds.Strength);
        double intelligence = Stat(victim, DaggerfallMechanicsIds.Intelligence);

        Assert.True(poison.Afflict(victim, 137));
        // Its onset is at most four minutes and its course two, so the fifth minute is its first arm.
        Tick(effects, 4);
        Assert.True(Stat(victim, DaggerfallMechanicsIds.Strength) > strength);
        Assert.True(Stat(victim, DaggerfallMechanicsIds.Intelligence) < intelligence);

        Tick(effects, 1);
        Assert.Equal(strength, Stat(victim, DaggerfallMechanicsIds.Strength));
        Assert.True(Stat(victim, DaggerfallMechanicsIds.Intelligence) < intelligence);
        Assert.True(poison.HasPersistingDamage(victim));
    }

    [Fact]
    public void A_poison_draining_an_attribute_moves_the_maximum_that_attribute_derives()
    {
        // Fatigue is derived from strength and endurance and magicka from intelligence, so a poison that
        // drains one of those has to move the maximum with it; leaving it stale is the shape the disease
        // path already avoids.
        using var fixture = new NormalizedRuntimeSeamTests.ConditionSessionFixture();
        DaggerfallSession session = fixture.Session;
        Actor player = session.State.Actors.Player.Actor;
        StatsComponent stats = player.Get<StatsComponent>();
        double endurance = stats.GetStat(StatId.Parse(DaggerfallMechanicsIds.Endurance.Value)).Value;
        double maximum = stats.GetStat(StatId.Parse(DaggerfallMechanicsIds.StaminaMaximum.Value)).Value;

        Assert.True(session.State.Poisons.Afflict(player, 129));
        // Arsenic waits ten minutes and then drains endurance a point a minute.
        _ = session.AdvanceElapsedTime(15 * 60);

        Assert.True(stats.GetStat(StatId.Parse(DaggerfallMechanicsIds.Endurance.Value)).Value < endurance);
        double drained = stats.GetStat(StatId.Parse(DaggerfallMechanicsIds.StaminaMaximum.Value)).Value;
        Assert.True(drained < maximum);

        // And the same follows taking the arms off: a cure leaves a maximum that matches its attribute again,
        // rather than the drained one standing until something else happens to refresh it.
        Assert.True(session.State.Poisons.Cure(player));
        Assert.Equal(endurance, stats.GetStat(StatId.Parse(DaggerfallMechanicsIds.Endurance.Value)).Value);
        Assert.Equal(maximum, stats.GetStat(StatId.Parse(DaggerfallMechanicsIds.StaminaMaximum.Value)).Value);
    }

    [Fact]
    public void A_save_names_the_effect_that_owns_a_poison_holding_attribute_damage()
    {
        // This is the case the parallel owner could not save: the arms are effect-backed stat sources, so
        // the save's rule is that an active effect with the same instance owns them. The poison is fully
        // established first, because only then are there sources for the rule to check.
        using var fixture = new NormalizedRuntimeSeamTests.ConditionSessionFixture();
        DaggerfallSession session = fixture.Session;
        Actor player = session.State.Actors.Player.Actor;
        Assert.True(session.State.Poisons.Afflict(player, 131));
        for (int minute = 0; minute < 30 && !session.State.Poisons.HasPersistingDamage(player); minute++)
            _ = session.AdvanceElapsedTime(60);
        Assert.True(session.State.Poisons.HasPersistingDamage(player));

        DaggerfallSavePayload payload = DaggerfallSavePayload.Read(session.CaptureSave());
        DaggerfallActiveEffectSave effect = Assert.Single(payload.ActiveEffects, value => value.EffectKey == "Poison-Drothweed");
        DaggerfallStatsSave stats = Assert.Single([payload.Player.Stats], value => value.Sources.Length != 0);
        Assert.Contains(stats.Sources, source =>
            source.Identity.Kind == DaggerfallStatSourceIdentityKind.Effect
            && StringComparer.Ordinal.Equals(source.Identity.InstanceId, effect.Instance));

        // And the rule is what makes it save: with the effect gone the same payload is refused rather than
        // written with arms nothing can take off.
        RulesetSavePayload orphaned = DaggerfallSavePayload.Encode(payload with { ActiveEffects = [] });
        ArgumentException refused = Assert.Throws<ArgumentException>(() => fixture.Restore(orphaned));
        Assert.Contains("has no matching active effect cleanup owner", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Forms_six_infliction_starts_the_archetype_it_admits_and_leaves_nothing_when_it_resists()
    {
        // The throw is the caller's, so the two outcomes are decided here rather than by the fixture's RNG.
        using DaggerCombatFixture fixture = new("nymph", playerHealth: 200d);
        Actor player = fixture.Actors.Player.Actor;
        (DaggerfallPoisonRuntime poison, _) = Runtime(fixture);
        DaggerfallPoisonExposure exposure = new(
            fixture.Actors.Player.DurableId,
            TargetLevel: 5,
            CareerImmune: false,
            RaceImmune: false,
            Willpower: 50,
            BypassResistance: true);

        // A bypassed attempt needs no throw, so the donor's order means it draws nothing either.
        int drawn = 0;
        int Roll(int minimum, int maximum)
        {
            _ = minimum;
            _ = maximum;
            drawn++;
            return 100;
        }

        Assert.Equal(DaggerfallPoisonAdmission.Admitted, DaggerfallPoisonPolicy.InflictPoison(poison, fixture.Actors, exposure, 130, Roll));
        Assert.Equal(0, drawn);
        Assert.Equal(130, poison.Affliction(player)!.Archetype.Variant);

        // The same attempt without the bypass is left to the throw: the donor's amount is non-zero for a
        // roll that fails to resist, and zero for one that throws the poison off, which starts nothing.
        using DaggerCombatFixture second = new("nymph", playerHealth: 200d);
        Actor other = second.Actors.Player.Actor;
        (DaggerfallPoisonRuntime none, _) = Runtime(second);
        DaggerfallPoisonExposure thrown = exposure with { BypassResistance = false };
        Assert.Equal(DaggerfallPoisonAdmission.Resisted, DaggerfallPoisonPolicy.InflictPoison(none, second.Actors, thrown, 130, (_, _) => 1));
        Assert.False(none.IsAfflicted(other));
        Assert.Equal(DaggerfallPoisonAdmission.Admitted, DaggerfallPoisonPolicy.InflictPoison(none, second.Actors, thrown, 130, (_, _) => 100));
        Assert.Equal(130, none.Affliction(other)!.Archetype.Variant);

        // A first-level target is never poisoned, and it is refused before the throw rather than by it: the
        // throw a target the donor settles earlier never happens, so this one draws nothing.
        int lateDraws = 0;
        Assert.Equal(DaggerfallPoisonAdmission.Immune,
            DaggerfallPoisonPolicy.InflictPoison(none, second.Actors, thrown with { TargetLevel = 1 }, 128, (_, _) => { lateDraws++; return 100; }));
        Assert.Equal(0, lateDraws);

        // A variant no archetype answers cannot come back as a quietly successful poisoning.
        Assert.Throws<ArgumentException>(() =>
            DaggerfallPoisonPolicy.InflictPoison(none, second.Actors, exposure, 127, (_, _) => 1));
    }

    [Fact]
    public void The_sessions_infliction_starts_the_dose_and_cures_it()
    {
        // The drug's own exposure: self-targeted and bypassing resistance, which is what the donor's drug
        // use does. The session is the one that supplies the draw and the background's poison resistance.
        using var fixture = new NormalizedRuntimeSeamTests.ConditionSessionFixture();
        DaggerfallSession session = fixture.Session;
        Actor player = session.State.Actors.Player.Actor;
        DaggerfallPoisonExposure dose = new(
            session.State.Actors.Player.DurableId,
            TargetLevel: 5,
            CareerImmune: false,
            RaceImmune: false,
            Willpower: 50,
            BypassResistance: true);

        Assert.Equal(DaggerfallPoisonAdmission.Admitted, session.InflictPoison(dose, 136));
        Assert.True(session.State.Poisons.IsAfflicted(player));
        Assert.Equal(136, session.State.Poisons.Affliction(player)!.Archetype.Variant);

        // The resistance the caller puts on the exposure is what the throw reads: the same roll that a plain
        // attempt resists is admitted once a background's own modifier is behind it, and vice versa.
        DaggerfallPoisonExposure modified = dose with { BypassResistance = false, BiographyModifier = 30 };
        int plain = DaggerfallPoisonPolicy.SavingThrowChance(50, DaggerfallDiseaseCareerTolerance.Normal);
        int strengthened = DaggerfallPoisonPolicy.SavingThrowChance(50, DaggerfallDiseaseCareerTolerance.Normal, modified.BiographyModifier);
        Assert.True(strengthened > plain);
        int between = plain - 15;
        Assert.Equal(DaggerfallPoisonAdmission.Admitted, DaggerfallPoisonPolicy.Admit(modified with { BiographyModifier = 0 }, between));
        Assert.Equal(DaggerfallPoisonAdmission.Resisted, DaggerfallPoisonPolicy.Admit(modified, between));

        Assert.True(session.CurePoison());
        Assert.False(session.State.Poisons.IsAfflicted(player));
    }

    [Fact]
    public void Career_poison_tolerance_reads_the_poison_flag_and_not_the_disease_one()
    {
        // The donor's own bits: poison is 4 and disease is 64, and its GetTolerance reads resistance before
        // immunity and both before the weaker tolerances.
        DaggerfallCareerDefinition career = new(
            "poison-career", "Poison career", [], [], [], [], [], 75, 2000, 1f, [], [],
            ResistanceFlags: 4, ImmunityFlags: 0, LowToleranceFlags: 0, CriticalWeaknessFlags: 0, 0, [],
            [], new DaggerfallCatalogCitation("test", "test"));

        Assert.Equal(DaggerfallDiseaseCareerTolerance.Resistant, DaggerfallPoisonPolicy.CareerTolerance(career));
        Assert.Equal(DaggerfallDiseaseCareerTolerance.Immune,
            DaggerfallPoisonPolicy.CareerTolerance(career with { ResistanceFlags = 0, ImmunityFlags = 4 }));
        Assert.Equal(DaggerfallDiseaseCareerTolerance.LowTolerance,
            DaggerfallPoisonPolicy.CareerTolerance(career with { ResistanceFlags = 0, LowToleranceFlags = 4 }));
        Assert.Equal(DaggerfallDiseaseCareerTolerance.CriticalWeakness,
            DaggerfallPoisonPolicy.CareerTolerance(career with { ResistanceFlags = 0, CriticalWeaknessFlags = 4 }));
        Assert.Equal(DaggerfallDiseaseCareerTolerance.Normal,
            DaggerfallPoisonPolicy.CareerTolerance(career with { ResistanceFlags = 0 }));

        // Resistance wins over immunity in the donor's order, and a disease-only career is not poison-tolerant.
        Assert.Equal(DaggerfallDiseaseCareerTolerance.Resistant,
            DaggerfallPoisonPolicy.CareerTolerance(career with { ImmunityFlags = 4 }));
        Assert.Equal(DaggerfallDiseaseCareerTolerance.Normal,
            DaggerfallPoisonPolicy.CareerTolerance(career with { ResistanceFlags = 64, ImmunityFlags = 64 }));
        Assert.Equal(DaggerfallDiseaseCareerTolerance.Immune,
            DaggerfallDiseasePolicy.CareerTolerance(career with { ResistanceFlags = 0, ImmunityFlags = 64 }));
    }

    [Fact]
    public void A_second_poison_is_measured_by_what_is_left_of_the_first()
    {
        // Nux Vomica's whole window is fourteen minutes and Moonseed's is four, so a whole-window comparison
        // would refuse Moonseed forever. Once Nux Vomica has burned down to two minutes left, Moonseed's four
        // have more left to give and must take over — the incumbent's own remaining time is what it is judged by.
        using DaggerCombatFixture fixture = new("nymph", playerHealth: 200d);
        Actor player = fixture.Actors.Player.Actor;
        (DaggerfallPoisonRuntime poison, DaggerfallEffectLifecycle effects) = Runtime(fixture);

        Assert.True(poison.Afflict(player, 128));
        Tick(effects, 11);
        Assert.Equal(2, poison.Affliction(player)!.TotalMinutesRemaining);

        Assert.True(poison.Afflict(player, 130));
        Assert.Equal(130, poison.Affliction(player)!.Archetype.Variant);
        Assert.Equal(4, poison.Affliction(player)!.TotalMinutesRemaining);

        // Strictly more is what wins: a challenger with exactly as much left as the incumbent is refused and
        // leaves the incumbent's course untouched.
        Tick(effects, 1);
        Assert.Equal(3, poison.Affliction(player)!.TotalMinutesRemaining);
        Assert.True(poison.Afflict(player, 130));
        int challengerLeft = poison.Affliction(player)!.TotalMinutesRemaining;
        Assert.Equal(4, challengerLeft);
    }

    [Fact]
    public void A_drug_completing_moves_back_the_maxima_its_help_was_holding_up()
    {
        // Sursum helps strength and harms intelligence, so its completion has to refresh the maxima the
        // withdrawn help was holding up while the harm that stays keeps its own maximum down.
        using var fixture = new NormalizedRuntimeSeamTests.ConditionSessionFixture();
        DaggerfallSession session = fixture.Session;
        Actor player = session.State.Actors.Player.Actor;
        StatsComponent stats = player.Get<StatsComponent>();
        double stamina = stats.GetStat(StatId.Parse(DaggerfallMechanicsIds.StaminaMaximum.Value)).Value;
        double magicka = stats.GetStat(StatId.Parse(DaggerfallMechanicsIds.MagickaMaximum.Value)).Value;

        Assert.True(session.State.Poisons.Afflict(player, 137));
        // Its onset is at most four minutes and its course two, so seven minutes finish it whatever it rolled.
        _ = session.AdvanceElapsedTime(7 * 60);
        // The drug's course is over; what keeps it on the actor is the harm that stays, which is the point.
        Assert.Equal(DaggerfallPoisonPhase.Complete, session.State.Poisons.Affliction(player)!.Phase);
        Assert.True(session.State.Poisons.HasPersistingDamage(player));

        Assert.Equal(stamina, stats.GetStat(StatId.Parse(DaggerfallMechanicsIds.StaminaMaximum.Value)).Value);
        Assert.True(stats.GetStat(StatId.Parse(DaggerfallMechanicsIds.MagickaMaximum.Value)).Value < magicka);
    }

    [Fact]
    public void A_restored_poison_refuses_a_source_or_a_total_its_state_does_not_carry()
    {
        // Both halves are malformed current data rather than play: a source the state never named would
        // survive a cure, and a total beyond the archetype's own window would apply a live mutation and then
        // overflow part-way through the next tick. Each is refused where it is read, and the instance is one
        // no live effect holds so the refusal is the only reason either restore can fail.
        using DaggerCombatFixture fixture = new("nymph", playerHealth: 200d);
        Actor victim = AttributedActor(fixture);
        (DaggerfallPoisonRuntime poison, DaggerfallEffectLifecycle effects) = Runtime(fixture);
        Assert.True(poison.Afflict(victim, 129));
        Assert.Single(effects.Active);

        const string Instance = "poison.129.99";
        StatsComponent stats = victim.Get<StatsComponent>();
        stats.GetStat(StatId.Parse(DaggerfallMechanicsIds.Endurance.Value)).SetSources(
            StatId.Parse(DaggerfallMechanicsIds.Endurance.Value),
            [
                new StatSource(
                    new EffectSourceIdentity(
                        victim.Entity,
                        EffectInstanceId.Parse(Instance),
                        1,
                        SourceDefinitionId.Parse("daggerfall.poison.attributes")),
                    SourceDefinitionId.Parse("daggerfall.poison.attributes"),
                    priority: 0,
                    [new StatContributionDefinition(
                        StatId.Parse(DaggerfallMechanicsIds.Endurance.Value),
                        StackingGroupId.Parse("daggerfall.poison.endurance"),
                        MechanicsStackingPolicy.Sum,
                        new StatContribution.Add(-4))]),
            ]);

        ArgumentException unnamed = Assert.Throws<ArgumentException>(() =>
            RestoreInto(fixture, PoisonEntry(victim, Instance, """{"minutesToStart":0,"minutesRemaining":20,"admitted":true,"draw":1,"totals":{}}"""u8.ToArray())));
        Assert.Contains("do not match its durable poison state", unnamed.Message, StringComparison.Ordinal);

        // A thousand and one endurance points is a thousand more than Arsenic's whole window can take.
        ArgumentException beyond = Assert.Throws<ArgumentException>(() =>
            RestoreInto(fixture, PoisonEntry(victim, "poison.129.100", """{"minutesToStart":0,"minutesRemaining":20,"admitted":true,"draw":1,"totals":{"endurance":1001}}"""u8.ToArray())));
        Assert.Contains("beyond what its archetype can do", beyond.Message, StringComparison.Ordinal);
    }

    /// <summary>Restores one crafted poison entry into a fresh lifecycle over the same actors.</summary>
    private static void RestoreInto(DaggerCombatFixture fixture, DaggerfallActiveEffectSave entry)
    {
        (_, DaggerfallEffectLifecycle effects) = Runtime(fixture);
        effects.Restore([entry]);
    }

    /// <summary>One saved poison effect over the fixture's victim, with the state under test.</summary>
    private static DaggerfallActiveEffectSave PoisonEntry(Actor victim, string instance, byte[] state)
    {
        using JsonDocument document = JsonDocument.Parse(state);
        return new DaggerfallActiveEffectSave(
            instance,
            "Poison-Arsenic",
            "poison",
            CasterId: null,
            checked((long)victim.Entity.Value),
            "classic",
            Element: null,
            ItemId: null,
            RemainingRounds: null,
            Stacks: 1,
            document.RootElement.Clone());
    }

    [Fact]
    public void A_second_poison_only_takes_over_when_it_has_more_left_to_give()
    {
        using DaggerCombatFixture fixture = new("nymph", playerHealth: 200d);
        Actor player = fixture.Actors.Player.Actor;
        (DaggerfallPoisonRuntime poison, _) = Runtime(fixture);

        // Arsenic outlasts Nux Vomica, so it takes Nux Vomica's place; once Arsenic is running, the shorter
        // course cannot displace it and does not shorten what is already there.
        Assert.True(poison.Afflict(player, 128));
        Assert.Equal(128, poison.Affliction(player)!.Archetype.Variant);
        int nuxVomicaLeft = poison.Affliction(player)!.TotalMinutesRemaining;
        Assert.True(poison.Afflict(player, 129));
        Assert.Equal(129, poison.Affliction(player)!.Archetype.Variant);
        int arsenicLeft = poison.Affliction(player)!.TotalMinutesRemaining;
        Assert.True(arsenicLeft > nuxVomicaLeft);

        // The shorter course is refused, and refusing it leaves what is running exactly where it stood.
        Assert.False(poison.Afflict(player, 128));
        Assert.Equal(129, poison.Affliction(player)!.Archetype.Variant);
        Assert.Equal(arsenicLeft, poison.Affliction(player)!.TotalMinutesRemaining);

        // A variant no archetype names is answered rather than assumed.
        Assert.False(poison.Afflict(player, 127));
    }
}
