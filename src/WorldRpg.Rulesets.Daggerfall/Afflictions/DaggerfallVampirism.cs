using Rusty.Engine;
using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// Vampirism, once its infection has run: the clan of the region it was caught in, the cemetery draw and the
/// time the awakening passes, becoming one, the hunger, the clan's work and the 84-day cure quest, the cure, and
/// what ending it fails. It is handed the racial override it rides on, the clock, its tuning, the factions, the
/// draw, the line it tells, the quests it asks, the healing a new curse brings, and "pass this much time": an
/// elapsed interval and a quest-owned one. Where the player wakes and what else the awakening does is the
/// session's transformation list (<c>BecomeVampire</c> in DaggerfallSession.Update.cs).
/// </summary>
internal sealed class DaggerfallVampirism(DaggerfallRacialOverrides overrides, DaggerfallWorldTime time,
    DaggerfallVampirismTuning tuning, DaggerfallFactionsSet factions, IRandomService random, PresentationState presentation,
    IDaggerfallAfflictionQuests quests, DaggerfallTransformationHealing healing, Action<long> passTime, Action<long> passQuestTime)
{
    /// <summary>The clan of the province an infection was caught in; false, with the reason, when none is published.</summary>
    internal bool TryClan(int infectionRegion, out int clan, out string? unavailable)
    {
        try
        {
            clan = DaggerfallVampirismPolicy.GetVampireClan(factions, infectionRegion).Id;
            unavailable = null;
            return true;
        }
        catch (NotSupportedException error)
        {
            clan = 0;
            unavailable = "Vampire clan is unavailable: " + error.Message;
            return false;
        }
    }

    /// <summary>Which of <paramref name="count"/> cemeteries the player of infection <paramref name="instance"/> wakes in.</summary>
    internal int CemeteryIndex(string instance, int count) => checked((int)random.DrawKeyed(new KeyedRngRequest(0,
        "daggerfall.vampirism", instance + ":cemetery", 0, count - 1)).Value);

    /// <summary>The awakening passes fourteen days and wakes the player in the hour after dusk.</summary>
    internal long AwakeningSeconds =>
        14L * DaggerfallCalendar.SecondsPerDay + (DaggerfallSkyTime.DuskHour + 1 - time.Calendar.Hour) * 3600L;

    /// <summary>
    /// A vampire infection becomes the curse of <paramref name="clan"/>, with its healing; refused while another
    /// racial override is active.
    /// </summary>
    internal bool Become(DaggerfallInfectionTransition transition, int clan)
    {
        if (!overrides.Select(DaggerfallRacialKind.Vampire, $"vampirism:{transition.Instance}",
            time.Calendar.MinuteNumber, vampireClan: clan)) return false;
        healing.Heal();
        return true;
    }

    /// <summary>
    /// The clan's opportunity every 266 days (Classic nests it inside the weekly and 38-day conditions) and the
    /// cure's every 84, each at most once per boundary this calendar interval crosses.
    /// </summary>
    internal void AdvanceQuestOpportunities(DaggerfallCalendar before, DaggerfallCalendar after)
    {
        if (overrides.Current is not { IsVampire: true } racial) return;
        long first = Math.Max(before.MinuteNumber, racial.State.AcquiredMinute), last = after.MinuteNumber;
        foreach (var period in new[] { (Minutes: 266L * 1440, Cure: false), (Minutes: 84L * 1440, Cure: true) })
            for (long boundary = ((first + period.Minutes - 1) / period.Minutes) * period.Minutes; boundary < last; boundary += period.Minutes)
                StartQuestOpportunity(boundary, period.Cure);
    }

    internal bool StartQuestOpportunity(long minute, bool cure)
    {
        if (overrides.Current is not { State.Vampire: { } vampire } racial) return false;
        string draw = $"{racial.Source}:{minute}:{cure}";
        int roll = checked((int)random.DrawKeyed(new(0, "daggerfall.vampirism", draw + ":quest", cure ? 10 : 1, 100)).Value);
        if (roll >= (cure ? 30 : 50)) return false;
        string? quest = cure ? "$CUREVAM" : vampire.InitialQuestStarted ? null : "P0A01L00";
        int faction = 0;
        if (quest is null)
        {
            IReadOnlyList<string> pool = quests.ClanWork(vampire.Clan);
            if (pool.Count == 0) return false;
            quest = pool[checked((int)random.DrawKeyed(new(0, "daggerfall.vampirism", draw + ":selection", 0, pool.Count - 1)).Value)];
            faction = vampire.Clan;
        }
        string identity = $"vampire-quest:{racial.Source}:{minute}:{cure}";
        if (quests.Started(identity)) return false;
        if (quests.Offer(identity, quest + ".txt", faction))
        {
            if (!cure && !vampire.InitialQuestStarted) overrides.MarkInitialVampireQuestStarted();
            return true;
        }
        presentation.SetOutcome(cure ? "The cure for vampirism cannot be offered right now." : "Your vampire clan's quest cannot be offered right now.");
        return false;
    }

    internal bool Cure(bool fromQuest = false)
    {
        if (overrides.Current is not { IsVampire: true } racial) return false;
        overrides.Remove(racial.Source);

        if (fromQuest) passQuestTime(60); else passTime(60);
        presentation.SetOutcome("Your vampirism is cured.");
        return true;
    }

    /// <summary>Ending vampirism fails the clan's active work, its P0 quests.</summary>
    internal void Ended(DaggerfallRacialOverrideView previous)
    {
        if (previous.IsVampire) quests.FailActive("P0", "Vampirism ended");
    }

    /// <summary>A vampire who has not fed within the satiation period must feed before resting.</summary>
    internal bool MustFeed => overrides.Current?.State.Vampire is { } vampire
        && time.Calendar.MinuteNumber - vampire.LastFedMinute > tuning.SatiationMinutes;
}
