using WorldRpg.Rulesets.Daggerfall.Crime;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallCrimeStateTests
{
    [Fact]
    public void Failed_unwitnessed_attempt_stays_separate_from_a_legal_incident()
    {
        DaggerfallCrimeState state = new();
        DaggerfallCrimeAttemptSave attempt = new(
            "pickpocket-01", DaggerfallCrimeAction.Pickpocket, 1, 2, 4, 50,
            DaggerfallCrimeAttemptOutcome.Failed, DaggerfallCrimeWitnessEvidence.NotQueried);

        Assert.True(state.RecordAttempt(attempt));
        Assert.False(state.RecordAttempt(attempt));
        Assert.Empty(state.Incidents);
        Assert.Equal(DaggerfallCrimeWitnessQuery.NotQueried, state.Attempts.Single().Witnesses.Query);
        Assert.Equal(0, state.ThievingRequirementTally);
    }

    [Fact]
    public void Accepted_theft_receipt_keeps_owner_and_one_legal_consequence_across_replay_and_restore()
    {
        DaggerfallCrimeState state = new();
        DaggerfallCrimeWitnessEvidence witnessed = new(DaggerfallCrimeWitnessQuery.CompletedWithWitnesses, [8]);
        DaggerfallCrimeAttemptSave attempt = new(
            "theft-02", DaggerfallCrimeAction.Shoplifting, 1, 7, 4, 60,
            DaggerfallCrimeAttemptOutcome.PropertyTransferred, witnessed);
        DaggerfallCrimeIncidentSave incident = new(
            "theft-02", DaggerfallCrimeKind.Theft, DaggerfallCrimeStage.Completed,
            1, 7, 4, 60, DaggerfallCrimeTargetKind.Other, witnessed, DaggerfallCrimeGuildCredit.Thieving);

        Assert.True(state.RecordAttempt(attempt));
        Assert.True(state.RecordIncident(incident));
        Assert.False(state.RecordIncident(incident));
        Assert.Single(state.Incidents);
        Assert.Equal(7, state.Incidents.Single().AffectedActorOrOwnerId);
        Assert.Equal([8L], state.Incidents.Single().Witnesses.WitnessActorIds);
        Assert.Equal(1, state.ThievingRequirementTally);

        DaggerfallCrimeState restored = new(state.Capture());
        Assert.False(restored.RecordIncident(incident));
        Assert.Equal(1, restored.ThievingRequirementTally);
        Assert.Single(restored.Incidents);
    }

    [Fact]
    public void Empty_witness_result_requires_a_completed_query_and_does_not_imply_guard_response()
    {
        DaggerfallCrimeState state = new();
        DaggerfallCrimeWitnessEvidence noWitnesses = new(DaggerfallCrimeWitnessQuery.CompletedWithoutWitnesses, []);
        DaggerfallCrimeIncidentSave incident = new(
            "breakin-03", DaggerfallCrimeKind.BreakingAndEntering, DaggerfallCrimeStage.Completed,
            1, 4, 2, 70, DaggerfallCrimeTargetKind.Unknown, noWitnesses, DaggerfallCrimeGuildCredit.None);

        Assert.True(state.RecordIncident(incident));
        Assert.Equal(DaggerfallCrimeWitnessQuery.CompletedWithoutWitnesses, state.Incidents.Single().Witnesses.Query);
        Assert.Empty(state.Incidents.Single().Witnesses.WitnessActorIds);
        Assert.Throws<ArgumentException>(() => new DaggerfallCrimeWitnessEvidence(
            DaggerfallCrimeWitnessQuery.NotQueried, [8]).Validate());
        Assert.Throws<ArgumentException>(() => new DaggerfallCrimeAttemptSave(
            "self-witness", DaggerfallCrimeAction.Pickpocket, 1, 2, 4, 70,
            DaggerfallCrimeAttemptOutcome.Failed,
            new(DaggerfallCrimeWitnessQuery.CompletedWithWitnesses, [1])).Validate());
    }

    [Fact]
    public void Guild_progress_uses_donor_threshold_delay_strict_time_and_outside_gate()
    {
        DaggerfallCrimeState state = new();
        for (int i = 0; i < DaggerfallCrimeState.ThievingInvitationThreshold; i++)
            Assert.True(state.RecordGuildRequirementProgress($"thief-{i}", DaggerfallCrimeGuildCredit.Thieving, 1_000));

        Assert.Equal(10, state.ThievingRequirementTally);
        Assert.Equal(1_000 + DaggerfallCrimeState.InvitationDelayMinutes, state.ThievesInvitationDueMinute);
        Assert.False(state.IsInvitationDue(DaggerfallCrimeGuildRequirement.Thieving, 5_320, playerInside: false));
        Assert.False(state.IsInvitationDue(DaggerfallCrimeGuildRequirement.Thieving, 5_321, playerInside: true));
        Assert.True(state.IsInvitationDue(DaggerfallCrimeGuildRequirement.Thieving, 5_321, playerInside: false));
        Assert.False(state.MarkInvitationStarted(DaggerfallCrimeGuildRequirement.Thieving, 5_321, playerInside: true));
        Assert.True(state.MarkInvitationStarted(DaggerfallCrimeGuildRequirement.Thieving, 5_321, playerInside: false));
        Assert.True(state.InvitationEvidence.ThievingCrimeRequirementSatisfied);
        Assert.Equal(100, state.ThievingRequirementTally);
        Assert.Equal(0, state.ThievesInvitationDueMinute);

        DaggerfallCrimeState restored = new(state.Capture());
        Assert.True(restored.InvitationEvidence.ThievingCrimeRequirementSatisfied);
    }

    [Fact]
    public void Murder_credit_matches_civilian_five_and_guard_one_donor_tallies()
    {
        DaggerfallCrimeState state = new();
        DaggerfallCrimeIncidentSave civilian = new(
            "murder-civilian", DaggerfallCrimeKind.Murder, DaggerfallCrimeStage.Completed,
            1, 2, 3, 100, DaggerfallCrimeTargetKind.Civilian, DaggerfallCrimeWitnessEvidence.NotQueried,
            DaggerfallCrimeGuildCredit.CivilianMurder);
        DaggerfallCrimeIncidentSave guard = new(
            "murder-guard", DaggerfallCrimeKind.Murder, DaggerfallCrimeStage.Completed,
            1, 3, 3, 101, DaggerfallCrimeTargetKind.Guard, DaggerfallCrimeWitnessEvidence.NotQueried,
            DaggerfallCrimeGuildCredit.GuardMurder);

        Assert.True(state.RecordIncident(civilian));
        Assert.True(state.RecordIncident(guard));
        Assert.Equal(6, state.MurderRequirementTally);
        Assert.False(state.RecordGuildRequirementProgress("murder-guard", DaggerfallCrimeGuildCredit.GuardMurder, 101));
        Assert.Equal(6, state.MurderRequirementTally);
        Assert.True(state.RecordGuildRequirementProgress("murder-civilian-02", DaggerfallCrimeGuildCredit.CivilianMurder, 102));
        Assert.True(state.RecordGuildRequirementProgress("murder-civilian-03", DaggerfallCrimeGuildCredit.CivilianMurder, 103));
        Assert.Equal(16, state.MurderRequirementTally);
        Assert.Equal(4_423, state.MurderInvitationDueMinute);
    }

    [Fact]
    public void Reusing_an_operation_key_for_a_different_attempt_or_guild_consequence_fails_loudly()
    {
        DaggerfallCrimeState state = new();
        DaggerfallCrimeAttemptSave first = new(
            "one-op", DaggerfallCrimeAction.Pickpocket, 1, 2, 4, 50,
            DaggerfallCrimeAttemptOutcome.Failed, DaggerfallCrimeWitnessEvidence.NotQueried);
        Assert.True(state.RecordAttempt(first));
        Assert.Throws<InvalidOperationException>(() => state.RecordAttempt(first with
        {
            Outcome = DaggerfallCrimeAttemptOutcome.PropertyTransferred,
        }));

        Assert.True(state.RecordGuildRequirementProgress("guild-op", DaggerfallCrimeGuildCredit.Thieving, 70));
        Assert.Throws<InvalidOperationException>(() => state.RecordGuildRequirementProgress(
            "guild-op", DaggerfallCrimeGuildCredit.CivilianMurder, 70));
    }
}
