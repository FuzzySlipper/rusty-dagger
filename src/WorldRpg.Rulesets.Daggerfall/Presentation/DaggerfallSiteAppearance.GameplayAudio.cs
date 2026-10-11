using System.Numerics;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

internal sealed partial class DaggerfallSiteAppearance
{
    private ulong gameplaySoundSequence;
    private readonly HashSet<int> unavailableGameplaySounds = [];
    private float footstepDistance;
    private bool alternateFootstep;
    private bool observedFootstepState;

    private void BindDoorAudio(DaggerfallDoorRuntime? owner)
    {
        if (doors is not null) doors.SoundRequested -= PlayDoorSound;
        doors = owner;
        if (doors is not null) doors.SoundRequested += PlayDoorSound;
    }

    private void PlayDoorSound(DaggerfallRdbDoorDefinition door, DaggerfallDoorOperationKind operation)
    {
        if (locationSuspended) return;
        bool dungeon = inputs.ProfileKind == DaggerfallWorldProfileKind.Dungeon;
        int ordinal = operation switch
        {
            DaggerfallDoorOperationKind.Open => dungeon ? 25 : 94,
            DaggerfallDoorOperationKind.Close => dungeon ? 24 : 93,
            DaggerfallDoorOperationKind.Bash => 7,
            _ => -1,
        };
        if (ordinal >= 0) PlayGameplaySound(ordinal, $"door-{operation.ToString().ToLowerInvariant()}", WorldPoint.From(door.Position));
    }

    /// <summary>Source ordinals resolve through the admitted manifest; semantic aliases remain intact.</summary>
    internal void PlayGameplaySound(int ordinal, string trigger, WorldPoint? position = null, float volume = 1F, float? pitch = null)
    {
        if (disposed || audio is null) return;
        string? clip = inputs.Audio.FirstOrDefault(value => value.SourceRecordOrdinal == ordinal)?.Id;
        if (clip is null)
        {
            if (unavailableGameplaySounds.Add(ordinal))
                System.Diagnostics.Trace.TraceWarning($"Gameplay sound ordinal {ordinal} ({trigger}) is absent from the admitted shared audio publication.");
            return;
        }
        Emit(clip, Event(0, 0, ++gameplaySoundSequence, 0, trigger), 0, position, volume, pitch);
    }

    internal void PlaySpellImpact(long sequence, int element, Vector3 position)
    {
        if (!deliveredEvents.Add(Event(0, 0, checked((ulong)sequence), 0, "spell-impact"))) return;
        PlayGameplaySound(element switch { 0 => 89, 1 => 90, 2 => 87, 3 => 88, _ => 86 },
            "spell-impact", WorldPoint.From(position));
    }

    internal void ResetFootsteps() { footstepDistance = 0; observedFootstepState = false; }

    /// <summary>PlayerFootsteps distance cadence over the accepted character step, never input intent.</summary>
    internal void AdvanceFootsteps(WorldPoint before, Vector3 after, bool wasGrounded, bool grounded,
        bool wasSwimming, bool swimming, bool shallowWater, bool onFoot, bool snow)
    {
        bool landed = observedFootstepState && !wasGrounded && grounded;
        observedFootstepState = true;
        if (!wasSwimming && swimming) PlayGameplaySound(342, "water-entry", volume: audioTuning.FootstepVolumeScale);
        if (!onFoot || !grounded && !swimming) { footstepDistance = 0; return; }
        Vector2 delta = new(after.X - before.X, after.Z - before.Z);
        footstepDistance += delta.Length();
        if (!landed && footstepDistance <= audioTuning.FootstepDistance) return;
        footstepDistance = 0;
        int ordinal = swimming ? 346 : shallowWater ? 334 : inputs.ProfileKind switch
        {
            DaggerfallWorldProfileKind.Dungeon => alternateFootstep ? 330 : 309,
            DaggerfallWorldProfileKind.Interior => alternateFootstep ? 333 : 313,
            _ => snow ? alternateFootstep ? 332 : 311 : alternateFootstep ? 331 : 310,
        };
        alternateFootstep = !alternateFootstep;
        PlayGameplaySound(ordinal, swimming ? "swim-stroke" : "footstep", volume: audioTuning.FootstepVolumeScale);
    }

    internal void PlayPlayerPain(string race, bool female, double loss, double maximum)
    {
        if (loss <= 0 || playerBeastForm() is not null) return;
        var identity = Event(0, 1, ++gameplaySoundSequence, 0, "player-pain-choice");
        if (DrawCue(identity, "chance", 1, 100) > audioTuning.PlayerPainChancePercent) return;
        bool heavy = loss >= maximum / 4;
        int ordinal = female
            ? race is "wood-elf" or "khajiit" ? heavy ? 59 : 58
                : heavy ? 50 : DrawCue(identity, "voice", 0, 1) == 0 ? 46 : 52
            : race switch { "breton" => 391, "redguard" => 394, "nord" => 397, "dark-elf" => 400,
                "high-elf" => 403, "wood-elf" => 406, "khajiit" => 409, "argonian" => 412, _ => -1 };
        if (ordinal >= 0) PlayGameplaySound(ordinal, "player-pain",
            pitch: audioTuning.Pitch + DrawCue(identity, "pitch", 0, 30) / 100F);
    }

    internal void PlayEquip(DaggerfallItemDefinition item, DaggerfallItemInstanceMetadata metadata)
    {
        var template = mobileDefinitions?.NativeTemplateFor(item);
        if (template is null) return;
        int ordinal = template.Groups.Any(group => group is "MensClothing" or "WomensClothing") ? 381
            : template.Groups.Any(group => group is "Jewellery" or "Gems") ? 383
            : template.Groups.Contains("Armor") ? item.Shield is not null || template.Index == 107 ? 419
                : metadata.Material == "leather" ? 417 : metadata.Material == "chain" ? 418 : 419
            : template.Groups.Contains("Weapons") ? template.Index switch
            {
                113 or 114 or 116 or 117 => 377, 118 or 119 or 120 or 121 => 378, 122 or 123 => 379,
                115 => 380, 124 or 126 => 413, 125 => 414, 127 or 128 => 415, 129 or 130 => 416, _ => -1,
            } : -1;
        if (ordinal >= 0) PlayGameplaySound(ordinal, "equip");
    }
}
