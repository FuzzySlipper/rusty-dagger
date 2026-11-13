using Rusty.Engine.Mechanics;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Policies;

/// <summary>
/// What rest asks of the world it happens in, each answered by the domain that knows it at the moment rest asks.
/// The session answers; the rest never reads a later domain's state itself.
/// </summary>
internal interface IDaggerfallRestSurroundings
{
    /// <summary>A vampire who has not fed cannot rest.</summary>
    bool MustFeed { get; }

    /// <summary>An encounter is selected and still waiting to be placed beside the player.</summary>
    bool EncounterPending { get; }

    /// <summary>A hostile actor is in sight or close by.</summary>
    bool EnemyNearby { get; }

    /// <summary>A quest message is waiting to be read and dismissed.</summary>
    bool QuestPromptPending { get; }

    bool PlayerDefeated { get; }

    /// <summary>The player has a world position.</summary>
    bool HasPosition { get; }

    /// <summary>The admitted world profile the player stands in.</summary>
    DaggerfallWorldProfileKey Profile { get; }

    /// <summary>The location record of the profile's site, or null when the catalog has none.</summary>
    DaggerfallSiteRecord? Site { get; }

    /// <summary>Inside a building: a paid room here, the player's own house or the Fighters Guild's hall privilege.</summary>
    bool InteriorPrivilege { get; }

    /// <summary>Inside a tavern: the seconds left on the room paid for here; null anywhere else.</summary>
    long? TavernRoomSeconds { get; }

    /// <summary>Outside: the climate of the exterior cell the player stands in.</summary>
    int ExteriorClimate { get; }

    /// <summary>Outside: whether the player stands in the site's own exterior cell.</summary>
    bool InSiteCell { get; }

    /// <summary>
    /// Selects the resting encounter for a calendar minute, queueing it for placement; true when it brings a foe.
    /// </summary>
    bool EncounterArrives(long minute);
}

