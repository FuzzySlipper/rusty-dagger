using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallDialogueWorldStateTests
{
    [Fact]
    public void Crime_wave_is_one_transition_with_expiry_and_persisted_marker()
    {
        DaggerfallDialogueWorldState state = new();

        DaggerfallDialogueWorldRumorSave first = Assert.IsType<DaggerfallDialogueWorldRumorSave>(
            state.Synchronize(DaggerfallDialogueWorldState.CrimeWaveType, 17,
                DaggerfallDialogueWorldState.CrimeWaveTextId, enabled: true, nowMinute: 100));
        Assert.Equal(100 + DaggerfallDialogueWorldState.GeneratedRumorDurationMinutes, first.ExpiresAtMinute);

        DaggerfallDialogueWorldRumorSave repeated = Assert.IsType<DaggerfallDialogueWorldRumorSave>(
            state.Synchronize(DaggerfallDialogueWorldState.CrimeWaveType, 17,
                DaggerfallDialogueWorldState.CrimeWaveTextId, enabled: true, nowMinute: 101));
        Assert.Equal(first, repeated);

        DaggerfallDialogueWorldSave saved = state.Capture();
        Assert.Single(saved.Rumors);
        DaggerfallDialogueWorldState restored = new(saved);
        Assert.Null(restored.Synchronize(DaggerfallDialogueWorldState.CrimeWaveType, 17,
            DaggerfallDialogueWorldState.CrimeWaveTextId, enabled: true, nowMinute: first.ExpiresAtMinute));

        // The expired marker survives a save while the source flag remains true. Only a real
        // false-to-true transition is allowed to create another event.
        Assert.Null(restored.Synchronize(DaggerfallDialogueWorldState.CrimeWaveType, 17,
            DaggerfallDialogueWorldState.CrimeWaveTextId, enabled: true, nowMinute: first.ExpiresAtMinute + 1));
        DaggerfallDialogueWorldState expiredReload = new(restored.Capture());
        Assert.Null(expiredReload.Synchronize(DaggerfallDialogueWorldState.CrimeWaveType, 17,
            DaggerfallDialogueWorldState.CrimeWaveTextId, enabled: true, nowMinute: first.ExpiresAtMinute + 1));
        Assert.Null(restored.Synchronize(DaggerfallDialogueWorldState.CrimeWaveType, 17,
            DaggerfallDialogueWorldState.CrimeWaveTextId, enabled: false, nowMinute: first.ExpiresAtMinute + 2));
        DaggerfallDialogueWorldRumorSave reopened = Assert.IsType<DaggerfallDialogueWorldRumorSave>(
            restored.Synchronize(DaggerfallDialogueWorldState.CrimeWaveType, 17,
                DaggerfallDialogueWorldState.CrimeWaveTextId, enabled: true, nowMinute: first.ExpiresAtMinute + 3));
        Assert.Equal(first.ExpiresAtMinute + 3 + DaggerfallDialogueWorldState.GeneratedRumorDurationMinutes,
            reopened.ExpiresAtMinute);
    }
}
