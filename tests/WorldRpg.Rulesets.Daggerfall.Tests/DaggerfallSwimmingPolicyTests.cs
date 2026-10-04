using System.Numerics;
using Rusty.Engine;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Policies;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallSwimmingPolicyTests
{
    [Fact]
    public void Trigger_admission_builds_one_swim_request_and_breath_exhaustion_requests_drowning()
    {
        const ulong player = 7;
        CharacterWaterVolume volume = new(41, new Vector3(-2f, -1f, -2f), new Vector3(2f, 3f, 2f));
        DaggerfallSwimmingPolicy policy = new(DaggerfallSwimmingTuning.Classic);
        policy.ObserveTriggers(
            [new SpatialTriggerFact(true, volume.Trigger, player, 1, SpatialTriggerCause.Movement)],
            player,
            [volume]);

        CharacterMovementRequest request = policy.Movement(volume, 3f);
        Assert.Equal(CharacterMovementMode.Swimming, request.Mode);
        Assert.Equal(1f, request.VerticalIntent);
        Assert.Equal(volume.Minimum, request.Minimum);
        Assert.Equal(volume.Maximum, request.Maximum);
        Assert.Equal(volume.Trigger, policy.ActiveWaterTrigger);

        List<DaggerfallSkillUse> uses = [];
        DaggerfallSwimmingStep result = policy.Complete(
            Receipt(swimming: true, headSubmerged: true),
            waterBreathing: false,
            endurance: 2,
            realSeconds: .4d,
            gameSeconds: .4d,
            gameMinute: 18,
            uses.Add);

        Assert.True(result.Swimming);
        Assert.True(result.HeadSubmerged);
        Assert.True(result.Drowning);
        Assert.Equal(0, policy.CurrentBreath);
        Assert.Equal(DaggerfallSkillUseReason.Swimming, Assert.Single(uses).Reason);
    }

    [Fact]
    public void Water_breathing_keeps_breath_empty_then_removal_starts_a_new_deep_breath()
    {
        DaggerfallSwimmingPolicy policy = new(DaggerfallSwimmingTuning.Classic);
        List<DaggerfallSkillUse> uses = [];
        _ = policy.Complete(Receipt(swimming: true, headSubmerged: true), waterBreathing: true,
            endurance: 20, realSeconds: 5d, gameSeconds: 5d, gameMinute: 1, uses.Add);
        Assert.Equal(0, policy.CurrentBreath);

        _ = policy.Complete(Receipt(swimming: true, headSubmerged: true), waterBreathing: false,
            endurance: 20, realSeconds: .1d, gameSeconds: .1d, gameMinute: 2, uses.Add);
        Assert.Equal(DaggerfallFormulaPolicy.MaxBreath(20), policy.CurrentBreath);
        Assert.True(policy.Capture().HeadSubmerged);

        _ = policy.Complete(Receipt(swimming: true, headSubmerged: false), waterBreathing: false,
            endurance: 20, realSeconds: .1d, gameSeconds: .1d, gameMinute: 3, uses.Add);
        Assert.Equal(0, policy.CurrentBreath);
        Assert.False(policy.HeadSubmerged);
    }

    [Fact]
    public void Mid_submersion_save_restores_breath_timer_and_trigger_identity()
    {
        const ulong player = 9;
        CharacterWaterVolume volume = new(52, Vector3.Zero, Vector3.One);
        DaggerfallSwimmingPolicy source = new(DaggerfallSwimmingTuning.Classic);
        source.ObserveTriggers(
            [new SpatialTriggerFact(true, volume.Trigger, player, 3, SpatialTriggerCause.Movement)],
            player,
            [volume]);
        _ = source.Complete(Receipt(swimming: true, headSubmerged: true), false, 20, .1d, .1d, 4, _ => { });
        DaggerfallSwimmingSave saved = source.Capture();

        DaggerfallSwimmingPolicy restored = new(DaggerfallSwimmingTuning.Classic);
        restored.Restore(saved);

        Assert.Equal(saved, restored.Capture());
        Assert.Equal(volume.Trigger, restored.ActiveWaterTrigger);
        Assert.Equal(saved.CurrentBreath, restored.CurrentBreath);
        Assert.Equal(saved.BreathSeconds, restored.Capture().BreathSeconds, precision: 8);
    }

    [Fact]
    public void Compiled_water_breathing_effect_publishes_typed_capability_and_donor_binding()
    {
        DaggerfallEffectDefinition definition = DaggerfallAlterationEffects.Definitions(_ => { })
            .Single(value => value.Key == "water-breathing");

        Assert.True(definition.MovementProtection.GrantsWaterBreathing);
        Assert.Equal((30, 255), (definition.Spell!.Type, definition.Spell.SubType));
        Assert.True(definition.Spell.SupportsDuration);
    }

    private static CharacterStepReceipt Receipt(bool swimming, bool headSubmerged) => default(CharacterStepReceipt) with
    {
        Movement = new CharacterMovementFact(
            swimming ? CharacterMovementMode.Swimming : CharacterMovementMode.Walking,
            swimming ? 1f : 0f,
            headSubmerged,
            false,
            false,
            false),
        Displacement = new Vector3(0f, 0f, .1f),
    };
}