/// <summary>
/// The rest interaction: whether the player may rest, wait or camp here, the intervals a rest passes and what
/// interrupts them, and the rest's own consequences for the player (recovery, Medical, the skills it raises).
/// It is handed the clock, the rest tuning, the player's skill uses and level-ups, and "pass this much time", a
/// rest slice through the session's elapsed-time list; what it needs of later domains (encounters, enemies, quest
/// prompts, lodging, the ground) it asks <see cref="IDaggerfallRestSurroundings"/>. What a rest does in other
/// domains once it ends (the time increase, the panel) is the session's rest list, <c>ApplyRest</c> in
/// DaggerfallSession.Update.cs.
/// </summary>
internal sealed class DaggerfallRest(DaggerfallWorldTime time, DaggerfallRestTuning tuning,
    DaggerfallSkillUseReactions skillUses, DaggerfallLevelUpState levelUps, Func<long, DaggerfallCalendarAdvance> passTime)
{
    private readonly DaggerfallRestRecoveryModule _recovery = new();
    private static readonly TrackId Health = TrackId.Parse(DaggerfallMechanicsIds.Health.Value);
    private static readonly StatId Endurance = StatId.Parse(DaggerfallMechanicsIds.Endurance.Value);
    private static readonly StatId Medical = StatId.Parse("medical");

    /// <summary>
    /// Whether the player may rest in this mode where they stand now: the location, a paid room and guild
    /// privileges, read at the ordinary rest boundary.
    /// </summary>
    internal DaggerfallRestEligibility Eligibility(DaggerfallRestMode mode, StatsComponent player, IDaggerfallRestSurroundings surroundings)
    {
        if (!(player.GetTrack(Health).Current > 0d)) return new(false, IsAlive: false, "You cannot rest while defeated.");
        if (surroundings.MustFeed) return new(false, Message: "You must feed before you can rest.");
        if (surroundings.EncounterPending)
            return new(false, Message: "Something approaches. Move or turn to let the encounter arrive before resting.");
        if (surroundings.EnemyNearby) return new(false, Message: "Enemies are too close to rest.");
        if (!surroundings.HasPosition) return new(false, Message: "You cannot rest before the player has a world position.");
        DaggerfallWorldProfileKey profile = surroundings.Profile;
        if (profile.LogicalId.Length == 0) return new(false, Message: "You cannot rest without an admitted world profile.");
        // The donor's loiter never asks where the player is (DaggerfallRestWindow.CanRest guards only
        // resting): waiting in a town, a shop or on any ground is not camping.
        if (mode == DaggerfallRestMode.Loiter) return new(true);

        // An interior needs proof of the exact placed building and a current room, property, or guild
        // privilege. The normalized profile carries that source identity; unknown interiors stay closed.
        if (surroundings.Site is not { } site)
            return new(false, Message: "You cannot rest without an admitted location record.");
        if (profile.Kind == DaggerfallWorldProfileKind.Interior)
            return surroundings.InteriorPrivilege
                ? new(true)
                : new(false, Message: "You cannot rest in this interior without an admitted room or guild privilege.");
        if (profile.Kind == DaggerfallWorldProfileKind.Exterior
            && surroundings.ExteriorClimate is not (224 or 225 or 226 or 227 or 228 or 229 or 230 or 231 or 232))
            return new(false, Message: "You cannot rest on this terrain.");
        if (profile.Kind == DaggerfallWorldProfileKind.Exterior
            && site.Kind is DaggerfallSiteKind.TownCity or DaggerfallSiteKind.TownHamlet or DaggerfallSiteKind.TownVillage
            && surroundings.InSiteCell)
            return new(false, Message: "Camping in a town is not permitted.");
        return new(true, FatigueDivisor: tuning.InnFatigueMultiplier);
    }

    /// <summary>
    /// The two recovery branches the committed career gives, preset or custom alike: its rapid-healing condition
    /// (when its light/dark condition holds here and now, matching the donor) and its no-spell-point-regeneration
    /// ability.
    /// </summary>
    internal (bool RapidHealing, bool NoRegeneration) CharacterTraits(DaggerfallCareerSpecials specials, DaggerfallWorldProfileKind place)
    {
        bool light = place == DaggerfallWorldProfileKind.Exterior && time.Calendar.IsDay;
        return (specials.RapidHealingApplies(light), specials.NoRegenSpellPoints);
    }

    /// <summary>
    /// Applies one rest choice: the request and eligibility are checked, then each rest tick passes through
    /// <paramref name="advanceTime"/> and every whole hour recovers the player. A caller must not pass time
    /// separately before it.
    /// </summary>
    /// <param name="currentTraits">The career's recovery branches as they hold at each recovered hour.</param>
    internal DaggerfallRestResult Apply(DaggerfallRestRequest request, DaggerfallRestEligibility eligibility, StatsComponent player,
        IDaggerfallRestSurroundings surroundings, Func<long, DaggerfallRestTimeAdvance> advanceTime,
        bool rapidHealing, bool noRegeneration, Func<(bool RapidHealing, bool NoRegeneration)> currentTraits)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(eligibility);
        ArgumentNullException.ThrowIfNull(advanceTime);
        try
        {
            request.Validate();
            eligibility.Validate();
        }
        catch (ArgumentException rejection)
        {
            return DaggerfallRestResult.Rejected(request.Mode, DaggerfallRejectionText.For(rejection));
        }

        if (surroundings.MustFeed) return DaggerfallRestResult.Rejected(request.Mode, "You must feed before you can rest.");
        DaggerfallRestResult result = _recovery.Apply(
            player,
            request,
            eligibility,
            player.GetStat(Endurance).ValueInt,
            player.GetStat(Medical).ValueInt,
            rapidHealing,
            noRegeneration,
            advanceTime,
            recordMedicalRest: () => skillUses.Record(new DaggerfallSkillUse(
                "medical",
                DaggerfallSkillUseReason.MedicalRest,
                DaggerfallSkillUseOutcome.Accepted)),
            advanceSkills: () =>
            {
                skillUses.RaiseSkills(time.Calendar.ToAbsoluteSeconds());
                levelUps.BeginIfEligible();
            },
            currentRecoveryInputs: () =>
            {
                (bool rapid, bool noRegen) = currentTraits();
                return (player.GetStat(Endurance).ValueInt, player.GetStat(Medical).ValueInt, rapid, noRegen);
            });
        if (result.Interruption == DaggerfallRestInterruption.Stopped && surroundings.QuestPromptPending)
            result = result with { Message = "Rest paused. Read and dismiss the pending quest message before resting again." };
        return result;
    }

    /// <summary>
    /// Passes one rest tick, a calendar minute at a time, and reports what interrupted it: an enemy, a quest
    /// prompt, the player's defeat, a place where rest is no longer allowed, or a resting encounter selected at a
    /// minute boundary. A selected encounter's actors are placed on the next ordinary admitted step.
    /// </summary>
    internal DaggerfallRestTimeAdvance PassInterval(long requestedSeconds, DaggerfallRestMode mode, StatsComponent player,
        IDaggerfallRestSurroundings surroundings)
    {
        if (surroundings.EnemyNearby) return new(requestedSeconds, 0, DaggerfallRestInterruption.Encounter);
        if (surroundings.QuestPromptPending) return new(requestedSeconds, 0, DaggerfallRestInterruption.Stopped);
        long applied = 0;
        while (applied < requestedSeconds)
        {
            if (!Eligibility(mode, player, surroundings).Allowed)
                return new(requestedSeconds, applied, DaggerfallRestInterruption.Prevented);
            long secondsToMinute = DaggerfallCalendar.SecondsPerMinute
                - (time.Calendar.ToAbsoluteSeconds() % DaggerfallCalendar.SecondsPerMinute);
            long slice = Math.Min(requestedSeconds - applied, secondsToMinute);
            // A rented room bounds a rest, not a wait in the tavern.
            if (mode != DaggerfallRestMode.Loiter && surroundings.Profile.Kind == DaggerfallWorldProfileKind.Interior
                && surroundings.TavernRoomSeconds is { } room)
                slice = Math.Min(slice, room);
            DaggerfallCalendarAdvance advance = passTime(slice);
            applied = checked(applied + advance.AppliedSeconds);
            if (surroundings.PlayerDefeated)
                return new(requestedSeconds, applied, DaggerfallRestInterruption.Defeated);
            // The donor's rest stops advancing while a quest popup holds the screen; a prompt that
            // appeared inside this slice owns the player's attention before any further rest time.
            if (surroundings.QuestPromptPending)
                return new(requestedSeconds, applied, DaggerfallRestInterruption.Stopped);
            if (surroundings.EnemyNearby)
                return new(requestedSeconds, applied, DaggerfallRestInterruption.Encounter);
            if (advance.AppliedSeconds != slice)
                return new(requestedSeconds, applied, DaggerfallRestInterruption.Prevented);
            if (time.Calendar.ToAbsoluteSeconds() % DaggerfallCalendar.SecondsPerMinute != 0) continue;

            if (surroundings.EncounterArrives(time.Calendar.ToAbsoluteSeconds() / DaggerfallCalendar.SecondsPerMinute))
                return new(requestedSeconds, applied, DaggerfallRestInterruption.Encounter);
        }
        return new(requestedSeconds, applied);
    }
}
